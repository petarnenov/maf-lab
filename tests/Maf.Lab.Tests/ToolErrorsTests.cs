using Grpc.Core;
using Maf.Lab.Retrieval.Tools;
using ModelContextProtocol.Protocol;

namespace Maf.Lab.Tests;

public class ToolErrorsTests
{
    [Fact]
    public void Error_marks_the_result_as_an_error_with_the_text_as_its_only_content()
    {
        var result = ToolErrors.Error("query is required: pass a natural-language phrase.");

        Assert.True(result.IsError);
        var block = Assert.Single(result.Content);
        var text = Assert.IsType<TextContentBlock>(block);
        Assert.Equal("query is required: pass a natural-language phrase.", text.Text);
    }

    [Fact]
    public void Transient_grpc_and_http_failures_report_the_capability_as_temporarily_unavailable()
    {
        Assert.Equal(
            "Document search is temporarily unavailable; try again shortly.",
            ToolErrors.ForException(new RpcException(new Status(StatusCode.Unavailable, "backend down")), "Document search"));
        Assert.Equal(
            "Document search is temporarily unavailable; try again shortly.",
            ToolErrors.ForException(new HttpRequestException("connection refused"), "Document search"));
    }

    [Fact]
    public void Transient_timeout_and_cancellation_failures_report_the_capability_as_temporarily_unavailable()
    {
        Assert.Equal(
            "Fee adjustment is temporarily unavailable; try again shortly.",
            ToolErrors.ForException(new TimeoutException("timed out"), "Fee adjustment"));
        Assert.Equal(
            "Fee adjustment is temporarily unavailable; try again shortly.",
            ToolErrors.ForException(new TaskCanceledException("cancelled"), "Fee adjustment"));
    }

    [Fact]
    public void Unauthorized_access_reports_that_the_request_is_not_authorized()
    {
        var message = ToolErrors.ForException(new UnauthorizedAccessException(), "Document search");

        Assert.Equal("The request is not authorized.", message);
    }

    [Fact]
    public void Any_other_failure_reports_the_capability_as_failed()
    {
        var message = ToolErrors.ForException(new InvalidOperationException("detailed internals"), "Fee adjustment");

        Assert.Equal("Fee adjustment failed; try rephrasing the request.", message);
    }
}