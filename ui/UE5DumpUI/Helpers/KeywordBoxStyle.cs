using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;

namespace UE5DumpUI.Helpers;

/// <summary>
/// [LEAD-KEYWORD-DROPDOWN-FIRST-CLICK] The app-wide style of the keyword boxes' keyword-memory dropdown. Every keyword
/// box is an AutoCompleteBox, and its dropdown is open right after typing. Its popup dismisses on a click outside and,
/// by default, swallows that click, so a click on the box's next-match button would only close the dropdown and a
/// second click would step. This style passes the closing click on to what it landed on. Built in C#, not AXAML, so
/// the headless test can apply the very style App adds.
/// </summary>
public static class KeywordBoxStyle
{
    public static Style Create() => new(x => x.OfType<AutoCompleteBox>().Template().OfType<Popup>())
    {
        Setters = { new Setter(Popup.OverlayDismissEventPassThroughProperty, true) },
    };
}
