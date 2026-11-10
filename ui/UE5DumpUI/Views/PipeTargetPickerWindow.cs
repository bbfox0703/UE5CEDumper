using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Views;

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Connect's game list, shown when several games serve UE5Dumper's pipe, or one does
/// beside an old DLL's legacy pipe. Each row is a game: its exe name, its main window title when it has one, its pid.
/// The legacy row says an old DLL runs somewhere and cannot be chosen. Returns the chosen pid, or null on cancel.
/// Code-behind only (AOT-safe, like ProcessPickerWindow); every word comes from en.axaml.
/// </summary>
public sealed class PipeTargetPickerWindow : Window
{
    private readonly ListBox _list;
    private readonly Button _connect;
    private int? _result;

    /// <summary>Show the list modally over the main window. Null when cancelled or when there is no main window.</summary>
    public static async Task<int?> ShowAsync(IReadOnlyList<PipeTarget> rows)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } owner)
            return null;
        var dlg = new PipeTargetPickerWindow(rows);
        await dlg.ShowDialog(owner);
        return dlg._result;
    }

    private PipeTargetPickerWindow(IReadOnlyList<PipeTarget> rows)
    {
        Title = Res.Get("str.Connect.Picker.Title");
        Width = 640;
        Height = 380;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#1E1E1E"));

        var root = new DockPanel { Margin = new Thickness(12) };

        var hint = new TextBlock
        {
            Text = Res.Get("str.Connect.Picker.Hint"),
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(hint);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var cancel = new Button { Content = Res.Get("str.Connect.Picker.Cancel"), Padding = new Thickness(14, 6) };
        cancel.Click += (_, _) => { _result = null; Close(); };
        _connect = new Button
        {
            Content = Res.Get("str.Connect.Picker.Connect"), Padding = new Thickness(14, 6),
            Foreground = new SolidColorBrush(Color.Parse("#4EC9B0")),
            BorderBrush = new SolidColorBrush(Color.Parse("#4EC9B0")),
            BorderThickness = new Thickness(1), Background = Brushes.Transparent,
        };
        _connect.Click += (_, _) => Accept();
        btnRow.Children.Add(cancel);
        btnRow.Children.Add(_connect);
        DockPanel.SetDock(btnRow, Dock.Bottom);
        root.Children.Add(btnRow);

        _list = new ListBox
        {
            Background = new SolidColorBrush(Color.Parse("#252526")),
            Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<PipeTarget>((row, _) => row is null ? new TextBlock() : BuildRow(row),
                                                          supportsRecycling: false),
        };
        _list.SelectionChanged += (_, _) => _connect.IsEnabled = _list.SelectedItem is PipeTarget { IsLegacy: false };
        _list.DoubleTapped += (_, _) => Accept();
        _list.SelectedItem = rows.FirstOrDefault(r => !r.IsLegacy);
        _connect.IsEnabled = _list.SelectedItem is PipeTarget { IsLegacy: false };
        root.Children.Add(_list);

        Content = root;
    }

    private static Control BuildRow(PipeTarget row)
    {
        if (row.IsLegacy)
        {
            // Amber, like ProcessPickerWindow's "already loaded" line: a fact to act on, not a choice.
            return new TextBlock
            {
                Text = Res.Get("str.Connect.Picker.LegacyRow"),
                Foreground = new SolidColorBrush(Color.Parse("#D7BA7D")),
                FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 3),
            };
        }
        string image = string.IsNullOrEmpty(row.ImageName) ? Res.Get("str.Connect.Picker.UnknownImage") : row.ImageName;
        var name = new TextBlock
        {
            Text = Res.Format("str.Connect.Picker.Row", image, row.Pid),
            Foreground = new SolidColorBrush(Color.Parse("#4EC9B0")),
            FontWeight = FontWeight.SemiBold,
        };
        var title = new TextBlock
        {
            Text = row.WindowTitle ?? Res.Get("str.Connect.Picker.NoWindowTitle"),
            Foreground = new SolidColorBrush(Color.Parse("#888888")),
            FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        return new StackPanel { Margin = new Thickness(2, 3), Children = { name, title } };
    }

    private void Accept()
    {
        if (_list.SelectedItem is PipeTarget { IsLegacy: false } t)
        {
            _result = t.Pid;
            Close();
        }
    }
}
