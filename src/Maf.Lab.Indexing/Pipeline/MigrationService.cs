using System.Diagnostics;
using Maf.Lab.Domain.Admin;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.Logging;

namespace Maf.Lab.Indexing.Pipeline;

/// <summary>
/// Fills a second dense vector with a new embedding model, batch by batch, only for points still on an old
/// model_version. Restartable: a killed run leaves already-migrated points alone and continues from the rest.
/// Queries keep using the configured Retrieval:DenseVector throughout.
/// </summary>
public sealed class MigrationService(
    CollectionBootstrapper bootstrapper,
    TenantScopedMaintenance store,
    IDenseEncoder dense,
    ILogger<MigrationService> logger)
{
    public async Task<MigrationSummary> RunAsync(IReadOnlySet<TenantId> tenants, string targetVector, uint batchSize, CancellationToken ct,
        Func<int, Task>? afterBatch = null)
    {
        await bootstrapper.EnsureAsync(ct);
        var available = await bootstrapper.DenseVectorNamesAsync(ct);
        if (!available.Contains(targetVector))
        {
            throw new InvalidOperationException($"Dense vector '{targetVector}' is not provisioned in the collection.");
        }

        var target = dense.ModelVersion(targetVector);
        var sw = Stopwatch.StartNew();
        long migrated = 0, alreadyCurrent = 0;
        var batchNo = 0;

        foreach (var tenant in tenants.OrderBy(t => t.Value, StringComparer.Ordinal))
        {
            var total = await store.CountAsync(tenant, ct);
            long tenantMigrated = 0;
            while (true)
            {
                var batch = await store.NextMigrationBatchAsync(tenant, target, batchSize, ct);
                if (batch.Count == 0)
                {
                    break;
                }
                var texts = batch.Select(c => c.Context is null ? $"{c.SectionPath}\n{c.Text}" : $"{c.Context}\n{c.SectionPath}\n{c.Text}").ToList();
                var vectors = await dense.EmbedDocumentsAsync(targetVector, texts, ct);
                await store.ApplyMigrationBatchAsync(tenant, targetVector, target, batch.Select((c, i) => (c.PointId, vectors[i])).ToList(), ct);
                tenantMigrated += batch.Count;
                batchNo++;
                if (afterBatch is not null)
                {
                    await afterBatch(batchNo);
                }
            }
            migrated += tenantMigrated;
            alreadyCurrent += total - tenantMigrated;
        }

        logger.LogInformation("Migration to {Vector} ({Model}) done: migrated={Migrated} already_current={Current} ms={Elapsed}",
            targetVector, target, migrated, alreadyCurrent, sw.ElapsedMilliseconds);
        return new MigrationSummary(target, migrated, alreadyCurrent);
    }
}
