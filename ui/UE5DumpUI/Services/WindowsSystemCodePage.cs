using System.Runtime.InteropServices;
using UE5DumpUI.Core;

namespace UE5DumpUI.Services;

/// <summary>
/// [PATH-CE-MODULE-VIEW] <see cref="ISystemCodePage"/> on Windows: <c>WideCharToMultiByte(CP_ACP, 0)</c> then
/// <c>MultiByteToWideChar(CP_ACP)</c>. Flags 0 on purpose: that is best fit ON, the conversion ANSI
/// <c>Module32First</c> performs. Measured on this PC (ACP 950) against Module32First itself, for
/// 遊戲 / ゲーム / Game™ / Café / Game® / Tony's: byte-identical in all six cases. <c>WC_NO_BEST_FIT_CHARS</c> would
/// give <c>Caf?</c> where CE shows <c>Cafe</c>.
///
/// The UI and CE both run with the system ANSI code page (neither manifest sets <c>activeCodePage</c>), so CP_ACP
/// read here is CE's.
/// </summary>
public sealed class WindowsSystemCodePage : ISystemCodePage
{
    private const uint CP_ACP = 0;

    public string AnsiView(string text) => AnsiView(text, CP_ACP);

    public byte[]? AnsiPathBytes(string path) => AnsiPathBytes(path, CP_ACP, ShortPath);

    private const uint CP_UTF8 = 65001;
    private const uint WC_NO_BEST_FIT_CHARS = 0x400;

    /// <summary>[PATH-CE-INJECT-ANSI] <see cref="ISystemCodePage.AnsiPathBytes"/> in an explicit code page, with the
    /// 8.3 lookup injectable, so tests do not depend on the machine's ACP or on the volume's 8.3 setting (8.3 names
    /// exist on C: here but are OFF on D:). Exact narrowing only: best fit turns Café into Cafe, a folder that does
    /// not exist.</summary>
    internal static byte[]? AnsiPathBytes(string path, uint codePage, Func<string, string?> shortPath)
    {
        if (string.IsNullOrEmpty(path)) return null;
        byte[]? exact = ExactAnsi(path, codePage);
        if (exact != null) return exact;
        string? sp = shortPath(path);
        if (string.IsNullOrEmpty(sp) || string.Equals(sp, path, StringComparison.Ordinal)) return null;
        return ExactAnsi(sp, codePage);
    }

    /// <summary>The narrowing, or null when a character has no exact form in the code page.</summary>
    private static byte[]? ExactAnsi(string text, uint codePage)
    {
        uint cp = codePage == CP_ACP ? GetACP() : codePage;
        // UTF-8 as the ANSI code page (the Windows "Beta: use Unicode UTF-8" option): every string is exact, and the
        // API refuses both WC_NO_BEST_FIT_CHARS and the used-default out-parameter for it.
        if (cp == CP_UTF8) return System.Text.Encoding.UTF8.GetBytes(text);
        int n = WideCharToMultiByteChecked(cp, WC_NO_BEST_FIT_CHARS, text, text.Length, null, 0, IntPtr.Zero,
                                           out int usedDefault);
        if (n <= 0 || usedDefault != 0) return null;
        var bytes = new byte[n];
        if (WideCharToMultiByteChecked(cp, WC_NO_BEST_FIT_CHARS, text, text.Length, bytes, n, IntPtr.Zero,
                                       out usedDefault) != n || usedDefault != 0) return null;
        return bytes;
    }

    /// <summary>GetShortPathNameW, or null. The path unchanged is a legitimate answer: a volume with 8.3 names off.</summary>
    private static string? ShortPath(string path)
    {
        int n = GetShortPathNameW(path, null, 0);
        if (n <= 0) return null;
        var buf = new char[n];
        int m = GetShortPathNameW(path, buf, n);
        return m > 0 && m < n ? new string(buf, 0, m) : null;
    }

    /// <summary>The round trip in an explicit code page, so tests do not depend on the machine's ACP. Any API
    /// failure returns the text unchanged: the same answer as before this existed.</summary>
    internal static string AnsiView(string text, uint codePage)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        bool ascii = true;
        foreach (char c in text)
            if (c >= 0x80) { ascii = false; break; }
        if (ascii) return text;   // ASCII is the same in every ANSI code page

        int n = WideCharToMultiByte(codePage, 0, text, text.Length, null, 0, IntPtr.Zero, IntPtr.Zero);
        if (n <= 0) return text;
        var bytes = new byte[n];
        if (WideCharToMultiByte(codePage, 0, text, text.Length, bytes, n, IntPtr.Zero, IntPtr.Zero) != n) return text;

        int m = MultiByteToWideChar(codePage, 0, bytes, n, null, 0);
        if (m <= 0) return text;
        var chars = new char[m];
        if (MultiByteToWideChar(codePage, 0, bytes, n, chars, m) != m) return text;
        return new string(chars);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "WideCharToMultiByte", ExactSpelling = true)]
    private static extern int WideCharToMultiByte(uint codePage, uint flags, string wide, int wideLength,
        byte[]? multiByte, int multiByteLength, IntPtr defaultChar, IntPtr usedDefaultChar);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "WideCharToMultiByte", ExactSpelling = true)]
    private static extern int WideCharToMultiByteChecked(uint codePage, uint flags, string wide, int wideLength,
        byte[]? multiByte, int multiByteLength, IntPtr defaultChar, out int usedDefaultChar);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetACP();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetShortPathNameW", ExactSpelling = true)]
    private static extern int GetShortPathNameW(string longPath, char[]? shortPath, int shortPathLength);

    // CharSet.Unicode is load-bearing here too: under the default (Ansi) a char[] is marshalled as ANSI chars.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "MultiByteToWideChar", ExactSpelling = true)]
    private static extern int MultiByteToWideChar(uint codePage, uint flags, byte[] multiByte, int multiByteLength,
        char[]? wide, int wideLength);
}
