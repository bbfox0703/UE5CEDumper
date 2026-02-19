using Avalonia;
using Avalonia.Markup.Xaml.Styling;

namespace UE5DumpUI.Services;

/// <summary>
/// Manages runtime language switching by reordering pre-loaded resource dictionaries.
/// Supports: en, zh-TW, ja
///
/// AOT-safe: all ResourceInclude instances are defined in App.axaml (compile-time resolved).
/// This service only moves the desired language dictionary to the end of MergedDictionaries
/// so its keys take priority in DynamicResource lookups (last writer wins).
/// </summary>
public sealed class LocalizationService
{
    private static readonly string[] SupportedLanguages = ["en", "zh-TW", "ja"];
    private string _currentLanguage = "en";

    public string CurrentLanguage => _currentLanguage;

    public void SwitchLanguage(string lang)
    {
        if (!SupportedLanguages.Contains(lang)) return;
        if (lang == _currentLanguage) return;

        var app = Application.Current;
        if (app == null) return;

        var merged = app.Resources.MergedDictionaries;

        // Find the ResourceInclude whose Source URI contains the target language file
        var targetSuffix = $"/{lang}.axaml";
        ResourceInclude? target = null;

        for (int i = 0; i < merged.Count; i++)
        {
            if (merged[i] is ResourceInclude ri &&
                ri.Source?.ToString().EndsWith(targetSuffix, StringComparison.OrdinalIgnoreCase) == true)
            {
                target = ri;
                merged.RemoveAt(i);
                break;
            }
        }

        if (target != null)
        {
            // Re-add at end so it takes priority (last merged dictionary wins)
            merged.Add(target);
            _currentLanguage = lang;
        }
    }
}
