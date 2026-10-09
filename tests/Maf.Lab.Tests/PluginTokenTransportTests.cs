using Maf.Lab.Api.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

public sealed class PluginTokenTransportTests
{
    [Fact]
    public void The_actual_exchange_client_transport_never_follows_redirects_with_subject_credentials()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var configuration = api.Services.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get(PluginTokenExchange.ClientName);
        var builder = new HandlerBuilder { Name = PluginTokenExchange.ClientName };
        foreach (var configure in configuration.HttpMessageHandlerBuilderActions) configure(builder);
        using var handler = Assert.IsType<HttpClientHandler>(builder.PrimaryHandler);
        Assert.False(handler.AllowAutoRedirect);
    }

    private sealed class HandlerBuilder : HttpMessageHandlerBuilder
    {
        public override string? Name { get; set; }
        public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();
        public override IList<DelegatingHandler> AdditionalHandlers { get; } = new List<DelegatingHandler>();
        public override HttpMessageHandler Build() => PrimaryHandler;
    }
}
