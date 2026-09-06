#include "DumperTestSubsystem.h"

#include "DumperTestActor.h"
#include "Engine/World.h"
#include "HAL/IConsoleManager.h"
#include "Misc/CommandLine.h"
#include "Misc/Parse.h"

namespace
{

/// Turn on the engine's idle-when-backgrounded mode, from CODE and only on request.
///
/// WHY NOT THE INI, which is where this obviously belongs. `t.IdleWhenNotForeground`
/// is registered `ECVF_Cheat` (Core/Private/HAL/ConsoleManager.cpp), and
/// ConfigUtilities.cpp refuses a cheat cvar from any ini but consolevariables.ini with
/// an `ensureMsgf(false, ...)`. In the editor that is a yellow message; **in a cook it
/// is 22 errors and `Cook failed / ExitCode=25`**. Measured 2026-08-05: putting it in
/// DefaultEngine.ini's [SystemSettings] made the package unbuildable, and it was the
/// only distinct error in a 198 KB log. The project cannot use ConsoleVariables.ini
/// either -- ConfigCacheIni.cpp reads that from FPaths::EngineDir(), so it is a shared
/// engine-wide developer file that is never packaged.
///
/// Setting it from C++ is a different path and is NOT blocked: the Shipping restriction
/// (DISABLE_CHEAT_CVARS, Build.h) hides cheat cvars from the CONSOLE -- IConsoleManager.h
/// says "hidden in the console and cannot be changed by the user" -- while
/// ProcessUserConsoleInput is the only place that refuses them.
///
/// WHY OPT-IN rather than always on. While you work in the dumper's UI the game IS the
/// background app, so an idling game thread makes every game-thread dispatch (Teleport,
/// invoke, POV) time out. Idle mode is wanted for exactly two checks -- B8's deferred
/// collision restore and Grausam's foreground lock -- and is actively harmful to the
/// rest of the sample. Hence a switch, defaulting to off.
void ApplyIdleWhenNotForeground()
{
	if (!FParse::Param(FCommandLine::Get(), TEXT("DumperTestIdle")))
	{
		UE_LOG(LogTemp, Warning,
		       TEXT("[DumperTest] idle-when-backgrounded is OFF (pass -DumperTestIdle to enable). "
		            "Game-thread dispatches keep working while you are in the dumper UI."));
		return;
	}

	IConsoleVariable* CVar = IConsoleManager::Get().FindConsoleVariable(TEXT("t.IdleWhenNotForeground"));
	if (!CVar)
	{
		UE_LOG(LogTemp, Error,
		       TEXT("[DumperTest] -DumperTestIdle requested but t.IdleWhenNotForeground was not "
		            "found -- the engine renamed or removed it; B8/Grausam cannot be staged."));
		return;
	}

	CVar->Set(TEXT("1"), ECVF_SetByCode);
	UE_LOG(LogTemp, Warning,
	       TEXT("[DumperTest] idle-when-backgrounded is ON (t.IdleWhenNotForeground=%d). "
	            "Alt-tab now STALLS the game thread -- expect game-thread commands to time out."),
	       CVar->GetInt());
}

/// Cap the frame rate. A ThirdPerson template on a modern GPU will happily render this
/// empty level at several hundred FPS and pin the card doing it -- absurd for a test
/// harness that exists to be alt-tabbed away from while you read property values.
///
/// Set from code for the same reason as the idle switch: this sample deliberately makes
/// NO ini changes (see the cook-breaking ini in README.md), and keeping that property
/// intact is worth more than saving three lines. `t.MaxFPS` is safe either way -- unlike
/// t.IdleWhenNotForeground it is registered with no flags at all (UnrealEngine.cpp), so
/// it is ECVF_Default and an ini would have been legal. Verified, not assumed.
///
/// Override with -DumperTestMaxFPS=N; 0 or less uncaps it.
void ApplyMaxFPS()
{
	float MaxFPS = 60.f;
	FParse::Value(FCommandLine::Get(), TEXT("DumperTestMaxFPS="), MaxFPS);

	IConsoleVariable* CVar = IConsoleManager::Get().FindConsoleVariable(TEXT("t.MaxFPS"));
	if (!CVar)
	{
		UE_LOG(LogTemp, Warning, TEXT("[DumperTest] t.MaxFPS not found \u2014 frame rate left uncapped."));
		return;
	}

	CVar->Set(*FString::SanitizeFloat(MaxFPS), ECVF_SetByCode);
	UE_LOG(LogTemp, Warning, TEXT("[DumperTest] frame rate capped at %.0f FPS "
	                              "(-DumperTestMaxFPS=0 to uncap)."), MaxFPS);
}

} // namespace

bool UDumperTestSubsystem::ShouldCreateSubsystem(UObject* Outer) const
{
	if (!Super::ShouldCreateSubsystem(Outer))
	{
		return false;
	}

	// Game and PIE only. The editor opens preview/inactive worlds constantly
	// (thumbnail rendering, asset previews, the blueprint viewport) and spawning
	// into those would litter the editor with actors that have nothing to do
	// with the test — and would make the Instances count depend on which asset
	// you last clicked.
	if (const UWorld* World = Cast<UWorld>(Outer))
	{
		return World->WorldType == EWorldType::Game
		    || World->WorldType == EWorldType::PIE;
	}
	return false;
}

// ============================================================
// AD18 step 4 — make this exe a genuine dinput8.dll importer.
//
// Reading the import table of all 16 installed UE shipping exes found NOT ONE importer of
// dinput8.dll, so the `dinput8` proxy flavour has never been exercised against a real UE host.
// The fixture has to be the importer.
//
// ⚠ Linking dinput8.lib is NOT sufficient. An import with no referencing call is dropped by the
// optimizer, so this makes one real call. The result is deliberately ignored -- whether
// DirectInput initialises is irrelevant; the IMPORT is the artefact under test.
// ⚠ IID_IDirectInput8W comes from dxguid.lib, not dinput8.lib. See DumperTest.Build.cs.
//
// EXPECTED once the proxy is deployed: init-0.log carries
//     Loaded real dinput8.dll: C:\WINDOWS\system32\dinput8.dll
// ⛔ NOT `lazily forwarded N/N exports` -- only the dxgi and winmm flavours print that; dinput8
// takes the version-flavour shape.
// ============================================================
#define DIRECTINPUT_VERSION 0x0800
#include "Windows/AllowWindowsPlatformTypes.h"
#include <dinput.h>

void UDumperTestSubsystem::Initialize(FSubsystemCollectionBase& Collection)
{
	Super::Initialize(Collection);

	IDirectInput8W* DI = nullptr;
	const HRESULT Hr = DirectInput8Create(GetModuleHandleW(nullptr), DIRECTINPUT_VERSION,
	                                      IID_IDirectInput8W, reinterpret_cast<void**>(&DI), nullptr);
	if (SUCCEEDED(Hr) && DI)
	{
		DI->Release();
	}
	UE_LOG(LogTemp, Warning,
	       TEXT("[DumperTest] DirectInput8Create hr=0x%08X (the IMPORT is the point, not the result)"),
	       static_cast<uint32>(Hr));
}

#include "Windows/HideWindowsPlatformTypes.h"

void UDumperTestSubsystem::OnWorldBeginPlay(UWorld& InWorld)
{
	Super::OnWorldBeginPlay(InWorld);

	if (SpawnedActor)
	{
		return;   // already spawned for this world
	}

	ApplyMaxFPS();
	ApplyIdleWhenNotForeground();

	// ⭐ -DumperTestStarveVM: reserve the ±2 GB window MinHook needs for a trampoline, so
	// MH_CreateHook fails with MH_ERROR_MEMORY_ALLOC. Four shipped behaviours depend on that
	// failure and NOT ONE has ever been observed, because it only happens intermittently in
	// the wild.
	//
	// ⛔ IT IS A SWITCH, NOT A UFUNCTION, and that is forced rather than chosen: an invoke is
	// drained from INSIDE the already-installed detour, so by the time any UFUNCTION of ours
	// could run, the hook it is meant to starve has already succeeded. It has to happen before
	// the DLL is injected.
	//
	// ⚠ The recovery half is Hook_ReleaseTrampolineVM, and it must be called within ~40 s
	// (8 attempts x 5 s cooldown) or the retry ladder is spent and `hook RECOVERED on attempt N`
	// can never appear.
	if (FParse::Param(FCommandLine::Get(), TEXT("DumperTestStarveVM")))
	{
		bStarveVmRequested = true;
	}

	FActorSpawnParameters Params;
	Params.Name = TEXT("DumperTestActor_0");
	// The dumper is often pointed at this actor by NAME, so a collision must not
	// silently produce "DumperTestActor_1" and send someone looking at the wrong
	// object. Requesting the name and accepting a rename only if it is genuinely
	// taken keeps the common case stable.
	Params.NameMode = FActorSpawnParameters::ESpawnActorNameMode::Requested;
	Params.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;

	SpawnedActor = InWorld.SpawnActor<ADumperTestActor>(
		ADumperTestActor::StaticClass(), FVector::ZeroVector, FRotator::ZeroRotator, Params);

	if (bStarveVmRequested && SpawnedActor)
	{
		SpawnedActor->ReserveTrampolineVm();
	}

	UE_LOG(LogTemp, Warning, TEXT("[DumperTest] subsystem spawned actor=%p in world '%s'"),
	       SpawnedActor.Get(), *InWorld.GetName());
}
