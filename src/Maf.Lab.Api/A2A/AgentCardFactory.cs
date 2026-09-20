using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using A2A;
using Maf.Lab.Retrieval.Configuration;

namespace Maf.Lab.Api.A2A;

/// <summary>
/// The card another agent discovers us by. Each skill says what it is for *and* what it is not for, because a
/// caller choosing a skill from a one-line description is how the wrong agent gets asked the wrong question.
/// </summary>
public static class AgentCardFactory
{
    public const string WellKnownPath = "/.well-known/agent-card.json";
    public const string A2APath = "/a2a";
    public const string PrivateSkillId = "start_billing_run";

    /// <summary>The rendering the signature covers, written so that a stranger can reproduce it from what it received.</summary>
    private static readonly JsonWriterOptions CanonicalWriter = new()
    {
        Indented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AgentCard Public(A2AOptions options) => Build(options, includePrivateSkills: false);

    public static AgentCard Extended(A2AOptions options) => Build(options, includePrivateSkills: true);

    private static AgentCard Build(A2AOptions options, bool includePrivateSkills)
    {
        var baseUrl = options.PublicBaseUrl.TrimEnd('/');
        var skills = new List<AgentSkill>
        {
            new()
            {
                Id = "search_billing_documentation",
                Name = "Search billing documentation",
                Description =
                    "Use when you need the procedure, policy or definition behind a billing question: what to do "
                    + "when a fee schedule is missing, how a tiered fee is calculated, what a failure code means. "
                    + "Do not use for the current state of a billing run, for anything outside the firms you are "
                    + "entitled to, or to change anything — this skill only reads documentation.",
                Tags = ["documentation", "procedures", "read-only"],
                Examples = ["What is the procedure when a fee schedule is missing?"],
            },
            new()
            {
                Id = "billing_run_status",
                Name = "Billing run status",
                Description =
                    "Use to ask the current state of a billing run you are entitled to see, by run id. Do not use "
                    + "to list runs of other firms, to ask why a run failed in procedural terms (use the "
                    + "documentation skill), or to start or change a run.",
                Tags = ["billing", "status", "read-only"],
                Examples = ["What is the status of run 4417?"],
            },
        };
        if (includePrivateSkills)
        {
            skills.Add(new AgentSkill
            {
                Id = PrivateSkillId,
                Name = "Start a billing run",
                Description =
                    "Starts a billing run for a firm you are entitled to and reports progress until it completes. "
                    + "Simulated in this lab: it walks the real task lifecycle over seeded data and does not bill "
                    + "anyone. Do not use it expecting money to move. Requires the period; the task will ask for it "
                    + "if you leave it out.",
                Tags = ["billing", "long-running", "simulated"],
                Examples = ["Start the billing run for firm-a for 2026-06."],
            });
        }

        return new AgentCard
        {
            Name = "maf-lab billing assistant",
            Description =
                "Answers questions about a TAMP billing domain from firm-scoped documentation and billing run data. "
                + "Every answer is grounded in retrieved documents; the agent never invents billing figures.",
            Version = options.AgentVersion,
            DocumentationUrl = $"{baseUrl}/docs/http-api.md",
            SupportedInterfaces =
            [
                // Both transports the SDK maps. gRPC is not offered; see DECISIONS.md.
                new AgentInterface { Url = $"{baseUrl}{A2APath}", ProtocolBinding = ProtocolBindingNames.JsonRpc },
                new AgentInterface { Url = $"{baseUrl}{A2APath}", ProtocolBinding = ProtocolBindingNames.HttpJson },
            ],
            Capabilities = new AgentCapabilities
            {
                Streaming = true,
                PushNotifications = true,
                ExtendedAgentCard = true,
            },
            Skills = skills,
            DefaultInputModes = ["text/plain"],
            DefaultOutputModes = ["text/plain", "application/json"],
            SecuritySchemes = new Dictionary<string, SecurityScheme>
            {
                ["oauth2"] = new SecurityScheme
                {
                    OAuth2SecurityScheme = new OAuth2SecurityScheme
                    {
                        Description = "Client credentials against the lab's issuer.",
                        Flows = new OAuthFlows
                        {
                            ClientCredentials = new ClientCredentialsOAuthFlow
                            {
                                TokenUrl = $"{baseUrl}/a2a/token",
                                Scopes = new Dictionary<string, string>
                                {
                                    [A2AScopes.BillingRead] = "Ask questions about firms you are entitled to.",
                                },
                            },
                        },
                    },
                },
            },
            SecurityRequirements = [ReadRequirement()],
        };
    }

    private static SecurityRequirement ReadRequirement()
    {
        // The SDK leaves Schemes null on a fresh instance.
        return new SecurityRequirement
        {
            Schemes = new Dictionary<string, StringList>
            {
                ["oauth2"] = new() { List = [A2AScopes.BillingRead] },
            },
        };
    }

    /// <summary>
    /// Signs the card with the lab's symmetric key (HS256 JWS, detached payload). A real deployment would sign
    /// asymmetrically so a partner can verify without holding the secret — recorded in DECISIONS.md.
    /// </summary>
    public static AgentCard Signed(AgentCard card, AuthOptions auth)
    {
        var header = Base64Url("""{"alg":"HS256","typ":"JOSE"}"""u8.ToArray());
        var payload = Base64Url(Encoding.UTF8.GetBytes(CanonicalJson(card)));
        card.Signatures =
        [
            new AgentCardSignature
            {
                Protected = header,
                Signature = Base64Url(Sign(auth, $"{header}.{payload}")),
            },
        ];
        return card;
    }

    /// <summary>The documented verification: re-serialise the card without its signatures and recompute.</summary>
    public static bool Verify(AgentCard card, AuthOptions auth)
    {
        var signature = card.Signatures?.FirstOrDefault();
        if (signature?.Protected is null || signature.Signature is null)
        {
            return false;
        }
        var unsigned = CanonicalJson(card);
        var expected = Base64Url(Sign(auth, $"{signature.Protected}.{Base64Url(Encoding.UTF8.GetBytes(unsigned))}"));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature.Signature));
    }

    /// <summary>
    /// The card as the signature covers it: the served document without its own <c>signatures</c>, with every
    /// object's members in lexicographic order and no whitespace. A verifier reproduces it from the card it was
    /// served — it never has to guess this service's property order or serializer settings.
    /// </summary>
    private static string CanonicalJson(AgentCard card)
    {
        var signatures = card.Signatures;
        card.Signatures = null;
        try
        {
            var served = JsonSerializer.SerializeToNode(card, A2AJsonUtilities.DefaultOptions);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, CanonicalWriter))
            {
                WriteCanonical(served, writer);
            }
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
        finally
        {
            card.Signatures = signatures;
        }
    }

    private static void WriteCanonical(System.Text.Json.Nodes.JsonNode? node, Utf8JsonWriter writer)
    {
        switch (node)
        {
            case System.Text.Json.Nodes.JsonObject obj:
                writer.WriteStartObject();
                foreach (var member in obj.OrderBy(m => m.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(member.Key);
                    WriteCanonical(member.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case System.Text.Json.Nodes.JsonArray array:
                writer.WriteStartArray();
                foreach (var item in array)
                {
                    WriteCanonical(item, writer);
                }
                writer.WriteEndArray();
                break;
            case null:
                writer.WriteNullValue();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    private static byte[] Sign(AuthOptions auth, string value) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(auth.SigningKey), Encoding.UTF8.GetBytes(value));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
