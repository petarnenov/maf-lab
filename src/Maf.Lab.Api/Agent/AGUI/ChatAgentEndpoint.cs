namespace Maf.Lab.Api.Agent.AGUI;

/// <summary>The chat agent at its AG-UI endpoint, behind the official server (agui-protocol-only).</summary>
public static class ChatAgentEndpoint
{
    public const string Path = "/api/chat";

    public static IEndpointRouteBuilder MapChatAgent(this IEndpointRouteBuilder app)
    {
        app.MapAgent(Path, app.ServiceProvider.GetRequiredService<ChatAgent>(), AGUIMappings.Agents())
            .AddEndpointFilter<ChatRunFilter>();
        return app;
    }
}
