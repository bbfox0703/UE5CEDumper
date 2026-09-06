// Copyright Epic Games, Inc. All Rights Reserved.

using UnrealBuildTool;

public class DumperTest : ModuleRules
{
	public DumperTest(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine", "InputCore", "EnhancedInput" });

		// ⭐ AD18 step 4 needs a UE game that IMPORTS dinput8.dll, and reading the import table of
		// all 16 installed UE shipping exes found NOT ONE. So the fixture has to be the importer.
		//
		// ⚠ dxguid is not optional: dinput8.lib carries only a short-import for
		// DirectInput8Create, while IID_IDirectInput8W lives in dxguid.lib. Without it the call
		// does not link, and linking is the entire point -- an import that is never referenced is
		// dropped by the optimizer and the exe ends up with no DINPUT8.dll entry at all.
		//
		// VERIFY OFFLINE BEFORE PACKAGING: py tools/pe/pe_imports_exports.py <exe> must list
		// DINPUT8.dll. If it does not, the reference was optimised away and a launch would waste
		// a whole session proving nothing.
		PublicSystemLibraries.AddRange(new string[] { "dinput8.lib", "dxguid.lib" });
	}
}
