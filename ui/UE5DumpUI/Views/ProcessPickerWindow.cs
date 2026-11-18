using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using UE5DumpUI.Models;

namespace UE5DumpUI.Views;

/// <summary>
/// Modal process picker for "Inject into running game". Lists UE game processes
/// (or every accessible process when "Show all" is ticked) and returns the
/// selected one, or null on cancel. Code-behind only (AOT-safe, like ConfirmDialog).
/// The process list comes from an injected loader so the window stays free of
/// platform/service references.
/// </summary>
public sealed class ProcessPickerWindow : Window
{
    private readonly Func<bool, Task<IReadOnlyList<GameProcessInfo>>> _loader;
    private readonly ListBox _list;
    private readonly CheckBox _showAll;
    private readonly TextBlock _status;
    private GameProcessInfo? _result;

    /// <summary>Show the picker modally over the main window. <paramref name="loader"/>
    /// takes showAll → the process list. Returns the chosen process, or null.</summary>
    public static async Task<GameProcessInfo?> ShowAsync(
        Func<bool, Task<IReadOnlyList<GameProcessInfo>>> loader)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } owner)
            return null;
        var dlg = new ProcessPickerWindow(loader);
        await dlg.ShowDialog(owner);
        return dlg._result;
    }

    private ProcessPickerWindow(Func<bool, Task<IReadOnlyList<GameProcessInfo>>> loader)
    {
        _loader = loader;
        Title = "Inject into running game";
        Width = 640;
        Height = 460;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#1E1E1E"));

        var root = new DockPanel { Margin = new Thickness(12) };

        // Top row — show-all + refresh + hint.
        var top = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _showAll = new CheckBox
        {
            Content = "Show all processes",
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _showAll.IsCheckedChanged += async (_, _) => await ReloadAsync();
        var refresh = new Button { Content = "Refresh", Padding = new Thickness(10, 4) };
        refresh.Click += async (_, _) => await ReloadAsync();
        var hint = new TextBlock
        {
            Text = "Pick the running UE game, then Inject.",
            Foreground = new SolidColorBrush(Color.Parse("#888888")),
            FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
        };
        top.Children.Add(_showAll);
        top.Children.Add(refresh);
        top.Children.Add(hint);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        // Bottom row — status + Cancel/Inject.
        var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        _status = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#888888")),
            FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(_status, Dock.Left);
        bottom.Children.Add(_status);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
        };
        var btnCancel = new Button { Content = "Cancel", Padding = new Thickness(14, 6) };
        btnCancel.Click += (_, _) => { _result = null; Close(); };
        var btnInject = new Button
        {
            Content = "Inject", Padding = new Thickness(14, 6),
            Foreground = new SolidColorBrush(Color.Parse("#4EC9B0")),
            BorderBrush = new SolidColorBrush(Color.Parse("#4EC9B0")),
            BorderThickness = new Thickness(1), Background = Brushes.Transparent,
        };
        btnInject.Click += (_, _) => Accept();
        btnRow.Children.Add(btnCancel);
        btnRow.Children.Add(btnInject);
        DockPanel.SetDock(btnRow, Dock.Right);
        bottom.Children.Add(btnRow);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        // Center — the process list.
        _list = new ListBox
        {
            Background = new SolidColorBrush(Color.Parse("#252526")),
            Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
            ItemTemplate = new FuncDataTemplate<GameProcessInfo>((item, _) =>
            {
                if (item is null) return new TextBlock();
                var title = new TextBlock
                {
                    Text = item.IsUe ? $"● {item.Display}" : item.Display,
                    Foreground = new SolidColorBrush(Color.Parse(item.IsUe ? "#4EC9B0" : "#D4D4D4")),
                    FontWeight = item.IsUe ? FontWeight.SemiBold : FontWeight.Normal,
                };
                var path = new TextBlock
                {
                    Text = item.Path,
                    Foreground = new SolidColorBrush(Color.Parse("#888888")),
                    FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis,
                };
                var panel = new StackPanel { Margin = new Thickness(2, 3), Children = { title, path } };
                // Amber "already loaded" line: our dumper DLL is active in this
                // process (proxy / prior inject / CE .CT). The VM connects to it rather
                // than injecting a second copy, which would take the game over from it.
                if (item.DumperLoaded)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = $"⬤ {item.DumperStatusLine} — already active; Inject will just Connect",
                        Foreground = new SolidColorBrush(Color.Parse("#D7BA7D")),
                        FontSize = 10, FontWeight = FontWeight.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    });
                }
                return panel;
            }, supportsRecycling: false),
        };
        _list.DoubleTapped += (_, _) => Accept();
        root.Children.Add(_list);

        Content = root;
        Opened += async (_, _) => await ReloadAsync();
    }

    private void Accept()
    {
        if (_list.SelectedItem is GameProcessInfo g)
        {
            _result = g;
            Close();
        }
    }

    private async Task ReloadAsync()
    {
        _status.Text = "Loading…";
        try
        {
            var loaded = await _loader(_showAll.IsChecked == true);
            var items = new List<GameProcessInfo>(loaded);
            // UE games first, then by name.
            items.Sort((a, b) =>
                a.IsUe != b.IsUe
                    ? (a.IsUe ? -1 : 1)
                    : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _list.ItemsSource = items;
            _status.Text = items.Count == 0 ? "No processes found." : $"{items.Count} process(es)";
        }
        catch (Exception ex)
        {
            _status.Text = $"Error: {ex.Message}";
        }
    }
}
