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
/// The Class/Struct field grid detaches its selection before a rebuild, as its sibling
/// panels do. (Cancelling the destructive orphan delete is covered in
/// ProxyOrphanDeleteRefreshTests, where the real scan-then-delete harness lives.)
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
    // Class/Struct detaches the selection before a rebuild
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
    /// in <c>ItemsSource</c>, so without the detach the xref context menu and the
    /// per-field "Find Funcs" column would go on acting on a field the grid has stopped
    /// showing. Every sibling panel detaches first too.
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

    /// <summary>A rebuild still detaches every row first, a SURVIVING one included — the
    /// selection model needs that. Deliberately ([KEYWORD-BOX-VIEW-KEEP]), the View is then
    /// handed the surviving row to re-select. That is safe HERE because selecting a field
    /// starts nothing; Instance Finder's survivor-preserving version needs a suppression
    /// flag to stop the restore re-issuing a pipe walk, and anything with such a side
    /// effect must re-select in its view model instead.</summary>
    [Fact]
    public async Task AE14_a_rebuild_detaches_a_surviving_row_and_hands_it_to_the_View()
    {
        var vm = await LoadedPanelAsync();
        var health = vm.Fields.First(f => f.Name == "Health");
        vm.SelectedField = health;
        var handedBack = new List<object>();
        vm.FieldsView.CaptureView = () => new UE5DumpUI.Helpers.FilterViewState(
            vm.SelectedField is { } s ? new object[] { s } : Array.Empty<object>(), null);
        vm.FieldsView.RestoreView = (state, _) => handedBack.AddRange(state.Selected);

        vm.FieldFilter = "health";      // "Health" still matches

        Assert.Contains(vm.Fields, f => f.Name == "Health");
        Assert.Null(vm.SelectedField);
        Assert.Same(health, Assert.Single(handedBack));
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
