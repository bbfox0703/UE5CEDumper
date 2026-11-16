using System.Text.Json.Nodes;
using UE5DumpUI.Mcp;
using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [MCP-WRITE-GRANTS-2026-11-14] Phase 1. The grant core on its own, with no pipe and no tool: which host gets writes,
/// how a DLL command is classed, what counts as a release, and what a grant admits. The plan and the measurements
/// behind each rule are docs/mcp-write-approval-eval.md.
/// </summary>
public sealed class McpGrantsTests
{
    private const long Pid = 4242;
    private const string Run = "1DB5F0A2C3E4D50";
    private const string OtherRun = "1DB5F0A2C3E4D51";

    private static JsonObject Ptrs(long pid = Pid, string? creationTime = Run)
    {
        var o = new JsonObject { ["ok"] = true, ["pid"] = pid };
        if (creationTime is not null) o["process_creation_time"] = creationTime;
        return o;
    }

    private static McpGrants Decided(string? name = "claude-code", string? version = "2.1.246",
        McpWriteMode mode = McpWriteMode.Default)
    {
        var g = new McpGrants(mode);
        g.Decide(name, version);
        return g;
    }

    private static JsonObject Req(string json) => JsonNode.Parse(json)!.AsObject();

    // ── The host decision ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("claude-code", "2.1.246", McpHostDecision.Grants)]
    [InlineData("claude-code", "2.1.322", McpHostDecision.Grants)]
    [InlineData("claude-code", "2.1.1000", McpHostDecision.Grants)]   // "2.1.1000" < "2.1.246" as text
    [InlineData("claude-code", "10.0.0", McpHostDecision.Grants)]
    [InlineData("claude-code", " 2.1.280 ", McpHostDecision.Grants)]
    [InlineData("claude-code", "2.1.245", McpHostDecision.MarkedPerCall)]
    [InlineData("claude-code", "2.1", McpHostDecision.MarkedPerCall)]
    [InlineData("claude-code", "2", McpHostDecision.MarkedPerCall)]
    [InlineData("claude-code", null, McpHostDecision.MarkedPerCall)]
    [InlineData("claude-code", "", McpHostDecision.MarkedPerCall)]
    [InlineData("claude-code", "2.1.246-beta", McpHostDecision.MarkedPerCall)]
    [InlineData("claude-code", "v2.1.300", McpHostDecision.MarkedPerCall)]
    [InlineData("claude-code", "+2.1.300", McpHostDecision.MarkedPerCall)]
    [InlineData("claude-code", "2.1.-300", McpHostDecision.MarkedPerCall)]
    [InlineData("Claude-Code", "2.1.300", McpHostDecision.ReadOnly)]
    [InlineData("claude-code ", "2.1.300", McpHostDecision.ReadOnly)]
    [InlineData("claude-ai", "0.1.0", McpHostDecision.ReadOnly)]
    [InlineData("mcp_client.py", "1", McpHostDecision.ReadOnly)]
    [InlineData(null, null, McpHostDecision.ReadOnly)]
    public void ByDefault_OnlyARecentClaudeCodeGetsGrants_AnOlderOneIsAskedPerCall_AndAnyOtherHostOnlyReads(
        string? name, string? version, McpHostDecision want)
        => Assert.Equal(want, McpGrants.DecideFor(McpWriteMode.Default, name, version));

    [Theory]
    [InlineData("claude-ai", "0.1.0", McpHostDecision.Grants)]
    [InlineData(null, null, McpHostDecision.Grants)]
    [InlineData("claude-code", "2.1.246", McpHostDecision.Grants)]
    // Doubt about Claude Code falls back to asking every call, never to trust, whatever the flag says.
    [InlineData("claude-code", "2.1.100", McpHostDecision.MarkedPerCall)]
    [InlineData("claude-code", null, McpHostDecision.MarkedPerCall)]
    public void AnyHost_OpensGrantsToEveryOtherHost_ButNotToAnOldClaudeCode(
        string? name, string? version, McpHostDecision want)
        => Assert.Equal(want, McpGrants.DecideFor(McpWriteMode.AnyHost, name, version));

    [Theory]
    [InlineData("claude-code", "2.1.300")]
    [InlineData("claude-code", "2.1.100")]
    [InlineData("claude-ai", "0.1.0")]
    [InlineData(null, null)]
    public void Off_LeavesEveryHostReadOnly(string? name, string? version)
        => Assert.Equal(McpHostDecision.ReadOnly, McpGrants.DecideFor(McpWriteMode.Off, name, version));

    [Fact]
    public void TheDecision_IsReadOnlyBeforeInitialize_AndAnInitializeLaterChangesNothing()
    {
        var g = new McpGrants();
        Assert.False(g.IsDecided);
        Assert.Equal(McpHostDecision.ReadOnly, g.Decision);

        Assert.Equal(McpHostDecision.MarkedPerCall, g.Decide("claude-code", "2.1.100"));
        // A client must not widen itself mid-session by introducing itself again.
        Assert.Equal(McpHostDecision.MarkedPerCall, g.Decide("claude-code", "2.1.300"));
        Assert.True(g.IsDecided);
        Assert.Equal(McpHostDecision.MarkedPerCall, g.Decision);
    }

    // ── How each command is classed ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheReadList_IsExactlyTheCommandsThatChangeNothing()
    {
        string[] reads =
        [
            "get_pointers", "scan_status", "trigger_scan", "search_objects", "find_object", "find_instances",
            "find_live_instance", "get_object", "find_by_address", "list_classes", "walk_class", "walk_functions",
            "walk_instance", "read_array_elements", "read_mem", "walk_world", "find_refs_to_uobject",
            "find_path_from_gworld", "get_related_objects", "get_current_target", "search_properties",
            "find_property_xrefs", "walk_function_props", "get_function_code_addr", "find_functions_by_class",
            "list_all_functions", "list_enums", "get_ce_pointer_info", "walk_datatable_rows", "begin_value_scan",
            "refine_value_scan", "query_candidates", "end_value_scan", "pipe_clients", "get_offsets", "get_holds",
            "teleport_get_pose", "get_movement_params", "get_time_state", "get_forced_fields", "seethrough_get_state",
        ];
        Assert.Equal(reads.Order(StringComparer.Ordinal), McpGrants.ReadCommands.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheGrantKinds_SplitAnnoyingFromMayLoseThePawn()
    {
        Assert.Equal(
            ["seethrough_set", "set_debug_camera", "set_foreground_lock", "set_god_mode", "set_mouse_cursor",
             "set_time_dilation"],
            McpGrants.CommandsOf(McpGrantKind.Toggles).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["fly_set", "set_gravity_direction", "set_movement_multiplier", "teleport_recall_marker"],
            McpGrants.CommandsOf(McpGrantKind.Movement).Order(StringComparer.Ordinal));
        Assert.Empty(McpGrants.CommandsOf(McpGrantKind.None));
        Assert.Equal(["force_field", "invoke_function", "write_mem"],
            McpGrants.PerCallCommands.Order(StringComparer.Ordinal));
        // An explicit set, never a reset_ prefix: reset_all_fields ends everyone's holds.
        Assert.Equal(["reset_field", "reset_gravity_direction", "reset_movement", "reset_time_dilation"],
            McpGrants.ReleaseCommands.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryCommandTheServerMaySend_IsInExactlyOneClass_AndNoForbiddenOneIsInAny()
    {
        IReadOnlySet<string>[] classes =
        [
            McpGrants.ReadCommands, McpGrants.ReleaseCommands, McpGrants.PerCallCommands,
            McpGrants.CommandsOf(McpGrantKind.Toggles), McpGrants.CommandsOf(McpGrantKind.Movement),
        ];
        Assert.Equal(McpToolsTests.AllowedCommands.Order(StringComparer.Ordinal),
            classes.SelectMany(c => c).Order(StringComparer.Ordinal));   // a union with no repeat
        foreach (var cmd in McpToolsTests.NeverSent)
        {
            Assert.DoesNotContain(classes, c => c.Contains(cmd));
            Assert.Equal(McpGrantKind.None, McpGrants.KindOf(cmd));
        }
        foreach (var cmd in McpGrants.CommandsOf(McpGrantKind.Movement))
            Assert.Equal(McpGrantKind.Movement, McpGrants.KindOf(cmd));
        Assert.Equal(McpGrantKind.None, McpGrants.KindOf(null));
        Assert.Equal(McpGrantKind.None, McpGrants.KindOf("SET_GOD_MODE"));
    }

    // ── What counts as a release ────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, bool> Releases => new()
    {
        { """{"cmd":"reset_movement","knob":"jump","if_free":true}""", true },
        { """{"cmd":"reset_gravity_direction","if_free":true}""", true },
        { """{"cmd":"reset_time_dilation","target":"pawn","if_free":true}""", true },
        { """{"cmd":"reset_field","class_name":"Pawn","field_name":"bCanBeDamaged","if_free":true}""", true },
        // Without if_free the DLL skips its holder check, and the OFF would end another's hold.
        { """{"cmd":"set_god_mode","enable":false}""", false },
        { """{"cmd":"set_god_mode","enable":false,"if_free":false}""", false },
        { """{"cmd":"reset_movement","knob":"jump"}""", false },
        { """{"cmd":"set_god_mode","enable":false,"if_free":true,"background":true,"id":7}""", true },
        { """{"cmd":"set_foreground_lock","enable":false,"if_free":true}""", true },
        { """{"cmd":"set_debug_camera","enable":false,"if_free":true}""", true },
        // An explicit See-through OFF drops every claim, the user's and CE's included.
        { """{"cmd":"seethrough_set","enable":false,"if_free":true}""", false },
        // ...but its own_claim OFF drops only the caller's claim (DLL 3714): a release whoever else holds one.
        { """{"cmd":"seethrough_set","enable":false,"own_claim":true,"if_free":true}""", true },
        { """{"cmd":"seethrough_set","enable":false,"own_claim":true,"if_free":true,"count":3}""", false },
        { """{"cmd":"seethrough_set","enable":false,"own_claim":false,"if_free":true}""", false },
        { """{"cmd":"set_god_mode","enable":false,"own_claim":true,"if_free":true}""", false },
        { """{"cmd":"fly_set","enable":false,"if_free":true}""", true },
        // A reset_ prefix is not enough: these two end what other clients hold, or reach the whole process.
        { """{"cmd":"reset_all_fields"}""", false },
        { """{"cmd":"reset_diagnostics"}""", false },
        { """{"cmd":"set_god_mode","enable":true}""", false },
        // The DLL reads a missing enable as false; the gate never reads absence as a release.
        { """{"cmd":"set_god_mode","if_free":true}""", false },
        { """{"cmd":"set_god_mode","enable":"false"}""", false },
        { """{"cmd":"set_god_mode","enable":0}""", false },
        { """{"cmd":"set_god_mode","enable":null}""", false },
        // These apply a global setting before enable, and nobody releases a global setting.
        { """{"cmd":"seethrough_set","enable":false,"count":3}""", false },
        { """{"cmd":"fly_set","enable":false,"noclip":true,"speed":20000}""", false },
        { """{"cmd":"fly_set","enable":false,"preset":2}""", false },
        { """{"cmd":"fly_set","speed":600}""", false },
        { """{"cmd":"set_gravity_direction","x":0,"y":0,"z":0}""", false },
        { """{"cmd":"set_mouse_cursor","show":false}""", false },
        { """{"cmd":"set_time_dilation","target":"global","value":1}""", false },
        { """{"cmd":"set_movement_multiplier","knob":"jump","multiplier":1}""", false },
        { """{"cmd":"teleport_recall_marker","x":0,"y":0,"z":0}""", false },
        { """{"cmd":"force_field","enable":false}""", false },
        { """{"cmd":"write_mem","addr":"0x1","bytes":"00"}""", false },
        { """{"cmd":"invoke_function","enable":false}""", false },
        { """{"cmd":"get_pointers"}""", false },
        { """{"cmd":"SET_GOD_MODE","enable":false}""", false },
        { """{"enable":false}""", false },
        { """{"cmd":7,"enable":false}""", false },
    };

    [Theory]
    [MemberData(nameof(Releases))]
    public void AReleaseIsAResetOrAnOffWithNothingElseInIt(string request, bool release)
        => Assert.Equal(release, McpGrants.IsRelease(Req(request)));

    // ── Grants ──────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, Run)]
    [InlineData(-1, Run)]
    [InlineData(Pid, "")]
    [InlineData(Pid, "0")]   // what get_pointers sends when GetProcessTimes failed: it would match every run of the pid
    [InlineData(Pid, null)]
    public void AGrant_NeedsTheGameRun_SoItCannotOutliveIt(long pid, string? creationTime)
    {
        var g = Decided();
        Assert.Null(g.Grant(McpGrantKind.Toggles, Ptrs(pid, creationTime), out var refusal));
        Assert.False(string.IsNullOrWhiteSpace(refusal));
        Assert.Empty(g.List());
    }

    [Fact]
    public void AGrant_IsRefusedOffAGrantsHost_AndForNoKind()
    {
        Assert.Null(Decided(version: "2.1.100").Grant(McpGrantKind.Toggles, Ptrs(), out var perCall));
        Assert.NotNull(perCall);
        Assert.Null(Decided("claude-ai").Grant(McpGrantKind.Toggles, Ptrs(), out var readOnly));
        Assert.NotNull(readOnly);
        Assert.Null(Decided().Grant(McpGrantKind.None, Ptrs(), out var none));
        Assert.NotNull(none);
    }

    [Fact]
    public void GrantingTheSameKindForTheSameRunTwice_KeepsOneGrant()
    {
        var g = Decided();
        var first = g.Grant(McpGrantKind.Toggles, Ptrs(), out _);
        var again = g.Grant(McpGrantKind.Toggles, Ptrs(), out _);
        Assert.NotNull(first);
        Assert.Equal(first!.Id, again!.Id);
        var movement = g.Grant(McpGrantKind.Movement, Ptrs(), out _);
        Assert.NotEqual(first.Id, movement!.Id);
        Assert.Equal(2, g.List().Count);
        Assert.Equal((Pid, Run), (first.Pid, first.CreationTime));
    }

    [Fact]
    public void AGrant_AdmitsItsOwnKindForItsOwnRun_AndNothingElse()
    {
        var g = Decided();
        Assert.Null(g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, Ptrs(), out var before));
        Assert.Contains(Constants.McpGrantToolToggles, before);   // the refusal names the exact grant to ask for

        g.Grant(McpGrantKind.Toggles, Ptrs(), out _);
        Assert.NotNull(g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, Ptrs(), out _));
        Assert.Null(g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Movement, Ptrs(), out var movement));
        Assert.Contains(Constants.McpGrantToolMovement, movement);
        Assert.Null(g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, Ptrs(creationTime: OtherRun), out _));
        Assert.Null(g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, Ptrs(pid: Pid + 1), out _));
    }

    [Fact]
    public void EveryCallTheGrantLetsThrough_IsCounted_AndNothingElse()
    {
        // [MCP-GRANT-USES-RELEASES] Found live (build 3714): uses counted an OFF and a reset that needed no grant.
        var g = Decided();
        g.Grant(McpGrantKind.Movement, Ptrs(), out _);
        var fly = Req("""{"cmd":"fly_set","enable":true}""");
        var flyOff = Req("""{"cmd":"fly_set","enable":false,"if_free":true}""");
        for (int i = 0; i < 3; i++)
        {
            var cap = g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Movement, Ptrs(), out _);
            Assert.Null(g.Check(fly, cap, Pid, Run));
        }
        g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, Ptrs(), out _);   // refused: not counted
        // Admitted but never sent (the tool failed after admission): not counted either.
        g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Movement, Ptrs(), out _);
        // An OFF of the server's own hold goes through on that, not on the grant.
        var own = g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Movement, Ptrs(), out _);
        Assert.Null(g.Check(flyOff, own, Pid, Run, Held("fly")));
        Assert.Equal(3, Assert.Single(g.List()).Uses);
    }

    [Fact]
    public void APerCallWrite_NeedsNoGrant_OnAGrantsHost()
    {
        var cap = Decided().Admit(McpToolRole.PerCallWrite, McpGrantKind.None, Ptrs(), out _);
        Assert.NotNull(cap);
        Assert.True(cap!.PerCall);
    }

    [Fact]
    public void OnAMarkedHost_EveryWriteIsAdmittedPerCall_AndOnAReadOnlyHostNone()
    {
        var marked = Decided(version: "2.1.100");
        var cap = marked.Admit(McpToolRole.GrantedWrite, McpGrantKind.Movement, Ptrs(), out _);
        Assert.NotNull(cap);
        Assert.True(cap!.PerCall);
        Assert.Equal(McpGrantKind.Movement, cap.Kind);

        var readOnly = Decided("claude-ai");
        Assert.Null(readOnly.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, Ptrs(), out var r1));
        Assert.Null(readOnly.Admit(McpToolRole.PerCallWrite, McpGrantKind.None, Ptrs(), out var r2));
        Assert.NotNull(r1);
        Assert.NotNull(r2);
        Assert.Null(new McpGrants().Admit(McpToolRole.PerCallWrite, McpGrantKind.None, Ptrs(), out _));
    }

    [Theory]
    [InlineData(McpToolRole.Read)]
    [InlineData(McpToolRole.Scan)]
    [InlineData(McpToolRole.Release)]
    [InlineData(McpToolRole.Grant)]
    [InlineData(McpToolRole.GrantAdmin)]
    public void OnlyAWriteRoleIsAdmitted(McpToolRole role)
    {
        var g = Decided();
        g.Grant(McpGrantKind.Toggles, Ptrs(), out _);
        Assert.Null(g.Admit(role, McpGrantKind.Toggles, Ptrs(), out _));
    }

    [Fact]
    public void RevokeOnlyNarrows()
    {
        var g = Decided();
        g.Grant(McpGrantKind.Toggles, Ptrs(), out _);
        g.Grant(McpGrantKind.Movement, Ptrs(), out _);
        g.Grant(McpGrantKind.Toggles, Ptrs(pid: 77, creationTime: OtherRun), out _);

        Assert.Equal(0, g.Revoke(McpGrantKind.Movement, pid: 77));
        Assert.Equal(1, g.Revoke(McpGrantKind.Movement));
        Assert.Equal(2, g.List().Count);
        Assert.Equal(1, g.Revoke(pid: 77));
        Assert.Equal(McpGrantKind.Toggles, Assert.Single(g.List()).Kind);
        Assert.Equal(1, g.Revoke());
        Assert.Empty(g.List());

        g.Grant(McpGrantKind.Movement, Ptrs(), out _);
        Assert.Equal(1, g.Clear());
        Assert.Empty(g.List());
    }

    // ── The gate's check ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheGate_PassesAReadWithNoCapability_OnEveryHost()
    {
        foreach (var g in new[] { new McpGrants(), Decided("claude-ai"), Decided(version: "2.1.100"), Decided() })
            Assert.Null(g.Check(Req("""{"cmd":"get_pointers"}"""), null, Pid, Run));
    }

    [Fact]
    public void TheGate_PassesAReleaseWithNoCapability_ButNotOnAReadOnlyHost()
    {
        var off = Req("""{"cmd":"set_god_mode","enable":false,"if_free":true}""");
        Assert.Null(Decided().Check(off, null, Pid, Run, Held("god_mode")));
        Assert.Null(Decided(version: "2.1.100").Check(off, null, Pid, Run, Held("god_mode")));
        Assert.NotNull(Decided("claude-ai").Check(off, null, Pid, Run, Held("god_mode")));
        Assert.NotNull(new McpGrants().Check(off, null, Pid, Run, Held("god_mode")));
    }

    /// <summary>A get_holds reply naming one hold.</summary>
    private static JsonObject Held(string hold, bool you = true, bool active = true, bool pending = false,
        string kind = "client")
    {
        var entry = new JsonObject
        {
            ["hold"] = hold, ["active"] = active,
            ["holder"] = new JsonObject { ["kind"] = kind, ["you"] = you },
        };
        if (pending) entry["pending"] = true;
        return new JsonObject { ["ok"] = true, ["holds"] = new JsonArray(entry) };
    }

    [Theory]
    [InlineData("""{"cmd":"set_god_mode","enable":false,"if_free":true}""", "god_mode")]
    [InlineData("""{"cmd":"set_foreground_lock","enable":false,"if_free":true}""", "foreground_lock")]
    [InlineData("""{"cmd":"set_debug_camera","enable":false,"if_free":true}""", "debug_camera")]
    [InlineData("""{"cmd":"fly_set","enable":false,"if_free":true}""", "fly")]
    [InlineData("""{"cmd":"reset_movement","knob":"jump","if_free":true}""", "movement:jump")]
    [InlineData("""{"cmd":"reset_gravity_direction","if_free":true}""", "gravity_direction")]
    [InlineData("""{"cmd":"reset_time_dilation","target":"pawn","if_free":true}""", "time:pawn")]
    [InlineData("""{"cmd":"reset_time_dilation","if_free":true}""", "time:global")]
    [InlineData("""{"cmd":"reset_field","class_name":"Pawn","field_name":"bCanBeDamaged","if_free":true}""",
        "force_field:Pawn::bCanBeDamaged")]
    public void AReleaseNeedsNoAdmission_OnlyWhileThisServerHoldsWhatItEnds(string request, string hold)
    {
        var g = Decided();
        var r = Req(request);
        Assert.Equal(hold, McpGrants.ReleasedHold(r));
        Assert.Null(g.Check(r, null, Pid, Run, Held(hold)));
        // Someone else's, nobody's, or not known: ending it changes the game -- god mode's OFF writes the normal value
        // to every protection bool, whoever made the pawn invulnerable -- so it is no release.
        Assert.NotNull(g.Check(r, null, Pid, Run, Held(hold, you: false, kind: "ce")));
        Assert.NotNull(g.Check(r, null, Pid, Run, Held(hold, active: false)));
        Assert.NotNull(g.Check(r, null, Pid, Run, Held(hold + "_other")));
        Assert.NotNull(g.Check(r, null, Pid, Run, null));
        Assert.NotNull(g.Check(r, null, Pid, Run, new JsonObject { ["ok"] = false }));
    }

    [Theory]
    [InlineData("""{"cmd":"reset_movement","knob":"run","if_free":true}""")]
    [InlineData("""{"cmd":"reset_time_dilation","target":"world","if_free":true}""")]
    [InlineData("""{"cmd":"reset_field","class_name":"Pawn","if_free":true}""")]
    public void ARequestNamingNoHold_ReleasesNothing(string request)
        => Assert.Null(McpGrants.ReleasedHold(Req(request)));

    [Theory]
    [InlineData("""{"cmd":"set_god_mode","enable":false,"if_free":true}""", "god_mode")]
    [InlineData("""{"cmd":"reset_field","class_name":"Pawn","field_name":"X","if_free":true}""", "force_field:Pawn::X")]
    public void AnOffOfWhatSomeoneElseHolds_IsRefusedNamingTheHolder_GrantOrNot(string request, string hold)
    {
        // Found live (build 3714, mcp_client.py holdsmore): the refusal stopped naming the holder once the server, not
        // the DLL's if_free, refused it. The DLL would refuse it anyway, so no grant is asked for.
        var g = Decided();
        g.Grant(McpGrantKind.Toggles, Ptrs(), out _);
        var toggles = g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, Ptrs(), out _);
        var held = Held(hold, you: false);
        held["holds"]![0]!["holder"]!["pid"] = 77;
        foreach (var cap in new[] { null, toggles })
        {
            string? refusal = g.Check(Req(request), cap, Pid, Run, held);
            Assert.Contains(hold + " is held by the UE5DumpUI window or another program (pid 77)", refusal);
            Assert.DoesNotContain("grant_", refusal);
        }
    }

    [Fact]
    public void ADebugCameraOnStillOnItsWay_IsTheServersToTakeBack()
        => Assert.Null(Decided().Check(Req("""{"cmd":"set_debug_camera","enable":false,"if_free":true}"""), null,
            Pid, Run, Held("debug_camera", active: false, pending: true)));

    [Fact]
    public void AnOffOfWhatTheServerDoesNotHold_GoesThroughOnlyOnTheGrantOfItsKind()
    {
        var g = Decided();
        var godOff = Req("""{"cmd":"set_god_mode","enable":false,"if_free":true}""");
        var jumpReset = Req("""{"cmd":"reset_movement","knob":"jump","if_free":true}""");
        var fieldReset = Req("""{"cmd":"reset_field","class_name":"Pawn","field_name":"X","if_free":true}""");
        var nobody = new JsonObject { ["ok"] = true, ["holds"] = new JsonArray() };
        Assert.Contains(Constants.McpGrantToolToggles, g.Check(godOff, null, Pid, Run, nobody));
        Assert.Contains(Constants.McpGrantToolMovement, g.Check(jumpReset, null, Pid, Run, nobody));

        g.Grant(McpGrantKind.Toggles, Ptrs(), out _);
        g.Grant(McpGrantKind.Movement, Ptrs(), out _);
        var toggles = g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, Ptrs(), out _);
        var movement = g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Movement, Ptrs(), out _);
        var perCall = g.Admit(McpToolRole.PerCallWrite, McpGrantKind.None, Ptrs(), out _);
        Assert.Null(g.Check(godOff, toggles, Pid, Run, nobody));
        Assert.Null(g.Check(jumpReset, movement, Pid, Run, nobody));
        Assert.NotNull(g.Check(jumpReset, toggles, Pid, Run, nobody));
        // A forced-field job this server does not hold is never ended through it, on any admission.
        Assert.NotNull(g.Check(fieldReset, perCall, Pid, Run, nobody));
        Assert.NotNull(g.Check(fieldReset, toggles, Pid, Run, nobody));
    }

    [Fact]
    public void SeeThroughsOwnClaimOff_NeedsNoAdmission_AndNoHoldsRead()
        => Assert.Null(Decided().Check(
            Req("""{"cmd":"seethrough_set","enable":false,"own_claim":true,"if_free":true}"""), null, Pid, Run));

    [Fact]
    public void SeeThroughOff_NeedsTheTogglesGrant()
    {
        var g = Decided();
        var off = Req("""{"cmd":"seethrough_set","enable":false,"if_free":true}""");
        Assert.Contains(Constants.McpGrantToolToggles, g.Check(off, null, Pid, Run, Held("god_mode")));
        g.Grant(McpGrantKind.Toggles, Ptrs(), out _);
        Assert.Null(g.Check(off, g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, Ptrs(), out _), Pid, Run));
    }

    [Theory]
    [InlineData("""{"cmd":"set_god_mode","enable":true}""")]
    [InlineData("""{"cmd":"write_mem","addr":"0x1","bytes":"00"}""")]
    [InlineData("""{"cmd":"reset_all_fields"}""")]
    [InlineData("""{"cmd":"teleport_save_marker"}""")]
    [InlineData("""{"cmd":"something_new"}""")]
    [InlineData("""{"enable":true}""")]
    public void TheGate_RefusesEveryOtherCommand_WithNoCapability(string request)
        => Assert.NotNull(Decided().Check(Req(request), null, Pid, Run));

    [Fact]
    public void TheGate_PassesAGrantedWrite_OnlyForTheRunItWasAdmittedFor()
    {
        var g = Decided();
        g.Grant(McpGrantKind.Toggles, Ptrs(), out _);
        var cap = g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, Ptrs(), out _);
        var on = Req("""{"cmd":"set_god_mode","enable":true,"if_free":true}""");

        Assert.Null(g.Check(on, cap, Pid, Run));
        // The link reconnected to whichever game answered: another run of the same pid, or another game.
        Assert.NotNull(g.Check(on, cap, Pid, OtherRun));
        Assert.NotNull(g.Check(on, cap, Pid + 1, Run));
        Assert.NotNull(g.Check(on, cap, Pid, null));
    }

    [Fact]
    public void TheGate_RefusesACapabilityForAnotherKindOrClass()
    {
        var g = Decided();
        g.Grant(McpGrantKind.Toggles, Ptrs(), out _);
        var toggles = g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, Ptrs(), out _);
        var perCall = g.Admit(McpToolRole.PerCallWrite, McpGrantKind.None, Ptrs(), out _);

        Assert.NotNull(g.Check(Req("""{"cmd":"fly_set","enable":true}"""), toggles, Pid, Run));
        Assert.NotNull(g.Check(Req("""{"cmd":"write_mem","addr":"0x1","bytes":"00"}"""), toggles, Pid, Run));
        Assert.NotNull(g.Check(Req("""{"cmd":"reset_all_fields"}"""), toggles, Pid, Run));
        Assert.Null(g.Check(Req("""{"cmd":"write_mem","addr":"0x1","bytes":"00"}"""), perCall, Pid, Run));
        // A per-call capability on a grants host does not stand in for a grant.
        Assert.NotNull(g.Check(Req("""{"cmd":"set_god_mode","enable":true}"""), perCall, Pid, Run));
    }

    [Fact]
    public void TheGate_LetsARevokeThatLandsBeforeTheSendWin()
    {
        var g = Decided();
        g.Grant(McpGrantKind.Movement, Ptrs(), out _);
        var cap = g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Movement, Ptrs(), out _);
        g.Revoke(McpGrantKind.Movement);
        Assert.NotNull(g.Check(Req("""{"cmd":"fly_set","enable":true}"""), cap, Pid, Run));

        // A new grant is a new grant: the capability admitted under the revoked one stays dead.
        g.Grant(McpGrantKind.Movement, Ptrs(), out _);
        Assert.NotNull(g.Check(Req("""{"cmd":"fly_set","enable":true}"""), cap, Pid, Run));
    }

    // ── Block AI writes ([MCP-WRITE-GRANTS-2026-11-14] §4.10) ───────────────────────────────────────────

    /// <summary>The block file as the server reads it on every call, changed by the test between calls.</summary>
    private sealed class BlockFile
    {
        public AiWriteBlockState State;
    }

    private static (McpGrants Grants, BlockFile File) Blockable(long stamp = 0)
    {
        var file = new BlockFile { State = new AiWriteBlockState(false, stamp, false) };
        var g = new McpGrants(McpWriteMode.Default, null, () => file.State);
        g.Decide("claude-code", "2.1.246");
        return (g, file);
    }

    private static readonly JsonObject GodOn = Req("""{"cmd":"set_god_mode","enable":true,"if_free":true}""");
    private static readonly JsonObject GodOff = Req("""{"cmd":"set_god_mode","enable":false,"if_free":true}""");
    private static readonly JsonObject WriteMem = Req("""{"cmd":"write_mem","addr":"0x1","bytes":"00","expect":"00"}""");

    [Fact]
    public void WhileBlocked_EveryChangeIsRefused_ButReadsAndReleasesPass()
    {
        var (g, file) = Blockable();
        g.Grant(McpGrantKind.Toggles, Ptrs(), out _);
        var toggles = g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, Ptrs(), out _);
        var perCall = g.Admit(McpToolRole.PerCallWrite, McpGrantKind.None, Ptrs(), out _);
        file.State = new AiWriteBlockState(true, 0, false);

        // Admitted before the switch went on, refused at the send: the still-marked per-call writes too.
        Assert.Contains("Block AI writes", g.Check(GodOn, toggles, Pid, Run));
        Assert.Contains("Block AI writes", g.Check(WriteMem, perCall, Pid, Run));
        // An AI can still clean up, and still read; scan_start stays a read.
        Assert.Null(g.Check(GodOff, null, Pid, Run, Held("god_mode")));
        Assert.Null(g.Check(Req("""{"cmd":"get_pointers"}"""), null, Pid, Run));
        Assert.Null(g.Check(Req("""{"cmd":"trigger_scan"}"""), null, Pid, Run));

        Assert.Null(g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, Ptrs(), out var granted));
        Assert.Contains("Block AI writes", granted);
        Assert.Null(g.Admit(McpToolRole.PerCallWrite, McpGrantKind.None, Ptrs(), out var asked));
        Assert.Contains("Block AI writes", asked);
        Assert.Null(g.Grant(McpGrantKind.Movement, Ptrs(), out var grant));
        Assert.Contains("Block AI writes", grant);
    }

    [Fact]
    public void AnUnreadableBlockFile_RefusesLikeABlock_InItsOwnWords()
    {
        // Fail closed, but never claim the user turned the switch on over a file that could not be read.
        var (g, file) = Blockable();
        g.Grant(McpGrantKind.Toggles, Ptrs(), out _);
        var cap = g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, Ptrs(), out _);
        file.State = new AiWriteBlockState(false, 0, true);
        string? refusal = g.Check(GodOn, cap, Pid, Run);
        Assert.Contains("could not be read", refusal);
        Assert.DoesNotContain("turned on", refusal);
        Assert.Null(g.Check(GodOff, null, Pid, Run, Held("god_mode")));
    }

    [Fact]
    public void TurningTheBlockOn_EndsEveryGrant_EvenWhenItIsOffAgainByTheNextCall()
    {
        var (g, file) = Blockable(stamp: 3);
        g.Grant(McpGrantKind.Toggles, Ptrs(), out _);
        g.Grant(McpGrantKind.Movement, Ptrs(), out _);
        var cap = g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, Ptrs(), out _);
        Assert.NotNull(cap);   // the first look at the stamp revokes nothing

        file.State = new AiWriteBlockState(false, 4, false);   // on, then off again, between two calls
        Assert.NotNull(g.Check(GodOn, cap, Pid, Run));
        Assert.Empty(g.List());
        Assert.Null(g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, Ptrs(), out _));

        // The user asked again: a new grant works.
        Assert.NotNull(g.Grant(McpGrantKind.Toggles, Ptrs(), out _));
        Assert.NotNull(g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, Ptrs(), out _));
    }

    [Fact]
    public void AStampThatGoesDown_StillRevokes()
    {
        // A deleted file restarts the counter: a "newer" check would never revoke again.
        var (g, file) = Blockable(stamp: 5);
        g.Grant(McpGrantKind.Movement, Ptrs(), out _);
        file.State = new AiWriteBlockState(false, 0, false);
        Assert.Empty(g.List());
    }

    [Fact]
    public void TheGate_PassesAMarkedHostsPerCallCapability_ForItsOwnKind()
    {
        var g = Decided(version: "2.1.100");
        var cap = g.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, Ptrs(), out _);
        Assert.Null(g.Check(Req("""{"cmd":"set_god_mode","enable":true}"""), cap, Pid, Run));
        Assert.NotNull(g.Check(Req("""{"cmd":"fly_set","enable":true}"""), cap, Pid, Run));
    }
}
