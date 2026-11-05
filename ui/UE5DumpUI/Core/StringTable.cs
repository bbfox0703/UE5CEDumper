using System.Collections.Generic;
using System.Xml.Linq;

namespace UE5DumpUI.Core;

/// <summary>
/// [I18N-READINESS-2026-11-04] en.axaml read as plain XML: every <c>x:Key</c>'d element's text, entities decoded. The
/// app never calls this -- it reads the file through Avalonia's resource dictionaries. It is the one reader for code
/// that has no Avalonia application, the unit tests above all, which kept fourteen hand-rolled ones. The headless
/// suite pins it against Avalonia's own loader, key for key, so a test cannot assert a sentence the app never shows.
/// </summary>
public static class StringTable
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Key -> text for each element of the dictionary that carries an <c>x:Key</c>.</summary>
    public static Dictionary<string, string> Parse(string axaml)
    {
        var map = new Dictionary<string, string>(System.StringComparer.Ordinal);
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
