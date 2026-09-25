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

    // CharSet.Unicode is load-bearing here too: under the default (Ansi) a char[] is marshalled as ANSI chars.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "MultiByteToWideChar", ExactSpelling = true)]
    private static extern int MultiByteToWideChar(uint codePage, uint flags, byte[] multiByte, int multiByteLength,
        char[]? wide, int wideLength);
}
