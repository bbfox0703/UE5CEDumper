using System.Collections.Generic;
using System.Linq;

namespace UE5DumpUI.Models;

/// <summary>
/// One UFunction entry returned by the <c>list_all_functions</c> pipe
/// command. Backs the "Interesting Functions Finder" panel
/// (<see cref="ViewModels.InterestingFunctionsViewModel"/>).
///
/// Lightweight by design -- the DLL omits param details to keep the
/// pipe payload bounded for games with ~50k+ functions. The UI fetches
/// full param data on-demand via <c>walk_functions</c> when the user
/// picks a function (e.g. for "Open in LiveWalker" or
/// "Copy AA Script (Baked)").
/// </summary>
public sealed class AllFunctionEntry
{
    public string ClassName       { get; init; } = "";
    public string ClassAddr       { get; init; } = "";
    public string SuperName       { get; init; } = "";
    public string ClassPath       { get; init; } = "";
    public string FuncName        { get; init; } = "";
    public string FuncAddr        { get; init; } = "";
    public uint   FunctionFlags   { get; init; }
    public byte   NumParms        { get; init; }
    public ushort ParmsSize       { get; init; }

    // ------------------------------------------------------------------
    // Computed display helpers (UI-bound; no network/IO)
    // ------------------------------------------------------------------

    /// <summary>
    /// Short flag label for the DataGrid -- compact subset of
    /// <see cref="FunctionInfoModel.DecodeFunctionFlags"/> showing only
    /// the flags that affect cheat-table relevance + safety.
    /// </summary>
    public string ShortFlags
    {
        get
        {
            var parts = new List<string>(4);
            if ((FunctionFlags & 0x0400_0000) != 0) parts.Add("BC");   // BlueprintCallable
            if ((FunctionFlags & 0x0800_0000) != 0) parts.Add("BE");   // BlueprintEvent
            if ((FunctionFlags & 0x1000_0000) != 0) parts.Add("BP");   // BlueprintPure
            if ((FunctionFlags & 0x4000_0000) != 0) parts.Add("Const");
            if ((FunctionFlags & 0x0000_0200) != 0) parts.Add("Exec");
            if ((FunctionFlags & 0x0000_0400) != 0) parts.Add("Native");
            if ((FunctionFlags & 0x0000_0800) != 0) parts.Add("Event");
            if ((FunctionFlags & 0x0000_2000) != 0) parts.Add("Static");
            return string.Join(",", parts);
        }
    }

    public bool IsBlueprintCallable => (FunctionFlags & 0x0400_0000) != 0;
    public bool IsBlueprintPure     => (FunctionFlags & 0x1000_0000) != 0;
    public bool IsBlueprintEvent    => (FunctionFlags & 0x0800_0000) != 0;
    public bool IsConst             => (FunctionFlags & 0x4000_0000) != 0;
    public bool IsNative            => (FunctionFlags & 0x0000_0400) != 0;

    /// <summary>
    /// UFUNCTION(exec) — a console-invokable command. The cooker preserves
    /// these in Shipping builds (often inside UCheatManager subclasses),
    /// so the developer's own debug/cheat entry points remain accessible
    /// at runtime. Backs the Console panel discovery filter.
    /// </summary>
    public bool IsExec              => (FunctionFlags & 0x0000_0200) != 0;

    /// <summary>"NumParms (ParmsSize B)" e.g. "2 (5B)".</summary>
    public string ParamsLabel => $"{NumParms} ({ParmsSize}B)";
}

/// <summary>
/// Full result set from <c>list_all_functions</c>. The
/// <see cref="ScannedObjects"/> / <see cref="ScannedClasses"/> /
/// <see cref="TotalFunctions"/> counters are surfaced in the UI status
/// bar so the user can sanity-check the scan against game-known counts
/// and spot regressions if a future DLL change starts dropping classes.
/// </summary>
public sealed class AllFunctionsResult
{
    public int Total          { get; init; }
    public int ScannedObjects { get; init; }
    public int ScannedClasses { get; init; }

    /// <summary>
    /// Functions emitted. Identical to <see cref="Total"/> by construction on the
    /// DLL side — it is NOT an honest pool total and must never be read as one.
    /// Use <see cref="Truncated"/> to know whether a pool larger than this exists.
    /// </summary>
    public int TotalFunctions { get; init; }

    /// <summary>
    /// The DLL walk stopped at <see cref="Limit"/>, so <see cref="Functions"/> is a
    /// PAGE, not the pool. Any "this game has no X" claim built from a truncated
    /// scan is a claim about the page. (audit #5 Z8)
    /// </summary>
    public bool Truncated { get; init; }

    /// <summary>The walk was aborted mid-GObjects (client gone / shutdown). Also partial.</summary>
    public bool Aborted { get; init; }

    /// <summary>The row cap the DLL actually applied (echoed back so the status line
    /// can name it without the caller re-deriving its own request).</summary>
    public int Limit { get; init; }

    /// <summary>True when the returned set is a page rather than the whole pool —
    /// the single predicate every caller should gate an "in this game" claim on.</summary>
    public bool IsPartial => Truncated || Aborted;

    /// <summary>
    /// Classes that emitted at least one row — the honest denominator for any
    /// "N functions from M classes" sentence. <see cref="ScannedClasses"/> is the
    /// EXAMINED count (post game-only filter) and answers a different question; do NOT
    /// substitute one for the other. On P3R they read 889 and 2,293.
    ///
    /// <para>DERIVED from <see cref="Functions"/> rather than carried on the wire, so it
    /// cannot drift from the rows it describes and cannot be left unset by a
    /// construction path — every VM test builds this object directly and would have
    /// seen 0 from a parser-computed field. Keyed on <c>ClassAddr</c>, not
    /// <c>ClassName</c>: two Blueprint classes can share a name, and the DLL always
    /// populates the address. (FUNCDENOM-2026-08-26)</para>
    /// </summary>
    public int ClassesWithFunctions =>
        Functions.Select(f => f.ClassAddr).Distinct().Count();

    public List<AllFunctionEntry> Functions { get; init; } = new();
}
