using System;
using System.IO;
using System.Linq;
using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [A4-EDIT-STALE-PENDING] Reopening an edited Live Walker cell and closing it without typing wrote
/// the PREVIOUS edit into the game again.
///
/// <para>The pending text (<c>LiveFieldValue._editableValue</c>) is written only by the editing
/// template's TwoWay bindings — the TextBox as the user types, the bool ComboBox when the user
/// picks — and nothing ever reset it. Since [LWREFRESH-2026-08-21] the
/// row object survives the post-commit refresh, so the last typed text survived with it: type 250
/// into Health and commit, the game drops Health to 57, double-click the cell and press Enter
/// without typing, and 250 is written again with "Written: Health = 250". Escape, reopen and Enter
/// did the same with no refresh at all.</para>
///
/// <para>The recorded safe fix resets the pending text when an edit BEGINS. That hook lives in the
/// view's code-behind (<c>FieldGrid_BeginningEdit</c>), which no VM test can drive, so the
/// semantics are pinned on <see cref="LiveFieldValue"/> and the hook is pinned by reading the
/// code-behind back (the <c>ClassListCapTests</c> pattern).</para>
/// </summary>
public class LiveWalkerEditPendingTests
{
    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"could not find {relative} walking up from {AppContext.BaseDirectory}");
    }

    private static LiveFieldValue Health(string current = "57") => new()
    {
        Name = "Health", TypeName = "FloatProperty", Offset = 0x10, Size = 4,
        TypedValue = current, FieldAddress = "0x100010",
    };

    [Fact]
    public void TheLastTypedValue_SurvivesASameObjectRefresh_ByDesign()
    {
        // The negative control for the recorded UNSAFE fix: the refresh's copy must NOT reset the
        // pending text, or a refresh landing mid-edit would drop what the user is typing.
        var row = Health("100");
        row.EditableValue = "250";   // the editor's TwoWay binding, as the user types
        row.CopyLiveValuesFrom(Health("57"));
        Assert.Equal("250", row.GetPendingEditValue());
    }

    [Fact]
    public void ResetPendingEdit_ForgetsTheLastTypedValue_SoEnterWithoutTypingWritesNothing()
    {
        var row = Health();
        row.EditableValue = "250";
        row.ResetPendingEdit();

        // FieldGrid_CellEditEnded commits only a NON-EMPTY pending value.
        Assert.Equal("", row.GetPendingEditValue());
        // ...and the editor still opens on the CURRENT value, which comes from the getter.
        Assert.Equal("57", row.EditableValue);
    }

    [Fact]
    public void ADeliberateReType_OfTheCurrentValue_IsStillPending()
    {
        // Why the fix is a reset and not a "same as current? skip" comparison: typing the value the
        // game already holds is a real request (e.g. to write it back after the game changed it
        // between the read and the keystroke), and a comparison would drop it silently. This pins
        // the MODEL half; TheCommitHook_… pins the commit half in the code-behind.
        var row = Health();
        row.ResetPendingEdit();
        row.EditableValue = "57";
        Assert.Equal("57", row.GetPendingEditValue());
    }

    [Fact]
    public void TheEditBeginHook_ResetsThePendingValue_AfterTheNonEditableVeto()
    {
        string src = File.ReadAllText(RepoFile(@"ui\UE5DumpUI\Views\LiveWalkerPanel.axaml.cs"));
        int at = src.IndexOf("private void FieldGrid_BeginningEdit(", StringComparison.Ordinal);
        Assert.True(at > 0, "FieldGrid_BeginningEdit not found — re-point this pin");
        int end = src.IndexOf("\n    }", at, StringComparison.Ordinal);   // the method's own closing brace
        Assert.True(end > at, "could not find the end of FieldGrid_BeginningEdit");
        string body = src.Substring(at, end - at);

        // Full-line comments dropped: the comment above the call names ResetPendingEdit on purpose,
        // and a commented-out call must not satisfy this pin (the CtDllDiscoveryTests.CodeOnly rule).
        string code = CodeOnly(body);

        int veto = code.IndexOf("!field.IsEditable", StringComparison.Ordinal);
        Assert.True(veto > 0, "the non-editable veto moved — re-check this pin");
        int vetoReturn = code.IndexOf("return;", veto, StringComparison.Ordinal);
        Assert.True(vetoReturn > veto, "the veto no longer returns — re-check this pin");
        int vetoEnd = code.IndexOf('}', vetoReturn);   // the veto block's closing brace

        int reset = code.LastIndexOf(".ResetPendingEdit();", StringComparison.Ordinal);
        Assert.True(reset > 0, "FieldGrid_BeginningEdit must call LiveFieldValue.ResetPendingEdit()");
        // After the veto BLOCK, not inside it: only an edit that actually opens forgets the old text.
        // (Moved INTO the veto block, the reset would run only for rows that never open.)
        Assert.True(reset > vetoEnd, "ResetPendingEdit must run after the non-editable veto block, not inside it");
    }

    /// <summary>The commit half of the "no same-as-current comparison" control. The recorded-UNSAFE
    /// alternative fix compared the pending text against the current value, and the natural place
    /// to add it is where the commit is decided — FieldGrid_CellEditEnded — not the model. So pin
    /// that the commit decision is exactly "a non-empty pending value", and reads nothing current.</summary>
    [Fact]
    public void TheCommitHook_CommitsAnyNonEmptyPendingValue_WithNoSameAsCurrentComparison()
    {
        string src = File.ReadAllText(RepoFile(@"ui\UE5DumpUI\Views\LiveWalkerPanel.axaml.cs"));
        int at = src.IndexOf("private async void FieldGrid_CellEditEnded(", StringComparison.Ordinal);
        Assert.True(at > 0, "FieldGrid_CellEditEnded not found — re-point this pin");
        int end = src.IndexOf("\n    }", at, StringComparison.Ordinal);
        Assert.True(end > at, "could not find the end of FieldGrid_CellEditEnded");
        string code = CodeOnly(src.Substring(at, end - at));

        Assert.Contains(".GetPendingEditValue()", code);
        // "" is the write-nothing signal a reset leaves behind; drop this guard and every
        // open-and-close would try to write an empty value.
        Assert.Contains("!string.IsNullOrEmpty(newValue)", code);
        // The recorded-unsafe shape would read the current value here. ("GetPendingEditValue" does
        // not contain either substring.)
        Assert.DoesNotContain("EditableValue", code);
        Assert.DoesNotContain("TypedValue", code);
    }

    private static string CodeOnly(string body)
        => string.Join('\n', body.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
}
