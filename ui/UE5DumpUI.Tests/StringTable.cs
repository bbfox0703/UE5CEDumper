using System.Xml.Linq;

namespace UE5DumpUI.Tests;

/// <summary>
/// [I18N-READINESS-2026-11-04] en.axaml read as plain XML: every <c>x:Key</c>'d element's text, entities decoded. The
/// one reader for code with no Avalonia application -- the unit tests, which kept fourteen hand-rolled ones.
/// It lives in the tests, NOT the app: in UE5DumpUI it pulled System.Xml.Linq into the AOT image (+3.1 MB, build 3689)
/// for a parser the app never calls. UE5DumpUI.HeadlessTests compiles this same file (a linked Compile item) and pins
/// it against Avalonia's own loader, key for key, so a test cannot assert a sentence the app never shows.
/// </summary>
internal static class StringTable
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Key -> text for each element of the dictionary that carries an <c>x:Key</c>.</summary>
    public static Dictionary<string, string> Parse(string axaml)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var root = XDocument.Parse(axaml).Root;
        if (root == null) return map;
        foreach (var e in root.Elements())
        {
            if (e.Attribute(Xaml + "Key")?.Value is { } key)
                map[key] = e.Value;
        }
        return map;
    }
}
