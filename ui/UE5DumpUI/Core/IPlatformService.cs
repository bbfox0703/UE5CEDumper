using UE5DumpUI.Models;

namespace UE5DumpUI.Core;

/// <summary>
/// Platform-specific operations interface.
/// All OS-dependent calls must go through this interface.
/// </summary>
public interface IPlatformService
{
    /// <summary>Try to acquire single-instance mutex. Returns true if this is the first instance.</summary>
    bool TryAcquireSingleInstance();

    /// <summary>Release the single-instance mutex.</summary>
    void ReleaseSingleInstance();

    /// <summary>Bring the already-running instance's window to the front. Called when
    /// <see cref="TryAcquireSingleInstance"/> fails, i.e. the second launch is about to
    /// exit — without this it exits before the logger exists, so the user gets no window,
    /// no dialog and no log line, and a double-click reads as "nothing happened". (B42)
    /// <para>Default no-op so test doubles need not implement it; returns whether a
    /// window was actually raised.</para></summary>
    bool ActivateExistingInstance() => false;

    /// <summary>Get the application data folder path (%APPDATA%).</summary>
    string GetAppDataPath();

    /// <summary>Get the log directory path.</summary>
    string GetLogDirectoryPath();

    /// <summary>
    /// Copy text to the clipboard. Returns <c>true</c> only when the text actually
    /// reached the clipboard; <c>false</c> means the copy did nothing.
    ///
    /// <para><b>Never throws for an ordinary clipboard failure</b>, and that is the
    /// contract, not an implementation detail. A clipboard write can fail for
    /// reasons that have nothing to do with this app (another process holding the
    /// clipboard open, an OLE source that went away), and every caller here is an
    /// <c>[RelayCommand]</c>: CommunityToolkit's <c>AsyncRelayCommand</c> rethrows a
    /// faulted command onto the dispatcher, where the fault carries OUR frames, so
    /// <c>InputLayerFaultClassifier</c> correctly refuses to swallow it and the
    /// process dies. Copy is on ~60 buttons; a momentarily busy clipboard must not
    /// be able to close the app from any of them ([PASTECRASH-2026-08-18]).</para>
    ///
    /// <para>Faults that mean the process is no longer trustworthy
    /// (<c>InputLayerFaultClassifier.IsNeverSwallowable</c>: OOM, access violation,
    /// trimming/AOT damage) still propagate — those must stay loud everywhere.</para>
    /// </summary>
    Task<bool> CopyToClipboardAsync(string text);

    /// <summary>Open the OS file browser at <paramref name="path"/>: reveal/select the
    /// file if it exists, otherwise open the containing directory. Never throws.</summary>
    Task RevealInExplorerAsync(string path);

    /// <summary>Get the machine name for per-machine file naming.</summary>
    string GetMachineName();

    /// <summary>
    /// Close the IME for the given top-level window handle so keystrokes pass
    /// through as direct alphanumeric input. Used when focus enters a text
    /// field: every input field in this app takes ASCII content (hex
    /// addresses, AOB patterns, numbers, class / property names, file paths),
    /// so a CJK IME left in composition mode only gets in the way. We do NOT
    /// restore the prior IME state on focus-out — a user who wants CJK input
    /// flips the IME back manually. No-op on non-Windows / null handle.
    /// </summary>
    void CloseImeForWindow(IntPtr windowHandle);

    /// <summary>
    /// Show a platform save-file dialog.
    /// Returns the chosen file path, or null if user cancelled.
    /// </summary>
    Task<string?> ShowSaveFileDialogAsync(string defaultFileName, string filterName, string filterExtension);

    /// <summary>
    /// Show a platform open-file dialog. Returns the chosen file path, or null if
    /// the user cancelled. Default returns null so non-Windows implementations and
    /// test doubles need not provide it; the real Windows service overrides it.
    /// </summary>
    Task<string?> ShowOpenFileDialogAsync(string filterName, string filterExtension) =>
        Task.FromResult<string?>(null);

    /// <summary>
    /// Open a path with its default handler (ShellExecute), e.g. a .txt in the user's text editor.
    /// Distinct from <see cref="RevealInExplorerAsync"/>, which opens Explorer AT the file instead of
    /// opening the file itself. Default no-op so non-Windows implementations and test doubles need
    /// not provide it. Never throws — failing to open a report must not disturb the app.
    /// </summary>
    Task OpenWithShellAsync(string path) => Task.CompletedTask;

    /// <summary>
    /// Move a file to the Recycle Bin so the operation is UNDOABLE. Returns true only when the
    /// shell reports success and the operation was not aborted.
    ///
    /// <para>Default returns <b>false</b>, and false means <b>refuse</b> — a caller must never fall
    /// back to <c>File.Delete</c>. The whole point of routing leftover-proxy cleanup through the
    /// Recycle Bin is that a wrong call costs nothing; a silent downgrade to a permanent delete
    /// would remove that property while keeping the reassuring wording.</para>
    ///
    /// <para>The Windows implementation additionally refuses whenever the volume has no working
    /// Recycle Bin — see <see cref="VolumeHasRecycleBin"/>: <c>FOF_ALLOWUNDO</c> there silently
    /// hard-deletes and returns success, which would make the promise a lie.</para>
    /// </summary>
    bool MoveToRecycleBin(string path) => false;

    /// <summary>Does the volume holding <paramref name="fullPath"/> have a working Recycle Bin?
    ///
    /// <para>Exposed separately so the ORPHAN SCAN can ask before the confirm dialog is built,
    /// and mark the row <c>NotOnFixedDrive</c> rather than promising a recycle it cannot deliver.
    /// Previously the question was first asked inside the delete call, which is after the user
    /// has already been told what would happen. (B13/B41)</para>
    ///
    /// <para>Default true so non-Windows test doubles keep their existing behaviour.</para>
    /// </summary>
    bool VolumeHasRecycleBin(string fullPath) => true;

    /// <summary>
    /// Opt the running process into the Windows Restart Manager so a
    /// reboot / update relaunches it on next sign-in (Win10 + Win11, gated by
    /// the user's "restart apps" setting). Default no-op so non-Windows
    /// implementations and test doubles need not provide it; the real Windows
    /// service overrides it with <c>RegisterApplicationRestart</c>.
    /// </summary>
    void RegisterForRestart() { }

    /// <summary>
    /// Enumerate ready fixed/removable drives with metadata AND the physical disk
    /// number backing each (via IOCTL_STORAGE_GET_DEVICE_NUMBER). Used by the
    /// generic (non-Steam) UE-game scan to group drives so that partitions on one
    /// physical disk are scanned sequentially while different disks scan in
    /// parallel. Default returns empty so non-Windows implementations and test
    /// doubles need not provide it. Never throws; one unreadable drive is skipped.
    /// </summary>
    IReadOnlyList<DriveDescriptor> GetLogicalDrives() => Array.Empty<DriveDescriptor>();

    /// <summary>
    /// Enumerate running processes as DLL-injection candidates, flagging which look
    /// like UE4/UE5 games (<see cref="GameProcessInfo.IsUe"/>). Default empty for
    /// non-Windows implementations and test doubles. Never throws; inaccessible
    /// processes are skipped.
    /// </summary>
    IReadOnlyList<GameProcessInfo> GetRunningProcesses() => Array.Empty<GameProcessInfo>();

    /// <summary>
    /// Inject a DLL into a running process via CreateRemoteThread + LoadLibraryW.
    /// x64 targets only (rejects Wow64). Default reports unsupported for non-Windows
    /// implementations and test doubles.
    /// </summary>
    InjectResult InjectDll(int pid, string dllPath) =>
        InjectResult.Failure("DLL injection is only supported on Windows.");

    /// <summary>True when the current process runs with Administrator rights.</summary>
    bool IsElevated() => false;

    /// <summary>
    /// Retry injection elevated (relaunch this app headless via UAC to do just the
    /// inject). Used when <see cref="InjectDll"/> returns
    /// <see cref="InjectResult.AccessDenied"/> because the game runs elevated.
    /// </summary>
    InjectResult InjectDllElevated(int pid, string dllPath) =>
        InjectResult.Failure("Elevated injection is only supported on Windows.");

    /// <summary>
    /// Free bytes available on the drive that contains <paramref name="path"/> (the
    /// caller-quota-aware figure, <c>DriveInfo.AvailableFreeSpace</c>). Powers the
    /// snapshot free-space guard. Default <see cref="long.MaxValue"/> so a test
    /// double / non-Windows impl reads as "unlimited" and never blocks a capture;
    /// the real Windows service overrides it. Never throws — an unreadable drive
    /// returns the safe default.
    /// </summary>
    long GetFreeDiskSpaceBytes(string path) => long.MaxValue;

    /// <summary>
    /// Total size in bytes of the drive that contains <paramref name="path"/>
    /// (<c>DriveInfo.TotalSize</c>). Default 0 so the percentage term of the
    /// free-space guard collapses (→ never blocks) when the drive can't be measured;
    /// the real Windows service overrides it. Never throws.
    /// </summary>
    long GetTotalDiskSpaceBytes(string path) => 0;
}
