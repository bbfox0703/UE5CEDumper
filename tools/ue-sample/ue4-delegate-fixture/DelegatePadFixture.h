// Portable delegate-layout fixture for UE4 C++ template projects.
//
// WHY IT EXISTS. `[D4B-DELEGATEPAD-2026-09-09]` changed how every delegate read derives its
// layout, and the derivation was measured on UE 5.4, 5.7 and 5.8 only. Two things about UE4
// were therefore never exercised:
//
//   1. ⭐ THE UProperty PATH. `DynOff::bUseFProperty` is false below UE 4.25 -- the walker
//      reads `UProperty` objects out of GObjects instead of the `FField` chain, and reports
//      ElementSize from a different place. Every delegate reader now goes through
//      `DelegatePadFromElementSize`, which REFUSES a size it does not recognise and renders the
//      field unread. Nothing had ever asked what UE4 reports.
//   2. UE4 has no access detector at all. `TScriptDelegate` / `TMulticastScriptDelegate` gained
//      their `TDelegateAccessHandlerBase` base in UE 5.3; 4.15, 4.23 and 4.27 were all checked
//      and none has it. So the expected pad is **0 in every UE4 build configuration**, and a
//      UE4 run is a test of RECOGNITION, not of the pad itself. Do not read a pad-0 result here
//      as evidence about the checked-build side -- that is what DumperTest Development covers.
//
// ⚠ THE CLASS ALONE IS ENOUGH FOR `tools/verify/d4b_pad_survey.py`. That rig walks CLASS field
// tables, so a UCLASS compiled into the game module is registered in GObjects at module load and
// reports its ElementSizes without ever being spawned. An INSTANCE is only needed by
// `d4b_delegate_pad.py`, which reads the bindings -- see install.py's `--spawn-from`.
//
// ⚠ DELIBERATELY NO MODULE API MACRO and no project-specific include. This pair is copied
// verbatim into whichever `Source/<Module>/` it is installed in, and `DelegatePadFixture.h`
// resolves `DelegatePadFixture.generated.h` the same way in every project.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "DelegatePadFixture.generated.h"

/// Produces a `MulticastInlineDelegateProperty` (UE 4.23+) or a `MulticastDelegateProperty`
/// (UE 4.22 and below -- the split into Inline/Sparse landed in 4.23). Ubel handles both names.
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FDPadPingSignature, int32, Ping);

/// Produces a `DelegateProperty`, whose storage is a STANDALONE `FScriptDelegate`. ⚠ On UE 5.3+
/// that type is padded while a multicast's invocation-list ELEMENTS are not, even though this
/// repo's comments call both "FScriptDelegate". On UE4 neither is padded; this row exists so the
/// single-cast reader is exercised at all.
DECLARE_DYNAMIC_DELEGATE_OneParam(FDPadUnicastSignature, int32, Value);

UCLASS()
class ADelegatePadFixture : public AActor
{
	GENERATED_BODY()

public:
	ADelegatePadFixture();

	virtual void BeginPlay() override;

	/// The only non-array `MulticastInlineDelegateProperty` in this fixture.
	UPROPERTY() FDPadPingSignature Multicast_Inline;

	/// The only `DelegateProperty`.
	UPROPERTY() FDPadUnicastSignature Del_Unicast;

	/// ⭐ Element [1] is bound and [0] is left empty, which is what makes the element STRIDE
	/// observable: a reader using the wrong stride reads [1] from inside [0] and reports both
	/// empty. With both elements identical, a right stride and a wrong one print the same
	/// string -- exactly the gap that made D3b unfalsifiable on DumperTest until 2026-09-09.
	UPROPERTY() TArray<FDPadPingSignature> Arr_MulticastDelegates;

	/// Bound to all three above. ⚠ Deliberately EMPTY: a handler that did anything would make
	/// this fixture's presence change the behaviour of the project it is installed into.
	UFUNCTION()
	void DPad_OnPingProbe(int32 Ping);
};
