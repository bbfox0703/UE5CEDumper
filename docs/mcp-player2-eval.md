# An AI as player 2 through MCP — EVALUATED 2026-11-15, NOT STARTED

> **The question (maintainer, 2026-11-15).** The human plays player 1 and an AI plays player 2; leave vision and image
> recognition aside. With the current MCP tools (`UE5DumpUI.exe --mcp`), can an AI find the UFunctions that act on
> the P2 actor, and then drive that actor by calling them?
>
> **Answer.** Partly. P2 can be found, and the UFunctions that act on it can be found and called. But every call needs
> player 1 to click Allow, and only commands work (move to a point, jump, fire). Per-frame control does not. Nothing is
> built yet; the next step would be the no-code proof of concept in §5. Tracked in [todo.md](todo.md) as
> `[MCP-PLAYER2-2026-11-15]`.
>
> **Sources.** Workflow `wf_4849e5b2-002`, four agents:
> - the MCP tools in this repo;
> - the engine mechanics, read in `vendor/UnrealEngine` (a gitignored reference clone);
> - latency and architecture;
> - a synthesis that re-read the code its answer rests on and corrected the other three.
>
> Engine claims cite `vendor/UnrealEngine` paths. Where a step was not run on a game, this document says
> "not tested".

## 1. What the current tools can do

### Finding P2 — yes, with read tools only (no approval)

- **A local split-screen P2:**
  - `read_status` gives GWorld. `read_instance` on it gives `OwningGameInstance`, and on that, `LocalPlayers`.
  - `read_array` pages through that array. Its order is the player index.
  - Each `ULocalPlayer` leads to its `PlayerController`, and that to its `Pawn` / `AcknowledgedPawn`.
- **An AI-controlled actor:** `read_find_instances` (a class-name substring; the results include `Default__`
  templates), `read_world`, or `read_related` (pawn to controller and back).
- **Traps:**
  - `read_find_live_instance` returns one object, which need not be P2.
  - `read_pose` and `read_current_target` cover player 0 only. P2's position comes from its RootComponent's
    `RelativeLocation` through `read_instance`.
  - Calling a getter such as `K2_GetActorLocation` is a `write_invoke`, which needs approval.
- **Most single-player games have no P2** until something creates one (§2).

### Finding the UFunctions — mostly

- `read_functions` lists a class's own functions only. Walk the superclasses with `read_class`, or search with
  `read_list_functions` and a name filter.
- `write_invoke` resolves a function name up the superclass chain and through interfaces by itself. The interfaces
  are reachable only when `UClass::Interfaces`' offset was measured at run time.
- Input handlers a C++ game binds in `SetupPlayerInputComponent` are usually not UFUNCTIONs, so ProcessEvent cannot
  reach them (general UE knowledge, not verified here).
- Live Funcs could show which UFunctions fire when player 1 presses a key. It is kept out of MCP on purpose (one
  recording per game, working-lessons §6), so a human records it in the UI and tells the AI.

### Calling them — partly

- **The target.** `write_invoke` takes a required `instance_addr`, and it never sends a class name. Any exact
  GObjects entry is a valid target: P2's pawn, its controller, its Enhanced Input subsystem, or a class default
  object, so static library functions can be called on `Default__X`.
  - Measured live: `Add_IntInt(40, 2)` on `Default__KismetMathLibrary` returned 42.
  - Measured live: `K2_SetActorLocation` with a 24-byte FVector moved a pawn and moved it back
    (verification-register.md `[PIPE-PER-PROCESS-2026-11-06]`).
  - A non-static function on a template object is only warned about, not refused.
- **Parameters it can pass:** bool, every integer width, an enum as its value, float and double, FString /
  FUtf8String / FAnsiString, object pointers as hex GObjects addresses (UObject*, UClass*, interface, weak, soft),
  and ONE level of struct (FVector, FRotator and the like by a trusted layout, others by the fields the DLL walks).
  It returns the decoded return value and out parameters.
- **Parameters it cannot pass:**
  - FText: refused.
  - FName: only as a number, and no tool turns a name into one.
  - Arrays, maps, sets, delegates, and structs with no known layout: sent empty.
  - A struct inside a struct: sent as zero.
- ⚠ **A parameter left out is sent as ZERO, not the function's declared default.** The AI must pass each of these
  explicitly:
  - `MoveToLocation`: `AcceptanceRadius` becomes 0 (default -1) and `bUsePathfinding` false (default true).
  - `AddMovementInput`: `ScaleValue` becomes 0.
  - `SpawnAIFromClass`: `bNoCollisionFail` becomes false.
  - `CreatePlayer`: `ControllerId` 0 collides with player 1 ("A local player already exists for PlatformUserId 0",
    `GameInstance.cpp`), and `bSpawnPlayerController` false spawns no controller.
- **The other write tools are hard-wired to player 0.** God mode, movement, pawn time dilation, Fly, teleport,
  See-through, the cursor and the current target all resolve GWorld → GameInstance → `LocalPlayers[0]`, and none
  takes a target. `write_force_field` reaches a whole class tree, so it changes player 1 too when both share a class.
  `write_memory` can write any data field of P2's, asked every call.
- **The decisive limits:**
  - every call is asked in Claude Code (`requiresUserInteraction`);
  - the AI has at most one invoke in flight;
  - the DLL refuses `FUNC_Net` functions and the deny list's names for a background client.

## 2. What control is achievable today

| Kind | Today | Notes |
|---|---|---|
| One-shot actions: `Jump`, `LaunchCharacter`, `K2_SetActorLocation` (a teleport by invoke), a game's own fire or ability UFunction, GAS's `TryActivateAbilityByClass` | Yes, one Allow each | `SetControlRotation` holds only on a PlayerController-possessed P2; an AIController overwrites it every tick (use `K2_SetFocalPoint`). |
| Command-level moves: `AAIController::MoveToLocation`, `UAIBlueprintHelperLibrary::SimpleMoveToLocation` | Yes, with conditions; not tested | Every parameter type passes. `MoveToLocation` needs P2 under an AIController; with `bUsePathfinding` true it needs a NavMesh, with false it moves in a straight line and needs only a NavigationSystem. `SimpleMoveToLocation` works for a PlayerController-possessed pawn too, but needs a real NavMesh. |
| Creating P2: `UGameplayStatics::CreatePlayer(any live actor, -1, true)`; or `SpawnAIFromClass`; or an existing NPC (UnPossess, then `SpawnDefaultController`) | Possible; not tested | The engine splits the screen by default (`SetForceDisableSplitscreen(true)` undoes it). Whether P2 gets a pawn is the GameMode's decision. |
| One-tick Enhanced Input injection: `InjectInputVectorForAction(UInputAction*, FVector, [], [])` on P2's `UEnhancedInputLocalPlayerSubsystem` | Possible; not tested | Every parameter type passes, and the engine wraps the vector in the action's own value type (`EnhancedInputSubsystemInterface.cpp`). One tick only: a press of Jump or Fire. Needs UE 5.0+ and a measured interface offset; interface invokes are unit-tested only. |
| Blueprint input-event stubs (`InpActEvt_*`) | Partly | The legacy stub's FKey parameter can be left zero. The Enhanced Input stub's `FInputActionValue` can only be sent as zero, so "Fire" may work and "Move" cannot. |
| Per-frame movement: `AddMovementInput`, `AddYawInput` | **No** | The input vector is consumed every tick, so one call moves the pawn for one frame. A call costs a model turn (seconds; not measured in this repo) plus about 6-15 pipe round trips before the call, then the game-thread queue (up to the 5 s invoke timeout; past it the call stays queued, -5, and the AI's next invoke is refused until it runs). |
| Holding a direction: `StartContinuousInputInjectionForAction` (UE 5.3+) | **No, today** | Its `RawValue` is an `FInputActionValue`, whose members are not UPROPERTYs (`InputActionValue.h`), so `write_invoke` can only send zero: it would hold "nothing pressed". |

**The practical limit is the approval.** Player 1 clicks Allow for every move player 2 makes. Under `claude -p` the
write tools are refused outright (measured in default and auto mode, verification-register.md item 22).

## 3. What is missing, by value

1. **Grants Phase 1b** (one approval per exact function), designed in
   [mcp-write-approval-eval.md](mcp-write-approval-eval.md) and not started. Without it, everything else here is a
   demo. With it, each call is still a model turn; `-p` and `dontAsk` still refuse; `write_memory` is still asked
   every call.
2. **`FInputActionValue` in `write_invoke`.** Add a known layout `InputActionValue` (UE 5: FVector `Value` at 0, the
   `uint8` `ValueType` at 24, size 32), guarded by the existing engine-size check. Confirm the layout against a
   fixture's PDB first, since the members are not reflected. It is small, and it unlocks:
   - `StartContinuousInputInjectionForAction` / `UpdateValueOfContinuousInputInjectionForAction` /
     `StopContinuousInputInjectionForAction`: three calls hold a stick direction, and the engine re-injects it every
     tick by itself;
   - the axis values of Enhanced Input stubs.

   For Enhanced Input games this is the best value for the effort.
3. **A player target for the pawn tools.** One shared resolver in place of the nine `LocalPlayers[0]` chains, taking
   a player index or a pawn address. `read_pose` (P2's position, no approval) and `write_teleport` first.
4. **A name-to-FName lookup tool**, for `InjectInputVectorForPlayerMapping`, gameplay tags and many game functions.
5. **A DLL-side "drive this pawn" worker** that applies a target point or direction every frame. At MCP latency it is
   the only route to smooth movement. It is large, and per-frame work inside the ProcessEvent hook is a known crash
   class; Stark has a one-shot queue today, no recurring job.
6. Nested-struct and non-empty-array parameters: low value for control.
7. A dedicated "create P2" tool (`CreatePlayer` + `SetForceDisableSplitscreen`, and `RemovePlayer` at session end):
   `write_invoke` already reaches it; the tool's value is avoiding the omitted-is-zero traps and cleaning up.

## 4. Safety and scope

- **Offline only:** single-player, local split screen or a local AI pawn. Online games are out of scope. The DLL
  already refuses network RPCs (`FUNC_Net`) for the AI, and calling a remote player's proxy locally does not control
  that player.
- **What protects today:** the `FUNC_Net` refusal; no `direct_call` for a background client (invokes run only on the
  game thread); one invoke in flight; `if_free`, so the AI never replaces a hold of the user's; holds and queued
  invokes released at session end.
- **Gaps:**
  - The deny list is a name heuristic. **`RemovePlayer` is not on it**, and with `bDestroyPawn` true it destroys
    the pawn (`GameplayStatics.cpp`). Recorded under `[INVOKE-DENY-INDIRECTION]`.
  - `CreatePlayer` on a network client sends a split-join to the server but carries no `FUNC_Net` flag.
  - What an invoke already did outlives the session: a spawned P2, a MoveTo in progress, a continuous injection.
  - A grant cannot stop a granted function being called on NPCs or repeatedly
    ([mcp-write-approval-eval.md](mcp-write-approval-eval.md) §4.9).
- **Side effects in a single-player game:**
  - split screen halves player 1's view;
  - with `bOffsetPlayerGamepadIds`, the human's gamepad can go to P2;
  - P2 may get no pawn;
  - a P2 pawn of player 1's Blueprint that hard-codes `GetPlayerController(0)` may act on player 1 (an inference,
    not verified).
- **Conditions for any new feature here:**
  - `NM_Standalone` only (refuse in a client net mode);
  - behind the experimental switch;
  - stop continuous injections and drives at session end;
  - the planned "Block AI writes" switch ([mcp-write-approval-eval.md](mcp-write-approval-eval.md) §4.10).

## 5. Options

| Option | What | Effort | Main risk |
|---|---|---|---|
| 1. Proof of concept, no code | On DumperTest Shipping, through `write_invoke`, one Allow per step: `CreatePlayer(-1, true)` + `SetForceDisableSplitscreen` (or `SpawnAIFromClass`); `MoveToLocation(bUsePathfinding=false)`; `Jump`; `InjectInputVectorForAction`. It settles five untested points: a GameplayStatics static call, spawning an actor inside ProcessEvent, a live interface invoke, MoveTo without a NavMesh, split screen and gamepad assignment. | none | Low (an offline fixture). Unknown: whether DumperTest has a NavMesh and whether its GameMode gives P2 a pawn. |
| 2. Slow commander | Grants Phase 1 + 1b (the eval's estimate: 2-3 sessions; 1b waits for its measurement M1), then the `FInputActionValue` layout, the FName lookup and a player target for `read_pose` (about 1-2 more sessions). The AI then issues MoveTo, Jump, Fire and held stick directions every few seconds without clicks. | 3-5 sessions | Harm inside a granted scope; a continuous injection left behind unless the session end stops it. |
| 3. DLL drive worker | A new module (a Frieren name), `drive_set` / `drive_get` pipe commands, a `drive:<target>` hold in the ledger, `write_drive` / `read_drive` MCP tools, a recurring job in Stark. A "Fly for P2" reusing Dunste's velocity drive is about 1-2 sessions, flight mode only. | 4-6 sessions | Per-frame work inside ProcessEvent (the crash class, the cost), a stale pawn pointer, fighting the game's own controller. |
| 4. Option 3 plus behaviours | Move-to with an arrival radius, follow, face a target, rate-limited abilities; the AI picks a behaviour and polls its state. | 6-10 sessions | Grows into a bot framework. The worker's own invokes run as owner 0, past the deny list and `if_free`, so it must apply both itself. |

**Recommendation: 1, then 2, then decide on 3; not 4.** An AI as P2 needs commands, not reflexes, and the model
turn's latency already rules out frame-level control. Option 2 is mostly the grants plan already evaluated, with
no DLL change. Option 3 is the only route to smooth movement, but it enters a known crash class, so it waits until
1 and 2 have shown it is worth it.

## 6. Corrections the synthesis made to the researchers

- One-tick axis injection IS expressible today: `InjectInputVectorForAction` takes a plain reflected FVector. What
  cannot be expressed is anything carrying an `FInputActionValue`: the continuous injection, and the Enhanced Input
  stubs' axis values.
- `StartContinuousInputInjectionForAction` is not a one-call lever through MCP today: its value is an
  `FInputActionValue`, sent as zero. Its storage differs by version: an unreflected interface member in 5.3.0, a
  `UPROPERTY(Transient)` TMap on the subsystem from 5.4.
- "MoveTo needs a NavMesh" holds only with `bUsePathfinding` true. With false it uses AbstractNavData, a straight
  line. Since an omitted parameter is zero, an MCP `MoveToLocation` that leaves it out gets the straight-line mode.
- "Write tools are denied under `-p` and `dontAsk` (measured)" overstates: only `claude -p` in default and auto mode
  was measured. `dontAsk` comes from Claude Code's documentation.
- `write_force_field` is not player-0-specific; it reaches a whole class tree.
- Unverified, kept as such: C++ input handlers are not UFUNCTIONs; a P2 pawn's Blueprint hard-coding player index 0
  acts on player 1; `AddMovementInput` adds at most about 34 cm/s per frame (arithmetic, not measured); a model
  turn takes "seconds" (not measured in this repo).
