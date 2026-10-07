using System.Text.Json;
using Maf.Lab.A2A;
using Maf.Lab.Plugins.Compliance;

namespace Maf.Lab.Tests;

/// <summary>
/// The consultation owns its copy of the reviewer's token request and response and of its card's path, so the plugin needs no core code; the
/// wire is the contract. This pins the two copies to one shape until extract-a2a-plugin gives both sides one definition
/// (DECISIONS §81 part H).
/// </summary>
public class TokenWireTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void The_token_request_goes_over_the_wire_as_the_reviewer_reads_it() =>
        Assert.Equal(
            JsonSerializer.Serialize(new A2AEndpoints.TokenRequest("maf-lab-assistant", "secret", "review"), Web),
            JsonSerializer.Serialize(new ComplianceConsultant.TokenRequest("maf-lab-assistant", "secret", "review"), Web));

    [Fact]
    public void The_token_response_is_read_as_the_reviewer_writes_it() =>
        Assert.Equal(
            JsonSerializer.Serialize(new A2AEndpoints.TokenResponse("token", "Bearer", 300, "review"), Web),
            JsonSerializer.Serialize(new ComplianceConsultant.TokenResponse("token", "Bearer", 300, "review"), Web));

    [Fact]
    public void The_card_is_asked_for_where_the_reviewer_serves_it() =>
        Assert.Equal(AgentCardFactory.WellKnownPath, ComplianceConsultant.WellKnownCardPath);
}
