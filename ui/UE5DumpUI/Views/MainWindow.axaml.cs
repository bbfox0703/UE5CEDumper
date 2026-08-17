using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using UE5DumpUI.Helpers;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class MainWindow : Window
{
    /// <summary>
    /// Position + size of the window the last time it was in
    /// <see cref="WindowState.Normal"/>. When the OS reports a
    /// transition Normal → Maximized → Normal, Avalonia 12 on Windows
    /// can land the restored window stretched across multiple monitors
    /// (especially common on dual-monitor setups) or at the wrong
    /// position. Snapshotting before the maximize and re-applying on
    /// restore puts the window back on the monitor it started from at
    /// its original size.
    /// </summary>
    private PixelPoint? _normalPosition;
    private double _normalWidth;
    private double _normalHeight;
    private WindowState _previousWindowState = WindowState.Normal;

    // Last "non-minimized" state. _previousWindowState is overwritten on every
    // transition, so after Maximized → Minimized it is already Minimized and
    // can't tell us the window was minimized FROM a maximized state. Track that
    // separately here so OnClosingSaveState can persist Maximized=true when the
    // window is closed while minimized-from-maximized.
    private WindowState _lastNonMinimizedState = WindowState.Normal;

    // Deferred-commit snapshot state. On Windows + Avalonia 12 the
    // property-change order during a maximize transition is
    // (Width/Height first, WindowState second), so reading
    // WindowState inside the Width/Height handler still sees Normal
    // when the values are already the maximized dimensions. Capturing
    // synchronously into _normalWidth/_normalHeight at that point
    // poisons the snapshot — the next restore then lands at
    // near-maximized size. Defer the commit one dispatcher tick at
    // Background priority so any concurrent WindowState change in the
    // same Win32 message has been propagated; the commit re-checks
    // WindowState and abandons when it has flipped to non-Normal.
    private double _pendingWidth;
    private double _pendingHeight;
    private PixelPoint _pendingPosition;
    private bool _snapshotCommitScheduled;

    // Set once the window has closed so a deferred (Background-posted) restore re-apply
    // that fires afterwards can't set Position/Width/Height on a torn-down window.
    private bool _closed;

    // ── Cross-restart placement persistence (AttachWindowState) ──────────────
    // Restores last-session position / size / maximized state, validated
    // against the monitors present THIS session (a window saved on a now-absent
    // second monitor is reset to a centered default). Reuses the normal-vs-
    // maximized snapshot above so un-maximizing a restored window lands right.
    private Services.WindowStateStore? _stateStore;
    private bool _restorePending;     // a saved record was applied; validate on Opened
    private double _defaultWidth;     // XAML default, used when resetting off-screen
    private double _defaultHeight;

    // ── Object Tree collapse ────────────────────────────────────────────────
    // When ObjectTree.IsCollapsed flips, resize the left grid column to a thin
    // strip (and hide the splitter) so the right panels get full width. The
    // pre-collapse Width / MinWidth are saved so re-expand restores whatever
    // the user had dragged the splitter to. View state only — not persisted.
    private ObjectTreeViewModel? _subscribedObjectTree;
    private GridLength _savedTreeWidth = new(Constants.TreePanelWidth);
    private double _savedTreeMinWidth = 200;
    private const double CollapsedTreeStripWidth = 26;

    public MainWindow()
    {
        InitializeComponent();
        // Audit fixes #16 / #17: dispose timer-owning child VMs when the
        // window closes, so background DispatcherTimers and Threading.Timer
        // callbacks don't fire post-close on torn-down state.
        Closed += OnClosed;
        // Track the ObjectTree sub-VM so the Object Tree collapse toggle can
        // resize the left grid column from code-behind (Width/MinWidth can't be
        // both bound and splitter-dragged cleanly).
        DataContextChanged += OnMainDataContextChanged;
        // Position isn't an AvaloniaProperty in 12 — listen via the
        // explicit event instead. Width / Height are AvaloniaProperty
        // and flow through OnPropertyChanged.
        PositionChanged += OnPositionChanged;
        // Close the IME whenever focus enters a text field, app-wide. Every
        // input field here takes ASCII (hex addresses, AOB patterns, numbers,
        // class / property names, file paths), so a CJK IME left in composition
        // mode only gets in the way. A single tunnel-to-bubble handler at the
        // window root (handledEventsToo so it still fires when a child marks the
        // event handled) beats wiring 48+ TextBox / AutoCompleteBox / NumericUpDown
        // controls individually.
        AddHandler(InputElement.GotFocusEvent, OnGlobalGotFocus,
            RoutingStrategies.Bubble, handledEventsToo: true);
        // Browser-style Live Walker history: Alt+Left / Alt+Right, the mouse's 4th /
        // 5th buttons, and the dedicated BrowserBack / BrowserForward keys.
        // At the window root and TUNNELLING, for two reasons: the field DataGrid
        // claims Left/Right for cell navigation and would eat the arrows on the way
        // up, and when focus sits on the tab header the Live Walker panel isn't even
        // on the event route — a panel-level handler would simply never fire.
        // Both gates (foreground window + Live Walker is the current tab) live in
        // LiveWalkerNavShortcuts so they stay testable.
        AddHandler(KeyDownEvent, OnNavShortcutKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnNavShortcutPointerPressed, RoutingStrategies.Tunnel);
        // Seed the snapshot with the XAML-declared default so the very
        // first restore (without a prior maximize) still has something
        // sane to fall back to.
        _normalWidth = Width;
        _normalHeight = Height;
        _pendingWidth = Width;
        _pendingHeight = Height;
        _pendingPosition = Position;

        // Remember the XAML default size (1400x900) as the reset fallback for
        // when a restored placement turns out to be off every current monitor.
        _defaultWidth = Width;
        _defaultHeight = Height;
    }

    /// <summary>
    /// Wire up cross-restart window placement. Called once by App right after
    /// construction and BEFORE the window is shown, so saved geometry is applied
    /// without a visible jump. Validation against the monitors present THIS
    /// session happens on Opened (Screens is only reliable once the platform
    /// window exists).
    /// </summary>
    public void AttachWindowState(Services.WindowStateStore store)
    {
        _stateStore = store;
        Closing += OnClosingSaveState;

        if (store.Load() is not { } r)
        {
            return; // first run / corrupt → keep XAML defaults + OS placement
        }

        // Clamp restored size to at least the window minimum so a tiny saved
        // size can't make the window unusable.
        double w = Math.Max(r.Width, MinWidth);
        double h = Math.Max(r.Height, MinHeight);
        var pos = new PixelPoint(r.X, r.Y);

        // Take placement over from the OS and seed BOTH the live geometry and
        // the normal-vs-maximized snapshot, so a later un-maximize restores to
        // exactly this rect.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = pos;
        Width = w;
        Height = h;
        _normalPosition = pos;
        _normalWidth = w;
        _normalHeight = h;
        _pendingPosition = pos;
        _pendingWidth = w;
        _pendingHeight = h;

        _restorePending = true;
        if (r.Maximized)
        {
            // Show maximized immediately; the snapshot above is the restore-down
            // target. Avalonia doesn't reliably keep the normal rect across a
            // maximize, and a born-maximized window has no valid OS restore
            // rectangle — so the first un-maximize is corrected by the deferred
            // ReapplyNormalSnapshot, which consumes this seeded snapshot.
            WindowState = WindowState.Maximized;
        }

        Opened += OnOpenedValidatePlacement;
    }

    /// <summary>
    /// Once the platform window exists (Screens reliable), confirm the restored
    /// NORMAL rect is reachable on some current monitor. If not — the monitor was
    /// removed or the resolution shrank — reset to a default size centered on the
    /// primary screen. Runs once.
    /// </summary>
    private void OnOpenedValidatePlacement(object? sender, EventArgs e)
    {
        Opened -= OnOpenedValidatePlacement;
        if (!_restorePending) return;
        _restorePending = false;

        var screens = CurrentScreenWorkingAreas();
        if (screens.Count == 0) return; // no screen info — leave as restored

        // RenderScaling reflects the monitor the window is CURRENTLY on; it is
        // fine for the visibility tolerance check (the restored rect is on that
        // monitor), but must NOT be used to convert the default size for primary-
        // screen centering — see ResetToDefaultPlacement / PrimaryScaling().
        double scale = RenderScaling > 0 ? RenderScaling : 1.0;
        int rx = _normalPosition?.X ?? 0;
        int ry = _normalPosition?.Y ?? 0;
        int rw = (int)Math.Round(_normalWidth * scale);
        int rh = (int)Math.Round(_normalHeight * scale);

        if (Services.WindowPlacement.IsVisibleEnough(rx, ry, rw, rh, screens))
            return; // restored placement is reachable — keep it

        ResetToDefaultPlacement();
    }

    /// <summary>Drop to a default-size window centered on the primary monitor.</summary>
    private void ResetToDefaultPlacement()
    {
        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;

        Width = _defaultWidth;
        Height = _defaultHeight;

        // Convert the default DIP size to physical px using the PRIMARY screen's
        // own scaling — NOT the window's current RenderScaling. On a mixed-DPI
        // multi-monitor setup the two differ, and using the current monitor's
        // scale offsets the window from center (can push the title bar off the
        // working area).
        var primary = PrimaryWorkingArea();
        double primaryScale = PrimaryScaling();
        int pw = (int)Math.Round(_defaultWidth * primaryScale);
        int ph = (int)Math.Round(_defaultHeight * primaryScale);
        var (cx, cy) = Services.WindowPlacement.CenterIn(primary, pw, ph);
        var pos = new PixelPoint(cx, cy);
        Position = pos;

        _normalPosition = pos;
        _normalWidth = _defaultWidth;
        _normalHeight = _defaultHeight;
        _pendingPosition = pos;
        _pendingWidth = _defaultWidth;
        _pendingHeight = _defaultHeight;
    }

    /// <summary>Persist the current placement on close (geometry still valid).</summary>
    private void OnClosingSaveState(object? sender, WindowClosingEventArgs e)
    {
        if (_stateStore is null) return;

        bool maximized = WindowState == WindowState.Maximized
            || (WindowState == WindowState.Minimized && _lastNonMinimizedState == WindowState.Maximized);

        int x, y;
        double w, h;
        if (WindowState == WindowState.Normal)
        {
            // Live geometry is authoritative when Normal.
            x = Position.X;
            y = Position.Y;
            w = Width;
            h = Height;
        }
        else
        {
            // Maximized / minimized report sentinel geometry — use the tracked
            // normal snapshot so we persist the real restore-down rect.
            var p = _normalPosition ?? Position;
            x = p.X;
            y = p.Y;
            w = _normalWidth;
            h = _normalHeight;
        }

        _stateStore.Save(new Services.WindowStateRecord(x, y, w, h, maximized));
    }

    /// <summary>
    /// True when (<paramref name="pos"/>, the current normal size) shows a grabbable chunk
    /// on some current monitor — the position twin of <see cref="CommitSnapshot"/>'s size
    /// &gt;0 guard. Stops a transition-artifact / off-screen top-left from latching into the
    /// snapshot (and being re-applied on the next restore — the "jumps to 0,0" class). No
    /// screen info yet (pre-show) => accept, so the very first open-time stash isn't dropped.
    /// </summary>
    private bool IsSnapshotPositionAcceptable(PixelPoint pos)
    {
        var screens = CurrentScreenWorkingAreas();
        if (screens.Count == 0) return true;
        return Services.WindowPlacement.IsVisibleEnough(
            pos.X, pos.Y,
            (int)Math.Round(_pendingWidth), (int)Math.Round(_pendingHeight),
            screens);
    }

    /// <summary>Current monitors' working areas as plain physical-pixel rects.</summary>
    private List<(int X, int Y, int W, int H)> CurrentScreenWorkingAreas()
    {
        var list = new List<(int, int, int, int)>();
        var all = Screens?.All;
        if (all == null) return list;
        foreach (var s in all)
        {
            var wa = s.WorkingArea;
            list.Add((wa.X, wa.Y, wa.Width, wa.Height));
        }
        return list;
    }

    /// <summary>Primary monitor working area (fallback: first screen, then 1080p).</summary>
    private (int X, int Y, int W, int H) PrimaryWorkingArea()
    {
        var primary = Screens?.Primary;
        if (primary == null)
        {
            var all = Screens?.All;
            if (all != null && all.Count > 0) primary = all[0];
        }
        if (primary != null)
        {
            var wa = primary.WorkingArea;
            return (wa.X, wa.Y, wa.Width, wa.Height);
        }
        return (0, 0, 1920, 1080);
    }

    /// <summary>
    /// Primary monitor's scaling (fallback: first screen, then 1.0). Used for the
    /// DIP→physical-px conversion when centering on the primary screen, so a
    /// mixed-DPI second monitor's RenderScaling can't skew the centered result.
    /// </summary>
    private double PrimaryScaling()
    {
        var primary = Screens?.Primary;
        if (primary == null)
        {
            var all = Screens?.All;
            if (all != null && all.Count > 0) primary = all[0];
        }
        double s = primary?.Scaling ?? 1.0;
        return s > 0 ? s : 1.0;
    }

    /// <summary>
    /// App-wide focus-in IME guard. Closes the IME (switches to direct
    /// alphanumeric input) when focus lands on any text-input control.
    /// </summary>
    private void OnGlobalGotFocus(object? sender, FocusChangedEventArgs e)
    {
        // AutoCompleteBox and NumericUpDown route keyboard focus to an inner
        // TextBox, so a single TextBox check covers all three field kinds
        // without per-control wiring. Non-text controls (buttons, grid cells)
        // are left alone.
        if (e.NewFocusedElement is not TextBox)
        {
            return;
        }
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }
        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }
        // Defer one dispatcher tick: Avalonia's Win32 backend associates the
        // IME context as part of its own focus handling for the same input
        // event. Closing the IME after that association has settled avoids it
        // being re-opened underneath us.
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => vm.Platform.CloseImeForWindow(hwnd),
            Avalonia.Threading.DispatcherPriority.Background);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // Block any still-queued Background restore re-apply from touching the torn-down
        // window (it reads this flag first).
        _closed = true;
        // Stop any in-flight heavy experimental query so the process can exit
        // promptly and release the single-instance mutex — otherwise a runaway
        // uncancellable scan kept the old host alive and blocked re-launch.
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Spc?.CancelPendingWork();
            vm.Snapshot?.CancelPendingWork();
            // Auto snapshot survives tab-switch (CancelPendingWork doesn't touch it),
            // so stop it explicitly on window close.
            vm.Snapshot?.StopAutoSnapshot();
            vm.Pivot?.CancelPendingWork();
        }
        if (DataContext is IDisposable d) d.Dispose();
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        // Reject an off-screen / maximized-origin top-left so a transition transient can't
        // poison the snapshot (the position twin of the size >0 guard in CommitSnapshot).
        if (WindowState == WindowState.Normal && IsSnapshotPositionAcceptable(Position))
        {
            _pendingPosition = Position;
            ScheduleSnapshotCommit();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            var newState = change.GetNewValue<WindowState>();
            HandleWindowStateTransition(_previousWindowState, newState);
            _previousWindowState = newState;
            if (newState != WindowState.Minimized)
                _lastNonMinimizedState = newState;
        }
        else if (change.Property == WidthProperty || change.Property == HeightProperty)
        {
            // Stash, but commit later (see the field comments). Reading
            // WindowState here is unreliable on the to-maximized
            // transition.
            if (WindowState == WindowState.Normal)
            {
                _pendingWidth = Width;
                _pendingHeight = Height;
                ScheduleSnapshotCommit();
            }
        }
    }

    /// <summary>
    /// Queue a deferred snapshot commit on the dispatcher at
    /// Background priority. Coalesced: scheduling while a commit is
    /// already pending is a no-op.
    /// </summary>
    private void ScheduleSnapshotCommit()
    {
        if (_snapshotCommitScheduled)
        {
            return;
        }
        _snapshotCommitScheduled = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            CommitSnapshot,
            Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// Apply the pending snapshot — but only if the window is still in
    /// <see cref="WindowState.Normal"/> at the time of the commit. If
    /// a WindowState change snuck in during the same dispatcher tick
    /// (the bug we're guarding against), the state will have flipped
    /// by now and the pending values are the maximized dimensions
    /// we don't want.
    /// </summary>
    private void CommitSnapshot()
    {
        _snapshotCommitScheduled = false;
        if (WindowState != WindowState.Normal)
        {
            return;
        }
        if (_pendingWidth > 0)
        {
            _normalWidth = _pendingWidth;
        }
        if (_pendingHeight > 0)
        {
            _normalHeight = _pendingHeight;
        }
        // Position promotion is gated like the stash (was unconditional): an off-screen /
        // maximized-origin top-left must never latch into _normalPosition, or the next
        // restore re-applies it (the "second restore jumps to 0,0" bug).
        if (IsSnapshotPositionAcceptable(_pendingPosition))
        {
            _normalPosition = _pendingPosition;
        }
    }

    private void HandleWindowStateTransition(WindowState oldState, WindowState newState)
    {
        // Leaving Normal: snapshot was already kept fresh by the
        // property-change branch above. Nothing to do here.
        if (oldState == WindowState.Normal && newState != WindowState.Normal)
        {
            return;
        }
        // Returning to Normal: re-apply the snapshot. Without this,
        // restoring from Maximized on a multi-monitor setup can land
        // the window straddling both screens or at the wrong position.
        //
        // DEFER the re-apply to a Background dispatcher tick rather than setting
        // Position/Width/Height synchronously inside the WindowState change. The
        // synchronous set fought the OS mid-un-maximize: it (a) emitted a maximized-origin
        // (~0,0) position transient that latched into the snapshot and re-surfaced as the
        // window jumping to 0,0 on the SECOND restore, and (b) lost the placement race
        // against a window BORN maximized (cross-restart), whose OS restore rectangle is a
        // garbage default — leaving the window off-screen. Background priority runs FIFO
        // after the current Win32 message burst, so the re-apply lands AFTER any OS
        // placement and OVERRIDES it; re-seeding _pending* stops the events it triggers
        // from being read as a fresh user move. The position acceptability guard
        // (IsSnapshotPositionAcceptable) backstops the snapshot should any off-screen
        // transient still be stashed.
        if (newState == WindowState.Normal && oldState != WindowState.Normal)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(
                ReapplyNormalSnapshot,
                Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Force the live window back onto the saved normal snapshot. Posted at Background
    /// priority from the Maximized/Minimized → Normal transition. Gated so it never runs
    /// during startup placement restore (<see cref="_restorePending"/> — that path is the
    /// sole authority until validated) nor on a closed window.
    /// </summary>
    private void ReapplyNormalSnapshot()
    {
        if (_closed || _restorePending) return;
        if (WindowState != WindowState.Normal) return;
        // Size before position so final DPI scaling resolves against the target monitor.
        if (_normalWidth > 0)
        {
            Width = _normalWidth;
        }
        if (_normalHeight > 0)
        {
            Height = _normalHeight;
        }
        if (_normalPosition is { } pos)
        {
            Position = pos;
        }
        // Re-seed the stash so the Position/Size events this re-apply triggers aren't
        // mis-read as a user move (which would thrash, or re-poison, the snapshot).
        _pendingPosition = _normalPosition ?? _pendingPosition;
        _pendingWidth = _normalWidth;
        _pendingHeight = _normalHeight;
    }

    /// <summary>
    /// Per-tab activation work:
    /// 1. Stop Live Walker auto-refresh when the user switches away
    ///    from Live Walker (no point polling while viewing other tabs).
    /// 2. Refresh AOBMaker availability for tabs whose actions depend
    ///    on the CE plugin (LiveWalker, InterestingFunctions, Pointers).
    ///    The re-check is fire-and-forget (cooldown-throttled where the
    ///    panel VM supports it) so rapid tab switches don't stack 2s
    ///    pipe-connect timeouts -- keeps the grayout state honest when
    ///    the user starts/stops CE without blocking the UI thread.
    /// </summary>
    /// <remarks>
    /// Routing is keyed on the selected <see cref="TabItem.Tag"/>, NOT its
    /// position. Index-based routing here has silently drifted twice as tabs
    /// were inserted into MainWindow.axaml (the Pointers re-check pointed at
    /// the wrong index, and the autorefresh-stop assumed LiveWalker == 0).
    /// The Tag travels with its TabItem, so reordering can't break this.
    /// SelectedItem is the outer TabControl's selection, so inner
    /// SelectionChanged events bubbling from child grids are harmless.
    /// </remarks>
    /// <summary>
    /// (Re)subscribe to the ObjectTree sub-VM's PropertyChanged so collapse
    /// toggles resize the left grid column. Resubscribes if the DataContext is
    /// swapped (it normally isn't), and syncs the column to the current state.
    /// </summary>
    private void OnMainDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedObjectTree != null)
        {
            _subscribedObjectTree.PropertyChanged -= OnObjectTreePropertyChanged;
            _subscribedObjectTree = null;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            _subscribedObjectTree = vm.ObjectTree;
            _subscribedObjectTree.PropertyChanged += OnObjectTreePropertyChanged;
            ApplyObjectTreeCollapse(_subscribedObjectTree.IsCollapsed);
        }
    }

    private void OnObjectTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ObjectTreeViewModel.IsCollapsed)
            && sender is ObjectTreeViewModel ot)
        {
            ApplyObjectTreeCollapse(ot.IsCollapsed);
        }
    }

    /// <summary>
    /// Collapse the Object Tree column to a thin strip (or restore it). The
    /// pre-collapse Width + MinWidth are saved so re-expand returns to whatever
    /// the user dragged the splitter to.
    /// </summary>
    private void ApplyObjectTreeCollapse(bool collapsed)
    {
        // ColumnDefinition x:Name doesn't generate a field in Avalonia, so reach
        // the two columns through the named Grid.
        if (ContentGrid is null || ContentGrid.ColumnDefinitions.Count < 2 || TreeSplitter is null) return;
        var treeColumn = ContentGrid.ColumnDefinitions[0];
        var splitterColumn = ContentGrid.ColumnDefinitions[1];

        if (collapsed)
        {
            // Don't overwrite the saved width if we're already collapsed.
            if (treeColumn.Width.Value > CollapsedTreeStripWidth || treeColumn.MinWidth > 0)
            {
                _savedTreeWidth = treeColumn.Width;
                _savedTreeMinWidth = treeColumn.MinWidth;
            }
            treeColumn.MinWidth = 0;
            treeColumn.Width = new GridLength(CollapsedTreeStripWidth);
            splitterColumn.Width = new GridLength(0);
            TreeSplitter.IsVisible = false;
        }
        else
        {
            treeColumn.MinWidth = _savedTreeMinWidth;
            // Fall back to the XAML default if the saved width is degenerate.
            treeColumn.Width = _savedTreeWidth.IsAbsolute && _savedTreeWidth.Value > CollapsedTreeStripWidth
                ? _savedTreeWidth
                : new GridLength(Constants.TreePanelWidth);
            splitterColumn.Width = new GridLength(4);
            TreeSplitter.IsVisible = true;
        }
    }

    // ── Browser-style Live Walker history shortcuts ─────────────────────────
    // Wired as tunnelling window-root handlers in the ctor. The decision of what a
    // given key / mouse button means lives in LiveWalkerNavShortcuts (pure, tested);
    // everything here is plumbing.

    /// <summary>True when the Live Walker tab is the one on screen. Matched by
    /// <c>TabItem.Tag</c>, never by index — MainTabIndex documents how those indices
    /// silently drifted when tabs were inserted.</summary>
    private bool IsLiveWalkerTabActive()
        => (MainTabs.SelectedItem as TabItem)?.Tag as string == "LiveWalker";

    private void OnNavShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (RunNavShortcut(LiveWalkerNavShortcuts.Resolve(
                e.Key, e.KeyModifiers, IsLiveWalkerTabActive(), IsActive)))
            e.Handled = true;
    }

    private void OnNavShortcutPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (RunNavShortcut(LiveWalkerNavShortcuts.Resolve(
                kind, IsLiveWalkerTabActive(), IsActive)))
            e.Handled = true;
    }

    /// <summary>Run the resolved navigation. Returns true when the input belonged to
    /// us and must not travel on — including when we deliberately swallowed it.</summary>
    private bool RunNavShortcut(NavShortcut action)
    {
        if (action == NavShortcut.None) return false;
        if (DataContext is not MainWindowViewModel vm) return false;
        var walker = vm.LiveWalker;

        // Auto-repeat guard. Avalonia 12 doesn't surface IsRepeat on KeyEventArgs, so
        // a HELD Alt+Left would otherwise fire ~30x/second and queue that many pipe
        // round-trips behind each other. One walk at a time — but still report the
        // input as handled, so the swallowed repeats never reach the DataGrid either.
        if (walker.IsLoading) return true;

        var command = action == NavShortcut.Back ? walker.GoBackCommand : walker.GoForwardCommand;
        if (command.CanExecute(null)) command.Execute(null);
        return true;
    }

    private void MainTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl tabs) return;
        if (DataContext is not MainWindowViewModel vm) return;

        // SelectionChanged BUBBLES. A ComboBox or ListBox inside the current tab
        // raises it and it arrives here with sender == the TabControl and the Tag
        // still naming the tab the user is already on — so this whole routine used
        // to re-run on an ordinary in-tab pick, cancelling in-flight work and
        // rebuilding lists underneath the user (audit #5 AF5).
        if (!TabActivation.ShouldRunActivation(e.Source, tabs)) return;

        var tag = (tabs.SelectedItem as TabItem)?.Tag as string;

        // Stop Live Walker auto-refresh when switching away from it.
        if (tag != "LiveWalker" && vm.LiveWalker.IsAutoRefreshing)
        {
            vm.LiveWalker.StopAutoRefreshTimer();
        }
        // NOTE: the field-search keyword is intentionally NOT cleared on tab
        // switch — it's kept. The Live Walker clears it itself when the grid
        // navigates to different data (drill-down / Back / Parent), where a
        // leftover keyword no longer applies. See UpdateDisplay(clearFieldSearch).

        // Cancel any experimental tab's in-flight HEAVY query when navigating
        // away from it. Leaving a multi-million-row SPC / diff / pivot running on
        // a thread-pool thread while another tab loads pegs a core + allocates
        // GBs — the tab-switch UI hang the user reported. Each VM's
        // CancelPendingWork only cancels its heavy in-memory op (capture, which
        // yields between chunks, is deliberately left alone).
        if (tag != "SpcQuery")   vm.Spc?.CancelPendingWork();
        if (tag != "Snapshot")   vm.Snapshot?.CancelPendingWork();
        if (tag != "ClassPivot") vm.Pivot?.CancelPendingWork();

        // Leaving Live Funcs auto-stops any live PE recording (so a forgotten
        // session doesn't keep the game thread taking the profile mutex per PE
        // call) and flushes its filter-keyword memory.
        if (tag != "LiveFuncs")  vm.LiveFuncs?.OnLeavingTab();

        // Same discipline for the System tab's Diagnostics auto-refresh: a forgotten
        // toggle would keep adding pipe traffic while the user works elsewhere — and
        // since every poll is itself a dispatch, it would skew the very numbers the
        // card reports. The checkbox stays ticked, so returning resumes the poll.
        if (tag != "Pointers") vm.Pointers.OnLeavingTab();
        else                   vm.Pointers.OnEnteringTab();

        // Opening any experimental tab while enabled permanently commits the
        // opt-in: the System-tab checkbox can no longer be unticked from here
        // on. The gate persists the lock; LockExperimental is idempotent and a
        // no-op when the feature isn't enabled.
        if (tag is "Snapshot" or "SpcQuery" or "ClassPivot")
            vm.LockExperimental();

        // Refresh AOBMaker state for tabs whose toolbar / per-row buttons
        // depend on it. LiveWalker / InterestingFunctions throttle via
        // TryCheckAobMaker; PointerPanel has no cooldown wrapper, so call
        // its async check directly (fire-and-forget, as before).
        switch (tag)
        {
            case "LiveWalker": vm.LiveWalker.TryCheckAobMaker(); break;
            case "InterestingFunctions": vm.InterestingFunctions.TryCheckAobMaker(); break;
            case "Pointers": _ = vm.Pointers.CheckAobMakerAsync(); break;
            case "Teleport": _ = vm.Teleport.CheckAobMakerAsync(); break;
            // SPC / Pivot read the snapshot list saved by the Snapshot tab —
            // refresh on activation so a just-captured snapshot shows up.
            case "SpcQuery": _ = vm.Spc?.RefreshCommand.ExecuteAsync(null); break;
            case "ClassPivot": _ = vm.Pivot?.RefreshCommand.ExecuteAsync(null); break;
        }
    }
}
