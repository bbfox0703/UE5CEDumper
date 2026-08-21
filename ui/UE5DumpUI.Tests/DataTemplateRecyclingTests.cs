using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Pins the pairing that broke the code-built DataGrid dialogs: a
/// <c>FuncDataTemplate</c> factory that BAKES its values must NOT advertise
/// <c>supportsRecycling: true</c>.
///
/// WHY THIS TEST EXISTS
///   `supportsRecycling: true` tells Avalonia the control the factory produced may be reused for a
///   DIFFERENT data item WITHOUT re-running the factory. Every cell template in these dialogs sets
///   its values at construction time — <c>Text = x?.Name</c>, <c>Foreground</c> computed from
///   <c>x</c> — with no bindings, so a recycled cell keeps the PREVIOUS item's text. Sorting a
///   column reshuffles which item lands in which row container and the grid then renders one row's
///   data twice.
///
///   Reported 2026-08-21 with screenshots: the Props dialog (Interesting Functions → Props) showed
///   "2 properties (1 written)" in its header while BOTH grid rows rendered
///   `read / DropItemLaunchParams_OnDeath / MapProperty`. The header was right and the grid was
///   wrong, which is the tell that the DATA is intact and the RENDERING is stale.
///
/// WHY A SOURCE SCAN RATHER THAN A UI TEST
///   The defect only shows once Avalonia actually recycles a container, which needs a realized
///   visual tree, a sort, and enough rows — none of which the headless test host gives us cheaply.
///   The source-level invariant is narrow, exact, and cannot regress silently: if someone adds a
///   value-baking template with recycling on, this fails.
///
/// ⚠ If a future template uses real BINDINGS instead of baked values, recycling is correct for it
///   and this test must be taught the difference — do not just delete the assertion. Today there
///   are zero such templates, which is why the check can be a flat "no `true` anywhere".
/// </summary>
public class DataTemplateRecyclingTests
{
    /// <summary>Every code-built view that constructs DataGrid cell templates by hand.</summary>
    private static readonly string[] ViewsWithCodeBuiltTemplates =
    {
        "FunctionPropsDialog.cs",
        "InvokeParamDialog.cs",
        "ObjectInstancePickerDialog.cs",
        "PropertyXrefDialog.cs",
        "ProcessPickerWindow.cs",
    };

    [Fact]
    public void NoFuncDataTemplateClaimsRecycling()
    {
        var offenders = new List<string>();
        int scanned = 0, templatesSeen = 0;

        foreach (var leaf in ViewsWithCodeBuiltTemplates)
        {
            var path = FindRepoFile(Path.Combine("ui", "UE5DumpUI", "Views", leaf));
            Assert.True(path != null, $"could not locate {leaf} — the test's file list has drifted");
            var text = File.ReadAllText(path!);
            scanned++;
            templatesSeen += Regex.Matches(text, @"new FuncDataTemplate<").Count;

            foreach (Match m in Regex.Matches(text, @"supportsRecycling:\s*true"))
            {
                int line = text.Substring(0, m.Index).Split('\n').Length;
                offenders.Add($"{leaf}:{line}");
            }
        }

        // Guard the guard: if the files stopped containing templates at all, this test would
        // pass vacuously and stop protecting anything.
        Assert.True(scanned == ViewsWithCodeBuiltTemplates.Length,
            "not every listed view was found");
        Assert.True(templatesSeen >= 15,
            $"only {templatesSeen} FuncDataTemplate(s) found across {scanned} files — the scan is " +
            "no longer looking at the code it was written for");

        Assert.True(offenders.Count == 0,
            "FuncDataTemplate factories in these views BAKE their values (Text = x?.Foo) and must " +
            "use supportsRecycling: false, or a recycled cell renders a stale item — the duplicate-row " +
            "bug reported 2026-08-21. Offenders: " + string.Join(", ", offenders));
    }

    /// <summary>The positive half: the files really do still use the safe form, so a careless
    /// "delete the argument entirely" (which defaults to recycling ON in some overloads) is
    /// caught too.</summary>
    [Fact]
    public void EveryCodeBuiltTemplateStatesRecyclingExplicitly()
    {
        foreach (var leaf in ViewsWithCodeBuiltTemplates)
        {
            var path = FindRepoFile(Path.Combine("ui", "UE5DumpUI", "Views", leaf));
            Assert.NotNull(path);
            var text = File.ReadAllText(path!);

            int templates = Regex.Matches(text, @"new FuncDataTemplate<").Count;
            int stated = Regex.Matches(text, @"supportsRecycling:\s*(true|false)").Count;

            Assert.True(templates == stated,
                $"{leaf}: {templates} FuncDataTemplate(s) but {stated} explicit supportsRecycling " +
                "argument(s) — every one must say so out loud, because the implicit default is not " +
                "obviously safe for a value-baking factory.");
        }
    }

    /// <summary>Walk up from the test binary to the repo root and resolve a tracked file.
    /// Same shape as AuditL11HonestyTests' helper.</summary>
    private static string? FindRepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
