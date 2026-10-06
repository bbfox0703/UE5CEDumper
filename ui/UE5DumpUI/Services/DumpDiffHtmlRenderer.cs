using System.IO;
using System.Net;
using System.Text;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// [DUMPDIFF-UI] The HTML report of Dump Explorer's Compare: the sections and wording of <c>diff_dumps.py</c>'s
/// Markdown report, as one self-contained page (the maintainer chose HTML only: most users never open a .md file).
/// Every name comes from a game and is escaped. No script runs in the page.
/// </summary>
public static class DumpDiffHtmlRenderer
{
    private const string Css = """
        :root { color-scheme: light dark; --fg: #1f2328; --bg: #ffffff; --muted: #59636e; --line: #d1d9e0;
                --code: #eff2f5; --warn: #9a6700; --break: #cf222e; }
        @media (prefers-color-scheme: dark) {
          :root { --fg: #e6edf3; --bg: #0d1117; --muted: #9198a1; --line: #3d444d; --code: #1f2630;
                  --warn: #d29922; --break: #f85149; }
        }
        body { font-family: "Segoe UI", system-ui, sans-serif; color: var(--fg); background: var(--bg);
               margin: 0 auto; max-width: 1100px; padding: 16px 20px 40px; line-height: 1.45; }
        h1 { font-size: 1.6em; margin: 0.2em 0 0.6em; }
        h2 { font-size: 1.25em; border-bottom: 1px solid var(--line); padding-bottom: 0.25em; margin-top: 1.6em; }
        h3 { font-size: 1.05em; margin: 1.2em 0 0.2em; }
        code { font-family: Consolas, "Cascadia Mono", monospace; background: var(--code); padding: 0 4px;
               border-radius: 4px; overflow-wrap: anywhere; }
        table { border-collapse: collapse; margin: 0.4em 0 0.8em; }
        th, td { border: 1px solid var(--line); padding: 3px 8px; text-align: left; vertical-align: top; }
        th { background: var(--code); }
        .muted { color: var(--muted); }
        .warn { color: var(--warn); }
        .break { color: var(--break); font-weight: 600; }
        .banner { border-left: 4px solid var(--warn); padding: 6px 10px; background: var(--code); }
        footer { margin-top: 2.5em; border-top: 1px solid var(--line); padding-top: 0.6em; }
        """;

    public static string Render(DumpDiffResult diff, bool minimal)
    {
        var o = diff.OldDump;
        var n = diff.NewDump;
        var sb = new StringBuilder(64 * 1024);
        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
          .Append("<title>Dump diff: ").Append(H(FileName(o))).Append(" &#8594; ").Append(H(FileName(n))).Append("</title>\n")
          .Append("<style>\n").Append(Css).Append("</style>\n</head>\n<body>\n")
          .Append("<h1>Game Version Diff Report</h1>\n<ul>\n");
        FileLine(sb, "Old", o);
        FileLine(sb, "New", n);
        sb.Append("</ul>\n<p class=\"muted\">Engine types: ")
          .Append(diff.IncludeEngine ? "included" : "left out (the game's own types only)")
          .Append(". Report: ").Append(minimal ? "breaking changes only" : "full").Append(".</p>\n");
        if (minimal)
            sb.Append("<p class=\"banner\"><b>Minimal mode</b> — showing only the changes that break existing cheat " +
                      "tables (moved or retyped fields, signature changes, changed enum values, struct size changes). " +
                      "Added / removed entries are hidden; untick “Breaking changes only” for the full report.</p>\n");

        Summary(sb, diff);

        if (!minimal)
        {
            Listing(sb, "Added Classes", diff.AddedClasses, DumpDiffService.FailedNames(o), "old");
            Listing(sb, "Removed Classes", diff.RemovedClasses, DumpDiffService.FailedNames(n), "new");
        }
        var classes = minimal ? diff.ChangedClasses.Where(c => c.HasBreakingChange).ToList() : diff.ChangedClasses;
        sb.Append("<h2>").Append(minimal ? $"Breaking Changes ({classes.Count} class(es))" : $"Changed Classes ({classes.Count})")
          .Append("</h2>\n");
        if (classes.Count == 0) sb.Append("<p class=\"muted\"><i>No matching class diffs to report.</i></p>\n");
        foreach (var cd in classes) TypeDiff(sb, cd, minimal);

        if (diff.StructsSkipped.Length == 0) Structs(sb, diff, minimal);
        if (diff.EnumsSkipped.Length == 0) Enums(sb, diff, minimal);

        // The maintainer's request (2026-10-06): say once where the same diff lives for someone with the repository.
        sb.Append("<footer><p class=\"muted\">Repository users can run the same diff from the command line: " +
                  "<code>scripts/analysis/diff_dumps.py</code>, a Python CLI that only a clone of the repository has " +
                  "(release builds do not include it).</p></footer>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static string FileName(DumpDiffInput d) => Path.GetFileName(d.FilePath);

    private static void FileLine(StringBuilder sb, string label, DumpDiffInput d) =>
        sb.Append("<li><b>").Append(label).Append("</b>: <code>").Append(H(FileName(d))).Append("</code> (module=")
          .Append(H(d.Meta.Module ?? "?")).Append(", UE=").Append(d.UeVersion).Append(", dumper build=")
          .Append(d.DumperBuild).Append(", dumped ").Append(H(d.Meta.DumpedAt ?? "?")).Append(")</li>\n");

    private static void Summary(StringBuilder sb, DumpDiffResult d)
    {
        var c = DumpDiffService.CountChanges(d.ChangedClasses);
        sb.Append("<h2>Summary</h2>\n<ul>\n");
        Li(sb, $"Classes: {B(d.AddedClasses.Count)} added, {B(d.RemovedClasses.Count)} removed, " +
               $"{B(d.ChangedClasses.Count)} changed, {B(d.UnchangedClasses)} unchanged");
        Li(sb, $"Properties moved (offset / size): {B(c.PropMoved)} across {B(c.TypesWithMovedFields)} class(es)");
        Li(sb, $"Property type changed: {B(c.PropTypeChanged)}");
        Li(sb, $"Properties added: {B(c.PropAdded)}, removed: {B(c.PropRemoved)}");
        Li(sb, $"Function signatures changed: {B(c.FuncSignatureChanged)} across {B(c.TypesWithSigChanges)} class(es)");
        Li(sb, $"Functions added: {B(c.FuncAdded)}, removed: {B(c.FuncRemoved)}");
        Li(sb, $"Classes with props_size delta: {B(c.TypesWithSizeDelta)}");
        if (d.StructsSkipped.Length > 0)
            Li(sb, "Structs: not compared — " + H(d.StructsSkipped));
        else
        {
            var s = DumpDiffService.CountChanges(d.ChangedStructs);
            Li(sb, $"Structs: {B(d.AddedStructs.Count)} added, {B(d.RemovedStructs.Count)} removed, " +
                   $"{B(d.ChangedStructs.Count)} changed, {B(d.UnchangedStructs)} unchanged");
            Li(sb, $"Struct fields moved (offset / size): {B(s.PropMoved)}, type changed: {B(s.PropTypeChanged)}, " +
                   $"across {B(s.TypesWithMovedFields)} struct(s)");
        }
        if (d.EnumsSkipped.Length > 0)
            Li(sb, "Enums: not compared — " + H(d.EnumsSkipped));
        else
        {
            int values = d.ChangedEnums.Sum(e => e.Changes.Count(x => x.Kind == DumpDiffEnumEntryKind.ValueChanged));
            var uncompared = d.UncomparedEnums > 0
                ? $", {B(d.UncomparedEnums)} present on both sides (entries not compared)"
                : "";
            Li(sb, $"Enums: {B(d.AddedEnums.Count)} added, {B(d.RemovedEnums.Count)} removed, " +
                   $"{B(d.ChangedEnums.Count)} changed, {B(d.UnchangedEnums)} unchanged{uncompared}");
            Li(sb, $"Enumerator values changed: {B(values)} across {B(d.ChangedEnums.Count(e => e.HasBreakingChange))} enum(s)");
        }
        foreach (var note in d.DumpNotes.Concat(d.EnumNotes).Concat(d.ParamNotes))
            sb.Append("<li class=\"warn\">⚠ ").Append(H(note)).Append("</li>\n");
        sb.Append("</ul>\n");
    }

    private static void Li(StringBuilder sb, string html) => sb.Append("<li>").Append(html).Append("</li>\n");

    private static string B(long v) => "<b>" + v.ToString(System.Globalization.CultureInfo.InvariantCulture) + "</b>";

    private static void Listing(StringBuilder sb, string title, List<DumpDiffLine> records, HashSet<string>? failed,
        string side)
    {
        if (records.Count == 0) return;
        sb.Append("<h2>").Append(title).Append(" (").Append(records.Count).Append(")</h2>\n<ul>\n");
        foreach (var r in records)
        {
            sb.Append("<li><code>").Append(H(r.Name)).Append("</code> — <code>")
              .Append(H(DumpDiffService.NormalizePath(r.Path))).Append("</code>");
            if (failed is not null && r.Name is not null && failed.Contains(r.Name))
                sb.Append(" <span class=\"warn\">(its walk failed in the ").Append(side).Append(" dump)</span>");
            sb.Append("</li>\n");
        }
        sb.Append("</ul>\n");
    }

    private static void Structs(StringBuilder sb, DumpDiffResult d, bool minimal)
    {
        List<DumpDiffTypeChange> emit;
        if (!minimal)
        {
            Listing(sb, "Added Structs", d.AddedStructs, DumpDiffService.FailedNames(d.OldDump), "old");
            Listing(sb, "Removed Structs", d.RemovedStructs, DumpDiffService.FailedNames(d.NewDump), "new");
            emit = d.ChangedStructs;
            sb.Append("<h2>Changed Structs (").Append(emit.Count).Append(")</h2>\n");
        }
        else
        {
            emit = d.ChangedStructs.Where(c => c.HasBreakingChange).ToList();
            sb.Append("<h2>Breaking Struct Changes (").Append(emit.Count).Append(" struct(s))</h2>\n");
        }
        if (emit.Count == 0) sb.Append("<p class=\"muted\"><i>No matching struct diffs to report.</i></p>\n");
        foreach (var cd in emit) TypeDiff(sb, cd, minimal);
    }

    private static void Enums(StringBuilder sb, DumpDiffResult d, bool minimal)
    {
        List<DumpDiffEnumChange> emit;
        if (!minimal)
        {
            Listing(sb, "Added Enums", d.AddedEnums, null, "old");
            Listing(sb, "Removed Enums", d.RemovedEnums, null, "new");
            emit = d.ChangedEnums;
            sb.Append("<h2>Changed Enums (").Append(emit.Count).Append(")</h2>\n");
        }
        else
        {
            emit = d.ChangedEnums.Where(e => e.HasBreakingChange).ToList();
            sb.Append("<h2>Changed Enum Values (").Append(emit.Count).Append(" enum(s))</h2>\n");
        }
        if (emit.Count == 0) sb.Append("<p class=\"muted\"><i>No matching enum diffs to report.</i></p>\n");
        foreach (var e in emit)
        {
            sb.Append("<h3><code>").Append(H(e.Name)).Append("</code></h3>\n<p class=\"muted\">Path: <code>")
              .Append(H(e.Path)).Append("</code></p>\n");
            var values = e.Changes.Where(c => c.Kind == DumpDiffEnumEntryKind.ValueChanged).ToList();
            if (values.Count > 0)
            {
                sb.Append("<table>\n<tr><th>Enumerator</th><th>Old value → New</th></tr>\n");
                foreach (var c in values)
                    sb.Append("<tr><td><code>").Append(H(c.Name)).Append("</code></td><td class=\"break\">")
                      .Append(DumpDiffService.FormatNumber(c.OldValue)).Append(" → ")
                      .Append(DumpDiffService.FormatNumber(c.NewValue)).Append("</td></tr>\n");
                sb.Append("</table>\n");
            }
            if (minimal) continue;
            EnumRows(sb, "Added enumerators", e.Changes.Where(c => c.Kind == DumpDiffEnumEntryKind.Added), c => c.NewValue);
            EnumRows(sb, "Removed enumerators", e.Changes.Where(c => c.Kind == DumpDiffEnumEntryKind.Removed), c => c.OldValue);
        }
    }

    private static void EnumRows(StringBuilder sb, string title, IEnumerable<DumpDiffEnumEntryChange> rows,
        Func<DumpDiffEnumEntryChange, long?> value)
    {
        var list = rows.ToList();
        if (list.Count == 0) return;
        sb.Append("<p><b>").Append(title).Append(" (").Append(list.Count).Append(")</b>:</p>\n<ul>\n");
        foreach (var c in list)
            sb.Append("<li><code>").Append(H(c.Name)).Append("</code> = ").Append(DumpDiffService.FormatNumber(value(c)))
              .Append("</li>\n");
        sb.Append("</ul>\n");
    }

    /// <summary>One changed class or struct: its moved and retyped fields, its signature changes, and (outside the
    /// minimal report) its added and removed members.</summary>
    private static void TypeDiff(StringBuilder sb, DumpDiffTypeChange cd, bool minimal)
    {
        sb.Append("<h3><code>").Append(H(cd.Name)).Append("</code></h3>\n<p class=\"muted\">Path: <code>")
          .Append(H(cd.Path)).Append("</code>");
        if (cd.PropsSizeDelta != 0)
            sb.Append("<br>props_size: ").Append(cd.OldSize).Append(" → ").Append(cd.NewSize).Append(" (")
              .Append(cd.PropsSizeDelta > 0 ? "+" : "").Append(cd.PropsSizeDelta).Append(')');
        sb.Append("</p>\n");

        var moved = cd.PropChanges.Where(p => p.Kind == DumpDiffPropKind.Moved).ToList();
        var retyped = cd.PropChanges.Where(p => p.Kind == DumpDiffPropKind.TypeChanged).ToList();
        var addedProps = cd.PropChanges.Where(p => p.Kind == DumpDiffPropKind.Added).ToList();
        var removedProps = cd.PropChanges.Where(p => p.Kind == DumpDiffPropKind.Removed).ToList();
        var sig = cd.FuncChanges.Where(f => f.Kind == DumpDiffFuncKind.SignatureChanged).ToList();
        var addedFuncs = cd.FuncChanges.Where(f => f.Kind == DumpDiffFuncKind.Added).ToList();
        var removedFuncs = cd.FuncChanges.Where(f => f.Kind == DumpDiffFuncKind.Removed).ToList();

        if (moved.Count > 0)
        {
            sb.Append("<p><b>Moved fields (").Append(moved.Count)
              .Append(")</b> — <span class=\"break\">these break existing cheat tables</span>:</p>\n")
              .Append("<table>\n<tr><th>Field</th><th>Old offset → New</th><th>Old size → New</th></tr>\n");
            foreach (var pc in moved)
                sb.Append("<tr><td><code>").Append(H(pc.Name)).Append("</code></td><td>")
                  .Append(DumpDiffService.FormatOffset(pc.Old?.Offset)).Append(" → ")
                  .Append(DumpDiffService.FormatOffset(pc.New?.Offset)).Append("</td><td>")
                  .Append(DumpDiffService.FormatNumber(pc.Old?.Size)).Append(" → ")
                  .Append(DumpDiffService.FormatNumber(pc.New?.Size)).Append("</td></tr>\n");
            sb.Append("</table>\n");
        }

        if (retyped.Count > 0)
        {
            sb.Append("<p><b>Property type changed (").Append(retyped.Count).Append(")</b>:</p>\n<ul>\n");
            foreach (var pc in retyped)
                sb.Append("<li><code>").Append(H(pc.Name)).Append("</code> @ ")
                  .Append(DumpDiffService.FormatOffset(pc.Old?.Offset)).Append(": ")
                  .Append(H(DumpDiffService.FormatPropType(pc.Old!))).Append(" → ")
                  .Append(H(DumpDiffService.FormatPropType(pc.New!))).Append("</li>\n");
            sb.Append("</ul>\n");
        }

        if (sig.Count > 0)
        {
            sb.Append("<p><b>Function signatures changed (").Append(sig.Count).Append(")</b>:</p>\n")
              .Append("<table>\n<tr><th>Func</th><th>return</th><th>num_parms</th><th>parms_size</th><th>flags</th></tr>\n");
            foreach (var fc in sig)
            {
                var a = fc.Old!;
                var b = fc.New!;
                sb.Append("<tr><td><code>").Append(H(fc.Name)).Append("</code></td><td>")
                  .Append(Cell(a.ReturnType, b.ReturnType)).Append("</td><td>")
                  .Append(Cell(a.NumParms, b.NumParms)).Append("</td><td>")
                  .Append(Cell(a.ParmsSize, b.ParmsSize)).Append("</td><td>")
                  .Append(Cell(a.Flags, b.Flags)).Append("</td></tr>\n");
            }
            sb.Append("</table>\n");
            var paramRows = sig.Where(fc => DumpDiffService.ParamsDiffer(fc.Old, fc.New)).ToList();
            if (paramRows.Count > 0)
            {
                sb.Append("<ul>\n");
                foreach (var fc in paramRows)
                    sb.Append("<li><code>").Append(H(fc.Name)).Append("</code> parameters: <code>")
                      .Append(H(DumpDiffService.FormatParams(fc.Old!))).Append("</code> → <code>")
                      .Append(H(DumpDiffService.FormatParams(fc.New!))).Append("</code></li>\n");
                sb.Append("</ul>\n");
            }
        }

        if (minimal) return;

        if (addedProps.Count > 0)
        {
            sb.Append("<p><b>Added properties (").Append(addedProps.Count).Append(")</b>:</p>\n<ul>\n");
            foreach (var pc in addedProps)
                sb.Append("<li><code>").Append(H(pc.Name)).Append("</code> (").Append(H(DumpDiffService.FormatPropType(pc.New!)))
                  .Append(") @ ").Append(DumpDiffService.FormatOffset(pc.New!.Offset)).Append(" (")
                  .Append(DumpDiffService.FormatNumber(pc.New.Size)).Append("B)</li>\n");
            sb.Append("</ul>\n");
        }
        if (removedProps.Count > 0)
        {
            sb.Append("<p><b>Removed properties (").Append(removedProps.Count).Append(")</b>:</p>\n<ul>\n");
            foreach (var pc in removedProps)
                sb.Append("<li><code>").Append(H(pc.Name)).Append("</code> (").Append(H(DumpDiffService.FormatPropType(pc.Old!)))
                  .Append(") @ ").Append(DumpDiffService.FormatOffset(pc.Old!.Offset)).Append("</li>\n");
            sb.Append("</ul>\n");
        }
        FuncList(sb, "Added functions", addedFuncs, f => f.New!);
        FuncList(sb, "Removed functions", removedFuncs, f => f.Old!);
    }

    private static void FuncList(StringBuilder sb, string title, List<DumpDiffFuncChange> funcs,
        Func<DumpDiffFuncChange, DumpDiffFunc> side)
    {
        if (funcs.Count == 0) return;
        sb.Append("<p><b>").Append(title).Append(" (").Append(funcs.Count).Append(")</b>:</p>\n<ul>\n");
        foreach (var fc in funcs)
        {
            var f = side(fc);
            sb.Append("<li><code>").Append(H(fc.Name)).Append("</code> (return=")
              .Append(H(string.IsNullOrEmpty(f.ReturnType) ? "void" : f.ReturnType)).Append(", num_parms=")
              .Append(DumpDiffService.FormatNumber(f.NumParms)).Append(", parms_size=")
              .Append(DumpDiffService.FormatNumber(f.ParmsSize)).Append(")</li>\n");
        }
        sb.Append("</ul>\n");
    }

    /// <summary>"old → new" when the two differ, else the one value; "?" for a key the dump lacks.</summary>
    private static string Cell(string? a, string? b) =>
        a != b ? $"<span class=\"break\">{H(a ?? "?")} → {H(b ?? "?")}</span>" : H(a ?? "?");

    private static string Cell(long? a, long? b) =>
        a != b
            ? $"<span class=\"break\">{DumpDiffService.FormatNumber(a)} → {DumpDiffService.FormatNumber(b)}</span>"
            : DumpDiffService.FormatNumber(a);
}
