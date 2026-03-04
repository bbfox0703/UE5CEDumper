namespace UE5DumpUI.Core;

/// <summary>
/// Bridge to AOBMaker CE Plugin for navigating CE Memory Viewer.
/// Communicates via <c>\\.\pipe\AOBMakerCEBridge</c> named pipe.
/// </summary>
public interface IAobMakerBridge
{
    /// <summary>Cached availability — true if the last pipe connect succeeded.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Test pipe connectivity and update <see cref="IsAvailable"/>.
    /// </summary>
    Task<bool> CheckAvailabilityAsync(CancellationToken ct = default);

    /// <summary>
    /// Navigate CE Memory Viewer hex dump (bottom pane) to the specified address.
    /// Sends <c>NavigateHexView</c>.
    /// </summary>
    /// <param name="hexAddress">Bare hex address without 0x prefix (e.g. "7FF769E29110")</param>
    Task<bool> NavigateHexViewAsync(string hexAddress, CancellationToken ct = default);

    /// <summary>
    /// Navigate CE Memory Viewer disassembler (top pane) to the specified address.
    /// Sends <c>NavigateDisassembler</c>.
    /// </summary>
    /// <param name="hexAddress">Bare hex address without 0x prefix (e.g. "7FF769E29110")</param>
    Task<bool> NavigateDisassemblerAsync(string hexAddress, CancellationToken ct = default);

    /// <summary>
    /// Create an Auto Assembler script entry in CE's address list.
    /// Sends <c>CreateAAScript</c>.
    /// </summary>
    /// <param name="description">Description shown in CE address list</param>
    /// <param name="script">Full AA script content ([ENABLE]/[DISABLE] sections)</param>
    /// <param name="autoActivate">Whether to activate the script immediately after creation</param>
    Task<bool> CreateAAScriptAsync(string description, string script, bool autoActivate = true,
        CancellationToken ct = default);

    /// <summary>
    /// Create an AOB-scan-based symbol registration AA script in CE's address list.
    /// Sends <c>CreateSymbolScript</c> — the CE Plugin's <c>BuildSymbolScanScript()</c>
    /// generates the full AA script from these AOB parameters.
    /// </summary>
    /// <param name="name">Description shown in CE address list</param>
    /// <param name="aob">AOB pattern string (e.g. "48 8B 1D ?? ?? ?? ??")</param>
    /// <param name="pos">Displacement offset within AOB match (instrOffset + opcodeLen)</param>
    /// <param name="aoblen">Instruction end relative to AOB match (instrOffset + totalLen)</param>
    /// <param name="symbol">CE symbol name to register (e.g. "gworld_addr")</param>
    /// <param name="module">Game module name for AOBScanModule (e.g. "Game-Win64-Shipping.exe")</param>
    /// <param name="autoActivate">Whether to activate the script immediately after creation</param>
    Task<bool> CreateSymbolScriptAsync(string name, string aob, int pos, int aoblen,
        string symbol, string module, bool autoActivate = true, CancellationToken ct = default);
}
