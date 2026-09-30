using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [AOBMAKER-EVAL-2026-09-29] The shared AOBMaker row actions (<see cref="AobMakerActions"/>): what reaches the bridge,
/// what the status line says, and the arithmetic behind the GObjects / GNames symbol ([AOBM-GNAMES-SYMBOL]).
/// Headless, so every text here is the literal fallback; <see cref="AobMakerWordingTests"/> pins en.axaml to it.
/// </summary>
public class AobMakerActionsTests
{
    private static readonly MockLoggingService Log = new();

    // ---------------------------------------------------------------------------------------------------------
    // FindRipSeed: the scan hit is where the PATTERN matched, and GenerateAob decodes the instruction it is given.
    // ---------------------------------------------------------------------------------------------------------

    private const ulong Match = 0x7FF6_1000_0000;

    /// <summary>A window with <paramref name="code"/> at <paramref name="at"/> and a displacement written so the
    /// operand lands on <paramref name="target"/>; the rest is int3 filler.</summary>
    private static byte[] Window(int at, byte[] opcode, ulong target, int size = AobMakerActions.ScanHitWindowBytes)
    {
        var w = Enumerable.Repeat((byte)0xCC, size).ToArray();
        opcode.CopyTo(w, at);
        int dispAt = at + opcode.Length;
        long disp = (long)target - (long)(Match + (ulong)dispAt + 4);
        BitConverter.GetBytes((int)disp).CopyTo(w, dispAt);
        return w;
    }

    [Fact]
    public void Seed_is_the_match_start_when_the_pattern_starts_with_the_RIP_instruction()
    {
        // lea rcx, [rip+X] -- GOBJ_V1's shape (instrOffset 0).
        const ulong target = 0x7FF6_1234_5670;
        var seed = AobMakerActions.FindRipSeed(Match, Window(0, [0x48, 0x8D, 0x0D], target), target);

        Assert.NotNull(seed);
        Assert.Equal(Match, seed!.Value.InstructionAddr);
        Assert.Equal(Match + 3, seed.Value.DispAddr);
    }

    [Fact]
    public void Seed_moves_past_leading_context_to_the_instruction_that_resolves()
    {
        // GOBJ_GH_1's shape: 12 bytes of context, then mov rax, [rip+X]. The match start decodes as something else
        // entirely, so handing it to GenerateAob would fail or, worse, describe a different instruction.
        const ulong target = 0x7FF6_0FF0_0000;   // BEFORE the code: a negative displacement
        var w = Window(12, [0x48, 0x8B, 0x05], target);
        new byte[] { 0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83, 0xEC, 0x20, 0x8B, 0xF9 }.CopyTo(w, 0);

        var seed = AobMakerActions.FindRipSeed(Match, w, target);

        Assert.Equal(Match + 12, seed!.Value.InstructionAddr);
        Assert.Equal(Match + 15, seed.Value.DispAddr);
    }

    [Fact]
    public void Seed_without_REX_starts_at_the_opcode()
    {
        // GOBJ_SAT426_2's shape: mov eax, [rip+X] (opcodeLen 2) after a ret -- 0xC3 is not a REX prefix.
        const ulong target = 0x7FF6_1100_0000;
        var w = Window(1, [0x8B, 0x05], target);
        w[0] = 0xC3;

        Assert.Equal(Match + 1, AobMakerActions.FindRipSeed(Match, w, target)!.Value.InstructionAddr);
    }

    [Fact]
    public void Seed_takes_the_0F_escape_and_the_REX_before_it()
    {
        // movzx eax, byte [rip+X] = 0F B6 05; with REX.W in front, 48 0F B6 05.
        const ulong target = 0x7FF6_1100_0040;
        var plain = Window(2, [0x0F, 0xB6, 0x05], target);
        plain[0] = 0x74; plain[1] = 0x07;   // jz +7
        var rex = Window(2, [0x48, 0x0F, 0xB6, 0x05], target);
        rex[0] = 0x74; rex[1] = 0x07;

        Assert.Equal(Match + 2, AobMakerActions.FindRipSeed(Match, plain, target)!.Value.InstructionAddr);
        Assert.Equal(Match + 2, AobMakerActions.FindRipSeed(Match, rex, target)!.Value.InstructionAddr);
    }

    [Fact]
    public void An_adjusted_signature_has_no_seed()
    {
        // GOBJ_AV1's shape: the operand lands on ObjObjects and the DLL subtracts 0x10 for the FUObjectArray base.
        // A symbol script registers the raw RIP target, so it could only ever register the wrong address.
        const ulong ripTarget = 0x7FF6_1234_5680;
        var w = Window(0, [0x48, 0x8D, 0x0D], ripTarget);

        Assert.Null(AobMakerActions.FindRipSeed(Match, w, ripTarget - 0x10));
    }

    [Fact]
    public void No_RIP_operand_and_a_short_window_both_answer_null_without_throwing()
    {
        var noRip = Enumerable.Repeat((byte)0x90, AobMakerActions.ScanHitWindowBytes).ToArray();
        Assert.Null(AobMakerActions.FindRipSeed(Match, noRip, 0x7FF6_0000_1000));
        Assert.Null(AobMakerActions.FindRipSeed(Match, [0x48, 0x8D, 0x0D], 0x7FF6_0000_1000));
        Assert.Null(AobMakerActions.FindRipSeed(Match, [], 0x7FF6_0000_1000));
    }

    [Fact]
    public void A_ModRM_that_is_not_RIP_relative_is_not_a_candidate()
    {
        // mod=01 rm=101 is [rbp+disp8], not [rip+disp32]: the same byte pattern must not be read as a displacement
        // even when the following bytes happen to land on the target.
        const ulong target = 0x7FF6_1234_5670;
        var w = Window(0, [0x48, 0x8B, 0x45], target);   // 0x45 = mod 01, reg 000, rm 101

        Assert.Null(AobMakerActions.FindRipSeed(Match, w, target));
    }

    [Fact]
    public void The_first_instruction_that_lands_wins()
    {
        const ulong target = 0x7FF6_1234_5670;
        var w = Window(0, [0x48, 0x8D, 0x0D], target);
        var second = Window(10, [0x48, 0x8B, 0x05], target);
        Array.Copy(second, 10, w, 10, 7);

        Assert.Equal(Match, AobMakerActions.FindRipSeed(Match, w, target)!.Value.InstructionAddr);
    }

    // ---------------------------------------------------------------------------------------------------------
    // SymbolTarget: the replay CE's symbol script performs, checked before anything is pushed.
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void SymbolTarget_replays_aob_plus_aoblen_plus_disp()
    {
        // The AOB starts 5 bytes before the seed; the instruction ends 12 bytes into it.
        Assert.Equal(0x1_4000_1000UL - 5 + 12 + 0x100,
                     AobMakerActions.SymbolTarget(0x1_4000_1000, injectionOffset: 5, aobLen: 12, disp: 0x100));
        Assert.Equal(0x1_4000_1000UL + 7 - 0x2000,
                     AobMakerActions.SymbolTarget(0x1_4000_1000, injectionOffset: 0, aobLen: 7, disp: -0x2000));
    }

    [Fact]
    public void FindRipSeed_and_SymbolTarget_agree_on_the_instruction_they_describe()
    {
        // What GenerateAob would answer for the seed, for any amount of context it prepends: pos/aoblen are relative
        // to the AOB start, which lies injectionOffset bytes before the seed.
        const ulong target = 0x7FF6_2000_0000;
        var w = Window(12, [0x48, 0x8B, 0x05], target);
        var seed = AobMakerActions.FindRipSeed(Match, w, target)!.Value;
        int disp = BitConverter.ToInt32(w, 15);

        foreach (int injectionOffset in new[] { 0, 4, 11 })
        {
            int pos = injectionOffset + 3, aobLen = injectionOffset + 7;
            Assert.Equal(seed.DispAddr, seed.InstructionAddr - (ulong)injectionOffset + (ulong)pos);
            Assert.Equal(target, AobMakerActions.SymbolTarget(seed.InstructionAddr, injectionOffset, aobLen, disp));
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Row helpers
    // ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Inventory[3]", true)]
    [InlineData("Inventory[3].Count", true)]
    [InlineData("Health", false)]
    [InlineData("", false)]
    public void Snapshot_element_rows_are_the_ones_with_a_subscript(string name, bool element)
        => Assert.Equal(element, AobMakerActions.IsSnapshotElementRow(name));

    [Theory]
    [InlineData("0x1000", 0x10, "0x1010")]
    [InlineData("0X1000", 0x10, "0x1010")]
    [InlineData("1000", 8, "0x1008")]
    [InlineData("", 4, "")]
    [InlineData("zz", 4, "")]
    public void OffsetAddress_adds_in_hex_and_refuses_what_does_not_parse(string obj, int off, string expected)
        => Assert.Equal(expected, AobMakerActions.OffsetAddress(obj, off));

    // ---------------------------------------------------------------------------------------------------------
    // HEX / ASM / +CE
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Hex_sends_the_bare_address_and_says_where_CE_went()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var status = new AobMakerStatus(bridge);

        var text = await AobMakerActions.HexAsync(status, "0x7FF600001000", "Pawn.Health", Log);

        Assert.Equal("7FF600001000", bridge.LastHex);
        Assert.Equal("CE hex view: Pawn.Health @ 0x7FF600001000", text);
        Assert.True(status.IsAvailable);
    }

    [Fact]
    public async Task Asm_goes_to_the_disassembler_not_the_hex_view()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };

        var text = await AobMakerActions.AsmAsync(new AobMakerStatus(bridge), "0x7FF600002000", "scan hit", Log);

        Assert.Equal("7FF600002000", bridge.LastAsm);
        Assert.Null(bridge.LastHex);
        Assert.StartsWith("CE disassembler:", text);
    }

    [Fact]
    public async Task A_refusal_and_a_lost_pipe_read_differently_and_the_status_learns_the_pipe_is_gone()
    {
        var refusing = new ScriptedAobMakerBridge { Available = true, NavigateResult = false };
        Assert.StartsWith("Cheat Engine did not move its view",
            await AobMakerActions.HexAsync(new AobMakerStatus(refusing), "0x10", "x", Log));

        var gone = new ScriptedAobMakerBridge { Available = true, NavigateResult = false, AvailableAfterCall = false };
        var status = new AobMakerStatus(gone);
        status.Apply(true);
        var text = await AobMakerActions.HexAsync(status, "0x10", "x", Log);

        Assert.Equal(AobMakerUnavailable.Text(gone), text);
        Assert.False(status.IsAvailable);
    }

    [Fact]
    public async Task No_bridge_or_no_address_is_a_silent_no_op()
    {
        Assert.Equal("", await AobMakerActions.HexAsync(new AobMakerStatus(null), "0x10", "x", Log));
        var bridge = new ScriptedAobMakerBridge { Available = true };
        Assert.Equal("", await AobMakerActions.AsmAsync(new AobMakerStatus(bridge), "", "x", Log));
        Assert.Null(bridge.LastAsm);
    }

    [Fact]
    public async Task AddRecord_pushes_the_typed_record_under_the_row_name()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var type = CeXmlExportService.MapTypeNameToCeRecordType("FloatProperty");

        var text = await AobMakerActions.AddRecordAsync(new AobMakerStatus(bridge), "Pawn::Health", "0x7FF600003000",
                                                         type, Log);

        var rec = Assert.Single(bridge.Records);
        Assert.EndsWith("Pawn::Health", rec.Description);
        Assert.Equal("7FF600003000", rec.Address);
        Assert.Equal(4, rec.ValueType);   // vtSingle
        Assert.Equal("Added to CE: Pawn::Health", text);
    }

    [Fact]
    public async Task A_refused_record_names_the_wrong_process_when_CE_has_one_open()
    {
        var bridge = new ScriptedAobMakerBridge
        {
            Available = true, RecordResult = false, Attached = new CeAttachedProcess(999, "Other.exe"),
        };

        var text = await AobMakerActions.AddRecordAsync(new AobMakerStatus(bridge), "Pawn::Health", "0x10",
            CeXmlExportService.MapTypeNameToCeRecordType("IntProperty"), Log, gamePid: 4242, gameModule: "Game.exe");

        Assert.StartsWith("Cheat Engine refused the record for Pawn::Health — ", text);
        Assert.Contains("Other.exe (pid 999)", text);
        Assert.Contains("Game.exe (pid 4242)", text);
    }

    [Fact]
    public async Task A_refused_record_with_the_right_process_open_says_only_that_it_was_refused()
    {
        var bridge = new ScriptedAobMakerBridge
        {
            Available = true, RecordResult = false, Attached = new CeAttachedProcess(4242, "Game.exe"),
        };

        var text = await AobMakerActions.AddRecordAsync(new AobMakerStatus(bridge), "Pawn::Health", "0x10",
            CeXmlExportService.MapTypeNameToCeRecordType("IntProperty"), Log, gamePid: 4242, gameModule: "Game.exe");

        Assert.Equal("Cheat Engine refused the record for Pawn::Health", text);
    }

    [Fact]
    public void An_enum_of_unknown_width_is_pushed_as_one_byte()
    {
        // Search and snapshot rows never know an enum's width. Four bytes of a uint8 enum would read three neighbours.
        Assert.Equal(0, CeXmlExportService.MapTypeNameToCeRecordType("EnumProperty").ValueType);
        Assert.Equal(2, CeXmlExportService.MapTypeNameToCeRecordType("EnumProperty", 4).ValueType);
        Assert.Equal(4, CeXmlExportService.MapTypeNameToCeRecordType("FloatProperty").ValueType);
    }

    // ---------------------------------------------------------------------------------------------------------
    // [AOBM-FUNC-DISASM] Disassemble in CE
    // ---------------------------------------------------------------------------------------------------------

    private sealed class CodeAddrDump(string codeAddr) : StubDumpService
    {
        public override Task<string> GetFunctionCodeAddrAsync(string funcAddr, CancellationToken ct = default)
            => Task.FromResult(codeAddr);
    }

    [Fact]
    public async Task Disassemble_adds_a_code_record_and_opens_the_disassembler_there()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };

        var text = await AobMakerActions.DisassembleFunctionAsync(bridge, new CodeAddrDump("0x7FF601234560"),
                                                                  "0x2000", "Pawn:Jump", Log);

        var rec = Assert.Single(bridge.Records);
        Assert.Equal("Pawn:Jump (code)", rec.Description);
        Assert.Equal("7FF601234560", rec.Address);
        Assert.Equal(8, rec.ValueType);   // ByteArray: a thing to right-click, not a value
        Assert.Equal("7FF601234560", bridge.LastAsm);
        Assert.Equal("Pawn:Jump: CE disassembler @ 0x7FF601234560", text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0x0")]
    public async Task Disassemble_without_a_native_entry_pushes_nothing(string codeAddr)
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };

        var text = await AobMakerActions.DisassembleFunctionAsync(bridge, new CodeAddrDump(codeAddr), "0x2000",
                                                                  "BP_Door:Open", Log);

        Assert.Empty(bridge.Records);
        Assert.Null(bridge.LastAsm);
        Assert.StartsWith("No native code address for BP_Door:Open", text);
    }

    [Fact]
    public async Task Disassemble_says_which_half_failed()
    {
        var recordOnly = new ScriptedAobMakerBridge { Available = true, NavigateResult = false };
        Assert.StartsWith("Added Pawn:Jump to the CE table",
            await AobMakerActions.DisassembleFunctionAsync(recordOnly, new CodeAddrDump("0x10"), "0x2", "Pawn:Jump", Log));

        var refused = new ScriptedAobMakerBridge { Available = true, RecordResult = false, NavigateResult = false };
        Assert.Equal("Cheat Engine refused the push for Pawn:Jump: nothing was added",
            await AobMakerActions.DisassembleFunctionAsync(refused, new CodeAddrDump("0x10"), "0x2", "Pawn:Jump", Log));
    }

    // ---------------------------------------------------------------------------------------------------------
    // [AOBM-INSTFINDER-AA] AA script: pushed when the plugin is up, copied otherwise
    // ---------------------------------------------------------------------------------------------------------

    private const string RecordXml =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<CheatTable>\n<CheatEntries>\n<CheatEntry>\n" +
        "<Description>\"gworld_addr\"</Description>\n<VariableType>Auto Assembler Script</VariableType>\n" +
        "<AssemblerScript>[ENABLE]\nregistersymbol(gworld_addr)\n[DISABLE]\nunregistersymbol(gworld_addr)\n" +
        "</AssemblerScript>\n</CheatEntry>\n</CheatEntries>\n</CheatTable>\n";

    [Fact]
    public async Task AaScript_goes_to_CE_disabled_when_the_plugin_is_up()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var status = new AobMakerStatus(bridge);
        status.Apply(true);
        var platform = new MockPlatformService(Path.GetTempPath());

        var (text, isError) = await AobMakerActions.PushAaScriptOrCopyAsync(status, platform, "\"gworld_addr\"",
                                                                          RecordXml, Log);

        var push = Assert.Single(bridge.AaScripts);
        Assert.Equal("\"gworld_addr\"", push.Description);
        Assert.StartsWith("[ENABLE]", push.Script);
        Assert.DoesNotContain("<AssemblerScript>", push.Script);
        Assert.False(push.AutoActivate);
        Assert.Null(platform.LastClipboard);
        Assert.False(isError);
        Assert.StartsWith("AA script added to CE's address list", text);
    }

    [Fact]
    public async Task AaScript_is_copied_when_the_plugin_is_down_and_after_a_refusal()
    {
        var platform = new MockPlatformService(Path.GetTempPath());
        var down = new AobMakerStatus(new ScriptedAobMakerBridge { Available = false });
        var (text, isError) = await AobMakerActions.PushAaScriptOrCopyAsync(down, platform, "d", RecordXml, Log);

        Assert.Equal(RecordXml, platform.LastClipboard);
        Assert.False(isError);
        Assert.StartsWith("CE AA script copied", text);

        var refusing = new ScriptedAobMakerBridge { Available = true, AaResult = false };
        var up = new AobMakerStatus(refusing);
        up.Apply(true);
        (text, isError) = await AobMakerActions.PushAaScriptOrCopyAsync(up, platform, "d", RecordXml, Log);

        Assert.Single(refusing.AaScripts);
        Assert.False(isError);
        Assert.StartsWith("Cheat Engine did not take the AA script", text);
    }

    [Fact]
    public async Task AaScript_that_reaches_neither_channel_is_an_error_that_says_do_not_paste()
    {
        var (text, isError) = await AobMakerActions.PushAaScriptOrCopyAsync(
            new AobMakerStatus(null), new RefusingClipboard(), "d", RecordXml, Log);

        Assert.True(isError);
        Assert.Equal(ClipboardDelivery.FailureText("the CE AA script"), text);
    }

    private sealed class RefusingClipboard : IPlatformService
    {
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => Path.GetTempPath();
        public string GetLogDirectoryPath() => Path.GetTempPath();
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(false);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "test";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string defaultFileName, string filterName, string filterExtension)
            => Task.FromResult<string?>(null);
    }

    // ---------------------------------------------------------------------------------------------------------
    // [AOBM-GNAMES-SYMBOL] Failure wording: each failure asks the user for something different
    // ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(GenerateAobFailure.NotRunning, "AOBMaker.UI is not running")]
    [InlineData(GenerateAobFailure.Refused, "same elevation")]
    [InlineData(GenerateAobFailure.NoReply, "older build")]
    [InlineData(GenerateAobFailure.Failed, "could not make a unique AOB for 'gobjects_addr': too many matches")]
    public void GenerateAob_failures_each_name_their_remedy(GenerateAobFailure failure, string expected)
    {
        var text = AobMakerActions.GenerateAobFailureText(
            new GenerateAobResult(null, failure, "too many matches"), "gobjects_addr");
        Assert.Contains(expected, text);
    }
}

/// <summary>
/// A bridge whose every answer is set by the test and whose every push is recorded. Shared by the
/// [AOBMAKER-EVAL-2026-09-29] tests; older test files keep their own private doubles.
/// </summary>
internal sealed class ScriptedAobMakerBridge : IAobMakerBridge
{
    public bool Available { get; set; }
    /// <summary>What <see cref="IsAvailable"/> reads after a push; null leaves <see cref="Available"/> alone.</summary>
    public bool? AvailableAfterCall { get; set; }
    public bool NavigateResult { get; set; } = true;
    public bool RecordResult { get; set; } = true;
    public bool AaResult { get; set; } = true;
    public bool SymbolResult { get; set; } = true;
    public (bool Ok, string? Error) InjectResult { get; set; } = (true, null);
    public CeAttachedProcess? Attached { get; set; }

    public string? LastHex { get; private set; }
    public string? LastAsm { get; private set; }
    public int CheckCalls { get; private set; }
    public List<(string Description, string Address, int ValueType, bool IsSigned, bool ShowAsHex)> Records { get; } = new();
    public List<(string Description, string Script, bool AutoActivate, string? Group)> AaScripts { get; } = new();
    public List<(string Name, string Aob, int Pos, int AobLen, string Symbol, string Module, bool AutoActivate)> Symbols { get; } = new();
    public List<(string FileName, string Content)> TableFiles { get; } = new();

    public bool IsAvailable => Available;

    private T After<T>(T result)
    {
        if (AvailableAfterCall is bool a) Available = a;
        return result;
    }

    public Task<bool> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        CheckCalls++;
        return Task.FromResult(Available);
    }

    public Task<bool> NavigateHexViewAsync(string hexAddress, CancellationToken ct = default)
    {
        LastHex = hexAddress;
        return Task.FromResult(After(NavigateResult));
    }

    public Task<bool> NavigateDisassemblerAsync(string hexAddress, CancellationToken ct = default)
    {
        LastAsm = hexAddress;
        return Task.FromResult(After(NavigateResult));
    }

    public Task<bool> CreateAAScriptAsync(string description, string script, bool autoActivate = true,
                                          string? group = null, CancellationToken ct = default)
    {
        AaScripts.Add((description, script, autoActivate, group));
        return Task.FromResult(After(AaResult));
    }

    public Task<bool> CreateSymbolScriptAsync(string name, string aob, int pos, int aoblen, string symbol,
                                              string module, bool autoActivate = true, CancellationToken ct = default)
    {
        Symbols.Add((name, aob, pos, aoblen, symbol, module, autoActivate));
        return Task.FromResult(After(SymbolResult));
    }

    /// <summary>[AOBM-ACTIVATE-RESULT] What the detailed call answers; null answers as a current plugin would for
    /// <see cref="SymbolResult"/>: created, active and registered, or not created.</summary>
    public SymbolScriptResult? SymbolDetail { get; set; }

    public Task<SymbolScriptResult> CreateSymbolScriptDetailedAsync(string name, string aob, int pos, int aoblen,
        string symbol, string module, bool autoActivate = true, CancellationToken ct = default)
    {
        Symbols.Add((name, aob, pos, aoblen, symbol, module, autoActivate));
        return Task.FromResult(After(SymbolDetail
            ?? new SymbolScriptResult(SymbolResult, SymbolResult ? true : null, SymbolResult ? true : null, null)));
    }

    public Task<bool> CreateMemoryRecordAsync(string description, string address, int valueType, bool isSigned = false,
                                              bool showAsHex = false, CancellationToken ct = default)
    {
        Records.Add((description, address, valueType, isSigned, showAsHex));
        return Task.FromResult(After(RecordResult));
    }

    public Task<(bool Ok, string? ErrorMessage)> InjectTableFileAsync(string fileName, string content,
                                                                      CancellationToken ct = default)
    {
        TableFiles.Add((fileName, content));
        return Task.FromResult(After(InjectResult));
    }

    public Task<CeAttachedProcess?> GetAttachedProcessAsync(CancellationToken ct = default)
        => Task.FromResult(Attached);
}
