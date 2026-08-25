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

    /// <summary>
    /// The SECOND rule, and the one the first structurally cannot see: <b>a column can be
    /// perfectly AOT-safe and still sort by the wrong key.</b>
    ///
    /// <para>The test above asks "does something root this sort path, or is a comparer
    /// wired?". A <c>DataGridTextColumn</c> that binds <c>ParamsLabel</c> and sorts
    /// <c>ParamsLabel</c> answers yes — the compiled binding roots the property, the header
    /// works in the trimmed build, nothing is inert. It also sorts <c>"11 (72B)"</c> above
    /// <c>"2 (9B)"</c>, because <c>ParamsLabel</c> is
    /// <c>$"{NumParms} ({ParmsSize}B)"</c> and ordinal order compares the first character.</para>
    ///
    /// <para><b>That is why three of these survived audit #5 AF20.</b> The sweep asked
    /// whether headers were inert under trimming; Live Walker's "Params" was (its value
    /// comes from an element-syntax <c>MultiBinding</c>, which roots nothing) and was fixed,
    /// while <c>ConsolePanel</c>, <c>InterestingFunctionsPanel</c> and <c>LiveFuncsPanel</c>
    /// were not inert — just wrong — and passed. <c>LiveFuncsPanel.axaml.cs</c>'s own comment
    /// listed Params among the columns that "are rooted and need nothing", which was true and
    /// beside the point. Fixed 2026-08-22, <c>[PARAMSSORT-2026-08-22]</c>.</para>
    ///
    /// <para><b>The rule enforced here:</b> no column may declare a
    /// <c>SortMemberPath</c> naming a computed <c>string</c> property whose expression
    /// interpolates a <b>numeric</b> member declared in the same model file. Sort the number
    /// and let the cell render the label.</para>
    ///
    /// <para><b>Known imprecision, stated rather than hidden:</b> markup does not name the
    /// grid's item type, so the label is matched by property NAME across all of
    /// <c>Models/</c>. Two different models can share a name — <c>Display</c> is
    /// <c>$"{ClassName}  ({InstanceCount:N0})"</c> in <c>PivotModels</c> (numeric, would be
    /// wrong to sort) and <c>"Name : ClassName"</c> in <c>RelatedObject</c> (purely textual,
    /// correct to sort). Collisions go in the exemption set below <b>with the reason</b>,
    /// and an exemption that stops being hit fails the test, so they cannot go stale.</para>
    /// </summary>
    private static readonly Dictionary<string, string> NumericLabelSortExemptions =
        new(StringComparer.Ordinal)
        {
            // Live Walker / Instance Finder "Value" — a HETEROGENEOUS column. LiveFieldValue
            // .DisplayValue is a fallback chain (FDateTime decode, TypedValue, pointer
            // "Name (Class)", "{StructType}", array/map/set counts, DataTable row count, raw
            // hex). Only some branches interpolate a number and they are not the same number,
            // so there is no numeric key to sort on; ordinal is the only order that exists.
            // LiveWalkerPanel.axaml.cs:31 wires Ordinal deliberately. What the scan actually
            // caught is one branch of the chain, not a formatted-number column.
            ["LiveWalkerPanel.axaml|DisplayValue"] =
                "LiveFieldValue.DisplayValue is a heterogeneous fallback chain; no single " +
                "numeric key exists, and Ordinal is wired on purpose.",
            ["InstanceFinderPanel.axaml|DisplayValue"] =
                "Same property, same column, in the Instance Finder's field grid.",

            ["RelatedObjectsPanel.axaml|Display"] =
                "RelatedObject.Display is \"Name : ClassName\" — no numeric part, so ordinal " +
                "order is the correct order. The numeric-composite Display is PivotModels', a " +
                "different model this scan cannot tell apart from markup alone.",
        };

    [Fact]
    public void No_column_sorts_on_a_label_that_formats_a_number()
    {
        var labels = NumericCompositeLabels(ModelsDir());

        // Guard the guard. If the regexes stop matching — a C# syntax the pattern does not
        // know, a Models/ reorganisation — `labels` goes empty and every column passes
        // vacuously. This is the assertion that makes the test able to fail.
        Assert.True(labels.Count >= 8,
            $"only {labels.Count} numeric-composite label(s) found in Models/ — the scan has " +
            "probably stopped matching, and an empty set passes everything. Expected the " +
            "known population (17 declarations over 12 names as of 2026-08-22).");
        Assert.Contains("ParamsLabel", labels.Keys);
        Assert.Contains("OffsetHex", labels.Keys);

        var violations = new List<string>();
        var exemptionsHit = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(ViewsDir(), "*.axaml"))
        {
            var name = Path.GetFileName(file);
            XDocument doc;
            try { doc = XDocument.Load(file); }
            catch (System.Xml.XmlException) { continue; }

            foreach (var col in doc.Descendants().Where(IsColumn))
            {
                var path = (string?)col.Attribute("SortMemberPath");
                if (path == null || !labels.TryGetValue(path, out var declaredAt)) continue;

                var key = $"{name}|{path}";
                if (NumericLabelSortExemptions.ContainsKey(key)) { exemptionsHit.Add(key); continue; }

                var header = (string?)col.Attribute("Header") ?? "?";
                violations.Add(
                    $"{name}  Header=\"{header}\"  SortMemberPath=\"{path}\" — that property " +
                    $"formats a number ({declaredAt}). Sort the numeric member instead and wire " +
                    "a comparer for it, or add an exemption WITH A REASON to " +
                    "NumericLabelSortExemptions.");
            }
        }

        Assert.True(violations.Count == 0,
            "Column(s) sorting on a label that formats a number:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));

        // A stale exemption is a silent hole. If the column moved or was fixed, drop the entry.
        var stale = NumericLabelSortExemptions.Keys.Except(exemptionsHit).ToList();
        Assert.True(stale.Count == 0,
            "Exemption(s) no longer matched by any column — delete them: " + string.Join(", ", stale));
    }

    /// <summary>
    /// The THIRD rule, and the sibling of the one above: <b>a column whose cell renders a
    /// number as text must not be sorted with a string comparer</b> — even when a comparer is
    /// correctly wired, which is what the AOT rule checks and what makes this invisible to it.
    ///
    /// <para>An <c>Ordinal</c> comparer on an address is right often enough to look right.
    /// Equal-length UPPERCASE hex compares identically ordinally and numerically, so on a host
    /// whose pool addresses are all the same width nothing misbehaves — measured on DumperTest
    /// 2026-08-22: all 137 <c>Object</c> instances 13 characters. That is a property of the
    /// heap's layout, not of the comparer. One 12-character address in the set (a static
    /// <c>FUObjectArray</c>, a <c>0x7FF…</c> module-resident object) and the order is decided by
    /// the first character.</para>
    ///
    /// <para>The tree already knew the answer: <c>DataGridSortComparers.Hex&lt;T&gt;(ulong)</c>
    /// exists, and <c>RelatedObjectsPanel.axaml.cs:22</c> was its <b>only</b> user while
    /// <c>ObjectInstancePickerDialog.cs</c>'s identically-named "Address" column used
    /// <c>Ordinal</c>. Two panels, one column name, two answers, one of them the documented one.
    /// Fixed 2026-08-22 with <c>[PARAMSSORT-2026-08-22]</c>.</para>
    /// </summary>
    private static readonly Dictionary<string, string> OrdinalOnNumericTextExemptions =
        new(StringComparer.Ordinal);

    [Fact]
    public void No_address_column_is_sorted_with_a_string_comparer()
    {
        var suspect = new Regex(@"(?:Addr|Address|Hex|Ptr)$|^(?:Addr|Address)",
                                RegexOptions.IgnoreCase);
        var wiring = new Regex(
            @"\[\s*""([^""]+)""\s*\]\s*=\s*DataGridSortComparers\.(\w+)<",
            RegexOptions.None);

        var violations = new List<string>();
        var hit = new HashSet<string>(StringComparer.Ordinal);
        int scanned = 0;

        foreach (var cs in Directory.GetFiles(ViewsDir(), "*.cs", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(cs);
            foreach (Match m in wiring.Matches(File.ReadAllText(cs)))
            {
                scanned++;
                var key = m.Groups[1].Value;
                var factory = m.Groups[2].Value;
                if (!suspect.IsMatch(key)) continue;
                if (!string.Equals(factory, "Ordinal", StringComparison.Ordinal)) continue;

                var id = $"{name}|{key}";
                if (OrdinalOnNumericTextExemptions.ContainsKey(id)) { hit.Add(id); continue; }
                violations.Add(
                    $"{name}  [\"{key}\"] uses DataGridSortComparers.Ordinal. An address/hex " +
                    "column must sort numerically — add a `ulong` accessor on the model (see " +
                    "RelatedObject.AddressValue) and use DataGridSortComparers.Hex, or add an " +
                    "exemption WITH A REASON to OrdinalOnNumericTextExemptions.");
            }
        }

        // Guard the guard: if the wiring regex stops matching, everything passes vacuously.
        Assert.True(scanned >= 30,
            $"only {scanned} comparer wiring(s) found across Views/*.cs — the scan has probably " +
            "stopped matching, and an empty scan passes everything. Expected the known " +
            "population (43 as of 2026-08-22).");

        Assert.True(violations.Count == 0,
            "Address/hex column(s) sorted as text:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));

        var stale = OrdinalOnNumericTextExemptions.Keys.Except(hit).ToList();
        Assert.True(stale.Count == 0,
            "Exemption(s) no longer matched — delete them: " + string.Join(", ", stale));
    }

    /// <summary>
    /// name -&gt; "file:line, interpolates X, Y" for every computed <c>string</c> property in
    /// <c>Models/</c> whose expression body interpolates a numeric member declared in the
    /// same file. The same-file requirement is what keeps unrelated string labels out.
    /// </summary>
    private static Dictionary<string, string> NumericCompositeLabels(string modelsDir)
    {
        const string Num = @"(?:byte|sbyte|short|ushort|int|uint|long|ulong|float|double|decimal|nint|nuint)";
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var cs in Directory.GetFiles(modelsDir, "*.cs", SearchOption.AllDirectories))
        {
            var src = File.ReadAllText(cs);
            var numerics = Regex.Matches(src, $@"public\s+{Num}\??\s+(\w+)\s*(?:\{{|=>)")
                                .Select(m => m.Groups[1].Value)
                                .ToHashSet(StringComparer.Ordinal);
            if (numerics.Count == 0) continue;

            foreach (Match m in Regex.Matches(src, @"public\s+string\??\s+(\w+)\s*=>"))
            {
                // the expression body runs to the terminating semicolon
                var end = src.IndexOf(';', m.Index + m.Length);
                if (end < 0) continue;
                var body = src[(m.Index + m.Length)..end];
                if (!body.Contains("$\"", StringComparison.Ordinal)) continue;

                var used = numerics.Where(n => Regex.IsMatch(body, @"\{" + Regex.Escape(n) + @"\b"))
                                   .OrderBy(n => n, StringComparer.Ordinal)
                                   .ToList();
                if (used.Count == 0) continue;

                var line = src[..m.Index].Count(c => c == '\n') + 1;
                var note = $"{Path.GetFileName(cs)}:{line} interpolates {string.Join(", ", used)}";
                found[m.Groups[1].Value] = found.TryGetValue(m.Groups[1].Value, out var prev)
                    ? prev + "; " + note : note;
            }
        }
        return found;
    }

    private static string ModelsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "ui", "UE5DumpUI", "Models");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("could not locate ui/UE5DumpUI/Models from " + AppContext.BaseDirectory);
    }

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
