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
///   "TQ2-Win64-Shipping.exe+1A2B3C4"                           → resolved via moduleBase
///   "\"TQ2-Win64-Shipping.exe\"+1A2B3C4"                       → resolved via moduleBase (quoted)
///
/// Note the READ side stays deliberately permissive — <see cref="TryNormalizeAddress"/>
/// accepts (and unwraps, via <c>unchecked</c>) the wrapped pseudo-RVAs CE itself
/// produces, e.g. <c>"…exe"+FFFF81820A83F268</c>. The WRITE side does not create
/// them any more; see <see cref="FormatAddress"/>.
/// </summary>
public static class AddressHelper
{
    /// <summary>
    /// The largest RVA any PE image can contain. <c>IMAGE_OPTIONAL_HEADER.SizeOfImage</c>
    /// is a <c>DWORD</c> even in PE32+, so an offset from the module base that does not
    /// fit in 32 bits cannot be inside the module — whatever the module's real size is.
    /// That makes <see cref="TryGetModuleRva"/> exact in the direction that matters: it
    /// never rejects an address that IS in the module.
    /// </summary>
    private const ulong MaxPossibleImageRva = uint.MaxValue;

    /// <summary>
    /// Compute <paramref name="hexAddr"/> as an offset from <paramref name="moduleBase"/>,
    /// but only when the result can actually be a module RVA.
    ///
    /// <para>
    /// Returns false — rather than a number — for the two provably-out-of-module cases:
    /// an address BELOW the base (an unsigned subtraction there wraps, which is how a
    /// heap pointer came out as <c>+FFFF81820A83F268</c>), and a delta wider than
    /// <see cref="MaxPossibleImageRva"/>. Also false when either string is not hex.
    /// </para>
    /// <para>
    /// Pure and static so the rule is testable without a game: it is the whole of
    /// audit #5 AE30. NOT exact in the other direction — a heap allocation that happens
    /// to land within 4 GiB above the image still passes, because the module's real
    /// SizeOfImage is not on the wire (the DLL sends <c>module_base</c> and
    /// <c>module_name</c> only). Tightening it further means adding a wire field.
    /// </para>
    /// </summary>
    public static bool TryGetModuleRva(string? hexAddr, string? moduleBase, out ulong rva)
    {
        rva = 0;
        if (string.IsNullOrEmpty(hexAddr) || string.IsNullOrEmpty(moduleBase)) return false;

        var addrHex = StripHexPrefix(hexAddr);
        var baseHex = StripHexPrefix(moduleBase);
        if (!IsAllHex(addrHex) || !IsAllHex(baseHex)) return false;
        // TryParse, not Convert.ToUInt64: an over-wide string throws from Convert, and a
        // clipboard format is not worth an exception from a fire-and-forget command.
        if (!ulong.TryParse(addrHex, System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out var addr))
            return false;
        if (!ulong.TryParse(baseHex, System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out var baseAddr))
            return false;

        if (addr < baseAddr) return false;
        var delta = addr - baseAddr;
        if (delta > MaxPossibleImageRva) return false;

        rva = delta;
        return true;
    }

    /// <summary>
    /// Format an address according to the selected format.
    ///
    /// <para>
    /// <see cref="AddressFormat.ModuleOffset"/> falls back to the absolute hex form when
    /// the address is not inside the module (audit #5 AE30). It used to subtract
    /// unconditionally, so a UObject on the heap — which is nearly every address this app
    /// copies, and which sits BELOW a 0x7FF7… image base — came out as
    /// <c>"game.exe"+FFFF81820A83F268</c>. That string round-trips within the same run
    /// (CE adds it back mod 2^64, and <see cref="TryNormalizeAddress"/> still accepts it),
    /// which is exactly what makes it dangerous: it LOOKS like the ASLR-stable form the
    /// user picked the option for, and after a relaunch the module base has moved while
    /// the heap address has not, so CE resolves it to an unrelated address instead of
    /// failing. Absolute hex makes no stability promise it cannot keep.
    /// </para>
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
                if (string.IsNullOrEmpty(moduleName)
                    || !TryGetModuleRva(hexAddr, moduleBase, out var rva))
                    goto case AddressFormat.HexNoPrefix;
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

    /// <summary>Drop a leading "0x"/"0X". Anchored, unlike the <c>Replace("0x", "")</c>
    /// the display branches use — equivalent for well-formed hex ('x' is not a hex
    /// digit, so it can only appear at the front), but this one is also correct for the
    /// malformed input <see cref="TryGetModuleRva"/> has to reject rather than mangle.</summary>
    private static string StripHexPrefix(string s)
        => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;

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
