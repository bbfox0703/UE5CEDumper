namespace UE5DumpUI.Models;

/// <summary>
/// Represents the current state of the UE5 engine connection.
/// </summary>
public sealed class EngineState
{
    public int UEVersion { get; init; }
    public bool VersionDetected { get; init; } = true;

    /// <summary>True when ueVersion came from a user-set persistent override (UI-driven, kept in HintCache).</summary>
    public bool IsUserOverride { get; init; }

    /// <summary>True when ueVersion came from Tier 3 bare-pattern OR publisher-bias fallback — UI surfaces a warning.</summary>
    public bool IsLowConfidence { get; init; }

    /// <summary>True when the DLL SKIPPED the scan entirely because the engine is not something
    /// it can read. Every pointer comes back empty by design, not by failure. Only ever set on a
    /// confidently detected version; a guess or a user override is never gated.
    /// <para>
    /// Two cases, split on <see cref="UEVersion"/> — see
    /// <c>PointerPanelViewModel.PreUE4SentinelVersion</c>:
    /// 400-410 is UE 4.0-4.10 (pre-<c>FUObjectItem</c>: raw <c>UObjectBase*</c> at stride 8 in an
    /// inline chunk table, a shape the layout presets cannot express), while 300 means positively
    /// identified as pre-UE4 (UE3) — a different object model with no <c>FUObjectArray</c> and no
    /// <c>FNamePool</c> at all. They get different banners because they have different remedies.
    /// </para></summary>
    public bool IsVersionTooOld { get; init; }

    /// <summary>Publisher thumbprint key (e.g. "SQUARE_ENIX") detected from PE VERSIONINFO — empty if no match.</summary>
    public string PublisherThumbprint { get; init; } = "";
    public string GObjectsAddr { get; init; } = "";
    public string GNamesAddr { get; init; } = "";
    public string GWorldAddr { get; init; } = "";

    /// <summary>True when GWorld was resolved to a non-null pointer — gates the
    /// "Locate in GWorld" feature (the forward path search needs a live UWorld
    /// root). False when the AOB scan failed and no UWorld fallback was found.</summary>
    public bool HasGWorld =>
        !string.IsNullOrEmpty(GWorldAddr) && GWorldAddr != "0" && GWorldAddr != "0x0";

    /// <summary>FSparseDelegateStorage::SparseDelegates address (UE 4.23+, optional — empty/0 when scan failed or version unsupported).</summary>
    public string SparseDelegatesAddr { get; init; } = "";

    public int ObjectCount { get; init; }
    public string ModuleName { get; init; } = "";

    /// <summary>PID of the process on the other end of the pipe. 0 when the DLL predates the
    /// field. The pipe name is a single global, so when two games have the dumper loaded they
    /// both serve it and the client lands on whichever instance is free -- this is what lets
    /// the UI say WHICH game it actually reached.</summary>
    public int ProcessId { get; init; }
    public string ModuleBase { get; init; } = "";

    // --- FUObjectItem layout (UE5.7+ packed-mode awareness) ---
    /// <summary>
    /// Detected within-item layout: "classic" (UObject*@+0x00, UE4.x..UE5.6),
    /// "unpacked57" (UObject*@+0x08, UE5.7+ reordered), or "packed57" (UObject* reconstructed
    /// from two split fields — UE5.7+ UE_ENABLE_FUOBJECT_ITEM_PACKING). Defaults to "classic".
    /// </summary>
    public string ItemLayoutMode { get; init; } = "classic";

    /// <summary>
    /// True when the game uses the *** UNVERIFIED *** UE5.7+ packed FUObjectItem layout. No
    /// shipping game uses it yet, so reconstructed addresses and every downstream export
    /// (CE XML / CSX / Teleport) are best-effort. UI surfaces a warning badge and export notes.
    /// </summary>
    public bool ItemPacked { get; init; }

    /// <summary>Within-item UObject* offset for the two direct layouts (0x00 classic, 0x08 unpacked). 0 under packed.</summary>
    public int ItemObjOffset { get; init; }

    // --- Dynamic offset validation (get_offsets) -------------------------------
    //
    // The DLL has always computed and published this verdict and the UI never asked
    // for it, so nothing could tell the user the walker was running on unmeasured
    // UE-version defaults — every value in Live Walker, every export, silently
    // derived from a guess. (audit #5 U3/X3)
    //
    // Two flags, not one, and the split is the DLL's (Grimoire.h:243):
    //   ProbeRan  — detection executed and the offsets are settled (success OR give-up)
    //   Validated — the values were actually MEASURED and are trustworthy

    /// <summary>
    /// True when every probed offset was measured. Defaults to TRUE so a DLL that
    /// predates the field — or a `get_offsets` call that failed — shows no warning,
    /// the same "absent reads as fine" convention <see cref="VersionDetected"/> uses.
    /// </summary>
    public bool OffsetsValidated { get; init; } = true;

    /// <summary>True when offset detection ran at all. False means DynOff still holds
    /// pure version defaults because detection never executed.</summary>
    public bool OffsetsProbeRan { get; init; }

    /// <summary>Which probe fell back, e.g. "unmeasured:elemsize" or
    /// "childprops-probe-failed". Empty when <see cref="OffsetsValidated"/> is true.</summary>
    public string OffsetsFallbackReason { get; init; } = "";

    /// <summary>One-line note embedded into exports generated while <see cref="ItemPacked"/> is true.</summary>
    public static string PackedExportNote =>
        "generated under UNVERIFIED UE5.7+ packed FUObjectItem layout — addresses are best-effort";

    /// <summary>How each pointer was found: "aob", "data_scan", "string_ref", "pointer_scan", "not_found"</summary>
    public string GObjectsMethod { get; init; } = "aob";
    public string GNamesMethod { get; init; } = "aob";
    public string GWorldMethod { get; init; } = "aob";
    public string SparseDelegatesMethod { get; init; } = "not_found";

    /// <summary>
    /// How the dumper was loaded into the game, self-reported by the DLL from its
    /// OWN module file name: "proxy:version.dll" | "proxy:dinput8.dll" |
    /// "proxy:dxgi.dll" | "injected" | "loaded:&lt;name&gt;" | "unknown". Empty on
    /// DLLs older than the load-mode change. The Proxy Deploy panel folds a proxy
    /// value into per-game "confirmed-working" LKG (after a stability dwell).
    /// </summary>
    public string LoadMode { get; init; } = "";

    // --- AOB Usage Tracking ---
    /// <summary>PE hash: TimeDateStamp + SizeOfImage (16 hex chars). Unique per game build.</summary>
    public string PeHash { get; init; } = "";

    /// <summary>
    /// Per-launch session token: the game process's creation time (FILETIME
    /// 100ns-since-1601, hi:lo packed as hex). Unique per launch even when the
    /// EXE loads at a CONSTANT base (no effective ASLR, e.g. SEED) — which is
    /// why <see cref="ModuleBase"/> alone could not distinguish an old launch
    /// from the current one. Empty on DLLs older than build 1227 (the
    /// stale-session gate then degrades to "no per-launch distinction").
    /// Folded into <see cref="GameSessionId"/>.
    /// </summary>
    public string ProcessCreationTime { get; init; } = "";

    /// <summary>
    /// Stable identity of the current live game launch = PE build hash + the
    /// per-launch process creation time. Snapshot captures stamp this into
    /// <c>SnapshotMeta.GameSessionId</c>; the Snapshot-diff / SPC per-row
    /// Live/Addr/🌍 gates compare a snapshot's stored id against the current
    /// session's to decide whether its session-local ObjAddr is still valid
    /// (a reinject/relaunch invalidates those addresses).
    ///
    /// Was <c>PeHash-ModuleBase</c> before build 1227, but ModuleBase is
    /// constant across launches on games without effective ASLR (SEED), so the
    /// gate never fired there. Process creation time always differs per launch,
    /// so it is a strictly better discriminator. Old-format snapshots
    /// (<c>PeHash-ModuleBase</c>) no longer match the new-format current id, so
    /// they correctly read as a different (stale) session and gray out.
    /// </summary>
    public string GameSessionId => $"{PeHash}-{ProcessCreationTime}";

    /// <summary>Winning pattern ID for each target (e.g. "GOBJ_V1"). Empty if fallback was used.</summary>
    public string GObjectsPatternId { get; init; } = "";
    public string GNamesPatternId { get; init; } = "";
    public string GWorldPatternId { get; init; } = "";
    public string SparseDelegatesPatternId { get; init; } = "";

    /// <summary>AOB scan hit addresses (instruction that references the pointer).</summary>
    public string GObjectsScanAddr { get; init; } = "";
    public string GNamesScanAddr { get; init; } = "";
    public string GWorldScanAddr { get; init; } = "";
    public string SparseDelegatesScanAddr { get; init; } = "";

    /// <summary>Per-target scan statistics.</summary>
    public int GObjectsPatternsTried { get; init; }
    public int GObjectsPatternsHit { get; init; }
    public int GNamesPatternsTried { get; init; }
    public int GNamesPatternsHit { get; init; }
    public int GWorldPatternsTried { get; init; }
    public int GWorldPatternsHit { get; init; }

    // --- GWorld AOB metadata (for CE symbol registration via CreateSymbolScript) ---
    /// <summary>Winning GWorld AOB pattern string (e.g. "48 8B 1D ?? ?? ?? ??").</summary>
    public string GWorldAob { get; init; } = "";
    /// <summary>Position of RIP displacement within AOB match (instrOffset + opcodeLen).</summary>
    public int GWorldAobPos { get; init; }
    /// <summary>Instruction end relative to AOB match (instrOffset + totalLen, for RIP calculation).</summary>
    public int GWorldAobLen { get; init; }

    // --- GEngine (&GEngine — the static slot, not the UEngine object) ---
    // Same contract as the GWorld triple above. A non-empty GEngineAob means a
    // GameEngine-rooted CE export can be AOB-wrapped and will survive a game restart;
    // empty means we only have the address (or the slot came from nowhere at all).
    /// <summary>Address of the &amp;GEngine slot ("0x..." or "" / "0x0" when unresolved).</summary>
    public string GEngine { get; init; } = "";
    /// <summary>"aob" when an AOB validated, otherwise "not_found".</summary>
    public string GEngineMethod { get; init; } = "not_found";
    public string GEnginePatternId { get; init; } = "";
    public string GEngineScanAddr { get; init; } = "";
    public string GEngineAob { get; init; } = "";
    public int GEngineAobPos { get; init; }
    public int GEngineAobLen { get; init; }

    /// <summary>
    /// Effective GameThreadDispatch invoke timeout in ms (default 5000 = Stark::kDefaultInvokeTimeoutMs).
    /// Driven from the DLL — already-loaded per-game override is reflected here. UI shows it next
    /// to the UE Version Override; user can adjust via SetInvokeTimeoutAsync.
    /// </summary>
    public int InvokeTimeoutMs { get; init; } = Constants.StarkDefaultInvokeTimeoutMs;

    /// <summary>
    /// VER_BUILD of the currently-running DLL (e.g. 648). Returned by the init pipe response.
    /// 0 means the DLL is older than build 653 and doesn't expose this field. The System tab
    /// compares against the UI's own bundled build to warn about "DLL not redeployed" — a
    /// common gotcha in proxy mode where the user updates the UI but forgets to copy the new
    /// DLL into the game's Binaries\Win64\ folder.
    /// </summary>
    public int DllBuildNumber { get; init; }
}
