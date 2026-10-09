using UE5DumpUI.Services;

namespace UE5DumpUI.Models;

/// <summary>
/// One entry in a multi-row .CT batch. Discriminated union over the row
/// kinds that <see cref="CheatTableBuilder"/> understands -- currently
/// Property (freeze script) and Function (baked invoke).
///
/// Designed agnostic of the source panel so future call sites
/// (LiveWalker mixed-row selection, Live ProcessEvent Profiler results,
/// ...) can feed the same builder without touching the panel-specific
/// ScoredFunctionRow / ScoredPropertyRow models.
/// </summary>
public abstract record CheatTableRow
{
    /// <summary>Group label the builder uses to bucket rows into
    /// per-category subgroups in the generated CT. e.g. "Stats",
    /// "Combat", "Movement". Free-form on input; the builder sorts +
    /// groups by exact string match so any consistent labelling works.
    /// Empty string is allowed and lands in an "Uncategorised" bucket.</summary>
    public required string Category { get; init; }

    /// <summary>Human-readable description for the CE entry. Shown in
    /// the CE table grid. e.g. "BP_Player_C::Health (ACharacter)".
    /// XML-escaped automatically by the builder.</summary>
    public required string Description { get; init; }

    /// <summary>Render the AA Script body to embed in the CE XML.
    /// Implementations call into BakedScriptGenerator /
    /// FreezeScriptGenerator etc. so the script-generation logic stays
    /// single-sourced.</summary>
    public abstract string GenerateScript();
}

/// <summary>Property freeze entry (uses ue5_freeze_helper.lua).
/// User edits CFG.value in CE after enabling.</summary>
public sealed record CtPropertyRow : CheatTableRow
{
    public required FreezeScriptParams FreezeParams { get; init; }
    public override string GenerateScript()
        => FreezeScriptGenerator.Generate(FreezeParams);
}

/// <summary>Generic raw-script entry — the caller supplies a fully-formed AA
/// Script body. Used by panels (e.g. Teleport) whose script generation doesn't
/// fit the Property/Function row kinds.</summary>
public sealed record CtScriptRow : CheatTableRow
{
    public required string Script { get; init; }
    public override string GenerateScript() => Script;
}

/// <summary>UFunction baked-invoke entry (uses ue5_invoke_helper.lua).
/// For functions with parameters the user typically wants to edit the
/// baked PARAMS table in CE before activating; the builder honours
/// whatever <see cref="BakedValues"/> the caller supplies (empty for
/// no-arg functions; zeroed for "TODO: edit me" placeholders).</summary>
public sealed record CtFunctionRow : CheatTableRow
{
    public required string ClassName { get; init; }
    public required string FuncName { get; init; }
    /// <summary>The CE helper's <c>parmsSize</c>, which gates the mailbox slab: where the function's parameter chain
    /// ends (<see cref="BakedScriptGenerator.BakedParmsSize(AllFunctionEntry)"/>), not the tail's ParmsSize.</summary>
    public required int ParmsSize { get; init; }
    public required IReadOnlyList<BakedParamValue> BakedValues { get; init; }
    public override string GenerateScript()
        => BakedScriptGenerator.Generate(
               ClassName, FuncName, ParmsSize, BakedValues);
}
