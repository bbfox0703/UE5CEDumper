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
//   --control                      uds / holders POSITIVE CONTROL: every entry the mapping lacks
//                                  is synthesized from the cooked schema, so every comparison
//                                  must come out SAME. It proves the harness (lookup by name,
//                                  seek-back, a deterministic comparison) and NOTHING about any
//                                  .usmap -- run it once before trusting a SAME from a real one.
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
// ⚠ WHAT THE uds / holders COMPARISON CANNOT SEE. (1) A property the cooked header SKIPS: an
// unversioned header lists only serialized properties, so a mapping descriptor for a skipped one
// is exercised only as an index slot, never by type. (2) A zero-mask property consumes no bytes,
// so its type is barely exercised -- which is why the "values" guard counts tags that consumed
// bytes, not tags. (3) Native types reached from either read (a Vector member, a native parent
// class) go through the SAME mapping or native reader on both sides and cancel; a wrong
// descriptor there reads identically wrong twice. (4) `uds` looks the UDS up by its COOKED
// object name; a consumer reading a holder looks it up by whatever name the mapping's own member
// descriptor spells, so a mapping that is internally consistent but names a UDS differently is
// MAPPING-MISSING here and fine in `holders` -- run both.
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

// A doubled enumerator prefix -- "EFoo::EFoo::Bar" against "EFoo::Bar" -- is the same VALUE
// rendered differently, not a misread: it means one writer stored the member name fully
// qualified and the other stored it bare, and the consumer qualifies it again. Report it
// separately so a cosmetic difference is never counted as a misalignment.
var doubled = new Regex(@"([A-Za-z_][A-Za-z0-9_]*)::\1::");
string Collapse(string s) { string p; do { p = s; s = doubled.Replace(s, "$1::"); } while (s != p); return s; }
var realDiffer = differ.Where(k => Collapse(a[k]) != Collapse(b[k])).ToList();
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

    public static void ControlBanner()
    {
        if (Control)
            Console.WriteLine("*** CONTROL RUN: every entry the mapping lacks is synthesized from the COOKED " +
                              "schema. SAME everywhere proves the harness only -- nothing about the .usmap. ***");
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
    public string Detail = "";
    public int Tags, Values, UdsTags, UdsValues;
    public long CookedBytes = -1, MappedBytes = -1;
    // Parse end minus the export's end (validPos). Both reads start at the same byte, so a
    // misaligned start makes them agree on the WRONG bytes; ending exactly at the export's end is
    // the one independent witness that the compared bytes were the intended ones.
    public long? EndDelta;
    public string? CookedJson, MappedJson;
    public readonly List<string> DifferingTags = new();
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

    // Reads the bytes at Ar.Position twice and leaves Ar.Position where it found it.
    //   cooked:  FStructFallback(Ar, UStruct) -> DeserializePropertiesUnversioned with a
    //            NON-UScriptClass -> SerializedStruct(cooked FProperty list): exactly the call
    //            UUserDefinedStruct.Deserialize / UObject.Deserialize make.
    //   mapping: FStructFallback(Ar, string) -> new UScriptClass(name) -> Mappings.Types[name].
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
            r.CookedJson = JsonConvert.SerializeObject(cooked, Formatting.None);
        }
        catch (Exception ex)
        {
            r.Verdict = "COOKED-FAILED";
            r.Detail = Common.Describe(ex);
            return r;
        }
        finally
        {
            Ar.Position = start;
        }
        r.Tags = cooked.Properties.Count;
        r.Values = cooked.Properties.Count(t => t.Size > 0);
        var uds = cooked.Properties.Where(IsUdsTag).ToList();
        r.UdsTags = uds.Count;
        r.UdsValues = uds.Count(t => t.Size > 0);

        // Asked EXPLICITLY, not inferred from an exception: DeserializePropertiesUnversioned
        // returns before the lookup when the header carries no values, so an absent name would
        // otherwise come out as two empty readings -- SAME.
        var types = Ar.Owner?.Mappings?.Types;
        if (Common.Control && types != null && !types.ContainsKey(mappingName))
        {
            // The same SerializedStruct the cooked read builds, but REACHED BY NAME: whatever
            // then differs is the harness's doing, never the mapping's.
            types[mappingName] = new SerializedStruct(Ar.Owner!.Mappings, cookedSchema);
            r.Detail = "control: entry synthesized from the cooked schema";
        }
        if (types == null || !types.ContainsKey(mappingName))
        {
            r.Verdict = "MAPPING-MISSING";
            return r;
        }

        FStructFallback mapped;
        try
        {
            mapped = new FStructFallback(Ar, mappingName);
            r.MappedBytes = Ar.Position - start;
            r.MappedJson = JsonConvert.SerializeObject(mapped, Formatting.None);
        }
        catch (Exception ex)
        {
            r.Verdict = "FAILED";
            r.Detail = Common.Describe(ex);
            return r;
        }
        finally
        {
            Ar.Position = start;
        }

        // The end position is compared as well as the JSON: a descriptor of the wrong WIDTH can
        // decode the same value from fewer bytes and leave the stream misaligned for whatever
        // follows, which the JSON of this struct alone cannot show.
        if (r.CookedJson == r.MappedJson && r.CookedBytes == r.MappedBytes)
        {
            r.Verdict = "SAME";
            return r;
        }
        r.Verdict = "DIFFER";
        var c = TagMap(cooked);
        var m = TagMap(mapped);
        foreach (var k in c.Keys.Union(m.Keys))
        {
            bool inC = c.TryGetValue(k, out var cv), inM = m.TryGetValue(k, out var mv);
            if (inC && inM && cv.json == mv.json) continue;
            string where = !inM ? " (cooked only)" : !inC ? " (mapping only)" : "";
            bool isUds = (inC && IsUdsTag(cv.tag)) || (inM && IsUdsTag(mv.tag));
            r.DifferingTags.Add(k + (isUds ? " [UDS]" : "") + where);
        }
        return r;
    }

    static Dictionary<string, (string json, FPropertyTag tag)> TagMap(FStructFallback s)
    {
        var d = new Dictionary<string, (string, FPropertyTag)>();
        foreach (var t in s.Properties)
        {
            string k = t.ArrayIndex > 0 ? $"{t.Name.Text}[{t.ArrayIndex}]" : t.Name.Text;
            d[k] = (JsonConvert.SerializeObject(t.Tag, Formatting.None), t);
        }
        return d;
    }

    public static readonly string[] VerdictOrder =
        { "DIFFER", "FAILED", "COOKED-FAILED", "HEADER-FAILED", "MAPPING-MISSING", "VERSIONED",
          "SKIPPED", "NO-DEFAULT-INSTANCE", "SAME" };

    public static void PrintResults(IReadOnlyCollection<ProbeResult> results, bool holders)
    {
        var counts = results.GroupBy(r => r.Verdict).ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine("verdicts: " + string.Join("  ",
            VerdictOrder.Where(counts.ContainsKey).Select(v => $"{v}={counts[v]}")));
        foreach (var v in VerdictOrder.Where(counts.ContainsKey))
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
                Console.WriteLine($"  {v,-16} {r.Key}  {counts2}" +
                                  (r.Detail.Length > 0 ? $"\n      {r.Detail}" : ""));
                if (v == "DIFFER")
                {
                    Console.WriteLine($"      differing tags: {string.Join(", ", r.DifferingTags.Take(8))}" +
                                      (r.DifferingTags.Count > 8 ? $" (+{r.DifferingTags.Count - 8})" : ""));
                    if (r.CookedJson != null && r.MappedJson != null)
                        Common.PrintAround("      ", "cooked ", r.CookedJson, "mapping", r.MappedJson);
                }
            }
        }
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

        var all = _results.Values.ToList();
        Dual.PrintResults(all, holders: false);
        PrintGuards(all, r => r.Values > 0, "serialized >= 1 value (a property that consumed bytes)");
        if (ver > 0 && unv == 0)
            Console.WriteLine("*** every package is VERSIONED: the mapping is never consulted. ***");
        return 0;
    }

    public static void PrintGuards(List<ProbeResult> all, Func<ProbeResult, bool> hasValues, string what)
    {
        int withValues = all.Count(hasValues);
        int compared = all.Count(r => r.Verdict is "SAME" or "DIFFER");
        int comparedWithValues = all.Count(r => r.Verdict is "SAME" or "DIFFER" && hasValues(r));
        Console.WriteLine("\nguards:");
        Console.WriteLine($"  {what,-62}: {withValues} of {all.Count}");
        Console.WriteLine($"  {"compared (both reads completed: SAME + DIFFER)",-62}: {compared}");
        var ends = all.Where(r => r.EndDelta.HasValue).ToList();
        Console.WriteLine($"  {"parse ended exactly at the export's end",-62}: " +
                          $"{ends.Count(r => r.EndDelta == 0)} of {ends.Count}");
        Console.WriteLine($"  {"compared AND serialized >= 1 value (what actually proves anything)",-62}: {comparedWithValues}");
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

        var all = _results.Values.ToList();
        Dual.PrintResults(all, holders: true);
        UdsMode.PrintGuards(all, r => r.UdsValues > 0,
                            "serialized >= 1 UDS-typed value (a UDS-typed tag that consumed bytes)");
        if (ver > 0 && unv == 0)
            Console.WriteLine("*** every package is VERSIONED: the mapping is never consulted. ***");
        return 0;
    }
}
