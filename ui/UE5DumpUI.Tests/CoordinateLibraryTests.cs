using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// P1 of the Teleport Coordinate Library — docs/teleport-coord-library-spec.md.
/// Covers the two things that silently corrupt a hand-curated 4000-entry list if
/// they are wrong: the capture-time precision contract (D4) and the per-game store
/// (D1 + the JSON dialect), plus the model-level character policy and sorting.
/// </summary>
public class CoordPrecisionTests
{
    // A UE4 float coordinate widened to double -- the NORMAL case, not a corner case.
    [Theory]
    [InlineData(67162.3984375, "67162.398")]
    [InlineData(-20380.642578125, "-20380.643")]
    [InlineData(35.79132080078125, "35.791")]
    [InlineData(0.0, "0")]
    [InlineData(100000.0, "100000")]
    public void RoundThenText_ProducesCleanShortText(double raw, string expected)
    {
        Assert.Equal(expected, CoordPrecision.Text(CoordPrecision.Round(raw)));
    }

    [Theory]
    [InlineData(67162.3984375)]
    [InlineData(-20380.642578125)]
    [InlineData(35.79132080078125)]
    [InlineData(1.4210855e-14)]
    [InlineData(0.0)]   // -0.0 is NOT a separate case here: xUnit compares InlineData by
                        // VALUE and -0.0 == 0.0, so listing it is a duplicate (xUnit1025).
                        // Negative zero's real contract is collapsing, covered by
                        // Round_CollapsesNegativeZero below.
    [InlineData(100000.0)]
    [InlineData(-1234.5678)]
    public void RoundThenText_RoundTripsBitExactly(double raw)
    {
        // The whole point of rounding at CAPTURE: the stored double is then the
        // nearest double to a 3-decimal literal, so the text round-trips exactly.
        // Without it a lossy "0.0###"/"0.000" would silently mutate the library on
        // every export->import cycle.
        var stored = CoordPrecision.Round(raw);
        Assert.True(CoordPrecision.TryParse(CoordPrecision.Text(stored), out var back));
        Assert.Equal(stored, back);
    }

    [Fact]
    public void RoundThenText_IsIdempotentOnASecondCycle()
    {
        var stored = CoordPrecision.Round(67162.3984375);
        var text1 = CoordPrecision.Text(stored);
        Assert.True(CoordPrecision.TryParse(text1, out var reparsed));
        var text2 = CoordPrecision.Text(CoordPrecision.Round(reparsed));
        Assert.Equal(text1, text2);          // export -> import -> export is byte-identical
    }

    [Fact]
    public void Round_CollapsesNegativeZero()
    {
        // "-0" would diff against a plain "0" on every export for no reason.
        Assert.Equal("0", CoordPrecision.Text(CoordPrecision.Round(-0.0)));
        Assert.Equal("0", CoordPrecision.Text(CoordPrecision.Round(-0.0001)));
    }

    [Fact]
    public void Round_DenoisesRotatorFloatNoise()
    {
        Assert.Equal(0.0, CoordPrecision.Round(1.4210855e-14));
    }

    [Fact]
    public void TryParse_AcceptsScientificNotation()
    {
        // "R" goes scientific below 1e-5, so a legitimately exported small value
        // comes back as "1E-05". Without AllowExponent the row would be rejected.
        var text = CoordPrecision.Text(1e-5);
        Assert.Contains("E", text, StringComparison.OrdinalIgnoreCase);
        Assert.True(CoordPrecision.TryParse(text, out var back));
        Assert.Equal(1e-5, back);
    }

    [Fact]
    public void Text_IsCultureInvariant()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            // de-DE writes a comma decimal separator, which would break every
            // downstream parser (and the CSV delimiter).
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1234.567", CoordPrecision.Text(CoordPrecision.Round(1234.5674)));
        }
        finally { CultureInfo.CurrentCulture = prev; }
    }
}

public class CoordTextPolicyTests
{
    [Theory]
    [InlineData("\0")]        // luaL_dostring measures with strlen -> truncates the chunk
    [InlineData("a\r\nb")]    // raw newline in a Lua single-quoted literal = compile error
    [InlineData("a\tb")]
    [InlineData("a\u0001b")]  // any other C0 control
    public void HasBlockedChars_DetectsUnescapableInput(string s)
        => Assert.True(CoordText.HasBlockedChars(s));

    [Theory]
    [InlineData("Chest 1")]
    [InlineData("]==]")]              // escape-not-block: the shared escapers handle this
    [InlineData("He said \"hi\"")]
    [InlineData("a'b\\c")]
    [InlineData("寶箱 3（上層）")]     // CJK is fine -- CE renders it, the pipe is UTF-8
    [InlineData("<tag> & more")]
    public void HasBlockedChars_AllowsEverythingEscapable(string s)
        => Assert.False(CoordText.HasBlockedChars(s));

    [Fact]
    public void Normalize_StripsBlockedTrimsAndCaps()
    {
        Assert.Equal("ab", CoordText.Normalize("  a\r\nb  ", 64));
        Assert.Equal(CoordText.MaxLabelLength,
                     CoordText.Normalize(new string('x', 200), CoordText.MaxLabelLength).Length);
        Assert.Equal("", CoordText.Normalize(null, 64));
    }

    [Fact]
    public void Normalize_KeepsCjkAndQuotes()
        => Assert.Equal("寶箱 \"3\"", CoordText.Normalize("寶箱 \"3\"", 64));
}

public class CoordNaturalSortTests
{
    [Fact]
    public void NaturalCompare_OrdersChest2BeforeChest10()
    {
        // Plain ordinal would put "Chest 10" first, which reads as a bug to a user
        // curating a numbered chest list.
        Assert.True(TeleportViewModel.NaturalCompare("Chest 2", "Chest 10") < 0);
        Assert.True(TeleportViewModel.NaturalCompare("Chest 10", "Chest 9") > 0);
    }

    [Fact]
    public void NaturalCompare_IsCaseInsensitiveAndHandlesLeadingZeros()
    {
        Assert.Equal(0, TeleportViewModel.NaturalCompare("chest 2", "CHEST 2"));
        Assert.Equal(0, TeleportViewModel.NaturalCompare("Chest 007", "Chest 7"));
    }

    [Fact]
    public void NaturalCompare_SortsAWholeListSensibly()
    {
        var names = new[] { "Chest 10", "Chest 1", "Boss", "Chest 2", "chest 3" };
        Array.Sort(names, TeleportViewModel.NaturalCompare);
        Assert.Equal(new[] { "Boss", "Chest 1", "Chest 2", "chest 3", "Chest 10" }, names);
    }
}

public class CoordinateLibraryStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly CoordinateLibraryStore _store;

    public CoordinateLibraryStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ue5cd-coordlib-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
        _store = new CoordinateLibraryStore(new MockPlatformService(_dir));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static CoordinateLibraryFile FileWith(params CoordEntry[] entries)
        => new() { Entries = entries.ToList() };

    [Theory]
    [InlineData("MyGame-Win64-Shipping.exe", "mygame-win64-shipping")]
    [InlineData("MyGame.exe", "mygame")]
    [InlineData("MyGame", "mygame")]
    [InlineData("My Game (Demo).exe", "my_game__demo")]   // trailing '_' trimmed
    [InlineData("", "")]
    [InlineData(null, "")]
    public void KeyFor_IsFileSafeAndCaseInsensitive(string? module, string expected)
        => Assert.Equal(expected, CoordinateLibraryStore.KeyFor(module));

    [Fact]
    public void KeyFor_SameGameDifferentCase_ResolvesToOneFile()
    {
        // A differently-cased module report must not orphan the user's list.
        Assert.Equal(CoordinateLibraryStore.KeyFor("MyGame-Win64-Shipping.exe"),
                     CoordinateLibraryStore.KeyFor("mygame-win64-shipping.EXE"));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEntries()
    {
        var e = new CoordEntry
        {
            Uid = "k3f9", Label = "Chest 1", Group = "Chest", Map = "Map01",
            X = 87668.672, Y = -24858.674, Z = 341.376,
            Pitch = 335.01, Yaw = 26.16, Roll = 0,
        };
        _store.Save("game", FileWith(e));

        var back = _store.Load("game");
        var got = Assert.Single(back.Entries);
        Assert.Equal("k3f9", got.Uid);
        Assert.Equal("Chest 1", got.Label);
        Assert.Equal("Map01", got.Map);
        Assert.Equal(e.X, got.X);
        Assert.Equal(e.Roll, got.Roll);
    }

    [Fact]
    public void SaveThenLoad_KeepsZeroValuedCoordinates()
    {
        // The regression this guards: with DefaultIgnoreCondition=WhenWritingDefault
        // a legitimately-saved 0.0 (extremely common for Pitch/Roll) is omitted from
        // the JSON and reloads as 0 BY ACCIDENT rather than by record. Assert the
        // properties are actually present in the serialized text.
        _store.Save("game", FileWith(new CoordEntry
        {
            Uid = "z", Label = "Origin", Map = "M", X = 0, Y = 0, Z = 0,
            Pitch = 0, Yaw = 0, Roll = 0,
        }));

        var json = File.ReadAllText(_store.FilePathFor("game"));
        Assert.Contains("\"x\": 0", json);
        Assert.Contains("\"roll\": 0", json);
        Assert.Contains("\"group\": \"\"", json);

        var got = Assert.Single(_store.Load("game").Entries);
        Assert.Equal(0, got.Pitch);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyNotNull()
    {
        var f = _store.Load("never-saved");
        Assert.NotNull(f);
        Assert.Empty(f.Entries);
    }

    [Fact]
    public void Load_EmptyKey_IsANoOp()
        => Assert.Empty(_store.Load("").Entries);

    [Fact]
    public void Save_EmptyKey_WritesNothing()
    {
        _store.Save("", FileWith(new CoordEntry { Label = "x" }));
        Assert.Empty(Directory.GetFiles(_dir, "*.json"));
    }

    [Fact]
    public void Load_CorruptMainFile_RecoversFromBackup()
    {
        // This is why the store keeps a .bak that BookmarkStore does not: the data is
        // hand-curated, so a torn write must not cost the user the whole list.
        _store.Save("game", FileWith(new CoordEntry { Uid = "a", Label = "Good" }));
        _store.Save("game", FileWith(new CoordEntry { Uid = "b", Label = "Newer" }));

        File.WriteAllText(_store.FilePathFor("game"), "{ this is not json");

        var got = Assert.Single(_store.Load("game").Entries);
        Assert.Equal("Good", got.Label);      // the rolled-aside previous good file
    }

    [Fact]
    public void SavePreImportBackup_SnapshotsSeparatelyFromTheRollingBackup()
    {
        _store.Save("game", FileWith(new CoordEntry { Uid = "a", Label = "Before import" }));
        var bak = _store.SavePreImportBackup("game", _store.Load("game"));
        Assert.True(File.Exists(bak));

        // Two further saves would overwrite the rolling .bak; the preimport copy
        // must survive them so a botched Replace stays recoverable.
        _store.Save("game", FileWith(new CoordEntry { Uid = "b", Label = "After 1" }));
        _store.Save("game", FileWith(new CoordEntry { Uid = "c", Label = "After 2" }));

        Assert.Contains("Before import", File.ReadAllText(bak));
    }

    [Fact]
    public void SavePreImportBackup_NoFileYet_ReturnsEmpty()
        => Assert.Equal("", _store.SavePreImportBackup("game", _store.Load("game")));

    [Fact]
    public void Delete_RemovesTheFile()
    {
        _store.Save("game", FileWith(new CoordEntry { Uid = "a", Label = "x" }));
        Assert.True(File.Exists(_store.FilePathFor("game")));
        _store.Delete("game");
        Assert.False(File.Exists(_store.FilePathFor("game")));
    }

    [Fact]
    public void SavePreClearBackup_SurvivesTheDeleteItGuards()
    {
        _store.Save("game", FileWith(new CoordEntry { Uid = "a", Label = "Hand-curated" }));

        var bak = _store.SavePreClearBackup("game", _store.Load("game"));
        _store.Delete("game");

        Assert.False(File.Exists(_store.FilePathFor("game")));
        Assert.True(File.Exists(bak));
        Assert.Contains("Hand-curated", File.ReadAllText(bak));
    }

    [Fact]
    public void SavePreClearBackup_IsDistinctFromThePreImportBackup()
    {
        // Audit #4 B6: the rolling .bak cannot stand in for either one-shot backup, and
        // a clear must not eat an import's rollback copy (or vice versa).
        _store.Save("game", FileWith(new CoordEntry { Uid = "a", Label = "Before import" }));
        var preImport = _store.SavePreImportBackup("game", _store.Load("game"));

        _store.Save("game", FileWith(new CoordEntry { Uid = "b", Label = "Before clear" }));
        var preClear = _store.SavePreClearBackup("game", _store.Load("game"));

        Assert.NotEqual(preImport, preClear);
        Assert.Contains("Before import", File.ReadAllText(preImport));
        Assert.Contains("Before clear", File.ReadAllText(preClear));
    }

    [Fact]
    public void SavePreClearBackup_OutlivesTheSavesThatEatTheRollingBackup()
    {
        // OnCoordZToleranceChanged persists on every spinner nudge, so two clicks of a
        // NumericUpDown used to destroy the last copy of a cleared library.
        _store.Save("game", FileWith(new CoordEntry { Uid = "a", Label = "Only copy" }));
        var bak = _store.SavePreClearBackup("game", _store.Load("game"));
        _store.Delete("game");

        _store.Save("game", FileWith(new CoordEntry { Uid = "b", Label = "After 1" }));
        _store.Save("game", FileWith(new CoordEntry { Uid = "c", Label = "After 2" }));

        Assert.Contains("Only copy", File.ReadAllText(bak));
    }

    [Fact]
    public void SavePreClearBackup_NoFileYet_ReturnsEmpty()
        => Assert.Equal("", _store.SavePreClearBackup("game", _store.Load("game")));

    // ── [A1-COORD-RESURRECT] + [A1-COORD-BACKUP] ─────────────────────────────────────────────
    //
    // Load fell back to the rolling .bak when the main file was MISSING as well as corrupt, so a
    // "Clear all" -- which deletes the main file and deliberately keeps the backups -- came back
    // on the next connect or restart. And after a .bak recovery the file on disk is still the
    // corrupt one (the load runs with persistence suppressed), so the one-shot backups copied
    // garbage, and a Replace-import's Save then rolled the corrupt main over the only good .bak.

    private void CorruptMain(string key) => File.WriteAllText(_store.FilePathFor(key), "{ this is not json");

    [Fact]
    public void ClearAll_ThenLoad_DoesNotResurrectTheLibrary()
    {
        // The recorded repro: Save, Save, SavePreClearBackup, Delete, Load -> empty.
        _store.Save("game", FileWith(new CoordEntry { Uid = "a", Label = "One" }));
        _store.Save("game", FileWith(new CoordEntry { Uid = "b", Label = "Two" }));   // .bak = "One"
        _store.SavePreClearBackup("game", _store.Load("game"));
        _store.Delete("game");

        Assert.Empty(_store.Load("game").Entries);
        // ...without the recorded-unsafe shortcut of deleting the rolling backup as well.
        Assert.True(File.Exists(_store.FilePathFor("game") + ".bak"));
    }

    [Fact]
    public void ClearAll_AfterATransientLockAtLoad_KeepsTheNewestRevision()
    {
        // (review of 2f8d36f8) A sharing violation at Load reads as "unreadable", so Load recovers
        // the OLDER .bak while the main on disk is the newest good file. The pre-clear backup is
        // written from that in-memory library, and Delete then removed the only copy of the newest.
        _store.Save("game", FileWith(new CoordEntry { Uid = "a", Label = "Older" }));
        _store.Save("game", FileWith(new CoordEntry { Uid = "b", Label = "Newest" }));   // .bak = "Older"
        var path = _store.FilePathFor("game");

        CoordinateLibraryFile shown;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            shown = _store.Load("game");                                   // locked: recovered from .bak
        Assert.Equal("Older", Assert.Single(shown.Entries).Label);

        _store.SavePreClearBackup("game", shown);
        _store.Delete("game");

        var kept = Directory.GetFiles(Path.GetDirectoryName(path)!).Select(File.ReadAllText).ToList();
        Assert.Contains(kept, t => t.Contains("Newest"));
    }

    [Fact]
    public void OneShotBackups_AreWrittenFromTheLibraryPassedIn_NotReReadFromDisk()
    {
        // (review of 2f8d36f8) Every backup test passed `current = Load(key)`, so a store that
        // ignored `current` and re-read the disk passed them all.
        _store.Save("game", FileWith(new CoordEntry { Uid = "d", Label = "OnDisk" }));
        var inMemory = FileWith(new CoordEntry { Uid = "m", Label = "InMemory" });

        var clear  = File.ReadAllText(_store.SavePreClearBackup("game", inMemory));
        var import = File.ReadAllText(_store.SavePreImportBackup("game", inMemory));

        Assert.Contains("InMemory", clear);
        Assert.DoesNotContain("OnDisk", clear);
        Assert.Contains("InMemory", import);
        Assert.DoesNotContain("OnDisk", import);
    }

    [Fact]
    public void OneShotBackup_KeepsTheZTolerance()
    {
        // The one-shot backups are a hand-built copy now, not a byte copy, so every field is a line
        // that can be forgotten. (review of 2f8d36f8)
        var lib = FileWith(new CoordEntry { Uid = "z", Label = "Tol" });
        lib.ZTolerance = 12.25;
        Assert.Contains("12.25", File.ReadAllText(_store.SavePreClearBackup("game", lib)));
    }

    [Fact]
    public void PreClearBackup_AfterABakRecovery_KeepsTheGoodLibrary()
    {
        _store.Save("game", FileWith(new CoordEntry { Uid = "a", Label = "Good" }));
        _store.Save("game", FileWith(new CoordEntry { Uid = "b", Label = "Newer" }));
        CorruptMain("game");
        var recovered = _store.Load("game");                              // from .bak: "Good"
        Assert.Equal("Good", Assert.Single(recovered.Entries).Label);

        var bak = _store.SavePreClearBackup("game", _store.Load("game"));

        Assert.True(File.Exists(bak));
        Assert.Contains("Good", File.ReadAllText(bak));                     // not the corrupt main
    }

    [Fact]
    public void PreImportBackup_AfterABakRecovery_KeepsTheGoodLibrary()
    {
        _store.Save("game", FileWith(new CoordEntry { Uid = "a", Label = "Good" }));
        _store.Save("game", FileWith(new CoordEntry { Uid = "b", Label = "Newer" }));
        CorruptMain("game");
        var recovered = _store.Load("game");

        var bak = _store.SavePreImportBackup("game", _store.Load("game"));

        Assert.True(File.Exists(bak));
        Assert.Contains("Good", File.ReadAllText(bak));
    }

    [Fact]
    public void Save_DoesNotRollAnUnparseableMainOverTheGoodBak()
    {
        // The import twin's second half: the Replace's Save rolled the corrupt main over .bak, so
        // the pre-import library ended up in no file at all.
        _store.Save("game", FileWith(new CoordEntry { Uid = "a", Label = "Good" }));
        _store.Save("game", FileWith(new CoordEntry { Uid = "b", Label = "Newer" }));   // .bak = "Good"
        CorruptMain("game");

        _store.Save("game", FileWith(new CoordEntry { Uid = "c", Label = "Replaced" }));

        Assert.Contains("Good", File.ReadAllText(_store.FilePathFor("game") + ".bak"));
        Assert.Equal("Replaced", Assert.Single(_store.Load("game").Entries).Label);
    }

    [Fact]
    public void Save_OverAnUnparseableMain_MovesItAside_InsteadOfDestroyingIt()
    {
        // (review of 2f8d36f8) [A1-COORD-BACKUP] stopped rolling an unparseable main over the good
        // .bak -- and the rename then destroyed it. Whatever it still held (a half-written save a
        // user can repair by hand) is kept aside now, bounded, with the good .bak untouched.
        _store.Save("game", FileWith(new CoordEntry { Uid = "a", Label = "Good" }));
        _store.Save("game", FileWith(new CoordEntry { Uid = "b", Label = "Newer" }));   // .bak = "Good"
        CorruptMain("game");

        _store.Save("game", FileWith(new CoordEntry { Uid = "c", Label = "Replaced" }));

        var path = _store.FilePathFor("game");
        var aside = Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".corrupt-*");
        Assert.Contains("this is not json", File.ReadAllText(Assert.Single(aside)));
        Assert.Contains("Good", File.ReadAllText(path + ".bak"));
        Assert.Equal("Replaced", Assert.Single(_store.Load("game").Entries).Label);
    }

    [Fact]
    public void Save_PruningNeverDeletesTheCopyItJustMade()
    {
        // (review of 70f9d372) The prune kept the newest N by the stamp's lexicographic order, so a
        // copy stamped in the FUTURE (clock skew, a file copied off another machine) outranked the one
        // just made -- and the fresh copy was the one deleted. AobUsageService excludes it; so does this.
        var path = _store.FilePathFor("game");
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileName(path);
        for (int i = 1; i <= UE5DumpUI.Helpers.AtomicFileHygiene.MaxCorruptCopies; i++)
            File.WriteAllText(Path.Combine(dir, $"{name}.corrupt-2999010{i}-000000000"), "old garbage");
        _store.Save("game", FileWith(new CoordEntry { Uid = "a", Label = "Good" }));
        CorruptMain("game");

        _store.Save("game", FileWith(new CoordEntry { Uid = "b", Label = "Replaced" }));

        var copies = Directory.GetFiles(dir, name + ".corrupt-*");
        Assert.Contains(copies, c => File.ReadAllText(c).Contains("this is not json"));   // the fresh one
        Assert.Equal(UE5DumpUI.Helpers.AtomicFileHygiene.MaxCorruptCopies, copies.Length);  // still bounded
    }

    [Fact]
    public void Delete_AfterABakRecovery_DoesNotRollTheCorruptMainOverTheGoodBak()
    {
        // (review of 70f9d372) Delete's roll is guarded on the main PARSING, exactly as Save's is; an
        // unguarded roll would put garbage over the only good copy.
        _store.Save("game", FileWith(new CoordEntry { Uid = "a", Label = "Good" }));
        _store.Save("game", FileWith(new CoordEntry { Uid = "b", Label = "Newer" }));   // .bak = "Good"
        CorruptMain("game");
        _store.Load("game");                                                      // recovered from .bak

        _store.Delete("game");

        Assert.Contains("Good", File.ReadAllText(_store.FilePathFor("game") + ".bak"));
    }

    [Fact]
    public void Delete_RefusesWhenTheRollFails_SoTheNewestRevisionSurvives()
    {
        // (review of 70f9d372) Delete ignored TryRollToBackup's result, so a failed roll still deleted
        // the one revision the roll existed to keep.
        _store.Save("game", FileWith(new CoordEntry { Uid = "a", Label = "Older" }));
        _store.Save("game", FileWith(new CoordEntry { Uid = "b", Label = "Newest" }));  // .bak = "Older"
        var path = _store.FilePathFor("game");

        using (new FileStream(path + ".bak", FileMode.Open, FileAccess.Read, FileShare.None))
            _store.Delete("game");                                                  // the roll cannot write .bak

        Assert.True(File.Exists(path));
        Assert.Contains("Newest", File.ReadAllText(path));
    }

    [Fact]
    public void Save_StillRollsAGoodMainToBak()
    {
        // The control, green before and after: the rolling backup keeps working. THREE saves, so
        // a guard that rolls only once (or parses the wrong file) cannot pass. (review of 2f8d36f8)
        _store.Save("game", FileWith(new CoordEntry { Uid = "a", Label = "First" }));
        _store.Save("game", FileWith(new CoordEntry { Uid = "b", Label = "Second" }));
        _store.Save("game", FileWith(new CoordEntry { Uid = "c", Label = "Third" }));

        var bak = File.ReadAllText(_store.FilePathFor("game") + ".bak");
        Assert.Contains("Second", bak);
        Assert.DoesNotContain("First", bak);
    }

    [Fact]
    public void Save_IsAtomic_NoTempFileLeftBehind()
    {
        _store.Save("game", FileWith(new CoordEntry { Uid = "a", Label = "x" }));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

}

public class CoordEntryIdentityTests
{
    [Fact]
    public void SameIdentityAs_IgnoresCaseOnAllThreeFields()
    {
        var a = new CoordEntry { Label = "Chest 1", Group = "Chest", Map = "Map01" };
        var b = new CoordEntry { Label = "chest 1", Group = "CHEST", Map = "map01" };
        Assert.True(a.SameIdentityAs(b));
    }

    [Fact]
    public void SameIdentityAs_DistinguishesDifferentMaps()
    {
        var a = new CoordEntry { Label = "Chest 1", Group = "Chest", Map = "Map01" };
        var b = new CoordEntry { Label = "Chest 1", Group = "Chest", Map = "Map02" };
        Assert.False(a.SameIdentityAs(b));
    }

    [Fact]
    public void Clone_IsADeepEnoughCopy()
    {
        var a = new CoordEntry { Uid = "u", Label = "L", Group = "G", Map = "M", X = 1, Yaw = 2 };
        var b = a.Clone();
        b.Label = "changed";
        Assert.Equal("L", a.Label);
        Assert.Equal(1, b.X);
        Assert.Equal(2, b.Yaw);
    }

    [Fact]
    public void Uid_IsNotNamedId_SoAobMakerCannotRenumberIt()
    {
        // AOBMaker's CtIdRenumberService classifies a script as an "ID-check script"
        // on ((?<![A-Za-z0-9_])id\s*=\s*)(\d+) alone and rewrites those literals, so
        // an entry key emitted as `id = 17` would be silently renumbered when the user
        // runs "renumber CT IDs" on a table holding our generated script. The property
        // name is load-bearing, not cosmetic -- assert it.
        var prop = typeof(CoordEntry).GetProperty("Uid");
        Assert.NotNull(prop);
        Assert.Null(typeof(CoordEntry).GetProperty("Id"));
    }
}

public class CoordMapComparisonTests
{
    [Theory]
    [InlineData("Map01", "map01", true)]
    [InlineData("Map01", "Map01", true)]
    [InlineData("", "", true)]
    [InlineData(null, "", true)]
    [InlineData("Map01", "Map02", false)]
    public void IsSameMap_IsCaseInsensitive(string? a, string? b, bool expected)
    {
        // CSV import makes Map user-authored, so an ordinal comparison would flag
        // every hand-typed "map01" as cross-map and force Force on the whole list.
        Assert.Equal(expected, TeleportViewModel.IsSameMap(a, b));
    }
}
