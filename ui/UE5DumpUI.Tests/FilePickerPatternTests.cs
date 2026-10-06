using System;
using System.IO;
using System.Linq;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The file-type pattern the save and open pickers are given. Windows' save dialog appends, to a name
/// typed without an extension, the extension it reads from the selected filter's pattern, so the pattern
/// must be <c>*.ext</c>: <c>"*" + "csv"</c> is <c>*csv</c>, from which it reads none, and the file is
/// written with no extension; the open dialog also lists every file whose name merely ends in "csv".
/// <see cref="Core.IPlatformService"/> does not say which form a caller passes, so the service adds the
/// dot when it is missing.
/// </summary>
public class FilePickerPatternTests
{
    [Theory]
    [InlineData("csv", "*.csv")]
    [InlineData("lua", "*.lua")]
    public void AnExtensionWithoutItsDotGetsOne(string extension, string expected) =>
        Assert.Equal(expected, WindowsPlatformService.FilePickerPattern(extension));

    [Theory]
    [InlineData(".csv", "*.csv")]
    [InlineData(".jsonl", "*.jsonl")]
    [InlineData(".CT", "*.CT")]   // the case is kept as given
    public void ADottedExtensionIsNotDottedTwice(string extension, string expected) =>
        Assert.Equal(expected, WindowsPlatformService.FilePickerPattern(extension));

    // The helper is only half of it: a picker that still builds "*" + extension by hand bypasses it.
    [Fact]
    public void EveryPickerInTheServiceBuildsItsPatternThroughTheHelper()
    {
        string[] patternLines = File.ReadAllLines(RepoFile(@"ui\UE5DumpUI\Services\WindowsPlatformService.cs"))
            .Where(l => l.Contains("Patterns ="))
            .ToArray();

        Assert.NotEmpty(patternLines);
        Assert.All(patternLines, l => Assert.Contains("FilePickerPattern(", l));
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"could not find {relative} walking up from {AppContext.BaseDirectory}");
    }
}
