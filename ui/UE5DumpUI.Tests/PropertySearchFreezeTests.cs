using System.Text.RegularExpressions;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Tests for the property-freeze flow on <see cref="PropertySearchViewModel"/>.
///
/// Covers the gating + handoff contract:
///   * No bridge -> command no-ops with a status hint (no crash).
///   * Bridge unavailable -> CreateAAScriptAsync NOT called; status reflects gating.
///   * Unsupported property type -> NOT called; status names the type.
///   * Dialog cancel -> NOT called; no error status.
///   * Happy path -> CreateAAScriptAsync called with a script containing the
///     class name + offset + value, and uses the defining class when present.
/// </summary>
public class PropertySearchFreezeTests
{
    [Fact]
    public async Task CopyFreezeScript_NoBridge_StatusReflectsMissingBridge()
    {
        var vm = new PropertySearchViewModel(new StubDumpService(), new NoopLog(), aobMaker: null);
        var match = NewMatch("FloatProperty");

        await vm.CopyFreezeScriptCommand.ExecuteAsync(match);

        Assert.Contains("AOBMaker", vm.StatusText);
    }

    [Fact]
    public async Task CopyFreezeScript_BridgeUnavailable_DoesNotCreateAaScript()
    {
        var bridge = new RecordingBridge { NextAvailability = false };
        var vm = new PropertySearchViewModel(new StubDumpService(), new NoopLog(), bridge);
        vm.FreezeValuePrompt = _ => Task.FromResult<string?>("100");

        await vm.CopyFreezeScriptCommand.ExecuteAsync(NewMatch("FloatProperty"));

        Assert.Equal(0, bridge.CreateAaCalls);
        Assert.Contains("AOBMaker", vm.StatusText);
    }

    [Fact]
    public async Task CopyFreezeScript_UnsupportedType_RejectsBeforeDialog()
    {
        var bridge = new RecordingBridge { NextAvailability = true };
        var vm = new PropertySearchViewModel(new StubDumpService(), new NoopLog(), bridge);
        var promptCalled = false;
        vm.FreezeValuePrompt = _ => { promptCalled = true; return Task.FromResult<string?>(null); };

        await vm.CopyFreezeScriptCommand.ExecuteAsync(NewMatch("StructProperty"));

        Assert.False(promptCalled);
        Assert.Equal(0, bridge.CreateAaCalls);
        Assert.Contains("StructProperty", vm.StatusText);
    }

    [Fact]
    public async Task CopyFreezeScript_DialogCancel_DoesNotCreateAaScript()
    {
        var bridge = new RecordingBridge { NextAvailability = true };
        var vm = new PropertySearchViewModel(new StubDumpService(), new NoopLog(), bridge);
        vm.FreezeValuePrompt = _ => Task.FromResult<string?>(null);  // user cancel

        await vm.CopyFreezeScriptCommand.ExecuteAsync(NewMatch("FloatProperty"));

        Assert.Equal(0, bridge.CreateAaCalls);
    }

    [Fact]
    public async Task CopyFreezeScript_Happy_PassesScriptWithClassOffsetValue()
    {
        var bridge = new RecordingBridge { NextAvailability = true, NextCreateResult = true };
        var vm = new PropertySearchViewModel(new StubDumpService(), new NoopLog(), bridge);
        vm.FreezeValuePrompt = _ => Task.FromResult<string?>("9999.0");

        var match = NewMatch("FloatProperty");
        await vm.CopyFreezeScriptCommand.ExecuteAsync(match);

        Assert.Equal(1, bridge.CreateAaCalls);
        Assert.Contains("BP_Teammate_C", bridge.LastScript);
        Assert.Contains("0x4F8", bridge.LastScript);
        Assert.Contains("9999.0", bridge.LastScript);
        Assert.Contains("freezeProperty", bridge.LastScript);
        Assert.Contains("created in CE", vm.StatusText);
    }

    [Fact]
    public async Task CopyFreezeScript_NestedRow_RefusesBeforeDialog()
    {
        // [LEAD-PROPSEARCH-TOPTIONAL-LEAVES] Past a container a deep row's offset is the element struct's; inside a
        // TOptional<FStruct> it lands in the optional's storage whether or not the optional is set. The row hides
        // Freeze, and the command refuses too.
        var bridge = new RecordingBridge { NextAvailability = true, NextCreateResult = true };
        var vm = new PropertySearchViewModel(new StubDumpService(), new NoopLog(), bridge);
        var promptCalled = false;
        vm.FreezeValuePrompt = _ => { promptCalled = true; return Task.FromResult<string?>("1"); };
        var match = NewMatch("FloatProperty");
        match.PropName = "Upgrade.Damage";
        match.IsNested = true;

        await vm.CopyFreezeScriptCommand.ExecuteAsync(match);

        Assert.False(promptCalled);
        Assert.Equal(0, bridge.CreateAaCalls);
        Assert.Contains("Upgrade.Damage", vm.StatusText);
        Assert.Contains("Live Walker", vm.StatusText);
    }

    [Fact]
    public async Task CopyOffset_NestedRow_CopiesNothing()
    {
        // [LEAD-PROPSEARCH-TOPTIONAL-LEAVES] review round 2: the row hides Copy Offset as it hides Freeze, and past a
        // container the offset it would copy is the element's, reading like a class offset. The command refuses too.
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = new PropertySearchViewModel(new StubDumpService(), new NoopLog(), platform: platform);
        var match = NewMatch("FloatProperty");
        match.PropName = "Slots[].Damage";
        match.IsNested = true;

        await vm.CopyOffsetCommand.ExecuteAsync(match);

        Assert.Null(platform.LastClipboard);
        Assert.Contains("Slots[].Damage", vm.StatusText);

        var direct = NewMatch("FloatProperty");   // the control: a class's own field still copies
        await vm.CopyOffsetCommand.ExecuteAsync(direct);
        Assert.Equal(direct.OffsetHex, platform.LastClipboard);
    }

    [Fact]
    public async Task CopyFreezeScript_UsesDefiningClassWhenPresent()
    {
        // The match's ClassName is the resolved class, but DefiningClassName
        // is where the property actually lives. Freezing at the defining
        // class hits every subclass too. Confirm the VM prefers it.
        var bridge = new RecordingBridge { NextAvailability = true, NextCreateResult = true };
        var vm = new PropertySearchViewModel(new StubDumpService(), new NoopLog(), bridge);
        vm.FreezeValuePrompt = _ => Task.FromResult<string?>("100");

        var match = new PropertySearchMatch
        {
            ClassName         = "BP_SpecificTeammate_C",
            DefiningClassName = "BP_Teammate_C",  // engine/base class
            PropName          = "Health",
            PropType          = "FloatProperty",
            PropOffset        = 0x4F8,
            PropSize          = 4,
        };
        await vm.CopyFreezeScriptCommand.ExecuteAsync(match);

        Assert.Equal(1, bridge.CreateAaCalls);
        // Should embed the DEFINING class, not the resolved one
        Assert.Contains("BP_Teammate_C", bridge.LastScript);
    }

    [Fact]
    public async Task CopyFreezeScript_BridgeRejects_StatusFlags()
    {
        var bridge = new RecordingBridge { NextAvailability = true, NextCreateResult = false };
        var vm = new PropertySearchViewModel(new StubDumpService(), new NoopLog(), bridge);
        vm.FreezeValuePrompt = _ => Task.FromResult<string?>("1");

        await vm.CopyFreezeScriptCommand.ExecuteAsync(NewMatch("IntProperty"));

        Assert.Equal(1, bridge.CreateAaCalls);
        Assert.Contains("not sent", vm.StatusText);
    }

    [Fact]
    public async Task Search_ReprobesThePlugin_SoTheNewRowsFreezeButtonsAreCurrent()
    {
        // [PROPSEARCH-FREEZE-STALE-AOBMAKER] Fresh rows are where Freeze appears; a plugin that came up since the tab
        // opened must not leave them disabled until a tab switch.
        var bridge = new RecordingBridge { NextAvailability = true };
        var vm = new PropertySearchViewModel(new StubDumpService(), new NoopLog(), bridge) { SearchQuery = "Health" };
        Assert.False(vm.IsAobMakerAvailable);

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(1, bridge.CheckCalls);
        Assert.True(vm.IsAobMakerAvailable);
    }

    [Fact]
    public async Task Freeze_WhileTheSearchProbeIsInFlight_WaitsForItsAnswer()
    {
        // [PROPSEARCH-FREEZE-STALE-AOBMAKER] review 1, F1. The probe sets its cooldown when it STARTS, so a Freeze
        // clicked while the Search's probe still waits on the pipe skipped its own probe and trusted the old "yes":
        // the value dialog opened over a dead pipe and the push failed with the wrong reason.
        var gate = new TaskCompletionSource<bool>();
        var bridge = new RecordingBridge { NextAvailability = true };
        await bridge.CheckAvailabilityAsync(TestContext.Current.CancellationToken);   // an earlier probe, before CE closed...
        bridge.CheckGate = gate.Task;
        var vm = new PropertySearchViewModel(new StubDumpService(), new NoopLog(), bridge) { SearchQuery = "Health" };
        Assert.True(vm.IsAobMakerAvailable);                 // ...and what the panel starts from
        var prompted = false;
        vm.FreezeValuePrompt = _ => { prompted = true; return Task.FromResult<string?>("1"); };

        await vm.SearchCommand.ExecuteAsync(null);           // its probe now waits on the pipe
        var freeze = vm.CopyFreezeScriptCommand.ExecuteAsync(NewMatch("FloatProperty"));
        Assert.False(prompted);                              // the click waits for that answer...
        gate.SetResult(false);
        await freeze;

        Assert.False(prompted);                              // ...which says the plugin is gone
        Assert.Equal(0, bridge.CreateAaCalls);
        Assert.Equal(2, bridge.CheckCalls);                  // the earlier probe and the Search's: the click made none
        Assert.Contains("not reachable", vm.StatusText);
    }

    [Fact]
    public void TheFlagStartsFromTheSharedStatus_NotTheBridgesCache()
    {
        // [PROPSEARCH-FREEZE-STALE-AOBMAKER] review 1, INFO 1. The panel follows CHANGES of the shared status, so a
        // start from anything else stays wrong until the status changes again.
        var bridge = new RecordingBridge();                  // never probed: its cache says false
        var status = new AobMakerStatus(bridge);
        status.Apply(true);
        var vm = new PropertySearchViewModel(new StubDumpService(), new NoopLog(), bridge, aobStatus: status);

        Assert.True(vm.IsAobMakerAvailable);
    }

    [Fact]
    public async Task WhatTheSearchProbeAndAFreezePushLearn_ReachTheSharedStatus()
    {
        var bridge = new RecordingBridge { NextAvailability = true };
        var status = new AobMakerStatus(bridge);
        var vm = new PropertySearchViewModel(new StubDumpService(), new NoopLog(), bridge, aobStatus: status)
        {
            SearchQuery = "Health",
            FreezeValuePrompt = _ => Task.FromResult<string?>("1"),
        };

        await vm.SearchCommand.ExecuteAsync(null);
        Assert.True(status.IsAvailable);

        bridge.AvailabilityAfterCreate = false;              // the push finds the pipe gone
        await vm.CopyFreezeScriptCommand.ExecuteAsync(NewMatch("FloatProperty"));
        Assert.Equal(1, bridge.CreateAaCalls);
        Assert.False(status.IsAvailable);
        Assert.False(vm.IsAobMakerAvailable);
    }

    [Fact]
    public void ADisposedPanelNoLongerFollowsTheSharedStatus()
    {
        var status = new AobMakerStatus(new RecordingBridge());
        var vm = new PropertySearchViewModel(new StubDumpService(), new NoopLog(), new RecordingBridge(), aobStatus: status);
        vm.Dispose();

        status.Apply(true);

        Assert.False(vm.IsAobMakerAvailable);
    }

    [Fact]
    public void FreezeUnavailableTooltip_ReflectsAvailabilityFlag()
    {
        var bridge = new RecordingBridge { NextAvailability = false };
        var vm = new PropertySearchViewModel(new StubDumpService(), new NoopLog(), bridge);
        Assert.Contains("AOBMaker", vm.FreezeUnavailableTooltip);

        // Drive the property change directly (CheckAvailabilityAsync is
        // async + cooldown-guarded; the public flag is the contract).
        vm.GetType().GetProperty("IsAobMakerAvailable")!.SetValue(vm, true);
        Assert.DoesNotContain("not found", vm.FreezeUnavailableTooltip);
    }

    // ------------------------------------------------------------------
    // [BOOL-NATIVE-SEARCH] -- a bool with no single-bit mask is native OR unresolved, and only the
    // DLL's bool_native tells them apart. This path used to freeze both with a whole-byte write,
    // which on an unresolved PACKED bool stamps 0x01 / 0x00 over up to 7 siblings every tick.
    // ------------------------------------------------------------------

    [Fact]
    public async Task CopyFreezeScript_UnresolvedBool_RefusesBeforeDialog()
    {
        var bridge = new RecordingBridge { NextAvailability = true, NextCreateResult = true };
        var vm = new PropertySearchViewModel(new StubDumpService(), new NoopLog(), bridge);
        var promptCalled = false;
        vm.FreezeValuePrompt = _ => { promptCalled = true; return Task.FromResult<string?>("true"); };

        await vm.CopyFreezeScriptCommand.ExecuteAsync(NewBool(native: false, mask: 0));

        Assert.False(promptCalled);
        Assert.Equal(0, bridge.CreateAaCalls);
        Assert.Contains("bIsInvulnerable", vm.StatusText);
    }

    [Fact]
    public async Task CopyFreezeScript_NativeBool_SendsTheWholeByteScript()
    {
        var bridge = new RecordingBridge { NextAvailability = true, NextCreateResult = true };
        var vm = new PropertySearchViewModel(new StubDumpService(), new NoopLog(), bridge);
        vm.FreezeValuePrompt = _ => Task.FromResult<string?>("true");

        await vm.CopyFreezeScriptCommand.ExecuteAsync(NewBool(native: true, mask: 0));

        Assert.Equal(1, bridge.CreateAaCalls);
        Assert.Contains("valueType          = 'bool',", bridge.LastScript);
        Assert.DoesNotContain("boolMask", bridge.LastScript);
    }

    [Fact]
    public async Task CopyFreezeScript_PackedBool_SendsItsMask()
    {
        var bridge = new RecordingBridge { NextAvailability = true, NextCreateResult = true };
        var vm = new PropertySearchViewModel(new StubDumpService(), new NoopLog(), bridge);
        vm.FreezeValuePrompt = _ => Task.FromResult<string?>("true");

        await vm.CopyFreezeScriptCommand.ExecuteAsync(NewBool(native: false, mask: 0x04));

        Assert.Equal(1, bridge.CreateAaCalls);
        Assert.Contains("boolMask           = 0x04,", bridge.LastScript);
    }

    /// <summary>The DLL half, pinned by source: Fern.cpp is compiled by no test target and
    /// SearchProperties needs a live GObjects. Both matchers must carry WalkClassEx's verdict into
    /// the match, and both search encoders must publish it -- the batch one feeds Interesting
    /// Properties' "Generate Cheat Table", the single one feeds Property Search's Freeze.</summary>
    [Fact]
    public void Dll_BothSearchPaths_PublishBoolNative()
    {
        var aura = File.ReadAllText(NumericInputCoercionTests.RepoFile("dll/src/Aura.cpp"));
        var fern = File.ReadAllText(NumericInputCoercionTests.RepoFile("dll/src/Fern.cpp"));

        Assert.Equal(2, Regex.Matches(aura, @"match\.boolNative\s*=\s*field\.boolNative\s*;").Count);
        Assert.Equal(2, Regex.Matches(fern, @"if \(m\.boolNative\) item\[""bool_native""\] = true;").Count);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static PropertySearchMatch NewBool(bool native, int mask) => new PropertySearchMatch
    {
        ClassName         = "BP_Hero_C",
        DefiningClassName = "BP_Hero_C",
        PropName          = "bIsInvulnerable",
        PropType          = "BoolProperty",
        PropOffset        = 0x328,
        PropSize          = 1,
        BoolFieldMask     = mask,
        BoolNative        = native,
    };

    private static PropertySearchMatch NewMatch(string propType) => new PropertySearchMatch
    {
        ClassName         = "BP_Teammate_C",
        DefiningClassName = "BP_Teammate_C",
        PropName          = "Health",
        PropType          = propType,
        PropOffset        = 0x4F8,
        PropSize          = 4,
    };

    private sealed class RecordingBridge : IAobMakerBridge
    {
        public bool NextAvailability { get; set; }
        public bool NextCreateResult { get; set; }
        /// <summary>When set, a probe waits for this answer instead of <see cref="NextAvailability"/>: the 2 s pipe
        /// connect with Cheat Engine absent.</summary>
        public Task<bool>? CheckGate { get; set; }
        /// <summary>When set, a push leaves the bridge this available (a push that finds the pipe gone).</summary>
        public bool? AvailabilityAfterCreate { get; set; }
        public int CheckCalls { get; private set; }
        public int CreateAaCalls { get; private set; }
        public string LastDescription { get; private set; } = "";
        public string LastScript { get; private set; } = "";

        public bool IsAvailable { get; private set; }

        public async Task<bool> CheckAvailabilityAsync(CancellationToken ct = default)
        {
            CheckCalls++;
            var answer = CheckGate is null ? NextAvailability : await CheckGate;
            IsAvailable = answer;
            return answer;
        }

        public Task<bool> CreateAAScriptAsync(string description, string script,
            bool autoActivate = true, string? group = null, CancellationToken ct = default)
        {
            CreateAaCalls++;
            LastDescription = description;
            LastScript = script;
            if (AvailabilityAfterCreate is bool a) IsAvailable = a;
            return Task.FromResult(NextCreateResult);
        }

        public Task<bool> NavigateHexViewAsync(string h, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> NavigateDisassemblerAsync(string h, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> CreateSymbolScriptAsync(string n, string a, int p, int l, string s, string m,
            bool autoActivate = true, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> CreateMemoryRecordAsync(string description, string address, int valueType,
            bool isSigned = false, bool showAsHex = false, CancellationToken ct = default) => Task.FromResult(false);
        public Task<(bool Ok, string? ErrorMessage)> InjectTableFileAsync(string f, string c,
            CancellationToken ct = default) => Task.FromResult((false, (string?)null));
    }

    private sealed class NoopLog : ILoggingService
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
}
