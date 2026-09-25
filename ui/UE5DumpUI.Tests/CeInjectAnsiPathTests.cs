using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UE5DumpUI.Core;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [PATH-CE-INJECT-ANSI] Cheat Engine's <c>injectDLL</c> copies the path's bytes into the game and calls
/// <c>LoadLibraryA</c> (CEFuncProc.pas InjectDll, no path check before it). The generated scripts baked the UI's
/// folder as a UTF-8 Lua literal, so under a non-ASCII folder (D:\工具\…, a CJK user profile, …™…) LoadLibraryA got
/// UTF-8 bytes read as the ANSI code page, failed, and CE silently manual-mapped the DLL instead (no TLS, no .pdata).
/// The path must reach injectDLL as the ANSI bytes of the path, the 8.3 short path, or not at all -- with a message.
/// </summary>
public class CeInjectAnsiPathTests
{
    private static readonly Encoding Big5;
    static CeInjectAnsiPathTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Big5 = Encoding.GetEncoding(950, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    // ── the bytes an ANSI API needs ──

    [Fact]
    public void AnsiPathBytes_RepresentableFolder_IsItsExactAnsiBytes()
    {
        byte[]? b = WindowsSystemCodePage.AnsiPathBytes(@"D:\工具\UE5Dumper.dll", 950, _ => null);
        Assert.NotNull(b);
        Assert.Equal(Big5.GetBytes(@"D:\工具\UE5Dumper.dll"), b);
    }

    [Fact]
    public void AnsiPathBytes_Unrepresentable_WithoutAShortName_IsNull()
    {
        // D: has no 8.3 names on this PC: GetShortPathNameW returns the long path unchanged.
        Assert.Null(WindowsSystemCodePage.AnsiPathBytes(@"D:\Tools\CE™\UE5Dumper.dll", 950, p => p));
        Assert.Null(WindowsSystemCodePage.AnsiPathBytes(@"D:\ツール\UE5Dumper.dll", 950, _ => null));
    }

    [Fact]
    public void AnsiPathBytes_BestFitIsNeverUsed_ForAPath()
    {
        // Best fit turns Café into Cafe -- a folder that does not exist. Only an exact narrowing opens the file.
        Assert.Null(WindowsSystemCodePage.AnsiPathBytes(@"D:\Café\UE5Dumper.dll", 950, _ => null));
        Assert.Equal(new byte[] { (byte)'D', (byte)':', (byte)'\\', (byte)'C', (byte)'a', (byte)'f', 0xE9 },
            WindowsSystemCodePage.AnsiPathBytes(@"D:\Café", 1252, _ => null));
    }

    [Fact]
    public void AnsiPathBytes_Unrepresentable_FallsBackToTheShortPath()
    {
        const string shortPath = @"C:\PROGRA~2\Steam\STEAMA~1\common\EVERSP~1\UE5Dumper.dll";
        var b = WindowsSystemCodePage.AnsiPathBytes(
            @"C:\Program Files (x86)\Steam\steamapps\common\EVERSPACE™ 2\UE5Dumper.dll", 950, _ => shortPath);
        Assert.Equal(Encoding.ASCII.GetBytes(shortPath), b);
    }

    [Fact]
    public void EscapeLuaBytes_HighBytesAsDecimal_AndTheLongBracketRule()
    {
        Assert.Equal(@"\185C\192\184", CeLuaHygiene.EscapeLuaBytes(new byte[] { 0xB9, (byte)'C', 0xC0, 0xB8 }));
        Assert.Equal(@"a\\b\'c", CeLuaHygiene.EscapeLuaBytes(Encoding.ASCII.GetBytes(@"a\b'c")));
        Assert.Equal(@"x\093]", CeLuaHygiene.EscapeLuaBytes(Encoding.ASCII.GetBytes("x]]")));
        Assert.Equal(@"\0011", CeLuaHygiene.EscapeLuaBytes(new byte[] { 1, (byte)'1' }));   // 3 digits: \001 then 1
    }

    // ── the two generators ──

    public static IEnumerable<object[]> Generators() => new[]
    {
        new object[] { "inject" }, new object[] { "autorun" },
    };

    private static string Gen(string which, string dllPath, ISystemCodePage cp) => which == "inject"
        ? CeInjectScriptGenerator.Generate(dllPath, cp)
        : CeAutorunScriptGenerator.Generate(dllPath, cp);

    [Theory]
    [MemberData(nameof(Generators))]
    public void NonAsciiFolder_InjectDllGetsTheAnsiBytes_TheMessagesShowTheRealPath(string which)
    {
        const string path = @"D:\工具\UE5CEDumper\UE5Dumper.dll";
        string lua = Gen(which, path, new FakeCodePage(Big5.GetBytes(path)));

        var baked = Regex.Match(lua, @"local DLL_PATH = '([^']*)'");
        Assert.True(baked.Success, "no DLL_PATH literal");
        Assert.All(baked.Groups[1].Value, c => Assert.True(c < 0x80, $"non-ASCII char U+{(int)c:X4} in DLL_PATH"));
        Assert.Contains(CeLuaHygiene.EscapeLuaBytes(Big5.GetBytes(path)), baked.Groups[1].Value);

        Assert.Contains($"local DLL_PATH_SHOWN = '{CeLuaHygiene.EscapeLuaString(path)}'", lua);
        Assert.Contains("injectDLL(DLL_PATH)", lua);
        Assert.DoesNotContain("expected at:\\n     ' .. DLL_PATH ..", lua);   // messages show the real path
        Assert.DoesNotContain("'[UE5CEDumper] injecting ' .. DLL_PATH)", lua);
    }

    [Theory]
    [MemberData(nameof(Generators))]
    public void NoAnsiForm_RefusesBeforeInjecting_AndSaysWhy(string which)
    {
        const string path = @"D:\Tools\CE™\UE5Dumper.dll";
        string lua = Gen(which, path, new FakeCodePage(null));

        Assert.Contains("local DLL_PATH = nil", lua);
        int refuse = lua.IndexOf("if DLL_PATH == nil then", StringComparison.Ordinal);
        int inject = lua.IndexOf("injectDLL(DLL_PATH)", StringComparison.Ordinal);
        Assert.True(refuse >= 0 && refuse < inject, "the refusal must come before injectDLL");
        string block = lua.Substring(refuse, inject - refuse);
        Assert.Contains("code page", block);
        Assert.Contains("LoadLibraryA", block);
        Assert.Contains("Inject into running game", block);                 // the route that works (LoadLibraryW)
        // Applied nothing: the record unticks (deferred), the autorun function reports failure.
        Assert.Contains(which == "inject" ? "memrec.Active = false end _u.Enabled=true end" : "return false", block);
    }

    [Theory]
    [MemberData(nameof(Generators))]
    public void AsciiFolder_IsBakedAsBefore(string which)
    {
        const string path = @"C:\Tools\UE5CEDumper\UE5Dumper.dll";
        string lua = Gen(which, path, new FakeCodePage(null));   // never consulted for ASCII
        Assert.Contains(@"local DLL_PATH = 'C:\\Tools\\UE5CEDumper\\UE5Dumper.dll'", lua);
    }

    // ── the skeptic review (wf_6ba4bc83-14d) ──

    [Fact]
    public void AnsiPathBytes_AnAsciiShortPath_IsPreferred_ItDoesNotDependOnTheGamesCodePage()
    {
        // (CEINJ-4) The bytes are made in the UI's ACP but decoded in the GAME's -- a game under Locale Emulator has
        // another one. An ASCII 8.3 alias reads the same in every code page.
        const string shortPath = @"C:\PROGRA~2\TOOLS~1\UE5Dumper.dll";
        Assert.Equal(Encoding.ASCII.GetBytes(shortPath),
            WindowsSystemCodePage.AnsiPathBytes(@"C:\Program Files (x86)\工具\UE5Dumper.dll", 950, _ => shortPath));
    }

    [Fact]
    public void AnsiPathBytes_TheRealApi_OnAFolderNoAnsiCodePageHolds()
    {
        // (T9) The production entry point (GetACP, GetShortPathNameW) was never run by a test. An emoji is in no ANSI
        // code page, so the answer must be null, an ASCII alias that opens the file, or (a UTF-8 ACP) the UTF-8 bytes.
        string dir = Path.Combine(Path.GetTempPath(), "ue5-ansi-\U0001F600-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string dll = Path.Combine(dir, "UE5Dumper.dll");
        File.WriteAllBytes(dll, new byte[] { 0x4D, 0x5A });
        try
        {
            byte[]? b = new WindowsSystemCodePage().AnsiPathBytes(dll);
            if (b is null) return;                                              // no ANSI form: refused upstream
            if (b.SequenceEqual(Encoding.UTF8.GetBytes(dll))) return;           // a UTF-8 ANSI code page
            Assert.All(b, x => Assert.True(x < 0x80, "a non-ASCII byte for a path no code page holds"));
            Assert.True(File.Exists(Encoding.ASCII.GetString(b)), "the ASCII alias does not open the file");
        }
        finally { try { Directory.Delete(dir, true); } catch { /* best effort */ } }
    }

    [Theory]
    [MemberData(nameof(Generators))]
    public void GeneratedScripts_OnCesVm_CompileAndBakeTheExactBytes(string which)
    {
        // (T9) The CE-VM check was run once by hand; this commits it. Skips where the host is not built.
        const string path = @"D:\工具\UE5CEDumper\UE5Dumper.dll";
        byte[] big5 = Big5.GetBytes(path);
        string lua = Gen(which, path, new FakeCodePage(big5));

        // Every Lua chunk compiles (the record: each {$lua} block; the autorun: the file).
        var chunks = which == "inject"
            ? Regex.Matches(lua, @"\{\$lua\}\n(.*?)\n\{\$asm\}", RegexOptions.Singleline).Select(m => m.Groups[1].Value).ToList()
            : new List<string> { lua };
        Assert.NotEmpty(chunks);
        string rhs = Regex.Match(lua, @"local DLL_PATH = ('[^']*')").Groups[1].Value;
        var sb = new StringBuilder();
        for (int i = 0; i < chunks.Count; i++)
            sb.Append($"assert(load([==[{chunks[i]}]==], 'chunk{i}'))\n");
        sb.Append($"local v = {rhs}\nlocal hex = {{}}\nfor k = 1, #v do hex[#hex + 1] = string.format('%02X', v:byte(k)) end\n");
        sb.Append("print('BYTES=' .. table.concat(hex))\n");

        var (exit, output) = CeLua53Host.Run(sb.ToString());
        Assert.True(exit == 0, output);
        Assert.Contains("BYTES=" + Convert.ToHexString(big5), output);
    }

    [Fact]
    public void AnsiPathBytes_AnAsciiPath_IsNeverAliased()
    {
        // (second review, T-ALIAS-LEAF-UI) The alias exists for code-page independence; ASCII already has it.
        Assert.Equal(Encoding.ASCII.GetBytes(@"C:\Program Files\UE5CEDumper\UE5Dumper.dll"),
            WindowsSystemCodePage.AnsiPathBytes(@"C:\Program Files\UE5CEDumper\UE5Dumper.dll", 950,
                _ => @"C:\PROGRA~1\UE5CED~1\UE5Dumper.dll"));
    }

    [Fact]
    public void AnsiPathBytes_AnAliasThatRenamesTheDll_IsNeverUsed()
    {
        // (second review, HIGH, measured) GetShortPathNameW of the FILE gives the leaf UE5DUM~1.DLL, and the game then
        // maps our DLL under that name: load_mode 'loaded:ue5dum~1.dll', the UI's loaded-module detection misses it.
        const string path = @"C:\Users\王小明\Downloads\UE5CEDumper\UE5Dumper.dll";
        byte[]? b = WindowsSystemCodePage.AnsiPathBytes(path, 950, _ => @"C:\Users\5B2F~1\DOWNLO~1\UE5CED~1\UE5DUM~1.DLL");
        Assert.Equal(Big5.GetBytes(path), b);                        // the exact narrowing, the DLL's own name
    }

    [Fact]
    public void AnsiPathBytes_TheProductionAlias_KeepsTheDllsName_AndIsFoundWhenTheVolumeHasOne()
    {
        // (second review, T-REALAPI-VACUOUS) The real entry point again, but no vacuous pass: when the folder HAS an
        // ASCII 8.3 form (measured independently here), the answer must be non-null and end in the DLL's own name.
        string dir = Path.Combine(Path.GetTempPath(), "ue5-alias-\U0001F600-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string dll = Path.Combine(dir, "UE5Dumper.dll");
        File.WriteAllBytes(dll, new byte[] { 0x4D, 0x5A });
        try
        {
            byte[]? b = new WindowsSystemCodePage().AnsiPathBytes(dll);
            var sb = new StringBuilder(1024);
            uint n = GetShortPathNameW(dir, sb, (uint)sb.Capacity);
            string shortDir = n > 0 && n < sb.Capacity ? sb.ToString() : "";
            bool aliasable = shortDir.Length > 0 && shortDir != dir && shortDir.All(c => c < 0x80);
            if (aliasable)
            {
                Assert.NotNull(b);
                Assert.Equal(Path.Combine(shortDir, "UE5Dumper.dll"), Encoding.ASCII.GetString(b!));
            }
            if (b != null && !b.SequenceEqual(Encoding.UTF8.GetBytes(dll)))
                Assert.EndsWith(@"\UE5Dumper.dll", Encoding.ASCII.GetString(b));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* best effort */ } }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint GetShortPathNameW(string longPath, StringBuilder shortPath, uint length);

    private sealed class FakeCodePage(byte[]? bytes) : ISystemCodePage
    {
        public string AnsiModuleName(string moduleFile) => moduleFile;
        public byte[]? AnsiPathBytes(string path) => bytes;
    }
}
