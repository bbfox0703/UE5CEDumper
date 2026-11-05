using UE5DumpUI.Core;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [I18N-READINESS-2026-11-04] P1. A unit test has no Avalonia application, so <see cref="Res.Get"/> answered "" for
/// every key: a string moved into en.axaml went blank in every test that read it, and the assertions that check a
/// sentence is ABSENT (DoesNotContain, NotEqual, Equal("")) kept passing while testing nothing. The test assembly now
/// reads en.axaml once and gives Res that lookup, so a test sees the sentence the app shows.
/// </summary>
public class ResLookupTests
{
    [Fact]
    public void Get_InAUnitTest_ReadsTheEnglishTheAppShows()
    {
        Assert.Equal("Connect", Res.Get("str.Connect"));
        Assert.Equal("Proxy Deploy", Res.Get("str.Tab.ProxyDeploy"));
    }

    [Fact]
    public void Get_DecodesTheXmlEntities()
    {
        // "&amp;" in the file is "&" on screen.
        Assert.Contains("Live camera & function-invoke", Res.Get("str.Banner.GameThreadPaused"), StringComparison.Ordinal);
    }

    [Fact]
    public void Format_FillsATemplateFromTheFile()
    {
        Assert.Contains("(Bob did not arrive)", Res.Format("str.Clipboard.NotCopied", "Bob"), StringComparison.Ordinal);
    }

    [Fact]
    public void Get_AnUnknownKey_IsStillEmpty()
    {
        // CONTROL: the app's contract for a missing key is "" -- unchanged.
        Assert.Equal("", Res.Get("str.NoSuchKey.ForThisTest"));
    }
}
