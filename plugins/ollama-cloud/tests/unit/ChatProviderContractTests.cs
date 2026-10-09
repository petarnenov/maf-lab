using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Maf.Lab.Plugins.OllamaCloud;
using Maf.Lab.Retrieval.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

public sealed class ChatProviderContractTests
{
    [Fact]
    public async Task Chat_uses_the_requested_model_and_base_options_through_IChatClient()
    {
        JsonNode? body = null;
        using var provider = new OllamaCloudProvider(Options.Create(new ModelOptions { ChatEndpoint = "http://localhost:11434" }),
            () => new Handler(async (request, ct) =>
            {
                body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"model\":\"task-model\",\"message\":{\"role\":\"assistant\",\"content\":\"reply\"},\"done\":true}\n", Encoding.UTF8, "application/x-ndjson"),
                };
            }));
        using IChatClient client = provider.CreateChatClient("task-model");
        var result = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "question")], provider.BaseChatOptions(), TestContext.Current.CancellationToken);

        Assert.Equal("reply", result.Text);
        Assert.Equal("task-model", body!["model"]!.GetValue<string>());
        Assert.False(body["think"]!.GetValue<bool>());
        Assert.Equal(0, body["options"]!["temperature"]!.GetValue<double>());
    }

    [Fact]
    public async Task Cancelling_a_chat_call_cancels_the_provider_request()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var provider = new OllamaCloudProvider(Options.Create(new ModelOptions { ChatEndpoint = "http://localhost:11434" }),
            () => new Handler(async (_, ct) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("unreachable");
            }));
        using IChatClient client = provider.CreateChatClient();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var response = client.GetResponseAsync([new ChatMessage(ChatRole.User, "question")], cancellationToken: stop.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }
}
