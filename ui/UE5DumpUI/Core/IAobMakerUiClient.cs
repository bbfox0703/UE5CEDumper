namespace UE5DumpUI.Core;

/// <summary>[AOBM-GNAMES-SYMBOL] Why a <see cref="IAobMakerUiClient.GenerateAobAsync"/> produced no AOB. Each asks the
/// user for something different, which is why it is an enum and not a message.</summary>
public enum GenerateAobFailure
{
    /// <summary>An AOB came back.</summary>
    None,
    /// <summary>No AOBMaker.UI pipe to connect to: the app is not running (Cheat Engine alone is not enough).</summary>
    NotRunning,
    /// <summary>The pipe is listed but no instance came free in time: AOBMaker.UI serves one client at a time, so it is
    /// busy (a scan of its own, or Cheat Engine's "Send to AOBMaker"), not absent.</summary>
    Busy,
    /// <summary>AOBMaker.UI refused this caller: it serves only the same user at the same or a higher integrity level,
    /// so an elevated AOBMaker rejects an unelevated UE5DumpUI.</summary>
    Refused,
    /// <summary>Connected, but no usable reply before the deadline. AOBMaker.UI answers an unknown request with
    /// silence, so an older build that lacks <c>GenerateAob</c> looks like this too.</summary>
    NoReply,
    /// <summary>AOBMaker.UI answered, and could not make a unique AOB; its message says why.</summary>
    Failed,
}

/// <summary>[AOBM-GNAMES-SYMBOL] What AOBMaker.UI's <c>GenerateAob</c> answered.
/// <see cref="Pos"/> and <see cref="AobLen"/> are relative to the AOB's start and present only when the seed instruction
/// is RIP-relative, which is exactly the case a symbol script can replay.</summary>
public sealed record GeneratedAob(string Aob, int InjectionOffset, int? Pos, int? AobLen, int MatchCount, string Module);

/// <summary>[AOBM-GNAMES-SYMBOL] The whole answer: an AOB, or why there is none plus AOBMaker.UI's own words.</summary>
public sealed record GenerateAobResult(GeneratedAob? Aob, GenerateAobFailure Failure, string? ServerMessage);

/// <summary>
/// [AOBM-GNAMES-SYMBOL] Client for AOBMaker.UI's own pipe, <c>\\.\pipe\AOBMaker</c>. This is a different program from the
/// Cheat Engine plugin behind <see cref="IAobMakerBridge"/>: it runs only while the AOBMaker.UI app is open.
/// </summary>
public interface IAobMakerUiClient
{
    /// <summary>
    /// Ask AOBMaker.UI for a unique AOB around the instruction at <paramref name="hexAddress"/> in process
    /// <paramref name="processId"/>. AOBMaker reads the game itself; Cheat Engine does not have to be attached.
    /// Never throws.
    /// </summary>
    Task<GenerateAobResult> GenerateAobAsync(string hexAddress, int processId, CancellationToken ct = default);

    /// <summary>
    /// [AOBM-UI-INDICATOR] Is AOBMaker.UI's pipe listed under <c>\\.\pipe\</c>? It never connects: AOBMaker.UI has ONE
    /// pipe instance, logs every connection, and a probe holding it could turn Cheat Engine's own "Send to AOBMaker"
    /// away. Listed is not "will accept GenerateAob" (that is decided by its caller check). Never throws.
    /// </summary>
    Task<bool> IsPipeListedAsync(CancellationToken ct = default) => Task.FromResult(false);
}
