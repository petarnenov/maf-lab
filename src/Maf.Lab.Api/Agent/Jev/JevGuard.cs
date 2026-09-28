using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Agent.Jev;

/// <summary>
/// Screening requests of their own — for text that has no intent request to ride in: a tool-result item, a reviewer's
/// words, a partner's question. Same endpoint, same pinned model and the same named client as the classifier, so the key
/// is set by <see cref="JevAuthHandler"/> and nowhere else. A timeout, an error status, a failure or a missing key is an
/// answer too: <see cref="GuardScores.Failure"/> says which, and the caller's policy decides what that means.
/// </summary>
public sealed class JevGuard(IHttpClientFactory http, JevCredential credential, IOptions<JevOptions> jev, IOptions<GuardOptions> options)
{
    public Task<GuardScores> ScreenPromptAsync(string text, CancellationToken ct) =>
        AskAsync(new JevState(text), JevGuardQuestions.Prompt, ct);

    public Task<GuardScores> ScreenContentAsync(string text, CancellationToken ct) =>
        AskAsync(new JevContentState(text), JevGuardQuestions.Content, ct);

    private async Task<GuardScores> AskAsync(object state, IReadOnlyDictionary<string, object> questions, CancellationToken ct)
    {
        var model = jev.Value.Model;
        var timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
        if (timeout <= TimeSpan.Zero)
        {
            return GuardScores.Failed("screening disabled", model, 0);
        }
        if (!credential.IsConfigured)
        {
            return GuardScores.Failed("no key", model, 0);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var call = PostAsync(state, questions, model, cts.Token);
            // As in the classifier: the race is what the caller waits on, so a transport that ignores cancellation costs
            // the timeout and no more.
            if (await Task.WhenAny(call, Task.Delay(timeout, ct)) != call)
            {
                _ = call.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
                return GuardScores.Failed($"timed out after {options.Value.TimeoutSeconds}s", model, sw.Elapsed.TotalMilliseconds);
            }
            var (status, body) = await call;
            if (body is null)
            {
                return GuardScores.Failed($"rejected ({status})", model, sw.Elapsed.TotalMilliseconds);
            }
            var scores = JevGuardQuestions.Read(body.Answers, questions.Keys);
            return scores is null
                ? GuardScores.Failed("no answer", body.Model ?? model, sw.Elapsed.TotalMilliseconds)
                : new GuardScores(scores, body.Model ?? model, sw.Elapsed.TotalMilliseconds, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return GuardScores.Failed($"timed out after {options.Value.TimeoutSeconds}s", model, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return GuardScores.Failed(ex.GetType().Name, model, sw.Elapsed.TotalMilliseconds);
        }
    }

    private async Task<(int Status, JevResponse? Body)> PostAsync(object state, IReadOnlyDictionary<string, object> questions,
        string model, CancellationToken ct)
    {
        var request = new JevGuardRequest(model, state, questions);
        // Buffered with a Content-Length, like the classifier's request: the CI stub does not read a chunked body.
        using var content = new StringContent(JsonSerializer.Serialize(request, JevRequest.Json), Encoding.UTF8, "application/json");
        using var response = await http.CreateClient(JevIntentClassifier.HttpClientName).PostAsync("v1/systemone", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            return ((int)response.StatusCode, null);
        }
        return ((int)response.StatusCode, await response.Content.ReadFromJsonAsync<JevResponse>(JevRequest.Json, ct));
    }
}
