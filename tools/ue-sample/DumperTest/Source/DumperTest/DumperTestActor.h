// ============================================================
// DumperTestActor — the property zoo UE5CEDumper is verified against.
//
// WHY THIS EXISTS. Half of docs/verification-register.md is
// blocked not on effort but on FINDING A GAME that happens to contain the right
// UPROPERTY. `TSet`/`TMap` scanning has been ⬜ since build 927, `TOptional`
// since 942, the NumericAll byte family since 796 — every one of them reads
// "needs a live game with such UPROPERTYs". This actor IS that game, and unlike
// a commercial title it is free, repeatable, and its expected values are written
// down.
//
// EVERY CJK LITERAL IS A \uXXXX ESCAPE, ON PURPOSE. A .cpp saved without a
// UTF-8 BOM is re-interpreted by MSVC through the system code page, which would
// silently corrupt exactly the strings B28 is about — the test would then be
// measuring the compiler, not the dumper. Escapes are encoding-proof. The glyphs
// are in the trailing comment so the file still reads.
//
// B28's trigger, restated so the literals below are checkable: an FText whose
// character count is EVEN and which contains a character whose LOW byte is 0x00
// (一 U+4E00, 最 U+6700, 言 U+8A00, 退 U+9000). In UTF-16LE such a character is
// stored `00 4E` — a NUL at an even byte offset, which is what a UTF-8/ASCII
// reader trips over.
// ============================================================
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "DumperTestTypes.h"
#include "DumperTestActor.generated.h"

/// A plain UObject (not an Actor) owned by ADumperTestActor.
///
/// Three jobs: (1) give Related Objects a genuine owned sub-object edge to
/// follow, (2) give "Locate in GWorld" a target that is only reachable THROUGH
/// an object pointer rather than through the level's actor list, and (3) give
/// Solide's force-ObjectProperty-to-null a strong pointer it is allowed to null
/// (weak/soft/lazy are refused by design).
UCLASS()
class DUMPERTEST_API UDumperTestPayload : public UObject
{
	GENERATED_BODY()

public:
	/// 統一言語 — even (4), contains U+4E00 and U+8A00. The B28 trigger, on a
	/// non-Actor UObject, because ReadFTextString does not care what owns it.
	UPROPERTY() FText   PayloadText;
	UPROPERTY() FString PayloadString;
	UPROPERTY() int32   PayloadValue = 0;

	void Populate();
};

/// ---------------------------------------------------------------------------
/// THE SPAWNER FAMILY — objects that appear and disappear ON DEMAND.
///
/// Everything already in this file MUTATES A FIELD on an object that exists for
/// the life of the level. Several verification rows need the other thing: objects
/// being CREATED and DESTROYED while the dumper watches. There is no way to ask a
/// commercial game to do that on cue, which is why those rows sat unrunnable.
///
/// The three classes below are a DISCRIMINATING SET, not three samples:
///   Holder  is the base.
///   Child   DERIVES from Holder and its name does NOT start with "Holder".
///   Decoy   does NOT derive from Holder but its NAME CONTAINS "DumperTestHolder".
/// A feature that claims to act on "a class and its subclasses" must hold Child and
/// must NOT hold Decoy. A substring match on the class name gets that exactly
/// backwards, and until now nothing in the tree could tell the two apart — which is
/// why Solide's derivation test has never actually been falsifiable here.
/// ---------------------------------------------------------------------------

/// Spawned in bulk by ADumperTestActor::Spawn_Holders.
UCLASS()
class DUMPERTEST_API ADumperTestHolder : public AActor
{
	GENERATED_BODY()

public:
	/// ⚠ DISTINCT PER INSTANCE (1000 + index), never a shared constant. A
	/// force-a-field-across-every-instance feature has to restore each instance's
	/// OWN prior value; if every instance started at the same number, restoring the
	/// wrong one to all of them is invisible. That defect shape is Solide L4.
	UPROPERTY() float HolderValue = 0.f;

	/// The bool lane of the same test (Solide reuses Solitar's bit writer for these).
	UPROPERTY() bool  bHolderFlag = false;

	/// Its own index, so a walker's report can be checked against ground truth
	/// instead of against another reading from the same walker.
	UPROPERTY() int32 HolderIndex = 0;
};

/// DERIVES from the base. A "holds subclasses too" claim MUST include this.
/// Deliberately not named `...HolderChild` first-token-wise — the point is that it
/// is reachable by DERIVATION and not by any string test on the base's name.
UCLASS()
class DUMPERTEST_API ADumperTestDerivedHolder : public ADumperTestHolder
{
	GENERATED_BODY()
};

/// Does NOT derive from ADumperTestHolder, but its class name CONTAINS
/// "DumperTestHolder" as a substring. A "holds subclasses too" claim MUST EXCLUDE
/// this. This is the negative half of the pair and the reason the pair exists.
UCLASS()
class DUMPERTEST_API ADumperTestHolderDecoy : public AActor
{
	GENERATED_BODY()

public:
	/// Same field NAME as the base's, so a match on the field rather than on the
	/// class cannot pass by accident either.
	UPROPERTY() float HolderValue = 0.f;
};

/// A UObject class with ZERO live instances until Spawn_LateInstance is called,
/// and no subclasses at all. That combination is the fixture AA12/AA13 step 3 needs:
/// "a legitimately empty result must NOT be reported as success". The previous
/// attempt picked NiagaraComponent, which turned out to have two live instances, so
/// the empty case was never actually exercised.
UCLASS()
class DUMPERTEST_API UDumperTestLateSpawn : public UObject
{
	GENERATED_BODY()

public:
	UPROPERTY() int32 LateValue = 0;
};

/// A second payload shape, allocated in alternation with UDumperTestPayload by
/// Spawn_RecycleChurn so a freed GObjects slot gets refilled by a DIFFERENT class.
/// ⚠ Same-class reuse does not test the identity guard at all: the stale pointer
/// still resolves to the same class and reads plausibly. Foreign-class slot reuse
/// is the whole defect.
UCLASS()
class DUMPERTEST_API UDumperTestPayloadB : public UObject
{
	GENERATED_BODY()

public:
	UPROPERTY() int32 BValue  = 0;
	UPROPERTY() float BScalar = 0.f;
};

UCLASS()
class DUMPERTEST_API ADumperTestActor : public AActor
{
	GENERATED_BODY()

public:
	ADumperTestActor();

	virtual void BeginPlay() override;
	virtual void Tick(float DeltaSeconds) override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

	// ========================================================
	// B28 — FText. The whole reason this project exists.
	// Expected: every one of these renders as CJK in Live Walker / Property
	// Search. FAIL is short ASCII punctuation soup (`,{1`, `-N?e`).
	// ========================================================

	/// 統一 — 2 chars (EVEN), one U+xx00 (一). Primary trigger.
	UPROPERTY() FText Text_Even2_OneNull;

	/// 一言 — 2 chars (EVEN), BOTH chars are U+xx00. Strongest trigger: the
	/// UTF-16LE bytes are `00 4E 00 8A`, i.e. NUL at offsets 0 and 2.
	UPROPERTY() FText Text_Even2_TwoNull;

	/// 統一言語 — 4 chars (EVEN), two U+xx00. Longer even case.
	UPROPERTY() FText Text_Even4_TwoNull;

	/// 走一步 — 3 chars (ODD), exactly one U+xx00 (一). CONTROL: odd length must render
	/// correctly both before and after the fix; if only this one works, the
	/// length parity is still being used as an encoding signal.
	UPROPERTY() FText Text_Odd3_OneNull;

	/// 日本語テスト — 6 chars (EVEN), NO U+xx00 anywhere. CONTROL: even length
	/// alone must not trigger anything.
	UPROPERTY() FText Text_Even6_NoNull;

	/// Pure ASCII. CONTROL for the other direction — a fix that swings to
	/// "always UTF-16" breaks this one.
	UPROPERTY() FText Text_Ascii;

	/// NSLOCTEXT, so this FText carries a DIFFERENT FTextHistory than the
	/// FromString ones above. Same glyphs as Text_Even4_TwoNull: if the two
	/// disagree, the bug is in history traversal, not in decoding.
	UPROPERTY() FText Text_Localized;

	/// Empty FText — the null/empty display-string path.
	UPROPERTY() FText Text_Empty;

	// ========================================================
	// FString mirrors of the same strings. These went through the UTF-16-only
	// reader and NEVER had B28, so they are the control group: if an FString
	// here is wrong too, the problem is not B28 and the FText result means
	// nothing.
	// ========================================================
	UPROPERTY() FString Str_Even2_OneNull;
	UPROPERTY() FString Str_Even4_TwoNull;
	UPROPERTY() FString Str_Odd3_OneNull;
	UPROPERTY() FString Str_Even6_NoNull;

	/// FName holding CJK — the FNamePool path (Serie), which is neither of the
	/// two above.
	UPROPERTY() FName Name_Cjk;

	// ========================================================
	// Value Search containers — ⬜ since builds 796 / 927 / 942 purely for want
	// of a game containing them.
	// ========================================================

	/// V1a. Expect rows rendered as `Set[idx]`. Scan 4242.
	UPROPERTY() TSet<int32> Set_Int;

	/// V1a. Expect `Map.Key[idx]` / `Map.Value[idx]`. Scan 222.
	UPROPERTY() TMap<FName, int32> Map_NameToInt;

	/// V1a, non-FName key so the key type is not the only shape tested.
	UPROPERTY() TMap<int32, float> Map_IntToFloat;

	UPROPERTY() TArray<int32> Arr_Int;

	/// The struct-element container the opt-in deep descent walks.
	UPROPERTY() TArray<FDumperTestStat> Arr_Struct;

	// ========================================================
	// Audit #5 cluster ① — container GEOMETRY witnesses (2026-08-14).
	//
	// EVERY container above is blind to the stride/offset defects M1/M2/M3/A2:
	// Map_NameToInt and Map_IntToFloat both have 4-aligned pairs (stride 20 and 16
	// before AND after the fix), TSet is unaffected by design, and TArray is not a
	// sparse container at all. That is the same blind spot dll_helpers_test had —
	// everything happened to land on a multiple of 8, so nothing discriminated.
	// Each property below is chosen for the specific arithmetic it exposes.
	// ========================================================

	/// M1. pairAlign 8, unpadded pair 12 → stride **24**; the broken build strides
	/// **20**, so every element after index 0 reads from its predecessor's tail.
	/// int64 key rather than UObject* so no GC/lifetime variable enters the test.
	UPROPERTY() TMap<int64, int32> Map_I64ToI32;

	/// M1, second witness with DIFFERENT arithmetic: unpadded pair 20 → stride
	/// **32** (broken: **28**). One wrong assumption cannot satisfy both this and
	/// Map_I64ToI32, which is the point of having two.
	UPROPERTY() TMap<FString, int32> Map_StrToInt;

	/// M3. FDumperTestVec3f is 4-aligned, so the value sits at **+4** and the pair
	/// is 16 → stride **24**. The broken size guess puts the value at **+8** with a
	/// stride of 28, so even ELEMENT 0 reads wrong — the only container here that
	/// fails at index 0. Doubles as A4's target: a scalar leaf inside a map's
	/// struct side, which the Deep pass must reach.
	UPROPERTY() TMap<int32, FDumperTestVec3f> Map_IntToVec3f;

	/// A2 + M2. 200 entries forces the TBitArray past its 128-bit inline buffer
	/// onto the heap; removing a LOW index (9005, index 5) then proves the walker
	/// reads the heap copy rather than the inline words frozen at spill time. The
	/// broken build still lists the removed element. M2: the header count must
	/// equal the number of rows rendered (NumFreeIndices read 0 before the fix).
	UPROPERTY() TSet<int32> Set_Big;

	/// A4, set side — a struct element whose scalar leaf Deep must reach.
	UPROPERTY() TSet<FDumperTestVec3f> Set_Struct;

	// ========================================================
	// V1c — TOptional. Verified available in UE 5.4: FOptionalProperty exists
	// (PropertyOptional.h) and UHT resolves it (UhtOptionalProperty.cs); the
	// only inner-type rule is CanBeContainerValue, which int32/float/FString
	// all satisfy. The engine itself ships TOptional<FBox> / TOptional<uint32>
	// UPROPERTYs.
	// ========================================================
	UPROPERTY() TOptional<int32>   Opt_Int_Set;
	UPROPERTY() TOptional<float>   Opt_Float_Set;
	UPROPERTY() TOptional<FString> Opt_Str_Set;

	/// Deliberately LEFT UNSET. The acceptance criterion is negative: a scan for
	/// 0 must NOT surface this (the bIsSet gate). An unset optional that shows
	/// up as a zero is the bug.
	UPROPERTY() TOptional<int32> Opt_Int_Unset;

	// ========================================================
	// NumericAll / byte families. -5 and 255 are the exact boundary cases
	// BuildNumericTargets' range gating is unit-tested against (300 → no
	// Int8/UInt8; -5 → Int8 yes / UInt8 no), so a live scan here is directly
	// comparable to the offline expectation.
	// ========================================================
	UPROPERTY() int8   I8_Neg;
	UPROPERTY() uint8  U8_Small;
	UPROPERTY() uint8  U8_Max;
	UPROPERTY() int16  I16;
	UPROPERTY() uint16 U16;
	UPROPERTY() int32  I32;
	UPROPERTY() int64  I64;

	/// 513.36f is the repo's own worked example for the Round/Trunc/Ceil switch
	/// (it renders as 513.36 / 513.4 / 513 depending on mode).
	UPROPERTY() float  F32;
	UPROPERTY() double F64;

	// ========================================================
	// RAW, NON-UPROPERTY members — invisible to reflection, so they become the
	// holes "Guess What" (Ubel::GuessGapTypes) and the Native-C value scan are
	// supposed to find. Deliberately sited HERE, in the middle of the reflected
	// fields, so the gap is INTERIOR: a trailing raw block would still be inside
	// PropertiesSize but is the easy case, and interior holes are what a real
	// game's native HP/MP look like.
	// ========================================================
	int32  RawInt;
	float  RawFloat;
	double RawDouble;

	// ---- bool masks: three bitfields sharing one byte, plus a full bool ----
	UPROPERTY() uint8 bFlagA : 1;
	UPROPERTY() uint8 bFlagB : 1;
	UPROPERTY() uint8 bFlagC : 1;
	UPROPERTY() bool  bPlainBool;

	UPROPERTY() EDumperTestGrade Grade;

	/// ArrayDim > 1 — a C array UPROPERTY, which is a different property shape
	/// from TArray and is easy to get wrong in an exporter.
	UPROPERTY() int32 FixedArr[8];

	/// Nested StructProperty, GAS-attribute shaped.
	UPROPERTY() FDumperTestAttribute Health;

	/// ⭐ A7 fixture — the only reachable instance of the empty-base pair. Without a
	/// UPROPERTY of this type nothing loads FDumperTestBracketPayload, and an
	/// unloaded struct is invisible to a whole-pool walk (see export-formats.md's
	/// Coverage section). See FDumperTestEmptyBase in DumperTestTypes.h for what
	/// the pair proves and why no shipping title could supply it.
	UPROPERTY() FDumperTestBracketPayload EmptyBasePayload;

	/// Strong object pointer to the owned payload.
	UPROPERTY() TObjectPtr<UDumperTestPayload> Payload;

	// ========================================================
	// Group Scan / Snapshot Mode B (temporal). The documented hard case is
	// "Current HP went DOWN while Max HP stayed UNCHANGED" — groups need
	// Unchanged. Health.CurrentValue falls 1/sec and wraps 1 → 100 (so both
	// Decreased and Increased occur); Health.BaseValue never moves. TickCount
	// rises monotonically; FrozenInt is the never-touched control.
	// ========================================================
	UPROPERTY() int32 TickCount;
	UPROPERTY() int32 FrozenInt;

	// ========================================================
	// TICKING float / double. The F32 / F64 above are STATIC on purpose -- F32 is the
	// repo's Round/Trunc/Ceil worked example (513.36) and moving it would break that --
	// so until now a Changed / Increased / Decreased scan had no float or double target
	// anywhere in the sample. These two move every second, one down and one up, so each
	// prev-value predicate has a guaranteed hit at each width.
	//
	// Appended at the END of the class, not slotted next to their static twins, so that
	// every offset the docs quote (TickCount +0x518, FrozenInt +0x51C, Opt_Int_Set
	// +0x468, Set_Int +0x358) still points at the same field.
	// ========================================================

	/// Falls 10.25/sec from 1000.5, wraps after ~96 s -- Decreased every second, and the
	/// wrap makes Increased reachable INSIDE a normal session (a 0.5 step would have taken
	/// 33 minutes, so that half would never have been observed).
	UPROPERTY() float  F32_Ticking;

	/// Rises 0.25/sec from 20000.125 -- Increased, always, never wraps.
	UPROPERTY() double F64_Ticking;

	// ========================================================
	// TICKING RAW members -- NOT UPROPERTY, so the ordinary scan cannot see them and
	// only the opt-in "Native-C (raw)" scan can. The static RawInt / RawFloat / RawDouble
	// above are the INTERIOR-hole case; these are trailing, which is the easier case for
	// hole detection but still inside PropertiesSize, and it is the price of not shifting
	// every documented offset. What they add is the half the static ones cannot test:
	// they MOVE, so a Native-C first scan can be refined with Changed / Increased /
	// Decreased instead of dying at the first prev-value pass.
	//
	// They are also on the HUD, so the value to search for can be read off the screen --
	// which for a raw member is the only way to know it, there being no reflection to ask.
	// ========================================================

	/// Rises 7/sec from 700000.
	int32  RawInt_Ticking;

	/// Falls 3.25/sec from 300.25, wraps after ~91 s.
	float  RawFloat_Ticking;

	/// Rises 0.5/sec from 50000.5.
	double RawDouble_Ticking;

	/// Frames since BeginPlay, for the on-screen readout (ADumperTestHUD).
	/// An accessor, NOT a UPROPERTY -- see the member below.
	int32 GetFrameCount() const { return FrameCount; }

	// ========================================================
	// MG2 / V8 / V1a / AD4 — the four rows that were blocked on "find a game
	// that happens to contain this". Same premise as the rest of this actor,
	// extended to the four samples it did not yet have.
	//
	// ⛔ NOT driven by a UCheatManager, and that is not a style choice.
	// CheatManagerDefines.h: `#define UE_WITH_CHEAT_MANAGER (1 && !UE_BUILD_SHIPPING)`
	// and APlayerController::AddCheats wraps its whole body in it
	// (PlayerController.cpp:1107-1110) — so a cheat manager compiles to NOTHING in
	// the Shipping package that actually gets tested. These are plain
	// BlueprintCallable UFUNCTIONs instead, invoked through the dumper's own
	// `invoke_function` pipe command, which works in Shipping and is scriptable
	// with no keyboard in the loop. (Fourth wrong-Shipping-gate near-miss in this
	// file; this one was checked by opening the header, not by inferring.)
	// ========================================================

	/// MG2 step 2 — `TSet<FName>`. The engine itself ships this shape as a
	/// UPROPERTY (`TSet<FName> MetaDataTagsForAssetRegistry`), so it is legal.
	UPROPERTY() TSet<FName> Set_Name;

	/// MG2 step 2 — `TSet<UObject*>`. Engine precedent:
	/// `TSet<TObjectPtr<UObject>> TemporarilyReferencedObjects`.
	/// Populated with this actor's own Payload plus a few fresh UObjects, so the
	/// set has real, resolvable pointers rather than nulls.
	UPROPERTY() TSet<TObjectPtr<UObject>> Set_Object;

	/// MG2 step 2 — a `UDataTable` small enough to read whole (8 rows).
	UPROPERTY() TObjectPtr<UDataTable> Table_Small;

	/// V8 — a `UDataTable` with MORE THAN 64 rows, which is the entire fixture
	/// that row has been waiting for. 100 rows: comfortably past the 64 cap, and
	/// the "showing 64 of 100" string has two distinct numbers so a wrong one is
	/// visible rather than plausible.
	UPROPERTY() TObjectPtr<UDataTable> Table_Big;

	/// MG2 step 1 / V1a step 1 — a map deliberately kept UNDER the 128 array
	/// limit, so the header count and the rendered row count must agree exactly.
	/// Mutated by MG2_RemoveOneMapEntry / V1a_GrowMap, which is what makes the
	/// "remove one and re-read" check runnable without a commercial game.
	UPROPERTY() TMap<int32, int32> Map_Churn;

	/// V1a step 1 — the reallocating container. Growing a TArray past its slack
	/// MOVES its data, which is the event that must invalidate a Next Scan
	/// candidate instead of reporting a stale address.
	UPROPERTY() TArray<int32> Arr_Churn;

	/// AD4 step 4 — when true, Tick re-asserts `bCanBeDamaged = true` on the
	/// player pawn every frame, i.e. the game FIGHTS a God Mode hold. That is
	/// exactly the state the badge calls `ON (contested)`
	/// (Want=1, Live=0, Resolvable=true). Off by default so the same fixture
	/// gives the negative control: contention off -> plain green `ON`.
	/// Not a UPROPERTY: it is a test knob, not a scan target.
	bool bContestDamage = false;

	/// AD4 — how many times the contention writer has actually written. Lets a
	/// run prove the contest was live rather than assuming it, the same way
	/// FrameCount separates "no timer" from "no actor".
	int32 ContestWrites = 0;

	/// Bumped by every BuildTable call so each UDataTable gets a UNIQUE object
	/// name. Not cosmetic: NewObject with an explicit name that already exists
	/// under this Outer tears the OLD object down while Table_Big still points
	/// at it. The suffix also makes a rebuild visible in the object list.
	int32 TableSerial = 0;

	// -------- mutators, invoked via the dumper's invoke_function --------

	/// MG2 step 1 — remove ONE entry from Map_Churn. Re-read the map afterwards:
	/// the header count and the row count must both drop by one and still agree.
	UFUNCTION(BlueprintCallable, Category = "DumperTest|MG2")
	void MG2_RemoveOneMapEntry();

	/// MG2 step 1, set flavour — remove one element from Set_Name.
	UFUNCTION(BlueprintCallable, Category = "DumperTest|MG2")
	void MG2_RemoveOneSetEntry();

	/// V1a step 1 — force a REALLOCATION by appending Count elements to
	/// Arr_Churn and adding the same number to Map_Churn. Call this between a
	/// First Scan and a Next Scan.
	UFUNCTION(BlueprintCallable, Category = "DumperTest|V1a")
	void V1a_GrowContainers(int32 Count = 64);

	/// V1a step 1 — the other direction: empty both containers so the candidate
	/// addresses become unreadable rather than merely moved.
	UFUNCTION(BlueprintCallable, Category = "DumperTest|V1a")
	void V1a_ShrinkContainers();

	/// V8 / MG2 step 2 — rebuild Table_Big with Rows rows. Default 100 is the V8
	/// fixture; pass 8 to check the un-capped case renders no warning at all.
	UFUNCTION(BlueprintCallable, Category = "DumperTest|V8")
	void V8_RebuildBigTable(int32 Rows = 100);

	/// V8 — remove one row, so the ">64" banner's N is seen to change rather
	/// than being a constant that could be hard-coded.
	UFUNCTION(BlueprintCallable, Category = "DumperTest|V8")
	void V8_RemoveOneTableRow();

	/// AD4 step 4 — turn the God Mode contention on or off. With it ON, Solide's
	/// hold and this writer fight every frame and the badge should reach
	/// `ON (contested)`; with it OFF the same session must settle to plain `ON`,
	/// which is the control that makes the amber reading mean something.
	UFUNCTION(BlueprintCallable, Category = "DumperTest|AD4")
	void AD4_SetDamageContention(bool bEnabled);

	/// AD4 — read back how many contention writes have happened, so a run can
	/// show the writer was actually firing during the observation window.
	UFUNCTION(BlueprintCallable, Category = "DumperTest|AD4")
	int32 AD4_GetContestWrites() const;

	// ======== the spawner (see the class comments above) =====================
	//
	// ⚠ EVERY ONE OF THESE IS DECLARED HERE, ON ADumperTestActor, and none is
	// inherited. Two prior attempts at rows like these died at `0 functions walked`
	// because they targeted INHERITED functions ([INVOKEINHERIT-2026-08-20]).
	// Declaring them here is not a style choice.

	/// GC roots. Without them the spawned objects are collected at an arbitrary
	/// moment and every count below becomes a race rather than a measurement.
	UPROPERTY() TArray<TObjectPtr<AActor>>  SpawnedHolders;
	UPROPERTY() TArray<TObjectPtr<UObject>> LateSpawns;

	// ========================================================
	// APPENDED-AT-END FIXTURES (U7 / U11 / U3-U17 / Y15).
	//
	// Deliberately NOT slotted beside their topical siblings. README.md's own rule:
	// new members go at the END so every offset the docs quote (TickCount +0x518,
	// FrozenInt +0x51C, Opt_Int_Set +0x468, Set_Int +0x358) still points at the
	// same field. Slotting Str_Even22_TwoNull into the Str_* block would move all four.
	// ========================================================

	/// U7 -- 22 CJK chars = 66 UTF-8 bytes. Property Search cuts previews at
	/// 50 BYTES (Ubel.cpp:5950), and this is the ONLY field here where that cut
	/// lands mid-sequence: the four Str_* above are 18 bytes at most, so they can
	/// never reach it.
	UPROPERTY() FString Str_Even22_TwoNull;

	/// U11 -- the only TOptional<FText>. Opt_Str_Set takes the string-inner arm and
	/// the Text_* family takes the plain TextProperty arm, so neither exercises the
	/// text-inner arm this one is for.
	UPROPERTY() TOptional<FText> Opt_Text_Set;

	/// Deliberately LEFT UNSET -- the FText sibling of Opt_Int_Unset.
	/// WEAK half, and labelled as such: on zeroed UObject memory a sentinel and the
	/// true bIsSet byte agree, so this passes on a correct build AND on a
	/// sentinel-based one. Opt_Text_Set alone carries U11.
	UPROPERTY() TOptional<FText> Opt_Text_Unset;

	/// U3/U17 step 3, map side -- a 24-byte 3x DOUBLE (LWC) FVector as a container
	/// element. Map_IntToVec3f is FDumperTestVec3f, three 4-byte FLOATS, and
	/// structurally cannot reach the 24-byte case.
	UPROPERTY() TMap<int32, FVector> Map_IntToVecLwc;

	/// U3/U17 step 3, set side -- its own stride/alignment path. Set_Struct is
	/// 12-byte and 4-aligned; this is the first 24-byte 8-ALIGNED set element here.
	UPROPERTY() TSet<FVector> Set_VecLwc;

	/// Y15 step 6 -- the 4-byte EnumProperty WRITE target. Engine enums of this
	/// width live on CDO-only classes, and CDOs are dropped before the instance cap,
	/// so a freeze on one resolves 0 instances and no byte is ever written.
	/// ADumperTestActor has live non-CDO instances.
	UPROPERTY() EDumperTestWideGrade WideGrade;

	/// The OVER-wide-write guard ONLY. It cannot detect a SHORT write: a 1-byte
	/// write leaves bytes +1..+3 of WideGrade itself stale and never reaches here.
	/// The short-write discriminator is Wide_Base and Wide_Target sharing a low byte.
	UPROPERTY() int32 WideGuard = 0;

	/// Bumped on every spawn/destroy round so a harness can prove churn ACTUALLY
	/// HAPPENED rather than assuming its invoke landed. A changed count with a flat
	/// generation means something other than these functions moved the numbers.
	int32 SpawnGeneration = 0;

	/// Address of the most recent allocation in a recycle round, handed out rather
	/// than left for the harness to guess. Not a UPROPERTY: a test knob.
	uint64 LastRecycledAddr = 0;

	/// Spawn `Count` holders at this actor's location. `bDerived` spawns
	/// ADumperTestDerivedHolder instead, so one call builds the positive half of the
	/// derivation pair. HolderValue is seeded 1000 + index — DISTINCT, see the class
	/// comment. Default 300 is deliberately above Solide's 256 cap so the capped
	/// badge has a local, deterministic trigger.
	UFUNCTION(BlueprintCallable, Category = "DumperTest|Spawn")
	void Spawn_Holders(int32 Count = 300, bool bDerived = false);

	/// Spawn `Count` decoys — the class that must NOT be held.
	UFUNCTION(BlueprintCallable, Category = "DumperTest|Spawn")
	void Spawn_Decoys(int32 Count = 8);

	/// Destroy every spawned actor, empty the root array, and FORCE A GC so the
	/// GObjects slots are really freed rather than merely unreferenced.
	UFUNCTION(BlueprintCallable, Category = "DumperTest|Spawn")
	void Spawn_DestroyHolders();

	/// How many are alive right now, counted from the game side.
	UFUNCTION(BlueprintCallable, Category = "DumperTest|Spawn")
	int32 Spawn_CountHolders() const;

	/// The churn counter described above.
	UFUNCTION(BlueprintCallable, Category = "DumperTest|Spawn")
	int32 Spawn_Generation() const;

	/// Create ONE UDumperTestLateSpawn. Before the first call that class has zero
	/// live instances, which is the state AA12/AA13 step 3 needs to observe.
	UFUNCTION(BlueprintCallable, Category = "DumperTest|Spawn")
	void Spawn_LateInstance();

	/// Alternate UDumperTestPayload / UDumperTestPayloadB allocations with a GC
	/// between rounds, so a freed slot is refilled by a DIFFERENT class.
	UFUNCTION(BlueprintCallable, Category = "DumperTest|Spawn")
	void Spawn_RecycleChurn(int32 Rounds = 32);

	/// The last address handed out by Spawn_RecycleChurn, as an integer the pipe can
	/// carry. int64 not uint64: BlueprintCallable has no unsigned 64-bit type.
	UFUNCTION(BlueprintCallable, Category = "DumperTest|Spawn")
	int64 Spawn_LastRecycledAddr() const;

	/// Attach `Count` bare UActorComponents to this actor, pushing the live
	/// ActorComponent-derived pool past 256 (and past 1024) without needing a
	/// commercial title that happens to have a big pool.
	UFUNCTION(BlueprintCallable, Category = "DumperTest|Spawn")
	void Spawn_ManyComponents(int32 Count = 1500);

private:
	/// Build a UDataTable at runtime with `Rows` rows. No cooked asset involved.
	UDataTable* BuildTable(const TCHAR* Name, int32 Rows);

private:
	void OnSecondTick();

	/// Install ADumperTestHUD on the local PlayerController if it is not already there.
	///
	/// Called from **Tick**, not BeginPlay and NEVER from the 1 Hz timer, and written as a
	/// re-assert rather than a one-shot. BeginPlay is too early -- this actor is spawned by
	/// a UWorldSubsystem and routinely beats the PlayerController into existence -- and a
	/// travel replaces the controller underneath us later.
	///
	/// The timer is ruled out for a stronger reason: it is the thing the readout EXISTS TO
	/// MEASURE. Install from there and a dead timer shows as a blank screen, which is
	/// indistinguishable from "the sample never spawned" -- the exact confusion the
	/// split-clock design was built to end. From Tick, a dead timer shows as a frozen
	/// TickCount beside a climbing `frames`, which is the diagnosis.
	///
	/// NOTE: APlayerController::ClientSetHUD DESTROYS the current HUD (PlayerController.cpp:1332).
	/// The Third Person template ships no custom HUD so nothing is lost, but a project
	/// that has one would lose it -- hence -DumperTestNoHud to opt out entirely.
	void EnsureHeartbeatHud();

	/// Frames since BeginPlay. Driven by Tick, so it advances even if the 1 Hz timer
	/// never fires -- which is the entire point: a blank screen and a dead timer used
	/// to look identical, because the heartbeat was drawn BY the thing it was meant to
	/// be testing. Not a UPROPERTY: it must not become another scan target.
	int32 FrameCount = 0;

	FTimerHandle TickHandle;
};
