using UE5DumpUI.Core;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>Persistence round-trip for the marker hotkey store (plain text,
/// AOT-safe). Uses a throwaway temp dir as the AppData root.</summary>
public class TeleportHotkeyStoreTests
{
    private sealed class TempPlatform : IPlatformService, IDisposable
    {
        public string Dir { get; } = Path.Combine(Path.GetTempPath(), "ue5cd-test-" + Guid.NewGuid().ToString("N"));
        public TempPlatform() => Directory.CreateDirectory(Dir);
        public string GetAppDataPath() => Dir;
        public void Dispose() { try { Directory.Delete(Dir, true); } catch { } }
        // unused
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetLogDirectoryPath() => Dir;
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "TEST";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string a, string b, string c) => Task.FromResult<string?>(null);
    }

    [Fact]
    public void Save_then_load_roundtrips()
    {
        using var platform = new TempPlatform();
        var store = new TeleportHotkeyStore(platform);

        var bindings = new Dictionary<string, TeleportHotkeyBinding>
        {
            ["save0"]   = new TeleportHotkeyBinding(0x02, 0x76),  // Ctrl+F7
            ["recall1"] = new TeleportHotkeyBinding(0x00, 0x65),  // Num5
        };
        store.Save(bindings);

        var loaded = store.Load();
        Assert.Equal(2, loaded.Count);
        Assert.Equal(0x02u, loaded["save0"].WinMods);
        Assert.Equal(0x76u, loaded["save0"].Vk);
        Assert.Equal("Ctrl+F7", loaded["save0"].Label);
        Assert.Equal("Num5", loaded["recall1"].Label);
    }

    [Fact]
    public void Load_missing_file_returns_empty()
    {
        using var platform = new TempPlatform();
        var store = new TeleportHotkeyStore(platform);
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Load_skips_malformed_lines()
    {
        using var platform = new TempPlatform();
        var path = Path.Combine(platform.Dir, "UE5CEDumper", "teleport-hotkeys.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, new[]
        {
            "# comment",
            "garbage-no-equals",
            "save0=2,118",       // valid
            "bad=notanumber,x",  // skipped
        });
        var store = new TeleportHotkeyStore(platform);
        var loaded = store.Load();
        Assert.Single(loaded);
        Assert.True(loaded.ContainsKey("save0"));
    }
}
