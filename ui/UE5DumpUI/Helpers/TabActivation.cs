namespace UE5DumpUI.Helpers;

/// <summary>
/// Whether a <c>SelectionChanged</c> that reached the main TabControl actually
/// represents a TAB SWITCH (audit #5 AF5).
///
/// <para><b>Avalonia's SelectionChanged is a bubbling routed event.</b> A
/// ComboBox or ListBox inside the selected tab raises it, and it travels up the
/// visual tree to the TabControl, which invokes the tab-activation handler with
/// <c>sender == TabControl</c> and <c>tabs.SelectedItem.Tag</c> still naming the
/// current tab. The handler then re-runs its whole per-tab activation routine —
/// cancelling in-flight work and rebuilding lists — in response to the user
/// merely picking an item inside the tab they are already on.</para>
///
/// <para>Measured against the pinned Avalonia version in a headless harness:
/// the TabControl <i>is</i> a visual ancestor of the tab's content (chain
/// StackPanel → ContentPresenter → Panel → DockPanel → Border → TabControl);
/// <b>ComboBox and ListBox</b> re-fire the handler this way, while
/// <b>DataGrid and AutoCompleteBox</b> do not. A genuine tab switch arrives with
/// <c>e.Source</c> being the TabControl itself.</para>
///
/// <para>So the discriminator is <c>e.Source</c>, not <c>sender</c> — sender is
/// the TabControl in every case, which is exactly why the old code could not
/// tell the two apart. <c>e.Handled</c> is not the answer either: the TabControl
/// is the end of the route, so setting it suppresses nothing.</para>
///
/// <para>Kept as a pure static, parameters typed <c>object?</c>, so it is
/// unit-testable without spinning up a toolkit — the same shape as
/// <see cref="LiveWalkerNavShortcuts"/>.</para>
/// </summary>
public static class TabActivation
{
    /// <summary>
    /// True only when the event originated at the TabControl itself, i.e. the
    /// selected TAB changed. False for anything bubbled up from a child
    /// selector, and false when the source is unknown.
    /// </summary>
    /// <param name="eventSource">
    /// <c>SelectionChangedEventArgs.Source</c> — the control that raised it.
    /// </param>
    /// <param name="tabControl">The TabControl the handler is attached to.</param>
    public static bool ShouldRunActivation(object? eventSource, object? tabControl)
    {
        // A null source cannot be shown to be the TabControl, so it must not
        // activate: guessing "probably a tab switch" is what this exists to stop.
        // ReferenceEquals also keeps a control with a custom Equals from
        // impersonating the TabControl.
        if (eventSource is null || tabControl is null) return false;
        return ReferenceEquals(eventSource, tabControl);
    }
}
