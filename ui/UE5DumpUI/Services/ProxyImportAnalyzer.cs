using System.Buffers.Binary;
using System.IO;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Offline, IO-light analysis of a game executable's PE import table to decide
/// which of our proxy DLLs can actually be loaded by that process, and to build
/// a per-game proxy suggestion for the Proxy Deploy panel.
///
/// WHAT AN IMPORT PROVES, AND WHAT IT DOES NOT. A name in the import table proves
/// the proxy WILL load: the loader resolves it from the .exe directory at process
/// start. Its ABSENCE proves nothing, because a run-time <c>LoadLibrary</c> searches
/// that same directory — and for <c>version.dll</c> that is the normal case, not the
/// exception (GetFileVersionInfo / COM / manifest parsing pull it in later). So
/// <c>version.dll</c> stays the safe universal default no matter what the table says;
/// <see cref="LoadsDynamically"/> holds the measurement that settles it.
///
/// We deliberately do NOT auto-escalate to dxgi merely because version isn't a static
/// import: that pattern matches nearly every D3D game (21 of 21 measured), and dxgi is
/// imported early enough that some games call it before the CRT is initialised —
/// Octopath Traveler instant-exits under the dxgi proxy. Escalating on that signal
/// would trade a working default for a crash. Import parsing here only reports
/// VIABILITY (dxgi/dinput8 importable) as advisory context.
///
/// The parser is pure (operates on a seekable <see cref="Stream"/>) so it is unit
/// testable against a synthetic PE with no live file. All OS file access lives in
/// <c>ProxyDeployService</c>.
/// </summary>
internal static class ProxyImportAnalyzer
{
    /// <summary>Which of our proxy-relevant system DLLs the .exe statically (or
    /// delay-) imports. <c>version.dll</c> is tracked for completeness but is
    /// almost never a static import (see class remarks) and is never used to
    /// downgrade the version default.</summary>
    public readonly record struct ProxyImportInfo(bool ImportsVersion, bool ImportsDinput8, bool ImportsDxgi, bool ImportsWinmm = false)
    {
        /// <summary>True when this PE imports none of the three — which for a game's
        /// main .exe is the signature of a MODULAR UE build's bootstrap stub, not of
        /// a game no proxy can reach. See <see cref="Merge"/>.</summary>
        public bool ImportsNone => !ImportsVersion && !ImportsDinput8 && !ImportsDxgi && !ImportsWinmm;

        /// <summary>Does this PE STATICALLY (or delay-) import the DLL a given proxy flavour
        /// hijacks? True means the proxy is GUARANTEED to load — the loader resolves the name
        /// from the .exe directory at process start.
        ///
        /// <para>False does NOT mean the opposite: a run-time <c>LoadLibrary</c> reaches the
        /// same directory. Do not turn a false here into a refusal — see
        /// <see cref="LoadsDynamically"/> for the measurement, and
        /// <see cref="DescribeLoadRisk"/> for the only case still worth reporting.</para>
        ///
        /// <para>The example that used to sit here — "Octopath Traveler imports winmm and dxgi
        /// but NOT version.dll" — is FALSE and was load-bearing for a bug. Its .exe import
        /// directory names WINMM.dll, dxgi.dll <b>and VERSION.dll</b>. Octopath's real quirk is
        /// unrelated: it instant-exits under the <i>dxgi</i> proxy, because dxgi is imported so
        /// early that the game calls it before the CRT is initialised.</para></summary>
        public bool Imports(ProxyType type) => type switch
        {
            ProxyType.Version => ImportsVersion,
            ProxyType.Dinput8 => ImportsDinput8,
            ProxyType.Dxgi    => ImportsDxgi,
            ProxyType.Winmm   => ImportsWinmm,
            _                 => true,   // unknown flavour: never block on a rule we cannot apply
        };

        /// <summary>OR two results together. Used to fold a modular build's
        /// <c>*-Win64-Shipping.dll</c> modules into the stub exe's (empty) result:
        /// a proxy is loaded if ANY module in the process imports that name, since
        /// the loader searches the .exe's directory whichever one asks.</summary>
        public ProxyImportInfo Merge(ProxyImportInfo other) => new(
            ImportsVersion || other.ImportsVersion,
            ImportsDinput8 || other.ImportsDinput8,
            ImportsDxgi    || other.ImportsDxgi,
            ImportsWinmm   || other.ImportsWinmm);
    }

    /// <summary>A per-game proxy suggestion: the recommended type (null when the
    /// known-good method is injection, which has no proxy type) plus a concise,
    /// self-contained column string (kept short so a plain sortable text column can
    /// show it without a tooltip).</summary>
    public readonly record struct ProxySuggestion(ProxyType? Type, string Display);

    private const int MZ = 0x5A4D;            // 'MZ'
    private const uint PE = 0x00004550;       // 'PE\0\0'
    private const ushort PE32 = 0x10B;
    private const ushort PE32PLUS = 0x20B;
    private const int MaxDescriptors = 4096;  // runaway guard on a malformed table
    private const int MaxNameLen = 256;

    /// <summary>
    /// Parse the PE import + delay-import directories of <paramref name="pe"/> and
    /// report which of {version,dinput8,dxgi}.dll it imports. Returns null on any
    /// malformation (not a PE, truncated, out-of-range RVA) — the caller then
    /// simply shows no viability hint. Never throws.
    /// </summary>
    public static ProxyImportInfo? Analyze(Stream pe)
    {
        try
        {
            long len = pe.Length;
            if (len < 0x40 || ReadU16(pe, 0) != MZ)
                return null;

            long lfanew = ReadU32(pe, 0x3C);
            if (lfanew <= 0 || lfanew + 24 > len || ReadU32(pe, lfanew) != PE)
                return null;

            long fileHeader = lfanew + 4;
            int numSections = ReadU16(pe, fileHeader + 2);
            int sizeOfOptional = ReadU16(pe, fileHeader + 16);

            long opt = lfanew + 24;
            ushort magic = (ushort)ReadU16(pe, opt);
            bool is64 = magic == PE32PLUS;
            if (magic != PE32PLUS && magic != PE32)
                return null;

            uint sizeOfHeaders = ReadU32(pe, opt + 60);
            long numRvaOff = opt + (is64 ? 108 : 92);
            long dataDirOff = opt + (is64 ? 112 : 96);
            uint numRvaAndSizes = ReadU32(pe, numRvaOff);

            // Section table follows the optional header.
            long secOff = opt + sizeOfOptional;
            var sections = new List<(uint Va, uint VSize, uint RawSize, uint RawPtr)>(numSections);
            for (int i = 0; i < numSections; i++)
            {
                long s = secOff + (long)i * 40;
                if (s + 40 > len) break;
                uint vSize = ReadU32(pe, s + 8);
                uint va = ReadU32(pe, s + 12);
                uint rawSize = ReadU32(pe, s + 16);
                uint rawPtr = ReadU32(pe, s + 20);
                sections.Add((va, vSize, rawSize, rawPtr));
            }

            long? RvaToOffset(uint rva)
            {
                if (rva < sizeOfHeaders) return rva; // headers map 1:1
                foreach (var (va, vSize, rawSize, rawPtr) in sections)
                {
                    uint span = Math.Max(vSize, rawSize);
                    if (rva >= va && rva < va + span)
                    {
                        long off = (long)rva - va + rawPtr;
                        return (off >= 0 && off < len) ? off : (long?)null;
                    }
                }
                return null;
            }

            bool ver = false, di8 = false, dxgi = false, winmm = false;
            void Classify(string name)
            {
                if (name.Equals("version.dll", StringComparison.OrdinalIgnoreCase)) ver = true;
                else if (name.Equals("dinput8.dll", StringComparison.OrdinalIgnoreCase)) di8 = true;
                else if (name.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase)) dxgi = true;
                else if (name.Equals("winmm.dll", StringComparison.OrdinalIgnoreCase)) winmm = true;
            }

            // ── Standard import directory (data directory index 1) ──
            if (numRvaAndSizes > 1)
            {
                uint importRva = ReadU32(pe, dataDirOff + 1 * 8);
                if (importRva != 0 && RvaToOffset(importRva) is long impOff)
                {
                    for (int i = 0; i < MaxDescriptors; i++)
                    {
                        long d = impOff + (long)i * 20;
                        if (d + 20 > len) break;
                        uint nameRva = ReadU32(pe, d + 12);   // IMAGE_IMPORT_DESCRIPTOR.Name
                        uint firstThunk = ReadU32(pe, d + 16);
                        if (nameRva == 0 && firstThunk == 0) break; // null terminator
                        if (nameRva != 0 && RvaToOffset(nameRva) is long nOff)
                            Classify(ReadAsciiZ(pe, nOff, len));
                    }
                }
            }

            // ── Delay-load import directory (data directory index 13) ──
            // Some games delay-load dxgi; a delay-loaded DLL is still loaded (just
            // later), so its proxy still activates. Modern linkers store RVAs here.
            if (numRvaAndSizes > 13)
            {
                uint delayRva = ReadU32(pe, dataDirOff + 13 * 8);
                if (delayRva != 0 && RvaToOffset(delayRva) is long delOff)
                {
                    for (int i = 0; i < MaxDescriptors; i++)
                    {
                        long d = delOff + (long)i * 32;   // IMAGE_DELAYLOAD_DESCRIPTOR = 32 bytes
                        if (d + 32 > len) break;
                        uint nameRva = ReadU32(pe, d + 4); // DllNameRVA
                        if (nameRva == 0) break;
                        if (RvaToOffset(nameRva) is long nOff)
                            Classify(ReadAsciiZ(pe, nOff, len));
                    }
                }
            }

            return new ProxyImportInfo(ver, di8, dxgi, winmm);
        }
        catch
        {
            return null; // any IO / bounds error → no hint
        }
    }

    /// <summary>
    /// Build the per-game suggestion. Order of preference:
    /// 1. a remembered proxy the user last deployed for this game (mini-LKG) — the
    ///    closest honest "last known good" we have without a DLL change;
    /// 2. otherwise <see cref="ProxyType.Version"/>, the safe universal default.
    /// The parsed <paramref name="imports"/> (if any) are surfaced as advisory
    /// "importable" context and are NEVER used to override the version default.
    /// </summary>
    public static ProxySuggestion Recommend(
        ProxyImportInfo? imports, ProxyType? confirmedPick, ProxyType? rememberedPick, bool injected)
    {
        // 0. A proxy the DLL CONFIRMED actually loaded this game (and the session
        //    stayed alive past the stability dwell) is the strongest known-good.
        if (confirmedPick is ProxyType confirmed)
            return new ProxySuggestion(confirmed, $"{confirmed.GetDisplayName()} · confirmed working");

        // 1. A proxy the user actually deployed for this game — weaker than a
        //    confirmed load, but still an honest "last known good".
        if (rememberedPick is ProxyType pick)
            return new ProxySuggestion(pick, $"{pick.GetDisplayName()} · last used");

        // 2. Injection is itself a known-good load method. If the user has
        //    successfully injected into this game via the UI and never deployed a
        //    proxy, surface that instead of guessing a proxy — injection is often
        //    the more reliable path (and the only one for launcher games that strip
        //    the exe's DLL search directory). No proxy type applies here.
        if (injected)
            return new ProxySuggestion(null, "injection · no proxy deployed");

        // 3. No history → the safe default. version.dll loads dynamically almost
        //    everywhere, so it stays the broadest-compatible pick regardless of the
        //    static import table. The imports only annotate the fallback options.
        //
        //    ONE exception, from [PROXYLOAD-2026-08-17]: if this game imports version.dll
        //    DIRECTLY, the default is at BYPASS risk — an already-mapped System32 version.dll
        //    (an overlay/launcher such as Steam maps it early) can satisfy that import before
        //    the game folder is searched, so ours is silently ignored. Surface it as a
        //    HEURISTIC and point at injection; never change the recommended TYPE (the Load
        //    column confirms per-game — this class must not turn a maybe into a wall).
        if (imports is ProxyImportInfo vi && vi.ImportsVersion)
            return new ProxySuggestion(
                ProxyType.Version, "version · default · imported, may be bypassed — try injection");

        string alt = DescribeImportable(imports);
        // An EMPTY alt with a parsed import table now means none of the three non-version
        // flavours is importable -- winmm included, which the old wording omitted from
        // both the list and this sentence. `imports is null` stays distinct: unknown is
        // not the same as none.
        string display = alt.Length > 0
            ? $"version · default · alt: {alt}"
            : (imports is not null
                ? "version · default · no dxgi/winmm/dinput8"
                : "version · default");
        return new ProxySuggestion(ProxyType.Version, display);
    }

    /// <summary>Short list of the non-version proxies the .exe imports (the ones
    /// that can actually activate), or "" when unknown/none. Extension trimmed for
    /// compactness ("dxgi", "winmm", "dinput8").
    ///
    /// <para>⚠ <c>winmm</c> was MISSING here until 2026-08-23
    /// (<c>[PROXYALTWINMM-2026-08-23]</c>) even though the analyzer has parsed
    /// <see cref="ProxyImportInfo.ImportsWinmm"/> since 2026-07-27 and winmm is one of the
    /// four proxies we build. Measured over the 16 UE shipping .exes installed on the
    /// maintainer's machine: <b>14 import winmm and 0 import dinput8</b> — so this helper
    /// was offering the flavour nothing imports while hiding the one almost everything
    /// does. It survived because <c>ImportsWinmm</c> was appended to the record WITH A
    /// DEFAULT, so all four Recommend tests constructed it with three positional
    /// arguments and silently asserted the no-winmm case.</para>
    ///
    /// <para>Order is deliberate: dxgi and winmm are the pure static-import hijacks (see
    /// the class remarks), so they are the deterministic picks and come first; dinput8 is
    /// the run-time-<c>LoadLibrary</c> shape.</para></summary>
    private static string DescribeImportable(ProxyImportInfo? imports)
    {
        if (imports is not ProxyImportInfo i) return "";
        var parts = new List<string>(3);
        if (i.ImportsDxgi) parts.Add("dxgi");
        if (i.ImportsWinmm) parts.Add("winmm");
        if (i.ImportsDinput8) parts.Add("dinput8");
        return string.Join(", ", parts);
    }

    /// <summary>
    /// Is this proxy flavour loaded DYNAMICALLY often enough that its ABSENCE from the
    /// import table proves nothing about whether the proxy works?
    ///
    /// <para>MEASURED, not assumed — 21 Steam UE games on the maintainer's machine,
    /// 2026-08-10: <b>11 of them run a working <c>version.dll</c> proxy whose .exe names
    /// version.dll in NEITHER the import nor the delay-import directory</b> (DQ7R, P3R,
    /// Stray, Palworld, Manor Lords, Ghostwire Tokyo, both DQ HD-2D remakes, Lushfoil,
    /// Arms of God, The Artisan of Glimmith). DQ7R is the strongest case: the DLL itself
    /// reported the proxy load, which is what drives the "confirmed working" suggestion.</para>
    ///
    /// <para>The mechanism: <c>version.dll</c> and <c>dinput8.dll</c> are pulled in at RUN
    /// TIME (GetFileVersionInfo / COM / manifest parsing; DirectInput device enumeration),
    /// and <c>LoadLibrary</c>'s default search order reaches the .exe directory before
    /// System32. None of our four names is a KnownDLL — verified against
    /// <c>HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\KnownDLLs</c>, which is the
    /// one thing that WOULD make exe-directory proxying impossible — so the exe-directory
    /// copy wins the search.</para>
    ///
    /// <para><b>Evidence is asymmetric, and the code should not pretend otherwise.</b> The 11
    /// games above all concern <c>version.dll</c>; <c>dinput8.dll</c> is grouped with it on the
    /// MECHANISM (middleware reaches DirectInput through a run-time
    /// <c>LoadLibrary("dinput8.dll")</c> + <c>GetProcAddress</c>, which is why mod loaders ship
    /// that name) and has no local measurement — only 1 of the 21 games imports it and none has
    /// a dinput8 proxy deployed. The grouping is safe because being in this set only suppresses
    /// a WARNING; nothing here can block a deploy either way.</para>
    ///
    /// <para><c>dxgi.dll</c> / <c>winmm.dll</c> are the other shape: pure static-import hijacks
    /// a game that never names them genuinely cannot load. That is worth SAYING, never worth
    /// blocking on — see <see cref="DescribeLoadRisk"/>.</para>
    /// </summary>
    public static bool LoadsDynamically(ProxyType type)
        => type is ProxyType.Version or ProxyType.Dinput8;

    /// <summary>
    /// Advisory note for deploying <paramref name="type"/> into a game with the given parsed
    /// <paramref name="imports"/>, or <c>null</c> when there is nothing honest to say.
    ///
    /// <para><b>This never blocks a deploy, and that is a deliberate reversal.</b> A hard
    /// refusal used to live in <c>ProxyDeployService.DeployAsync</c> on the premise that a
    /// proxy absent from the import table "would never load". It was built on a misreading —
    /// its worked example, Octopath Traveler, DOES import VERSION.dll (verified in its PE
    /// import directory alongside WINMM.dll and dxgi.dll) — and it is falsified by the 11
    /// games cited in <see cref="LoadsDynamically"/>. Its effect was to turn the most
    /// broadly-compatible proxy into a hard failure on exactly the games that need it.</para>
    ///
    /// <para>What survives is the real lesson: when a proxy genuinely cannot load, it fails
    /// SILENTLY and TOTALLY — no log at all, indistinguishable from "nothing happened". So
    /// the case is still surfaced, as a note the user can act on rather than a wall.</para>
    /// </summary>
    public static string? DescribeLoadRisk(ProxyImportInfo? imports, ProxyType type)
    {
        // Unparseable PE → no claim to make.
        if (imports is not ProxyImportInfo info) return null;

        // A stub .exe naming none of the four is a MODULAR build whose real modules were
        // already folded in by Merge. "Names nothing" there means "cannot tell", not "no".
        if (info.ImportsNone) return null;

        // Statically imported → the loader resolves it from the .exe directory at process
        // start. Guaranteed; nothing to say.
        if (info.Imports(type)) return null;

        // Loaded dynamically by virtually every process — the normal, working case for
        // 11 of 21 measured games. Warning here would cry wolf on the default pick.
        if (LoadsDynamically(type)) return null;

        string alt = DescribeAlternatives(info, type);
        string tail = alt.Length > 0 ? $" Try {alt} instead." : "";
        return $"{type.GetDllName()} is not in this game's import table — it may never load "
             + $"(the symptom is no log at all).{tail}";
    }

    /// <summary>The flavours OTHER than <paramref name="exclude"/> worth suggesting: the ones
    /// this .exe actually names (guaranteed to load), plus <c>version</c>, which loads
    /// dynamically nearly everywhere and is the broadest-compatible fallback. Extension
    /// trimmed to match <see cref="DescribeImportable"/>.</summary>
    private static string DescribeAlternatives(ProxyImportInfo imports, ProxyType exclude)
    {
        var parts = new List<string>(4);
        foreach (var t in new[] { ProxyType.Version, ProxyType.Dinput8, ProxyType.Dxgi, ProxyType.Winmm })
        {
            if (t == exclude) continue;
            if (imports.Imports(t) || t == ProxyType.Version)
                parts.Add(t.GetDllName().Replace(".dll", "", StringComparison.OrdinalIgnoreCase));
        }
        return string.Join(", ", parts);
    }

    /// <summary>
    /// Advisory when the proxy flavour IS named in a game's import table.
    ///
    /// <para><b>Counter-intuitively this is a RISK, not the guarantee the rest of this class once
    /// assumed.</b> Measured 2026-08-17 (<c>[PROXYLOAD-2026-08-17]</c>): OCTOPATH TRAVELER
    /// statically imports <c>version.dll</c> and its <c>version.dll</c> proxy is silently ignored —
    /// only <c>System32\version.dll</c> loads, and no per-process log folder is ever created. The
    /// observed correlation was 3 for 3: titles that statically import the flavour's base name got
    /// the SYSTEM copy; titles that do not got ours. The fitting mechanism — the Windows loader
    /// satisfies an import by base name from a module ALREADY MAPPED into the process (an overlay or
    /// launcher such as Steam maps <c>version.dll</c> early), never searching the application
    /// directory — is stated in the finding as a HEURISTIC, not a law, and this method treats it as
    /// one: it warns, it never blocks. It is worded as a maybe on purpose, because it CAN
    /// false-positive (a game imports <c>winmm.dll</c> yet its winmm proxy works when nothing
    /// pre-maps winmm — OCTOPATH's own resolved flavour). The Load column settles it per-game.</para>
    ///
    /// <para>Deliberately NOT the mirror of <see cref="DescribeLoadRisk"/>. That one fires when a
    /// static-only flavour is ABSENT (it then cannot load at all); this one fires when a flavour is
    /// PRESENT (it may be pre-empted). The two are mutually exclusive on
    /// <see cref="ProxyImportInfo.Imports"/>, which is what lets
    /// <see cref="DescribeDeployAdvisory"/> return whichever applies.</para>
    ///
    /// <para>Returns null when the PE could not be parsed, when it is a modular stub that names
    /// nothing (<see cref="ProxyImportInfo.ImportsNone"/> — the real modules were folded in, so a
    /// bare stub proves nothing), or when the flavour is simply not imported.</para>
    /// </summary>
    public static string? DescribeImportBypassRisk(ProxyImportInfo? imports, ProxyType type)
    {
        if (imports is not ProxyImportInfo info) return null; // unparseable → no claim
        if (info.ImportsNone) return null;                    // modular stub → cannot tell
        if (!info.Imports(type)) return null;                 // not imported → this method is silent
        return $"{type.GetDllName()} is imported directly by this game — an already-mapped copy "
             + "(e.g. one an overlay or launcher loaded early) can satisfy that import before the "
             + "game folder is searched, so this proxy may be silently ignored. Heuristic: if the "
             + "Load column stays “not observed”, try a flavour it does not import, or inject.";
    }

    /// <summary>The single advisory to attach when deploying <paramref name="type"/>: the
    /// import-BYPASS risk when the flavour is present, otherwise the never-loads risk when a
    /// static-only flavour is absent. At most one is non-null (they partition on
    /// <see cref="ProxyImportInfo.Imports"/>). Null = nothing honest to say.</summary>
    public static string? DescribeDeployAdvisory(ProxyImportInfo? imports, ProxyType type)
        => DescribeImportBypassRisk(imports, type) ?? DescribeLoadRisk(imports, type);

    // ─────────────────────────────────────────────────────────────────────────
    // "Did it actually load?" signal — the per-process log-folder tell
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the injected DLL has been OBSERVED to load for a game, inferred from the per-process
    /// log folder it creates on load. DISK state (<c>ProxyDeployStatus.DeployedCurrent</c>) says only
    /// that a file is in place; this says whether it actually ran. See <c>[PROXYLOAD-2026-08-17]</c>.
    /// </summary>
    public enum ProxyLoadSignal
    {
        /// <summary>No per-process log folder found. HONESTLY UNKNOWN — the game may simply not have
        /// been launched with the proxy yet. This is never reported as a failure.</summary>
        Unknown,
        /// <summary>Log folder present and written recently — the DLL ran.</summary>
        Observed,
        /// <summary>Log folder present but OLD (older than <c>staleAfterDays</c>) — the DLL ran, but in
        /// an earlier session/build; it is NOT evidence of a current load.</summary>
        ObservedStale,
    }

    /// <summary>
    /// Classify the load signal from the per-process log folder's presence + newest write time.
    /// Pure so it is unit-testable without touching the filesystem; the folder lookup itself lives in
    /// <c>ProxyDeployService</c>.
    ///
    /// <para><paramref name="lastWrite"/> and <paramref name="now"/> must be the same kind (both local
    /// or both UTC). A present folder whose timestamp cannot be read is still
    /// <see cref="ProxyLoadSignal.Observed"/> — the folder exists only because the DLL created it —
    /// but without a date. The staleness guard is the direct answer to the finding's warning that a
    /// leftover folder from a previous BUILD must not read as a current load: the date is ALWAYS
    /// shown, so nothing here ever claims "loaded now".</para>
    /// </summary>
    public static (ProxyLoadSignal Signal, string Display) ClassifyLoad(
        bool logFolderPresent, DateTime? lastWrite, DateTime now, int staleAfterDays)
    {
        if (!logFolderPresent) return (ProxyLoadSignal.Unknown, "not observed");
        if (lastWrite is not DateTime ts) return (ProxyLoadSignal.Observed, "loaded");
        string date = ts.ToString("yyyy-MM-dd");
        return (now - ts).TotalDays > staleAfterDays
            ? (ProxyLoadSignal.ObservedStale, $"loaded {date} (stale)")
            : (ProxyLoadSignal.Observed, $"loaded {date}");
    }

    /// <summary>
    /// The per-process log SUBFOLDER name the DLL creates for a host executable — the join key
    /// between a <c>DetectedGame</c> and its <c>%LOCALAPPDATA%\UE5CEDumper\Logs\&lt;name&gt;</c>
    /// folder. Mirrors <c>dll/src/Sein.cpp InitProcessMirror</c> EXACTLY: take the file leaf, drop the
    /// last extension, then replace each Windows-invalid path character with '_'. Kept a pure string
    /// transform (no IO) so a test can pin it against that C++ rule — a drift there silently makes
    /// every load probe miss its folder.
    /// </summary>
    public static string ProcessLogFolderName(string exePathOrName)
    {
        if (string.IsNullOrEmpty(exePathOrName)) return "";
        // Leaf: last separator of EITHER kind (Sein is handed the DLL-side leaf; we may get a path).
        int slash = exePathOrName.LastIndexOfAny(s_pathSeparators);
        string leaf = slash >= 0 ? exePathOrName[(slash + 1)..] : exePathOrName;
        // Strip the last extension (Sein: rfind('.')).
        int dot = leaf.LastIndexOf('.');
        string name = dot >= 0 ? leaf[..dot] : leaf;
        // Replace the exact 9 characters Sein replaces — no more, no less.
        char[] buf = name.ToCharArray();
        for (int i = 0; i < buf.Length; i++)
        {
            char c = buf[i];
            if (c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
                buf[i] = '_';
        }
        return new string(buf);
    }

    private static readonly char[] s_pathSeparators = { '/', '\\' };

    // ─────────────────────────────────────────────────────────────────────────
    // EXPORT table reader — used by the leftover-proxy cleanup to confirm a DLL is ours
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Read the exported NAMES from a PE (data directory 0). Returns an empty set for anything that
    /// is not a readable PE with a name-exporting export directory — never throws for malformed
    /// input, because every caller treats "cannot tell" as "not ours" and must fail closed.
    ///
    /// <para>Why this exists next to the import reader instead of sharing its RVA translator:
    /// <c>Analyze</c>'s <c>RvaToOffset</c> is a local function closing over three of its locals, and
    /// restructuring a path the proxy-suggestion feature depends on is a worse trade than repeating
    /// ~20 lines of header parse. The leaf readers (<see cref="ReadU16"/>, <see cref="ReadU32"/>,
    /// <see cref="ReadAsciiZ"/>) ARE shared, which is where the subtle code is.</para>
    ///
    /// <para>Takes a <see cref="Stream"/> rather than a path on purpose: it is the only ownership
    /// signal that can be evaluated through an ALREADY-OPEN handle, which is what lets the caller
    /// re-verify identity across the confirm→delete gap without the path being swapped underneath
    /// it. <c>FileVersionInfo</c> takes a path only and cannot do that.</para>
    /// </summary>
    internal static IReadOnlySet<string> ReadExportNames(Stream pe)
    {
        var empty = (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal);
        try
        {
            long len = pe.Length;
            if (len < 0x40) return empty;

            if (ReadU16(pe, 0) != 0x5A4D) return empty;            // 'MZ'
            long lfanew = ReadU32(pe, 0x3C);
            if (lfanew <= 0 || lfanew + 24 > len) return empty;
            if (ReadU32(pe, lfanew) != 0x00004550) return empty;    // 'PE\0\0'

            long fileHeader = lfanew + 4;
            int numSections = ReadU16(pe, fileHeader + 2);
            int sizeOfOptional = ReadU16(pe, fileHeader + 16);

            long opt = lfanew + 24;
            ushort magic = (ushort)ReadU16(pe, opt);
            bool is64 = magic == PE32PLUS;
            if (magic != PE32PLUS && magic != PE32) return empty;

            uint sizeOfHeaders = ReadU32(pe, opt + 60);
            long numRvaOff = opt + (is64 ? 108 : 92);
            long dataDirOff = opt + (is64 ? 112 : 96);
            if (ReadU32(pe, numRvaOff) < 1) return empty;           // no export directory entry

            long secOff = opt + sizeOfOptional;
            var sections = new List<(uint Va, uint VSize, uint RawSize, uint RawPtr)>(numSections);
            for (int i = 0; i < numSections; i++)
            {
                long s = secOff + (long)i * 40;
                if (s + 40 > len) break;
                sections.Add((ReadU32(pe, s + 12), ReadU32(pe, s + 8), ReadU32(pe, s + 16), ReadU32(pe, s + 20)));
            }

            long? Rva(uint rva)
            {
                if (rva == 0) return null;
                if (rva < sizeOfHeaders) return rva;
                foreach (var (va, vSize, rawSize, rawPtr) in sections)
                {
                    uint span = Math.Max(vSize, rawSize);
                    if (rva >= va && rva < va + span)
                    {
                        long off = (long)rva - va + rawPtr;
                        return (off >= 0 && off < len) ? off : (long?)null;
                    }
                }
                return null;
            }

            uint expRva = ReadU32(pe, dataDirOff);                   // data directory 0 == exports
            long? expOff = Rva(expRva);
            if (expOff == null || expOff.Value + 0x28 > len) return empty;

            uint numNames = ReadU32(pe, expOff.Value + 0x18);
            uint namesRva = ReadU32(pe, expOff.Value + 0x20);
            if (numNames == 0 || numNames > 65536) return empty;     // sanity bound
            long? namesOff = Rva(namesRva);
            if (namesOff == null) return empty;

            var result = new HashSet<string>(StringComparer.Ordinal);
            for (uint i = 0; i < numNames; i++)
            {
                long entry = namesOff.Value + (long)i * 4;
                if (entry + 4 > len) break;
                long? nameOff = Rva(ReadU32(pe, entry));
                if (nameOff == null) continue;
                string name = ReadAsciiZ(pe, nameOff.Value, len);
                if (name.Length > 0) result.Add(name);
            }
            return result;
        }
        catch
        {
            // Malformed / truncated / unreadable — the caller must treat this as "not ours".
            return empty;
        }
    }

    /// <summary>
    /// Export names that have been present in EVERY proxy we have ever shipped. Used as a QUORUM,
    /// not individually: a single <c>UE5_Init</c> is a weak signal (that exact name is guessable),
    /// whereas a foreign DLL matching six of these exact spellings is not a credible accident.
    /// </summary>
    internal static readonly string[] FoundingExportNames =
    {
        "UE5_Init", "UE5_Shutdown", "UE5_GetVersion",
        "UE5_GetGObjectsAddr", "UE5_GetGNamesAddr",
        "UE5_GetObjectByIndex", "UE5_GetObjectFullName",
        "UE5_WalkClassBegin", "UE5_WalkClassGetField", "UE5_WalkClassEnd",
        "UE5_ResolveFName", "UE5_FindObject", "UE5_FindClass",
    };

    /// <summary>Default number of <see cref="FoundingExportNames"/> that must be present.</summary>
    internal const int DefaultExportQuorum = 6;

    /// <summary>
    /// Does this PE export at least <paramref name="quorum"/> of our founding C ABI names? This is
    /// the identity signal that works through an open handle and does not depend on the version
    /// resource surviving a build-system change.
    /// </summary>
    internal static bool HasExportQuorum(Stream pe, int quorum = DefaultExportQuorum)
    {
        IReadOnlySet<string> names = ReadExportNames(pe);
        if (names.Count == 0) return false;
        int hits = 0;
        foreach (string n in FoundingExportNames)
        {
            if (names.Contains(n) && ++hits >= quorum) return true;
        }
        return false;
    }

    // ── Little-endian PE field readers (seekable stream) ──
    private static int ReadU16(Stream s, long off)
    {
        s.Seek(off, SeekOrigin.Begin);
        Span<byte> b = stackalloc byte[2];
        s.ReadExactly(b);
        return BinaryPrimitives.ReadUInt16LittleEndian(b);
    }

    private static uint ReadU32(Stream s, long off)
    {
        s.Seek(off, SeekOrigin.Begin);
        Span<byte> b = stackalloc byte[4];
        s.ReadExactly(b);
        return BinaryPrimitives.ReadUInt32LittleEndian(b);
    }

    /// <summary>Read a null-terminated ASCII DLL name at a file offset, bounded.</summary>
    private static string ReadAsciiZ(Stream s, long off, long len)
    {
        s.Seek(off, SeekOrigin.Begin);
        Span<byte> buf = stackalloc byte[MaxNameLen];
        int n = 0;
        for (; n < MaxNameLen && off + n < len; n++)
        {
            int c = s.ReadByte();
            if (c <= 0) break; // null terminator or EOF
            buf[n] = (byte)c;
        }
        return System.Text.Encoding.ASCII.GetString(buf[..n]);
    }
}
