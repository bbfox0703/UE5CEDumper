using System.Globalization;
using System.Text.Json.Nodes;

namespace UE5DumpUI.Mcp;

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Stage D. Keeps a tool reply under <see cref="MaxChars"/> by cutting its largest
/// arrays, and says what was cut.
///
/// Claude Code warns above 10,000 tokens and moves a result over 25,000 tokens, or over 50,000 characters, to a file
/// the model must then open. The tools ask the DLL for small pages, but some replies grow with the game rather than
/// with the request: a class with hundreds of fields, an instance's arrays, a world's components. Cutting text at a
/// length would leave JSON the model cannot parse, so whole array elements go, from the end, until the reply fits;
/// the note names each array and how many elements it kept, so the model knows to narrow the query or page.
/// </summary>
internal static class McpResultFit
{
    /// <summary>The largest reply text sent: under Claude Code's 50,000-character file threshold, and JSON runs near
    /// 3-4 characters a token, so this stays near its 10,000-token warning rather than its 25,000-token cap.</summary>
    public const int MaxChars = 40_000;

    // Each pass cuts one array to an estimate; a few passes settle any shape, and the bound stops a pathological one.
    private const int MaxPasses = 32;

    /// <summary>Trims <paramref name="payload"/> in place until its text fits <paramref name="maxChars"/>. Returns the
    /// text, a note when anything was cut (null when the reply fitted as it was), and whether nothing could be cut far
    /// enough, so the text is a stub that says so: an error, not a result.</summary>
    public static (string Text, string? Note, bool Stubbed) Fit(JsonNode payload, int maxChars = MaxChars)
    {
        string text = McpJson.Write(payload);
        if (text.Length <= maxChars) return (text, null, false);

        // path -> elements the array had before the first cut
        var cuts = new Dictionary<string, int>(StringComparer.Ordinal);
        var kept = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
        for (int pass = 0; pass < MaxPasses && text.Length > maxChars; pass++)
        {
            var (array, path, length) = Largest(payload);
            if (array is null || array.Count == 0) break;

            cuts.TryAdd(path, array.Count);
            kept[path] = array;
            // Scale the array down by the share of the excess it holds, with 10% headroom; always drop at least one.
            long excess = text.Length - (long)(maxChars * 0.9);
            double perElement = (double)length / array.Count;
            int drop = (int)Math.Ceiling(excess / Math.Max(perElement, 1.0));
            int keep = Math.Clamp(array.Count - drop, 0, array.Count - 1);
            while (array.Count > keep) array.RemoveAt(array.Count - 1);
            text = McpJson.Write(payload);
        }

        if (text.Length > maxChars)
        {
            // Nothing left to cut, or a single element alone is too big: say so rather than send a file-sized reply.
            var stub = new JsonObject
            {
                ["error"] = "The reply is too large to return even after cutting its arrays.",
                ["reply_chars"] = text.Length,
            };
            return (McpJson.Write(stub),
                "Narrow the request: a smaller limit, a name_filter, or a smaller object.", true);
        }

        var parts = new List<string>(cuts.Count);
        foreach (var (path, before) in cuts)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{path} keeps {kept[path].Count} of {before}"));
        return (text, "The reply was cut to fit (" + string.Join("; ", parts)
                      + "). Narrow the request or page with offset / limit for the rest.", false);
    }

    /// <summary>The array whose text is longest, anywhere in <paramref name="root"/>, with its path ("fields",
    /// "class.fields", "actors[3].components"). An array is measured whole, its nested arrays included, so the
    /// outermost array of a large subtree wins over the arrays inside it.</summary>
    private static (JsonArray? Array, string Path, int Length) Largest(JsonNode root)
    {
        JsonArray? best = null;
        string bestPath = "";
        int bestLength = 0;

        void Visit(JsonNode? node, string path)
        {
            switch (node)
            {
                case JsonArray arr:
                    if (arr.Count > 0)
                    {
                        int len = McpJson.Write(arr).Length;
                        if (len > bestLength) { best = arr; bestPath = path; bestLength = len; }
                    }
                    for (int i = 0; i < arr.Count; i++)
                        Visit(arr[i], path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]");
                    break;
                case JsonObject obj:
                    foreach (var (key, child) in obj)
                        Visit(child, path.Length == 0 ? key : path + "." + key);
                    break;
            }
        }

        Visit(root, "");
        return (best, bestPath.Length == 0 ? "(reply)" : bestPath, bestLength);
    }
}
