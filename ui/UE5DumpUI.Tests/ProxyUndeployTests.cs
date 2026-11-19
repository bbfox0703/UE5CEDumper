using System;
using System.Collections.Generic;
using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Locks the Undeploy policy. The rule: undeploy is a CLEAN-UP
/// and must sweep every proxy flavour we ship, not the one selected in the UI —
/// scoped to the selection, it would leave a user who deployed dxgi.dll and later
/// moved the radio to version.dll unable to remove it at all, while the grid
/// reported DeployedOtherType at them.
///
/// The decision is tested through the pure helpers rather than the file-touching
/// method, because ownership is decided by <c>FileVersionInfo.ProductName</c> and
/// fabricating a PE with a version resource in a unit test would test the fixture,
/// not the policy.
/// </summary>
public class ProxyUndeployTests
{
    private static readonly IReadOnlyList<string> None = Array.Empty<string>();

    private static (string, bool, bool) Ours(string n)    => (n, true, true);
    private static (string, bool, bool) Foreign(string n) => (n, true, false);
    private static (string, bool, bool) Absent(string n)  => (n, false, false);

    // ── Which files get swept ────────────────────────────────────────

    [Fact]
    public void AllProxyDllNames_covers_every_shipped_flavour()
    {
        var names = ProxyDeployService.AllProxyDllNames();
        Assert.Contains("version.dll", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("dinput8.dll", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("dxgi.dll", names, StringComparer.OrdinalIgnoreCase);
        // One entry per enum value, de-duplicated.
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(Enum.GetValues<ProxyType>().Length, names.Length);
    }

    [Fact]
    public void Plan_removes_our_dll_of_a_type_other_than_the_selected_one()
    {
        // THE case: dxgi deployed, version selected → dxgi must still go.
        var plan = ProxyDeployService.PlanUndeploy(new[]
        {
            Absent("version.dll"), Absent("dinput8.dll"), Ours("dxgi.dll"),
        });
        Assert.Equal(new[] { "dxgi.dll" }, plan.ToDelete);
        Assert.Empty(plan.ForeignSkipped);
    }

    [Fact]
    public void Plan_removes_every_one_of_ours_at_once()
    {
        var plan = ProxyDeployService.PlanUndeploy(new[]
        {
            Ours("version.dll"), Ours("dinput8.dll"), Ours("dxgi.dll"),
        });
        Assert.Equal(3, plan.ToDelete.Count);
        Assert.Empty(plan.ForeignSkipped);
    }

    [Fact]
    public void Plan_never_touches_a_foreign_dll()
    {
        // A mod loader's version.dll must survive even while we remove our dxgi.
        var plan = ProxyDeployService.PlanUndeploy(new[]
        {
            Foreign("version.dll"), Ours("dxgi.dll"), Absent("dinput8.dll"),
        });
        Assert.Equal(new[] { "dxgi.dll" }, plan.ToDelete);
        Assert.Equal(new[] { "version.dll" }, plan.ForeignSkipped);
    }

    [Fact]
    public void Plan_ignores_absent_files()
    {
        var plan = ProxyDeployService.PlanUndeploy(new[]
        {
            Absent("version.dll"), Absent("dinput8.dll"), Absent("dxgi.dll"),
        });
        Assert.Empty(plan.ToDelete);
        Assert.Empty(plan.ForeignSkipped);
    }

    // ── How the outcome is reported ──────────────────────────────────

    [Fact]
    public void Outcome_clean_folder_is_a_success()
    {
        var (status, msg, ok) = ProxyDeployService.ResolveUndeployOutcome(0, None, None);
        Assert.True(ok);
        Assert.Equal(ProxyDeployStatus.NotDeployed, status);
        Assert.Null(msg);
    }

    [Fact]
    public void Outcome_removed_everything_is_a_success()
    {
        var (status, msg, ok) = ProxyDeployService.ResolveUndeployOutcome(2, None, None);
        Assert.True(ok);
        Assert.Equal(ProxyDeployStatus.NotDeployed, status);
        Assert.Null(msg);
    }

    [Fact]
    public void Outcome_only_foreign_present_is_the_old_refusal()
    {
        var (status, msg, ok) = ProxyDeployService.ResolveUndeployOutcome(
            0, new[] { "version.dll" }, None);
        Assert.False(ok);
        Assert.Equal(ProxyDeployStatus.OtherProxy, status);
        Assert.Contains("version.dll", msg!, StringComparison.Ordinal);
    }

    [Fact]
    public void Outcome_removed_ours_and_left_foreign_is_a_success_with_a_note()
    {
        // Refusing to touch someone else's DLL is not a failure of OUR clean-up.
        var (status, msg, ok) = ProxyDeployService.ResolveUndeployOutcome(
            1, new[] { "version.dll" }, None);
        Assert.True(ok);
        Assert.Equal(ProxyDeployStatus.NotDeployed, status);
        Assert.Contains("version.dll", msg!, StringComparison.Ordinal);
    }

    [Fact]
    public void Outcome_locked_outranks_everything_and_names_the_file()
    {
        var (status, msg, ok) = ProxyDeployService.ResolveUndeployOutcome(
            1, new[] { "version.dll" }, new[] { "dxgi.dll" });
        Assert.False(ok);
        Assert.Equal(ProxyDeployStatus.ErrorLocked, status);
        Assert.Contains("dxgi.dll", msg!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Outcome_locked_is_a_failure_regardless_of_how_many_were_removed(int removed)
    {
        var (_, _, ok) = ProxyDeployService.ResolveUndeployOutcome(
            removed, None, new[] { "dinput8.dll" });
        Assert.False(ok);
    }
}
