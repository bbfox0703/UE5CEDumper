using System.Collections.Generic;

namespace UE5DumpUI.Models;

/// <summary>
/// Offsets baked into a no-DLL standalone CE-Lua trainer
/// (StandaloneTrainerScriptGenerator). Populated from the DLL's
/// <c>get_trainer_offsets</c> pipe reply (the decomposed *GWorld->Pawn chain +
/// per-field offsets) plus the GWorld AOB metadata from <see cref="EngineState"/>.
/// Parsed via JsonNode in DumpService — no source-gen context needed (this is a
/// response model, not an AOBMaker request).
/// </summary>
public sealed class TrainerOffsets
{
    public int Code { get; set; }

    /// <summary>*GWorld -> Pawn pointer chain, one bake-able (offset, deref) hop each.</summary>
    public List<TrainerChainHop> Chain { get; set; } = new();

    public int PawnToRoot { get; set; } = -1;     // APawn.RootComponent (deref)
    public int RootToRelLoc { get; set; } = -1;   // USceneComponent.RelativeLocation (inline FVector)
    public int FVectorWidth { get; set; }         // total FVector size: 12 (float) or 24 (double/LWC)
    public int PawnToCmc { get; set; } = -1;      // APawn.CharacterMovement (deref)
    public int WalkSpeedOff { get; set; } = -1;   // CMC.MaxWalkSpeed (float)
    public int GravityOff { get; set; } = -1;     // CMC.GravityScale (float)
    public int JumpOff { get; set; } = -1;        // CMC.JumpZVelocity (float)
    public int CtrlRotOff { get; set; } = -1;     // AController.ControlRotation (inline FRotator)
    public int CtrlRotSize { get; set; }
    public int PawnToController { get; set; } = -1; // APawn.Controller (deref) — fly ControlRotation read
    public int MoveModeOff { get; set; } = -1;    // CMC.MovementMode (1 byte enum) — fly
    public int VelocityOff { get; set; } = -1;    // CMC.Velocity (inline FVector) — fly
    public int VelocitySize { get; set; }         // 12 (float) / 24 (double LWC)

    /// <summary>Matched protection bits (bCanBeDamaged + any invincibility bool).</summary>
    public List<TrainerProtectBit> GodBits { get; set; } = new();

    // ---- Filled client-side from EngineState before generating the trainer ----
    public string Module { get; set; } = "";
    public string GWorldAob { get; set; } = "";
    public int GWorldAobPos { get; set; }
    public int GWorldAobLen { get; set; }

    /// <summary>Per-axis FVector element width (4 = float / 8 = double LWC).</summary>
    public int VecElementWidth => FVectorWidth >= 24 ? 8 : 4;

    /// <summary>The minimum needed to emit a working teleport trainer.</summary>
    public bool IsUsable =>
        Code == 0 && Chain.Count > 0 && PawnToRoot >= 0 && RootToRelLoc >= 0
        && !string.IsNullOrWhiteSpace(GWorldAob);
}

/// <summary>One hop of the *GWorld -> Pawn pointer chain.</summary>
public sealed class TrainerChainHop
{
    public string Field { get; set; } = "";
    public int Offset { get; set; }
    public bool Deref { get; set; } = true;
}

/// <summary>A protection bool resolved as a pawn-relative bit (for GodMode freeze).</summary>
public sealed class TrainerProtectBit
{
    public string Name { get; set; } = "";
    public int ByteOffset { get; set; }   // absolute byte offset from the pawn base
    public int Mask { get; set; }         // single-bit mask (power of two)
    public int Protect { get; set; }      // bit value meaning "protected" (0 for bCanBeDamaged)
}
