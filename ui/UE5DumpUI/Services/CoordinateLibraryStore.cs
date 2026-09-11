using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Text.Json;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Persists the Teleport coordinate library PER GAME to
/// %LOCALAPPDATA%\UE5CEDumper\TeleportCoords\teleport-coords.{module}.json.
///
/// Structural clone of <see cref="BookmarkStore"/> — sync, lock-guarded, source-gen
/// JSON, atomic temp+rename, swallow-and-log with empty defaults, and the same
/// <see cref="AppDataFolderMaintenance.Prepare"/> call from the constructor with the
/// sweep OFF — with three deliberate deviations, all documented in
/// docs/teleport-coord-library-spec.md §8:
///
///  1. Keyed by the EXE MODULE NAME, not the PE hash (D1), so a game patch does not
///     orphan a hand-curated list.
///  2. <see cref="CoordinateLibraryJsonContext"/> does NOT use
///     <c>WhenWritingDefault</c>, so a legitimate 0.0 coordinate is written.
///  3. Keeps a rolling <c>.bak</c> of the previous good file, plus two distinct
///     one-shot backups: <see cref="SavePreImportBackup"/> (<c>.preimport.bak</c>)
///     before an import commits, and <see cref="SavePreClearBackup"/>
///     (<c>.preclear.bak</c>) before a "Clear all". This is hand-curated data; a
///     crash-on-write, a botched Replace-import or a mis-clicked clear are by far the
///     worst failure modes in this feature, and they are the ones
///     <see cref="BookmarkStore"/> does not defend against. The rolling <c>.bak</c>
///     cannot stand in for either: the next Save overwrites it.
/// </summary>
public sealed class CoordinateLibraryStore
{
    private readonly string _dir;
    private readonly ILoggingService? _log;
    private readonly object _ioLock = new();

    private static readonly CoordinateLibraryJsonContext s_jsonCtx =
        CoordinateLibraryJsonContext.Default;

    public CoordinateLibraryStore(IPlatformService platform, ILoggingService? log = null)
    {
        _log = log;
        // Own subfolder, not the flat app-data root (audit #5 AF11). This is a
        // per-GAME family — up to four files per module (.json + .bak +
        // .preimport.bak + .preclear.bak, plus at most AtomicFileHygiene.MaxCorruptCopies
        // quarantined .corrupt-* copies), one more set on every new game, forever —
        // and CLAUDE.md's App-data layout rule reserves the root for files that are
        // app-wide and fixed in number. Called from the CONSTRUCTOR for the same
        // reason BookmarkStore does: the store that reads the folder is the one that
        // migrates it, so nothing can read the old location after the move.
        //
        // maxAgeDays: 0 — NO age sweep, and this is the clearer case of the two the
        // rule already covers. Snapshots\ is swept because a snapshot is a regenerable
        // multi-GB capture; Bookmarks\ is not, because it is hand-placed navigation.
        // A coordinate library is the SAME kind of data as a bookmark and then some:
        // hand-authored, labelled and grouped by the user, importable from a friend's
        // paste, and unrecoverable if deleted — which is exactly why this store keeps
        // three separate backup flavours (see the class doc). Sweeping it would delete
        // the one thing here nobody can replay their way back to.
        _dir = AppDataFolderMaintenance.Prepare(
            Path.Combine(platform.GetAppDataPath(), Constants.LogFolderName),
            Constants.CoordLibrarySubFolder,
            Constants.CoordLibraryFilePrefix,
            maxAgeDays: 0,
            log);
    }

    /// <summary>
    /// Turn a raw module name ("MyGame-Win64-Shipping.exe") into a file-name-safe key.
    /// Case-insensitive by lowering, so a differently-cased report of the same module
    /// resolves to the same file. Returns "" for an unusable module name, which makes
    /// every store operation a no-op.
    /// </summary>
    public static string KeyFor(string? moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName)) return "";
        var name = moduleName!.Trim();
        // Drop a trailing .exe/.dll so "MyGame.exe" and "MyGame" agree.
        int dot = name.LastIndexOf('.');
        if (dot > 0 &&
            (name.AsSpan(dot).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
             name.AsSpan(dot).Equals(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            name = name[..dot];
        }

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_'
                ? char.ToLowerInvariant(c)
                : '_');
        }
        var key = sb.ToString().Trim('_');
        return key.Length > 96 ? key[..96] : key;
    }

    private string PathFor(string key) =>
        Path.Combine(_dir, $"{Constants.CoordLibraryFilePrefix}.{key}.json");

    /// <summary>File path for a game's library (testing/diagnostics).</summary>
    public string FilePathFor(string key) => PathFor(key);

    /// <summary>
    /// Load a game's library, or a fresh empty file on missing / unreadable / corrupt.
    /// Returns an empty file (no-op) when <paramref name="key"/> is empty.
    ///
    /// On a corrupt main file the <c>.bak</c> is tried before giving up — the whole
    /// point of keeping it. ONLY on a corrupt one: a MISSING main file is "Clear all" (which
    /// deletes it and deliberately keeps the backups), and falling back there resurrected the
    /// cleared library on every connect and restart. [A1-COORD-RESURRECT]
    /// </summary>
    public CoordinateLibraryFile Load(string key)
    {
        if (string.IsNullOrEmpty(key)) return new CoordinateLibraryFile();
        lock (_ioLock)
        {
            var path = PathFor(key);
            TryDeleteStaleTemp(path);

            var loaded = TryRead(path);
            if (loaded != null) return loaded;

            // [A1-COORD-RESURRECT] Missing is not unreadable -- see the summary.
            var fromBak = File.Exists(path) ? TryRead(path + ".bak") : null;
            if (fromBak != null)
            {
                _log?.Warn(Constants.LogCatView,
                    $"CoordinateLibraryStore: {key} main file unreadable, recovered from .bak " +
                    $"({fromBak.Entries.Count} entries)");
                return fromBak;
            }

            return new CoordinateLibraryFile { Module = key };
        }
    }

    private CoordinateLibraryFile? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize(json, s_jsonCtx.CoordinateLibraryFile);
            if (file == null) return null;
            file.Entries ??= new List<CoordEntry>();
            return file;
        }
        catch (Exception ex)
        {
            _log?.Warn(Constants.LogCatView,
                $"CoordinateLibraryStore: failed to read {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Persist a game's library: atomic temp + rename, rolling the previous good file
    /// to <c>.bak</c> first. No-op on an empty key.
    /// </summary>
    public void Save(string key, CoordinateLibraryFile file)
    {
        if (string.IsNullOrEmpty(key)) return;
        lock (_ioLock)
        {
            try
            {
                file.Module = key;
                var json = JsonSerializer.Serialize(file, s_jsonCtx.CoordinateLibraryFile);
                var path = PathFor(key);
                var temp = path + ".tmp";

                File.WriteAllText(temp, json);
                // Roll the previous good file aside BEFORE the rename, so a torn write
                // never leaves us with neither -- but only a main file that PARSES. After a
                // .bak recovery the main on disk is still the corrupt one, and rolling it would
                // overwrite the only good copy with garbage. [A1-COORD-BACKUP]
                if (TryRead(path) != null)
                    TryRollToBackup(path, path + ".bak");
                else if (File.Exists(path))
                    // Unparseable: not rolled over the good .bak, and not destroyed by the rename
                    // below either -- COPIED aside, and bounded. The bound follows AobUsageService's;
                    // the copy does not: AobUsageService MOVES its file, and this store copies for the
                    // reason QuarantineUnparseableMain gives. A copy that fails throws into the catch,
                    // so this Save is refused rather than overwriting the only copy of whatever that
                    // file still holds. (reviews of 2f8d36f8, c002f6bf, b496c866)
                    QuarantineUnparseableMain(key, path);
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                _log?.Error(Constants.LogCatView, "CoordinateLibraryStore: failed to save", ex);
            }
        }
    }

    /// <summary>
    /// Snapshot the library to <c>.preimport.bak</c>. Called immediately before an import
    /// commits so a botched Replace is recoverable — distinct from the rolling <c>.bak</c>,
    /// which the very next Save would overwrite. Returns the backup path, or "" when nothing
    /// was backed up.
    ///
    /// <para>Written from <paramref name="current"/> -- the library the user is looking at --
    /// not copied from disk: after a <c>.bak</c> recovery the file on disk is still the corrupt
    /// one, and the copy backed up garbage. [A1-COORD-BACKUP]</para>
    /// </summary>
    public string SavePreImportBackup(string key, CoordinateLibraryFile current)
    {
        if (string.IsNullOrEmpty(key)) return "";
        lock (_ioLock)
        {
            return TryWriteOneShotBackup(key, current, PathFor(key) + ".preimport.bak");
        }
    }

    /// <summary>
    /// Snapshot the current on-disk library to <c>.preclear.bak</c>. Called immediately
    /// before <see cref="Delete"/> so a mis-clicked "Clear all" is recoverable.
    /// Returns the backup path, or "" when nothing was backed up.
    ///
    /// A DISTINCT file from both the rolling <c>.bak</c> and <c>.preimport.bak</c> on
    /// purpose: the rolling one is overwritten by the very next Save — and
    /// <c>OnCoordZToleranceChanged</c> saves on every spinner nudge — so after a clear
    /// it survives about two clicks. This one is only ever written by a clear.
    /// </summary>
    public string SavePreClearBackup(string key, CoordinateLibraryFile current)
    {
        if (string.IsNullOrEmpty(key)) return "";
        lock (_ioLock)
        {
            // From the IN-MEMORY library, like SavePreImportBackup. [A1-COORD-BACKUP]
            return TryWriteOneShotBackup(key, current, PathFor(key) + ".preclear.bak");
        }
    }

    /// <summary>Delete a game's library file (user "clear all"). Leaves the backups.</summary>
    public void Delete(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        lock (_ioLock)
        {
            try
            {
                var path = PathFor(key);
                // Roll a main that PARSES to .bak first, as Save does. Whenever Load had to recover
                // from .bak (a sharing violation reads as unreadable) the in-memory library the
                // pre-clear backup was written from is OLDER than this file, and the newest revision
                // then ended up in no file at all. (review of 2f8d36f8)
                if (TryRead(path) != null && !TryRollToBackup(path, path + ".bak"))
                {
                    // The roll exists to keep this revision; deleting it anyway loses exactly that.
                    // Refused: the next Load shows the library again. (review of 70f9d372)
                    _log?.Warn(Constants.LogCatView,
                        $"CoordinateLibraryStore: {key} could not be backed up to .bak, NOT deleted");
                    return;
                }
                if (File.Exists(path))
                {
                    // An unparseable main is not rolled (that would put garbage over the good .bak), and
                    // deleting it outright lost whatever it still held -- the destruction Save stopped
                    // doing. Copied aside first, as Save does; a copy that fails throws into the catch and
                    // refuses the Delete. (review of c002f6bf)
                    if (TryRead(path) == null) QuarantineUnparseableMain(key, path);
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _log?.Error(Constants.LogCatView, "CoordinateLibraryStore: failed to delete", ex);
            }
        }
    }

    /// <summary>
    /// Write <paramref name="current"/> to a one-shot backup, atomically (temp + rename). An empty
    /// library has nothing to protect, so it writes nothing and returns "". [A1-COORD-BACKUP]
    /// </summary>
    private string TryWriteOneShotBackup(string key, CoordinateLibraryFile? current, string bak)
    {
        if (current == null || current.Entries.Count == 0) return "";
        try
        {
            var copy = new CoordinateLibraryFile
            {
                Module = key,
                Entries = current.Entries.ToList(),
                ZTolerance = current.ZTolerance,
            };
            var temp = bak + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(copy, s_jsonCtx.CoordinateLibraryFile));
            File.Move(temp, bak, overwrite: true);
            return bak;
        }
        catch (Exception ex)
        {
            _log?.Warn(Constants.LogCatView,
                $"CoordinateLibraryStore: backup to {Path.GetFileName(bak)} failed: {ex.Message}");
            return "";
        }
    }

    /// <summary>COPY an unparseable main file aside as <c>&lt;file&gt;.corrupt-&lt;stamp&gt;</c> and keep at
    /// most <see cref="AtomicFileHygiene.MaxCorruptCopies"/> of them. The copy shares the game's key,
    /// so it moves and expires with the game's group. The copy is deliberately unguarded: see Save.
    /// A copy, not a move: moved first, a rename that then failed left NO main at all, which Load
    /// reads as a Clear all. (review of 70f9d372)</summary>
    private void QuarantineUnparseableMain(string key, string path)
    {
        var dir   = Path.GetDirectoryName(path)!;
        var name  = Path.GetFileName(path);
        var aside = Path.Combine(dir, AtomicFileHygiene.QuarantineNameFor(name, DateTime.UtcNow));
        File.Copy(path, aside, overwrite: false);
        _log?.Warn(Constants.LogCatView,
            $"CoordinateLibraryStore: {key} main file unreadable, copied aside to " +
            $"{Path.GetFileName(aside)}; the .bak is untouched");
        try
        {
            // The copy just made is never a prune candidate (a future-stamped copy would outrank it),
            // and it still counts toward the cap: hence the -1, as in AobUsageService.
            var fresh = Path.GetFileName(aside);
            var names = Directory.EnumerateFiles(dir, AtomicFileHygiene.CorruptPrefixFor(name) + "*")
                                 .Select(Path.GetFileName)
                                 .Where(n => !string.IsNullOrEmpty(n)
                                             && !string.Equals(n, fresh, StringComparison.OrdinalIgnoreCase))
                                 .Select(n => n!);
            foreach (var stale in AtomicFileHygiene.SelectCorruptCopiesToPrune(
                         names, name, AtomicFileHygiene.MaxCorruptCopies - 1))
                File.Delete(Path.Combine(dir, stale));
        }
        catch (Exception ex)
        {
            _log?.Warn(Constants.LogCatView,
                $"CoordinateLibraryStore: pruning old corrupt copies failed: {ex.Message}");
        }
    }

    private bool TryRollToBackup(string path, string bak)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Copy(path, bak, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            _log?.Warn(Constants.LogCatView,
                $"CoordinateLibraryStore: backup to {Path.GetFileName(bak)} failed: {ex.Message}");
            return false;
        }
    }

    private static void TryDeleteStaleTemp(string path)
    {
        try { var t = path + ".tmp"; if (File.Exists(t)) File.Delete(t); }
        catch { /* best-effort */ }
    }
}
