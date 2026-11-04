namespace UE5DumpUI.Models;

/// <summary>
/// [MAINWINDOW-NAVIGATE-EXACT-NAME] What <c>find_live_instance</c> answered: one live object of the class or of a
/// subclass, never a template, or none. <see cref="Address"/> is empty when there is none.
/// </summary>
public sealed class LiveInstanceResult
{
    public const string PickLive = "live";
    /// <summary>The Debug Camera controller's own object: nothing else of the class was live.</summary>
    public const string PickDebugCameraOwned = "debug_camera_owned";
    public const string PickNone = "none";
    /// <summary>A DLL that predates find_live_instance answered through the old exact-name lookup.</summary>
    public const string PickLegacy = "legacy";

    public string Address { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>The object's own class: a subclass of the one asked for, or that class.</summary>
    public string ClassName { get; init; } = "";
    public string ClassAddress { get; init; } = "";
    /// <summary>One of the Pick* constants.</summary>
    public string Pick { get; init; } = PickNone;
    public int Scanned { get; init; }

    public bool Found => !string.IsNullOrEmpty(Address);
}
