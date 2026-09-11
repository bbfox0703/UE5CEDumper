using System.Text.Json.Serialization;

namespace UE5DumpUI.Models;

/// <summary>
/// Persisted Live-Walker bookmarks for ONE game, keyed by PE hash. Stored as
/// %LOCALAPPDATA%\UE5CEDumper\Bookmarks\bookmarks.{peHash}.json so each game's bookmarks
/// are isolated and auto-follow the game (same per-game-file convention as the snapshot
/// DB / denylist). Never aged out, unlike the snapshot DBs — see
/// <see cref="Services.BookmarkStore"/>.
///
/// What's persisted = the navigation SPINE (breadcrumb field name+offset+class, the
/// saved address as a same-process fast-path hint), the selected field rows, and the
/// scroll anchor. NOT persisted: the live <c>ContainerField</c> / cached world walk
/// (session objects with volatile addresses) — on reload those are rebuilt or re-walked.
///
/// Addresses ARE stored as a same-process fast-path hint: they stay valid when the UI is
/// restarted while the SAME game process keeps running (ASLR is per-process, not per-PE).
/// After a game RESTART they go stale. On load the spine is RE-RESOLVED from a live anchor
/// (GWorld / GameEngine) by re-walking the stable field name+offset chain, which
/// reconstructs fresh addresses so the bookmark still lands on the right object after a
/// restart (see <c>LiveWalkerViewModel.TryReresolveBookmarkSpineAsync</c>). When
/// re-resolution can't re-anchor (non-GWorld root / a hop no longer matches), the load
/// path validates the walked class against <c>SavedClassName</c> and degrades to a
/// "stale — re-create" message rather than showing wrong data, never auto-clearing it.
/// </summary>
public sealed class BookmarkFile
{
    /// <summary>Schema version for future migrations (v1 = initial).</summary>
    public int Version { get; set; } = 1;

    /// <summary>The game's PE hash (informational; the file name is the real key).</summary>
    public string PeHash { get; set; } = "";

    /// <summary>Occupied slots only (sparse — each carries its SlotIndex).</summary>
    public List<PersistedBookmark> Slots { get; set; } = new();
}

/// <summary>One saved bookmark slot.</summary>
public sealed class PersistedBookmark
{
    public int SlotIndex { get; set; }
    public string Label { get; set; } = "";
    public string SavedObjectName { get; set; } = "";
    public string SavedClassName { get; set; } = "";
    public string SavedAddress { get; set; } = "";
    public string SavedClassAddr { get; set; } = "";
    public List<PersistedCrumb> Breadcrumbs { get; set; } = new();
    public List<PersistedFieldRef> SelectedFields { get; set; } = new();
    public PersistedFieldRef? TopRow { get; set; }
}

/// <summary>A flattened breadcrumb hop (re-resolvable parts only).</summary>
public sealed class PersistedCrumb
{
    public string Address { get; set; } = "";
    public string Label { get; set; } = "";
    public string ClassAddr { get; set; } = "";
    public int FieldOffset { get; set; }
    public string FieldName { get; set; } = "";
    public string TargetClassName { get; set; } = "";
    public bool IsPointerDeref { get; set; }
    public bool IsContainerView { get; set; }
    /// <summary>[W4-BOOKMARK-DT] A DataTable row view. Its rows are live-only and never persisted, so the
    /// load path re-walks them; without this flag the restored crumb looked like a plain container and the
    /// restore failed as "the game may have restarted". Absent in older files, where it reads false.</summary>
    public bool IsDataTableView { get; set; }
}

/// <summary>A field row reference (name + byte offset) for re-selection / scroll restore.</summary>
public sealed class PersistedFieldRef
{
    public string Name { get; set; } = "";
    public int Offset { get; set; }
}

/// <summary>
/// Source-generated JSON context (AOT/trimming — reflection JSON is disabled).
/// All nested list/sub-object types are reachable from the root.
/// </summary>
[JsonSerializable(typeof(BookmarkFile))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class BookmarkJsonContext : JsonSerializerContext
{
}
