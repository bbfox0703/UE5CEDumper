// ============================================================
// DumperTestActor — the property zoo UE5CEDumper is verified against.
//
// WHY THIS EXISTS. Half of docs/todo.md § Pending live-game verification is
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
