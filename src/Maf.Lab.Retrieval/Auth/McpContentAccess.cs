using Maf.Lab.Domain.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;

namespace Maf.Lab.Retrieval.Auth;

/// <summary>Official SDK filters protect direct protocol content reads, independently of the API's route filter.</summary>
public static class McpContentAccess
{
    public static IMcpServerBuilder WithOperatorContentAccess(this IMcpServerBuilder builder) => builder.WithRequestFilters(filters =>
    {
        filters.AddCallToolFilter(next => async (context, ct) => { await CheckAsync(context.Services, ct); return await next(context, ct); });
        filters.AddReadResourceFilter(next => async (context, ct) => { await CheckAsync(context.Services, ct); return await next(context, ct); });
        filters.AddGetPromptFilter(next => async (context, ct) => { await CheckAsync(context.Services, ct); return await next(context, ct); });
        filters.AddListResourcesFilter(next => async (context, ct) => { await CheckAsync(context.Services, ct); return await next(context, ct); });
        filters.AddListResourceTemplatesFilter(next => async (context, ct) => { await CheckAsync(context.Services, ct); return await next(context, ct); });
        filters.AddListPromptsFilter(next => async (context, ct) => { await CheckAsync(context.Services, ct); return await next(context, ct); });
    });

    private static async Task CheckAsync(IServiceProvider? services, CancellationToken ct)
    {
        if (services is null) throw new McpProtocolException("Authenticated content access is required.", McpErrorCode.InvalidRequest);
        var principal = services.GetRequiredService<IPrincipalAccessor>().Current;
        if (!await services.GetRequiredService<IOperatorContentAccess>().MayReadAsync(principal, ct))
            throw new McpProtocolException("Operator content access requires an active break-glass grant.", McpErrorCode.InvalidRequest);
    }
}
