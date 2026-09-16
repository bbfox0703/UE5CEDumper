// Parse the SAME cooked assets under TWO .usmap mappings and diff every property reading.
//
//   dotnet run -c Release --project tools/verify/usmap_probe -- \
//       <paks-dir> <a.usmap> <b.usmap> [maxExports] [pathFilter] [EGame]
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
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

if (args.Length < 3)
{
    Console.Error.WriteLine(
        "usage: usmap_probe <paks-dir> <a.usmap> <b.usmap> [maxExports] [pathFilter] [EGame]");
    return 2;
}

string paks = args[0];
string usmapA = args[1];
string usmapB = args[2];
int max = args.Length > 3 ? int.Parse(args[3]) : 400;
string filter = args.Length > 4 ? args[4] : "";
EGame game = args.Length > 5 && Enum.TryParse<EGame>(args[5], out var parsed)
    ? parsed
    : EGame.GAME_UE5_4;

Dictionary<string, string> Run(string usmap, out Stats st)
{
    var provider = new DefaultFileProvider(paks, SearchOption.AllDirectories, false,
                                           new VersionContainer(game));
    provider.Initialize();
    provider.Mount();
    provider.MappingsContainer =
        new FileUsmapTypeMappingsProvider(usmap, StringComparer.OrdinalIgnoreCase);

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
            var flags = pkg switch
            {
                CUE4Parse.UE4.Assets.IoPackage io => io.Summary.PackageFlags,
                CUE4Parse.UE4.Assets.Package leg => leg.Summary.PackageFlags,
                _ => (EPackageFlags)0,
            };
            if (flags.HasFlag(EPackageFlags.PKG_UnversionedProperties)) st.Unversioned++;
            else st.Versioned++;

            foreach (var exp in pkg.GetExports())
                outp[kv.Key + "::" + exp.Name] = JsonConvert.SerializeObject(exp, Formatting.None);
            st.Ok++;
        }
        catch
        {
            st.Failed++;
        }
    }
    return outp;
}

var a = Run(usmapA, out var sa);
Console.WriteLine($"A  {Path.GetFileName(usmapA),-40} files={sa.Files} packages ok={sa.Ok} " +
                  $"failed={sa.Failed} exports={a.Count}  unversioned={sa.Unversioned} versioned={sa.Versioned}");
var b = Run(usmapB, out var sb);
Console.WriteLine($"B  {Path.GetFileName(usmapB),-40} files={sb.Files} packages ok={sb.Ok} " +
                  $"failed={sb.Failed} exports={b.Count}  unversioned={sb.Unversioned} versioned={sb.Versioned}");

if (sa.Unversioned == 0 && sa.Ok > 0)
    Console.WriteLine("\n*** NO package carries PKG_UnversionedProperties -- this cook is VERSIONED, " +
                      "the .usmap is never consulted, and any agreement below is meaningless. ***");

var shared = a.Keys.Intersect(b.Keys).ToList();
var differ = shared.Where(k => a[k] != b[k]).ToList();
Console.WriteLine($"\nshared exports: {shared.Count}   IDENTICAL: {shared.Count - differ.Count}   " +
                  $"DIFFER: {differ.Count}");
foreach (var k in differ.Take(8))
{
    Console.WriteLine($"\nDIFF {k}");
    Console.WriteLine($"   A: {a[k][..Math.Min(500, a[k].Length)]}");
    Console.WriteLine($"   B: {b[k][..Math.Min(500, b[k].Length)]}");
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
