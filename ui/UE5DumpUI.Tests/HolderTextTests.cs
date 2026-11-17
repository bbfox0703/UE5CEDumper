using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [MCP-WRITE-GRANTS-2026-11-14] §4.10. The words a gameplay card shows when someone other than this window holds its
/// feature, from get_holds (and See-through's claims). The text is en.axaml's, which the unit tests load.
/// </summary>
public sealed class HolderTextTests
{
    private static HoldEntry Hold(string kind, long? pid = null, bool background = false, bool you = false,
        bool active = true, bool pending = false)
        => new()
        {
            Hold = Constants.HoldGodMode, Active = active, Pending = pending,
            Holder = new HoldOwner { Kind = kind, Pid = pid, Background = background, You = you },
        };

    private static bool Running(long pid) => false;

    [Fact]
    public void NobodyElse_ShowsNothing()
    {
        Assert.Equal("", HolderText.For(null, Running));
        Assert.Equal("", HolderText.For(Hold("client", 77, you: true), Running));
        // The DLL names a holder even for a hold that ended: only one in force counts.
        Assert.Equal("", HolderText.For(Hold("client", 77, active: false), Running));
        Assert.Equal("", HolderText.For(Hold("none", active: false), Running));
    }

    [Fact]
    public void EachHolderKind_HasItsOwnWords()
    {
        Assert.Equal("Held by an AI session (pid 8048)", HolderText.For(Hold("client", 8048, background: true), Running));
        Assert.Equal("Held by an AI session", HolderText.For(Hold("client", null, background: true), Running));
        Assert.Equal("Held by another program (pid 77)", HolderText.For(Hold("client", 77), Running));
        Assert.Equal("Held by another program", HolderText.For(Hold("client"), Running));
        Assert.Equal("Held by Cheat Engine", HolderText.For(Hold("ce"), Running));
        // In force with no owner recorded: a CE script calling the exports, or a hold from an older DLL.
        Assert.Equal("Held by a program that recorded no owner", HolderText.For(Hold("none"), Running));
        // A debug-camera ON still on its way is held by its sender already.
        Assert.Equal("Held by an AI session (pid 9)",
            HolderText.For(Hold("client", 9, background: true, active: false, pending: true), Running));
    }

    [Fact]
    public void AHolderThatHasExited_IsSaidToHave()
    {
        // A restarted window's own earlier holds outlive it under the old pid.
        Assert.Equal("Held by a program that has exited (pid 77)", HolderText.For(Hold("client", 77), pid => pid == 77));
        Assert.Equal("Held by a program that has exited (pid 77)",
            HolderText.For(Hold("client", 77, background: true), pid => pid == 77));
        Assert.Equal("Held by another program (pid 78)", HolderText.For(Hold("client", 78), pid => pid == 77));
    }

    // [MCP-WRITE-GRANTS-2026-11-14] review: the DLL drops an ended AI session's ownership of the debug camera when its
    // OFF has to wait for a stalled game thread, and says the release is owed (DLL 3707+). Nobody holds it: the camera
    // is on its way off.
    [Fact]
    public void AnEndedAiSessionsCamera_ThatTheDllIsTurningOff_SaysSo()
    {
        var owed = new HoldEntry
        {
            Hold = Constants.HoldDebugCamera, Active = true, ReleaseOwed = true, Holder = new HoldOwner { Kind = "none" },
        };
        Assert.Equal("Turning off: the AI session that held it has ended", HolderText.For(owed, Running));
        Assert.Equal(Res.Get(HolderText.KeyReleaseOwed), HolderText.For(owed, Running));
    }

    // Review round 2: the DLL keeps the owed record until its next monitor tick, after someone else has taken the
    // camera (its OFF is then abandoned): the holder is named, not an ended session.
    [Theory]
    [InlineData("ce", null, false, false, "Held by Cheat Engine")]
    [InlineData("client", 77L, false, false, "Held by another program (pid 77)")]
    // Another client's ON in flight, its owner not yet recorded: an ON on its way is not the camera turning off.
    [InlineData("none", null, false, true, "Held by a program that recorded no owner")]
    public void AnOwedRelease_SomeoneTookOver_NamesThem(string kind, long? pid, bool background, bool pendingOn,
        string expected)
    {
        var hold = new HoldEntry
        {
            Hold = Constants.HoldDebugCamera, Active = !pendingOn, Pending = pendingOn, ReleaseOwed = true,
            Holder = new HoldOwner { Kind = kind, Pid = pid, Background = background },
        };
        Assert.Equal(expected, HolderText.For(hold, Running));
    }

    [Fact]
    public void TheWordsAreTheDictionarys()
    {
        Assert.Equal(Res.Format(HolderText.KeyAi, 8048L), HolderText.For(Hold("client", 8048, background: true), Running));
        Assert.Equal(Res.Get(HolderText.KeyCe), HolderText.For(Hold("ce"), Running));
        Assert.Equal(Res.Get(HolderText.KeyUnrecorded), HolderText.For(Hold("none"), Running));
    }

    [Theory]
    [InlineData(false, 2, true, true, "")]                      // off
    [InlineData(true, 1, false, true, "")]                      // this window's claim alone
    [InlineData(true, 2, false, true, "Held by another program or AI session")]
    [InlineData(true, 3, false, true, "Held by 2 other programs or AI sessions")]
    [InlineData(true, 2, true, true, "Held by Cheat Engine")]
    [InlineData(true, 3, true, true, "Held by Cheat Engine and another program or AI session")]
    [InlineData(true, 4, true, true, "Held by Cheat Engine and 2 other programs or AI sessions")]
    [InlineData(true, 1, false, false, "Held by another program or AI session")]   // the window has none
    // DLL 3698-3713 does not say whether the window holds one: never count its own claim as another's on a guess.
    [InlineData(true, 1, false, null, "")]
    [InlineData(true, 2, true, null, "Held by Cheat Engine")]
    public void SeeThrough_CountsTheClaimsThatAreNotThisWindows(bool active, int claims, bool ce, bool? you,
        string expected)
        => Assert.Equal(expected, HolderText.ForSeeThrough(new SeeThroughStatus
        {
            Active = active, Claims = claims, CeClaimed = ce, You = you,
        }));
}
