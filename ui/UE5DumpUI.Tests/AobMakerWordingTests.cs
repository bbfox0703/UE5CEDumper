using System.Net;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [AOBMAKER-EVAL-2026-09-29] Every status sentence the AOBMaker actions build has TWO copies: the en.axaml value the
/// app shows, and the literal fallback <see cref="AobMakerStatus.Say"/> keeps for when <c>Res</c> cannot resolve it
/// (always, headless). The VM tests read the fallback, so if the two drifted they would pin wording no user sees.
/// This formats each en.axaml value with the same arguments and demands the same sentence.
/// </summary>
public class AobMakerWordingTests
{
    private static readonly MockLoggingService Log = new();

    [Fact]
    public async Task Every_en_axaml_value_is_the_sentence_the_fallback_builds()
    {
        var axaml = ReadEnAxaml();
        var mismatches = new List<string>();
        int checkedCount = 0;

        void Same(string key, string headless, params object[] args)
        {
            checkedCount++;
            var open = $"x:Key=\"{key}\">";
            int start = axaml.IndexOf(open, StringComparison.Ordinal);
            if (start < 0) { mismatches.Add($"{key}: missing from en.axaml"); return; }
            start += open.Length;
            int end = axaml.IndexOf("</sys:String>", start, StringComparison.Ordinal);
            var shown = string.Format(WebUtility.HtmlDecode(axaml[start..end]), args);
            if (shown != headless) mismatches.Add($"{key}:\n  en.axaml: {shown}\n  fallback: {headless}");
        }

        // Row actions
        var ok = new AobMakerStatus(new ScriptedAobMakerBridge { Available = true });
        Same(AobMakerActions.KeyHexDone, await AobMakerActions.HexAsync(ok, "0x10", "L", Log), "L", "0x10");
        Same(AobMakerActions.KeyAsmDone, await AobMakerActions.AsmAsync(ok, "0x10", "L", Log), "L", "0x10");
        var refuses = new AobMakerStatus(new ScriptedAobMakerBridge
            { Available = true, NavigateResult = false, RecordResult = false });
        Same(AobMakerActions.KeyNavRefused, await AobMakerActions.HexAsync(refuses, "0x10", "L", Log), "L", "0x10");
        var type = CeXmlExportService.MapTypeNameToCeRecordType("IntProperty");
        Same(AobMakerActions.KeyRecordDone, await AobMakerActions.AddRecordAsync(ok, "N", "0x10", type, Log), "N");
        Same(AobMakerActions.KeyRecordRefused, await AobMakerActions.AddRecordAsync(refuses, "N", "0x10", type, Log), "N");
        Same(AobMakerActions.KeyNoOwnAddress, AobMakerActions.NoOwnAddressText("A[3]"), "A[3]");

        // Attach check
        Same(AobMakerStatus.KeyAttachNone,
             AobMakerStatus.DescribeAttach(new CeAttachedProcess(0, ""), 1, "G.exe"), "G.exe");
        Same(AobMakerStatus.KeyAttachOther,
             AobMakerStatus.DescribeAttach(new CeAttachedProcess(2, "O.exe"), 1, "G.exe"), "O.exe", 2, "G.exe", 1);

        // Disassemble in CE
        var dump = new CodeAddrDump("0x20");
        Same(AobMakerActions.KeyDisasmNoCode,
             await AobMakerActions.DisassembleFunctionAsync(ok.Bridge!, new CodeAddrDump(""), "0x1", "F", Log), "F");
        Same(AobMakerActions.KeyDisasmDone,
             await AobMakerActions.DisassembleFunctionAsync(ok.Bridge!, dump, "0x1", "F", Log), "F", "0x20");
        Same(AobMakerActions.KeyDisasmRecordOnly, await AobMakerActions.DisassembleFunctionAsync(
             new ScriptedAobMakerBridge { Available = true, NavigateResult = false }, dump, "0x1", "F", Log), "F", "0x20");
        Same(AobMakerActions.KeyDisasmRefused,
             await AobMakerActions.DisassembleFunctionAsync(refuses.Bridge!, dump, "0x1", "F", Log), "F");

        // GenerateAob
        Same(AobMakerActions.KeyGenAobPending, AobMakerActions.GenerateAobPendingText("s"), "s");
        Same(AobMakerActions.KeyGenAobNotRunning, AobMakerActions.GenerateAobFailureText(
             new GenerateAobResult(null, GenerateAobFailure.NotRunning, null), "s"), "s");
        Same(AobMakerActions.KeyGenAobBusy, AobMakerActions.GenerateAobFailureText(
             new GenerateAobResult(null, GenerateAobFailure.Busy, null), "s"), "s");
        Same(AobMakerActions.KeyGenAobRefused, AobMakerActions.GenerateAobFailureText(
             new GenerateAobResult(null, GenerateAobFailure.Refused, null), "s"));
        Same(AobMakerActions.KeyGenAobNoReply, AobMakerActions.GenerateAobFailureText(
             new GenerateAobResult(null, GenerateAobFailure.NoReply, null), "s"));
        Same(AobMakerActions.KeyGenAobFailed, AobMakerActions.GenerateAobFailureText(
             new GenerateAobResult(null, GenerateAobFailure.Failed, "why"), "s"), "s", "why");
        Same(AobMakerActions.KeyGenAobNotRip, AobMakerActions.GenerateAobNotRipText("s", 0xAB), "s", 0xABUL);
        Same(AobMakerActions.KeyGenAobNoDirectRip, AobMakerActions.GenerateAobNoDirectRipText("s"), "s");
        Same(AobMakerActions.KeyGenAobUnreadable, AobMakerActions.GenerateAobUnreadableText("s", 0xAB), "s", 0xABUL);
        Same(AobMakerActions.KeyGenAobMismatch, AobMakerActions.GenerateAobMismatchText("s", 0xAB, "0xCD"),
             "s", 0xABUL, "0xCD");

        // AA script delivery
        var platform = new MockPlatformService(Path.GetTempPath());
        var up = new AobMakerStatus(new ScriptedAobMakerBridge { Available = true });
        up.Apply(true);
        Same(AobMakerActions.KeyAaPushed,
             (await AobMakerActions.PushAaScriptOrCopyAsync(up, platform, "D", RecordXml, Log)).Text, "D");
        Same(AobMakerActions.KeyAaCopied,
             (await AobMakerActions.PushAaScriptOrCopyAsync(new AobMakerStatus(null), platform, "D", RecordXml, Log)).Text,
             "D");
        var refusingUp = new AobMakerStatus(new ScriptedAobMakerBridge { Available = true, AaResult = false });
        refusingUp.Apply(true);
        Same(AobMakerActions.KeyAaCopiedAfterRefusal,
             (await AobMakerActions.PushAaScriptOrCopyAsync(refusingUp, platform, "D", RecordXml, Log)).Text, "D");

        // Auto Structure Dissect
        Same(AobMakerActions.KeyDissectInjecting, AobMakerActions.DissectInjectingText(),
             DissectLuaResource.DefaultFileName);
        Same(AobMakerActions.KeyDissectAdded, AobMakerActions.DissectAddedText(), DissectScriptGenerator.RecordDescription);
        Same(AobMakerActions.KeyDissectFileFailed, AobMakerActions.DissectFileFailedText("e"),
             DissectLuaResource.DefaultFileName, "e");
        Same(AobMakerActions.KeyDissectRecordFailed, AobMakerActions.DissectRecordFailedText(),
             DissectLuaResource.DefaultFileName);
        Same(AobMakerActions.KeyDissectUnavailable, AobMakerActions.DissectUnavailableText(null),
             AobMakerUnavailable.Text(null));
        Same(AobMakerActions.KeyDissectFailed, AobMakerActions.DissectFailedText("r"), "r");

        // [AOBM-UI-INDICATOR] The toolbar dots' tooltips
        var dllUp = new AobMakerStatus(new ScriptedAobMakerBridge { Available = true });
        dllUp.Apply(true);
        Same(AobMakerStatus.KeyDllOn, dllUp.DllTip);
        var uiUp = new AobMakerUiStatus(new ListedUiClient { Listed = true });
        await uiUp.PollAsync(TestContext.Current.CancellationToken);
        Same(AobMakerUiStatus.KeyTipOn, uiUp.Tip);
        Same(AobMakerUiStatus.KeyTipOff, new AobMakerUiStatus(null).Tip);

        // [AOBM-TRAINER-SETUP-MODAL] The standalone trainer's push
        Same(UE5DumpUI.ViewModels.TeleportViewModel.KeyTrainerPushed,
             UE5DumpUI.ViewModels.TeleportViewModel.TrainerPushedText(12), 12);

        // [AOBM-SYSTAB-ASM-SILENT] The System tab's ASM label
        Same(UE5DumpUI.ViewModels.PointerPanelViewModel.KeyScanHitLabel,
             UE5DumpUI.ViewModels.PointerPanelViewModel.ScanHitLabel("P"), "P");

        // [AOBM-ACTIVATE-RESULT] SYM beyond "created"
        Same(AobMakerActions.KeySymbolNotActivated, AobMakerActions.SymbolNotActivatedText("s", "r"), "s", "r");
        Same(AobMakerActions.KeySymbolNotRegistered, AobMakerActions.SymbolNotRegisteredText("s", "r"), "s", "r");
        Same(AobMakerActions.KeySymbolActivationUnknown, AobMakerActions.SymbolActivationUnknownText("s"), "s");
        Same(AobMakerActions.KeySymbolTimedOut, AobMakerActions.SymbolTimedOutText("s"), "s");
        Same(AobMakerActions.KeySymbolNoReason, AobMakerActions.SymbolNoReasonText());

        // [AOBM-PLUSCE-FIDELITY] +CE in the CreateMemoryRecord form when the plugin cannot build the record as it is
        Same(AobMakerActions.KeyRecordAsByte, AobMakerActions.AsByteText());
        Same(AobMakerActions.KeyRecordAsPointer, AobMakerActions.AsPointerText());
        Same(AobMakerActions.KeyRecordsDegraded, AobMakerActions.DegradedCountText(2), 2);

        // [AOBM-LIVEWALKER-HEX-SILENT] Live Walker's pointer-target HEX label
        Same(UE5DumpUI.ViewModels.LiveWalkerViewModel.KeyPtrTargetLabel,
             UE5DumpUI.ViewModels.LiveWalkerViewModel.PtrTargetLabel("P"), "P");

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
        Assert.Equal(45, checkedCount);   // guard the guard: a skipped block must not pass silently
    }

    private const string RecordXml =
        "<CheatTable><CheatEntries><CheatEntry><Description>D</Description>" +
        "<AssemblerScript>[ENABLE]\n[DISABLE]\n</AssemblerScript></CheatEntry></CheatEntries></CheatTable>";

    private sealed class CodeAddrDump(string codeAddr) : StubDumpService
    {
        public override Task<string> GetFunctionCodeAddrAsync(string funcAddr, CancellationToken ct = default)
            => Task.FromResult(codeAddr);
    }

    private static string ReadEnAxaml()
        => File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Resources/Strings/en.axaml"));
}
