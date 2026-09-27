using System.Text.RegularExpressions;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [SNAPSHOT-MANUAL-GATE] SnapshotViewModel.CanManualCapture (CanCapture and Auto Snapshot off)
/// exists so the manual Capture / Estimate buttons stand down while the auto loop owns capturing,
/// and the VM raises it on every change. The panel bound both buttons to CanCapture instead, so
/// the gate was never wired and a manual capture could start between two auto ticks.
/// </summary>
public class SnapshotManualCaptureGateTests
{
    [Theory]
    [InlineData("EstimateSizeCommand")]
    [InlineData("CaptureCommand")]
    public void Manual_capture_buttons_bind_the_manual_gate(string command)
    {
        var panel = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Views/SnapshotPanel.axaml"));
        var button = Regex.Match(panel, @"<Button[^>]*Command=""\{Binding " + command + @"\}""[^>]*>", RegexOptions.Singleline);
        Assert.True(button.Success, $"no button bound to {command}");
        Assert.Contains("IsEnabled=\"{Binding CanManualCapture}\"", button.Value, StringComparison.Ordinal);
    }
}
