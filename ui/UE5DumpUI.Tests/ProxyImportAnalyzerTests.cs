using System.Buffers.Binary;
using System.IO;
using System.Text;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Unit tests for the offline PE import-table parser + proxy suggestion logic.
/// The parser is exercised against a hand-built minimal PE32+ image (no live
/// file), which validates the DOS→NT→section→RVA→file-offset math end to end.
/// </summary>
public class ProxyImportAnalyzerTests
{
    // ── Recommendation logic (pure, no PE) ──

    [Fact]
    public void Recommend_NoHistoryNoImports_DefaultsToVersion()
    {
        var s = ProxyImportAnalyzer.Recommend(null, null, null, injected: false);
        Assert.Equal(ProxyType.Version, s.Type);
        Assert.Equal("version · default", s.Display);
    }

    [Fact]
    public void Recommend_RememberedPick_Wins_OverImports()
    {
        // Even when the exe imports dxgi/dinput8, a remembered manual pick is used.
        var imports = new ProxyImportAnalyzer.ProxyImportInfo(false, true, true);
        var s = ProxyImportAnalyzer.Recommend(imports, null, ProxyType.Dxgi, injected: false);
        Assert.Equal(ProxyType.Dxgi, s.Type);
        Assert.Equal("dxgi.dll · last used", s.Display);
    }

    [Fact]
    public void Recommend_ConfirmedProxy_WinsOverEverything()
    {
        // A proxy the DLL confirmed actually loaded beats deployed/injected/imports.
        var imports = new ProxyImportAnalyzer.ProxyImportInfo(false, true, true);
        var s = ProxyImportAnalyzer.Recommend(imports, ProxyType.Dxgi, ProxyType.Version, injected: true);
        Assert.Equal(ProxyType.Dxgi, s.Type);
        Assert.Equal("dxgi.dll · confirmed working", s.Display);
    }

    [Fact]
    public void Recommend_Injected_NoProxy_SurfacesInjectionKnownGood()
    {
        var s = ProxyImportAnalyzer.Recommend(null, null, null, injected: true);
        Assert.Null(s.Type); // injection has no proxy type
        Assert.Equal("injection · no proxy deployed", s.Display);
    }

    [Fact]
    public void Recommend_RememberedProxy_WinsOverInjection()
    {
        // A deployed proxy is a stronger known-good than "also injected once".
        var s = ProxyImportAnalyzer.Recommend(null, null, ProxyType.Version, injected: true);
        Assert.Equal(ProxyType.Version, s.Type);
        Assert.Equal("version.dll · last used", s.Display);
    }

    [Fact]
    public void Recommend_ImportsDxgi_AnnotatesAlternative_ButKeepsVersionDefault()
    {
        var imports = new ProxyImportAnalyzer.ProxyImportInfo(false, false, true);
        var s = ProxyImportAnalyzer.Recommend(imports, null, null, injected: false);
        Assert.Equal(ProxyType.Version, s.Type);   // never auto-escalates to dxgi
        Assert.Equal("version · default · alt: dxgi", s.Display);
    }

    [Fact]
    public void Recommend_ImportsBoth_ListsBothAlternatives()
    {
        var imports = new ProxyImportAnalyzer.ProxyImportInfo(false, true, true);
        var s = ProxyImportAnalyzer.Recommend(imports, null, null, injected: false);
        Assert.Equal("version · default · alt: dxgi, dinput8", s.Display);
    }

    [Fact]
    public void Recommend_ImportsNeither_FlagsHardCase()
    {
        var imports = new ProxyImportAnalyzer.ProxyImportInfo(false, false, false);
        var s = ProxyImportAnalyzer.Recommend(imports, null, null, injected: false);
        Assert.Equal(ProxyType.Version, s.Type);
        Assert.Equal("version · default · no dxgi/dinput8", s.Display);
    }

    [Fact]
    public void FromDllName_MapsProxyNames_NullForNonProxy()
    {
        // The load_mode → ProxyType bridge used by the Phase 2 confirmation gate.
        Assert.Equal(ProxyType.Version, ProxyTypeExtensions.FromDllName("version.dll"));
        Assert.Equal(ProxyType.Dinput8, ProxyTypeExtensions.FromDllName("DINPUT8.DLL")); // case-insensitive
        Assert.Equal(ProxyType.Dxgi, ProxyTypeExtensions.FromDllName("dxgi.dll"));
        Assert.Null(ProxyTypeExtensions.FromDllName("UE5Dumper.dll"));                    // inject → not a proxy
        Assert.Null(ProxyTypeExtensions.FromDllName(""));
        Assert.Null(ProxyTypeExtensions.FromDllName(null));
    }

    // ── PE import parsing (synthetic image) ──

    [Fact]
    public void Analyze_ExeImportingDxgi_DetectsDxgiOnly()
    {
        var pe = BuildPe64WithImports("dxgi.dll");
        var info = ProxyImportAnalyzer.Analyze(new MemoryStream(pe));
        Assert.NotNull(info);
        Assert.True(info!.Value.ImportsDxgi);
        Assert.False(info.Value.ImportsDinput8);
        Assert.False(info.Value.ImportsVersion);
    }

    [Fact]
    public void Analyze_ExeImportingDinput8AndDxgi_DetectsBoth()
    {
        var pe = BuildPe64WithImports("dinput8.dll", "dxgi.dll");
        var info = ProxyImportAnalyzer.Analyze(new MemoryStream(pe));
        Assert.NotNull(info);
        Assert.True(info!.Value.ImportsDxgi);
        Assert.True(info.Value.ImportsDinput8);
    }

    [Fact]
    public void Analyze_IsCaseInsensitive()
    {
        var pe = BuildPe64WithImports("DXGI.DLL");
        var info = ProxyImportAnalyzer.Analyze(new MemoryStream(pe));
        Assert.True(info!.Value.ImportsDxgi);
    }

    [Fact]
    public void Analyze_ExeImportingUnrelatedDll_DetectsNone()
    {
        var pe = BuildPe64WithImports("kernel32.dll");
        var info = ProxyImportAnalyzer.Analyze(new MemoryStream(pe));
        Assert.NotNull(info);
        Assert.False(info!.Value.ImportsDxgi);
        Assert.False(info.Value.ImportsDinput8);
        Assert.False(info.Value.ImportsVersion);
    }

    [Fact]
    public void Analyze_NonIdentityRvaMapping_ResolvesCorrectly()
    {
        // Real exes almost never have VirtualAddress == PointerToRawData; this
        // exercises the RVA→file-offset conversion (rva - VA + PointerToRawData)
        // that the identity-mapped cases above cannot catch (e.g. a va/rawPtr swap).
        var pe = BuildPe64(0x1000, new[] { "dinput8.dll" });
        var info = ProxyImportAnalyzer.Analyze(new MemoryStream(pe));
        Assert.NotNull(info);
        Assert.True(info!.Value.ImportsDinput8);
        Assert.False(info.Value.ImportsDxgi);
    }

    [Fact]
    public void Analyze_RealOnDiskPe_ParsesWithoutThrowing()
    {
        // Smoke test against a genuine multi-section PE (this assembly's own file):
        // real RVA→offset conversion, real section table, real import directory —
        // the parser must return a value (not null / not throw). We don't assert
        // WHICH imports (a managed assembly's import table is minimal), only that a
        // real binary parses cleanly, which the synthetic fixtures cannot guarantee.
        var path = typeof(ProxyImportAnalyzer).Assembly.Location;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return; // single-file/trimmed host with no on-disk location — skip

        using var fs = File.OpenRead(path);
        var info = ProxyImportAnalyzer.Analyze(fs);
        Assert.NotNull(info); // valid PE → parsed; our proxy DLLs simply aren't imported
    }

    [Fact]
    public void Analyze_NotAPe_ReturnsNull()
    {
        Assert.Null(ProxyImportAnalyzer.Analyze(new MemoryStream(new byte[] { 1, 2, 3, 4, 5 })));

        // Valid MZ but no PE signature.
        var bogus = new byte[0x100];
        bogus[0] = 0x4D; bogus[1] = 0x5A;
        BinaryPrimitives.WriteUInt32LittleEndian(bogus.AsSpan(0x3C), 0x80);
        Assert.Null(ProxyImportAnalyzer.Analyze(new MemoryStream(bogus)));
    }

    // ── Modular-build folding (ImportsNone / Merge) ──
    //
    // A MODULAR UE build ships a thin bootstrap .exe with the engine split across
    // sibling *-Win64-Shipping.dll modules (Satisfactory's stub is ~264 KB). Reading
    // the stub alone reports NOTHING imported, which made the Suggested column claim
    // "no dxgi/dinput8" for a game where a dxgi proxy loads perfectly well — D3D12RHI
    // imports it. ProxyDeployService folds the siblings in via these two members;
    // the file-walking half lives there (this type stays OS-free by design).

    [Fact]
    public void ImportsNone_is_true_only_when_nothing_is_imported()
    {
        Assert.True(new ProxyImportAnalyzer.ProxyImportInfo(false, false, false).ImportsNone);
        Assert.False(new ProxyImportAnalyzer.ProxyImportInfo(true, false, false).ImportsNone);
        Assert.False(new ProxyImportAnalyzer.ProxyImportInfo(false, true, false).ImportsNone);
        Assert.False(new ProxyImportAnalyzer.ProxyImportInfo(false, false, true).ImportsNone);
    }

    [Fact]
    public void ImportsNone_detects_the_bootstrap_stub_shape()
    {
        // A stub imports plenty (kernel32 &c.) — just none of OUR three.
        var stub = ProxyImportAnalyzer.Analyze(
            new MemoryStream(BuildPe64WithImports("kernel32.dll", "user32.dll")));
        Assert.NotNull(stub);
        Assert.True(stub!.Value.ImportsNone);
    }

    [Fact]
    public void Merge_folds_a_module_into_the_stub_result()
    {
        // The real Satisfactory case: stub imports none; ApplicationCore/D3D12RHI
        // bring dxgi and Core brings version.
        var stub = new ProxyImportAnalyzer.ProxyImportInfo(false, false, false);
        var rhi = ProxyImportAnalyzer.Analyze(
            new MemoryStream(BuildPe64WithImports("dxgi.dll", "d3d12.dll")))!.Value;
        var core = ProxyImportAnalyzer.Analyze(
            new MemoryStream(BuildPe64WithImports("version.dll")))!.Value;

        var folded = stub.Merge(rhi).Merge(core);
        Assert.True(folded.ImportsDxgi);
        Assert.True(folded.ImportsVersion);
        Assert.False(folded.ImportsDinput8);
        Assert.False(folded.ImportsNone);
    }

    [Fact]
    public void Merge_is_a_pure_or_and_never_clears_a_flag()
    {
        var all = new ProxyImportAnalyzer.ProxyImportInfo(true, true, true);
        var none = new ProxyImportAnalyzer.ProxyImportInfo(false, false, false);
        Assert.Equal(all, all.Merge(none));
        Assert.Equal(all, none.Merge(all));
        Assert.Equal(all, all.Merge(all));
        Assert.Equal(none, none.Merge(none));
    }

    [Fact]
    public void Merge_does_not_mutate_either_operand()
    {
        var a = new ProxyImportAnalyzer.ProxyImportInfo(true, false, false);
        var b = new ProxyImportAnalyzer.ProxyImportInfo(false, false, true);
        _ = a.Merge(b);
        Assert.Equal(new ProxyImportAnalyzer.ProxyImportInfo(true, false, false), a);
        Assert.Equal(new ProxyImportAnalyzer.ProxyImportInfo(false, false, true), b);
    }

    private static byte[] BuildPe64WithImports(params string[] dllNames) =>
        BuildPe64(0x200, dllNames);   // 0x200 == PointerToRawData → identity mapping

    // ── Synthetic PE32+ builder ──
    //
    // Minimal but structurally valid: DOS stub → PE sig → 20-byte FileHeader →
    // 240-byte OptionalHeader (PE32+) → one ".idata" section at file offset 0x200.
    // The import descriptor array lives at the start of the section and the DLL
    // name strings at +0xA0. <paramref name="sectionVa"/> is the section's
    // VirtualAddress: pass 0x200 for identity mapping (RVA == file offset) or any
    // other value (e.g. 0x1000) to exercise the RVA→file-offset conversion.
    private static byte[] BuildPe64(uint sectionVa, string[] dllNames)
    {
        const uint rawPtr = 0x200;                    // section raw data at file 0x200
        var buf = new byte[0x400];

        // DOS header
        buf[0] = 0x4D; buf[1] = 0x5A;                 // 'MZ'
        WriteU32(buf, 0x3C, 0x80);                    // e_lfanew

        // PE signature
        buf[0x80] = 0x50; buf[0x81] = 0x45;           // 'PE\0\0'

        // IMAGE_FILE_HEADER @ 0x84
        WriteU16(buf, 0x84, 0x8664);                  // Machine = x64
        WriteU16(buf, 0x86, 1);                       // NumberOfSections
        WriteU16(buf, 0x94, 0xF0);                    // SizeOfOptionalHeader = 240
        WriteU16(buf, 0x96, 0x22);                    // Characteristics

        // IMAGE_OPTIONAL_HEADER64 @ 0x98 (opt)
        WriteU16(buf, 0x98, 0x20B);                   // Magic = PE32+
        WriteU32(buf, 0xD4, rawPtr);                  // SizeOfHeaders   (opt+60)
        WriteU32(buf, 0x104, 16);                     // NumberOfRvaAndSizes (opt+108)

        // Data directory index 1 (import) @ opt+112 + 8 = 0x110
        int numDesc = dllNames.Length;
        WriteU32(buf, 0x110, sectionVa);              // import table RVA
        WriteU32(buf, 0x114, (uint)((numDesc + 1) * 20)); // import table size

        // Section header @ opt + 240 = 0x188
        var secName = Encoding.ASCII.GetBytes(".idata");
        Array.Copy(secName, 0, buf, 0x188, secName.Length);
        WriteU32(buf, 0x190, 0x200);                  // VirtualSize
        WriteU32(buf, 0x194, sectionVa);              // VirtualAddress
        WriteU32(buf, 0x198, 0x200);                  // SizeOfRawData
        WriteU32(buf, 0x19C, rawPtr);                 // PointerToRawData

        // Import descriptors @ file rawPtr (RVA sectionVa), 20 bytes each; a null
        // descriptor terminates the array (left zero).
        for (int i = 0; i < numDesc; i++)
        {
            int d = (int)rawPtr + i * 20;
            uint nameRva = sectionVa + 0xA0 + (uint)(i * 0x10);
            int nameFileOff = (int)(nameRva - sectionVa + rawPtr);
            WriteU32(buf, d + 12, nameRva);           // IMAGE_IMPORT_DESCRIPTOR.Name
            WriteU32(buf, d + 16, 0x100);             // FirstThunk (nonzero ≠ terminator)

            var nb = Encoding.ASCII.GetBytes(dllNames[i]);
            Array.Copy(nb, 0, buf, nameFileOff, nb.Length); // string (null-term already)
        }

        return buf;
    }

    // ── Export-name reader: the "is this DLL ours" signal used by leftover-proxy cleanup ──

    [Fact]
    public void ReadExportNames_NonPeInput_ReturnsEmptyRatherThanThrowing()
    {
        // Every caller treats "cannot tell" as "not ours", so this must fail closed, never throw.
        using var junk = new MemoryStream(Encoding.ASCII.GetBytes("this is not a PE file at all"));
        Assert.Empty(ProxyImportAnalyzer.ReadExportNames(junk));

        using var empty = new MemoryStream();
        Assert.Empty(ProxyImportAnalyzer.ReadExportNames(empty));
        Assert.False(ProxyImportAnalyzer.HasExportQuorum(empty));
    }

    [Fact]
    public void HasExportQuorum_RealBuiltProxy_SatisfiesIt()
    {
        // Asserted against the ACTUAL artifact so that a build-system change which drops the UE5_*
        // exports (or renames one) turns into a red build rather than a silent loss of recall — the
        // cleanup would quietly stop recognising its own DLLs.
        string? proxy = FindBuiltProxy();
        Assert.SkipWhen(proxy == null, "dist/proxy/version.dll not built in this checkout");

        using var fs = File.OpenRead(proxy!);
        var names = ProxyImportAnalyzer.ReadExportNames(fs);

        // Every founding name must be present, not merely the quorum — the quorum exists to tolerate
        // a FUTURE rename, not to excuse one today.
        foreach (string expected in ProxyImportAnalyzer.FoundingExportNames)
            Assert.Contains(expected, names);

        fs.Position = 0;
        Assert.True(ProxyImportAnalyzer.HasExportQuorum(fs));
    }

    [Fact]
    public void HasExportQuorum_GenuineWindowsSystemDll_IsRejected()
    {
        // The catastrophic false positive this signal must never produce: deleting the real
        // version.dll. It exports 17 names, none of them ours.
        string sys = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                                  "version.dll");
        Assert.SkipWhen(!File.Exists(sys), "System32\\version.dll not present");

        using var fs = File.OpenRead(sys);
        Assert.False(ProxyImportAnalyzer.HasExportQuorum(fs));
    }

    // ── Load-risk advisory (pure) ──
    //
    // These pin the REVERSAL of a rule that used to hard-refuse a deploy: "not in the import
    // table" meant "would never load". It was false. 21 Steam UE games were measured on
    // 2026-08-10 and 11 of them run a WORKING version.dll proxy whose .exe names version.dll
    // in neither the import nor the delay-import directory (DQ7R self-confirmed the load).
    // Every assertion below is "returns null" = "do not block, do not even warn".

    [Fact]
    public void LoadsDynamically_VersionAndDinput8_Only()
    {
        // version/dinput8 arrive via a run-time LoadLibrary; dxgi/winmm are static-import
        // hijacks. Only the former two may be deployed with no import-table evidence.
        Assert.True(ProxyImportAnalyzer.LoadsDynamically(ProxyType.Version));
        Assert.True(ProxyImportAnalyzer.LoadsDynamically(ProxyType.Dinput8));
        Assert.False(ProxyImportAnalyzer.LoadsDynamically(ProxyType.Dxgi));
        Assert.False(ProxyImportAnalyzer.LoadsDynamically(ProxyType.Winmm));
    }

    [Fact]
    public void DescribeLoadRisk_VersionNotImported_IsSilent()
    {
        // THE REGRESSION TEST. This exact shape — imports dxgi + winmm, not version — is
        // DragonSword Awakening, DQ7R, P3R, Stray, Palworld and 6 more. The old rule refused
        // the deploy outright; the row then read "NotDeployed" with a blank Error.
        var imports = new ProxyImportAnalyzer.ProxyImportInfo(
            ImportsVersion: false, ImportsDinput8: false, ImportsDxgi: true, ImportsWinmm: true);
        Assert.Null(ProxyImportAnalyzer.DescribeLoadRisk(imports, ProxyType.Version));
    }

    [Fact]
    public void DescribeLoadRisk_Dinput8NotImported_IsSilent()
    {
        var imports = new ProxyImportAnalyzer.ProxyImportInfo(false, false, true, true);
        Assert.Null(ProxyImportAnalyzer.DescribeLoadRisk(imports, ProxyType.Dinput8));
    }

    [Fact]
    public void DescribeLoadRisk_ImportedFlavour_IsSilent()
    {
        // Statically imported → the loader resolves it from the .exe directory. Guaranteed.
        var imports = new ProxyImportAnalyzer.ProxyImportInfo(false, false, true, true);
        Assert.Null(ProxyImportAnalyzer.DescribeLoadRisk(imports, ProxyType.Dxgi));
        Assert.Null(ProxyImportAnalyzer.DescribeLoadRisk(imports, ProxyType.Winmm));
    }

    [Fact]
    public void DescribeLoadRisk_StaticOnlyFlavourNotImported_Warns_ButNamesAWayOut()
    {
        // dxgi into a game that never names it genuinely cannot load, and fails with NO log —
        // the one case still worth saying out loud. It must stay a note, not a refusal.
        var imports = new ProxyImportAnalyzer.ProxyImportInfo(
            ImportsVersion: false, ImportsDinput8: false, ImportsDxgi: false, ImportsWinmm: true);
        string? note = ProxyImportAnalyzer.DescribeLoadRisk(imports, ProxyType.Dxgi);
        Assert.NotNull(note);
        Assert.Contains("dxgi.dll", note);
        // Must offer the alternatives rather than dead-end: winmm IS imported here, and
        // version is always worth trying.
        Assert.Contains("version", note);
        Assert.Contains("winmm", note);
    }

    [Fact]
    public void DescribeLoadRisk_UnparseablePe_IsSilent()
    {
        // "Could not tell" must never become "will not work".
        Assert.Null(ProxyImportAnalyzer.DescribeLoadRisk(null, ProxyType.Dxgi));
    }

    [Fact]
    public void DescribeLoadRisk_ModularStubImportingNothing_IsSilent()
    {
        // A stub .exe naming none of the four is a modular build whose real modules were
        // already folded in by Merge. Warning on it would fire on every modular game.
        var none = new ProxyImportAnalyzer.ProxyImportInfo(false, false, false, false);
        Assert.True(none.ImportsNone);
        Assert.Null(ProxyImportAnalyzer.DescribeLoadRisk(none, ProxyType.Dxgi));
    }

    // ── Import-bypass screening ([PROXYLOAD-2026-08-17]) ──
    //
    // The counter-intuitive half of the finding: a flavour the .exe imports DIRECTLY may be
    // PRE-EMPTED by an already-mapped System32 copy (OCTOPATH's version.dll proxy is silently
    // ignored). It is a HEURISTIC — warns, never blocks — and is the exact inverse condition of
    // DescribeLoadRisk (which fires when a static-only flavour is ABSENT).

    [Fact]
    public void DescribeImportBypassRisk_ImportedFlavour_Warns_AsAHeuristic()
    {
        var imports = new ProxyImportAnalyzer.ProxyImportInfo(ImportsVersion: true, false, false, false);
        string? note = ProxyImportAnalyzer.DescribeImportBypassRisk(imports, ProxyType.Version);
        Assert.NotNull(note);
        Assert.Contains("version.dll", note);
        Assert.Contains("Heuristic", note);      // worded as a maybe, never a certainty
        Assert.Contains("not observed", note);   // points at the Load column that settles it
    }

    [Fact]
    public void DescribeImportBypassRisk_FiresForEveryImportedFlavour()
    {
        // The finding generalises across all four base names; screen them all.
        Assert.NotNull(ProxyImportAnalyzer.DescribeImportBypassRisk(
            new ProxyImportAnalyzer.ProxyImportInfo(false, true, false, false), ProxyType.Dinput8));
        Assert.NotNull(ProxyImportAnalyzer.DescribeImportBypassRisk(
            new ProxyImportAnalyzer.ProxyImportInfo(false, false, true, false), ProxyType.Dxgi));
        Assert.NotNull(ProxyImportAnalyzer.DescribeImportBypassRisk(
            new ProxyImportAnalyzer.ProxyImportInfo(false, false, false, true), ProxyType.Winmm));
    }

    [Fact]
    public void DescribeImportBypassRisk_NotImported_IsSilent()
    {
        // version not imported here → it loads dynamically → nothing to warn about.
        var imports = new ProxyImportAnalyzer.ProxyImportInfo(false, false, true, false);
        Assert.Null(ProxyImportAnalyzer.DescribeImportBypassRisk(imports, ProxyType.Version));
    }

    [Fact]
    public void DescribeImportBypassRisk_ModularStub_And_UnparseablePe_AreSilent()
    {
        // "Names nothing" is a folded modular stub, and null is an unparseable PE — both mean
        // "cannot tell", which must never become a warning.
        var none = new ProxyImportAnalyzer.ProxyImportInfo(false, false, false, false);
        Assert.True(none.ImportsNone);
        Assert.Null(ProxyImportAnalyzer.DescribeImportBypassRisk(none, ProxyType.Version));
        Assert.Null(ProxyImportAnalyzer.DescribeImportBypassRisk(null, ProxyType.Version));
    }

    [Fact]
    public void DescribeDeployAdvisory_PicksWhicheverApplies_AndNeverBoth()
    {
        // Imported → bypass note; static-only absent → never-loads note; the two partition on
        // Imports(type), so at most one ever fires.
        var octopath = new ProxyImportAnalyzer.ProxyImportInfo(
            ImportsVersion: true, false, ImportsDxgi: true, ImportsWinmm: true);
        string? ver = ProxyImportAnalyzer.DescribeDeployAdvisory(octopath, ProxyType.Version);
        Assert.NotNull(ver);
        Assert.Contains("imported directly", ver);

        var winmmOnly = new ProxyImportAnalyzer.ProxyImportInfo(false, false, false, true);
        string? dxgi = ProxyImportAnalyzer.DescribeDeployAdvisory(winmmOnly, ProxyType.Dxgi);
        Assert.NotNull(dxgi);
        Assert.Contains("never load", dxgi);

        // version absent → loads dynamically → nothing to say either way.
        Assert.Null(ProxyImportAnalyzer.DescribeDeployAdvisory(winmmOnly, ProxyType.Version));
    }

    [Fact]
    public void Recommend_VersionImported_CautionsAndSuggestsInjection_ButKeepsTypeVersion()
    {
        // OCTOPATH's default: version.dll is imported, so the default pick is at bypass risk. The
        // recommended TYPE stays version (never auto-changed); only the display flags the heuristic.
        var imports = new ProxyImportAnalyzer.ProxyImportInfo(ImportsVersion: true, false, true, true);
        var s = ProxyImportAnalyzer.Recommend(imports, null, null, injected: false);
        Assert.Equal(ProxyType.Version, s.Type);
        Assert.Contains("may be bypassed", s.Display);
        Assert.Contains("injection", s.Display);
    }

    [Fact]
    public void Recommend_VersionImported_HistoryStillWins_OverTheHeuristic()
    {
        // A confirmed pick is a stronger signal than the bypass heuristic and must not be overridden.
        var imports = new ProxyImportAnalyzer.ProxyImportInfo(ImportsVersion: true, false, false, false);
        var s = ProxyImportAnalyzer.Recommend(imports, ProxyType.Winmm, null, injected: false);
        Assert.Equal(ProxyType.Winmm, s.Type);
        Assert.Equal("winmm.dll · confirmed working", s.Display);
    }

    // ── Load signal (ClassifyLoad) — pure ──

    [Fact]
    public void ClassifyLoad_NoFolder_IsUnknown_NotFailure()
    {
        var (sig, disp) = ProxyImportAnalyzer.ClassifyLoad(false, null, DateTime.Now, 21);
        Assert.Equal(ProxyImportAnalyzer.ProxyLoadSignal.Unknown, sig);
        Assert.Equal("not observed", disp);   // honest unknown, never "failed"
    }

    [Fact]
    public void ClassifyLoad_RecentFolder_IsObserved_WithDate()
    {
        var now = new DateTime(2026, 8, 18, 12, 0, 0);
        var (sig, disp) = ProxyImportAnalyzer.ClassifyLoad(true, now.AddDays(-1), now, 21);
        Assert.Equal(ProxyImportAnalyzer.ProxyLoadSignal.Observed, sig);
        Assert.Equal("loaded 2026-08-17", disp);
    }

    [Fact]
    public void ClassifyLoad_OldFolder_IsStale_AndNeverClaimsLoadedNow()
    {
        // The finding's explicit warning: a leftover folder from a previous BUILD must not read as a
        // current load. The date is always shown AND anything past the window is flagged stale.
        var now = new DateTime(2026, 8, 18);
        var (sig, disp) = ProxyImportAnalyzer.ClassifyLoad(true, now.AddDays(-30), now, 21);
        Assert.Equal(ProxyImportAnalyzer.ProxyLoadSignal.ObservedStale, sig);
        Assert.Contains("(stale)", disp);
        Assert.Contains("2026-07-19", disp);
    }

    [Fact]
    public void ClassifyLoad_PresentButNoTimestamp_IsObserved_WithoutDate()
    {
        var (sig, disp) = ProxyImportAnalyzer.ClassifyLoad(true, null, DateTime.Now, 21);
        Assert.Equal(ProxyImportAnalyzer.ProxyLoadSignal.Observed, sig);
        Assert.Equal("loaded", disp);
    }

    // ── Log-folder join key — must mirror dll/src/Sein.cpp InitProcessMirror ──

    [Theory]
    [InlineData(@"D:\Steam\common\Octopath\Binaries\Win64\Octopath_Traveler-Win64-Shipping.exe",
                "Octopath_Traveler-Win64-Shipping")]
    [InlineData("P3R.exe", "P3R")]                        // bare leaf, single extension
    [InlineData("game.with.dots.exe", "game.with.dots")] // only the LAST extension is dropped
    [InlineData("NoExtension", "NoExtension")]            // no dot → unchanged
    [InlineData("a/b/c/Foo.exe", "Foo")]                  // forward slashes too
    public void ProcessLogFolderName_MatchesTheDllRule(string input, string expected)
    {
        Assert.Equal(expected, ProxyImportAnalyzer.ProcessLogFolderName(input));
    }

    [Fact]
    public void ProcessLogFolderName_EmptyInput_IsEmpty()
    {
        Assert.Equal("", ProxyImportAnalyzer.ProcessLogFolderName(""));
    }

    /// <summary>Locate dist/proxy/version.dll by walking up from the test assembly.</summary>
    private static string? FindBuiltProxy()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "dist", "proxy", "version.dll");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static void WriteU16(byte[] b, int off, ushort v) =>
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(off), v);

    private static void WriteU32(byte[] b, int off, uint v) =>
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(off), v);
}
