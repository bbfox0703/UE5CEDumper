namespace UE5DumpUI.ViewModels;

/// <summary>
/// Outcome of the Property Search "Force → value…" prompt.
///
/// <para>
/// Three states, because collapsing them to two is the defect this type exists to remove
/// (audit #5 AF6). The prompt used to return <c>double?</c>: null meant cancel, and a
/// literal the dialog had ALREADY validated per-type but the caller could not convert
/// returned null too — so a rejected value was reported to the user as "you pressed
/// Cancel", i.e. as nothing at all.
/// </para>
/// <list type="bullet">
///   <item><description><see cref="Cancel"/> — user dismissed the dialog. Say nothing.</description></item>
///   <item><description><see cref="Reject"/> — a value was entered and cannot be used. Say why.</description></item>
///   <item><description><see cref="Accept"/> — use <see cref="Value"/>.</description></item>
/// </list>
/// </summary>
public readonly struct ForceValuePromptResult
{
    /// <summary>True when the user dismissed the dialog without entering anything.</summary>
    public bool Cancelled { get; private init; }

    /// <summary>Why the entered value cannot be forced; null when there is no problem.</summary>
    public string? Error { get; private init; }

    /// <summary>The value to force. Meaningful only when neither Cancelled nor Error is set.</summary>
    public double Value { get; private init; }

    public static ForceValuePromptResult Cancel() => new() { Cancelled = true };

    public static ForceValuePromptResult Reject(string reason) => new() { Error = reason };

    public static ForceValuePromptResult Accept(double value) => new() { Value = value };
}
