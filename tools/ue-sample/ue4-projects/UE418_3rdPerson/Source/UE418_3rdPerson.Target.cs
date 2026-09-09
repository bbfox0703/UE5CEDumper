// Minimal game target, added 2026-09-09 so the delegate-layout fixture has a host module.
using UnrealBuildTool;
using System.Collections.Generic;

public class UE418_3rdPersonTarget : TargetRules
{
	public UE418_3rdPersonTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Game;
		ExtraModuleNames.Add("UE418_3rdPerson");
	}
}
