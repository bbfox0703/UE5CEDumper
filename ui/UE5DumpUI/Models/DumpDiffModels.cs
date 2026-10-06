using System.Text.Json;
using System.Text.Json.Serialization;

namespace UE5DumpUI.Models;

// ---------------------------------------------------------------------------
// [DUMPDIFF-UI] Dump Explorer's "Compare": the C# port of scripts/analysis/diff_dumps.py, which stays the reference.
// Its DTOs read only the keys the diff compares, and every one is nullable where the script tells "absent" from
// "empty" (a missing key and "" are different values to it): a port that defaulted them would report changes the
// script does not. scripts/analysis/fixtures/diff_dumps/ holds the cases both must agree on.
// ---------------------------------------------------------------------------

/// <summary>One line of a Dump All file as the diff reads it: the union of the keys it compares across the meta,
/// class, struct, enum, error and summary lines.</summary>
public sealed class DumpDiffLine
{
    [JsonPropertyName("kind")]       public string? Kind { get; set; }
    [JsonPropertyName("name")]       public string? Name { get; set; }
    [JsonPropertyName("path")]       public string? Path { get; set; }
    [JsonPropertyName("props_size")] public long? PropsSize { get; set; }
    [JsonPropertyName("props")]      public List<DumpDiffProp>? Props { get; set; }
    [JsonPropertyName("funcs")]      public List<DumpDiffFunc>? Funcs { get; set; }
    [JsonPropertyName("entries")]    public List<DumpDiffEnumEntry>? Entries { get; set; }

    // meta
    [JsonPropertyName("module")]       public string? Module { get; set; }
    [JsonPropertyName("ue_version")]   public long? UeVersion { get; set; }
    [JsonPropertyName("dumper_build")] public long? DumperBuild { get; set; }
    [JsonPropertyName("dumped_at")]    public string? DumpedAt { get; set; }
    [JsonPropertyName("file")]         public string? File { get; set; }
    [JsonPropertyName("class_dump")]   public string? ClassDump { get; set; }

    // summary: the script tests some of these for PRESENCE, so they stay null when absent
    [JsonPropertyName("structs_emitted")]       public long? StructsEmitted { get; set; }
    [JsonPropertyName("enums_emitted")]         public long? EnumsEmitted { get; set; }
    [JsonPropertyName("enums_listed")]          public bool? EnumsListed { get; set; }
    [JsonPropertyName("enum_names_failed")]     public bool? EnumNamesFailed { get; set; }
    [JsonPropertyName("enums_truncated")]       public bool? EnumsTruncated { get; set; }
    [JsonPropertyName("params_from_num_parms")] public long? ParamsFromNumParms { get; set; }
}

/// <summary>A property of a class or struct line.</summary>
public sealed class DumpDiffProp
{
    [JsonPropertyName("name")]        public string? Name { get; set; }
    [JsonPropertyName("type")]        public string? Type { get; set; }
    [JsonPropertyName("offset")]      public long? Offset { get; set; }
    [JsonPropertyName("size")]        public long? Size { get; set; }
    [JsonPropertyName("inner_type")]  public string? InnerType { get; set; }
    [JsonPropertyName("struct_type")] public string? StructType { get; set; }
    [JsonPropertyName("obj_class")]   public string? ObjClass { get; set; }
    [JsonPropertyName("enum")]        public string? Enum { get; set; }
}

/// <summary>A function of a class line. <see cref="Params"/> is null in a dump from before build 3622, which is not
/// the same as a function with none.</summary>
public sealed class DumpDiffFunc
{
    [JsonPropertyName("name")]        public string? Name { get; set; }
    [JsonPropertyName("return_type")] public string? ReturnType { get; set; }
    [JsonPropertyName("num_parms")]   public long? NumParms { get; set; }
    [JsonPropertyName("parms_size")]  public long? ParmsSize { get; set; }
    /// <summary>Written as a "0x…" string; read as text whatever its JSON type, since the diff only compares it.</summary>
    [JsonPropertyName("flags")]
    [JsonConverter(typeof(DumpDiffLenientStringConverter))]
    public string? Flags { get; set; }
    [JsonPropertyName("params")]      public List<DumpDiffParam>? Params { get; set; }
}

/// <summary>A parameter of a dumped function, the return included.</summary>
public sealed class DumpDiffParam
{
    [JsonPropertyName("name")]        public string? Name { get; set; }
    [JsonPropertyName("type")]        public string? Type { get; set; }
    [JsonPropertyName("struct_type")] public string? StructType { get; set; }
    [JsonPropertyName("obj_class")]   public string? ObjClass { get; set; }
    [JsonPropertyName("out")]         public bool? Out { get; set; }
    [JsonPropertyName("ret")]         public bool? Ret { get; set; }
    [JsonPropertyName("offset")]      public long? Offset { get; set; }
    [JsonPropertyName("size")]        public long? Size { get; set; }
}

/// <summary>An enumerator of an enum line.</summary>
public sealed class DumpDiffEnumEntry
{
    [JsonPropertyName("name")]  public string? Name { get; set; }
    [JsonPropertyName("value")] public long? Value { get; set; }
}

/// <summary>Reads a string, or any other JSON value as its raw text.</summary>
public sealed class DumpDiffLenientStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.String) return reader.GetString();
        using var doc = JsonDocument.ParseValue(ref reader);
        return doc.RootElement.GetRawText();
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

/// <summary>Source-generated JSON context (AOT/trimming — reflection JSON is disabled).</summary>
[JsonSerializable(typeof(DumpDiffLine))]
internal partial class DumpDiffJsonContext : JsonSerializerContext
{
}

/// <summary>A Dump All file loaded for the diff.</summary>
public sealed class DumpDiffInput
{
    public string FilePath { get; init; } = "";
    public DumpDiffLine Meta { get; set; } = new();
    public List<DumpDiffLine> Classes { get; } = new();
    public List<DumpDiffLine> Structs { get; } = new();
    public List<DumpDiffLine> Enums { get; } = new();
    public List<DumpDiffLine> Errors { get; } = new();
    public DumpDiffLine Summary { get; set; } = new();
    /// <summary>Every Dump All file ends with its summary line, so a file without one was cut off mid-write: what it
    /// lacks may simply be what it never reached.</summary>
    public bool HasSummary { get; set; }
    /// <summary>Lines that were not JSON, skipped as the script skips them.</summary>
    public int BadLines { get; set; }

    public long DumperBuild => Meta.DumperBuild ?? 0;
    public long UeVersion => Meta.UeVersion ?? 0;

    /// <summary>The file carries struct lines, or a summary from a build that writes them.</summary>
    public bool CarriesStructs => Structs.Count > 0 || Summary.StructsEmitted.HasValue;
    public bool CarriesEnums => Enums.Count > 0 || Summary.EnumsEmitted.HasValue || Summary.EnumsListed.HasValue;
    public bool EnumsListed => Summary.EnumsListed ?? true;
    public bool EnumNamesFailed => Summary.EnumNamesFailed ?? false;
    public bool EnumsTruncated => Summary.EnumsTruncated ?? false;
    public long ParamsFromNumParms => Summary.ParamsFromNumParms ?? 0;
}

/// <summary>Raised when the file is Dump All's object index, which lists objects, not types: diffed as a class dump
/// it would report every class removed.</summary>
public sealed class DumpDiffObjectIndexException : Exception
{
    public string ClassDump { get; }
    public DumpDiffObjectIndexException(string fileName, string classDump)
        : base($"{fileName} is Dump All's object index, not a class dump") => ClassDump = classDump;
}

public enum DumpDiffPropKind { Added, Removed, Moved, TypeChanged }
public enum DumpDiffFuncKind { Added, Removed, SignatureChanged }
public enum DumpDiffEnumEntryKind { Added, Removed, ValueChanged }

public sealed record DumpDiffPropChange(string Name, DumpDiffPropKind Kind, DumpDiffProp? Old, DumpDiffProp? New);
public sealed record DumpDiffFuncChange(string Name, DumpDiffFuncKind Kind, DumpDiffFunc? Old, DumpDiffFunc? New);
public sealed record DumpDiffEnumEntryChange(string Name, DumpDiffEnumEntryKind Kind, long? OldValue, long? NewValue);

/// <summary>A class or struct present in both dumps that changed.</summary>
public sealed class DumpDiffTypeChange
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public DumpDiffLine Old { get; init; } = new();
    public DumpDiffLine New { get; init; } = new();
    public List<DumpDiffPropChange> PropChanges { get; init; } = new();
    public List<DumpDiffFuncChange> FuncChanges { get; init; } = new();
    /// <summary>A struct's size is the stride of every array and map holding it, so its growth alone breaks a table.</summary>
    public bool IsStruct { get; init; }

    public long OldSize => Old.PropsSize ?? 0;
    public long NewSize => New.PropsSize ?? 0;
    public long PropsSizeDelta => NewSize - OldSize;

    /// <summary>What breaks a working cheat table: a moved or retyped field, a changed signature, or a struct whose
    /// size changed. An added or removed member does not move the others.</summary>
    public bool HasBreakingChange
    {
        get
        {
            if (IsStruct && PropsSizeDelta != 0) return true;
            foreach (var pc in PropChanges)
                if (pc.Kind is DumpDiffPropKind.Moved or DumpDiffPropKind.TypeChanged) return true;
            foreach (var fc in FuncChanges)
                if (fc.Kind == DumpDiffFuncKind.SignatureChanged) return true;
            return false;
        }
    }
}

/// <summary>An enum present in both dumps whose enumerators changed.</summary>
public sealed class DumpDiffEnumChange
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public List<DumpDiffEnumEntryChange> Changes { get; init; } = new();
    /// <summary>A table that writes an enum's value breaks when the value moves; an enumerator added or removed does
    /// not move the others.</summary>
    public bool HasBreakingChange => Changes.Exists(c => c.Kind == DumpDiffEnumEntryKind.ValueChanged);
}

/// <summary>The change counts the report's summary draws on, per kind of type.</summary>
public sealed class DumpDiffCounts
{
    public int PropAdded { get; set; }
    public int PropRemoved { get; set; }
    public int PropMoved { get; set; }
    public int PropTypeChanged { get; set; }
    public int FuncAdded { get; set; }
    public int FuncRemoved { get; set; }
    public int FuncSignatureChanged { get; set; }
    public int TypesWithMovedFields { get; set; }
    public int TypesWithSigChanges { get; set; }
    public int TypesWithSizeDelta { get; set; }
}

/// <summary>The diff of two dumps. A *Skipped reason is "" when that kind was compared.</summary>
public sealed class DumpDiffResult
{
    public DumpDiffInput OldDump { get; init; } = new();
    public DumpDiffInput NewDump { get; init; } = new();
    public bool IncludeEngine { get; init; }

    public List<DumpDiffLine> AddedClasses { get; set; } = new();
    public List<DumpDiffLine> RemovedClasses { get; set; } = new();
    public List<DumpDiffTypeChange> ChangedClasses { get; set; } = new();
    public int UnchangedClasses { get; set; }

    public List<DumpDiffLine> AddedStructs { get; set; } = new();
    public List<DumpDiffLine> RemovedStructs { get; set; } = new();
    public List<DumpDiffTypeChange> ChangedStructs { get; set; } = new();
    public int UnchangedStructs { get; set; }
    public string StructsSkipped { get; set; } = "";

    public List<DumpDiffLine> AddedEnums { get; set; } = new();
    public List<DumpDiffLine> RemovedEnums { get; set; } = new();
    public List<DumpDiffEnumChange> ChangedEnums { get; set; } = new();
    public int UnchangedEnums { get; set; }
    /// <summary>Enums on both sides whose enumerators could not be compared (no member names on one side).</summary>
    public int UncomparedEnums { get; set; }
    public string EnumsSkipped { get; set; } = "";

    /// <summary>What a whole dump cannot say: cut off before its summary, or carrying error lines.</summary>
    public List<string> DumpNotes { get; } = new();
    public List<string> EnumNotes { get; } = new();
    public List<string> ParamNotes { get; } = new();
}
