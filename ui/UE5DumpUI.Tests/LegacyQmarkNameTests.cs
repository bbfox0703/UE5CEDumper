using System.IO;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [PATH-UI-LEGACY-QMARK] A DLL built before [PATH-MODULE-NAME-UTF8] (a proxy not yet updated, a .CT injection)
/// still reports every non-ASCII character of the exe name as '?'. '?' is never legal in a Windows file name, so a
/// name that contains one is LOSSY: it names no file, and two different games can report the same one. The UI must
/// not key anything on it.
/// </summary>
public class LegacyQmarkNameTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ue5-qmark", Guid.NewGuid().ToString("N"));
    public LegacyQmarkNameTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    // ── the teleport coordinate library ──

    [Theory]
    [InlineData("???.exe")]                     // gave "" -- and rows were "Added" but never saved
    [InlineData("???-Win64-Shipping.exe")]      // gave "-win64-shipping" -- shared by every such game
    [InlineData("Game?-Win64-Shipping.exe")]    // gave "game_-win64-shipping" -- shared with Game®, Game™ ...
    public void CoordLibraryKey_RefusesALossyName(string module)
        => Assert.Equal("", CoordinateLibraryStore.KeyFor(module));

    [Fact]
    public void CoordLibraryKey_KeepsARealNonAsciiName()
        => Assert.Equal("ゲーム-win64-shipping", CoordinateLibraryStore.KeyFor("ゲーム-Win64-Shipping.exe"));

    [Theory]
    [InlineData("???.exe")]
    [InlineData("???-Win64-Shipping.exe")]
    public void Teleport_ALossyName_DisablesTheLibrary_SaysWhy_AndSavesNothing(string module)
    {
        var platform = new MockPlatformService(_dir);
        var vm = new TeleportViewModel(new StubDumpService(), new MockLoggingService(), platform,
            coordStore: new CoordinateLibraryStore(platform));

        vm.LoadCoordLibraryForGame(module);
        Assert.Contains("unavailable", vm.CoordStatus, StringComparison.OrdinalIgnoreCase);

        vm.CoordX = 1; vm.CoordY = 2; vm.CoordZ = 3;
        vm.AddCoordFromFieldsCommand.Execute(null);

        Assert.Empty(vm.CoordEntries);                                  // not an in-memory row that dies on restart
        Assert.Contains("Nothing was saved", vm.CoordStatus);
        Assert.Empty(Directory.GetFiles(_dir, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void Teleport_ARealName_StillSaves()
    {
        var platform = new MockPlatformService(_dir);
        var vm = new TeleportViewModel(new StubDumpService(), new MockLoggingService(), platform,
            coordStore: new CoordinateLibraryStore(platform));

        vm.LoadCoordLibraryForGame("ゲーム-Win64-Shipping.exe");
        vm.CoordX = 1; vm.CoordY = 2; vm.CoordZ = 3;
        vm.AddCoordFromFieldsCommand.Execute(null);

        Assert.Single(vm.CoordEntries);
        Assert.DoesNotContain("unavailable", vm.CoordStatus, StringComparison.OrdinalIgnoreCase);
    }

    // ── the skeptic review (wf_6ba4bc83-14d) ──

    private TeleportViewModel Vm(out CoordinateLibraryStore store)
    {
        var platform = new MockPlatformService(_dir);
        store = new CoordinateLibraryStore(platform);
        return new TeleportViewModel(new StubDumpService(), new MockLoggingService(), platform, coordStore: store);
    }

    private static UE5DumpUI.Models.CoordinateLibraryFile TwoRows() => new()
    {
        Entries =
        {
            new UE5DumpUI.Models.CoordEntry { Uid = "a", Label = "Chest", Map = "Map01", X = 1, Y = 2, Z = 3 },
            new UE5DumpUI.Models.CoordEntry { Uid = "b", Label = "Boss", Map = "Map01", X = 4, Y = 5, Z = 6 },
        },
    };

    [Fact]
    public void Key_ANameWithNoFileNameCharacters_StillGetsAStableKey()
    {
        // (QM-7) '★.exe' keyed to '' under the NEW DLL too, and the text blamed an old DLL: an update that loops.
        string star = CoordinateLibraryStore.KeyFor("★.exe");
        Assert.NotEqual("", star);
        Assert.Equal(star, CoordinateLibraryStore.KeyFor("★.EXE"));
        Assert.NotEqual(star, CoordinateLibraryStore.KeyFor("☆.exe"));
    }

    [Theory]
    [InlineData("Pokémon.exe", "pok_mon")]        // what the older DLL's 'Pok?mon.exe' keyed to
    [InlineData("Pok?mon.exe", "pok_mon")]        // the older DLL's name itself
    [InlineData("ゲーム-Win64-Shipping.exe", "-win64-shipping")]
    [InlineData("Plain-Win64-Shipping.exe", "")]  // an ASCII name never had another key
    public void LegacyKey_IsWhatAnOlderDllsNameKeyedTo(string module, string expected)
        => Assert.Equal(expected, CoordinateLibraryStore.LegacyKeyFor(module));

    [Fact]
    public void Teleport_ALibrarySavedUnderTheOlderDllsKey_IsCarriedOver_AndTheOldFileKept()
    {
        // (QM-1 / T6) Before: 'Pok?mon.exe' -> 'pok_mon' saved real rows. After the fix the old DLL is refused and the
        // new DLL keys 'pokémon' -- the rows were stranded, with no notice.
        var vm = Vm(out var store);
        store.Save("pok_mon", TwoRows());
        string legacyPath = store.FilePathFor("pok_mon");
        string before = File.ReadAllText(legacyPath);

        vm.LoadCoordLibraryForGame("Pokémon.exe");

        Assert.Equal(2, vm.CoordEntries.Count);
        Assert.Contains(Path.GetFileName(legacyPath), vm.CoordStatus);
        Assert.True(File.Exists(store.FilePathFor(CoordinateLibraryStore.KeyFor("Pokémon.exe"))), "not saved under the new key");
        Assert.Equal(before, File.ReadAllText(legacyPath));                 // left as it was
    }

    [Fact]
    public void Teleport_ACarriedOverLibrary_ClearedByTheUser_StaysCleared()
    {
        // (second review, COORD-CARRY-RESURRECT / T-QMARK-RESURRECT, MED) The carry-over's own status says 'use Clear
        // all' if the rows belong to another game. Clear all deletes the main file -- so the next connect saw 'no
        // library under the real name' and carried the legacy rows over AGAIN: the [A1-COORD-RESURRECT] shape.
        var vm = Vm(out var store);
        store.Save("pok_mon", TwoRows());
        vm.LoadCoordLibraryForGame("Pokémon.exe");
        Assert.Equal(2, vm.CoordEntries.Count);

        store.Delete(CoordinateLibraryStore.KeyFor("Pokémon.exe"));   // what Clear all does
        vm.LoadCoordLibraryForGame("Pokémon.exe");

        Assert.Empty(vm.CoordEntries);
    }

    [Fact]
    public void Key_TheHashKeyIsStableAcrossProcesses()
    {
        // (second review, T-QMARK-HASH-STABILITY) Within one process, string.GetHashCode would pass the other test and
        // strand the library on every restart. FNV-1a over U+2605, computed independently.
        Assert.Equal("name-003b8f40", CoordinateLibraryStore.KeyFor("★.exe"));
    }

    [Fact]
    public void Teleport_UnderTheOlderDll_TheRefusalNamesTheEarlierLibrary()
    {
        var vm = Vm(out var store);
        store.Save("pok_mon", TwoRows());
        vm.LoadCoordLibraryForGame("Pok?mon.exe");
        Assert.Contains("unavailable", vm.CoordStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFileName(store.FilePathFor("pok_mon")), vm.CoordStatus);
    }

    [Fact]
    public void Teleport_TheRefusalTellsTheWholeRemedy()
    {
        // (QM-5) Re-injecting into the running game reattaches to the OLD DLL; Update All cannot replace a proxy the
        // running game holds. The game must be closed first.
        var vm = Vm(out _);
        vm.LoadCoordLibraryForGame("???-Win64-Shipping.exe");
        Assert.Contains("Close the game", vm.CoordStatus);
    }

    [Fact]
    public void Teleport_TheRefusalDoesNotOutliveItsCause()
    {
        // (QM-2) After the remedy (update, reconnect), the old refusal naming '???-…' stayed on screen.
        var vm = Vm(out _);
        vm.LoadCoordLibraryForGame("???-Win64-Shipping.exe");
        vm.LoadCoordLibraryForGame("ゲーム-Win64-Shipping.exe");
        Assert.DoesNotContain("unavailable", vm.CoordStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Teleport_BeforeAnyGameIsConnected_AddingIsRefused()
    {
        // (QM-4) No key yet: 'Added …' was shown, nothing was saved, and the first connect cleared the row.
        var vm = Vm(out _);
        vm.CoordX = 1;
        vm.AddCoordFromFieldsCommand.Execute(null);
        Assert.Empty(vm.CoordEntries);
        Assert.Contains("connect", vm.CoordStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nothing was saved", vm.CoordStatus);
    }

    [Fact]
    public void Teleport_ApplyImport_IsRefusedWhileUnavailable_AndThePreviewSaysSo()
    {
        // (T5 / QM-6) The preview said 'Press Apply to commit' while Apply could only be refused.
        var vm = Vm(out _);
        vm.LoadCoordLibraryForGame("???-Win64-Shipping.exe");
        vm.BuildImportPreview(UE5DumpUI.Services.CoordCsvCodec.Parse("label,map,x,y,z\nChest 1,Map01,1,2,3\n"), "t.csv");
        Assert.Contains("unavailable", vm.CoordImportPreview, StringComparison.OrdinalIgnoreCase);

        vm.ApplyCoordImportCommand.Execute(null);
        Assert.Empty(vm.CoordEntries);
        Assert.Contains("Nothing was saved", vm.CoordStatus);
        Assert.Empty(Directory.GetFiles(_dir, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void Teleport_ARealName_WritesTheFile()
    {
        // (T5) 'StillSaves' checked only the in-memory list.
        var vm = Vm(out var store);
        vm.LoadCoordLibraryForGame("ゲーム-Win64-Shipping.exe");
        vm.CoordX = 1;
        vm.AddCoordFromFieldsCommand.Execute(null);
        Assert.True(File.Exists(store.FilePathFor(CoordinateLibraryStore.KeyFor("ゲーム-Win64-Shipping.exe"))));
    }

    [Theory]
    [InlineData("???-Win64-Shipping.exe", "Fortnite-Win64-Shipping.exe")]   // '?' stood for non-ASCII: never 'F'
    [InlineData("???.exe", "Foo.exe")]
    [InlineData("???.exe", "ゲーム-Win64-Shipping.exe")]                     // a different length
    public void DumpIdentity_ALegacyNameThatCannotBeTheLiveOne_IsRefused(string file, string live)
    {
        // (QM-3) Any '?' name used to mean 'unknown', so a legacy dump was never refused -- not even against an
        // ASCII name the old DLL would have reported verbatim.
        var (refused, _) = DumpExplorerViewModel.JudgeIdentity(file, "A", live, "B");
        Assert.True(refused);
    }

    [Fact]
    public void DumpIdentity_ALegacyNameThatCanBeTheLiveOne_IsNotRefused()
    {
        var (refused, caveat) = DumpExplorerViewModel.JudgeIdentity("???.exe", "A", "游戏的.exe", "B");
        Assert.False(refused);
        Assert.Contains("could not confirm", caveat);
    }

    // ── the confirmed-working proxy record ──

    [Fact]
    public void ConfirmedProxy_IsNeverRecordedUnderALossyName()
    {
        var vm = new ProxyDeployViewModel(
            new ProxyDeployService(new MockLoggingService(), new MockPlatformService(_dir)), new MockLoggingService());

        vm.RecordConfirmedProxy("Game?-Win64-Shipping.exe", "version.dll");
        Assert.Empty(vm.ConfirmedProxyByExe);

        vm.RecordConfirmedProxy("Game™-Win64-Shipping.exe", "version.dll");   // the real name is fine
        Assert.Single(vm.ConfirmedProxyByExe);
    }

    // ── the Dump Explorer's wrong-game gate ──

    [Fact]
    public void DumpIdentity_TwoLossyNames_AreNotProofOfTheSameGame()
    {
        var (refused, caveat) = DumpExplorerViewModel.JudgeIdentity("???.exe", "AAAA", "???.exe", "BBBB");
        Assert.False(refused);
        Assert.Contains("could not confirm", caveat);
        Assert.DoesNotContain("same game", caveat);                    // was "Different build of the same game"
    }

    [Fact]
    public void DumpIdentity_ALegacyDumpOfThisVeryExe_IsNotRefused()
    {
        // Dump taken through an old DLL ('???'), live game on a fixed DLL (real name), same exe bytes.
        var (refused, caveat) = DumpExplorerViewModel.JudgeIdentity("???.exe", "AAAA", "ゲーム.exe", "aaaa");
        Assert.False(refused);
        Assert.Equal("", caveat);
    }

    [Fact]
    public void DumpIdentity_ALegacyName_WithADifferentBuild_CannotConfirm()
    {
        var (refused, caveat) = DumpExplorerViewModel.JudgeIdentity("???.exe", "AAAA", "ゲーム.exe", "BBBB");
        Assert.False(refused);
        Assert.Contains("could not confirm", caveat);
    }

    [Theory]
    [InlineData("Foo.exe", "A", "Bar.exe", "B", true, "")]
    [InlineData("Foo.exe", "A", "foo.EXE", "B", false, "Different build of the same game")]
    [InlineData("Foo.exe", "A", "Foo.exe", "A", false, "")]
    [InlineData("", "", "Foo.exe", "A", false, "carries no game identity")]
    public void DumpIdentity_RealNames_KeepTheirRules(string fm, string fp, string lm, string lp, bool refuse,
        string caveatPart)
    {
        var (refused, caveat) = DumpExplorerViewModel.JudgeIdentity(fm, fp, lm, lp);
        Assert.Equal(refuse, refused);
        if (caveatPart.Length == 0) Assert.Equal("", caveat); else Assert.Contains(caveatPart, caveat);
    }

    // ── the export file-name suggestions ──

    [Theory]
    [InlineData("Game?-Win64-Shipping.exe", "game", "Game_-Win64-Shipping")]   // '?' is a wildcard in the dialog
    [InlineData("My Game.exe", "game", "My Game")]                             // spaces are legal, and kept
    [InlineData("ゲーム-Win64-Shipping.exe", "game", "ゲーム-Win64-Shipping")]
    [InlineData("", "game", "game")]
    public void ExportSuggestion_IsALegalFileName(string module, string fallback, string expected)
        => Assert.Equal(expected, UE5DumpUI.ViewModels.MainWindowViewModel.SuggestedExportStem(module, fallback));
}
