using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Domain.Feedback;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Tests;

public partial class FeedbackApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Feedback_for_someone_elses_turn_is_not_found()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var done = (await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), "hello"))[^1].Data;
        var request = new FeedbackRequest(done.GetProperty("threadId").GetString()!, done.GetProperty("runId").GetString()!, FeedbackKind.WrongAnswer, null);

        var response = await api.ClientFor("bianca", "firm-b", Role.USER).PostAsJsonAsync("/api/feedback", request, Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
