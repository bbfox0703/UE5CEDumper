using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// VM-level tests for the Console panel — UFUNCTION(exec) discovery
/// (filters on FUNC_Exec = 0x00000200) + one-click invoke.
///
/// Coverage targets:
/// - Load filters to IsExec entries only, sorted by Class then Func.
/// - Filter text matches funcName OR className (case-insensitive
///   substring).
/// - RunSelectedAsync invokes InvokeFunctionAsync with the row's
///   className, no instance addr, and zero parmsSize.
/// - History gets one entry per Run, capped at MaxHistoryEntries.
/// - Commands with NumParms &gt; 0 raise RequestParameterInvoke
///   instead of invoking directly.
/// - RunCommandTextAsync resolves typed name + handles leading slash.
/// </summary>
public class ConsoleViewModelTests
{
    private sealed class FakeDumpService : StubDumpService
    {
        public AllFunctionsResult NextListResult { get; set; } = new();
        public InvokeFunctionResult NextInvokeResult { get; set; } =
            new() { Result = 0, Message = "ProcessEvent OK" };

        public int InvokeCallCount { get; private set; }
        public string LastInvokeClass { get; private set; } = "";
        public string LastInvokeFunc { get; private set; } = "";
        public int LastInvokeParmsSize { get; private set; }
        public string? LastInvokeInstanceAddr { get; private set; }

        /// <summary>Every instanceAddr passed to InvokeFunctionAsync, in
        /// call order — lets sticky-instance tests assert exactly when a
        /// pinned address was reused vs. a fresh null resolution.</summary>
        public List<string?> InstanceAddrHistory { get; } = new();

        /// <summary>Optional per-call result override; when set it takes
        /// precedence over <see cref="NextInvokeResult"/> for one call each.
        /// Used by the self-heal test to fail the pinned attempt + its retry
        /// while still pinning on an earlier success.</summary>
        public Queue<InvokeFunctionResult>? InvokeResultQueue { get; set; }

        // ── Debug Camera helper plumbing (logic now lives DLL-side; the VM
        //    is a thin bridge over these two pipe calls) ─────────────────
        public int NextDebugCameraState { get; set; }          // get_debug_camera_state
        /// <summary>set_debug_camera result; null = echo (enable ? 1 : 0).</summary>
        public int? SetDebugCameraResult { get; set; }
        public List<bool> SetDebugCameraCalls { get; } = new();
        public int GetDebugCameraStateCallCount { get; private set; }

        public override Task<int> GetDebugCameraStateAsync(CancellationToken ct = default)
        {
            GetDebugCameraStateCallCount++;
            return Task.FromResult(NextDebugCameraState);
        }

        public override Task<int> SetDebugCameraAsync(bool enable, CancellationToken ct = default)
        {
            SetDebugCameraCalls.Add(enable);
            return Task.FromResult(SetDebugCameraResult ?? (enable ? 1 : 0));
        }

        public override Task<AllFunctionsResult> ListAllFunctionsAsync(
            bool gameOnly = true, int limit = 100000, CancellationToken ct = default)
        {
            return Task.FromResult(NextListResult);
        }

        public override Task<InvokeFunctionResult> InvokeFunctionAsync(
            string funcName, string? instanceAddr = null, string? className = null,
            int parmsSize = 0, string? paramsHex = null, bool directCall = false,
            IReadOnlyList<InvokeStringParam>? stringParams = null,
            CancellationToken ct = default)
        {
            InvokeCallCount++;
            LastInvokeFunc = funcName;
            LastInvokeClass = className ?? "";
            LastInvokeParmsSize = parmsSize;
            LastInvokeInstanceAddr = instanceAddr;
            InstanceAddrHistory.Add(instanceAddr);
            var result = (InvokeResultQueue is { Count: > 0 })
                ? InvokeResultQueue.Dequeue()
                : NextInvokeResult;
            return Task.FromResult(result);
        }
    }

    private sealed class NoopLogger : ILoggingService
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Error(string message, Exception ex) { }
        public void Debug(string message) { }
        public void Info(string category, string message) { }
        public void Warn(string category, string message) { }
        public void Error(string category, string message) { }
        public void Error(string category, string message, Exception ex) { }
        public void Debug(string category, string message) { }
        public void StartProcessMirror(string processName) { }
        public void StopProcessMirror() { }
    }

    // ------------------------------------------------------------------
    // Test data: mix of exec + non-exec entries so the filter is exercised.
    // FUNC_Exec = 0x00000200; FUNC_BlueprintCallable = 0x04000000.
    // ------------------------------------------------------------------

    private const uint FUNC_Exec               = 0x0000_0200;
    private const uint FUNC_BlueprintCallable  = 0x0400_0000;
    private const uint FUNC_Native             = 0x0000_0400;

    // ClassAddr is populated because list_all_functions really does supply it per row —
    // and the handoff events now carry it so MainWindow never re-derives an address from
    // the CAPPED list_classes page (audit #5 X2).
    private static List<AllFunctionEntry> BuildSampleEntries() => new()
    {
        // 4 exec entries — different classes + arities
        new() { ClassName="UCheatManager", ClassAddr="0x1000", FuncName="Fly",
                FunctionFlags=FUNC_Exec | FUNC_Native, NumParms=0, ParmsSize=0 },
        new() { ClassName="UCheatManager", ClassAddr="0x1000", FuncName="God",
                FunctionFlags=FUNC_Exec | FUNC_Native, NumParms=0, ParmsSize=0 },
        new() { ClassName="MyGameCheatMgr", ClassAddr="0x2000", FuncName="GiveItem",
                FunctionFlags=FUNC_Exec, NumParms=1, ParmsSize=4 },
        new() { ClassName="PlayerController", ClassAddr="0x3000", FuncName="DebugTeleport",
                FunctionFlags=FUNC_Exec | FUNC_BlueprintCallable, NumParms=3, ParmsSize=12 },

        // Non-exec — should be excluded from the results
        new() { ClassName="PlayerCharacter", ClassAddr="0x4000", FuncName="AddMoney",
                FunctionFlags=FUNC_BlueprintCallable, NumParms=2, ParmsSize=8 },
        new() { ClassName="GameMode", ClassAddr="0x5000", FuncName="StartPlay",
                FunctionFlags=FUNC_BlueprintCallable, NumParms=0, ParmsSize=0 },
    };

    private static ConsoleViewModel CreateVm(FakeDumpService fake)
    {
        var log = new NoopLogger();
        return new ConsoleViewModel(fake, log);
    }

    // ------------------------------------------------------------------

    [Fact]
    public async Task Load_filters_to_exec_only_and_sorts_by_class_then_func()
    {
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult
            {
                Total = 6,
                ScannedObjects = 100,
                ScannedClasses = 5,
                TotalFunctions = 6,
                Functions = BuildSampleEntries(),
            }
        };
        var vm = CreateVm(fake);

        await vm.LoadCommand.ExecuteAsync(null);

        // Exec entries only: 4 of the 6 inputs
        Assert.Equal(4, vm.Results.Count);
        // Sorted by class, then func
        Assert.Equal("MyGameCheatMgr",     vm.Results[0].ClassName);
        Assert.Equal("PlayerController",   vm.Results[1].ClassName);
        Assert.Equal("UCheatManager",      vm.Results[2].ClassName);
        Assert.Equal("UCheatManager",      vm.Results[3].ClassName);
        Assert.Equal("Fly",                vm.Results[2].FuncName);
        Assert.Equal("God",                vm.Results[3].FuncName);
        // Every surfaced row IS exec
        Assert.All(vm.Results, r => Assert.True(r.IsExec));
    }

    [Fact]
    public async Task Load_with_no_exec_entries_reports_helpful_status()
    {
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult
            {
                Total = 2,
                ScannedObjects = 50,
                ScannedClasses = 1,
                TotalFunctions = 2,
                Functions = new List<AllFunctionEntry>
                {
                    new() { ClassName="A", FuncName="X", FunctionFlags=FUNC_BlueprintCallable },
                    new() { ClassName="B", FuncName="Y", FunctionFlags=FUNC_Native },
                }
            }
        };
        var vm = CreateVm(fake);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.Results);
        Assert.Contains("No UFUNCTION(exec)", vm.StatusText);
        // A COMPLETE scan may make a claim about the game.
        Assert.Contains("in this game", vm.StatusText);
    }

    // ==================================================================
    // Audit #5 Z8 — a capped list_all_functions cannot support a claim
    // about the GAME, only about the page that was scanned.
    // ==================================================================

    /// <summary>
    /// The panel used to state "No UFUNCTION(exec) commands found in this game (scanned
    /// 100,000 functions…)" from a walk that stopped at its row cap, possibly before
    /// ever reaching the classes in question. The DLL emitted no truncation marker of
    /// any kind, and the one field that could have exposed it (<c>total_functions</c>)
    /// is identically <c>entries.size()</c> by construction. With the flag on the wire
    /// the sentence must become a claim about the SCAN.
    /// </summary>
    [Fact]
    public async Task Load_with_no_exec_on_a_TRUNCATED_scan_does_not_claim_the_game_has_none()
    {
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult
            {
                Total = 100_000, ScannedObjects = 900_000, ScannedClasses = 9_000,
                TotalFunctions = 100_000, Truncated = true, Limit = 100_000,
                Functions = new List<AllFunctionEntry>
                {
                    new() { ClassName="A", FuncName="X", FunctionFlags=FUNC_BlueprintCallable },
                },
            }
        };
        var vm = CreateVm(fake);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.Results);
        // The claim must be scoped to the scan, never to the game.
        Assert.DoesNotContain("in this game", vm.StatusText);
        Assert.Contains("scanned so far", vm.StatusText);
        Assert.Contains("not evidence the game has none", vm.StatusText);
        // ...and it must name the cap + a lever the panel actually has.
        Assert.Contains("STOPPED at the 100,000-row cap", vm.StatusText);
        Assert.Contains("Game classes only", vm.StatusText);
    }

    /// <summary>An aborted walk is partial for a different reason; same honesty rule.</summary>
    [Fact]
    public async Task Load_with_no_exec_on_an_ABORTED_scan_says_cancelled()
    {
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult
            {
                Total = 12, ScannedObjects = 900, ScannedClasses = 4,
                TotalFunctions = 12, Aborted = true, Limit = 100_000,
                Functions = new List<AllFunctionEntry>
                {
                    new() { ClassName="A", FuncName="X", FunctionFlags=FUNC_BlueprintCallable },
                },
            }
        };
        var vm = CreateVm(fake);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.DoesNotContain("in this game", vm.StatusText);
        Assert.Contains("SCAN CANCELLED", vm.StatusText);
    }

    /// <summary>
    /// The truncation disclosure is not only for the empty case: a partial scan that DID
    /// find execs still under-reports, and the user needs to know more may exist.
    /// </summary>
    [Fact]
    public async Task Load_with_exec_rows_on_a_truncated_scan_still_discloses_the_cap()
    {
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult
            {
                Total = 100_000, ScannedObjects = 900_000, ScannedClasses = 9_000,
                TotalFunctions = 100_000, Truncated = true, Limit = 100_000,
                Functions = BuildSampleEntries(),
            }
        };
        var vm = CreateVm(fake);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.Results);
        Assert.Contains("STOPPED at the 100,000-row cap", vm.StatusText);
    }

    [Fact]
    public async Task ClearOnDisconnect_drops_exec_rows_and_resets_status()
    {
        // X5: a reconnect (often to a different game) must not leave the previous
        // game's exec rows — each carries a live ClassAddr the Run action would use.
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult
            {
                Total = 6, ScannedObjects = 100, ScannedClasses = 5, TotalFunctions = 6,
                Functions = BuildSampleEntries(),
            }
        };
        var vm = CreateVm(fake);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.Results);

        vm.ClearOnDisconnect();

        Assert.Empty(vm.Results);
        Assert.Contains("Click Load", vm.StatusText);   // back to the initial prompt
    }

    [Fact]
    public async Task FilterText_matches_funcName_substring_case_insensitive()
    {
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult
            {
                Functions = BuildSampleEntries(),
                Total = 6, ScannedClasses = 5,
            }
        };
        var vm = CreateVm(fake);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.FilterText = "GIVE";
        Assert.Single(vm.Results);
        Assert.Equal("GiveItem", vm.Results[0].FuncName);

        vm.FilterText = "fly";
        Assert.Single(vm.Results);
        Assert.Equal("Fly", vm.Results[0].FuncName);
    }

    [Fact]
    public async Task FilterText_matches_className_substring_too()
    {
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult { Functions = BuildSampleEntries() }
        };
        var vm = CreateVm(fake);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.FilterText = "CheatMgr";
        Assert.Single(vm.Results);
        Assert.Equal("MyGameCheatMgr", vm.Results[0].ClassName);
    }

    [Fact]
    public async Task RunSelected_with_no_param_exec_invokes_directly_and_appends_history()
    {
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult { Functions = BuildSampleEntries() }
        };
        var vm = CreateVm(fake);
        await vm.LoadCommand.ExecuteAsync(null);

        // Pick UCheatManager::Fly (no params) — index 2 after the sort
        vm.SelectedResult = vm.Results[2];
        Assert.Equal("Fly", vm.SelectedResult.FuncName);

        await vm.RunSelectedCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.InvokeCallCount);
        Assert.Equal("UCheatManager", fake.LastInvokeClass);
        Assert.Equal("Fly", fake.LastInvokeFunc);
        Assert.Equal(0, fake.LastInvokeParmsSize);
        Assert.Null(fake.LastInvokeInstanceAddr);
        Assert.Single(vm.History);
        Assert.True(vm.History[0].Success);
        Assert.Equal("UCheatManager", vm.History[0].ClassName);
        Assert.Equal("Fly", vm.History[0].FuncName);
    }

    [Fact]
    public async Task RunSelected_with_params_raises_RequestParameterInvoke_and_skips_pipe()
    {
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult { Functions = BuildSampleEntries() }
        };
        var vm = CreateVm(fake);
        await vm.LoadCommand.ExecuteAsync(null);

        string? capturedClass = null;
        string? capturedFunc = null;
        string? capturedAddr = null;
        vm.RequestParameterInvoke += (cls, fn, addr) =>
        {
            capturedClass = cls;
            capturedFunc = fn;
            capturedAddr = addr;
        };

        // GiveItem (NumParms=1) is the first row after the sort.
        vm.SelectedResult = vm.Results[0];
        Assert.Equal("GiveItem", vm.SelectedResult.FuncName);

        await vm.RunSelectedCommand.ExecuteAsync(null);

        Assert.Equal("MyGameCheatMgr", capturedClass);
        Assert.Equal("GiveItem", capturedFunc);
        // The row's own UClass address rides along, so the handler never has to look the
        // class up in the capped list_classes page (audit #5 X2).
        Assert.Equal("0x2000", capturedAddr);
        Assert.Equal(0, fake.InvokeCallCount);
        Assert.Empty(vm.History);
    }

    [Fact]
    public async Task RunSelected_with_no_selection_sets_helpful_status()
    {
        var fake = new FakeDumpService { NextListResult = new AllFunctionsResult() };
        var vm = CreateVm(fake);

        Assert.Null(vm.SelectedResult);
        await vm.RunSelectedCommand.ExecuteAsync(null);

        Assert.Contains("Pick an exec command", vm.StatusText);
        Assert.Equal(0, fake.InvokeCallCount);
    }

    [Fact]
    public async Task RunCommandText_resolves_typed_name_and_strips_leading_slash()
    {
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult { Functions = BuildSampleEntries() }
        };
        var vm = CreateVm(fake);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.CommandInput = "/fly";
        await vm.RunCommandTextCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.InvokeCallCount);
        Assert.Equal("Fly", fake.LastInvokeFunc);
    }

    [Fact]
    public async Task RunCommandText_unknown_command_reports_friendly_error()
    {
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult { Functions = BuildSampleEntries() }
        };
        var vm = CreateVm(fake);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.CommandInput = "nonexistent";
        await vm.RunCommandTextCommand.ExecuteAsync(null);

        Assert.Contains("No exec command named 'nonexistent'", vm.StatusText);
        Assert.Equal(0, fake.InvokeCallCount);
    }

    [Fact]
    public async Task RunCommandText_with_inline_args_routes_to_param_dialog()
    {
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult { Functions = BuildSampleEntries() }
        };
        var vm = CreateVm(fake);
        await vm.LoadCommand.ExecuteAsync(null);

        string? capturedFunc = null;
        string? capturedAddr = null;
        vm.RequestParameterInvoke += (_, fn, addr) => { capturedFunc = fn; capturedAddr = addr; };

        // "giveitem 5" — GiveItem takes 1 param; we don't parse "5"
        // yet (FString-input gap), so route to dialog.
        vm.CommandInput = "giveitem 5";
        await vm.RunCommandTextCommand.ExecuteAsync(null);

        Assert.Equal("GiveItem", capturedFunc);
        Assert.Equal("0x2000", capturedAddr);   // typed-command path carries it too (X2)
        Assert.Equal(0, fake.InvokeCallCount);
    }

    [Fact]
    public async Task History_appends_failed_invocations_with_error_text()
    {
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult { Functions = BuildSampleEntries() },
            NextInvokeResult = new InvokeFunctionResult
            {
                Result = -5,
                Error = "ProcessEvent error code -5 (game-thread dispatch timeout)",
            }
        };
        var vm = CreateVm(fake);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SelectedResult = vm.Results[2]; // Fly
        await vm.RunSelectedCommand.ExecuteAsync(null);

        Assert.Single(vm.History);
        Assert.False(vm.History[0].Success);
        Assert.Contains("dispatch timeout", vm.History[0].ResultText);
        Assert.Equal("Err", vm.History[0].Badge);
    }

    [Fact]
    public async Task History_caps_at_max_entries()
    {
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult { Functions = BuildSampleEntries() }
        };
        var vm = CreateVm(fake);
        await vm.LoadCommand.ExecuteAsync(null);

        // Fly is no-arg — fire it 25 times. MaxHistoryEntries is 20.
        vm.SelectedResult = vm.Results[2];
        for (int i = 0; i < 25; i++)
            await vm.RunSelectedCommand.ExecuteAsync(null);

        Assert.Equal(20, vm.History.Count);
        // Newest first
        Assert.Equal("Fly", vm.History[0].FuncName);
    }

    [Fact]
    public async Task ReplayHistory_re_runs_the_same_command()
    {
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult { Functions = BuildSampleEntries() }
        };
        var vm = CreateVm(fake);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SelectedResult = vm.Results[2]; // Fly
        await vm.RunSelectedCommand.ExecuteAsync(null);
        Assert.Equal(1, fake.InvokeCallCount);

        await vm.ReplayHistoryCommand.ExecuteAsync(vm.History[0]);
        Assert.Equal(2, fake.InvokeCallCount);
    }

    // ------------------------------------------------------------------
    // Sticky-instance pinning (the "Debug Camera won't turn off" fix).
    //
    // Stateful exec toggles must land on the SAME UObject across invokes.
    // The first invoke resolves by classname (instanceAddr=null); the DLL
    // echoes back the address it used, and every later invoke of any exec
    // on that class reuses the pinned address.
    // ------------------------------------------------------------------

    [Fact]
    public async Task FirstRun_resolves_by_classname_then_pins_for_subsequent_runs()
    {
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult { Functions = BuildSampleEntries() },
            // DLL resolves + echoes the instance it actually used.
            NextInvokeResult = new InvokeFunctionResult
            {
                Result = 0, Message = "ProcessEvent OK", InstanceAddr = "0x1A2B3C",
            },
        };
        var vm = CreateVm(fake);
        await vm.LoadCommand.ExecuteAsync(null);

        // First: UCheatManager::Fly — no pin yet → classname resolution.
        vm.SelectedResult = vm.Results[2];
        Assert.Equal("Fly", vm.SelectedResult.FuncName);
        await vm.RunSelectedCommand.ExecuteAsync(null);

        // Second: UCheatManager::God — SAME class → reuse the pinned addr.
        vm.SelectedResult = vm.Results[3];
        Assert.Equal("God", vm.SelectedResult.FuncName);
        await vm.RunSelectedCommand.ExecuteAsync(null);

        Assert.Equal(2, fake.InvokeCallCount);
        // Call 1 passed null (resolve), call 2 passed the pinned address.
        Assert.Null(fake.InstanceAddrHistory[0]);
        Assert.Equal("0x1A2B3C", fake.InstanceAddrHistory[1]);
        Assert.Equal("0x1A2B3C", fake.LastInvokeInstanceAddr);
    }

    [Fact]
    public async Task StalePin_failing_invoke_drops_pin_and_retries_with_classname()
    {
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult { Functions = BuildSampleEntries() },
            // Call 1 (Fly): success → pins 0x1234.
            // Call 2 (God, pinned attempt): fails → drop pin.
            // Call 3 (God, classname retry): fails too (still a dead game).
            InvokeResultQueue = new Queue<InvokeFunctionResult>(new[]
            {
                new InvokeFunctionResult { Result = 0,  Message = "OK", InstanceAddr = "0x1234" },
                new InvokeFunctionResult { Result = -2, Error = "vtable read failed" },
                new InvokeFunctionResult { Result = -2, Error = "vtable read failed" },
            }),
        };
        var vm = CreateVm(fake);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SelectedResult = vm.Results[2]; // Fly — pins
        await vm.RunSelectedCommand.ExecuteAsync(null);

        vm.SelectedResult = vm.Results[3]; // God — pinned attempt fails → retry
        await vm.RunSelectedCommand.ExecuteAsync(null);

        Assert.Equal(3, fake.InvokeCallCount);
        Assert.Null(fake.InstanceAddrHistory[0]);          // Fly: resolve
        Assert.Equal("0x1234", fake.InstanceAddrHistory[1]); // God: pinned attempt
        Assert.Null(fake.InstanceAddrHistory[2]);          // God: self-heal retry
    }

    [Fact]
    public async Task Pin_is_per_class_and_not_shared_across_classes()
    {
        // Two no-arg execs on DIFFERENT classes — a pin for one must not
        // leak into the other.
        var entries = new List<AllFunctionEntry>
        {
            new() { ClassName="ClassA", FuncName="DoA",
                    FunctionFlags=FUNC_Exec, NumParms=0, ParmsSize=0 },
            new() { ClassName="ClassB", FuncName="DoB",
                    FunctionFlags=FUNC_Exec, NumParms=0, ParmsSize=0 },
        };
        var fake = new FakeDumpService
        {
            NextInvokeResult = new InvokeFunctionResult
            {
                Result = 0, Message = "OK", InstanceAddr = "0xAAAA",
            },
        };
        var vm = CreateVm(fake);
        vm.SeedForTests(entries);

        // ClassA::DoA — pins ClassA.
        vm.SelectedResult = vm.Results[0];
        Assert.Equal("DoA", vm.SelectedResult.FuncName);
        await vm.RunSelectedCommand.ExecuteAsync(null);

        // ClassB::DoB — different class → must resolve fresh (null), not
        // reuse ClassA's pin.
        vm.SelectedResult = vm.Results[1];
        Assert.Equal("DoB", vm.SelectedResult.FuncName);
        await vm.RunSelectedCommand.ExecuteAsync(null);

        Assert.Null(fake.InstanceAddrHistory[0]); // ClassA: resolve
        Assert.Null(fake.InstanceAddrHistory[1]); // ClassB: resolve (no leak)
    }

    [Fact]
    public async Task Load_clears_stale_pins_from_a_previous_session()
    {
        var fake = new FakeDumpService
        {
            NextListResult = new AllFunctionsResult { Functions = BuildSampleEntries() },
            NextInvokeResult = new InvokeFunctionResult
            {
                Result = 0, Message = "OK", InstanceAddr = "0xCAFE",
            },
        };
        var vm = CreateVm(fake);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SelectedResult = vm.Results[2]; // Fly — resolves (null), then pins 0xCAFE
        await vm.RunSelectedCommand.ExecuteAsync(null);
        Assert.Null(fake.InstanceAddrHistory[0]); // call 1: classname resolution

        // Re-discover (reconnect to a different process) clears pins.
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SelectedResult = vm.Results[2]; // Fly again — pin cleared → resolve fresh
        await vm.RunSelectedCommand.ExecuteAsync(null);

        // Without the Load-clear, call 2 would have reused "0xCAFE"; the
        // null proves the stale pin was dropped.
        Assert.Null(fake.InstanceAddrHistory[1]);
    }

    // ------------------------------------------------------------------
    // Debug Camera state-aware helper. ToggleDebugCamera is a stateful
    // toggle; the helper reads the CheatManager's DebugCameraControllerRef
    // to show ON/OFF and drive deterministic Force On / Force Off.
    // ------------------------------------------------------------------

    private const uint FUNC_Native2 = 0x0000_0400;

    private static List<AllFunctionEntry> DebugCamEntries() => new()
    {
        new() { ClassName="CheatManager", FuncName="ToggleDebugCamera",
                FunctionFlags=FUNC_Exec | FUNC_Native2, NumParms=0, ParmsSize=0 },
        new() { ClassName="CheatManager", FuncName="Fly",
                FunctionFlags=FUNC_Exec | FUNC_Native2, NumParms=0, ParmsSize=0 },
    };

    // The two-hop state read + controller-swap now live DLL-side; the VM is a
    // thin bridge over GetDebugCameraStateAsync / SetDebugCameraAsync. These
    // tests assert the VM calls the right pipe op and maps the tri-state
    // (1=ON / 0=OFF / -1=unknown) onto the badge + status.

    [Fact]
    public void DetectsToggleDebugCamera_and_enables_helper()
    {
        var vm = CreateVm(new FakeDumpService());
        vm.SeedForTests(DebugCamEntries());

        Assert.True(vm.HasDebugCameraToggle);
        Assert.Equal("Unknown", vm.DebugCameraState);
    }

    [Fact]
    public void NoToggleDebugCamera_keeps_helper_hidden()
    {
        var vm = CreateVm(new FakeDumpService());
        vm.SeedForTests(BuildSampleEntries()); // no ToggleDebugCamera

        Assert.False(vm.HasDebugCameraToggle);
    }

    [Fact]
    public async Task RefreshState_reads_ON()
    {
        var fake = new FakeDumpService { NextDebugCameraState = 1 };
        var vm = CreateVm(fake);
        vm.SeedForTests(DebugCamEntries());

        await vm.RefreshDebugCameraStateCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.GetDebugCameraStateCallCount);
        Assert.Equal("ON", vm.DebugCameraState);
        Assert.Contains("ON", vm.StatusText);
    }

    [Fact]
    public async Task RefreshState_reads_OFF()
    {
        var fake = new FakeDumpService { NextDebugCameraState = 0 };
        var vm = CreateVm(fake);
        vm.SeedForTests(DebugCamEntries());

        await vm.RefreshDebugCameraStateCommand.ExecuteAsync(null);

        Assert.Equal("OFF", vm.DebugCameraState);
    }

    [Fact]
    public async Task RefreshState_unknown_when_dll_returns_minus1()
    {
        var fake = new FakeDumpService { NextDebugCameraState = -1 };
        var vm = CreateVm(fake);
        vm.SeedForTests(DebugCamEntries());

        await vm.RefreshDebugCameraStateCommand.ExecuteAsync(null);

        Assert.Equal("Unknown", vm.DebugCameraState);
        Assert.Contains("unknown", vm.StatusText);
    }

    [Fact]
    public async Task ForceOn_calls_SetDebugCamera_true_and_reflects_state()
    {
        var fake = new FakeDumpService { SetDebugCameraResult = 1 };
        var vm = CreateVm(fake);
        vm.SeedForTests(DebugCamEntries());

        await vm.ForceDebugCameraOnCommand.ExecuteAsync(null);

        Assert.Equal(new[] { true }, fake.SetDebugCameraCalls);
        Assert.Equal("ON", vm.DebugCameraState);
        Assert.Contains("forced ON", vm.StatusText);
    }

    [Fact]
    public async Task ForceOff_calls_SetDebugCamera_false_and_reflects_state()
    {
        var fake = new FakeDumpService { SetDebugCameraResult = 0 };
        var vm = CreateVm(fake);
        vm.SeedForTests(DebugCamEntries());

        await vm.ForceDebugCameraOffCommand.ExecuteAsync(null);

        Assert.Equal(new[] { false }, fake.SetDebugCameraCalls);
        Assert.Equal("OFF", vm.DebugCameraState);
        Assert.Contains("forced OFF", vm.StatusText);
    }

    [Fact]
    public async Task ForceOff_warns_when_dll_reports_still_ON()
    {
        // DLL tried the toggle + swap but the camera is still possessing
        // (set_debug_camera returns 1 despite a disable request).
        var fake = new FakeDumpService { SetDebugCameraResult = 1 };
        var vm = CreateVm(fake);
        vm.SeedForTests(DebugCamEntries());

        await vm.ForceDebugCameraOffCommand.ExecuteAsync(null);

        Assert.Equal("ON", vm.DebugCameraState);
        Assert.Contains("re-drive", vm.StatusText);
    }

    [Fact]
    public async Task Force_reports_error_when_dll_returns_minus1()
    {
        var fake = new FakeDumpService { SetDebugCameraResult = -1 };
        var vm = CreateVm(fake);
        vm.SeedForTests(DebugCamEntries());

        await vm.ForceDebugCameraOnCommand.ExecuteAsync(null);

        Assert.Equal("Unknown", vm.DebugCameraState);
        Assert.Contains("no live CheatManager", vm.StatusText);
    }

    [Fact]
    public void CopyDebugCameraScript_raises_RequestDebugCameraCeScript()
    {
        var vm = CreateVm(new FakeDumpService());
        vm.SeedForTests(DebugCamEntries());

        int raised = 0;
        vm.RequestDebugCameraCeScript += () => raised++;

        vm.CopyDebugCameraScriptCommand.Execute(null);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void SeedForTests_filters_and_sorts_outside_the_pipe()
    {
        var fake = new FakeDumpService();
        var vm = CreateVm(fake);

        vm.SeedForTests(BuildSampleEntries());

        Assert.Equal(4, vm.Results.Count);
        Assert.All(vm.Results, r => Assert.True(r.IsExec));
        // Sort order locked
        Assert.Equal("MyGameCheatMgr",   vm.Results[0].ClassName);
        Assert.Equal("PlayerController", vm.Results[1].ClassName);
    }

    [Fact]
    public void IsExec_decoder_matches_FUNC_Exec_bit()
    {
        // Belt-and-braces guard against the wrong bit literal — historically
        // 0x4 was confused for FUNC_Exec (it's actually FUNC_BlueprintAuthorityOnly).
        // UE5 ObjectMacros.h: FUNC_Exec = 0x00000200.
        var exec = new AllFunctionEntry { FunctionFlags = 0x0000_0200 };
        Assert.True(exec.IsExec);

        var nonExec = new AllFunctionEntry { FunctionFlags = 0x0000_0004 };
        Assert.False(nonExec.IsExec);

        var execWithMore = new AllFunctionEntry { FunctionFlags = 0x0400_0200 }; // exec + BlueprintCallable
        Assert.True(execWithMore.IsExec);

        // ShortFlags should include "Exec" when the bit is set
        Assert.Contains("Exec", exec.ShortFlags);
        Assert.DoesNotContain("Exec", nonExec.ShortFlags);
    }

    // ------------------------------------------------------------------
    // Pick #6: UCheatManager stripped-body hint detection.
    //
    // Locks the predicate that decides whether the footer warning shows
    // up. False-negative is the cost mode (silent strip surprise);
    // false-positive is the mild mode (nudge user toward a different
    // verification target). The heuristic prefers covering more
    // subclasses over precision.
    // ------------------------------------------------------------------

    [Theory]
    // Engine class — canonical hit.
    [InlineData("UCheatManager", "",          true)]
    [InlineData("CheatManager",  "",          true)]
    // Game-defined subclass — class name carries the hint.
    [InlineData("MyGameCheatManager", "UCheatManager", true)]
    [InlineData("BP_CheatManager_C",  "CheatManager",  true)]
    // Subclass whose own name doesn't carry "CheatManager" but the
    // SuperName does — covers `class AFooCheats : public UCheatManager`.
    [InlineData("AFooCheats", "UCheatManager", true)]
    // Case-insensitive match — covers `cheatmanager` typos in BPGCs.
    [InlineData("bp_cheatmanager_c", "object", true)]
    // Negative cases — must NOT trigger the hint.
    [InlineData("PlayerController", "Pawn",   false)]
    [InlineData("ACharacter",       "APawn",  false)]
    [InlineData("BP_Player_C",      "ACharacter", false)]
    [InlineData("",                 "",       false)]
    public void IsLikelyUCheatManagerExec_matchesByClassOrSuperName(
        string className, string superName, bool expected)
    {
        var entry = new AllFunctionEntry
        {
            ClassName = className,
            SuperName = superName,
            FuncName  = "DoesNotMatter",
            FunctionFlags = 0x0000_0200,  // exec
        };
        Assert.Equal(expected, ConsoleViewModel.IsLikelyUCheatManagerExec(entry));
    }

    [Fact]
    public void IsLikelyUCheatManagerExec_NullEntry_ReturnsFalse()
    {
        Assert.False(ConsoleViewModel.IsLikelyUCheatManagerExec(null!));
    }

    [Fact]
    public void SelectedExecHint_IsEmpty_WhenNoSelection()
    {
        var vm = CreateVm(new FakeDumpService());
        Assert.Equal("", vm.SelectedExecHint);
    }

    [Fact]
    public void SelectedExecHint_PopulatesForUCheatManager()
    {
        var vm = CreateVm(new FakeDumpService());
        vm.SelectedResult = new AllFunctionEntry
        {
            ClassName = "UCheatManager",
            FuncName  = "Fly",
            FunctionFlags = 0x0000_0200,
        };
        Assert.NotEqual("", vm.SelectedExecHint);
        Assert.Contains("body-stripped",            vm.SelectedExecHint);
        Assert.Contains("cooked Shipping",          vm.SelectedExecHint);
        Assert.Contains("feedback_ucheatmanager_stripped", vm.SelectedExecHint);
    }

    [Fact]
    public void SelectedExecHint_EmptyForUnrelatedClass()
    {
        var vm = CreateVm(new FakeDumpService());
        vm.SelectedResult = new AllFunctionEntry
        {
            ClassName = "PlayerController",
            FuncName  = "ClientMessage",
            FunctionFlags = 0x0000_0200,
        };
        Assert.Equal("", vm.SelectedExecHint);
    }

    [Fact]
    public void SelectedExecHint_RefreshesOnSelectionChange()
    {
        // Locks the OnSelectedResultChanged partial — the property must
        // re-evaluate when SelectedResult flips. Without the
        // notification the panel would show a stale hint after the
        // user changes rows.
        var vm = CreateVm(new FakeDumpService());
        var cheatRow = new AllFunctionEntry
        {
            ClassName = "MyGameCheatManager", FuncName = "Fly",
            FunctionFlags = 0x0000_0200,
        };
        var normalRow = new AllFunctionEntry
        {
            ClassName = "PlayerController", FuncName = "Say",
            FunctionFlags = 0x0000_0200,
        };

        int changes = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.SelectedExecHint)) changes++;
        };

        vm.SelectedResult = cheatRow;
        Assert.True(changes >= 1, "SelectedExecHint must fire PropertyChanged on selection");
        Assert.NotEqual("", vm.SelectedExecHint);

        vm.SelectedResult = normalRow;
        Assert.True(changes >= 2, "PropertyChanged must fire again on subsequent selection");
        Assert.Equal("", vm.SelectedExecHint);
    }
}
