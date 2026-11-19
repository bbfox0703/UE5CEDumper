using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Views;

/// <summary>
/// Confirmation for removing leftover proxy DLLs. Code-behind only, AOT-safe, matching the other
/// dialogs in this folder.
///
/// <para>Deliberately NOT <see cref="ConfirmDialog"/>: that one is a fixed 460px, height-to-content,
/// non-resizable box with no scroller, and this content is a per-row list of absolute paths. An
/// unexplained delete list cannot be audited by whoever clicks it, so this window shows, for every
/// row: each file with its size and version, each folder that will be removed IN REMOVAL ORDER, what
/// is explicitly NOT touched, and the evidence for calling it a leftover at all.</para>
/// </summary>
public sealed class OrphanCleanupConfirmDialog : Window
{
    private bool _result;

    private static readonly IBrush Dim = new SolidColorBrush(Color.Parse("#8A8A8A"));
    private static readonly IBrush Text = new SolidColorBrush(Color.Parse("#D4D4D4"));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#4EC9B0"));
    private static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#E6A817"));
    private static readonly IBrush Danger = new SolidColorBrush(Color.Parse("#E05252"));

    /// <summary>Show modally over the desktop main window. Returns true only on explicit confirm.</summary>
    public static async Task<bool> ShowAsync(IReadOnlyList<OrphanProxy> rows)
    {
        if (Application.Current?.ApplicationLifetime is not
            IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } owner)
            return false;

        var dlg = new OrphanCleanupConfirmDialog(rows);
        await dlg.ShowDialog(owner);
        return dlg._result;
    }

    private OrphanCleanupConfirmDialog(IReadOnlyList<OrphanProxy> rows)
    {
        Title = Res.Get("str.ProxyDeploy.Orphans.ConfirmTitle");
        Width = 760;
        Height = 560;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#1E1E1E"));

        var root = new DockPanel { Margin = new Thickness(16) };

        // --- lead paragraph: says where things go, unambiguously ---
        var lead = new TextBlock
        {
            Text = Res.Get("str.ProxyDeploy.Orphans.ConfirmLead"),
            Foreground = Text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        DockPanel.SetDock(lead, Dock.Top);
        root.Children.Add(lead);

        // --- buttons pinned to the bottom so they survive any list length ---
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var cancel = new Button
        {
            Content = Res.Get("str.ProxyDeploy.Orphans.ConfirmNo"),
            Padding = new Thickness(14, 5),
            IsCancel = true,
        };
        cancel.Click += (_, _) => { _result = false; Close(); };
        var confirm = new Button
        {
            Content = Res.Get("str.ProxyDeploy.Orphans.ConfirmYes"),
            Padding = new Thickness(14, 5),
            Foreground = Danger,
        };
        confirm.Click += (_, _) => { _result = true; Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        // --- the auditable list ---
        var list = new StackPanel { Spacing = 10 };
        foreach (var row in rows) list.Children.Add(BuildRow(row));

        root.Children.Add(new ScrollViewer
        {
            Content = list,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        });

        Content = root;
    }

    private static Control BuildRow(OrphanProxy row)
    {
        var panel = new StackPanel { Spacing = 3 };

        panel.Children.Add(Label(row.DllDirectory, Accent, bold: true, mono: true));

        // (2) every file, with size and version — the version is what lets the user recognise it.
        panel.Children.Add(Label(Res.Get("str.ProxyDeploy.Orphans.ConfirmFilesHeader"), Dim));
        string fileLine = $"    {row.DllNames}";
        if (row.SizeText.Length > 0) fileLine += $"    {row.SizeText}";
        if (row.FileVersion.Length > 0) fileLine += $"    v{row.FileVersion}";
        panel.Children.Add(Label(fileLine, Text, mono: true));

        // (1) every directory, deepest-first, in the order they will actually be removed.
        if (row.ChainDirs.Count > 0)
        {
            panel.Children.Add(Label(Res.Get("str.ProxyDeploy.Orphans.ConfirmDirsHeader"), Dim));
            int n = 1;
            foreach (string d in row.ChainDirs)
                panel.Children.Add(Label($"    {n++}. {d}", Text, mono: true));

            // What is explicitly NOT touched. Stating the boundary is half the reassurance.
            if (!string.IsNullOrEmpty(row.TopmostRemovableDir))
            {
                string? parent = System.IO.Path.GetDirectoryName(row.TopmostRemovableDir);
                if (!string.IsNullOrEmpty(parent))
                {
                    panel.Children.Add(Label(
                        Res.Format("str.ProxyDeploy.Orphans.ConfirmKeptNote", parent), Warn, mono: true));
                }
            }
        }
        else
        {
            // File-only. Do NOT assert a reason we did not check: PlanPrune returns FileOnly for
            // six different reasons and "outside a Steam library" is only one of them. The most
            // common is !prunableLeaf — a third-party wrapper such as ReShade's dxgi.dll sitting
            // beside our version.dll — which happens INSIDE a library, and whose real blocker is
            // printed two lines below by the Blockers loop. Say the outcome (which is certain) and
            // let Blockers say the cause, as the dry-run report does.
            panel.Children.Add(Label(
                "    " + Res.Get("str.ProxyDeploy.Orphans.FileOnlyNote"), Warn));
        }

        // (5) blockers, if this row is only partially actionable.
        foreach (string b in row.Blockers)
            panel.Children.Add(Label($"    {b}", Warn));

        // (3) why it was judged a leftover, naming the evidence.
        if (row.EvidenceText.Length > 0)
        {
            panel.Children.Add(Label(Res.Get("str.ProxyDeploy.Orphans.ConfirmWhyHeader"), Dim));
            panel.Children.Add(Label($"    {row.EvidenceText}", Dim));
        }

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#252526")),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8),
            Child = panel,
        };
    }

    private static TextBlock Label(string text, IBrush brush, bool bold = false, bool mono = false) =>
        new()
        {
            Text = text,
            Foreground = brush,
            FontSize = 11,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            FontFamily = mono ? new FontFamily("Consolas") : FontFamily.Default,
            TextWrapping = TextWrapping.Wrap,
        };
}
