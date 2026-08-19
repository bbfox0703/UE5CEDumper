using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The structural guard behind audit #5 AF16-AF23.
///
/// <para>Six findings in that batch were all one defect — <i>a user-sortable DataGrid
/// column with no AOT comparer</i> — and the sweep that fixed them found <b>four more
/// sites no finding named</b>, including one whose comment argued the omission was
/// deliberate. Closing six of ten is exactly how this recurs, so the rule is enforced
/// here rather than left to review.</para>
///
/// <para><b>The rule</b> (Helpers/DataGridSortComparers.cs class doc): Avalonia's default
/// column sort resolves <c>SortMemberPath</c> by reflection. Under Native-AOT/trim that
/// metadata survives only for a property some compiled binding roots. So a user-sortable
/// column is safe iff <b>either</b>
/// <list type="number">
///   <item>it is a column with a <c>Binding</c> whose path equals its
///   <c>SortMemberPath</c> (the compiled binding roots the property), <b>or</b></item>
///   <item>a comparer is wired for that <c>SortMemberPath</c>.</item>
/// </list>
/// Anything else compiles, runs untrimmed, and ships a header that animates and does
/// nothing — CLAUDE.md's headline AOT hazard.</para>
///
/// <para><b>Why source-scanning and not a headless Avalonia run:</b> the defect is
/// invisible at runtime in a JIT test host — that is the entire problem. Only the
/// trimmed binary misbehaves, and a test cannot trim itself. What CAN be checked without
/// a game, a GPU or a publish is the pairing of markup to comparer table, which is where
/// the mistake is actually made. Same file-locating shape as
/// <c>CeExecuteCodeExArityTests.FindRepoFile</c>.</para>
/// </summary>
public class DataGridSortWiringTests
{
    private const string Av = "{https://github.com/avaloniaui}";
    private const string Xaml = "{http://schemas.microsoft.com/winfx/2006/xaml}";

    /// <summary>
    /// Grids that own sorting themselves via a <c>Sorting</c> handler that sets
    /// <c>e.Handled = true</c>. Their headers drive a SERVER-side re-sort of the whole
    /// result set instead of the built-in one, so a comparer would be dead code.
    /// Keyed by grid name; the test verifies the handler attribute is really there, so
    /// deleting the handler re-arms the rule rather than silently keeping the exemption.
    /// </summary>
    private static readonly HashSet<string> SelfSortingGrids = new(StringComparer.Ordinal)
    {
        "ResultsGrid",   // ValueSearchPanel — windowed result set, ValueSearchViewModel.ApplyColumnSort
    };

    [Fact]
    public void Every_user_sortable_XAML_column_is_binding_rooted_or_has_a_comparer()
    {
        var views = ViewsDir();
        var comparerKeys = ComparerKeysByGrid(views);

        var violations = new List<string>();
        int grids = 0, sortable = 0;

        foreach (var file in Directory.GetFiles(views, "*.axaml").OrderBy(f => f, StringComparer.Ordinal))
        {
            var baseName = Path.GetFileNameWithoutExtension(file);      // "LiveFuncsPanel.axaml" -> "LiveFuncsPanel"
            var root = XDocument.Load(file).Root!;

            foreach (var grid in root.DescendantsAndSelf().Where(e => e.Name.LocalName == "DataGrid"))
            {
                grids++;
                var gridName = (string?)grid.Attribute(Xaml + "Name") ?? (string?)grid.Attribute("Name");

                // CanUserSortColumns="False" turns the whole grid's header sorting off.
                if (string.Equals((string?)grid.Attribute("CanUserSortColumns"), "False",
                                  StringComparison.OrdinalIgnoreCase))
                    continue;

                // A grid that cancels the built-in sort and drives its own is exempt —
                // but only if the handler is actually attached right here.
                if (gridName != null && SelfSortingGrids.Contains(gridName)
                    && grid.Attribute("Sorting") != null)
                    continue;

                var keys = gridName != null && comparerKeys.TryGetValue((baseName, gridName), out var k)
                    ? k : new HashSet<string>(StringComparer.Ordinal);

                foreach (var col in grid.DescendantsAndSelf().Where(IsColumn))
                {
                    var smp = (string?)col.Attribute("SortMemberPath");
                    if (string.IsNullOrEmpty(smp)) continue;
                    if (string.Equals((string?)col.Attribute("CanUserSort"), "False",
                                      StringComparison.OrdinalIgnoreCase))
                        continue;

                    sortable++;
                    if (keys.Contains(smp!)) continue;                  // rule (2)
                    if (BindingPath(col) == smp) continue;              // rule (1)

                    violations.Add(
                        $"{Path.GetFileName(file)} / {gridName ?? "(no x:Name — cannot be wired)"} : " +
                        $"{col.Name.LocalName} SortMemberPath=\"{smp}\" " +
                        $"Binding=\"{BindingPath(col) ?? "(none)"}\"");
                }
            }
        }

        // Guard the guard: if the parse silently matched nothing, the assertion below is
        // vacuously true and would stay green through any regression.
        Assert.True(grids >= 30, $"expected the sweep to find the repo's DataGrids, saw {grids}");
        Assert.True(sortable >= 120, $"expected >=120 user-sortable columns, saw {sortable}");

        Assert.True(violations.Count == 0,
            "These DataGrid columns are user-sortable but their sort path is neither rooted by the " +
            "column's own Binding nor covered by a wired comparer, so the header is inert in the " +
            "shipped trimmed build. Add an entry to the panel's *SortComparers dictionary (and an " +
            "x:Name + WireSortComparers call if the grid has none):\n  " +
            string.Join("\n  ", violations));
    }

    /// <summary>
    /// The code-built half. <c>FunctionPropsDialog</c>, <c>PropertyXrefDialog</c> and
    /// <c>ObjectInstancePickerDialog</c> build their grids in C#, so the XAML sweep above
    /// cannot see them — and all three shipped unwired. Every
    /// <c>SortMemberPath = nameof(T.P)</c> in a View must have a <c>["P"]</c> comparer
    /// entry in the same file.
    /// </summary>
    [Fact]
    public void Every_code_built_sortable_column_has_a_comparer_in_its_own_file()
    {
        var violations = new List<string>();
        int seen = 0;

        foreach (var file in Directory.GetFiles(ViewsDir(), "*.cs").OrderBy(f => f, StringComparer.Ordinal))
        {
            var text = File.ReadAllText(file);
            var paths = Regex.Matches(text, @"SortMemberPath\s*=\s*nameof\(\s*\w+\.(\w+)\s*\)")
                             .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
            if (paths.Count == 0) continue;

            var keys = Regex.Matches(text, @"\[\s*""([^""]+)""\s*\]\s*=\s*(?:Helpers\.)?DataGridSortComparers\.")
                            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

            foreach (var p in paths)
            {
                seen++;
                if (!keys.Contains(p))
                    violations.Add($"{Path.GetFileName(file)} : SortMemberPath \"{p}\" has no comparer entry");
            }
        }

        Assert.True(seen >= 15, $"expected the code-built dialogs' sortable columns, saw {seen}");
        Assert.True(violations.Count == 0,
            "Code-built DataGrid columns with no AOT comparer — the header is inert in the shipped " +
            "trimmed build:\n  " + string.Join("\n  ", violations));
    }

    // ---- helpers --------------------------------------------------------------

    private static bool IsColumn(XElement e) =>
        e.Name.LocalName.StartsWith("DataGrid", StringComparison.Ordinal) &&
        e.Name.LocalName.EndsWith("Column", StringComparison.Ordinal);

    /// <summary>
    /// The property path of the column's own <c>Binding</c>, or null when there isn't a
    /// simple one.
    ///
    /// <para>Trailing binding options are allowed and do not change the answer:
    /// <c>{Binding Offset, StringFormat=0x{0:X}}</c> still roots <c>Offset</c> — the
    /// format is applied to the value the compiled binding already fetched. Rejecting
    /// those was this test's own first bug, and it disagreed with the independent Python
    /// sweep on exactly three columns (working-lessons.md §1.4).</para>
    ///
    /// <para>Deliberately null for element-syntax bindings such as
    /// <c>&lt;MultiBinding&gt;</c>: the attribute is simply absent, those are reflection
    /// bindings, and they do NOT root the property — which is precisely what made Live
    /// Walker's "Params" column (AF20) look safe while being dead.</para>
    /// </summary>
    private static string? BindingPath(XElement col)
    {
        var b = (string?)col.Attribute("Binding");
        if (b == null) return null;
        var m = Regex.Match(b.Trim(),
            @"^\{\s*(?:Compiled)?Binding\s+(?:Path=)?([A-Za-z_][\w.]*)\s*(?:,[\s\S]*)?\}$");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// (panel, gridName) -&gt; the SortMemberPaths its wired comparer dictionary covers,
    /// read out of the panel's code-behind.
    /// </summary>
    private static Dictionary<(string, string), HashSet<string>> ComparerKeysByGrid(string viewsDir)
    {
        var map = new Dictionary<(string, string), HashSet<string>>();

        foreach (var cs in Directory.GetFiles(viewsDir, "*.axaml.cs"))
        {
            var text = File.ReadAllText(cs);
            var panel = Path.GetFileName(cs)[..^".axaml.cs".Length];

            // Each `private static readonly IReadOnlyDictionary<string, IComparer> Name = new ... { ... };`
            var dicts = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (Match d in Regex.Matches(text,
                         @"IReadOnlyDictionary<string,\s*(?:System\.Collections\.)?IComparer>\s+(\w+)\s*=" +
                         @"[\s\S]*?\{([\s\S]*?)\n\s*\};"))
            {
                dicts[d.Groups[1].Value] = Regex.Matches(d.Groups[2].Value, @"\[\s*""([^""]+)""\s*\]")
                                                .Select(m => m.Groups[1].Value)
                                                .ToHashSet(StringComparer.Ordinal);
            }

            foreach (Match w in Regex.Matches(text,
                         @"FindControl<DataGrid>\(""(\w+)""\)\s*\?\.\s*WireSortComparers\((\w+)\)"))
            {
                map[(panel, w.Groups[1].Value)] =
                    dicts.TryGetValue(w.Groups[2].Value, out var keys)
                        ? keys : new HashSet<string>(StringComparer.Ordinal);
            }
        }
        return map;
    }

    private static string ViewsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "ui", "UE5DumpUI", "Views");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("could not locate ui/UE5DumpUI/Views from " + AppContext.BaseDirectory);
    }
}
