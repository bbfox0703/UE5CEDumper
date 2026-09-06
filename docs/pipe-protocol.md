# Pipe Protocol — JSON IPC Specification

Named pipe: `\\.\pipe\UE5DumpBfx`
Format: JSON, newline-delimited (one message per `\n`)
Direction: bidirectional — Request/Response + async push Events
Total commands: **99** — a DERIVED number, regenerate it, never hand-edit:
`grep -c 'constexpr const char\* CMD' dll/src/Renge.h`. (It read 31 from build ~547 until
2026-08-05, i.e. it was wrong by a factor of three for most of this project's life.)

-----

## General Rules

- Every request carries an `"id"` (integer, caller-assigned).
- Every response echoes the same `"id"` and includes `"ok": true|false`.
- On failure: `"ok": false, "error": "message"`.
- On partial success: `"ok": true, "error": "message"` — check for `"error"` even when `ok` is true.
- All addresses are hex strings with no prefix (e.g. `"7FF600A12340"`) unless noted.
- Pagination advances by `"scanned"` (indices iterated), **not** by `objects.length` — null slots are skipped but still counted.
- **`"game_thread_stalled"` (bool) is OPTIONAL, and its absence is meaningful.** It rides every
  **success** envelope, but only when the DLL can actually measure it — i.e. once a ProcessEvent
  hook is installed. **Present ⇒ a measurement.** Absent ⇒ either the DLL cannot tell (no hook
  yet, which is the normal state of a fresh connection because the hook installs lazily on the
  first invoke) or an older DLL that predates this. Error envelopes and push events have never
  carried it. ⚠ A client must treat absence on a success envelope as **withdrawing** any standing
  claim, not as "no news": the hook can go back down mid-session, and a banner raised by a real
  stall would otherwise stay up forever. It used to be stamped unconditionally as
  `!IsGameThreadResponsive()` — a *gate* predicate that maps "unknown" to "responsive" — so the
  wire asserted a healthy game thread nobody had measured. (STALLDEFAULT-2026-08-26)
  `get_diagnostics.game_thread.liveness` carries the same fact in three states
  (`"responsive"` / `"stalled"` / `"unknown"`); `game_thread.responsive` beside it is the legacy
  gate predicate and is kept unchanged for older clients.

-----

## Commands (UI → DLL)

### Initialization & Info

```jsonc
// Initialize — returns UE version; DLL runs AOB scans internally on startup
{ "id": 1, "cmd": "init" }

// Get global pointer addresses
{ "id": 2, "cmd": "get_pointers" }

// Get total object count
{ "id": 3, "cmd": "get_object_count" }

// Get dynamically-detected DynOff values (diagnostics)
{ "id": 4, "cmd": "get_offsets" }
```

### Object Enumeration

```jsonc
// Paginated object list — advance by "scanned", not objects.length
{ "id": 5, "cmd": "get_object_list", "offset": 0, "limit": 200 }

// Single object detail
{ "id": 6, "cmd": "get_object", "addr": "7FF123456789" }

// Find object by full path
{ "id": 7, "cmd": "find_object", "path": "/Game/BP_Player.BP_Player_C" }

// Reverse address lookup: given any address, find the containing UObject
{ "id": 8, "cmd": "find_by_address", "addr": "7FF123456789" }

// Reverse reference scan (Find Refs v3): who holds a UObject* pointing at addr?
// Covers direct Object/Class/Interface, Weak/Soft/Lazy, OptionalProperty<pointer>,
// Delegate / MulticastInline / MulticastDelegate, TArray of all of those,
// TMap<UObject*, V> / TMap<K, UObject*>, TSet<UObject*>. Excludes
// MulticastSparseDelegate (storage is external to the field).
{ "id": 30, "cmd": "find_refs_to_uobject", "addr": "7FF123456789", "max_results": 32 }

// Forward path search ("Locate in GWorld"): shortest pointer chain GWorld → target.
{ "id": 31, "cmd": "find_path_from_gworld", "target": "7FF123456789", "object_addr": "7FF123456789", "max_depth": 5 }

// Related-object graph ("Related Objects" panel): an object's class/outer, its
// Controller<->Pawn counterpart, and the sub-objects it OWNS (components, GAS
// AbilitySystemComponent → AttributeSets). Forward owned walk up to depth 3.
{ "id": 32, "cmd": "get_related_objects", "addr": "7FF123456789", "max_results": 128 }

// Current-target auto-detect ("Related Objects" Phase 2, Edel): resolve
// GWorld → PlayerController → Pawn and return a ranked list of candidate
// "current target" actors (best-first; top is the auto-pick).
{ "id": 33, "cmd": "get_current_target", "max_candidates": 8 }
```

### Class & Instance Walking

```jsonc
// Walk all FFields of a UClass (static schema, no instance required)
{ "id": 9, "cmd": "walk_class", "addr": "7FF123456789" }

// Walk all UFunctions of a UClass (returns signatures + struct sub-field layouts)
{ "id": 20, "cmd": "walk_functions", "addr": "7FF123456789" }

// Walk live field values of a UObject instance
// class_addr is optional (auto-resolved from UObject::ClassPrivate)
// array_limit: max inline array elements (default 64)
// preview_limit: max struct sub-fields in preview (0=none, default 2, max 6)
{ "id": 10, "cmd": "walk_instance", "addr": "7FF123456789" }
{ "id": 10, "cmd": "walk_instance", "addr": "7FF123456789", "class_addr": "7FF...", "preview_limit": 2 }

// Walk GWorld → PersistentLevel → Actors
{ "id": 11, "cmd": "walk_world" }

// Find all instances of a class by name. `limit` (default 500, clamped 1..50000)
// caps the RETURNED list; the GObjects scan is always exhaustive. `exclude_classes`
// (optional) skips those classes server-side BEFORE the cap (the class-noise
// filter), so a wanted instance that today sits past the cap survives once the
// noise classes ahead of it are excluded.
{ "id": 12, "cmd": "find_instances", "class_name": "BP_Player_C", "limit": 5000, "exact_match": false, "newest_first": false, "exclude_classes": ["StaticMeshActor", "NiagaraActor"] }
```

### Array Reading

```jsonc
// Read array elements (paginated) — Phase B+ for scalar/pointer/struct arrays
{
  "id": 13, "cmd": "read_array_elements",
  "addr": "7FF6BB123000",         // UObject instance address
  "field_offset": 256,             // byte offset of the TArray field within the instance
  "inner_addr": "7FF601234560",   // FProperty* of the inner element type
  "inner_type": "FloatProperty",
  "elem_size": 4,
  "offset": 0,                    // pagination start
  "limit": 64                     // max elements to return
}
```

### Memory Access

```jsonc
// Read raw memory (returns hex string)
{ "id": 14, "cmd": "read_mem", "addr": "7FF123456789", "size": 256 }

// Write raw memory
{ "id": 15, "cmd": "write_mem", "addr": "7FF123456789", "bytes": "3F800000" }

// Subscribe to address for periodic push (Live Watch)
{ "id": 16, "cmd": "watch", "addr": "7FF123456789", "size": 4, "interval_ms": 500 }

// Unsubscribe
{ "id": 17, "cmd": "unwatch", "addr": "7FF123456789" }
```

### Search & Enumeration

```jsonc
// Global keyword object search. "query" is space=AND — each whitespace-separated
// term must match the object name OR the class name (case-insensitive). Optional
// "instances_only": true (default false) hides the reflection/type layer (UClass,
// UFunction, UScriptStruct, UEnum, UPackage, UE4 FooProperty) so only live instances
// return. Response adds "truncated": true when the "limit" cap was hit.
{ "id": 19, "cmd": "search_objects", "query": "BP_ Enemy", "limit": 5000, "instances_only": true }

// Search properties by name across all classes.
// Optional "deep": true (default false) also descends into nested struct members
// + struct-typed container elements (TArray/TSet<FStruct>, TMap<K,FStruct>); such
// matches set "is_nested": true and carry a dotted path in "prop_name"
// (e.g. SaveSlotList[].MsTuneData.GP).
//
// Each match may carry "bool_mask" (uint8): the FBoolProperty FieldMask, i.e. the
// single bit this bool owns inside the byte at "prop_offset". EMITTED ONLY when
// non-zero, and the DLL only sets it after reading FieldSize == 1 with a
// single-bit mask — so its PRESENCE means "packed bitfield (`uint8 bFoo:1`, up to
// 8 per byte)" and its ABSENCE means "native bool, owns its whole byte". Because
// it is only reported for a FieldSize == 1 property, the bit is always in the byte
// at "prop_offset"; there is no ByteOffset to accompany it. Populated during the
// class field walk, so it is present on the batch (no-preview) path too.
// A client that writes a bool MUST do a masked read-modify-write when this is
// present — stamping the whole byte clobbers up to 7 sibling bools and, unless the
// mask is 0x01, never sets the intended one. (audit #5 AA1)
{ "id": 20, "cmd": "search_properties", "query": "Health", "limit": 100, "deep": false }

// List all classes (UClass objects). Response adds "truncated": true when the walk
// STOPPED at "limit" instead of reaching the end of GObjects — the list is then a PAGE,
// not the pool. Callers that resolve a class BY NAME out of it must surface this: a class
// past the cap is otherwise indistinguishable from one that does not exist, which is what
// made the UI report "Class X not found" about a class it was displaying (audit #5 X2).
// Note "total_classes" is NOT a pool size — it moves in lockstep with the returned
// results, so on a truncated walk it equals "total".
{ "id": 21, "cmd": "list_classes", "limit": 500 }

// List all enum definitions
{ "id": 22, "cmd": "list_enums" }
```

### DataTable

```jsonc
// Walk DataTable rows (RowMap probe, returns row keys + addresses)
{ "id": 23, "cmd": "walk_datatable_rows", "addr": "7FF123456789" }
```

### Rescan & Scan Control

```jsonc
// Trigger background rescan of global pointers (non-blocking)
{ "id": 24, "cmd": "rescan" }

// Query rescan progress
{ "id": 25, "cmd": "rescan_status" }

// Apply rescanned pointers (replaces current GObjects/GNames/GWorld)
{ "id": 26, "cmd": "apply_rescan" }

// Trigger full re-initialization (proxy DLL deferred scan)
{ "id": 27, "cmd": "trigger_scan" }

// Query scan progress feedback
{ "id": 28, "cmd": "scan_status" }
```

### CE Export

```jsonc
// Get CE-compatible XML pointer chain for an instance
{ "id": 18, "cmd": "get_ce_pointer_info", "addr": "7FF123456789", "class_addr": "7FF..." }
```

### UFunction Invocation

```jsonc
// Invoke ProcessEvent via pipe (bypasses CE executeCodeEx)
{ "id": 42, "cmd": "invoke_function", "func_name": "Attack", "instance_addr": "0x7FF...", "parms_size": 16, "params_hex": "3F800000" }
```

### Debug Camera (robust force on/off)

Shared by the Console panel (here) and CE Lua (`setDebugCamera` export). All logic — two-hop state read, ToggleDebugCamera invoke, and controller-swap fallback for Shipping builds that strip `DisableDebugCamera` — lives in the DLL (`UE5_GetDebugCameraState` / `UE5_SetDebugCamera`). `state`: `1` = ON, `0` = OFF, `-1` = unknown/error.

```jsonc
// Read live state
{ "id": 50, "cmd": "get_debug_camera_state" }
// → { "ok": true, "state": 1 }

// Force ON (enable:true) / OFF (enable:false); idempotent
{ "id": 51, "cmd": "set_debug_camera", "enable": false }
// → { "ok": true, "state": 0 }
```

### Value Search (build 738 + Phase 2 build 757)

CE-style First Scan / Next Scan workflow over UPROPERTY fields. Three commands form a session: `begin_value_scan` opens, `refine_value_scan` narrows, `end_value_scan` closes. Sessions auto-expire after 5 min idle.

```jsonc
// First Scan — open a new session, return enriched candidates.
// data_type: Int8/Int16/Int32/Int64/UInt8/UInt16/UInt32/UInt64/Float/Double/Bool
//          | FString/FName/FText           (Phase 2A — case-insensitive default)
//          | FVector/FRotator/FTransform   (Phase 2B — FTransform reserved, 0 hits pending Translation offset)
// scan_type for numeric/vector: Exact/Bigger/Smaller/Between
// scan_type for string:         Exact/Contains/StartsWith/EndsWith
// value:    string-encoded target (e.g. "100", "3.14", "true", "Engine", "100,200,300" CSV for vectors)
// value2:   second target for Between (numeric/vector only)
// rounding_mode: "Round"(default)/"Trunc"/"Ceil" — how a float/double value is reduced
//          to the integer the game DISPLAYS before compare (Round=half-away, Trunc=toward
//          zero, Ceil=up); also coerces a FRACTIONAL target/bound on an integer field.
//          Omitted when "Round". Replaced the old float ± "tolerance" (build 1672).
// case_sensitive: string types only — omitted unless true (CE-style default is insensitive)
// parallel: omitted unless false. false forces a single-threaded GObjects walk
//           (slower, but avoids the burst of concurrent cross-thread reads some
//           games' anti-tamper flags). First Scan only — refine is always serial.
// batch_read: omitted unless false. false forces one SEH read per field instead
//           of one per-object body read (the DLL default, which is faster — fewer
//           reads + better locality, with automatic per-field fallback).
{
  "id": 50, "cmd": "begin_value_scan",
  "data_type": "FString",
  "scan_type": "Contains",
  "value":     "Engine",
  "game_only": true,
  "max_results": 50000,
  "case_sensitive": false,      // optional, string types only
  "parallel": false,            // optional, default true (omitted when parallel)
  "batch_read": false,          // optional, default true (omitted when batching)
  "native_c": true,             // optional (P1), default false; see below
  "native_align": 4,            // optional (P1), stride 1/2/4/8, default 4
  "newest_first": true,         // optional (P1), default false; see below
  "deadline_ms": 30000          // optional, default 15000; see below
}

// Next Scan — refine candidates in an open session.
// scan_type may switch between targeted (Exact/Contains/...) and prev-value
// (Changed/Unchanged/Increased/Decreased). value/value2 omitted for prev-value types.
{
  "id": 51, "cmd": "refine_value_scan",
  "session_id": 1234,
  "scan_type":  "Decreased"
}

// End — drop the session. Idempotent (returns ok=true even when already expired).
{ "id": 52, "cmd": "end_value_scan", "session_id": 1234 }
```

**Wire-shape contract** (locked by tests):
- `rounding_mode` is attached only when **not** `"Round"` (the default). When absent the DLL applies Round, so pre-build-1672 clients (no field) keep the historical half-away behavior. Per-slot on group scans; top-level on single. The old float `tolerance` field is gone — value/group scans no longer carry it.
- `case_sensitive` is attached only when true AND the data type is FString/FName/FText.
- `parallel` is attached only when **false** (the DLL default is true / full parallel). `false` caps the GObjects walk to one worker thread; the UI exposes it as the default-ON "Parallel scan" toggle for anti-tamper-sensitive games.
- `batch_read` is attached only when **false** (DLL default true). `false` forces one SEH read per field; default batches each object's fixed-width leaf fields into a single body read (per-thread reused buffer, span-capped, with per-field fallback on fault). Strings + container data are always read directly. UI = default-ON "Batch read" toggle.
- `(data_type, scan_type)` combinations are validated server-side by `IsScanTypeValidFor` — `FString + Bigger` or `Int32 + Contains` return an explicit error rather than running with garbage semantics.
- `deep` (build 1283) is attached only when **true** (default off). It forces the recursive deep-container leaf pass on every class — reaching values buried inside deeply-nested containers (struct arrays, struct-valued maps, nested `TArray`/`TSet`) that the auto `needsDeepWalk` heuristic doesn't flag. Heavier per object; the UI exposes it as the default-OFF "Deep" toggle.
- `native_c` (P1, Native-C value scan) is attached only when **true** (default off). It additionally scans each object's **unmanaged holes** — the byte ranges within `[UObject header, class PropertiesSize)` that no UPROPERTY covers — for the requested value at the user's width, so native (non-UPROPERTY) C++ members (HP/MP) are findable. Numeric/multi-numeric data types only (a no-op for string/vector/bool). `native_align` (default 4, values 1/2/4/8) is the stride for sliding within each hole. Matching candidates carry `is_native_c: true` + `guessed_type` (the interpreted width, e.g. `"Int32"`). Intentionally noisy on first scan — pair with `newest_first` + Next-Scan refine. See [native-c-value-scan-spec.md](native-c-value-scan-spec.md).
- `newest_first` (P1) is attached only when **true** (default off). It walks GObjects high-index-first so that when results hit `max_results` the survivors are the most-recently-allocated instances (a just-spawned pawn) rather than low-index CDOs/templates. Applies to the whole scan (reflected + native); affects only which matches survive truncation. The UI auto-checks it when `native_c` is enabled (the user can uncheck it).
- `deadline_ms` (scan wall-clock budget) is attached only when **≠ 15000** (the DLL default). When the GObjects walk exceeds it the scan bails early, sets `deadline_hit: true`, and returns whatever matched so far. The DLL clamps the value to **[1000, 300000]** ms. The UI exposes it as the Value Search "Timeout" slider (10–60 s); raise it for huge games (400K+ objects) that keep hitting the deadline. Applies to `begin_value_scan` and `begin_group_scan`; refine re-reads only the existing candidates and is unaffected. Older DLLs that don't read the field simply use the fixed 15 s.
- **Class-noise filter (P2, server-side).** Because the candidate set is windowed (the DLL owns it, the UI sees one page), the class-distinct "noise picker" runs server-side:
  - `begin_value_scan` / `refine_value_scan` / `begin_group_scan` / `refine_group_scan` responses carry **`class_histogram`** — a Top-40 array `[{ "class_name": "...", "count": N }, …]` tallied over the **FULL session** (pre-filter, pre-exclude; sorted count-desc, name-asc) — plus **`class_distinct`** (the true distinct-class count, ≥ the array length when capped). For group scans the bucket is the candidate's **object-level class** (the first non-empty slot's match class — NOT per-slot `owner_class`). Refine recomputes it over the pruned survivor set. The UI's "Class filter" picker is built from this; older DLLs that omit it just show no picker.
  - `query_candidates` / `query_group_candidates` accept **`exclude_classes`** — a string array of class names to hide. Attached only when **non-empty** (omitted = no exclusion, so the common page stays byte-identical for the view cache). The DLL skips candidates whose owning class (group: object-level class) is in the set when building the ordered view, so `filtered_total` reflects post-filter **and** post-exclude. The exclusion is reversible (it never prunes the session) and folds into the per-session view cache key, so toggling re-windows without a re-scan.

#### `detect_noise_classes` (P3, opt-in safe auto-detect)

Classifies a set of class names as engine/system "noise" so the class-noise picker's **Auto-detect** button can pre-tick them. Shared by every panel that hosts the picker (Instance / Interesting Funcs+Props / Property Search / Value Search). Pre-tick only — the UI never auto-prunes, and the picks are reversible.

```jsonc
{ "id": 70, "cmd": "detect_noise_classes",
  "class_names": ["WBP_HUD_C", "BP_Enemy_C", "SoundCue", "Texture2D"] }
// → { "ok": true, "classes": [
//      { "class_name": "WBP_HUD_C",  "is_noise": true,  "reason": "engine base class" },
//      { "class_name": "BP_Enemy_C", "is_noise": false, "reason": "" },
//      { "class_name": "SoundCue",   "is_noise": true,  "reason": "engine base class" },
//      { "class_name": "Texture2D",  "is_noise": true,  "reason": "engine package" } ] }
```

A class is `is_noise: true` **only** by safe-by-construction rules: (a) it lives in an engine package (`Aura::IsEnginePackage` on the class full path — `/Script/Engine`, `/UMG`, `/Slate`, `/Niagara`, `/AudioMixer`, …), or (b) its super-chain reaches a pure-engine **leaf base** that structurally cannot hold gameplay save data — `Widget`/`UserWidget`, `SoundBase`, `Texture`, `MaterialInterface`, `ParticleSystem`, `NiagaraSystem`, `AnimInstance`. It **never** uses class-name substrings and **never** flags `ActorComponent` descendants (gameplay HP/MP lives there) — both documented hard bans. One GObjects pass resolves the names to UClasses (metaclass-gated, de-duped); unresolved names come back `is_noise:false`. The C# client omits the call entirely when `class_names` is empty.

### Multiple Values Group Scan (build 1276)

Object-aware "group scan": find objects (blocks) that **simultaneously** hold ALL of N values (2..4) at **distinct** numeric-property offsets, in any order (the object/schema-aware analogue of Cheat Engine's Group Scan). Far more selective than N separate single-value scans — matching e.g. Str + Def + Dex narrows thousands of hits to a handful. A separate session family (`GroupSessionManager`, same 5-min idle expiry). Each slot is a `NumericNoByte`/`NumericAll` match over direct numeric properties (+ one-level StructProperty descent; deep containers via `deep`). P2 (build 1296): each slot carries its own `scan_type` — see below. Numeric containers + GAS attribute-component cross-object reach are later phases.

```jsonc
// First Scan — open a group session. `values` carries 2..4 slots; each slot's
// data_type defaults to "NumericNoByte" (fans out over int16/int32/int64/float/
// double widths) and may be "NumericAll" (adds 1-byte). scan_type (P2, default
// "Exact") is a first-scan targeted predicate: Exact / Bigger / Smaller / Between.
// Between carries an upper bound in `value2` (the bounded-unknown entry point —
// e.g. an HP bar you know is in [1,100] but whose exact value you don't).
{
  "id": 60, "cmd": "begin_group_scan",
  "game_only": true, "max_results": 50000, "page_size": 1000,
  "deadline_ms": 30000,                                 // optional, default 15000 (clamp 1000..300000); see begin_value_scan
  "deep": false,                                        // optional (build 1283); see below
  "cross_object": false,                                // optional (P4, build 1303); see below
  "native_c": false,                                    // optional (P2): fold each object's
                                                        //   unmanaged-hole leaves into its block —
                                                        //   object block only, <=64 raw leaves/obj,
                                                        //   stride 4, slot-width union. Matching slots
                                                        //   carry is_native_c + guessed_type. Noisy on
                                                        //   first scan; prefer distinctive values.
  "values": [
    { "value": "24", "data_type": "NumericNoByte" },
    { "value": "10", "scan_type": "Bigger" },           // scan_type optional -> "Exact"
    { "value": "1",  "scan_type": "Between", "value2": "100" },  // 1 <= leaf <= 100
    { "value": "8"  }
  ]
}
// `deep` (default off): also treat each numeric CONTAINER as its own block — a
// numeric TArray/TSet's elements, or each struct-array/map element's inner numeric
// fields — and match the group WITHIN one array/element. Finds groups hidden in
// deeply-nested containers (e.g. SaveSlotList[1].MsTuneData.MsTunes[0].
// WeaponTuneList[0].Tunes[N]). Attached only when true. A deep candidate's slot
// `field_name` is the fully-indexed path and `addr` is the absolute element address.
//
// `cross_object` (P4, default off): also fold the numeric leaves of the sub-objects
// each actor OWNS into the actor's block (a bounded 2-level owned BFS: depth 1 =
// components, depth 2 = a GAS ASC's SpawnedAttributes -> UAttributeSet), so a group
// whose values are split across {actor, components, attribute sets} is found.
// Ownership-gated (the sub-object's Outer must chain back to the actor); selectivity
// is the value AND, not a class-name filter. A cross-object slot's `field_name` is
// the path (e.g. "HealthComp.CurrentHealth"), `owner_addr` is the owning sub-object,
// and `owner_class` (P4 inc 2) is that sub-object's class (drives the Pivot handoff).

// Next Scan — re-target every slot (count MUST match the first scan). Survivors
// are objects where every slot still matches at a distinct offset; the per-slot
// matched-offset list narrows toward a single "locked" offset. P2: each slot's
// scan_type may be a targeted type (Exact/Bigger/Smaller, with a new value, or
// Between with value+value2) OR a prev-value type (Changed/Unchanged/Increased/
// Decreased) — those compare each located leaf against ITS value from the previous
// round and need no value. Substring predicates are rejected. (data_type is fixed.)
{ "id": 61, "cmd": "refine_group_scan", "session_id": 99,
  "values": [ {"value":"24"}, {"scan_type":"Increased"}, {"value":"1","scan_type":"Between","value2":"100"}, {"scan_type":"Unchanged"} ] }

// Window query (server-side filter/sort/page over the OBJECT-level rows).
// sort_key: "" / "scan" / "class" / "instance" / "value" (first slot) / "offset" (first slot).
// `filter` is SPACE = AND (build 2719): whitespace-separated terms are ANDed, each may
// match a different field (class / instance / field name / value). This is also how a
// caller asks for a SPECIFIC pairing — "tickcount frozenint" both keeps the candidate
// AND puts one named field in each slot's displayed witness. Before it, the filter was
// one substring, so a two-field request could not be expressed at all.
{ "id": 62, "cmd": "query_group_candidates", "session_id": 99,
  "offset": 0, "limit": 1000, "filter": "", "sort_key": "class", "sort_desc": false }

// End — drop the session (idempotent).
{ "id": 63, "cmd": "end_group_scan", "session_id": 99 }
```

**`per_slot_cap_hit` / `per_slot_cap` (build 3266)** — on the responses to **all three** of
`begin_group_scan`, `refine_group_scan` and `query_group_candidates`.

- `per_slot_cap_hit` (bool): at least one block had MORE leaves satisfying a slot than the cap
  kept, so that slot's witness list — the `(+N)` annotation, the `query_group_slot_leaves`
  answer, and what a later prev-value refine can re-read — is a **page**, not the whole match.
- `per_slot_cap` (int): the cap the DLL actually applied, **after** its own 8–4096 clamp. The
  client cannot derive it: `per_slot_cap` is omitted from the request when it equals the UI
  default, so the request does not name the number the server used.

Both are **inherited by the session**, not recomputed: a refine prunes the stored pool and never
re-runs the matcher, and a window query is a pure projection, so a truncation that happened at
`begin` is invisible to either unless it is carried. Absent on an older DLL ⇒ parse as `false`/`0`
("no evidence of truncation"), which is the only claim a missing key supports.

*Why it exists:* this is a different fact from `deadline_hit`, and it is the one that explains the
report class this area keeps producing. `deadline_hit` bounds how many **objects** were examined;
`per_slot_cap_hit` bounds how many **witnesses** an examined object kept — so the result set is
complete while a slot's field list is not. `Orden::MatchGroup` has always computed it and
`ScanForValueGroup` only ever wrote it to `LOG_WARN`, where no user could see it (audit #5 AE13).

#### `query_group_slot_leaves` (build 2719) — name the fields a row cannot show

A candidate usually satisfies the group in **many** ways at once, and a row displays exactly **one** assignment (see `match_count` below). Every other matching field existed on the wire only as a raw integer inside `matched_offsets`, and an integer cannot tell anyone that offset `1308` is `FrozenInt` — so correctly-matched fields were repeatedly read as misses. This names them.

```jsonc
{ "id": 64, "cmd": "query_group_slot_leaves", "session_id": 99,
  "instance_addr": "0x112FBCB4600",  // the candidate's owning UObject
  "slot_index": 1,                   // 0-based; which value's kept fields to name
  "leaf_addr": "0x112FBCB4B04",      // optional tie-breaker — SEND IT (see below)
  "offset": 0, "limit": 4096 }       // optional; server ceiling 4096
// → { "ok": true, "session_id": 99, "instance_addr": "0x112FBCB4600", "slot_index": 1,
//     "total": 36, "offset": 0, "count": 36,
//     "leaves": [ { "field_name": "FrozenInt", "field_offset": 1308,
//                   "field_type": "IntProperty", "bool_field_mask": 255,
//                   "leaf_value": "424242", "addr": "0x112FBCB4B1C",
//                   "owner_addr": "0x112FBCB4600", "owner_class": "DumperTestActor" }, … ] }
```

- Each element is emitted by the **same encoder** as the row's representative leaf inside `candidates[].slots[]`, so the two can never disagree about a field's name, value or address.
- **Ordered: the object's OWN declared fields first**, inherited/struct-nested after (`Radar::OrderGroupSlotLeaves`, stable within each tier so scan order survives). Leaves are collected base-class-first, so without this an actor's list opens with `PrimaryActorTick.*` / `InitialLifeSpan` / `CustomTimeDilation` / `AttachmentReplication.*` and the field the user came for sits thirty rows down.
- **On demand only.** A page carries up to `limit` (default 1000) candidates × N slots × `per_slot_cap` (up to 4096) leaves; inlining them is a non-starter. Because the request ceiling equals `per_slot_cap`'s maximum, a slot's whole list always fits in one response — `offset` / `limit` are a server-side bound, not a paging protocol the client must loop.
- **`leaf_addr` matters with `deep`.** One UObject owns one candidate *per container block* and they share an instance address, so `instance_addr` alone is ambiguous and the server would answer with whichever block it met first. Send the displayed leaf's `addr`; a leaf address belongs to exactly one block. (A candidate *index* would not work — `refine_group_scan` rebuilds the candidate vector.) Omitted ⇒ first match wins. If the hint matches nothing the row is stale (a refine dropped that leaf): with a single candidate at that address the fallback is exact, but where several share it the server returns **`stale_leaf_addr`** rather than guessing — re-query the row and retry.
- Errors: `session_not_found`, `candidate_not_found`, `stale_leaf_addr`, `slot_index out of range`, plus the malformed-argument cases.
- No game memory is read: every field is already resident in the session. The query does not rebuild or invalidate the cached ordered view.
- UI: the **All fields** button in the group row's expanded details (Value Search → Group mode).

A group candidate is **object-level** with nested per-slot matches:

```jsonc
{
  "instance_addr": "7FF6..A0", "instance_index": 12345, "instance_name": "BP_PlayerStats_C_0",
  "class_name": "BP_PlayerStats_C", "defining_class_name": "...",
  "slots": [
    { "slot_index": 0, "value": "24", "scan_type": "Exact",
      "field_name": "Str", "field_offset": 32, "field_type": "IntProperty",
      "bool_field_mask": 255, "leaf_value": "24", "addr": "7FF6..C0",
      "owner_addr": "7FF6..A0",                         // own-block leaf -> owner == the candidate actor
      "owner_class": "BP_PlayerStats_C",               // ...so owner_class == class_name here
      "matched_offsets": [32], "locked": true,        // locked once a single offset remains
      "match_count": 1 },                              // (2690) how many fields this slot really kept
    { "slot_index": 1, "value": "10", "scan_type": "Exact",
      "field_name": "HealthComp.CurrentHealth",        // cross_object leaf: path from the actor
      "field_offset": 64, "field_type": "FloatProperty",
      "leaf_value": "10", "addr": "1AD0..40",
      "owner_addr": "1AD0..00",                         // the OWNED sub-object holding the leaf (handoffs open it)
      "owner_class": "UHealthComponent",               // (P4 inc 2) the sub-object's class -> drives the Pivot handoff
      "matched_offsets": [64], "locked": true }
  ]
}
```

**`field_name` / `leaf_value` on a slot are ONE WITNESS, not the whole match.** A slot keeps every field that satisfied it (up to `per_slot_cap`, default 256, clamp 8–4096 on `begin_group_scan` — that kept list is also what a later prev-value refine re-reads). The row picks one leaf per slot such that no two slots show the same one, preferring a field from the same struct and, when a filter is active, the field that matched it (`Radar::PickGroupWitnessAssignment` — deliberately in `Radar`, beside the filter it must agree with). **`match_count` (build 2690) says how many that one field is standing in for**, and `query_group_slot_leaves` (above) names the rest. `leaf_value` is the last-scanned snapshot of the leaf, not a live read.

`scan_type` echoes each slot's stored predicate (`Radar::NameOf(ScanType)`); a prev-value slot carries an empty `value` and its `leaf_value` is the current bytes; a Between slot additionally echoes `value2` (the upper bound). `addr` / `field_offset` / `field_name` on each slot drive the same Live Walker / Locate-in-GWorld / Copy handoffs as a single-value candidate. `owner_addr` (P4) is the object directly holding the leaf — the candidate actor for an own-block leaf, or an owned sub-object for a cross-object leaf; the per-slot handoffs target it. `owner_class` (P4 inc 2) is that owning object's class (== `class_name` for an own-block leaf, the owned sub-object's class for a cross-object leaf) and drives the per-slot **Pivot** handoff so it lands on the class that declares the field, not the actor. The object's `class_name` drives Instance Finder / Class Pivot at the candidate level. Once every slot's `locked` is true the UI shows the **locked-offset table** (class + each value's offset).

### Snapshot Capture (experimental — Phase A)

Type-agnostic streamed capture of every numeric UPROPERTY of every (scoped) UObject, for the experimental Snapshot / SPC / Pivot tabs. **Stateless cursor pagination** (mirrors `get_object_list`): no server-side session. `begin_snapshot` returns the total object count for a progress bar; `snapshot_chunk` streams `[offset, offset+limit)` objects. Advance `offset` by the returned `scanned` (indices iterated), NOT by `objects.length` (objects with zero numeric fields are skipped). Phase A1a captures scalar numeric fields only; array elements arrive in A1b.

```jsonc
// Begin — validate scope, return total object count.
// data_type: NumericNoByte (default) | NumericAll. Must be a multi-numeric
//            meta type; the structured walk compares each field by its own
//            declared width (no byte-reinterpret). NumericNoByte excludes
//            1-byte families to avoid flooding.
{ "id": 60, "cmd": "begin_snapshot", "data_type": "NumericNoByte" }

// Chunk — stream the next window of objects.
// array_cap bounds struct-array elements captured per array (default 256).
{ "id": 61, "cmd": "snapshot_chunk",
  "data_type": "NumericNoByte",
  "game_only": true,
  "offset":    0,
  "limit":     100,
  "array_cap": 256,
  "native_c":  false,    // optional (P3): also capture each object's unmanaged-hole
                         //   guesses as synthetic "<raw@0xNN>" fields (Guess-What +
                         //   normalize to canonical type; pointer/padding dropped),
                         //   so SPC Query / Class Pivot can track native values.
  "auto_skip_noise": true }  // optional, DLL default false (UI sends true by default):
                         //   skip pure engine/system classes (UI widgets, textures,
                         //   sounds, Niagara, anim instances, /Script engine packages)
                         //   at CAPTURE time so they never enter the snapshot — faster
                         //   capture + smaller DB. A gameplay guardrail force-keeps
                         //   Actor/Pawn/Character/component-derived classes (a player
                         //   Pawn's X/Y/Z is never dropped). Mirrors ClassifyNoiseClasses.
```

Each chunk object may also carry an `arrays` field (Phase A1b) — struct-array
inner-key capture for cargo/inventory cases. Each element has an inner key
(`key_name`/`key_value`, e.g. `ItemID`=`Fuel`) so the same logical slot joins
across snapshots regardless of reordering, plus its numeric inner fields:

```jsonc
"arrays": [
  { "field": "Cargo",
    "elements": [
      { "i": 0, "key_name": "ItemID", "key_value": "Fuel",
        "fields": [ { "name": "Quantity", "off": 8, "type": "IntProperty", "hex": "64000000" } ] }
    ] }
]
```

### Live ProcessEvent Profiler (Linie — build 2103)

Behaviour-based UFunction discovery: record which UFunctions the game dispatches through `ProcessEvent` during a Start/Stop window, then rank by fire count. Pipe-only (no Mimic/CE-Lua mailbox). Counting is gated by an atomic in the Stark hook, so the not-recording path is free; the recording table + mutex live in the `Linie` module. State is dropped on client disconnect.

```jsonc
// Start — force the game-thread PE hook to install (so the game's own PE calls
// are counted without first issuing an invoke), then begin recording. Clears any
// prior table. Response: recording:true, hook_active (false ⇒ PE-vtable detection
// failed on this game → counts will stay 0; still ok:true, a domain state not an error).
{ "id": 70, "cmd": "pe_profile_start" }

// Stop — freeze the table (idempotent). Counts are retained for pe_profile_get.
{ "id": 71, "cmd": "pe_profile_stop" }

// Get — snapshot + rank by fire count desc, cap to `limit` (default 200), resolve
// each UFunction* to its name/class at query time (stale/recycled pointers dropped
// via a "Function" meta-class guard). Safe to call while recording (live peek).
{ "id": 72, "cmd": "pe_profile_get", "limit": 200 }
```

Response for `pe_profile_get`:

```jsonc
{ "id": 72, "ok": true,
  "recording":      false,   // still recording?
  "distinct_funcs": 214,     // distinct UFunctions seen (pre-cap)
  "total_calls":    98213,   // sum of all fire counts
  "functions": [
    { "class_name": "AShopVendor", "func_name": "OpenShop",
      "func_addr": "0x1B2C3D40", "num_parms": 1, "parms_size": 8, "count": 3,
      "first_seq": 40,   // call-stream position of the FIRST fire (1-based). Causal
                         // signal: an entry point fires before the reactions it triggers,
                         // so sorting NEW rows by first_seq asc floats the true opener up.
      "function_flags": 67108864,  // UFunction::FunctionFlags — UI tags Event/Delegate
                                   // (a reaction) vs Call (an imperative entry point).
      "is_widget": false }   // owning class derives from UUserWidget/UWidget — the
                             // transient UI created BY the action, not its opener; the
                             // UI can hide these so the persistent opener surfaces.
    // ... ranked by count desc, capped at `limit`
  ] }
```

-----

### Force-field hold + stealth meter (Solide — build 2168)

Hold a *discovered* reflected field at a value across **all live instances** of a
class via a write-on-drift re-assert worker (the honest subset of "enemies can't
detect you" — there is no universal detection bool). Pipe-only.

**`class_name` means that class AND every subclass of it** (build 3036, audit #5 A6).
It has to: a Property Search row for an *inherited* field is keyed to the class that
DECLARES the field, so an exact-name pool for e.g. `"Actor"` resolved essentially
nothing and the hold silently held nothing. This is a **derivation** test on the UClass
super chain, NOT a name-substring match — `"Enemy"` does not capture `"EnemyProjectile"`
unless that class genuinely derives from `Enemy`.

Two consequences worth planning for: class-default objects are excluded, and the pool is
capped (`SOLIDE_MAX_INSTANCES` = 256), which a broad base class reaches easily —
`get_forced_fields` / the `force_field` reply carry `truncated` for exactly that, and
`held` is then a floor rather than a total.

```jsonc
// Force a field on every live instance of a class (and its subclasses) and hold it.
// kind = "bool" (uses `on`) | "object_null" (value ignored — strong ObjectProperty
//        only; weak/soft/lazy refused) | "numeric" (uses `value`, absolute).
{ "cmd": "force_field", "class_name": "BP_Enemy_C", "field_name": "bInvincible",
  "kind": "bool", "on": true }
// → { "held": 3, "resolved": true, "code": 0 }   // held = live "N held" count (0 = matched nothing)
//   `"truncated": true` is added when the instance pool hit its cap.

// Release one hold (best-effort restore the captured base; object-null is not reversible).
{ "cmd": "reset_field", "class_name": "BP_Enemy_C", "field_name": "bInvincible" }
// → { "code": 0 }

{ "cmd": "reset_all_fields" }                      // → { "code": 0 }

// Snapshot the active holds + their live counts.
{ "cmd": "get_forced_fields" }
// → { "code": 0, "fields": [ { "class_name", "field_name", "kind", "value",
//        "held", "owner_addr"?, "field_offset"? } ] }

// Auto-find the player's stealth/noise/visibility/detection meter (read-only).
{ "cmd": "find_stealth_meter", "max": 8 }
// → { "code": 0, "candidates": [ { "class_name", "class_addr", "field_name",
//        "prop_type", "owner_addr", "current", "score" } ] }   // ranked, best first
```

-----

## Responses (DLL → UI)

### init

```jsonc
{ "id": 1, "ok": true, "ue_version": 507 }
// ue_version: 507=UE5.7, 505=UE5.5, 427=UE4.27, 422=UE4.22, etc.
```

### get_pointers

```jsonc
{
  "id": 2, "ok": true,
  "gobjects":     "7FF600A12340",
  "gnames":       "7FF600B56780",
  "gworld":       "7FF600C89ABC",   // may be "0" if not found
  "object_count": 58432,
  "module_name":  "MyGame-Win64-Shipping.exe",
  "module_base":  "7FF600000000",
  "process_creation_time": "01D9ABCDEF012345",  // per-launch token (FILETIME hi:lo hex); folded into the UI's GameSessionId for stale-session gating
  "ue_version":   504,
  "gobjects_method": "aob",         // "aob", "data_scan", "string_ref", "pointer_scan", "not_found"
  "gnames_method":   "string_ref",
  "gworld_method":   "not_found",
  // AOB Usage Tracking (added v1.1)
  "pe_hash":              "5F3A1B2CCDD40000",  // TimeDateStamp(8hex) + SizeOfImage(8hex)
  "gobjects_pattern_id":  "GOBJ_V1",           // winning pattern ID, "" if not AOB
  "gnames_pattern_id":    "",
  "gworld_pattern_id":    "",
  "scan_stats": {
    "gobjects_tried": 40,    // patterns evaluated
    "gobjects_hit":   3,     // patterns with >=1 match
    "gnames_tried":   27,
    "gnames_hit":     0,
    "gworld_tried":   37,
    "gworld_hit":     0
  }
}
```

### get_object_list

```jsonc
{
  "id": 5, "ok": true,
  "total":   58432,
  "scanned": 200,      // ← indices iterated; advance offset by this, NOT by objects.length
  "objects": [
    {
      "addr":  "7FF123456000",
      "name":  "BP_Player_C_0",
      "class": "BlueprintGeneratedClass",
      "outer": "7FF123400000"
    }
  ]
}
```

### begin_snapshot / snapshot_chunk

```jsonc
// begin_snapshot
{ "id": 60, "ok": true, "total": 58432 }

// snapshot_chunk — one entry per object with >=1 numeric field.
// "index" is the GObjects index (stable in-session join key). "path" is the
// full object path (cross-session identity; UI normalises the FName suffix).
// "off" is the field byte offset; "hex" is the little-endian raw bytes.
{
  "id": 61, "ok": true,
  "total":   58432,
  "scanned": 100,          // ← advance offset by this, NOT objects.length
  "objects": [
    {
      "index":       12345,
      "addr":        "0x7FF123456000",
      "name":        "BP_Player_C_0",
      "class":       "BP_Player_C",
      "outer_class": "World",
      "path":        "/Game/Maps/Map.Map:PersistentLevel.BP_Player_C_0",
      "fields": [
        { "name": "Health", "off": 720, "type": "FloatProperty", "hex": "0000C842" },
        { "name": "Ammo",   "off": 728, "type": "IntProperty",   "hex": "1E000000" }
      ]
    }
  ]
}
```

### walk_class

```jsonc
{
  "id": 9, "ok": true,
  "class": {
    "name":       "BP_Player_C",
    "full_path":  "/Game/BP_Player.BP_Player_C",
    "addr":       "7FF123456000",
    "super_addr": "7FF123450000",
    "super_name": "Character",
    "props_size": 1024,
    "fields": [
      {
        "addr":   "7FF601234000",
        "name":   "Health",
        "type":   "FloatProperty",
        "offset": 720,
        "size":   4,
        "prop_flags": "0x0040000000000001"
      },
      {
        "addr":   "7FF601234020",
        "name":   "Inventory",
        "type":   "ArrayProperty",
        "offset": 728,
        "size":   16
      }
    ]
  }
}
```

Extended per-field keys are emitted **only when non-default**: `struct_type`,
`obj_class`, `inner_type`, `inner_struct_type`, `inner_obj_class`,
`key_type`/`key_struct_type`, `value_type`/`value_struct_type`,
`elem_type`/`elem_struct_type`, `enum_name`, `bool_mask`, plus **`prop_flags`**
(uint64 `CPF_*` reflection flags — `SaveGame`/`BlueprintVisible`/`Net`/
`Transient`/`EditConst`/… — as an `"0x…"` hex string, omitted when 0) and
**`array_dim`** (static C-array dimension `Type Foo[N]`, omitted when 1). The
full field footprint is `size * array_dim`. `walk_class_batch` emits each class
object through the same serialiser, so these keys appear identically there and
in the `Dump All Metadata` JSONL. `search_properties` / `search_properties_batch`
match rows also carry **`prop_flags`** (same `"0x…"` hex form, omitted when 0) so
the Interesting Properties scorer can gate on `SaveGame`/`BlueprintVisible`/`EditorOnly`.

### walk_instance

Field objects include all `walk_class` fields **plus** live typed values and array element data.

```jsonc
{
  "id": 10, "ok": true,
  "addr":        "7FF6AA000000",
  "name":        "BP_Player_C_0",
  "class":       "BP_Player_C",
  "class_addr":  "7FF123456000",
  "outer":       "7FF6BB000000",
  "outer_name":  "ThirdPersonMap",
  "outer_class": "World",
  // "stale": true,   // present only when the class pointer looks recycled/garbage
                      // (PropertiesSize negative, or beyond
                      // kMaxPlausiblePropertiesSize = 64 MB). Returned with no fields +
                      // props_size omitted; the client must NOT retry fill_gaps (a
                      // bogus multi-hundred-MB size would wedge the pipe).
  // "gap_fill_skipped": true,
                      // present only when true, and only on a non-lean walk that
                      // ASKED for fill_gaps. The class is real and the fields above are
                      // complete — it is merely larger than kMaxGapFillBytes = 1 MB, so
                      // the Guess-What raw-byte pass was skipped. NOT staleness: the two
                      // used to share one bound, which is how a live 3.6 MB USaveGame
                      // was reported to the user as freed. (SANEPROPS-2026-08-26)
  "fields": [
    // --- Scalar field ---
    {
      "name":   "Health",
      "type":   "FloatProperty",
      "offset": 720,
      "size":   4,
      "hex":    "0000C842",
      "value":  "100.0000000000"
    },
    // --- BoolProperty (bit field) ---
    {
      "name":          "bIsDead",
      "type":          "BoolProperty",
      "offset":        724,
      "size":          1,
      "hex":           "00",
      "value":         "false",
      "bool_mask":     4,
      "bool_bit_idx":  2
    },
    // --- ObjectProperty (pointer) ---
    {
      "name":      "WeaponComponent",
      "type":      "ObjectProperty",
      "offset":    728,
      "size":      8,
      "hex":       "0050AA6F0C020000",
      "value":     "7FF20C6FAA5000",
      "ptr_name":  "BP_Weapon_C_3",
      "ptr_class": "BP_Weapon_C"
    },
    // --- EnumProperty ---
    {
      "name":       "MovementMode",
      "type":       "EnumProperty",
      "offset":     736,
      "size":       4,
      "hex":        "02000000",
      "value":      "2",
      "enum_name":  "EMovementMode::Walking"
    },
    // --- StrProperty (FString) ---
    {
      "name":       "PlayerTag",
      "type":       "StrProperty",
      "offset":     740,
      "size":       16,
      "hex":        "...",
      "str_value":  "Hero_01"
    },
    // --- ArrayProperty: scalar inner type (Phase B inline elements) ---
    {
      "name":             "DamageMultipliers",
      "type":             "ArrayProperty",
      "offset":           756,
      "size":             16,
      "hex":              "000001A0B4C00000 00000005 00000005",
      "count":            5,
      "array_inner_type": "FloatProperty",
      "array_elem_size":  4,
      "array_inner_addr": "7FF601234560",
      "elements": [
        { "i": 0, "v": "1.5000000000", "h": "0000C03F" },
        { "i": 1, "v": "2",            "h": "00000040" },
        { "i": 2, "v": "0.5000000000", "h": "0000003F" }
      ]
      // "elements" only present for scalar arrays with count <= 64
      // For enum inner type, each element also has "en": "EnumName::Value"
    },
    // --- ArrayProperty: NameProperty inner (Phase B) ---
    {
      "name":             "MissionIDs",
      "type":             "ArrayProperty",
      "offset":           772,
      "size":             16,
      "count":            30,
      "array_inner_type": "NameProperty",
      "array_elem_size":  8,
      "array_inner_addr": "7FF601234580",
      "elements": [
        { "i": 0, "v": "S001", "h": "..." },
        { "i": 1, "v": "S002", "h": "..." }
      ]
    },
    // --- ArrayProperty: struct inner type (no inline elements) ---
    {
      "name":                  "LevelCollections",
      "type":                  "ArrayProperty",
      "offset":                788,
      "size":                  16,
      "count":                 3,
      "array_inner_type":      "StructProperty",
      "array_inner_struct_type": "LevelCollection",
      "array_elem_size":       120,
      "array_inner_addr":      "7FF6012345A0",
      "array_inner_struct_addr": "7FF601234600"
      // no "elements" — Phase F scope
    }
  ]
}
```

#### Request options

| key | default | meaning |
|---|---|---|
| `class_addr` | resolved from the object | walk as this UClass/UScriptStruct |
| `array_limit` | 64 | max inline elements per container |
| `preview_limit` | 2 | max inline Map/Set entries |
| `fill_gaps` | false | "Guess What" — synthesise leaves for unreflected holes |
| `lean` | false | **omit the keys a CE XML export never reads** (see below) |

#### `lean: true` — the export payload shape (build 2351)

Measured ([multipipe-eval.md](multipipe-eval.md) §10.6): of a real Copy CE XML's
`walk_instance` bytes, the per-instance header is **99% dead** and per-field keys are
**16.7% unused / 18.6% CSX-only**, with inline array elements **44.6% unused** —
`elements[].h` alone is ~9% of the whole payload. `lean` drops exactly those, which
attacks the payload-proportional IPC *and* the UI-side JSON parse that batching
cannot touch.

It is **subtractive only** — a lean object is the full object minus keys, never a
different encoding. Consequences that matter: a client needs no new parsing branch
(a missing key already falls back to its default), and an **older DLL that does not
know the flag simply returns the full shape**, which is still correct.

Dropped when `lean` is set:

| level | keys |
|---|---|
| instance | `name`, `class`, `class_addr`, `outer`, `outer_name`, `outer_class`, `is_definition`, `props_size` (kept: `addr`, `stale`) |
| field | `hex`, `value`, `str_value`, `enum_name`, `enum_value`, `ptr_name`, `bool_mask`, `bool_byte_offset`, `array_inner_addr` |
| `elements[]` | `h`, `pn` |
| `elements[].sf[]` | `v`, `pn` |
| `map_elements[]` / `set_elements[]` | `kh` (**`vh` is kept** — the exporter parses it as a little-endian int for the value DropDownList) |

**Who may ask for it.** The CE XML export path only. CSX (Structure Dissect) and the
Live Walker grid genuinely read `hex` / `value` / `bool_mask` / `bool_byte_offset`, so
the shared resolver defaults to the full shape and only the CE XML callers opt in.
`WalkInstanceLeanTests` pins the contract by running the same export over full and
lean payloads and demanding byte-identical XML.

### walk_world

```jsonc
{
  "id": 11, "ok": true,
  "world_addr": "7FF6CC000000",
  "world_name": "ThirdPersonMap",
  "level_addr": "7FF6DD000000",
  "actors": [
    { "addr": "7FF6AA000000", "name": "BP_Player_C_0",  "class": "BP_Player_C"  },
    { "addr": "7FF6AB000000", "name": "BP_Enemy_C_0",   "class": "BP_Enemy_C"   }
  ]
}

// Partial success (GWorld null, UWorld found via GObjects fallback):
{ "id": 11, "ok": true, "world_addr": "...", "actors": [...], "error": "GWorld=0, found via GObjects fallback" }

// GWorld failure (CDO or no UWorld instance):
{ "id": 11, "ok": true, "actors": [], "error": "PersistentLevel is null (CDO or uninitialized)" }
```

### find_instances

```jsonc
{
  "id": 12, "ok": true,
  "total":     308,            // returned instance count (== instances.length, <= limit)
  "scanned":   58432,          // GObjects indices walked (full array)
  "non_null":  41020,
  "named":     40998,
  "truncated": true,           // more NON-EXCLUDED matches exist than the cap returned
  "instances": [
    {
      "addr":  "7FF6AA000000",
      "name":  "BP_Player_C_0",
      "class": "BP_Player_C",
      "index": 344179,         // InternalIndex (client-side sort)
      "outer": "7FF6BB000000"
    }
  ],
  // Class-noise picker: full-pool histogram (Top-40, count desc) tallied over the
  // whole matched set PRE-exclude — so an excluded class (or one whose instances
  // all sit past the cap) still appears here and can be unticked to restore it.
  "class_histogram": [ { "class_name": "StaticMeshActor", "count": 8046 }, ... ],
  "class_distinct":  279        // true distinct matched-class count (>= histogram length)
}
```

Request params: `exact_match` (substring vs exact class-name match) and
`newest_first` (default `false`). The DLL walks GObjects ascending and stops at
`limit`, so the default returns the **lowest** indices — the class-default object
/ template / earliest instances (good for finding a Blueprint's defaults), and
for a high-population class the **newest** instances are truncated off the end.
`newest_first: true` walks from the high (most-recently-allocated) end instead,
so the just-spawned instances survive the cap (e.g. catch an enemy that just
appeared). `index` (InternalIndex) is returned per instance for client-side sort.

`exclude_classes` (optional string array) is the **server-side class-noise
filter**: matched rows whose class is in the set are skipped BEFORE they consume a
`limit` slot (comparison is EXACT + case-sensitive — names come from
`class_histogram`, correctly cased). Because the histogram is tallied over the full
matched pool *pre-exclude* and *independently of the cap*, the picker can still show
— and untick — a class whose instances were all excluded or pushed past the cap.
The DLL scans the whole array for this path (the cheap internal callers that don't
need the histogram keep the old early-exit at `limit`). `truncated` now means "more
non-excluded matches exist than were returned" — narrow the query, exclude more
noise, or raise `limit`.

### find_by_address

```jsonc
// Exact match (query addr == UObject base)
{
  "id": 8, "ok": true, "found": true, "match_type": "exact",
  "addr":            "7FF123456000",
  "index":           12345,
  "name":            "BP_Player_C_0",
  "class":           "BP_Player_C",
  "outer":           "7FF6BB000000",
  "offset_from_base": 0,
  "query_addr":      "7FF123456000"
}

// Contains match (query addr is inside a UObject)
{
  "id": 8, "ok": true, "found": true, "match_type": "contains",
  "addr":            "7FF123456000",
  "index":           12345,
  "name":            "BP_Player_C_0",
  "class":           "BP_Player_C",
  "outer":           "7FF6BB000000",
  "offset_from_base": 1929,
  "query_addr":      "7FF123456789"
}

// Not found
{ "id": 8, "ok": true, "found": false }
```

**Container-aware lookup.** Request may set `"scan_containers": true` to also
attribute addresses that fall inside a UObject's heap-allocated container buffer
(TArray/TSet/TMap data — these don't fall within any UObject's PropertiesSize).
`"container_depth": N` (default 1 = shallow only) opts into a **recursive deep
descent**: when the fast shallow scan finds nothing, the DLL descends struct-array
/ map-value / set elements up to depth N to locate values in *separately-allocated*
nested containers (e.g. a `TArray<int>` whose header is inline in a struct element
but whose data lives elsewhere). `"container_elem_cap": M` (default 256) caps how
many elements are probed per container during that descent (UI-configurable via the
Options flyout). The deep scan runs only on a shallow miss (common case stays
fast), is bounded by the element cap + the 15s deadline, and early-outs on the
first match.

```jsonc
// Container match(es). Shallow 1-level hit has no "nested_chain"; a deeply-nested
// value carries the full chain (outermost stays in the match fields, each deeper
// hop in nested_chain; the last hop's intra_offset locates the value).
{
  "id": 8, "ok": true, "found": false,
  "query_addr": "228F1251BE8",
  // container_scan describes the pass that produced the answer. When "deep_scan" is
  // true BOTH passes ran and their stats are FOLDED: counters + classes_primed from the
  // deep pass (the one that answered), duration_ms SUMMED (the caller waited for both),
  // deadline_hit the OR of the two. Until audit #5 Z12 the deep stats replaced the
  // shallow ones only when the deep pass found NOTHING — so a deep SUCCESS reported
  // counters describing a pass unrelated to the answer and dropped the deep pass's own
  // deadline flag. ⚠ "deep_scan": true also means a SECOND bound applied that no counter
  // here expresses: the per-container element probe cap (request "container_elem_cap",
  // default 256). A deep miss is therefore not proof of absence.
  "container_scan": {
    "objects_scanned": 28116, "objects_total": 28116,
    "classes_primed": 4382, "duration_ms": 51,
    "deadline_hit": false, "deep_scan": true
  },
  "container_matches": [
    {
      "owner_addr": "2294EDBE830", "owner_index": 17231,
      "owner_name": "BP_LifeSaveData_C", "owner_class": "BP_LifeSaveData_C",
      "field_offset": 1240, "field_name": "SaveSlotList", "field_type": "ArrayProperty",
      "inner_type": "StructProperty", "element_index": 1, "element_size": 1280,
      "intra_offset": 0, "data_addr": "226CD6A5000", "count": 4,
      "nested_chain": [
        { "field_name": "MsTuneData.MsTunes", "field_type": "MapProperty",
          "element_index": 0, "element_size": 96, "intra_offset": 0,
          "data_addr": "...", "map_value_side": true },
        { "field_name": "WeaponTuneList", "field_type": "ArrayProperty",
          "element_index": 0, "element_size": 64, "intra_offset": 0, "data_addr": "..." },
        { "field_name": "Tunes", "field_type": "ArrayProperty", "inner_type": "IntProperty",
          "element_index": 42, "element_size": 4, "intra_offset": 0, "data_addr": "228F1251B40" }
      ]
    }
  ]
}
```

### find_refs_to_uobject

Reverse reference scan. `references[]` lists each UObject that holds a pointer
to the target via a reflected field (or a container slot). Map matches set
`field_name` to `<owningField>.Key` or `.Value`; array/set element matches
populate `element_index` (otherwise `-1`).

```jsonc
{
  "id": 30, "ok": true,
  "query_addr": "7FF6AA000000",
  "scan": {
    "objects_scanned": 1180536,
    "objects_total":   1180536,
    "classes_primed":  6234,
    "duration_ms":     224,
    "deadline_hit":    false
  },
  "references": [
    {
      "owner_addr":   "7FF6BB100000",
      "owner_index":  98231,
      "owner_name":   "BP_PlayerState_C_0",
      "owner_class":  "BP_PlayerState_C",
      "field_offset": 0x2A8,
      "field_name":   "ActiveAbilities",
      "field_type":   "ArrayProperty",
      "inner_type":   "ObjectProperty",
      "element_index": 3
    }
  ]
}
```

Cache is per-class and persists for DLL lifetime — a cold scan on a 1.18M-object
game is typically ~200-300ms; warm scans are ~70ms. Hard deadline is 30s
(`deadline_hit: true` indicates the scan was truncated and the UI should offer
a re-run after warm-up).

### get_related_objects

Forward owned-object graph for one UObject ("Related Objects" panel). `related[]`
lists, in this order: the object itself (`relation: "Self"`), its `Class` and
`Outer`, its `Controller`/`Pawn` counterpart (reflected by field name), then the
sub-objects it OWNS, discovered by a bounded owned walk up to depth 3 over
outgoing object pointers gated by an Outer-chain ownership test (the same
mechanism the cross-object group scan uses):

- **depth 1** — direct owned sub-objects (`UActorComponent`s, custom
  Health/Stats components, the GAS `AbilitySystemComponent`);
- **depth 2-3** — each sub-object's owned objects (the ASC's `UAttributeSet`s),
  so a GAS AttributeSet is reached even when nested behind a stats/ability layer:
  pawn → stats component → ASC → AttributeSet (some games — e.g. TQ2 — don't hang
  the ASC directly off the actor, so depth 2 reached only the ASC from the pawn).

`relation` is one of `Self` / `Class` / `Outer` / `Controller` / `Pawn` /
`AbilitySystem (ASC)` / `AttributeSet` / `Owned Component` / `Owned Object` (the
ASC/AttributeSet/Component labels are a class-name convenience on top of the
structural walk, not the discovery filter). `field_name` is the field/path on
`parent_addr` that points here (empty for Self/Class/Outer); `field_offset` is
the offset within the parent (-1 when N/A). Fast and bounded — no full GObjects
scan; the reverse "who points AT this object" view is `find_refs_to_uobject`.

```jsonc
{
  "id": 32, "ok": true,
  "query_addr": "7FF6AA000000",
  "related": [
    { "addr": "7FF6AA000000", "index": 100, "name": "BP_Enemy_C_0",
      "class": "BP_Enemy_C", "relation": "Self",
      "field_name": "", "field_offset": -1, "depth": 0, "parent_addr": "0" },
    { "addr": "7FF6BB000000", "index": 200, "name": "AbilitySystem_0",
      "class": "AbilitySystemComponent", "relation": "AbilitySystem (ASC)",
      "field_name": "AbilitySystem", "field_offset": 0x2A8, "depth": 1,
      "parent_addr": "7FF6AA000000" },
    { "addr": "7FF6CC000000", "index": 300, "name": "HealthSet_0",
      "class": "MyHealthAttributeSet", "relation": "AttributeSet",
      "field_name": "SpawnedAttributes[0]", "field_offset": 0x1B0, "depth": 2,
      "parent_addr": "7FF6BB000000" }
  ]
}
```

### get_current_target

Auto-detect the actor the local player is currently targeting / focused on
("Related Objects" Phase 2, the `Edel` module). Resolves the player chain
(GWorld → `OwningGameInstance` → `LocalPlayers[0]` → `PlayerController` → `Pawn`,
with the same instance-scan + DebugCamera-hop fallbacks as teleport), enumerates
the outgoing object pointers of {PC, Pawn, their depth-1 owned `ActorComponent`s},
and **scores** each candidate: kept only if it walks like an `AActor` (a bounded
super-class FName walk), excludes the player's own PC/Pawn, and ranks by a
structural-gate-then-keyword formula (+50 positive-keyword, +30 is-Pawn / +15
is-Actor, −40 infra-negative like `ViewTarget`/`Owner`/`camera`, −60 not-Actor,
+10 near-combat source, +5 real GObjects index). The English keyword table is a
scoring boost, not a gate, so the detector degrades to a ranked guess-list on
non-English / obfuscated games instead of returning nothing.

The chain diagnostics (`resolved`/`world`/`player_controller`/`player_pawn`/`note`)
are ALWAYS present so the UI can say exactly where detection stopped.
`resolved` is true only when the chain reached a Pawn AND the top candidate has a
positive `score` that clears the runner-up by a ≥20 margin (the confident
auto-pick — a guard against arbitrarily picking one of several equally-plausible
actors); otherwise candidates may still be returned as weak guesses the user
picks from manually. Read-only, fast, bounded (no full
GObjects scan except the PC instance-scan fallback).

```jsonc
{
  "id": 33, "ok": true,
  "resolved": true,
  "world": "1AD00000000",
  "player_controller": "7FF6AA000000",
  "player_pawn": "7FF6BB000000",
  "note": "Detected target: Enemy_0 (BP_Enemy_C) — score 95 via CurrentTarget.",
  "candidates": [
    { "addr": "7FF6CC000000", "index": 500, "name": "Enemy_0",
      "class": "BP_Enemy_C", "score": 95,
      "source_addr": "7FF6AA000000", "source_class": "BP_PlayerController_C",
      "field_name": "CurrentTarget", "field_offset": 0x3C0,
      "reason": "field 'CurrentTarget', is-Pawn" },
    { "addr": "7FF6DD000000", "index": 600, "name": "Ally_0",
      "class": "BP_Ally_C", "score": 45,
      "source_addr": "7FF6BB000000", "source_class": "BP_Pawn_C",
      "field_name": "FocusActor", "field_offset": -1,
      "reason": "field 'FocusActor', is-Pawn" }
  ]
}
```

### find_path_from_gworld

Forward object-graph path search ("Locate in GWorld") — the inverse of
`find_refs_to_uobject`. Computes the SHORTEST (fewest-hop) pointer chain from the
live `UWorld` (GWorld) down to a target, by breadth-first walking the same
outgoing object-pointer edges the reverse search uses (direct Object/Class/
Interface, Weak/Soft/Lazy, TArray/TMap/TSet of objects, and fields nested in
StructProperty to depth 3). Reuses the per-class reference-metadata cache.

Request:

```jsonc
{
  "id": 31, "cmd": "find_path_from_gworld",
  "target": "0x7FF6BB100000",     // address to locate (a UObject, or a value inside one)
  "object_addr": "0x7FF6BB100000",// OPTIONAL — the owning UObject if the caller already
                                  //   knows it (Value Search / Instance Finder); skips the
                                  //   FindByAddress resolution scan
  "max_depth": 5,                 // max pointer hops from GWorld (default 5; hard-capped 32)
  "root_kind": "gworld",          // OPTIONAL — "gworld" (default) or "engine" (root at UGameEngine)
  "deep": false,                  // OPTIONAL — opt-in deep BFS (default off): ALSO follow object
                                  //   pointers inside ONE struct-element container level
                                  //   (TArray<FStruct>/TSet<FStruct>/TMap<*,FStruct> whose element
                                  //   struct holds a UObject*) — reaches objects referenced ONLY
                                  //   from a struct-array element. The graph analogue of Value
                                  //   Search "Deep". Heavier (reads each struct-array's elements
                                  //   per visited node); each edge is one CE-splittable hop
                                  //   (elem_stride + elem_value_offset point at the pointer).
  "container_depth": 1            // OPTIONAL — >1 attributes a BARE value address inside a deeply-
                                  //   nested heap container to its owning UObject via the deep
                                  //   container scan (FindInContainersDeep), instead of returning
                                  //   invalid_target. Ignored when object_addr is supplied.
}
```

Response (`steps` is the path `root → steps[0].to → … → target_obj`; empty when
the target IS the root). When no path exists within the depth budget, `found` is
false and `status` explains why:

```jsonc
{
  "id": 31, "ok": true,
  "found": true,
  "status": "ok",               // ok / ok_via_level / not_reachable / deadline / cancelled / no_gworld / invalid_target / visited_cap
  "root_addr":  "0x7FF6AA000000",
  "root_name":  "World_0",
  "target_obj": "0x7FF6BB100000",
  "target_name":  "BP_PlayerState_C_0",
  "target_class": "BP_PlayerState_C",
  "target_intra_offset": 0,      // (value addr - target_obj); >0 when target was a value inside the object
  "max_depth": 5,
  "depth":     4,                // hop count (== steps.length)
  "visited":   18342,            // distinct objects discovered
  "duration_ms": 120,
  "steps": [
    // ⚠ Addresses AND field_offsets below are PLACEHOLDERS — do not mine them, the
    // layout is per-build and every offset here is resolved at runtime. What IS real
    // is the shape: both fields are reflected UPROPERTYs (UWorld::GameState,
    // AGameStateBase::PlayerArray), and this is a logged path — Elliot 2026-07-23,
    // GWorld > GameState > PlayerArray[0] > PawnPrivate > … (see docs/todo.md).
    { "from": "0x7FF6AA000000", "to": "0x7FF6AA001000",
      "field_offset": 0x1D8, "field_name": "GameState",
      "field_type": "ObjectProperty", "element_index": -1,
      "to_name": "BP_GameState_C_0", "to_class": "BP_GameState_C" },
    { "from": "0x7FF6AA001000", "to": "0x7FF6AA002000",
      "field_offset": 0x2A8, "field_name": "PlayerArray",
      "field_type": "ArrayProperty", "inner_type": "ObjectProperty", "element_index": 0,
      "to_name": "BP_PlayerState_C_0", "to_class": "BP_PlayerState_C" }
    // … → target_obj
  ]
}
```

⚠ **Every step the forward BFS emits is a REFLECTED property hop** — it enumerates
`GetClassRefMeta`, so a field with no `UPROPERTY` can never appear. In particular
`ULevel::Actors` is a plain C++ member (`Engine/Classes/Engine/Level.h:429`,
`TArray<TObjectPtr<AActor>> Actors;`, no `UPROPERTY` — contrast
`DestroyedReplicatedStaticActors` at `:886`, which has one), so **there is no
`GWorld → PersistentLevel → Actors[k]` step and never was**; audit #5 F8/F9. The
only way a level actor enters a path is the synthetic `ok_via_level` recovery below.

The UI replaces the Live Walker breadcrumb spine with this path. For a property
VALUE it lands on `target_obj` and scrolls to the value field; for an OBJECT /
class instance it stops at the parent (drops the final node) and highlights the
pointer field, without drilling into the target. BFS first-hit == shortest hops.

**`ok_via_level` recovery (streaming / World-Partition actors).** When the plain
BFS returns `not_reachable` and the target is (or is owned by) an actor whose
`ULevel` isn't forward-reachable from the world, the DLL recovers the chain
through the world's level list: it reaches the owning `ULevel` by its
`OwningWorld` back-reference (an actor's Outer IS its level) and returns
`found: true` with `status: "ok_via_level"`.

⚠ **The FIRST TWO steps are both SYNTHETIC** — neither is a pointer deref, and a
CE chain must not be built through either:

| step | `field_name` | `field_type` | `field_offset` | `element_index` |
|---|---|---|---|---|
| `world → level` | `Levels` | `WorldLevel` | `-1` | `-1` |
| `level → actor` | `Actors` | `LevelActor` | `-1` | `-1` |

Both are back-references, not forward static pointers (`dll/src/Aura.cpp:4085-4105`).
There is **no membership lookup and no array index**: `ULevel::Actors` carries no
`UPROPERTY`, so audit #5 F8 deleted the lookup outright — the Outer climb exits only
with `level = GetOuter(actor)`, so membership is guaranteed by construction and `-1`
is the honest answer for both the offset and the index. Only the TAIL steps
(`actor → … → target`, empty when the target IS the actor) are real reflected edges.

So the chain is for in-tool reachability / Live Walker navigation, **not** a clean CE
pointer chain — the UI renders an offset-less hop as a navigation anchor
(`IsPointerDeref=false`) and the CE exporter re-roots at the deepest such hop rather
than fabricating an offset for it. A truly unreferenced actor not in any world level
still returns `not_reachable`.
20s deadline; also bails on `Cancel::Requested()` (pipe disconnect / shutdown).
MulticastSparseDelegateProperty edges are intentionally NOT followed (their
bindings live in a CoreUObject-global TMap — a per-node global walk would be
prohibitively expensive).

**Deep traversal (`deep: true`).** The forward BFS additionally follows object
pointers stored inside ONE struct-element container level — a `UObject*` held by
a `TArray<FStruct>` / `TSet<FStruct>` / `TMap<*,FStruct>` element struct (incl.
nested in an inline sub-struct, which the metadata flattens to a fixed
element-relative offset). The emitted step carries `field_type` =
`ArrayProperty`/`SetProperty`/`MapProperty`, `inner_type` = `StructProperty`,
`element_index`, `elem_stride` (the element/pair stride) and `elem_value_offset`
(the pointer's offset within the element), so the UI splits it into a container
deref + an element-pointer deref at `element_index*elem_stride +
elem_value_offset` — a faithful CE pointer chain. Object containers nested INSIDE
the element struct (two container levels) are deliberately NOT followed: they
cannot be expressed as a single splittable hop. Bounded by a per-container
element cap plus the deadline / visited cap. Default off (heavier).

### get_offsets

⚠ **The payload is FLAT, not nested.** `Renge::ApplyPayload` splices each key straight into the
response envelope (`for (it : data) res[it.key()] = ...`), so there is no `"offsets"` object to
index into. This section previously showed one, along with two key names the DLL has never
emitted (`case_preserving_name` → `case_preserving`, `offsets_validated` → `validated`) — a client
written from the old text parsed nothing at all. Keys below are the emitted set, verbatim from
`Fern.cpp`'s `CMD_GET_OFFSETS` handler.

```jsonc
{
  "id": 4, "ok": true,

  // --- always present ---
  "build_info":         "3369 ...",   // BuildStamp::VersionString()
  "validated":          true,          // DynOff::bOffsetsValidated — the probe agreed with itself
  "probe_ran":          true,          // the probe ran at all (false = defaults, untouched)
  "fallback_reason":    "",            // non-empty names WHICH probe gave up, e.g. "childprops-probe-failed"
  "use_fproperty":      true,          // false = UE4 <4.25 UProperty mode; selects the branch below
  "case_preserving":    false,         // WITH_CASE_PRESERVING_NAME. ⚠ the Name->Outer SLOT becomes
                                       // 0x10; sizeof(FName) is 0xC. See DynOff::bCasePreservingName.
  "uobject_outer":      32,            // 0x20 standard, 0x28 case-preserving
  "ffieldclass_name":   0,             // FFieldClass::Name — 0x00 up to UE 5.7, 0x08 from 5.8
                                       // (5.8 made ~FFieldClass() virtual). ⚠ 0x00 is FOUR-WAY
                                       // ambiguous: <=5.7, probe gave up, probe gave up earlier,
                                       // or UProperty mode. Read `fallback_reason` before it.

  // --- UStruct spine (always present) ---
  "ustruct_super":      64,
  "ustruct_children":   72,
  "ustruct_childprops": 80,            // FField* chain; absent as a CHAIN in UProperty mode
  "ustruct_propssize":  88,
  "ustruct_script":     96,            // == propssize + 8 on every measured layout

  // --- FUObjectItem layout (always present; see Lineal.h) ---
  "item_packed":        false,
  "item_obj_offset":    0,             // 0x08 on the UE5.7+ reordered item
  "item_size":          24,            // 16 / 20 / 24 / 32 / 40 — 40 is a UE5.7+ Test build
  "item_layout_mode":   "classic",     // "classic" | "unpacked57" | "packed57"

  // --- ONE of these two blocks, chosen by use_fproperty ---

  // use_fproperty == true  (UE 4.25+ / UE5)
  "ffield_class":       8,
  "ffield_next":        32,
  "ffield_name":        40,
  "fproperty_elemsize": 60,            // 0x3C. ⚠ NOT 0x38 — that is ArrayDim (Grimoire.h)
  "fproperty_flags":    64,
  "fproperty_offset":   76,

  // use_fproperty == false (UE4 <4.25)
  "ufield_next":        40,
  "uproperty_elemsize": 52,
  "uproperty_flags":    56,
  "uproperty_offset":   68
}
```

### read_array_elements

```jsonc
{
  "id": 13, "ok": true,
  "total":      128,
  "read":       64,
  "inner_type": "FloatProperty",
  "elem_size":  4,
  "elements": [
    { "i": 0, "v": "100.5000000000", "h": "0000C842" },
    { "i": 1, "v": "200",            "h": "00004843" }
  ]
}
```

### get_ce_pointer_info

Builds a CE pointer chain (`ce_base` + `ce_offsets`) for a GObjects instance. Under the
UE5.7+ packed FUObjectItem layout a native CE chain cannot reconstruct the bit-packed
object pointer, so the response degrades to the absolute object address and sets
`packed_layout:true` + a `warning` (the chain won't survive a restart / ASLR rebase). The
direct-layout item hop includes `Aura::GetItemObjOffset()` so it dereferences the Object
pointer at its real within-item offset (+0x00 classic, +0x08 UE5.7+ unpacked).

```jsonc
// Direct (classic / unpacked57): full GObjects → chunk → item → field chain
{ "id": 18, "ok": true, "packed_layout": false,
  "ce_base": "\"Game.exe\"+1BA1820",
  "ce_offsets": [64, 264, 24, 0] }            // [field, withinChunk*itemSize+objOff, chunkIndex*8, 0]

// Packed57 (UNVERIFIED): degraded to the absolute object address
{ "id": 18, "ok": true, "packed_layout": true,
  "warning": "UE5.7+ packed FUObjectItem layout (UNVERIFIED): ... absolute address only ...",
  "ce_base": "0x1F809E08FB0", "ce_offsets": [64] }
```

### set_packed_consts

Runtime calibration / force-enable for the UE5.7+ **UNVERIFIED** packed FUObjectItem
reconstruction (no DLL rebuild). Leave a field unchanged with `align_bits<=0` /
`ptr_mask_bits=="0x0"` / `serial_off<0`. `force:true` switches the live layout to packed
unconditionally. Echoes the resulting mode + reconstructed `GObjects[0..7]` samples for
eyeball calibration (tweak constants until names look like real UObjects).

```jsonc
// Request
{ "id": 60, "cmd": "set_packed_consts",
  "align_bits": 3, "ptr_mask_bits": "0x3FFF", "force": true, "serial_off": 12 }

// Response
{ "id": 60, "ok": true,
  "item_packed": true, "item_layout_mode": "packed57", "item_obj_offset": 0, "item_size": 24,
  "samples": [ { "index": 0, "addr": "0x1F800000000", "name": "CoreUObject" }, ... ] }
```

> `get_pointers` (and `get_offsets`) additionally carry `item_layout_mode` /
> `item_packed` / `item_obj_offset` / `item_size` so the UI can flag the unverified
> packed mode (badge + export notes).

### read_mem / write_mem

```jsonc
// read_mem response
{ "id": 14, "ok": true, "bytes": "48 8B 05 AB CD EF 12 ..." }

// write_mem response
{ "id": 15, "ok": true }
```

### walk_functions

Walk all UFunctions of a UClass. Returns function signatures with parameters,
including StructProperty sub-field layouts discovered by walking the UScriptStruct.

```jsonc
// Request
{ "id": 20, "cmd": "walk_functions", "addr": "7FF123456789" }

// Response
{
  "id": 20, "ok": true,
  "count": 1,
  "functions": [
    {
      "name": "SetAttribute",
      "full": "Function /Script/Game.Character.SetAttribute",
      "addr": "0x7FF601234500",
      "flags": 67109120,
      "num_parms": 1,
      "parms_size": 8,
      "ret_offset": 65535,
      "ret": "",
      "params": [
        {
          "name": "NewValue",
          "type": "StructProperty",
          "size": 8,
          "offset": 0,
          "out": false,
          "ret": false,
          "struct_type": "GameplayAttributeData",
          "struct_fields": [
            { "name": "BaseValue", "type": "FloatProperty", "offset": 0, "size": 4 },
            { "name": "CurrentValue", "type": "FloatProperty", "offset": 4, "size": 4 }
          ]
        }
      ]
    }
  ]
}
```

**`struct_fields`** (optional): Present only for `StructProperty` params where the DLL
successfully walked the UScriptStruct's FField chain. Used by the UI as fallback when
`KnownStructLayouts` has no hardcoded definition for the struct type. Each sub-field
includes name, type, byte offset within the struct, and size. Nested StructProperty
sub-fields are not recursively expanded (Phase B scope).

### invoke_function

Invoke a UFunction via ProcessEvent. The DLL executes in-process, bypassing CE's
`executeCodeEx` (which uses `CreateRemoteThread` and is blocked by some games).

**Game-thread dispatch:** When available, the DLL hooks ProcessEvent with MinHook
and dispatches invocations to the game thread via a queue. This ensures correct
thread context for state-changing functions (UI, rendering, spawning). If the hook
is not available, falls back to direct call from the pipe handler thread (risky for
state-changing operations but works for simple getters).

```jsonc
// Request
{
  "id": 42,
  "cmd": "invoke_function",
  "func_name": "Attack",           // required
  "instance_addr": "0x7FF6AA000",  // optional (one of instance_addr / class_name required)
  "class_name": "BP_Player_C",     // optional
  "parms_size": 16,                // optional -- the DLL reads UFunction::ParmsSize
                                   // itself and uses the LARGER of the two. Omitting it
                                   // is safe as of build 3350; before that it defaulted
                                   // to 0, and a zero-length buffer handed to
                                   // ProcessEvent overflowed the game's heap and came
                                   // back only as "-4 (exception during call)".
  "params_hex": "3F800000",        // optional (hex param bytes; scalars only)
  // optional: string INPUT params. An FString is passed by value as
  // { Data*, Num, Max } (16 bytes) inline in the params buffer, and its Data
  // pointer must be a valid GAME-process address — which the UI can't allocate.
  // The DLL (injected) mallocs a char buffer, patches the struct at `off`, runs
  // ProcessEvent, then frees it (LEAKS on a -5 game-thread timeout to stay
  // crash-safe). Leave the corresponding 16-byte slots zeroed in params_hex.
  // Only send INPUT strings; an OUT FString must stay a zeroed/empty struct.
  "str_params": [
    { "off": 0, "wide": true,  "text": "Hero" },   // wide=true  -> UTF-16 FString
    { "off": 16, "wide": false, "text": "tag" }     // wide=false -> FUtf8String/FAnsiString bytes
  ]
}

// Response (success)
{
  "id": 42, "ok": true,
  "result": 0,
  "instance_addr": "0x7FF6AA000",
  "func_addr": "0x7FF123ABC",
  "parms_size": 16,
  "result_hex": "3F80000000000000...",  // post-call buffer (out-params)
  "message": "ProcessEvent OK"
}

// Response (ProcessEvent error)
{
  "id": 42, "ok": true,
  "result": -2,
  "instance_addr": "0x7FF6AA000",
  "error": "ProcessEvent error code -2 (vtable read failed)"
}
```

Error codes:
- `0` = success
- `-1` = invalid args
- `-2` = vtable read failed
- `-3` = ProcessEvent offset not found
- `-4` = SEH exception during call
- `-5` = game-thread dispatch timeout (5s) — game may be paused or unresponsive
- `-7` = hook not active, fell back to direct call (may have succeeded but on wrong thread)

### walk_instance_batch

N instance walks in ONE round-trip. **Measured justification** ([multipipe-eval.md](multipipe-eval.md)
§10.4): a Copy CE XML issued **20,357** single `walk_instance` calls, splitting as
dll 30% / **ipc 59-73%** / ui ~0%. Per call the round-trip overhead (0.16-0.21 ms) is roughly
**twice** the actual walk (0.08 ms) — so collapsing the calls, not changing the dispatch model, is
the lever. Chunk at ~200 (what the UI does).

The DLL implementation is a **trivial loop over the single-call path**, and both share one
serialiser, so each element is byte-identical to a `walk_instance` response.

```jsonc
// Request — per-item class_addr optional; array_limit / preview_limit / fill_gaps /
// lean may be set per batch (defaults) and overridden per item.
{ "id": 7, "cmd": "walk_instance_batch",
  "items": [ { "addr": "1F2A3B40", "class_addr": "1C0DE000" },
             { "addr": "1F2A3C80" } ],
  "array_limit": 64 }

// Response — "instances" is positionally aligned with "items".
{ "id": 7, "ok": true, "count": 2,
  "instances": [ { /* exactly a walk_instance payload */ }, { ... } ] }
```

A malformed item yields an empty object in its slot rather than aborting the batch, and the loop
honours the same cooperative cancel as every other bulk command. **The UI replays a chunk as single
calls** whenever the batch fails *or* returns the wrong number of rows — a short reply would
otherwise mis-pair results with addresses, which in a CE export is a wrong pointer chain that looks
perfectly valid.

### get_diagnostics / reset_diagnostics

Self-health telemetry (`Sense`). Read-only and safe to poll. Exists to answer the
question [multipipe-eval.md](multipipe-eval.md) leaves open: that doc names DLL-side
**serial-dispatch head-of-line blocking** as the root cause of UI lag and game-thread
CPU starvation as the CE-mailbox risk, but nothing measured either — so Phase 1
(non-blocking dispatch) was a blind decision. `busy_percent` plus the per-command
ranking is the evidence.

Timing is taken around Fern's `inFlight` window, i.e. exactly the CPU-bound,
pipe-free stretch during which that connection's dispatcher is unavailable.

```jsonc
// Request
{ "id": 1, "cmd": "get_diagnostics", "limit": 25 }   // limit = top-N commands, 0 = all

// Response
{
  "id": 1, "ok": true,
  "uptime_ms": 61234,            // since DLL start / last reset
  "total_dispatches": 842,
  "total_busy_ms": 9310,
  "busy_percent": 15.2,          // ← the headline: fraction of wall-clock a dispatcher was busy
  "gobjects_count": 486231,      // pool size over time = cheap GC / leak signal
  "commands": [                  // heaviest TOTAL first (who OWNS the dispatcher)
    { "cmd": "value_scan_begin", "count": 3, "total_ms": 8100,
      "max_ms": 4200,            // ← worst single dispatch = the spike a user feels
      "last_ms": 1900, "avg_ms": 2700.0 }
  ],
  "process": {                   // Tier 2 — Win32 only, no UE dependency
    "working_set_bytes": 734003200, "private_bytes": 812345678,
    "peak_working_set": 900000000, "handle_count": 1204, "thread_count": 61,
    "cpu_percent": 3.4           // -1 until a SECOND sample exists to difference against
  },
  "game_thread": {               // from Stark's ProcessEvent hook
    "hook_active": true, "hook_fire_count": 918273,
    "ms_since_last_fire": 16, "responsive": true, "invoke_timeout_ms": 5000
  }
}
```

```jsonc
// Clear the counters and restart the uptime clock, to scope a measurement to one
// deliberate action. Also happens automatically when the last client disconnects.
{ "id": 2, "cmd": "reset_diagnostics" }   →   { "id": 2, "ok": true, "ok": true }
```

**UI:** System tab → *Diagnostics — DLL dispatch cost*, directly above the Pipe
Activity card (that one shows *what* crossed the pipe; this shows what it *cost*).

### Error response (any command)

```jsonc
{ "id": 5, "ok": false, "error": "Object not found at address 7FF123456789" }
```

-----

## Push Events (DLL → UI, no id)

```jsonc
// Live watch periodic push (triggered by "watch" command)
{
  "event":     "watch",
  "addr":      "7FF123456789",
  "bytes":     "0000803F",
  "timestamp": 1234567890
}
```

-----

## Teleport (Wirbel) — marker save/recall + cursor teleport

6 request/response commands (build 1027; full contract in
[teleport-spec.md](teleport-spec.md) §7). Non-zero `code` is still an
`ok:true` response — the UI maps codes to user hints; `MakeError` is reserved
for malformed requests. `tier`: 1 = engine invoke (clean), 2 = raw-write
fallback (game may snap back). Codes (§8): 0 OK, -1 not-init, -2 no controller,
-3 no pawn, -4 reflection, -5 invoke-timeout, -6 empty marker, -7 map mismatch,
-8 no hit, -9 no cursor, -10 write failed.

```jsonc
{ "cmd": "teleport_get_pose" }
→ { "x":…,"y":…,"z":…,"pitch":…,"yaw":…,"roll":…,"map":"Map","source":"raw|invoke","code":0,
//   pawn_addr = the resolved pawn (hex str, "0x0" if none) for the "Locate in GWorld" handoff.
//   loc_owner_addr / loc_field_offset / loc_field_name = owner (RootComponent) + offset of the
//     position FVector (RelativeLocation) — the "Locate position vector in GWorld" handoff lands
//     the path on this exact field. Present whenever the pose resolved.
//   has_movement = pawn has a CharacterMovement; when true, the live vel_/acc_/speed fields are
//   present (velocity cm/s, acceleration cm/s²). Absent on vehicle / custom-framework pawns.
//   vel_owner_addr / vel_field_offset / vel_field_name = owner (CharacterMovement) + offset of the
//     velocity FVector — the "Locate velocity vector in GWorld" handoff. Present only when has_movement.
    "pawn_addr":"0x…",
    "loc_owner_addr":"0x…","loc_field_offset":0x140,"loc_field_name":"RelativeLocation",
    "has_movement":true,
    "vel_x":…,"vel_y":…,"vel_z":…, "acc_x":…,"acc_y":…,"acc_z":…, "speed":…,
    "vel_owner_addr":"0x…","vel_field_offset":0x16C,"vel_field_name":"Velocity" }

{ "cmd": "teleport_save_marker", "slot": 0 }
→ { "slot":0,"x":…,…,"map":"…","code":0 }

{ "cmd": "teleport_recall_marker", "slot": 0, "force": false }
→ { "code":0, "tier":1 }   |   { "code":-7,"map":"Map_B","markerMap":"Map_A" }
// Explicit-pose variant (BugItGo): pass x/y/z (+optional pitch/yaw/roll)
// instead of slot — bypasses the marker store and the map check.
{ "cmd": "teleport_recall_marker", "x":…, "y":…, "z":…, "pitch":…, "yaw":…, "roll":… }

{ "cmd": "teleport_to_cursor", "zOffset":100.0, "channel":0, "fallbackCenter":true }
→ { "code":0,"tier":1,"usedCenter":false,"hitX":…,"hitY":…,"hitZ":… }

{ "cmd": "teleport_get_markers" }
→ { "markers":[ { "slot":0,"valid":true,"x":…,…,"map":"…" }, { "slot":1,"valid":false }, … ] }

{ "cmd": "teleport_clear_marker", "slot": 0 } → { "slot":0,"code":0 }

// Camera POV (read-only) — distinct from the pawn pose. There is no Set POV.
{ "cmd": "teleport_get_pov" }
→ { "code":0, "camX":…,"camY":…,"camZ":…, "pitch":…,"yaw":…,"roll":…,
    "fov":…, "hasPawn":true, "pawnX":…,"pawnY":…,"pawnZ":…, "source":"invoke" }

// Teleport along the pawn's facing by `distance` uu (negative = backward).
// horizontal=true keeps Z (ground-plane); false = full 3D forward (incl. pitch).
// Returns the resulting pose. Undoable via teleport_recall_last.
{ "cmd": "teleport_relative", "distance":100.0, "horizontal":true }
→ { "code":0, "tier":1, "x":…,"y":…,"z":…, "pitch":…,"yaw":…,"roll":… }

// Force the mouse cursor on/off (writes APlayerController.bShowMouseCursor).
{ "cmd": "set_mouse_cursor", "show": true } → { "code":0, "state":true }
{ "cmd": "get_mouse_cursor" }              → { "code":0, "state":true }
// (Explicit-coordinate teleport reuses teleport_recall_marker with x/y/z above.)

// Movement tuning (Laufen) — per-pawn UCharacterMovementComponent float knobs
// forced to base × multiplier and held by a re-assert worker. `knob` ∈
// { "walk_speed" (MaxWalkSpeed), "gravity" (GravityScale), "jump" (JumpZVelocity) }.
// Each knob surfaces (owner_addr,field_offset,field_name) for the Locate-in-GWorld
// handoff (object_addr=owner, target=owner+offset → find_path_from_gworld).
{ "cmd": "get_movement_params" }
→ { "code":0, "has_cmc":true, "cmc_addr":"0x…",
    "knobs": { "walk_speed": { "resolved":true,"current":600.0,"base":600.0,
                               "multiplier":1.0,"active":false,
                               "owner_addr":"0x…","field_offset":316,"field_name":"MaxWalkSpeed" },
               "gravity": { … }, "jump": { … } } }
// State: 1 = override active, 0 = inactive, negative = no pawn / no CMC / reflect.
{ "cmd": "set_movement_multiplier", "knob":"walk_speed", "multiplier":2.0 }
→ { "state":1,"code":0,"current":1200.0,"base":600.0,"multiplier":2.0,"active":true,
    "resolved":true,"owner_addr":"0x…","field_offset":316,"field_name":"MaxWalkSpeed" }
{ "cmd": "reset_movement", "knob":"walk_speed" }
→ { "code":0,"current":600.0,"base":600.0,"multiplier":1.0,"active":false,"resolved":true }
```

The CE Lua path uses the Mimic mailbox `CMD_MOVEMENT=10` (instanceAddr = knobId
0/1/2, paramsData[0..7] = double percent; **100% = OFF**, knob 2 = jump HEIGHT %),
driven by the `UE5_SetMovementPercent(knobId, percent)` export — `executeCodeEx`
can't read export return values. The Teleport tab's "Add action records to CE" /
"Save .CT" emit stateful movement toggles that poke this mailbox.

The CE Lua path uses the Mimic mailbox `CMD_TELEPORT=8` instead (see
[teleport-spec.md](teleport-spec.md) §8) — `executeCodeEx` can't read export
return values.

-----

## Pagination Pattern

```
UI loop:
  offset = 0
  while allNodes.Count < target:
      send: { "cmd": "get_object_list", "offset": offset, "limit": 200 }
      recv: { "scanned": N, "objects": [...] }
      append objects to tree
      offset += scanned          ← MUST use "scanned", not objects.length
      if scanned == 0: break     ← end of array
```

**Why:** The DLL silently skips null/unnamed slots. `scanned` reports how many indices were actually iterated, ensuring the next request starts from the correct position even when many consecutive slots are empty (common in UE4).
