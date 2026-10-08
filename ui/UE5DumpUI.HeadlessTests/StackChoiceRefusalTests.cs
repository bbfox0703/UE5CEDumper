using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.HeadlessTests;

/// <summary>
/// [LIVEFUNCS-STEP3] Live Funcs' Stack? box on a real CheckBox: bound one way to <see cref="PeProfileEntry.IsStackChosen"/>,
/// with the view model's ToggleStack as its command. Choosing a per-frame function asks first (T9.2), and a refusal
/// leaves the value as it was. Avalonia 12.1.3 flips the box on the click, before the command runs, and an unchanged
/// value raises nothing, so the box would show a choice never made. Pinned here: Avalonia's flip and when it happens,
/// and that <see cref="PeProfileEntry.RaiseIsStackChosen"/> reads the unchanged value back into the box.
/// </summary>
public class StackChoiceRefusalTests
{
    private sealed class Refuse(Action onRefused) : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => onRefused();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }

    private static PeProfileEntry PerFrameRow() => new()
    {
        ClassName = "A", FuncName = "Tick", FnameKey = new NameKey(1, 0, 9, 0), IsPerFrame = true,
    };

    /// <summary>The Stack? cell's box, clicked: the row as its data context, the binding one way, as LiveFuncsPanel.axaml
    /// has it. The command is made for the box, so it can read the box as it runs.</summary>
    private static CheckBox ClickBox(PeProfileEntry row, Func<CheckBox, ICommand> command)
    {
        var box = new CheckBox { CommandParameter = row, DataContext = row };
        box.Command = command(box);
        box.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(PeProfileEntry.IsStackChosen)) { Mode = BindingMode.OneWay });
        var window = new Window { Content = box, Width = 200, Height = 100 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(box.IsChecked);

        var centre = box.TranslatePoint(new Point(box.Bounds.Width / 2, box.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        return box;
    }

    [Fact]
    public Task A_refused_choice_reads_the_unchanged_value_back_into_the_box() => Headless.Run(() =>
    {
        var row = PerFrameRow();
        var box = ClickBox(row, _ => new Refuse(row.RaiseIsStackChosen));

        Assert.False(row.IsStackChosen);
        Assert.False(box.IsChecked);
    });

    /// <summary>Avalonia's own behaviour, the reason the re-raise exists: the box is ticked by the time the command runs
    /// (so it shows ticked while the question is open), and stays ticked over a refusal. A future Avalonia that reads
    /// the value back by itself shows up here.</summary>
    [Fact]
    public Task Without_the_re_raise_a_refused_click_leaves_the_box_ticked() => Headless.Run(() =>
    {
        var row = PerFrameRow();
        bool? seenByTheCommand = null;
        var box = ClickBox(row, b => new Refuse(() => seenByTheCommand = b.IsChecked));

        Assert.True(seenByTheCommand);
        Assert.False(row.IsStackChosen);
        Assert.True(box.IsChecked);
    });
}
