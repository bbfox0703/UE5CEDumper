using UnrealBuildTool;

public class UE418_3rdPerson : ModuleRules
{
	public UE418_3rdPerson(ReadOnlyTargetRules Target) : base(Target)
	{
		// IWYU rather than the module-wide PCH: 4.16+ supports it, and it keeps the fixture's
		// own includes meaningful. install.py still prepends the module header, which is
		// harmless here and required on 4.15.
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
		PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine", "InputCore" });
	}
}
