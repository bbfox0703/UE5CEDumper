using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Audit #5 batch L11 — V8 / V9 / V10 / V11 / W8 / Y10-Y14.
///
/// <para>One theme runs through all of them: <b>a report and the thing it reports on were
/// produced by different code</b>. A drill-down printed the container's true total over a
/// capped grid; a Cancel button was bound to an already-cancelled token; a scan's result
/// text was erased by the refresh the scan itself triggered; a symbol registration that
/// never reached CE looked exactly like one that did; an exporter's collector accepted a
/// narrower set of classes than its own siblings; and the invoke dialog announced N baked
/// params over values it had failed to parse.</para>
///
/// <para>Each fact below fails on the pre-fix tree — the negative control is named in the
/// comment beside it.</para>
/// </summary>
public class AuditL11HonestyTests
{
    // ══ V8 — the DataTable drill is the container path the [CONTAINERCAP] badge missed ══
    //
    // WalkDataTableRowsAsync fetches a FIXED page of rows (its `limit` default) and the UI
    // never pages, but the crumb printed dtResult.RowCount — the TRUE total the DLL
    // reports. A 5,000-row table therefore announced "5000" over a grid holding 64, so a
    // row that had simply not been fetched read as a row the table does not contain.
    //
    // Negative control: drop the BadgeSuffix calls in NavigateToDataTableContainer /
    // PopulateDataTableRowFields / DataTableFieldPreview -> all three Truncated facts fail.

    private static LiveWalkerViewModel MakeWalker()
    {
        var vm = new LiveWalkerViewModel(new StubDumpService(), new MockLoggingService(),
                                         new MockPlatformService(Path.GetTempPath()));
        vm.CurrentAddress = "0x10000000";
        return vm;
    }

    private static DataTableWalkResult DataTable(int total, int fetched) => new()
    {
        RowCount = total,
        RowMapOffset = 0x40,
        RowStructAddr = "0x20000000",
        RowStructName = "ItemRow",
        FNameSize = 8,
        Stride = 24,
        Rows = Enumerable.Range(0, fetched)
            .Select(i => new DataTableRowInfo
            {
                SparseIndex = i,
                RowName = $"Row_{i}",
                DataAddr = $"0x{0x30000000 + i * 0x100:X}",
                Fields = new List<LiveFieldValue>(),
            })
            .ToList(),
    };

    private static LiveFieldValue RowMapField(DataTableWalkResult dt) => new()
    {
        Name = "RowMap",
        TypeName = "DataTableRows",
        Offset = dt.RowMapOffset,
        DataTableRowCount = dt.RowCount,
        DataTableStructName = dt.RowStructName,
        DataTableRowStructAddr = dt.RowStructAddr,
        DataTableRowData = dt.Rows,
    };

    [Fact]
    public void V8_DataTableDrill_Truncated_BadgesCrumbHeaderAndStatus()
    {
        var vm = MakeWalker();
        var dt = DataTable(total: 5000, fetched: 64);

        vm.NavigateToDataTableContainer(RowMapField(dt), dt);

        Assert.Contains("showing 64 of 5,000", vm.Breadcrumbs[^1].Label);
        Assert.Contains("showing 64 of 5,000", vm.CurrentObjectName);
        // FixedCap wording, not the Array Limit one: that slider does NOT govern this
        // view, so naming it would be a second false statement on top of the first.
        Assert.Contains("64", vm.StatusText);
        Assert.Contains("5,000", vm.StatusText);
        Assert.DoesNotContain("Array Limit", vm.StatusText);
    }

    [Fact]
    public void V8_DataTableDrill_Complete_SaysNothing()
    {
        var vm = MakeWalker();
        var dt = DataTable(total: 12, fetched: 12);

        vm.NavigateToDataTableContainer(RowMapField(dt), dt);

        Assert.DoesNotContain("showing", vm.Breadcrumbs[^1].Label);
        Assert.DoesNotContain("showing", vm.CurrentObjectName);
        Assert.Equal("", vm.StatusText);
    }

    [Fact]
    public void V8_DataTableDrill_RowsStillRender()
    {
        // Guard against "fixed" by suppressing the view: the rows must still be there.
        var vm = MakeWalker();
        var dt = DataTable(total: 5000, fetched: 64);

        vm.NavigateToDataTableContainer(RowMapField(dt), dt);

        Assert.Equal(64, vm.Fields.Count);
    }

    [Fact]
    public void V8_SyntheticRowMapField_CarriesBadgeBeforeTheClick()
    {
        // The preview row is what the user clicks to drill in, so the cap belongs here too.
        Assert.Contains("showing 64 of 5,000",
            LiveWalkerViewModel.DataTableFieldPreview(DataTable(total: 5000, fetched: 64)));
        Assert.DoesNotContain("showing",
            LiveWalkerViewModel.DataTableFieldPreview(DataTable(total: 12, fetched: 12)));
    }

    [Fact]
    public void V8_ContainerTruncation_FixedCapStatusLine_DoesNotMentionTheSlider()
    {
        var line = ContainerTruncation.FixedCapStatusLine(64, 5000, "rows");
        Assert.Contains("rows", line);
        Assert.Contains((64).ToString("N0"), line);
        Assert.Contains((5000).ToString("N0"), line);
        Assert.DoesNotContain("Array Limit", line);
        Assert.Equal("", ContainerTruncation.FixedCapStatusLine(12, 12, "rows"));
        // A read failure is not a cap — same rule as the other two formatters.
        Assert.Equal("", ContainerTruncation.FixedCapStatusLine(0, 5000, "rows"));
    }

    // ══ V9 — the Object Tree's Cancel button could not cancel a search ══════════════
    //
    // SearchAsync cancelled _loadCts and never replaced it, then called
    // SearchObjectsAsync WITHOUT a token. For the whole (full-GObjects-sweep) search the
    // panel's only enabled control was a Cancel bound to an already-cancelled source.
    //
    // Negative control: drop the `ct` argument at the SearchObjectsAsync call site ->
    // V9_CancelDuringSearch_ActuallyCancels hangs on its own Task.Delay ceiling and fails.

    private sealed class BlockingSearchStub : StubDumpService
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes once the VM is actually inside the search round-trip.</summary>
        public Task Entered => _entered.Task;

        /// <summary>True when the VM handed us a token at all (false = the V9 defect).</summary>
        public bool ReceivedCancellableToken { get; private set; }

        public override async Task<ObjectListResult> SearchObjectsAsync(
            string query, int limit = 200, bool instancesOnly = false, CancellationToken ct = default)
        {
            ReceivedCancellableToken = ct.CanBeCanceled;
            _entered.TrySetResult();
            // A real search_objects sweep is long. Wait on the token, with a ceiling so a
            // regression fails the test instead of hanging the suite (working-lessons §2.7:
            // a hang is not a test result).
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new ObjectListResult { Total = 1 };
        }
    }

    [Fact]
    public async Task V9_CancelDuringSearch_ActuallyCancels()
    {
        var stub = new BlockingSearchStub();
        var vm = new ObjectTreeViewModel(stub, new MockLoggingService(),
                                         new MockPlatformService(Path.GetTempPath()))
        {
            SearchText = "Player",
        };

        var search = vm.SearchCommand.ExecuteAsync(null);
        await stub.Entered;

        Assert.True(vm.IsLoading);                     // the Cancel button is visible now
        Assert.True(stub.ReceivedCancellableToken);    // ...and the token is a real one

        vm.CancelLoadCommand.Execute(null);
        await search.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(vm.IsLoading);
        Assert.Equal("Search cancelled", vm.StatusText);
        Assert.Null(vm.ErrorMessage);   // a user cancel is not an error
    }

    // ══ V10 — Update() erased the Extra Scan's own result ═══════════════════════════
    //
    // ExtraScanAsync sets ScanResultText, then raises RescanApplied; MainWindowVM's
    // handler re-fetches pointers and calls Update(), which blanked it milliseconds later.
    // ApplyOverrideAsync and ApplyInvokeTimeoutAsync reach Update() the same way, and the
    // UE-version ComboBox is gated on IsApplyingOverride — NOT IsScanning — so clearing
    // IsScanning there re-enabled CanExtraScan mid-scan.
    //
    // Negative control: put the five assignments back at the end of Update() -> both
    // V10 facts fail.

    private static PointerPanelViewModel MakePointers()
        => new(new MockPlatformService(Path.GetTempPath()));

    [Fact]
    public void V10_Update_DoesNotEraseScanState()
    {
        var vm = MakePointers();
        vm.Update(new EngineState { UEVersion = 504, ObjectCount = 10 });

        // Stand in for ExtraScanAsync's success path.
        vm.IsScanning = true;
        vm.ScanComplete = true;
        vm.ScanStatusText = "Scan complete — results applied.";
        vm.ScanResultText = "Found: GObjects: 0x14000000";

        // The refresh RescanApplied performs.
        vm.Update(new EngineState { UEVersion = 504, ObjectCount = 20 });

        Assert.True(vm.IsScanning);
        Assert.True(vm.ScanComplete);
        Assert.Equal("Found: GObjects: 0x14000000", vm.ScanResultText);
        Assert.Equal("Scan complete — results applied.", vm.ScanStatusText);
        Assert.Equal(20, vm.TotalObjects);   // ...while still applying the new state
    }

    [Fact]
    public void V10_ResetScanState_IsWhatClears()
    {
        var vm = MakePointers();
        vm.Update(new EngineState { UEVersion = 504, ObjectCount = 10 });
        vm.IsScanning = true;
        vm.ScanComplete = true;
        vm.ScanStatusText = "x";
        vm.ScanResultText = "y";
        vm.CacheStatusText = "z";
        vm.SymbolStatusText = "s";

        vm.ResetScanState();

        Assert.False(vm.IsScanning);
        Assert.False(vm.ScanComplete);
        Assert.Equal("", vm.ScanStatusText);
        Assert.Equal("", vm.ScanResultText);
        Assert.Equal("", vm.CacheStatusText);
        Assert.Equal("", vm.SymbolStatusText);
    }

    [Fact]
    public void V10_Disconnect_ClearsScanStateSoAReconnectShowsNoStaleResult()
    {
        var vm = MakePointers();
        vm.Update(new EngineState { UEVersion = 504, ObjectCount = 10 });
        vm.ScanResultText = "Found: GObjects: 0x14000000";

        vm.ClearOnDisconnect();

        Assert.False(vm.HasData);
        Assert.Equal("", vm.ScanResultText);
    }

    // ══ V11 — "Register symbol" looked identical on success and failure ═════════════
    //
    // CreateSymbolScriptAsync's bool chose _log.Info vs _log.Warn and nothing else.
    // Negative control: revert ReportSymbolRegistration to the two log calls -> both
    // V11 facts fail.

    [Fact]
    public void V11_SymbolRegistrationSuccess_ReachesTheUser()
    {
        var vm = MakePointers();
        vm.ReportSymbolRegistration(true, "gworld_addr", "AOB: 48 8B, pos=3, len=7");

        Assert.Contains("gworld_addr", vm.SymbolStatusText);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void V11_SymbolRegistrationFailure_ReachesTheUser()
    {
        var vm = MakePointers();
        vm.ReportSymbolRegistration(false, "gengine_addr", "AOB: 48 8B, pos=3, len=7");

        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        Assert.Contains("gengine_addr", vm.ErrorMessage!);
        // ...and it must not ALSO claim success.
        Assert.Equal("", vm.SymbolStatusText);
    }

    [Fact]
    public void V11_SymbolStringsExistInEnAxaml()
    {
        // Res.Get returns "" for a key it cannot find — no throw, no log. The VM has a
        // literal fallback so the message is never invisible, but a missing key would
        // silently downgrade every user's wording, so pin the keys themselves.
        var axaml = FindRepoFile(Path.Combine("ui", "UE5DumpUI", "Resources", "Strings", "en.axaml"));
        Assert.NotNull(axaml);
        var text = File.ReadAllText(axaml!);
        Assert.Contains("x:Key=\"str.Pointers.Symbol.Registered\"", text, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"str.Pointers.Symbol.Failed\"", text, StringComparison.Ordinal);
    }

    /// <summary>Walk up from the test binary to the repo root and resolve a tracked file.
    /// Same shape as SelfTestAdviceTests' helper.</summary>
    private static string? FindRepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    [Fact]
    public void V11_FailureThenSuccess_ClearsTheStaleError()
    {
        var vm = MakePointers();
        vm.ReportSymbolRegistration(false, "gworld_addr", "d");
        vm.ReportSymbolRegistration(true, "gworld_addr", "d");

        Assert.Null(vm.ErrorMessage);
        Assert.Contains("gworld_addr", vm.SymbolStatusText);
    }

    // ══ W8 — the USMAP collector dropped every Blueprint-generated class ════════════
    //
    // `ClassName is "Class" or "ScriptStruct"` accepted native classes only. On a shipped
    // title that is a few hundred rows against thousands of BlueprintGeneratedClass ones —
    // and Blueprint classes are exactly what a .usmap consumer needs. Both sibling
    // exporters already route through the whitelist.
    //
    // Negative control: restore the bare check -> the four BPGC-family facts fail.

    private sealed class MetaClassStub : StubDumpService
    {
        private readonly List<UObjectNode> _objects;
        public List<string> Walked { get; } = new();

        public MetaClassStub(params (string name, string meta)[] objs)
        {
            _objects = objs
                .Select((o, i) => new UObjectNode
                {
                    Address = $"0x{0x1000 + i * 0x100:X}",
                    Name = o.name,
                    ClassName = o.meta,
                })
                .ToList();
        }

        public override Task<List<EnumDefinition>> ListEnumsAsync(CancellationToken ct = default)
            => Task.FromResult(new List<EnumDefinition>());

        public override Task<ObjectListResult> GetObjectListAsync(
            int offset, int limit, CancellationToken ct = default, bool includePath = false)
            => Task.FromResult(new ObjectListResult
            {
                Total = _objects.Count,
                Scanned = _objects.Count,
                Objects = offset == 0 ? _objects : new List<UObjectNode>(),
            });

        public override Task<ClassInfoModel> WalkClassAsync(string addr, CancellationToken ct = default)
        {
            var obj = _objects.First(o => o.Address == addr);
            Walked.Add(obj.Name);
            return Task.FromResult(new ClassInfoModel
            {
                Name = obj.Name,
                Fields = new List<FieldInfoModel>(),
            });
        }
    }

    [Fact]
    public async Task W8_UsmapCollector_AcceptsEveryClassLikeMeta()
    {
        var stub = new MetaClassStub(
            ("APlayerCharacter", "Class"),
            ("FItemRow", "ScriptStruct"),
            ("BP_Enemy_C", "BlueprintGeneratedClass"),
            ("ABP_Hero_C", "AnimBlueprintGeneratedClass"),
            ("WBP_HUD_C", "WidgetBlueprintGeneratedClass"),
            ("DynCls", "DynamicClass"),
            // ...and nothing that is NOT class-like:
            ("SomeFunction", "Function"),
            ("EItemKind", "Enum"),
            ("DefaultPawn_0", "APlayerCharacter"));

        await UsmapExportService.GenerateUsmapAsync(stub, ct: TestContext.Current.CancellationToken);

        Assert.Contains("APlayerCharacter", stub.Walked);
        Assert.Contains("FItemRow", stub.Walked);
        Assert.Contains("BP_Enemy_C", stub.Walked);
        Assert.Contains("ABP_Hero_C", stub.Walked);
        Assert.Contains("WBP_HUD_C", stub.Walked);
        Assert.Contains("DynCls", stub.Walked);

        Assert.DoesNotContain("SomeFunction", stub.Walked);
        Assert.DoesNotContain("EItemKind", stub.Walked);
        Assert.DoesNotContain("DefaultPawn_0", stub.Walked);   // an INSTANCE, not a class
    }

    [Fact]
    public async Task W8_UsmapCollector_MatchesTheSdkExporter()
    {
        // The point of the fix is that the two exporters agree. Assert the shared
        // predicate rather than a second hand-written list, so they cannot drift again.
        var metas = new[]
        {
            "Class", "BlueprintGeneratedClass", "AnimBlueprintGeneratedClass",
            "WidgetBlueprintGeneratedClass", "DynamicClass", "ScriptStruct",
            "Function", "Enum", "Package",
        };
        var stub = new MetaClassStub(metas.Select(m => ($"N_{m}", m)).ToArray());

        await UsmapExportService.GenerateUsmapAsync(stub, ct: TestContext.Current.CancellationToken);

        foreach (var m in metas)
        {
            bool expected = DumpAllService.IsClassLikeMetaName(m) || m == "ScriptStruct";
            Assert.Equal(expected, stub.Walked.Contains($"N_{m}"));
        }
    }

    // ══ Y10 — the baked verify script wrote into the mailbox with no contract check ══
    //
    // Negative control: delete the AppendContractCheck call -> the ordering fact fails.
    // The clamp's negative control is restoring `parmsSize` in the zero loop.

    private static string BakedVerify(int parmsSize, BakedParamValue? ret = null) =>
        BakedScriptGenerator.Generate(
            "APlayerCharacter", "AddMoney", parmsSize,
            new List<BakedParamValue>(), returnParam: ret, verifyReturn: true);

    [Fact]
    public void Y10_VerifyMode_ContractCheckPrecedesTheFirstMailboxWrite()
    {
        var script = BakedVerify(16);

        int check = script.IndexOf("g_mailboxContract", StringComparison.Ordinal);
        int write = script.IndexOf("writeByte(_PD_dbg", StringComparison.Ordinal);

        Assert.True(check >= 0, "verify mode must emit a contract check");
        Assert.True(write >= 0, "verify mode still pre-zeroes the params buffer");
        Assert.True(check < write,
            "the contract check must come BEFORE the first mailbox write — the layout is " +
            "what is in question, so a write cannot come first");
    }

    [Fact]
    public void Y10_VerifyMode_ContractBailUnticksTheRecord()
    {
        // A bail-out that applied nothing must untick, or CE shows a ticked row for a
        // cheat that was never set (CLAUDE.md's CE Lua hygiene rule).
        var script = BakedVerify(16);
        Assert.Contains("memrec.Active = false", script);
        Assert.Contains(CeMailboxLayout.ContractVersion.ToString(), script);
    }

    [Fact]
    public void Y10_NonVerifyMode_TouchesNoMailboxAndNeedsNoCheck()
    {
        // The plain baked script writes nothing to the mailbox itself (the helper does),
        // so it must not have grown a check — that would be a new failure mode for a
        // path that never had the defect.
        var script = BakedScriptGenerator.Generate(
            "APlayerCharacter", "AddMoney", 16, new List<BakedParamValue>());
        Assert.DoesNotContain("writeByte(_PD_dbg", script);
        Assert.DoesNotContain("g_mailboxContract", script);
    }

    [Theory]
    [InlineData(16, 16)]        // ordinary function: unchanged
    [InlineData(1024, 1024)]    // exactly the params region
    [InlineData(4096, 1024)]    // a big by-value struct param: CLAMPED
    public void Y10_PreZeroLoop_NeverWritesPastTheParamsRegion(int parmsSize, int expected)
    {
        var script = BakedVerify(parmsSize);
        Assert.Contains($"for i = 0, {expected} - 1 do writeByte(_PD_dbg + i, 0) end", script);
    }

    // ══ Y13 — the 32-byte dump could not contain the return it pointed at ═══════════
    //
    // Negative control: restore `Math.Max(8, Math.Min(parmsSize, 32))` -> the widening
    // facts and the emitted-hint fact fail.

    private static BakedParamValue Ret(string type, int size, int offset) =>
        new(ParamName: "ReturnValue", UeTypeName: type, Size: size, Offset: offset, LiteralText: "");

    [Fact]
    public void Y13_DumpWindow_WidensToCoverTheReturnSlot()
    {
        // FString return at +32, 16 bytes wide: the old flat 32 stopped one byte short of
        // its first byte, which is the whole finding.
        Assert.Equal(48, BakedScriptGenerator.ComputeDumpLength(64, Ret("StrProperty", 16, 32)));
    }

    [Fact]
    public void Y13_DumpWindow_KeepsTheOldDefaultWhenNothingNeedsMore()
    {
        Assert.Equal(32, BakedScriptGenerator.ComputeDumpLength(64, Ret("IntProperty", 4, 8)));
        Assert.Equal(32, BakedScriptGenerator.ComputeDumpLength(64, null));
        // Tiny params buffer: unchanged floor of 8, exactly as before.
        Assert.Equal(8, BakedScriptGenerator.ComputeDumpLength(4, null));
    }

    [Fact]
    public void Y13_DumpWindow_ClampsToParmsSizeAndToTheCeiling()
    {
        // Never read past the function's own params buffer (that would render mailbox
        // fields as if they were params).
        Assert.Equal(40, BakedScriptGenerator.ComputeDumpLength(40, Ret("StructProperty", 64, 24)));
        // Never flood CE's Lua Engine.
        Assert.Equal(BakedScriptGenerator.MaxDumpBytes,
            BakedScriptGenerator.ComputeDumpLength(4096, Ret("StructProperty", 512, 1024)));
    }

    [Fact]
    public void Y13_ComplexReturnHint_OnlyClaimsTheDumpWhenItReallyHoldsIt()
    {
        // Covered: the "see After: dump above" wording is true, so keep it.
        var covered = BakedVerify(64, Ret("StrProperty", 16, 32));
        Assert.Contains("see After: dump above", covered);

        // Not covered (return past the 256-byte ceiling): the hint must NOT send the user
        // to a dump that cannot contain the value.
        var beyond = BakedVerify(4096, Ret("StructProperty", 512, 1024));
        Assert.DoesNotContain("see After: dump above", beyond);
        Assert.Contains("past the", beyond);
    }

    // ══ Y11 — FIRE had no unsupported-param gate ════════════════════════════════════
    //
    // An FText / TArray / TMap / TSet / delegate / layout-less struct param's textbox was
    // written as a raw int32 over the structure's first pointer field and handed to
    // ProcessEvent. The exported script's helper already refuses exactly these (AA16);
    // the FIRE path did not, so one dialog input had two different answers.
    //
    // Negative control: delete the IsUnwritableParam early-return in WriteParam and the
    // two guards in TryValidateScalar -> every Y11 fact fails.

    private static byte[] Fire(string type, int size, string text, int bufLen = 32)
    {
        var buf = new byte[bufLen];
        ParamBufferBuilder.WriteParam(buf, 0, type, size, text);
        return buf;
    }

    [Theory]
    [InlineData("ArrayProperty", 16)]
    [InlineData("MapProperty", 80)]
    [InlineData("SetProperty", 80)]
    [InlineData("TextProperty", 24)]
    [InlineData("StructProperty", 12)]
    [InlineData("DelegateProperty", 16)]
    [InlineData("MulticastInlineDelegateProperty", 16)]
    [InlineData("FieldPathProperty", 16)]
    [InlineData("OptionalProperty", 8)]
    public void Y11_UnwritableParam_LeavesTheSlotZeroed(string type, int size)
    {
        // 0x11223344 would previously have landed on the structure's Data pointer.
        var buf = Fire(type, size, "0x11223344");
        Assert.All(buf, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Y11_ScalarParamsStillWrite()
    {
        // Negative control for the gate itself: it must not swallow the supported types.
        var buf = Fire("IntProperty", 4, "0x11223344");
        Assert.Equal(0x44, buf[0]);
        Assert.Equal(0x11, buf[3]);
    }

    [Theory]
    [InlineData("ArrayProperty")]
    [InlineData("MapProperty")]
    [InlineData("SetProperty")]
    [InlineData("StructProperty")]
    [InlineData("DelegateProperty")]
    public void Y11_EmptyOnlyParam_RefusesATypedValueButAcceptsTheDefault(string type)
    {
        Assert.True(ParamBufferBuilder.TryValidateScalar(type, 16, "0", out _));
        Assert.True(ParamBufferBuilder.TryValidateScalar(type, 16, "", out _));
        Assert.True(ParamBufferBuilder.TryValidateScalar(type, 16, "0x0", out _));

        Assert.False(ParamBufferBuilder.TryValidateScalar(type, 16, "42", out var err));
        Assert.Contains("cannot be built", err);
    }

    [Fact]
    public void Y11_FText_IsRefusedEvenAtItsDefault()
    {
        // Unlike the containers, an all-zero FText is NOT an empty FText — it holds a
        // TSharedRef the engine dereferences, so zeros crash. The exported script's helper
        // refuses it unconditionally; FIRE must give the same answer.
        Assert.False(ParamBufferBuilder.TryValidateScalar("TextProperty", 24, "0", out var err));
        Assert.Contains("FText", err);
        Assert.False(ParamBufferBuilder.TryValidateScalar("TextProperty", 24, "hello", out _));
    }

    // ── Y11 step 3: the SUB-FIELD path had no gate at all ──────────────────────
    //
    // The dialog validated top-level params and then called WriteStructParam, which
    // forwards each sub-field straight to WriteParam. WriteParam's opaque-type guard
    // returns SILENTLY, so a nested-struct sub-field (FSlateBrush::ImageSize) had its
    // typed value dropped while FIRE still said "ProcessEvent OK", and an out-of-range
    // integer sub-field masked to width. Measured on DQ7R 2026-08-22.
    //
    // Negative control: delete the TryValidateStructSubFields call in
    // InvokeParamDialog (or make this helper always return true) -> both facts below
    // fail, and Y11_SubField_TypedValueIsDroppedSilently documents why that matters.

    private static readonly DynamicStructField[] BrushLike =
    [
        new("Tint",      "StructProperty", 0,  16),   // opaque nested struct
        new("DrawAs",    "ByteProperty",   16, 1),    // 1-byte, range-checked
        new("ImageSize", "StructProperty", 20, 8),    // opaque nested struct
    ];

    [Fact]
    public void Y11_SubField_OpaqueStructWithATypedValueIsRefusedAndNamed()
    {
        var ok = ParamBufferBuilder.TryValidateStructSubFields(
            BrushLike, ["0", "0", "42"], out var field, out var err);

        Assert.False(ok);
        Assert.Equal("ImageSize", field);          // names the offender, not just "a field"
        Assert.Contains("cannot be built", err);
    }

    [Fact]
    public void Y11_SubField_OutOfRangeIntegerIsRefused()
    {
        // The width family (W6/Y2/Y9/Y15/AE1) survived here because the fix was applied
        // to top-level params only: 9999 into a 1-byte sub-field used to fire as 15.
        var ok = ParamBufferBuilder.TryValidateStructSubFields(
            BrushLike, ["0", "9999", "0"], out var field, out var err);

        Assert.False(ok);
        Assert.Equal("DrawAs", field);
        Assert.Contains("does not fit", err);
    }

    [Fact]
    public void Y11_SubField_UntouchedDefaultsStillPass()
    {
        // The control: leaving the boxes at their zero default legitimately means
        // "send the zeroed struct", which step 2 proved is safe. Refusing that would
        // break a passing case to fix a failing one.
        Assert.True(ParamBufferBuilder.TryValidateStructSubFields(
            BrushLike, ["0", "0", "0"], out _, out _));
        Assert.True(ParamBufferBuilder.TryValidateStructSubFields(
            BrushLike, ["", "", ""], out _, out _));
    }

    [Fact]
    public void Y11_SubField_TypedValueIsDroppedSilently_WhichIsWhyTheGateExists()
    {
        // This is the defect itself, pinned: WriteStructParam does NOT write an opaque
        // sub-field, and says nothing about it. The buffer staying zero is correct
        // behaviour -- writing 42 over a struct's first pointer was the original Y11 bug
        // -- so the repair had to be a refusal, never a write.
        var buf = new byte[32];
        ParamBufferBuilder.WriteStructParam(buf, 0, BrushLike, ["0", "0", "42"]);
        Assert.All(buf, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Y11_StringAndPointerParamsAreStillAccepted()
    {
        // FString has a real builder (InvokeStringParam) and pointers are plain qwords —
        // neither must be caught by the gate.
        Assert.True(ParamBufferBuilder.TryValidateScalar("StrProperty", 16, "hello", out _));
        Assert.True(ParamBufferBuilder.TryValidateScalar("ObjectProperty", 8, "0x7FF612340000", out _));
        Assert.False(ParamBufferBuilder.IsUnwritableParam("StrProperty"));
        Assert.False(ParamBufferBuilder.IsUnwritableParam("ObjectProperty"));
    }

    // ══ Y14 — "N baked param(s)" was reported over params that failed to parse ══════
    //
    // Negative control: replace IsUnparsedLiteral's body with `=> false` -> the
    // unparsed-detection facts fail.

    [Theory]
    [InlineData("IntProperty", "not-a-number")]
    [InlineData("FloatProperty", "1,5")]        // thousands separator: rejected, baked 0
    [InlineData("FloatProperty", "NaN")]        // parses as a double, is not a Lua literal
    [InlineData("BoolProperty", "maybe")]
    [InlineData("ObjectProperty", "0xZZZZ")]
    public void Y14_UnparsedInput_IsDetectable(string type, string text)
    {
        Assert.True(BakedScriptGenerator.IsUnparsedLiteral(type, text));
        // ...and the emitted literal really is the 0 fallback the report must disclose.
        Assert.Contains("0", BakedScriptGenerator.RenderLiteral(type, text));
    }

    [Theory]
    [InlineData("IntProperty", "42")]
    [InlineData("IntProperty", "-7")]
    [InlineData("FloatProperty", "1.5")]
    [InlineData("BoolProperty", "true")]
    [InlineData("ObjectProperty", "0x7FF612340000")]
    [InlineData("StrProperty", "anything at all")]   // strings never fail to render
    [InlineData("IntProperty", "")]                  // empty = a deliberate 0, not a failure
    public void Y14_ParsedInput_IsNotFlagged(string type, string text)
    {
        Assert.False(BakedScriptGenerator.IsUnparsedLiteral(type, text));
    }

    [Fact]
    public void Y14_DetectionAgreesWithWhatTheScriptActuallyContains()
    {
        // The whole root cause is "the report and the reality computed by different code",
        // so pin that the predicate and the emitted script agree.
        var values = new List<BakedParamValue>
        {
            new("Amount", "IntProperty", 4, 0, "42"),
            new("Rate",   "FloatProperty", 4, 4, "1,5"),
        };
        var script = BakedScriptGenerator.Generate("APlayerCharacter", "AddMoney", 8, values);

        Assert.Contains(BakedScriptGenerator.UnparsedMarker, script);
        Assert.False(BakedScriptGenerator.IsUnparsedLiteral(values[0].UeTypeName, values[0].LiteralText));
        Assert.True(BakedScriptGenerator.IsUnparsedLiteral(values[1].UeTypeName, values[1].LiteralText));
    }

    // ══ Y12 — the baked clipboard fallback shipped a bare AA body ═══════════════════
    //
    // A bare [ENABLE]/[DISABLE] body cannot be pasted into CE's address list; it has to be
    // a CheatEntry with VariableType = Auto Assembler Script. Every other clipboard
    // fallback wraps. The dialog's own handler needs a window, so what is pinned here is
    // that the wrapper survives a real baked script intact — the dialog change itself is
    // one call and is listed for live verification.

    [Fact]
    public void Y12_WrappedBakedScript_IsAPasteableCheatEntry()
    {
        var script = BakedScriptGenerator.Generate(
            "APlayerCharacter", "AddMoney", 8,
            new List<BakedParamValue> { new("Amount", "IntProperty", 4, 0, "42") });
        var xml = CheatTableBuilder.WrapAaScriptXml("Invoke (baked): APlayerCharacter::AddMoney", script);

        Assert.Contains("<CheatTable>", xml);
        Assert.Contains("<VariableType>Auto Assembler Script</VariableType>", xml);
        Assert.Contains("APlayerCharacter::AddMoney", xml);
        // The body must survive escaping — a wrapper that mangles the script is worse
        // than no wrapper.
        Assert.Contains("[ENABLE]", xml);
        Assert.Contains("invokeUFunction", xml);
        Assert.DoesNotContain("<AssemblerScript>[ENABLE]\r", xml);   // no CR injected
    }
}
