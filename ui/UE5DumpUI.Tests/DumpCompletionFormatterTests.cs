using System;
using System.Globalization;
using UE5DumpUI.Helpers;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Pins the two X4 defects in the Dump All completion line:
/// (1) success was announced from the file's byte length, so a zero-class /
/// all-errored dump still read as an export; (2) the size was integer division
/// (<c>length/1024/1024</c>), which printed "3.0 MB" for 3.7 MB and "0.0 MB" for
/// anything under a megabyte. Both halves get negative controls.
/// </summary>
public class DumpCompletionFormatterTests
{
    // Format uses N0/F1 (culture-dependent by design in the UI). Pin the culture so
    // the assertions are deterministic on any dev/CI machine.
    private static T Invariant<T>(Func<T> f)
    {
        var prev = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try { return f(); } finally { CultureInfo.CurrentCulture = prev; }
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(500_000L, "488.3 KB")]     // old int-div => "0.0 MB"
    [InlineData(1_048_576L, "1.0 MB")]
    [InlineData(3_879_731L, "3.7 MB")]     // old int-div => "3.0 MB"
    public void FormatSize_UsesFloatingDivision_AndStepsDownToKbBytes(long bytes, string expected)
        => Assert.Equal(expected, Invariant(() => DumpCompletionFormatter.FormatSize(bytes)));

    [Fact]
    public void FormatSize_NegativeControl_DoesNotRoundRealFileToZeroOrIntegerMb()
    {
        // These are the exact strings the buggy `length/1024/1024:F1` produced.
        Assert.NotEqual("3.0 MB", Invariant(() => DumpCompletionFormatter.FormatSize(3_879_731L)));
        Assert.NotEqual("0.0 MB", Invariant(() => DumpCompletionFormatter.FormatSize(500_000L)));
    }

    [Fact]
    public void Format_ZeroClasses_ReadsAsFailure_NotAnExport()
    {
        var s = Invariant(() => DumpCompletionFormatter.Format(
            new DumpResult(ClassesEmitted: 0, ClassesSkippedEngine: 0, Errors: 0, ObjectsScanned: 5000),
            byteLength: 240, fileName: "x.jsonl"));

        Assert.Contains("no classes", s);
        Assert.DoesNotContain("Dumped", s);   // must NOT claim an export
    }

    [Fact]
    public void Format_ZeroClassesWithErrors_NamesTheErrorCount()
    {
        var s = Invariant(() => DumpCompletionFormatter.Format(
            new DumpResult(0, 0, Errors: 7, ObjectsScanned: 5000),
            byteLength: 240, fileName: "x.jsonl"));

        Assert.Contains("no classes", s);
        Assert.Contains("7 errors", s);
    }

    [Fact]
    public void Format_Success_ReportsClassCountAndFloatingSize()
    {
        var s = Invariant(() => DumpCompletionFormatter.Format(
            new DumpResult(ClassesEmitted: 42, ClassesSkippedEngine: 3, Errors: 0, ObjectsScanned: 9000),
            byteLength: 3_879_731L, fileName: "game-dump.jsonl"));

        Assert.Equal("Dumped 42 classes (3.7 MB) to game-dump.jsonl", s);
    }

    [Fact]
    public void Format_SuccessWithErrors_AppendsErrorCount()
    {
        var s = Invariant(() => DumpCompletionFormatter.Format(
            new DumpResult(ClassesEmitted: 42, ClassesSkippedEngine: 0, Errors: 4, ObjectsScanned: 9000),
            byteLength: 1_048_576L, fileName: "g.jsonl"));

        Assert.Equal("Dumped 42 classes (1.0 MB, 4 errors) to g.jsonl", s);
    }
}
