using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [I18N-READINESS-2026-11-04] Safety net. A template from the string table goes through string.Format, and a template
/// whose placeholders do not fit its arguments -- "{2}" with two arguments, a stray brace, a translator's slip -- throws
/// FormatException out of whatever command was setting the status line. There is one formatter for such templates,
/// Res.FormatTemplate, which degrades instead of throwing; nothing else formats a looked-up template itself.
/// </summary>
public class ResFormatSafetyTests
{
    private static string Ui(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var c = Path.Combine(new[] { dir.FullName, "ui", "UE5DumpUI" }.Concat(parts).ToArray());
            if (File.Exists(c)) return File.ReadAllText(c);
        }
        throw new FileNotFoundException(string.Join("/", parts) + " not found above " + AppContext.BaseDirectory);
    }

    [Theory]
    [InlineData("Helpers/AobMakerStatus.cs")]
    [InlineData("ViewModels/CallTraceViewModel.cs")]
    [InlineData("ViewModels/LiveFuncsViewModel.cs")]
    [InlineData("Views/FreezeValueDialog.cs")]
    public void A_looked_up_template_is_formatted_only_by_Res(string file)
    {
        var code = string.Join('\n', Ui(file.Split('/')).Split('\n').Select(l =>
        {
            int c = l.IndexOf("//", StringComparison.Ordinal);
            return c >= 0 ? l[..c] : l;
        }));
        foreach (var raw in new[] { "string.Format(template,", "string.Format(t,", "string.Format(fallback," })
            Assert.False(code.Contains(raw, StringComparison.Ordinal), $"{file} formats a looked-up template itself: {raw}");
    }
}
