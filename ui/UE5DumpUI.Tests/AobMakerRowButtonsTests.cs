using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [AOBMAKER-EVAL-2026-09-29] The HEX / +CE / ASM / AA buttons the evaluation added to panels that only copied before:
/// what each sends to the plugin, and which rows must send nothing. The session-gated panels (Snapshot, SPC) are the
/// ones that matter most: an address from an earlier launch, or an array element's owner base, would land a record
/// on bytes that are not the value.
/// </summary>
public class AobMakerRowButtonsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"UE5DumpAobRows_{Guid.NewGuid():N}");
    private readonly MockLoggingService _log = new();

    public AobMakerRowButtonsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static AobMakerStatus Up(ScriptedAobMakerBridge bridge)
    {
        var status = new AobMakerStatus(bridge);
        status.Apply(bridge.Available);
        return status;
    }

    private static EngineState Game => new()
    {
        PeHash = "PE", ProcessCreationTime = "T", UEVersion = 504, ModuleBase = "7FF600000000",
        ProcessId = 4242, ModuleName = "Game.exe",
    };

    // ---- [AOBM-INSTFINDER-AA] ----

    [Fact]
    public async Task InstanceFinder_AA_is_pushed_into_CE_when_the_plugin_is_up()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var platform = new MockPlatformService(_dir);
        var vm = new InstanceFinderViewModel(new StubDumpService(), _log, platform, Up(bridge));

        await vm.GenerateCeAAScriptCommand.ExecuteAsync(
            new InstanceResult { Address = "0x7FF600001000", ClassName = "BP_Player_C", Name = "BP_Player_C_0" });

        var push = Assert.Single(bridge.AaScripts);
        Assert.Equal("\"BP_Player_C\"", push.Description);
        Assert.Contains("BP_Player_C", push.Script);
        Assert.False(push.AutoActivate);
        Assert.Null(platform.LastClipboard);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task InstanceFinder_AA_falls_back_to_the_clipboard_when_the_plugin_is_down()
    {
        var bridge = new ScriptedAobMakerBridge { Available = false };
        var platform = new MockPlatformService(_dir);
        var vm = new InstanceFinderViewModel(new StubDumpService(), _log, platform, Up(bridge));

        await vm.GenerateCeAAScriptCommand.ExecuteAsync(
            new InstanceResult { Address = "0x7FF600001000", ClassName = "BP_Player_C", Name = "BP_Player_C_0" });

        Assert.Empty(bridge.AaScripts);
        Assert.Contains("<CheatTable>", platform.LastClipboard);
        Assert.StartsWith("CE AA script copied", vm.StatusText);
    }

    // ---- [AOBM-INSTFINDER-CE] ----

    [Fact]
    public async Task InstanceFinder_field_plus_CE_uses_the_payload_address_and_the_field_type()
    {
        // A delegate on a checked build: the field starts at FieldAddress, its payload 8 bytes later.
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var vm = new InstanceFinderViewModel(new StubDumpService(), _log, new MockPlatformService(_dir), Up(bridge));
        var field = new LiveFieldValue
        {
            Name = "OnDeath", TypeName = "MulticastInlineDelegateProperty", Offset = 0x3D4, DelegatePad = 8,
            FieldAddress = "0x7FF600001400",
        };

        await vm.AddFieldToCeCommand.ExecuteAsync(field);
        await vm.HexFieldAddressCommand.ExecuteAsync(field);

        Assert.Equal("7FF600001408", Assert.Single(bridge.Records).Address);
        Assert.Equal("7FF600001408", bridge.LastHex);
    }

    [Fact]
    public async Task InstanceFinder_instance_rows_get_HEX_only()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var vm = new InstanceFinderViewModel(new StubDumpService(), _log, new MockPlatformService(_dir), Up(bridge));

        await vm.HexInstanceAddressCommand.ExecuteAsync(new InstanceResult { Address = "0x7FF600001000", Name = "P_0" });

        Assert.Equal("7FF600001000", bridge.LastHex);
        Assert.Empty(bridge.Records);
        Assert.Equal("CE hex view: P_0 @ 0x7FF600001000", vm.StatusText);
    }

    // ---- [AOBM-VALUE-ROWS-CE] Value Search ----

    [Fact]
    public async Task ValueSearch_candidate_plus_CE_is_typed_from_the_field()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var vm = new ValueSearchViewModel(new StubDumpService(), _log, Up(bridge));

        await vm.AddCandidateToCeCommand.ExecuteAsync(new ValueCandidate
        {
            Addr = "0x7FF600002010", ClassName = "BP_Player_C", InstanceName = "BP_Player_C_0",
            FieldName = "Health", FieldType = "FloatProperty",
        });

        var rec = Assert.Single(bridge.Records);
        Assert.Equal("7FF600002010", rec.Address);
        Assert.Equal(4, rec.ValueType);   // vtSingle
        Assert.EndsWith("BP_Player_C_0.Health", rec.Description);
    }

    [Fact]
    public async Task ValueSearch_group_slot_without_its_own_leaf_address_sends_nothing()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var vm = new ValueSearchViewModel(new StubDumpService(), _log, Up(bridge));
        var slot = new GroupSlotMatch
        {
            Addr = "0x7FF600003000", ClassName = "C", FieldName = "Gold", FieldType = "IntProperty",
            HasLeafAddress = false,
        };

        await vm.AddGroupSlotToCeCommand.ExecuteAsync(slot);
        await vm.HexGroupSlotCommand.ExecuteAsync(slot);
        Assert.Empty(bridge.Records);
        Assert.Null(bridge.LastHex);

        slot.HasLeafAddress = true;
        await vm.AddGroupSlotToCeCommand.ExecuteAsync(slot);
        Assert.Equal("7FF600003000", Assert.Single(bridge.Records).Address);
    }

    // ---- [AOBM-VALUE-ROWS-CE] Snapshot diff ----

    private async Task<SnapshotViewModel> SnapshotVm(AobMakerStatus status, string diffBSession)
    {
        var vm = new SnapshotViewModel(new StubDumpService(), new SnapshotStore(new MockPlatformService(_dir)), _log,
                                       aobMaker: status);
        vm.SetEngineState(Game);
        await vm.PendingRefresh!;
        vm.DiffB = new SnapshotMeta { Label = "B", PeHash = "PE", GameSessionId = diffBSession };
        return vm;
    }

    private static SnapshotDiffRow DiffRow(string prop = "HP") => new()
    {
        ClassName = "BP_Player_C", PropName = prop, PropOffset = 0x10, DeclaredType = "IntProperty",
        ObjAddr = "0x7FF600001000",
    };

    [Fact]
    public async Task Snapshot_diff_row_plus_CE_lands_on_owner_plus_offset_in_session()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var vm = await SnapshotVm(Up(bridge), "PE-T");
        Assert.True(vm.CanPushDiffRowToCe);

        await vm.AddDiffRowToCeCommand.ExecuteAsync(DiffRow());

        var rec = Assert.Single(bridge.Records);
        Assert.Equal("7FF600001010", rec.Address);
        Assert.Equal(2, rec.ValueType);   // 4 Bytes
    }

    [Fact]
    public async Task Snapshot_rows_from_another_launch_send_nothing()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var vm = await SnapshotVm(Up(bridge), "PE-EARLIER");
        Assert.False(vm.CanPushDiffRowToCe);

        await vm.AddDiffRowToCeCommand.ExecuteAsync(DiffRow());
        await vm.HexDiffRowCommand.ExecuteAsync(DiffRow());

        Assert.Empty(bridge.Records);
        Assert.Null(bridge.LastHex);
    }

    [Fact]
    public async Task Snapshot_array_element_rows_are_refused_with_the_reason()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var vm = await SnapshotVm(Up(bridge), "PE-T");

        await vm.AddDiffRowToCeCommand.ExecuteAsync(DiffRow("Inventory[3].Count"));

        Assert.Empty(bridge.Records);
        Assert.StartsWith("Inventory[3].Count is an array element", vm.DiffStatusText);
    }

    [Fact]
    public async Task Snapshot_gate_follows_the_plugin()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var status = Up(bridge);
        var vm = await SnapshotVm(status, "PE-T");
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        status.Apply(false);

        Assert.False(vm.CanPushDiffRowToCe);
        Assert.Contains(nameof(SnapshotViewModel.CanPushDiffRowToCe), raised);
    }

    // ---- [AOBM-VALUE-ROWS-CE] SPC ----

    [Fact]
    public async Task Spc_result_row_plus_CE_needs_the_newest_pick_to_be_this_launch()
    {
        var store = new SnapshotStore(new MockPlatformService(_dir));
        var ct = TestContext.Current.CancellationToken;
        store.SetActiveGame("PE");
        long id = await store.CreateSnapshotAsync(new SnapshotMeta { Label = "S1", PeHash = "PE", GameSessionId = "PE-T" }, ct);
        var o = new SnapshotCapturedObject { Index = 1, Addr = "0x7FF600001000", Name = "O_1", ClassName = "C", Path = "/G.M:L.O_1" };
        o.Fields.Add(new SnapshotCapturedField { Name = "HP", Type = "IntProperty", Hex = "64000000", Offset = 0x10 });
        await store.WriteChunkAsync(id, new[] { o }, ct);
        await store.FinalizeSnapshotAsync(id, 1, 1, ct);

        var bridge = new ScriptedAobMakerBridge { Available = true };
        var vm = new SpcQueryViewModel(store, _log, aobMaker: Up(bridge));
        vm.SetEngineState(Game);
        await vm.PendingRefresh!;
        foreach (var p in vm.SnapshotPicks) p.IsSelected = true;
        Assert.True(vm.CanPushResultRowToCe);

        var row = new SpcResultRow
        {
            ClassName = "C", PropName = "HP", PropOffset = 0x10, DeclaredType = "IntProperty", ObjAddr = "0x7FF600001000",
        };
        await vm.AddResultRowToCeCommand.ExecuteAsync(row);
        Assert.Equal("7FF600001010", Assert.Single(bridge.Records).Address);

        row.PropName = "Arr[2]";
        await vm.HexResultRowCommand.ExecuteAsync(row);
        Assert.Null(bridge.LastHex);
        Assert.StartsWith("Arr[2] is an array element", vm.StatusText);
    }

    // ---- [AOBM-OBJECT-HEX] / [AOBM-FUNC-DISASM] ----

    [Fact]
    public async Task ObjectTree_and_RelatedObjects_HEX_park_the_hex_view_on_the_object()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var status = Up(bridge);

        var tree = new ObjectTreeViewModel(new StubDumpService(), _log, new MockPlatformService(_dir), status);
        await tree.HexAddressCommand.ExecuteAsync(new UObjectNode { Address = "0x7FF600004000", Name = "PC_0" });
        Assert.Equal("7FF600004000", bridge.LastHex);

        var related = new RelatedObjectsViewModel(new StubDumpService(), _log, new MockPlatformService(_dir), status);
        await related.HexAddressCommand.ExecuteAsync(new RelatedObject { Address = "0x7FF600005000", Name = "Pawn_0" });
        Assert.Equal("7FF600005000", bridge.LastHex);
        Assert.Equal("CE hex view: Pawn_0 @ 0x7FF600005000", related.StatusText);
    }

    private sealed class CodeAddrDump : StubDumpService
    {
        public override Task<string> GetFunctionCodeAddrAsync(string funcAddr, CancellationToken ct = default)
            => Task.FromResult(funcAddr == "0x7FF600006000" ? "0x7FF601230000" : "");
    }

    [Fact]
    public async Task LiveFuncs_ASM_opens_the_functions_native_entry()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var vm = new LiveFuncsViewModel(new CodeAddrDump(), _log, aobMaker: Up(bridge));

        await vm.AsmFuncCommand.ExecuteAsync(new PeProfileEntry { FuncAddr = "0x7FF600006000", FuncName = "Pawn:Jump" });

        Assert.Equal("7FF601230000", bridge.LastAsm);
        Assert.Equal("Pawn:Jump (code)", Assert.Single(bridge.Records).Description);
        Assert.Equal("Pawn:Jump: CE disassembler @ 0x7FF601230000", vm.StatusText);
    }

    [Fact]
    public async Task LiveFuncs_ASM_on_an_unloaded_row_asks_nothing()
    {
        // [TRACE-UNLOADED-NAMES] Its address is dead: no pipe call, no record pushed to CE.
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var vm = new LiveFuncsViewModel(new CodeAddrDump(), _log, aobMaker: Up(bridge));

        await vm.AsmFuncCommand.ExecuteAsync(new PeProfileEntry
        {
            FuncAddr = "0x7FF600006000", FuncName = "WBP_Inventory_C:OnOpen", IsUnloaded = true,
        });

        Assert.Null(bridge.LastAsm);
        Assert.Empty(bridge.Records);
    }

    // ---- [AOBM-LIVEWALKER-HEX-SILENT] Live Walker's four HEX buttons dropped the bridge's answer ----

    private LiveWalkerViewModel Walker(ScriptedAobMakerBridge bridge)
    {
        var vm = new LiveWalkerViewModel(new StubDumpService(), _log, new MockPlatformService(_dir), bridge);
        vm.ApplyAobMakerProbe(bridge.Available);
        return vm;
    }

    private static LiveFieldValue Hp => new()
    {
        Name = "HP", TypeName = "FloatProperty", Offset = 0x400, FieldAddress = "0x7FF600001400",
        PtrAddress = "0x7FF600009000",
    };

    [Fact]
    public async Task LiveWalker_HEX_says_where_CE_went()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var vm = Walker(bridge);
        vm.CurrentObjectName = "Pawn_0";
        vm.CurrentAddress = "0x7FF600001000";
        vm.CurrentOuterName = "Level_0";
        vm.CurrentOuterAddr = "0x7FF600002000";

        await vm.HexFieldAddressCommand.ExecuteAsync(Hp);
        Assert.Equal("7FF600001400", bridge.LastHex);
        Assert.Equal("CE hex view: HP @ 0x7FF600001400", vm.StatusText);

        await vm.HexPtrAddressCommand.ExecuteAsync(Hp);
        Assert.Equal("7FF600009000", bridge.LastHex);
        Assert.Equal("CE hex view: HP target @ 0x7FF600009000", vm.StatusText);

        await vm.HexObjectAddressCommand.ExecuteAsync(null);
        Assert.Equal("7FF600001000", bridge.LastHex);
        Assert.Equal("CE hex view: Pawn_0 @ 0x7FF600001000", vm.StatusText);

        await vm.HexOuterAddressCommand.ExecuteAsync(null);
        Assert.Equal("7FF600002000", bridge.LastHex);
        Assert.Equal("CE hex view: Level_0 @ 0x7FF600002000", vm.StatusText);
    }

    [Fact]
    public async Task LiveWalker_HEX_that_CE_refuses_says_so()
    {
        var vm = Walker(new ScriptedAobMakerBridge { Available = true, NavigateResult = false });

        await vm.HexFieldAddressCommand.ExecuteAsync(Hp);

        Assert.Equal("Cheat Engine did not move its view to HP @ 0x7FF600001400", vm.StatusText);
        Assert.True(vm.IsAobMakerAvailable);
    }

    [Fact]
    public async Task LiveWalker_HEX_that_finds_the_plugin_gone_says_why_and_turns_the_buttons_off()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true, NavigateResult = false, AvailableAfterCall = false };
        var vm = Walker(bridge);

        await vm.HexFieldAddressCommand.ExecuteAsync(Hp);

        Assert.Equal(AobMakerUnavailable.Text(bridge), vm.StatusText);
        Assert.False(vm.IsAobMakerAvailable);
    }
}
