using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class LiveWalkerPanel : UserControl
{
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(60, 255, 200, 0));

    // AOT-safe sort comparers for the FieldGrid's TEMPLATE data columns ("Value" and
    // "Type"). Text columns sort out-of-box via their Binding path; a template column
    // has no column-level binding, so its reflection sort is trimmed under AOT — wire
    // an explicit comparer (aot-pitfalls.md §4.5).
    //
    // ⚠ "Type" joined this list on 2026-08-23 and did NOT start as a template column.
    // It was a DataGridTextColumn, therefore binding-rooted and safe, until
    // [V8PREVIEWCLIP-2026-08-23] converted it so the cell would have an element to hang
    // ToolTip.Tip on (a DataGridTextColumn's Binding= is not an element). That
    // conversion silently removed the column-level binding that was rooting TypeName,
    // and a sort header that animates and does nothing is invisible in every
    // JIT test host — only the trimmed binary misbehaves.
    //   DataGridSortWiringTests caught it the same minute, which is exactly what that
    // guard exists for. Worth remembering as a shape: making a column PRETTIER can
    // break its SORT, because the two ride on the same attribute.
    private static readonly IReadOnlyDictionary<string, IComparer> FieldsSortComparers =
        new Dictionary<string, IComparer>
        {
            ["DisplayValue"] = DataGridSortComparers.Ordinal<LiveFieldValue>(r => r.DisplayValue),
            ["TypeName"]     = DataGridSortComparers.Ordinal<LiveFieldValue>(r => r.TypeName),
        };

    // FunctionGrid's "Params" column (audit #5 AF20). This file wired 1 of its 3
    // DataGrids; the missed one is subtler than a template column: "Params" IS a
    // DataGridTextColumn, but its value comes from a <MultiBinding> in ELEMENT
    // syntax, which is a reflection binding rather than a compiled one — so nothing
    // roots NumParms and the header was inert in the shipped trimmed build. The
    // third grid (the reverse-lookup results) binds and sorts on the same path for
    // every column, so it is rooted and needs no dictionary.
    private static readonly IReadOnlyDictionary<string, IComparer> FunctionsSortComparers =
        new Dictionary<string, IComparer>
        {
            ["NumParms"] = DataGridSortComparers.Number<FunctionInfoModel>(r => r.NumParms),
        };

    // Audit fix #18: track the currently-subscribed VM so we can `-=` from
    // it before re-subscribing to a new one. Without this, every
    // DataContext reassignment leaves a stale handler on the previous VM
    // (keeping it alive) and adds a duplicate handler on the new one
    // (firing the scroll callback N times per event).
    private LiveWalkerViewModel? _subscribedVm;

    public LiveWalkerPanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("FieldGrid")?.WireSortComparers(FieldsSortComparers);
        this.FindControl<DataGrid>("FunctionGrid")?.WireSortComparers(FunctionsSortComparers);
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    /// <summary>
    /// Detach the six VM callbacks, if any are currently attached. Idempotent.
    /// </summary>
    private void UnsubscribeVm()
    {
        if (_subscribedVm == null) return;
        _subscribedVm.ScrollToFieldRequested -= OnScrollToFieldRequested;
        _subscribedVm.ScrollToFirstSearchMatch -= OnScrollToFirstSearchMatch;
        _subscribedVm.ScrollToFunctionRequested -= OnScrollToFunctionRequested;
        _subscribedVm.ScrollFieldIntoView -= OnScrollFieldIntoView;
        _subscribedVm.CaptureViewAnchor -= OnCaptureViewAnchor;
        _subscribedVm.RestoreBookmarkView -= OnRestoreBookmarkView;
        _subscribedVm = null;
    }

    /// <summary>
    /// Attach the six VM callbacks to the current DataContext, replacing any previous
    /// subscription. Idempotent — safe to call from both DataContextChanged and
    /// AttachedToVisualTree, which is the point.
    /// </summary>
    private void SubscribeVm()
    {
        if (ReferenceEquals(_subscribedVm, DataContext)) return;   // already wired to this VM
        UnsubscribeVm();

        if (DataContext is LiveWalkerViewModel vm)
        {
            vm.ScrollToFieldRequested += OnScrollToFieldRequested;
            vm.ScrollToFirstSearchMatch += OnScrollToFirstSearchMatch;
            vm.ScrollToFunctionRequested += OnScrollToFunctionRequested;
            vm.ScrollFieldIntoView += OnScrollFieldIntoView;
            vm.CaptureViewAnchor += OnCaptureViewAnchor;
            vm.RestoreBookmarkView += OnRestoreBookmarkView;
            _subscribedVm = vm;
        }
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e) => SubscribeVm();

    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // Cleanup when the panel leaves the visual tree — NOT final. A TabControl detaches
        // and re-attaches the panel on every tab switch, and re-attaching does not raise
        // DataContextChanged (the VM is the same object), so this used to be a one-way door:
        // after one round trip through another tab, scroll-to-field, scroll-to-match and the
        // bookmark view restore were all silently dead. OnAttached re-subscribes. (audit #5 AF4)
        UnsubscribeVm();

        if (_shineTimer != null)
        {
            _shineTimer.Stop();
            _shineTimer.Tick -= ShineTimer_Tick;
        }
    }

    // --- Empty-state logo "specular shine" easter-egg (build 1846) ----------
    // A rare, one-shot diagonal glint sweeps across the idle logo, like light
    // reflecting from one corner to the opposite. Kept deliberately low-odds: a
    // slow timer rolls a small probability, and only while the logo is showing.
    private static readonly Random ShineRng = new();
    private const double ShineTimerIntervalSeconds = 5.0;   // how often we roll
    private const double ShineTriggerProbability = 0.045;   // per-roll odds (rare)
    private const double ShineSweepMilliseconds = 820.0;    // single sweep length
    private DispatcherTimer? _shineTimer;
    private bool _shinePlaying;

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // Re-wire the VM callbacks OnDetached tore down. Idempotent, so the common case
        // (DataContextChanged already ran) costs a reference compare. (audit #5 AF4)
        SubscribeVm();

        _shineTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(ShineTimerIntervalSeconds),
        };
        _shineTimer.Tick -= ShineTimer_Tick; // guard against double-subscribe
        _shineTimer.Tick += ShineTimer_Tick;
        _shineTimer.Start();
    }

    private void ShineTimer_Tick(object? sender, EventArgs e)
    {
        if (_shinePlaying) return;
        // Only glint while the idle logo is actually on screen.
        if (DataContext is not LiveWalkerViewModel vm || !vm.ShowEmptyStateLogo) return;
        if (ShineRng.NextDouble() < ShineTriggerProbability)
            _ = PlayShineAsync();
    }

    /// <summary>
    /// Run one diagonal specular sweep across the logo, picking at random one of
    /// the four "corner to opposite corner" diagonals. A fresh TransformGroup is
    /// built each time so no animated state leaks between plays.
    /// </summary>
    private async Task PlayShineAsync()
    {
        if (_shinePlaying) return;

        var shine = this.FindControl<Border>("LogoShine");
        var bar = this.FindControl<Rectangle>("LogoShineBar");
        if (shine is null || bar is null) return;

        double w = shine.Bounds.Width, h = shine.Bounds.Height;
        if (w < 8 || h < 8) return; // not laid out yet — skip this roll

        _shinePlaying = true;
        try
        {
            // Two coin flips => one of four corner->opposite-corner diagonals.
            bool slashUp = ShineRng.Next(2) == 0; // band orientation: "/" vs "\"
            bool reverse = ShineRng.Next(2) == 0; // sweep direction along it
            double angle = slashUp ? -45.0 : 45.0;
            double range = w + h;                  // start/end fully off the image
            double fromX = reverse ? range : -range;
            double toX = reverse ? -range : range;

            var translate = new TranslateTransform { X = fromX };
            var group = new TransformGroup();
            group.Children.Add(new ScaleTransform(1.0, 2.4)); // lengthen band to span the diagonal
            group.Children.Add(new RotateTransform(angle));
            group.Children.Add(translate);
            bar.RenderTransformOrigin = RelativePoint.Center;
            bar.RenderTransform = group;

            shine.Opacity = 1;

            // Drive the sweep manually instead of Avalonia's Animation: the
            // built-in TransformAnimator only animates a Visual's RenderTransform
            // (it casts the target to Visual and throws InvalidCast when handed a
            // bare Transform). A per-frame timer that writes translate.X directly
            // re-renders cleanly, is AOT-safe, and gives full control of easing.
            await SweepXAsync(translate, fromX, toX, ShineSweepMilliseconds);
        }
        catch
        {
            // Easter-egg only — never let a glint failure surface to the user.
        }
        finally
        {
            shine.Opacity = 0;
            _shinePlaying = false;
        }
    }

    /// <summary>Ease translate.X from <paramref name="fromX"/> to <paramref name="toX"/>
    /// over <paramref name="durationMs"/> using a ~60fps dispatcher timer (sine in-out).</summary>
    private static Task SweepXAsync(TranslateTransform translate, double fromX, double toX, double durationMs)
    {
        var tcs = new TaskCompletionSource();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            double t = durationMs <= 0 ? 1.0 : Math.Min(1.0, sw.Elapsed.TotalMilliseconds / durationMs);
            double eased = -(Math.Cos(Math.PI * t) - 1.0) / 2.0; // sine ease-in-out
            translate.X = fromX + (toX - fromX) * eased;
            if (t >= 1.0)
            {
                timer.Stop();
                tcs.TrySetResult();
            }
        };
        timer.Start();
        return tcs.Task;
    }

    private void OnScrollToFieldRequested(string fieldName)
    {
        // Post to UI thread to ensure DataGrid has rendered the new items
        Dispatcher.UIThread.Post(() =>
        {
            var grid = this.FindControl<DataGrid>("FieldGrid");
            if (grid?.ItemsSource == null) return;

            var target = grid.ItemsSource.Cast<LiveFieldValue>()
                .FirstOrDefault(f => f.Name == fieldName);
            if (target != null)
            {
                grid.ScrollIntoView(target, null);
                grid.SelectedItem = target;
            }
        }, DispatcherPriority.Background);
    }

    private void OnScrollToFirstSearchMatch()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var grid = this.FindControl<DataGrid>("FieldGrid");
            if (grid?.ItemsSource == null) return;

            var target = grid.ItemsSource.Cast<LiveFieldValue>()
                .FirstOrDefault(f => f.IsSearchMatch);
            if (target != null)
                grid.ScrollIntoView(target, null);
        }, DispatcherPriority.Background);
    }

    // Match-navigation (prev/next): the VM already set SelectedField to this
    // exact row, so scroll it into view. Using the object (not the name)
    // keeps us on the right row when field names repeat.
    private void OnScrollFieldIntoView(LiveFieldValue field)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var grid = this.FindControl<DataGrid>("FieldGrid");
            grid?.ScrollIntoView(field, null);
        }, DispatcherPriority.Background);
    }

    // Bookmark save: report the topmost visible field row so loading can scroll
    // back to it. The DataGrid has no public pixel-offset API, so we anchor on a
    // row. Synchronous — the VM reads the carrier right after raising the event.
    private void OnCaptureViewAnchor(ViewAnchorRef anchor)
    {
        var grid = this.FindControl<DataGrid>("FieldGrid");
        if (grid == null) return;

        // Topmost realised data row whose top edge sits at/below the grid's top
        // (rows scrolled above the viewport translate to a negative Y).
        LiveFieldValue? topField = null;
        double bestY = double.MaxValue;
        foreach (var row in grid.GetVisualDescendants().OfType<DataGridRow>())
        {
            if (row.DataContext is not LiveFieldValue f) continue;
            var pt = row.TranslatePoint(new Point(0, 0), grid);
            if (pt is { } p && p.Y >= -1 && p.Y < bestY) { bestY = p.Y; topField = f; }
        }
        if (topField != null)
            anchor.TopRow = new BookmarkFieldRef(topField.Name, topField.Offset);
    }

    // Bookmark load: re-select the saved rows (one or many) and scroll the saved
    // anchor row back into view, so the bookmark returns to what was on screen.
    private void OnRestoreBookmarkView(IReadOnlyList<BookmarkFieldRef> selected, BookmarkFieldRef? topRow)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var grid = this.FindControl<DataGrid>("FieldGrid");
            if (grid?.ItemsSource == null) return;
            var rows = grid.ItemsSource.Cast<LiveFieldValue>().ToList();

            // Prefer an exact name+offset match; fall back to name only (container
            // element rows can share offset 0 across elements).
            static LiveFieldValue? Match(List<LiveFieldValue> rs, BookmarkFieldRef r)
                => rs.FirstOrDefault(f => f.Name == r.Name && f.Offset == r.Offset)
                   ?? rs.FirstOrDefault(f => f.Name == r.Name);

            if (selected.Count > 0)
            {
                try
                {
                    grid.SelectedItems.Clear();
                    foreach (var sel in selected)
                    {
                        var hit = Match(rows, sel);
                        if (hit != null && !grid.SelectedItems.Contains(hit))
                            grid.SelectedItems.Add(hit);
                    }
                }
                catch (System.NotSupportedException)
                {
                    // Defensive: if this Avalonia build's SelectedItems list is
                    // read-only, fall back to restoring the single anchor row.
                    grid.SelectedItem = Match(rows, selected[0]);
                }
            }

            // Restore the view position after selection-driven layout settles.
            // Anchor on the saved top row, falling back to the first selected row.
            Dispatcher.UIThread.Post(() =>
            {
                var anchor = topRow != null ? Match(rows, topRow) : null;
                if (anchor == null && selected.Count > 0)
                    anchor = Match(rows, selected[0]);
                if (anchor != null)
                    grid.ScrollIntoView(anchor, null);
            }, DispatcherPriority.Background);
        }, DispatcherPriority.Background);
    }

    private void OnScrollToFunctionRequested(string functionName)
    {
        // Cross-tab nav from Interesting Funcs lands here. The grid lives
        // inside an Expander that is collapsed by default — when it has
        // never been expanded, ItemsSource has no realised rows yet, so
        // post twice (background then loaded) to give layout a chance to
        // populate the row presenters before scrolling.
        Dispatcher.UIThread.Post(() =>
        {
            var grid = this.FindControl<DataGrid>("FunctionGrid");
            if (grid?.ItemsSource == null) return;

            var target = grid.ItemsSource.Cast<FunctionInfoModel>()
                .FirstOrDefault(f => f.Name == functionName);
            if (target != null)
            {
                grid.SelectedItem = target;
                grid.ScrollIntoView(target, null);
            }
        }, DispatcherPriority.Background);
    }

    private void AddressInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is LiveWalkerViewModel vm
            && sender is TextBox tb)
        {
            if (vm.NavigateToAddressCommand.CanExecute(tb.Text))
            {
                vm.NavigateToAddressCommand.Execute(tb.Text);
                e.Handled = true;
            }
        }
    }

    private static readonly IBrush GuessedForeground = new SolidColorBrush(Color.Parse("#666666"));
    private static readonly IBrush NormalForeground = new SolidColorBrush(Color.Parse("#D4D4D4"));

    // Sync the DataGrid's multi-selection into the ViewModel snapshot.
    // Avalonia's DataGrid.SelectedItems is read-only, so we push it into
    // the VM here whenever the selection changes. The Copy CE Field(s)
    // export command reads this snapshot when invoked.
    private void FieldGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid && DataContext is LiveWalkerViewModel vm)
        {
            vm.UpdateSelectedFields(grid.SelectedItems);
        }
    }

    // FieldGrid_LoadingRow was HERE and is deliberately gone. [LWREFRESH-2026-08-21]
    //
    // It painted Row.Background/Foreground on realization, which cannot react to a property change
    // on a row that is reused rather than replaced — and reusing rows is what stops the grid
    // drifting a row upward on every Refresh. The tint now comes from a bound DataGridRow style in
    // LiveWalkerPanel.axaml.
    //
    // ⚠ Do not reinstate it "as a safety net": `e.Row.Background = …` writes at LocalValue
    // priority, which outranks a Style setter, so its Transparent branch alone would pin every
    // non-matching row and silently kill the binding.

    private void FieldGrid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        // Cancel edit for non-editable fields (pointers, structs, containers, strings, etc.)
        if (e.Row.DataContext is LiveFieldValue field && !field.IsEditable)
        {
            e.Cancel = true;
            return;
        }

        // Suppress auto-refresh while editing
        if (DataContext is LiveWalkerViewModel vm)
            vm.IsEditing = true;
    }

    private async void FieldGrid_CellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (DataContext is not LiveWalkerViewModel vm) return;

        try
        {
            // Only commit on user confirmation (Enter / focus loss), not on cancel (Escape)
            if (e.EditAction == DataGridEditAction.Cancel) return;

            if (e.Row.DataContext is LiveFieldValue field && field.IsEditable)
            {
                var newValue = field.GetPendingEditValue();
                if (!string.IsNullOrEmpty(newValue))
                {
                    // Save field identity for re-selection after refresh
                    var editedName = field.Name;
                    var editedOffset = field.Offset;

                    await vm.CommitFieldEditAsync(field, newValue);

                    // Scroll to keep the edited row visible after refresh
                    ScrollToField(editedName, editedOffset);
                }
            }
        }
        finally
        {
            vm.IsEditing = false;
        }
    }

    /// <summary>
    /// Commit (Enter) or cancel (Escape) the in-place edit without advancing.
    /// Avalonia's DataGrid default treats Enter as "commit + move selection to the
    /// next row (re-entering edit mode)", so each Enter walked the editor down the
    /// grid. Committing ourselves and marking the event Handled stops the event from
    /// bubbling to the DataGrid, so the editor closes on the field the user edited.
    /// </summary>
    private void EditBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Escape) return;

        var grid = this.FindControl<DataGrid>("FieldGrid");
        if (grid == null) return;

        if (e.Key == Key.Enter)
            grid.CommitEdit();
        else
            grid.CancelEdit();
        e.Handled = true;
    }

    /// <summary>Auto-open the dropdown when a BoolProperty enters edit mode.</summary>
    private void BoolCombo_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is ComboBox cb)
        {
            // Delay to ensure layout is complete before opening dropdown
            Dispatcher.UIThread.Post(() => cb.IsDropDownOpen = true,
                DispatcherPriority.Background);
        }
    }

    /// <summary>Auto-commit the edit when user selects a value from the bool dropdown.</summary>
    private void BoolCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Only handle actual user changes (not initial binding load)
        if (e.RemovedItems.Count == 0) return;
        if (DataContext is not LiveWalkerViewModel vm || !vm.IsEditing) return;

        // Post CommitEdit to let the binding update _editableValue first
        Dispatcher.UIThread.Post(() =>
        {
            var grid = this.FindControl<DataGrid>("FieldGrid");
            grid?.CommitEdit();
        }, DispatcherPriority.Background);
    }

    /// <summary>Scroll the DataGrid to show the field with the given name and offset.</summary>
    private void ScrollToField(string fieldName, int fieldOffset)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var grid = this.FindControl<DataGrid>("FieldGrid");
            if (grid?.ItemsSource == null) return;

            var restored = grid.ItemsSource.Cast<LiveFieldValue>()
                .FirstOrDefault(f => f.Name == fieldName && f.Offset == fieldOffset);
            if (restored != null)
            {
                grid.SelectedItem = restored;
                grid.ScrollIntoView(restored, null);
            }
        }, DispatcherPriority.Background);
    }
}
