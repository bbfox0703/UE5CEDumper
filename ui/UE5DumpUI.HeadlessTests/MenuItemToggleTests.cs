using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

namespace UE5DumpUI.HeadlessTests;

/// <summary>
/// A <c>ToggleType="CheckBox"</c> MenuItem in a DropDownButton's MenuFlyout, bound TwoWay to a view model — the shape
/// of the Export menu's "Dump All also writes the object index" ([EXTPR-539-540-2026-10-02] D4.1) and of Live
/// Walker's export options. Avalonia 12.1.3 flips IsChecked on the click and the binding writes it back.
/// </summary>
/// <remarks>
/// Written after a false alarm on the build-3625 live check: clicks sent in a separate computer-use call AFTER the
/// one that opened the flyout landed once the flyout had closed, so nothing ticked, and it read as Avalonia not
/// toggling. Opening and clicking in one batch ticked it and saved the option. One real quirk, pinned below: when
/// the flyout closes the item loses its DataContext and reads unchecked until it opens again, without writing that
/// back.
/// </remarks>
public class MenuItemToggleTests
{
    private sealed class Options : INotifyPropertyChanged
    {
        private bool _on;
        public bool On
        {
            get => _on;
            set { if (_on == value) return; _on = value; PropertyChanged?.Invoke(this, new(nameof(On))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private static (MenuFlyout Flyout, DropDownButton Button, MenuItem Item) Show(Options vm)
    {
        var item = new MenuItem { Header = "Dump All also writes the object index", ToggleType = MenuItemToggleType.CheckBox };
        item.Bind(MenuItem.IsCheckedProperty, new Binding(nameof(Options.On)) { Mode = BindingMode.TwoWay });
        var flyout = new MenuFlyout();
        flyout.Items.Add(item);
        var button = new DropDownButton { Content = "Export", Flyout = flyout };
        new Window { Content = button, Width = 400, Height = 300, DataContext = vm }.Show();
        Dispatcher.UIThread.RunJobs();
        return (flyout, button, item);
    }

    private static void OpenAndClick(MenuFlyout flyout, DropDownButton button, MenuItem item)
    {
        flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        var top = TopLevel.GetTopLevel(item)!;
        var centre = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), top)!.Value;
        top.MouseDown(centre, MouseButton.Left);
        top.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task A_click_ticks_the_option_and_a_second_unticks_it() => Headless.Run(() =>
    {
        var vm = new Options();
        var (flyout, button, item) = Show(vm);

        OpenAndClick(flyout, button, item);
        Assert.True(vm.On);
        Assert.False(flyout.IsOpen);

        OpenAndClick(flyout, button, item);
        Assert.False(vm.On);
    });

    [Fact]
    public Task The_closed_flyouts_item_reads_unchecked_until_it_reopens() => Headless.Run(() =>
    {
        var vm = new Options();
        var (flyout, button, item) = Show(vm);

        OpenAndClick(flyout, button, item);
        Assert.True(vm.On);
        Assert.False(item.IsChecked);     // detached: no DataContext, the binding's default, not written back

        flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        Assert.True(item.IsChecked);      // reattached: the option again
    });
}
