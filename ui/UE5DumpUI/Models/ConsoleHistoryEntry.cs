namespace UE5DumpUI.Models;

/// <summary>
/// One row in the Console panel's invocation history. Built by
/// <see cref="ViewModels.ConsoleViewModel"/> after every Run; pinned to
/// the panel's bottom list so the user can scan recent calls at a glance
/// without scrolling the discovery grid.
///
/// Pure DTO — no behaviour, no IO — keeps the AOT-trimming surface
/// minimal and the test fixture-construction trivial.
/// </summary>
public sealed class ConsoleHistoryEntry
{
    public required DateTime When        { get; init; }
    public required string   ClassName   { get; init; }
    public required string   FuncName    { get; init; }
    public required bool     Success     { get; init; }
    public required string   ResultText  { get; init; }

    /// <summary>"HH:mm:ss Class::Func" — left column of the history list.</summary>
    public string Label =>
        $"{When:HH:mm:ss}  {ClassName}::{FuncName}";

    /// <summary>"OK" or "Err" — short status badge.</summary>
    public string Badge => Success ? "OK" : "Err";

    public string BadgeColor => Success ? "#4EC9B0" : "#F44747";

    /// <summary>Full tooltip with class, function, timestamp, result.</summary>
    public string Tooltip =>
        $"{ClassName}::{FuncName} @ {When:yyyy-MM-dd HH:mm:ss}\n{ResultText}";
}
