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
