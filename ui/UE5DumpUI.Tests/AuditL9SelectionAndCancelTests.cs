using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Audit #5 AE14 — the Class/Struct field grid was rebuilt without detaching the
/// selection first, the one panel of five missing the line its siblings carry verbatim.
/// (AE20, filed in the same batch, is covered in ProxyOrphanDeleteRefreshTests, where
/// the real scan-then-delete harness already lives.)
/// </summary>
public class AuditL9SelectionAndCancelTests
{
    private sealed class NoopPlatform : IPlatformService
    {
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => System.IO.Path.GetTempPath();
        public string GetLogDirectoryPath() => System.IO.Path.GetTempPath();
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "TEST";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string a, string b, string c)
            => Task.FromResult<string?>(null);
    }

    // ══════════════════════════════════════════════════════════════════
    // AE14 — Class/Struct was the one panel of five missing the detach
    // ══════════════════════════════════════════════════════════════════

    private sealed class FieldsDump : StubDumpService
    {
        public override Task<ClassInfoModel> WalkClassAsync(string classAddr, CancellationToken ct = default)
            => Task.FromResult(new ClassInfoModel
            {
                Name = "BP_Hero_C", FullPath = "/Game/BP_Hero_C", SuperName = "Character",
                PropertiesSize = 0x400,
                Fields = new List<FieldInfoModel>
                {
                    new() { Name = "Health",  TypeName = "float", Address = "0xAAA0" },
                    new() { Name = "Stamina", TypeName = "float", Address = "0xAAA4" },
                    new() { Name = "Nickname", TypeName = "FString", Address = "0xAAB0" },
                },
            });
    }

    private static async Task<ClassStructViewModel> LoadedPanelAsync()
    {
        var vm = new ClassStructViewModel(new FieldsDump(), new MockLoggingService(), new NoopPlatform());
        await vm.LoadClassCommand.ExecuteAsync("0x1000");
        return vm;
    }

    /// <summary>
    /// Avalonia's DataGrid keeps <c>SelectedItem</c> pointing at a row that is no longer
    /// in <c>ItemsSource</c>, so the xref context menu and the per-field "Find Funcs"
    /// column went on acting on a field the grid had stopped showing. Every sibling
    /// panel detaches first; this one did not.
    ///
    /// NEGATIVE CONTROL: remove <c>SelectedField = null</c> from
    /// <c>ApplyFieldFilter</c> and this fails — the stale FieldInfoModel survives.
    /// </summary>
    [Fact]
    public async Task AE14_filtering_a_selected_field_out_of_the_grid_detaches_it()
    {
        var vm = await LoadedPanelAsync();
        vm.SelectedField = vm.Fields.First(f => f.Name == "Nickname");

        vm.FieldFilter = "health";      // "Nickname" is no longer in Fields

        Assert.DoesNotContain(vm.Fields, f => f.Name == "Nickname");
        Assert.Null(vm.SelectedField);
    }

    /// <summary>The detach is unconditional, exactly as in the four siblings — a row
    /// that SURVIVES the filter is also cleared. Pinned deliberately so nobody
    /// "improves" it into a survivor-preserving variant without deciding to: Instance
    /// Finder's survivor-preserving version needed a whole suppression flag (Z7) to
    /// stop the restore re-issuing a pipe walk, and this panel has no such guard.</summary>
    [Fact]
    public async Task AE14_the_detach_is_unconditional_like_its_four_siblings()
    {
        var vm = await LoadedPanelAsync();
        vm.SelectedField = vm.Fields.First(f => f.Name == "Health");

        vm.FieldFilter = "health";      // "Health" still matches

        Assert.Contains(vm.Fields, f => f.Name == "Health");
        Assert.Null(vm.SelectedField);
    }

    [Fact]
    public async Task AE14_loading_a_class_also_leaves_no_stale_selection()
    {
        var vm = await LoadedPanelAsync();
        vm.SelectedField = vm.Fields.First();
        await vm.LoadClassCommand.ExecuteAsync("0x2000");   // ApplyFieldFilter runs again
        Assert.Null(vm.SelectedField);
    }
}
