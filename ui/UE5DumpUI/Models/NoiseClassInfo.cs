namespace UE5DumpUI.Models;

/// <summary>
/// One class's verdict from the opt-in "auto-detect system classes" classifier
/// (<c>detect_noise_classes</c>). The DLL marks a class <see cref="IsNoise"/>
/// only by safe-by-construction rules — an engine package (/Script/Engine,
/// /Script/UMG, /Script/Slate, …; Aura.h IsEnginePackage) or a super-chain that
/// reaches a pure-engine leaf base (Aura.h SnapshotEngineNoiseBases). It never
/// uses class-name substrings, and its base rule never flags an ActorComponent
/// descendant (the package rule still flags one that lives in an engine
/// package). <see cref="Reason"/> is the matched rule
/// label (empty when not noise), shown next to the pre-ticked picker row.
/// </summary>
public sealed class NoiseClassInfo
{
    public string ClassName { get; set; } = "";
    public bool   IsNoise   { get; set; }
    public string Reason    { get; set; } = "";
}
