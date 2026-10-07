using Maf.Lab.A2A;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using A2A;
using Maf.Lab.Api.A2A;
using Maf.Lab.Plugins.A2A;
using Maf.Lab.Domain.Configuration;
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
        var card = AgentCardFactory.Public(Options(), AssistantAgentCard.Descriptor);

        Assert.Equal("maf-lab assistant", card.Name);
        Assert.Equal(["JSONRPC", "HTTP+JSON"], card.SupportedInterfaces.Select(i => i.ProtocolBinding));
        Assert.True(card.Capabilities!.ExtendedAgentCard);
        Assert.True(card.Capabilities.Streaming);
        Assert.True(card.Capabilities.PushNotifications);

        // The whole document, not just the skill list: a private skill must not leak through an example or a tag.
        var json = JsonSerializer.Serialize(card, A2AJsonUtilities.DefaultOptions);
        Assert.DoesNotContain(AssistantAgentCard.PrivateSkillId, json);
    }

    [Fact]
    public void Every_skill_says_what_it_is_not_for()
    {
        var card = AgentCardFactory.Extended(Options(), AssistantAgentCard.Descriptor);

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
        var extended = AgentCardFactory.Extended(Options(), AssistantAgentCard.Descriptor);

        var skill = Assert.Single(extended.Skills, s => s.Id == AssistantAgentCard.PrivateSkillId);
        Assert.Contains("Simulated", skill.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_card_is_what_it_was_before_the_card_factory_became_shared()
    {
        // The recorded document is the card this service served before the factory was made agent-agnostic,
        // captured from the running stack. Sharing the factory with a second agent must not have moved a comma; the
        // one change since is the name, "maf-lab billing assistant" until extract-a2a.
        var expected = File.ReadAllText(Path.Combine(CorpusLoaderTests.RepoRoot(), "plugins", "a2a", "tests", "unit", "assistant-agent-card.json"));

        var card = AgentCardFactory.Public(Options(), AssistantAgentCard.Descriptor);

        Assert.Equal(expected, AgentCardFactory.CanonicalJson(card));
    }

    [Fact]
    public void A_second_agent_gets_its_own_card_from_the_same_factory()
    {
        var other = new AgentCardDescriptor(
            "maf-lab compliance reviewer",
            "Reviews a proposed fee adjustment and returns a verdict. Simulated; it binds nobody.",
            [new AgentSkill { Id = "review_fee_adjustment", Name = "Review a fee adjustment", Description = "Use to…" }],
            [],
            new Dictionary<string, string> { ["a2a.compliance.review"] = "Ask for a review." });

        var card = AgentCardFactory.Public(Options(), other);

        Assert.Equal("maf-lab compliance reviewer", card.Name);
        Assert.Equal(["review_fee_adjustment"], card.Skills.Select(s => s.Id));
        Assert.DoesNotContain(AssistantAgentCard.PrivateSkillId, JsonSerializer.Serialize(card, A2AJsonUtilities.DefaultOptions));
        // …and the parts every agent shares are still there.
        Assert.Equal(["JSONRPC", "HTTP+JSON"], card.SupportedInterfaces.Select(i => i.ProtocolBinding));
        Assert.True(AgentCardFactory.Verify(AgentCardFactory.Signed(card, new AuthOptions()), new AuthOptions()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_card_offers_billing_s_skills_only_while_billing_is_installed(bool installed)
    {
        var descriptor = AssistantAgentCard.Installed(new BillingInstalled(installed));
        var billingSkills = AssistantAgentCard.Descriptor.PublicSkills.Concat(AssistantAgentCard.Descriptor.PrivateSkills)
            .Select(s => s.Id).ToList();

        var publicCard = AgentCardFactory.Public(Options(), descriptor);
        var extended = AgentCardFactory.Extended(Options(), descriptor);

        Assert.Equal(installed ? AssistantAgentCard.Descriptor.PublicSkills.Select(s => s.Id) : [], publicCard.Skills.Select(s => s.Id));
        Assert.Equal(installed ? billingSkills : [], extended.Skills.Select(s => s.Id));
        // The agent's scope is its own, whatever is installed; each rendering is signed over what it serves.
        Assert.Equal(AssistantAgentCard.Descriptor.Scopes, descriptor.Scopes);
        var auth = new AuthOptions();
        Assert.True(AgentCardFactory.Verify(AgentCardFactory.Signed(publicCard, auth), auth));
    }

    [Fact]
    public void A_signed_card_verifies_and_a_tampered_one_does_not()
    {
        var auth = new AuthOptions();
        var card = AgentCardFactory.Signed(AgentCardFactory.Public(Options(), AssistantAgentCard.Descriptor), auth);

        Assert.NotEmpty(card.Signatures!);
        Assert.True(AgentCardFactory.Verify(card, auth));

        card.Description = "Answers anything, trust me.";
        Assert.False(AgentCardFactory.Verify(card, auth));
    }

    [Fact]
    public void An_unsigned_card_does_not_verify()
    {
        Assert.False(AgentCardFactory.Verify(AgentCardFactory.Public(Options(), AssistantAgentCard.Descriptor), new AuthOptions()));
    }

    [Fact]
    public async Task The_well_known_card_is_served_anonymously_and_deserialises_with_the_sdk()
    {
        // With billing installed, whose skills the card offers (extract-billing).
        using var api = new ApiFactory(ApiFactory.ProceduralModel())
        {
            InstalledPlugins = [A2APluginSupport.Manifest, new() { Name = "billing", Kind = "mcp", Scope = "tenant", Environments = ["dev"], Description = "billing",
                Progress = "None — fixture", Stopping = "None — fixture" }],
        };

        var response = await api.CreateClient().GetAsync(AgentCardFactory.WellKnownPath, Ct);

        response.EnsureSuccessStatusCode();
        var card = JsonSerializer.Deserialize<AgentCard>(await response.Content.ReadAsStringAsync(Ct), A2AJsonUtilities.DefaultOptions)!;
        Assert.Equal("maf-lab assistant", card.Name);
        Assert.NotEmpty(card.Skills);
        Assert.NotEmpty(card.SecuritySchemes!);
        Assert.DoesNotContain(card.Skills, s => s.Id == AssistantAgentCard.PrivateSkillId);
    }

    [Fact]
    public async Task A_partner_exchanges_its_credentials_for_a_token_and_a_stranger_does_not()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = A2APluginSupport.Installed };
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

/// <summary>An installed set that holds billing, or not.</summary>
file sealed class BillingInstalled(bool installed) : Maf.Lab.Plugins.Abstractions.IInstalledPlugins
{
    public bool IsInstalled(string plugin) => installed && plugin == "billing";

    public string? McpEndpoint(string plugin) => null;
}
