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

    /// <summary>[LEAD-CONSOLE-PIN-EXPECT-CLASS] The class the DLL read at <see cref="InstanceAddr"/> (hex string); empty
    /// from a DLL that does not send it.</summary>
    public string InstanceClassAddr { get; init; } = "";

    /// <summary>[FINDINSTANCE-SUBSTRING-USERFACING] How the DLL chose <see cref="InstanceAddr"/>: one of
    /// <c>Constants.InvokePick*</c> ("address" when the caller sent one); empty from a DLL that does not say.</summary>
    public string InstancePick { get; init; } = "";

    /// <summary>The object's name and its class's name, read before the call; empty from a DLL that does not send
    /// them.</summary>
    public string InstanceName { get; init; } = "";
    public string InstanceClassName { get; init; } = "";

    /// <summary>[INVOKE-NONSTATIC-ON-CDO] Whether the object is a template: "" (a live object), "class_default", or
    /// "template" (one of a class-default object's default subobjects, or a Blueprint's component template). Null from a
    /// DLL that does not say.</summary>
    public string? InstanceTemplate { get; init; }

    /// <summary>What a "template" belongs to: the class default object above it, or, for an archetype with none, its
    /// own outer (a Blueprint class, for its component templates).</summary>
    public string TemplateOwnerName { get; init; } = "";

    /// <summary>The DLL's verdict: the call ran a function that is not static on a template, where it may do nothing.
    /// A warning, never a refusal. Null from a DLL that does not say.</summary>
    public bool? NonStaticOnTemplate { get; init; }

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

/// <summary>
/// A string INPUT param for a pipe invoke. String params can't be baked into
/// <c>params_hex</c> like scalars — an FString is passed by value as a 16-byte
/// <c>{ Data*, Num, Max }</c> struct, and its <c>Data</c> pointer must be a
/// valid address in the GAME process. The DLL (injected, so its heap is the
/// game's) allocates the char buffer, patches the struct at <see cref="Offset"/>,
/// runs ProcessEvent, then frees it. The UI leaves those 16 bytes zeroed in
/// <c>params_hex</c> and sends this descriptor instead.
/// </summary>
/// <param name="Offset">Byte offset of the FString slot within the params buffer.</param>
/// <param name="Wide">True for UTF-16 <c>FString</c>; false for byte
///     <c>FUtf8String</c>/<c>FAnsiString</c>.</param>
/// <param name="Text">The string value to build in the target process.</param>
public sealed record InvokeStringParam(int Offset, bool Wide, string Text);
