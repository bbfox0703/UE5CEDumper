using Avalonia;
using Avalonia.Controls;

namespace UE5DumpUI.Core;

/// <summary>
/// Helper for loading string resources from en.axaml in code-behind.
/// Ensures all UI strings are externalized per CLAUDE.md rules.
/// </summary>
public static class Res
{
    /// <summary>Load a string resource by key. Returns empty string if not found.</summary>
    public static string Get(string key)
    {
        if (Application.Current?.TryFindResource(key, out object? value) == true && value is string s)
            return s;
        return "";
    }

    /// <summary>Load a string resource and format it with arguments (string.Format style).</summary>
    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        return string.IsNullOrEmpty(template) ? "" : string.Format(template, args);
    }
}
