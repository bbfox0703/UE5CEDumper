using System.IO;
using UE5DumpUI;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [MCP-WRITE-GRANTS-2026-11-14] §4.10. The "Block AI writes" file: the window writes it, every MCP server reads it on
/// every call that would change the game. A stop control, so a save that did not land must say so, a server's read
/// must never make the window's save fail, and a file the server cannot read must block.
/// </summary>
public sealed class AiWriteBlockGateTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MockPlatformService _platform;

    public AiWriteBlockGateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UE5DumpAiBlockTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _platform = new MockPlatformService(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    private string FilePath => AiWriteBlockGate.PathFor(_platform);

    private void WriteFile(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, text);
    }

    [Fact]
    public void TheFile_SitsBesideExperimentalJson()
        => Assert.Equal(Path.Combine(_tempDir, Constants.LogFolderName, "ai-writes.json"), FilePath);

    [Fact]
    public void NoFile_IsNotBlocked_AndLoadingCreatesNone()
    {
        var gate = new AiWriteBlockGate(_platform);
        Assert.False(gate.IsBlocked);
        Assert.False(File.Exists(FilePath));
        Assert.Equal(new AiWriteBlockState(false, 0, false), AiWriteBlockGate.ReadState(FilePath));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    public void AFileTheServerCannotRead_Blocks(string text)
    {
        WriteFile(text);
        Assert.True(AiWriteBlockGate.ReadState(FilePath).Unreadable);
    }

    [Fact]
    public void TurningItOn_PersistsAndBumpsTheStamp_AndOffKeepsTheStamp()
    {
        var gate = new AiWriteBlockGate(_platform);
        Assert.True(gate.TrySetBlocked(true));
        Assert.Equal(new AiWriteBlockState(true, 1, false), AiWriteBlockGate.ReadState(FilePath));
        Assert.True(gate.TrySetBlocked(false));
        Assert.Equal(new AiWriteBlockState(false, 1, false), AiWriteBlockGate.ReadState(FilePath));
        Assert.True(gate.TrySetBlocked(true));
        Assert.Equal(new AiWriteBlockState(true, 2, false), AiWriteBlockGate.ReadState(FilePath));
        // false and 0 are written, not omitted: an omitted key would read back as whatever the default becomes.
        Assert.True(gate.TrySetBlocked(false));
        string text = File.ReadAllText(FilePath);
        Assert.Contains("\"blocked\": false", text);
        Assert.Contains("\"revokeStamp\": 2", text);
    }

    [Fact]
    public void AFreshGate_NeverWritesALowerStampThanTheFileHolds()
    {
        // A second window, or one started after another wrote the file, must still revoke on its next turn-on.
        WriteFile("{ \"blocked\": false, \"revokeStamp\": 7 }");
        var gate = new AiWriteBlockGate(_platform);
        Assert.True(gate.TrySetBlocked(true));
        Assert.Equal(8, AiWriteBlockGate.ReadState(FilePath).RevokeStamp);
    }

    [Fact]
    public void TheServersRead_NeverMakesTheWindowsSaveFail()
    {
        var gate = new AiWriteBlockGate(_platform);
        Assert.True(gate.TrySetBlocked(false));
        // A server's read in progress, with the share mode ReadState opens the file in.
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            Assert.True(gate.TrySetBlocked(true));
        Assert.True(AiWriteBlockGate.ReadState(FilePath).Blocked);
    }

    [Fact]
    public void ASaveThatCannotLand_ReturnsFalse_AndKeepsTheOldState()
    {
        var gate = new AiWriteBlockGate(_platform);
        Assert.True(gate.TrySetBlocked(false));
        // Another program holds the file without letting it be replaced.
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.False(gate.TrySetBlocked(true));
        Assert.False(gate.IsBlocked);
        Assert.False(AiWriteBlockGate.ReadState(FilePath).Blocked);
    }

    [Fact]
    public async Task Reload_PicksUpAChangeMadeElsewhere_AndSaysSo()
    {
        var gate = new AiWriteBlockGate(_platform);
        int changed = 0;
        gate.Changed += (_, _) => changed++;
        WriteFile("{ \"blocked\": true, \"revokeStamp\": 3 }");
        await gate.ReloadAsync();
        Assert.True(gate.IsBlocked);
        Assert.Equal(3, gate.RevokeStamp);
        Assert.Equal(1, changed);
        await gate.ReloadAsync();
        Assert.Equal(1, changed);   // nothing new, nothing raised
    }
}
