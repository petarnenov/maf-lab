using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Plugins.Code;

/// <summary>
/// The code plugin's in-process part (introduce-plugins 5.2): the codebase domain's behaviour — the structural code
/// question that starts with a graph call — and the Code snippets pane's endpoint. Its descriptor is the manifest's
/// [domain] table and its MCP server the mcp-code service; this part only adds what data cannot express.
/// </summary>
public sealed class CodePlugin : IMafPlugin, IContributesDomainBehaviour, IContributesServices, IContributesEndpoints
{
    public const string PluginName = "code";

    /// <summary>The domain's id, as the manifest's [domain] table names it.</summary>
    public const string DomainId = "codebase";

    public string Name => PluginName;

    public IDomainBehaviour Behaviour { get; } = new CodebaseBehaviour();

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddSingleton<ICodeSnippetSource, McpCodeSnippetSource>();

    public void MapEndpoints(IMafEndpoints endpoints) => CodeSnippetsEndpoints.MapCodeSnippets(endpoints.Routes);
}
