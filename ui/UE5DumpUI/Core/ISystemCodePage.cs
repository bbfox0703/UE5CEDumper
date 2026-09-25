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
    /// <summary>The ANSI round trip of <paramref name="text"/> in the system code page, best fit included
    /// (what ANSI <c>Module32First</c> produces). Pure ASCII is returned unchanged.</summary>
    string AnsiView(string text);

    /// <summary>[PATH-CE-INJECT-ANSI] The bytes an ANSI API (<c>LoadLibraryA</c>, which Cheat Engine's
    /// <c>injectDLL</c> uses) needs to open <paramref name="path"/>: its EXACT narrowing (never best fit -- best fit
    /// names another folder), else its 8.3 short form if that narrows exactly, else null (no ANSI form exists).
    /// The default answers only for pure ASCII.</summary>
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
    public string AnsiView(string text) => text ?? "";
}
