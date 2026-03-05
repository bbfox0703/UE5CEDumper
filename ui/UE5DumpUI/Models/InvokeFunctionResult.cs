namespace UE5DumpUI.Models;

/// <summary>
/// Result of a pipe-based ProcessEvent invocation.
/// Returned by IDumpService.InvokeFunctionAsync.
/// </summary>
public sealed class InvokeFunctionResult
{
    /// <summary>Return code from UE5_CallProcessEvent (0=success, negative=error).</summary>
    public int Result { get; init; }

    /// <summary>Resolved UObject instance address (hex string).</summary>
    public string InstanceAddr { get; init; } = "";

    /// <summary>Resolved UFunction address (hex string).</summary>
    public string FuncAddr { get; init; } = "";

    /// <summary>Size of parameter buffer sent (bytes).</summary>
    public int ParmsSize { get; init; }

    /// <summary>Post-call param buffer as hex (may contain out-param values).</summary>
    public string ResultHex { get; init; } = "";

    /// <summary>Human-readable status message on success.</summary>
    public string Message { get; init; } = "";

    /// <summary>Error description (set when Result != 0 or call failed).</summary>
    public string Error { get; init; } = "";

    /// <summary>Convenience: true if ProcessEvent returned 0 and no error.</summary>
    public bool Success => Result == 0 && string.IsNullOrEmpty(Error);
}
