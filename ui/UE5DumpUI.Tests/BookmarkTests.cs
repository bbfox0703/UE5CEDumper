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
        Assert.Contains("empty", slot.TooltipText);  // computed empty-slot hint
        Assert.Empty(slot.SavedBreadcrumbs);
        Assert.Equal("", slot.SavedAddress);
        Assert.Equal("", slot.SavedObjectName);
        Assert.Equal("", slot.SavedClassName);
        Assert.Equal("", slot.SavedClassAddr);
        Assert.Null(slot.SavedCachedWorld);
        Assert.Empty(slot.SavedSelectedFields);
        Assert.Null(slot.SavedTopRow);
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

        Assert.Contains("IsOccupied", changedProps);
        Assert.Contains("Label", changedProps);
        // TooltipText is computed; flipping IsOccupied raises its change too.
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
    public void ViewModel_InitializesWith8EmptyBookmarkSlots()
    {
        var vm = CreateViewModel();

        Assert.Equal(UE5DumpUI.Constants.BookmarkSlotCount, vm.BookmarkSlots.Count);
        Assert.Equal(8, vm.BookmarkSlots.Count);
        for (int i = 0; i < vm.BookmarkSlots.Count; i++)
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
    public void SaveBookmarkToSlot_IntoAnOccupiedSlot_RefreshesTheTooltip()
    {
        // [P8-BOOKMARK-TIP] Save mode has no occupied-slot guard, so this is the normal re-save gesture. IsOccupied goes
        // true -> true and raises nothing: the label repainted, the hover kept the PREVIOUS target, a click went to the new.
        var vm = CreateViewModel();
        SetupViewModelWithData(vm, objectName: "FirstObject", address: "0x11111111");
        var slot = vm.BookmarkSlots[0];
        vm.SaveBookmarkToSlotCommand.Execute(slot);
        var raised = new List<string>();
        slot.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        SetupViewModelWithData(vm, objectName: "SecondObject", address: "0x22222222");
        vm.SaveBookmarkToSlotCommand.Execute(slot);

        Assert.Contains(nameof(BookmarkSlot.TooltipText), raised);   // the hover repaints
        Assert.Contains("SecondObject", slot.TooltipText);
        Assert.Contains("0x22222222", slot.TooltipText);
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
        Assert.Contains("empty", slot.TooltipText);  // back to the empty-slot hint
        Assert.Empty(slot.SavedBreadcrumbs);
        Assert.Equal("", slot.SavedAddress);
        Assert.Empty(slot.SavedSelectedFields);
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

    [Fact]
    public void BookmarkSlot_TooltipText_Occupied_ShowsTargetAndJumpHint()
    {
        var vm = CreateViewModel();
        SetupViewModelWithData(vm, objectName: "PlayerPawn", className: "Pawn");
        var slot = vm.BookmarkSlots[0];

        vm.SaveBookmarkToSlotCommand.Execute(slot);

        Assert.Contains("Pawn", slot.TooltipText);
        Assert.Contains("PlayerPawn", slot.TooltipText);
        Assert.Contains("Jump", slot.TooltipText);   // occupied slots invite a jump-back
        Assert.DoesNotContain("empty", slot.TooltipText);
    }

    [Fact]
    public void SaveBookmarkToSlot_CapturesSelectedRows()
    {
        var vm = CreateViewModel();
        SetupViewModelWithData(vm);
        vm.UpdateSelectedFields(new List<LiveFieldValue>
        {
            new() { Name = "Health", Offset = 0x10 },
            new() { Name = "Mana",   Offset = 0x14 },
        });
        var slot = vm.BookmarkSlots[0];

        vm.SaveBookmarkToSlotCommand.Execute(slot);

        Assert.Equal(2, slot.SavedSelectedFields.Count);
        Assert.Contains(slot.SavedSelectedFields, f => f.Name == "Health" && f.Offset == 0x10);
        Assert.Contains(slot.SavedSelectedFields, f => f.Name == "Mana" && f.Offset == 0x14);
    }

    [Fact]
    public void SaveBookmarkToSlot_NoSnapshot_FallsBackToSelectedFieldAnchor()
    {
        var vm = CreateViewModel();
        SetupViewModelWithData(vm);
        // Single-row selection set without a grid SelectionChanged sync.
        vm.SelectedField = new LiveFieldValue { Name = "Stamina", Offset = 0x20 };
        var slot = vm.BookmarkSlots[0];

        vm.SaveBookmarkToSlotCommand.Execute(slot);

        Assert.Single(slot.SavedSelectedFields);
        Assert.Equal("Stamina", slot.SavedSelectedFields[0].Name);
        Assert.Equal(0x20, slot.SavedSelectedFields[0].Offset);
    }

    [Fact]
    public void SaveBookmarkToSlot_CapturesViewAnchorFromView()
    {
        var vm = CreateViewModel();
        SetupViewModelWithData(vm);
        // The View answers the capture request synchronously with the top row.
        vm.CaptureViewAnchor += a => a.TopRow = new BookmarkFieldRef("Stamina", 0x40);
        var slot = vm.BookmarkSlots[0];

        vm.SaveBookmarkToSlotCommand.Execute(slot);

        Assert.NotNull(slot.SavedTopRow);
        Assert.Equal("Stamina", slot.SavedTopRow!.Name);
        Assert.Equal(0x40, slot.SavedTopRow.Offset);
    }

    [Fact]
    public async Task LoadBookmark_RaisesRestoreWithSavedSelectionAndAnchor()
    {
        var vm = CreateViewModel();
        SetupViewModelWithData(vm);
        vm.UpdateSelectedFields(new List<LiveFieldValue> { new() { Name = "Gold", Offset = 0x30 } });
        vm.CaptureViewAnchor += a => a.TopRow = new BookmarkFieldRef("HeaderRow", 0x0);
        var slot = vm.BookmarkSlots[0];
        vm.SaveBookmarkToSlotCommand.Execute(slot);

        IReadOnlyList<BookmarkFieldRef>? gotSel = null;
        BookmarkFieldRef? gotTop = null;
        var raised = false;
        vm.RestoreBookmarkView += (sel, top) => { gotSel = sel; gotTop = top; raised = true; };

        await vm.LoadBookmarkCommand.ExecuteAsync(slot);

        Assert.True(raised);
        Assert.NotNull(gotSel);
        Assert.Single(gotSel!);
        Assert.Equal("Gold", gotSel![0].Name);
        Assert.NotNull(gotTop);
        Assert.Equal("HeaderRow", gotTop!.Name);
    }

    // --- PLV_game routing bug: a deeper crumb sharing the UWorld address ---

    [Fact]
    public async Task LoadBookmark_DeepCrumbAtWorldAddress_WalksInstance_NotActorList()
    {
        // Regression: navigating PersistentLevel -> OwningWorld lands on a crumb
        // whose address equals the UWorld address. Loading that bookmark must walk
        // it as an instance, NOT swap in the GWorld actor-list view ("PLV_game").
        var dump = new StubDumpService();
        dump.RegisterStruct("0xA8B0", new InstanceWalkResult
        {
            Name = "PLV_game", ClassName = "World", Address = "0xA8B0",
            Fields = new List<LiveFieldValue> { new() { Name = "OwningGameInstance", Offset = 0x10 } },
        });
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(),
            new MockPlatformService(Path.GetTempPath()));

        var slot = vm.BookmarkSlots[0];
        slot.SavedCachedWorld = new WorldWalkResult
        {
            WorldAddr = "0xA8B0", WorldName = "PLV_game",
            LevelAddr = "0x4500", LevelName = "PersistentLevel", LevelOffset = 0x30,
        };
        slot.SavedBreadcrumbs = new List<BreadcrumbItem>
        {
            new() { Address = "0xA8B0", Label = "GWorld", FieldName = "GWorld", IsPointerDeref = true },
            new() { Address = "0x4500", Label = "PersistentLevel", FieldName = "PersistentLevel", IsPointerDeref = true, FieldOffset = 0x30 },
            new() { Address = "0xA8B0", Label = "OwningWorld", FieldName = "OwningWorld", IsPointerDeref = true, FieldOffset = 0xB8 },
        };
        slot.SavedAddress = "0xA8B0";
        slot.IsOccupied = true;

        await vm.LoadBookmarkCommand.ExecuteAsync(slot);

        Assert.Contains(vm.Fields, f => f.Name == "OwningGameInstance");  // instance walk
        Assert.Equal("World", vm.CurrentClassName);
        Assert.DoesNotContain(vm.Fields, f => f.Name == "PersistentLevel"); // actor-list absent
    }

    [Fact]
    public async Task LoadBookmark_GWorldRoot_ShowsActorList()
    {
        // Control: the genuine GWorld root (FieldName=="GWorld", sole crumb) DOES
        // restore the synthetic actor-list view.
        var dump = new StubDumpService();
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(),
            new MockPlatformService(Path.GetTempPath()));

        var slot = vm.BookmarkSlots[0];
        slot.SavedCachedWorld = new WorldWalkResult
        {
            WorldAddr = "0xA8B0", WorldName = "PLV_game",
            LevelAddr = "0x4500", LevelName = "PersistentLevel", LevelOffset = 0x30,
        };
        slot.SavedBreadcrumbs = new List<BreadcrumbItem>
        {
            new() { Address = "0xA8B0", Label = "GWorld", FieldName = "GWorld", IsPointerDeref = true },
        };
        slot.SavedAddress = "0xA8B0";
        slot.IsOccupied = true;

        await vm.LoadBookmarkCommand.ExecuteAsync(slot);

        Assert.Equal("UWorld", vm.CurrentClassName);
        Assert.Contains(vm.Fields, f => f.Name == "PersistentLevel");  // actor-list signature
    }

    // --- Cross-restart spine re-resolution ---

    [Fact]
    public async Task LoadBookmark_DeepContainerSpine_ReresolvesAfterGameRestart()
    {
        // A deep bookmark saved in a PREVIOUS game session: its saved addresses are all
        // stale (the game restarted, ASLR moved everything). The spine — GWorld → GameState
        // → PlayerArray[0] → PawnPrivate — is stable, so loading must re-walk it from the
        // LIVE GWorld and land on the current player object, not the dead saved address.
        var dump = new StubDumpService();   // WalkWorldAsync default → WorldAddr "0xA8B0"

        dump.RegisterStruct("0xA8B0", new InstanceWalkResult   // live UWorld
        {
            Name = "World", ClassName = "World", Address = "0xA8B0",
            Fields = new()
            {
                new() { Name = "GameState", Offset = 432, TypeName = "ObjectProperty", PtrAddress = "0xGS_LIVE" },
            },
        });
        dump.RegisterStruct("0xGS_LIVE", new InstanceWalkResult   // live AGameState
        {
            Name = "GameState", ClassName = "GameState", Address = "0xGS_LIVE",
            Fields = new()
            {
                new()
                {
                    Name = "PlayerArray", Offset = 736, TypeName = "ArrayProperty",
                    ArrayCount = 1, ArrayInnerType = "ObjectProperty",
                    ArrayElements = new() { new() { Index = 0, PtrAddress = "0xPS_LIVE" } },
                },
            },
        });
        dump.RegisterStruct("0xPS_LIVE", new InstanceWalkResult   // live APlayerState
        {
            Name = "PlayerState", ClassName = "PlayerState", Address = "0xPS_LIVE",
            Fields = new()
            {
                new() { Name = "PawnPrivate", Offset = 832, TypeName = "ObjectProperty", PtrAddress = "0xPAWN_LIVE" },
            },
        });
        dump.RegisterStruct("0xPAWN_LIVE", new InstanceWalkResult   // live player pawn
        {
            Name = "BP_PlayerCharacter", ClassName = "BP_PlayerCharacter_C", Address = "0xPAWN_LIVE",
            Fields = new()
            {
                new() { Name = "Health", Offset = 100, TypeName = "FloatProperty", TypedValue = "513" },
            },
        });

        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(),
            new MockPlatformService(Path.GetTempPath()));

        var slot = vm.BookmarkSlots[0];
        slot.SavedClassName = "BP_PlayerCharacter_C";
        slot.SavedObjectName = "BP_PlayerCharacter";
        slot.SavedAddress = "0xDEADPAWN";   // stale, from the previous session
        slot.SavedBreadcrumbs = new List<BreadcrumbItem>
        {
            new() { Address = "0xDEADWORLD", Label = "GWorld", FieldName = "GWorld", IsPointerDeref = true },
            new() { Address = "0xDEADGS", Label = "GameState", FieldName = "GameState", FieldOffset = 432, IsPointerDeref = true, TargetClassName = "GameState" },
            new() { Address = "0xDEADGS", Label = "PlayerArray", FieldName = "PlayerArray", FieldOffset = 736, IsContainerView = true },
            new() { Address = "0xDEADPS", Label = "PlayerState", FieldName = "[0]", FieldOffset = 0, IsPointerDeref = true, TargetClassName = "PlayerState" },
            new() { Address = "0xDEADPAWN", Label = "BP_PlayerCharacter", FieldName = "PawnPrivate", FieldOffset = 832, IsPointerDeref = true, TargetClassName = "BP_PlayerCharacter_C" },
        };
        slot.SavedSelectedFields = new() { new BookmarkFieldRef("Health", 100) };
        slot.IsOccupied = true;

        IReadOnlyList<BookmarkFieldRef>? gotSel = null;
        vm.RestoreBookmarkView += (sel, _) => gotSel = sel;

        await vm.LoadBookmarkCommand.ExecuteAsync(slot);

        // Landed on the LIVE pawn via the re-walked spine, not the dead saved address.
        Assert.Equal("BP_PlayerCharacter_C", vm.CurrentClassName);
        Assert.Contains(vm.Fields, f => f.Name == "Health");
        Assert.Equal("0xPAWN_LIVE", vm.Breadcrumbs[^1].Address);   // fresh, not "0xDEADPAWN"
        Assert.Equal("0xGS_LIVE", vm.Breadcrumbs[1].Address);      // every hop re-resolved
        Assert.NotNull(gotSel);                                    // selection restored (not stale)
        Assert.Contains(gotSel!, f => f.Name == "Health");
    }

    [Fact]
    public async Task LoadBookmark_SpineHopNoLongerMatches_FallsBackAndReportsStale()
    {
        // Re-resolution can't match a moved/renamed hop → it bails to the saved addresses;
        // after a restart those are dead, the walked class mismatches SavedClassName, and the
        // bookmark is reported stale (kept, selection dropped) rather than showing wrong data.
        var dump = new StubDumpService();   // WalkWorldAsync → "0xA8B0"
        dump.RegisterStruct("0xA8B0", new InstanceWalkResult   // live UWorld: no "GameState" field
        {
            Name = "World", ClassName = "World", Address = "0xA8B0",
            Fields = new()
            {
                new() { Name = "SomethingElse", Offset = 432, TypeName = "ObjectProperty", PtrAddress = "0xX" },
            },
        });
        // The stale saved leaf resolves to a DIFFERENT class (wrong object).
        dump.RegisterStruct("0xDEADGS", new InstanceWalkResult
        {
            Name = "Garbage", ClassName = "SomeOtherClass", Address = "0xDEADGS",
            Fields = new(),
        });

        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(),
            new MockPlatformService(Path.GetTempPath()));
        var slot = vm.BookmarkSlots[0];
        slot.SavedClassName = "GameState";
        slot.SavedAddress = "0xDEADGS";
        slot.SavedBreadcrumbs = new List<BreadcrumbItem>
        {
            new() { Address = "0xDEADWORLD", Label = "GWorld", FieldName = "GWorld", IsPointerDeref = true },
            new() { Address = "0xDEADGS", Label = "GameState", FieldName = "GameState", FieldOffset = 432, IsPointerDeref = true, TargetClassName = "GameState" },
        };
        slot.SavedSelectedFields = new() { new BookmarkFieldRef("Score", 200) };
        slot.IsOccupied = true;

        var restoreRaised = false;
        vm.RestoreBookmarkView += (_, _) => restoreRaised = true;

        await vm.LoadBookmarkCommand.ExecuteAsync(slot);

        Assert.False(restoreRaised);   // selection NOT restored — bookmark is stale
        Assert.Contains("stale", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("0xDEADGS", vm.Breadcrumbs[^1].Address);   // kept the saved (fallback) address
    }

    [Fact]
    public async Task LoadBookmark_NonGWorldRoot_FallsBackToSavedAddress()
    {
        // An address-rooted ("Custom") bookmark has no stable live anchor; re-resolution
        // returns null and the load uses the saved address (same-process fast path).
        var dump = new StubDumpService();
        dump.RegisterStruct("0xC0DE", new InstanceWalkResult
        {
            Name = "Widget", ClassName = "UserWidget", Address = "0xC0DE",
            Fields = new() { new() { Name = "Opacity", Offset = 80, TypeName = "FloatProperty", TypedValue = "1" } },
        });

        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(),
            new MockPlatformService(Path.GetTempPath()));
        var slot = vm.BookmarkSlots[0];
        slot.SavedClassName = "UserWidget";
        slot.SavedAddress = "0xC0DE";
        slot.SavedBreadcrumbs = new List<BreadcrumbItem>
        {
            new() { Address = "0xC0DE", Label = "Widget", FieldName = "0xC0DE", IsPointerDeref = true },
        };
        slot.IsOccupied = true;

        await vm.LoadBookmarkCommand.ExecuteAsync(slot);

        Assert.Equal("UserWidget", vm.CurrentClassName);
        Assert.Equal("0xC0DE", vm.Breadcrumbs[^1].Address);
    }

    [Fact]
    public async Task LoadBookmark_GameEngineRoot_ReresolvesViaEngineAnchor()
    {
        // A bookmark whose spine was rooted on the live UGameEngine (Start-from-GameEngine
        // or Locate-in-GameEngine): the root crumb's FieldName is "GameEngine", so after a
        // restart re-resolution must re-anchor via ResolveGameEngineAsync, not WalkWorldAsync.
        var dump = new EngineRootStubDumpService(new GameEngineResult
        {
            Found = true, Address = "0xENGINELIVE", ClassName = "GameEngine",
            GameViewportOk = true, GameInstanceOk = true,
        });
        dump.RegisterStruct("0xENGINELIVE", new InstanceWalkResult
        {
            Name = "GameEngine", ClassName = "GameEngine", Address = "0xENGINELIVE",
            Fields = new()
            {
                new() { Name = "GameInstance", Offset = 0xC00, TypeName = "ObjectProperty", PtrAddress = "0xGILIVE" },
            },
        });
        dump.RegisterStruct("0xGILIVE", new InstanceWalkResult
        {
            Name = "GameInstance", ClassName = "MyGameInstance", Address = "0xGILIVE",
            Fields = new()
            {
                new() { Name = "PlayerCount", Offset = 0x40, TypeName = "IntProperty", TypedValue = "1" },
            },
        });

        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(),
            new MockPlatformService(Path.GetTempPath()));
        var slot = vm.BookmarkSlots[0];
        slot.SavedClassName = "MyGameInstance";
        slot.SavedAddress = "0xDEADGI";
        slot.SavedBreadcrumbs = new List<BreadcrumbItem>
        {
            new() { Address = "0xDEADENGINE", Label = "GameEngine", FieldName = "GameEngine", IsPointerDeref = true },
            new() { Address = "0xDEADGI", Label = "GameInstance", FieldName = "GameInstance", FieldOffset = 0xC00, IsPointerDeref = true, TargetClassName = "MyGameInstance" },
        };
        slot.IsOccupied = true;

        await vm.LoadBookmarkCommand.ExecuteAsync(slot);

        Assert.Equal("MyGameInstance", vm.CurrentClassName);
        Assert.Equal("0xGILIVE", vm.Breadcrumbs[^1].Address);   // re-resolved via the engine anchor
        Assert.Contains(vm.Fields, f => f.Name == "PlayerCount");
    }

    // --- Spine re-resolution helpers (unit) ---

    [Fact]
    public void MatchSpineField_NameAndOffset_PrefersExact()
    {
        var walk = new InstanceWalkResult
        {
            Fields = new()
            {
                new() { Name = "HP", Offset = 0x10 },
                new() { Name = "HP", Offset = 0x20 },   // duplicate name, different offset
            },
        };
        var crumb = new BreadcrumbItem { FieldName = "HP", FieldOffset = 0x20 };
        var hit = LiveWalkerViewModel.MatchSpineField(walk, crumb);
        Assert.NotNull(hit);
        Assert.Equal(0x20, hit!.Offset);
    }

    [Fact]
    public void MatchSpineField_OffsetShifted_UniqueNameFallback()
    {
        // Offset moved across a game patch but the name is unique → still resolves.
        var walk = new InstanceWalkResult
        {
            Fields = new() { new() { Name = "Mana", Offset = 0x44 } },
        };
        var crumb = new BreadcrumbItem { FieldName = "Mana", FieldOffset = 0x40 };
        var hit = LiveWalkerViewModel.MatchSpineField(walk, crumb);
        Assert.NotNull(hit);
        Assert.Equal(0x44, hit!.Offset);
    }

    [Fact]
    public void MatchSpineField_AmbiguousNameNoOffset_Refuses()
    {
        var walk = new InstanceWalkResult
        {
            Fields = new()
            {
                new() { Name = "Slot", Offset = 0x10 },
                new() { Name = "Slot", Offset = 0x18 },
            },
        };
        var crumb = new BreadcrumbItem { FieldName = "Slot", FieldOffset = 0x99 };  // no exact match
        Assert.Null(LiveWalkerViewModel.MatchSpineField(walk, crumb));
    }

    [Fact]
    public void ResolveContainerElementHop_ObjectArray_ReturnsElementPtr()
    {
        var arr = new LiveFieldValue
        {
            Name = "SpawnedAttributes", ArrayCount = 3, ArrayInnerType = "ObjectProperty",
            ArrayElements = new()
            {
                new() { Index = 0, PtrAddress = "0xA" },
                new() { Index = 2, PtrAddress = "0xC" },
            },
        };
        var hop = LiveWalkerViewModel.ResolveContainerElementHop(arr, 2, "[2]");
        Assert.NotNull(hop);
        Assert.Equal("0xC", hop!.Value.Addr);
        Assert.Equal("", hop.Value.ClassAddr);   // object ptr — DLL resolves class
    }

    [Fact]
    public void ResolveContainerElementHop_StructArray_ComputesElementAddr()
    {
        var arr = new LiveFieldValue
        {
            Name = "Tunes", ArrayCount = 4, ArrayInnerType = "StructProperty",
            ArrayStructClassAddr = "0xSTRUCT", ArrayDataAddr = "0x1000", ArrayElemSize = 0x20,
        };
        var hop = LiveWalkerViewModel.ResolveContainerElementHop(arr, 2, "[2]");
        Assert.NotNull(hop);
        Assert.Equal("0x1040", hop!.Value.Addr);   // 0x1000 + 2*0x20
        Assert.Equal("0xSTRUCT", hop.Value.ClassAddr);
    }

    [Fact]
    public void ResolveContainerElementHop_NullPointerElement_ReturnsNull()
    {
        var arr = new LiveFieldValue
        {
            Name = "Refs", ArrayCount = 1, ArrayInnerType = "ObjectProperty",
            ArrayElements = new() { new() { Index = 0, PtrAddress = "0x0" } },
        };
        Assert.Null(LiveWalkerViewModel.ResolveContainerElementHop(arr, 0, "[0]"));
    }

    [Theory]
    [InlineData("[0]", true)]
    [InlineData("[12].Value", true)]
    [InlineData("PawnPrivate", false)]
    [InlineData("", false)]
    public void IsElementCrumb_DetectsBracketNames(string name, bool expected)
        => Assert.Equal(expected, LiveWalkerViewModel.IsElementCrumb(name));

    [Theory]
    [InlineData("0x1040", 0x1040UL)]
    [InlineData("0X20", 0x20UL)]
    [InlineData("", 0UL)]
    [InlineData("notahex", 0UL)]
    public void ParseHexAddr_ParsesOrZero(string addr, ulong expected)
        => Assert.Equal(expected, LiveWalkerViewModel.ParseHexAddr(addr));

    // --- Helpers ---

    /// <summary>StubDumpService whose ResolveGameEngineAsync returns a configured engine
    /// (the base throws), for testing GameEngine-rooted bookmark re-resolution.</summary>
    private sealed class EngineRootStubDumpService : StubDumpService
    {
        private readonly GameEngineResult _engine;
        public EngineRootStubDumpService(GameEngineResult engine) => _engine = engine;
        public override Task<GameEngineResult> ResolveGameEngineAsync(CancellationToken ct = default)
            => Task.FromResult(_engine);
    }

    // --- [W4-BOOKMARK-DT] a bookmark saved on a DataTable row view ---
    //
    // PersistedCrumb carried IsContainerView but not IsDataTableView, and a DataTable row view's rows
    // (DataTableData) are live-only. A bookmark restored from the FILE therefore came back as a plain
    // container crumb with nothing to show, fell through to an instance walk, failed the class check and
    // reported "stale (game may have restarted)" -- blaming the session for a serialisation gap.

    /// <summary>Answers the DataTable row walk the restore now makes, and counts it.</summary>
    private sealed class DataTableDump : StubDumpService
    {
        public string RowStruct = "FItemRow";
        public bool Refuse;
        public int RowWalks;

        public override Task<DataTableWalkResult> WalkDataTableRowsAsync(
            string addr, int offset = 0, int limit = 64, CancellationToken ct = default)
        {
            RowWalks++;
            if (Refuse) throw new InvalidOperationException("not a DataTable (class=Actor)");
            return Task.FromResult(new DataTableWalkResult
            {
                RowCount = 2, RowStructName = RowStruct, RowMapOffset = 0x30,
                Rows = new()
                {
                    new DataTableRowInfo { RowName = "Sword",  DataAddr = "0x1000", Fields = new() },
                    new DataTableRowInfo { RowName = "Shield", DataAddr = "0x2000", Fields = new() },
                },
            });
        }
    }

    private static LiveWalkerViewModel DataTableVm(DataTableDump dump)
        => new(dump, new MockLoggingService(), new MockPlatformService(Path.GetTempPath()));

    /// <summary>A slot shaped the way the FILE hydrates a DataTable row view: the flag, no live rows.
    /// <paramref name="liveRows"/> gives it the in-session shape instead.</summary>
    private static void FillDataTableSlot(BookmarkSlot slot, DataTableWalkResult? liveRows = null)
    {
        slot.SavedBreadcrumbs = new List<BreadcrumbItem>
        {
            new() { Address = "0xDA7A", Label = "DT_Items", FieldName = "DT_Items", IsPointerDeref = true },
            new()
            {
                Address = "0xDA7A", Label = "RowMap [2 x FItemRow]", FieldName = "RowMap", FieldOffset = 0x30,
                IsContainerView = true, IsDataTableView = true,
                DataTableData = liveRows,
                ContainerField = liveRows != null ? new LiveFieldValue { Name = "RowMap", Offset = 0x30 } : null,
            },
        };
        slot.SavedAddress = "0xDA7A";
        slot.SavedClassName = "DataTable<FItemRow>";
        slot.IsOccupied = true;
    }

    [Fact]
    public void DataTableViewBookmark_KeepsItsKindThroughTheFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"UE5DumpBmDt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var platform = new MockPlatformService(dir);
            var vm1 = new LiveWalkerViewModel(new StubDumpService(), new MockLoggingService(), platform,
                                              bookmarks: new BookmarkStore(platform));
            vm1.LoadBookmarksForGame("PE");
            SetupViewModelWithData(vm1, objectName: "RowMap", className: "DataTable<FItemRow>", address: "0xDA7A");
            vm1.Breadcrumbs.Add(new BreadcrumbItem
            {
                Address = "0xDA7A", Label = "RowMap [2 x FItemRow]", FieldName = "RowMap", FieldOffset = 0x30,
                IsContainerView = true, IsDataTableView = true,
            });
            vm1.SaveBookmarkToSlotCommand.Execute(vm1.BookmarkSlots[0]);

            var vm2 = new LiveWalkerViewModel(new StubDumpService(), new MockLoggingService(), platform,
                                              bookmarks: new BookmarkStore(platform));   // an app restart
            vm2.LoadBookmarksForGame("PE");

            Assert.True(vm2.BookmarkSlots[0].IsOccupied);
            Assert.True(vm2.BookmarkSlots[0].SavedBreadcrumbs[^1].IsDataTableView);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task DataTableViewBookmark_FromTheFile_ReWalksItsRows()
    {
        var dump = new DataTableDump();
        var vm = DataTableVm(dump);
        var slot = vm.BookmarkSlots[0];
        FillDataTableSlot(slot);

        await vm.LoadBookmarkCommand.ExecuteAsync(slot);

        Assert.Equal(1, dump.RowWalks);
        Assert.Equal("DataTable<FItemRow>", vm.CurrentClassName);
        Assert.Contains(vm.Fields, f => f.Name.Contains("Sword"));
        Assert.Contains("loaded", vm.StatusText);
        // The restored crumb has the in-session shape, so Refresh and Back treat it the same.
        Assert.NotNull(vm.Breadcrumbs[^1].DataTableData);
        Assert.NotNull(vm.Breadcrumbs[^1].ContainerField);
    }

    [Theory]
    [InlineData(false)]   // a DIFFERENT DataTable now sits at the saved address
    [InlineData(true)]    // the DLL refuses the address: it is not a DataTable any more
    public async Task DataTableViewBookmark_WhoseTableIsGone_SaysSo_NotAGameRestart(bool refuse)
    {
        var dump = new DataTableDump { RowStruct = "FOtherRow", Refuse = refuse };
        var vm = DataTableVm(dump);
        var slot = vm.BookmarkSlots[0];
        FillDataTableSlot(slot);

        await vm.LoadBookmarkCommand.ExecuteAsync(slot);

        Assert.DoesNotContain("restarted", vm.StatusText);
        Assert.Contains("DataTable", vm.StatusText);
        Assert.NotEqual("DataTable<FOtherRow>", vm.CurrentClassName);   // the wrong table is not shown as the bookmark
    }

    [Theory]
    [InlineData(false)]   // a DIFFERENT DataTable now sits at the saved address
    [InlineData(true)]    // the DLL refuses the address: it is not a DataTable any more
    public async Task DataTableViewBookmark_WhoseTableIsGone_IsNotReWalkedByRefresh(bool refuse)
    {
        // Review of 34681166: the guard's reject left the file-shaped crumb (IsDataTableView, no rows) at the
        // end of the spine, and Refresh's DataTable branch re-walked that address with NO row-struct check.
        var dump = new DataTableDump { RowStruct = "FOtherRow", Refuse = refuse };
        var vm = DataTableVm(dump);
        SetupViewModelWithData(vm, objectName: "Prev", className: "Actor", address: "0xBEEF");   // a prior view, so Refresh runs
        var slot = vm.BookmarkSlots[0];
        FillDataTableSlot(slot);
        await vm.LoadBookmarkCommand.ExecuteAsync(slot);
        Assert.Equal(1, dump.RowWalks);                         // the guarded re-walk, rejected

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(1, dump.RowWalks);                         // no second, unguarded walk
        Assert.DoesNotContain(vm.Fields, f => f.Name.Contains("Sword"));
        Assert.Contains("gone or has changed", vm.StatusText);
    }

    [Fact]
    public async Task DataTableViewBookmark_InSession_UsesItsCachedRows()
    {
        // The control, green before and after: an in-memory bookmark still holds its rows.
        var dump = new DataTableDump();
        var vm = DataTableVm(dump);
        var slot = vm.BookmarkSlots[0];
        FillDataTableSlot(slot, new DataTableWalkResult
        {
            RowCount = 1, RowStructName = "FItemRow",
            Rows = new() { new DataTableRowInfo { RowName = "Cached", DataAddr = "0x3000", Fields = new() } },
        });

        await vm.LoadBookmarkCommand.ExecuteAsync(slot);

        Assert.Equal(0, dump.RowWalks);
        Assert.Contains(vm.Fields, f => f.Name.Contains("Cached"));
    }

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
