using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UE5DumpUI.Mcp;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Stage D1. The MCP tool catalogue: what every tool advertises, what it sends the DLL,
/// and what it hands back. A fake game records each request and answers from a script, so no pipe is involved.
/// </summary>
public sealed class McpToolsTests
{
    // ── Rig ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A game that records every request and answers by command; scanned unless told otherwise.</summary>
    private sealed class FakeGame : IMcpGame
    {
        public List<JsonObject> Sent { get; } = new();
        public Dictionary<string, Func<JsonObject, JsonObject>> Answers { get; } = new();
        public bool ScannedGame { get; set; } = true;
        public bool ScanRunning { get; set; }
        public string? PendingNotice { get; set; }
        private bool _marked;

        public (IReadOnlyList<McpGameInfo> Games, bool LegacyPresent) ListGames()
            => ([new McpGameInfo(4242, "Game-Win64-Shipping.exe", "Game", true)], false);

        public int ConnectedPid => 4242;
        public bool Scanned => _marked;
        public void MarkScanned() => _marked = true;
        public string? TakeNotice() { var n = PendingNotice; PendingNotice = null; return n; }

        public Task<JsonObject> SendAsync(int? pid, JsonObject request, TimeSpan timeout, CancellationToken ct)
        {
            Sent.Add((JsonObject)request.DeepClone());
            string cmd = request["cmd"]!.GetValue<string>();
            JsonObject reply = Answers.TryGetValue(cmd, out var f) ? f(request) : cmd switch
            {
                "get_pointers" => new JsonObject
                {
                    ["object_count"] = ScannedGame ? 24530 : 0,
                    ["gobjects_method"] = ScannedGame ? "aob" : "not_found",
                    ["build_number"] = 1,
                    ["pid"] = 4242,
                },
                "scan_status" => new JsonObject { ["running"] = ScanRunning, ["scanned"] = ScannedGame },
                _ => new JsonObject(),
            };
            reply["id"] = 1;
            reply["ok"] ??= true;
            reply["_t_read_ms"] = 3;
            return Task.FromResult(reply);
        }

        public IEnumerable<string> Commands => Sent.Select(r => r["cmd"]!.GetValue<string>());
    }

    private static IReadOnlyList<McpTool> Catalog(FakeGame? game = null) => McpTools.Create(game ?? new FakeGame());

    private static McpTool Tool(string name, FakeGame game) => Catalog(game).Single(t => t.Name == name);

    private static async Task<(JsonNode? Payload, McpToolResult Result)> Call(FakeGame game, string tool,
        JsonObject? args = null)
    {
        McpToolResult result;
        try { result = await Tool(tool, game).Run(new McpArgs(args), CancellationToken.None); }
        catch (McpToolError e) { result = McpToolResult.Fail(e.Message); }
        return (result.Payload, result);
    }

    // ── The catalogue ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryName_FitsClaudeCode_AndIsUnique()
    {
        // Claude Code drops a tool whose mcp__<server>__<tool> name passes 128 characters, and MCP asks for names of
        // [A-Za-z0-9_.-]; this catalogue keeps to the stricter lower-case snake case.
        var names = Catalog().Select(t => t.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        foreach (var n in names)
        {
            Assert.Matches("^[a-z][a-z0-9_]{2,63}$", n);
            Assert.True(("mcp__" + Constants.McpServerName + "__" + n).Length <= 128, n);
        }
    }

    [Fact]
    public void ReadOnlyHint_IsExactlyTheReadPrefix_AndScanStartIsTheOnlyOtherTool()
    {
        // One allow rule, mcp__<server>__read_*, must admit every read and nothing else.
        var tools = Catalog();
        Assert.All(tools, t => Assert.Equal(t.Name.StartsWith("read_", StringComparison.Ordinal), t.ReadOnly));
        Assert.Equal(["scan_start"], tools.Where(t => !t.ReadOnly).Select(t => t.Name).ToArray());
    }

    [Fact]
    public void EverySchema_IsAPlainObject_WithFittingPropertyNames_AndItsRequiredDeclared()
    {
        foreach (var t in Catalog())
        {
            var s = t.InputSchema;
            Assert.Equal("object", s["type"]!.GetValue<string>());
            // Claude Code flattens a root-level anyOf / oneOf.
            Assert.False(s.ContainsKey("anyOf") || s.ContainsKey("oneOf") || s.ContainsKey("allOf"), t.Name);
            var props = s["properties"]!.AsObject();
            foreach (var (name, _) in props) Assert.Matches("^[A-Za-z0-9_.-]{1,64}$", name);
            if (s["required"] is JsonArray req)
                foreach (var r in req) Assert.True(props.ContainsKey(r!.GetValue<string>()), $"{t.Name}: {r}");
            Assert.InRange(t.Description.Length, 20, 4096);
        }
    }

    [Fact]
    public void EveryGameTool_TakesAPid_AndTheGameListDoesNot()
    {
        foreach (var t in Catalog())
        {
            bool hasPid = t.InputSchema["properties"]!.AsObject().ContainsKey("pid");
            Assert.Equal(t.Name != "read_games", hasPid);
        }
    }

    /// <summary>The DLL commands D1 may send. Everything else either writes, toggles or invokes (D2), or changes state
    /// another client shares (maintainer, 2026-11-11: an AI beside the UI must not disturb it): begin_snapshot clears
    /// the name cache a UI snapshot uses, the LiveFuncs recording is one per game, and reset_diagnostics and the
    /// version / timeout / packed overrides are process-wide.</summary>
    private static readonly HashSet<string> AllowedCommands =
    [
        "get_pointers", "scan_status", "trigger_scan", "search_objects", "find_object", "find_instances",
        "find_live_instance", "get_object", "find_by_address", "list_classes", "walk_class", "walk_functions",
        "walk_instance", "read_array_elements", "read_mem", "walk_world", "find_refs_to_uobject",
        "find_path_from_gworld", "get_related_objects", "get_current_target", "search_properties",
        "find_property_xrefs", "walk_function_props", "get_function_code_addr", "find_functions_by_class",
        "list_all_functions", "list_enums", "get_ce_pointer_info", "walk_datatable_rows", "begin_value_scan",
        "refine_value_scan", "query_candidates", "end_value_scan", "pipe_clients", "get_offsets",
    ];

    [Fact]
    public async Task EveryTool_CalledWithArgumentsItsOwnSchemaAllows_SendsOnlyReadCommands()
    {
        // Driven by each schema, so a tool added later is covered without a line here. Twice: on a scanned game (every
        // tool's main path) and on an unscanned one (scan_start's path that starts a scan).
        var refused = new List<string>();
        foreach (var t in Catalog())
        {
            foreach (bool scanned in new[] { true, false })
            {
                var game = new FakeGame { ScannedGame = scanned };
                var (payload, result) = await Call(game, t.Name, ArgsFromSchema(t.InputSchema));
                if (scanned && result.IsError) refused.Add($"{t.Name}: {payload!["error"]}");
                foreach (var cmd in game.Commands)
                    Assert.True(AllowedCommands.Contains(cmd), $"{t.Name} sent {cmd}");
            }
        }
        // Every tool's schema-valid call succeeded on a scanned game, so its main path was walked.
        Assert.Empty(refused);
    }

    [Fact]
    public void TheMcpSource_NamesNoDllCommandBeyondTheReadSet()
    {
        // The dynamic test above follows each tool's main path; this one reads every string literal under Mcp/ against
        // the DLL's whole command vocabulary (Renge.h), so a command on a path the test does not reach is seen too.
        string renge = File.ReadAllText(NumericInputCoercionTests.RepoFile("dll/src/Renge.h"));
        var vocabulary = Regex.Matches(renge, "CMD_[A-Z0-9_]+\\s*=\\s*\"([a-z0-9_]+)\"")
            .Select(m => m.Groups[1].Value).ToHashSet();
        Assert.True(vocabulary.Count >= 100, $"Renge.h vocabulary read as {vocabulary.Count}");
        Assert.Subset(vocabulary, AllowedCommands);

        var mcpDir = Path.GetDirectoryName(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Mcp/McpTools.cs"))!;
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(mcpDir, "*.cs"))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"([a-z0-9_]+)\""))
                if (vocabulary.Contains(m.Groups[1].Value)) used.Add(m.Groups[1].Value);
        Assert.NotEmpty(used);
        foreach (var cmd in used) Assert.True(AllowedCommands.Contains(cmd), $"Mcp/ names the DLL command {cmd}");
    }

    /// <summary>A value for every property but pid, each one its schema allows: the optional ones too, since a rule
    /// a schema cannot state ("class_name or name_filter") would otherwise refuse the call.</summary>
    private static JsonObject ArgsFromSchema(JsonObject schema)
    {
        var args = new JsonObject();
        var props = schema["properties"]!.AsObject();
        foreach (var (name, node) in props)
        {
            if (name == "pid") continue;
            var p = node!.AsObject();
            string type = p["type"]!.GetValue<string>();
            args[name] = type switch
            {
                "integer" => name == "session_id" ? 7 : p["minimum"]?.GetValue<int>() ?? 1,
                "boolean" => false,
                "array" => new JsonArray(),
                _ when p["enum"] is JsonArray e => e[0]!.GetValue<string>(),
                _ when p.ContainsKey("pattern") => "0x7FF600001000",
                _ => "Health",
            };
        }
        return args;
    }

    // ── What the tools send ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchObjects_SendsTheQueryWithASmallDefaultLimit()
    {
        var game = new FakeGame();
        await Call(game, "read_search_objects", new JsonObject { ["query"] = "BP_ Enemy" });
        var req = game.Sent.Last();
        Assert.Equal("search_objects", req["cmd"]!.GetValue<string>());
        Assert.Equal("BP_ Enemy", req["query"]!.GetValue<string>());
        Assert.Equal(50, req["limit"]!.GetValue<int>());
        Assert.False(req.ContainsKey("instances_only"));
    }

    [Theory]
    [InlineData("7ff6a1b2c3d0", "0x7FF6A1B2C3D0")]
    [InlineData("0x7ff6a1b2c3d0", "0x7FF6A1B2C3D0")]
    [InlineData(" 0X10 ", "0x10")]
    public async Task Addresses_AreNormalisedToUpperHexWithThePrefix(string given, string sent)
    {
        var game = new FakeGame();
        await Call(game, "read_object", new JsonObject { ["addr"] = given });
        Assert.Equal(sent, game.Sent.Last()["addr"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("zz")]
    [InlineData("0x")]
    [InlineData("0")]
    [InlineData("0x12345678901234567")]   // 17 digits
    public async Task AMalformedAddress_IsRefusedBeforeAnythingIsSent(string given)
    {
        // The DLL's read handlers read malformed hex as 0 and answer "not found": the model would conclude the object
        // does not exist.
        var game = new FakeGame();
        var (payload, result) = await Call(game, "read_object", new JsonObject { ["addr"] = given });
        Assert.True(result.IsError);
        Assert.Contains("addr", payload!["error"]!.GetValue<string>());
        Assert.DoesNotContain("get_object", game.Commands);
    }

    [Fact]
    public async Task ReadMemory_ClampsTheSizeToItsSchemaBound()
    {
        var game = new FakeGame();
        await Call(game, "read_memory", new JsonObject { ["addr"] = "0x1000", ["size"] = 1_000_000 });
        Assert.Equal(4096, game.Sent.Last()["size"]!.GetValue<int>());
    }

    [Fact]
    public async Task FindInstances_NeedsAClassOrAName()
    {
        var game = new FakeGame();
        var (payload, result) = await Call(game, "read_find_instances", new JsonObject());
        Assert.True(result.IsError);
        Assert.Contains("class_name", payload!["error"]!.GetValue<string>());
        Assert.DoesNotContain("find_instances", game.Commands);
    }

    [Fact]
    public async Task ValueScanBegin_RefusesAnUnknownType_AndSendsOnlyNonDefaultFlags()
    {
        var game = new FakeGame();
        var (_, bad) = await Call(game, "read_value_scan_begin",
            new JsonObject { ["data_type"] = "Int33", ["value"] = "1" });
        Assert.True(bad.IsError);
        await Call(game, "read_value_scan_begin", new JsonObject { ["data_type"] = "Float", ["value"] = "3.5" });
        var req = game.Sent.Last();
        Assert.Equal("begin_value_scan", req["cmd"]!.GetValue<string>());
        Assert.Equal("Exact", req["scan_type"]!.GetValue<string>());
        Assert.False(req.ContainsKey("deadline_ms"));   // the DLL default
        Assert.False(req.ContainsKey("deep"));
        Assert.Equal(50, req["page_size"]!.GetValue<int>());
    }

    [Fact]
    public async Task PathFromRoot_RefusesAnUnknownRoot()
    {
        var game = new FakeGame();
        var (_, r) = await Call(game, "read_path_from_root",
            new JsonObject { ["target"] = "0x1000", ["root_kind"] = "moon" });
        Assert.True(r.IsError);
        Assert.DoesNotContain("find_path_from_gworld", game.Commands);
    }

    // ── The scan gate ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnAnUnscannedGame_AnObjectToolSaysToScan_AndSendsNothingThatWouldAnswerEmpty()
    {
        // A proxy-loaded game's object commands answer with zeros: "nothing there" would be a wrong answer.
        var game = new FakeGame { ScannedGame = false };
        var (payload, result) = await Call(game, "read_search_objects", new JsonObject { ["query"] = "Pawn" });
        Assert.True(result.IsError);
        Assert.Contains("scan_start", payload!["error"]!.GetValue<string>());
        Assert.DoesNotContain("search_objects", game.Commands);

        game.ScanRunning = true;
        (payload, _) = await Call(game, "read_search_objects", new JsonObject { ["query"] = "Pawn" });
        Assert.Contains("still running", payload!["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task OnAScannedGame_TheGateAsksOnce()
    {
        var game = new FakeGame();
        await Call(game, "read_search_objects", new JsonObject { ["query"] = "A" });
        await Call(game, "read_search_objects", new JsonObject { ["query"] = "B" });
        Assert.Equal(1, game.Commands.Count(c => c == "get_pointers"));
    }

    [Fact]
    public async Task ReadMemory_NeedsNoScan()
    {
        var game = new FakeGame { ScannedGame = false };
        var (_, r) = await Call(game, "read_memory", new JsonObject { ["addr"] = "0x1000" });
        Assert.False(r.IsError);
        Assert.Contains("read_mem", game.Commands);
    }

    [Fact]
    public async Task ScanStart_OnAScannedGame_StartsNothing()
    {
        // Another client (the UI) has scanned: a second scan would clear the name cache it is using.
        var game = new FakeGame();
        var (payload, _) = await Call(game, "scan_start");
        Assert.True(payload!["already_scanned"]!.GetValue<bool>());
        Assert.DoesNotContain("trigger_scan", game.Commands);
    }

    [Fact]
    public async Task ScanStart_OnAnUnscannedGame_StartsTheScan_AndSaysToPoll()
    {
        var game = new FakeGame { ScannedGame = false };
        game.Answers["trigger_scan"] = _ => new JsonObject { ["started"] = true };
        var (payload, result) = await Call(game, "scan_start");
        Assert.Contains("trigger_scan", game.Commands);
        Assert.True(payload!["started"]!.GetValue<bool>());
        Assert.Contains("read_status", result.Note);
    }

    // ── What the tools hand back ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AReply_LosesItsEnvelopeAndTheUiTimings()
    {
        var game = new FakeGame();
        game.Answers["get_object"] = _ => new JsonObject { ["name"] = "PC" };
        var (payload, _) = await Call(game, "read_object", new JsonObject { ["addr"] = "0x1000" });
        var obj = payload!.AsObject();
        Assert.Equal("PC", obj["name"]!.GetValue<string>());
        Assert.False(obj.ContainsKey("id") || obj.ContainsKey("ok") || obj.ContainsKey("_t_read_ms"));
    }

    [Fact]
    public async Task ADllRefusal_IsAToolErrorCarryingTheDllsText()
    {
        var game = new FakeGame();
        game.Answers["find_object"] = _ => new JsonObject { ["ok"] = false, ["error"] = "Object not found" };
        var (payload, result) = await Call(game, "read_find_object", new JsonObject { ["path"] = "/Game/X" });
        Assert.True(result.IsError);
        Assert.Contains("Object not found", payload!["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task ANoticeOwedByTheConnection_RidesTheNextReply()
    {
        var game = new FakeGame { PendingNotice = "reconnected" };
        var (_, result) = await Call(game, "read_object", new JsonObject { ["addr"] = "0x1000" });
        Assert.Contains("reconnected", result.Note);
        Assert.Null(game.TakeNotice());
    }

    [Fact]
    public async Task ReadInstance_NameFilter_KeepsFieldsMatchingEveryWord()
    {
        var game = new FakeGame();
        game.Answers["walk_instance"] = _ => new JsonObject
        {
            ["fields"] = new JsonArray(
                new JsonObject { ["name"] = "AcknowledgedPawn" }, new JsonObject { ["name"] = "PawnHealth" },
                new JsonObject { ["name"] = "Health" }, new JsonObject { ["name"] = "MaxPawnHealth" }),
        };
        var (payload, _) = await Call(game, "read_instance",
            new JsonObject { ["addr"] = "0x1000", ["name_filter"] = "health pawn" });
        var names = payload!["fields"]!.AsArray().Select(f => f!["name"]!.GetValue<string>()).ToArray();
        Assert.Equal(["PawnHealth", "MaxPawnHealth"], names);
    }

    [Fact]
    public async Task ReadClass_NameFilter_ReachesTheClassFields()
    {
        var game = new FakeGame();
        game.Answers["walk_class"] = _ => new JsonObject
        {
            ["class"] = new JsonObject
            {
                ["name"] = "C",
                ["fields"] = new JsonArray(new JsonObject { ["name"] = "Speed" }, new JsonObject { ["name"] = "Mass" }),
            },
        };
        var (payload, _) = await Call(game, "read_class", new JsonObject { ["addr"] = "0x1000", ["name_filter"] = "spe" });
        Assert.Single(payload!["class"]!["fields"]!.AsArray());
    }

    [Fact]
    public async Task ListClasses_WithAFilter_AsksForEverything_ThenFiltersAndCuts()
    {
        // The DLL's own limit would cut the list before any name is looked at.
        var game = new FakeGame();
        game.Answers["list_classes"] = _ => new JsonObject
        {
            ["classes"] = new JsonArray(Enumerable.Range(0, 30).Select(i => (JsonNode)new JsonObject
            {
                ["class_name"] = i % 3 == 0 ? $"BP_Enemy{i}_C" : $"Other{i}",
            }).ToArray()),
        };
        var (payload, _) = await Call(game, "read_list_classes",
            new JsonObject { ["name_filter"] = "enemy", ["limit"] = 4 });
        Assert.Equal(Constants.MaxSearchCap, game.Sent.Last()["limit"]!.GetValue<int>());
        Assert.Equal(4, payload!["classes"]!.AsArray().Count);
        Assert.Equal(10, payload["matched"]!.GetValue<int>());
        Assert.True(payload["truncated"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ListClasses_WithoutAFilter_PassesTheLimitThrough()
    {
        var game = new FakeGame();
        game.Answers["list_classes"] = _ => new JsonObject { ["classes"] = new JsonArray() };
        await Call(game, "read_list_classes", new JsonObject { ["limit"] = 7 });
        Assert.Equal(7, game.Sent.Last()["limit"]!.GetValue<int>());
    }

    [Fact]
    public async Task ListEnums_IsCutHere_SinceTheDllReturnsThemAll()
    {
        var game = new FakeGame();
        game.Answers["list_enums"] = _ => new JsonObject
        {
            ["enums"] = new JsonArray(Enumerable.Range(0, 80).Select(i => (JsonNode)new JsonObject
            {
                ["name"] = "E" + i,
            }).ToArray()),
        };
        var (payload, _) = await Call(game, "read_list_enums");
        Assert.False(game.Sent.Last().ContainsKey("limit"));
        Assert.Equal(50, payload!["enums"]!.AsArray().Count);
        Assert.Equal(80, payload["matched"]!.GetValue<int>());
    }

    [Fact]
    public async Task ReadStatus_OnAnUnscannedGame_AddsTheScanStatus_AndSaysWhatToDo()
    {
        var game = new FakeGame { ScannedGame = false };
        var (payload, result) = await Call(game, "read_status");
        Assert.False(payload!["scanned"]!.GetValue<bool>());
        Assert.NotNull(payload["scan"]);
        Assert.Contains("scan_start", result.Note);
    }

    [Fact]
    public async Task ReadGames_ListsWithoutTouchingThePipe()
    {
        var game = new FakeGame();
        var (payload, _) = await Call(game, "read_games");
        Assert.Equal(4242, payload!["games"]![0]!["pid"]!.GetValue<int>());
        Assert.Empty(game.Sent);
    }

    // ── Arguments ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Args_ReadLenientlyInTypeAndStrictlyInValue()
    {
        var a = new McpArgs((JsonObject)JsonNode.Parse(
            """{"n":"12","b":"true","s":"x","list":"one","addr":140737488355328,"big":1e20}""")!);
        Assert.Equal(12, a.OptInt("n"));
        Assert.True(a.Bool("b", false));
        Assert.Equal(["one"], a.StrList("list")!);
        Assert.Equal("0x800000000000", a.OptAddr("addr"));
        Assert.Equal(5, a.Int("missing", 5, 1, 10));
        Assert.Equal(10, a.Int("n", 5, 1, 10));
        Assert.Throws<McpToolError>(() => a.OptInt("big"));
        Assert.Throws<McpToolError>(() => a.OptInt("s"));
        Assert.Throws<McpToolError>(() => a.ReqStr("missing"));
    }
}
