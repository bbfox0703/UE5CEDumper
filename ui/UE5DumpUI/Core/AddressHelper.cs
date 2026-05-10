namespace UE5DumpUI.Core;

/// <summary>
/// Address copy format options for the toolbar selector.
/// </summary>
public enum AddressFormat
{
    HexNoPrefix = 0,   // 7FF71B7A1820
    HexWithPrefix = 1, // 0x7FF71B7A1820
    ModuleOffset = 2,  // "module.exe"+RVA
}

/// <summary>
/// Shared address string parsing and normalization utilities.
/// Supports CE (Cheat Engine) address formats:
///   "0x16255B8A224"                                            → "0x16255B8A224"
///   "16255B8A224"                                              → "0x16255B8A224"
///   "TQ2-Win64-Shipping.exe+FFFF81820A83F268"                  → resolved via moduleBase
///   "\"TQ2-Win64-Shipping.exe\"+FFFF81820A83F268"              → resolved via moduleBase (quoted)
/// </summary>
public static class AddressHelper
{
    /// <summary>
    /// Format an address according to the selected format.
    /// </summary>
    /// <param name="hexAddr">Raw hex address (e.g., "0x7FF71B7A1820")</param>
    /// <param name="moduleName">Module name (e.g., "TQ2-Win64-Shipping.exe")</param>
    /// <param name="moduleBase">Module base address (e.g., "0x7FF700000000")</param>
    /// <param name="format">The desired output format</param>
    public static string FormatAddress(string hexAddr, string? moduleName, string? moduleBase, AddressFormat format)
    {
        switch (format)
        {
            case AddressFormat.ModuleOffset:
                if (string.IsNullOrEmpty(moduleName) || string.IsNullOrEmpty(moduleBase))
                    goto case AddressFormat.HexNoPrefix;
                var addrHex = hexAddr.Replace("0x", "").Replace("0X", "");
                var baseHex = moduleBase.Replace("0x", "").Replace("0X", "");
                var addr = Convert.ToUInt64(addrHex, 16);
                var baseAddr = Convert.ToUInt64(baseHex, 16);
                var rva = addr - baseAddr;
                return $"\"{moduleName}\"+{rva:X}";

            case AddressFormat.HexWithPrefix:
                var hex = hexAddr.Replace("0x", "").Replace("0X", "");
                return $"0x{hex}";

            case AddressFormat.HexNoPrefix:
            default:
                return hexAddr.Replace("0x", "").Replace("0X", "");
        }
    }

    /// <summary>
    /// Parse a user-provided address string into a normalized "0x..." hex address.
    /// When a module+offset format is detected and <paramref name="moduleBase"/> is available,
    /// the absolute address is computed as moduleBase + offset.
    /// </summary>
    /// <param name="input">Raw address input from user (CE format, hex, etc.)</param>
    /// <param name="moduleBase">Optional module base address (e.g., "0x7FF700000000")</param>
    /// <returns>Normalized address string prefixed with "0x"</returns>
    /// <remarks>
    /// Legacy entry point — does NOT validate hex content. Garbage input like
    /// "0xajsd;jald" passes through and surfaces as "no UObject found" downstream
    /// instead of the more honest "invalid address". New callers should prefer
    /// <see cref="TryNormalizeAddress"/>.
    /// </remarks>
    public static string NormalizeAddress(string input, string? moduleBase = null)
    {
        return TryNormalizeAddress(input, moduleBase, out var normalized) ? normalized : "0x0";
    }

    /// <summary>
    /// Strict variant of <see cref="NormalizeAddress"/>. Returns false (and writes
    /// "0x0" to <paramref name="normalized"/>) when:
    ///   - input is empty / whitespace only
    ///   - the hex body has non-hex characters (e.g. "0xajsd;jald", CE placeholders like "0x[ply_base]")
    ///   - the module+offset path produces an unparseable offset
    /// Mirrors the DLL's Renge::TryStrToAddr — UI rejects bad addresses before round-tripping
    /// through the pipe so the user gets a clearer error than "No UObject found".
    /// </summary>
    public static bool TryNormalizeAddress(string input, string? moduleBase, out string normalized)
    {
        normalized = "0x0";
        if (string.IsNullOrWhiteSpace(input)) return false;
        var s = input.Trim().Trim('"');

        // CE format: "module.exe"+offset or module.exe+offset
        var plusIdx = s.LastIndexOf('+');
        if (plusIdx >= 0 && plusIdx < s.Length - 1)
        {
            var beforePlus = s[..plusIdx].Trim().Trim('"');
            if (beforePlus.Contains('.') || beforePlus.Any(char.IsLetter))
            {
                var offsetHex = s[(plusIdx + 1)..].Trim();
                if (offsetHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    offsetHex = offsetHex[2..];

                if (!IsAllHex(offsetHex)) return false;

                if (!string.IsNullOrEmpty(moduleBase))
                {
                    var baseHex = moduleBase;
                    if (baseHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        baseHex = baseHex[2..];

                    if (!IsAllHex(baseHex)) return false;
                    if (!ulong.TryParse(baseHex, System.Globalization.NumberStyles.HexNumber,
                                        System.Globalization.CultureInfo.InvariantCulture, out var baseAddr))
                        return false;
                    if (!ulong.TryParse(offsetHex, System.Globalization.NumberStyles.HexNumber,
                                        System.Globalization.CultureInfo.InvariantCulture, out var offset))
                        return false;
                    var absolute = unchecked(baseAddr + offset);
                    normalized = "0x" + absolute.ToString("X");
                    return true;
                }

                normalized = "0x" + offsetHex;
                return true;
            }
        }

        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s[2..];

        if (s.Length == 0 || !IsAllHex(s)) return false;

        normalized = "0x" + s;
        return true;
    }

    private static bool IsAllHex(string s)
    {
        if (s.Length == 0) return false;
        foreach (var c in s)
        {
            bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex) return false;
        }
        return true;
    }
}
