using System.Text.Json.Nodes;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Wire seam for Solide's pool-truncation flag (build 2531).
///
/// The flag exists so the UI can tell "the class/field resolved nothing" from "it resolved
/// more instances than the DLL's cap and silently threw the rest away". That distinction is
/// only worth anything if it survives the JSON hop, and the hop has a trap: an older DLL
/// omits the key entirely, so the parse MUST default to false rather than throwing or
/// (worse) defaulting to true and crying wolf on every hold.
///
/// These assert the parse in BOTH directions. A test that only pins the true case cannot
/// tell you the false case regressed — and the false case is the common one.
/// </summary>
public class SolideTruncationWireTests
{
    private readonly MockPipeClient _pipe = new();
    private readonly MockLoggingService _log = new();

    private DumpService CreateService() => new(_pipe, _log, UE5DumpUI.Core.IdentityCodePage.Instance);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ForceField_parses_truncated_both_ways(bool truncated)
    {
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true, ["held"] = 12, ["resolved"] = true, ["code"] = 0,
            ["truncated"] = truncated,
        });

        var r = await CreateService().ForceFieldAsync("BP_Enemy_C", "bInvincible", "bool", on: true,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(truncated, r.Truncated);
        Assert.Equal(12, r.Held);
    }

    [Fact]
    public async Task ForceField_missing_truncated_defaults_false_for_an_older_dll()
    {
        // No "truncated" key at all — a DLL from before build 2531, or the held<=0 path
        // where the DLL deliberately omits it.
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true, ["held"] = 3, ["resolved"] = true, ["code"] = 0,
        });

        var r = await CreateService().ForceFieldAsync("BP_Enemy_C", "bInvincible", "bool", on: true,
            ct: TestContext.Current.CancellationToken);

        Assert.False(r.Truncated);   // absent must never read as "capped"
        Assert.Equal(3, r.Held);
    }

    [Fact]
    public async Task GetForcedFields_carries_truncated_per_row()
    {
        // Two holds, only the first capped — proves the flag is read per row and not
        // smeared across the list.
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["code"] = 0,
            ["fields"] = new JsonArray
            {
                new JsonObject
                {
                    ["class_name"] = "BP_Enemy_C", ["field_name"] = "bInvincible",
                    ["kind"] = "bool", ["value"] = 1.0, ["held"] = 256, ["truncated"] = true,
                },
                new JsonObject
                {
                    ["class_name"] = "BP_Door_C", ["field_name"] = "bLocked",
                    ["kind"] = "bool", ["value"] = 0.0, ["held"] = 2, ["truncated"] = false,
                },
            },
        });

        var fields = await CreateService().GetForcedFieldsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, fields.Count);
        Assert.True(fields[0].Truncated);
        Assert.Equal(256, fields[0].Held);
        Assert.False(fields[1].Truncated);
    }

    [Fact]
    public async Task GetForcedFields_missing_truncated_defaults_false()
    {
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["code"] = 0,
            ["fields"] = new JsonArray
            {
                new JsonObject
                {
                    ["class_name"] = "BP_Enemy_C", ["field_name"] = "bInvincible",
                    ["kind"] = "bool", ["value"] = 1.0, ["held"] = 4,
                },
            },
        });

        var fields = await CreateService().GetForcedFieldsAsync(TestContext.Current.CancellationToken);

        Assert.Single(fields);
        Assert.False(fields[0].Truncated);
    }
}
