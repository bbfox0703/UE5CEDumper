using UE5DumpUI.Core;
using UE5DumpUI.Services;

namespace UE5DumpUI.Helpers;

/// <summary>
/// [AOBMAKER-EVAL-2026-09-29] The HEX / ASM / +CE row actions for panels that hold an <see cref="AobMakerStatus"/>.
/// Each returns the status line to show, and publishes what the call learned about the pipe. A panel that keeps its own
/// availability flag calls the form that takes the bridge, and publishes <c>IsAvailable</c> itself.
///
/// <para><b>Why the text comes back instead of being set here.</b> Every panel has its own status property, and
/// the status strings live in en.axaml ([VM-INLINE-STRINGS]): a view model assigning the returned string adds no
/// literal of its own.</para>
///
/// <para>The per-row buttons Live Walker already had do the same work inline; they predate this helper and are
/// left alone.</para>
/// </summary>
internal static class AobMakerActions
{
    internal const string KeyHexDone = "str.AobMaker.Hex.Done";
    internal const string KeyAsmDone = "str.AobMaker.Asm.Done";
    internal const string KeyNavRefused = "str.AobMaker.Nav.Refused";
    internal const string KeyRecordDone = "str.AobMaker.Record.Done";
    internal const string KeyRecordRefused = "str.AobMaker.Record.Refused";

    internal const string KeyNoOwnAddress = "str.AobMaker.Row.NoOwnAddress";

    /// <summary>The plugin wants a bare hex address.</summary>
    internal static string StripHexPrefix(string addr)
        => addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? addr[2..] : addr;

    /// <summary>
    /// [AOBM-VALUE-ROWS-CE] A snapshot-built row names an array element <c>Array[3]</c> or <c>Array[3].Inner</c> and
    /// points it at the OWNING object's base: a capture cannot record the element's heap address (see
    /// <c>SnapshotStore</c>). Such a row has no address of its own to send to CE.
    /// </summary>
    internal static bool IsSnapshotElementRow(string displayName) => displayName.Contains('[');

    /// <summary>The status line for refusing <see cref="IsSnapshotElementRow"/>.</summary>
    internal static string NoOwnAddressText(string displayName)
        => AobMakerStatus.Say(KeyNoOwnAddress,
            "{0} is an array element: the snapshot kept only its owner's address, so there is nothing of its own to send to CE",
            displayName);

    /// <summary><c>obj + offset</c> as CE-ready hex, or empty when the base does not parse.</summary>
    internal static string OffsetAddress(string objAddr, int offset)
        => ulong.TryParse(StripHexPrefix(objAddr), System.Globalization.NumberStyles.HexNumber,
                          System.Globalization.CultureInfo.InvariantCulture, out var baseAddr)
            ? $"0x{baseAddr + (ulong)offset:X}"
            : "";

    /// <summary>Park CE's hex view (the Memory Viewer's bottom pane) on <paramref name="address"/>.</summary>
    internal static Task<string> HexAsync(AobMakerStatus status, string address, string label, ILoggingService log)
        => NavigateAsync(status, address, label, log, disassembler: false);

    /// <summary>Park CE's disassembler (the Memory Viewer's top pane) on <paramref name="address"/>.</summary>
    internal static Task<string> AsmAsync(AobMakerStatus status, string address, string label, ILoggingService log)
        => NavigateAsync(status, address, label, log, disassembler: true);

    private static async Task<string> NavigateAsync(AobMakerStatus status, string address, string label,
                                                    ILoggingService log, bool disassembler)
    {
        var bridge = status.Bridge;
        if (bridge == null || string.IsNullOrEmpty(address)) return "";
        var (text, _) = await MoveViewAsync(bridge, address, label, log, disassembler);
        status.Apply(bridge.IsAvailable);
        return text;
    }

    /// <summary>
    /// [AOBM-SYSTAB-ASM-SILENT] Move CE's hex view or disassembler to <paramref name="address"/> for a panel that keeps
    /// its own availability flag instead of an <see cref="AobMakerStatus"/>: the caller publishes
    /// <c>bridge.IsAvailable</c> itself. <c>Ok</c> says whether the view moved, so a panel that shows success and
    /// failure on different lines can tell them apart without parsing the text.
    /// </summary>
    internal static async Task<(string Text, bool Ok)> MoveViewAsync(IAobMakerBridge bridge, string address,
        string label, ILoggingService? log, bool disassembler)
    {
        var bare = StripHexPrefix(address);
        bool ok;
        try
        {
            ok = disassembler
                ? await bridge.NavigateDisassemblerAsync(bare)
                : await bridge.NavigateHexViewAsync(bare);
        }
        catch (Exception ex)
        {
            log?.Error($"AOBMaker {(disassembler ? "ASM" : "HEX")} failed for {label} @ {address}", ex);
            ok = false;
        }
        if (ok)
            return (disassembler
                ? AobMakerStatus.Say(KeyAsmDone, "CE disassembler: {0} @ {1}", label, address)
                : AobMakerStatus.Say(KeyHexDone, "CE hex view: {0} @ {1}", label, address), true);
        return (bridge.IsAvailable
            ? AobMakerStatus.Say(KeyNavRefused, "Cheat Engine did not move its view to {0} @ {1}", label, address)
            : AobMakerUnavailable.Text(bridge), false);   // [W1-PIPEBUSY-STATUS] busy is not "open Cheat Engine"
    }

    /// <summary>
    /// Add one typed memory record at <paramref name="address"/> to CE's address list, named like Live Walker's +CE
    /// records (<see cref="PackedLayoutNotice.RecordNamePrefix"/>).
    /// <para>When CE refuses, the [AOBM-ATTACH-CHECK] verdict is appended if it has one: a CE with no process, or the
    /// wrong one, is the refusal a user can actually fix. <paramref name="gamePid"/> 0 skips that question.</para>
    /// </summary>
    internal static async Task<string> AddRecordAsync(AobMakerStatus status, string name, string address,
        CeXmlExportService.CeRecordType type, ILoggingService log, int gamePid = 0, string gameModule = "",
        int stringLength = 0)
    {
        var bridge = status.Bridge;
        if (bridge == null || string.IsNullOrEmpty(address)) return "";
        var push = await PushRecordAsync(bridge, PackedLayoutNotice.RecordNamePrefix + name, address, type,
                                         stringLength, log);
        bool ok = push.Added;
        status.Apply(bridge.IsAvailable);
        if (ok) return AobMakerStatus.Say(KeyRecordDone, "Added to CE: {0}", name) + DegradedSuffix(push);
        if (!bridge.IsAvailable) return AobMakerUnavailable.Text(bridge);

        var refused = AobMakerStatus.Say(KeyRecordRefused, "Cheat Engine refused the record for {0}", name);
        var why = await status.CheckAttachAsync(gamePid, gameModule);
        return string.IsNullOrEmpty(why) ? refused : refused + " — " + why;
    }

    /// <summary>
    /// [AOBM-EXPORT-GWORLD-AOB] Cheat Engine's name for an export the DLL found a pointer through, or "" when it
    /// cannot be named. Measured on Satisfactory with CE 7.7 (2026-10-01): CE lists exports UNDECORATED, so
    /// <c>?GWorld@@3VUWorldProxy@@A</c> resolves as <c>GWorld</c> and never under its MSVC name.
    /// </summary>
    internal static string CeExportSymbol(string mangled)
    {
        // "?<name>@@3<type>": a global variable in no namespace, the shape of every export Himmel looks up. A scoped
        // name ("?Foo@Bar@@3...") would undecorate to Bar::Foo, which CE's lookup was never measured on, so it gets
        // no name rather than a guessed one; so does a function ("@@Q...", "@@Y...").
        if (string.IsNullOrEmpty(mangled) || mangled[0] != '?') return "";
        int at = mangled.IndexOf("@@3", 1, StringComparison.Ordinal);
        if (at <= 1 || at + 3 >= mangled.Length) return "";
        var name = mangled.Substring(1, at - 1);
        if (char.IsAsciiDigit(name[0])) return "";
        foreach (var ch in name)
            if (!char.IsAsciiLetterOrDigit(ch) && ch != '_') return "";
        return name;
    }

    internal const string KeyRecordAsByte = "str.AobMaker.Record.AsByte";
    internal const string KeyRecordAsPointer = "str.AobMaker.Record.AsPointer";

    /// <summary>[AOBM-PLUSCE-FIDELITY] What a +CE push did. <c>Degraded</c> is set when the record went in, but in the
    /// <c>CreateMemoryRecord</c> form because the plugin could not build it as it is (see
    /// <see cref="CeXmlExportService.CeRecordType.NeedsRecordTree"/>).</summary>
    internal readonly record struct RecordPush(bool Added, bool Degraded, CeXmlExportService.CeRecordType Type);

    /// <summary>
    /// [AOBM-PLUSCE-FIDELITY] The one +CE push every site shares. A record <c>CreateMemoryRecord</c> cannot express goes
    /// through the plugin's record tree; only a plugin that cannot build it -- no tree at all, or no bits -- gets the
    /// <c>CreateMemoryRecord</c> form instead, flagged <c>Degraded</c> so the status can say what arrived.
    /// <para>A tree Cheat Engine refused is NOT retried in the old form: the refusal is the answer the user needs, and
    /// a byte record on top of it would read as success.</para>
    /// </summary>
    internal static async Task<RecordPush> PushRecordAsync(IAobMakerBridge bridge, string description,
        string address, CeXmlExportService.CeRecordType type, int stringLength, ILoggingService log)
    {
        bool degraded = false;
        if (type.NeedsRecordTree)
        {
            RecordTreeResult tree;
            try
            {
                tree = await bridge.CreateRecordTreeAsync(description,
                    new[] { ToRecordNode(description, address, type, stringLength) });
            }
            catch (Exception ex)
            {
                log.Error($"AOBMaker +CE record tree failed for {description} @ {address}", ex);
                return new RecordPush(false, false, type);
            }
            switch (tree.Outcome)
            {
                case RecordTreeOutcome.Created:
                    return new RecordPush(true, false, type);
                case RecordTreeOutcome.Failed:
                    log.Warn($"AOBMaker +CE: Cheat Engine did not build {description} @ {address}: {tree.Message}");
                    return new RecordPush(tree.Created > 0, false, type);
                case RecordTreeOutcome.Unavailable:
                    return new RecordPush(false, false, type);
            }
            log.Info($"AOBMaker +CE: {description} goes as its CreateMemoryRecord form ({tree.Outcome}" +
                     (tree.MissingFeatures.Count > 0 ? ": " + string.Join(", ", tree.MissingFeatures) : "") + ")");
            degraded = true;
        }

        bool ok;
        try
        {
            ok = await bridge.CreateMemoryRecordAsync(description, StripHexPrefix(address), type.ValueType,
                                                      type.IsSigned, type.ShowAsHex);
        }
        catch (Exception ex)
        {
            log.Error($"AOBMaker +CE failed for {description} @ {address}", ex);
            ok = false;
        }
        return new RecordPush(ok, degraded && ok, type);
    }

    /// <summary>[AOBM-PLUSCE-FIDELITY] The note a degraded push adds after "Added to CE: …", or "".</summary>
    internal static string DegradedSuffix(RecordPush push)
    {
        if (!push.Added || !push.Degraded) return "";
        return " — " + (push.Type.BitStart >= 0 ? AsByteText() : AsPointerText());
    }

    internal static string AsByteText() => AobMakerStatus.Say(KeyRecordAsByte,
        "as its whole byte: this AOBMaker plugin cannot set a bit (needs AOBMaker v20260930 or later)");

    internal static string AsPointerText() => AobMakerStatus.Say(KeyRecordAsPointer,
        "as its data pointer: this AOBMaker plugin cannot build a string record (update AOBMaker)");

    /// <summary>
    /// [AOBM-PLUSCE-FIDELITY] The record-tree node for a +CE record that needs one, shaped like the Copy CE XML leaf for
    /// the same field: a string reads through the FString's data pointer (<c>Offsets [0]</c>) at the String Len the
    /// exports use (<paramref name="stringLength"/>, or their default when 0); a bit-field bool is a Binary record at
    /// its byte.
    /// </summary>
    internal static CeRecordNode ToRecordNode(string description, string address,
        CeXmlExportService.CeRecordType type, int stringLength)
    {
        var bare = StripHexPrefix(address);
        if (type.String != CeXmlExportService.CeStringKind.None)
            return new CeRecordNode(description, bare, "String", Offsets: new[] { 0 },
                StringLength: stringLength > 0 ? stringLength : Constants.DefaultCeStringLength,
                Unicode: type.String == CeXmlExportService.CeStringKind.Utf16);
        return new CeRecordNode(description, bare, "Binary", ShowAsHex: type.ShowAsHex, IsSigned: type.IsSigned,
                                BitStart: type.BitStart, BitLength: type.BitLength);
    }

    internal const string KeyRecordsDegraded = "str.AobMaker.Record.SomeDegraded";

    /// <summary>[AOBM-PLUSCE-FIDELITY] The batch form of <see cref="DegradedSuffix"/>, or "" when none was.</summary>
    internal static string DegradedCountText(int degraded) => degraded <= 0 ? "" : AobMakerStatus.Say(KeyRecordsDegraded,
        "{0} of them as a whole byte or a data pointer: this AOBMaker plugin cannot build them as they are (update AOBMaker)",
        degraded);

    internal const string KeyDisasmNoCode = "str.AobMaker.Disasm.NoCode";
    internal const string KeyDisasmDone = "str.AobMaker.Disasm.Done";
    internal const string KeyDisasmRecordOnly = "str.AobMaker.Disasm.RecordOnly";
    internal const string KeyDisasmRefused = "str.AobMaker.Disasm.Refused";

    /// <summary>ByteArray: the record exists to be right-clicked ("Find out what executes this"), not to show a value.</summary>
    private const int CeVtByteArray = 8;

    /// <summary>
    /// [AOBM-FUNC-DISASM] Resolve a UFunction's native entry (<c>UFunction::Func</c>) through the DLL, add a ByteArray
    /// record labelled "<paramref name="funcName"/> (code)" and move CE's disassembler there. The sequence is the one
    /// <c>PropertyXrefDialog</c>'s "Disassemble in CE" introduced; that dialog keeps its own copy because it colours
    /// each outcome.
    /// <para>A native function lands on its exec thunk; a Blueprint one on the script interpreter, which is still the
    /// honest answer to "what runs when this is called".</para>
    /// <para>The two pushes are not interchangeable, so the text says which one failed: the record is what the user
    /// right-clicks, the navigation is what they look at.</para>
    /// </summary>
    internal static async Task<string> DisassembleFunctionAsync(IAobMakerBridge bridge, IDumpService dump,
        string funcAddr, string funcName, ILoggingService? log)
    {
        string codeAddr;
        try { codeAddr = await dump.GetFunctionCodeAddrAsync(funcAddr); }
        catch (Exception ex)
        {
            log?.Error($"AOBMaker ASM: resolving the code address of {funcName} failed", ex);
            codeAddr = "";
        }
        if (string.IsNullOrEmpty(codeAddr) || codeAddr == "0x0")
            return AobMakerStatus.Say(KeyDisasmNoCode,
                "No native code address for {0} (a Blueprint-only function, or UFunction::Func is not detected on this engine)",
                funcName);

        var bare = StripHexPrefix(codeAddr);
        bool recorded = false, navigated = false;
        try
        {
            recorded = await bridge.CreateMemoryRecordAsync(funcName + " (code)", bare, CeVtByteArray, false, true);
            navigated = await bridge.NavigateDisassemblerAsync(bare);
        }
        catch (Exception ex) { log?.Error($"AOBMaker ASM push failed for {funcName}", ex); }

        if (recorded && navigated)
            return AobMakerStatus.Say(KeyDisasmDone, "{0}: CE disassembler @ {1}", funcName, codeAddr);
        if (recorded)
            return AobMakerStatus.Say(KeyDisasmRecordOnly,
                "Added {0} to the CE table @ {1}, but CE did not open its disassembler there: find the record and browse to it",
                funcName, codeAddr);
        return bridge.IsAvailable
            ? AobMakerStatus.Say(KeyDisasmRefused, "Cheat Engine refused the push for {0}: nothing was added", funcName)
            : AobMakerUnavailable.Text(bridge);
    }

    /// <summary>The same, for a panel that holds an <see cref="AobMakerStatus"/>; publishes what the pushes learned.</summary>
    internal static async Task<string> DisassembleFunctionAsync(AobMakerStatus status, IDumpService dump,
        string funcAddr, string funcName, ILoggingService log)
    {
        if (status.Bridge == null || string.IsNullOrEmpty(funcAddr)) return "";
        var text = await DisassembleFunctionAsync(status.Bridge, dump, funcAddr, funcName, log);
        status.Apply(status.Bridge.IsAvailable);
        return text;
    }

    // --- [AOBM-GNAMES-SYMBOL] symbol registration through AOBMaker.UI's GenerateAob ---

    internal const string KeyGenAobPending = "str.AobMaker.GenAob.Pending";
    internal const string KeyGenAobNotRunning = "str.AobMaker.GenAob.NotRunning";
    internal const string KeyGenAobBusy = "str.AobMaker.GenAob.Busy";
    internal const string KeyGenAobRefused = "str.AobMaker.GenAob.Refused";
    internal const string KeyGenAobNoReply = "str.AobMaker.GenAob.NoReply";
    internal const string KeyGenAobFailed = "str.AobMaker.GenAob.Failed";
    internal const string KeyGenAobNotRip = "str.AobMaker.GenAob.NotRipRelative";
    internal const string KeyGenAobNoDirectRip = "str.AobMaker.GenAob.NoDirectRip";
    internal const string KeyGenAobUnreadable = "str.AobMaker.GenAob.Unreadable";
    internal const string KeyGenAobMismatch = "str.AobMaker.GenAob.Mismatch";

    internal static string GenerateAobPendingText(string symbol)
        => AobMakerStatus.Say(KeyGenAobPending, "Asking AOBMaker.UI for a unique AOB for '{0}'…", symbol);

    /// <summary>Why there is no AOB, in the words that tell the user what to do about it.</summary>
    internal static string GenerateAobFailureText(GenerateAobResult result, string symbol) => result.Failure switch
    {
        GenerateAobFailure.NotRunning => AobMakerStatus.Say(KeyGenAobNotRunning,
            "AOBMaker.UI is not running: '{0}' needs the AOBMaker app open, which makes the AOB (Cheat Engine alone is not enough)",
            symbol),
        GenerateAobFailure.Busy => AobMakerStatus.Say(KeyGenAobBusy,
            "AOBMaker.UI is busy with another request: try '{0}' again in a moment", symbol),
        GenerateAobFailure.Refused => AobMakerStatus.Say(KeyGenAobRefused,
            "AOBMaker.UI refused this app: run both at the same elevation (an elevated AOBMaker rejects an unelevated caller)"),
        GenerateAobFailure.NoReply => AobMakerStatus.Say(KeyGenAobNoReply,
            "AOBMaker.UI did not answer: an older build may not have GenerateAob"),
        _ => AobMakerStatus.Say(KeyGenAobFailed, "AOBMaker.UI could not make a unique AOB for '{0}': {1}",
            symbol, result.ServerMessage ?? "no reason given"),
    };

    internal static string GenerateAobNotRipText(string symbol, ulong seedAddr)
        => AobMakerStatus.Say(KeyGenAobNotRip,
            "Not pushed: AOBMaker.UI did not decode the instruction at 0x{1:X} as RIP-relative, so no symbol script can replay '{0}'",
            symbol, seedAddr);

    internal static string GenerateAobNoDirectRipText(string symbol)
        => AobMakerStatus.Say(KeyGenAobNoDirectRip,
            "Not pushed: no instruction in the scan hit for '{0}' points straight at it (its signature adjusts the address, dereferences it or follows a call), so a symbol script cannot replay it",
            symbol);

    internal static string GenerateAobUnreadableText(string symbol, ulong addr)
        => AobMakerStatus.Say(KeyGenAobUnreadable,
            "Not pushed: could not read game memory at 0x{1:X} to check where '{0}' would land", symbol, addr);

    internal static string GenerateAobMismatchText(string symbol, ulong target, string resolved)
        => AobMakerStatus.Say(KeyGenAobMismatch,
            "Not pushed: the AOB would register '{0}' at 0x{1:X}, but the DLL resolved {2} (this signature dereferences or adjusts)",
            symbol, target, resolved);

    // [AOBM-ACTIVATE-RESULT] What SYM says about a symbol script beyond "created" (AOBMaker v20260930 reports it).
    internal const string KeySymbolNotActivated = "str.Pointers.Symbol.NotActivated";
    internal const string KeySymbolNotRegistered = "str.Pointers.Symbol.NotRegistered";
    internal const string KeySymbolActivationUnknown = "str.Pointers.Symbol.ActivationUnknown";
    internal const string KeySymbolTimedOut = "str.Pointers.Symbol.TimedOut";
    internal const string KeySymbolNoReason = "str.Pointers.Symbol.NoReason";

    /// <summary>Stands in for CE's reason when the plugin gave none.</summary>
    internal static string SymbolNoReasonText() => AobMakerStatus.Say(KeySymbolNoReason, "no reason given");

    internal static string SymbolNotActivatedText(string symbol, string reason)
        => AobMakerStatus.Say(KeySymbolNotActivated,
            "CE symbol script '{0}' was added, but Cheat Engine did not enable it: {1}", symbol, reason);

    internal static string SymbolNotRegisteredText(string symbol, string reason)
        => AobMakerStatus.Say(KeySymbolNotRegistered,
            "CE symbol script '{0}' is enabled, but the symbol is not usable: {1}", symbol, reason);

    internal static string SymbolActivationUnknownText(string symbol)
        => AobMakerStatus.Say(KeySymbolActivationUnknown,
            "Added CE symbol script '{0}'. This AOBMaker plugin does not say whether Cheat Engine enabled it: " +
            "check that the record is ticked (AOBMaker v20260930 or later reports it)", symbol);

    internal static string SymbolTimedOutText(string symbol)
        => AobMakerStatus.Say(KeySymbolTimedOut,
            "No answer from the AOBMaker plugin in time for '{0}': the script may still have been added. " +
            "Check Cheat Engine's address list before pressing SYM again", symbol);

    /// <summary>
    /// [AOBM-GNAMES-SYMBOL] Where a CE symbol script built from a <see cref="GeneratedAob"/> lands, replaying what the
    /// plugin's script does: <c>disp = [aob + pos]</c>, <c>target = aob + aoblen + disp</c>. The AOB starts
    /// <see cref="GeneratedAob.InjectionOffset"/> bytes before the seed instruction. Checked against the DLL's own
    /// answer before anything is pushed, because a signature that dereferences or adjusts resolves somewhere else.
    /// </summary>
    internal static ulong SymbolTarget(ulong seedAddr, int injectionOffset, int aobLen, int disp)
        => (ulong)((long)(seedAddr - (ulong)injectionOffset) + aobLen + disp);

    /// <summary>How many bytes of the scan hit <see cref="FindRipSeed"/> looks through. The DLL's signatures put their
    /// RIP instruction at most a few dozen bytes into the match.</summary>
    internal const int ScanHitWindowBytes = 64;

    /// <summary>The instruction to hand <c>GenerateAob</c> as its seed, and where its displacement sits.</summary>
    internal readonly record struct RipSeed(ulong InstructionAddr, ulong DispAddr);

    /// <summary>
    /// [AOBM-GNAMES-SYMBOL] Find the instruction in a scan hit whose RIP-relative operand lands exactly on
    /// <paramref name="expected"/>. The DLL reports where its PATTERN matched, and a pattern may carry a few instructions
    /// of context ahead of the one it resolves; <c>GenerateAob</c> decodes the instruction at the address it is given,
    /// so the match start is the wrong seed for those.
    /// <para>Null when no displacement lands there: the signature adjusts the target, dereferences it or follows a call,
    /// none of which a symbol script can replay. Only operands with no immediate after the displacement are
    /// recognised, which is what every GObjects and GNames signature resolves through.</para>
    /// <para>The instruction start is a guess (the opcode byte before the ModRM, a <c>0F</c> escape and a REX prefix
    /// before that). A wrong guess costs a refusal, never a wrong symbol: the caller re-derives the target from what
    /// AOBMaker.UI decoded and pushes only when it matches.</para>
    /// </summary>
    internal static RipSeed? FindRipSeed(ulong matchAddr, ReadOnlySpan<byte> window, ulong expected)
    {
        for (int d = 2; d + 4 <= window.Length; d++)
        {
            // mod=00 rm=101: [rip + disp32] in 64-bit code.
            if ((window[d - 1] & 0xC7) != 0x05) continue;
            var disp = BitConverter.ToInt32(window.Slice(d, 4));
            var dispAddr = matchAddr + (ulong)d;
            if ((ulong)((long)dispAddr + 4 + disp) != expected) continue;

            int start = d - 2;                                   // one-byte opcode
            if (start >= 1 && window[start - 1] == 0x0F) start--;  // two-byte opcode
            if (start >= 1 && window[start - 1] is >= 0x40 and <= 0x4F) start--;  // REX
            return new RipSeed(matchAddr + (ulong)start, dispAddr);
        }
        return null;
    }

    internal const string KeyAaPushed = "str.AobMaker.Aa.Pushed";
    internal const string KeyAaCopied = "str.AobMaker.Aa.Copied";
    internal const string KeyAaCopiedAfterRefusal = "str.AobMaker.Aa.CopiedAfterRefusal";

    /// <summary>
    /// [AOBM-INSTFINDER-AA] Deliver an AA-script record: pushed into CE's address list when the plugin is reachable,
    /// otherwise as paste-able record XML on the clipboard. It is the two-channel delivery Live Walker's AA button has.
    /// <para><paramref name="recordXml"/> is the whole <c>&lt;CheatTable&gt;</c> record, because the clipboard needs
    /// it; the push sends only the script inside it, which is what <c>CreateAAScript</c> takes. The record lands
    /// DISABLED either way: ticking it is the user's call, as on the clipboard path.</para>
    /// <para><c>IsError</c> is true only when neither channel took it, and then the text is
    /// <see cref="ClipboardDelivery.FailureText"/>: saying "copied" over a clipboard that refused would have the user
    /// paste whatever was there before.</para>
    /// </summary>
    internal static async Task<(string Text, bool IsError)> PushAaScriptOrCopyAsync(AobMakerStatus status,
        IPlatformService? platform, string description, string recordXml, ILoggingService log)
    {
        var bridge = status.Bridge;
        bool tried = bridge != null && status.IsAvailable;
        if (tried)
        {
            var script = CeXmlExportService.ExtractAssemblerScript(recordXml);
            bool sent = false;
            if (!string.IsNullOrEmpty(script))
            {
                try { sent = await bridge!.CreateAAScriptAsync(description, script, autoActivate: false); }
                catch (Exception ex) { log.Error($"AOBMaker AA push failed for {description}", ex); }
            }
            status.Apply(bridge!.IsAvailable);
            if (sent)
                return (AobMakerStatus.Say(KeyAaPushed,
                    "AA script added to CE's address list: {0} — tick it to register the symbol", description), false);
        }

        if (!await ClipboardDelivery.TryAsync(platform, recordXml))
            return (ClipboardDelivery.FailureText("the CE AA script"), true);
        return (tried
            ? AobMakerStatus.Say(KeyAaCopiedAfterRefusal,
                "Cheat Engine did not take the AA script, so it was copied to the clipboard instead: {0}", description)
            : AobMakerStatus.Say(KeyAaCopied, "CE AA script copied: {0} — paste it into CE's address list", description),
            false);
    }

    // --- [AOBM-DISSECT-INJECT] Tools -> Add Auto Structure Dissect ---

    internal const string KeyDissectInjecting = "str.AobMaker.Dissect.Injecting";
    internal const string KeyDissectAdded = "str.AobMaker.Dissect.Added";
    internal const string KeyDissectFileFailed = "str.AobMaker.Dissect.FileFailed";
    internal const string KeyDissectRecordFailed = "str.AobMaker.Dissect.RecordFailed";
    internal const string KeyDissectUnavailable = "str.AobMaker.Dissect.Unavailable";
    internal const string KeyDissectFailed = "str.AobMaker.Dissect.Failed";

    internal static string DissectInjectingText()
        => AobMakerStatus.Say(KeyDissectInjecting, "Adding {0} to the current CE table…", DissectLuaResource.DefaultFileName);

    internal static string DissectAddedText()
        => AobMakerStatus.Say(KeyDissectAdded,
            "Auto Structure Dissect added to the current CE table: tick '{0}' to turn it on. While ticked it stands " +
            "in for CE's own Unreal Engine → Use when dissecting structures, and puts it back when unticked",
            DissectScriptGenerator.RecordDescription);

    /// <param name="pluginError">The plugin's own reason, or null when the failure was a connect or timeout here.</param>
    internal static string DissectFileFailedText(string? pluginError)
        => AobMakerStatus.Say(KeyDissectFileFailed, "Could not embed {0} in the CE table: {1}",
            DissectLuaResource.DefaultFileName, pluginError ?? "no reply from the plugin (CE closed?)");

    /// <summary>The file went in and the record did not. Says so, because the embedded file alone does nothing and
    /// running the action again replaces it rather than adding a second copy.</summary>
    internal static string DissectRecordFailedText()
        => AobMakerStatus.Say(KeyDissectRecordFailed,
            "{0} is embedded, but its enable record did not reach CE (CE closed?): run this again",
            DissectLuaResource.DefaultFileName);

    internal static string DissectUnavailableText(IAobMakerBridge? bridge)
        => AobMakerStatus.Say(KeyDissectUnavailable, "Auto Structure Dissect needs the AOBMaker CE plugin: {0}",
            AobMakerUnavailable.Text(bridge));

    internal static string DissectFailedText(string reason)
        => AobMakerStatus.Say(KeyDissectFailed, "Adding Auto Structure Dissect failed: {0}", reason);
}
