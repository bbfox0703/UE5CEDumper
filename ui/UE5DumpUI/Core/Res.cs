using System;
using Avalonia;
using Avalonia.Controls;

namespace UE5DumpUI.Core;

/// <summary>
/// Helper for loading string resources from en.axaml in code-behind: the C# side
/// of the CLAUDE.md rule that UI strings are externalized.
/// </summary>
public static class Res
{
    /// <summary>
    /// [I18N-READINESS-2026-11-04] Where a key's text comes from: the running app's merged resource dictionaries. A
    /// unit test has no Avalonia application, so they answer nothing there and every string behind Res reads "" --
    /// an assertion that a sentence is ABSENT would then pass while testing nothing. The test assembly replaces it
    /// once, before any test runs, with en.axaml read as XML (the tests' StringTable, which is kept out of this
    /// assembly: it would put System.Xml.Linq in the AOT image). Null = no such key.
    /// </summary>
    internal static Func<string, string?> Lookup { get; set; } = FromApplication;

    private static string? FromApplication(string key)
        => Application.Current?.TryFindResource(key, out object? value) == true ? value as string : null;

    /// <summary>Load a string resource by key. Returns empty string if not found.</summary>
    public static string Get(string key) => Lookup(key) ?? "";

    /// <summary>Load a string resource and format it with arguments (string.Format style).</summary>
    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        return string.IsNullOrEmpty(template) ? "" : FormatTemplate(template, args);
    }

    /// <summary>
    /// [I18N-READINESS-2026-11-04] string.Format over a template from the string table -- the ONE place that does it
    /// (ResFormatSafetyTests). A template whose placeholders do not fit its arguments ("{2}" with two arguments, a
    /// stray brace: a translator's slip) would throw FormatException out of whatever command is setting the line;
    /// instead it degrades to the template's own text followed by the arguments: the line still says something, and
    /// the slip shows. tools/check_res_format.py keeps en.axaml's own templates from ever reaching this.
    /// </summary>
    public static string FormatTemplate(string template, params object?[] args)
    {
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return args.Length == 0 ? template : template + " [" + string.Join(", ", args) + "]";
        }
    }
}
