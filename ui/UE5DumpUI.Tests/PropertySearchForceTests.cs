using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Tests for the per-row Force-field flow on <see cref="PropertySearchViewModel"/>
/// (Solide): the context-menu actions send the right kind/value/scope, the numeric
/// value prompt + object-null confirm gate the call, the "N held" status reflects
/// the DLL result, and the forced-fields list round-trips get/remove/clear.
/// All over a recording fake IDumpService (no pipe).
/// </summary>
public class PropertySearchForceTests
{
    [Fact]
    public async Task ForceBoolOn_sends_bool_kind_true()
    {
        var dump = new RecordingDump();
        var vm = new PropertySearchViewModel(dump, new NoopLog());

        await vm.ForceBoolOnCommand.ExecuteAsync(NewMatch("BoolProperty", "bInvincible"));

        Assert.Equal(1, dump.ForceCalls);
        Assert.Equal("BP_Enemy_C", dump.LastClass);
        Assert.Equal("bInvincible", dump.LastField);
        Assert.Equal("bool", dump.LastKind);
        Assert.True(dump.LastOn);
    }

    [Fact]
    public async Task ForceBoolOff_sends_bool_kind_false()
    {
        var dump = new RecordingDump();
        var vm = new PropertySearchViewModel(dump, new NoopLog());

        await vm.ForceBoolOffCommand.ExecuteAsync(NewMatch("BoolProperty", "bInvincible"));

        Assert.Equal("bool", dump.LastKind);
        Assert.False(dump.LastOn);
    }

    [Fact]
    public async Task ForceNumeric_prompts_then_sends_numeric_value()
    {
        var dump = new RecordingDump();
        var vm = new PropertySearchViewModel(dump, new NoopLog())
        {
            ForceValuePrompt = _ => Task.FromResult(ForceValuePromptResult.Accept(42.5)),
        };

        await vm.ForceNumericCommand.ExecuteAsync(NewMatch("FloatProperty", "Visibility"));

        Assert.Equal(1, dump.ForceCalls);
        Assert.Equal("numeric", dump.LastKind);
        Assert.Equal(42.5, dump.LastValue);
    }

    /// <summary>
    /// A REJECTED value is not a cancel: nothing is sent (same as cancel), but the user is
    /// told why. Before AF6 both came back as null and the command returned in silence.
    /// </summary>
    [Fact]
    public async Task ForceNumeric_prompt_reject_does_not_send_but_reports_why()
    {
        var dump = new RecordingDump();
        var vm = new PropertySearchViewModel(dump, new NoopLog())
        {
            ForceValuePrompt = _ => Task.FromResult(
                ForceValuePromptResult.Reject("needs more than 53 bits. Refused.")),
        };

        await vm.ForceNumericCommand.ExecuteAsync(NewMatch("Int64Property", "Gold"));

        Assert.Equal(0, dump.ForceCalls);
        Assert.Contains("53 bits", vm.StatusText);
        Assert.Contains("Gold", vm.StatusText);
    }

    [Fact]
    public async Task ForceNumeric_prompt_cancel_does_not_send()
    {
        var dump = new RecordingDump();
        var vm = new PropertySearchViewModel(dump, new NoopLog())
        {
            ForceValuePrompt = _ => Task.FromResult(ForceValuePromptResult.Cancel()),
        };

        await vm.ForceNumericCommand.ExecuteAsync(NewMatch("FloatProperty", "Visibility"));

        Assert.Equal(0, dump.ForceCalls);
    }

    [Fact]
    public async Task ForceNull_confirm_yes_sends_object_null()
    {
        var dump = new RecordingDump();
        var vm = new PropertySearchViewModel(dump, new NoopLog())
        {
            ForceNullConfirm = _ => Task.FromResult(true),
        };

        await vm.ForceNullCommand.ExecuteAsync(NewMatch("ObjectProperty", "TargetActor"));

        Assert.Equal(1, dump.ForceCalls);
        Assert.Equal("object_null", dump.LastKind);
    }

    [Fact]
    public async Task ForceNull_confirm_no_does_not_send()
    {
        var dump = new RecordingDump();
        var vm = new PropertySearchViewModel(dump, new NoopLog())
        {
            ForceNullConfirm = _ => Task.FromResult(false),  // declined
        };

        await vm.ForceNullCommand.ExecuteAsync(NewMatch("ObjectProperty", "TargetActor"));

        Assert.Equal(0, dump.ForceCalls);
    }

    [Fact]
    public async Task Force_zero_held_reports_nothing_held()
    {
        var dump = new RecordingDump { NextForce = new() { Held = 0, Resolved = false } };
        var vm = new PropertySearchViewModel(dump, new NoopLog());

        await vm.ForceBoolOnCommand.ExecuteAsync(NewMatch("BoolProperty", "bInvincible"));

        Assert.Contains("nothing held", vm.StatusText);
    }

    [Fact]
    public async Task RefreshForcedFields_populates_list_and_flag()
    {
        var dump = new RecordingDump
        {
            NextForcedFields = new List<ForcedFieldInfo>
            {
                new() { ClassName = "BP_Enemy_C", FieldName = "bInvincible", Kind = "bool", Held = 3 },
            },
        };
        var vm = new PropertySearchViewModel(dump, new NoopLog());

        await vm.RefreshForcedFieldsAsync();

        Assert.True(vm.HasForcedFields);
        Assert.Single(vm.ForcedFields);
        Assert.Equal(3, vm.ForcedFields[0].Held);
    }

    [Fact]
    public async Task RemoveForcedField_calls_reset_field()
    {
        var dump = new RecordingDump();
        var vm = new PropertySearchViewModel(dump, new NoopLog());

        await vm.RemoveForcedFieldCommand.ExecuteAsync(
            new ForcedFieldInfo { ClassName = "BP_Enemy_C", FieldName = "bInvincible" });

        Assert.Equal(1, dump.ResetFieldCalls);
        Assert.Equal("BP_Enemy_C", dump.LastClass);
    }

    [Fact]
    public async Task ClearAllForcedFields_calls_reset_all()
    {
        var dump = new RecordingDump();
        var vm = new PropertySearchViewModel(dump, new NoopLog());

        await vm.ClearAllForcedFieldsCommand.ExecuteAsync(null);

        Assert.Equal(1, dump.ResetAllCalls);
    }

    // A capped instance pool makes Held a FLOOR, not a total: more live instances exist
    // and are NOT held. Both directions are pinned — asserting only the capped case
    // cannot tell you the uncapped message regressed, and the uncapped one is the common
    // path a user reads.
    [Fact]
    public async Task Force_capped_pool_says_more_exist_unheld()
    {
        var dump = new RecordingDump { NextForce = new() { Held = 256, Resolved = true, Truncated = true } };
        var vm = new PropertySearchViewModel(dump, new NoopLog());

        await vm.ForceBoolOnCommand.ExecuteAsync(NewMatch("BoolProperty", "bInvincible"));

        Assert.Contains("cap reached", vm.StatusText);
        Assert.Contains("256 instance(s)", vm.StatusText);
    }

    [Fact]
    public async Task Force_uncapped_pool_does_not_claim_a_cap()
    {
        var dump = new RecordingDump { NextForce = new() { Held = 3, Resolved = true, Truncated = false } };
        var vm = new PropertySearchViewModel(dump, new NoopLog());

        await vm.ForceBoolOnCommand.ExecuteAsync(NewMatch("BoolProperty", "bInvincible"));

        Assert.DoesNotContain("cap reached", vm.StatusText);
        Assert.Contains("3 instance(s)", vm.StatusText);
    }

    [Fact]
    public async Task RefreshForcedFields_carries_the_capped_flag_onto_the_row()
    {
        var dump = new RecordingDump
        {
            NextForcedFields = new List<ForcedFieldInfo>
            {
                new() { ClassName = "BP_Enemy_C", FieldName = "bInvincible", Kind = "bool", Held = 256, Truncated = true },
                new() { ClassName = "BP_Door_C",  FieldName = "bLocked",     Kind = "bool", Held = 2 },
            },
        };
        var vm = new PropertySearchViewModel(dump, new NoopLog());

        await vm.RefreshForcedFieldsAsync();

        Assert.True(vm.ForcedFields[0].Truncated);    // drives the "⚠ capped" badge
        Assert.False(vm.ForcedFields[1].Truncated);
    }

    [Fact]
    public void ForceEnabled_reflects_experimental_gate()
    {
        var gate = new FakeGate { IsEnabled = false };
        var vm = new PropertySearchViewModel(new RecordingDump(), new NoopLog(),
            aobMaker: null, platform: null, experimentalGate: gate);

        Assert.False(vm.ForceEnabled);
        gate.IsEnabled = true;
        gate.RaiseChanged();
        Assert.True(vm.ForceEnabled);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static PropertySearchMatch NewMatch(string propType, string propName) => new PropertySearchMatch
    {
        ClassName         = "BP_Enemy_C",
        DefiningClassName = "BP_Enemy_C",
        PropName          = propName,
        PropType          = propType,
        PropOffset        = 0x100,
        PropSize          = 4,
    };

    private sealed class RecordingDump : StubDumpService
    {
        public ForceFieldResult NextForce { get; set; } = new() { Held = 1, Resolved = true };
        public IReadOnlyList<ForcedFieldInfo> NextForcedFields { get; set; } = new List<ForcedFieldInfo>();
        public int ForceCalls { get; private set; }
        public int ResetFieldCalls { get; private set; }
        public int ResetAllCalls { get; private set; }
        public string? LastClass { get; private set; }
        public string? LastField { get; private set; }
        public string? LastKind { get; private set; }
        public double LastValue { get; private set; }
        public bool LastOn { get; private set; }

        public override Task<ForceFieldResult> ForceFieldAsync(string className, string fieldName, string kind, double value = 0, bool on = false, CancellationToken ct = default)
        {
            ForceCalls++;
            LastClass = className; LastField = fieldName; LastKind = kind; LastValue = value; LastOn = on;
            return Task.FromResult(NextForce);
        }

        public override Task<int> ResetFieldAsync(string className, string fieldName, CancellationToken ct = default)
        { ResetFieldCalls++; LastClass = className; LastField = fieldName; return Task.FromResult(0); }

        public override Task<int> ResetAllFieldsAsync(CancellationToken ct = default)
        { ResetAllCalls++; return Task.FromResult(0); }

        public override Task<IReadOnlyList<ForcedFieldInfo>> GetForcedFieldsAsync(CancellationToken ct = default)
            => Task.FromResult(NextForcedFields);
    }

    private sealed class FakeGate : IExperimentalGate
    {
        public bool IsEnabled { get; set; }
        public int SnapshotQuotaMb { get; set; }
        public bool IsLocked { get; private set; }
        public void Lock() { IsLocked = true; RaiseChanged(); }
        public event EventHandler? Changed;
        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
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
