using Maf.Lab.Domain.SharedState;
using Maf.Lab.TestSupport;

namespace Maf.Lab.Tests;

/// <summary>
/// The protocol says an interrupted stream is sent again. The ledger already refuses to apply one adjustment
/// twice; this is the other promise — that a call whose answer never arrived is answered, not done again.
/// </summary>
public class IdempotencyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_key_nobody_has_used_is_the_caller_s_to_do_the_work_under()
    {
        var store = new FakeIdempotencyStore();
        var (outcome, answer) = await store.CheckAsync("firm-a", "k1", "digest-1", Ct);

        Assert.Equal(IdempotencyOutcome.Fresh, outcome);
        Assert.Null(answer);
    }

    [Fact]
    public async Task The_same_call_under_the_same_key_is_answered_with_the_first_answer()
    {
        var store = new FakeIdempotencyStore();
        await store.RecordAsync("firm-a", "k1", "digest-1", """{"status":"applied"}""", Ct);

        var (outcome, answer) = await store.CheckAsync("firm-a", "k1", "digest-1", Ct);

        Assert.Equal(IdempotencyOutcome.Replay, outcome);
        Assert.Equal("""{"status":"applied"}""", answer!.Answer);
    }

    [Fact]
    public async Task A_different_call_under_a_used_key_is_a_conflict_and_not_an_answer()
    {
        var store = new FakeIdempotencyStore();
        await store.RecordAsync("firm-a", "k1", "digest-1", """{"status":"applied"}""", Ct);

        var (outcome, _) = await store.CheckAsync("firm-a", "k1", "a-different-digest", Ct);

        // Answering this with the first answer would be answering a question nobody asked.
        Assert.Equal(IdempotencyOutcome.Conflict, outcome);
    }

    [Fact]
    public async Task One_firm_s_key_never_answers_another_firm_s_call()
    {
        var store = new FakeIdempotencyStore();
        await store.RecordAsync("firm-a", "k1", "digest-1", """{"status":"applied"}""", Ct);

        var (outcome, _) = await store.CheckAsync("firm-b", "k1", "digest-1", Ct);

        Assert.Equal(IdempotencyOutcome.Fresh, outcome);
    }
}
