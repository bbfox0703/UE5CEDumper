namespace UE5DumpUI;

/// <summary>
/// Centralized constants for the UI application.
/// </summary>
public static class Constants
{
    // Named Pipe
    public const string PipeName = "UE5DumpBfx";

    // Application
    public const string AppName = "UE5DumpUI";
    public const string MutexName = "Global\\UE5DumpUI_SingleInstance";
    public const string AppVersion = "1.0.0";

    // Logging — category-routed to separate files
    public const string LogFolderName = "UE5CEDumper";

    /// <summary>
    /// Breadcrumb file naming the folder that holds UE5Dumper.dll, so
    /// scripts/UE5CEDumper.CT can find the DLL when Cheat Engine gives it nothing
    /// to infer from. Lives at the %LOCALAPPDATA%\UE5CEDumper ROOT, deliberately
    /// NOT under Logs\ — the retention sweep walks only the Logs directory and
    /// globs only *.log, so this is outside it on both counts.
    /// </summary>
    public const string DllPathBreadcrumbFile = "dll-path.txt";
    public const string LogSubFolder = "Logs";
    public const string LogSubfolderName = "UE5DumpUI";      // UI module subfolder under Logs/
    public const string MirrorLogPrefix = "ui";               // Prefix for mirror files in game folders
    // Retention is by AGE (LogMaxAgeDays), not by generation count. The previous
    // session's -0.log is archived to -YYYYMMDD-HHMMSS.log and aged out from there;
    // a file count could not express "keep 15 days" because rotation runs on every
    // startup. Matches Grimoire::LOG_RETENTION_DAYS on the DLL side.
    public const long LogMaxSizeBytes = 8 * 1024 * 1024;     // 8MB per file

    // Per-process mirror logging.
    //
    // There is deliberately NO folder-COUNT cap here (audit #5 AF9 removed
    // MaxProcessFolders = 20). The comment above is the whole policy, and a count cap
    // sat four lines below it contradicting it: a per-process folder is named after the
    // GAME, so the count is "distinct games played in the window" and cannot run away,
    // while the eviction was recursive and took the DLL's five log categories with it —
    // files Sein.cpp's age-only sweep had deliberately kept.
    public const int LogMaxAgeDays = 21;               // Keep this in sync with Grimoire::LOG_RETENTION_DAYS

    // ---- Log compression (compact /C /EXE:LZX, in place) -------------------------
    //
    // Retention bounds the AGE of the log corpus, not its SIZE: 21 days of multi-game
    // sessions measured 111.9 MB. LZX took the eligible 102.05 MB of that to 7.98 MB
    // (12.8:1) in 2.8 s, and left LastWriteTime untouched on all 289 files — which is why
    // PruneAgedLogs needs no change. Numbers + traps: docs/log-compression-eval.md.
    //
    // Size floor: 347 of 698 files were <= 4 KB and held 0.4 MB between them — half the
    // work for 0.4% of the bytes.
    public const long LogCompressMinSizeBytes = 4096;

    // Idle window for the MANUAL button: don't touch a file something may still be
    // appending to. The -0.log name guard is the real protection; this covers the
    // DLL-written per-game categories whose names the UI does not know.
    public const int LogCompressMinIdleHours = 1;

    // Extra idle time a `<cat>-0.log` must have before it counts as finished — applied on
    // BOTH triggers, on top of the ordinary idle window.
    //
    // `-0.log` is a SLOT NAME, not a liveness fact: a game last played 13 days ago still
    // owns a walk-0.log that nothing will ever append to again. Excluding those outright
    // was stricter than LoggingService.PurgeOrphanedLogs, which passes a live-name set only
    // for the UI's own folder and sweeps every GAME folder on age alone — i.e. we were
    // refusing to COMPRESS files the same subsystem is willing to DELETE. Measured cost of
    // the old rule on a real folder: 36 files / 5 MB permanently uncompressed, and the set
    // only grows, one final log per game ever tested.
    //
    // Compressing them is durable: Sein archives a `-0.log` by RENAME on the next
    // injection, and a rename preserves LZX (verified 335,872 bytes on disk before and
    // after). A game that really is running keeps its log's mtime fresh, and the file lock
    // is the backstop if it somehow does not.
    public const int LogCompressLiveFileMinAgeDays = 7;

    // Age floor for the AUTOMATIC startup sweep (opt-in, default OFF). Deliberately longer
    // than the manual window: an unattended pass should only ever touch logs that are
    // plainly historical, and a log you might still be reading about yesterday's session
    // is not.
    public const int LogAutoCompressMinAgeDays = 7;

    // compact.exe batching. The CHARACTER budget is the binding one — Windows caps a
    // command line at 32,767 and these paths are long (a per-game folder is named after
    // the game EXE). Set well under the cap; overflowing fails a whole batch.
    public const int LogCompressBatchSize = 40;
    public const int LogCompressMaxArgChars = 24000;

    // Leftover-proxy cleanup reports (Reports/leftover-proxies-<stamp>.txt).
    //
    // AGE, not a generation count, for the same reason the logs above use age — and the flaw is the
    // same one, just driven by clicks instead of launches: comparing before/after can produce several
    // reports in one session, and "keep the last 5" would then evict a genuinely older useful one
    // regardless of its date.
    //
    // Longer than the log window on purpose: a report is the record of a DESTRUCTIVE action, which is
    // exactly the thing someone comes back to look at. Pruned when a new report is written rather than
    // at startup (these are user-initiated artefacts, so a launch that never touches the feature must
    // not silently delete anything), and the NEWEST report is never pruned whatever its age.
    public const int ReportMaxAgeDays = 30;

    // Log category names
    public const string LogCatInit = "init";
    public const string LogCatPipe = "pipe";
    public const string LogCatView = "view";

    // ---- Crash diagnostics -------------------------------------------------
    //
    // Single-file crash report at the %LOCALAPPDATA%\UE5CEDumper ROOT (not under
    // Logs\ — the retention sweep walks only that folder and globs only *.log).
    // Under Native AOT this is the ONLY diagnostic surface for a fault that kills
    // the process before, or instead of, anything reaching the normal log files.
    public const string CrashLogFileName = "crash.log";

    // Above this uptime, a crash report still claiming the "Startup" phase is
    // reporting a STALE marker rather than a real startup failure — no startup of
    // this app takes a minute. CrashReportFormatter.Headline says so explicitly
    // instead of repeating the phase; see [PASTECRASH-2026-08-18], where the
    // hard-coded phrase "startup crash" mislabelled a 31-minute-in crash.
    public const int CrashStartupPhaseMaxSeconds = 60;

    // Pipe Communication
    public const int PipeConnectTimeoutMs = 5000;
    public const int DefaultPageSize = 200;

    // Hard timeout for Live Walker refresh / walk calls. If the DLL hangs on a
    // destroyed object (e.g. garbage FField chain from recycled memory), this
    // guarantees the UI unblocks instead of spinning IsLoading forever.
    public const int LiveWalkerRefreshTimeoutMs = 10000;

    // Object Tree
    public const int ObjectTreePageSize = 2000;     // Batch size for loading all objects
    public const int ObjectTreeMaxDisplay = 5000;   // Max items shown in FilteredNodes ListBox
    // Server-side cap for the top Search box (search_objects). Matches ObjectTreeMaxDisplay
    // so every returned row can display; the server reports Truncated when more matches
    // exist (the UI then points at Reload + bottom filter for the whole pool).
    public const int ObjectTreeSearchCap = 5000;

    // Objects-per-page when walking the FULL GObjects pool via GetObjectListAsync
    // (SDK / USMAP / symbol / dump-all exports). Distinct from ObjectTreePageSize
    // (2000) — the exporters page in larger 5000-object batches.
    public const int GObjectsWalkPageSize = 5000;

    // Live Walker preview
    public const int DefaultPreviewLimit = 2;       // Struct sub-fields in preview (0-6)

    // Live Walker auto-refresh
    public const int DefaultAutoRefreshIntervalSec = 10;
    public const int MinAutoRefreshIntervalSec = 6;
    public const int AutoRefreshBenchmarkBufferSec = 5; // Extra seconds added to benchmarked duration

    // AOB Usage Tracking
    public const string AobUsageFilePrefix = "UE5CEDumper";

    // Experimental features (Snapshot / SPC Query / Class Pivot — gated via the
    // System-tab credit checkbox). Persisted under %LOCALAPPDATA%\UE5CEDumper.
    public const string ExperimentalSettingsFile = "experimental.json";

    // Global panel/Live-Walker OPTIONS (stable user preferences — export toggles,
    // scan/search behaviour, formats, limits). Persisted under %LOCALAPPDATA%\UE5CEDumper.
    public const string UiOptionsFile = "ui-options.json";

    // Per-game Live-Walker bookmarks — one file per game, bookmarks.<pe_hash>.json
    // under %LOCALAPPDATA%\UE5CEDumper\Bookmarks (same per-game convention as the
    // snapshot DB). See BookmarkSubFolder.
    public const string BookmarkFilePrefix = "bookmarks";

    // Number of Live-Walker bookmark slots.
    public const int BookmarkSlotCount = 8;

    // Per-game Teleport coordinate library — teleport-coords.<module>.json under
    // %LOCALAPPDATA%\UE5CEDumper\TeleportCoords\. Keyed by the EXE MODULE NAME, not the
    // PE hash: bookmarks store offsets and should die on a game patch, but a
    // hand-curated coordinate list must survive one. See
    // docs/teleport-coord-library-spec.md D1.
    //
    // It is the THIRD per-game family and it used to write to the flat app-data ROOT,
    // which the App-data layout rule forbids — the root is for files that are app-wide
    // and FIXED IN NUMBER, and this one grows by up to four files per game forever
    // (.json + .bak + .preimport.bak + .preclear.bak). Moved to its own subfolder
    // beside Snapshots\ and Bookmarks\ (audit #5 AF11); AppDataFolderMaintenance moves
    // anything left at the root on first use, per game, as a GROUP.
    public const string CoordLibraryFilePrefix = "teleport-coords";
    public const string CoordLibrarySubFolder  = "TeleportCoords";

    // Soft warning threshold for a CE-Lua export (CE's AA-script EDITOR gets
    // sluggish long before the pipe does: 4000 entries is ~480 KB, ~4.6% of the
    // verified 10 MiB cap). Not a hard cap.
    public const int CoordLibraryExportWarnCount = 2000;

    // Experimental Snapshot store — per-game SQLite DB under
    // %LOCALAPPDATA%\UE5CEDumper\Snapshots, named snapshots.<pe_hash>.db so each
    // game's snapshots stay isolated (no cross-game mixing / growth / corruption).
    // See SnapshotSubFolder.
    public const string SnapshotDbPrefix = "snapshots";

    // ---- Per-game data SUBFOLDERS under %LOCALAPPDATA%\UE5CEDumper ----------------
    //
    // Both families used to sit at the folder ROOT. They are the only files there whose
    // count grows without bound — one set per game, and a new PE hash on every game
    // patch — so they buried the handful of app-wide files (dll-path.txt,
    // experimental.json, ui-options.json, teleport-hotkeys.txt, window-state.txt) that
    // someone actually has to find by hand.
    //
    // Sibling folders of Logs\ and Reports\, PascalCase to match them. Files still at
    // the old root location are MOVED here once, at first use, by
    // Services.AppDataFolderMaintenance — see that file for why a game's files move as
    // a GROUP (a .db that arrives without its -wal has silently lost every transaction
    // the WAL held).
    //
    // Same folder scheme, DIFFERENT retention: Snapshots\ is swept at DataMaxAgeDays,
    // Bookmarks\ is never swept. See DataMaxAgeDays for why.
    public const string SnapshotSubFolder = "Snapshots";
    public const string BookmarkSubFolder = "Bookmarks";

    // Age-out window for the SNAPSHOT folder only. Same 21 days as LogMaxAgeDays, and by
    // AGE for the same reason a file count could not express the policy — but a SEPARATE
    // constant on purpose: this is user data, not logs, so the two windows must be free
    // to diverge without one silently dragging the other along.
    //
    // BOOKMARKS ARE DEDUCTIBLY EXEMPT — BookmarkStore passes maxAgeDays: 0. A snapshot DB
    // is a regenerable multi-GB capture, which is what makes a disk-reclaiming sweep worth
    // its risk; a bookmark file is a few KB of hand-placed navigation nobody can replay
    // their way back to. Same folder scheme, deliberately different retention.
    //
    // "Unused" is honest rather than "unmodified", but NOT via last-access time: NTFS
    // last-access updates are on by default here (fsutil DisableLastAccess = 2), so AV /
    // backup / indexer reads keep every file looking like today and the sweep would never
    // fire. Instead SnapshotStore RECORDS use — AppDataFolderMaintenance.TouchUsed stamps
    // the write time when a game becomes active — so connecting to a game resets its
    // window even in a session that never opens the Snapshot tab. Aged out per GAME, not
    // per file: the newest timestamp in a game's set governs the whole set, so a
    // 30-day-old denylist sibling never outlives (or drags down) the .db it belongs to.
    public const int DataMaxAgeDays = 21;
    // Objects streamed per snapshot_chunk pipe round-trip. 8192 (raised 200 -> 1000
    // -> 8192) for two reasons on huge games (FF7 Rebirth ~433K objects: ~53 chunks):
    //   (a) fewer pipe round-trips + SQLite write transactions;
    //   (b) the DLL parallelizes each chunk's object walk, and ScanThreadCount only
    //       engages worker threads at >= 8192 work items — so a full chunk runs
    //       multi-threaded (the single-threaded walk was the 10+ min bottleneck).
    // Safe: byte-mode pipe (no message cap) + StreamReader.ReadLineAsync accumulates
    // any size. See docs/todo.md snapshot-perf item (in-game re-test pending).
    public const int SnapshotChunkSize = 8192;

    // Snapshot Diff / SPC / Group / Value snapshot-query row cap. Shared by the
    // model defaults (SpcQuery.MaxRows, SnapshotDiffFilter.MaxRows, group MaxResults,
    // ValueSearchUiOptions.MaxResults) AND the SnapshotStore `x > 0 ? x : N` fallbacks
    // so a single number governs the cap everywhere.
    public const int DefaultMaxQueryRows = 50000;

    // Live DLL value/group scan candidate cap (the DLL session's max returned rows).
    // Intentionally a SEPARATE constant from DefaultMaxQueryRows even though the value
    // matches — different subsystem (in-game scan session vs. snapshot-DB query).
    public const int ScanSessionMaxResults = 50000;

    // Value/group scan begin deadline in ms (Value Search "Timeout" slider default).
    // Also the sentinel the wire-shape code compares against to omit the field when
    // the user hasn't overridden it. Mirrors the DLL's 15000 ms scan default.
    /// <summary>Leaves kept PER SLOT per object in a group scan (DLL `per_slot_cap`).
    /// NOT a performance knob: this list is what a later Changed/Decreased refine
    /// re-reads, and leaves arrive base-class-first, so too small a value silently hides
    /// a derived class's own fields — at the old fixed 8 every AActor stored only
    /// PrimaryActorTick/CustomTimeDilation and a Changed refine pruned everything.
    /// The DLL owns the clamp band; these mirror it so the UI can validate locally.</summary>
    public const int GroupPerSlotCap    = 256;
    public const int GroupPerSlotCapMin = 8;
    public const int GroupPerSlotCapMax = 4096;

    public const int ScanSessionDeadlineMs = 15000;

    // Server-side paging window for scan begin / refine / query. Distinct from
    // DefaultPageSize (200) — the scan session pages in 1000-row windows.
    public const int ScanSessionPageSize = 1000;

    // GameThreadDispatch (Stark) default invoke timeout in ms. Mirrors the DLL's
    // Stark::kDefaultInvokeTimeoutMs; also the sentinel the UI uses to treat a value
    // as "clear override" so the per-game JSON stays clean.
    public const int StarkDefaultInvokeTimeoutMs = 5000;

    // Class / array pivot group cap (PivotQuery.MaxGroups, ArrayPivotQuery.MaxGroups).
    public const int DefaultMaxPivotGroups = 5000;

    // Batch-xref confirm warning threshold: warn before running a batch "Find Funcs"
    // over more than this many classes/rows (each is a full game-wide sweep).
    // NOTE: InterestingFunctionsViewModel intentionally uses its own local 100.
    public const int XrefBatchWarnThreshold = 25;

    // WalkWorldAsync actor limit passed by the Live Walker world-root walks.
    public const int WorldWalkMaxDepth = 500;

    // Default inline array element read limit (2^7). InstanceFinder / LiveWalker
    // seed their per-panel ArrayLimit with this; MainWindow derives its own from an
    // exponent slider.
    public const int DefaultArrayLimit = 128;

    // Default CE DropDownList enum-entry cap (2^9). InstanceFinder / LiveWalker seed
    // their per-panel DropDownLimit with this.
    public const int DefaultDropDownLimit = 512;

    // Default CE String leaf <Length> display window (2^8 = 256 chars). InstanceFinder /
    // LiveWalker seed their per-panel CeStringLength with this; the toolbar "String Length"
    // exponent slider floors at 2^4 = 16. Applies to Copy CE XML / Copy CE Field only.
    public const int DefaultCeStringLength = 256;

    // Default instance-search result cap (InstanceFinderUiOptions.InstanceSearchCap
    // and the InstanceFinder panel's own default).
    public const int DefaultInstanceSearchCap = 5000;

    // Default Property Search result cap (PropertySearchUiOptions.PropertySearchCap and
    // the panel's own default). [PROPSEARCHCAP-2026-08-19]
    //
    // ⚠ Deliberately NOT DefaultInstanceSearchCap. A property-search row is a match per
    // property per class WITH a resolved preview value, so it is far heavier than an
    // instance address; 200 was the long-standing wire default and stays the default here.
    // What changed is that the panel now has a control to raise it.
    public const int DefaultPropertySearchCap = 200;

    // Default Classes-tab row cap (GameClassFilterUiOptions.ClassListCap and the panel's own
    // default). [CLASSCAP-2026-08-21] — the panel's own status line has always advised "raise
    // the cap" and there was no control to raise, the third instance of the Z10 shape.
    //
    // ⚠ 5,000 is the long-standing wire default and stays the default: a class row carries a
    // walked property/function summary, so a big pool is not free. Avowed has 7,409 game classes
    // (and 5,102 game-only), so a real title genuinely lands past it.
    public const int DefaultClassListCap = 5000;

    // Shared floor/ceiling for EVERY row cap the user can set — Instance Finder, Property
    // Search, Classes. The ceiling is also enforced DLL-side in Fern.cpp per command; the UI
    // clamp is a convenience, not the guarantee. Keep this in step with those clamps —
    // PropertySearchCapClampTests and ClassListCapTests each pin their own pairing.
    public const int MinSearchCap = 100;
    public const int MaxSearchCap = 50000;

    // UI
    public const int DefaultWindowWidth = 1400;
    public const int DefaultWindowHeight = 900;
    public const int TreePanelWidth = 350;

    // Proxy DLL Deploy
    // Three proxy DLL options — user picks one via the UI RadioButton.
    // ProxyDllName is kept as the legacy "default" name for backward
    // compatibility with existing tests; the canonical lookup goes through
    // ProxyType.GetDllName() (Models/ProxyType.cs).
    public const string ProxyDllName = "version.dll";
    public const string ProxyDllNameDinput8 = "dinput8.dll";
    // dxgi.dll — statically imported by every D3D11/D3D12 UE game; the
    // reliable hijack target for EXEs that import neither version nor dinput8.
    public const string ProxyDllNameDxgi = "dxgi.dll";
    // winmm.dll — measured at 24/24 importable across every installed UE game,
    // exactly matching dxgi. It exists NOT for coverage (it reaches nothing dxgi
    // misses) but as a second free SLOT: dxgi.dll is the name ReShade and many
    // mod loaders take, and version.dll is likewise often occupied. See the
    // 4th-proxy section in docs/todo.md.
    public const string ProxyDllNameWinmm = "winmm.dll";
    public const string ProxyProductName = "UE5CEDumper";
    public const string SteamRegistryPath = @"SOFTWARE\WOW6432Node\Valve\Steam";
    public const string SteamRegistryKey = "InstallPath";
    public const string SteamDefaultPath = @"C:\Program Files (x86)\Steam";
    public const string SteamLibraryFoldersVdf = @"config\libraryfolders.vdf";
    public const string SteamAppsCommon = @"steamapps\common";
}
