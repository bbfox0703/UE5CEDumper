namespace UE5DumpUI.Core;

/// <summary>
/// [PATH-CE-MODULE-VIEW] How a program that talks to Windows in ANSI sees a piece of text: the text narrowed to the
/// system ANSI code page and widened back. Cheat Engine is such a program for module names. Its symbol handler names
/// a module <c>WinCPToUTF8(szModule)</c> from ANSI <c>Module32First</c>, and its Lua <c>enumModules</c> /
/// <c>process</c> hand out the raw ANSI bytes. So a string the UI gives CE (a <c>"module"+RVA</c> address, an AA
/// <c>define</c>, a symbol script's module) must carry THIS name, not the Unicode one: on code page 950,
/// <c>ゲーム-…</c> is <c>???-…</c> to CE and <c>Café-…</c> is <c>Cafe-…</c>.
/// </summary>
public interface ISystemCodePage
{
    /// <summary>CE's name for a module FILE named <paramref name="moduleFile"/>: exactly what ANSI
    /// <c>Module32First</c> puts in <c>szModule</c>. The name is narrowed to the system code page with best fit
    /// (Café → Cafe, ™ → ?) -- and then cut after its LAST byte 0x5C, because Module32First takes the part of the
    /// ANSI PATH after the last '\' byte, and a DBCS character's trail byte can be 0x5C (Big5 功 = A5 5C, Shift-JIS
    /// ソ = 83 5C): CE knows 功夫-….exe as 夫-….exe (measured, skeptic MODVIEW-5C-TRAIL). Pure ASCII is unchanged.</summary>
    string AnsiModuleName(string moduleFile);

    /// <summary>[PATH-CE-INJECT-ANSI] The bytes an ANSI API (<c>LoadLibraryA</c>, which Cheat Engine's
    /// <c>injectDLL</c> uses) needs to open <paramref name="path"/>: an ASCII path as it is; else the ASCII 8.3 alias
    /// of its FOLDER plus the file's own name (code-page independent -- the game may run under another locale; the
    /// file's own alias would rename the loaded module); else its EXACT narrowing (never best fit -- best fit names
    /// another folder); else that alias's exact narrowing; else null (no ANSI form exists). The default answers only
    /// for pure ASCII.</summary>
    byte[]? AnsiPathBytes(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        foreach (char c in path) if (c >= 0x80) return null;
        return System.Text.Encoding.ASCII.GetBytes(path);
    }
}

/// <summary>No conversion: the view of a machine whose ANSI code page holds every character. Used where no platform
/// service is wired (tests, and any caller that does not pass one), which keeps pre-fix behaviour.</summary>
public sealed class IdentityCodePage : ISystemCodePage
{
    public static readonly IdentityCodePage Instance = new();
    public string AnsiModuleName(string moduleFile) => moduleFile ?? "";
}
