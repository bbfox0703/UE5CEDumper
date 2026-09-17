// ============================================================
// DumperTestTypes — enum + structs for the dumper verification actor.
//
// Split from DumperTestActor.h so the struct/enum shapes can be reused (and so
// a UHT failure on one of them is easy to isolate).
//
// Every value here is DELIBERATE. Nothing is a placeholder — each field exists
// to settle a specific ⬜ row in docs/verification-register.md.
// If you change a literal, change the expectation in tools/ue-sample/README.md
// with it, or the next person will scan for a number that is no longer there.
// ============================================================
#pragma once

#include "CoreMinimal.h"
#include "Engine/DataTable.h"   // FTableRowBase — the V8 / MG2 row shape
#include "DumperTestTypes.generated.h"

/// Deliberately NON-contiguous so an enum-name lookup cannot pass by accident
/// through "index == value". Legend=7 leaves a hole at 3..6.
UENUM(BlueprintType)
enum class EDumperTestGrade : uint8
{
	Rookie  = 0   UMETA(DisplayName = "Rookie"),
	Veteran = 1   UMETA(DisplayName = "Veteran"),
	Elite   = 2   UMETA(DisplayName = "Elite"),
	Legend  = 7   UMETA(DisplayName = "Legend"),
};

/// Y15 step 6 -- a **4-byte** EnumProperty, which this zoo did not have.
/// `enum class : int32`, and both halves of that are forced:
///   * NOT a raw `UENUM enum` -- UHT rejects a raw enum as a member UPROPERTY, and
///     TEnumAsByte<> would give a 1-byte ByteProperty that never reaches the
///     "EnumProperty" arm at all.
///   * NOT BlueprintType -- a non-uint8 base on a BlueprintType enum is a UHT error.
/// EDumperTestGrade above is `enum class : uint8`, i.e. ONE byte, so it cannot
/// stand in for this.
UENUM()
enum class EDumperTestWideGrade : int32
{
	Wide_Zero   = 0,
	Wide_Base   = 24000,        // 0x00005DC0 -- byte 1 is 0x5D, NON-ZERO
	Wide_Target = 16064,        // 0x00003EC0 -- shares the low byte 0xC0 with Wide_Base
	Wide_High   = 0x007F0000,   // byte 2 set, so a 2-byte mis-width is caught too
};

/// L39 -- the element type of `Probe_Lanes`, the `TArray<TEnumAsByte<E>>` this zoo had
/// nowhere. Three things about it are forced:
///   * a RAW `enum`, not `enum class`. `TEnumAsByte<>` only accepts an unscoped enum, and
///     an unscoped UENUM is also what makes a UEnum store its members UNPREFIXED
///     (`Lane_Left`, not `EDumperTestLane::Lane_Left`) -- so this doubles as the control
///     arm for `[USMAP-ENUM-NAME-QUALIFIED]`, whose fix strips the prefix a SCOPED enum
///     carries. EDumperTestWideGrade above is the scoped half of that pair.
///   * `: int` explicitly. UE 5.4 UHT wants an underlying type spelled out, and `int` is
///     what the engine's own TEnumAsByte hosts use (`ECollisionChannel`).
///   * every value <= 255. `TEnumAsByte` stores ONE byte; a larger enumerator would be
///     silently truncated and the fixture would document a value it does not hold.
/// Values are non-contiguous and non-sorted so an element read at the wrong stride cannot
/// land on a plausible-looking neighbour.
UENUM()
enum EDumperTestLane : int
{
	Lane_None   = 0,
	Lane_Left   = 11,
	Lane_Center = 33,
	Lane_Right  = 22,
};

/// L30 + L39 -- the values BP_UsmapProbe's CDO must carry, as CODE rather than as prose.
///
/// ⛔ They are NOT the actor's constructor defaults, and cannot be: unversioned property
/// serialization writes only what differs from the archetype, so a probe seeded to the
/// same value in C++ would be ABSENT from the cooked stream and both mappings would
/// "agree" on nothing at all. The constructor leaves the probes quiet; the ASSET is loud.
///
/// Declaring them here rather than in the authoring script gives the three readers that
/// must not drift -- the asset, README.md's table and check_ue_sample_values.py -- one
/// source of truth. tools/ue-sample/make_usmap_probe.py parses these two lines.
namespace DumperTestUsmapProbe
{
	/// The int32 declared immediately AFTER the 4-byte enum. Its whole job is to be read
	/// at the wrong offset when the enum's width is described wrongly.
	inline constexpr int32 AfterWideEnum = 0x11223344;

	/// The int32 declared immediately AFTER the TEnumAsByte array. A DIFFERENT constant
	/// from the one above on purpose -- equal guards could mask a shift from one onto the
	/// other.
	inline constexpr int32 AfterLanes = 0x44556677;
}

/// Two-float struct in the shape of a GAS `FGameplayAttributeData`
/// (BaseValue / CurrentValue). Exists for the nested-StructProperty capture and
/// the "Flatten GAS attributes" CE-export toggle, which special-cases exactly
/// this shape. CurrentValue is driven by the actor's 1 Hz timer; BaseValue is
/// held still on purpose — see the group-scan note in the README.
USTRUCT(BlueprintType)
struct FDumperTestAttribute
{
	GENERATED_BODY()

	UPROPERTY() float BaseValue = 0.f;
	UPROPERTY() float CurrentValue = 0.f;
};

/// ⭐ A7 fixture — an EMPTY native USTRUCT, and a child whose first field really is
/// at offset 0. This pair exists because NOTHING ELSE PRODUCES IT: measured
/// 2026-09-05 across EVERSPACE 2 (3,808 loaded classes) and DumperTest's own
/// reachable structs, **zero** had `PropertiesSize == 1`, so audit A7's fix had no
/// live vehicle on any installed title.
///
/// Why 1 and not 0: UE reports an empty native USTRUCT's `PropertiesSize` as **1**
/// (a zero-size struct is not addressable in C++). The SDK emitter splits own from
/// inherited fields on `Offset >= superPropsSize`, so with `superPropsSize == 1` a
/// child field at offset 0 fell BELOW the floor, was dropped, and the trailing-pad
/// pass wrote `Pad_0000[0x1]` in its place. Empty-base optimisation means the child
/// genuinely starts at 0, so this is not a synthetic shape — it is what EBO does.
///
/// Expected once walked: base `props_size == 1`, no fields, `own_props_start == -1`;
/// child `super_props_size == 1`, `own_props_start == 0`, `Description` at offset 0.
USTRUCT()
struct FDumperTestEmptyBase
{
	GENERATED_BODY()
	// Deliberately EMPTY. Adding a UPROPERTY here destroys the fixture.
};

/// The derived half of the A7 pair. ONE field, and it must sit at offset 0 — that
/// is the whole test. See FDumperTestEmptyBase above.
USTRUCT()
struct FDumperTestBracketPayload : public FDumperTestEmptyBase
{
	GENERATED_BODY()

	UPROPERTY() FString Description;
};

/// Struct used as a CONTAINER ELEMENT (`TArray<FDumperTestStat>`). That is the
/// one-struct-element-deep container level the opt-in deep descent walks, and it
/// carries an FText so the deep path has an FText to mis-decode if B28 ever
/// regresses inside a container.
USTRUCT(BlueprintType)
struct FDumperTestStat
{
	GENERATED_BODY()

	UPROPERTY() FName StatName;
	UPROPERTY() int32 Value = 0;
	UPROPERTY() FText Label;
};

// ============================================================
// FDumperTestVec3f — deliberately 4-ALIGNED POD (audit #5 M3).
//
// Three floats: no FText, no pointer, no double. That matters. FDumperTestStat
// above carries an FText, i.e. a TSharedRef, i.e. 8 bytes of alignment — which
// is EXACTLY the case the broken size guess ("value >= 8 bytes => align 8")
// happens to get right. A struct that is genuinely 4-aligned is the only shape
// that can tell Ubel::GetStructAlignment's real MinAlignment read apart from
// that guess: as a TMap value it sits at +4, where the guess says +8.
//
// operator== and GetTypeHash are required by UE for a TSet element / TMap key.
// ============================================================
USTRUCT()
struct FDumperTestVec3f
{
	GENERATED_BODY()

	UPROPERTY() float X = 0.f;
	UPROPERTY() float Y = 0.f;
	UPROPERTY() float Z = 0.f;

	bool operator==(const FDumperTestVec3f& Other) const
	{
		return X == Other.X && Y == Other.Y && Z == Other.Z;
	}
};

FORCEINLINE uint32 GetTypeHash(const FDumperTestVec3f& V)
{
	return HashCombine(HashCombine(GetTypeHash(V.X), GetTypeHash(V.Y)), GetTypeHash(V.Z));
}

/// Row shape for the two runtime-built `UDataTable`s (V8, MG2 step 2).
///
/// It is a ROW STRUCT, so it must derive from `FTableRowBase` — that is what lets
/// `UDataTable::AddRow` copy it in. Built at RUNTIME rather than shipped as a
/// content asset, which is the whole trick: `AddRow`/`RemoveRow` sit OUTSIDE
/// `WITH_EDITOR` (DataTable.h:316-319), so a packaged Shipping build can construct
/// a table of any size with no cooked asset and no editor round-trip.
///
/// Carries an FText on purpose, for the same reason `FDumperTestStat` does: if B28
/// ever regresses, a DataTable row is another place it can surface.
USTRUCT()
struct FDumperTestTableRow : public FTableRowBase
{
	GENERATED_BODY()

	UPROPERTY() int32 Index = 0;
	UPROPERTY() FName Label;
	UPROPERTY() float Value = 0.f;

	/// 走一步 — odd (3), contains U+4E00. Escaped, per the file header rule.
	UPROPERTY() FText Caption;
};

// ============================================================
// A9 — the three-level container the per-object deep-walk budget needs.
//
// ⛔ A FLAT 500x500 DOES NOT WORK, and that was the original proposal. Aura's deep
// walk clamps EVERY container at 256 elements before the budget is consulted, so
// 500x500 visits 65,792 elements and never approaches the 50,000-element budget in
// any way that distinguishes "budget bit" from "clamp bit". THREE levels is what
// makes the two separable: the unclamped visit count is 256 + 256^2 + 256^3 ~= 16.8M
// against a 50,000 budget, a ~335x ratio that is measurable on a wall clock.
//
// Seed with A9_BuildDeepContainers(300, 300, 300) for the positive case. The
// NEGATIVE control is (30, 30, 30) = 27,930 visits, which is UNDER budget and must
// therefore reach every leaf.
// ============================================================
USTRUCT()
struct FDumperTestDeepLeaf
{
	GENERATED_BODY()

	UPROPERTY() TArray<float> Leaves;
};

USTRUCT()
struct FDumperTestDeepMid
{
	GENERATED_BODY()

	UPROPERTY() TArray<FDumperTestDeepLeaf> Subs;
};

// ============================================================
// Deep Value-Search multi-level 🌍 drill.
//
// The only known witness for "a multi-[N] locate lands on the SEEDED element rather
// than the first one" was a commercial title. Two element hops are the minimum that
// can tell a correct implementation from one that parses only the LAST [N].
//
// ⚠ The OUTER container must stay a TArray. A top-level TSet/TMap of structs has its
// element direct-fields collected by NEITHER of the two capture paths, so a Set/Map
// here returns zero rows for a reason that has nothing to do with the drill.
//
// Seeded 3 blocks x 5 ints with value 7000 + block*100 + element, so every leaf is
// unique and its address is derivable from its path: Arr_TuneBlocks[2].Tunes[4] is
// 7204, while a one-hop parse lands on 7104 and is visibly wrong.
// ============================================================
USTRUCT()
struct FDumperTestTuneBlock
{
	GENERATED_BODY()

	UPROPERTY() FName BlockName;
	UPROPERTY() TArray<int32> Tunes;
};

// ============================================================
// FDumperTestInvokeProbe — the host for [P3-INVOKE-STRUCT-FSTRING] (FIX PASS live check L10).
//
// A struct PARAM with an FString MEMBER. Before that fix, the app's FIRE sent a string member's
// typed text down the scalar route, i.e. as a raw int32 over FString.Data -- so a typed "42"
// handed the callee Data = 0x2A. The fix refuses typed text in a string member and leaves its
// 16 bytes zeroed, which is the valid empty FString {null, 0, 0}.
//
// ⭐ The two int32s are the CONTROL, not decoration: they must arrive exactly as typed while the
// string member between them arrives empty. A zeroed buffer would pass the string half by
// accident; a Head/Tail that also read 0 would say nothing was written at all.
//
// Layout on x64: Head +0, Label +8 (16 bytes), Tail +24 -- 32 bytes, 8-aligned. Read the
// offsets off the running game's param walk rather than trusting this line.
// ============================================================
// ============================================================
// FDumperTestStrRow — a struct ARRAY ELEMENT with an FString MEMBER (live check L28).
//
// `[W5-CEXML-FSTRING]`'s fourth entrance is exactly this shape: a struct-array element's string
// member, which the broken exporter turned into an empty placeholder folder. Nothing here had it —
// FDumperTestStat carries FName / int32 / FText — and widening THAT struct would have moved every
// offset the README quotes for `Arr_Struct`. A new type instead.
//
// ⚠ `Note` is an FText, so the same element also exercises the FText arm the fix folded in, and
// `Num` is the control: a plain scalar beside the string members must still export as a value.
// ============================================================
USTRUCT(BlueprintType)
struct FDumperTestStrRow
{
	GENERATED_BODY()

	UPROPERTY() FString Text;
	UPROPERTY() int32   Num = 0;
	UPROPERTY() FText   Note;
};

USTRUCT(BlueprintType)
struct FDumperTestInvokeProbe
{
	GENERATED_BODY()

	UPROPERTY() int32   Head = 0;
	UPROPERTY() FString Label;
	UPROPERTY() int32   Tail = 0;
};
