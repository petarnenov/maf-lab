using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using A2A;
using Maf.Lab.Domain.Configuration;

namespace Maf.Lab.A2A;

/// <summary>
/// The card another agent discovers an agent by. What differs between agents comes in as an
/// <see cref="AgentCardDescriptor"/>; what every agent in this lab shares — the two transports, the client
/// credentials scheme, the signature and the rendering it covers — is here, so the two cards cannot drift apart.
/// </summary>
public static class AgentCardFactory
{
    public const string WellKnownPath = "/.well-known/agent-card.json";
    public const string A2APath = "/a2a";
    public const string TokenPath = "/a2a/token";

    /// <summary>The rendering the signature covers, written so that a stranger can reproduce it from what it received.</summary>
    private static readonly JsonWriterOptions CanonicalWriter = new()
    {
        Indented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AgentCard Public(A2AOptions options, AgentCardDescriptor agent) =>
        Build(options, agent, includePrivateSkills: false);

    public static AgentCard Extended(A2AOptions options, AgentCardDescriptor agent) =>
        Build(options, agent, includePrivateSkills: true);

    private static AgentCard Build(A2AOptions options, AgentCardDescriptor agent, bool includePrivateSkills)
    {
        var baseUrl = options.PublicBaseUrl.TrimEnd('/');
        List<AgentSkill> skills = [.. agent.PublicSkills];
        if (includePrivateSkills)
        {
            skills.AddRange(agent.PrivateSkills);
        }

        return new AgentCard
        {
            Name = agent.Name,
            Description = agent.Description,
            Version = options.AgentVersion,
            DocumentationUrl = $"{baseUrl}{agent.DocumentationPath}",
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
                                TokenUrl = $"{baseUrl}{TokenPath}",
                                Scopes = new Dictionary<string, string>(agent.Scopes),
                            },
                        },
                    },
                },
            },
            SecurityRequirements = [Requirement(agent)],
        };
    }

    private static SecurityRequirement Requirement(AgentCardDescriptor agent)
    {
        // The SDK leaves Schemes null on a fresh instance.
        return new SecurityRequirement
        {
            Schemes = new Dictionary<string, StringList>
            {
                ["oauth2"] = new() { List = [.. agent.Scopes.Keys] },
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
    /// The card as the signature covers it — and the form a verifier reproduces: the served document without its
    /// own <c>signatures</c>, with every
    /// object's members in lexicographic order and no whitespace. A verifier reproduces it from the card it was
    /// served — it never has to guess this service's property order or serializer settings.
    /// </summary>
    public static string CanonicalJson(AgentCard card)
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
