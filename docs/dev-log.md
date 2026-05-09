# Dev Log

Running log of milestone work, current capability matrix, and known gaps.
Newest section first. Tied to the `dev` branch — entries reference build
numbers from `build_number.txt` so a commit can be cross-referenced.

-----

## 2026-05-09 — Find Refs v2/v3, OptionalProperty, Property Search UX, Class Structure fixes

Session focused on closing reverse-reference coverage gaps and fixing UI
papercuts in the Property Search / Game Class / Class Structure tabs.

### Reverse Reference Search (Find Refs)

`Aura::FindReferencesToUObject` walks every UObject's pointer-shaped
fields (and the containers that hold them) to answer "who logically owns
this object?" — UE's `OuterPrivate` is a naming hierarchy, not a gameplay
hierarchy, so for runtime-spawned objects the reverse scan is the only
way to surface real ownership.

**Find Refs v2** (build 511+, [`394a285`](../dll/src/Aura.cpp)):
extended from "ObjectProperty/ClassProperty + TArray<UObject*>" to:
- Direct `Object/Class/Interface` (8B raw pointer at field+0)
- Direct `Weak/Soft{Class}/Lazy` (resolves embedded `FWeakObjectPtr`)
- `TArray` of any of the above
- `TMap<UObject*, V>` / `TMap<K, UObject*>` (allocated slots only)
- `TSet<UObject*>` (allocated slots only)

**Find Refs v3** (build 519+, [`7efe862`](../dll/src/Aura.cpp)):
added delegate / multicast target scan:
- `DelegateProperty` (single `FScriptDelegate` — `FWeakObjectPtr` target
  at field+0)
- `MulticastInlineDelegateProperty` / `MulticastDelegateProperty`
  (FMulticastScriptDelegate is `TArray<FScriptDelegate>` at field+0;
  walks each binding's `FWeakObjectPtr`)
- `TArray<FScriptDelegate>` via `ArrayProperty<DelegateProperty>`

Stride for `FScriptDelegate` derives from `DynOff::bCasePreservingName`
(16 or 24) at runtime, so case-preserving builds compute the right
per-element step.

`MulticastSparseDelegateProperty` is **not** covered — bindings live in
CoreUObject's global `FSparseDelegateStorage`
(`TMap<FObjectKey, TMap<FName, TSharedPtr<FMulticastScriptDelegate>>>`),
not at the field. The AOB to locate that storage is universal (it's UE
engine code, same as GObjects/GNames/GWorld). The blocker is the
read-side TMap walk, not finding the address.

### OptionalProperty (UE 5.2+)

`feat(walker): OptionalProperty drill-down + Find Refs coverage`
([`8f52f63`](../dll/src/Ubel.cpp))

`TOptional<T>` ships in two layouts depending on `T`:
- **Intrusive** (UE 5.4+ for pointer types `Object/Class/Interface` and
  the `FWeakObjectPtr`-shaped `Weak/Soft/Lazy`): `T` directly at field+0,
  null/zero is the unset sentinel. `sizeof(TOptional<T>) == sizeof(T)`.
- **Non-intrusive** (older + non-pointer T): `{ T value; uint8 bIsSet; }`
  with the trailing flag at `field + sizeof(T)`.

`WalkClassEx` probes `FOptionalProperty::ValueProperty` using the same
`FARRAYPROP_INNER` offset (FOptional + FArray have the same shape:
`FProperty + FProperty*`), populating `innerType` /
`innerStructType` / `innerObjClass`.

`WalkInstance` dispatches by inner type:
- Object/Class/Interface: read pointer at field+0; null = unset
- Weak/Soft/Lazy: `{ idx=0, serial=0 }` = unset
- Scalar: trailing `bIsSet` at field+`ResolveInnerSize(inner)`
- Display: `(unset)` or rendered inner value

Find Refs reuses `directPointers` / `weakLikePointers` because the
intrusive layout puts T at field+0 — the comparison is identical to the
bare T. `fieldType` is reported as `OptionalProperty` so the user sees
the Optional wrapper in the result.

**Verified on Everspace 2** (UE 5.4): `OptionalPropertyTestObject`'s 4
test fields (Str/Text/Name/Int) plus 5 game-class StructProperty inners
(`WorldPartitionRuntimeCellData.CellBounds`, `FontFace.Platform...`,
`MapScalabilityModifierComponent.VolumetricFog...`).

### MulticastSparseDelegateProperty bound-flag surfacing

`feat(walker): MulticastSparseDelegateProperty bound-flag surfacing`
([`600045f`](../dll/src/Ubel.cpp))

Sparse multicast delegates were falling through the generic scalar
handler and rendering as garbage hex. Added a field-level handler that:
- Reads `bIsBound` byte at field+0
- Displays `(sparse, bound — bindings in FSparseDelegateStorage)` or
  `(sparse, unbound)`
- Hex over reported size (defensively capped at 16B)
- Leaves `arrayCount=0` so `IsContainerNavigable` stays false

Binding enumeration (drill-down into individual `FScriptDelegate`s) is
queued for v4 — needs the storage AOB + nested TMap + TSharedPtr walk.

### Property Search panel UX

Three usability fixes pushed in successive commits because they all hit
the same "find every OptionalProperty in this game" workflow:

**Type filter exposed + type-only queries allowed**
([`b461c40`](../dll/src/Fern.cpp))

The DLL backend (`Aura::SearchProperties`) and `DumpService` already
supported a `types` filter, but the UI never surfaced it. Added a Type
filter input. Also relaxed the empty-query check in
`Fern.cpp::CMD_SEARCH_PROPERTIES` — name OR types must be set, not
strictly name. SearchProperties already tolerates an empty substring
(empty `find` returns 0 always).

**Type filter autocomplete + client-side result filter +
ObjectTree suggestions refresh**
([`67eaa62`](../ui/UE5DumpUI/ViewModels/PropertySearchViewModel.cs))

- **AutoCompleteBox** for the Type filter, backed by a curated 32-entry
  `PropertyTypeSuggestions` list. Typing "opt" surfaces
  `OptionalProperty`; "del" surfaces all four delegate variants; "weak"
  surfaces `WeakObjectProperty`. Comma-separated multi-type input still
  parsed in the VM.
- **Client-side result filter**: a new `ResultFilter` TextBox under the
  search bar. `ApplyResultFilter` walks a private `_allResults` cache
  and rebuilds `Results` with case-insensitive substring match across
  Class / Property / Type / Super / Preview. 150 ms debounce on the
  partial-changed hook so per-keystroke rebuild doesn't churn the
  ObservableCollection. VM now `IDisposable`.
- **ObjectTree.SearchSuggestions refresh**: dropped A/U-prefixed
  duplicates (`ACharacter`/`Character`, `APawn`/`Pawn`,
  `UAttributeSet`/`AttributeSet` — UE introspection drops prefixes so
  the A/U variants never matched), added universally-useful entries
  (`GameInstance`, `World`, `Level`, `SaveGame`, `GameplayAbility`,
  `GameplayEffect`, `AnimInstance`, ...). 30 categorized entries:
  GAS / Components / Player & Character / Game Framework /
  World+Level / UMG.

### Class Structure / Game Class fixes

**Find Refs reverse Open auto-scroll**
([`a5634b9`](../ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs))

After Open from a Find Refs row the holding field was selected but the
DataGrid stayed at the top. Avalonia's DataGrid only auto-scrolls on
user-driven selection, not on programmatic `SelectedItem` assignment —
raise `ScrollToFieldRequested` so the View calls `ScrollIntoView`,
matching the path edit-commit and inline drill navigation already use.

**Class Structure flash-then-blank** (build 524,
[`449f4e4`](../ui/UE5DumpUI/ViewModels/ClassStructViewModel.cs))

ClassStructPanel briefly showed the clicked object's class data and
then went blank. Avalonia's ListBox raises `SelectionChanged` with null
whenever its `ItemsSource` mutates (filter typing, fresh load,
suggestion auto-selection), and the MainWindow handler dutifully
forwarded that null to `ClassStruct.OnObjectSelected`, which then set
`HasClass=false` and cleared `Fields`. Fix: treat null as
"selection cleared, but keep showing what we last loaded" — the user
already picked a class, the IDE-style transient selection wobble
shouldn't undo that. Plus dedupe consecutive selections of the same
node via a private `_lastLoadedNodeAddress`.

**Class Structure: route class-like nodes to themselves**
(build 525, [`89f637b`](../ui/UE5DumpUI/ViewModels/ClassStructViewModel.cs))

Even after fixing the null-fire blank, clicking `LocalPlayer` (or any
other UClass) in the ObjectTree still showed `//Script/CoreUObject/Class`
with 0 fields — `GetObjectAsync` on a UClass returns its metaclass
(UClass-of-Class), and walking that metaclass yields an empty FProperty
chain because UClass's data lives in native C++ members rather than
UPROPERTY-tagged ones. Fix: detect class-like nodes by ClassName —
anything ending in `Class` (Class, BlueprintGeneratedClass,
WidgetBlueprintGeneratedClass, AnimBlueprintGeneratedClass, ...) plus
`ScriptStruct` / `UserDefinedStruct` / `Enum` / `UserDefinedEnum` /
`Function` / `DelegateFunction` — and walk `node.Address` directly.
Only proper instances go through `GetObjectAsync`.

**Game Class: auto-run Find Instances pre-fill** (`449f4e4`)

`PropertySearch` and `GameClassFilter` both raised
`NavigateToInstanceFinder`, which switched to the Instance Finder tab
and pre-filled `SearchClassName` — but stopped short of running the
query. Trigger `SearchCommand` immediately after pre-fill (with
`CanExecute` guard) so clicking "Find Instances" produces results
without an extra Search click.

**Game Class: add Package column** (`89f637b`)

The Super filter aligned with a Super column, but the Package filter
had no matching column — it was prefix-matching against ClassPath,
which displayed only the full `/Script/Engine.Actor` form. Added a
"Package" column showing the extracted prefix (`/Script/Engine`,
`/Game`, `/Script/ES2`, ...) next to Super and Path. Moved
`ExtractPackagePrefix` from the VM onto `GameClassEntry` so the column
binding and the filter logic share one implementation.

-----

## Capability matrix (current — build 525)

| Layer | Drill-down | Find Refs |
|-------|-----------|-----------|
| Object / Class / Interface | ✅ | ✅ |
| Weak / Soft{Class} / Lazy (single + array) | ✅ | ✅ |
| TArray of any pointer-shaped inner | ✅ | ✅ |
| TMap / TSet (Object/Class) | ✅ | ✅ (allocated slots only) |
| Delegate (single FScriptDelegate) | ✅ | ✅ (v3) |
| MulticastInline / MulticastDelegate | ✅ | ✅ (v3) |
| TArray<FScriptDelegate> | ✅ | ✅ (v3) |
| MulticastSparseDelegate | ⚠️ bound flag only | ❌ (needs FSparseDelegateStorage AOB + walk) |
| OptionalProperty\<pointer / weak / scalar\> | ✅ | ✅ |
| OptionalProperty\<Struct\> | ❌ inner struct fields not surfaced | — |
| FieldPathProperty | ❌ | ❌ |
| TMap / TSet with weak-like inner sides | — | ❌ (v4 candidate) |

## Remaining gaps (next-session pickup candidates)

1. **OptionalProperty\<StructProperty\> inner field drill-down** —
   intrusive layout, but inner struct sub-fields aren't surfaced. ES2
   has 5 such cases in real game classes
   (`WorldPartitionRuntimeCellData.CellBounds`,
   `FontFace.PlatformRasterization`,
   `MapScalabilityModifierComponent.VolumetricFog...`) so a fix would
   immediately validate against live data.

2. **MulticastSparseDelegateProperty bindings list** — bound flag is
   shown; full enumeration needs:
   - Universal AOB for `FSparseDelegateStorage::SparseDelegates` static
     map (effort: similar to existing 128 AOB patterns)
   - Nested `TMap<FObjectKey, TMap<FName, TSharedPtr<...>>>` walk
     (effort: NEW — TMap walking infra is mostly there but `FObjectKey`
     hashing + `TSharedPtr` control-block reading aren't)

3. **Find Refs v4** —
   - `TMap` / `TSet` with weak-like inner sides (currently Object/Class
     only)
   - MulticastSparseDelegate target scan (needs the storage AOB above)

4. **Find Refs auto-drill into array/map/set element [N]** — currently
   auto-scrolls to the container field, but the user has to click drill
   manually to reach the specific element.

5. **Soft array CE XML enhancement** — per-element group with FName leaf
   at +0x10 instead of just 8B hex.

6. **`FieldPathProperty`** — rare, low priority.

7. **GWorld**: Star Wars Jedi untested, Satisfactory fails.

8. **UE version misdetection** on some UE4 games (DQ I&II,
   Ghostwire: Tokyo show UE505 incorrectly).

## Tested games (last verified 2026-05-09)

- **Everspace 2** ✅ (UE 5.4): item template ID via container scan; Find
  Refs v3 returns 9 correct references in 224ms (cache hot, scan
  complete: 1180536/1180536); auto-scroll-to-field after Open works;
  Class Structure for `LocalPlayer` shows correct fields after the
  class-like routing fix; PropertySearch type filter `OptionalProperty`
  finds 9 matches across 5 real classes + 4 test-object fields.
- **DQ I&II HD-2D / FF7 Rebirth** (UE4): Char Lv / Cloud HP / Party Lv
  in non-reflected memory (custom allocator, high-memory region
  0x255*/0x296*/0x7FEF*). Container scan complete, 0 matches — out of
  reflection scope. Use CE pointer scan for these.
