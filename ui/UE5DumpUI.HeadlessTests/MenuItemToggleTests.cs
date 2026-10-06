using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.HeadlessTests;

/// <summary>
/// [MENU-TOGGLE-CLICK] A <c>ToggleType="CheckBox"</c> MenuItem on a real Avalonia Menu, clicked with the mouse.
/// Measured on build 3625 (Avalonia 12.1.3): ticking "Dump All also writes the object index" in the Export menu,
/// and Live Walker's "Append +Type", changed nothing — no check glyph, no option saved. Each case runs without the
/// fix (Avalonia's own behaviour, so an Avalonia that starts toggling by itself shows up here and the fix, which
/// would then undo its toggle, must go) and with it.
/// </summary>
public class MenuItemToggleTests
{
    private static (Window Window, MenuItem Item) ShowMenu()
    {
        var item = new MenuItem { Header = "Toggle me", ToggleType = MenuItemToggleType.CheckBox };
        var menu = new Menu();
        menu.Items.Add(item);
        var window = new Window { Content = menu, Width = 300, Height = 120 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, item);
    }

    private static void ClickWithTheMouse(Window window, MenuItem item)
    {
        var centre = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task Without_the_fix_a_click_does_not_tick_a_checkbox_menu_item() => Headless.Run(() =>
    {
        var (window, item) = ShowMenu();

        ClickWithTheMouse(window, item);

        Assert.False(item.IsChecked);
        window.Close();
    });

    [Fact]
    public Task With_the_fix_each_click_flips_it_once() => Headless.Run(() =>
    {
        using var fix = MenuToggleFix.Register();
        var (window, item) = ShowMenu();

        ClickWithTheMouse(window, item);
        Assert.True(item.IsChecked);
        ClickWithTheMouse(window, item);
        Assert.False(item.IsChecked);
        window.Close();
    });

    [Fact]
    public Task The_fix_leaves_a_plain_menu_item_alone() => Headless.Run(() =>
    {
        using var fix = MenuToggleFix.Register();
        var item = new MenuItem { Header = "Plain" };
        var menu = new Menu();
        menu.Items.Add(item);
        var window = new Window { Content = menu, Width = 300, Height = 120 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        ClickWithTheMouse(window, item);

        Assert.False(item.IsChecked);
        window.Close();
    });
}
