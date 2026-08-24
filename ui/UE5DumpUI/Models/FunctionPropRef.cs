namespace UE5DumpUI.Models;

/// <summary>
/// One FProperty referenced by a UFunction, with read/write tally. Result of the
/// reverse edge (function -> properties it reads/writes). Two recovery methods:
/// Path 1 walks Blueprint/script bytecode (exact); Path 2 disassembles native
/// (C++) functions (heuristic — see <see cref="Confidence"/>).
/// </summary>
public sealed class FunctionPropRef
{
    public string PropAddress { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";       // "FloatProperty" / "StructProperty" / ...
    public int Occurrences { get; init; }
    public int WriteCount { get; init; }           // reads = Occurrences - WriteCount

    /// <summary>
    /// "instance" (class member — the RE target), "local" (function local/param,
    /// e.g. BP compiler CallFunc_* temporaries), "default", "sparse", "struct",
    /// "frame". The dialog defaults to instance-only.
    /// </summary>
    public string Scope { get; init; } = "";

    /// <summary>
    /// Path 2 only: the class-member offset the <c>[this+off]</c> access mapped to
    /// (-1 = not applicable / bytecode path).
    /// </summary>
    public int Offset { get; init; } = -1;

    /// <summary>
    /// Path 2 only: "high" (base register proven to be <c>this</c>) or "low"
    /// (<c>[reg+off]</c> matched a property offset but the base isn't provably
    /// <c>this</c>). Empty for the exact bytecode path.
    /// </summary>
    public string Confidence { get; init; } = "";

    /// <summary>True for a low-confidence heuristic (native disasm) row.</summary>
    public bool IsLowConfidence => Confidence == "low";

    /// <summary>True when this is a class member field (vs a local/temporary).</summary>
    public bool IsClassField => Scope == "instance";

    /// <summary>Compact access summary: "3W / 4R", "write", or "read".</summary>
    public string AccessSummary =>
        WriteCount <= 0 ? "read"
        : WriteCount >= Occurrences ? "write"
        : $"{WriteCount}W / {Occurrences - WriteCount}R";
}

/// <summary>Result of a walk_function_props scan.</summary>
public sealed class FunctionPropRefsResult
{
    public string QueryAddress { get; init; } = "";
    public int ScriptBytes { get; init; }          // 0 = native / empty bytecode

    /// <summary>
    /// How the props were recovered: "bytecode" (Path 1 Kismet scan, exact),
    /// "disasm" (Path 2 native x64 disassembly, heuristic), "none" (native
    /// but analysis unavailable — Func offset unresolved on this build), or
    /// "blueprint_no_script".
    ///
    /// <para>⚠ The last one is a REFUSAL and the caller must not render it as an empty
    /// result: a script/Blueprint UFunction with no usable Script buffer points
    /// <c>Func</c> at the shared interpreter (<c>UObject::ProcessInternal</c>), so Path 2
    /// would disassemble the INTERPRETER and attribute its field accesses to this
    /// function. Zero props here means "not looked at", not "touches nothing" — which is
    /// the opposite of what this dialog is read for.</para>
    /// </summary>
    public string Method { get; init; } = "bytecode";

    /// <summary>Path 2: [reg+off] accesses with no matching class property.</summary>
    public int Unmapped { get; init; }

    /// <summary>
    /// Path 2: the disassembler stopped at its instruction budget, so
    /// <see cref="Props"/> is a PREFIX of the fields this function touches — a field
    /// missing from the list is "not seen YET", not "not used" (audit #5 AF7).
    ///
    /// <para>Matters most for the read the dialog is used for: "nothing writes this
    /// field, so freezing it is safe". Denken computed the flag and Aura logged it,
    /// and it was dropped before the wire — the same discard shape as Z9/AE13.
    /// Always false for the exact bytecode path.</para>
    /// </summary>
    public bool BudgetHit { get; init; }

    /// <summary>True when results came from native x64 disassembly (heuristic).</summary>
    public bool IsDisasm => Method == "disasm";

    public List<FunctionPropRef> Props { get; init; } = new();
}
