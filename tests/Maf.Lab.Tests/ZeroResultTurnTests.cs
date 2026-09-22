using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Feedback;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>
/// What a turn looks like when its search finds nothing. Sources are not chosen by the model — every row the tool
/// returns becomes one — so a search that returns no rows is the only thing that can stop an honest "we have no
/// documentation on that" from carrying citations underneath it.
/// </summary>
public class ZeroResultTurnTests
{
    private const string Empty = """{"results":[],"totalMatches":0,"truncated":false,"refineHint":"No matching documentation. Rephrase as a question about a procedure, policy or term."}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_search_that_returns_nothing_leaves_the_answer_without_sources()
    {
        var tools = new FakeToolSource { SearchPayloadJson = Empty };
        using var api = new ApiFactory(ApiFactory.ProceduralModel("We have no documentation covering that."), tools);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var events = await ApiFactory.ChatAsync(client, "what is JWE");

        Assert.Equal(["search_documents"], tools.Invocations);
        // No rows came back, so nothing can be cited. Before the floors existed this state was unreachable.
        Assert.Null(ApiFactory.SourcesOf(events));
    }

    [Fact]
    public async Task A_search_that_returns_nothing_flags_the_turn_for_review()
    {
        var tools = new FakeToolSource { SearchPayloadJson = Empty };
        using var api = new ApiFactory(ApiFactory.ProceduralModel("We have no documentation covering that."), tools);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        await ApiFactory.ChatAsync(client, "what is JWE");

        await using var db = api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContext();
        var turn = await db.Turns.AsNoTracking().OrderByDescending(t => t.Id).FirstAsync(Ct);
        Assert.Contains(TurnSignal.ZeroRetrievalResults, turn.SignalsJson);
    }

    [Fact]
    public async Task A_search_that_returns_rows_still_cites_them_and_raises_nothing()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var events = await ApiFactory.ChatAsync(client, "what is the procedure when a fee schedule is missing");

        Assert.NotNull(ApiFactory.SourcesOf(events));
        await using var db = api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContext();
        var turn = await db.Turns.AsNoTracking().OrderByDescending(t => t.Id).FirstAsync(Ct);
        Assert.DoesNotContain(TurnSignal.ZeroRetrievalResults, turn.SignalsJson);
    }
}
