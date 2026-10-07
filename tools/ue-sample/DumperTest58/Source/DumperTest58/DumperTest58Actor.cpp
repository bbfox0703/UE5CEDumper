// ============================================================
// ADumperTest58Actor — seed the 5.5+ optional family with KNOWN values.
//
// The values here are acceptance criteria; tools/ue-sample/README.md carries the same table.
// ============================================================
#include "DumperTest58Actor.h"

#include "Engine/World.h"
#include "HAL/PlatformMemory.h"
#include "TimerManager.h"

#include "Windows/AllowWindowsPlatformTypes.h"
#include <Windows.h>
#include "Windows/HideWindowsPlatformTypes.h"

namespace
{
	/// [LIVEFUNCS-STEP2] A parameter's memory inside a block built for `F`, found by the parameter's name.
	template <typename T>
	T* SnapParm(UFunction* F, uint8* Parms, const TCHAR* Name)
	{
		FProperty* P = FindFProperty<FProperty>(F, FName(Name));
		return P ? P->ContainerPtrToValuePtr<T>(Parms) : nullptr;
	}

	/// L86: would the PRE-FIX gate's 16-byte read from `Field` fault? Only when it crosses into
	/// the next page and that page is not readable. The post-fix read (4 bytes, inside the
	/// object) never can.
	bool TailReadWouldFault(const void* Field)
	{
		const uintptr_t Start = reinterpret_cast<uintptr_t>(Field);
		const uintptr_t Page = FPlatformMemory::GetConstants().PageSize;
		const uintptr_t NextPage = (Start / Page + 1) * Page;
		if (Start + 16 <= NextPage)
		{
			return false;   // the whole read stays inside this page
		}
		MEMORY_BASIC_INFORMATION Mbi = {};
		if (VirtualQuery(reinterpret_cast<LPCVOID>(NextPage), &Mbi, sizeof(Mbi)) == 0)
		{
			return true;
		}
		if (Mbi.State != MEM_COMMIT)
		{
			return true;   // free or reserved-only: unreadable
		}
		return (Mbi.Protect & (PAGE_NOACCESS | PAGE_GUARD)) != 0;
	}
}

ADumperTest58Actor::ADumperTest58Actor()
{
	PrimaryActorTick.bCanEverTick = true;
}

void ADumperTest58Actor::BeginPlay()
{
	Super::BeginPlay();

	// ---- the SET halves. The *_Unset halves are seeded by being left alone. ----------
	// Distinct, recognisable values: a wrong read has to produce something visibly wrong
	// rather than a plausible zero.
	Opt_Arr_Set = TArray<int32>({ 5801, 5802, 5803 });
	Opt_Str_Set = FString(TEXT("Opt58StringPresent"));
	Opt_Name_Set = FName(TEXT("Opt58NamePresent"));

	{
		FDumperTest58OptInner Inner;
		Inner.Tag = 58001;
		Inner.Obj = this;
		Inner.Objs.Add(this);
		Opt_Struct_Set = Inner;
	}

	if (UWorld* W = GetWorld())
	{
		FActorSpawnParameters P;
		P.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
		P.Owner = this;
		Anchor = W->SpawnActor<ADumperTest58Anchor>(
			ADumperTest58Anchor::StaticClass(), GetActorLocation(), FRotator::ZeroRotator, P);
		if (Anchor)
		{
			Anchor->AnchorIndex = 58000;
			// L12 step 1's starting state.
			Opt_Obj = TObjectPtr<AActor>(Anchor);
		}
	}

	// [LIVEFUNCS-TIMELINE-2026-10-04] The trace chain. The timer calls a plain C++ method (no ProcessEvent), which
	// dispatches Outer through ProcessEvent: the trace sees Outer as a root.
	OnTraceNestLeaf.AddDynamic(this, &ADumperTest58Actor::TraceNest_Leaf);
	if (UWorld* W = GetWorld())
	{
		W->GetTimerManager().SetTimer(TraceNestTimer, this, &ADumperTest58Actor::TraceNest_Fire,
									  FMath::Max(0.05f, TraceNest_PeriodSeconds), /*bLoop=*/true);
		// [LIVEFUNCS-STEP2] The snapshot rounds, on their own timer so TraceNest's counts stay step 1's.
		W->GetTimerManager().SetTimer(SnapNestTimer, this, &ADumperTest58Actor::SnapNest_Fire,
									  FMath::Max(0.05f, SnapNest_PeriodSeconds), /*bLoop=*/true);
	}
}

void ADumperTest58Actor::TraceNest_Dispatch(FName Func, int32 Round)
{
	if (UFunction* F = FindFunction(Func))
	{
		// The parameter block of a function whose only parameter is an int32.
		struct { int32 Round; } Parms{ Round };
		ProcessEvent(F, &Parms);
	}
}

void ADumperTest58Actor::TraceNest_Fire()
{
	TraceNest_Dispatch(GET_FUNCTION_NAME_CHECKED(ADumperTest58Actor, TraceNest_Outer), ++TraceNest_Rounds);
}

void ADumperTest58Actor::TraceNest_Outer(int32 Round)
{
	// Through ProcessEvent, nested under Outer.
	TraceNest_Dispatch(GET_FUNCTION_NAME_CHECKED(ADumperTest58Actor, TraceNest_Inner), Round);
}

void ADumperTest58Actor::TraceNest_Inner(int32 Round)
{
	OnTraceNestLeaf.Broadcast(Round);   // each binding through ProcessEvent, nested under Inner
}

void ADumperTest58Actor::TraceNest_Leaf(int32 Round)
{
	++TraceNest_Leaves;
}

void ADumperTest58Actor::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	++FrameCountReflected;
	// [LIVEFUNCS-STEP2] The per-frame probe, by name through ProcessEvent: a direct C++ call never reaches the hook.
	Snap_Call(GET_FUNCTION_NAME_CHECKED(ADumperTest58Actor, SnapProbe_PerFrame),
			  [DeltaSeconds](UFunction* F, uint8* P)
			  {
				  if (float* V = SnapParm<float>(F, P, TEXT("Delta"))) *V = DeltaSeconds;
			  },
			  [](UFunction*, uint8*) {});
}

void ADumperTest58Actor::Snap_Call(FName Func, TFunctionRef<void(UFunction*, uint8*)> Fill,
								   TFunctionRef<void(UFunction*, uint8*)> Read)
{
	UFunction* F = FindFunction(Func);
	if (!F)
	{
		return;
	}
	// The engine's way: memory for ParmsSize, every parameter initialised by its own property, then destroyed by it.
	uint8* Parms = F->ParmsSize > 0
		? static_cast<uint8*>(FMemory_Alloca_Aligned(F->ParmsSize, F->GetMinAlignment()))
		: nullptr;
	if (Parms)
	{
		FMemory::Memzero(Parms, F->ParmsSize);
		for (TFieldIterator<FProperty> It(F); It && It->HasAnyPropertyFlags(CPF_Parm); ++It)
		{
			It->InitializeValue_InContainer(Parms);
		}
	}
	Fill(F, Parms);
	ProcessEvent(F, Parms);
	Read(F, Parms);
	if (Parms)
	{
		for (TFieldIterator<FProperty> It(F); It && It->HasAnyPropertyFlags(CPF_Parm); ++It)
		{
			It->DestroyValue_InContainer(Parms);
		}
	}
}

void ADumperTest58Actor::SnapProbe_Dispatch(int32 Round)
{
	const int32 R = Round;
	Snap_Call(GET_FUNCTION_NAME_CHECKED(ADumperTest58Actor, SnapProbe_Call),
			  [this, R](UFunction* F, uint8* P)
			  {
				  const FVector Vec(static_cast<double>(R), static_cast<double>(-R), 0.5);
				  if (int32* V = SnapParm<int32>(F, P, TEXT("Round"))) *V = R;
				  if (float* V = SnapParm<float>(F, P, TEXT("F"))) *V = static_cast<float>(R) + 0.5f;
				  if (double* V = SnapParm<double>(F, P, TEXT("D"))) *V = static_cast<double>(R) * 0.25;
				  if (FBoolProperty* B = CastField<FBoolProperty>(FindFProperty<FProperty>(F, FName(TEXT("bFlag")))))
				  {
					  B->SetPropertyValue_InContainer(P, (R % 2) != 0);
				  }
				  if (EDumperTest58SnapKind* V = SnapParm<EDumperTest58SnapKind>(F, P, TEXT("Kind")))
				  {
					  *V = static_cast<EDumperTest58SnapKind>(R % 3);
				  }
				  if (FName* V = SnapParm<FName>(F, P, TEXT("Tag"))) *V = FName(TEXT("SnapTag"), R % 3 + 1);
				  if (FString* V = SnapParm<FString>(F, P, TEXT("Label"))) *V = FString::Printf(TEXT("Label%d"), R);
				  if (TArray<int32>* V = SnapParm<TArray<int32>>(F, P, TEXT("Values"))) *V = { R, R + 1, R + 2 };
				  if (AActor** V = SnapParm<AActor*>(F, P, TEXT("Who"))) *V = Anchor.Get();
				  if (TSoftObjectPtr<AActor>* V = SnapParm<TSoftObjectPtr<AActor>>(F, P, TEXT("Soft")))
				  {
					  *V = TSoftObjectPtr<AActor>(Anchor.Get());
				  }
				  if (FVector* V = SnapParm<FVector>(F, P, TEXT("V"))) *V = Vec;
				  if (FDumperTest58SnapStruct* V = SnapParm<FDumperTest58SnapStruct>(F, P, TEXT("S")))
				  {
					  V->A = R;
					  V->bP = (R % 2) != 0;
					  V->bQ = (R % 2) == 0;
					  V->W = Vec;
				  }
				  if (int32* V = SnapParm<int32>(F, P, TEXT("OutTwice"))) *V = -1;
				  if (int32* V = SnapParm<int32>(F, P, TEXT("InOut"))) *V = 100;
			  },
			  [this](UFunction* F, uint8* P)
			  {
				  if (int32* V = SnapParm<int32>(F, P, TEXT("OutTwice"))) SnapProbe_LastOutTwice = *V;
				  if (int32* V = SnapParm<int32>(F, P, TEXT("InOut"))) SnapProbe_LastInOut = *V;
				  if (int32* V = SnapParm<int32>(F, P, TEXT("ReturnValue"))) SnapProbe_LastReturn = *V;
			  });
}

void ADumperTest58Actor::SnapNest_Fire()
{
	const int32 R = ++SnapNest_Rounds;
	// In scope: SnapNest_Outer is the root, SnapProbe_Call under it.
	TraceNest_Dispatch(GET_FUNCTION_NAME_CHECKED(ADumperTest58Actor, SnapNest_Outer), R);
	// Lone: the same call outside every scope.
	SnapProbe_Dispatch(R);
	Snap_Call(GET_FUNCTION_NAME_CHECKED(ADumperTest58Actor, SnapProbe_RetOnly),
			  [](UFunction*, uint8*) {}, [](UFunction*, uint8*) {});
	Snap_Call(GET_FUNCTION_NAME_CHECKED(ADumperTest58Actor, SnapProbe_ConstRefOnly),
			  [R](UFunction* F, uint8* P)
			  {
				  if (FString* V = SnapParm<FString>(F, P, TEXT("Label"))) *V = FString::Printf(TEXT("Const%d"), R);
			  },
			  [](UFunction*, uint8*) {});
}

void ADumperTest58Actor::SnapNest_Outer(int32 Round)
{
	SnapProbe_Dispatch(Round);
}

int32 ADumperTest58Actor::SnapProbe_Call(int32 Round, float F, double D, bool bFlag, EDumperTest58SnapKind Kind,
										 FName Tag, const FString& Label, const TArray<int32>& Values, AActor* Who,
										 TSoftObjectPtr<AActor> Soft, FVector V, FDumperTest58SnapStruct S,
										 int32& OutTwice, int32& InOut)
{
	++SnapProbe_Calls;
	OutTwice = 2 * Round;
	InOut += Round;
	return 3 * Round;
}

int32 ADumperTest58Actor::SnapProbe_RetOnly()
{
	return 7 * SnapNest_Rounds;
}

void ADumperTest58Actor::SnapProbe_ConstRefOnly(const FString& Label)
{
}

void ADumperTest58Actor::SnapProbe_PerFrame(float Delta)
{
	++SnapProbe_PerFrameCalls;
}

void ADumperTest58Actor::SnapLate_Begin(int32 Value)
{
	TraceNest_Dispatch(GET_FUNCTION_NAME_CHECKED(ADumperTest58Actor, SnapLate_Call), Value);
}

void ADumperTest58Actor::SnapLate_Call(int32 Value)
{
	++SnapLate_Calls;
	SnapLate_LastValue = Value;
}

void ADumperTest58Actor::Opt_SetObject()
{
	if (Anchor)
	{
		Opt_Obj = TObjectPtr<AActor>(Anchor);
	}
}

void ADumperTest58Actor::Opt_ResetObject()
{
	// ⭐ No value bytes are written; only the trailing bIsSet flag changes.
	Opt_Obj.Reset();
}

void ADumperTest58Actor::Opt_SetObjectNull()
{
	Opt_Obj = TObjectPtr<AActor>(nullptr);
}

void ADumperTest58Actor::Opt_SetArray(int32 Count)
{
	Count = FMath::Clamp(Count, 0, 4096);
	TArray<int32> V;
	V.Reserve(Count);
	for (int32 i = 0; i < Count; ++i)
	{
		V.Add(5801 + i);
	}
	Opt_Arr_Set = MoveTemp(V);
}

void ADumperTest58Actor::Opt_ResetArray()
{
	// The intrusive case: with no trailing flag, "unset" lives inside the value itself.
	Opt_Arr_Set.Reset();
}

void ADumperTest58Actor::OptTail_Spawn(int32 MaxObjects, int32 WantEdges)
{
	MaxObjects = FMath::Clamp(MaxObjects, 1, 400000);
	WantEdges = FMath::Clamp(WantEdges, 1, 64);
	const int32 EdgesAtStart = OptTail_Edges;
	for (int32 i = 0; i < MaxObjects && OptTail_Edges - EdgesAtStart < WantEdges; ++i)
	{
		UDumperTest58OptTail* O = NewObject<UDumperTest58OptTail>(this);
		if (!O)
		{
			break;
		}
		O->Opt_Tail = FName(TEXT("Opt58TailName"));
		OptTails.Add(O);
		++OptTail_Total;
		if (OptTail_ObjectSize == 0)
		{
			OptTail_ObjectSize = static_cast<int32>(sizeof(UDumperTest58OptTail));
			OptTail_FieldOffset = static_cast<int32>(
				reinterpret_cast<const uint8*>(&O->Opt_Tail) - reinterpret_cast<const uint8*>(O));
		}
		if (TailReadWouldFault(&O->Opt_Tail))
		{
			OptTail_EdgeObjs.Add(O);
			++OptTail_Edges;
		}
	}
}
