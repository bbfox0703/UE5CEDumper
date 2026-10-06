using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [DUMPDIFF-UI] The Dump Explorer's diff is a C# port of <c>scripts/analysis/diff_dumps.py</c>, and the script stays
/// the reference. <c>scripts/analysis/fixtures/diff_dumps/</c> holds pairs of dumps with what the script computes for
/// them (<c>expected.json</c>, its <c>canonical()</c>); the script's own self-test fails when those files no longer
/// match it. Each case is diffed here and must come out the same, so a change to either side that the other does not
/// follow fails one of the two. The canonical form below is built in the script's key order.
/// </summary>
public class DumpDiffParityTests
{
    private static string FixtureDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "analysis", "fixtures", "diff_dumps");
            if (File.Exists(Path.Combine(dir.FullName, "build.ps1")) && Directory.Exists(candidate))
                return candidate;
        }
        throw new DirectoryNotFoundException("scripts/analysis/fixtures/diff_dumps not found above the test binaries");
    }

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var d in Directory.GetDirectories(FixtureDir()).OrderBy(x => x, StringComparer.Ordinal))
            data.Add(Path.GetFileName(d));
        return data;
    }

    [Fact]
    public void The_fixture_set_is_there()
    {
        // A vacuous Theory (no case found) passes silently; the script writes 24 cases today.
        Assert.True(Cases().Count >= 24, $"only {Cases().Count} fixture case(s) found");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_port_computes_what_diff_dumps_py_computes(string caseName)
    {
        var dir = Path.Combine(FixtureDir(), caseName);
        var expected = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(dir, "expected.json"),
            TestContext.Current.CancellationToken))!;
        bool includeEngine = expected["include_engine"]!.GetValue<bool>();

        var oldDump = await DumpDiffService.LoadAsync(Path.Combine(dir, "old.jsonl"), ct: TestContext.Current.CancellationToken);
        var newDump = await DumpDiffService.LoadAsync(Path.Combine(dir, "new.jsonl"), ct: TestContext.Current.CancellationToken);
        var diff = DumpDiffService.Diff(oldDump, newDump, includeEngine);

        Assert.Equal(Text(expected["diff"]!), Text(Canonical(diff)));
    }

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Text(JsonNode node) => node.ToJsonString(Pretty);

    // ---- the script's canonical(), key for key ----

    internal static JsonObject Canonical(DumpDiffResult d)
    {
        var oldFailed = DumpDiffService.FailedNames(d.OldDump);
        var newFailed = DumpDiffService.FailedNames(d.NewDump);
        var structs = new JsonObject { ["skipped"] = d.StructsSkipped };
        var enums = new JsonObject { ["skipped"] = d.EnumsSkipped };
        var o = new JsonObject
        {
            ["classes"] = new JsonObject
            {
                ["added"] = Listing(d.AddedClasses, oldFailed),
                ["removed"] = Listing(d.RemovedClasses, newFailed),
                ["changed"] = new JsonArray(d.ChangedClasses.Select(TypeChange).ToArray<JsonNode?>()),
                ["unchanged"] = d.UnchangedClasses,
                ["counts"] = Counts(DumpDiffService.CountChanges(d.ChangedClasses)),
            },
            ["structs"] = structs,
            ["enums"] = enums,
            ["notes"] = new JsonObject
            {
                ["dump"] = Strings(d.DumpNotes),
                ["enum"] = Strings(d.EnumNotes),
                ["param"] = Strings(d.ParamNotes),
            },
        };
        if (d.StructsSkipped.Length == 0)
        {
            structs["added"] = Listing(d.AddedStructs, oldFailed);
            structs["removed"] = Listing(d.RemovedStructs, newFailed);
            structs["changed"] = new JsonArray(d.ChangedStructs.Select(TypeChange).ToArray<JsonNode?>());
            structs["unchanged"] = d.UnchangedStructs;
            structs["counts"] = Counts(DumpDiffService.CountChanges(d.ChangedStructs));
        }
        if (d.EnumsSkipped.Length == 0)
        {
            enums["added"] = Listing(d.AddedEnums, null);
            enums["removed"] = Listing(d.RemovedEnums, null);
            enums["changed"] = new JsonArray(d.ChangedEnums.Select(e => (JsonNode?)new JsonObject
            {
                ["path"] = e.Path,
                ["name"] = e.Name,
                ["breaking"] = e.HasBreakingChange,
                ["changes"] = new JsonArray(e.Changes.Select(c => (JsonNode?)new JsonObject
                {
                    ["name"] = c.Name,
                    ["kind"] = EnumEntryKind(c.Kind),
                    ["old"] = c.OldValue,
                    ["new"] = c.NewValue,
                }).ToArray()),
            }).ToArray());
            enums["unchanged"] = d.UnchangedEnums;
            enums["uncompared"] = d.UncomparedEnums;
        }
        return o;
    }

    private static JsonArray Strings(IEnumerable<string> s) => new(s.Select(x => (JsonNode?)x).ToArray());

    private static JsonArray Listing(List<DumpDiffLine> records, HashSet<string>? failed) =>
        new(records.Select(r =>
        {
            var row = new JsonObject
            {
                ["path"] = DumpDiffService.NormalizePath(r.Path),
                ["name"] = r.Name ?? "",
            };
            if (failed is not null) row["walk_failed"] = r.Name is not null && failed.Contains(r.Name);
            return (JsonNode?)row;
        }).ToArray());

    private static JsonNode TypeChange(DumpDiffTypeChange cd) => new JsonObject
    {
        ["path"] = cd.Path,
        ["name"] = cd.Name,
        ["old_size"] = cd.OldSize,
        ["new_size"] = cd.NewSize,
        ["breaking"] = cd.HasBreakingChange,
        ["props"] = new JsonArray(cd.PropChanges.Select(pc => (JsonNode?)new JsonObject
        {
            ["name"] = pc.Name,
            ["kind"] = PropKind(pc.Kind),
            ["old"] = Prop(pc.Old),
            ["new"] = Prop(pc.New),
        }).ToArray()),
        ["funcs"] = new JsonArray(cd.FuncChanges.Select(fc => (JsonNode?)new JsonObject
        {
            ["name"] = fc.Name,
            ["kind"] = FuncKind(fc.Kind),
            ["old"] = Func(fc.Old),
            ["new"] = Func(fc.New),
            ["params_changed"] = DumpDiffService.ParamsDiffer(fc.Old, fc.New),
        }).ToArray()),
    };

    private static JsonNode? Prop(DumpDiffProp? p) => p is null ? null : new JsonObject
    {
        ["offset"] = p.Offset,
        ["size"] = p.Size,
        ["type"] = DumpDiffService.FormatPropType(p),
    };

    private static JsonNode? Func(DumpDiffFunc? f) => f is null ? null : new JsonObject
    {
        ["return_type"] = f.ReturnType,
        ["num_parms"] = f.NumParms,
        ["parms_size"] = f.ParmsSize,
        ["flags"] = f.Flags,
        ["params"] = f.Params is null ? null : DumpDiffService.FormatParams(f),
    };

    private static JsonObject Counts(DumpDiffCounts c) => new()
    {
        ["prop_added"] = c.PropAdded,
        ["prop_removed"] = c.PropRemoved,
        ["prop_moved"] = c.PropMoved,
        ["prop_type_changed"] = c.PropTypeChanged,
        ["func_added"] = c.FuncAdded,
        ["func_removed"] = c.FuncRemoved,
        ["func_signature_changed"] = c.FuncSignatureChanged,
        ["classes_with_moved_fields"] = c.TypesWithMovedFields,
        ["classes_with_sig_changes"] = c.TypesWithSigChanges,
        ["classes_with_size_delta"] = c.TypesWithSizeDelta,
    };

    private static string PropKind(DumpDiffPropKind k) => k switch
    {
        DumpDiffPropKind.Added => "added",
        DumpDiffPropKind.Removed => "removed",
        DumpDiffPropKind.Moved => "moved",
        _ => "type_changed",
    };

    private static string FuncKind(DumpDiffFuncKind k) => k switch
    {
        DumpDiffFuncKind.Added => "added",
        DumpDiffFuncKind.Removed => "removed",
        _ => "signature_changed",
    };

    private static string EnumEntryKind(DumpDiffEnumEntryKind k) => k switch
    {
        DumpDiffEnumEntryKind.Added => "added",
        DumpDiffEnumEntryKind.Removed => "removed",
        _ => "value_changed",
    };
}
