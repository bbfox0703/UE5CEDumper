using Avalonia;
using Avalonia.Markup.Xaml.Styling;

namespace UE5DumpUI.Services;

/// <summary>
/// Manages runtime language switching by swapping merged resource dictionaries.
/// Supports: en, zh-TW, ja
/// </summary>
public sealed class LocalizationService
{
    private static readonly string[] SupportedLanguages = ["en", "zh-TW", "ja"];
    private string _currentLanguage = "en";
    private ResourceInclude? _currentDict;

    public string CurrentLanguage => _currentLanguage;

    public void SwitchLanguage(string lang)
    {
        if (!SupportedLanguages.Contains(lang)) return;
        if (lang == _currentLanguage && _currentDict != null) return;

        var app = Application.Current;
        if (app == null) return;

        // Remove current language dictionary
        if (_currentDict != null)
        {
            app.Resources.MergedDictionaries.Remove(_currentDict);
        }

        // Add new language dictionary
        var uri = new Uri($"avares://UE5DumpUI/Resources/Strings/{lang}.axaml");
        _currentDict = new ResourceInclude(new Uri("avares://UE5DumpUI")) { Source = uri };
        app.Resources.MergedDictionaries.Add(_currentDict);

        _currentLanguage = lang;
    }
}
