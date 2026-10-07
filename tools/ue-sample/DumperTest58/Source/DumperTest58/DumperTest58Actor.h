// ============================================================
// ADumperTest58Actor — the 5.8 half of the property zoo, and ONLY the parts that
// need an engine newer than 5.4.
//
// WHY THIS EXISTS, and why it is not a copy of DumperTestActor. The 5.4 fixture
// (tools/ue-sample/DumperTest/) carries the whole zoo, and duplicating it here would
// create two copies of every acceptance value to drift apart. What 5.4 CANNOT host is
// a small, specific list, measured rather than assumed:
//
//   * A CONTAINER optional. UE 5.4's UHT refuses one outright — `TOptional<TArray<int32>>`
//     fails with "The type 'TArray<int32>' can not be used as a value in a TOptional"
//     (measured 2026-09-16). From 5.5 it is legal AND intrusive, i.e. it carries no
//     trailing bIsSet flag, which is the state `[A2-TOPTIONAL-INTRUSIVE]` (live check L12
//     step 2) is about: a walker that reads bIsSet at field+innerSize reads the NEXT
//     property's first byte instead.
//   * INTRUSIVE string / name optionals. On 5.3-5.4 these are non-intrusive; from 5.5 they
//     use a sentinel inside the value. Same row, step 3, and the hosts for
//     `[A2-TOPTIONAL-VALUESCAN]` and `[A2-SENTINEL-OVERREAD]`, both of which need 5.5+.
//   * A STRUCT optional whose inner holds a UObject* and a TArray<UObject*>
//     (`[A2-TOPTIONAL-STRUCT-DESCENT]`).
//
// ⭐ The object optional is here too, even though 5.4 can host it: the row asks for BOTH
// engines, and the states that discriminate (Reset(), which writes no value bytes, and
// set-to-null, which is "set") must be reachable at runtime on each.
//
// ⚠ NAMED DIFFERENTLY FROM THE 5.4 ACTOR ON PURPOSE. A session points the dumper at a class
// by name, and two classes called DumperTestActor on two engines is how a run ends up
// reporting the wrong fixture's values.
// ============================================================
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "TimerManager.h"
#include "DumperTest58Actor.generated.h"

/// [LIVEFUNCS-TIMELINE-2026-10-04] The trace chain's last link: a dynamic delegate's broadcast reaches each binding
/// through ProcessEvent, which is one of the ways a game nests calls the trace has to show.
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FDumperTest58TraceLeaf, int32, Round);

/// [LIVEFUNCS-STEP2] The snapshot probe's enum: a uint8 enum class with a MAX, the shape most game enums take.
UENUM()
enum class EDumperTest58SnapKind : uint8
{
	Alpha,
	Beta,
	Gamma,
	MAX UMETA(Hidden)
};

/// [LIVEFUNCS-STEP2] The snapshot probe's struct parameter: an int, two packed bools sharing one byte and a vector, so
/// a decoder has to follow a sub-layout, a bit mask and a struct inside a struct.
USTRUCT()
struct FDumperTest58SnapStruct
{
	GENERATED_BODY()

	UPROPERTY() int32 A = 0;
	UPROPERTY() uint8 bP : 1;
	UPROPERTY() uint8 bQ : 1;
	UPROPERTY() FVector W = FVector::ZeroVector;

	FDumperTest58SnapStruct() : bP(0), bQ(0) {}
};

/// The struct optional's inner. Deliberately holds a bare object pointer AND a container of
/// them: `[A2-TOPTIONAL-STRUCT-DESCENT]`'s reset case is a struct optional whose descent
/// reports NEITHER, so the inner has to have something to descend into.
USTRUCT(BlueprintType)
struct FDumperTest58OptInner
{
	GENERATED_BODY()

	UPROPERTY() TObjectPtr<UObject> Obj = nullptr;
	UPROPERTY() TArray<TObjectPtr<UObject>> Objs;
	UPROPERTY() int32 Tag = 0;
};

/// The object optional's target. A dedicated actor, not the fixture itself: Find Refs
/// (L12 step 4) has to distinguish "the optional references this actor" from "the
/// subsystem holds the fixture", and pointing the optional at itself confuses the two.
UCLASS()
class DUMPERTEST58_API ADumperTest58Anchor : public AActor
{
	GENERATED_BODY()

public:
	/// 58000 + index, out of every value range the 5.4 fixture uses.
	UPROPERTY() int32 AnchorIndex = 0;
};

/// ⭐ L86's host (`[A2-SENTINEL-OVERREAD]`): an intrusive `TOptional<FName>` as the object's
/// LAST eight bytes.
///
/// The defect: the intrusive-optional gate read 16 bytes from a field that is 8, so on an
/// object whose optional is the last field the read ran PAST THE OBJECT -- out of the
/// prefetched body, into `ReadBytesSafe` -- and faulted when the next page was unreadable.
/// A SET optional was then not a hit on exactly those instances. `Opt_Name_Set` on the actor
/// cannot show it: it is mid-object, and there is one actor.
///
/// ⚠ THE SIZE IS THE POINT. UObject's own fields are 40 bytes on this engine, the two pads make
/// 56, and the optional ends the object at 64 -- a size that divides the allocator's 64 KB
/// blocks, so the LAST slot of every block ends exactly on the block edge. Whether the page
/// after it is unreadable is the OS's business, so `ADumperTest58Actor::OptTail_Spawn`
/// MEASURES it for every instance instead of assuming, and reports the numbers.
UCLASS()
class DUMPERTEST58_API UDumperTest58OptTail : public UObject
{
	GENERATED_BODY()

public:
	/// Layout filler: exists only to put the optional at the end of a 64-byte object.
	UPROPERTY() int64 Pad_A = 0;
	UPROPERTY() int64 Pad_B = 0;

	/// ⛔ MUST STAY THE LAST FIELD. Set on every instance by OptTail_Spawn.
	UPROPERTY() TOptional<FName> Opt_Tail;
};

UCLASS()
class DUMPERTEST58_API ADumperTest58Actor : public AActor
{
	GENERATED_BODY()

public:
	ADumperTest58Actor();

	virtual void BeginPlay() override;
	virtual void Tick(float DeltaSeconds) override;

	// ---- the 5.5+ optional family -------------------------------------------------
	// ⚠ Every *_Unset field is seeded by NOT being touched. Assigning it "empty" would
	// destroy the only thing it is for.

	/// L12 step 2. INTRUSIVE from 5.5: no trailing flag, so a walker that reads
	/// `field + innerSize` reads whatever property follows.
	UPROPERTY() TOptional<TArray<int32>> Opt_Arr_Set;
	UPROPERTY() TOptional<TArray<int32>> Opt_Arr_Unset;

	/// ⭐ The NEIGHBOUR that makes step 2 falsifiable. A wrong walker reads this field's
	/// first byte as the optional's bIsSet, so it is seeded NON-ZERO (0x7F): with a zero
	/// neighbour, a right and a wrong read of an unset optional agree.
	UPROPERTY() int32 Opt_Arr_Neighbour = 0x7F;

	/// L12 step 3 on 5.8, and the host for the intrusive-sentinel rows.
	UPROPERTY() TOptional<FString> Opt_Str_Set;
	UPROPERTY() TOptional<FString> Opt_Str_Unset;

	/// `[A2-TOPTIONAL-VALUESCAN]` / `[A2-SENTINEL-OVERREAD]`: an intrusive optional whose
	/// value is 8 bytes (12 under case-preserving names), not 16.
	UPROPERTY() TOptional<FName> Opt_Name_Set;
	UPROPERTY() TOptional<FName> Opt_Name_Unset;

	/// `[A2-TOPTIONAL-STRUCT-DESCENT]`.
	UPROPERTY() TOptional<FDumperTest58OptInner> Opt_Struct_Set;
	UPROPERTY() TOptional<FDumperTest58OptInner> Opt_Struct_Unset;

	/// L12 steps 1 and 4. Non-intrusive on every version (an object optional is intrusive
	/// only under CPF_NonNullable), seeded to the anchor below in BeginPlay.
	UPROPERTY() TOptional<TObjectPtr<AActor>> Opt_Obj;

	/// GC root and Find Refs target for Opt_Obj.
	UPROPERTY() TObjectPtr<ADumperTest58Anchor> Anchor;

	/// Liveness, the same role FrameCountReflected plays on 5.4: a frozen count separates
	/// "the fixture never spawned" from "the engine is not ticking".
	UPROPERTY() int32 FrameCountReflected = 0;

	// ---- mutators, invoked through the dumper's invoke_function --------------------

	/// Set the object optional to `Anchor` — L12 step 1's starting state.
	UFUNCTION(BlueprintCallable, Category = "DumperTest58|Opt")
	void Opt_SetObject();

	/// ⭐ Reset(). Writes NO value bytes, so a walker that derives "set" from the value
	/// still publishes the old pointer. That is the defect the state exists to expose.
	UFUNCTION(BlueprintCallable, Category = "DumperTest58|Opt")
	void Opt_ResetObject();

	/// SET, and null — which a value-derived reader calls "(unset)".
	UFUNCTION(BlueprintCallable, Category = "DumperTest58|Opt")
	void Opt_SetObjectNull();

	/// The container optional's two states, so step 2 does not depend on construction order.
	UFUNCTION(BlueprintCallable, Category = "DumperTest58|Opt")
	void Opt_SetArray(int32 Count = 3);

	UFUNCTION(BlueprintCallable, Category = "DumperTest58|Opt")
	void Opt_ResetArray();

	// ---- L86: the page-edge optional ---------------------------------------------------------

	/// Create `UDumperTest58OptTail` objects (Opt_Tail = `Opt58TailName`) until `WantEdges` of them
	/// END at an unreadable page -- the pre-fix 16-byte read from `Opt_Tail` would cross into a page
	/// that is not committed, or is no-access / guard -- or until `MaxObjects` exist. Additive:
	/// call again for more. On demand, never from BeginPlay: thousands of objects would slow every
	/// other 5.8 row's scans.
	UFUNCTION(BlueprintCallable, Category = "DumperTest58|OptTail")
	void OptTail_Spawn(int32 MaxObjects = 200000, int32 WantEdges = 8);

	/// Every spawned instance; this array is their GC root.
	UPROPERTY() TArray<TObjectPtr<UDumperTest58OptTail>> OptTails;

	/// The instances that were at an unreadable page edge WHEN SPAWNED. ⚠ Only a hint for the
	/// rig: a later allocation can commit the page after one, so the rig re-measures right
	/// before it scans.
	UPROPERTY() TArray<TObjectPtr<UDumperTest58OptTail>> OptTail_EdgeObjs;

	UPROPERTY() int32 OptTail_Total = 0;
	UPROPERTY() int32 OptTail_Edges = 0;

	/// The layout as COMPILED, measured from the first instance: sizeof the class and the byte
	/// offset of Opt_Tail. The host is only valid while offset + 8 == size.
	UPROPERTY() int32 OptTail_ObjectSize = 0;
	UPROPERTY() int32 OptTail_FieldOffset = 0;

	// ---- [LIVEFUNCS-TIMELINE-2026-10-04] a nested ProcessEvent chain for the Live Funcs call trace -------------
	// The stock template's ProcessEvent traffic is flat: every call is a root, so a ticked scope has nothing under it
	// to show. Every TraceNest_PeriodSeconds this actor runs TraceNest_Outer; Outer runs TraceNest_Inner; Inner
	// broadcasts OnTraceNestLeaf, bound to TraceNest_Leaf. Outer and Inner are dispatched by name through ProcessEvent
	// (TraceNest_Dispatch), the way native code calls a reflected function, and a dynamic broadcast reaches each
	// binding through ProcessEvent, so every round is three NESTED ProcessEvent calls on the game thread, with known
	// counts: Outer, Inner and Leaf once per round, Leaf under Inner under Outer. Tick Outer in Live Funcs and the
	// trace holds exactly those rounds.
	//
	// ⚠ NOT BlueprintNativeEvents. Measured on the first package (2026-10-07): only Leaf reached the hook. UHT's thunk
	// for a BlueprintNativeEvent calls _Implementation DIRECTLY when the owning class is native (nothing overrides it in
	// a Blueprint), so a C++ call to one never touches ProcessEvent.

	UFUNCTION(BlueprintCallable, Category = "DumperTest58|Trace")
	void TraceNest_Outer(int32 Round);

	UFUNCTION(BlueprintCallable, Category = "DumperTest58|Trace")
	void TraceNest_Inner(int32 Round);

	/// Bound to OnTraceNestLeaf in BeginPlay; a UFUNCTION because a dynamic delegate binds by name.
	UFUNCTION()
	void TraceNest_Leaf(int32 Round);

	UPROPERTY(BlueprintAssignable, Category = "DumperTest58|Trace")
	FDumperTest58TraceLeaf OnTraceNestLeaf;

	/// Rounds started, and leaves reached: equal unless a link of the chain is broken.
	UPROPERTY() int32 TraceNest_Rounds = 0;
	UPROPERTY() int32 TraceNest_Leaves = 0;

	/// Seconds between rounds. Slower than any frame, so the chain is never mistaken for per-frame work and is not
	/// left out by Leave out per-frame.
	UPROPERTY() float TraceNest_PeriodSeconds = 0.5f;

	// ---- [LIVEFUNCS-STEP2] parameter snapshots -------------------------------------------------------------------
	// Every SnapNest_PeriodSeconds the timer runs SnapNest_Outer (by name through ProcessEvent), which calls
	// SnapProbe_Call INSIDE its scope; then the timer calls SnapProbe_Call again on its own, outside every scope, so a
	// chosen SnapProbe_Call is recorded both nested and LONE. Both calls get the same round, and every argument is a
	// fixed function of it, so a decoded snapshot is checked by its Round alone:
	//   F = R + 0.5, D = R * 0.25, bFlag = R odd, Kind = R % 3, Tag = SnapTag with Number R % 3 + 1 ("SnapTag_<R%3>"),
	//   Label = "Label<R>", Values = {R, R+1, R+2}, Who = Anchor, Soft = Anchor, V = (R, -R, 0.5),
	//   S = {A = R, bP = R odd, bQ = R even, W = V}; OutTwice = -1 and InOut = 100 going in.
	// The body sets OutTwice = 2R, adds R to InOut and returns 3R. The parameter block is built the ENGINE's way, from
	// the function's own properties, so the layout under test is UHT's and not a hand-written struct's.
	// ⚠ TraceNest_* is left exactly as it was: its counts are step 1's acceptance figures.

	UFUNCTION()
	void SnapNest_Outer(int32 Round);

	UFUNCTION()
	int32 SnapProbe_Call(int32 Round, float F, double D, bool bFlag, EDumperTest58SnapKind Kind, FName Tag,
						 const FString& Label, const TArray<int32>& Values, AActor* Who, TSoftObjectPtr<AActor> Soft,
						 FVector V, FDumperTest58SnapStruct S, int32& OutTwice, UPARAM(ref) int32& InOut);

	/// A return value and nothing else: pins whether FUNC_HasOutParms covers a lone return (the after copy's rule).
	UFUNCTION()
	int32 SnapProbe_RetOnly();

	/// A const reference and nothing else: pins whether a const-ref-only function takes an after copy.
	UFUNCTION()
	void SnapProbe_ConstRefOnly(const FString& Label);

	/// Every frame, by name through ProcessEvent from Tick: the per-frame probe (per_frame, the budget).
	UFUNCTION()
	void SnapProbe_PerFrame(float Delta);

	/// The rig calls it (invoke_function); it calls SnapLate_Call through ProcessEvent, so SnapLate_Call is first
	/// called after a Start and its arm's layout is read late -- by Stop's final pass when Stop follows at once.
	UFUNCTION()
	void SnapLate_Begin(int32 Value);

	UFUNCTION()
	void SnapLate_Call(int32 Value);

	/// Receipts, read back by the rig.
	UPROPERTY() int32 SnapNest_Rounds = 0;
	UPROPERTY() int32 SnapProbe_Calls = 0;
	UPROPERTY() int32 SnapProbe_LastOutTwice = 0;
	UPROPERTY() int32 SnapProbe_LastInOut = 0;
	UPROPERTY() int32 SnapProbe_LastReturn = 0;
	UPROPERTY() int32 SnapProbe_PerFrameCalls = 0;
	UPROPERTY() int32 SnapLate_Calls = 0;
	UPROPERTY() int32 SnapLate_LastValue = 0;

	/// Seconds between snapshot rounds, like TraceNest_PeriodSeconds: slower than any frame.
	UPROPERTY() float SnapNest_PeriodSeconds = 0.5f;

private:
	FTimerHandle TraceNestTimer;
	void TraceNest_Fire();
	/// Run this actor's UFUNCTION `Func(int32 Round)` through ProcessEvent.
	void TraceNest_Dispatch(FName Func, int32 Round);

	FTimerHandle SnapNestTimer;
	void SnapNest_Fire();
	/// SnapProbe_Call for round `Round`, through ProcessEvent.
	void SnapProbe_Dispatch(int32 Round);
	/// Run this actor's UFUNCTION `Func` through ProcessEvent with a parameter block built from its own properties:
	/// `Fill` sets the inputs, `Read` takes the receipts before the block is destroyed.
	void Snap_Call(FName Func, TFunctionRef<void(UFunction*, uint8*)> Fill, TFunctionRef<void(UFunction*, uint8*)> Read);
};
