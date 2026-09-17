#include "DumperTest58Subsystem.h"

#include "DumperTest58Actor.h"
#include "Engine/World.h"

bool UDumperTest58Subsystem::ShouldCreateSubsystem(UObject* Outer) const
{
	if (!Super::ShouldCreateSubsystem(Outer))
	{
		return false;
	}

	// Game and PIE only. The editor opens preview and inactive worlds constantly, and
	// spawning into those would make the instance count depend on which asset was last
	// clicked.
	if (const UWorld* World = Cast<UWorld>(Outer))
	{
		return World->WorldType == EWorldType::Game
		    || World->WorldType == EWorldType::PIE;
	}
	return false;
}

void UDumperTest58Subsystem::OnWorldBeginPlay(UWorld& InWorld)
{
	Super::OnWorldBeginPlay(InWorld);

	if (SpawnedActor)
	{
		return;   // already spawned for this world
	}

	FActorSpawnParameters Params;
	// The dumper is pointed at this actor BY NAME, so a collision must not silently
	// produce DumperTest58Actor_1 and send a session looking at the wrong object.
	Params.Name = TEXT("DumperTest58Actor_0");
	Params.NameMode = FActorSpawnParameters::ESpawnActorNameMode::Requested;
	Params.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;

	SpawnedActor = InWorld.SpawnActor<ADumperTest58Actor>(
		ADumperTest58Actor::StaticClass(), FVector::ZeroVector, FRotator::ZeroRotator, Params);
}
