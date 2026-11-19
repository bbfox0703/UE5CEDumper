using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Tests for <see cref="EngineState.GameSessionId"/> — the per-launch session
/// token that gates the Snapshot-diff / SPC per-row Live/Addr/🌍 actions.
/// The id must distinguish two launches of the SAME build even when the EXE
/// loads at a CONSTANT base (no effective ASLR, e.g. SEED), which a
/// <c>PeHash-ModuleBase</c> form cannot.
/// </summary>
public class EngineStateTests
{
    [Fact]
    public void GameSessionId_IsPeHashDashCreationTime()
    {
        var s = new EngineState { PeHash = "ABCD1234", ProcessCreationTime = "01D9ABCDEF012345" };
        Assert.Equal("ABCD1234-01D9ABCDEF012345", s.GameSessionId);
    }

    [Fact]
    public void GameSessionId_DiffersPerLaunch_EvenAtSameConstantBase()
    {
        // The whole point of the per-launch token: same build, same (constant)
        // module base, but a different process creation time per launch → the
        // two launches MUST get different session ids so an old-launch snapshot
        // grays out. ModuleBase is intentionally NOT part of the id.
        var launchA = new EngineState
        {
            PeHash = "BUILD1", ModuleBase = "7FF600000000", ProcessCreationTime = "AAAAAAAA",
        };
        var launchB = new EngineState
        {
            PeHash = "BUILD1", ModuleBase = "7FF600000000", ProcessCreationTime = "BBBBBBBB",
        };

        Assert.Equal("BUILD1-AAAAAAAA", launchA.GameSessionId);
        Assert.Equal("BUILD1-BBBBBBBB", launchB.GameSessionId);
        Assert.NotEqual(launchA.GameSessionId, launchB.GameSessionId);
    }

    [Fact]
    public void GameSessionId_OldFormatSnapshot_DoesNotMatchNewCurrent()
    {
        // A snapshot that stored a "PeHash-ModuleBase" id can never equal the
        // current session's "PeHash-CreationTime" one → the gate correctly treats
        // such a snapshot as a different (stale) session.
        var current = new EngineState { PeHash = "BUILD1", ProcessCreationTime = "01D9FFFF" }.GameSessionId;
        const string oldFormatStored = "BUILD1-7FF600000000";   // PeHash-ModuleBase
        Assert.NotEqual(oldFormatStored, current);
    }

    [Fact]
    public void GameSessionId_OlderDll_EmptyCreationTime_DegradesToPeHashDash()
    {
        // A DLL that omits process_creation_time → "" → the id is
        // "PeHash-". Documented degradation (no per-launch split); must not throw.
        var s = new EngineState { PeHash = "ABCD" };
        Assert.Equal("ABCD-", s.GameSessionId);
    }
}
