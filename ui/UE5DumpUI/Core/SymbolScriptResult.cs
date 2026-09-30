namespace UE5DumpUI.Core;

/// <summary>
/// [AOBM-ACTIVATE-RESULT] What the AOBMaker CE plugin says about a <c>CreateSymbolScript</c> sent with
/// <c>autoActivate</c>. <see cref="Created"/> -- the reply's <c>success</c> -- only ever meant "the record exists";
/// whether Cheat Engine enabled it is <see cref="Activated"/>, which a plugin older than AOBMaker v20260930
/// (build 155) never sends. So null is "not known", never success.
/// </summary>
/// <param name="Created">The record exists in CE's address list.</param>
/// <param name="Activated">CE reads the record back as active; null when the plugin did not say.</param>
/// <param name="SymbolRegistered">The symbol is registered and resolves to that registration; null when not said.</param>
/// <param name="Message">The plugin's reason: what failed, or why a created record is not active or not usable.</param>
/// <param name="TimedOut">No reply in time. The record may exist and may be active, so a blind retry can add a
/// second one (AOBMaker reply §3 rule 8).</param>
public sealed record SymbolScriptResult(bool Created, bool? Activated, bool? SymbolRegistered, string? Message,
                                        bool TimedOut = false)
{
    /// <summary>A plugin client that only reports creation: activation not known.</summary>
    public static SymbolScriptResult FromCreated(bool created) => new(created, null, null, null);
}
