using Avalonia.Controls;
using Avalonia.Styling;

namespace UE5DumpUI.Helpers;

/// <summary>
/// [LEAD-KEYWORD-DROPDOWN-FIRST-CLICK] The app-wide style of the keyword boxes' keyword-memory dropdown.
/// </summary>
public static class KeywordBoxStyle
{
    public static Style Create() => new(x => x.OfType<AutoCompleteBox>());
}
