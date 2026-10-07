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
