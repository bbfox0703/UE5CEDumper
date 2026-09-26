using System.Text;
using UE5DumpUI.Core;

namespace UE5DumpUI.Services;

/// <summary>
/// [PATH-CE-INJECT-ANSI] How the generated CE scripts hand UE5Dumper.dll's path to <c>injectDLL</c>. Shared by the
/// inject bootstrap record (<see cref="CeInjectScriptGenerator"/>) and the autorun file
/// (<see cref="CeAutorunScriptGenerator"/>), so the two cannot drift.
///
/// CE's <c>injectDLL</c> copies the string's bytes into the game and calls <c>LoadLibraryA</c> on them
/// (CEFuncProc.pas InjectDll; no path check before it). Every route our Lua takes to CE is UTF-8 (the AOBMaker JSON,
/// the clipboard XML, the autorun file), so a non-ASCII folder reached LoadLibraryA as UTF-8 bytes read in the ANSI
/// code page and failed -- and CE then silently manual-mapped the DLL (no TLS, no .pdata). So:
/// <list type="bullet">
///   <item><c>DLL_PATH</c> is the path's ANSI bytes, as a pure-ASCII <c>\ddd</c> literal
///     (<see cref="CeLuaHygiene.EscapeLuaBytes"/>), in <see cref="ISystemCodePage.AnsiPathBytes"/>' order: the ASCII
///     8.3 alias of the FOLDER with the DLL's own name (it reads the same in every code page), else the exact
///     narrowing, else the alias's exact narrowing. An ASCII path is baked exactly as before, never aliased.</item>
///   <item><c>DLL_PATH = nil</c> when no ANSI form exists (a character the code page lacks, on a volume without 8.3
///     names -- D: here), and the script refuses before <c>injectDLL</c>, saying why and what works.</item>
///   <item><c>DLL_PATH_SHOWN</c> is the real path (UTF-8), for every message: CE shows UTF-8.</item>
/// </list>
/// ⚠ CE's "Always force load modules" option skips LoadLibraryA and hands the same string to its Lua manual mapper,
/// which expects UTF-8; with a non-ASCII folder that option cannot work either way.
/// </summary>
internal static class CeInjectPathLua
{
    /// <summary>The two locals, at <paramref name="indent"/>.</summary>
    public static void AppendLocals(StringBuilder sb, string dllPath, ISystemCodePage codePage, string indent = "")
    {
        bool ascii = true;
        foreach (char c in dllPath) if (c >= 0x80) { ascii = false; break; }
        if (ascii)
        {
            sb.Append(indent).Append($"local DLL_PATH = '{CeLuaHygiene.EscapeLuaString(dllPath)}'").Append('\n');
        }
        else
        {
            byte[]? ansi = codePage.AnsiPathBytes(dllPath);
            sb.Append(indent)
              .Append(ansi is null
                  ? "local DLL_PATH = nil   -- no ANSI form of this path: see the refusal before injectDLL"
                  : $"local DLL_PATH = '{CeLuaHygiene.EscapeLuaBytes(ansi)}'   -- the path in the ANSI code page: "
                    + "injectDLL calls LoadLibraryA")
              .Append('\n');
        }
        sb.Append(indent).Append($"local DLL_PATH_SHOWN = '{CeLuaHygiene.EscapeLuaString(dllPath)}'").Append('\n');
    }

    /// <summary>The refusal, emitted before <c>injectDLL</c>: <c>if DLL_PATH == nil then … end</c>.
    /// <paramref name="bailOut"/> is the caller's no-op exit (untick + return, or <c>return false</c>).</summary>
    public static void AppendRefusal(StringBuilder sb, string indent, string bailOut)
    {
        sb.Append(indent).Append("if DLL_PATH == nil then").Append('\n');
        sb.Append(indent).Append("  showMessage('[UE5CEDumper] Cheat Engine cannot load UE5Dumper.dll from this folder:\\n     ' .. DLL_PATH_SHOWN .. '\\n\\n' ..").Append('\n');
        sb.Append(indent).Append("    'The path has characters the Windows code page (ANSI) cannot represent, and there is no short (8.3) name for it.\\n' ..").Append('\n');
        sb.Append(indent).Append("    'CE\\'s injectDLL hands the path to LoadLibraryA, so the DLL would not load.\\n\\n' ..").Append('\n');
        sb.Append(indent).Append("    'Move the UE5CEDumper folder to a path of plain English letters, then create this again --\\n' ..").Append('\n');
        sb.Append(indent).Append("    'or use UE5DumpUI > Proxy Deploy > Inject into running game, which does not go through Cheat Engine.')").Append('\n');
        sb.Append(bailOut).Append('\n');
        sb.Append(indent).Append("end").Append('\n');
    }
}
