using System.Runtime.CompilerServices;
using UE5DumpUI.Core;

namespace UE5DumpUI.Tests;

/// <summary>
/// [I18N-READINESS-2026-11-04] Gives <see cref="Res"/> the app's English before any test runs. A unit test has no
/// Avalonia application, so Res answered "" for every key; this reads en.axaml from the checkout once and installs it
/// as <see cref="Res.Lookup"/>. The headless suite's StringTableFidelityTests pins the reader against Avalonia's own.
/// </summary>
internal static class EnStringsModule
{
    private static readonly string[] RelativePath = { "ui", "UE5DumpUI", "Resources", "Strings", "en.axaml" };

    [ModuleInitializer]
    internal static void Install()
    {
        var path = Find();
        if (path == null)
        {
            // Loud, not "": a run that cannot see the file must not quietly fall back to blank strings.
            var from = AppContext.BaseDirectory;
            Res.Lookup = key => throw new InvalidOperationException(
                $"Res.Get(\"{key}\") in a test, and en.axaml was not found above {from}");
            return;
        }
        var table = StringTable.Parse(File.ReadAllText(path));
        Res.Lookup = key => table.TryGetValue(key, out var text) ? text : null;
    }

    /// <summary>The file itself, walking up from the test binaries: a marker file such as CLAUDE.md is not enough,
    /// since ui/ has its own (a test that stopped at the first one went red in CI, 2026-10-01).</summary>
    private static string? Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(RelativePath).ToArray());
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
