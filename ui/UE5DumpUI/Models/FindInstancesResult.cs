namespace UE5DumpUI.Models;

/// <summary>
/// Result container for FindInstances, including diagnostic counters.
/// </summary>
public sealed class FindInstancesResult
{
    public List<InstanceResult> Instances { get; init; } = new();

    /// <summary>Total GObject indices scanned (= NumElements at call time).</summary>
    public int Scanned { get; init; }

    /// <summary>Non-null objects encountered during scan.</summary>
    public int NonNull { get; init; }

    /// <summary>Objects whose class name resolved successfully.</summary>
    public int Named { get; init; }
}
