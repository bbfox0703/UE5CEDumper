using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

public class BookmarkTests
{
    [Fact]
    public void BookmarkSlot_DefaultValues()
    {
        var slot = new BookmarkSlot { SlotIndex = 0 };

        Assert.False(slot.IsOccupied);
        Assert.Equal("", slot.Label);
        Assert.Equal("", slot.TooltipText);
        Assert.Empty(slot.SavedBreadcrumbs);
        Assert.Equal("", slot.SavedAddress);
        Assert.Equal("", slot.SavedObjectName);
        Assert.Equal("", slot.SavedClassName);
        Assert.Equal("", slot.SavedClassAddr);
        Assert.Null(slot.SavedCachedWorld);
    }

    [Fact]
    public void BookmarkSlot_DisplayNumber_IsOneBased()
    {
        var slot0 = new BookmarkSlot { SlotIndex = 0 };
        var slot3 = new BookmarkSlot { SlotIndex = 3 };

        Assert.Equal(1, slot0.DisplayNumber);
        Assert.Equal(4, slot3.DisplayNumber);
    }

    [Fact]
    public void BookmarkSlot_SetProperties_NotifiesChange()
    {
        var slot = new BookmarkSlot { SlotIndex = 0 };
        var changedProps = new List<string>();
        slot.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        slot.IsOccupied = true;
        slot.Label = "Test";
        slot.TooltipText = "Tip";

        Assert.Contains("IsOccupied", changedProps);
        Assert.Contains("Label", changedProps);
        Assert.Contains("TooltipText", changedProps);
    }

    [Fact]
    public void BookmarkSlot_SetSameValue_DoesNotNotify()
    {
        var slot = new BookmarkSlot { SlotIndex = 0 };
        slot.IsOccupied = false; // Same as default

        var changedProps = new List<string>();
        slot.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        slot.IsOccupied = false; // Set same value again
        Assert.DoesNotContain("IsOccupied", changedProps);
    }

    [Fact]
    public void ViewModel_InitializesWith4EmptyBookmarkSlots()
    {
        var vm = CreateViewModel();

        Assert.Equal(4, vm.BookmarkSlots.Count);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(i, vm.BookmarkSlots[i].SlotIndex);
            Assert.False(vm.BookmarkSlots[i].IsOccupied);
        }
    }

    [Fact]
    public void ToggleBookmarkSaveMode_WithNoData_DoesNothing()
    {
        var vm = CreateViewModel();
        // No breadcrumbs, no address => should not toggle
        vm.ToggleBookmarkSaveModeCommand.Execute(null);
        Assert.False(vm.IsBookmarkSaveMode);
    }

    [Fact]
    public void ToggleBookmarkSaveMode_WithData_TogglesMode()
    {
        var vm = CreateViewModel();
        SetupViewModelWithData(vm);

        vm.ToggleBookmarkSaveModeCommand.Execute(null);
        Assert.True(vm.IsBookmarkSaveMode);

        vm.ToggleBookmarkSaveModeCommand.Execute(null);
        Assert.False(vm.IsBookmarkSaveMode);
    }

    [Fact]
    public void SaveBookmarkToSlot_WithNoData_DoesNotSave()
    {
        var vm = CreateViewModel();
        var slot = vm.BookmarkSlots[0];

        vm.SaveBookmarkToSlotCommand.Execute(slot);

        Assert.False(slot.IsOccupied);
    }

    [Fact]
    public void SaveBookmarkToSlot_WithData_SavesState()
    {
        var vm = CreateViewModel();
        SetupViewModelWithData(vm);
        var slot = vm.BookmarkSlots[1];

        vm.SaveBookmarkToSlotCommand.Execute(slot);

        Assert.True(slot.IsOccupied);
        Assert.Equal("0x12345678", slot.SavedAddress);
        Assert.Equal("TestObject", slot.SavedObjectName);
        Assert.Equal("Actor", slot.SavedClassName);
        Assert.Contains("Actor", slot.TooltipText);
        Assert.Contains("TestObject", slot.TooltipText);
        Assert.False(vm.IsBookmarkSaveMode); // Save mode cleared after save
    }

    [Fact]
    public void SaveBookmarkToSlot_TruncatesLongLabel()
    {
        var vm = CreateViewModel();
        SetupViewModelWithData(vm, objectName: "VeryLongObjectNameThatExceedsFourteenChars");
        var slot = vm.BookmarkSlots[0];

        vm.SaveBookmarkToSlotCommand.Execute(slot);

        Assert.True(slot.Label.Length <= 16 + 2); // 14 chars + ".."
        Assert.EndsWith("..", slot.Label);
    }

    [Fact]
    public void ClearBookmark_ResetsSlot()
    {
        var vm = CreateViewModel();
        SetupViewModelWithData(vm);
        var slot = vm.BookmarkSlots[0];

        vm.SaveBookmarkToSlotCommand.Execute(slot);
        Assert.True(slot.IsOccupied);

        vm.ClearBookmarkCommand.Execute(slot);

        Assert.False(slot.IsOccupied);
        Assert.Equal("", slot.Label);
        Assert.Equal("", slot.TooltipText);
        Assert.Empty(slot.SavedBreadcrumbs);
        Assert.Equal("", slot.SavedAddress);
    }

    [Fact]
    public void ClearAllBookmarks_ClearsAllSlots()
    {
        var vm = CreateViewModel();
        SetupViewModelWithData(vm);

        // Fill all 4 slots
        foreach (var slot in vm.BookmarkSlots)
            vm.SaveBookmarkToSlotCommand.Execute(slot);

        Assert.All(vm.BookmarkSlots, s => Assert.True(s.IsOccupied));

        vm.ClearAllBookmarks();

        Assert.All(vm.BookmarkSlots, s => Assert.False(s.IsOccupied));
        Assert.False(vm.IsBookmarkSaveMode);
    }

    [Fact]
    public void ClearBookmark_NullSlot_DoesNotThrow()
    {
        var vm = CreateViewModel();
        vm.ClearBookmarkCommand.Execute(null);
        // Should not throw
    }

    [Fact]
    public void SaveBookmarkToSlot_PreservesBreadcrumbs()
    {
        var vm = CreateViewModel();
        SetupViewModelWithData(vm, breadcrumbCount: 3);
        var slot = vm.BookmarkSlots[0];

        vm.SaveBookmarkToSlotCommand.Execute(slot);

        Assert.Equal(3, slot.SavedBreadcrumbs.Count);
        Assert.Equal("GWorld", slot.SavedBreadcrumbs[0].Label);
    }

    [Fact]
    public async Task LoadBookmark_InSaveMode_SavesInstead()
    {
        var vm = CreateViewModel();
        SetupViewModelWithData(vm);
        vm.IsBookmarkSaveMode = true;
        var slot = vm.BookmarkSlots[2];

        // LoadBookmark should redirect to save when in save mode
        await vm.LoadBookmarkCommand.ExecuteAsync(slot);

        Assert.True(slot.IsOccupied);
        Assert.False(vm.IsBookmarkSaveMode); // Save mode cleared
    }

    [Fact]
    public async Task LoadBookmark_EmptySlot_DoesNothing()
    {
        var vm = CreateViewModel();
        var slot = vm.BookmarkSlots[0];

        // Should not throw or change state
        await vm.LoadBookmarkCommand.ExecuteAsync(slot);
    }

    // --- Helpers ---

    private static LiveWalkerViewModel CreateViewModel()
    {
        var dump = new StubDumpService();
        var log = new MockLoggingService();
        var platform = new MockPlatformService(Path.GetTempPath());
        return new LiveWalkerViewModel(dump, log, platform);
    }

    private static void SetupViewModelWithData(LiveWalkerViewModel vm,
        string objectName = "TestObject",
        string className = "Actor",
        string address = "0x12345678",
        int breadcrumbCount = 1)
    {
        // Use reflection-free approach: set observable properties via their public setters
        vm.CurrentObjectName = objectName;
        vm.CurrentClassName = className;
        vm.CurrentAddress = address;
        vm.HasData = true;

        vm.Breadcrumbs.Clear();
        vm.Breadcrumbs.Add(new BreadcrumbItem
        {
            Address = "0xGWorld",
            Label = "GWorld",
            IsPointerDeref = true,
            FieldOffset = 0,
            FieldName = "GWorld",
        });

        for (int i = 1; i < breadcrumbCount; i++)
        {
            vm.Breadcrumbs.Add(new BreadcrumbItem
            {
                Address = $"0x{i:X8}",
                Label = $"Object{i}",
                IsPointerDeref = true,
                FieldOffset = i * 8,
                FieldName = $"Field{i}",
            });
        }
    }
}
