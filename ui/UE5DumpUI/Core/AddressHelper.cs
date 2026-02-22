namespace UE5DumpUI.Core;

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
