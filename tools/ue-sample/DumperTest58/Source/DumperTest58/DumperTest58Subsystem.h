// ============================================================
// UDumperTest58Subsystem — spawns ADumperTest58Actor into every game world.
//
// Same reasoning as the 5.4 fixture's subsystem, restated because this project does not
// share its source: a level is a BINARY asset, so "remember to drag the actor into the map"
// is a step that will eventually be forgotten, and its failure mode is silent — the dumper
// simply finds no fixture, which reads as a dumper bug. A UWorldSubsystem needs no asset
// edit and works in PIE and in the packaged build alike.
// ============================================================
#pragma once

#include "CoreMinimal.h"
#include "Subsystems/WorldSubsystem.h"
#include "DumperTest58Subsystem.generated.h"

class ADumperTest58Actor;

UCLASS()
class DUMPERTEST58_API UDumperTest58Subsystem : public UWorldSubsystem
{
	GENERATED_BODY()

public:
	virtual bool ShouldCreateSubsystem(UObject* Outer) const override;
	virtual void OnWorldBeginPlay(UWorld& InWorld) override;

private:
	/// GC root, and a second inbound edge for Related Objects to find.
	UPROPERTY()
	TObjectPtr<ADumperTest58Actor> SpawnedActor;
};
