using System.Collections.Generic;
using Avalonia;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Unit tests for the pure maximize/restore snapshot state machine that backs
/// <see cref="Views.ManagedDialogWindow"/> (the Find Func / Prop / instance-picker
/// dialogs). Verifies the core bug fix: after a maximize→restore, the window's
/// pre-maximize NORMAL rect is what gets re-applied — even though the Windows
/// property-change order stashes the maximized dimensions first.
/// </summary>
public class WindowRestoreStateTests
{
    [Fact]
    public void Unseeded_HasNoRestoreRect()
    {
        var s = new WindowRestoreState();
        Assert.False(s.Seeded);
        Assert.False(s.TryGetRestoreRect(out _, out _, out _));
    }

    [Fact]
    public void Seed_CapturesNormalRect()
    {
        var s = new WindowRestoreState();
        s.Seed(new PixelPoint(100, 200), 860, 520);

        Assert.True(s.Seeded);
        Assert.Equal(new PixelPoint(100, 200), s.NormalPosition);
        Assert.Equal(860, s.NormalWidth);
        Assert.Equal(520, s.NormalHeight);

        Assert.True(s.TryGetRestoreRect(out var pos, out var w, out var h));
        Assert.Equal(new PixelPoint(100, 200), pos);
        Assert.Equal(860, w);
        Assert.Equal(520, h);
    }

    [Fact]
    public void Commit_WhileNormal_PromotesPendingGeometry()
    {
        var s = new WindowRestoreState();
        s.Seed(new PixelPoint(0, 0), 800, 500);

        // User drags + resizes the still-Normal window.
        s.NotePosition(new PixelPoint(300, 150));
        s.NoteSize(900, 600);
        s.Commit(isNormalNow: true);

        Assert.Equal(new PixelPoint(300, 150), s.NormalPosition);
        Assert.Equal(900, s.NormalWidth);
        Assert.Equal(600, s.NormalHeight);
    }

    [Fact]
    public void Commit_AfterFlipToMaximized_IsAbandoned()
    {
        var s = new WindowRestoreState();
        s.Seed(new PixelPoint(120, 80), 860, 520);

        // The Windows quirk: Width/Height arrive as the MAXIMIZED dims while the
        // window still reads Normal, so they get stashed...
        s.NoteSize(2560, 1380);
        s.NotePosition(new PixelPoint(0, 0));
        // ...but by commit time WindowState has flipped to Maximized -> abandon.
        s.Commit(isNormalNow: false);

        // The pre-maximize NORMAL rect must survive untouched.
        Assert.Equal(new PixelPoint(120, 80), s.NormalPosition);
        Assert.Equal(860, s.NormalWidth);
        Assert.Equal(520, s.NormalHeight);
    }

    [Fact]
    public void MaximizeThenRestore_ReappliesOriginalRect()
    {
        var s = new WindowRestoreState();
        s.Seed(new PixelPoint(120, 80), 860, 520);   // opened, Normal

        // --- maximize: poisoned stash gets abandoned at commit ---
        s.NoteSize(2560, 1380);
        s.NotePosition(new PixelPoint(0, 0));
        s.Commit(isNormalNow: false);

        // --- restore: the rect we re-apply is the original normal one ---
        Assert.True(s.TryGetRestoreRect(out var pos, out var w, out var h));
        Assert.Equal(new PixelPoint(120, 80), pos);
        Assert.Equal(860, w);
        Assert.Equal(520, h);
    }

    [Fact]
    public void NoteSize_IgnoresNonPositiveValues()
    {
        var s = new WindowRestoreState();
        s.Seed(new PixelPoint(0, 0), 800, 500);

        s.NoteSize(0, -1);          // transient layout noise
        s.Commit(isNormalNow: true);

        Assert.Equal(800, s.NormalWidth);
        Assert.Equal(500, s.NormalHeight);
    }

    // ── Position acceptability guard (the position twin of NoteSize's >0 guard) ──────

    private static readonly IReadOnlyList<(int, int, int, int)> SinglePrimary =
        new[] { (0, 0, 1920, 1080) };

    [Fact]
    public void NotePosition_OffScreen_IsRejected()
    {
        // Direct BUG 1 regression: a transition transient at a far-off top-left must not
        // poison the stash. Without the guard, Commit would promote it into the snapshot.
        var s = new WindowRestoreState();
        s.SetScreens(SinglePrimary);
        s.Seed(new PixelPoint(100, 200), 1400, 900);

        s.NotePosition(new PixelPoint(-5000, -5000));   // off every monitor
        s.Commit(isNormalNow: true);

        Assert.Equal(new PixelPoint(100, 200), s.NormalPosition); // unchanged
    }

    [Fact]
    public void NotePosition_OnScreenCorner_IsAccepted()
    {
        // The guard must NOT over-reject: (0,0) is a legitimate on-screen top-left, so a
        // user who parks the window in the corner is honoured. (The "jumps to 0,0" bug is
        // fixed by the deferred re-apply ordering, not by rejecting (0,0).)
        var s = new WindowRestoreState();
        s.SetScreens(SinglePrimary);
        s.Seed(new PixelPoint(100, 200), 1400, 900);

        s.NotePosition(new PixelPoint(0, 0));
        s.Commit(isNormalNow: true);

        Assert.Equal(new PixelPoint(0, 0), s.NormalPosition);
    }

    [Fact]
    public void Commit_OffScreenPendingPosition_KeepsPositionPromotesSize()
    {
        // The position and size guards are independent: a monitor unplugged between stash
        // and commit leaves an off-screen pending position, which Commit must reject while
        // still promoting the (valid) pending size.
        var s = new WindowRestoreState();
        s.Seed(new PixelPoint(100, 200), 860, 520);     // no screens yet -> all accepted

        s.NotePosition(new PixelPoint(2000, 100));       // on the now-removed 2nd monitor
        s.NoteSize(900, 600);
        s.SetScreens(SinglePrimary);                     // 2nd monitor gone
        s.Commit(isNormalNow: true);

        Assert.Equal(new PixelPoint(100, 200), s.NormalPosition); // off-screen pos rejected
        Assert.Equal(900, s.NormalWidth);                        // size still promoted
        Assert.Equal(600, s.NormalHeight);
    }

    [Fact]
    public void OnRestoreReapplied_ResetsPendingToCommittedNormal()
    {
        // The core BUG 1 regression at the state-machine level. A maximize leaves the
        // maximized dims + origin poisoning the pending stash. The caller defers the
        // restore re-apply and then calls OnRestoreReapplied so a LATER commit (the
        // Background-deferred commit firing while genuinely Normal) can't promote the
        // poisoned stash into the snapshot.
        var s = new WindowRestoreState();
        s.Seed(new PixelPoint(100, 200), 860, 520);

        // maximize: pending poisoned, commit abandoned because not Normal at commit time.
        s.NoteSize(2560, 1380);
        s.NotePosition(new PixelPoint(0, 0));
        s.Commit(isNormalNow: false);

        // restore: caller re-applied the rect to the live window, then re-seeds the stash.
        s.OnRestoreReapplied();

        // A stray commit that NOW reads Normal must keep the original normal rect, not the
        // maximized poison.
        s.Commit(isNormalNow: true);

        Assert.Equal(new PixelPoint(100, 200), s.NormalPosition);
        Assert.Equal(860, s.NormalWidth);
        Assert.Equal(520, s.NormalHeight);
    }

    [Fact]
    public void SetScreens_Null_TreatedAsEmpty_Accepts()
    {
        var s = new WindowRestoreState();
        s.SetScreens(null!);                 // defensive: treated as "no screens" -> accept
        s.Seed(new PixelPoint(0, 0), 800, 500);

        s.NotePosition(new PixelPoint(-9000, -9000));
        s.Commit(isNormalNow: true);

        Assert.Equal(new PixelPoint(-9000, -9000), s.NormalPosition);
    }

    // ── [W3-DIP-PIXELS] the guard is handed PHYSICAL pixels, as WindowPlacement requires ──────
    // WindowPlacement's header: "All coordinates are PHYSICAL pixels". The position and the screens are; the stash's
    // width/height are Avalonia Width/Height -- DIPs. MainWindow got the AF21 unit fix and this newer twin never did, so at
    // 225% a 1,124-DIP dialog was judged by a 1,124 px rect instead of 2,529 px, and a legitimately placed position was
    // discarded. The AF21 geometry, so this measures the same case: 3840 px wide at 225%, x = -1707.
    private static readonly IReadOnlyList<(int, int, int, int)> Af21Screen = new[] { (0, 0, 3840, 2400) };

    [Fact]
    public void AtHiDpi_APositionReachableInPhysicalPixels_IsKept()
    {
        var s = new WindowRestoreState();
        s.SetScreens(Af21Screen);
        s.SetScale(2.25);
        s.Seed(new PixelPoint(200, 146), 1124, 900);

        s.NotePosition(new PixelPoint(-1707, 146));   // 822 physical px of it on screen
        s.Commit(isNormalNow: true);

        Assert.Equal(new PixelPoint(-1707, 146), s.NormalPosition);
    }

    [Fact]
    public void AtHiDpi_AGenuinelyOffScreenPosition_IsStillRejected()
    {
        // The control: the scale widens the rect to its real size; it does not accept everything.
        var s = new WindowRestoreState();
        s.SetScreens(Af21Screen);
        s.SetScale(2.25);
        s.Seed(new PixelPoint(200, 146), 1124, 900);

        s.NotePosition(new PixelPoint(-2809, 146));   // below the AF21 band: off-screen at either width
        s.Commit(isNormalNow: true);

        Assert.Equal(new PixelPoint(200, 146), s.NormalPosition);
    }

    [Fact]
    public void ManagedDialogWindow_PushesItsScale_WithEveryScreenRefresh()
    {
        // The state machine only knows the scale it is given; the dialog is what has RenderScaling.
        string? src = null;
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null && src is null; i++, dir = dir.Parent)
        {
            var c = System.IO.Path.Combine(dir.FullName, "ui", "UE5DumpUI", "Views", "ManagedDialogWindow.cs");
            if (System.IO.File.Exists(c)) src = System.IO.File.ReadAllText(c);
        }
        Assert.NotNull(src);
        int screens = System.Text.RegularExpressions.Regex.Matches(src!, @"_restore\.SetScreens\(").Count;
        int scales  = System.Text.RegularExpressions.Regex.Matches(src!, @"_restore\.SetScale\(RenderScaling\)").Count;
        Assert.True(screens >= 2, $"only {screens} SetScreens calls -- the pattern no longer matches");
        Assert.Equal(screens, scales);
    }
}
