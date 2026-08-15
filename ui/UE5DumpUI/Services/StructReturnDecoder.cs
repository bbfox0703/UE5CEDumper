using UE5DumpUI.Models;
using UE5DumpUI.Views;

namespace UE5DumpUI.Services;

/// <summary>
/// Helper that converts an invoke post-call ParamBuffer + the function's
/// return-param metadata into a list of decoded
/// <see cref="StructFieldValue"/> rows. Used by InvokeParamDialog to
/// render a property-grid of the return struct's sub-fields below the
/// raw hex line (pick #5, build 772+).
///
/// Resolution order (matches the existing inline decoder in the dialog
/// so the grid and the text line always agree on field values):
/// <list type="number">
/// <item>Known fixed-layout struct (FVector / FRotator / FHitResult etc.
///     via <see cref="KnownStructLayouts.GetLayout"/>) — best signal
///     because the layout is locked per UE version and survives even
///     when DLL-side StructFields metadata is empty.</item>
/// <item>DLL-discovered dynamic StructFields (param.StructFields populated
///     by WalkFunctions for any USTRUCT). Covers user-defined USTRUCTs
///     and engine structs the KnownStructLayouts table doesn't know
///     about yet.</item>
/// <item>Non-struct returns return an empty list — caller falls back to
///     the existing single-line decode for the result label.</item>
/// </list>
///
/// All decoding uses <see cref="InvokeParamDialog.DecodeParamValue"/>
/// so adding new property types to the dialog's switch automatically
/// updates the grid; no parallel decoder to keep in sync.
/// </summary>
public static class StructReturnDecoder
{
    /// <summary>Decode the return param's struct sub-fields from the
    /// post-call ParamBuffer. Returns an empty list when the param is
    /// not a struct OR no layout is resolvable. <paramref name="ueVersion"/>
    /// is passed straight to <see cref="KnownStructLayouts.GetLayout"/>
    /// so per-version variants (UE4 vs UE5 FVector double-precision,
    /// etc.) pick the right entry.</summary>
    public static List<StructFieldValue> Decode(
        byte[] buf,
        FunctionParamModel returnParam,
        int ueVersion)
    {
        ArgumentNullException.ThrowIfNull(buf);
        ArgumentNullException.ThrowIfNull(returnParam);

        if (returnParam.TypeName != "StructProperty")
            return new List<StructFieldValue>();

        // Prefer the known layout when available — survives empty
        // DLL-side StructFields and locks the per-version offset table.
        //
        // GetTrustedLayout, not GetLayout: a hardcoded layout that CONTRADICTS the
        // engine-reported size is a wrong guess (mis-detected UE version, or a
        // licensee fork), and decoding on it labels every value with the wrong
        // field name at the wrong offset. Falling through to the DLL-discovered
        // fields below is strictly better — those came from the real UScriptStruct.
        // The invoke dialog has refused such a layout for its INPUT boxes since Y7;
        // this RESULT path accepted it for four more builds (audit #5 AC2).
        if (!string.IsNullOrEmpty(returnParam.StructName))
        {
            var layout = KnownStructLayouts.GetTrustedLayout(
                returnParam.StructName, returnParam.Size, ueVersion);
            if (layout != null)
                return DecodeWithLayout(buf, returnParam, layout);
        }

        // Fallback: DLL-discovered dynamic StructFields. WalkFunctions
        // populates these for any USTRUCT whose UScriptStruct chain is
        // readable — covers user USTRUCTs the KnownStructLayouts table
        // wouldn't list.
        if (returnParam.StructFields.Count > 0)
            return DecodeWithDynamicFields(buf, returnParam);

        return new List<StructFieldValue>();
    }

    /// <summary>True when <see cref="Decode"/> would return a non-empty
    /// list. Cheaper than running the full decode just to check
    /// visibility on a UI binding. Mirrors the resolution order
    /// above so the gate matches what would actually render.</summary>
    public static bool CanDecode(FunctionParamModel returnParam, int ueVersion)
    {
        if (returnParam is null) return false;
        if (returnParam.TypeName != "StructProperty") return false;
        // Must use the SAME predicate as Decode above, or the gate and the render
        // disagree: CanDecode would show the grid and Decode would then return the
        // dynamic-field rows (or nothing at all).
        if (KnownStructLayouts.GetTrustedLayout(
                returnParam.StructName, returnParam.Size, ueVersion) != null)
        {
            return true;
        }
        return returnParam.StructFields.Count > 0;
    }

    // ------------------------------------------------------------------
    // Layout-driven decoders. Both produce StructFieldValue rows with
    // ABSOLUTE buffer offsets so the user can copy-paste a row's offset
    // back into other tooling (Find In Containers, CE memrec setup).
    // ------------------------------------------------------------------

    private static List<StructFieldValue> DecodeWithLayout(
        byte[] buf,
        FunctionParamModel returnParam,
        KnownStructLayouts.StructLayout layout)
    {
        var rows = new List<StructFieldValue>(layout.Fields.Count);
        foreach (var sf in layout.Fields)
        {
            int absOffset = returnParam.Offset + sf.Offset;
            var subParam = new FunctionParamModel
            {
                Name     = sf.Name,
                TypeName = sf.TypeName,
                Size     = sf.Size,
                Offset   = absOffset,
            };
            string value = SafeDecode(buf, subParam);
            rows.Add(new StructFieldValue(sf.Name, sf.TypeName, value, absOffset));
        }
        return rows;
    }

    private static List<StructFieldValue> DecodeWithDynamicFields(
        byte[] buf, FunctionParamModel returnParam)
    {
        var rows = new List<StructFieldValue>(returnParam.StructFields.Count);
        foreach (var sf in returnParam.StructFields)
        {
            int absOffset = returnParam.Offset + sf.Offset;
            var subParam = new FunctionParamModel
            {
                Name     = sf.Name,
                TypeName = sf.TypeName,
                Size     = sf.Size,
                Offset   = absOffset,
            };
            string value = SafeDecode(buf, subParam);
            rows.Add(new StructFieldValue(sf.Name, sf.TypeName, value, absOffset));
        }
        return rows;
    }

    private static string SafeDecode(byte[] buf, FunctionParamModel p)
    {
        try
        {
            return InvokeParamDialog.DecodeParamValue(buf, p);
        }
        catch
        {
            // The dialog's DecodeParamValue is already defensive but a
            // future buffer-end edge case shouldn't blow up the whole
            // grid — fall back to "?" for that cell and let the
            // remaining rows render.
            return "?";
        }
    }
}
