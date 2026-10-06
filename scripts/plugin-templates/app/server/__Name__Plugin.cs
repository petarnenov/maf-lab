using Maf.Lab.Plugins.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Maf.Lab.Plugins.$Name;

/// <summary>What the $title page shows: a small record made for it, never a stored entity.</summary>
public sealed record ${Name}Summary(int Conversations, bool MoreThanAPage);

/// <summary>
/// The $name plugin: one route behind the core's gate, over a core port (the caller's own conversations, through
/// <see cref="IConversationStore"/>, which reads the caller from the request). Add a port's reads, never a store's.
/// </summary>
public sealed class ${Name}Plugin : IMafPlugin, IContributesEndpoints
{
    public const string PluginName = "$name";

    public string Name => PluginName;

    public void MapEndpoints(IMafEndpoints endpoints) =>
        endpoints.Routes.MapGet("/api/$name/summary", async (IConversationStore conversations, CancellationToken ct) =>
        {
            var page = await conversations.PageAsync(null, 100, null, ct);
            return Results.Ok(new ${Name}Summary(page.Conversations.Count, page.NextCursor is not null));
        }).RequireAuthorization();
}
