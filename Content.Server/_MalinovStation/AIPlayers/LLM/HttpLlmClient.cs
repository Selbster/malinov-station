using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._MalinovStation.AIPlayers;
using Robust.Shared.Configuration;

namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// Talks to an OpenAI-compatible chat completions endpoint (works for OpenAI itself, and for
/// Ollama/most local servers, which expose the same shape). Never throws out of either public method -
/// every failure mode (unconfigured, network error, timeout, bad JSON) results in a logged warning and a
/// null result, so the caller can fall back to non-LLM behaviour.
/// </summary>
public sealed partial class HttpLlmClient : ILlmClient, IPostInjectInit
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ILogManager _logManager = default!;

    private static readonly HttpClient Http = new();

    private ISawmill _sawmill = default!;

    public async Task<LlmDecision?> DecideAsync(AiContext context, IReadOnlyCollection<string> allowedIntents, CancellationToken cancellationToken)
    {
        var raw = await RequestChatCompletionAsync(
            PromptBuilder.BuildSystemPrompt(allowedIntents),
            PromptBuilder.BuildUserPrompt(context),
            cancellationToken);

        var decision = ResponseParser.Parse(raw);
        if (raw is not null && decision is null)
            _sawmill.Warning("LLM decision response failed validation; discarding.");

        return decision;
    }

    public async Task<LlmCognitiveDecision?> DecideCognitiveAsync(CognitiveState context, IReadOnlyCollection<string> allowedIntents, CancellationToken cancellationToken)
    {
        var raw = await RequestChatCompletionAsync(
            PromptBuilder.BuildCognitiveSystemPrompt(allowedIntents),
            PromptBuilder.BuildCognitiveUserPrompt(context),
            cancellationToken);

        var decision = CognitiveResponseParser.Parse(raw);
        if (raw is not null && decision is null)
            _sawmill.Warning("LLM cognitive decision response failed validation; discarding.");

        return decision;
    }

    public async Task<string?> GenerateLineAsync(DialogueContext context, CancellationToken cancellationToken)
    {
        var raw = await RequestChatCompletionAsync(
            PromptBuilder.BuildDialogueSystemPrompt(),
            PromptBuilder.BuildDialogueUserPrompt(context),
            cancellationToken);

        var line = LineResponseParser.Parse(raw);
        if (raw is not null && line is null)
            _sawmill.Warning("LLM line response failed validation; discarding.");

        return line;
    }

    /// <summary>
    /// Sends a chat-completion request and returns the raw <c>choices[0].message.content</c> string, or
    /// null on any failure. Shared by both public methods - schema validation is their job, not this one's.
    /// </summary>
    private async Task<string?> RequestChatCompletionAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var endpoint = _cfg.GetCVar(MalinovAiPlayerCVars.AiPlayersLlmEndpoint);
        var model = _cfg.GetCVar(MalinovAiPlayerCVars.AiPlayersLlmModel);

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
        {
            _sawmill.Warning("LLM endpoint/model is not configured; skipping request.");
            return null;
        }

        var apiKey = _cfg.GetCVar(MalinovAiPlayerCVars.AiPlayersLlmApiKey);

        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
            response_format = new { type = "json_object" },
            temperature = 0.7,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload),
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var response = await Http.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _sawmill.Warning($"LLM request failed: {(int)response.StatusCode} {response.StatusCode}");
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ExtractMessageContent(body);
        }
        catch (OperationCanceledException)
        {
            _sawmill.Debug("LLM request timed out or was cancelled.");
            return null;
        }
        catch (Exception e)
        {
            // A network failure reaching an external LLM endpoint is an expected, handled condition (not a
            // bug) - callers fall back to non-LLM behaviour, so this stays below Error severity.
            _sawmill.Warning($"LLM request could not be completed: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Pulls <c>choices[0].message.content</c> out of an OpenAI-compatible chat completions response body.
    /// </summary>
    private string? ExtractMessageContent(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Could not read message content from LLM response: {e.Message}");
            return null;
        }
    }

    void IPostInjectInit.PostInject()
    {
        _sawmill = _logManager.GetSawmill("aiplayers.llm.http");
    }
}
