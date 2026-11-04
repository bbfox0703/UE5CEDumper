namespace UE5DumpUI.Models;

/// <summary>
/// [MAINWINDOW-NAVIGATE-EXACT-NAME] What <c>find_live_instance</c> answered: one live object of the class or of a
/// subclass, never a template, or none. <see cref="Address"/> is empty when there is none.
/// </summary>
public sealed class LiveInstanceResult
{
    public string Address { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>The object's own class: a subclass of the one asked for, or that class.</summary>
    public string ClassName { get; init; } = "";
    public string ClassAddress { get; init; } = "";
    /// <summary>"live", "debug_camera_owned" (no other object was live) or "none".</summary>
    public string Pick { get; init; } = "none";
    public int Scanned { get; init; }

    public bool Found => !string.IsNullOrEmpty(Address);
}
