using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

namespace UE5DumpUI.HeadlessTests;

/// <summary>
/// [DUMPDIFF-UI] Dump Explorer's Compare is the repository's first SplitButton: its main part runs the command, its
/// arrow opens a MenuFlyout of two ToggleType CheckBox items bound TwoWay (the shape <see cref="MenuItemToggleTests"/>
/// pins for a DropDownButton). Pinned here: a click on the main part runs Compare and opens no menu, and a click on a
/// menu item writes the option back.
/// </summary>
public class SplitButtonCompareTests
{
    private sealed class Options : INotifyPropertyChanged
    {
        private bool _breakingOnly;
        public bool BreakingOnly
        {
            get => _breakingOnly;
            set { if (_breakingOnly == value) return; _breakingOnly = value; PropertyChanged?.Invoke(this, new(nameof(BreakingOnly))); }
        }
        public int Runs { get; private set; }
        public ICommand Compare { get; }
        public Options() => Compare = new Run(() => Runs++);
        public event PropertyChangedEventHandler? PropertyChanged;

        private sealed class Run(Action a) : ICommand
        {
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => a();
            public event EventHandler? CanExecuteChanged { add { } remove { } }
        }
    }

    private static (SplitButton Button, MenuFlyout Flyout, MenuItem Item, TopLevel Top) Show(Options vm)
    {
        var item = new MenuItem { Header = "Breaking changes only", ToggleType = MenuItemToggleType.CheckBox };
        item.Bind(MenuItem.IsCheckedProperty, new Binding(nameof(Options.BreakingOnly)) { Mode = BindingMode.TwoWay });
        var flyout = new MenuFlyout();
        flyout.Items.Add(item);
        var button = new SplitButton { Content = "Compare…", Flyout = flyout, Width = 160 };
        button.Bind(SplitButton.CommandProperty, new Binding(nameof(Options.Compare)));
        var window = new Window { Content = button, Width = 400, Height = 300, DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (button, flyout, item, window);
    }

    private static void Click(TopLevel top, Visual target, Point local)
    {
        var p = target.TranslatePoint(local, top)!.Value;
        top.MouseDown(p, MouseButton.Left);
        top.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task A_click_on_the_main_part_runs_Compare_and_opens_no_menu() => Headless.Run(() =>
    {
        var vm = new Options();
        var (button, flyout, _, top) = Show(vm);

        Click(top, button, new Point(20, button.Bounds.Height / 2));   // well left of the arrow

        Assert.Equal(1, vm.Runs);
        Assert.False(flyout.IsOpen);
    });

    [Fact]
    public Task A_menu_item_click_writes_the_option_back() => Headless.Run(() =>
    {
        var vm = new Options();
        var (button, flyout, item, top) = Show(vm);

        flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        Click(TopLevel.GetTopLevel(item)!, item, new Point(item.Bounds.Width / 2, item.Bounds.Height / 2));

        Assert.True(vm.BreakingOnly);
        Assert.Equal(0, vm.Runs);
    });
}
