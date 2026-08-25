using System.Collections.Generic;
using System.Linq;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Pins the X9 fix: the competing-dumper-host banner must never list the game you
/// are connected to among its own "also loaded" competitors — including when the
/// DLL reports no PID (older builds send 0), where self is matched by module name.
/// </summary>
public class CompetingHostBannerTests
{
    private static GameProcessInfo Host(int pid, string name) =>
        new(pid, name, $@"C:\Games\{name}", IsUe: true, DumperLoaded: true);

    [Fact]
    public void PidKnown_ExcludesSelfByPid()
    {
        var banner = CompetingHostBanner.Build(
            new List<GameProcessInfo> { Host(100, "Game.exe"), Host(200, "Other.exe") },
            connectedPid: 100, connectedModule: "Game.exe");

        Assert.NotNull(banner);
        Assert.Equal("Game.exe (PID 100)", banner!.Value.ConnectedLabel);
        Assert.Equal(new[] { "Other.exe (PID 200)" }, banner.Value.Others.ToArray());
    }

    [Fact]
    public void NoPid_ExcludesSelfByModuleName()
    {
        var banner = CompetingHostBanner.Build(
            new List<GameProcessInfo> { Host(100, "Game.exe"), Host(200, "Other.exe") },
            connectedPid: 0, connectedModule: "Game.exe");

        Assert.NotNull(banner);
        Assert.Equal("Game.exe", banner!.Value.ConnectedLabel);   // no "(PID …)" when unknown
        // The BUG: with `p.Pid != 0` nothing was excluded, so self appeared here.
        Assert.DoesNotContain("Game.exe (PID 100)", banner.Value.Others);
        Assert.Equal(new[] { "Other.exe (PID 200)" }, banner.Value.Others.ToArray());
    }

    [Fact]
    public void NoPid_ModuleNameWithoutExtension_StillMatchesTheExeProcess()
    {
        var banner = CompetingHostBanner.Build(
            new List<GameProcessInfo> { Host(100, "Game.exe"), Host(200, "Other.exe") },
            connectedPid: 0, connectedModule: "Game");   // module reported without ".exe"

        Assert.NotNull(banner);
        Assert.Equal(new[] { "Other.exe (PID 200)" }, banner!.Value.Others.ToArray());
    }

    [Fact]
    public void NoPid_TwoInstancesSameName_ExcludesOnlyOne_KeepsTheGenuineCompetitor()
    {
        var banner = CompetingHostBanner.Build(
            new List<GameProcessInfo> { Host(100, "Game.exe"), Host(200, "Game.exe") },
            connectedPid: 0, connectedModule: "Game.exe");

        Assert.NotNull(banner);
        // Exactly one instance is self; the other is a real competitor that MUST survive.
        Assert.Single(banner!.Value.Others);
        Assert.Equal("Game.exe (PID 200)", banner.Value.Others[0]);
    }

    [Fact]
    public void ZeroOrOneHost_ReturnsNull_NoAmbiguityToWarnAbout()
    {
        Assert.Null(CompetingHostBanner.Build(new List<GameProcessInfo>(), 100, "Game.exe"));
        Assert.Null(CompetingHostBanner.Build(
            new List<GameProcessInfo> { Host(100, "Game.exe") }, 100, "Game.exe"));
    }

    [Theory]
    [InlineData("Game.exe", "Game.exe", true)]
    [InlineData("GAME.EXE", "game.exe", true)]     // case-insensitive
    [InlineData("Game.exe", "Game", true)]         // extension on one side only
    [InlineData("Game", "Game.exe", true)]
    [InlineData("Game.exe", "Other.exe", false)]   // negative control
    [InlineData("", "Game.exe", false)]
    public void NameMatchesModule(string procName, string module, bool expected)
        => Assert.Equal(expected, CompetingHostBanner.NameMatchesModule(procName, module));
}
