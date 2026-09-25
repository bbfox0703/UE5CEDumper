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

    private sealed class FakeCodePage(byte[]? bytes) : ISystemCodePage
    {
        public string AnsiModuleName(string moduleFile) => moduleFile;
        public byte[]? AnsiPathBytes(string path) => bytes;
    }
}
