using Maf.Lab.Retrieval.Store;

namespace Maf.Lab.CodeSearch;

/// <summary>Creates this server's collections when Qdrant becomes reachable; never blocks startup.</summary>
internal sealed class BootstrapService(CollectionBootstrapper bootstrapper, ILogger<BootstrapService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await bootstrapper.EnsureAsync(stoppingToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Qdrant bootstrap attempt {Attempt} failed: {ErrorType}", attempt, ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, attempt * 2)), stoppingToken);
            }
        }
    }
}
