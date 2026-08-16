using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class PropertySearchPanel : UserControl
{
    // AOT-safe sort comparers for the columns whose sort property isn't
    // rooted by a column-level Binding: the three template columns (Scope /
    // Property / Preview) and the Offset text column (sorts by PropOffset
    // while it displays OffsetHex). Without these the header click is a
    // silent no-op under AOT (aot-pitfalls.md §4.5).
    private static readonly IReadOnlyDictionary<string, IComparer> ResultsSortComparers =
        new Dictionary<string, IComparer>
        {
            ["InheritedByCount"] = DataGridSortComparers.Number<PropertySearchMatch>(r => r.InheritedByCount),
            ["PropName"]         = DataGridSortComparers.Ordinal<PropertySearchMatch>(r => r.PropName),
            ["PropOffset"]       = DataGridSortComparers.Number<PropertySearchMatch>(r => r.PropOffset),
            ["Preview"]          = DataGridSortComparers.Ordinal<PropertySearchMatch>(r => r.Preview),
        };

    public PropertySearchPanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("ResultsGrid")?.WireSortComparers(ResultsSortComparers);
        Loaded += OnPanelLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is not PropertySearchViewModel vm) return;
        // Inject the value-prompt callback: the VM stays View-free so it
        // remains unit-testable, while the dialog mechanics live here.
        vm.FreezeValuePrompt = PromptFreezeValueAsync;
        // Force-field (Solide) dialogs — numeric value prompt + object-null confirm.
        vm.ForceValuePrompt = PromptForceValueAsync;
        vm.ForceNullConfirm = ConfirmForceNullAsync;
        // Probe AOBMaker once on attach so the Freeze button reflects
        // current state (cooldown inside the VM prevents pipe spam).
        _ = vm.RefreshAobMakerAvailabilityAsync();
        // Reflect any holds already active in the DLL (e.g. from a prior session).
        _ = vm.RefreshForcedFieldsAsync();
    }

    private async System.Threading.Tasks.Task<ForceValuePromptResult> PromptForceValueAsync(
        PropertySearchMatch match)
    {
        // Reuse the Freeze value dialog (per-type validation), then convert the returned
        // literal to the double the force_field command carries — DLL-side too:
        // Solide::AddForce takes a double, so this is the wire's width, not a UI choice.
        var dialog = new FreezeValueDialog(match);
        Window? owner = null;
        if (Avalonia.Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop)
            owner = desktop.MainWindow;
        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();
        var literal = dialog.ValueLiteral;
        if (string.IsNullOrWhiteSpace(literal)) return ForceValuePromptResult.Cancel();
        return ParseForceLiteral(literal);
    }

    /// <summary>
    /// Convert a value the Freeze dialog already accepted for this property's type into the
    /// double the force_field path carries — and REFUSE rather than write a different number.
    ///
    /// <para>
    /// Two failures used to look identical to a cancel (audit #5 AF6): an unconvertible
    /// literal, and a 64-bit integer beyond a double's 53-bit mantissa. The second is the
    /// nastier one — <c>double.TryParse</c> SUCCEEDS on 9223372036854775807 and hands back
    /// 9223372036854775808, so Force would have silently held the field at a value the user
    /// never typed. Refusing is the honest answer while the wire is a double end-to-end;
    /// widening it is a DLL + protocol change, not a UI one.
    /// </para>
    /// </summary>
    internal static ForceValuePromptResult ParseForceLiteral(string literal)
    {
        var s = literal.Trim();
        // Try integer first: only an exact integral literal can suffer the precision trap,
        // and only for integers is "the value I typed" exactly representable in the first place.
        if (long.TryParse(s, System.Globalization.NumberStyles.Integer,
                          System.Globalization.CultureInfo.InvariantCulture, out var i64))
        {
            var asDouble = (double)i64;
            // Range-guard FIRST, then cast back. Three tempting one-liners are all wrong here:
            //   (long)asDouble != i64        — an out-of-range double->long SATURATES in .NET
            //                                  Core, so long.MaxValue reports itself unchanged
            //                                  and the check passes on the one input it is for;
            //   asDouble.ToString("F0")      — a formatting question, answered differently at
            //                                  the ends of the range;
            //   (decimal)asDouble != i64     — the double->decimal conversion ROUNDS TO 15
            //                                  SIGNIFICANT DIGITS, so it rejects 2^53, which a
            //                                  double holds exactly.
            // Guarded, the cast is exact and this is a true representability test.
            const double kLongMin = -9223372036854775808.0;   // -2^63, exact
            const double kLongMax =  9223372036854775808.0;   //  2^63, exact, EXCLUSIVE
            bool inRange = asDouble >= kLongMin && asDouble < kLongMax;
            if (!inRange || (long)asDouble != i64)
            {
                var wouldBe = inRange
                    ? ((long)asDouble).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "a different number";
                return ForceValuePromptResult.Reject(
                    $"{i64} cannot be held exactly — force_field carries a double, which has 53 " +
                    $"bits of mantissa, so it would be held at {wouldBe} instead. Refused.");
            }
            return ForceValuePromptResult.Accept(asDouble);
        }

        if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            // NaN / ±Infinity parse cleanly and would be written straight into game memory.
            if (double.IsNaN(v) || double.IsInfinity(v))
                return ForceValuePromptResult.Reject($"'{s}' is not a finite number.");
            return ForceValuePromptResult.Accept(v);
        }

        return ForceValuePromptResult.Reject($"'{s}' is not a number this field can be forced to.");
    }

    private async System.Threading.Tasks.Task<bool> ConfirmForceNullAsync(PropertySearchMatch match)
        => await ConfirmDialog.ShowAsync(
            "Force object pointer to null?",
            $"This nulls {match.ClassName}::{match.PropName} on every live instance and holds " +
            "it null. If the game later dereferences that pointer it may crash. Continue?",
            confirmText: "Force null", cancelText: "Cancel");

    private async System.Threading.Tasks.Task<string?> PromptFreezeValueAsync(PropertySearchMatch match)
    {
        var dialog = new FreezeValueDialog(match);
        // Find the owning Window so the dialog modals correctly.
        Window? owner = null;
        if (Avalonia.Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop)
        {
            owner = desktop.MainWindow;
        }
        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();  // fallback — shouldn't happen in real runs
        return dialog.ValueLiteral;
    }

    /// <summary>
    /// Forward the grid's multi-select (empty = all rows) to the batch Find
    /// Funcs command, which fills each row's inline "Funcs" column. Mirrors
    /// InterestingPropertiesPanel.OnGenerateCtClick's typed forwarding.
    /// </summary>
    private void OnBatchFindFuncsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not PropertySearchViewModel vm) return;
        var grid = this.FindControl<DataGrid>("ResultsGrid");
        var rows = new List<PropertySearchMatch>();
        if (grid?.SelectedItems is { } sel)
        {
            foreach (var item in sel)
                if (item is PropertySearchMatch m) rows.Add(m);
        }
        vm.BatchFindFuncsCommand.Execute(rows);
    }

    private void SearchQueryInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is PropertySearchViewModel vm
            && vm.SearchCommand.CanExecute(null))
        {
            vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnPanelLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not PropertySearchViewModel vm) return;
        if (vm.SelectedResult == null) return;
        var grid = this.FindControl<DataGrid>("ResultsGrid");
        if (grid == null) return;
        // Defer until the DataGrid has its row visuals materialized — without
        // the dispatcher hop ScrollIntoView no-ops because the row isn't in
        // the realized container set yet.
        Dispatcher.UIThread.Post(() =>
        {
            try { grid.ScrollIntoView(vm.SelectedResult, null); }
            catch { /* defensive: missing row, recycled grid, etc. */ }
        }, DispatcherPriority.Background);
    }
}
