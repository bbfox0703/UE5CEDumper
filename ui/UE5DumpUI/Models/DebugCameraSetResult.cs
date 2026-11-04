namespace UE5DumpUI.Models;

/// <summary>
/// [INVOKE-NONSTATIC-ON-CDO] What a Debug Camera Force answered: the resulting state (as
/// <c>IDumpService.SetDebugCameraAsync</c> returns it), and whether the toggle ran on a template -- the CheatManager's
/// class default object, when no live CheatManager exists -- where it does nothing. Null from a DLL that does not say:
/// one older than build 3676, or one newer than 3681, which refuses that ON with -17 instead ([DEBUGCAM-FORCEON-CDO]).
/// </summary>
public sealed record DebugCameraSetResult(int State, bool? NonStaticOnTemplate);
