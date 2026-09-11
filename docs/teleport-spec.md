# Teleport (BugIt-style) — Technical Specification

> Status: **IMPLEMENTED (build 1027+, branch dev).** This document is the
> design contract; the feature shipped following it. It mirrors the Debug
> Camera force on/off architecture (PR #264, build 1014): **all logic
> DLL-side, exposed as C ABI exports + pipe commands + a Mimic mailbox
> command; the UI and the generated CE Lua are thin, stateless clients.** No
> pure-Lua / baked-offset variant is in scope.
>
> **Implementation notes (where the shipped code differs from / refines the
> spec below):**
> - Module is **`Wirbel`** (`dll/src/Wirbel.cpp/.h`); the shared field-offset
>   helper landed as **`Ubel::FindField` / `Ubel::FindFieldOffset`** (the
>   Debug Camera `DbgCam_FieldOffset` now wraps it).
> - **UFunction lookup walks the class Super chain** (build 1035 fix). The
>   spec assumed reflection would find inherited functions; `Ubel::WalkFunctions`
>   only lists a class's OWN functions, and `K2_SetActorLocation` / `K2_TeleportTo`
>   live on `AActor`, so `Wirbel::FindFunc` now climbs class → Super → … Without
>   this every recall fell to the tier-2 raw write (which CharacterMovement
>   overwrites) → "recall does nothing".
> - **CE export injects via AOBMaker** (build 1035), not clipboard: "Add hotkeys
>   to CE" = `CreateAAScript` tickable record (`GenerateAaRecord`), "Add action
>   records to CE" = 7 momentary `CreateAAScript` records; clipboard/.CT are
>   fallbacks. The §9.2A "paste into CE" clipboard path is the fallback now.
> - **Global cursor hotkey** (build 1035): `WindowsGlobalHotkeyService` /
>   `IGlobalHotkeyService` registers an OS hotkey (Ctrl+F8→F5, then Alt+F8→F5,
>   first free wins) so cursor teleport works with the game focused — the §6.2
>   cursor flow can't be driven from a UI button (focus moves the cursor out of
>   the game).
> - Teleport tab is **appended last** in the TabControl (`MainTabIndex.Teleport`)
>   so existing indices never shift; the hidden experimental tabs collapse, so
>   it renders right after Proxy Deploy when experimental mode is off.
> - **.CT export carries no CE record-level hotkeys** — the Lua bundle is the
>   hotkey path; the .CT records are 7 momentary auto-unticking actions
>   (`CtScriptRow`). §9.2B/§9.3 rule 6 (CE `<Hotkeys>` per record) is deferred.
> - BugItGo explicit-pose recall reuses `teleport_recall_marker` with optional
>   `x/y/z` (+`pitch/yaw/roll`) fields, exactly as §10.3 specifies.

-----

## 1. Goal & Non-Goals

### Goal

Two teleport modes, working on any UE 4.18+ / 5.0–5.8 game the dumper attaches
to, with **zero per-game configuration**:

1. **Generic (marker) mode** — save the player's current position+rotation
   into one of **3 marker slots**, recall to any slot later. BugIt-style.
2. **Pointer (cursor) mode** — teleport the player pawn to the world position
   under the **mouse cursor** (2.5D / 45° view games: Titan Quest II-likes,
   SE HD-2D titles). For games with no visible cursor, fall back to the
   **screen-center** ray.

Deliverables for the user:

- A **Teleport panel** in the UI (works after Connect): live pose display,
  Save/Recall/Clear per slot, Teleport-to-cursor button, BugItGo interop.
- One-click **"Copy CE Lua"** generators producing **self-contained** scripts
  (paste into CE, no `ue5_invoke_helper.lua` dependency) with hotkeys, plus a
  batch **.CT** export via `CheatTableBuilder`.
- **BugIt / BugItGo integration** (see §10) — read pose as a `BugItGo` string,
  run pasted `BugItGo` strings, and explain/handle the "BugIt returns nothing"
  behavior the user observed.

### Known limitations (live-verified)

- **Cooked-out location setters** (Octopath Traveler / SE HD-2D): some builds
  strip `AActor::K2_SetActorLocation` / `K2_TeleportTo` AND the component-level
  `K2_SetWorldLocation` / `K2_SetRelativeLocation` from reflection (only
  StopMovement-class functions survive). Tiered fallback (build 1042-1043):
  actor setter → component setter (`UpdateComponentToWorld`, unlike a raw write)
  → raw `RelativeLocation` write **plus a "deep force"** that scans the
  RootComponent for the cached world-transform vectors (`ComponentToWorld`,
  bounds origin) holding the old position and overwrites them with the target.
  **Octopath: the character now teleports** (deep force rewrote 2 vectors).
  **Caveat — camera/POV may not follow**: on these titles the camera often
  tracks a separate child component / camera actor whose world transform we
  don't refresh, so the character moves but the view may not snap to it. Best-
  effort; the panel carries an in-app disclaimer.


- **Engine setters that report success but don't move (e.g. Titan Quest II):**
  `K2_SetActorLocation` can return `true` yet leave the actor where it was
  (CharacterMovement reverts it). The CMC-freeze retry (build 1041) forces the
  memory position, and **build 1113** makes it also refresh the world transform —
  in the CMC-freeze path it now ALWAYS runs the component setter
  `K2_SetWorldLocation` (which runs `UpdateComponentToWorld` and propagates to the
  child mesh) + `DeepForceWorldPos`, instead of trusting the actor setter's return.
  **TQ2 marker teleport now works.** (TQ2 was previously, *incorrectly*, recorded
  as a "separate visible actor" game — a `ViewTarget` diagnostic disproved that:
  the pawn IS the camera view-target and owns the mesh + CMC; see
  [lessons-learned.md](lessons-learned.md) "TQ2 verdict".) Residual: a minor visual
  lag — the mesh snaps over on the next move (CMC network smoothing; cosmetic).
- **Heavily-stripped builds block *cursor* teleport (e.g. Titan Quest II):** some
  Shipping builds remove `GetMousePosition` (returns `(0,0)` — a custom virtual
  cursor), `GetViewportSize` (no screen-center fallback), and `KismetSystemLibrary`
  / `LineTraceSingle` (no line trace), leaving no generic way to read where the
  cursor points. Build 1114-1116 added robustness — `GetHitResultAtScreenPosition`
  (no `KismetSystemLibrary` needed) + an auto-scan of trace channels — which helps
  other cursor games, but can't overcome a build missing the whole toolkit.
  Marker teleport is unaffected. Per-game RE only; deferred.

### Non-Goals (v1)

- No pure-Lua baked-offset trainer export (explicitly rejected).
- No marker persistence across game restarts (markers live in the DLL;
  optional UI-side persistence is a v2 idea, see §14).
- No multiplayer/server-authoritative support. Online games will rubber-band
  or flag the player; the UI shows a one-line warning, nothing more.
- No path/ground snapping beyond what `K2_TeleportTo` already does.

-----

## 2. Decisions Already Made

| # | Decision |
|---|----------|
| D1 | Architecture = **DLL-side primitives + Mimic mailbox** (Debug Camera pattern). Generated CE scripts are identical for every game; all offsets resolved live by reflection inside the DLL. |
| D2 | New DLL module **`Wirbel.cpp` / `Wirbel.h`** (namespace `Wirbel::`) — Wirbel, poll #20, the pragmatic soldier: swift battlefield repositioning. Register in [naming-convention.md](naming-convention.md). Thin `extern "C"` wrappers live in `Frieren.cpp` like every other export. |
| D3 | Recall uses **`K2_SetActorLocation(bSweep=false, bTeleport=true)`** (exact landing). Cursor mode uses **`K2_TeleportTo`** (collision-adjusted landing). Raw property write is the **fallback tier only**. |
| D4 | Rotation restore via **`AController::SetControlRotation`** invoke; fallback = raw write of the `ControlRotation` property (known-safe; PC re-consumes it every frame). |
| D5 | Markers store **coordinates only** (never pawn/actor pointers) + the map name. The pawn is re-resolved on every operation. |
| D6 | Recall **refuses on map mismatch** (error `-7`); a separate *force* op overrides. |
| D7 | All pose values cross the ABI as **double** regardless of engine width; the DLL converts at the boundary (UE4 float ↔ UE5 double resolved per §5.3). |
| D8 | Mailbox: **one new command `CMD_TELEPORT = 8`** with an op code, mirroring `CMD_SET_DEBUG_CAMERA`'s "request in `instanceAddr`" precedent. |
| D9 | We never invoke `UCheatManager::BugIt` to *read* pose. Pose reads are our own reflection walk. BugItGo interop is formatting/parsing on top of our primitives (§10). |
| D10 | Per CLAUDE.md: all new magic numbers/strings go in `Grimoire.h` (DLL) / the single constants file (UI); all UI strings in `en.axaml`; everything async UI-side; AOT-safe (source-gen JSON). |
| D11 | Hotkeys: **CE distinguishes top-row digits from numpad digits** (`Ctrl+1` = VK 0x31 ≠ `Ctrl+Num1` = VK 0x61). The generators offer a **3-way scheme selector — Numpad (default) / Top-row / Both** (§9.3). Default is Numpad because ARPGs (e.g. Titan Quest-likes) already bind `Ctrl+1..3` for skills. If a user's game conflicts with both schemes, **the user re-binds inside CE themselves** — no in-app conflict detection. |

-----

## 3. Existing Infrastructure Reused

| Piece | Where | Used for |
|---|---|---|
| GWorld discovery | `Genau` → cached in `Frieren.cpp` (`g_cachedGWorld`) | Root of the resolution chain. **Note:** check existing deref semantics (the cached value is the address of the `GWorld` global → deref once for `UWorld*`), same as the Pointer panel path. |
| Reflection field-offset resolver | `DbgCam_FieldOffset` / `DbgCam_ReadPtr` / `DbgCam_WritePtr` in `Frieren.cpp:537` | **Extract** into a shared helper (suggested: `Ubel::FindFieldOffset(classAddr, exact, contains, excluding)` + read/write ptr helpers) with identical semantics. Debug Camera call sites switch to the shared helper — behavior-preserving refactor, covered by existing tests/logs. |
| Class walk incl. inherited fields | `Ubel::WalkClassEx` | Field offsets by name on engine base classes. UFunction param walking too (a `UFunction` is a `UStruct`; its children are the param properties). |
| Function lookup | `UE5_FindFunctionByName(classAddr, name)` | Find `K2_SetActorLocation`, `K2_TeleportTo`, etc. |
| Game-thread invoke | `UE5_CallProcessEvent(instance, ufunc, paramsPtr)` (Stark queue) | ALL invokes in this feature. **Never** use the static-native direct path (`UE5_CallProcessEventDirect`) for traces or actor mutation — traces read the physics scene, actor moves mutate world state; both must run on the game thread. |
| Instance finder | `Aura::FindInstancesByClass` | Fallback PlayerController resolution; CheatManager detection (Tier 0). |
| Object name reader | existing Frieren export used by the UI/CE for object names (via `Serie`) | Map-name capture for markers. |
| Mailbox | `Mimic.cpp/.h`, `g_invokeMailbox` | CE Lua entry point. New `CMD_TELEPORT=8` handler delegates to the same exports the pipe uses. |
| Pipe | `Fern.cpp` + `Renge.h` | 6 new request/response commands (§7). |
| Script generation precedent | `DebugCameraScriptGenerator.cs` (+ its tests) | Template for the self-contained mailbox AA scripts (§9). |
| Batch CT | `CheatTableBuilder` (build 760) | "Save .CT" export with hotkeys. |
| Debug camera state | `DbgCam_ReadState` | Detect "player is currently possessed by DebugCameraController" (§5.6 caveat). |

-----

## 4. Tier Model (execution strategy per operation)

| Tier | Mechanism | When used |
|---|---|---|
| **Tier 1 (primary)** | Invoke engine `BlueprintCallable` UFunctions via ProcessEvent on the game thread (Stark) | Recall, cursor teleport, rotation set, traces, velocity reset. |
| **Tier 2 (fallback)** | Raw SEH property write (`Macht::WriteBytes`) of `RootComponent.RelativeLocation` / `Controller.ControlRotation` | Only when Tier 1 fails (UFunction missing — stripped BP metadata — or invoke timeout). Result code distinguishes the tier so the UI can show "fallback used; game may snap back". |
| **Tier 0 (opportunistic, separate button)** | Real `UCheatManager::BugItGo` invoke when a live CheatManager instance exists | Optional convenience only (§10.4). Never the primary path — Shipping builds usually have no CheatManager. |

Pose **reads** are raw (fast, no game-thread dependency) with an invoke
fallback for the attached-pawn case (§5.4).

-----

## 5. The Resolution Chain (heart of the feature)

All property names below are declared on **engine base classes** and are
stable across UE 4.18 → 5.8 and across games (game subclasses inherit them,
and `Ubel::WalkClassEx` walks inherited fields). Resolution is by name via
the shared field-offset helper (§3). **Nothing is hardcoded** (CLAUDE.md rule).

### 5.1 Local PlayerController

```
UWorld* world = deref(g_cachedGWorld)
  → UWorld.OwningGameInstance      (ObjectProperty; fallback name "GameInstance")
  → UGameInstance.LocalPlayers     (ArrayProperty<ULocalPlayer*>; read TArray
                                    {data,num}; take element [0]; num==0 → error -2)
  → UPlayer.PlayerController       (ObjectProperty — same field DbgCam_SwapControllerBack
                                    already resolves on the LocalPlayer class)
```

**Fallback** (any hop unresolvable): `Aura::FindInstancesByClass` with
contains-match `"PlayerController"`, skip CDOs, prefer the instance whose
`Player` (ObjectProperty on AController) is non-null — that's the local one.
Log which path won (`source: "chain" | "scan"`).

### 5.2 Pawn, Root, Pose

```
AController.Pawn                   (ObjectProperty; fallback "AcknowledgedPawn"
                                    on APlayerController)
AActor.RootComponent               (ObjectProperty → USceneComponent)
USceneComponent.RelativeLocation   (StructProperty FVector  → X/Y/Z)
USceneComponent.AttachParent       (ObjectProperty — §5.4 gate)
AController.ControlRotation        (StructProperty FRotator → Pitch/Yaw/Roll)
```

Null pawn (menu, cutscene, spectating) → error `-3`, never a partial result.

### 5.3 LWC (float vs double) detection

UE5.0+ `FVector`/`FRotator` are 3×double (24 bytes); UE4 are 3×float (12).
Detect **once per process** from the resolved `RelativeLocation` property:
use the property's reported size if Ubel exposes it, else the size of the
`FVector` ScriptStruct. Cache the result (`bool s_engineUsesDouble`). Apply
the same width to FRotator, FHitResult members, and all invoke param packing.
**Never key this off a version number.**

### 5.4 Pose read (GET_POSE)

1. Resolve chain (§5.1–5.2).
2. If `AttachParent == null` (the normal case — possessed pawns are top-level
   actors): raw-read `RelativeLocation` (== world location) + `ControlRotation`.
   `source = raw`.
3. If `AttachParent != null` (vehicle / moving platform): `RelativeLocation`
   is parent-relative — **invoke `AActor::K2_GetActorLocation`** (returns
   world-space FVector) instead. `source = invoke`. If the invoke fails
   (game-thread idle), return the raw values anyway with `source = raw` and a
   warning flag — better an approximate display than an error.
4. Map name: object name of the dereferenced `UWorld` (via the existing
   object-name path). Stored with markers, compared case-insensitively on
   recall.

The 1-shot raw read of 12/24 bytes can tear against the game thread
mid-write; for a display read this is cosmetic and acceptable. Do **not**
"fix" it with suspension.

### 5.5 Teleport write (RECALL / CURSOR)

Order of operations (all Tier 1 invokes through Stark; each step independently
falls back or degrades):

1. **Location** —
   - RECALL: `AActor::K2_SetActorLocation(NewLocation, bSweep=false,
     SweepHitResult, bTeleport=true)` on the pawn. `bTeleport=true` ⇒
     `ETeleportType::TeleportPhysics` (physics body moved, transform cache
     updated, overlaps fired correctly). Zero-init the whole parms buffer —
     a zeroed out-`FHitResult` is valid input.
   - CURSOR: `AActor::K2_TeleportTo(DestLocation, DestRotation)` — runs
     `FindTeleportSpot` so the pawn lands beside/atop geometry instead of
     inside it. `DestRotation` = current ControlRotation (keep facing).
   - Tier 2 fallback: raw-write `RelativeLocation` (engine-width respected).
     Return code marks the degradation (see §8 result codes / `tier` field).
2. **Rotation** (RECALL only) — invoke `AController::SetControlRotation`
   (BlueprintCallable on AController); fallback raw-write `ControlRotation`.
3. **Velocity reset** (RECALL + CURSOR, best-effort, never fails the op) —
   resolve `ACharacter.CharacterMovement` (ObjectProperty named
   `CharacterMovement`); if present, invoke
   `UCharacterMovementComponent::StopMovementImmediately()` (no params).
   Non-Character pawns simply skip this. Prevents conserved fall velocity
   from killing the player right after arrival.

**Param packing**: never assume a layout. Walk the target `UFunction`'s param
properties via Ubel (offsets + types + `ParmsSize`), then:
- FVector/FRotator members → width per §5.3.
- `BoolProperty` params in parm structs are byte-aligned in practice; write a
  single byte at the property offset (same approach the existing invoke
  machinery uses).
- Allocate/zero a buffer of exactly `UFunction::ParmsSize`.

### 5.6 Caveat: Debug Camera active

When the Debug Camera feature (this project's own) is ON, the LocalPlayer's
`PlayerController` **is the DebugCameraController** — its `Pawn` is null or a
spectator. Before erroring with `-3`: if the resolved PC's class name contains
`"DebugCameraController"`, follow `OriginalControllerRef` (reuse
`DbgCam_ReadState` plumbing) to the real PC and continue. This makes
"fly somewhere with debug camera → save marker" work naturally.

-----

## 6. DLL Design

### 6.1 New module `Wirbel` (`dll/src/Wirbel.cpp` / `Wirbel.h`)

```cpp
// Wirbel — 維爾貝爾 (北部魔法隊小隊長 — pragmatic soldier, swift repositioning)
// Teleport: marker save/recall + cursor teleport. All offsets reflected live.
namespace Wirbel {

struct Pose {            // always doubles at this layer (D7)
    double X, Y, Z;
    double Pitch, Yaw, Roll;
};

struct Marker {
    bool  valid = false;
    Pose  pose{};
    char  mapName[128] = {};
};

// --- public API (called by Frieren exports, Fern pipe, Mimic mailbox) ---
int32_t GetPose(Pose& out, char* mapName, int32_t mapCap, uint8_t& outSource);
int32_t SaveMarker(int32_t slot);                       // GetPose → store
int32_t RecallMarker(int32_t slot, bool force);         // map check unless force
int32_t TeleportToCursor(double zOffset, int32_t traceChannel,
                         bool fallbackToCenter);
int32_t GetMarker(int32_t slot, Marker& out);
int32_t ClearMarker(int32_t slot);
}
```

- Marker array: `static Marker s_markers[kTeleportSlots]` guarded by a
  `std::mutex` — handlers are reachable from **both** the Fern pipe thread and
  the Mimic polling thread concurrently.
- Resolved offsets (chain hops, UFunction addresses) are **cached after first
  success** but every cached UObject pointer (PC, pawn) is **re-validated /
  re-read per call** (D5). Cache offsets, never instances. Invalidate the
  offset cache if `UE5_Init` re-runs.
- All constants → `Grimoire.h`: `kTeleportSlots = 3`,
  `kTeleportDefaultZOffset = 100.0`, `kTeleportTraceDist = 100000.0`,
  `kTeleportMapNameCap = 128`, error codes enum, mailbox op codes.
- Logging: `LOG_CAT "WALK"` or a clearly-prefixed `Teleport:` tag in the
  existing categories (do **not** add a 6th log file — CLAUDE.md fixes the
  DLL at 5).

### 6.2 Cursor flow (TeleportToCursor)

```
1. Resolve PC (§5.1).
2. Mouse position:
   a. invoke APlayerController::GetMousePosition(float& X, float& Y) → bool.
      (Screen-space floats in BOTH UE4 and UE5 — do not LWC-widen; but still
      pack via reflection, per the universal rule.)
   b. ReturnValue false or invoke fails:
      - fallbackToCenter == false → error -9.
      - else invoke GetViewportSize(int32& SizeX, int32& SizeY); use center.
3. Hit test, first of these that succeeds:
   a. APlayerController::GetHitResultUnderCursorByChannel(
        ETraceTypeQuery TraceChannel, bool bTraceComplex=true,
        FHitResult& HitResult) → bool
      — only meaningful when the game actually drives a cursor; a false
      return falls through to (b).
   b. APlayerController::DeprojectScreenPositionToWorld(
        ScreenX, ScreenY, FVector& WorldLocation, FVector& WorldDirection) → bool
      then UKismetSystemLibrary::LineTraceSingle(
        WorldContextObject=pawn, Start=WorldLocation,
        End=WorldLocation + WorldDirection * kTeleportTraceDist,
        TraceChannel, bTraceComplex=true, ActorsToIgnore={} (zeroed TArray),
        DrawDebugType=None(0), FHitResult& OutHit, bIgnoreSelf=true,
        colors zeroed, DrawTime 0).
      ProcessEvent instance for the static library call = the
      KismetSystemLibrary CDO (Default__KismetSystemLibrary).
      ⚠ MUST go through Stark (game thread) even though the function is
      static-native — it reads the physics scene. Do not reuse Mimic's
      static-native direct shortcut here.
4. No blocking hit from both → error -8.
5. Read FHitResult.ImpactPoint — resolve member offsets on the FHitResult
   ScriptStruct via reflection (note: leading bBlockingHit/bStartPenetrating
   are bitfields; never assume member offsets; LWC width per §5.3).
6. dest = ImpactPoint + (0, 0, zOffset). Teleport per §5.5 (K2_TeleportTo,
   keep current ControlRotation). Velocity reset.
```

`traceChannel` is the **ETraceTypeQuery byte value** (default `0` =
TraceTypeQuery1, the stock Visibility mapping). Games can remap channels —
that's why it's a parameter surfaced in the UI, not a constant.

### 6.3 New C ABI exports (Frieren.cpp wrappers) — 30 → 38

```c
// All return Wirbel error codes (§8). Pose arrays are X,Y,Z,Pitch,Yaw,Roll.
int32_t UE5_TeleportGetPose(double outPose[6], char* outMapName, int32_t mapNameCap);
int32_t UE5_TeleportSaveMarker(int32_t slot);                       // slot 0..2
int32_t UE5_TeleportRecallMarker(int32_t slot, int32_t force);      // force: 0/1
int32_t UE5_TeleportToCursor(double zOffset, int32_t traceChannel,
                             int32_t fallbackToCenter);
int32_t UE5_TeleportGetMarker(int32_t slot, double outPose[6],
                              char* outMapName, int32_t mapNameCap); // -6 if empty
int32_t UE5_TeleportClearMarker(int32_t slot);
int32_t UE5_TeleportRecallLast();                                   // one-way undo
int32_t UE5_TeleportGetLast(double outPose[6],
                            char* outMapName, int32_t mapNameCap);   // -6 if empty
```

Reminder (proven by Debug Camera): **CE Lua's `executeCodeEx` cannot retrieve
these return values** — the CE path is the mailbox only. Exports exist for the
pipe/UI and for symmetry/debugging.

-----

## 7. Pipe Protocol (Fern/Renge) — 7 new commands, ~42 → ~49

Request/response only, no events. JSON shapes (`MakeResponse` envelope as
usual; pose numbers are JSON doubles):

```jsonc
// teleport_get_pose
{ "cmd": "teleport_get_pose" }
→ { "x":…, "y":…, "z":…, "pitch":…, "yaw":…, "roll":…,
    "map":"Map_Act1", "source":"raw|invoke", "code":0 }

// teleport_save_marker
{ "cmd": "teleport_save_marker", "slot": 0 }
→ { "slot":0, "x":…, …, "map":"…", "code":0 }

// teleport_recall_marker
{ "cmd": "teleport_recall_marker", "slot": 0, "force": false }
→ { "code":0, "tier":1 }            // tier: 1=invoke, 2=raw-write fallback
→ { "code":-7, "map":"Map_Act2", "markerMap":"Map_Act1" }   // mismatch detail

// teleport_to_cursor
{ "cmd": "teleport_to_cursor", "zOffset": 100.0, "channel": 0,
  "fallbackCenter": true }
→ { "code":0, "tier":1, "hitX":…, "hitY":…, "hitZ":…,
    "usedCenter": false }

// teleport_get_markers — the markers array also carries the system "last"
// slot as a sentinel entry with slot == -1 (auto-saved pre-jump pose), so the
// UI refreshes it in the same round trip.
{ "cmd": "teleport_get_markers" }
→ { "markers": [ { "slot":0, "valid":true, "x":…, …, "map":"…" },
                 { "slot":1, "valid":false }, …,
                 { "slot":-1, "valid":true, "x":…, …, "map":"…" } ] }

// teleport_clear_marker
{ "cmd": "teleport_clear_marker", "slot": 0 } → { "code":0 }

// teleport_recall_last — one-way undo of the most recent jump (recall the
// system "last" pose). EmptyMarker (-6) when nothing has been auto-saved yet.
{ "cmd": "teleport_recall_last" } → { "code":0, "tier":1 }
```

Non-zero `code` still returns a normal response (not a pipe error) so the UI
can render the specific failure; reserve `MakeError` for malformed requests
(bad slot index type, etc.).

Add `Renge::CMD_TELEPORT_*` string constants; update
[pipe-protocol.md](pipe-protocol.md).

-----

## 8. Mailbox Protocol (Mimic) — `CMD_TELEPORT = 8`

Field usage (mirrors the `CMD_SET_DEBUG_CAMERA` precedent — request rides in
the pointer-sized fields):

| Field | Direction | Meaning |
|---|---|---|
| `cmd` (0x00) | CE→DLL, **written LAST** | `8` |
| `status` (0x04) | DLL→CE | poll until `1` (STATUS_DONE) |
| `result` (0x08) | DLL→CE | error code (below) |
| `instanceAddr` (0x10) | CE→DLL | **op code** (below) |
| `ufuncAddr` (0x18) | CE→DLL | **slot** (0..2) for SAVE/RECALL/GET/CLEAR ops |
| `errorMsg` (0x228) | DLL→CE | human-readable failure |
| `paramsData` (0x328) | both | op-specific, below |

Op codes (`Grimoire.h` + mirrored in the C# constants file):

| Op | Name | paramsData IN | paramsData OUT |
|---|---|---|---|
| 0 | GET_POSE | — | pose block |
| 1 | SAVE | — | pose block (the saved pose) |
| 2 | RECALL | — | — |
| 3 | RECALL_FORCE | — | — |
| 4 | CURSOR | `[0..7]` double zOffset, `[8]` u8 traceChannel, `[9]` u8 fallbackToCenter | `[0..23]` 3×double hit point |
| 5 | GET_MARKER | — | pose block (`result=-6` if slot empty) |
| 6 | CLEAR_MARKER | — | — |
| 7 | RECALL_LAST | — (slot ignored) | `[177]` tier (recall the system auto-saved "last" pose) |
| 8 | GET_LAST | — | pose block (`result=-6` if nothing auto-saved yet) |
| 9 | BUGIT_SAVE | — (slot ignored) | pose block (stores the pose in the BugIt slot) |
| 10 | BUGIT_GO | — | `[177]` tier (`result=-6` no-op if no BugIt stored yet) |

The **BugIt slot** (op 9/10) is a single user-triggered pose (UE BugIt/BugItGo
semantics) the DLL holds between hotkey presses: BUGIT_SAVE stores the current
pose (and returns it so the Lua can also clipboard a `BugItGo X Y Z` string),
BUGIT_GO teleports to it (restores rotation; auto-saves "last" first like every
jump; no-op when nothing stored). Mailbox-only (CE Lua path) — the UI's own
BugItGo flow stays textbox-based via `teleport_recall_marker`, so there is no
pipe command / C export for this slot.

The **"last" slot** (op 7/8) is a system-managed extra marker the DLL writes
automatically right before *every* jump (RECALL / RECALL_FORCE / CURSOR and the
pipe BugItGo explicit-pose path), so a teleport that lands the pawn somewhere
bad can be undone. RECALL_LAST is a **one-way restore** — it never overwrites the
slot, so repeated calls always return to the same pre-teleport spot. The user
can never save into it (no SAVE op for "last"); the map check is skipped (the
pose is always from moments ago on the current map).

Pose block layout in `paramsData` (offsets relative to 0x328):

```
[0..7]    double X          [24..31] double Pitch
[8..15]   double Y          [32..39] double Yaw
[16..23]  double Z          [40..47] double Roll
[48..175] char  mapName[128] (null-terminated)
[176]     u8    source (0=raw, 1=invoke)         // GET_POSE only
[177]     u8    tier   (1=invoke, 2=raw fallback) // teleport ops
[178]     u8    flags  (bit0 parent-relative: not world coordinates;
                        bit1 landing unknown: X..Roll are NaN)   // contract 4, every pose op
```

⚠ Op 4 (CURSOR) reads its inputs **before** writing outputs into the same
buffer.

Handler `HandleTeleport()` in `Mimic.cpp` is a thin switch that delegates to
the `UE5_Teleport*` exports (identical to `HandleSetDebugCamera`'s style),
then copies results into `paramsData`. Document the new command in
`Mimic.h`'s `Cmd` enum comment block.

### Error / result codes (shared across exports, pipe, mailbox)

| Code | Meaning | UI message hint |
|---|---|---|
| 0 | OK | — |
| -1 | DLL not initialized / GWorld unavailable | "Connect & init first / GWorld not found" |
| -2 | Local PlayerController not resolved | "No local player (main menu?)" |
| -3 | Pawn is null | "Not possessing a pawn (menu/cutscene/spectator?)" |
| -4 | Required property/UFunction not resolved by reflection | "Engine layout unrecognized — please report game+version" |
| -5 | Invoke failed or timed out | "Game thread idle (menu/loading?) — try during gameplay" |
| -6 | Marker slot empty / slot index out of range | "Marker N not set" |
| -7 | Map mismatch on recall | "Marker was saved on '<map>' — Force recall to override" |
| -8 | Trace found no blocking hit | "Nothing under cursor/center within range" |
| -9 | Mouse position unavailable and center fallback disabled | "No cursor — enable center fallback" |
| -10 | Tier 2 raw-write fallback also failed | "Write failed (protected memory?)" |

-----

## 9. UI Design

### 9.1 TeleportPanel (new panel + ViewModel)

New `TeleportPanel.axaml` + `TeleportViewModel` (ObservableProperty
source-gen, AOT-safe), tab placed next to Console. All actions async via
`IDumpService`; everything disabled until pipe-connected. Strings in
`en.axaml` via `StaticResource` (English only).

Layout (top → bottom):

1. **Current pose** — read-only X/Y/Z/Pitch/Yaw/Roll + map name + `source`
   chip; **Refresh** button; optional **Auto** toggle (500 ms polling timer —
   default OFF; stop on disconnect/tab-leave like other polling panels).
2. **Markers** — 3 rows: `[Save] [Recall] [Force] [Clear]` + stored
   coords/map (or "empty"). On connect, populate via `teleport_get_markers`
   (markers survive UI restarts as long as the game process lives).
3. **Cursor teleport** — `[Teleport to cursor]` button; numeric **Z offset**
   (default from constants, 100); **Channel** ComboBox (`Visibility (0)`,
   `Camera (1)`, plus a raw 0–32 numeric entry for remapped games);
   **Fall back to screen center** CheckBox (default ON).
4. **BugItGo interop** (§10) — `[Copy as BugItGo]`; a TextBox + `[Run]` that
   accepts pasted `BugItGo X Y Z` (and full BugIt `?BugLoc=…?BugRot=…`)
   strings; optional `[BugItGo via CheatManager]` button enabled only when a
   live CheatManager was detected (reuse the Console panel's detection).
5. **CE export** — **Hotkey scheme** ComboBox (`Numpad (default)` /
   `Top-row` / `Both`, see §9.3), `[Copy CE Lua (hotkey bundle)]`,
   `[Copy CE record: <action>]` per-action dropdown+copy, `[Save .CT…]`
   (CheatTableBuilder).
6. **Warning line** — static text: "Single-player only. Online games will
   rubber-band or may flag teleports."

`IDumpService` additions (+ `StubDumpService` for tests):
`TeleportGetPoseAsync`, `TeleportSaveMarkerAsync(int)`,
`TeleportRecallMarkerAsync(int, bool force)`,
`TeleportToCursorAsync(double, int, bool)`, `TeleportGetMarkersAsync`,
`TeleportClearMarkerAsync(int)`. New DTOs registered in the
`[JsonSerializable]` context (AOT rule).

Error codes map to the §8 message hints; `-7` surfaces a dialog offering
Force recall.

### 9.2 Generated CE artifacts

> **⚠ Historical — §9.2A / §9.3 no longer reflect the code.** The
> `TeleportLuaBundleGenerator` "hotkey bundle" (and its `TeleportHotkeyScheme`
> selector) was **REMOVED in build 1111** and its "Copy CE Lua" button was gone
> since **build 1038**: the `createHotkey` registration never reliably fired in
> CE (an earlier cut also relied on `executeCodeEx`, which can't return export
> values). The surviving CE path is **§9.2B only** — `TeleportScriptGenerator`'s
> per-action **mailbox AA records** (ship via AOBMaker / `.CT`); hotkeys come
> from CE **record-level** bindings or the app's own OS-level global hotkeys
> (Teleport tab). The §9.3 numpad/top-row/both scheme table is obsolete. The
> rest of §9.2A below is kept for historical context.

Both generators are pure static classes (mirroring
`DebugCameraScriptGenerator`), fully unit-testable, LF-only line endings.

**A. `TeleportLuaBundleGenerator`** ~~(primary deliverable — "塞到CE" path)~~
**[REMOVED build 1111 — see the note above].**
One self-contained Lua script the user pastes into CE (Table Lua Script or
Lua Engine window). Contents:

```lua
-- UE5CEDumper Teleport bundle -- generated, self-contained (mailbox only)
-- Requires UE5Dumper.dll injected.
-- Hotkeys (scheme-dependent, see §9.3 -- numpad scheme shown; the generator
-- emits the actual bindings of the chosen scheme in BOTH notations here):
--   Ctrl+Num1..3 = save marker 1..3      Num1..3 = recall marker 1..3
--   Num0 = teleport to cursor            Ctrl+Num0 = copy BugItGo string
-- NumLock must be ON for numpad bindings. If a key clashes with your game,
-- edit the createHotkey lines below (VK codes commented) or rebind in CE.
local function mb()
  local a = getAddressSafe('g_invokeMailbox')
  if not a or a == 0 then a = getAddressSafe('UE5Dumper.g_invokeMailbox') end
  return a
end
local function tp(op, slot, zofs, chan, center)
  local m = mb()
  if not m then print('[Teleport] mailbox not found - DLL injected?') return nil end
  if readInteger(m) ~= 0 then print('[Teleport] busy, skipped') return nil end
  if op == 4 then
    writeDouble(m + 0x328, zofs or 100.0)
    writeBytes(m + 0x330, chan or 0)
    writeBytes(m + 0x331, (center == false) and 0 or 1)
  end
  writeQword(m + 0x18, slot or 0)
  writeQword(m + 0x10, op)
  writeInteger(m + 0x04, 0)
  writeInteger(m + 0x00, 8)          -- CMD_TELEPORT (write LAST)
  local e = 0
  while readInteger(m + 0x04) ~= 1 do
    sleep(1); e = e + 1
    if e >= 10000 then print('[Teleport] mailbox timeout') return nil end
  end
  local r = readInteger(m + 0x08)
  if r ~= 0 then print('[Teleport] op=' .. op .. ' code=' .. r .. ' ' ..
                       readString(m + 0x228, 256)) end
  return r, m
end
-- per-action wrappers + createHotkey bindings + copy-BugItGo:
--   save(i)   → tp(1, i-1)
--   recall(i) → tp(2, i-1); on code -7 print the map-mismatch hint
--   cursor()  → tp(4, 0, <zOffset from UI>, <channel>, <center>)
--   copyBugItGo() → tp(0,0) then readDouble x/y/z from paramsData and
--                   writeToClipboard(string.format('BugItGo %.3f %.3f %.3f', x,y,z))
```

Hotkey bindings follow the **scheme selector** in §9.3 (decision D11). The
generator parameterizes zOffset / channel / center-fallback from the panel's
current values.

⚠ Implementer: per CLAUDE.md, verify each CE Lua API against celua.txt before
use (`getAddressSafe`, `createHotkey`, `writeToClipboard`, `readString`,
`writeBytes`, `writeDouble`, `readDouble` are all stock CE APIs; do not invent
others). Re-running the bundle must not double-register hotkeys — destroy
previously created hotkeys via stored globals
(`if _ue5tpHotkeys then for _,h in ipairs(_ue5tpHotkeys) do h.destroy() end end`).

**B. `TeleportScriptGenerator`** (per-action AA memory records, for users who
prefer .CT records / `CheatTableBuilder` batch export). One record per
action. Since every action is **momentary** (not a stateful toggle like Debug
Camera), records auto-deactivate:

- `[ENABLE]` `{$lua}` block: same mailbox round-trip as the bundle, then a
  deferred self-untick to avoid reentrancy:
  `local t=createTimer(nil); t.Interval=50; t.OnTimer=function(s) s.destroy() if memrec then memrec.Active=false end end`
- `[DISABLE]` block: nop (`{$lua}` + comment), so the auto-untick is silent.
- "Save .CT" emits: 1 header group + 7 records (Save 1-3, Recall 1-3, Cursor)
  with the suggested hotkeys attached as record hotkeys.

Both generators embed the op-code/offset table as comments so a user can
audit the script (same courtesy as the Debug Camera script).

### 9.3 Hotkey schemes (decision D11)

CE treats top-row digit keys and numpad digit keys as **different virtual-key
codes**: `1` (top row) = `0x31`, `Numpad 1` = `0x61` (`VK_NUMPAD1`). A
`Ctrl+1` binding will NOT fire on `Ctrl+Numpad1` and vice versa. Many target
games (Titan Quest-style ARPGs, MMO-likes) already consume `Ctrl+1..3` for
skill/loadout bindings — hence the numpad default — while TKL/laptop
keyboards have no numpad — hence the top-row option.

The UI exposes a **Hotkey scheme** ComboBox (in the CE-export group, §9.1
item 5), applied to BOTH generators (Lua bundle `createHotkey` calls AND the
.CT record `<Hotkeys>` entries):

| Action | **Numpad** (default) | **Top-row** | **Both** |
|---|---|---|---|
| Save marker 1/2/3 | `Ctrl+Num1/2/3` (0x11+0x61/0x62/0x63) | `Ctrl+1/2/3` (0x11+0x31/0x32/0x33) | both bindings registered |
| Recall marker 1/2/3 | `Num1/2/3` (0x61/0x62/0x63) | `Alt+1/2/3` (0x12+0x31/0x32/0x33) — bare top-row `1..3` would collide with virtually every game's skill bar, so the top-row scheme adds Alt | both |
| Teleport to cursor | `Num0` (0x60) | `Alt+0` (0x12+0x30) | both |
| Copy BugItGo string | `Ctrl+Num0` (0x11+0x60) | `Ctrl+0` (0x11+0x30) | both |

Implementation rules:

1. **Emit numeric VK literals with a trailing comment** (e.g.
   `createHotkey(save1, 0x11, 0x61) -- Ctrl+Numpad1`). Do NOT rely on CE's
   named constants for top-row digits — CE's `defines.lua` provides
   `VK_NUMPAD0..9` / `VK_CONTROL` / `VK_MENU` but has no `VK_1`-style names
   for 0x30–0x39. Literals + comments work for every key and keep the
   generated script self-contained (and trivially auditable/editable).
2. **"Both" registers two `createHotkey` calls per action** invoking the same
   wrapper function; the re-run guard (`_ue5tpHotkeys` destroy list, §9.2A)
   must track every handle regardless of scheme.
3. The generated bundle's header comment lists the active bindings **in both
   notations** (`Ctrl+Num1` / `Ctrl+1`) so the user can see at a glance which
   physical keys are live.
4. **Conflict policy**: none in-app. The header comment states: "If a hotkey
   clashes with your game, edit the `createHotkey` lines below (VK codes are
   commented) or change the record hotkeys in CE — regenerating with another
   scheme also works." That sentence (or its string resource) is part of the
   generator output and covered by the generator unit tests.
5. **NumLock caveat** (goes in the same header comment): numpad digits send
   `VK_NUMPAD*` only while **NumLock is ON**; with NumLock off they send
   navigation keys (`VK_END` etc.) and the numpad-scheme hotkeys will appear
   dead. Top-row / Both schemes are the workaround for users who play with
   NumLock off.
6. For the .CT path, `CheatTableBuilder` writes the same VK code sets into
   each record's `<Hotkeys><Hotkey><Keys>` elements; scheme "Both" emits two
   `<Hotkey>` entries per record (CE supports multiple hotkeys per record).

-----

## 10. BugIt / BugItGo Integration

### 10.1 What they actually are (engine facts)

- **`UCheatManager::BugIt(FString ScreenShotDescription)`** — exec. *Records*
  the current location: reads the view/pawn location+rotation, builds two
  strings — `BugItGo X Y Z` and `?BugLoc=(X=…,Y=…,Z=…)?BugRot=(P=…,Y=…,R=…)`
  — takes a screenshot, writes the strings **to the log** (`LogCheatManager`)
  and **to the OS clipboard** (desktop platforms). **Returns void. No out
  params.**
- **`UCheatManager::BugItGo(float X, float Y, float Z)`** — exec. *Teleports*
  the local pawn to X/Y/Z. Param widths must still be confirmed via
  reflection at invoke time (D7 rule), even though they are floats in every
  engine version we know.

### 10.2 Why the user's UI invoke of BugIt "returned nothing"

Expected behavior, not a failure:

1. The function has **no ReturnValue and no out params** → the ProcessEvent
   parms buffer comes back unchanged → `StructReturnDecoder` correctly shows
   nothing.
2. Its outputs are **side effects inside the game process**: a log line
   (compiled out in most Shipping builds — `NO_LOGGING`), a screenshot (often
   silently failing in Shipping), and a clipboard write (may actually work —
   worth telling users to check Ctrl+V after a BugIt invoke).
3. Additionally it only exists where a live `UCheatManager` instance exists.

**Document this in the panel** (tooltip on the BugItGo interop group): "BugIt
prints/copies its result inside the game — it cannot return data to the
dumper. Use 'Copy as BugItGo' instead, which reads the pose directly."

### 10.3 Our equivalents (universal, Shipping-safe)

| BugIt concept | Our implementation |
|---|---|
| `BugIt` (record) | `UE5_TeleportGetPose` → UI/CE formats `BugItGo %.3f %.3f %.3f` (+ optionally the full `?BugLoc=…?BugRot=…` form) and copies to clipboard. |
| `BugItGo` (go) | Parse a pasted string → feed X/Y/Z into the **Tier 1 recall primitive** (`K2_SetActorLocation`, keep current rotation unless a `BugRot` was supplied → then also `SetControlRotation`). Works with zero CheatManager. |

Parser accepts, case-insensitively, with comma or space separators:
`BugItGo 123.4 -56.7 890`, bare `x y z`, and the full BugIt clipboard format
`…?BugLoc=(X=123.4,Y=-56.7,Z=890.0)?BugRot=(P=-30.0,Y=90.0,R=0.0)`.
Implement UI-side (`BugItGoParser`, pure static, unit-tested); send the
parsed numbers through a recall-to-explicit-coords path — add an optional
explicit-pose variant to the pipe `teleport_recall_marker`? **No** — keep the
surface small: reuse `teleport_to_cursor`'s plumbing? **No.** Cleanest:
extend `teleport_recall_marker` with optional `"x"/"y"/"z"/"pitch"/"yaw"/
"roll"` fields that, when present, bypass the marker store and map check
(DLL: `Wirbel::RecallExplicit(Pose, bool hasRot)`). Mailbox does **not** need
this op (CE users have markers); pipe-only.

### 10.4 Tier 0: real CheatManager passthrough (optional convenience)

When the Console panel's existing CheatManager detection reports a live
instance, enable a `[BugItGo via CheatManager]` button that invokes the real
exec (params packed via reflection). Purpose: parity-testing our primitive
against the engine's own, and games where the engine teleport does extra
game-specific work. Hidden/disabled otherwise. Never used by generated CE
scripts.

-----

## 11. Caveats & Pitfalls (MUST be respected by the implementation)

1. **Game thread**: every invoke (teleport, trace, rotation, velocity) goes
   through Stark's ProcessEvent queue. The queue only drains while the game
   fires ProcessEvent — in menus/loading it stalls → invoke timeout → error
   `-5` with the "try during gameplay" hint. Never block the pipe thread
   beyond the existing invoke timeout.
2. **Never the static-native shortcut for traces** (§6.2). Mimic's
   `CMD_INVOKE` fast path is for pure math libraries; `LineTraceSingle` reads
   world state despite being static-native.
3. **LWC width** resolved from reflection, cached per-process, applied
   everywhere (§5.3). FHitResult member offsets resolved per-struct — UE4 vs
   UE5 differ in both layout and width, and it starts with bitfields.
4. **No cached instance pointers** (D5). Pawn/PC re-resolved per op; markers
   are pure coordinates. This is the classic stale-pointer crash class from
   the 2026-06-10 audit — don't reintroduce it.
5. **Map mismatch** (D6): compare stored vs current world name
   (case-insensitive) before recall; `-7` unless force. Cross-map recall =
   void fall / unloaded cell.
6. **Attached pawns** (§5.4): RelativeLocation is parent-relative when
   `AttachParent != null` — pose reads must branch; teleporting an attached
   pawn (in a vehicle) may be rejected by the game; report whatever the
   invoke returns honestly.
7. **Debug Camera interaction** (§5.6): resolve through
   `OriginalControllerRef` when the active PC is the DebugCameraController.
8. **Mailbox is single-slot**: generated Lua checks `cmd == 0` before
   writing (skip + print if busy); the DLL handlers are mutex-guarded against
   pipe/mailbox concurrency. Hotkey spam must never interleave two ops.
9. **`executeCodeEx` cannot return export values** (Debug Camera lesson) —
   CE integration is mailbox-only.
10. **CE caches loaded Lua until restart** (Debug Camera lesson) — that's
    why both generators emit **self-contained** scripts with no helper-file
    dependency; the bundle additionally guards against double hotkey
    registration on re-run (§9.2A).
11. **Velocity reset is best-effort** — missing `CharacterMovement` must not
    fail the teleport; log and continue.
12. **Tier 2 raw write** can be snapped back by CharacterMovement/physics in
    some games — the `tier` field exists so the UI/CE can say so instead of
    looking broken. Raw writes use the resolved engine width and are a single
    `Macht::WriteBytes` per struct (location, rotation) — no partial writes.
13. **Trace channel remapping**: games remap ECC channels; default
    TraceTypeQuery1≈Visibility is a default, not a truth — hence the UI
    parameter. A hit on a remapped channel may be a trigger volume; the
    `K2_TeleportTo` spot-adjust mitigates landing inside it.
14. **Z offset**: cursor teleports add `zOffset` (default 100 ≈ capsule half
    height + margin) so the capsule doesn't spawn intersecting the ground.
    (v2 idea: read `CapsuleHalfHeight` off the root `UCapsuleComponent`.)
15. **Anti-cheat / online**: invokes execute game code and teleporting is the
    most detectable cheat there is. UI carries the single-player warning;
    no further mitigation in scope.
16. **Bool params** are written as a single byte at the reflected param
    offset; out-params (`FHitResult`) zero-initialized; parms buffer sized
    exactly `UFunction::ParmsSize` (§5.5).
17. **Pose read tearing** is accepted for display (§5.4); recalls re-read
    nothing mid-flight (they use stored marker values).
18. **No new log file**: stay within the 5 DLL categories.
19. **Magic words**: all op codes, error codes, defaults, command strings,
    VK defaults live in `Grimoire.h` / the single C# constants file; the C#
    side mirrors the mailbox op/offset table in one place only (the
    generators reference it, never re-derive).
20. **CE Lua API verification** (CLAUDE.md): every CE function emitted by the
    generators must be confirmed in celua.txt; no invented APIs.

-----

## 12. Testing Plan

### Unit (C# — xUnit v3 / MTP; run via `dotnet test --project …`)

- `TeleportLuaBundleGeneratorTests` / `TeleportScriptGeneratorTests` —
  mirror `DebugCameraScriptGeneratorTests`: emitted op codes / offsets match
  the constants table; LF-only; busy-check present; hotkey re-registration
  guard present; zOffset/channel/center parameterization; auto-untick timer
  in the AA variant; no helper-file references; **all 3 hotkey schemes**
  emit the exact VK literal sets of the §9.3 table ("Both" = two
  `createHotkey` calls / two `<Hotkey>` entries per action), header lists
  bindings in both notations and contains the conflict + NumLock sentences.
- `BugItGoParserTests` — the three accepted formats, negative numbers,
  comma/space separators, `BugRot` extraction, garbage rejection.
- `TeleportViewModelTests` (with `StubDumpService`) — gating on
  connect/disconnect, error-code→message mapping incl. `-7` force flow,
  marker list refresh on connect, auto-poll start/stop.
- DTO round-trips through the source-gen JSON context (AOT).

### Unit (dll_helpers)

- Pose↔paramsData block pack/unpack helpers (if extracted as pure
  functions); marker store semantics (slot bounds, clear, map compare
  case-insensitivity); error-code mapping. Keep game-dependent code out of
  these (consistent with the existing 452-test suite's style).

### Live smoke checklist (carry into the LIVE-VERIFY list)

| # | Check | Pass sign |
|---|---|---|
| 1 | UE4.27 game (float) + UE5 game (double): save/recall all 3 slots | exact-position recall, no snap-back, velocity zeroed |
| 2 | Recall in main menu | error -3/-5 surfaced, no crash, no partial write |
| 3 | Map change → recall | -7 + Force works (and lands where expected or falls — documented) |
| 4 | Cursor teleport in a cursor game (TQ2-like) | lands under cursor, zOffset honored |
| 5 | No-cursor game (HD-2D): center fallback | lands at screen-center hit |
| 6 | Hotkey spam (hold Num1) | busy-skip prints, no interleaved ops, no hang |
| 7 | Debug Camera ON → save marker → OFF → recall | §5.6 path works |
| 8 | CE bundle re-paste/re-run | hotkeys not duplicated |
| 9 | BugIt invoke from UI → check game-process clipboard | §10.2 explanation holds; Copy-as-BugItGo string matches pose |
| 10 | .CT records auto-untick after firing | record checkbox clears itself ~50 ms later |

-----

## 13. Documentation & Bookkeeping Updates (same PR)

- [dll-spec.md](dll-spec.md): export table 30 → 36; Wirbel module.
- [pipe-protocol.md](pipe-protocol.md): +6 commands (~48 total).
- `Mimic.h` enum comment + [docs/dll-spec.md] mailbox section: `CMD_TELEPORT=8`.
- [naming-convention.md](naming-convention.md): Wirbel row (+ strike it from
  "Available for Future Use").
- CLAUDE.md architecture diagram: add `Wirbel (Teleport)` module line, bump
  export/pipe counts, add `TeleportPanel` to the UI list.
- [architecture.md](architecture.md): file counts (18 .cpp / 20 .h).
- [tips.md](tips.md): new recipe "Save & recall positions / teleport to
  cursor" (goal → panel → buttons → generated CE bundle).
- [roadmap.md](roadmap.md) capability matrix + [dev-log.md](dev-log.md) entry
  with the build number.
- CMakeLists: add Wirbel.cpp.

-----

## 14. v2 / Deferred Ideas (record only, do not build)

- Persist markers UI-side per-game (settings JSON) with a "push to DLL"
  restore on reconnect.
- CapsuleHalfHeight-aware Z offset (caveat 14).
- More slots / named markers; marker list in the CE bundle (`print` table).
- `?BugRot=` full-pose recall in the CE bundle (currently UI-only via the
  explicit-pose pipe path).
- Ground-snap option for marker recall (trace down from marker + epsilon).
- Per-game saved cursor-channel preference once a second cursor game is
  live-tested.

-----

## 15. Camera POV (read-only) — build 1110+

A read-only camera-POV readout on the Teleport tab, **distinct from the pawn
pose** §5 reads. Answers "does this game's camera follow the pawn, or is it
independent?" (the Octopath / SE HD-2D / TQ2 class — see §1 known limitations).

### Why read-only (no Set POV)

The on-screen view is `APlayerCameraManager.CameraCachePrivate.POV`
(`FMinimalViewInfo`), **recomputed every tick by `UpdateCamera`** from the view
target. A raw write to it is overwritten the next frame (see
[tips.md](tips.md) "Forcing camera rotation", approach 4). The mechanisms that
*do* move the camera already exist and are not POV writes:

- **Debug Camera** (`ToggleDebugCamera` → free-fly `ADebugCameraController`) —
  on the same Teleport tab.
- **Moving the view-target pawn + ControlRotation** — exactly what the marker /
  cursor teleport already does (the camera follows on normal games).
- **SpringArm / CameraComponent rotation** or `SetViewTargetWithBlend` to a
  camera actor — per-game, via Live Walker.

The only persistently-settable POV component is **FOV** (`SetFOV` writes
`LockedFOV`); that's a deferred Phase-2 idea, not in this read-only cut.

### Resolution & read path (`Wirbel::GetPov`)

```
GWorld → … → LocalPlayer.PlayerController        (ResolveLocalPC, NO debug-camera
                                                  hop — POV must reflect the ACTIVE
                                                  on-screen view, which IS the debug
                                                  controller's when it's on)
  → APlayerController.PlayerCameraManager         (ObjectProperty)
  → invoke GetCameraLocation()  → FVector         (BlueprintCallable, UE4.18→5.x;
    invoke GetCameraRotation()  → FRotator         the Geri-verified struct-return
    invoke GetFOVAngle()        → float            invoke path)
```

Both location+rotation getters failing ⇒ `TP_ERR_INVOKE`. On a hard-stripped
Shipping build that **cooks the getters out of reflection** (`FindFunc` returns
nothing) this fires immediately with no game-thread round-trip; `GetPovImpl`
`LOG_WARN`s the camera-manager class + per-getter found-flags + whether
`CameraCache` is reflected (build 1112 diagnostics). FOV is best-effort (0 when
absent). A best-effort pawn world location (root `RelativeLocation`, resolving
the pawn through the debug-camera hop) is included for the **camera↔pawn delta**
the UI shows — a large delta that barely changes after a teleport flags an
independent camera.

### Live-verify (build 1112)

**LIVE-VERIFIED ✅ (2026-06-14)** — POV reads on all four tested titles:

| Game | getters | raw fallback | Verified values |
|------|---------|--------------|-----------------|
| SEED Battle Destiny Remastered (4.27) | ✅ | — | getters work |
| DQ III HD-2D Remake (UE5) | ✅ | — | getters work |
| Octopath Traveler (UE5 HD-2D) | ❌ | ✅ (raw) | fixed HD-2D cam: pitch -16°, yaw 0, FOV 40, static |
| Titan Quest II (UE5) | ❌ | ✅ (raw) | angled ARPG cam: pitch -47°, yaw -116.6° fixed, FOV 75, drifting (live follow); `TQ2PlayerCameraManager`, `CameraCache` off=5472 |

PC resolves on all four. On TQ2 / Octopath the getters are **present in
reflection** (`found loc=1 rot=1`) — not cooked out — but the invoke yields
nothing; the **raw cached-POV fallback** below recovers it (`src=raw`, values sane
and matching each camera archetype, LWC double-width correct).

### Raw cached-POV fallback (build 1112)

When both `GetCameraLocation`/`GetCameraRotation` invokes return nothing,
`Wirbel::ReadPovRaw` reads the cached POV directly, **fully by reflection**:
`CameraCachePrivate` (`FCameraCacheEntry`, StructProperty) → `POV`
(`FMinimalViewInfo`) → `Location` / `Rotation` / `FOV`. Inner `UScriptStruct*`s
resolved via `FStructProperty::Struct` (`DynOff::FSTRUCTPROP_STRUCT` probe, same
as Ubel's value walk); offsets + LWC width from each field's reflected `Size`. No
hardcoded layout. Result is tagged `source = "raw"` (vs `"invoke"`), surfaced as a
chip in the UI POV header. `CameraCachePrivate` is private but reflected on the
titles seen; UE4's public `CameraCache` is covered by the contains-match.

### Auto-refresh (build 1112)

The Teleport tab's **Auto (0.5s)** toggle refreshes the camera POV alongside the
pawn pose each tick. On any POV failure the update is **skipped** (last good
values / "—" stay; no error, no clear), so the pose display is unaffected where
POV is unavailable. POV clears on disconnect. Manual **Get POV** still surfaces
the error code.

### Surfaces (build 1110)

- **DLL**: `Wirbel::Pov` struct + `Wirbel::GetPov`; export `UE5_TeleportGetPov`
  (11-double out: cam[6], fov, pawn[3], hasPawn).
- **Mailbox**: `TP_OP_GET_POV = 11`; POV block in `paramsData`
  (`[0..47]` cam 6 doubles, `[48..55]` FOV, `[56..79]` pawn 3 doubles,
  `[80]` hasPawn, `[81]` source).
- **Pipe**: `teleport_get_pov` → `{ code, camX/Y/Z, pitch/yaw/roll, fov,
  hasPawn, pawnX/Y/Z, source }`.
- **UI**: read-only "Camera POV" section + **Get POV** button + `pov_get`
  global hotkey row (OS `RegisterHotKey`, not CE); `TeleportPov` DTO.
- **CE**: `.CT` / AA "Get camera POV" **mailbox record** (`TeleportScriptGenerator`,
  op 11 — ticking it fires the round-trip and prints the camera block, then
  auto-unticks). This is the working CE path. POV was deliberately NOT added to
  the `createHotkey` Lua bundle (`TeleportLuaBundleGenerator`), which was
  unreliable in CE and unsurfaced since build 1038 — and that whole bundle was
  subsequently **removed in build 1111** (see the §9.2 note).

-----

## 16. Directional / explicit-coordinate teleport + force mouse cursor — build 1144+

Three additions, all riding the existing resolution chain (§5) and tier ladder
(§4) — so they inherit every per-game robustness path already built for recall.

### 16.1 Directional teleport (`Wirbel::TeleportRelative`)

Move the pawn along its facing direction by a signed distance (uu; negative =
backward). Facing comes from `AActor::GetActorForwardVector` (invoke; the actor's
exact orientation), falling back to the controller's `ControlRotation` Yaw/Pitch
trig (UE convention `X = cos(P)cos(Y), Y = cos(P)sin(Y), Z = sin(P)`). Two modes:

- **Horizontal** (default): drop Z from the forward vector and renormalize —
  the natural "walk toward that compass bearing" on the ground plane, height
  preserved.
- **3D**: full forward including pitch (look up/down → move up/down; fly/noclip).

`dest = currentWorldPos + unitForward * distance`, then the standard
`TeleportPawnTo` write + `StopMovement`. `SaveLastImpl()` runs first, so **Recall
last undoes it**. Returns the re-read landed pose (so the UI shows the resulting
X/Y/Z/Pitch/Yaw — the "回算" requirement).

### 16.2 Teleport to explicit coordinates (force)

Reuses the existing `Wirbel::RecallExplicit` (no new core): force-teleport to an
exact world X/Y/Z, **no map check**, optional rotation restore. Already exposed on
the pipe via `teleport_recall_marker` (x/y/z variant). New surfaces only add a CE
mailbox op + export + a dedicated UI section (X/Y/Z + optional Pitch/Yaw/Roll +
"Fill from current"). Also undoable via Recall last.

### 16.3 Force mouse cursor (`Wirbel::SetMouseCursor` / `GetMouseCursor`)

Write `APlayerController.bShowMouseCursor` (a `BlueprintReadWrite` **bitfield**
UPROPERTY). `SetShowMouseCursor` is NOT a UFUNCTION, so the bit is written
directly: resolve the reflected `FBoolProperty` layout
(`[FieldSize=1, ByteOffset, ByteMask, FieldMask]` at `FBOOLPROP_FIELDSIZE`,
probed ±), value byte = `pc + Property.Offset + ByteOffset`, toggle `& FieldMask`,
write back. (`Ubel::FindField` only surfaces `FieldMask`, not `ByteOffset`, so the
layout is read directly in `Wirbel::ResolveCursorBit`.)

**Input mode (build 1147).** Live-testing on TQ2 + DQIII HD-2D showed the
property write alone has NO visible effect (logs confirm the bit is written, but
a `GameOnly` viewport recaptures/hides the OS cursor). So `SetMouseCursor` also
drives the input mode via `UWidgetBlueprintLibrary` (`ApplyCursorInputMode`):
- show → `SetInputMode_GameOnly(pc)` THEN
  `SetInputMode_GameAndUIEx(pc, null, DoNotLock, bHideCursorDuringCapture=FALSE)`
  — the `false` is the key (the param defaults to `true`, which would hide it).
  The GameOnly→GameAndUI **transition** is forced (build 1150): a single
  GameAndUI call from the game's running input state often doesn't show the
  cursor until the mode actually CHANGES (live: first force shows nothing,
  OFF-then-ON works — this reproduces that transition in one action).
- hide → `SetInputMode_GameOnly(pc)`.
Best-effort: titles without UMG (no `WidgetBlueprintLibrary`) keep just the flag
write. `SetMouseCursor` now re-reads the bit and reports the actual state (a game
re-setting it per tick shows as not-stuck).

**Remaining limitation.** Still a one-shot — a game that re-sets the flag / input
mode every tick may revert it (no keep-forcing loop yet = potential Phase 3).
Forcing `GameOnly` on **hide** suits titles that hide the cursor by default
(DQIII); on a cursor-native game (TQ2 ARPG) prefer **show**.

Pairs with Cursor Teleport (§6.2): forcing the cursor visible helps on titles
that hide it.

### 16.4 Surfaces (build 1144)

- **DLL**: `Wirbel::TeleportRelative` / `SetMouseCursor` / `GetMouseCursor`
  (+ reused `RecallExplicit`); exports `UE5_TeleportRelative`,
  `UE5_TeleportRecallExplicit`, `UE5_SetMouseCursor`, `UE5_GetMouseCursor`
  (30→38 → **42** total).
- **Mailbox**: `TP_OP_RELATIVE = 12` (in: `[0..7]` double distance, `[8]` mode
  0=horizontal/1=3D; out: pose block + tier), `TP_OP_EXPLICIT = 13`
  (in: `[0..47]` 6 doubles, `[48]` hasRot; out: tier), `TP_OP_SET_CURSOR = 14`
  (in: `ufuncAddr` = 1/0; out: `paramsData[0]` = state), `TP_OP_GET_CURSOR = 15`
  (out: `paramsData[0]` = state).
- **Pipe**: `teleport_relative` (`{distance, horizontal}` → pose),
  `set_mouse_cursor` (`{show}` → `{code, state}`), `get_mouse_cursor`
  (→ `{code, state}`); explicit coords reuse `teleport_recall_marker`.
- **UI**: "Teleport in Facing Direction" (distance + horizontal-only toggle),
  "Teleport to Coordinates (force)" (X/Y/Z + optional rotation + Fill from
  current), "Force Mouse Cursor" (ON/OFF/↻ + state badge); 4 new global-hotkey
  rows (`relative`, `coords`, `cursor_on`, `cursor_off`).
- **CE**: `.CT` / AOBMaker mailbox records for all four + a **"Get current
  coords"** record (`Action.GetPose`, op 0 — reads back the 6 pose doubles into
  the mailbox and prints them, build 1147); batch is now **17 rows**. The
  directional/coordinate records bake the current UI field values.
