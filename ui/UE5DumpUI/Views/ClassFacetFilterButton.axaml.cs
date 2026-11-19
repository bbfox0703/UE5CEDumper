using Avalonia;
using Avalonia.Controls;
using UE5DumpUI.Helpers;

namespace UE5DumpUI.Views;

/// <summary>
/// Reusable picker button for the client-side class-noise filter. Drop it into a
/// panel toolbar and bind its <see cref="Filter"/> property to the owning
/// ViewModel's <see cref="ClassFacetFilter"/>:
/// <code>&lt;views:ClassFacetFilterButton Filter="{Binding ClassFilter}"/&gt;</code>
/// The button surfaces the Top-N class histogram in a flyout; ticking a row
/// hides that class from the result list immediately (the helper re-runs the
/// owner's filter). Shared across result panels so the filter behaves the
/// same in each.
/// </summary>
public partial class ClassFacetFilterButton : UserControl
{
    /// <summary>The class-noise filter this button drives. Set by the owner via
    /// a styled-property binding (NOT DataContext) so the owner's compiled
    /// bindings stay in the owner's own scope.</summary>
    public static readonly StyledProperty<ClassFacetFilter?> FilterProperty =
        AvaloniaProperty.Register<ClassFacetFilterButton, ClassFacetFilter?>(nameof(Filter));

    public ClassFacetFilter? Filter
    {
        get => GetValue(FilterProperty);
        set => SetValue(FilterProperty, value);
    }

    public ClassFacetFilterButton()
    {
        InitializeComponent();
    }
}
