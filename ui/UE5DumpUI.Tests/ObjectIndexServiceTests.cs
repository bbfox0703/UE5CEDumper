using System.Text;
using System.Text.Json;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [EXTPR-539-540-2026-10-02] D4: the object index, Dump All's opt-in second file. Every object the DLL lists,
/// one JSON line each with its GObjects index (Dumper-7's lesson: two files match by slot), between a meta line
/// that pairs it with the class dump and a summary line. Before writing, an estimate from the first page, so
/// the user can decline a file that may run to hundreds of megabytes.
/// </summary>
public class ObjectIndexServiceTests
{
    /// <summary>Pages through a fixed list as get_object_list does: Scanned counts slots, not rows.</summary>
    private sealed class FakeIndexDump : StubDumpService, IDumpService
    {
        public List<UObjectNode> Objects { get; } = new();
        public int Calls { get; private set; }
        public Action? OnPage { get; set; }

        Task<ObjectListResult> IDumpService.GetObjectIndexPageAsync(int offset, int limit, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            OnPage?.Invoke();
            var slice = Objects.Skip(offset).Take(limit).ToList();
            return Task.FromResult(new ObjectListResult { Total = Objects.Count, Scanned = slice.Count, Objects = slice });
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public void Advance(TimeSpan by) => _ticks += by.Ticks;
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private static UObjectNode Obj(int index, string name, string cls, string path, bool withIndex = true) => new()
    {
        Address = $"0x{0x1000 + index * 0x40:X}", Name = name, ClassName = cls, OuterAddr = "0x900",
        FullPath = path, Index = withIndex ? index : null,
    };

    private static EngineState State() => new()
    {
        UEVersion = 505, ModuleName = "Game-Win64-Shipping.exe", PeHash = "ABC123", ObjectCount = 5,
    };

    private static async Task<List<string>> GenerateLinesAsync(FakeIndexDump dump)
    {
        var ms = new MemoryStream();
        await ObjectIndexService.GenerateAsync(dump, State(), ms, dumperBuild: 3625, classDumpFile: "game-dump.jsonl",
            ct: TestContext.Current.CancellationToken);
        return Encoding.UTF8.GetString(ms.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static FakeIndexDump EveryKind()
    {
        // R2 (maintainer, 2026-10-02): every object, packages and type objects included, as Dumper-7 and RE-UE4SS.
        var dump = new FakeIndexDump();
        dump.Objects.Add(Obj(0, "/Script/Game", "Package", "/Script/Game"));
        dump.Objects.Add(Obj(1, "AHero", "Class", "/Script/Game.AHero"));
        dump.Objects.Add(Obj(2, "FHit", "ScriptStruct", "/Script/Game.FHit"));
        dump.Objects.Add(Obj(3, "Default__AHero", "AHero", "/Script/Game.Default__AHero"));
        dump.Objects.Add(Obj(4, "AHero_0", "AHero", "/Game/Maps/Main.Main:PersistentLevel.AHero_0"));
        return dump;
    }

    [Fact]
    public async Task Generate_WritesAMetaLine_EveryObject_AndASummary()
    {
        var lines = await GenerateLinesAsync(EveryKind());

        Assert.Equal(7, lines.Count);
        using (var meta = JsonDocument.Parse(lines[0]))
        {
            var m = meta.RootElement;
            Assert.Equal("meta", m.GetProperty("kind").GetString());
            Assert.Equal("objects", m.GetProperty("file").GetString());
            Assert.Equal("Game-Win64-Shipping.exe", m.GetProperty("module").GetString());
            Assert.Equal("ABC123", m.GetProperty("pe_hash").GetString());
            Assert.Equal("game-dump.jsonl", m.GetProperty("class_dump").GetString());
            Assert.Equal(3625, m.GetProperty("dumper_build").GetInt32());
        }
        var names = lines.Skip(1).Take(5).Select(l =>
        {
            using var d = JsonDocument.Parse(l);
            Assert.Equal("object", d.RootElement.GetProperty("kind").GetString());
            return d.RootElement.GetProperty("name").GetString();
        }).ToArray();
        Assert.Equal(new[] { "/Script/Game", "AHero", "FHit", "Default__AHero", "AHero_0" }, names);
        using var summary = JsonDocument.Parse(lines[^1]);
        Assert.Equal(5, summary.RootElement.GetProperty("objects_written").GetInt32());
        Assert.False(summary.RootElement.GetProperty("index_missing").GetBoolean());
    }

    [Fact]
    public async Task Generate_AnObjectLine_CarriesItsIndex_AddressClassOuterAndPath()
    {
        var lines = await GenerateLinesAsync(EveryKind());

        using var d = JsonDocument.Parse(lines[5]);
        var o = d.RootElement;
        Assert.Equal(4, o.GetProperty("index").GetInt32());
        Assert.Equal("0x1100", o.GetProperty("addr").GetString());
        Assert.Equal("AHero", o.GetProperty("class").GetString());
        Assert.Equal("0x900", o.GetProperty("outer").GetString());
        Assert.Equal("/Game/Maps/Main.Main:PersistentLevel.AHero_0", o.GetProperty("path").GetString());
    }

    [Fact]
    public async Task Generate_AnOlderDll_WritesNoIndex_RatherThanGuessingIt_AndSaysSo()
    {
        // The handler skips null and unnamed slots, so a row's place in the page is not its slot.
        var dump = new FakeIndexDump();
        dump.Objects.Add(Obj(0, "A", "Class", "/Script/Game.A", withIndex: false));
        dump.Objects.Add(Obj(1, "B", "Class", "/Script/Game.B", withIndex: false));

        var lines = await GenerateLinesAsync(dump);

        using (var d = JsonDocument.Parse(lines[1]))
            Assert.False(d.RootElement.TryGetProperty("index", out _));
        using var summary = JsonDocument.Parse(lines[^1]);
        Assert.True(summary.RootElement.GetProperty("index_missing").GetBoolean());
    }

    [Fact]
    public async Task Generate_EscapesNames_SoEveryLineIsJson()
    {
        var dump = new FakeIndexDump();
        dump.Objects.Add(Obj(0, "Odd \"name\" \\ ä", "Class", "/Game/Odd.Odd"));

        var lines = await GenerateLinesAsync(dump);

        using var d = JsonDocument.Parse(lines[1]);
        Assert.Equal("Odd \"name\" \\ ä", d.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Generate_WalksEveryPage()
    {
        var dump = new FakeIndexDump();
        int n = Constants.GObjectsWalkPageSize * 2 + 3;
        for (int i = 0; i < n; i++) dump.Objects.Add(Obj(i, $"O{i}", "Class", $"/Script/Game.O{i}"));

        var lines = await GenerateLinesAsync(dump);

        Assert.Equal(n + 2, lines.Count);
        Assert.Equal(3, dump.Calls);
    }

    [Fact]
    public async Task Estimate_ExtrapolatesTheFirstPage_ToTheWholePool()
    {
        var dump = new FakeIndexDump();
        int pages = 4, n = Constants.GObjectsWalkPageSize * pages;
        for (int i = 0; i < n; i++) dump.Objects.Add(Obj(i, $"Obj_{i % 1000:D4}", "Class", $"/Script/Game.Obj_{i % 1000:D4}"));
        var clock = new ManualClock();
        dump.OnPage = () => clock.Advance(TimeSpan.FromSeconds(2));

        var est = await ObjectIndexService.EstimateAsync(dump, TestContext.Current.CancellationToken, clock);

        Assert.Equal(1, dump.Calls);                       // one page, not the pool
        Assert.Equal(n, est.Objects);
        Assert.Equal(TimeSpan.FromSeconds(2 * pages), est.Duration);
        dump.OnPage = null;
        var ms = new MemoryStream();
        await ObjectIndexService.GenerateAsync(dump, State(), ms, 3625, "game-dump.jsonl",
            ct: TestContext.Current.CancellationToken);
        Assert.InRange(est.Bytes, ms.Length * 0.97, ms.Length * 1.03);
    }

    [Fact]
    public async Task Estimate_AnEmptyPool_IsZeroObjects()
    {
        var est = await ObjectIndexService.EstimateAsync(new FakeIndexDump(), TestContext.Current.CancellationToken);

        Assert.Equal(0, est.Objects);
        Assert.Equal(TimeSpan.Zero, est.Duration);
    }

    [Fact]
    public async Task Generate_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ObjectIndexService.GenerateAsync(EveryKind(), State(), new MemoryStream(), 3625, "x.jsonl", ct: cts.Token));
    }

    [Fact]
    public void FileName_SitsBesideTheClassDump()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dumps");
        Assert.Equal(Path.Combine(dir, "game-dump-1.objects.jsonl"),
            ObjectIndexService.FileNameFor(Path.Combine(dir, "game-dump-1.jsonl")));
    }
}
