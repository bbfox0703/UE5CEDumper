// Parse the SAME cooked assets under TWO .usmap mappings and diff every property reading.
//
//   dotnet run -c Release --project tools/verify/usmap_probe -- \
//       <paks-dir> <a.usmap> <b.usmap> [maxExports] [pathFilter] [EGame] [showKey]
//
// Two more modes check ONE mapping against the game's own cooked schema (see "WHY `diff` CANNOT
// MEASURE A USERDEFINEDSTRUCT" below):
//
//   ... -- --mode uds     <paks-dir> <mapping.usmap> [maxPackages] [pathFilter] [EGame]
//   ... -- --mode holders <paks-dir> <mapping.usmap> [maxPackages] [pathFilter] [EGame]
//
// Options, accepted anywhere on the line:
//   --oodle <oo2core_9_win64.dll>  (or env USMAP_PROBE_OODLE) a LOCAL native Oodle library,
//                                  in place of CUE4Parse's managed port. Nothing is downloaded.
//   --errors N                     failure / log-error messages printed per run (default 10)
//   --list N                       per-verdict lines printed by uds / holders (default 20)
//   --control                      uds / holders POSITIVE CONTROL: every entry the mapping lacks --
//                                  the probed struct, its Blueprint parents, and each non-native
//                                  struct a member names -- is synthesized from the cooked schema
//                                  the way a .usmap spells it: types by NAME, with no loaded
//                                  UStruct / UEnum, so a nested type resolves through the mapping
//                                  exactly as it would from a real file. It proves the harness
//                                  (lookup by name, seek-back, a deterministic comparison, a schema
//                                  check that passes an identical entry) and NOTHING about any
//                                  .usmap -- run it once before trusting a SAME from a real one.
//                                  Enums are NOT synthesized: a member whose enum the mapping lacks
//                                  renders as a number on the mapping side, so that row is DIFFER
//                                  with [ENUM-MISSING:X] -- what a real mapping without X reads.
//   --selftest                     uds NEGATIVE CONTROL for the schema check: every UDS's control
//                                  entry is installed with ONE descriptor changed at a time, and
//                                  each such entry must come out SCHEMA-DIFFER whatever the reading
//                                  says. It counts the changes the reading ALONE calls SAME.
//
// uds / holders VERDICTS. A static check runs first and wins: SCHEMA-DIFFER when the mapping's
// descriptors -- name, array size, type recursively, after the cooked-only type names are folded
// onto the ones a .usmap can spell -- differ from the cooked schema's at ANY index, serialized or
// not; SCHEMA-FAILED when the check itself threw. Otherwise the reading decides: SAME (identical,
// and >= 1 property consumed bytes), SAME-ZERO-ONLY (identical, every listed property zero-mask),
// EMPTY (the header listed nothing, so nothing was compared), DIFFER, FAILED, RENDER-FAILED, and
// NESTED-MISSING(X) (the mapping lacks a type X that a member names). Before either: NO-MAPPINGS,
// MAPPING-MISSING, COOKED-FAILED, and COOKED-NEEDS-MAPPING(X) -- the cooked read itself reached a
// native X the mapping lacks, so there is no reference to compare against.
//
// `showKey` prints the actual JSON READING of every export whose key contains it, under
// both mappings. Agreement is not the whole question: L30 and L39 ask what a consumer
// READS -- "the elements show enum NAMES", "the property after it stays aligned" -- and
// two mappings can agree on a wrong answer.
//
// ⛔ NOT A GATE, and every path is an argument with no default. The inputs are a game's
// cooked content and two exports under the gitignored out/ -- none of it is in the repo, so a
// gate built on this would fail CI today and rot the moment a game is patched or uninstalled.
// See tools/verify/usmap_reader.py's header.
//
// WHY A CONSOLE AND NOT FModel. CUE4Parse IS FModel's parsing engine and this drives it
// through the same entry points (DefaultFileProvider + FileUsmapTypeMappingsProvider), so the
// reading is the same one FModel would show -- but scriptable and diffable, which is what
// turns "load it in FModel and look" into 40,000 exports compared automatically. What is lost:
// a human eye on the property tree, and the fact that FModel pins its own CUE4Parse revision.
// Say which you used; they are not the same claim.
//
// ⭐ THE VERSIONED-COOK GUARD IS THE POINT OF THE PKG_UnversionedProperties COUNT. A versioned
// cook never consults a .usmap at all, so ANY two mappings "agree" over it and the run reports
// a flawless pass while measuring nothing. UE 4.27 ships
// CanUseUnversionedPropertySerialization=False in BaseEngine.ini, so this is not hypothetical.
// The per-package count makes it announce itself instead.
//
// ⭐ PAIR IT WITH usmap_rewrite.py. Diffing two different writers' exports cannot isolate one
// descriptor change -- they also disagree on schema indices ([USMAP-INHERITED-DUPES]), struct
// membership and compression. Rewrite ONE file (identity / degrade / flipwidth / promote) and
// pass the original and the rewrite here: everything else is identical and cancels, so a
// difference is attributable to exactly the bytes that were changed.
//
// ⚠ A DIFFERENCE IS NOT AUTOMATICALLY THE MAPPING'S DOING, unless the two inputs differ ONLY
// in the mapping. CUE4Parse renders some structs with its own native reader and never consults
// the .usmap for them -- measured on the DumperTest fixture, thousands of cooked
// EPropertyBagContainerType names come out of the native reader, and PropertyBagContainerTypes
// has 0 records in either mapping. Reading those as "the elements show enum names" would have
// measured CUE4Parse, not us.
//
// ⭐ WHY `diff` CANNOT MEASURE A USERDEFINEDSTRUCT. CUE4Parse looks a type up in the .usmap by
// NAME only when it has no cooked schema for it: DeserializePropertiesUnversioned consults the
// mapping for a UScriptClass (a native type, known only by name) and wraps anything loaded from
// a package -- a UserDefinedStruct, a Blueprint class -- in a SerializedStruct built from that
// package's own cooked FProperty list. A member typed as a UDS carries the LOADED UStruct
// (PropertyType.Struct), so it never reaches the mapping either. Two mappings that differ only
// in their UDS entries therefore read byte-identically, and `diff` reports a flawless pass that
// measured nothing. `uds` and `holders` force the other path on the SAME bytes -- once with the
// cooked schema, once by NAME (FStructFallback(Ar, string) -> UScriptClass -> the mapping) --
// and compare. The cooked read is the reference: it is what the game itself serialized against.
//
// ⭐ WHY A SAME READING IS NOT ENOUGH, AND THE SCHEMA CHECK EXISTS. An unversioned header lists
// only serialized properties, so a descriptor for a SKIPPED one is exercised only as an index
// slot; a ZERO-MASK property consumes no bytes, so its type is never exercised; and a wrong type
// of the right width (Int for UInt32, Object for Weak) decodes the same value. All three read
// identically under a wrong descriptor -- measured on EVERSPACE 2's UDS with one descriptor
// flipped at a time, 66 of 157 flips came out SAME: 59 zero-mask, a valued Int->UInt32 and six
// valued Array<Object/Class>->Array<Weak>. The schema check compares the descriptors themselves
// at every index, so none of that depends on what this instance happened to serialize.
//
// ⚠ WHAT THE uds / holders COMPARISON STILL CANNOT SEE. (1) The schema check compares descriptors
// as CUE4Parse builds them from the cooked FProperty list: PropertyType(FProperty) keeps an
// EnumProperty's underlying width only when it is 4 bytes, and SerializedStruct mis-indexes a
// static array (ArrayDim > 1). Neither occurs in a UserDefinedStruct. (2) It recurses into a
// member's struct type only when that type is non-native; native types reached from either read
// (a Vector member, a native parent class) go through the SAME mapping or native reader on both
// sides and cancel, so a wrong descriptor there reads identically wrong twice. (3) `uds` looks the
// UDS up by its COOKED object name; a consumer reading a holder looks it up by whatever name the
// mapping's own member descriptor spells, so a mapping that is internally consistent but names a
// UDS differently is MAPPING-MISSING here and a StructProperty<name> SCHEMA-DIFFER in `holders`
// -- run both.
using CUE4Parse;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

string mode = "diff";
string? oodle = Environment.GetEnvironmentVariable("USMAP_PROBE_OODLE");
var positional = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    string a0 = args[i];
    if (!a0.StartsWith("--", StringComparison.Ordinal))
    {
        positional.Add(a0);
        continue;
    }
    if (a0 == "--control")
    {
        Common.Control = true;
        continue;
    }
    if (a0 == "--selftest")
    {
        Common.SelfTest = true;
        continue;
    }
    if (i + 1 >= args.Length)
    {
        Console.Error.WriteLine($"{a0} needs a value");
        return 2;
    }
    string v = args[++i];
    switch (a0)
    {
        case "--mode": mode = v; break;
        case "--oodle": oodle = v; break;
        case "--errors": Common.ShowErrors = int.Parse(v); break;
        case "--list": Common.ListPerVerdict = int.Parse(v); break;
        default:
            Console.Error.WriteLine($"unknown option {a0}");
            return 2;
    }
}
args = positional.ToArray();

// Without --oodle, CUE4Parse decodes Oodle with OodleSharp, the managed port it bundles; the
// native library is the reference decoder, so switching to it rules the port out whenever a
// package fails to decompress. (EVERSPACE 2's IoStore blocks are overwhelmingly Oodle and the
// uds run read identically both ways, measured 2026-09-27.)
// OodleHelper.Initialize(string) is NOT used: when the file it is handed does not exist it
// DOWNLOADS one from GitHub (DownloadOodleDllAsync). Constructing the native wrapper from a path
// that was checked first cannot reach the network.
if (!string.IsNullOrEmpty(oodle))
{
    if (!File.Exists(oodle))
    {
        Console.Error.WriteLine($"--oodle / USMAP_PROBE_OODLE: no such file: {oodle}");
        return 2;
    }
    CUE4Parse.Compression.OodleHelper.Initialize(new OodleDotNet.Oodle(oodle));
    Console.WriteLine($"oodle: native, {oodle}");
}
else
{
    Console.WriteLine("oodle: CUE4Parse's managed port (OodleSharp)");
}

// A typo in EGame used to fall back to GAME_UE5_4 SILENTLY, and a wrong engine version parses
// unversioned data with the wrong layout rules -- a failure that reads as the mapping's fault.
bool TryGame(string[] a, int index, out EGame g)
{
    g = EGame.GAME_UE5_4;
    if (a.Length <= index) return true;
    if (Enum.TryParse(a[index], ignoreCase: false, out g) && !int.TryParse(a[index], out _)) return true;
    Console.Error.WriteLine($"unknown EGame '{a[index]}' (expected a CUE4Parse EGame name, e.g. GAME_UE5_6)");
    return false;
}

if (Common.SelfTest && mode != "uds")
{
    Console.Error.WriteLine("--selftest needs --mode uds");
    return 2;
}
if (mode is "uds" or "holders")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine(
            $"usage: usmap_probe --mode {mode} <paks-dir> <mapping.usmap> [maxPackages] [pathFilter] [EGame]");
        return 2;
    }
    if (!TryGame(args, 4, out var g2)) return 2;
    int maxPk = args.Length > 2 ? int.Parse(args[2]) : 400;
    string filt = args.Length > 3 ? args[3] : "";
    return mode == "uds"
        ? UdsMode.Run(args[0], args[1], maxPk, filt, g2)
        : HolderMode.Run(args[0], args[1], maxPk, filt, g2);
}
if (mode != "diff")
{
    Console.Error.WriteLine($"unknown --mode {mode} (diff | uds | holders)");
    return 2;
}

if (args.Length < 3)
{
    Console.Error.WriteLine(
        "usage: usmap_probe <paks-dir> <a.usmap> <b.usmap> [maxExports] [pathFilter] [EGame] [showKey]");
    return 2;
}

string paks = args[0];
string usmapA = args[1];
string usmapB = args[2];
int max = args.Length > 3 ? int.Parse(args[3]) : 400;
string filter = args.Length > 4 ? args[4] : "";
if (!TryGame(args, 5, out var game)) return 2;
string show = args.Length > 6 ? args[6] : "";

Dictionary<string, string> Run(string usmap, out Stats st)
{
    Common.BeginRun();
    var provider = Common.Open(paks, usmap, game);

    var outp = new Dictionary<string, string>();
    st = new Stats { Files = provider.Files.Count };
    foreach (var kv in provider.Files.OrderBy(k => k.Key))
    {
        if (outp.Count >= max) break;
        if (!kv.Key.EndsWith(".uasset") && !kv.Key.EndsWith(".umap")) continue;
        if (filter.Length > 0 && !kv.Key.Contains(filter, StringComparison.OrdinalIgnoreCase))
            continue;
        try
        {
            var pkg = provider.LoadPackage(kv.Value);
            if (Common.IsUnversioned(pkg)) st.Unversioned++;
            else st.Versioned++;

            foreach (var exp in pkg.GetExports())
                outp[kv.Key + "::" + exp.Name] = JsonConvert.SerializeObject(exp, Formatting.None);
            st.Ok++;
        }
        catch (Exception ex)
        {
            st.Failed++;
            Common.Fail(kv.Key, ex);
        }
    }
    return outp;
}

var a = Run(usmapA, out var sa);
Console.WriteLine($"A  {Path.GetFileName(usmapA),-40} files={sa.Files} packages ok={sa.Ok} " +
                  $"failed={sa.Failed} exports={a.Count}  unversioned={sa.Unversioned} versioned={sa.Versioned}");
Common.PrintFailures("A");
var b = Run(usmapB, out var sb);
Console.WriteLine($"B  {Path.GetFileName(usmapB),-40} files={sb.Files} packages ok={sb.Ok} " +
                  $"failed={sb.Failed} exports={b.Count}  unversioned={sb.Unversioned} versioned={sb.Versioned}");
Common.PrintFailures("B");

if (sa.Unversioned == 0 && sa.Ok > 0)
    Console.WriteLine("\n*** NO package carries PKG_UnversionedProperties -- this cook is VERSIONED, " +
                      "the .usmap is never consulted, and any agreement below is meaningless. ***");

// ⛔ A KEY IN ONLY ONE SIDE IS NOT A NON-EVENT. The intersection below silently drops
// every export that threw under one mapping and parsed under the other -- which is the
// LOUDEST failure a wrong descriptor can produce -- and the run then reports DIFFER: 0.
var onlyA = a.Keys.Except(b.Keys).ToList();
var onlyB = b.Keys.Except(a.Keys).ToList();
if (onlyA.Count > 0 || onlyB.Count > 0)
{
    Console.WriteLine($"\n*** exports produced by ONLY ONE mapping: A-only={onlyA.Count} " +
                      $"B-only={onlyB.Count} -- these are NOT in the diff below ***");
    foreach (var k in onlyA.Take(5)) Console.WriteLine($"   A-only: {k}");
    foreach (var k in onlyB.Take(5)) Console.WriteLine($"   B-only: {k}");
}

var shared = a.Keys.Intersect(b.Keys).ToList();
var differ = shared.Where(k => a[k] != b[k]).ToList();
Console.WriteLine($"\nshared exports: {shared.Count}   IDENTICAL: {shared.Count - differ.Count}   " +
                  $"DIFFER: {differ.Count}");

var realDiffer = differ.Where(k => Common.Collapse(a[k]) != Common.Collapse(b[k])).ToList();
Console.WriteLine($"   of those, differ ONLY by a doubled enumerator prefix: {differ.Count - realDiffer.Count}");
Console.WriteLine($"   DIFFER for any other reason                        : {realDiffer.Count}");
differ = realDiffer.Count > 0 ? realDiffer : differ;
foreach (var k in differ.Take(8))
{
    Console.WriteLine($"\nDIFF {k}");
    Common.PrintAround("   ", "A", a[k], "B", b[k]);
}
if (show.Length > 0)
{
    var keys = a.Keys.Union(b.Keys)
                .Where(k => k.Contains(show, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k).ToList();
    Console.WriteLine($"\n=== readings for keys containing '{show}': {keys.Count} ===");
    foreach (var k in keys.Take(20))
    {
        Console.WriteLine($"\n{k}");
        Console.WriteLine($"   A: {(a.TryGetValue(k, out var av) ? av : "<absent: the " +
                                    "package did not parse under A>")}");
        if (!b.TryGetValue(k, out var bv))
            Console.WriteLine("   B: <absent: the package did not parse under B>");
        else if (a.TryGetValue(k, out var av2) && av2 == bv)
            Console.WriteLine("   B: (identical)");
        else
            Console.WriteLine($"   B: {bv}");
    }
}

return 0;

struct Stats
{
    public int Files;
    public int Ok;
    public int Failed;
    public int Unversioned;
    public int Versioned;
}

// ---------------------------------------------------------------------------------------------
// Shared plumbing: provider, failure capture, printing.
// ---------------------------------------------------------------------------------------------
static class Common
{
    public static int ShowErrors = 10;
    public static int ListPerVerdict = 20;
    public static bool Control;
    public static bool SelfTest;

    public static void ControlBanner()
    {
        if (Control)
            Console.WriteLine("*** CONTROL RUN: every struct entry the mapping lacks is synthesized BY NAME from " +
                              "the COOKED schema (enums are not). A clean result proves the harness only -- " +
                              "nothing about the .usmap. ***");
    }

    // A doubled enumerator prefix -- "EFoo::EFoo::Bar" against "EFoo::Bar" -- is the same VALUE
    // rendered differently, not a misread: it means one writer stored the member name fully
    // qualified and the other stored it bare, and the consumer qualifies it again. Folded before
    // any comparison so a cosmetic difference is never counted as a misalignment.
    static readonly Regex Doubled = new(@"([A-Za-z_][A-Za-z0-9_]*)::\1::");

    public static string Collapse(string s)
    {
        string p;
        do { p = s; s = Doubled.Replace(s, "$1::"); } while (s != p);
        return s;
    }

    // CUE4Parse's wording for a struct it could not find by name -- thrown for a type the reader
    // needed, logged as a warning for a missing native parent.
    static readonly Regex MissingMappings = new(@"Missing prop mappings for type ([^\s|]+)");

    public static string? MissingType(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (MissingMappings.Match(e.Message) is { Success: true } m) return m.Groups[1].Value;
        return null;
    }

    static readonly List<string> _failures = new();
    static int _failureCount;
    static LogCapture _log = new();

    // CUE4Parse catches an export's own deserialization exception (AbstractUePackage.
    // DeserializeObject) and only LOGS it -- the package still "loads", the export comes back
    // half-read, and a probe that counts only thrown exceptions reports it as parsed. Its logger
    // defaults to Serilog's silent one, so without this sink those errors reach nobody.
    public static void BeginRun()
    {
        _failures.Clear();
        _failureCount = 0;
        _log = new LogCapture();
        CUE4ParseLog.UseLogger(new Serilog.LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.Sink(_log)
            .CreateLogger());
    }

    public static DefaultFileProvider Open(string paks, string usmap, EGame game)
    {
        var provider = new DefaultFileProvider(paks, SearchOption.AllDirectories, false,
                                               new VersionContainer(game));
        provider.Initialize();
        provider.Mount();
        provider.MappingsContainer =
            new FileUsmapTypeMappingsProvider(usmap, StringComparer.OrdinalIgnoreCase);
        return provider;
    }

    public static List<KeyValuePair<string, CUE4Parse.FileProvider.Objects.GameFile>> Match(
        DefaultFileProvider provider, string filter) =>
        provider.Files
            .Where(kv => kv.Key.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) &&
                         (filter.Length == 0 ||
                          kv.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();

    public static bool IsUnversioned(IPackage pkg)
    {
        var flags = pkg switch
        {
            IoPackage io => io.Summary.PackageFlags,
            Package leg => leg.Summary.PackageFlags,
            _ => (EPackageFlags)0,
        };
        return flags.HasFlag(EPackageFlags.PKG_UnversionedProperties);
    }

    public static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (Exception? e = ex; e != null && parts.Count < 4; e = e.InnerException)
            parts.Add($"{e.GetType().Name}: {e.Message}");
        // A ParserException's message carries a second line (the archive position); keep one
        // failure per printed line.
        string s = Regex.Replace(string.Join(" -> ", parts), @"\s*[\r\n]+\s*", " | ");
        return s.Length > 400 ? s[..400] + "…" : s;
    }

    public static void Fail(string where, Exception ex)
    {
        _failureCount++;
        if (_failures.Count < ShowErrors) _failures.Add($"{where}: {Describe(ex)}");
    }

    public static void PrintFailures(string label)
    {
        if (_failureCount > 0)
        {
            Console.WriteLine($"   [{label}] package failures: {_failureCount} (first {_failures.Count}):");
            foreach (var f in _failures) Console.WriteLine($"      {f}");
        }
        _log.Print(label);
    }

    // Show the region AROUND the first difference, not the first 500 characters: two readings of
    // the same export usually agree for a long prefix, and a prefix dump then looks identical and
    // says nothing about what actually diverged.
    public static void PrintAround(string indent, string la, string x, string lb, string y)
    {
        int i = 0;
        while (i < x.Length && i < y.Length && x[i] == y[i]) i++;
        int from = Math.Max(0, i - 80);
        Console.WriteLine($"{indent}first difference at char {i} of {x.Length}/{y.Length}");
        Console.WriteLine($"{indent}{la}: …{x[from..Math.Min(x.Length, i + 160)]}");
        Console.WriteLine($"{indent}{lb}: …{y[from..Math.Min(y.Length, i + 160)]}");
    }

    public static string Key(UObject o) => $"{o.Owner?.Name ?? "?"}.{o.Name}";
}

sealed class LogCapture : Serilog.Core.ILogEventSink
{
    readonly object _gate = new();
    readonly List<string> _errors = new();
    int _errorCount;
    readonly Dictionary<string, int> _warnings = new();

    public void Emit(Serilog.Events.LogEvent e)
    {
        lock (_gate)
        {
            if (e.Level >= Serilog.Events.LogEventLevel.Error)
            {
                _errorCount++;
                if (_errors.Count < Common.ShowErrors)
                    _errors.Add(e.RenderMessage() +
                                (e.Exception != null ? " :: " + Common.Describe(e.Exception) : ""));
            }
            else
            {
                string t = e.MessageTemplate.Text;
                _warnings[t] = _warnings.GetValueOrDefault(t) + 1;
            }
        }
    }

    public void Print(string label)
    {
        lock (_gate)
        {
            if (_errorCount > 0)
            {
                Console.WriteLine($"   [{label}] CUE4Parse logged {_errorCount} error(s) -- exports it " +
                                  $"caught and kept half-read (first {_errors.Count}):");
                foreach (var s in _errors) Console.WriteLine($"      {s}");
            }
            if (_warnings.Count > 0)
            {
                Console.WriteLine($"   [{label}] CUE4Parse warnings: {_warnings.Values.Sum()} " +
                                  "(top templates):");
                foreach (var kv in _warnings.OrderByDescending(k => k.Value).Take(5))
                    Console.WriteLine($"      {kv.Value,6} x {kv.Key}");
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------
// uds / holders: one read with the cooked schema, one by NAME through the mapping.
// ---------------------------------------------------------------------------------------------
sealed class ProbeResult
{
    public string Key = "";
    public string? ClassName;
    public string Verdict = "";
    // The type a "Missing prop mappings for type X" names (NESTED-MISSING / COOKED-NEEDS-MAPPING).
    public string? Missing;
    // What the two READS concluded on their own. The schema check overrides it in Verdict; keeping
    // it is how --selftest shows a wrong descriptor that the reading alone calls SAME.
    public string Reading = "";
    public string Detail = "";
    public int Tags, Values, UdsTags, UdsValues;
    public long CookedBytes = -1, MappedBytes = -1;
    // Parse end minus the export's end (validPos). Both reads start at the same byte, so a
    // misaligned start makes them agree on the WRONG bytes; ending exactly at the export's end is
    // the one independent witness that the compared bytes were the intended ones.
    public long? EndDelta;
    public bool SchemaChecked, BothRead;
    public string? CookedJson, MappedJson;
    // Bytes each cooked tag consumed, by property name: 0 is zero-mask, an absent name was skipped.
    public readonly Dictionary<string, int> TagSizes = new(StringComparer.Ordinal);
    public readonly List<string> SchemaDiffs = new();
    public readonly List<string> DifferingTags = new();

    public string Label =>
        Missing != null && Verdict is "NESTED-MISSING" or "COOKED-NEEDS-MAPPING" ? $"{Verdict}({Missing})" : Verdict;

    public bool EnumMissingOnly =>
        DifferingTags.Count > 0 && DifferingTags.All(t => t.Contains("[ENUM-MISSING:", StringComparison.Ordinal));
}

static class Dual
{
    // UDS names the holders pass found in class schemas; a tag is UDS-typed when its cooked tag
    // data carries a loaded UDS OR names one of these (inner / value types included).
    public static readonly HashSet<string> KnownUds = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsUdsTag(FPropertyTag t) => RefersToUds(t.TagData);

    static bool RefersToUds(FPropertyTagData? d) =>
        d != null &&
        (d.Struct is UUserDefinedStruct || d.Struct is UdsProbe ||
         (d.StructType != null && KnownUds.Contains(d.StructType)) ||
         RefersToUds(d.InnerTypeData) || RefersToUds(d.ValueTypeData));

    static readonly HashSet<string> SameFamily = new(StringComparer.Ordinal) { "SAME", "SAME-ZERO-ONLY", "EMPTY" };

    // Reads the bytes at Ar.Position twice and leaves Ar.Position where it found it.
    //   cooked:  FStructFallback(Ar, UStruct) -> DeserializePropertiesUnversioned with a
    //            NON-UScriptClass -> SerializedStruct(cooked FProperty list): exactly the call
    //            UUserDefinedStruct.Deserialize / UObject.Deserialize make.
    //   mapping: FStructFallback(Ar, string) -> new UScriptClass(name) -> Mappings.Types[name].
    // Between the two, the mapping's entry is compared with the cooked schema without reading a byte.
    public static ProbeResult ReadTwice(string key, FAssetArchive Ar, UStruct cookedSchema,
                                        string mappingName)
    {
        var r = new ProbeResult { Key = key };
        long start = Ar.Position;
        FStructFallback cooked;
        try
        {
            cooked = new FStructFallback(Ar, cookedSchema);
            r.CookedBytes = Ar.Position - start;
        }
        catch (Exception ex)
        {
            // The cooked read looks a NATIVE member type up by name as well, so a native struct the
            // mapping lacks leaves no reference to compare against: a gap, not a misread.
            r.Missing = Common.MissingType(ex);
            r.Verdict = r.Missing != null ? "COOKED-NEEDS-MAPPING" : "COOKED-FAILED";
            r.Detail = Common.Describe(ex);
            return r;
        }
        finally
        {
            Ar.Position = start;
        }
        r.Tags = cooked.Properties.Count;
        r.Values = cooked.Properties.Count(t => t.Size > 0);
        foreach (var t in cooked.Properties)
            r.TagSizes[t.Name.Text] = Math.Max(r.TagSizes.GetValueOrDefault(t.Name.Text), t.Size);
        var uds = cooked.Properties.Where(IsUdsTag).ToList();
        r.UdsTags = uds.Count;
        r.UdsValues = uds.Count(t => t.Size > 0);

        var maps = Ar.Owner?.Mappings;
        if (maps == null)
        {
            // Not an absent ENTRY: with no mapping loaded, nothing at all can be looked up by name.
            r.Verdict = "NO-MAPPINGS";
            return r;
        }
        if (Common.Control)
        {
            int made = Control.Synthesize(maps, cookedSchema, mappingName);
            if (made > 0) r.Detail = $"control: {made} entr{(made == 1 ? "y" : "ies")} synthesized by name";
        }
        // Asked EXPLICITLY, not inferred from an exception: DeserializePropertiesUnversioned
        // returns before the lookup when the header carries no values, so an absent name would
        // otherwise come out as two empty readings that agree.
        if (!maps.Types.TryGetValue(mappingName, out var entry))
        {
            r.Verdict = "MAPPING-MISSING";
            return r;
        }

        var gaps = new SortedSet<string>(StringComparer.Ordinal);
        string? schemaError = null;
        try
        {
            Schema.Compare(maps, cookedSchema, entry, "", r.SchemaDiffs, gaps,
                           new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            r.SchemaChecked = true;
        }
        catch (Exception ex)
        {
            // Building a descriptor list can load a parent or a member type; a throw there must
            // cost this row its schema verdict, not the whole export its probe.
            schemaError = Common.Describe(ex);
        }

        string reading = ReadMapped(r, Ar, start, maps, cooked, mappingName, out string? readMissing);
        r.Reading = readMissing != null ? $"{reading}({readMissing})" : reading;
        if (gaps.Count > 0)
            r.Detail = Join(r.Detail, $"the mapping has no entry for {string.Join(", ", gaps)}, which a member names");
        if (r.SchemaDiffs.Count > 0)
        {
            r.Verdict = "SCHEMA-DIFFER";
        }
        else if (schemaError != null)
        {
            r.Verdict = "SCHEMA-FAILED";
            r.Detail = Join(r.Detail, $"schema check: {schemaError}; the reading alone: {r.Reading}");
        }
        else if (readMissing != null)
        {
            r.Verdict = reading;
            r.Missing = readMissing;
        }
        else if (gaps.Count > 0 && SameFamily.Contains(reading))
        {
            // A zero-mask or skipped member never made the read look its type up; the mapping still
            // cannot serve an instance that does serialize it.
            r.Verdict = "NESTED-MISSING";
            r.Missing = gaps.Min;
        }
        else
        {
            r.Verdict = reading;
        }
        return r;
    }

    static string Join(string a, string b) => a.Length == 0 ? b : a + "; " + b;

    static string ReadMapped(ProbeResult r, FAssetArchive Ar, long start, TypeMappings maps,
                             FStructFallback cooked, string mappingName, out string? missing)
    {
        missing = null;
        FStructFallback mapped;
        try
        {
            mapped = new FStructFallback(Ar, mappingName);
            r.MappedBytes = Ar.Position - start;
        }
        catch (Exception ex)
        {
            r.Detail = Join(r.Detail, Common.Describe(ex));
            // The entry itself was checked present, so a type this names is a NESTED one.
            missing = Common.MissingType(ex);
            return missing != null ? "NESTED-MISSING" : "FAILED";
        }
        finally
        {
            Ar.Position = start;
        }
        r.BothRead = true;

        // Rendered only now, outside the reads' try: a JSON converter that throws is not a failed read.
        try { r.CookedJson = Common.Collapse(JsonConvert.SerializeObject(cooked, Formatting.None)); }
        catch (Exception ex) { r.Detail = Join(r.Detail, "cooked: " + Common.Describe(ex)); return "RENDER-FAILED"; }
        try { r.MappedJson = Common.Collapse(JsonConvert.SerializeObject(mapped, Formatting.None)); }
        catch (Exception ex) { r.Detail = Join(r.Detail, "mapping: " + Common.Describe(ex)); return "RENDER-FAILED"; }

        // The end position is compared as well as the JSON: a descriptor of the wrong WIDTH can
        // decode the same value from fewer bytes and leave the stream misaligned for whatever
        // follows, which the JSON of this struct alone cannot show.
        if (r.CookedJson == r.MappedJson && r.CookedBytes == r.MappedBytes)
            return r.Tags == 0 ? "EMPTY" : r.Values == 0 ? "SAME-ZERO-ONLY" : "SAME";

        var c = TagMap(cooked);
        var m = TagMap(mapped);
        foreach (var k in c.Keys.Union(m.Keys))
        {
            bool inC = c.TryGetValue(k, out var cv), inM = m.TryGetValue(k, out var mv);
            if (inC && inM && cv.json == mv.json) continue;
            string where = !inM ? " (cooked only)" : !inC ? " (mapping only)" : "";
            bool isUds = (inC && IsUdsTag(cv.tag)) || (inM && IsUdsTag(mv.tag));
            var absent = new SortedSet<string>(StringComparer.Ordinal);
            if (inC) EnumsAbsent(maps, cv.tag.TagData, null, absent);
            if (inM) EnumsAbsent(maps, mv.tag.TagData, mv.json, absent);
            r.DifferingTags.Add(k + (isUds ? " [UDS]" : "") + where +
                                string.Concat(absent.Select(e => $" [ENUM-MISSING:{e}]")));
        }
        return "DIFFER";
    }

    // An enum the mapping lacks renders as "EFoo::<index>" on the mapping side (EnumProperty's
    // IndexToEnum fallback) against the loaded UEnum's member name on the cooked side: a GAP in the
    // mapping, not a wrong descriptor. The tag's own type data finds a direct or container enum;
    // the rendered text finds one inside a nested struct.
    static readonly Regex NumericEnum = new(@"""([A-Za-z_][A-Za-z0-9_]*)::-?[0-9]+""");

    static void EnumsAbsent(TypeMappings maps, FPropertyTagData? d, string? json, SortedSet<string> absent)
    {
        for (var stack = new Stack<FPropertyTagData?>(new[] { d }); stack.Count > 0;)
        {
            var x = stack.Pop();
            if (x == null) continue;
            if (x.EnumName is { Length: > 0 } e && !e.Equals("None", StringComparison.OrdinalIgnoreCase) &&
                !maps.Enums.ContainsKey(e))
                absent.Add(e);
            stack.Push(x.InnerTypeData);
            stack.Push(x.ValueTypeData);
        }
        if (json == null) return;
        foreach (Match mt in NumericEnum.Matches(json))
            if (!maps.Enums.ContainsKey(mt.Groups[1].Value)) absent.Add(mt.Groups[1].Value);
    }

    static Dictionary<string, (string json, FPropertyTag tag)> TagMap(FStructFallback s)
    {
        var d = new Dictionary<string, (string, FPropertyTag)>();
        foreach (var t in s.Properties)
        {
            string k = t.ArrayIndex > 0 ? $"{t.Name.Text}[{t.ArrayIndex}]" : t.Name.Text;
            d[k] = (Common.Collapse(JsonConvert.SerializeObject(t.Tag, Formatting.None)), t);
        }
        return d;
    }

    public static readonly string[] VerdictOrder =
    {
        "SCHEMA-DIFFER", "SCHEMA-FAILED", "DIFFER", "FAILED", "NESTED-MISSING", "RENDER-FAILED", "COOKED-FAILED",
        "COOKED-NEEDS-MAPPING", "HEADER-FAILED", "NO-MAPPINGS", "MAPPING-MISSING", "VERSIONED", "SKIPPED",
        "NO-DEFAULT-INSTANCE", "EMPTY", "SAME-ZERO-ONLY", "SAME",
    };

    public static void PrintResults(IReadOnlyCollection<ProbeResult> results, bool holders)
    {
        var counts = results.GroupBy(r => r.Verdict).ToDictionary(g => g.Key, g => g.Count());
        // A verdict absent from the order is still printed: a new one must never vanish silently.
        var order = VerdictOrder.Where(counts.ContainsKey)
                                .Concat(counts.Keys.Except(VerdictOrder).OrderBy(v => v, StringComparer.Ordinal))
                                .ToList();
        Console.WriteLine("verdicts: " + string.Join("  ", order.Select(v => $"{v}={counts[v]}")));
        foreach (var v in new[] { "NESTED-MISSING", "COOKED-NEEDS-MAPPING" }.Where(counts.ContainsKey))
            Console.WriteLine($"   {v} by type: " + string.Join("  ", results
                .Where(r => r.Verdict == v).GroupBy(r => r.Missing ?? "?")
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key}={g.Count()}")));
        foreach (var v in order)
        {
            var rows = results.Where(r => r.Verdict == v).OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
            Console.WriteLine($"\n--- {v}: {rows.Count} (first {Math.Min(rows.Count, Common.ListPerVerdict)}) ---");
            foreach (var r in rows.Take(Common.ListPerVerdict))
            {
                string counts2 = $"tags={r.Tags} values={r.Values}" +
                                 (holders ? $" udsTags={r.UdsTags} udsValues={r.UdsValues}" : "") +
                                 (r.CookedBytes >= 0 ? $" cooked={r.CookedBytes}B" : "") +
                                 (r.MappedBytes >= 0 ? $" mapped={r.MappedBytes}B" : "") +
                                 (r.EndDelta is long d && d != 0 ? $" *** EXPORT END OFF BY {d}B ***" : "");
                Console.WriteLine($"  {r.Label,-16} {r.Key}  {counts2}" +
                                  (r.Detail.Length > 0 ? $"\n      {r.Detail}" : ""));
                if (v == "SCHEMA-DIFFER")
                {
                    Console.WriteLine($"      the reading alone: {r.Reading}");
                    foreach (var s in r.SchemaDiffs.Take(8)) Console.WriteLine($"      schema {s}");
                    if (r.SchemaDiffs.Count > 8) Console.WriteLine($"      schema (+{r.SchemaDiffs.Count - 8} more)");
                }
                if (r.DifferingTags.Count > 0)
                    Console.WriteLine($"      differing tags: {string.Join(", ", r.DifferingTags.Take(8))}" +
                                      (r.DifferingTags.Count > 8 ? $" (+{r.DifferingTags.Count - 8})" : ""));
                if (v == "DIFFER" && r.CookedJson != null && r.MappedJson != null)
                    Common.PrintAround("      ", "cooked ", r.CookedJson, "mapping", r.MappedJson);
            }
        }
    }
}

// ⭐ THE STATIC CHECK. The mapping's entry against the descriptors CUE4Parse builds from the cooked
// FProperty list -- the very SerializedStruct the cooked read uses -- index by index over the whole
// super chain, without reading a byte. See "WHY A SAME READING IS NOT ENOUGH" in the header.
static class Schema
{
    public static void Compare(TypeMappings maps, UStruct cooked, Struct mapped, string prefix,
                               List<string> diffs, SortedSet<string> gaps, HashSet<string> seen)
    {
        if (!seen.Add(cooked.Name)) return;
        var c = new SerializedStruct(maps, cooked);
        int nc = c.CountProperties(true), nm = mapped.CountProperties(true);
        if (nc != nm) diffs.Add($"{prefix}property count (super chain included): cooked {nc}, mapping {nm}");
        for (int i = 0; i < Math.Max(nc, nm); i++)
        {
            bool hc = c.TryGetValue(i, out var pc), hm = mapped.TryGetValue(i, out var pm);
            if (!hc && !hm) continue;
            if (!hc || !hm)
            {
                var p = hc ? pc : pm;
                diffs.Add($"{prefix}#{i} {p.Name} {Canon(p.MappingType)}: {(hc ? "cooked" : "mapping")} only");
                continue;
            }
            var why = new List<string>();
            if (pc.Name != pm.Name) why.Add($"named {pm.Name} by the mapping");
            if ((pc.ArraySize ?? 1) != (pm.ArraySize ?? 1))
                why.Add($"array size {pc.ArraySize ?? 1} -> mapping {pm.ArraySize ?? 1}");
            string tc = Canon(pc.MappingType), tm = Canon(pm.MappingType);
            if (tc != tm) why.Add($"{tc} -> mapping {tm}");
            if (why.Count > 0)
            {
                diffs.Add($"{prefix}#{i} {pc.Name}: {string.Join("; ", why)}");
                continue;
            }
            // A non-native member type is read through the mapping's entry of that NAME (the loaded
            // UStruct is only on the cooked side), so its descriptors are this struct's too.
            foreach (var t in Walk(pc.MappingType))
            {
                if (t.Struct is not UStruct ns || ns is UScriptClass || t.StructType == null) continue;
                if (maps.Types.TryGetValue(t.StructType, out var ne))
                    Compare(maps, ns, ne, $"{prefix}{pc.Name}.", diffs, gaps, seen);
                else
                    gaps.Add(t.StructType);
            }
        }
    }

    // Cooked FProperty classes a .usmap has no separate spelling for; each reads the same bytes
    // as the name it folds onto.
    static string Fold(string type) => type switch
    {
        "ClassProperty" => "ObjectProperty",
        "SoftClassProperty" => "SoftObjectProperty",
        "MulticastInlineDelegateProperty" or "MulticastSparseDelegateProperty" => "MulticastDelegateProperty",
        _ => type,
    };

    // One spelling per descriptor the READER treats alike: a ByteProperty carrying an enum
    // (TEnumAsByte) is read as EnumProperty -- how a .usmap spells it, with a ByteProperty
    // underlying type -- and an EnumProperty with no underlying type reads one byte.
    public static string Canon(PropertyType? t)
    {
        if (t == null) return "?";
        string type = Fold(t.Type);
        string? en = t.EnumName is { Length: > 0 } e && !e.Equals("None", StringComparison.OrdinalIgnoreCase) ? e : null;
        if (type == "ByteProperty" && en != null) type = "EnumProperty";
        return type switch
        {
            "EnumProperty" => $"EnumProperty<{(t.InnerType == null ? "ByteProperty" : Canon(t.InnerType))}, {en ?? "?"}>",
            "StructProperty" => $"StructProperty<{t.StructType ?? "?"}>",
            "ArrayProperty" or "SetProperty" or "OptionalProperty" => $"{type}<{Canon(t.InnerType)}>",
            "MapProperty" => $"MapProperty<{Canon(t.InnerType)}, {Canon(t.ValueType)}>",
            _ => en != null ? $"{type}<{en}>" : type,
        };
    }

    public static IEnumerable<PropertyType> Walk(PropertyType? t)
    {
        if (t == null) yield break;
        yield return t;
        foreach (var x in Walk(t.InnerType)) yield return x;
        foreach (var x in Walk(t.ValueType)) yield return x;
    }
}

// --control: the entry a .usmap would carry for a struct, made from its cooked schema. Every type
// is rebuilt BY NAME, with no loaded UStruct / UEnum, because a real mapping has only names: a
// loaded object lets a nested struct or enum resolve past the mapping. Measured on EVERSPACE 2's
// 46 readable UDS: the loaded-object entry read 46 identical, the by-name one 43 identical + 3
// DIFFER -- three rows whose enum no mapping entry covered.
static class Control
{
    public static PropertyType ByName(PropertyType t) =>
        new(t.Type, t.StructType, t.InnerType == null ? null : ByName(t.InnerType),
            t.ValueType == null ? null : ByName(t.ValueType), t.EnumName, t.IsEnumAsByte, t.Bool);

    public static Struct Entry(TypeMappings maps, UStruct s, SerializedStruct? ser = null)
    {
        ser ??= new SerializedStruct(maps, s);
        var props = ser.Properties.ToDictionary(
            kv => kv.Key,
            kv => new PropertyInfo(kv.Value.Index, kv.Value.Name, ByName(kv.Value.MappingType), kv.Value.ArraySize));
        string? super = s.SuperStruct is { IsNull: false } sp ? sp.Name : null;
        return new Struct(maps, s.Name, super, props, ser.PropertyCount);
    }

    // Synthesizes `name` and every non-native struct it reaches -- Blueprint parents, member types --
    // that the mapping lacks, so the control does not depend on which package happened to load first.
    public static int Synthesize(TypeMappings maps, UStruct s, string name)
    {
        if (s is UScriptClass || s.ChildProperties == null || maps.Types.ContainsKey(name)) return 0;
        var ser = new SerializedStruct(maps, s);
        maps.Types[name] = Entry(maps, s, ser);
        int made = 1;
        UStruct? parent = null;
        try { parent = s.SuperStruct is { IsNull: false } sp ? sp.Load<UStruct>() : null; }
        catch { /* an unloadable parent stays unmapped, as it would in a real file */ }
        if (parent != null) made += Synthesize(maps, parent, parent.Name);
        foreach (var p in ser.Properties.Values)
            foreach (var t in Schema.Walk(p.MappingType))
                if (t.Struct is UStruct ns && t.StructType != null) made += Synthesize(maps, ns, t.StructType);
        return made;
    }
}

// --selftest: the NEGATIVE control for the schema check. Each UDS's control entry (identical to its
// cooked schema, so it must pass) is installed with ONE descriptor changed at a time, and every
// changed entry must come out SCHEMA-DIFFER. The reading's own verdict is tallied beside it: those
// it calls SAME are exactly the wrong descriptors a reading-only verdict let through.
static class SelfTest
{
    // Same width, other interpretation (Int / UInt, Object / Weak) -- invisible to a reading -- or
    // another width. None is a pair the schema check folds together.
    static readonly Dictionary<string, string> Swap = new(StringComparer.Ordinal)
    {
        ["IntProperty"] = "UInt32Property", ["UInt32Property"] = "IntProperty",
        ["Int64Property"] = "UInt64Property", ["UInt64Property"] = "Int64Property",
        ["Int16Property"] = "UInt16Property", ["UInt16Property"] = "Int16Property",
        ["FloatProperty"] = "DoubleProperty", ["DoubleProperty"] = "FloatProperty",
        ["ObjectProperty"] = "WeakObjectProperty", ["ClassProperty"] = "WeakObjectProperty",
        ["WeakObjectProperty"] = "ObjectProperty",
        ["ByteProperty"] = "Int8Property", ["BoolProperty"] = "ByteProperty",
    };

    static readonly HashSet<string> Done = new(StringComparer.Ordinal);
    static readonly SortedDictionary<string, (int n, int caught)> Tally = new(StringComparer.Ordinal);
    static readonly List<string> Missed = new();
    static int _structs, _identityFlagged, _changes, _caught, _readingSame;

    static PropertyType With(PropertyType t, string? type = null, string? structType = null,
                             PropertyType? inner = null, string? enumName = null) =>
        new(type ?? t.Type, structType ?? t.StructType, inner ?? t.InnerType, t.ValueType,
            enumName ?? t.EnumName, t.IsEnumAsByte, t.Bool);

    static IEnumerable<(string kind, PropertyInfo p)> Variants(PropertyInfo p)
    {
        var t = p.MappingType;
        PropertyInfo P(PropertyType nt, string? name = null) => new(p.Index, name ?? p.Name, nt, p.ArraySize);
        if (Swap.TryGetValue(t.Type, out var to))
            yield return ($"{t.Type}->{to}", P(With(t, type: to)));
        if (t.Type == "StructProperty")
            yield return ("StructProperty<X>->StructProperty<NoSuch>", P(With(t, structType: "NoSuchStruct_SelfTest")));
        if ((t.Type is "ArrayProperty" or "SetProperty" or "OptionalProperty") &&
            t.InnerType != null && Swap.TryGetValue(t.InnerType.Type, out var ti))
            yield return ($"{t.Type}<{t.InnerType.Type}>->{t.Type}<{ti}>", P(With(t, inner: With(t.InnerType, type: ti))));
        if (t.EnumName is { Length: > 0 } en && !en.Equals("None", StringComparison.OrdinalIgnoreCase))
            yield return ("enum name", P(With(t, enumName: "ENoSuchEnum_SelfTest")));
        yield return ("property name", P(t, p.Name + "_SelfTest"));
    }

    public static void Run(FAssetArchive Ar, UStruct schema, string name, ProbeResult baseline)
    {
        var maps = Ar.Owner?.Mappings;
        if (maps == null || baseline.CookedBytes < 0 || !Done.Add(baseline.Key)) return;
        long start = Ar.Position;
        maps.Types.TryGetValue(name, out var original);
        try
        {
            _structs++;
            var entry = Control.Entry(maps, schema);
            if (Probe(maps, Ar, schema, name, entry).Verdict == "SCHEMA-DIFFER")
            {
                _identityFlagged++;
                Missed.Add($"{baseline.Key}: the UNCHANGED control entry came out SCHEMA-DIFFER");
            }
            foreach (int idx in entry.Properties.Keys.OrderBy(k => k).ToList())
            {
                var p = entry.Properties[idx];
                string ser = baseline.TagSizes.TryGetValue(p.Name, out int size)
                    ? size > 0 ? "value" : "zero-mask"
                    : "skipped";
                foreach (var (kind, np) in Variants(p))
                {
                    var props = new Dictionary<int, PropertyInfo>(entry.Properties) { [idx] = np };
                    var r = Probe(maps, Ar, schema, name,
                                  new Struct(maps, entry.Name, entry.SuperType, props, entry.PropertyCount));
                    bool caught = r.Verdict == "SCHEMA-DIFFER";
                    string reading = r.Reading.Length > 0 ? r.Reading : r.Verdict;
                    _changes++;
                    if (caught) _caught++;
                    if (reading.StartsWith("SAME", StringComparison.Ordinal) || reading == "EMPTY") _readingSame++;
                    string key = $"{kind,-52} {ser,-9} reading alone {reading}";
                    var (n, c) = Tally.GetValueOrDefault(key);
                    Tally[key] = (n + 1, c + (caught ? 1 : 0));
                    if (!caught) Missed.Add($"{baseline.Key} #{idx} {p.Name} {kind} [{ser}]: {r.Verdict}");
                }
            }
        }
        finally
        {
            if (original != null) maps.Types[name] = original;
            else maps.Types.Remove(name);
            Ar.Position = start;
        }
    }

    static ProbeResult Probe(TypeMappings maps, FAssetArchive Ar, UStruct schema, string name, Struct entry)
    {
        maps.Types[name] = entry;
        return Dual.ReadTwice(name, Ar, schema, name);
    }

    // True when the schema check held: every change caught, no unchanged entry flagged.
    public static bool Print()
    {
        Console.WriteLine($"\nselftest: {_structs} UDS, {_changes} single-descriptor changes to the control entry" +
                          $"   SCHEMA-DIFFER={_caught}  missed={_changes - _caught}" +
                          $"   the reading ALONE called {_readingSame} of them SAME / SAME-ZERO-ONLY / EMPTY" +
                          $"   unchanged entries flagged SCHEMA-DIFFER: {_identityFlagged} of {_structs}");
        foreach (var kv in Tally)
            Console.WriteLine($"  {kv.Value.n,4} x {kv.Key}  -> SCHEMA-DIFFER {kv.Value.caught}");
        foreach (var m in Missed.Take(Common.ShowErrors)) Console.WriteLine($"   MISSED {m}");
        if (_changes == 0)
        {
            Console.WriteLine("*** the self-test changed nothing: it proves nothing. ***");
            return false;
        }
        if (_caught == _changes && _identityFlagged == 0) return true;
        Console.WriteLine("*** SELF-TEST FAILED: the schema check is not trustworthy on this input. ***");
        return false;
    }
}

// ⭐ THE HOOK. A UDS's default instance sits in the MIDDLE of its export -- after UStruct's
// fields, the Status property and StructFlags -- so there is no public offset to seek to from
// outside, and reproducing IoStore's export-offset arithmetic would be a second parser to trust.
// Every export's C# type comes from ObjectTypeRegistry (IoPackage -> ConstructObject ->
// UClass.ConstructObject -> ObjectTypeRegistry.Get(class name)), the library's own extension
// point for game-specific types, so registering this type under "UserDefinedStruct" hands us
// the archive at exactly the byte where the default instance starts, on the real load path.
//
// It derives from UStruct, not UUserDefinedStruct, because C# cannot call a grandparent's
// Deserialize: this is UUserDefinedStruct.Deserialize's own tail, line for line, with the one
// read replaced by two. ⚠ It is a REPLICA of CUE4Parse 1.2.2.202609 -- re-read the decompiled
// UUserDefinedStruct.Deserialize whenever the package version in usmap_probe.csproj moves.
// Nothing in that CUE4Parse revision tests `is UUserDefinedStruct` (a member typed as a UDS only
// needs a UStruct), so the substitution does not change how anything else loads.
public sealed class UdsProbe : UStruct
{
    public uint StructFlags;

    public override void Deserialize(FAssetArchive Ar, long validPos)
    {
        try
        {
            base.Deserialize(Ar, validPos);
        }
        catch (Exception ex)
        {
            UdsMode.Record(new ProbeResult
                { Key = Common.Key(this), Verdict = "HEADER-FAILED", Detail = Common.Describe(ex) });
            throw;
        }
        var status = GetOrDefault("Status", EUserDefinedStructureStatus.UDSS_UpToDate);
        if (Flags.HasFlag(EObjectFlags.RF_ClassDefaultObject) ||
            status != EUserDefinedStructureStatus.UDSS_UpToDate)
        {
            UdsMode.Record(new ProbeResult
                { Key = Common.Key(this), Verdict = "SKIPPED", Detail = $"status {status}, flags {Flags}" });
            return;
        }
        StructFlags = Ar.Read<uint>();
        if (FFrameworkObjectVersion.Get(Ar) < FFrameworkObjectVersion.Type.UserDefinedStructsStoreDefaultInstance)
        {
            UdsMode.Record(new ProbeResult { Key = Common.Key(this), Verdict = "NO-DEFAULT-INSTANCE" });
            return;
        }
        if (!Ar.HasUnversionedProperties)
        {
            // Tagged: both constructors call the same DeserializePropertiesTagged and the mapping
            // is never consulted, so there is nothing to compare.
            var tagged = new FStructFallback(Ar, (UStruct)this);
            UdsMode.Record(new ProbeResult
                { Key = Common.Key(this), Verdict = "VERSIONED", Tags = tagged.Properties.Count });
            return;
        }
        var r = Dual.ReadTwice(Common.Key(this), Ar, this, Name);
        UdsMode.Record(r);
        if (Common.SelfTest) SelfTest.Run(Ar, this, Name, r);
        if (r.CookedBytes < 0)
            throw new InvalidOperationException($"cooked default instance of {Name}: {r.Detail}");
        Ar.Position += r.CookedBytes;
        // The default instance is the last thing a UDS export serializes.
        r.EndDelta = Ar.Position - validPos;
    }
}

static class UdsMode
{
    static readonly Dictionary<string, ProbeResult> _results = new(StringComparer.Ordinal);
    static int _duplicates;

    // No package cache in CUE4Parse: a UDS imported by several iterated packages is re-read each
    // time. The first reading is kept; the count says how many repeats were discarded.
    public static void Record(ProbeResult r)
    {
        if (!_results.TryAdd(r.Key, r)) _duplicates++;
    }

    public static int Run(string paks, string usmap, int max, string filter, EGame game)
    {
        ObjectTypeRegistry.RegisterClass("UserDefinedStruct", typeof(UdsProbe));
        Common.BeginRun();
        var provider = Common.Open(paks, usmap, game);
        var files = Common.Match(provider, filter);
        Console.WriteLine($"mode uds   mapping={Path.GetFileName(usmap)}  game={game}  filter='{filter}'");
        Common.ControlBanner();

        int ok = 0, failed = 0, unv = 0, ver = 0, seen = 0;
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var unprobed = new List<string>();
        foreach (var kv in files.Take(max))
        {
            try
            {
                var pkg = provider.LoadPackage(kv.Value);
                if (Common.IsUnversioned(pkg)) unv++; else ver++;
                foreach (var exp in pkg.GetExports())
                {
                    if (exp.ExportType != "UserDefinedStruct") continue;
                    seen++;
                    string k = Common.Key(exp);
                    seenKeys.Add(k);
                    if (exp is not UdsProbe) unprobed.Add($"{k} (constructed as {exp.GetType().Name}: the hook did not take)");
                    else if (!_results.ContainsKey(k)) unprobed.Add($"{k} (never reached its default instance -- see the logged errors)");
                }
                ok++;
            }
            catch (Exception ex)
            {
                failed++;
                Common.Fail(kv.Key, ex);
            }
        }

        Console.WriteLine($"packages matched={files.Count} iterated={Math.Min(files.Count, max)} ok={ok} " +
                          $"failed={failed}  unversioned={unv} versioned={ver}" +
                          (files.Count > max ? $"   *** TRUNCATED at maxPackages={max} ***" : ""));
        Common.PrintFailures("uds");
        var inSet = _results.Values.Where(r => seenKeys.Contains(r.Key)).ToList();
        int imported = _results.Count - inSet.Count;
        Console.WriteLine($"UserDefinedStruct exports in the iterated packages: {seen}   probed: {inSet.Count}" +
                          $"   (+{imported} more read as imports of them; {_duplicates} repeat reads discarded)");
        foreach (var u in unprobed.Take(Common.ShowErrors)) Console.WriteLine($"   UNPROBED {u}");

        // The iterated set only: a UDS read as an IMPORT of one is outside the filter, and counting it
        // here would make the verdicts disagree with the "probed" line above.
        Dual.PrintResults(inSet, holders: false);
        PrintGuards(inSet, r => r.Values > 0, "serialized >= 1 value (a property that consumed bytes)");
        if (ver > 0 && unv == 0)
            Console.WriteLine("*** every package is VERSIONED: the mapping is never consulted. ***");
        if (Common.SelfTest && !SelfTest.Print()) return 1;
        return 0;
    }

    public static void PrintGuards(List<ProbeResult> all, Func<ProbeResult, bool> hasValues, string what)
    {
        int withValues = all.Count(hasValues);
        int schemaChecked = all.Count(r => r.SchemaChecked);
        int schemaSame = all.Count(r => r.SchemaChecked && r.SchemaDiffs.Count == 0);
        // EMPTY is not a comparison: a header that lists nothing never consults the mapping.
        int compared = all.Count(r => r.BothRead && r.Tags > 0);
        int comparedWithValues = all.Count(r => r.BothRead && r.Tags > 0 && hasValues(r));
        int cleared = all.Count(r => r.Verdict == "SAME" && hasValues(r));
        int enumOnly = all.Count(r => r.Verdict == "DIFFER" && r.EnumMissingOnly);
        Console.WriteLine("\nguards:");
        Console.WriteLine($"  {what,-66}: {withValues} of {all.Count}");
        Console.WriteLine($"  {"schema checked (the mapping has the entry)",-66}: {schemaChecked}");
        Console.WriteLine($"  {"   of those, descriptors identical to the cooked schema",-66}: {schemaSame}");
        Console.WriteLine($"  {"compared (both reads completed on a non-empty header)",-66}: {compared}");
        var ends = all.Where(r => r.EndDelta.HasValue).ToList();
        Console.WriteLine($"  {"parse ended exactly at the export's end",-66}: " +
                          $"{ends.Count(r => r.EndDelta == 0)} of {ends.Count}");
        Console.WriteLine($"  {"compared AND serialized >= 1 value",-66}: {comparedWithValues}");
        Console.WriteLine($"  {"SAME on that value AND identical descriptors (what proves anything)",-66}: {cleared}");
        if (enumOnly > 0)
            Console.WriteLine($"  {"DIFFER whose every differing tag is [ENUM-MISSING] (a gap)",-66}: {enumOnly}");
        if (ends.Any(r => r.EndDelta != 0))
            Console.WriteLine("*** some parses did NOT end at the export's end: for those, both reads may " +
                              "have compared the WRONG bytes, and their SAME is worthless. ***");
        if (withValues == 0)
            Console.WriteLine("*** NOTHING serialized a value: this run measured nothing. ***");
        else if (comparedWithValues == 0)
            Console.WriteLine("*** nothing with a value was COMPARED: a MAPPING-MISSING / FAILED run says " +
                              "nothing about the mapping's descriptors. ***");
        Common.ControlBanner();
    }
}

// A Blueprint CDO's class is a BlueprintGeneratedClass, which UClass.ConstructObject maps to a
// PLAIN UObject when ObjectTypeRegistry has no entry for the class's own name. Registering THAT
// name (pass 1 finds which) makes the CDO this type instead: same base Deserialize, plus the
// dual read first. ⚠ Non-CDO instances of a registered class also become this type (instead of
// the native ancestor's C# type) -- harmless for .uasset Blueprint packages, where the CDO is
// the only instance, but it would drop native-specific parsing on a .umap's placed actors.
public sealed class HolderProbe : UObject
{
    public override void Deserialize(FAssetArchive Ar, long validPos)
    {
        var r = Flags.HasFlag(EObjectFlags.RF_ClassDefaultObject) ? HolderMode.Probe(this, Ar) : null;
        base.Deserialize(Ar, validPos);
        // Only after the WHOLE export (sparse class data included) is the end comparable; the
        // property read is the same call the dual read made, so its alignment is witnessed here.
        if (r != null) r.EndDelta = Ar.Position - validPos;
    }
}

static class HolderMode
{
    static readonly Dictionary<string, ProbeResult> _results = new(StringComparer.Ordinal);
    static int _duplicates;

    public static ProbeResult Probe(HolderProbe cdo, FAssetArchive Ar)
    {
        string key = Common.Key(cdo);
        string? cls = cdo.Class?.Name.Text;
        ProbeResult r;
        long start = Ar.Position;
        try
        {
            if (!Ar.HasUnversionedProperties)
                r = new ProbeResult { Key = key, Verdict = "VERSIONED" };
            else if (cdo.Class?.Object?.Value is not UStruct schema)
                r = new ProbeResult { Key = key, Verdict = "COOKED-FAILED", Detail = "class did not resolve to a UStruct" };
            else
                r = Dual.ReadTwice(key, Ar, schema, schema.Name);
        }
        catch (Exception ex)
        {
            r = new ProbeResult { Key = key, Verdict = "FAILED", Detail = Common.Describe(ex) };
        }
        finally
        {
            Ar.Position = start;
        }
        r.ClassName = cls;
        if (!_results.TryAdd(key, r)) _duplicates++;
        return r;
    }

    static void CollectUds(FProperty? p, HashSet<string> names)
    {
        switch (p)
        {
            case FStructProperty sp:
                var ro = sp.Struct?.ResolvedObject;
                if (ro == null) return;
                string? cls = null;
                try { cls = ro.Class?.Name.Text; } catch { /* unresolvable class: fall back to a load */ }
                if (cls == "UserDefinedStruct") names.Add(ro.Name.Text);
                else if (cls == null && sp.Struct!.TryLoad<UStruct>(out var loaded) && loaded is UUserDefinedStruct)
                    names.Add(loaded.Name);
                break;
            case FArrayProperty ap: CollectUds(ap.Inner, names); break;
            case FSetProperty setp: CollectUds(setp.ElementProp, names); break;
            case FMapProperty mp: CollectUds(mp.KeyProp, names); CollectUds(mp.ValueProp, names); break;
            case FOptionalProperty op: CollectUds(op.ValueProperty, names); break;
        }
    }

    public static int Run(string paks, string usmap, int max, string filter, EGame game)
    {
        Console.WriteLine($"mode holders   mapping={Path.GetFileName(usmap)}  game={game}  filter='{filter}'");
        Common.ControlBanner();

        // Pass 1 -- stock types: which Blueprint classes declare (or inherit from a Blueprint
        // parent) a member typed as a UDS, directly or inside an array / set / map / optional.
        Common.BeginRun();
        var provider = Common.Open(paks, usmap, game);
        var files = Common.Match(provider, filter);
        var classes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        int ok1 = 0, failed1 = 0, bpClasses = 0;
        var blind = new List<string>();
        foreach (var kv in files.Take(max))
        {
            try
            {
                var pkg = provider.LoadPackage(kv.Value);
                foreach (var exp in pkg.GetExports())
                {
                    if (exp is not UStruct cls || !exp.ExportType.EndsWith("BlueprintGeneratedClass")) continue;
                    bpClasses++;
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    UStruct? s = cls;
                    for (int depth = 0; s != null && depth < 32 &&
                                        s.ExportType.EndsWith("BlueprintGeneratedClass"); depth++)
                    {
                        // A class export reads its own UObject properties THROUGH THE MAPPING
                        // (its class, BlueprintGeneratedClass, is native) before its member list;
                        // when that throws, ChildProperties is never assigned -- an empty class
                        // gets an empty array, not null. Discovery is blind to such a class.
                        if (s.ChildProperties == null)
                        {
                            blind.Add(s == cls ? cls.Name : $"{cls.Name} (ancestor {s.Name})");
                            break;
                        }
                        foreach (var f in s.ChildProperties)
                            if (f is FProperty fp) CollectUds(fp, names);
                        try { s = s.SuperStruct?.Load<UStruct>(); } catch { s = null; }
                    }
                    if (names.Count == 0) continue;
                    classes[cls.Name] = names;
                    Dual.KnownUds.UnionWith(names);
                }
                ok1++;
            }
            catch (Exception ex)
            {
                failed1++;
                Common.Fail(kv.Key, ex);
            }
        }
        Console.WriteLine($"pass 1: packages matched={files.Count} iterated={Math.Min(files.Count, max)} ok={ok1} " +
                          $"failed={failed1}  Blueprint classes={bpClasses}  with UDS-typed members={classes.Count}" +
                          $"  distinct UDS types={Dual.KnownUds.Count}" +
                          (files.Count > max ? $"   *** TRUNCATED at maxPackages={max} ***" : ""));
        Common.PrintFailures("pass 1");
        if (blind.Count > 0)
        {
            Console.WriteLine($"*** {blind.Count} of {bpClasses} Blueprint classes have NO readable member list " +
                              "(the class export failed before it -- see the logged errors): pass 1 cannot " +
                              "tell whether they hold a UDS, so 'with UDS-typed members' is a LOWER bound. " +
                              "Typical cause: a mapping from another engine build misreads " +
                              "BlueprintGeneratedClass's own properties. ***");
            foreach (var bc in blind.Take(Common.ShowErrors)) Console.WriteLine($"   BLIND {bc}");
        }
        if (classes.Count == 0)
        {
            Console.WriteLine("*** no Blueprint class in the iterated packages has a UDS-typed member: " +
                              "nothing to probe. ***");
            return 0;
        }

        // Pass 2 -- a FRESH provider, so every CDO is constructed after the registrations.
        foreach (var c in classes.Keys) ObjectTypeRegistry.RegisterClass(c, typeof(HolderProbe));
        Common.BeginRun();
        provider = Common.Open(paks, usmap, game);
        int ok2 = 0, failed2 = 0, unv = 0, ver = 0, cdos = 0;
        var wrongType = new List<string>();
        var seenCdos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kv in files.Take(max))
        {
            try
            {
                var pkg = provider.LoadPackage(kv.Value);
                if (Common.IsUnversioned(pkg)) unv++; else ver++;
                // GetExports is LAZY: an export -- the CDO included -- is constructed and
                // deserialized only when enumerated, so it must be walked, not just called.
                foreach (var exp in pkg.GetExports())
                {
                    if (!exp.Flags.HasFlag(EObjectFlags.RF_ClassDefaultObject) ||
                        !classes.ContainsKey(exp.ExportType)) continue;
                    cdos++;
                    seenCdos.Add(Common.Key(exp));
                    if (exp is not HolderProbe)
                        wrongType.Add($"{Common.Key(exp)} (constructed as {exp.GetType().Name}: the hook did not take)");
                }
                ok2++;
            }
            catch (Exception ex)
            {
                failed2++;
                Common.Fail(kv.Key, ex);
            }
        }
        Console.WriteLine($"pass 2: ok={ok2} failed={failed2}  unversioned={unv} versioned={ver}  " +
                          $"CDOs of registered classes seen={cdos}");
        Common.PrintFailures("pass 2");
        foreach (var w in wrongType.Take(Common.ShowErrors)) Console.WriteLine($"   UNPROBED {w}");

        var probedClasses = _results.Values.Select(r => r.ClassName).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var never = classes.Keys.Where(c => !probedClasses.Contains(c)).OrderBy(c => c, StringComparer.Ordinal).ToList();
        Console.WriteLine($"registered classes: {classes.Count}   CDOs probed: {_results.Count}   " +
                          $"registered classes whose CDO was never probed: {never.Count}" +
                          $"   ({_duplicates} repeat reads discarded)");
        foreach (var c in never.Take(Common.ShowErrors)) Console.WriteLine($"   UNPROBED {c}");

        // The iterated set only, as in `uds`: a CDO probed while loading an import is outside the filter.
        var all = _results.Values.Where(r => seenCdos.Contains(r.Key)).ToList();
        if (all.Count < _results.Count)
            Console.WriteLine($"   ({_results.Count - all.Count} more CDOs probed as imports, outside the filter: not counted)");
        Dual.PrintResults(all, holders: true);
        UdsMode.PrintGuards(all, r => r.UdsValues > 0,
                            "serialized >= 1 UDS-typed value (a UDS-typed tag that consumed bytes)");
        if (ver > 0 && unv == 0)
            Console.WriteLine("*** every package is VERSIONED: the mapping is never consulted. ***");
        return 0;
    }
}
