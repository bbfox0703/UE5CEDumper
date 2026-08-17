namespace UE5DumpUI.Models;

/// <summary>
/// Pawn pose returned by <c>teleport_get_pose</c> / <c>teleport_save_marker</c>.
/// X/Y/Z = world location, Pitch/Yaw/Roll = control rotation. The DLL widens
/// UE4 float fields to double at the boundary, so these are always doubles.
/// </summary>
public sealed class TeleportPose
{
    /// <summary>Wirbel result code (0 = OK, negatives per
    /// docs/teleport-spec.md §8). <see cref="TeleportCodes"/> maps these.</summary>
    public int Code { get; init; }

    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double Pitch { get; init; }
    public double Yaw { get; init; }
    public double Roll { get; init; }

    /// <summary>UWorld object name the pose was read on.</summary>
    public string Map { get; init; } = "";

    /// <summary>"raw" (direct property read) or "invoke"
    /// (K2_GetActorLocation, used for attached/vehicle pawns).</summary>
    public string Source { get; init; } = "raw";

    /// <summary>Resolved pawn object address as a hex string ("0x0"/"" when
    /// unavailable) — the object whose coordinates this pose reports. Used by the
    /// Teleport tab's "Locate in GWorld" handoff to select this exact pawn in the
    /// Live Walker. <see cref="HasPawnAddr"/> tests validity.</summary>
    public string PawnAddr { get; init; } = "";

    /// <summary>True when <see cref="PawnAddr"/> is a usable non-null address.</summary>
    public bool HasPawnAddr =>
        !string.IsNullOrEmpty(PawnAddr) && PawnAddr != "0x0" && PawnAddr != "0X0";

    /// <summary>Owning sub-object (RootComponent) of the position FVector, for the
    /// "Locate position vector in GWorld" handoff. "" / "0x0" when unresolved.</summary>
    public string LocOwnerAddr { get; init; } = "";

    /// <summary>RelativeLocation offset within <see cref="LocOwnerAddr"/>.</summary>
    public int LocFieldOffset { get; init; }

    /// <summary>Field name to land on ("RelativeLocation").</summary>
    public string LocFieldName { get; init; } = "";

    /// <summary>True when the position vector's owner+offset are usable.</summary>
    public bool HasLocAddr =>
        !string.IsNullOrEmpty(LocOwnerAddr) && LocOwnerAddr != "0x0" && LocOwnerAddr != "0X0";

    /// <summary>Owning sub-object (CharacterMovement) of the velocity FVector, for
    /// the "Locate velocity vector in GWorld" handoff. Only set when
    /// <see cref="HasMovement"/>. "" / "0x0" when unresolved.</summary>
    public string VelOwnerAddr { get; init; } = "";

    /// <summary>Velocity offset within <see cref="VelOwnerAddr"/>.</summary>
    public int VelFieldOffset { get; init; }

    /// <summary>Field name to land on ("Velocity").</summary>
    public string VelFieldName { get; init; } = "";

    /// <summary>True when the velocity vector's owner+offset are usable.</summary>
    public bool HasVelAddr =>
        !string.IsNullOrEmpty(VelOwnerAddr) && VelOwnerAddr != "0x0" && VelOwnerAddr != "0X0";

    /// <summary>Owning sub-object (CharacterMovement) of the acceleration FVector,
    /// for the "Locate acceleration vector in GWorld" handoff. Only set when
    /// <see cref="HasMovement"/>.</summary>
    public string AccOwnerAddr { get; init; } = "";

    /// <summary>Acceleration offset within <see cref="AccOwnerAddr"/>.</summary>
    public int AccFieldOffset { get; init; }

    /// <summary>Field name to land on ("Acceleration").</summary>
    public string AccFieldName { get; init; } = "";

    /// <summary>True when the acceleration vector's owner+offset are usable.</summary>
    public bool HasAccAddr =>
        !string.IsNullOrEmpty(AccOwnerAddr) && AccOwnerAddr != "0x0" && AccOwnerAddr != "0X0";

    /// <summary>Owning object (Controller) of the ControlRotation FRotator, for the
    /// "Locate rotation in GWorld" handoff.</summary>
    public string RotOwnerAddr { get; init; } = "";

    /// <summary>ControlRotation offset within <see cref="RotOwnerAddr"/>.</summary>
    public int RotFieldOffset { get; init; }

    /// <summary>Field name to land on ("ControlRotation").</summary>
    public string RotFieldName { get; init; } = "";

    /// <summary>True when the rotation field's owner+offset are usable.</summary>
    public bool HasRotAddr =>
        !string.IsNullOrEmpty(RotOwnerAddr) && RotOwnerAddr != "0x0" && RotOwnerAddr != "0X0";

    /// <summary>True when the pawn has a UCharacterMovementComponent whose
    /// reflected Velocity field resolved, so <see cref="VelX"/>/<see cref="Speed"/>
    /// (and usually <see cref="AccX"/>) are live. False on vehicle / custom-framework
    /// pawns — the UI then shows velocity/acceleration as "unavailable".</summary>
    public bool HasMovement { get; init; }

    // Live velocity (cm/s) and acceleration (cm/s²) off the CharacterMovement.
    // Only meaningful when HasMovement is true.
    public double VelX { get; init; }
    public double VelY { get; init; }
    public double VelZ { get; init; }
    public double AccX { get; init; }
    public double AccY { get; init; }
    public double AccZ { get; init; }

    /// <summary>Velocity magnitude |Velocity| in cm/s (0 when no movement).</summary>
    public double Speed { get; init; }
}

/// <summary>
/// One tunable CharacterMovement float (MaxWalkSpeed / GravityScale /
/// JumpZVelocity) as reported by <c>get_movement_params</c> (Laufen). The DLL
/// forces the value to <see cref="Base"/> × <see cref="Multiplier"/> and holds it
/// against per-tick overwrites with a re-assert worker.
/// </summary>
public sealed class MovementKnob
{
    /// <summary>The reflected float field was found on the live CMC this instant.</summary>
    public bool Resolved { get; init; }

    /// <summary>Live value read from memory (cm/s for walk speed; unitless scale
    /// for gravity; cm/s for jump velocity).</summary>
    public double Current { get; init; }

    /// <summary>Captured untouched base value (valid only while <see cref="Active"/>).</summary>
    public double Base { get; init; }

    /// <summary>Desired multiplier of <see cref="Base"/> (1.0 = neutral/off).</summary>
    public double Multiplier { get; init; } = 1.0;

    /// <summary>Override engaged (the re-assert worker is holding this value).</summary>
    public bool Active { get; init; }

    /// <summary>Owning sub-object (CharacterMovement) of the float field, for the
    /// "Locate in GWorld" handoff. "" / "0x0" when unresolved.</summary>
    public string OwnerAddr { get; init; } = "";

    /// <summary>Field offset within <see cref="OwnerAddr"/>.</summary>
    public int FieldOffset { get; init; } = -1;

    /// <summary>Reflected property name (e.g. "MaxWalkSpeed").</summary>
    public string FieldName { get; init; } = "";

    /// <summary>True when the owner+offset can be handed to the GWorld locator.</summary>
    public bool HasAddr =>
        Resolved && FieldOffset >= 0 &&
        !string.IsNullOrEmpty(OwnerAddr) && OwnerAddr != "0x0" && OwnerAddr != "0X0";
}

/// <summary>
/// Gravity DIRECTION vector (UE5.4+ UCharacterMovementComponent.GravityDirection).
/// A unit vector — <see cref="Resolved"/> is false on pre-5.4 games where the
/// field isn't reflected.
/// </summary>
public sealed class MovementVectorKnob
{
    public bool Resolved { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; } = -1;   // default (0,0,-1) = straight down
    public bool Active { get; init; }
    public string OwnerAddr { get; init; } = "";
    public int FieldOffset { get; init; } = -1;
    public string FieldName { get; init; } = "";

    public bool HasAddr =>
        Resolved && FieldOffset >= 0 &&
        !string.IsNullOrEmpty(OwnerAddr) && OwnerAddr != "0x0" && OwnerAddr != "0X0";
}

/// <summary>Result of a <c>set_gravity_direction</c> / <c>reset_gravity_direction</c>
/// call. <see cref="State"/> 1 = active, 0 = off, negative = error / not reflected.</summary>
public sealed class MovementVectorResult
{
    public int State { get; init; }
    public int Code { get; init; }
    public bool Resolved { get; init; }
    public bool Active { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; } = -1;
}

/// <summary>
/// Snapshot of all movement knobs on the current pawn's CharacterMovement,
/// returned by <c>get_movement_params</c> (Laufen). <see cref="HasCmc"/> is false
/// on vehicle / custom-framework pawns with no UCharacterMovementComponent.
/// </summary>
public sealed class MovementParams
{
    /// <summary>Laufen::MoveResult (0 = OK, negative = no pawn / no CMC / reflect).</summary>
    public int Code { get; init; }

    /// <summary>A UCharacterMovementComponent resolved on the local pawn.</summary>
    public bool HasCmc { get; init; }

    public MovementKnob WalkSpeed { get; init; } = new();
    public MovementKnob Gravity { get; init; } = new();   // P2
    public MovementKnob Jump { get; init; } = new();      // P3
    public MovementVectorKnob GravityDirection { get; init; } = new();  // UE5.4+
}

/// <summary>Result of a <c>set_movement_multiplier</c> / <c>reset_movement</c>
/// call (Laufen). <see cref="State"/> is 1 = override active, 0 = inactive,
/// negative = error (no pawn / no CMC / property not resolved).</summary>
public sealed class MovementSetResult
{
    public int State { get; init; }
    public int Code { get; init; }
    public bool Active { get; init; }
    public double Current { get; init; }
    public double Base { get; init; }
    public double Multiplier { get; init; } = 1.0;
}

/// <summary>
/// One time-dilation lever (Hemmung) as reported by <c>get_time_state</c> /
/// <c>set_time_dilation</c>: global <c>AWorldSettings::TimeDilation</c> or per-pawn
/// <c>AActor::CustomTimeDilation</c>. The DLL holds the value at
/// <see cref="Value"/> against per-tick game/Sequencer overwrites and restores
/// <see cref="Base"/> on reset.
/// </summary>
public sealed class TimeDilationKnob
{
    /// <summary>The reflected float was found on the live owner this instant.</summary>
    public bool Resolved { get; init; }
    /// <summary>Live dilation read from memory (1.0 = normal, 0.5 = half, 0 = frozen).</summary>
    public double Current { get; init; }
    /// <summary>Captured natural value, restored on reset (valid while <see cref="Active"/>).</summary>
    public double Base { get; init; } = 1.0;
    /// <summary>Desired held value (1.0 = normal).</summary>
    public double Value { get; init; } = 1.0;
    /// <summary>Override engaged (the re-assert worker is holding this value).</summary>
    public bool Active { get; init; }
    /// <summary>Owning object (WorldSettings / pawn) of the float, for the
    /// "Locate in GWorld" handoff. "" / "0x0" when unresolved.</summary>
    public string OwnerAddr { get; init; } = "";
    /// <summary>Field offset within <see cref="OwnerAddr"/>.</summary>
    public int FieldOffset { get; init; } = -1;
    /// <summary>Reflected property name ("TimeDilation" / "CustomTimeDilation").</summary>
    public string FieldName { get; init; } = "";
    /// <summary>True when the owner+offset can be handed to the GWorld locator.</summary>
    public bool HasAddr =>
        Resolved && FieldOffset >= 0 &&
        !string.IsNullOrEmpty(OwnerAddr) && OwnerAddr != "0x0" && OwnerAddr != "0X0";
}

/// <summary>Result of a <c>set_time_dilation</c> / <c>reset_time_dilation</c> call
/// (Hemmung). <see cref="State"/> is 1 = override active, 0 = inactive, negative =
/// error (no WorldSettings / no pawn / not reflected).</summary>
public sealed class TimeDilationSetResult
{
    public int State { get; init; }
    public int Code { get; init; }
    public bool Active { get; init; }
    public double Current { get; init; }
    public double Base { get; init; } = 1.0;
    public double Value { get; init; } = 1.0;
}

/// <summary>Snapshot of both time-dilation levers (Hemmung), returned by
/// <c>get_time_state</c>. <see cref="Global"/> = world speed
/// (AWorldSettings::TimeDilation), <see cref="Pawn"/> = per-player speed
/// (AActor::CustomTimeDilation).</summary>
public sealed class TimeState
{
    /// <summary>Hemmung::TimeResult for the global target (0 = OK, negative = error).</summary>
    public int Code { get; init; }
    public TimeDilationKnob Global { get; init; } = new();
    public TimeDilationKnob Pawn { get; init; } = new();
}

/// <summary>
/// Full God Mode state from <c>get_protect_state</c> (Solitar), as opposed to the
/// single tri-state <c>get_god_mode</c> returns.
///
/// <para>The three fields answer different questions, and collapsing them is what
/// made the badge lie (audit #5 AD4):</para>
/// <list type="bullet">
/// <item><see cref="Want"/> — what the USER asked for. Survives a reconnect, and
/// is what the re-assert worker keeps driving.</item>
/// <item><see cref="Live"/> — what the pawn's <c>bCanBeDamaged</c> ACTUALLY reads
/// right now. <c>-1</c> when there is no pawn to read.</item>
/// <item><see cref="Resolvable"/> — whether a canonical target was found at all.
/// Without it, <see cref="Want"/> is an intention with nothing to write to.</item>
/// </list>
///
/// <para>The wire name for <see cref="Live"/> is <c>godmode</c>, not <c>live</c> —
/// see <c>Fern.cpp</c>'s CMD_GET_PROTECT_STATE handler.</para>
/// </summary>
public sealed class ProtectState
{
    /// <summary>Desired toggle (1 = on, 0 = off). Survives reconnect.</summary>
    public int Want { get; init; }

    /// <summary>Observed live state (1 = immune, 0 = damageable, -1 = no pawn).</summary>
    public int Live { get; init; } = -1;

    /// <summary>True when a canonical <c>bCanBeDamaged</c> target was resolved.</summary>
    public bool Resolvable { get; init; }

    /// <summary>Solitar::ProtectResult (0 = OK, negative = error).</summary>
    public int Code { get; init; }
}

/// <summary>Live state of the Fly feature (Dunste — no-gravity 3D flight),
/// returned by <c>fly_set</c> / <c>fly_get_state</c>. <see cref="HasCmc"/> is
/// false on vehicle / custom-framework pawns with no UCharacterMovementComponent
/// (fly can't engage on those).</summary>
public sealed class FlyStatus
{
    /// <summary>Dunste::FlyResult (0 = OK, negative = no pawn / no CMC / reflect).</summary>
    public int Code { get; init; }
    /// <summary>Fly currently engaged (the worker is holding MOVE_Flying).</summary>
    public bool Active { get; init; }
    /// <summary>Noclip (position-drive, fly through walls) vs velocity (collision).</summary>
    public bool Noclip { get; init; }
    /// <summary>A UCharacterMovementComponent resolved on the local pawn.</summary>
    public bool HasCmc { get; init; }
    /// <summary>Active keyboard preset: 0 = WASD, 1 = numpad, 2 = arrows.</summary>
    public int Preset { get; init; }
    /// <summary>Flight speed in uu/s.</summary>
    public double Speed { get; init; }
    /// <summary>Live EMovementMode enum byte (5 = MOVE_Flying), or -1 unknown.</summary>
    public int CurrentMode { get; init; } = -1;
    /// <summary>Result of the last enable/disable (1 active / 0 off / negative),
    /// or -1 when the call carried no enable field.</summary>
    public int State { get; init; } = -1;
}

/// <summary>Live state of the See-through occluders feature (Schlacht), returned
/// by <c>seethrough_set</c> / <c>seethrough_get_state</c>. Stage 1 hides the single
/// nearest non-Pawn occluder on the camera→pawn line.</summary>
public sealed class SeeThroughStatus
{
    /// <summary>Schlacht::SeeThroughResult (0 = OK, negative = not-init / no-pawn /
    /// reflection / no-camera on the last tick).</summary>
    public int Code { get; init; }
    /// <summary>The see-through worker is engaged.</summary>
    public bool Active { get; init; }
    /// <summary>Camera + pawn resolved on the last tick (the trace can run).</summary>
    public bool HasTarget { get; init; }
    /// <summary>Occluders currently hidden.</summary>
    public int HiddenCount { get; init; }
    /// <summary>How many nearest occluders are hidden along the ray (pierce depth).</summary>
    public int PierceCount { get; init; } = 1;
    /// <summary>Result of the last enable/disable (1 active / 0 off / negative),
    /// or -1 when the call carried no enable field.</summary>
    public int State { get; init; } = -1;
    /// <summary>Whether the DLL's game-thread ProcessEvent hook is up RIGHT NOW.
    /// See-through traces the world by invoking on the game thread, so with the
    /// hook down the feature refuses to enable (<see cref="Code"/> -5). The hook
    /// install can fail transiently (MinHook trampoline allocation) and recover on
    /// a later attempt, so this is polled rather than remembered.</summary>
    public bool HookActive { get; init; } = true;
}

/// <summary>Result of a teleport action (recall / cursor).</summary>
public sealed class TeleportResult
{
    /// <summary>Wirbel result code (0 = OK).</summary>
    public int Code { get; init; }

    /// <summary>1 = engine invoke path (clean), 2 = raw-write fallback
    /// (the game may snap the pawn back).</summary>
    public int Tier { get; init; }

    // --- Map mismatch detail (Code == -7) ---
    public string? CurrentMap { get; init; }
    public string? MarkerMap { get; init; }

    // --- Cursor teleport detail (Code == 0) ---
    public bool UsedCenter { get; init; }
    public double HitX { get; init; }
    public double HitY { get; init; }
    public double HitZ { get; init; }
}

/// <summary>One marker slot as reported by <c>teleport_get_markers</c>.</summary>
public sealed class TeleportMarker
{
    public int Slot { get; init; }
    public bool Valid { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double Pitch { get; init; }
    public double Yaw { get; init; }
    public double Roll { get; init; }
    public string Map { get; init; } = "";
}

/// <summary>
/// Camera point-of-view returned by <c>teleport_get_pov</c> (read-only). The
/// camera world location/rotation come from APlayerCameraManager's getters
/// (GetCameraLocation / GetCameraRotation / GetFOVAngle) and are DISTINCT from
/// <see cref="TeleportPose"/> (the pawn). On games that drive the camera
/// independently of the possessed pawn (HD-2D / fixed-view), the two diverge —
/// surfacing both makes that visible. There is no Set POV: the on-screen view
/// is recomputed every tick, so a write wouldn't stick (see teleport-spec §POV).
/// </summary>
public sealed class TeleportPov
{
    /// <summary>Wirbel result code (0 = OK). <see cref="TeleportCodes"/> maps these.</summary>
    public int Code { get; init; }

    // Camera world location.
    public double CamX { get; init; }
    public double CamY { get; init; }
    public double CamZ { get; init; }
    // Camera rotation.
    public double Pitch { get; init; }
    public double Yaw { get; init; }
    public double Roll { get; init; }

    /// <summary>Effective field-of-view angle in degrees (0 when unavailable).</summary>
    public double Fov { get; init; }

    /// <summary>"invoke" (camera-manager getters) or "raw" (cached POV read,
    /// used when the getters exist but ProcessEvent returns nothing — e.g. on
    /// TQ2 / Octopath).</summary>
    public string Source { get; init; } = "invoke";

    /// <summary>True when the pawn world location was resolved for the delta.</summary>
    public bool HasPawn { get; init; }
    public double PawnX { get; init; }
    public double PawnY { get; init; }
    public double PawnZ { get; init; }

    /// <summary>3D distance camera↔pawn (0 when no pawn). A large value that does
    /// NOT change when you teleport indicates an independent camera.</summary>
    public double PawnDistance => HasPawn
        ? System.Math.Sqrt((CamX - PawnX) * (CamX - PawnX)
                         + (CamY - PawnY) * (CamY - PawnY)
                         + (CamZ - PawnZ) * (CamZ - PawnZ))
        : 0;

    /// <summary>The APlayerCameraManager object — owner for the cached-POV
    /// "Locate in GWorld" handoffs (Location / FOV). "" / "0x0" when unresolved.</summary>
    public string CamOwnerAddr { get; init; } = "";

    /// <summary>Offset of CameraCachePrivate.POV.Location within the camera manager.</summary>
    public int CamLocFieldOffset { get; init; } = -1;
    public string CamLocFieldName { get; init; } = "";

    /// <summary>Offset of CameraCachePrivate.POV.Rotation within the camera manager.</summary>
    public int CamRotFieldOffset { get; init; } = -1;
    public string CamRotFieldName { get; init; } = "";

    /// <summary>Offset of CameraCachePrivate.POV.FOV within the camera manager.</summary>
    public int CamFovFieldOffset { get; init; } = -1;
    public string CamFovFieldName { get; init; } = "";

    private bool CamOwnerUsable =>
        !string.IsNullOrEmpty(CamOwnerAddr) && CamOwnerAddr != "0x0" && CamOwnerAddr != "0X0";

    /// <summary>True when the camera Location field can be handed to the GWorld locator.</summary>
    public bool HasCamLocAddr => CamOwnerUsable && CamLocFieldOffset >= 0;

    /// <summary>True when the camera Rotation field can be handed to the GWorld locator.</summary>
    public bool HasCamRotAddr => CamOwnerUsable && CamRotFieldOffset >= 0;

    /// <summary>True when the camera FOV field can be handed to the GWorld locator.</summary>
    public bool HasCamFovAddr => CamOwnerUsable && CamFovFieldOffset >= 0;
}

/// <summary>
/// Maps Wirbel result codes (docs/teleport-spec.md §8) to user-facing hint
/// strings. Kept here (not in the DLL) so the UI owns its own phrasing.
/// </summary>
public static class TeleportCodes
{
    public const int Ok            = 0;
    public const int NotInit       = -1;
    public const int NoController  = -2;
    public const int NoPawn        = -3;
    public const int Reflection    = -4;
    public const int Invoke        = -5;
    public const int EmptyMarker   = -6;
    public const int MapMismatch   = -7;
    public const int NoHit         = -8;
    public const int NoCursor      = -9;
    public const int WriteFailed   = -10;

    public static string Describe(int code) => code switch
    {
        Ok           => "OK",
        NotInit      => "DLL not initialized — Connect & scan first (GWorld not found).",
        NoController => "No local player controller (are you in the main menu?).",
        NoPawn       => "Not possessing a pawn (menu / cutscene / spectator?).",
        Reflection   => "Engine layout unrecognized — please report the game + UE version.",
        Invoke       => "Game thread idle (menu / loading?) — try again during gameplay.",
        EmptyMarker  => "Marker slot is empty.",
        MapMismatch  => "Marker was saved on a different map — use Force to override.",
        NoHit        => "Nothing under the cursor / center within range.",
        NoCursor     => "No cursor available — enable the screen-center fallback.",
        WriteFailed  => "Raw write failed (protected memory?).",
        _            => $"Teleport failed (code {code}).",
    };
}
