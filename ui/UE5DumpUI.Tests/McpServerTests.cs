using System.Text;
using System.Text.Json.Nodes;
using UE5DumpUI.Core;
using UE5DumpUI.Mcp;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Stage D1. The MCP server's protocol layer, against the 2025-11-25 revision: the
/// handshake, the methods a tools-only server answers, the framing of every line it writes, and cancellation. The tools
/// themselves are McpToolsTests; the game connection McpGameLinkTests.
/// </summary>
public sealed class McpServerTests
{
    // ── Rig ─────────────────────────────────────────────────────────────────────────────────────────────

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Rig
    {
        public MemoryStream Out { get; } = new();
        public McpServer Server { get; }

        public Rig(params McpTool[] tools)
        {
            Server = new McpServer(tools.Length > 0 ? tools : [Echo()], new NullLog(), Out, "1.2.3.4");
        }

        public async Task<List<JsonObject>> Send(string line)
        {
            int before = Lines().Count;
            await Server.HandleLineAsync(line);
            return Lines().Skip(before).ToList();
        }

        public async Task<JsonObject> Request(string method, JsonObject? parameters = null, JsonNode? id = null)
        {
            var msg = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id ?? 1, ["method"] = method };
            if (parameters is not null) msg["params"] = parameters;
            var got = await Send(msg.ToJsonString());
            return Assert.Single(got);
        }

        public string Raw => Encoding.UTF8.GetString(Out.ToArray());

        public List<JsonObject> Lines()
        {
            string raw = Raw;
            if (raw.Length == 0) return [];
            Assert.EndsWith("\n", raw);
            return raw[..^1].Split('\n').Select(l => (JsonObject)JsonNode.Parse(l)!).ToList();
        }
    }

    private static McpTool Echo(string name = "read_echo") => new(name, "Echo", "Echoes its text.",
        McpSchema.Object(("text", McpSchema.Str("Text."), false)), ReadOnly: true,
        (a, _) => Task.FromResult(new McpToolResult(new JsonObject { ["text"] = a.Str("text") })));

    internal sealed class NullLog : ILoggingService
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Error(string message, Exception ex) { }
        public void Debug(string message) { }
        public void Info(string category, string message) { }
        public void Warn(string category, string message) { }
        public void Error(string category, string message) { }
        public void Error(string category, string message, Exception ex) { }
        public void Debug(string category, string message) { }
        public void StartProcessMirror(string processName) { }
        public void StopProcessMirror() { }
    }

    private static JsonObject Init(string version) => new()
    {
        ["protocolVersion"] = version,
        ["capabilities"] = new JsonObject(),
        ["clientInfo"] = new JsonObject { ["name"] = "test", ["version"] = "1" },
    };

    // ── Handshake ───────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2025-11-25", "2025-11-25")]
    [InlineData("2025-06-18", "2025-06-18")]
    [InlineData("2024-11-05", "2024-11-05")]
    [InlineData("2026-07-28", "2025-11-25")]   // the stateless revision: answered with the newest handshake revision
    [InlineData("1999-01-01", "2025-11-25")]
    public async Task Initialize_EchoesARevisionItSpeaks_AndAnswersItsNewestOtherwise(string asked, string answered)
    {
        var r = await new Rig().Request("initialize", Init(asked));
        Assert.Equal(answered, r["result"]!["protocolVersion"]!.GetValue<string>());
    }

    [Fact]
    public async Task Initialize_DeclaresTools_NamesTheServer_AndGivesInstructionsUnderTheClientCut()
    {
        var r = (await new Rig().Request("initialize", Init("2025-11-25")))["result"]!;
        Assert.False(r["capabilities"]!["tools"]!["listChanged"]!.GetValue<bool>());
        Assert.Equal(Constants.McpServerName, r["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Equal("1.2.3.4", r["serverInfo"]!["version"]!.GetValue<string>());
        string instructions = r["instructions"]!.GetValue<string>();
        // Claude Code cuts server instructions at 4,096 characters.
        Assert.InRange(instructions.Length, 200, 4096);
        Assert.Contains("read_status", instructions);
        Assert.Contains("scan_start", instructions);
    }

    [Fact]
    public async Task ServerDiscover_IsMethodNotFound_AndTheServerKeepsServing()
    {
        // 2026-07-28 clients probe with server/discover; Claude Code falls back to the handshake on an error, and a
        // server that exited instead would be lost.
        var rig = new Rig();
        var r = await rig.Request("server/discover", new JsonObject());
        Assert.Equal(McpErrorCode.MethodNotFound, r["error"]!["code"]!.GetValue<int>());
        Assert.Contains("initialize", r["error"]!["message"]!.GetValue<string>());
        var ping = await rig.Request("ping", id: 2);
        Assert.Empty((JsonObject)ping["result"]!);
    }

    // ── Methods and malformed input ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnUnknownMethod_IsMethodNotFound()
    {
        var r = await new Rig().Request("resources/list");
        Assert.Equal(McpErrorCode.MethodNotFound, r["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task ANotification_GetsNoResponse_EvenAnUnknownOne()
    {
        var rig = new Rig();
        Assert.Empty(await rig.Send("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
        Assert.Empty(await rig.Send("""{"jsonrpc":"2.0","method":"notifications/whatever","params":{}}"""));
        Assert.Empty(await rig.Send("""{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":99}}"""));
    }

    [Fact]
    public async Task ALineThatIsNotJson_IsAParseErrorWithANullId()
    {
        var r = Assert.Single(await new Rig().Send("{not json"));
        Assert.Equal(McpErrorCode.ParseError, r["error"]!["code"]!.GetValue<int>());
        Assert.True(r.ContainsKey("id"));
        Assert.Null(r["id"]);
    }

    [Fact]
    public async Task ABatch_IsAnInvalidRequest()
    {
        // JSON-RPC batching left MCP in 2025-06-18.
        var r = Assert.Single(await new Rig().Send("""[{"jsonrpc":"2.0","id":1,"method":"ping"}]"""));
        Assert.Equal(McpErrorCode.InvalidRequest, r["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task ANullId_IsAnInvalidRequest()
    {
        var r = Assert.Single(await new Rig().Send("""{"jsonrpc":"2.0","id":null,"method":"ping"}"""));
        Assert.Equal(McpErrorCode.InvalidRequest, r["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task AResponseFromTheClient_IsIgnored()
    {
        Assert.Empty(await new Rig().Send("""{"jsonrpc":"2.0","id":5,"result":{}}"""));
    }

    [Theory]
    [InlineData("\"abc\"")]
    [InlineData("42")]
    public async Task TheIdComesBackAsSent_StringOrNumber(string idJson)
    {
        var r = Assert.Single(await new Rig().Send($$"""{"jsonrpc":"2.0","id":{{idJson}},"method":"ping"}"""));
        Assert.Equal(idJson, r["id"]!.ToJsonString());
        Assert.Equal("2.0", r["jsonrpc"]!.GetValue<string>());
    }

    // ── Framing ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EveryLineWritten_IsOneMessage_EndedByLfAlone_EvenWithANewlineOrUnicodeInside()
    {
        var rig = new Rig();
        await rig.Request("tools/call", new JsonObject
        {
            ["name"] = "read_echo",
            ["arguments"] = new JsonObject { ["text"] = "line1\nline2\r\n統一 <TArray>" },
        });
        await rig.Request("tools/list", id: 2);
        string raw = rig.Raw;
        Assert.DoesNotContain("\r", raw);
        var lines = raw.Split('\n');
        Assert.Equal(3, lines.Length);   // two messages, each ended by LF
        Assert.Equal("", lines[2]);
        // Relaxed escaping: what an AI reads stays legible.
        Assert.Contains("統一 <TArray>", JsonNode.Parse(rig.Lines()[0]["result"]!["content"]![0]!["text"]!
            .GetValue<string>())!["text"]!.GetValue<string>());
        Assert.Contains("<TArray>", raw);
    }

    // ── tools/list and tools/call ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ToolsList_CarriesEachToolsSchemaAndAnnotations()
    {
        var write = new McpTool("scan_x", "X", "X.", McpSchema.Object(), ReadOnly: false,
            (_, _) => Task.FromResult(new McpToolResult(new JsonObject())), Idempotent: false);
        var tools = (await new Rig(Echo(), write).Request("tools/list"))["result"]!["tools"]!.AsArray();
        Assert.Equal(2, tools.Count);
        var echo = tools[0]!;
        Assert.Equal("read_echo", echo["name"]!.GetValue<string>());
        Assert.Equal("object", echo["inputSchema"]!["type"]!.GetValue<string>());
        Assert.True(echo["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.False(echo["annotations"]!["openWorldHint"]!.GetValue<bool>());
        Assert.False(tools[1]!["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.False(tools[1]!["annotations"]!["idempotentHint"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ToolsCall_ReturnsThePayloadAsOneJsonTextBlock()
    {
        var r = await new Rig().Request("tools/call", new JsonObject
        {
            ["name"] = "read_echo",
            ["arguments"] = new JsonObject { ["text"] = "hi" },
        });
        var content = r["result"]!["content"]!.AsArray();
        Assert.Single(content);
        Assert.Equal("text", content[0]!["type"]!.GetValue<string>());
        Assert.Equal("hi", JsonNode.Parse(content[0]!["text"]!.GetValue<string>())!["text"]!.GetValue<string>());
        Assert.False(r["result"]!.AsObject().ContainsKey("isError"));
    }

    [Fact]
    public async Task ToolsCall_AnUnknownTool_IsInvalidParams()
    {
        var r = await new Rig().Request("tools/call", new JsonObject { ["name"] = "read_nothing" });
        Assert.Equal(McpErrorCode.InvalidParams, r["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task ToolsCall_ArgumentsThatAreNotAnObject_AreInvalidParams()
    {
        var r = await new Rig().Request("tools/call", new JsonObject { ["name"] = "read_echo", ["arguments"] = "x" });
        Assert.Equal(McpErrorCode.InvalidParams, r["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task ToolsCall_AToolError_IsAResultWithIsError_SoTheModelSeesTheText()
    {
        var failing = new McpTool("read_fail", "F", "F.", McpSchema.Object(), ReadOnly: true,
            (_, _) => throw new McpToolError("pass class_name"));
        var crashing = new McpTool("read_crash", "C", "C.", McpSchema.Object(), ReadOnly: true,
            (_, _) => throw new InvalidOperationException("boom"));
        var rig = new Rig(failing, crashing);
        var r = (await rig.Request("tools/call", new JsonObject { ["name"] = "read_fail" }))["result"]!;
        Assert.True(r["isError"]!.GetValue<bool>());
        Assert.Contains("pass class_name", r["content"]![0]!["text"]!.GetValue<string>());
        var c = (await rig.Request("tools/call", new JsonObject { ["name"] = "read_crash" }, id: 2))["result"]!;
        Assert.True(c["isError"]!.GetValue<bool>());
        Assert.Contains("boom", c["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolsCall_ANote_IsASecondTextBlock()
    {
        var noted = new McpTool("read_noted", "N", "N.", McpSchema.Object(), ReadOnly: true,
            (_, _) => Task.FromResult(new McpToolResult(new JsonObject { ["a"] = 1 }, Note: "reconnected")));
        var content = (await new Rig(noted).Request("tools/call", new JsonObject { ["name"] = "read_noted" }))
            ["result"]!["content"]!.AsArray();
        Assert.Equal(2, content.Count);
        Assert.Equal("Note: reconnected", content[1]!["text"]!.GetValue<string>());
    }

    // ── Cancellation and shutdown ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ACancelledRequest_GetsNoResponse_AndItsTokenIsCancelled()
    {
        var entered = new TaskCompletionSource();
        var sawCancel = new TaskCompletionSource();
        var slow = new McpTool("read_slow", "S", "S.", McpSchema.Object(), ReadOnly: true, async (_, ct) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { sawCancel.SetResult(); throw; }
            return new McpToolResult(new JsonObject());
        });
        var rig = new Rig(slow);
        var call = rig.Server.HandleLineAsync(
            """{"jsonrpc":"2.0","id":"c1","method":"tools/call","params":{"name":"read_slow"}}""");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        await rig.Server.HandleLineAsync(
            """{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":"c1","reason":"test"}}""");
        await call.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        await sawCancel.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Empty(rig.Lines());
    }

    [Fact]
    public async Task ARequestWhoseIdIsStillRunning_IsRefused()
    {
        var release = new TaskCompletionSource();
        var slow = new McpTool("read_slow", "S", "S.", McpSchema.Object(), ReadOnly: true, async (_, ct) =>
        {
            await release.Task.WaitAsync(ct);
            return new McpToolResult(new JsonObject());
        });
        var rig = new Rig(slow);
        var first = rig.Server.HandleLineAsync(
            """{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"read_slow"}}""");
        var dup = Assert.Single(await rig.Send("""{"jsonrpc":"2.0","id":7,"method":"ping"}"""));
        Assert.Equal(McpErrorCode.InvalidRequest, dup["error"]!["code"]!.GetValue<int>());
        release.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5), Ct);
    }

    [Fact]
    public async Task RunAsync_ReturnsZeroAtEof_AfterAnsweringWhatItHadRead()
    {
        // A one-shot client pipes its requests in and closes stdin: what was read is still answered.
        var slowish = new McpTool("read_slowish", "S", "S.", McpSchema.Object(), ReadOnly: true, async (_, ct) =>
        {
            await Task.Delay(200, ct);
            return new McpToolResult(new JsonObject { ["done"] = true });
        });
        var rig = new Rig(slowish);
        var input = new MemoryStream(Encoding.UTF8.GetBytes(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\"}}\n"
            + "\n"   // a blank line is skipped
            + "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"read_slowish\"}}\n"));
        int code = await rig.Server.RunAsync(input, Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal(0, code);
        var lines = rig.Lines();
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l["id"]!.GetValue<int>() == 2 && l["result"] is not null);
    }

    [Fact]
    public void EofGrace_IsShortEnoughForAClientThatKillsTheServer()
    {
        // Claude Code ends the process 300 ms after closing stdin; a client that waits gets a bounded wait.
        Assert.InRange(McpServer.EofGrace, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(5));
    }
}
