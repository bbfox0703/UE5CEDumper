using System;
using System.IO;
using System.Linq;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The breadcrumb that lets scripts/UE5CEDumper.CT find UE5Dumper.dll when Cheat
/// Engine gives it nothing to infer from.
///
/// <para>Context: a CE table script cannot read its own .CT path, so the .CT infers the
/// folder from CE's Open/Save dialog objects. <c>File &gt; Open</c> fills those in; a
/// double-click in Explorer does not — which is how a user hit "UE5Dumper.dll not found"
/// with the DLL sitting in the same folder as the .CT (2026-08-04). This file is the one
/// channel that depends on nothing Cheat Engine exposes, so it is also the only half of
/// the fix that can be tested here at all.</para>
/// </summary>
public class DumperDllPathStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly DumperDllPathStore _store;

    public DumperDllPathStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ue5cd-crumb-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
        _store = new DumperDllPathStore(new MockPlatformService(_dir));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Lives_at_the_appdata_root_not_under_Logs()
    {
        // The log retention sweep walks the Logs directory and globs *.log. Putting the
        // breadcrumb under Logs\ would make an age-based sweep delete it, silently
        // regressing the fix months later.
        Assert.Equal(Path.Combine(_dir, "UE5CEDumper", "dll-path.txt"), _store.FilePath);
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}Logs{Path.DirectorySeparatorChar}",
            _store.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_file_reads_as_empty()
        => Assert.Empty(_store.Load());

    [Fact]
    public void Record_then_Load_round_trips_and_creates_the_folder()
    {
        _store.Record(@"D:\Github\UE5CEDumper\dist");
        Assert.Equal(new[] { @"D:\Github\UE5CEDumper\dist" }, _store.Load().ToArray());
    }

    [Fact]
    public void Trailing_separator_is_normalised_away()
    {
        // The .CT appends "UE5Dumper.dll" after re-adding a separator, so a stored
        // trailing slash would produce a double separator in the probed path.
        _store.Record(@"D:\dist\");
        Assert.Equal(@"D:\dist", _store.Load()[0]);
    }

    [Fact]
    public void Newest_first_and_no_duplicates()
    {
        _store.Record(@"D:\one");
        _store.Record(@"D:\two");
        _store.Record(@"D:\ONE");   // same folder, different case
        Assert.Equal(new[] { @"D:\ONE", @"D:\two" }, _store.Load().ToArray());
    }

    [Fact]
    public void Recording_the_current_head_again_does_not_rewrite_the_file()
    {
        // Steady state is every app start. It must not churn the file's mtime.
        _store.Record(@"D:\dist");
        var before = File.GetLastWriteTimeUtc(_store.FilePath);
        File.SetLastWriteTimeUtc(_store.FilePath, before.AddDays(-1));
        var stamped = File.GetLastWriteTimeUtc(_store.FilePath);

        _store.Record(@"D:\dist");

        Assert.Equal(stamped, File.GetLastWriteTimeUtc(_store.FilePath));
    }

    [Fact]
    public void History_is_capped()
    {
        foreach (var d in new[] { @"D:\a", @"D:\b", @"D:\c", @"D:\d", @"D:\e", @"D:\f" })
            _store.Record(d);
        Assert.True(_store.Load().Count <= 4);
        Assert.Equal(@"D:\f", _store.Load()[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("D:\\bad\rpath")]
    [InlineData("D:\\bad\npath")]
    public void Rejects_input_that_would_corrupt_the_line_format(string bad)
    {
        _store.Record(@"D:\good");
        _store.Record(bad);
        // A newline would split one path across two lines, and the Lua reader would
        // take the fragments for real folders.
        Assert.Equal(new[] { @"D:\good" }, _store.Load().ToArray());
    }

    // (fifth review, R5-04) A RELATIVE line -- an older UE5CEDumper.CT's self-heal could write one -- is neither read nor
    // carried forward, and none is written: the .CT would probe it against Cheat Engine's current folder.
    [Fact]
    public void A_relative_line_is_not_read_and_not_carried_forward()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_store.FilePath)!);
        File.WriteAllLines(_store.FilePath, new[] { "# header", "sub", @"D:\old", @"C:rel", @"\x" , @"\\srv\share" });
        Assert.Equal(new[] { @"D:\old", @"\\srv\share" }, _store.Load());

        _store.Record(@"D:\new");
        var lines = File.ReadAllLines(_store.FilePath).Where(l => l.Length > 0 && l[0] != '#').ToArray();
        Assert.Equal(new[] { @"D:\new", @"D:\old", @"\\srv\share" }, lines);
    }

    // (sixth review, R6-01 -- measured) A DLL folder at a DRIVE ROOT: Record trimmed 'E:\' to a bare 'E:', which Load
    // then skipped as relative -- UE5DumpUI started from E:\ rewrote the file on every start, and the .CT ignored it.
    [Theory]
    [InlineData(@"E:\")]
    [InlineData(@"e:\")]      // (seventh review, R7-04) lower case: the .CT's rule agrees
    public void A_drive_root_round_trips_as_the_root(string root)
    {
        _store.Record(root);
        // (seventh review, R7-03) The WRITER's half: the line on disk keeps its separator -- Load's own fix alone would
        // pass a round trip with 'E:' written.
        Assert.Equal(new[] { root }, File.ReadAllLines(_store.FilePath).Where(l => l.Length > 0 && l[0] != '#').ToArray());
        Assert.Equal(new[] { root }, _store.Load());
        // Already the head: no rewrite. The stamp is moved back first, as the neighbouring test does, so a rewrite
        // inside the same clock tick cannot pass as none.
        var before = File.GetLastWriteTimeUtc(_store.FilePath);
        File.SetLastWriteTimeUtc(_store.FilePath, before.AddDays(-1));
        var stamped = File.GetLastWriteTimeUtc(_store.FilePath);
        _store.Record(root);
        Assert.Equal(stamped, File.GetLastWriteTimeUtc(_store.FilePath));
    }

    [Fact]
    public void A_legacy_bare_drive_line_reads_as_that_drives_root()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_store.FilePath)!);
        File.WriteAllLines(_store.FilePath, new[] { "# header", "E:", "e:", @"D:\old" });
        Assert.Equal(new[] { @"E:\", @"e:\", @"D:\old" }, _store.Load());
    }

    [Theory]
    [InlineData("sub")]
    [InlineData(@"C:rel")]
    [InlineData(@"\x")]
    public void Record_refuses_a_relative_folder(string relative)
    {
        _store.Record(relative);
        Assert.False(File.Exists(_store.FilePath));
    }

    [Fact]
    public void Comments_and_blank_lines_are_ignored_by_the_reader()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_store.FilePath)!);
        File.WriteAllLines(_store.FilePath, new[]
        {
            "# a comment",
            "",
            @"D:\real",
            "   ",
            "# another",
        });
        Assert.Equal(new[] { @"D:\real" }, _store.Load().ToArray());
    }

    [Fact]
    public void File_is_written_without_a_BOM()
    {
        // CE Lua reads this with io.open + string ops. A BOM would be prepended to the
        // first path; the .CT strips one defensively, but not emitting it is better.
        _store.Record(@"D:\dist");
        var bytes = File.ReadAllBytes(_store.FilePath);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Fact]
    public void A_corrupt_file_does_not_throw()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_store.FilePath)!);
        File.WriteAllBytes(_store.FilePath, new byte[] { 0xFF, 0xFE, 0x00, 0x01, 0x02 });
        var ex = Record.Exception(() => _store.Load());
        Assert.Null(ex);
    }
}
