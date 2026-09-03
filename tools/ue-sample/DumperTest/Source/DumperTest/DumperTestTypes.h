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
