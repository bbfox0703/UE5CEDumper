using System.IO;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [DUMPDIFF-UI] Dump Explorer's Compare: the loaded class dump against one the user picks, written as an HTML
/// report and opened. The maintainer's decision (2026-10-06): HTML only, started from this panel. The diff itself
/// is <see cref="DumpDiffParityTests"/>'s; these pin the panel's part — which file is old, what is refused, what is
/// written where, and the two options.
/// </summary>
public class DumpExplorerCompareTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dumpcompare-{Guid.NewGuid():N}");

    public DumpExplorerCompareTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class ComparePlatform : IPlatformService
    {
        public string? OpenAnswer;
        public string? SaveAnswer;
        public int OpenCalls, SaveCalls;
        public string LastSaveName = "", LastSaveExtension = "";
        public readonly List<string> Opened = new();

        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => Path.GetTempPath();
        public string GetLogDirectoryPath() => Path.GetTempPath();
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "TEST";
        public void CloseImeForWindow(IntPtr windowHandle) { }

        public Task<string?> ShowOpenFileDialogAsync(string filterName, string filterExtension)
        {
            OpenCalls++;
            return Task.FromResult(OpenAnswer);
        }

        public Task<string?> ShowSaveFileDialogAsync(string defaultFileName, string filterName, string filterExtension)
        {
            SaveCalls++;
            LastSaveName = defaultFileName;
            LastSaveExtension = filterExtension;
            return Task.FromResult(SaveAnswer);
        }

        public Task OpenWithShellAsync(string path)
        {
            Opened.Add(path);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopLogger : ILoggingService
    {
        public void Info(string m) { }
        public void Warn(string m) { }
        public void Error(string m) { }
        public void Error(string m, Exception ex) { }
        public void Debug(string m) { }
        public void Info(string c, string m) { }
        public void Warn(string c, string m) { }
        public void Error(string c, string m) { }
        public void Error(string c, string m, Exception ex) { }
        public void Debug(string c, string m) { }
        public void StartProcessMirror(string p) { }
        public void StopProcessMirror() { }
    }

    private static string Meta(string dumpedAt) =>
        "{\"kind\":\"meta\",\"module\":\"Game.exe\",\"ue_version\":505,\"dumper_build\":3626,\"dumped_at\":\"" + dumpedAt + "\"}\n";

    private static string Cls(string name, int size) =>
        "{\"kind\":\"class\",\"name\":\"" + name + "\",\"addr\":\"0x1\",\"path\":\"/Game/" + name + "\",\"meta\":\"BlueprintGeneratedClass\"," +
        "\"super\":\"Actor\",\"props_size\":" + size + ",\"props\":[{\"name\":\"Health\",\"type\":\"FloatProperty\",\"offset\":" + size +
        ",\"size\":4}],\"funcs\":[]}\n";

    private const string Summary = "{\"kind\":\"summary\",\"classes_emitted\":2,\"structs_emitted\":0,\"enums_emitted\":0," +
                                   "\"enums_listed\":true,\"enum_names_failed\":false,\"enums_truncated\":false}\n";

    /// <summary>The older dump has AHero; the newer one moved Health and added ANew.</summary>
    private (string Older, string Newer) WritePair()
    {
        var older = Path.Combine(_dir, "Game-2026-01-01.jsonl");
        var newer = Path.Combine(_dir, "Game-2026-02-01.jsonl");
        File.WriteAllText(older, Meta("2026-01-01T00:00:00Z") + Cls("AHero", 64) + Summary);
        File.WriteAllText(newer, Meta("2026-02-01T00:00:00Z") + Cls("AHero", 72) + Cls("ANew", 8) + Summary);
        return (older, newer);
    }

    private static DumpExplorerViewModel Vm(ComparePlatform platform) =>
        new(new StubDumpService(), new NoopLogger(), platform);

    [Fact]
    public async Task Compare_is_offered_only_with_a_class_dump_loaded()
    {
        var platform = new ComparePlatform();
        var vm = Vm(platform);
        Assert.False(vm.CompareCommand.CanExecute(null));

        var (_, newer) = WritePair();
        await vm.LoadFromPathAsync(newer);
        Assert.True(vm.CompareCommand.CanExecute(null));
    }

    [Fact]
    public async Task Compare_writes_the_HTML_report_where_the_user_saves_it_and_opens_it()
    {
        var (older, newer) = WritePair();
        var report = Path.Combine(_dir, "report.html");
        var platform = new ComparePlatform { OpenAnswer = older, SaveAnswer = report };
        var vm = Vm(platform);
        await vm.LoadFromPathAsync(newer);

        await vm.CompareCommand.ExecuteAsync(null);

        Assert.True(File.Exists(report));
        var html = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("ANew", html);
        Assert.Equal(new[] { report }, platform.Opened);
        Assert.EndsWith(".html", platform.LastSaveName);
        Assert.Equal(".html", platform.LastSaveExtension);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task The_dump_taken_earlier_is_the_old_one_whichever_is_loaded()
    {
        var (older, newer) = WritePair();
        var report = Path.Combine(_dir, "report.html");
        // The OLDER dump is the one loaded; the user picks the newer one.
        var platform = new ComparePlatform { OpenAnswer = newer, SaveAnswer = report };
        var vm = Vm(platform);
        await vm.LoadFromPathAsync(older);

        await vm.CompareCommand.ExecuteAsync(null);

        var html = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        Assert.Contains("Added Classes", html);       // ANew is new in the later dump
        Assert.DoesNotContain("Removed Classes", html);
        Assert.True(html.IndexOf("Game-2026-01-01", StringComparison.Ordinal)
                    < html.IndexOf("Game-2026-02-01", StringComparison.Ordinal), "the old file is named first");
    }

    [Fact]
    public async Task The_object_index_is_refused_before_anything_is_written()
    {
        var (_, newer) = WritePair();
        var index = Path.Combine(_dir, "Game.objects.jsonl");
        File.WriteAllText(index, "{\"kind\":\"meta\",\"file\":\"objects\",\"class_dump\":\"Game.jsonl\"}\n");
        var platform = new ComparePlatform { OpenAnswer = index, SaveAnswer = Path.Combine(_dir, "r.html") };
        var vm = Vm(platform);
        await vm.LoadFromPathAsync(newer);

        await vm.CompareCommand.ExecuteAsync(null);

        Assert.Equal(0, platform.SaveCalls);
        Assert.Empty(platform.Opened);
        Assert.False(File.Exists(Path.Combine(_dir, "r.html")));
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Cancelling_either_dialog_writes_and_opens_nothing()
    {
        var (older, newer) = WritePair();
        var platform = new ComparePlatform { OpenAnswer = null };
        var vm = Vm(platform);
        await vm.LoadFromPathAsync(newer);

        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Equal(0, platform.SaveCalls);

        platform.OpenAnswer = older;   // picked, but the save dialog is cancelled
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Equal(1, platform.SaveCalls);
        Assert.Empty(platform.Opened);
        Assert.Empty(Directory.GetFiles(_dir, "*.html"));
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Breaking_changes_only_writes_the_minimal_report()
    {
        var (older, newer) = WritePair();
        var report = Path.Combine(_dir, "report.html");
        var platform = new ComparePlatform { OpenAnswer = older, SaveAnswer = report };
        var vm = Vm(platform);
        vm.DiffBreakingOnly = true;
        await vm.LoadFromPathAsync(newer);

        await vm.CompareCommand.ExecuteAsync(null);

        var html = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        Assert.Contains("Minimal mode", html);
        Assert.DoesNotContain("Added Classes", html);
    }

    [Fact]
    public async Task Include_engine_types_reaches_the_diff()
    {
        var older = Path.Combine(_dir, "e-old.jsonl");
        var newer = Path.Combine(_dir, "e-new.jsonl");
        File.WriteAllText(older, Meta("2026-01-01T00:00:00Z") +
            "{\"kind\":\"class\",\"name\":\"Actor\",\"path\":\"//Script/Engine/Actor\",\"props_size\":16}\n" + Summary);
        File.WriteAllText(newer, Meta("2026-02-01T00:00:00Z") +
            "{\"kind\":\"class\",\"name\":\"Actor\",\"path\":\"//Script/Engine/Actor\",\"props_size\":24}\n" + Summary);
        var report = Path.Combine(_dir, "report.html");
        var platform = new ComparePlatform { OpenAnswer = older, SaveAnswer = report };
        var vm = Vm(platform);
        vm.DiffIncludeEngine = true;
        await vm.LoadFromPathAsync(newer);

        await vm.CompareCommand.ExecuteAsync(null);

        var html = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        Assert.Contains("Engine types: included", html);
        Assert.Contains("Changed Classes (1)", html);
    }

    // ---- the two options persist (D8) ----

    [Fact]
    public void The_options_default_off_and_survive_a_restart()
    {
        var dir = Path.Combine(_dir, "appdata");
        Directory.CreateDirectory(dir);
        var store = new UiOptionsStore(new MockPlatformService(dir));
        var o = store.Load();
        Assert.False(o.DumpExplorer.DiffIncludeEngine);
        Assert.False(o.DumpExplorer.DiffBreakingOnly);

        o.DumpExplorer.DiffIncludeEngine = true;
        o.DumpExplorer.DiffBreakingOnly = true;
        store.Save(o);

        var r = new UiOptionsStore(new MockPlatformService(dir)).Load();
        Assert.True(r.DumpExplorer.DiffIncludeEngine);
        Assert.True(r.DumpExplorer.DiffBreakingOnly);
    }

    [Fact]
    public void The_main_window_saves_and_restores_both_options()
    {
        // MainWindowViewModel cannot be built in a unit test; pin its three persistence sites by source.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "build.ps1"))) root = root.Parent;
        Assert.NotNull(root);
        var src = File.ReadAllText(Path.Combine(root!.FullName, "ui", "UE5DumpUI", "ViewModels", "MainWindowViewModel.cs"));
        Assert.Contains("DumpExplorer.DiffIncludeEngine = o.DumpExplorer.DiffIncludeEngine", src);
        Assert.Contains("DumpExplorer.DiffBreakingOnly = o.DumpExplorer.DiffBreakingOnly", src);
        Assert.Contains("o.DumpExplorer.DiffIncludeEngine = DumpExplorer.DiffIncludeEngine", src);
        Assert.Contains("o.DumpExplorer.DiffBreakingOnly = DumpExplorer.DiffBreakingOnly", src);
        Assert.Contains("Track(DumpExplorer, DumpExplorerPersist)", src);
    }
}
