# GodMode (damage immunity) — Technical Specification

> Status: **IMPLEMENTED (build 1251, branch dev).** All DLL/pipe/mailbox/CE/UI
> pieces shipped following this contract; 1551 C# + 548 dll + 31 utf8 tests green.
> **Deviations from the plan (intentional, lower-risk):** the in-app UI was
> **folded into the Teleport tab as a "God Mode" section** (mirroring the Debug
> Camera force toggle that already lives there) instead of a separate
> `ProtectionPanel` — no new tab, no `MainTabIndex` shift. **Hotkey rows shipped**
> (build 1252): "God Mode ON" / "God Mode OFF" added to the Teleport tab's hotkey
> list (count 16→18), wired to the same OS global-hotkey path as the other rows.
> `get_protect_state` (want/live/resolvable) ships on
> the pipe + export but the UI badge uses the simpler `get_god_mode` tri-state,
> exactly like the Debug Camera badge.
>
> **Build 1256 — generic invincibility-bool scan (T2).** GodMode no longer flips
> only `bCanBeDamaged`: on toggle it reflection-scans the pawn's whole class
> hierarchy for FBoolProperty fields matching a universal damage/invincibility
> keyword table (`Solitar::MatchProtectionBool`: invincib / invulnerab / immort /
> godmode / unkillable / cannotdie / muteki / damageimmune / cantakedamage /
> canbedamaged …) with a per-keyword polarity, and applies ALL of them (ON = each
> flag's "protect" value, OFF = its normal value), re-asserted by the worker. Zero
> per-game config — same philosophy as `PropertyScoringTable`. The matched flags
> are logged so the user sees exactly what was found on their game. **Why:**
> live-test on SEED BATTLE DESTINY REMASTERED showed `bCanBeDamaged` flips
> successfully (log `rc=1`) but has no effect — SEED is a custom battle framework
> (`LifeMSUnit : LifeUnitBase : UnitFwBaseUnit`) that doesn't gate damage on it.
> T2 auto-covers games that DO expose a named invincibility bool; games that are
> purely value-based (HP number, like SEED appears to be) still need Value Search +
> Freeze (a generic auto-HP-freeze, "T3", was considered and deferred by the user).
> ⚠ In-game live-verify still pending (smoke checklist §9). This document is the design contract; the companion
> **[godmode-implementation-plan.md](archive/godmode-implementation-plan.md) *(archived — the plan shipped at build 1251)*** is the
> step-by-step build plan. Modeled on **[teleport-spec.md](teleport-spec.md)**
> and the Debug Camera force on/off architecture (PR #264, build 1014): **all
> logic DLL-side, exposed as C ABI exports + pipe commands + a Mimic mailbox
> command; the UI and any generated CE Lua are thin, stateless clients.** No
> pure-Lua / baked-offset variant is in scope.
>
> **Scope was narrowed after review (2026-06-17):** the feature is **GodMode
> only** (`AActor::bCanBeDamaged`). "Invisible" was dropped — see §1 Non-Goals
> and §3.2 for the reasoning (visual `bHidden` hide isn't useful; "enemies can't
> detect you" has **no universal reflected bool**). The general bit-write
> primitive (§5.4) keeps the door open for per-game stealth flags via Property
> Search in v2.

-----

## 1. Goal & Non-Goals

### Goal

**One stateful toggle — God Mode (damage immunity)** — working on any UE 4.18+ /
5.0–5.8 game the dumper attaches to, with **zero per-game configuration**:

- Set the local player pawn's `AActor::bCanBeDamaged` bitfield to `false`, so all
  damage routed through the standard engine pipeline
  (`UGameplayStatics::ApplyDamage` / `ApplyPointDamage` / `ApplyRadialDamage`,
  which gate on `Actor->CanBeDamaged()`) is dropped before `TakeDamage` runs.
- **Re-assert** the flag on a short timer so it survives respawns / level
  changes / games that reset it (§5.3).

> **The user's original question — "find the correct `bCanBeDamaged` and another
> bool?"** — resolved:
> - GodMode bool = **`AActor::bCanBeDamaged`** ✓ (correct).
> - The "**another bool**" was going to be `AActor::bHidden` for an "Invisible"
>   toggle. **Cut** — see Non-Goals + §3.2.

Deliverables for the user:

- A **Protection panel** in the UI (works after Connect): a God Mode
  checkbox/toggle with a live state badge, an optional persisted global hotkey
  (like the Teleport tab), and a re-assert indicator.
- One-click **"Copy CE Lua"** / **"Save .CT"** generators producing
  **self-contained** stateful toggle records (mirroring
  `DebugCameraScriptGenerator`).
- **Discovery cross-links** (§11): when `bCanBeDamaged` isn't enough (custom
  health system), point the user to Value Search + Freeze (freeze the health
  value) and Property Search (find the game's own `bIsImmortal`). The universal
  flag is already *discoverable* (`PropertyScoringTable` scores
  `bCanBeDamaged @ AActor` and `IsImmortal @ BP_Player_C`); this feature is the
  one-click **actuation** layer.

### Non-Goals

- **No "Invisible" toggle.** Two reasons, both decided 2026-06-17:
  1. **Visual hide (`bHidden` / `SetActorHiddenInGame`) is not useful** — it
     hides *your own* model (3rd-person: no body; 1st-person: a no-op) and does
     **not** stop enemies finding you.
  2. **"Enemies can't detect you" has no universal reflected bool.** AI
     perception (`UAIPerceptionComponent` sight/hearing/damage senses) traces to
     the target's *location*, not its render state, and detection-immunity is
     implemented per-game (perception stimuli registration, team/faction ids, a
     BP `bIsInvisible`). It is **per-game RE**, surfaced through Property Search,
     not a universal toggle (§3.2).
- **No health/value manipulation.** Infinite health/ammo/mana are *values*, not
  bools — already covered by Value Search + Freeze. GodMode only flips a flag.
- **No multiplayer / server-authoritative support.** `bCanBeDamaged` is a
  replicated, server-authoritative property; a client-side write is overwritten
  by the next replication update and may flag the account. UI shows the same
  single-player warning the Teleport panel uses.
- **No anti-cheat evasion.** Single-player only.
- **No noclip / collision toggle in v1.** `bActorEnableCollision` is *related*
  (§3.3) but risky (fall through world); deferred to v2 behind an advanced gate
  via the general primitive (§5.4).

-----

## 2. Decisions (LOCKED)

| # | Decision |
|---|----------|
| D1 | Architecture = **DLL-side primitives + Mimic mailbox** (Debug Camera / Teleport pattern). Generated CE scripts identical for every game; all offsets resolved live by reflection inside the DLL. |
| D2 | New DLL module **`Solitar.cpp` / `Solitar.h`** (namespace `Solitar::`, ASCII for Solitär, matching the `Ubel`/`Lugner` umlaut→ASCII convention). [naming-convention.md](naming-convention.md) pre-assigns Solitär (#11) to "Stealth/concealment"; here she covers **invulnerability** (her character: an overwhelmingly powerful, near-unkillable mage) and the **general bool-forcing primitive** keeps her assigned concealment role alive for v2 (§5.4, §11). |
| D3 | GodMode write = **raw FBoolProperty bit write** of `bCanBeDamaged` — **no UFunction invoke**, so it works even in menus/loading (no game-thread dependency). The flag is polled at damage time. Optional `SetCanBeDamaged(bool)` invoke deferred (v2; replication notify only). |
| D4 | **Re-assertion loop in v1** (O2 resolved = yes). A lightweight DLL worker re-resolves the pawn and corrects flag drift every `kProtectReassertMs` while the toggle is ON (§5.3). |
| D5 | Toggle is **stateful** (ON/OFF + query), mirroring `CMD_SET_DEBUG_CAMERA`, **not** momentary like Teleport ops. |
| D6 | **No cached instance pointers** (the stale-pointer crash class from the 2026-06-10 audit). Pawn re-resolved per op and per re-assert tick; only reflection *offsets* are cached. Bonus: re-resolution re-applies the flag to a respawned pawn automatically. |
| D7 | Apply target = the **local player pawn** (v1). v2 may extend to owned/attached actors + PlayerState (§11). |
| D8 | Mailbox: **one new command `CMD_PROTECT = 9`** with an op code + value, mirroring `CMD_SET_DEBUG_CAMERA` / `CMD_TELEPORT`. |
| D9 | The bit-write primitive is built **general** — `Solitar::SetActorBool(obj, classAddr, propName, on)` — so v2 can wire a "Force this bool ON/OFF" button onto any Property Search bool row (§11). v1 ships only the `bCanBeDamaged` preset. |
| D10 | **Module path = Path B (self-contained `Solitar`)** (O3 resolved). Solitar reimplements the small pawn-resolution chain + the FBoolProperty bit resolver using **public `Ubel`/`Aura`/`Macht` APIs + `DynOff`** — **zero coupling to `Wirbel`**, so it doesn't conflict with the session currently editing `Wirbel.cpp`/`Frieren.cpp`/`Genau.cpp`/`Aura.cpp`. A later behaviour-preserving refactor (Path A) can extract the shared helpers. |
| D11 | Per CLAUDE.md: all magic numbers/strings → `Grimoire.h` (DLL) / single C# constants file; all UI strings in `en.axaml`; everything async UI-side; AOT-safe (source-gen JSON). |

-----

## 3. Engine Facts

### 3.1 `AActor::bCanBeDamaged` — the GodMode flag

| Aspect | Detail |
|---|---|
| **Reflected name** | `bCanBeDamaged` (exact). Fallback: contains `"CanBeDamaged"`, `BoolProperty` filter. |
| **Type** | `uint8 : 1` bitfield → reflected `FBoolProperty` (UE4.25+/UE5) or `UBoolProperty` (UE4 <4.25). Carries `ByteOffset` + `FieldMask`. |
| **Version notes** | Public in UE4.18–4.23; privatised in UE4.24 behind `SetCanBeDamaged()` / `CanBeDamaged()` but **remains a replicated `UPROPERTY` named `bCanBeDamaged`** — reflection by name works UE4.18→5.x. Live verification per game is part of the smoke test (§9). |
| **Effect of `false`** | `AActor::CanBeDamaged()` returns false → `UGameplayStatics::ApplyDamage` / `ApplyPointDamage` / `ApplyRadialDamage` early-out **without** calling `TakeDamage`. Covers all damage on the standard gameplay pipeline (common across UE titles). |
| **Does NOT cover** | Games whose damage **bypasses** `TakeDamage`/`CanBeDamaged()` — a custom health component writing `Health -= X` directly, or a GAS `UAbilitySystemComponent` health attribute. → Value Search + Freeze, or the game's own `bIsImmortal` (§11). |
| **Write** | Raw FBoolProperty bit (read-modify-write one byte with `FieldMask`). Polarity: **GodMode ON ⇒ `bCanBeDamaged = FALSE`.** |

### 3.2 Why "Invisible" was cut

- **`AActor::bHidden`** (set via `SetActorHiddenInGame`) only stops the actor
  **rendering**. AI sight (`UAISense_Sight`) line-traces to the target's
  *location* and consults registered perception targets — **a hidden actor is
  still perceived**. So `bHidden` ≠ "enemies can't see you"; it just removes your
  own model. Not useful enough to ship.
- **Detection-immunity is per-game.** Stealth is implemented via
  `UAIPerceptionComponent` senses + stimuli-source registration, team/faction
  ids (`IGenericTeamAgentInterface`), or a BP-specific `bIsInvisible` /
  `bStealthed`. There is **no single reflected bool** that universally disables
  it. This is **discoverable** (Property Search surfaces a game's
  `bIsInvisible @ BP_PlayerCharacter_C`) but not universal.
- **Conclusion:** ship GodMode only. Per-game stealth becomes a Property Search
  recipe (§11) and, in v2, a one-click toggle via the **general** bit-write
  primitive (§5.4) — no special "Invisible" feature needed.

### 3.3 Related AActor bools (NOT in v1 — context)

| Bool | What it does | Why deferred |
|---|---|---|
| `bActorEnableCollision` | toggle actor collision (noclip-adjacent) | risky (fall through floor); needs `SetActorEnableCollision` invoke; v2 advanced gate via the general primitive |
| custom `bIsImmortal` / `bGodMode` / `bIsInvisible` (game BP) | the game's own immortality / stealth flag | per-game, not universal → Property Search + general primitive (D9) |

-----

## 4. Existing Infrastructure Reused

> **Path B (D10):** Solitar reuses only **public** APIs — it does **not** depend
> on any `Wirbel` internal. The "template" column points at the proven Wirbel
> code Solitar's logic is *copied from* (not linked to).

| Piece | Where (public API) | Wirbel template to copy from | Used for |
|---|---|---|---|
| Cached GWorld | `extern uintptr_t g_cachedGWorld;` (Frieren.cpp, global) | `Wirbel::DerefWorld` ([Wirbel.cpp:77](../dll/src/Wirbel.cpp)) | root of the resolution chain (deref once for `UWorld*`) |
| Reflection field lookup | `Ubel::FindField` / `Ubel::FindFieldOffset` / `Ubel::GetClass` ([Ubel.h](../dll/src/Ubel.h)) | `Wirbel::ResolveLocalPC`/`ResolvePawn` ([Wirbel.cpp:290,357](../dll/src/Wirbel.cpp)) | resolve `OwningGameInstance`→`LocalPlayers[0]`→`PlayerController`→`Pawn`, and the `bCanBeDamaged` `BoolProperty` |
| Instance scan fallback | `Aura::FindInstancesByClass` / `UE5_FindInstanceOfClass` | `Wirbel::ResolveLocalPC` fallback ([Wirbel.cpp:318](../dll/src/Wirbel.cpp)) | local PlayerController when the chain breaks |
| DebugCameraController hop | `Ubel` + `Macht` reads | `Wirbel::HopThroughDebugCamera` ([Wirbel.cpp:341](../dll/src/Wirbel.cpp)) | target the gameplay pawn when the project's Debug Camera is ON |
| **FBoolProperty bit resolver** | `Ubel::FindField` + `DynOff::FBOOLPROP_FIELDSIZE`/`UBOOLPROP_FIELDSIZE` ([Grimoire.h:118,125](../dll/src/Grimoire.h)) | **`Wirbel::ResolveCursorBit` ([Wirbel.cpp:1091](../dll/src/Wirbel.cpp)) — the exact template** | read `[FieldSize,ByteOffset,ByteMask,FieldMask]`, compute `byteAddr`+`mask` |
| SEH read / write | `Macht::ReadSafe` / `Macht::WriteBytes` ([Macht.h:28,49](../dll/src/Macht.h)) | `Wirbel::SetMouseCursor` ([Wirbel.cpp:1548-1552](../dll/src/Wirbel.cpp)) | read-modify-write the single bitfield byte |
| Stateful toggle export/pipe/mailbox | `UE5_SetDebugCamera` ([Frieren.cpp:724](../dll/src/Frieren.cpp)) + `set_debug_camera` + `CMD_SET_DEBUG_CAMERA` | — | template for the export/pipe/mailbox + UI badge |
| Cooperative cancel / shutdown | `Tot` ([Tot.h](../dll/src/Tot.h)) *(renamed from `Cancel` by the Frieren-naming migration)* + `RequestShutdown()` | — | stop the re-assert worker on disconnect / shutdown |
| Global hotkeys (persisted) | `WindowsGlobalHotkeyService` / `IGlobalHotkeyService` | Teleport cursor hotkey | toggle GodMode with the game focused |
| Discovery front-end | `PropertyScoringTable`, Interesting Properties / Property Search panels | — | find game-specific bools when the universal flag isn't enough (§11) |

-----

## 5. DLL Design

### 5.1 New module `Solitar` (`dll/src/Solitar.cpp` / `Solitar.h`)

```cpp
// Solitar — 索莉塔 (壓倒性實力的魔法使 — overwhelming, near-unkillable mage)
// GodMode: force the local player pawn's AActor::bCanBeDamaged bitfield off via
// live UE reflection. Stateful toggle + re-assert worker. The bit-write
// primitive is general (SetActorBool) so per-game stealth/other bools can reuse
// it. Nothing hardcoded — UE4/UE5 version-agnostic. Contract: docs/godmode-spec.md.
namespace Solitar {

enum ProtectResult : int32_t {
    PR_OK            = 0,
    PR_ERR_NOT_INIT  = -1,   // DLL not initialized / GWorld unavailable
    PR_ERR_NO_PAWN   = -3,   // pawn null (menu/cutscene/spectator)
    PR_ERR_REFLECT   = -4,   // bool property not resolved by reflection
    PR_ERR_WRITE     = -10,  // raw bit write failed
};

// Stateful toggle (called by Frieren exports, Fern pipe, Mimic mailbox).
int32_t SetGodMode(bool on);   // applies now + sets desired state; returns observed state (1/0) or <0
int32_t GetGodMode();          // observed bit state on the live pawn (1/0) or <0

// Combined snapshot for the UI badge / connect-time query.
struct State { int8_t godmode; bool resolvable; };
int32_t GetState(State& out);

// General primitive (D9) — v2 hook for "force any bool" from Property Search.
// Resolve + read-modify-write a single reflected FBoolProperty on an object.
// `on` is the DESIRED VALUE OF THE PROPERTY (not "enable godmode"); GodMode
// calls this with on=false for bCanBeDamaged.
int32_t SetActorBool(uintptr_t obj, uintptr_t classAddr,
                     const char* propName, bool on);

// Worker lifecycle (started lazily by SetGodMode(true); stopped on
// SetGodMode(false) when nothing else is active, and on UE5_Shutdown()).
void StopWorker();

} // namespace Solitar
```

- Desired state: `static std::atomic<bool> s_wantGod;` guarded by
  `std::mutex s_mutex` (reachable from Fern pipe + Mimic threads + the worker).
- Resolved reflection offsets cached after first success; **every UObject
  pointer re-resolved per call** (D6). Invalidate the offset cache if `UE5_Init`
  re-runs.
- Constants → `Grimoire.h`: `kProtectReassertMs` (e.g. 300), property-name
  strings (`"bCanBeDamaged"`, `"CanBeDamaged"`), result codes, mailbox op codes.
- Logging: reuse a category (e.g. `LOG_CAT "WALK"` with a `GodMode:` tag) — **no
  6th DLL log file** (CLAUDE.md fixes the DLL at 5).

### 5.2 GodMode write mechanics (the heart)

Identical to `Wirbel::ResolveCursorBit` + `SetMouseCursor`, retargeted to the
**pawn** and `bCanBeDamaged`:

```
1. Resolve pawn (self-contained chain — §4, copied from Wirbel logic):
   world = deref(g_cachedGWorld)
   pc    = ResolveLocalPC(world)  → HopThroughDebugCamera(pc)
   pawn  = ResolvePawn(pc)        (Pawn, fallback AcknowledgedPawn)
   null pawn → PR_ERR_NO_PAWN
2. ResolveActorBoolBit(pawn, pawnClass, "bCanBeDamaged"):
   a. Ubel::FindField(pawnClass, "bCanBeDamaged", nullptr, nullptr,
                      "BoolProperty", fi)   (fallback: contains "CanBeDamaged")
   b. baseOff = DynOff::bUseFProperty ? FBOOLPROP_FIELDSIZE : UBOOLPROP_FIELDSIZE
      for tryOff in { baseOff, -4, +4, +8, -8 }:
          read 4 bytes at fi.Address + tryOff → [FieldSize,ByteOffset,ByteMask,FieldMask]
          validate FieldSize==1, single-bit FieldMask, ByteOffset<=7, single-bit ByteMask
      byteAddr = pawn + fi.Offset + ByteOffset ;  mask = FieldMask
3. b = Macht::ReadSafe(byteAddr)
   GodMode ON  ⇒ b &= ~mask   (clear bCanBeDamaged = cannot be damaged)
   GodMode OFF ⇒ b |=  mask   (restore can-be-damaged)
   Macht::WriteBytes(byteAddr, &b, 1)   → PR_ERR_WRITE on failure
4. Re-read byteAddr → report observed state. (A per-tick game write can revert
   between the lines; the re-assert worker (§5.3) corrects drift.)
```

> Bitfield write = **read-modify-write of a single byte with the mask** — never
> write a whole byte (would clobber the 7 neighbouring bitfields). Exactly what
> `SetMouseCursor` ([Wirbel.cpp:1548-1552](../dll/src/Wirbel.cpp)) already does.
>
> **`[SOLIDE-NATIVE-BOOL]`** Step 2b also takes a **native** bool —
> `{FieldSize 1, ByteOffset 0, ByteMask 0x01, FieldMask 0xFF}`, every Blueprint
> bool, which the T2 scan meets on a Blueprint pawn — when no packed layout is
> found at any probed offset (`Solitar::PickBoolBits`). A native bool owns the
> whole byte, so it is written `0x01` / `0x00` (UE's own `SetPropertyValue`:
> clear `FieldMask`, set `ByteMask`), never `0xFF`; a packed bool resolves and is
> written exactly as above; any other layout is still refused. The same resolver
> serves Force ON / OFF (`Solide`, bool kind), which held nothing on a native bool
> before.

### 5.3 Re-assert worker (D4)

One lightweight thread (pattern: Fern's disconnect monitor), started lazily when
the toggle goes ON, stopped when the toggle is OFF and on `UE5_Shutdown` /
disconnect:

```
loop while s_wantGod && !Cancel::Requested():
    sleep(kProtectReassertMs)              // ~300 ms
    lock s_mutex
    resolve pawn (re-resolve EVERY tick — D6)
    if resolvable:
        read bCanBeDamaged bit; if it drifted from desired (should be CLEARED)
            → re-write (cheap, on-drift only)
    unlock
```

- **Read-cheap, write-on-drift:** one SEH byte read per tick; a write only when
  the game reverted it. Minimises write traffic; avoids fighting a game that
  doesn't touch the flag. `bCanBeDamaged` is rarely written per-tick, so GodMode
  is steady.
- Respawn / level change falls out for free: the chain re-resolves to the new
  pawn and re-applies the flag next tick.
- The worker is **cancellable** (`Cancel::Requested()`) and joined in
  `UE5_Shutdown` before the DLL unloads (don't reintroduce "game won't close").
- Because GodMode is a pure memory write (no invoke), the worker never touches
  the game thread — it cannot stall and needs no Stark queue.

### 5.4 General primitive (D9)

`SetActorBool(obj, classAddr, propName, on)` is steps 2–3 of §5.2 with the
property name and target object as parameters. GodMode is the single pre-wired
caller in v1 (`SetActorBool(pawn, pawnClass, "bCanBeDamaged", /*on=*/false)`).
v2 wires a "Force ON/OFF" button on Property Search bool rows straight onto this
primitive (per-game `bIsImmortal` / `bIsInvisible` / `bGodMode`), reusing the
same re-assert worker keyed by (objectIndex-revalidated, property).

### 5.5 New C ABI exports (Frieren.cpp wrappers)

```c
// Mirror UE5_SetDebugCamera's "returns the resulting state" convention.
int32_t UE5_SetGodMode(int32_t enable);   // -> 1/0 observed state, or <0 ProtectResult
int32_t UE5_GetGodMode(void);             // -> 1/0, or <0
int32_t UE5_GetProtectState(int32_t* outGod, int32_t* outResolvable); // for the UI badge
```

Reminder (Debug Camera lesson): **CE Lua `executeCodeEx` cannot read export
return values** — the CE path is the **mailbox only**. Exports exist for the
pipe/UI and for symmetry/debugging.

-----

## 6. Wiring

### 6.1 Pipe Protocol (Fern / Renge)

Request/response only (state is queried, like `get_debug_camera_state`):

```jsonc
// set_god_mode
{ "cmd": "set_god_mode", "enable": true }  → { "state": 1, "code": 0 }
// get_god_mode
{ "cmd": "get_god_mode" }                  → { "state": 1, "code": 0 }
// get_protect_state  (one round trip for the UI badge on connect)
{ "cmd": "get_protect_state" }             → { "godmode": 1, "resolvable": true, "code": 0 }
```

Non-zero `code` still returns a normal response (UI renders the specific
failure); reserve `MakeError` for malformed requests. Add `Renge::CMD_PROTECT_*`
string constants; update [pipe-protocol.md](pipe-protocol.md).

### 6.2 Mailbox Protocol (Mimic) — `CMD_PROTECT = 9`

Mirrors `CMD_SET_DEBUG_CAMERA` (request rides in the pointer-sized fields):

| Field | Direction | Meaning |
|---|---|---|
| `cmd` (0x00) | CE→DLL, **written LAST** | `9` |
| `status` (0x04) | DLL→CE | poll until `1` (STATUS_DONE) |
| `result` (0x08) | DLL→CE | ProtectResult (≥0 = observed state, <0 = error) |
| `instanceAddr` (0x10) | CE→DLL | **op code** (below) |
| `ufuncAddr` (0x18) | CE→DLL | **value** (0/1) for SET ops |
| `errorMsg` (0x228) | DLL→CE | human-readable failure |
| `paramsData` (0x328) | DLL→CE | `[0]` u8 godmode, `[1]` u8 resolvable |

Op codes (`Mimic.h` `ProtectOp` enum + mirrored in the C# constants file):

| Op | Name | Input | Output |
|---|---|---|---|
| 0 | SET_GODMODE | value 0/1 | result = observed state |
| 1 | GET_GODMODE | — | result = observed state |
| 2 | GET_STATE | — | paramsData[0..1] = godmode/resolvable |

(Reserve op 3 = `SET_ACTOR_BOOL` for the v2 general primitive — needs object
addr + property name in `instanceAddr`/`className`; documented but not wired in
v1.) `HandleProtect()` in `Mimic.cpp` is a thin switch delegating to the
`UE5_*` exports (style of `HandleSetDebugCamera`, [Mimic.cpp:633](../dll/src/Mimic.cpp)).

### 6.3 Error / result codes

| Code | Meaning | UI hint |
|---|---|---|
| 0 | OK | — |
| -1 | DLL not init / GWorld unavailable | "Connect & init first" |
| -3 | Pawn null | "Not possessing a pawn (menu/cutscene?) — try during gameplay" |
| -4 | `bCanBeDamaged` not resolved | "Engine layout unrecognized — please report game+version" |
| -10 | Raw bit write failed | "Write failed (protected memory?)" |

-----

## 7. UI Design

New `ProtectionPanel.axaml` + `ProtectionViewModel` (`[ObservableProperty]`
source-gen, AOT-safe), tab placed next to Teleport. All actions async via
`IDumpService`; everything disabled until pipe-connected. Strings in `en.axaml`
via `StaticResource` (English only).

Layout (top → bottom):

1. **God Mode** — a CheckBox/ToggleSwitch + state badge (`ON` green / `OFF` /
   `unavailable` amber). Tooltip: *"Sets the player's `bCanBeDamaged` to false.
   Blocks standard engine damage (ApplyDamage). Games with custom health systems
   may bypass it — then use Value Search → Freeze on the health value, or find
   the game's own immortal flag in Property Search."*
2. **Re-assert** — read-only indicator (`re-asserting every 300 ms` when ON), so
   the user understands why it survives respawns.
3. **Hotkey** — optional global hotkey row (capture + persist), same component
   the Teleport tab uses, so the toggle works with the game focused.
4. **CE export** — `[Copy CE Lua (toggle)]` + `[Save .CT…]` (CheatTableBuilder),
   self-contained mailbox toggle record.
5. **Warning line** — *"Single-player only. Online games replicate this flag
   server-side; client writes are overwritten and may flag your account."*
6. **Discovery cross-link** — a hint line: *"Still taking damage? The game may
   use a custom health system — try Value Search → Freeze, or Property Search to
   find its own immortal flag."*

State is queried via `get_protect_state` on connect and after each toggle (the
re-assert worker makes the DLL the source of truth, like the Debug Camera badge).
`IDumpService` additions (+ `StubDumpService` for tests): `SetGodModeAsync(bool)`,
`GetGodModeAsync()`, `GetProtectStateAsync()`. New DTOs in the
`[JsonSerializable]` context (AOT rule).

`ProtectionScriptGenerator` (pure static class, mirroring
`DebugCameraScriptGenerator`, LF-only, unit-tested): a **tickbox toggle** AA
record — `[ENABLE]` writes op=SET_GODMODE value=1, `[DISABLE]` writes value=0 (so
ticking the CE record turns GodMode ON, unticking OFF — the Debug Camera "Force
ON/OFF" UX). Verify every CE Lua API against `celua.txt` (CLAUDE.md); invent none.

-----

## 8. Caveats & Pitfalls (MUST be respected)

1. **Bitfield write = read-modify-write one byte with the mask.** Never write a
   whole byte. (Pattern: `SetMouseCursor`.)
2. **Polarity:** GodMode ON ⇒ `bCanBeDamaged = FALSE`. Easy to invert — assert
   in tests.
3. **No cached instance pointers** (D6). Re-resolve pawn per op and per
   re-assert tick. The 2026-06-10 audit's stale-pointer crash class.
4. **`bCanBeDamaged` only covers the standard damage pipeline** (§3.1). Be
   honest in the UI; cross-link Value Search + Freeze (§11).
5. **Pure memory write — no game thread.** Unlike teleport/Invisible-invoke,
   GodMode needs no Stark queue, so it works in menus/loading and the worker
   can't stall. (`PR_ERR_NO_PAWN` simply means "no pawn yet".)
6. **Per-tick reset → flicker** for a game that re-sets the flag every frame
   (rare for `bCanBeDamaged`). Re-assert-on-drift mitigates write spam; can't
   stop a determined per-tick game. Document.
7. **Multiplayer / replication:** server-authoritative; client writes are
   overwritten. Single-player warning in UI.
8. **Re-assert worker lifecycle:** start lazily, stop when OFF + on shutdown,
   cancellable, joined before unload (don't reintroduce "game won't close").
9. **Mailbox single-slot:** generated Lua checks `cmd == 0` before writing (skip
   + print if busy); DLL handlers mutex-guarded against pipe/mailbox concurrency.
10. **`executeCodeEx` can't return export values** → CE integration is
    mailbox-only.
11. **CE caches loaded Lua until restart** → emit self-contained scripts.
12. **Magic words:** op codes, result codes, property-name strings, intervals,
    command strings → `Grimoire.h` / single C# constants file; C# mirrors the
    mailbox op/offset table in one place only.
13. **DebugCameraController hop:** copy `HopThroughDebugCamera` so applying
    GodMode while the project's Debug Camera is ON targets the real gameplay
    pawn, not the pawnless debug controller.

-----

## 9. Testing Plan

### Unit (C# — xUnit v3 / MTP; `dotnet test --project …`)

- `ProtectionScriptGeneratorTests` — emitted op codes / offsets match the
  constants table; LF-only; busy-check present; ENABLE writes value=1, DISABLE
  value=0; no helper-file references.
- `ProtectionViewModelTests` (with `StubDumpService`) — gating on
  connect/disconnect; toggle → state badge mapping; error-code→message mapping;
  hotkey persist/restore.
- DTO round-trips through the source-gen JSON context (AOT).

### Unit (dll_helpers, where pure)

- Bit read-modify-write helper: set/clear with assorted `FieldMask` values
  leaves the other 7 bits intact (the critical correctness property).
- Result-code mapping; op/value pack-unpack for the mailbox block.

### Live smoke checklist (carry into LIVE-VERIFY)

| # | Check | Pass sign |
|---|---|---|
| 1 | UE4.27 + UE5 game: GodMode ON during combat | standard-pipeline damage no longer reduces health |
| 2 | GodMode in main menu | `-3` surfaced, no crash, no partial write |
| 3 | Respawn / level change with GodMode ON | re-assert re-applies to the new pawn (still immune) |
| 4 | Toggle spam (hold hotkey) | busy-skip prints, no interleaved ops, no hang |
| 5 | GodMode ON → close game / disconnect | re-assert worker stops promptly; game closes cleanly |
| 6 | Custom-health game (GodMode no effect) | UI cross-link leads user to Value Search/Freeze or Property Search bool |
| 7 | Debug Camera ON → toggle GodMode | targets real pawn via the DebugCameraController hop |
| 8 | CE `.CT` toggle record | tick = ON, untick = OFF (Debug-Camera-style) |
| 9 | Reflected name check on each test game | `bCanBeDamaged` resolves (log the resolved offset/mask) |

-----

## 10. Documentation & Bookkeeping (same PR as implementation)

- [dll-spec.md](dll-spec.md): export table += `UE5_SetGodMode`/`GetGodMode`/
  `GetProtectState`; `Solitar` module row.
- [pipe-protocol.md](pipe-protocol.md): +3 commands.
- `Mimic.h` enum comment + dll-spec mailbox section: `CMD_PROTECT=9` + `ProtectOp`.
- [naming-convention.md](naming-convention.md): add `Solitar` row; strike Solitär
  (#11) from "Available for Future Use".
- CLAUDE.md architecture diagram: add `Solitar (GodMode)` module line; bump
  export/pipe counts; add `ProtectionPanel` to the UI list.
- [architecture.md](architecture.md): file counts (+1 .cpp / +1 .h).
- [roadmap.md](roadmap.md): capability matrix row.
- [tips.md](tips.md): recipe "God Mode — toggle, hotkey, what to do when the game
  has custom health".
- [dev-log.md](dev-log.md): milestone entry on ship.

-----

## 11. Relationship to existing features & v2 ideas

**Discovery → Actuation pipeline.** The dumper already surfaces the flag as a
property: `PropertyScoringTable` scores `bCanBeDamaged @ AActor` and a game's
`IsImmortal @ BP_Player_C`; Interesting Properties / Property Search rank and
locate them. This feature is the missing **one-click actuation** of the universal
case — no manual CE record needed.

**When the universal flag isn't enough** (custom health / GAS / `TakeDamage`
bypass), the honest fallbacks already exist and the UI links to them:
1. **Value Search → Freeze** the health value.
2. **Property Search** → find the game's own `bIsImmortal` bool.

**v2 — generalise the primitive (D9).** `Solitar::SetActorBool(obj, classAddr,
propName, on)` is already general. Wiring a **"Force ON / OFF"** button onto any
Property Search bool row turns the whole BoolProperty surface into one-click
toggles — GodMode becomes the pre-wired preset, and **per-game stealth**
(`bIsInvisible`, the dropped "Invisible" use-case) is covered the same way.

**v2 — wider apply scope (D7).** Apply `bCanBeDamaged` to owned/attached actors +
PlayerState for games that route damage to a companion actor.

**v2 — noclip (`bActorEnableCollision`, §3.3)** behind an advanced gate, via the
general primitive + a `SetActorEnableCollision` invoke.

**v2 — `SetCanBeDamaged` invoke companion (D3)** for replication notify on the
rare game that needs it.
