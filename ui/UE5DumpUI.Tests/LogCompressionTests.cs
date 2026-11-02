using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UE5DumpUI;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The pure half of log compression: which files are eligible, and how they are batched
/// onto a command line. No disk, no process — see <see cref="LogCompressionServiceTests"/>
/// for the end-to-end pass.
/// </summary>
public class LogCompressionPolicyTests
{
    private static readonly DateTime Now = new(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan LiveAge = TimeSpan.FromDays(Constants.LogCompressLiveFileMinAgeDays);
    private const long Min = Constants.LogCompressMinSizeBytes;   // 4096

    private static LogFileEntry F(string name, long len, long onDisk, int hoursAgo, bool ours = false) =>
        new($@"C:\Logs\{name}", name, len, onDisk, Now.AddHours(-hoursAgo), ours);

    private static LogCompressionDecision D(in LogFileEntry f, TimeSpan? idle = null) =>
        LogCompressionPolicy.Decide(f, Now, idle ?? Hour, Min, LiveAge);

    // ---------- IsLiveLog ----------

    [Theory]
    [InlineData("pipe-0.log", true)]
    [InlineData("ui-pipe-0.log", true)]
    [InlineData("WALK-0.LOG", true)]
    [InlineData("pipe-20260804-205327.log", false)]
    [InlineData("pipe-1.log", false)]          // legacy generation — archived, fair game
    [InlineData("init-0.log.bak", false)]
    public void IsLiveLog_MatchesOnlyTheSessionFile(string name, bool expected)
        => Assert.Equal(expected, LogCompressionPolicy.IsLiveLog(name));

    // ---------- Decide ----------

    [Fact]
    public void Decide_EligibleArchive_IsCompressed()
        => Assert.Equal(LogCompressionDecision.Compress,
                        D(F("walk-20260701-101010.log", 8_000_000, 8_000_000, 48)));

    [Fact]
    public void Decide_AlreadyCompressed_IsSkipped()
    {
        // The signal that matters: LZX does NOT set FileAttributes.Compressed, so
        // on-disk < length is the only cheap way to know. Real measured pair.
        Assert.Equal(LogCompressionDecision.SkipAlreadyCompressed,
                     D(F("walk-a.log", 8_441_784, 335_872, 48)));
    }

    [Fact]
    public void Decide_UnreadableOnDiskSize_IsNotMistakenForCompressed()
    {
        // OnDiskBytes == 0 means "could not read it", not "it is empty". Treating that as
        // already-compressed would permanently skip the file.
        Assert.Equal(LogCompressionDecision.Compress, D(F("walk-b.log", 8_000_000, 0, 48)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4096)]     // the floor is inclusive: "> min", not ">= min"
    public void Decide_AtOrBelowTheFloor_IsTooSmall(long len)
        => Assert.Equal(LogCompressionDecision.SkipTooSmall, D(F("walk-c.log", len, len, 48)));

    [Fact]
    public void Decide_JustOverTheFloor_IsEligible()
        => Assert.Equal(LogCompressionDecision.Compress, D(F("walk-d.log", 4097, 4097, 48)));

    [Fact]
    public void Decide_WrittenWithinTheIdleWindow_IsTooFresh()
        => Assert.Equal(LogCompressionDecision.SkipTooFresh, D(F("walk-e.log", 8_000_000, 8_000_000, 0)));

    [Fact]
    public void Decide_ExactlyAtTheIdleWindow_IsEligible()
        => Assert.Equal(LogCompressionDecision.Compress, D(F("walk-f.log", 8_000_000, 8_000_000, 1)));

    // ---------- the -0.log rule ----------

    [Fact]
    public void Decide_OurOwnLiveFile_IsSkippedAtAnyAge()
    {
        // A Serilog sink holds these for the whole session. Even a 900-hour-old init-0.log
        // (a very long-running session) is still open, so this one is identity, not age.
        Assert.Equal(LogCompressionDecision.SkipLive,
                     D(F("init-0.log", 8_000_000, 8_000_000, 900, ours: true)));
    }

    [Fact]
    public void Decide_AGamesNewestLog_IsSkippedWhileItCouldStillBeWritten()
    {
        // 48 h clears the 1 h idle window but not the 7-day live floor.
        Assert.Equal(LogCompressionDecision.SkipLive, D(F("walk-0.log", 8_000_000, 8_000_000, 48)));
    }

    [Fact]
    public void Decide_AGamesNewestLog_IsCompressedOnceItHasGoneQuiet()
    {
        // The case that prompted the rule: SEED BATTLE DESTINY REMASTERED\walk-0.log,
        // 3.64 MB, untouched for 13 days. Nothing will append to it until that game is
        // injected again — and that path RENAMES it, which preserves LZX.
        Assert.Equal(LogCompressionDecision.Compress,
                     D(F("walk-0.log", 3_800_000, 3_800_000, 13 * 24)));
    }

    [Fact]
    public void Decide_TheLiveFloorAppliesToTheAutomaticTriggerToo()
    {
        // The auto sweep's own idle window is 7 days, so the floor is not what binds
        // there — but a -0.log one hour short of it must still be refused.
        var almost = F("walk-0.log", 8_000_000, 8_000_000, Constants.LogCompressLiveFileMinAgeDays * 24 - 1);
        Assert.Equal(LogCompressionDecision.SkipLive,
                     D(almost, idle: TimeSpan.FromDays(Constants.LogAutoCompressMinAgeDays)));
    }

    [Fact]
    public void Decide_AnAlreadyCompressedLiveSlotReportsDone_NotLive()
    {
        // Ordering check: a -0.log compressed on a previous pass must not be re-reported
        // as "in use", or the counts stop adding up across runs.
        Assert.Equal(LogCompressionDecision.SkipAlreadyCompressed,
                     D(F("walk-0.log", 8_441_784, 335_872, 13 * 24)));
    }

    // ---------- Plan ----------

    [Fact]
    public void Plan_CountsEveryReasonAndSumsOnlyTheCandidates()
    {
        var plan = LogCompressionPolicy.Plan(new[]
        {
            F("walk-20260701-1.log", 5_000_000, 5_000_000, 48),        // compress
            F("walk-20260701-2.log", 3_000_000, 3_000_000, 48),        // compress
            F("walk-0.log",          9_000_000, 9_000_000, 13 * 24),   // compress (gone quiet)
            F("scan-0.log",          9_000_000, 9_000_000, 48),        // live (still recent)
            F("init-0.log",          9_000_000, 9_000_000, 900, true), // live (ours)
            F("pipe-20260701-1.log", 8_441_784,   335_872, 48),        // already done
            F("init-20260701-1.log",       100,       100, 48),        // too small
            F("view-20260805-1.log", 6_000_000, 6_000_000,  0),        // too fresh
        }, Now, Hour, Min, LiveAge);

        Assert.Equal(3, plan.ToCompress.Count);
        Assert.Equal(17_000_000, plan.CandidateBytes);
        Assert.Equal(2, plan.SkippedLive);
        Assert.Equal(1, plan.SkippedAlreadyCompressed);
        Assert.Equal(1, plan.SkippedTooSmall);
        Assert.Equal(1, plan.SkippedTooFresh);
    }

    // [LOG-RESUME-LIVE-ROLLED] A session that rolls a category past 8 MB writes the highest-numbered
    // `{prefix}-0_NNN.log`, which IsLiveLog does not name; once it was quiet an hour, the sweep tried it and reported
    // a failure. In our own folder the newest rolled file of each category is the live one; an older one is finished.

    [Fact]
    public void Plan_OurNewestRolledFileOfACategory_IsLive()
    {
        var plan = LogCompressionPolicy.Plan(new[]
        {
            F("view-0.log",     9_000_000, 9_000_000, 3, true),   // live: ours, the slot name
            F("view-0_001.log", 9_000_000, 9_000_000, 3, true),   // finished: compress
            F("view-0_002.log", 9_000_000, 9_000_000, 2, true),   // live: the newest rolled view file
            F("pipe-0_001.log", 9_000_000, 9_000_000, 2, true),   // live: the newest rolled pipe file
        }, Now, Hour, Min, LiveAge);

        Assert.Equal(new[] { @"C:\Logs\view-0_001.log" }, plan.ToCompress);
        Assert.Equal(3, plan.SkippedLive);
    }

    // Review round 2: the UI writes a game's folder too while connected -- its mirror sinks, ui-{cat}-0.log, roll to
    // ui-{cat}-0_NNN.log the same way -- and the DLL never writes a -0_NNN name (Sein archives as {cat}-{stamp}.log).
    // So in a game's folder the newest rolled file of a category is that folder's slot, held to the -0.log slot's idle
    // rule (LiveAge), and an older one is finished.

    private static LogFileEntry G(string folder, string name, int hoursAgo) =>
        new($@"C:\Logs\{folder}\{name}", name, 9_000_000, 9_000_000, Now.AddHours(-hoursAgo), false);

    [Fact]
    public void Plan_AGameFoldersNewestRolledFile_IsHeldLikeItsSlot()
    {
        var plan = LogCompressionPolicy.Plan(new[]
        {
            G("Game", "ui-pipe-0_001.log", 48),        // finished: compress
            G("Game", "ui-pipe-0_002.log", 2),         // the mirror's live one, quiet an hour: kept
            G("Other", "ui-pipe-0_001.log", 48),       // another folder's newest, quiet two days: kept
            G("Old", "ui-view-0_003.log", 24 * 8),     // quiet past the slot's rule: compress
        }, Now, Hour, Min, LiveAge);

        Assert.Equal(new[] { @"C:\Logs\Game\ui-pipe-0_001.log", @"C:\Logs\Old\ui-view-0_003.log" }, plan.ToCompress);
        Assert.Equal(2, plan.SkippedLive);
    }

    [Fact]
    public void Plan_EmptyInputIsEmpty()
    {
        var plan = LogCompressionPolicy.Plan(Array.Empty<LogFileEntry>(), Now, Hour, Min, LiveAge);
        Assert.Empty(plan.ToCompress);
        Assert.Equal(0, plan.CandidateBytes);
    }

    // ---------- Batch ----------

    [Fact]
    public void Batch_SplitsByCount()
    {
        var paths = Enumerable.Range(0, 95).Select(i => $@"C:\Logs\f{i}.log").ToList();
        var b = LogCompressionPolicy.Batch(paths, 40, 24000);
        Assert.Equal(3, b.Count);
        Assert.Equal(40, b[0].Count);
        Assert.Equal(40, b[1].Count);
        Assert.Equal(15, b[2].Count);
        Assert.Equal(95, b.Sum(x => x.Count));
    }

    [Fact]
    public void Batch_SplitsByCommandLineLength_BeforeHittingTheCountCap()
    {
        // Long paths are the real case: a per-game folder is named after the game EXE.
        var longPath = @"C:\Users\Someone\AppData\Local\UE5CEDumper\Logs\SEED BATTLE DESTINY REMASTERED\walk-20260804-205327.log";
        var paths = Enumerable.Repeat(longPath, 40).ToList();
        var b = LogCompressionPolicy.Batch(paths, 40, 500);

        Assert.True(b.Count > 1, "a 500-char budget must split 40 x ~110-char paths");
        Assert.Equal(40, b.Sum(x => x.Count));
        foreach (var batch in b)
            Assert.True(batch.Count == 1 || batch.Sum(p => p.Length + 3) <= 500);
    }

    [Fact]
    public void Batch_APathLongerThanTheWholeBudgetIsStillEmitted()
    {
        // Dropping it would lose a file silently, which is worse than one command line the
        // OS rejects and we then report as a failure.
        var huge = new string('x', 40_000);
        var b = LogCompressionPolicy.Batch(new[] { huge }, 40, 24000);
        Assert.Equal(new[] { huge }, Assert.Single(b));
    }

    [Fact]
    public void Batch_EmptyInputIsEmpty()
        => Assert.Empty(LogCompressionPolicy.Batch(Array.Empty<string>(), 40, 24000));
}

/// <summary>
/// End-to-end over real files. The load-bearing assertion is the LAST one:
/// <b>compression must not move <c>LastWriteTime</c></b>, because
/// <see cref="LoggingService"/>'s 21-day retention keys on exactly that. If this ever
/// fails, compression has started deleting logs early.
/// </summary>
public class LogCompressionServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly WindowsLogCompressionService _svc = new();

    public LogCompressionServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"UE5DumpLogComp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    /// <summary>~600 KB of log-shaped, highly compressible text.</summary>
    private string WriteLog(string relative, int hoursAgo, int lines = 6000)
    {
        var p = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var sb = new StringBuilder();
        for (int i = 0; i < lines; i++)
            sb.Append("[2026-08-04 20:48:05.931] [INFO] [WALK] object ")
              .Append(i).Append(" class BP_Player_C offset 0x").Append(i.ToString("X4"))
              .Append(" value 12345\n");
        File.WriteAllText(p, sb.ToString());
        var t = DateTime.UtcNow.AddHours(-hoursAgo);
        File.SetLastWriteTimeUtc(p, t);
        return p;
    }

    [Fact]
    public async Task Compress_ShrinksArchives_SkipsLiveAndSmall_AndNEVERMovesTheWriteTime()
    {
        // A per-game subfolder whose name contains SPACES — the exact shape that silently
        // lost 23 files to an unquoted command line during the evaluation.
        var archive = WriteLog(Path.Combine("SEED BATTLE DESTINY REMASTERED", "walk-20260701-101010.log"), 48);
        var live    = WriteLog("walk-0.log", 48);
        var small   = Path.Combine(_dir, "init-20260701-101010.log");
        File.WriteAllText(small, "tiny");
        File.SetLastWriteTimeUtc(small, DateTime.UtcNow.AddHours(-48));

        var before = File.GetLastWriteTimeUtc(archive);
        long archiveLength = new FileInfo(archive).Length;

        var r = await _svc.CompressAsync(_dir, TimeSpan.FromHours(1), Constants.LogCompressMinSizeBytes,
                                         TestContext.Current.CancellationToken);

        if (!r.Supported)
        {
            // Not NTFS (CI on ReFS / a share). "Unsupported" is a valid answer, and the
            // service must have touched nothing.
            Assert.Equal(0, r.Compressed);
            Assert.Equal(archiveLength, new FileInfo(archive).Length);
            return;
        }

        Assert.Equal(1, r.Compressed);
        Assert.Equal(0, r.Failed);
        Assert.Equal(1, r.SkippedLive);
        Assert.Equal(1, r.SkippedTooSmall);
        Assert.True(r.BytesAfter < r.BytesBefore,
                    $"expected the archive to shrink on disk ({r.BytesBefore} -> {r.BytesAfter})");

        // Content is still readable as plain text — the whole reason this is not a .gz pass.
        Assert.StartsWith("[2026-08-04 20:48:05.931] [INFO] [WALK] object 0 ",
                          File.ReadLines(archive).First(), StringComparison.Ordinal);
        Assert.Equal(archiveLength, new FileInfo(archive).Length);   // logical size unchanged
        Assert.True(File.Exists(live));

        // THE assertion: retention keys on LastWriteTime, so compression must not move it.
        Assert.Equal(before, File.GetLastWriteTimeUtc(archive));
    }

    [Fact]
    public async Task Compress_AQuietGamesNewestLogIsCompressed_ButOurOwnSessionFileIsNot()
    {
        // The real case: SEED BATTLE DESTINY REMASTERED\walk-0.log, 3.64 MB, 13 days idle.
        // It is the game's newest log, nothing will append to it until that game is
        // injected again, and that path renames it (which preserves LZX).
        var quiet = WriteLog(Path.Combine("SEED BATTLE DESTINY REMASTERED", "walk-0.log"), 13 * 24);
        // Ours: Serilog holds this for the whole session, so it is excluded on identity —
        // being 13 days old (a very long-running session) must not make it eligible.
        var ours  = WriteLog(Path.Combine(Constants.LogSubfolderName, "init-0.log"), 13 * 24);

        var oursBefore = new FileInfo(ours).Length;
        var r = await _svc.CompressAsync(_dir, TimeSpan.FromHours(1), Constants.LogCompressMinSizeBytes,
                                         TestContext.Current.CancellationToken);
        if (!r.Supported) return;

        Assert.Equal(1, r.Compressed);
        Assert.Equal(1, r.SkippedLive);
        Assert.True(WindowsLogCompressionService.OnDiskBytes(quiet) < new FileInfo(quiet).Length,
                    "the quiet game log should have shrunk on disk");
        Assert.Equal(oursBefore, WindowsLogCompressionService.OnDiskBytes(ours));
    }

    [Fact]
    public async Task Compress_IsIdempotent_SecondPassReportsAlreadyDone()
    {
        WriteLog("walk-20260701-101010.log", 48);

        var first = await _svc.CompressAsync(_dir, TimeSpan.FromHours(1), Constants.LogCompressMinSizeBytes,
                                             TestContext.Current.CancellationToken);
        if (!first.Supported) return;

        var second = await _svc.CompressAsync(_dir, TimeSpan.FromHours(1), Constants.LogCompressMinSizeBytes,
                                              TestContext.Current.CancellationToken);
        Assert.Equal(0, second.Compressed);
        Assert.Equal(0, second.Failed);
        Assert.Equal(1, second.SkippedAlreadyCompressed);
        Assert.True(second.NothingToDo);
    }

    [Fact]
    public async Task Compress_EmptyFolderIsNotAnError()
    {
        var r = await _svc.CompressAsync(_dir, TimeSpan.FromHours(1), Constants.LogCompressMinSizeBytes,
                                         TestContext.Current.CancellationToken);
        Assert.True(r.NothingToDo);
        Assert.Equal(0, r.Failed);
    }

    [Fact]
    public async Task Compress_MissingFolderIsNotAnError()
    {
        var r = await _svc.CompressAsync(Path.Combine(_dir, "nope"), TimeSpan.FromHours(1),
                                         Constants.LogCompressMinSizeBytes,
                                         TestContext.Current.CancellationToken);
        Assert.Equal(0, r.Compressed);
        Assert.Equal(0, r.Failed);
    }

    [Fact]
    public void OnDiskBytes_OfAPlainFileEqualsItsAllocatedSize()
    {
        var p = WriteLog("plain-20260701-101010.log", 48, lines: 100);
        long onDisk = WindowsLogCompressionService.OnDiskBytes(p);
        Assert.True(onDisk >= new FileInfo(p).Length,
                    $"an uncompressed file must not report less on disk (len={new FileInfo(p).Length}, disk={onDisk})");
    }

    [Fact]
    public void OnDiskBytes_OfAMissingFileIsZero_NotAFalseCompressedSignal()
        => Assert.Equal(0, WindowsLogCompressionService.OnDiskBytes(Path.Combine(_dir, "ghost.log")));
}
