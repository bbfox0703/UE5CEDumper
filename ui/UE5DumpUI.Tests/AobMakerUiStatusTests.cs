using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [AOBM-UI-INDICATOR] The toolbar's "UI" dot: is the AOBMaker app running? It is decided by LISTING AOBMaker.UI's pipe,
/// never by connecting to it, and an absent name turns it grey only on the second poll in a row, because AOBMaker.UI
/// disposes its one pipe instance before it creates the next.
/// </summary>
public class AobMakerUiStatusTests
{
    [Fact]
    public async Task A_listed_pipe_is_running_at_once()
    {
        var status = new AobMakerUiStatus(new ListedUiClient { Listed = true });
        Assert.True(await status.PollAsync());
        Assert.True(status.IsRunning);
    }

    [Fact]
    public async Task One_miss_is_not_offline_two_are_and_a_sighting_resets_the_count()
    {
        var client = new ListedUiClient { Listed = true };
        var status = new AobMakerUiStatus(client);
        await status.PollAsync();

        client.Listed = false;
        await status.PollAsync();
        Assert.True(status.IsRunning);          // the accept loop's gap, not a closed app
        await status.PollAsync();
        Assert.False(status.IsRunning);

        client.Listed = true;
        await status.PollAsync();
        client.Listed = false;
        await status.PollAsync();
        Assert.True(status.IsRunning);          // the earlier misses do not count any more
    }

    [Fact]
    public async Task A_throwing_client_counts_as_a_miss()
    {
        var status = new AobMakerUiStatus(new ListedUiClient { Throws = true });
        status.Observe(true);
        await status.PollAsync();
        await status.PollAsync();
        Assert.False(status.IsRunning);
    }

    [Fact]
    public async Task No_client_is_never_running_and_says_so()
    {
        var status = new AobMakerUiStatus(null);
        Assert.False(await status.PollAsync());
        Assert.False(status.IsRunning);
        Assert.Contains("not running", status.Tip);
    }

    [Fact]
    public async Task Polls_never_overlap()
    {
        var client = new ListedUiClient { Listed = true, Gate = new TaskCompletionSource<bool>() };
        var status = new AobMakerUiStatus(client);
        var first = status.PollAsync();
        var second = status.PollAsync();        // arrives while the first still waits on the listing
        client.Gate.SetResult(true);
        await Task.WhenAll(first, second);
        Assert.Equal(1, client.ListCalls);
    }

    [Fact]
    public async Task GenerateAob_passes_through_unchanged()
    {
        var answer = new GenerateAobResult(new GeneratedAob("48 8D", 0, 3, 7, 1, "Game.exe"), GenerateAobFailure.None, null);
        var client = new ListedUiClient { Answer = answer };
        var status = new AobMakerUiStatus(client);

        var result = await status.GenerateAobAsync("0x1234", 42);

        Assert.Same(answer, result);
        Assert.Equal(("0x1234", 42), client.GenerateCalls.Single());
        Assert.True(status.IsRunning);          // it answered, so it runs
    }

    [Fact]
    public async Task NotRunning_with_the_pipe_gone_greys_the_dot_at_once()
    {
        var client = new ListedUiClient { Listed = true };
        var status = new AobMakerUiStatus(client);
        await status.PollAsync();

        client.Listed = false;
        client.Answer = new GenerateAobResult(null, GenerateAobFailure.NotRunning, null);
        await status.GenerateAobAsync("0x1", 1);

        Assert.False(status.IsRunning);         // the connect's miss plus the re-list's: two
    }

    [Theory]
    [InlineData(GenerateAobFailure.Busy)]
    [InlineData(GenerateAobFailure.Refused)]
    [InlineData(GenerateAobFailure.NoReply)]
    [InlineData(GenerateAobFailure.Failed)]
    public async Task Any_other_answer_means_a_server_was_there(GenerateAobFailure failure)
    {
        var client = new ListedUiClient { Answer = new GenerateAobResult(null, failure, "m") };
        var status = new AobMakerUiStatus(client);
        await status.GenerateAobAsync("0x1", 1);
        Assert.True(status.IsRunning);
    }

    [Fact]
    public async Task The_tip_follows_the_state()
    {
        var status = new AobMakerUiStatus(new ListedUiClient { Listed = true });
        var raised = new List<string?>();
        status.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await status.PollAsync();

        Assert.Contains(nameof(AobMakerUiStatus.Tip), raised);
        Assert.Contains("running", status.Tip);
        Assert.DoesNotContain("not running", status.Tip);
    }
}

/// <summary>An AOBMaker.UI client whose pipe listing and GenerateAob answer the test sets.</summary>
internal sealed class ListedUiClient : IAobMakerUiClient
{
    public bool Listed { get; set; }
    public bool Throws { get; set; }
    /// <summary>When set, a listing waits for it -- a slow enumeration.</summary>
    public TaskCompletionSource<bool>? Gate { get; set; }
    public int ListCalls { get; private set; }
    public GenerateAobResult Answer { get; set; } = new(null, GenerateAobFailure.NotRunning, null);
    public List<(string Address, int Pid)> GenerateCalls { get; } = new();

    public async Task<bool> IsPipeListedAsync(CancellationToken ct = default)
    {
        ListCalls++;
        if (Gate != null) await Gate.Task;
        if (Throws) throw new IOException("listing refused");
        return Listed;
    }

    public Task<GenerateAobResult> GenerateAobAsync(string hexAddress, int processId, CancellationToken ct = default)
    {
        GenerateCalls.Add((hexAddress, processId));
        return Task.FromResult(Answer);
    }
}
