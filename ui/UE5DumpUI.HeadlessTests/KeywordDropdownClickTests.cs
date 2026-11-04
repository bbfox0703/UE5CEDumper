using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.HeadlessTests;

/// <summary>
/// [LEAD-KEYWORD-DROPDOWN-FIRST-CLICK] Every keyword box is an AutoCompleteBox with a keyword-memory dropdown. Right
/// after typing that dropdown is open, and a click on the box's ▼ (next match) only closed it: the popup's
/// light-dismiss layer took the click, and the second click stepped. The app's keyword-box style (the one App adds)
/// lets the click that closes the dropdown go on to what it landed on.
/// </summary>
public class KeywordDropdownClickTests
{
    [Fact]
    public Task A_click_beside_an_open_keyword_dropdown_reaches_its_button() => Headless.Run(() =>
    {
        int clicks = 0;
        var auto = new AutoCompleteBox
        {
            Width = 200, MinimumPrefixLength = 1, FilterMode = AutoCompleteFilterMode.StartsWith,
            ItemsSource = new[] { "alpha", "alpine", "beta" },
        };
        var next = new Button { Content = "▼", Width = 40 };
        next.Click += (_, _) => clicks++;
        var window = new Window
        {
            Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { auto, next } },
            Width = 400, Height = 300,
        };
        window.Styles.Add(KeywordBoxStyle.Create());
        window.Show();
        Dispatcher.UIThread.RunJobs();
        auto.Focus();
        auto.Text = "al";
        auto.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(auto.IsDropDownOpen);   // the premise: the memory dropdown is open, as right after typing

        var p = next.TranslatePoint(new Point(next.Bounds.Width / 2, next.Bounds.Height / 2), window)!.Value;
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, clicks);             // was 0: the first click only closed the dropdown
        Assert.False(auto.IsDropDownOpen);
    });
}
