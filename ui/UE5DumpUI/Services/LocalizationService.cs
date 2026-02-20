using Avalonia;
using Avalonia.Controls;
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

        // Application.Resources is the ResourceDictionary defined in App.axaml.
        // MergedDictionaries contains the ResourceInclude items for each language.
        var merged = app.Resources.MergedDictionaries;

        // Find the ResourceInclude whose Source URI contains the target language file.
        // At runtime the URI may be avares://UE5DumpUI/Resources/Strings/{lang}.axaml
        // or /Resources/Strings/{lang}.axaml — use Contains for robust matching.
        var targetFile = $"/{lang}.axaml";
        IResourceProvider? target = null;
        int targetIndex = -1;

        for (int i = 0; i < merged.Count; i++)
        {
            var item = merged[i];
            string? sourceStr = null;

            if (item is ResourceInclude ri)
                sourceStr = ri.Source?.ToString();

            if (sourceStr != null &&
                sourceStr.Contains(targetFile, StringComparison.OrdinalIgnoreCase))
            {
                target = item;
                targetIndex = i;
                break;
            }
        }

        if (target != null && targetIndex >= 0)
        {
            // Remove and re-add at end so it takes priority (last merged dictionary wins)
            merged.RemoveAt(targetIndex);
            merged.Add(target);
            _currentLanguage = lang;
        }
    }
}
