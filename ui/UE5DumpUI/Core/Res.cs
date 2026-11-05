using System;
using Avalonia;
using Avalonia.Controls;

namespace UE5DumpUI.Core;

/// <summary>
/// Helper for loading string resources from en.axaml in code-behind.
/// Ensures all UI strings are externalized per CLAUDE.md rules.
/// </summary>
public static class Res
{
    /// <summary>
    /// [I18N-READINESS-2026-11-04] Where a key's text comes from: the running app's merged resource dictionaries. A
    /// unit test has no Avalonia application, so this answered nothing there and every string behind Res read "" --
    /// the assertions that check a sentence is ABSENT then passed while testing nothing. The test assembly replaces it
    /// once, before any test runs, with en.axaml read as XML (<see cref="StringTable"/>). Null = no such key.
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
        return string.IsNullOrEmpty(template) ? "" : string.Format(template, args);
    }
}
