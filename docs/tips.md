# Tips & Recipes

User-facing how-to recipes for common tasks with UE5CEDumper. Each recipe maps
a goal to the panels / buttons that get you there. Add new recipes as separate
`##` sections.

-----

## Getting `UE5Dumper.dll` into the game — which of the four routes?

They are ranked, and the ranking is repeated in the UI (Proxy Deploy panel header +
each button's tooltip). Pick the first one that fits your situation.

| # | Route | Where | Use it when |
|---|---|---|---|
| ① | **Deploy a proxy DLL** | Proxy Deploy tab → *Deploy* | You can restart the game. It loads with the game, survives restarts, and Cheat Engine is never involved. **The default answer.** |
| ② | **Inject into a running game** | Proxy Deploy tab → *Inject into running game…* | The game is already up and you didn't deploy a proxy. Nothing to install, but you repeat it every launch. |
| ③ | **From Cheat Engine** | Tools ▸ *Install CE autorun Helper* — or ▸ *Add "Inject DLL" Record* | You work inside CE anyway. See below. |
| ④ | **Standalone `dist\UE5CEDumper.CT`** | Open it in CE, tick the record | Developer fallback. Costs you a two-stage table load (CE holds one table at a time), so prefer ③. |

### ③ has two flavours — pick by how often you use CE

- **`Install CE autorun Helper`** *(permanent, no plugin needed)* — writes `ue5_autorun.lua` into
  `<CheatEngine>\autorun\`. CE runs that folder at start-up, so **every** table then has
  `ue5_inject()` / `ue5_shutdown()`, plus a **UE5CEDumper: Inject DLL** entry in CE's main menu.
  Installs straight into a running Cheat Engine when one is found. **Takes effect on the next CE
  start.** Re-run it after moving UE5DumpUI — the DLL path is baked into the file.
- **`Add "Inject DLL" Record`** *(one-off, needs the AOBMaker plugin)* — pushes an
  `[ENABLE]`/`[DISABLE]` record into the table you already have open, grouped under
  *UE5CEDumper (DLL)*. Tick it to inject. Without the plugin it lands on the clipboard as CE XML for
  you to paste.

Both poll the DLL until its pipe server is actually up rather than sleeping a fixed budget, and
both report a real error if start-up fails or times out.

### After any route

Launch `UE5DumpUI.exe` and click **Connect**. If the DLL is already present, every route detects it
and skips re-injecting rather than double-mapping.

-----

## Walking the engine from the top: "Start from GameEngine"

The Live Walker tab has two root entry points:

- **Start from GWorld** — roots on the live `UWorld` and lists its top-level
  actors. Best for "find an actor / enemy / component in the level".
- **Start from GameEngine** — roots on the live `UEngine`/`UGameEngine` object.
  Best for reaching engine-owned state: `GameInstance` (→ subsystems, save data,
  local players), `GameViewport`, the engine's own `World`, audio/subsystem
  managers, etc.

It's resolved by a reflected member (the engine's `GameViewport` property), **not
by class name**, so it works regardless of UE version or a game-specific
`UGameEngine` subclass. From the engine root, drill down with normal pointer
navigation (`GameInstance → …`).

### What works from a GameEngine root (and what doesn't)

- **Copy CE XML / Copy CE Field — yes.** The exported pointer chain is anchored
  on the engine object's **absolute runtime address** plus the breadcrumb spine.
  It's valid for the current session.
- **The `AOB` checkbox is disabled** from a GameEngine (or any non-GWorld) root.
  The AOB symbol anchors specifically on **GWorld**, so it can only stabilize a
  GWorld-rooted chain across restarts. From other roots the export uses the
  direct address — which is why the option greys out. If you need a
  restart-stable chain, start from GWorld instead, or re-resolve the engine
  address each session.
- No "Locate in GameEngine" / reverse-path features — those are GWorld-only.

-----

## Finding character-control / shop functions (jump / dash / interact / open-shop)

The **Interesting Functions** finder is tuned for *cheat-value* targets — stat/resource
nouns (HP/MP/Gold) and movement/combat cheat verbs. Basic **operation** verbs like
`Dash` / `Dodge` / `Roll` / `Interact` / `Open` / `Buy` / `Sell` / `Shop` / `Vendor` /
`Trade` are **not** in the default keyword tables, so they score 0 and hide below the
threshold. Two ways to surface them:

### The opt-in "Gameplay Actions" pack (recommended)

1. Open **Interesting Functions → Load**.
2. Tick the green **Gameplay Actions** checkbox (next to *Show All*). This folds an extra
   keyword pack (`Dash`/`Dodge`/`Interact`/`Use`/`Open`/`Buy`/`Sell`/`Shop`/`Vendor`/
   `Merchant`/`Trade`/`Purchase`/…) into the score and **re-scores in place** — no reload.
   The matching rows get a green **Gameplay** category chip.
3. Narrow with the filter box (space = AND over func + class name): type `shop`, `buy`,
   `dash`, `interact`. Filter to the **Gameplay** category in the dropdown to see only these.
4. Tick **BP/Exec only** to hide native getter/setter/plumbing noise — gameplay/control/shop
   entry points are almost always `BlueprintCallable` or `Exec` (both survive cooking). Combine
   with *Show All* to browse callable action functions even when they score below threshold.

It's opt-in (default off) because it's noisier than the cheat-value default — turn it back
off to return to the tuned view.

### Best for "open shop UI" and other unguessable functions: the Live Funcs profiler

When the name gives you nothing (a game-specific `OpenShop` / `BeginTrade` / a mangled
Blueprint name), stop guessing names and watch what the game **actually calls**:

A single recording captures a lot (one real case: 70 functions / ~75k calls in 7s), and
per-frame `Tick`/`Update` noise dominates the top while the shop-open function — which fires
only a handful of times — sinks to the bottom. **Use the baseline diff to isolate the action:**

1. Open the **Live Funcs** tab → **Start** → stand still a few seconds → **Stop**. This is
   your idle baseline. Click **⚑ Set Baseline** (turns on Diff mode).
2. **Start** → ALT-TAB to the game → walk up to the merchant and **open the shop** → ALT-TAB
   back → **Stop**.
3. The list now shows a **diff**: functions that did NOT fire while idle are tagged **NEW**
   (green) and ranked to the top; "New/changed only" hides the unchanged Tick noise.
4. Tick **Hide UI widgets** (the created UI, tagged **UI**, not the entry point) and **Hide
   events/delegates** (the `On*`/callback *reactions*, tagged **Event**/**Deleg** — not functions
   you call). What's left are imperative **Call** functions on persistent objects.
5. Tick **Earliest first**. An action's entry point fires *before* the reactions it triggers, so
   the causal opener sorts to the top of the NEW set (see the **Order** column) — name-independent.
6. Click **Live** on the top candidate to open it in Live Walker and invoke it.

> **If Start says the PE hook couldn't install** (counts stay 0 even though the game is running):
> MinHook couldn't place its trampoline near ProcessEvent in this process. **Change to another
> map/scene and Start again** — a level reload reshuffles memory and almost always frees the space
> (verified on Elliot). If it persists, restart the game and re-inject. This is unrelated to whether
> the game is *supported* — the same game hooks fine in a fresh/differently-loaded process.
>
> **If "Hide events/delegates" empties the list**, the action opened via a **native C++ call** the
> ProcessEvent hook can't see (or the panel hadn't fully appeared — record through until it's on
> screen). That's the limit of behaviour-based discovery. Fallback: while the UI is open, use
> Instance Finder on the vendor/inventory class and invoke its own `BlueprintCallable` functions
> (`AddGold`/`SellItem`/…) directly — you often don't need to "open" anything to change the state.

(Without a baseline it still works — just Start → action → Stop and sort/scan by count — but
the diff is what makes a busy game tractable. Leaving the tab auto-stops recording.)

> **Remember the earlier caveat:** a shop *widget* class like `DOLLShopStoreLayout` is the
> transient thing the action *creates* (GC'd when the shop closes — only its `Default__` CDO
> remains), not the opener. The NEW row you want is the function on a **persistent** object
> (PlayerController / a UI-manager subsystem / the vendor's interaction component) that opened it.

### Then: find the right instance and call it

A control/shop function is a **stateful instance method** — it needs a live, non-CDO `this`:

- Click **Live** on the row to open it in Live Walker (falls back to Class Struct if there's
  no live instance). Or use the row's **inst** button → Instance Finder.
- For the player: **Related Objects → 🎯 Detect target**, or Live Walker's *Start from GWorld*
  → PlayerController → Pawn.
- **Pipe Invoke** the function (Live Walker row) → fill params → **FIRE**. No-arg verbs
  (`Jump`, `StopJumping`, `Dash`) fire directly.

### Open-shop caveats (honest limits)

"Open shop UI" is **game-specific** and often needs a param (a vendor object pointer, or a
vendor/shop ID). Practical path:

1. Find the vendor/shop object: **Instance Finder** → `Shop`/`Store`/`Vendor`/`Merchant`, or
   spot the interaction actor in Live Walker's GWorld list while standing at the merchant.
2. See its functions **with params**: open its class in Class Struct / Live Walker
   (`walk_functions` shows each param's name/type/`obj_class`). Or use **Find Func** to list
   functions that *take that class as a parameter* — often lands `OpenShopFor(AVendor*)`.
3. In the invoke dialog, enter a vendor **ID** (`IntProperty`, decimal or `0x…`) or use
   **[Pick…]** to select the vendor **object pointer** (pre-filtered to the param's class).

Some games open the shop purely via a **UMG widget event** with no callable UFunction entry
point — those can't be reproduced by invoking a single function.

-----

## Forcing camera rotation in a fixed-view (2.5D / 45°) game

UE4 and UE5 share the camera pipeline, so the same handful of entry points work
across versions. A "locked" 45° / top-down camera usually just means the game
**never wired input to the camera** — the underlying UE camera chain is still
complete and writable.

### The shared camera chain (bottom → top)

```
APlayerController.ControlRotation
        ↓ (bUsePawnControlRotation / bInheritYaw ...)
USpringArmComponent.RelativeRotation   ← 2.5D games most often hard-code the angle here
        ↓
UCameraComponent
        ↓
APlayerCameraManager.CameraCachePrivate.POV.Rotation  ← final output, recomputed every tick
```

Typical fixed-view setup: the SpringArm's `RelativeRotation.Pitch` is set to
-45/-60 with `bUsePawnControlRotation=false` and `bInheritYaw=false`, so mouse
input never reaches the camera.

### Approaches (easy → hard)

**1. Debug Camera (easiest, UE4/5 both).**
`UCheatManager::ToggleDebugCamera` spawns an `ADebugCameraController` — a free-fly
camera (WASD + mouse), zero memory edits. In UE5CEDumper: **Console → load exec
commands → the Debug Camera control row** (visible when `ToggleDebugCamera` is
found). Use **Force On / Force Off** (robust — handles Shipping builds whose
`DisableDebugCamera` is stripped by switching the player's controller back), or
**Copy CE Script** for a self-contained CE checkbox (tick = on, untick = off).
Caveat: this is a *separate* camera; game logic keeps running underneath.

**2. Rotate the SpringArm / SceneComponent (most common real fix).**
Instances → find `SpringArmComponent` → open it in **Live Walker** → look at
`RelativeRotation` (FRotator = Pitch/Yaw/Roll; note UE5 LWC makes these `double`,
UE4 `float`). Edit **Yaw** to turn the view. If the value snaps back, a Blueprint
is re-setting it every tick — either **Freeze** the field, or invoke the function
instead of a raw write:

- `USceneComponent::K2_SetRelativeRotation` — BlueprintCallable, exists in UE4 &
  UE5, callable through the invoke helper via ProcessEvent (cleaner than a raw
  write — it runs `UpdateComponentToWorld`).
- While you're there, check the `bInheritYaw` / `bUsePawnControlRotation`
  bitfields — flipping `bUsePawnControlRotation` to `true` often restores mouse
  control of the view outright.

**3. PlayerController.ControlRotation.**
`APlayerController::SetControlRotation` is a cross-version native function (the K2
`SetControlRotation` is BlueprintCallable). In fixed-view games it's usually cut
off by the SpringArm's inherit flags, so combine it with the bitfield flips from
approach 2 for it to have any effect.

**4. CameraManager POV (last resort).**
`APlayerCameraManager.CameraCachePrivate.POV.Rotation` is the final value, but
`UpdateCamera` overwrites it every tick — a raw write does nothing. You'd have to
hook (UE5CEDumper already has the Stark/MinHook ProcessEvent base) or NOP the
write. Rarely worth it; the first three paths usually suffice.

> **Diagnose first — Teleport tab → "Camera POV" → Get POV.** This reads the
> live camera location / rotation / FOV (via `GetCameraLocation` /
> `GetCameraRotation` / `GetFOVAngle`) and shows the camera↔pawn distance. It's
> read-only (there's no Set POV — `UpdateCamera` would overwrite it), but it
> tells you *which* approach you need: if the camera location barely changes
> when you teleport the pawn, the camera is independent (use approach 1's Debug
> Camera or approach 2/SpringArm) rather than pawn-following.

### Caveats

- Some games don't use a SpringArm — they `SetViewTargetWithBlend` to a fixed
  `CameraActor` placed in the level. Then rotate **that actor's** RootComponent
  instead (`K2_SetActorRotation` / `K2_SetWorldRotation`).
- 2.5D side effects: after rotating, billboard sprites, occlusion culling, and
  "front-only" art can break — that's a limitation of the game's art, not a tech
  problem.
- FOV: `APlayerCameraManager.DefaultFOV` (or the `fov` console command) is also
  cross-version; you'll often want to widen it when pulling the view back.

### Fastest workflow in UE5CEDumper

Instance Finder → search `SpringArmComponent` → Live Walker → inspect
`RelativeRotation` → edit **Yaw** and watch the screen turn → if it gets
overwritten, flip `bUsePawnControlRotation` / `bInheritYaw`, or invoke
`K2_SetRelativeRotation` instead of writing raw.

-----

## Save & recall positions / teleport to the cursor

Use the **Teleport** tab (works after Connect + scan). It resolves the local
player's pawn for you — no per-game class names needed — and teleports by
invoking the engine's own functions, so it's clean across UE4 and UE5.

**Marker save / recall (BugItGo-style).** Stand where you want, click **Save**
on Marker 1/2/3. Later click **Recall** to jump back (rotation restored, fall
velocity zeroed). Markers live in the DLL, so they survive a UI restart as long
as the game keeps running. Recall is refused with a hint if you've changed maps
— click **Force** to override (you may land in an unloaded area). Markers store
coordinates only, so dying / respawning never breaks them.

**Teleport to the cursor (2.5D / 45° games).** For top-down ARPGs (Titan
Quest-likes) click **Teleport to cursor** — the pawn jumps to whatever's under
the mouse. For games with no visible cursor (SE HD-2D-style), leave **Fall back
to screen center** ticked and it traces from the middle of the view instead. If
hits land on the wrong surface, bump the **Channel** number (games remap trace
channels); **Z offset** lifts you off the ground so you don't spawn inside it.

**BugItGo interop.** **Copy as BugItGo** puts a `BugItGo X Y Z` string on the
clipboard (handy for sharing a spot). Paste any `BugItGo X Y Z`, bare `X Y Z`,
or a full `?BugLoc=(…)?BugRot=(…)` string into the box and click **Run** to
teleport there — no CheatManager needed. (Note: invoking the game's own `BugIt`
from the Console tab returns nothing on purpose — it writes to the game's log /
screenshot / clipboard, not back to the dumper. Use Copy-as-BugItGo instead.)

**Hotkeys in Cheat Engine.** Pick a **Hotkey scheme** (Numpad / Top-row / Both),
click **Copy CE Lua (hotkey bundle)**, and paste into a CE *Table Lua Script*.
That binds save/recall/cursor/copy to keys (default: Ctrl+Num1-3 save, Num1-3
recall, Num0 cursor — NumLock must be on). Use **Top-row** on laptops or when
the game already uses Ctrl+1-3. Requires `UE5Dumper.dll` injected; the script is
self-contained (no helper Lua). **Save .CT…** exports the same actions as 7
momentary records instead.

> Single-player only — online games will rubber-band or may flag teleports.

-----

## Slowing, freezing, or speeding up game time (time dilation)

Goal: global slow-mo / bullet-time / freeze / fast-forward — or slow **just the
player** while the world runs normally. Use the **Teleport tab → Time Dilation
card**. It forces Unreal's reflected dilation floats and re-asserts the value
every ~250 ms, so a game's own slow-mo ability or a cutscene time track can't
revert it.

### Do it

1. Enter actual gameplay (a live world / pawn), open the **Teleport** tab.
2. **Two independent rows — Whole world and Player pawn.** The **Whole world** row
   writes `AWorldSettings.TimeDilation` — **everything** slows (enemies, projectiles,
   physics); the **Player pawn** row writes only `AActor.CustomTimeDilation`. Each
   row has its own slider + **Apply** / **Reset**, and the DLL holds both at once, so
   you can run them **together**: the pawn's effective rate is *world × pawn*, so
   **World ½× + Player 2× = the player at normal speed in a half-speed world**
   (classic bullet-time dodging). The Player row shows a live **Combined player
   speed = world × pawn** readout, so *Whole world* also slowing the player is never
   a surprise — for a slow world but a normal-speed player, set **Player = 1 ÷ World**
   (World 0.5× → Player 2×).
3. On each row, drag the slider (**Whole world 0 – 3×**, **Player pawn 0 – 10×**) or hit a preset
   (**Freeze / ¼× / ½× / 1× / 2×**) → **Apply**. `1×` = normal, `0.5×` = half speed,
   `0` = frozen. **Reset** restores that lever's natural value and snaps its slider
   back to 1× (the other row is untouched); **↻ Refresh both** re-reads both levers'
   live values + whether each override is engaged.

The held value lives in the DLL, so it **survives a UI reconnect** (reconnect and
the card shows what's engaged), and your last slider value + target are remembered
across UI restarts.

### No UI? Export it to Cheat Engine

The card's **Add to CE** (AOBMaker) / **Save .CT…** ship two on/off records —
**Time: World** and **Time: Player** — each baked at its own row's dilation. Ticking
a record holds the value; unticking resets it to the game's natural value. The two
records are **independent — tick both at the same time** for the bullet-time combo
(they serialise cleanly through the DLL's single-slot mailbox, so enabling one never
clobbers the other). Runs from a standalone CE table with just `UE5Dumper.dll`
injected, no UI open.

### Caveats

- A literal `0` can destabilise physics on some games — use `0.05×` for a
  near-freeze instead.
- You can't "step" a **paused** world (the game's own pause menu) via dilation.
- During a **Sequencer / cutscene** time track the value flickers briefly as the
  override fights the game.
- Single-player only.
- A few games drive speed from a **bespoke, non-`WorldSettings`** multiplier —
  those won't respond to dilation at all.

### Follow-up: find the cooldown / timer behind an effect

Slowing time is often step one; step two is finding the **value** (a cooldown /
countdown float) or the **function** (the timer callback) driving an effect:

- **The countdown value** — [Value Search] → begin a scan → let it tick → refine
  with **Decreased** repeatedly; a ticking timer survives every pass (a count-up
  "Elapsed" uses *Increased*; a value held by `TimeDilation = 0` reads
  *Unchanged*). A new **Timing** category chip in **Interesting Properties** also
  surfaces `Cooldown` / `RemainingTime` / `Duration` / `TimeDilation`-named fields.
- **The timer callback** (which UFunction drives it) — **Live Funcs → Start →
  stand idle ~15-20 s → Stop → tick "Periodic only"**. Functions firing at a
  steady cadence outside the per-frame Tick band are tagged **Timer** with their
  measured **Period**. Only UFUNCTION-bound (Blueprint / `SetTimerByFunctionName`)
  timers appear — native C++ `SetTimer` callbacks bypass the ProcessEvent hook.

-----

## Hiding from enemies / forcing a discovered flag (Force field + Stealth Meter)

Unreal has **no universal "enemies can't detect you" bool** — detection is per-game
(a stealth/visibility meter, a `bIsInvisible` flag, a team id, a per-enemy target
pointer). So instead of a magic toggle, this tool gives you a **discover-then-hold**
workflow: find the game's own field, then force-and-hold it. Both features are
behind the **Experimental** opt-in (they write to live gameplay objects).

### Zero your stealth / visibility meter (Teleport → Stealth Meter card)

1. Enable **Experimental features**, connect, and enter gameplay (a live pawn).
2. Teleport tab → **Stealth Meter** card → **Detect meter**. It resolves the player
   pawn + its owned components and keyword-scores numeric fields
   (visibility / noise / detection / awareness / concealment). The readout shows the
   field it locked onto (e.g. `BP_Player_C::Visibility = 0.73`) — **verify it's right**.
3. **Hold @0** freezes that field at 0 across every live instance of its class, and
   the DLL re-asserts it (write-on-drift) so the game can't refill it. **Reset** releases it.
4. **Nothing found?** The game doesn't expose a reflected stealth meter — use the
   Force workflow below on a field you find yourself.

### Force any discovered field (Property Search → right-click → Force field)

For a flag / meter / target pointer you locate yourself:

1. **Property Search** for the field — e.g. `invisible`, `stealth`, `detect`,
   `aggro`, `target`, `alert`, or a game-specific name. (Live Funcs / Related Objects
   help you find *which* class and field.)
2. Right-click the row → **Force field** (submenu appears when Experimental is on):
   - **Force ON / OFF** — a `BoolProperty` (e.g. `bIsInvisible` → ON, `bIsAlerted` → OFF).
   - **Force → null** — a strong `ObjectProperty` (e.g. an enemy's `TargetActor`); a
     confirm warns that nulling a live pointer can crash if the game dereferences it.
   - **Force value…** — a numeric field held at a value you enter.
3. The hold applies to **all live instances of the row's class**. The bottom
   **"Forced fields (N held)"** strip shows how many instances each hold is on —
   **N = 0 means the class/field matched nothing right now** (an honest no-op signal;
   enter combat / spawn the enemy and re-force). Remove a hold with **✕**, or **Clear all**.

### Honest limits

- **By-class, not per-instance.** A force hits *every* live instance of the class
  (the N-held count shows it). For a player-only field that's usually 1; for a shared
  enemy class it's all of them (often what you want).
- **Only reflected fields.** Native (non-`UPROPERTY`) team ids / raw handles aren't
  reachable this way — find them with the Native-C Value Scan and Freeze instead.
- **Perception ≠ de-aggro.** Zeroing a visibility meter stops *new* detection that
  reads it; an already-aggro'd enemy with a latched target may keep chasing.
- **Single-player only** — these are client-side writes.

-----

## Flattening save-data records into a clean Cheat Engine table

A struct-heavy object — a save slot, a stats block, a `TMap` of mission records —
exports to CE as a tree of nested folders: one group per struct, one per array /
map element, each wrapping the actual values. CE *can* watch that, but you click
through a lot of folders to reach a number. The Live Walker **Options** dropdown
has a family of toggles that flatten that tree at export time. They all apply to
**Copy CE XML** and **Copy CE Field** only (the CSX Structure-Dissect export is
deliberately left nested), and **none of them change the watched address** — a
flattened row resolves to the exact same memory the nested one did, just with the
offsets folded into the row instead of spread across parent folders.

### The toggles (Live Walker → Options)

- **Flatten primitive-leaf structs** — collapses any struct whose whole contents
  are plain numbers (`int` / `float` / `bool` / `enum`), e.g. an `FVector`
  (X/Y/Z), an `FRotator`, an `FDateTime` (its single `Ticks`), or a `{Min,Max}`
  pair. Children become sibling leaves named `Struct ▸ Field` at the combined
  offset. A struct that contains a pointer / string / container keeps its group.

- **Flatten leaf records (names/strings)** — a superset of the above that *also*
  accepts `FName` and `FString` fields as leaves, so a save-data "record" struct
  like `{Score, Rank, MsID (FName), PilotName (FString)}` flattens **completely**.
  An `FName` renders as its 4-byte index; an `FString` renders as a CE **String**
  (it carries the one pointer-dereference CE needs to read the text). There is no
  field-count limit — the "every field is a leaf" requirement is the safety gate.
  **This also reaches into containers:** a `TMap` / `TArray` *of* such records
  drops its per-element `[i]` folders too, so 200 mission records become 200 flat
  rows (`[0] mc1om_001 ▸ Score`, …) instead of 200 folders. (Struct arrays / sets
  already flattened; only `TMap` kept a wrapper before.)

- **Collapse single-leaf pointers** — for the "pointer to a string" case: when a
  pointer's target object holds exactly **one** watchable value (a scalar, an
  `FName`, or an `FString`), it collapses the folder-plus-lone-child into one
  `Pointer ▸ Field` record at the pointer offset, following the pointer in the
  row's offset chain. A pointer whose target has two or more fields keeps its
  group (its object identity is worth a boundary).

All three are **off by default** and persist across sessions. The `▸` segment
names honour the **Append +Offset** option; the struct / class type (if you turn
on **Append +Type**) is shown once at the end of the row.

### Telling the records apart: Record Colors

Once a map of records is flattened, the per-element folders that used to separate
them are gone — 200 rows in a row. **Options → Record Colors…** opens an editor
that tints each element's rows by index parity (CE colours the row text), so
even-indexed records (`struct[0]`, `[2]`, …) read in one colour and odd-indexed
(`[1]`, `[3]`, …) in another — adjacent records stay visually distinct.

- Each of **Even** / **Odd** takes a colour from a neutral preset palette, a hex
  value, **Custom…** (an in-app picker: rainbow hue strip + R/G/B sliders + live
  preview), or **Reset** (no colour → CE uses its theme). Default: enabled, Even =
  azure, Odd = unset.
- Colours only land on **flattened container rows** — ordinary fields are never
  tinted, and nothing is coloured unless one of the flatten toggles is also on.

### Worked example

A save slot exposes `StoryMissionRecord` = `TMap<FName, LifeStoryMissionRecord>`
with 222 entries, each `{Score, Time, ClearCount, Rank, MsID, PilotID, PilotName}`.
Drill to it in Live Walker, tick **Flatten leaf records**, open **Record Colors…**
(leave the azure default), then **Copy CE XML** and paste into CE. You get a flat
list — `[0] mc1om_001 ▸ Score`, `… ▸ PilotName` (a readable String), `[1]
mc1om_002 ▸ Score`, … — with even and odd missions in alternating colours, no
folders to expand, every `Score` directly editable.

### Caveats

- **CE XML / Field only.** CSX export stays nested by design — if you export both
  from the same view they'll look different.
- `FText` is **not** flattened (its internal chain has no clean CE encoding); a
  struct / pointer holding one keeps its group.
- Flattening removes the object / element **boundary**, not the data. If you
  prefer the folders for orientation, just leave the toggles off.

-----

## Capturing snapshots automatically over time (Auto Snapshot)

Use this when you want a series of snapshots taken hands-free — e.g. to diff a
value across a fight, a level transition, or a long resource tick — instead of
clicking **Capture Snapshot** each time. It lives on the **Snapshot** tab's
Capture panel (experimental tabs must be enabled first).

### Setup

1. Connect to the game and open **Snapshot**.
2. In the **Auto snapshot** box set:
   - **Interval (sec)** — target time between the *start* of one snapshot and the
     next (default 900 s = 15 min, min 60 s). If a capture runs longer than the
     interval, the interval auto-extends; there is always at least a 60 s idle gap
     between snapshots.
   - **Retention** — `KeepRecent` (capture forever, keep only the newest **N**) or
     `FixedCount` (capture exactly **N** times, then stop).
   - **Count (N)** — the N for whichever retention mode.
   - **Auto-adjust quota** (optional) — see below.
3. Flip the **Auto snapshot** toggle **On**. The status line shows a live
   countdown to the next capture and the running count.

To stop: flip the toggle **Off**, or press **Cancel** during a capture (Cancel
stops the whole loop, not just the current capture). Auto snapshot is
**session-only** — it never starts by itself on connect and never resumes on the
next launch; only the settings are remembered.

### Quota interaction

- Snapshots still obey the per-game **quota**. With `KeepRecent`, the newest N are
  kept *and* the byte quota is enforced — whichever drops more wins.
- **Auto-adjust quota off** (default): if the quota can only hold **one** snapshot
  while you asked to keep more, auto snapshot stops with a message. Raise the quota
  or turn auto-adjust on.
- **Auto-adjust quota on**: when the quota can't hold the wanted number of
  snapshots it is raised to the next preset that fits (up to Unlimited).

### Disk-space safety net (applies to *all* captures)

Next to **Used:** is **Min free disk: [ % ] / [ GB ]**. Before *any* capture
(manual or auto), the drive holding the snapshot database must have at least
`min(percent, GB)` free — default **10% / 50 GB**, whichever is smaller. Below
that, the capture is refused (a multi-GB snapshot can't fill your system drive).
Set the **%** to 0 to rely on the GB floor alone (or the GB to 0 for the % alone).

### Reducing impact on the game

Auto snapshot deliberately does **not** lower the scan thread priority — that was
tried and reverted because it starves the capture ~20× when the game is busy. The
lever is the **idle gap** between snapshots: a longer interval leaves the game more
breathing room. The capture itself already caps its worker threads at `cores − 2`.

-----

## Cleaning up leftover proxy DLLs after uninstalling a game

**The symptom.** You uninstall a game from Steam and the game's folder is still there, holding
nothing but our `version.dll` (or `dxgi.dll` / `winmm.dll` / `dinput8.dll`). That is not a Steam
bug: Steam will not delete a file it does not own, so our proxy keeps the whole folder chain alive.
Measured on one real machine: **9 leftover shells**, the oldest from build 447.

**Where.** Proxy Deploy tab → *Leftover proxy DLLs* card.

| Button | What it does |
|---|---|
| **Find leftovers** | Scans and REPORTS. Changes nothing. |
| **Report…** | Writes a dry-run `.txt` listing every path and opens it. Changes nothing. |
| **Delete checked (N)** | The only button that touches the disk. Rows start unchecked and there is no select-all. |

### The two-step, and why it is two steps

*Find leftovers* only lists. **Report…** writes
`%LOCALAPPDATA%\UE5CEDumper\Reports\leftover-proxies-<timestamp>.txt` and opens it in whatever you
use for text files, so you can read the full plan outside a modal dialog and keep it. It is built
from the same rows the delete acts on, so it cannot describe a different plan.

Then tick the rows you want and press delete; you get one more confirmation listing the same paths.

### What it will and will not remove

- **Our DLL always goes** — to the **Recycle Bin**, never unlinked, so a wrong call is recoverable.
- **Folders are removed one level at a time and only while each is left completely empty.** The
  moment a folder still holds anything, that folder and everything above it are kept. The Steam
  library's `common` folder is never touched.
- **Anything that is not ours is never touched.** If ReShade's `dxgi.dll` shares the folder with our
  `winmm.dll`, only `winmm.dll` goes and the folders all stay. Same for a dead tool's leftover
  `.log` / `.ini`.
- **A folder that still holds an executable is skipped entirely** — that is an installed game, and a
  proxy you deployed on purpose. Use *Undeploy* for those.
- **Steam is asked first.** If any `appmanifest_*.acf` in that library still names the folder, the
  row is refused outright, even if the folder looks empty — that is what stops a mid-update or
  mid-verify game being mistaken for an uninstalled one.
- **Junctions and symbolic links are refused**, and a folder we cannot fully read is refused. "Could
  not read it" never counts as "it is empty".

### Non-Steam locations

Rows found outside a Steam library (recovered from our own logs) are listed as **file only**: the DLL
is recycled, no folder is removed. There is no `appmanifest` out there to prove the game is gone, so
the folder chain gets no anchor and is left alone.

### If the report and the result disagree

They can, and only in one direction. The report is a snapshot; every condition is checked again
immediately before anything is removed, and the plan is then narrowed to what you confirmed. So the
real result **can be smaller than the report, and never larger.**

### Where the reports live, and how long they stay

`%LOCALAPPDATA%\UE5CEDumper\Reports\leftover-proxies-<timestamp>.txt`, one per click, never
overwritten — so two scans can be compared and a report you are still reading is never replaced.

Old ones are aged out after **30 days**, and only when you write a NEW report — a launch that never
touches this feature deletes nothing. **The newest report is never removed, whatever its age**, so one
you kept deliberately survives. Age rather than "keep the last N" for the same reason the logs use
age: several reports in one before/after session would otherwise evict an older, more useful one.

-----

## "Guess?" — why some regions get guessed rows and others do not

The Live Walker's **Guess?** toggle fills the space *between* reflected properties with best-guess
rows (`?0x678_i32`, `Float?`, `Padding`) so a struct's unknown bytes are at least addressable. Two
things about what it deliberately does NOT do, both of which look like bugs the first time:

**Container internals are not decomposed.** A `TArray`, `TMap` or `TSet` field occupies its whole
inline allocator footprint — the data pointer, `Num`, `Max`, and a map's hash tables. CE's Structure
Dissect shows those as loose ints, so the same object side by side looks like Guess? is "missing"
16-80 bytes. It is not: that region is fully owned by the single Array/Map/Set property, and our
walker shows the contents as an expandable row instead of flattening the bookkeeping. Confirmed live
on Elliot's `LSGameWork`, where `0x170` (ArrayProperty, 16 bytes) and `0x180` (MapProperty, 80 bytes)
cover `0x170-0x1D0` exactly and the gap list has nothing in that span.

**A static C-array is one property, not N.** `UPROPERTY() int32 Foo[8]` is a single reflected field
whose footprint is `ElementSize * ArrayDim` — 32 bytes, not 4. The walker shows the first element,
and Guess? claims the whole span. ⚠ Before 2026-09-07 it did not: it claimed only the first element
and then emitted a phantom guessed row over each remaining one, so an eight-element array appeared as
one real field followed by seven invented `?0x…_i32` rows. If you are reading an older dump, those
rows are array elements, not unknown data.

**What a guessed row is worth.** It is a guess, and it is labelled as one — a `?` suffix and a
generated name. A region can be padding the compiler inserted, an editor-only field compiled out of
your build, or a genuinely unreflected member. Guess? tells you the bytes are unclaimed; it cannot
tell you they mean anything.
