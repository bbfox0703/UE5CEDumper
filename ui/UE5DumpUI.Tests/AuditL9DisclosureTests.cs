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
/// Audit #5 L9 (segment T1c) — the findings whose theme is "a signal the code already
/// computed never reaches the user", plus the two window/selection races in the same
/// batch. Grouped here because they share a vocabulary
/// (<see cref="PartialResultNotice"/>) rather than a file.
///
/// <para>
/// Each behavioural test has a stated negative control: what it asserts must be FALSE
/// against the pre-fix code. Where the pre-fix behaviour is "the string simply is not
/// there", the control is the <c>DoesNotContain</c> half of a sibling assertion.
/// </para>
/// </summary>
public class AuditL9DisclosureTests
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
    // AE30 — ModuleOffset must not dress a heap address as an RVA
    // ══════════════════════════════════════════════════════════════════

    private const string Mod = "TQ2-Win64-Shipping.exe";
    private const string Base = "0x7FF6BA800000";

    [Fact]
    public void AE30_in_module_address_still_formats_as_module_plus_rva()
    {
        // The whole point of the option: an address inside the image keeps working.
        var s = AddressHelper.FormatAddress("0x7FF6BA801234", Mod, Base, AddressFormat.ModuleOffset);
        Assert.Equal("\"TQ2-Win64-Shipping.exe\"+1234", s);
    }

    [Fact]
    public void AE30_module_base_itself_is_rva_zero()
    {
        var s = AddressHelper.FormatAddress(Base, Mod, Base, AddressFormat.ModuleOffset);
        Assert.Equal("\"TQ2-Win64-Shipping.exe\"+0", s);
    }

    /// <summary>
    /// The defect. A UObject on the heap sits BELOW a 0x7FF7… image base, so the
    /// unsigned subtraction wrapped and produced "…exe"+FFFF81EDA0608D40 — a string
    /// that resolves correctly this run and to garbage after a relaunch, while looking
    /// exactly like the ASLR-stable form the user asked for.
    /// </summary>
    [Fact]
    public void AE30_heap_address_below_the_base_is_not_claimed_to_be_module_relative()
    {
        var s = AddressHelper.FormatAddress("0x1E55C298D40", Mod, Base, AddressFormat.ModuleOffset);
        Assert.DoesNotContain(Mod, s);           // pre-fix: "TQ2-Win64-Shipping.exe"+FFFF...
        Assert.DoesNotContain("+", s);
        Assert.Equal("1E55C298D40", s);          // the honest absolute form
    }

    [Fact]
    public void AE30_an_offset_wider_than_a_PE_image_is_refused()
    {
        // SizeOfImage is a DWORD even in PE32+, so no RVA can exceed 0xFFFFFFFF.
        // base = 0x100000000; addr - base is exactly 0xFFFFFFFF at 0x1FFFFFFFF.
        Assert.True(AddressHelper.TryGetModuleRva("0x1FFFFFFFF", "0x100000000", out var ok));
        Assert.Equal(0xFFFFFFFFUL, ok);          // exactly the largest possible RVA

        Assert.False(AddressHelper.TryGetModuleRva("0x200000000", "0x100000000", out _)); // one past
    }

    [Fact]
    public void AE30_garbage_input_is_refused_rather_than_thrown()
    {
        // Convert.ToUInt64 threw here; a clipboard format is not worth an exception
        // out of a fire-and-forget command.
        Assert.False(AddressHelper.TryGetModuleRva("0xzzzz", Base, out _));
        Assert.False(AddressHelper.TryGetModuleRva("", Base, out _));
        Assert.False(AddressHelper.TryGetModuleRva("0x1234", "not-hex", out _));
        var s = AddressHelper.FormatAddress("0xzzzz", Mod, Base, AddressFormat.ModuleOffset);
        Assert.Equal("zzzz", s);
    }

    [Fact]
    public void AE30_missing_module_info_still_falls_back_exactly_as_before()
    {
        // Control: the pre-existing fallback branch must not have moved.
        Assert.Equal("1E55C298D40",
            AddressHelper.FormatAddress("0x1E55C298D40", null, null, AddressFormat.ModuleOffset));
    }

    // ══════════════════════════════════════════════════════════════════
    // The three new PartialResultNotice clauses share the class's rules:
    // the marker is ⚠, the separator is an em dash, and the CAUSE is named
    // before the consequence.
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void New_notices_follow_the_house_rules()
    {
        var all = new[]
        {
            PartialResultNotice.PerSlotWitnessCap(256),
            PartialResultNotice.PerSlotWitnessCap(0),
            PartialResultNotice.InheritedTruncation(),
            PartialResultNotice.DerivedListFromCappedPage("Super / Package suggestions", 5000, 6609, "classes"),
        };
        foreach (var s in all)
        {
            Assert.Contains("⚠", s);
            Assert.Contains("—", s);             // em dash, not a hyphen
            Assert.DoesNotContain(" - ", s);
        }
    }

    [Fact]
    public void AE13_witness_cap_notice_names_the_cap_when_the_DLL_sent_one()
    {
        Assert.Contains("256", PartialResultNotice.PerSlotWitnessCap(256));
        // An older DLL sends the flag with no number; the sentence must not invent "0".
        Assert.DoesNotContain("0 fields", PartialResultNotice.PerSlotWitnessCap(0));
    }

    [Fact]
    public void AE15_derived_list_notice_names_both_numbers()
    {
        var s = PartialResultNotice.DerivedListFromCappedPage("Super / Package suggestions",
                                                              5000, 6609, "classes");
        Assert.Contains("5,000", s);
        Assert.Contains("6,609", s);
        Assert.Contains("Super / Package suggestions", s);
    }

    // ══════════════════════════════════════════════════════════════════
    // AE15 — the suggestion dropdowns must admit they are a sample
    // ══════════════════════════════════════════════════════════════════

    private sealed class ClassListDump : StubDumpService
    {
        public bool Truncated;
        public override Task<ClassListResult> ListClassesAsync(
            bool gameOnly = true, int limit = 5000, CancellationToken ct = default)
            => Task.FromResult(new ClassListResult
            {
                Classes = new List<GameClassEntry>
                {
                    new() { ClassName = "BP_Hero_C",  SuperName = "Character", ClassPath = "/Game/Chars/BP_Hero_C" },
                    new() { ClassName = "BP_Enemy_C", SuperName = "Pawn",      ClassPath = "/Game/Chars/BP_Enemy_C" },
                },
                Total = 2,
                TotalClasses = Truncated ? 6609 : 2,
                RequestedLimit = limit,
                Truncated = Truncated,
                ScannedObjects = 1000,
            });
    }

    [Fact]
    public async Task AE15_truncated_class_walk_marks_the_suggestion_lists_as_a_sample()
    {
        var vm = new GameClassFilterViewModel(new ClassListDump { Truncated = true },
                                              new MockLoggingService(), new NoopPlatform());
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.SuperSuggestions);          // the lists still populate
        Assert.NotEqual("", vm.SuggestionsNote);       // ... and now say what they are
        Assert.Contains("⚠", vm.SuggestionsNote);
        Assert.Contains("6,609", vm.SuggestionsNote);
    }

    /// <summary>Negative control: a COMPLETE walk must stay silent, or the warning is
    /// noise that trains the user to ignore it.</summary>
    [Fact]
    public async Task AE15_complete_class_walk_shows_no_suggestion_warning()
    {
        var vm = new GameClassFilterViewModel(new ClassListDump { Truncated = false },
                                              new MockLoggingService(), new NoopPlatform());
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.SuperSuggestions);
        Assert.Equal("", vm.SuggestionsNote);
    }

    [Fact]
    public async Task AE15_disconnect_clears_the_suggestion_warning_with_the_lists()
    {
        var vm = new GameClassFilterViewModel(new ClassListDump { Truncated = true },
                                              new MockLoggingService(), new NoopPlatform());
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.NotEqual("", vm.SuggestionsNote);

        vm.ClearOnDisconnect();
        Assert.Equal("", vm.SuggestionsNote);
        Assert.Empty(vm.SuperSuggestions);
    }

    // ══════════════════════════════════════════════════════════════════
    // AE21 — "Load More" must not page against a superseded window
    // ══════════════════════════════════════════════════════════════════

    private static ValueSearchViewModel.WindowQuery Q(string filter = "", string sort = "",
                                                      bool desc = false, string excl = "")
        => new(filter, sort, desc, excl);

    [Fact]
    public void AE21_append_is_allowed_only_when_the_loaded_window_matches()
    {
        Assert.False(ValueSearchViewModel.ShouldPromoteAppendToReset(
            resetInFlight: false, loaded: Q("hp"), current: Q("hp")));
    }

    [Fact]
    public void AE21_a_page0_reload_in_flight_forces_a_reset()
    {
        // The filed scenario: Load More CANCELS the in-flight reload and then derives
        // its offset from a Candidates list that reload was about to replace.
        Assert.True(ValueSearchViewModel.ShouldPromoteAppendToReset(
            resetInFlight: true, loaded: Q("hp"), current: Q("hp")));
    }

    [Fact]
    public void AE21_a_filter_change_still_inside_its_debounce_forces_a_reset()
    {
        // 250 ms window where FilterText has moved but no reload has started yet —
        // the hole an "is a reload running?" flag alone would not cover.
        Assert.True(ValueSearchViewModel.ShouldPromoteAppendToReset(
            resetInFlight: false, loaded: Q("hp"), current: Q("hpmax")));
    }

    [Fact]
    public void AE21_sort_direction_and_exclusions_count_as_a_different_window()
    {
        Assert.True(ValueSearchViewModel.ShouldPromoteAppendToReset(
            false, Q(sort: "value"), Q(sort: "value", desc: true)));
        Assert.True(ValueSearchViewModel.ShouldPromoteAppendToReset(
            false, Q(excl: "0:"), Q(excl: "1:Actor")));
        Assert.True(ValueSearchViewModel.ShouldPromoteAppendToReset(
            false, loaded: null, current: Q()));
    }

    [Fact]
    public void AE21_exclusion_key_is_order_insensitive_and_unambiguous()
    {
        var a = ValueSearchViewModel.ExclusionKey(new[] { "Actor", "Pawn" });
        var b = ValueSearchViewModel.ExclusionKey(new[] { "Pawn", "Actor" });
        Assert.Equal(a, b);   // a picker re-emitting the same set must not read as a change
        Assert.NotEqual(a, ValueSearchViewModel.ExclusionKey(new[] { "Actor,Pawn" }));
        Assert.Equal("", ValueSearchViewModel.ExclusionKey(Array.Empty<string>()));
    }

    // ══════════════════════════════════════════════════════════════════
    // AE24 / AE13 — a refine must not look cleaner than its input
    // ══════════════════════════════════════════════════════════════════

    private sealed class TruncatingScanDump : StubDumpService
    {
        public bool FirstDeadline = true;

        public override Task<ValueScanBeginResult> BeginValueScanAsync(
            ValueScanDataType dataType, ValueScanType scanType, string value, string? value2 = null,
            bool gameOnly = true, int maxResults = 50000, FloatRoundMode roundMode = FloatRoundMode.Round,
            bool caseSensitive = false, bool parallel = true, bool batchRead = true, bool deep = false,
            bool nativeC = false, bool newestFirst = false, int pageSize = 1000, int deadlineMs = 15000,
            bool autoSkipNoise = false, CancellationToken ct = default)
            => Task.FromResult(new ValueScanBeginResult
            {
                SessionId = 1, Total = 10, DeadlineHit = FirstDeadline,
            });

        public override Task<ValueScanRefineResult> RefineValueScanAsync(
            ulong sessionId, ValueScanType scanType, string? value = null, string? value2 = null,
            FloatRoundMode roundMode = FloatRoundMode.Round, bool caseSensitive = false,
            int pageSize = 1000, CancellationToken ct = default)
            => Task.FromResult(new ValueScanRefineResult { SessionId = sessionId, Total = 3 });

        public override Task EndValueScanAsync(ulong sessionId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task AE24_next_scan_keeps_saying_the_first_scan_was_truncated()
    {
        var vm = new ValueSearchViewModel(new TruncatingScanDump(), new MockLoggingService())
        {
            Value = "100",
        };
        await vm.FirstScanCommand.ExecuteAsync(null);
        Assert.Contains("truncated", vm.StatusText);
        Assert.True(vm.ClassFilter.CountsPartial);

        await vm.NextScanCommand.ExecuteAsync(null);
        // Pre-fix: "Next Scan (Exact): 3 surviving candidates in 0 ms" — no ⚠ at all,
        // and CountsPartial had been reset to false.
        Assert.Contains("TRUNCATED", vm.StatusText);
        Assert.True(vm.ClassFilter.CountsPartial);
    }

    /// <summary>Negative control: a First Scan that finished must not have its refine
    /// decorated with an inherited-truncation warning.</summary>
    [Fact]
    public async Task AE24_a_complete_first_scan_leaves_the_refine_clean()
    {
        var vm = new ValueSearchViewModel(new TruncatingScanDump { FirstDeadline = false },
                                          new MockLoggingService())
        {
            Value = "100",
        };
        await vm.FirstScanCommand.ExecuteAsync(null);
        Assert.DoesNotContain("truncated", vm.StatusText);

        await vm.NextScanCommand.ExecuteAsync(null);
        Assert.DoesNotContain("TRUNCATED", vm.StatusText);
        Assert.False(vm.ClassFilter.CountsPartial);
    }

    [Fact]
    public async Task AE24_new_scan_clears_the_inherited_truncation_latch()
    {
        var dump = new TruncatingScanDump();
        var vm = new ValueSearchViewModel(dump, new MockLoggingService()) { Value = "100" };
        await vm.FirstScanCommand.ExecuteAsync(null);
        await vm.NewScanCommand.ExecuteAsync(null);

        dump.FirstDeadline = false;
        await vm.FirstScanCommand.ExecuteAsync(null);
        await vm.NextScanCommand.ExecuteAsync(null);
        Assert.DoesNotContain("TRUNCATED", vm.StatusText);
    }

    // AE13 — the group per-slot witness cap, end to end from the DTO.

    private sealed class CappedGroupDump : StubDumpService
    {
        public bool CapHit = true;

        public override Task<GroupScanBeginResult> BeginGroupScanAsync(
            IReadOnlyList<GroupSlotInput> slots, bool gameOnly = true, int maxResults = 50000,
            bool deep = false, bool crossObject = false, bool nativeC = false, bool newestFirst = false,
            int pageSize = 1000, int deadlineMs = 15000, bool autoSkipNoise = false,
            FloatRoundMode roundMode = FloatRoundMode.Round, int perSlotCap = 256,
            CancellationToken ct = default)
            => Task.FromResult(new GroupScanBeginResult
            {
                SessionId = 5, Total = 4, PerSlotCapHit = CapHit, PerSlotCap = CapHit ? 256 : 0,
            });

        public override Task<GroupScanRefineResult> RefineGroupScanAsync(
            ulong sessionId, IReadOnlyList<GroupSlotInput> slots, int pageSize = 1000,
            FloatRoundMode roundMode = FloatRoundMode.Round, CancellationToken ct = default)
            => Task.FromResult(new GroupScanRefineResult
            {
                SessionId = sessionId, Total = 2, PerSlotCapHit = CapHit, PerSlotCap = CapHit ? 256 : 0,
            });

        public override Task EndGroupScanAsync(ulong sessionId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static void FillGroupInputs(ValueSearchViewModel vm)
    {
        foreach (var g in vm.GroupInputs) g.Value = "100";
    }

    [Fact]
    public async Task AE13_group_scan_says_a_slot_kept_only_a_page_of_its_witnesses()
    {
        var vm = new ValueSearchViewModel(new CappedGroupDump(), new MockLoggingService());
        FillGroupInputs(vm);

        await vm.GroupFirstScanCommand.ExecuteAsync(null);
        // Pre-fix this fact existed only as a LOG_WARN line inside the DLL.
        Assert.Contains("256", vm.StatusText);
        Assert.Contains("All fields", vm.StatusText);

        await vm.GroupNextScanCommand.ExecuteAsync(null);
        Assert.Contains("All fields", vm.StatusText);   // carried, not forgotten
    }

    /// <summary>Negative control: an uncapped scan must say nothing about the cap.</summary>
    [Fact]
    public async Task AE13_an_uncapped_group_scan_stays_quiet()
    {
        var vm = new ValueSearchViewModel(new CappedGroupDump { CapHit = false },
                                          new MockLoggingService());
        FillGroupInputs(vm);

        await vm.GroupFirstScanCommand.ExecuteAsync(null);
        Assert.DoesNotContain("All fields", vm.StatusText);

        await vm.GroupNextScanCommand.ExecuteAsync(null);
        Assert.DoesNotContain("All fields", vm.StatusText);
    }

    [Fact]
    public async Task AE13_group_new_scan_clears_the_cap_latch()
    {
        var dump = new CappedGroupDump();
        var vm = new ValueSearchViewModel(dump, new MockLoggingService());
        FillGroupInputs(vm);
        await vm.GroupFirstScanCommand.ExecuteAsync(null);
        await vm.GroupNewScanCommand.ExecuteAsync(null);

        dump.CapHit = false;
        await vm.GroupFirstScanCommand.ExecuteAsync(null);
        await vm.GroupNextScanCommand.ExecuteAsync(null);
        Assert.DoesNotContain("All fields", vm.StatusText);
    }
}
