// ============================================================
// ADumperTest58Actor — seed the 5.5+ optional family with KNOWN values.
//
// The values here are acceptance criteria; tools/ue-sample/README.md carries the same table.
// ============================================================
#include "DumperTest58Actor.h"

#include "Engine/World.h"

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
