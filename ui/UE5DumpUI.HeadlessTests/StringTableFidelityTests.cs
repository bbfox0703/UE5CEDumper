using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UE5DumpUI.Tests;   // StringTable: the unit tests' reader, compiled into this project as a linked file
using Xunit;

namespace UE5DumpUI.HeadlessTests;

/// <summary>
/// [I18N-READINESS-2026-11-04] The unit tests read en.axaml through <see cref="StringTable"/> (they have no Avalonia
/// application). That is only safe if it reads every key exactly as Avalonia's XAML loader does -- entities, leading
/// and trailing spaces, line breaks -- or a unit test asserts a sentence the app never shows. Pinned key for key.
/// </summary>
public class StringTableFidelityTests
{
    [Fact]
    public Task StringTable_reads_every_key_as_Avalonia_does() => Headless.Run(() =>
    {
        var loaded = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://UE5DumpUI/Resources/Strings/en.axaml"));
        var table = StringTable.Parse(File.ReadAllText(FindEnAxaml()));

        Assert.True(table.Count > 1000, $"only {table.Count} keys parsed -- not the file this test means");
        var missing = loaded.Keys.Select(k => k.ToString()!).Where(k => !table.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "keys Avalonia loads that StringTable does not: " + string.Join(", ", missing.Take(5)));
        var differ = table
            .Where(kv => !(loaded.TryGetValue(kv.Key, out var v) && v is string s && s == kv.Value))
            .Select(kv => kv.Key + " = [" + kv.Value + "] vs [" + (loaded.TryGetValue(kv.Key, out var v) ? v : "(absent)") + "]")
            .ToList();
        Assert.True(differ.Count == 0, differ.Count + " key(s) read differently: " + string.Join(" | ", differ.Take(5)));
    });

    private static string FindEnAxaml()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "ui", "UE5DumpUI", "Resources", "Strings", "en.axaml");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("en.axaml not found above " + AppContext.BaseDirectory);
    }
}
