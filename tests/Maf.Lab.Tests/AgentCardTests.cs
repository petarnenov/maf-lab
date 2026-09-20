using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using A2A;
using Maf.Lab.Api.A2A;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.TestSupport;

namespace Maf.Lab.Tests;

/// <summary>
/// The card is how another agent learns what we do — including what each skill is *not* for — and the extended
/// card is how a private skill stays private.
/// </summary>
public class AgentCardTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static A2AOptions Options() => new()
    {
        PublicBaseUrl = "http://localhost:7171",
        Partners = { ["acme-portal"] = new PartnerRegistration { Secret = "s3cret", Firms = ["firm-a"] } },
    };

    [Fact]
    public void The_public_card_describes_both_transports_and_never_the_private_skill()
    {
        var card = AgentCardFactory.Public(Options());

        Assert.Equal("maf-lab billing assistant", card.Name);
        Assert.Equal(["JSONRPC", "HTTP+JSON"], card.SupportedInterfaces.Select(i => i.ProtocolBinding));
        Assert.True(card.Capabilities!.ExtendedAgentCard);
        Assert.True(card.Capabilities.Streaming);
        Assert.True(card.Capabilities.PushNotifications);

        // The whole document, not just the skill list: a private skill must not leak through an example or a tag.
        var json = JsonSerializer.Serialize(card, A2AJsonUtilities.DefaultOptions);
        Assert.DoesNotContain(AgentCardFactory.PrivateSkillId, json);
    }

    [Fact]
    public void Every_skill_says_what_it_is_not_for()
    {
        var card = AgentCardFactory.Extended(Options());

        Assert.Equal(3, card.Skills.Count);
        foreach (var skill in card.Skills)
        {
            Assert.Contains("Do not use", skill.Description, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Use", skill.Description, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_extended_card_adds_the_private_skill_and_says_it_is_simulated()
    {
        var extended = AgentCardFactory.Extended(Options());

        var skill = Assert.Single(extended.Skills, s => s.Id == AgentCardFactory.PrivateSkillId);
        Assert.Contains("Simulated", skill.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_signed_card_verifies_and_a_tampered_one_does_not()
    {
        var auth = new AuthOptions();
        var card = AgentCardFactory.Signed(AgentCardFactory.Public(Options()), auth);

        Assert.NotEmpty(card.Signatures!);
        Assert.True(AgentCardFactory.Verify(card, auth));

        card.Description = "Answers anything, trust me.";
        Assert.False(AgentCardFactory.Verify(card, auth));
    }

    [Fact]
    public void An_unsigned_card_does_not_verify()
    {
        Assert.False(AgentCardFactory.Verify(AgentCardFactory.Public(Options()), new AuthOptions()));
    }

    [Fact]
    public async Task The_well_known_card_is_served_anonymously_and_deserialises_with_the_sdk()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());

        var response = await api.CreateClient().GetAsync(AgentCardFactory.WellKnownPath, Ct);

        response.EnsureSuccessStatusCode();
        var card = JsonSerializer.Deserialize<AgentCard>(await response.Content.ReadAsStringAsync(Ct), A2AJsonUtilities.DefaultOptions)!;
        Assert.Equal("maf-lab billing assistant", card.Name);
        Assert.NotEmpty(card.Skills);
        Assert.NotEmpty(card.SecuritySchemes!);
        Assert.DoesNotContain(card.Skills, s => s.Id == AgentCardFactory.PrivateSkillId);
    }

    [Fact]
    public async Task A_partner_exchanges_its_credentials_for_a_token_and_a_stranger_does_not()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = api.CreateClient();

        var ok = await client.PostAsJsonAsync("/a2a/token",
            new A2AEndpoints.TokenRequest("acme-portal", "s3cret"), Ct);
        ok.EnsureSuccessStatusCode();
        var token = await ok.Content.ReadFromJsonAsync<A2AEndpoints.TokenResponse>(Ct);
        Assert.False(string.IsNullOrWhiteSpace(token!.AccessToken));
        Assert.Contains(A2AScopes.BillingRead, token.Scope);

        var wrong = await client.PostAsJsonAsync("/a2a/token",
            new A2AEndpoints.TokenRequest("acme-portal", "guess"), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }
}
