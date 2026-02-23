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
    public static string NormalizeAddress(string input, string? moduleBase = null)
    {
        var s = input.Trim().Trim('"');

        // CE format: "module.exe"+offset or module.exe+offset
        // Extract the part after the last '+'
        var plusIdx = s.LastIndexOf('+');
        if (plusIdx >= 0 && plusIdx < s.Length - 1)
        {
            // Check if the part before '+' looks like a module name (contains '.' or letters)
            var beforePlus = s[..plusIdx].Trim().Trim('"');
            if (beforePlus.Contains('.') || beforePlus.Any(char.IsLetter))
            {
                var offsetHex = s[(plusIdx + 1)..].Trim();
                if (offsetHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    offsetHex = offsetHex[2..];

                // Resolve to absolute address if moduleBase is available
                if (!string.IsNullOrEmpty(moduleBase))
                {
                    var baseHex = moduleBase;
                    if (baseHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        baseHex = baseHex[2..];

                    var baseAddr = Convert.ToUInt64(baseHex, 16);
                    var offset = Convert.ToUInt64(offsetHex, 16);
                    var absolute = unchecked(baseAddr + offset);
                    return "0x" + absolute.ToString("X");
                }

                // No moduleBase — use offset as-is (best effort)
                return "0x" + offsetHex;
            }
        }

        // Remove 0x prefix if present (we'll re-add it)
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            s = s[2..];
        }

        return "0x" + s;
    }
}
