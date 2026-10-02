using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UE5DumpUI.Models;

namespace UE5DumpUI.Helpers;

/// <summary>
/// One JSON object per line for the Live Funcs table the user is looking at.
/// Not a Dump All file: the explorer and the analysis scripts ignore an unknown kind.
/// </summary>
public static class LiveFuncsJsonl
{
    public readonly record struct Header(
        int Fetched,
        int Distinct,
        bool Recording,
        bool Diff,
        bool Truncated,
        bool BaselinePartial,
        string Filter,
        int FetchLimit,
        int MinCalls);

    public static string Format(IReadOnlyList<PeProfileEntry> rows, Header header)
    {
        var sb = new StringBuilder();
        var ctx = LiveFuncsJsonlContext.Default;
        sb.Append(JsonSerializer.Serialize(new LiveFuncsJsonlMeta
        {
            Kind = "live_funcs",
            Rows = rows.Count,
            Fetched = header.Fetched,
            Distinct = header.Distinct,
            Recording = header.Recording,
            Diff = header.Diff,
            Truncated = header.Truncated,
            BaselinePartial = header.BaselinePartial,
            Filter = header.Filter ?? "",
            FetchLimit = header.FetchLimit,
            MinCalls = header.MinCalls,
        }, ctx.LiveFuncsJsonlMeta));
        sb.Append('\n');
        foreach (var e in rows)
        {
            sb.Append(JsonSerializer.Serialize(RowFrom(e), ctx.LiveFuncsJsonlRow));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static LiveFuncsJsonlRow RowFrom(PeProfileEntry e) => new()
    {
        Kind = "func",
        Class = e.ClassName,
        Func = e.FuncName,
        Addr = e.FuncAddr,
        Calls = e.Count,
        Order = e.FirstSeq,
        Delta = e.Delta,
        IsNew = e.IsNew,
        Badge = e.Kind,
        Type = e.TypeLabel,
        NumParms = e.NumParms,
        ParmsSize = e.ParmsSize,
        Flags = e.FunctionFlags,
        Widget = e.IsWidget,
        Periodic = e.IsPeriodic,
        MeanPeriodMs = double.IsFinite(e.MeanPeriodMs) ? e.MeanPeriodMs : 0,
        Cv = double.IsFinite(e.Cv) ? e.Cv : 0,
        Gaps = e.GapSamples,
    };
}

public sealed class LiveFuncsJsonlMeta
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("rows")] public int Rows { get; set; }
    [JsonPropertyName("fetched")] public int Fetched { get; set; }
    [JsonPropertyName("distinct")] public int Distinct { get; set; }
    [JsonPropertyName("recording")] public bool Recording { get; set; }
    [JsonPropertyName("diff")] public bool Diff { get; set; }
    [JsonPropertyName("truncated")] public bool Truncated { get; set; }
    [JsonPropertyName("baseline_partial")] public bool BaselinePartial { get; set; }
    [JsonPropertyName("filter")] public string Filter { get; set; } = "";
    [JsonPropertyName("fetch_limit")] public int FetchLimit { get; set; }
    [JsonPropertyName("min_calls")] public int MinCalls { get; set; }
}

public sealed class LiveFuncsJsonlRow
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("class")] public string Class { get; set; } = "";
    [JsonPropertyName("func")] public string Func { get; set; } = "";
    [JsonPropertyName("addr")] public string Addr { get; set; } = "";
    [JsonPropertyName("calls")] public long Calls { get; set; }
    [JsonPropertyName("order")] public long Order { get; set; }
    [JsonPropertyName("delta")] public long Delta { get; set; }
    [JsonPropertyName("is_new")] public bool IsNew { get; set; }
    [JsonPropertyName("badge")] public string Badge { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("num_parms")] public int NumParms { get; set; }
    [JsonPropertyName("parms_size")] public int ParmsSize { get; set; }
    [JsonPropertyName("flags")] public uint Flags { get; set; }
    [JsonPropertyName("widget")] public bool Widget { get; set; }
    [JsonPropertyName("periodic")] public bool Periodic { get; set; }
    [JsonPropertyName("mean_period_ms")] public double MeanPeriodMs { get; set; }
    [JsonPropertyName("cv")] public double Cv { get; set; }
    [JsonPropertyName("gaps")] public long Gaps { get; set; }
}

[JsonSerializable(typeof(LiveFuncsJsonlMeta))]
[JsonSerializable(typeof(LiveFuncsJsonlRow))]
internal partial class LiveFuncsJsonlContext : JsonSerializerContext
{
}
