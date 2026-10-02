using System.Text.Json.Serialization;

namespace UE5DumpUI.Models;

// ---------------------------------------------------------------------------
// DTOs for parsing a "Dump All" JSON-Lines file (produced by
// Services.DumpAllService). One JSON object per line, keyed by "kind":
//   meta     — first line (engine/module/version header)
//   class    — one per class-like object: name/addr/path/super/props/funcs
//   struct   — same shape as class, for a ScriptStruct or UserDefinedStruct
//   enum     — name/addr/path plus entries
//   instance — live object index; the explorer skips these before deserialize
//   error    — a walk that failed (ignored by the browser)
//   summary  — last line (counters, ignored)
//
// System.Text.Json ignores unknown properties, so every line can be probed by
// deserializing into DumpClassLine (which captures "kind" + the class fields);
// the single "meta" line is re-read into DumpMetaLine for the header. All
// deserialization goes through the source-generated DumpJsonlContext — the UI
// is Native-AOT / trimmed, so reflection-based JSON is unavailable.
// ---------------------------------------------------------------------------

/// <summary>A "class" line: a class-like UObject plus its properties and functions.</summary>
public sealed class DumpClassLine
{
    [JsonPropertyName("kind")]  public string Kind  { get; set; } = "";
    [JsonPropertyName("name")]  public string Name  { get; set; } = "";
    [JsonPropertyName("addr")]  public string Addr  { get; set; } = "";
    [JsonPropertyName("path")]  public string Path  { get; set; } = "";
    [JsonPropertyName("meta")]  public string Meta  { get; set; } = "";
    [JsonPropertyName("super")] public string Super { get; set; } = "";
    [JsonPropertyName("props")] public List<DumpPropLine>? Props { get; set; }
    [JsonPropertyName("funcs")] public List<DumpFuncLine>? Funcs { get; set; }
}

/// <summary>One property inside a class line.</summary>
public sealed class DumpPropLine
{
    [JsonPropertyName("name")]              public string  Name            { get; set; } = "";
    [JsonPropertyName("type")]              public string  Type            { get; set; } = "";
    [JsonPropertyName("offset")]            public int     Offset          { get; set; }
    [JsonPropertyName("size")]              public int     Size            { get; set; }
    [JsonPropertyName("struct_type")]       public string? StructType      { get; set; }
    [JsonPropertyName("inner_type")]        public string? InnerType       { get; set; }
    [JsonPropertyName("inner_struct_type")] public string? InnerStructType { get; set; }
    [JsonPropertyName("obj_class")]         public string? ObjClass        { get; set; }
    [JsonPropertyName("enum")]              public string? Enum            { get; set; }
}

/// <summary>One function inside a class line.</summary>
public sealed class DumpFuncLine
{
    [JsonPropertyName("name")]        public string Name       { get; set; } = "";
    [JsonPropertyName("addr")]        public string Addr       { get; set; } = "";
    [JsonPropertyName("return_type")] public string ReturnType { get; set; } = "";
    [JsonPropertyName("num_parms")]   public int    NumParms   { get; set; }
    [JsonPropertyName("params")]      public List<DumpFuncParamLine>? Params { get; set; }
}

/// <summary>One parameter of a dumped function. Members of a struct param are not
/// inlined; <see cref="StructType"/> is the join to that struct.</summary>
public sealed class DumpFuncParamLine
{
    [JsonPropertyName("name")]        public string  Name       { get; set; } = "";
    [JsonPropertyName("type")]        public string  Type       { get; set; } = "";
    [JsonPropertyName("offset")]      public int     Offset     { get; set; }
    [JsonPropertyName("size")]        public int     Size       { get; set; }
    [JsonPropertyName("struct_type")] public string? StructType { get; set; }
    [JsonPropertyName("obj_class")]   public string? ObjClass   { get; set; }
}

/// <summary>The "meta" header line — surfaced in the browser header bar.</summary>
public sealed class DumpMetaLine
{
    [JsonPropertyName("kind")]         public string Kind        { get; set; } = "";
    [JsonPropertyName("ue_version")]   public int    UeVersion   { get; set; }
    [JsonPropertyName("module")]       public string Module      { get; set; } = "";
    [JsonPropertyName("gobjects")]     public string GObjects    { get; set; } = "";
    [JsonPropertyName("gworld")]       public string GWorld      { get; set; } = "";
    [JsonPropertyName("object_count")] public int    ObjectCount { get; set; }
    [JsonPropertyName("pe_hash")]      public string PeHash      { get; set; } = "";
    [JsonPropertyName("dumped_at")]    public string DumpedAt    { get; set; } = "";
    [JsonPropertyName("dumper_build")] public int    DumperBuild { get; set; }
}

/// <summary>One enumerator. <c>n</c>/<c>v</c> are the names <c>list_enums</c> already uses.</summary>
public sealed class DumpEnumEntryLine
{
    [JsonPropertyName("n")] public string N { get; set; } = "";
    [JsonPropertyName("v")] public long    V { get; set; }
}

/// <summary>A <c>kind:enum</c> line.</summary>
public sealed class DumpEnumLine
{
    [JsonPropertyName("kind")]    public string Kind { get; set; } = "";
    [JsonPropertyName("name")]    public string Name { get; set; } = "";
    [JsonPropertyName("addr")]    public string Addr { get; set; } = "";
    [JsonPropertyName("path")]    public string Path { get; set; } = "";
    [JsonPropertyName("meta")]    public string Meta { get; set; } = "";
    [JsonPropertyName("entries")] public List<DumpEnumEntryLine>? Entries { get; set; }
}

[JsonSerializable(typeof(DumpClassLine))]
[JsonSerializable(typeof(DumpMetaLine))]
[JsonSerializable(typeof(DumpEnumLine))]
/// <summary>Source-generated JSON context (AOT/trimming — reflection JSON is disabled).</summary>
internal partial class DumpJsonlContext : JsonSerializerContext
{
}

// ---------------------------------------------------------------------------
// Flattened, searchable view of the dump.
// ---------------------------------------------------------------------------

/// <summary>Category of a flattened dump row.</summary>
public enum DumpEntryKind
{
    Class,
    Property,
    Function,
    Struct,
    Enum,
    Enumerator,
}

/// <summary>
/// One flattened, searchable metadata item — a class, struct, or enum, or one
/// member of it. Members carry their owning type's
/// <see cref="Path"/> and <see cref="ClassAddr"/> so a single per-class live
/// match (see DumpExplorerViewModel) classifies the whole family and every row
/// can jump to the live class object.
///
/// <see cref="IsMatched"/> / <see cref="LiveAddr"/> are the only mutable fields —
/// set during the live-match pass; everything else is fixed at parse time.
/// </summary>
public sealed class DumpEntry
{
    public DumpEntryKind Kind { get; init; }

    /// <summary>Class / property / function name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Owning class name (empty for a class, struct, or enum row).</summary>
    public string OwnerClass { get; init; } = "";

    /// <summary>GObjects class name of the owning type (<c>Class</c>, <c>ScriptStruct</c>,
    /// <c>Enum</c>, …). Find Instances navigates only when this is class-like.</summary>
    public string OwnerMeta { get; init; } = "";

    /// <summary>The owning type's SHORT name — the row's live-match key. For a
    /// class, struct, or enum row that's its own <see cref="Name"/>; for a member
    /// it's the <see cref="OwnerClass"/>. Matched by name (not full path) because the
    /// live object list (get_object_list) only exposes the short FName — see
    /// DumpExplorerViewModel.BuildLiveClassIndexAsync.</summary>
    public string OwningClassName =>
        Kind is DumpEntryKind.Class or DumpEntryKind.Struct or DumpEntryKind.Enum
            ? Name
            : OwnerClass;

    /// <summary>Type summary: property type (+ inner/struct/enum), function
    /// return type + parm count, or a class's super name.</summary>
    public string TypeInfo { get; init; } = "";

    /// <summary>Property offset within its class; -1 for class/function rows.</summary>
    public int Offset { get; init; } = -1;

    /// <summary>Object path of the owning class (display + Copy path only — NOT
    /// the match key; see <see cref="OwningClassName"/>).</summary>
    public string Path { get; init; } = "";

    /// <summary>The class object's address AS RECORDED in the dump (dump-time).</summary>
    public string ClassAddr { get; init; } = "";

    /// <summary>Native code address of a function row (reference only — not a UObject).</summary>
    public string FuncAddr { get; init; } = "";

    /// <summary>Lower-cased search haystack (name + owner + type + path), precomputed.</summary>
    public string Haystack { get; init; } = "";

    // --- Live-match state (mutable) ---

    /// <summary>True when the owning class resolves to a live object in the
    /// currently-connected game.</summary>
    public bool IsMatched { get; set; }

    /// <summary>The owning class's CURRENT live address (jump target); empty when unmatched.</summary>
    public string LiveAddr { get; set; } = "";

    // --- Display helpers ---

    public string KindLabel => Kind switch
    {
        DumpEntryKind.Class       => "Class",
        DumpEntryKind.Property    => "Prop",
        DumpEntryKind.Function    => "Func",
        DumpEntryKind.Struct      => "Struct",
        DumpEntryKind.Enum        => "Enum",
        DumpEntryKind.Enumerator  => "Enum",
        _                         => "",
    };

    public string OffsetDisplay => Offset >= 0 ? $"0x{Offset:X}" : "";
}

/// <summary>Parsed dump: the meta header plus the flattened searchable corpus.</summary>
public sealed class DumpFileModel
{
    public DumpMetaLine? Meta { get; init; }
    public List<DumpEntry> Entries { get; init; } = new();
    public int ClassCount { get; init; }
    public int PropertyCount { get; init; }
    public int FunctionCount { get; init; }
}
