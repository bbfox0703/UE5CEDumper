using System.Text;

namespace UE5DumpUI.Services;

/// <summary>
/// Shared emitter for the "print the UFunction return value" block that both
/// CE-Lua invoke generators drop into their DEBUG path.
///
/// <para>The two generators (<see cref="InvokeScriptGenerator"/> — the
/// self-contained "Invoke:" script — and <see cref="BakedScriptGenerator"/> —
/// the helper-backed "Invoke (baked):" script) resolve the mailbox differently,
/// but the actual <b>decode + print</b> of the return slot must be identical so
/// the output can't drift between them. That common part lives here.</para>
///
/// <para>The return value of a UFunction is written back into the mailbox params
/// buffer at <c>params_data + returnOffset</c> after ProcessEvent runs (UE lays
/// the return param out inside the same params blob as the inputs). This helper
/// reads that slot with the CE Lua read primitive matching the property type and
/// prints it. Scalars decode directly; <c>FString</c>/<c>FUtf8String</c>/
/// <c>FAnsiString</c> dereference the <c>{Data, Num, Max}</c> header and read the
/// string; every other buffer-shaped type (<c>FText</c>/<c>TArray</c>/<c>TMap</c>/
/// <c>TSet</c>/<c>FStruct</c>/delegate) falls back to a bounded raw-hex dump so
/// there is always <i>something</i> to see.</para>
///
/// <para>Callers gate this behind <c>DEBUG ~= 0</c> (and their own success flag),
/// per the CE-Lua output-hygiene rule — quiet by default, verbose only when the
/// user sets <c>UE5_DEBUG=1</c>. This helper emits only the read+print lines; the
/// <c>if ... then</c> wrapper and the <c>params_data</c> base resolution belong to
/// the caller.</para>
/// </summary>
public static class CeInvokeReturn
{
    /// <summary>
    /// Emit CE-Lua that reads the return slot at <paramref name="pdLocal"/> +
    /// <paramref name="offset"/> and <c>print()</c>s it. Lines are LF-only and
    /// ASCII-only (CE-transmit safe).
    /// </summary>
    /// <param name="sb">Target buffer.</param>
    /// <param name="className">Owning UClass name (for the print label).</param>
    /// <param name="funcName">UFunction name (for the print label).</param>
    /// <param name="retName">Return param name (for the print label).</param>
    /// <param name="ueType">UE FProperty type name of the return param
    ///     (e.g. <c>"StrProperty"</c>, <c>"IntProperty"</c>).</param>
    /// <param name="size">Return param size in bytes (bounds the raw-hex dump).</param>
    /// <param name="offset">Return param offset within the params buffer.</param>
    /// <param name="pdLocal">Lua expression evaluating to the params_data base
    ///     address in the caller's scope (e.g. <c>"_PDret"</c>).</param>
    /// <param name="indent">Leading whitespace applied to every emitted line.</param>
    public static void AppendDecodeAndPrint(
        StringBuilder sb,
        string className, string funcName, string retName,
        string ueType, int size, int offset,
        string pdLocal, string indent)
    {
        // Label baked as a literal so the print never runs the class/func name
        // through string.format (a stray '%' in a cooked name would break it).
        var label = $"{Esc(className)}::{Esc(funcName)} -> {Esc(string.IsNullOrEmpty(retName) ? "ReturnValue" : retName)}";

        // Wide (FString) vs narrow (FUtf8String / FAnsiString) string returns.
        switch (ueType)
        {
            case "StrProperty":
                AppendStringDecode(sb, label, "FString", offset, pdLocal, wide: true, indent);
                return;
            case "Utf8StrProperty":
                AppendStringDecode(sb, label, "FUtf8String", offset, pdLocal, wide: false, indent);
                return;
            case "AnsiStrProperty":
                AppendStringDecode(sb, label, "FAnsiString", offset, pdLocal, wide: false, indent);
                return;
        }

        // Everything else is classified via the same table the baked helper uses,
        // so scalar reads stay consistent with writeBakedParams.
        // `size` is already a parameter of this method; without it a 1-byte enum
        // RETURN was reported from a 4-byte read (audit #5 Y16 site 3).
        var helperType = BakedScriptGenerator.MapToHelperType(ueType, size);
        var (readFn, fmt) = ScalarRead(helperType);
        if (readFn != null)
        {
            Line(sb,
                $"{indent}print('[Invoke] return: {label} ({helperType}@{offset}) = ' .. " +
                $"string.format('{fmt}', {readFn}({pdLocal} + {offset}) or 0))");
            return;
        }

        // Buffer-shaped type with no scalar decoder (ftext/tarray/tmap/tset/
        // fstruct/delegate): dump up to 32 bytes of the slot so "did it run"
        // and "is it non-zero" are still answerable.
        int dumpLen = Math.Max(1, Math.Min(size <= 0 ? 16 : size, 32));
        Line(sb, $"{indent}do");
        Line(sb, $"{indent}  local _s = '[Invoke] return: {label} ({helperType}@{offset}) raw = '");
        Line(sb, $"{indent}  for _i = 0, {dumpLen} - 1 do " +
                 $"_s = _s .. string.format('%02X ', readByte({pdLocal} + {offset} + _i) or 0) end");
        Line(sb, $"{indent}  print(_s)");
        Line(sb, $"{indent}end");
    }

    /// <summary>
    /// Emit the {Data, Num, Max} FString/FUtf8String/FAnsiString decode. Reads the
    /// data pointer + element count, then reads the null-terminated string (bounded
    /// at 512 chars — CE's <c>readString</c> stops at the terminator).
    /// </summary>
    private static void AppendStringDecode(
        StringBuilder sb, string label, string tag, int offset,
        string pdLocal, bool wide, string indent)
    {
        var wideArg = wide ? "true" : "false";
        Line(sb, $"{indent}do");
        Line(sb, $"{indent}  local _sp = readQword({pdLocal} + {offset})");
        Line(sb, $"{indent}  local _sn = readInteger({pdLocal} + {offset} + 8)");
        Line(sb, $"{indent}  if _sp ~= 0 and _sn and _sn > 0 then");
        Line(sb, $"{indent}    print('[Invoke] return: {label} ({tag}@{offset}) = ' .. " +
                 $"(readString(_sp, 512, {wideArg}) or '<unreadable>'))");
        Line(sb, $"{indent}  else");
        Line(sb, $"{indent}    print('[Invoke] return: {label} ({tag}@{offset}) = <empty>')");
        Line(sb, $"{indent}  end");
        Line(sb, $"{indent}end");
    }

    /// <summary>
    /// Map a helper type token to the CE Lua read function + string.format spec
    /// for scalar returns. Returns <c>(null, null)</c> for non-scalar types
    /// (the caller then emits a raw-hex dump).
    /// </summary>
    private static (string? readFn, string? fmt) ScalarRead(string helperType) => helperType switch
    {
        "bool" or "byte" => ("readByte",         "%d"),
        "int16"          => ("readSmallInteger", "%d"),
        "int32"          => ("readInteger",      "%d"),
        "int64"          => ("readQword",        "%d"),
        "float"          => ("readFloat",        "%.10g"),
        "double"         => ("readDouble",       "%.10g"),
        "pointer"        => ("readQword",        "0x%X"),
        _                => (null, null),
    };

    /// <summary>Escape a string for embedding in a Lua single-quoted literal.</summary>
    private static string Esc(string s)
        => (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");

    /// <summary>Append a line with LF-only ending (no CR) for CE compatibility.</summary>
    private static void Line(StringBuilder sb, string text)
    {
        sb.Append(text);
        sb.Append('\n');
    }
}
