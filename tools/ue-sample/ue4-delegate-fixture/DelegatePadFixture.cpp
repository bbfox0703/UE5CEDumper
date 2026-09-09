#include "DelegatePadFixture.h"

ADelegatePadFixture::ADelegatePadFixture()
{
	// ⚠ Nothing about this actor may cost the host project anything: no tick, no replication,
	// no components. It exists to carry three UPROPERTY declarations into the game's class
	// table, and -- when spawned -- to hold three bound delegates.
	PrimaryActorTick.bCanEverTick = false;
}

void ADelegatePadFixture::BeginPlay()
{
	Super::BeginPlay();

	// Bound rather than left empty, because an UNBOUND delegate reads the same at every
	// candidate offset and would prove nothing about the layout.
	Multicast_Inline.AddDynamic(this, &ADelegatePadFixture::DPad_OnPingProbe);
	Del_Unicast.BindDynamic(this, &ADelegatePadFixture::DPad_OnPingProbe);

	// ⭐ Two elements, and ONLY [1] bound -- see the header for why an all-empty array cannot
	// measure a stride.
	Arr_MulticastDelegates.SetNum(2);
	Arr_MulticastDelegates[1].AddDynamic(this, &ADelegatePadFixture::DPad_OnPingProbe);
}

void ADelegatePadFixture::DPad_OnPingProbe(int32 /*Ping*/)
{
}
