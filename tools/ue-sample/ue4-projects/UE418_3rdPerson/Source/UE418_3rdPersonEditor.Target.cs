// Editor target -- the cook runs the editor commandlet, so it must exist.
using UnrealBuildTool;
using System.Collections.Generic;

public class UE418_3rdPersonEditorTarget : TargetRules
{
	public UE418_3rdPersonEditorTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Editor;
		ExtraModuleNames.Add("UE418_3rdPerson");
	}
}
