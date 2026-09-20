using System.Text.Json;
using A2A;
using Maf.Lab.A2A;
using Maf.Lab.ComplianceAgent;
using Maf.Lab.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MessageRole = A2A.Role;

namespace Maf.Lab.Tests;

/// <summary>
/// A reviewer that answers with whatever it is told to answer with — the verdicts in
/// `evals/injection-a2a.jsonl`. It speaks the real protocol on a real socket, because the point is what this
/// system does with a hostile answer that arrived the ordinary way.
/// </summary>
public sealed class HostileReviewerFactory : IAsyncDisposable
{
    private WebApplication? app;

    /// <summary>The verdict this reviewer puts in its artifact. Set before the review is asked for.</summary>
    public JsonElement Verdict { get; set; }

    /// <summary>
    /// The adjustment id the fixture wrote for "the one we were asked about". The caller mints its own id at
    /// proposal time, so the reviewer echoes what it was actually sent wherever the fixture used the placeholder
    /// — and leaves every other identifier exactly as the fixture wrote it.
    /// </summary>
    public string? EchoedPlaceholder { get; set; }

    public async Task<string> ListenAsync()
    {
        if (app is not null)
        {
            return Address;
        }

        var builder = WebApplication.CreateBuilder([]);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["A2A:Audience"] = "maf-lab-compliance",
            ["A2A:PublicBaseUrl"] = "",
            ["A2A:RequiredScope"] = ComplianceAgentCard.ReviewScope,
            ["A2A:Partners:maf-lab-assistant:Secret"] = "assistant-secret",
            ["A2A:Partners:maf-lab-assistant:Scopes:0"] = ComplianceAgentCard.ReviewScope,
        });
        builder.Logging.ClearProviders();

        builder.Services.Configure<Maf.Lab.Domain.Configuration.AuthOptions>(
            builder.Configuration.GetSection(Maf.Lab.Domain.Configuration.AuthOptions.Section));
        builder.Services.AddA2APartnerAuthentication(builder.Configuration);
        builder.Services.AddAuthorizationBuilder();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(ComplianceAgentCard.Descriptor);
        builder.Services.AddSingleton<ITaskStore, InMemoryTaskStore>();
        builder.Services.AddSingleton<IPushConfigStore, InMemoryPushConfigStore>();
        builder.Services.AddSingleton<IAgentHandler>(_ => new ScriptedVerdictHandler(() => (Verdict, EchoedPlaceholder)));
        builder.Services.AddSingleton<ChannelEventNotifier>();
        builder.Services.AddSingleton<A2AServer>();
        builder.Services.AddSingleton<IA2ARequestHandler, A2ARequestHandlerWithExtras>();

        app = builder.Build();
        app.UseInstanceHeader();
        app.UseA2ASpecWire();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapInstanceHealth();
        app.MapA2ASurface();
        app.MapA2AProtocol();
        await app.StartAsync();

        app.Services.GetRequiredService<IOptions<A2AOptions>>().Value.PublicBaseUrl = Address.TrimEnd('/');
        return Address;
    }

    public string Address => app!.Services.GetRequiredService<IServer>().Features
        .Get<IServerAddressesFeature>()!.Addresses.First();

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}

/// <summary>Completes every review with the verdict it is handed, whatever that verdict says.</summary>
internal sealed class ScriptedVerdictHandler(Func<(JsonElement Verdict, string? Placeholder)> scripted) : IAgentHandler
{
    public async Task ExecuteAsync(RequestContext context, AgentEventQueue queue, CancellationToken cancellationToken)
    {
        var updater = new TaskUpdater(queue, context.TaskId, context.ContextId);
        if (!context.IsContinuation)
        {
            await updater.SubmitAsync(cancellationToken);
        }
        var (verdict, placeholder) = scripted();
        await updater.AddArtifactAsync([new Part { Data = Echoing(verdict, placeholder, Asked(context)) }],
            name: ReviewAgentHandler.ArtifactName, cancellationToken: cancellationToken);
        await updater.CompleteAsync(new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            Role = MessageRole.Agent,
            Parts = [new Part { Text = "Reviewed." }],
        }, cancellationToken);
    }

    public async Task CancelAsync(RequestContext context, AgentEventQueue queue, CancellationToken cancellationToken) =>
        await new TaskUpdater(queue, context.TaskId, context.ContextId).CancelAsync(cancellationToken);

    /// <summary>The adjustment id the caller actually sent, from the structured part of its request.</summary>
    private static string? Asked(RequestContext context)
    {
        var parts = context.Message?.Parts ?? context.Task?.History?.SelectMany(m => m.Parts ?? []).ToList() ?? [];
        foreach (var part in parts)
        {
            if (part.Data is { } data && data.ValueKind is JsonValueKind.Object
                && data.TryGetProperty("adjustmentId", out var id) && id.GetString() is { Length: > 0 } value)
            {
                return value;
            }
        }
        return null;
    }

    private static JsonElement Echoing(JsonElement verdict, string? placeholder, string? asked)
    {
        if (placeholder is not { Length: > 0 } || asked is not { Length: > 0 }
            || verdict.ValueKind is not JsonValueKind.Object)
        {
            return verdict;
        }
        var node = System.Text.Json.Nodes.JsonNode.Parse(verdict.GetRawText())!.AsObject();
        if (node["adjustmentId"]?.GetValue<string>() == placeholder)
        {
            node["adjustmentId"] = asked;
        }
        return JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());
    }
}
