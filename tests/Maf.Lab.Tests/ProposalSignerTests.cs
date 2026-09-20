using Maf.Lab.Retrieval.Billing;

namespace Maf.Lab.Tests;

/// <summary>A clock the test moves. Small enough not to be worth a package.</summary>
internal sealed class MovableTime(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void SetUtcNow(DateTimeOffset now) => _now = now;
}

public class ProposalSignerTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static FeeAdjustmentProposalState Proposal(DateTimeOffset issuedAt) => new(
        "adj-1", "firm-a", "adam", "A-1042", -200m, "USD", ProposalSigner.DigestOf("client moved to the flat schedule"),
        issuedAt, issuedAt.AddMinutes(30));

    private static (ProposalSigner Signer, MovableTime Time) Signer(string key = "a-test-signing-key-that-is-long-enough")
    {
        var time = new MovableTime(Noon);
        return (new ProposalSigner(key, time), time);
    }

    [Fact]
    public void A_round_trip_returns_the_same_proposal()
    {
        var (signer, _) = Signer();
        var issued = Proposal(Noon);

        var check = signer.Verify(signer.Issue(issued));

        var ok = Assert.IsType<ProposalCheck.Ok>(check);
        Assert.Equal(issued, ok.Proposal);
    }

    [Fact]
    public void A_single_altered_character_is_refused()
    {
        var (signer, _) = Signer();
        var state = signer.Issue(Proposal(Noon));

        // Flip one character of the payload, leaving the signature alone.
        var dot = state.IndexOf('.');
        var flipped = state[..(dot - 1)] + (state[dot - 1] == 'A' ? 'B' : 'A') + state[(dot)..];

        Assert.IsType<ProposalCheck.Altered>(signer.Verify(flipped));
    }

    [Fact]
    public void A_state_signed_with_another_key_is_refused()
    {
        var (mine, _) = Signer();
        var (theirs, _) = Signer("a-different-key-entirely-but-long");

        Assert.IsType<ProposalCheck.Altered>(mine.Verify(theirs.Issue(Proposal(Noon))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-state")]
    [InlineData("no-dot-here")]
    [InlineData("!!!.!!!")]
    [InlineData(".")]
    public void Nonsense_is_refused_rather_than_thrown(string state)
    {
        var (signer, _) = Signer();
        Assert.IsType<ProposalCheck.Altered>(signer.Verify(state));
    }

    [Fact]
    public void Null_is_refused()
    {
        var (signer, _) = Signer();
        Assert.IsType<ProposalCheck.Altered>(signer.Verify(null));
    }

    [Fact]
    public void An_expired_state_is_refused_and_told_apart_from_an_altered_one()
    {
        var (signer, time) = Signer();
        var state = signer.Issue(Proposal(Noon));

        time.SetUtcNow(Noon.AddMinutes(31));

        Assert.IsType<ProposalCheck.Expired>(signer.Verify(state));
    }

    [Fact]
    public void A_state_is_still_good_just_before_it_expires()
    {
        var (signer, time) = Signer();
        var state = signer.Issue(Proposal(Noon));

        time.SetUtcNow(Noon.AddMinutes(29));

        Assert.IsType<ProposalCheck.Ok>(signer.Verify(state));
    }

    [Fact]
    public void The_advisors_reason_does_not_travel_in_the_state()
    {
        var (signer, _) = Signer();
        var state = signer.Issue(Proposal(Noon));

        var decoded = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(state[..state.IndexOf('.')].Replace('-', '+').Replace('_', '/').PadRight((state.IndexOf('.') + 3) / 4 * 4, '=')));

        Assert.DoesNotContain("flat schedule", decoded);
        Assert.Contains(ProposalSigner.DigestOf("client moved to the flat schedule"), decoded);
    }

    [Fact]
    public void A_different_reason_digests_differently()
    {
        Assert.NotEqual(ProposalSigner.DigestOf("one reason"), ProposalSigner.DigestOf("another reason"));
        Assert.Equal(ProposalSigner.DigestOf("one reason"), ProposalSigner.DigestOf("one reason"));
    }
}
