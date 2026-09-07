// ============================================================
// DumperTestActor — populate the property zoo with KNOWN values.
//
// The numbers here are the acceptance criteria. tools/ue-sample/README.md holds
// the same table; if you change a literal, change both.
//
// Encoding: every CJK string is a \uXXXX escape. See the header for why — a
// source file re-interpreted through the system code page would corrupt exactly
// the strings B28 is about, and the test would silently be measuring MSVC.
// ============================================================

#include "DumperTestActor.h"

#include "DumperTestHUD.h"
#include "GameFramework/PlayerController.h"
#include "Engine/Engine.h"
#include "Engine/World.h"
#include "Misc/CommandLine.h"
#include "Misc/Parse.h"
#include "TimerManager.h"
#include "Engine/DataTable.h"          // runtime-built tables (V8 / MG2)
#include "GameFramework/Pawn.h"        // AD4 contention target
#include "Kismet/GameplayStatics.h"    // GetPlayerPawn
#include "Components/SceneComponent.h" // Spawn_ManyComponents (ActorComponent is abstract)
#include "UObject/UObjectGlobals.h"     // ForceGarbageCollection

#define LOCTEXT_NAMESPACE "DumperTest"

// The five CJK literals, defined once so the FText and FString mirrors are
// GUARANTEED to be the same bytes. Two copies of the same escape sequence would
// be a place for them to drift, and "the FString one is right and the FText one
// is wrong" is the entire measurement.
namespace DumperTestStrings
{
	// 統一 — U+7D71 U+4E00. 2 chars (even), one low-byte-00 char.
	static const TCHAR* Even2_OneNull = TEXT("\u7D71\u4E00");

	// 一言 — U+4E00 U+8A00. 2 chars (even), BOTH low-byte-00.
	// UTF-16LE bytes: 00 4E 00 8A — a NUL at offset 0 AND at offset 2.
	static const TCHAR* Even2_TwoNull = TEXT("\u4E00\u8A00");

	// 統一言語 — U+7D71 U+4E00 U+8A00 U+8A9E. 4 chars (even), two low-byte-00.
	static const TCHAR* Even4_TwoNull = TEXT("\u7D71\u4E00\u8A00\u8A9E");

	// 走一步 — U+8D70 U+4E00 U+6B65. 3 chars (ODD), exactly ONE low-byte-00.
	// 退 (U+9000) was here first and made the constant's NAME lie: it is also
	// low-byte-00, so the string held two. check_ue_sample_values invariant 4 caught
	// it on its first run - which is the whole argument for that gate existing.
	static const TCHAR* Odd3_OneNull = TEXT("\u8D70\u4E00\u6B65");

	// U7. 22 chars (even), exactly TWO low-byte-00 (一 U+4E00, 言 U+8A00).
	// 66 UTF-8 bytes, and the Property Search preview is cut at 50 BYTES
	// (Utf8Helpers::TruncateUtf8(s, 50), Ubel.cpp:5950). 50 mod 3 == 2, so byte 50
	// is a CONTINUATION byte and a naive resize(50) splits a 3-byte sequence.
	// Built from escape groups this file already carries, so the risk is copy-paste
	// rather than 22 fresh escapes.
	static const TCHAR* Even22_TwoNull = TEXT("\u7D71\u4E00\u8A00\u8A9E\u65E5\u672C\u8A9E\u30C6\u30B9\u30C8\u65E5\u672C\u8A9E\u30C6\u30B9\u30C8\u65E5\u672C\u8A9E\u30C6\u30B9\u30C8");

	// U11. 6 chars (even), exactly TWO low-byte-00 (言 U+8A00, 最 U+6700).
	// Shares no substring with any other literal here, in either direction, so a
	// harness that reads the WRONG FText is visible instead of silently agreeing.
	static const TCHAR* Even6_TwoNull = TEXT("\u9078\u629E\u8A00\u8A9E\u6700\u65B0");

	// 日本語テスト — U+65E5 U+672C U+8A9E U+30C6 U+30B9 U+30C8.
	// 6 chars (even), NOT ONE low byte is 00 (E5 2C 9E C6 B9 C8).
	static const TCHAR* Even6_NoNull = TEXT("\u65E5\u672C\u8A9E\u30C6\u30B9\u30C8");
}

void UDumperTestPayload::Populate()
{
	PayloadText   = FText::FromString(DumperTestStrings::Even4_TwoNull);
	PayloadString = FString(DumperTestStrings::Even4_TwoNull);
	PayloadValue  = 909090;
}

ADumperTestActor::ADumperTestActor()
{
	// Tick is ON only to drive the on-screen readout. The VALUES are still driven by
	// the 1 Hz timer -- keeping the two on separate clocks is what makes "timer dead"
	// distinguishable from "nothing is drawing at all".
	PrimaryActorTick.bCanEverTick = true;

	// --- B28: FText ---
	Text_Even2_OneNull  = FText::FromString(DumperTestStrings::Even2_OneNull);
	Text_Even2_TwoNull  = FText::FromString(DumperTestStrings::Even2_TwoNull);
	Text_Even4_TwoNull  = FText::FromString(DumperTestStrings::Even4_TwoNull);
	Text_Odd3_OneNull   = FText::FromString(DumperTestStrings::Odd3_OneNull);
	Text_Even6_NoNull   = FText::FromString(DumperTestStrings::Even6_NoNull);
	Text_Ascii          = FText::FromString(TEXT("DumperTest FText ASCII"));
	// Same glyphs as Text_Even4_TwoNull but a DIFFERENT FTextHistory. If these
	// two disagree the fault is history traversal, not decoding.
	Text_Localized      = LOCTEXT("Loc_Cjk", "\u7D71\u4E00\u8A00\u8A9E");   // 統一言語
	Text_Empty          = FText::GetEmpty();

	// --- FString controls (never had B28) ---
	Str_Even2_OneNull = DumperTestStrings::Even2_OneNull;
	Str_Even4_TwoNull = DumperTestStrings::Even4_TwoNull;
	Str_Odd3_OneNull  = DumperTestStrings::Odd3_OneNull;
	Str_Even6_NoNull  = DumperTestStrings::Even6_NoNull;

	Name_Cjk = FName(DumperTestStrings::Even2_OneNull);

	// --- containers (V1a) ---
	Set_Int.Add(1337);
	Set_Int.Add(4242);
	Set_Int.Add(8888);

	Map_NameToInt.Add(FName(TEXT("Alpha")), 111);
	Map_NameToInt.Add(FName(TEXT("Beta")),  222);
	Map_NameToInt.Add(FName(TEXT("Gamma")), 333);

	Map_IntToFloat.Add(1, 1.5f);
	Map_IntToFloat.Add(2, 2.5f);
	Map_IntToFloat.Add(3, 3.5f);

	Arr_Int = { 10, 20, 30, 40, 50 };

	// --- audit #5 cluster (1): container GEOMETRY witnesses ---
	// The scan values are deliberately distinct per container so a hit identifies
	// WHICH geometry is being exercised. See the header for the arithmetic.
	Map_I64ToI32.Add(600000000001, 6001);
	Map_I64ToI32.Add(600000000002, 6002);
	Map_I64ToI32.Add(600000000003, 6003);

	Map_StrToInt.Add(TEXT("StrAlpha"), 6101);
	Map_StrToInt.Add(TEXT("StrBeta"),  6102);
	Map_StrToInt.Add(TEXT("StrGamma"), 6103);

	// Element 0 is the one that matters: the broken build reads its value at +8
	// instead of +4, so 6201 is wrong even at index 0.
	Map_IntToVec3f.Add(1, FDumperTestVec3f{ 6201.f, 6202.f, 6203.f });
	Map_IntToVec3f.Add(2, FDumperTestVec3f{ 6211.f, 6212.f, 6213.f });
	Map_IntToVec3f.Add(3, FDumperTestVec3f{ 6221.f, 6222.f, 6223.f });

	// 200 entries (9000..9199) pushes the TBitArray past 128 bits onto the heap;
	// removing 9005 frees a LOW slot (index 5) whose bit lives in the inline words
	// the spill left frozen. A build with the stale-bits defect still lists 9005.
	for (int32 i = 0; i < 200; ++i)
	{
		Set_Big.Add(9000 + i);
	}
	Set_Big.Remove(9005);

	Set_Struct.Add(FDumperTestVec3f{ 6301.f, 6302.f, 6303.f });
	Set_Struct.Add(FDumperTestVec3f{ 6311.f, 6312.f, 6313.f });

	// ---- MG2 / V1a seeds -------------------------------------------------
	// Kept well UNDER the 128 array limit on purpose: MG2 step 1 is precisely
	// "a container whose real row count is NOT truncated", so the header count
	// and the rendered rows must agree exactly, before and after a removal.
	Set_Name.Add(FName(TEXT("Alpha")));
	Set_Name.Add(FName(TEXT("Beta")));
	Set_Name.Add(FName(TEXT("Gamma")));
	Set_Name.Add(FName(TEXT("Delta")));

	for (int32 i = 0; i < 6; ++i)
	{
		Map_Churn.Add(4000 + i, 500 + i);
	}
	Arr_Churn = { 7001, 7002, 7003, 7004 };


	// Struct-element container: an FText one level deep inside a TArray, so a
	// B28 regression that only shows up under the deep descent still has a home.
	{
		FDumperTestStat A;
		A.StatName = FName(TEXT("Attack"));
		A.Value    = 7777;
		A.Label    = FText::FromString(DumperTestStrings::Even2_OneNull);
		Arr_Struct.Add(A);

		FDumperTestStat B;
		B.StatName = FName(TEXT("Defence"));
		B.Value    = 6666;
		B.Label    = FText::FromString(DumperTestStrings::Even2_TwoNull);
		Arr_Struct.Add(B);
	}

	// --- TOptional (V1c) ---
	Opt_Int_Set   = 24680;
	Opt_Float_Set = 99.5f;
	Opt_Str_Set   = FString(TEXT("OptionalPresent"));
	// Opt_Int_Unset is LEFT ALONE on purpose. A scan for 0 must not find it.

	// --- numerics ---
	I8_Neg   = -5;      // Int8 yes / UInt8 no — the unit-tested boundary
	U8_Small = 1;
	U8_Max   = 255;
	I16      = -12345;
	U16      = 54321;
	I32      = 1234567;
	I64      = 8899001122334455LL;
	F32      = 513.36f;             // Round 513.36 / Trunc 513.3 / Ceil 513.4
	F64      = 2718.281828;

	// --- raw non-UPROPERTY holes ---
	RawInt    = 0x5A5A5A5A;   // 1515870810
	RawFloat  = 777.75f;
	RawDouble = 31415.926535;

	bFlagA     = 1;
	bFlagB     = 0;
	bFlagC     = 1;
	bPlainBool = true;

	Grade = EDumperTestGrade::Elite;

	for (int32 i = 0; i < 8; ++i)
	{
		FixedArr[i] = (i + 1) * 100;   // 100..800
	}

	Health.BaseValue    = 100.f;
	Health.CurrentValue = 100.f;

	// A7: a value the tester can see in Live Walker's Value column at OFFSET 0 of
	// BracketPayload. If the emitter's empty-base split regresses, this field is
	// the one that disappears behind a Pad_0000 row.
	EmptyBasePayload.Description = TEXT("A7EmptyBase");

	TickCount = 0;
	FrozenInt = 424242;   // never written again — the Unchanged control

	// --- ticking numerics: the prev-value targets the sample never had ---
	// Distinctive starting values so a first Exact scan is selective on its own; the
	// static F32/F64/Raw* above stay put because they are documented acceptance criteria.
	F32_Ticking = 1000.5f;
	F64_Ticking = 20000.125;

	RawInt_Ticking    = 700000;
	RawFloat_Ticking  = 300.25f;
	RawDouble_Ticking = 50000.5;

	// ---- appended-at-END fixtures (see the matching block in the header) ----

	// U7 -- the only string here that is BOTH non-ASCII AND past the 50-BYTE cut.
	Str_Even22_TwoNull = DumperTestStrings::Even22_TwoNull;

	// U11 -- FText::FromString, NOT NSLOCTEXT: that carries the same history as
	// Text_Even2_OneNull, so a failure isolates to the optional arm rather than to
	// history traversal (Text_Localized already owns that question).
	Opt_Text_Set = FText::FromString(DumperTestStrings::Even6_TwoNull);
	// Opt_Text_Unset is left alone on purpose -- the FText half of the negative criterion.

	// U3/U17 step 3. Magnitudes ~6.4e6 exceed UE4's WORLD_MAX (2,097,152), i.e. a
	// coordinate a 12-byte FVector3f could not hold as a world position -- which is
	// the whole reason LWC exists. The .1234/.2345/.3456 tails are the point: every
	// one of them CHANGES when narrowed through float32 (6403000.3456 -> 6403000.5),
	// so a silent narrow-to-float anywhere in decoder -> wire -> UI is visible. A
	// round number like 62010.5 round-trips through float32 exactly and would be blind.
	Map_IntToVecLwc.Add(1, FVector(6401000.1234, 6402000.2345, 6403000.3456));
	Map_IntToVecLwc.Add(2, FVector(6411000.1234, 6412000.2345, 6413000.3456));

	Set_VecLwc.Add(FVector(6501000.1234, 6502000.2345, 6503000.3456));
	Set_VecLwc.Add(FVector(6511000.1234, 6512000.2345, 6513000.3456));

	// Y15 step 6 -- seeded at Wide_Base; a freeze targets Wide_Target. The two share
	// their low byte, so a 1-byte write leaves the field BIT-IDENTICAL and the
	// failure cannot be confused with "the write never landed".
	WideGrade = EDumperTestWideGrade::Wide_Base;
	WideGuard = 0x7F7F7F7F;

}

void ADumperTestActor::BeginPlay()
{
	Super::BeginPlay();

	// Created at runtime rather than as a default subobject so it is a genuine
	// heap UObject in GObjects, reachable only through the pointer — which is
	// what makes it a real "Locate in GWorld" target rather than a level actor.
	Payload = NewObject<UDumperTestPayload>(this, TEXT("DumperTestPayload"));
	if (Payload)
	{
		Payload->Populate();
	}

	// MG2 step 2 — TSet<UObject*> with REAL, resolvable pointers. Built here and
	// not in the constructor because Payload is a runtime NewObject: a set full of
	// nulls would render identically whether or not object-set walking works.
	Set_Object.Empty();
	if (Payload)
	{
		Set_Object.Add(Payload);
	}
	for (int32 i = 0; i < 3; ++i)
	{
		if (UDumperTestPayload* Extra = NewObject<UDumperTestPayload>(
			    this, *FString::Printf(TEXT("SetPayload_%d"), i)))
		{
			Extra->Populate();
			Extra->PayloadValue = 8100 + i;
			Set_Object.Add(Extra);
		}
	}

	// V8 / MG2 step 2 — the tables. Runtime-built, so no cooked asset is needed
	// and the row count is a parameter rather than a content decision.
	Table_Small = BuildTable(TEXT("DumperTestTable_Small"), 8);
	Table_Big   = BuildTable(TEXT("DumperTestTable_Big"), 100);

	if (UWorld* W = GetWorld())
	{
		W->GetTimerManager().SetTimer(TickHandle, this, &ADumperTestActor::OnSecondTick, 1.0f, /*loop*/ true);
	}

	// ---- A1: the soft/lazy pointer family -------------------------------------------
	// ⚠ These are PATH references, not loads. A ctor/BeginPlay soft reference is not a cook
	// dependency, so the acceptance is that the PATH reads back -- never that the asset is
	// present. Engine paths are used so nothing project-specific has to be cooked at all.
	Soft_Mesh = TSoftObjectPtr<UStaticMesh>(FSoftObjectPath(TEXT("/Engine/BasicShapes/Cube.Cube")));
	Arr_SoftMesh.Reset();
	Arr_SoftMesh.Add(TSoftObjectPtr<UStaticMesh>(FSoftObjectPath(TEXT("/Engine/BasicShapes/Cube.Cube"))));
	Arr_SoftMesh.Add(TSoftObjectPtr<UStaticMesh>(FSoftObjectPath(TEXT("/Engine/BasicShapes/Sphere.Sphere"))));
	Arr_SoftMesh.Add(TSoftObjectPtr<UStaticMesh>(FSoftObjectPath(TEXT("/Engine/BasicShapes/Cone.Cone"))));
	// [3] stays DEFAULT on purpose: the `(none)` control. Without it, "every element shows a
	// path" is equally consistent with a reader that renders a path for anything at all.
	Arr_SoftMesh.AddDefaulted(1);

	// ---- the multi-[N] drill subject ------------------------------------------------
	// value = 7000 + block*100 + element, so Arr_TuneBlocks[2].Tunes[4] is 7204 and a
	// one-hop parse lands visibly on 7104 instead.
	Arr_TuneBlocks.Reset();
	for (int32 b = 0; b < 3; ++b)
	{
		FDumperTestTuneBlock Block;
		Block.BlockName = FName(*FString::Printf(TEXT("Tune_%d"), b));
		for (int32 e = 0; e < 5; ++e)
		{
			Block.Tunes.Add(7000 + b * 100 + e);
		}
		Arr_TuneBlocks.Add(MoveTemp(Block));
	}

	// Deliberately DIFFERENT lengths, so an FName stride question has a subject whose
	// entries cannot be confused with one another by size alone.
	Arr_Name.Reset();
	Arr_Name.Add(FName(TEXT("NameA")));
	Arr_Name.Add(FName(TEXT("NameBB")));
	Arr_Name.Add(FName(TEXT("NameCCC")));

	// ---- A1 lazy array: needs REAL actors, so it spawns three of its own ------------
	// ⚠ Three DISTINCT actors, because the failure fingerprint of the old stride is three
	// IDENTICAL garbage GUIDs -- with one element that is indistinguishable from success.
	if (UWorld* W = GetWorld())
	{
		FActorSpawnParameters P;
		P.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
		P.Owner = this;
		Arr_LazyPtr.Reset();
		for (int32 i = 0; i < 3; ++i)
		{
			if (ADumperTestHolder* H = W->SpawnActor<ADumperTestHolder>(
				    ADumperTestHolder::StaticClass(), GetActorLocation(), FRotator::ZeroRotator, P))
			{
				H->HolderIndex = 90000 + i;   // out of Spawn_Holders' range, so they never collide
				LazyAnchors.Add(H);
				Arr_LazyPtr.Add(TLazyObjectPtr<AActor>(H));
			}
		}
	}

	// Confirms the actor exists WITHOUT attaching the dumper -- but ONLY in Development/Test.
	//
	// This comment used to read "Warning level so it survives a Shipping build's default log
	// verbosity". That is FALSE, verified 2026-08-05: the Shipping branch of Build.h:328 sets
	// NO_LOGGING = !USE_LOGGING_IN_SHIPPING (0 unless the Target.cs opts in), and
	// LogMacros.h:146-158 reduces UE_LOG to Fatal-only under NO_LOGGING -- so this call
	// compiles to nothing in the package that actually gets tested. THIRD wrong assertion in
	// this file about what a Shipping build keeps, all three made by inferring a gate rather
	// than opening it. The on-screen HUD readout is the Shipping-safe check; set
	// bUseLoggingInShipping = true in the Target.cs if you want this line as well.
	UE_LOG(LogTemp, Warning,
	       TEXT("[DumperTest] ADumperTestActor ready at %p, Payload=%p. ")
	       TEXT("Find it via Instances -> 'DumperTestActor'."),
	       this, Payload.Get());
}

void ADumperTestActor::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	++FrameCount;   // the HUD reads this; drawing happens in ADumperTestHUD::DrawHUD

	// Installed from TICK, never from the 1 Hz timer. Putting it on the timer would make
	// the readout depend on the very thing it exists to measure: a dead timer would show
	// as a BLANK SCREEN, which is indistinguishable from "the sample never spawned" -- the
	// exact confusion the split-clock design was built to end. From Tick, a dead timer
	// shows as a frozen TickCount beside a climbing frames, which is the diagnosis.
	// Cheap after the first success: a cached bool, two derefs and an IsA.
	// AD4 step 4 — the contention writer. When armed, the "game" re-asserts
	// damageability every frame, so a Solide God Mode hold and this writer race
	// continuously and the badge can reach ON (contested). Costs one bool test
	// per frame when disarmed, which is the default.
	if (bContestDamage)
	{
		if (APawn* P = UGameplayStatics::GetPlayerPawn(this, 0))
		{
			P->SetCanBeDamaged(true);
			++ContestWrites;
		}
	}

	EnsureHeartbeatHud();
}

void ADumperTestActor::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (UWorld* W = GetWorld())
	{
		W->GetTimerManager().ClearTimer(TickHandle);
		W->GetTimerManager().ClearTimer(LiniePeriodicHandle);
	}
	// ⚠ UNCONDITIONAL. If -DumperTestStarveVM reserved the ±2 GB window and the run aborts
	// before Hook_ReleaseTrampolineVM is called, leaving it reserved would starve every
	// subsequent hook attempt in a process the next session assumes is clean.
	ReleaseReservedVm();
	Super::EndPlay(EndPlayReason);
}

void ADumperTestActor::OnSecondTick()
{
	++TickCount;

	// The canonical group-scan case, running once a second:
	//   CurrentValue  DECREASED   (and INCREASED on the wrap)
	//   BaseValue     UNCHANGED   <- the slot a group match needs
	// A scan for "something went down while something else held still" has a
	// guaranteed hit here, which no commercial game can promise on demand.
	Health.CurrentValue -= 1.f;
	if (Health.CurrentValue <= 1.f)
	{
		Health.CurrentValue = Health.BaseValue;
	}

	// FrozenInt and BaseValue are deliberately NOT touched.

	// Ticking float / double, one falling and one rising, so BOTH directions of the
	// prev-value predicates have a target at both widths. The fall wraps, which is what
	// makes Increased reachable on F32_Ticking too -- at ~96 s, chosen so the wrap happens
	// INSIDE a normal session. Every step is a power-of-two fraction (10.25 = 41/4,
	// 3.25 = 13/4, 0.25, 0.5), so the values stay exactly representable and what the HUD
	// prints is exactly what a scan must match -- no accumulated drift to explain away.
	F32_Ticking -= 10.25f;
	if (F32_Ticking <= 10.25f)
	{
		F32_Ticking = 1000.5f;
	}
	F64_Ticking += 0.25;

	// The RAW (non-UPROPERTY) ones move on the same clock. Reflection cannot see these
	// at all -- they exist so the opt-in Native-C scan has something that CHANGES, which
	// the static RawInt/RawFloat/RawDouble could never provide.
	RawInt_Ticking += 7;
	RawFloat_Ticking -= 3.25f;
	if (RawFloat_Ticking <= 3.25f)
	{
		RawFloat_Ticking = 300.25f;
	}
	RawDouble_Ticking += 0.5;
}

/// Put the two ticking values ON SCREEN -- through a path that survives SHIPPING.
///
/// WHY. This actor is invisible by design -- no mesh, no gameplay, it exists only to be
/// read by the dumper -- so "is the timer actually running?" was unanswerable without
/// attaching the dumper and walking the object. That question came up three times in one
/// session, because a group scan finding nothing CHANGED and a game that is not ticking
/// look identical from the outside.
///
/// This USED to call GEngine->AddOnScreenDebugMessage, which is a no-op in a Shipping
/// package (UnrealEngine.cpp:11397 wraps the whole body in
/// `#if !(UE_BUILD_SHIPPING || UE_BUILD_TEST)`). It printed in Development and not in
/// Shipping, and that difference was misread twice -- once as "Shipping strips it" and
/// once as "config" -- before anyone opened the function. The drawing now lives in
/// ADumperTestHUD, which uses AHUD::DrawText: verified ungated in the same source.
///
/// Called from Tick, never from the 1 Hz timer -- see the header for why that distinction
/// is the whole point of this readout.
///
/// -DumperTestNoHud turns it off for a clean screenshot, and is also the opt-out for the
/// ClientSetHUD side effect described in the header.
void ADumperTestActor::EnsureHeartbeatHud()
{
	// Parsed ONCE. FParse::Param scans the whole command line, and this runs every frame --
	// the early-outs below are only cheap if the expensive test is not in front of them.
	static const bool bNoHud = FParse::Param(FCommandLine::Get(), TEXT("DumperTestNoHud"));
	if (bNoHud)
	{
		return;
	}

	UWorld* W = GetWorld();
	if (!W)
	{
		return;
	}

	// Not up yet is NORMAL, not an error: this actor is spawned by a UWorldSubsystem and
	// routinely beats the PlayerController into existence. Tick retries next frame.
	APlayerController* PC = W->GetFirstPlayerController();
	if (!PC)
	{
		return;
	}

	if (PC->MyHUD && PC->MyHUD->IsA<ADumperTestHUD>())
	{
		return;   // already ours -- do NOT respawn it every second
	}

	PC->ClientSetHUD(ADumperTestHUD::StaticClass());
}

#undef LOCTEXT_NAMESPACE

// ============================================================
// MG2 / V8 / V1a / AD4 — runtime fixtures and their mutators.
//
// All reachable through the dumper's `invoke_function`, so every one of these
// rows becomes scriptable instead of "wait until a commercial game happens to
// contain the right UPROPERTY". See the header for why this is deliberately NOT
// a UCheatManager (it is compiled out of Shipping).
// ============================================================

UDataTable* ADumperTestActor::BuildTable(const TCHAR* Name, int32 Rows)
{
	// UNIQUE name per build -- see TableSerial in the header for why reusing an
	// existing name is not safe here.
	const FString Unique = FString::Printf(TEXT("%s_%d"), Name, ++TableSerial);
	UDataTable* Table = NewObject<UDataTable>(this, *Unique);
	if (!Table)
	{
		return nullptr;
	}

	// RowStruct must be set BEFORE AddRow: AddRow copies RowData through the
	// struct's layout, so a null RowStruct yields a table that looks populated
	// and reads back as garbage.
	Table->RowStruct = FDumperTestTableRow::StaticStruct();

	for (int32 i = 0; i < Rows; ++i)
	{
		FDumperTestTableRow Row;
		Row.Index = i;
		Row.Label = FName(*FString::Printf(TEXT("Row_%03d"), i));
		Row.Value = 100.f + static_cast<float>(i);
		// 走一步 + the index, so every row is distinguishable AND the
		// B28 trigger is present inside a DataTable row.
		// The literal is NOT inlined: DumperTestStrings::Odd3_OneNull already holds
		// exactly this string, and check_ue_sample_values follows ONE hop from a field
		// to a NAMED constant. An inlined escape is invisible to that hop, so the
		// README row for `Caption` could not be attributed to any source line.
		Row.Caption = FText::FromString(FString::Printf(TEXT("%s %d"), DumperTestStrings::Odd3_OneNull, i));

		Table->AddRow(Row.Label, Row);
	}

	return Table;
}

void ADumperTestActor::MG2_RemoveOneMapEntry()
{
	// Remove the LOWEST key rather than the last inserted: a walker that reads a
	// stale inline copy tends to still show early entries, so dropping a low one
	// is the harder case to fake.
	TArray<int32> Keys;
	Map_Churn.GetKeys(Keys);
	if (Keys.Num() == 0)
	{
		return;
	}
	Keys.Sort();
	Map_Churn.Remove(Keys[0]);
}

void ADumperTestActor::MG2_RemoveOneSetEntry()
{
	// COPY the name out before removing. `const FName&` from a range-for aliases
	// the set's own element storage, and TSet::Remove destroys that element while
	// still holding the reference it was handed -- self-aliasing removal that
	// happens to work today is not a fixture anyone should trust.
	FName Victim = NAME_None;
	for (const FName& N : Set_Name)
	{
		Victim = N;
		break;   // exactly one, so the caller can count
	}
	if (!Victim.IsNone())
	{
		Set_Name.Remove(Victim);
	}
}

void ADumperTestActor::V1a_GrowContainers(int32 Count)
{
	if (Count <= 0)
	{
		return;
	}

	// Append rather than Reserve+append: the point is to blow past the existing
	// slack so the allocation MOVES, which is the event a Next Scan candidate has
	// to survive by being discarded rather than by reading the old address.
	const int32 Base = Arr_Churn.Num();
	for (int32 i = 0; i < Count; ++i)
	{
		Arr_Churn.Add(7100 + Base + i);
		Map_Churn.Add(4100 + Base + i, 600 + i);
	}
}

void ADumperTestActor::V1a_ShrinkContainers()
{
	// Empty(0) releases the allocation instead of keeping slack, so the old
	// element addresses become genuinely unreadable rather than merely stale.
	Arr_Churn.Empty(0);
	Map_Churn.Empty(0);
}

void ADumperTestActor::V8_RebuildBigTable(int32 Rows)
{
	Rows = FMath::Clamp(Rows, 1, 5000);
	Table_Big = BuildTable(TEXT("DumperTestTable_Big"), Rows);
}

void ADumperTestActor::V8_RemoveOneTableRow()
{
	if (!Table_Big)
	{
		return;
	}
	TArray<FName> Names = Table_Big->GetRowNames();
	if (Names.Num() > 0)
	{
		Table_Big->RemoveRow(Names[0]);
	}
}

void ADumperTestActor::AD4_SetDamageContention(bool bEnabled)
{
	bContestDamage = bEnabled;
	if (!bEnabled)
	{
		ContestWrites = 0;   // so the next armed window counts from zero
	}
}

int32 ADumperTestActor::AD4_GetContestWrites() const
{
	return ContestWrites;
}


// ============================================================================
// The spawner. Objects that appear and disappear ON DEMAND.
//
// Read the class comments in the header first: ADumperTestHolder /
// ADumperTestDerivedHolder / ADumperTestHolderDecoy are a DISCRIMINATING SET, and
// what makes them worth having is that Decoy's NAME contains the base's while its
// TYPE does not derive from it.
// ============================================================================

void ADumperTestActor::Spawn_Holders(int32 Count, bool bDerived)
{
	UWorld* W = GetWorld();
	if (!W || Count <= 0)
	{
		return;
	}

	FActorSpawnParameters P;
	// ALWAYS spawn. The default handling silently refuses a spawn that would
	// overlap, so a request for 300 would quietly deliver some smaller number and
	// every count downstream would be measuring the collision solver instead of the
	// feature under test.
	P.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
	P.Owner = this;

	UClass* Cls = bDerived ? ADumperTestDerivedHolder::StaticClass()
	                       : ADumperTestHolder::StaticClass();

	const FVector  Loc = GetActorLocation();
	const FRotator Rot = FRotator::ZeroRotator;
	const int32    Base = SpawnedHolders.Num();

	for (int32 i = 0; i < Count; ++i)
	{
		ADumperTestHolder* H = W->SpawnActor<ADumperTestHolder>(Cls, Loc, Rot, P);
		if (!H)
		{
			continue;
		}
		// DISTINCT per instance, and derived from the GLOBAL index rather than the
		// loop index, so a second call does not restart the sequence and hand two
		// live instances the same value.
		H->HolderIndex = Base + i;
		H->HolderValue = 1000.f + static_cast<float>(Base + i);
		H->bHolderFlag = ((Base + i) % 2) == 0;
		// ⭐ DELIBERATELY BUCKETED, not distinct. HolderValue above is unique per instance,
		// which makes it useless as a pivot KEY -- 300 instances would give 300 groups of 1.
		// Five buckets over hundreds of instances is the shape Class Pivot grouping and
		// Suggest Targets exist to surface, and it is the shape a real game's HP/team/state
		// field actually has.
		H->HolderHealth.BaseValue    = 100.f;
		H->HolderHealth.CurrentValue = 100.f - static_cast<float>((Base + i) % 5) * 10.f;
		SpawnedHolders.Add(H);
	}
	++SpawnGeneration;
}

void ADumperTestActor::Spawn_Decoys(int32 Count)
{
	UWorld* W = GetWorld();
	if (!W || Count <= 0)
	{
		return;
	}

	FActorSpawnParameters P;
	P.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
	P.Owner = this;

	const FVector Loc = GetActorLocation();
	for (int32 i = 0; i < Count; ++i)
	{
		ADumperTestHolderDecoy* D =
			W->SpawnActor<ADumperTestHolderDecoy>(ADumperTestHolderDecoy::StaticClass(),
			                                      Loc, FRotator::ZeroRotator, P);
		if (D)
		{
			// A value a "held" field would visibly overwrite, so the decoy staying
			// untouched is observable rather than merely asserted.
			D->HolderValue = -1.f;
			SpawnedHolders.Add(D);
		}
	}
	++SpawnGeneration;
}

void ADumperTestActor::Spawn_DestroyHolders()
{
	for (TObjectPtr<AActor>& A : SpawnedHolders)
	{
		if (AActor* Raw = A.Get())
		{
			Raw->Destroy();
		}
	}
	SpawnedHolders.Empty();

	// FORCE the collection. Destroy() only marks the actor pending-kill and removes
	// it from the level; the UObject keeps its GObjects slot until a GC runs. Every
	// row that cares here cares about the SLOT being freed and reused, so leaving
	// that to the engine's own schedule would make the test a race.
	if (GEngine)
	{
		GEngine->ForceGarbageCollection(true);
	}
	++SpawnGeneration;
}

int32 ADumperTestActor::Spawn_CountHolders() const
{
	int32 N = 0;
	for (const TObjectPtr<AActor>& A : SpawnedHolders)
	{
		if (IsValid(A.Get()))
		{
			++N;
		}
	}
	return N;
}

int32 ADumperTestActor::Spawn_Generation() const
{
	return SpawnGeneration;
}

void ADumperTestActor::Spawn_LateInstance()
{
	UDumperTestLateSpawn* L = NewObject<UDumperTestLateSpawn>(this);
	if (!L)
	{
		return;
	}
	L->LateValue = 5000 + LateSpawns.Num();
	LateSpawns.Add(L);
	++SpawnGeneration;
}

void ADumperTestActor::Spawn_RecycleChurn(int32 Rounds)
{
	Rounds = FMath::Clamp(Rounds, 1, 512);

	for (int32 r = 0; r < Rounds; ++r)
	{
		// ALTERNATE the class. Refilling a freed slot with the SAME class does not
		// test an identity guard at all -- the stale pointer still resolves to the
		// same class and reads plausibly. A foreign class in the recycled slot is
		// the whole defect.
		UObject* O = ((r % 2) == 0)
			? static_cast<UObject*>(NewObject<UDumperTestPayload>(this))
			: static_cast<UObject*>(NewObject<UDumperTestPayloadB>(this));
		if (!O)
		{
			continue;
		}
		LastRecycledAddr = static_cast<uint64>(reinterpret_cast<UPTRINT>(O));

		// Hold only the newest, so the previous one becomes collectable and its slot
		// is a candidate for the next allocation.
		LateSpawns.Empty();
		LateSpawns.Add(O);

		if (GEngine)
		{
			GEngine->ForceGarbageCollection(true);
		}
	}
	++SpawnGeneration;
}

int64 ADumperTestActor::Spawn_LastRecycledAddr() const
{
	return static_cast<int64>(LastRecycledAddr);
}

void ADumperTestActor::Spawn_ManyComponents(int32 Count)
{
	Count = FMath::Clamp(Count, 1, 20000);
	for (int32 i = 0; i < Count; ++i)
	{
		// USceneComponent, NOT UActorComponent. UActorComponent is declared
		// `UCLASS(..., abstract, ...)` (ActorComponent.h:131), so NewObject on it
		// fails at runtime -- the pool would never grow and the row would read as
		// "the cap never fires" rather than as a broken fixture. USceneComponent is
		// concrete and still derives from UActorComponent, which is what a
		// derived-pool count is counting.
		USceneComponent* C = NewObject<USceneComponent>(this);
		if (!C)
		{
			continue;
		}
		// Register, or it is a UObject that merely happens to be a component type
		// and never joins the actor's component set.
		C->RegisterComponent();
	}
	++SpawnGeneration;
}

// ============================================================
// A9 — build the three-level container.
//
// ⚠ Cost is Outer*Mid*Inner floats. (300,300,300) is 27,000,000 floats ~= 108 MB and takes a
// few seconds; that is the POINT (the unclamped visit count must dwarf the 50,000 budget), but
// it is why this is opt-in rather than seeded in BeginPlay.
// ⚠ The NEGATIVE control is (30,30,30) = 27,930 visits, deliberately UNDER budget.
// ============================================================
void ADumperTestActor::A9_BuildDeepContainers(int32 Outer, int32 Mid, int32 Inner)
{
	Outer = FMath::Clamp(Outer, 0, 400);
	Mid   = FMath::Clamp(Mid,   0, 400);
	Inner = FMath::Clamp(Inner, 0, 400);

	Deep_Buckets.Reset();
	Deep_Buckets.Reserve(Outer);
	for (int32 o = 0; o < Outer; ++o)
	{
		FDumperTestDeepMid M;
		M.Subs.Reserve(Mid);
		for (int32 m = 0; m < Mid; ++m)
		{
			FDumperTestDeepLeaf L;
			L.Leaves.Reserve(Inner);
			for (int32 i = 0; i < Inner; ++i)
			{
				// Unique per path, so a truncated walk is visible in the VALUES and not only
				// in a count -- a count alone cannot say WHERE it stopped.
				L.Leaves.Add(static_cast<float>(o * 1000000 + m * 1000 + i));
			}
			M.Subs.Add(MoveTemp(L));
		}
		Deep_Buckets.Add(MoveTemp(M));
	}

	UE_LOG(LogTemp, Warning, TEXT("[DumperTest] A9 deep containers: %d x %d x %d"),
	       Outer, Mid, Inner);
}

// ============================================================
// Linie — ProcessEvent call counting.
// ============================================================
void ADumperTestActor::Linie_Marker()
{
	// Deliberately empty. Its only job is to BE dispatched, so anything it did would be
	// another variable in whatever the profiler is measuring.
}

void ADumperTestActor::Linie_Burst(int32 Times)
{
	Times = FMath::Clamp(Times, 0, 100000);

	// ⭐ REFLECTED DISPATCH, NOT A DIRECT CALL. A direct C++ call to Linie_Marker() never
	// enters ProcessEvent, so Linie would count zero and the row would read as a defect in
	// the profiler rather than a mistake in the driver.
	UFunction* Fn = FindFunctionChecked(TEXT("Linie_Marker"));
	for (int32 i = 0; i < Times; ++i)
	{
		ProcessEvent(Fn, nullptr);
	}

	// ⚠ EXPECT Linie_Burst's OWN count to stay 0 while Linie_Marker reads exactly `Times`.
	// A queued invoke is drained from inside the installed MinHook trampoline, so this
	// function does not re-enter the detour. A non-zero count here is the interesting result.
}

void ADumperTestActor::Linie_StartPeriodic(float PeriodSeconds)
{
	// ⚠ Floored at 1/30 s. A UE timer fires at most once per frame and this harness runs at
	// t.MaxFPS 15, so asking for 1 ms does not stress anything -- it just makes the measured
	// period the frame time and the cadence row then measures the FPS cap.
	PeriodSeconds = FMath::Max(PeriodSeconds, 1.0f / 30.0f);
	if (UWorld* W = GetWorld())
	{
		W->GetTimerManager().ClearTimer(LiniePeriodicHandle);
		W->GetTimerManager().SetTimer(LiniePeriodicHandle, this,
		                              &ADumperTestActor::Linie_Marker, PeriodSeconds, true);
	}
}

void ADumperTestActor::Linie_StopPeriodic()
{
	if (UWorld* W = GetWorld())
	{
		W->GetTimerManager().ClearTimer(LiniePeriodicHandle);
	}
}

// ============================================================
// MinHook trampoline-VM starvation — release half.
// ============================================================
int32 ADumperTestActor::Hook_ReleaseTrampolineVM()
{
	const int32 Freed = ReleaseReservedVm();
	UE_LOG(LogTemp, Warning, TEXT("[DumperTest] released %d reserved VM block(s)"), Freed);
	return Freed;
}

// ============================================================
// Trampoline-VM starvation.
//
// MinHook must place its trampoline within ±2 GB of the hooked function, because the detour is
// a 32-bit relative jump. Reserving that window makes MH_CreateHook fail with
// MH_ERROR_MEMORY_ALLOC -- which is the intermittent, never-observed failure four shipped
// behaviours depend on (bounded retry, the single-line fallback WARN, worker refusal -8, and
// the UI recovering after release).
//
// ⚠ MEM_RESERVE only, never MEM_COMMIT: this must consume ADDRESS SPACE, not RAM. Committing
// ~4 GB would page the machine rather than starve the allocator, and the harness drives the game
// under test on this same box.
// ============================================================
#include "Windows/AllowWindowsPlatformTypes.h"

int32 ADumperTestActor::ReserveTrampolineVm()
{
	constexpr SIZE_T kGranularity = 64 * 1024;      // Windows allocation granularity
	constexpr int32  kMaxBlocks   = 200000;         // hard ceiling on VAD entries we will create

	// Centre the sweep on this module: the functions MinHook will hook live here, so this is the
	// window its trampoline allocator will search.
	const uintptr_t Base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
	const uintptr_t Lo   = (Base > 0x7FFFFFFFull) ? (Base - 0x7FFFFFFFull) : kGranularity;
	const uintptr_t Hi   = Base + 0x7FFFFFFFull;

	// ⛔ WALK THE FREE REGIONS WITH VirtualQuery -- DO NOT STRIDE BLINDLY IN FIXED BLOCKS.
	// The first version of this reserved 1 MB at a time stepping 1 MB, which LOOKS like full
	// coverage and is not: VirtualAlloc(MEM_RESERVE) fails for the WHOLE request if any part of
	// it overlaps an existing allocation, so every 1 MB window that clipped a module or heap
	// failed outright and left the rest of that megabyte free. Measured 2026-09-07: 3,824 of
	// 4,096 blocks succeeded -- 93% coverage, which reads like success -- and the 272 failures
	// left up to ~960 KB free apiece. MinHook needs a few KB. The hook installed on the first
	// attempt (`hook_active=1`) and the run measured NOTHING.
	//
	// Querying instead reserves exactly what is actually free, so nothing is left behind.
	int32 Reserved = 0;
	uintptr_t Addr = Lo;
	while (Addr < Hi && Reserved < kMaxBlocks)
	{
		MEMORY_BASIC_INFORMATION Mbi{};
		if (VirtualQuery(reinterpret_cast<void*>(Addr), &Mbi, sizeof(Mbi)) != sizeof(Mbi))
		{
			break;
		}
		const uintptr_t RegionBase = reinterpret_cast<uintptr_t>(Mbi.BaseAddress);
		const uintptr_t RegionEnd  = RegionBase + Mbi.RegionSize;

		if (Mbi.State == MEM_FREE)
		{
			// Align up to allocation granularity, clamp to the window, reserve the whole run.
			uintptr_t Start = (RegionBase + kGranularity - 1) & ~(kGranularity - 1);
			if (Start < Lo) { Start = Lo; }
			const uintptr_t End = (RegionEnd < Hi) ? RegionEnd : Hi;
			if (End > Start)
			{
				const SIZE_T Size = static_cast<SIZE_T>(End - Start) & ~(kGranularity - 1);
				if (Size >= kGranularity)
				{
					void* P = VirtualAlloc(reinterpret_cast<void*>(Start), Size,
					                       MEM_RESERVE, PAGE_NOACCESS);
					if (P)
					{
						ReservedVmBlocks.Add(P);
						++Reserved;
					}
				}
			}
		}
		Addr = (RegionEnd > Addr) ? RegionEnd : (Addr + kGranularity);
	}
	UE_LOG(LogTemp, Warning,
	       TEXT("[DumperTest] -DumperTestStarveVM: reserved %d block(s) around module base %p. ")
	       TEXT("Call Hook_ReleaseTrampolineVM within ~40s to observe the RECOVERY half."),
	       Reserved, reinterpret_cast<void*>(Base));
	return Reserved;
}

int32 ADumperTestActor::ReleaseReservedVm()
{
	int32 Freed = 0;
	for (void* P : ReservedVmBlocks)
	{
		if (P && VirtualFree(P, 0, MEM_RELEASE))
		{
			++Freed;
		}
	}
	ReservedVmBlocks.Reset();
	return Freed;
}

#include "Windows/HideWindowsPlatformTypes.h"
