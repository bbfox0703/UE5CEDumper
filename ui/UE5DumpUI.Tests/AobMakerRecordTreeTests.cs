using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [AOBM-PLUSCE-FIDELITY] +CE pushed every record through <c>CreateMemoryRecord</c>, which carries an address, a type
/// and two display flags -- so a bit-field bool arrived as its whole byte and an FString as its 8-byte data pointer.
/// These records now go through the plugin's record tree (<c>CreateRecordTreeBegin</c> / <c>Chunk</c> / <c>End</c>),
/// which carries the bit and the string; a plugin that cannot build them still gets the old record, and the status
/// says so.
/// </summary>
public class AobMakerRecordTreeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"UE5DumpTree_{Guid.NewGuid():N}");
    private readonly MockLoggingService _log = new();

    public AobMakerRecordTreeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static LiveFieldValue BitBool => new()
    {
        Name = "bIsDead", TypeName = "BoolProperty", Offset = 0x58, Size = 1, BoolBitIndex = 3,
        FieldAddress = "0x7FF600001458",
    };

    private static LiveFieldValue Str(string type = "StrProperty") => new()
    {
        Name = "PlayerName", TypeName = type, Offset = 0x60, Size = 16, FieldAddress = "0x7FF600001460",
    };

    // ---- the type descriptor: what the record really is, and its CreateMemoryRecord fallback ----

    [Fact]
    public void A_bit_field_bool_carries_its_bit_and_keeps_the_byte_as_the_fallback()
    {
        var t = CeXmlExportService.MapFieldToCeRecordType(BitBool);
        Assert.True(t.NeedsRecordTree);
        Assert.Equal(3, t.BitStart);
        Assert.Equal(1, t.BitLength);
        Assert.Equal(0, t.ValueType);   // CE vtByte: what a plugin without the tree still gets
    }

    [Fact]
    public void A_native_bool_and_scalars_stay_on_CreateMemoryRecord()
    {
        var nativeBool = new LiveFieldValue { Name = "b", TypeName = "BoolProperty", Size = 1, BoolBitIndex = -1 };
        Assert.False(CeXmlExportService.MapFieldToCeRecordType(nativeBool).NeedsRecordTree);
        Assert.False(CeXmlExportService.MapTypeNameToCeRecordType("IntProperty").NeedsRecordTree);
        Assert.False(CeXmlExportService.MapTypeNameToCeRecordType("FloatProperty").NeedsRecordTree);
        // FName stays its 4-byte index: the "UE FName" custom type is its own item, not this one.
        Assert.False(CeXmlExportService.MapTypeNameToCeRecordType("NameProperty").NeedsRecordTree);
    }

    [Theory]
    [InlineData("StrProperty", CeXmlExportService.CeStringKind.Utf16)]
    [InlineData("AnsiStrProperty", CeXmlExportService.CeStringKind.Ansi)]
    [InlineData("Utf8StrProperty", CeXmlExportService.CeStringKind.Utf8)]
    public void An_FString_family_field_is_a_string_with_the_pointer_as_the_fallback(
        string type, CeXmlExportService.CeStringKind kind)
    {
        var t = CeXmlExportService.MapFieldToCeRecordType(Str(type));
        Assert.True(t.NeedsRecordTree);
        Assert.Equal(kind, t.String);
        Assert.Equal(3, t.ValueType);   // CE vtQword, shown as hex: the data pointer, today's record
        Assert.True(t.ShowAsHex);
    }

    [Fact]
    public void A_row_that_knows_only_its_type_name_gets_the_string_too()
    {
        // Value Search, Snapshot and SPC rows carry a declared type name, not a LiveFieldValue.
        Assert.Equal(CeXmlExportService.CeStringKind.Utf16,
                     CeXmlExportService.MapTypeNameToCeRecordType("StrProperty").String);
    }

    // ---- the node ----

    [Fact]
    public void The_bit_node_is_a_Binary_record_at_the_field_byte()
    {
        var node = AobMakerActions.ToRecordNode("bIsDead", "0x7FF600001458",
                                                CeXmlExportService.MapFieldToCeRecordType(BitBool), 0);
        Assert.Equal("Binary", node.TypeKeyword);
        Assert.Equal("7FF600001458", node.Address);
        Assert.Equal(3, node.BitStart);
        Assert.Equal(1, node.BitLength);
        Assert.Null(node.Offsets);
        Assert.Equal(CeRecordTreeFeatures.BinaryBits, node.RequiredFeature);
    }

    [Fact]
    public void The_string_node_reads_through_the_data_pointer_at_the_export_length()
    {
        var node = AobMakerActions.ToRecordNode("PlayerName", "0x7FF600001460",
                                                CeXmlExportService.MapFieldToCeRecordType(Str()), 1024);
        Assert.Equal("String", node.TypeKeyword);
        Assert.Equal("7FF600001460", node.Address);
        Assert.Equal(new[] { 0 }, node.Offsets);   // FString { Data*, Num, Max }: Offsets [0] reads at Data
        Assert.Equal(1024, node.StringLength);
        Assert.True(node.Unicode);
        Assert.False(node.ShowAsHex);
        Assert.Null(node.RequiredFeature);
    }

    [Fact]
    public void An_unset_string_length_is_the_export_default_and_an_ansi_string_is_not_unicode()
    {
        var node = AobMakerActions.ToRecordNode("Tag", "7FF600001460",
                                                CeXmlExportService.MapFieldToCeRecordType(Str("AnsiStrProperty")), 0);
        Assert.Equal(Constants.DefaultCeStringLength, node.StringLength);
        Assert.False(node.Unicode);
    }

    // ---- the wire: a fake plugin that answers one request per connection, as the real one does ----

    private static string NobodysPipe() => $"UE5DumpTreeTest_{Guid.NewGuid():N}";

    /// <summary>Serve one reply per connection, in order, and keep each request's JSON.</summary>
    private static async Task<List<JsonElement>> ServeAsync(string pipe, string[] replies, CancellationToken ct)
    {
        var requests = new List<JsonElement>();
        foreach (var reply in replies)
        {
            using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                                                         PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync(ct);
            var len = new byte[4];
            await server.ReadExactlyAsync(len, ct);
            var body = new byte[BitConverter.ToUInt32(len, 0)];
            await server.ReadExactlyAsync(body, ct);
            requests.Add(JsonDocument.Parse(body).RootElement.Clone());
            var payload = Encoding.UTF8.GetBytes(reply);
            await server.WriteAsync(BitConverter.GetBytes((uint)payload.Length), ct);
            await server.WriteAsync(payload, ct);
            await server.FlushAsync(ct);
            server.WaitForPipeDrain();
        }
        return requests;
    }

    private const string Begin155 =
        "{\"type\":\"CreateRecordTreeBeginResult\",\"success\":true,\"batchId\":\"batch_7\"," +
        "\"features\":[\"bulk.plainGroupHeader\",\"bulk.binaryBits\",\"bulk.customType\",\"bulk.options\"]}";
    private const string Chunk1 = "{\"type\":\"CreateRecordTreeChunkResult\",\"success\":true,\"created\":1}";
    private const string End1 = "{\"type\":\"CreateRecordTreeEndResult\",\"success\":true,\"totalCreated\":1}";

    private static async Task<(RecordTreeResult Result, List<JsonElement> Requests)> PushAsync(
        string[] replies, CeRecordNode node)
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = NobodysPipe();
        var serving = ServeAsync(pipe, replies, ct);
        IAobMakerBridge bridge = new AobMakerBridgeService(new MockLoggingService(), pipe, 2000, _ => true);
        var result = await bridge.CreateRecordTreeAsync("UE5CEDumper +CE", new[] { node }, ct);
        var requests = await serving.WaitAsync(TimeSpan.FromSeconds(10), ct);
        return (result, requests);
    }

    private static CeRecordNode BitNode =>
        new("[UE5] bIsDead", "7FF600001458", "Binary", BitStart: 3, BitLength: 1);

    [Fact]
    public async Task A_push_is_Begin_Chunk_End_under_the_plugins_short_names()
    {
        var (result, requests) = await PushAsync(new[] { Begin155, Chunk1, End1 }, BitNode);

        Assert.Equal(RecordTreeOutcome.Created, result.Outcome);
        Assert.Equal(1, result.Created);
        Assert.Equal(3, requests.Count);

        Assert.Equal("CreateRecordTreeBegin", requests[0].GetProperty("type").GetString());
        Assert.Equal(1, requests[0].GetProperty("totalNodes").GetInt32());

        var chunk = requests[1];
        Assert.Equal("CreateRecordTreeChunk", chunk.GetProperty("type").GetString());
        Assert.Equal("batch_7", chunk.GetProperty("batchId").GetString());
        Assert.Equal(0, chunk.GetProperty("seq").GetInt32());
        var node = Assert.Single(chunk.GetProperty("nodes").EnumerateArray());
        // The plugin reads exactly these names and silently ignores any other: a "description" or an "address"
        // would build a nameless record at address 0 and still answer success.
        Assert.Equal("[UE5] bIsDead", node.GetProperty("desc").GetString());
        Assert.Equal("7FF600001458", node.GetProperty("addr").GetString());
        Assert.Equal("Binary", node.GetProperty("type").GetString());
        Assert.Equal(3, node.GetProperty("bitStart").GetInt32());
        Assert.Equal(1, node.GetProperty("bitLength").GetInt32());
        Assert.False(node.TryGetProperty("parent", out _));        // a root
        Assert.False(node.TryGetProperty("description", out _));
        Assert.False(node.TryGetProperty("address", out _));

        Assert.Equal("CreateRecordTreeEnd", requests[2].GetProperty("type").GetString());
        Assert.Equal("batch_7", requests[2].GetProperty("batchId").GetString());
    }

    [Fact]
    public async Task A_string_node_sends_the_hop_the_length_and_unicode()
    {
        var (result, requests) = await PushAsync(new[] { Begin155, Chunk1, End1 },
            new CeRecordNode("[UE5] PlayerName", "7FF600001460", "String", Offsets: new[] { 0 }, StringLength: 256,
                             Unicode: true));

        Assert.Equal(RecordTreeOutcome.Created, result.Outcome);
        var node = Assert.Single(requests[1].GetProperty("nodes").EnumerateArray());
        Assert.Equal("String", node.GetProperty("type").GetString());
        Assert.Equal(0, Assert.Single(node.GetProperty("offsets").EnumerateArray()).GetInt32());
        Assert.Equal(256, node.GetProperty("length").GetInt32());
        Assert.True(node.GetProperty("unicode").GetBoolean());
        Assert.False(node.GetProperty("hex").GetBoolean());
        Assert.False(node.TryGetProperty("bitStart", out _));
    }

    [Fact]
    public async Task A_plugin_without_the_record_tree_reads_as_unsupported_and_gets_no_chunk()
    {
        var (result, requests) = await PushAsync(
            new[] { "{\"type\":\"Error\",\"success\":false,\"message\":\"Unknown type\"}" }, BitNode);

        Assert.Equal(RecordTreeOutcome.Unsupported, result.Outcome);
        Assert.Single(requests);
    }

    [Fact]
    public async Task A_plugin_without_bits_closes_the_batch_before_any_record_exists()
    {
        // Build 153 and earlier: the tree, but no feature list. A Binary node there would be built without its bit
        // and answer success -- the byte-instead-of-bit record this item removes, now hidden behind a "success".
        const string begin153 = "{\"type\":\"CreateRecordTreeBeginResult\",\"success\":true,\"batchId\":\"batch_2\"}";
        const string end0 = "{\"type\":\"CreateRecordTreeEndResult\",\"success\":true,\"totalCreated\":0}";

        var (result, requests) = await PushAsync(new[] { begin153, end0 }, BitNode);

        Assert.Equal(RecordTreeOutcome.MissingFeature, result.Outcome);
        Assert.Equal(new[] { CeRecordTreeFeatures.BinaryBits }, result.MissingFeatures);
        Assert.Equal(2, requests.Count);
        Assert.Equal("CreateRecordTreeEnd", requests[1].GetProperty("type").GetString());
        Assert.Equal("batch_2", requests[1].GetProperty("batchId").GetString());
    }

    [Fact]
    public async Task A_failed_chunk_is_a_failure_with_the_plugins_reason()
    {
        const string chunkFail =
            "{\"type\":\"CreateRecordTreeChunkResult\",\"success\":false,\"created\":0,\"message\":\"node 0: bad\"}";
        const string end0 = "{\"type\":\"CreateRecordTreeEndResult\",\"success\":true,\"totalCreated\":0}";

        var (result, requests) = await PushAsync(new[] { Begin155, chunkFail, end0 }, BitNode);

        Assert.Equal(RecordTreeOutcome.Failed, result.Outcome);
        Assert.Equal(0, result.Created);
        Assert.Equal("node 0: bad", result.Message);
        Assert.Equal(3, requests.Count);   // End is still sent once, so the plugin frees the batch
    }

    // ---- the shared +CE push ----

    private static AobMakerStatus Up(ScriptedAobMakerBridge bridge)
    {
        var status = new AobMakerStatus(bridge);
        status.Apply(bridge.Available);
        return status;
    }

    private static RecordTreeResult Made(int n) => new(RecordTreeOutcome.Created, n, null, Array.Empty<string>());

    [Fact]
    public async Task PlusCE_on_a_bit_field_bool_sends_the_bit_not_the_byte()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true, TreeResult = Made(1) };

        var text = await AobMakerActions.AddRecordAsync(Up(bridge), "bIsDead", BitBool.FieldAddress,
                                                        CeXmlExportService.MapFieldToCeRecordType(BitBool), _log);

        Assert.Equal("Added to CE: bIsDead", text);
        Assert.Empty(bridge.Records);
        var node = Assert.Single(Assert.Single(bridge.Trees).Nodes);
        Assert.Equal(3, node.BitStart);
        Assert.Equal(PackedLayoutNotice.RecordNamePrefix + "bIsDead", node.Description);
    }

    [Fact]
    public async Task PlusCE_on_a_plugin_without_the_tree_falls_back_to_the_byte_and_says_so()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };   // TreeResult null: no record tree

        var text = await AobMakerActions.AddRecordAsync(Up(bridge), "bIsDead", BitBool.FieldAddress,
                                                        CeXmlExportService.MapFieldToCeRecordType(BitBool), _log);

        Assert.Equal(0, Assert.Single(bridge.Records).ValueType);
        Assert.Equal("Added to CE: bIsDead — " + AobMakerActions.AsByteText(), text);
    }

    [Fact]
    public async Task PlusCE_on_a_plugin_without_bits_falls_back_to_the_byte_and_says_so()
    {
        var bridge = new ScriptedAobMakerBridge
        {
            Available = true,
            TreeResult = new RecordTreeResult(RecordTreeOutcome.MissingFeature, 0, null,
                                              new[] { CeRecordTreeFeatures.BinaryBits }),
        };

        var text = await AobMakerActions.AddRecordAsync(Up(bridge), "bIsDead", BitBool.FieldAddress,
                                                        CeXmlExportService.MapFieldToCeRecordType(BitBool), _log);

        Assert.Single(bridge.Records);
        Assert.EndsWith(AobMakerActions.AsByteText(), text);
    }

    [Fact]
    public async Task PlusCE_on_an_FString_without_the_tree_falls_back_to_the_pointer_and_says_so()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };

        var text = await AobMakerActions.AddRecordAsync(Up(bridge), "PlayerName", Str().FieldAddress,
                                                        CeXmlExportService.MapFieldToCeRecordType(Str()), _log);

        var rec = Assert.Single(bridge.Records);
        Assert.Equal(3, rec.ValueType);
        Assert.True(rec.ShowAsHex);
        Assert.EndsWith(AobMakerActions.AsPointerText(), text);
    }

    [Fact]
    public async Task A_tree_that_CE_refused_is_a_refusal_not_a_silent_fallback()
    {
        // Cheat Engine was reached and made nothing: pushing the degraded record on top would hide the refusal.
        var bridge = new ScriptedAobMakerBridge
        {
            Available = true,
            TreeResult = new RecordTreeResult(RecordTreeOutcome.Failed, 0, "node 0: bad", Array.Empty<string>()),
        };

        var text = await AobMakerActions.AddRecordAsync(Up(bridge), "bIsDead", BitBool.FieldAddress,
                                                        CeXmlExportService.MapFieldToCeRecordType(BitBool), _log);

        Assert.Empty(bridge.Records);
        Assert.StartsWith("Cheat Engine refused the record for bIsDead", text);
    }

    [Fact]
    public async Task PlusCE_on_an_int_never_touches_the_tree()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true, TreeResult = Made(1) };

        await AobMakerActions.AddRecordAsync(Up(bridge), "HP", "0x7FF600001400",
                                             CeXmlExportService.MapTypeNameToCeRecordType("IntProperty"), _log);

        Assert.Empty(bridge.Trees);
        Assert.Single(bridge.Records);
    }

    // ---- the panels ----

    private LiveWalkerViewModel Walker(ScriptedAobMakerBridge bridge)
    {
        var vm = new LiveWalkerViewModel(new StubDumpService(), _log, new MockPlatformService(_dir), bridge);
        vm.ApplyAobMakerProbe(bridge.Available);
        return vm;
    }

    [Fact]
    public async Task LiveWalker_plus_CE_on_a_bit_field_bool_sends_the_bit()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true, TreeResult = Made(1) };
        var vm = Walker(bridge);

        await vm.AddFieldToCeCommand.ExecuteAsync(BitBool);

        var node = Assert.Single(Assert.Single(bridge.Trees).Nodes);
        Assert.Equal("Binary", node.TypeKeyword);
        Assert.Equal(3, node.BitStart);
        Assert.Empty(bridge.Records);
        Assert.Equal("Added to CE: bIsDead", vm.StatusText);
    }

    [Fact]
    public async Task LiveWalker_flat_batch_sends_an_FString_as_a_string_at_the_String_Len_setting()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true, TreeResult = Made(1) };
        var vm = Walker(bridge);
        vm.CeStringLength = 512;
        vm.SelectedField = Str();

        await vm.PushCeFieldToCeCommand.ExecuteAsync(null);

        var node = Assert.Single(Assert.Single(bridge.Trees).Nodes);
        Assert.Equal("String", node.TypeKeyword);
        Assert.Equal(512, node.StringLength);
        Assert.Empty(bridge.Records);
    }

    [Fact]
    public async Task InstanceFinder_plus_CE_on_an_FString_sends_a_string_record()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true, TreeResult = Made(1) };
        var vm = new InstanceFinderViewModel(new StubDumpService(), _log, new MockPlatformService(_dir), Up(bridge));

        await vm.AddFieldToCeCommand.ExecuteAsync(Str());

        var node = Assert.Single(Assert.Single(bridge.Trees).Nodes);
        Assert.Equal("String", node.TypeKeyword);
        Assert.Equal(new[] { 0 }, node.Offsets);
        Assert.Empty(bridge.Records);
    }
}
