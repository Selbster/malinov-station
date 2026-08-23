using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._MalinovStation.AIPlayers.Actions;
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
            LlmRole.CognitiveDecision,
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
            LlmRole.CognitiveDecision,
            PromptBuilder.BuildCognitiveSystemPrompt(allowedIntents),
            PromptBuilder.BuildCognitiveUserPrompt(context),
            cancellationToken);

        var decision = CognitiveResponseParser.Parse(raw);
        if (raw is not null && decision is null)
            _sawmill.Warning("LLM cognitive decision response failed validation; discarding.");

        return decision;
    }

    public async Task<LlmIntentDecision?> DecideIntentAsync(CognitiveState context, IReadOnlyCollection<string> allowedIntents, IReadOnlyCollection<string> eligibleCategories, CancellationToken cancellationToken)
    {
        var raw = await RequestChatCompletionAsync(
            LlmRole.CognitiveDecision,
            PromptBuilder.BuildIntentSystemPrompt(allowedIntents, eligibleCategories),
            PromptBuilder.BuildIntentUserPrompt(context),
            cancellationToken);

        var decision = IntentResponseParser.Parse(raw);
        if (raw is not null && decision is null)
            _sawmill.Warning("LLM intent decision response failed validation; discarding.");

        return decision;
    }

    public async Task<LlmActionSelectionDecision?> SelectActionAsync(CognitiveState context, LlmIntentDecision intent, IReadOnlyList<IAiAction> eligibleActions, CancellationToken cancellationToken)
    {
        var raw = await RequestChatCompletionAsync(
            LlmRole.ActionSelection,
            PromptBuilder.BuildActionSelectionSystemPrompt(intent.Category, eligibleActions),
            PromptBuilder.BuildActionSelectionUserPrompt(context, intent),
            cancellationToken);

        var decision = ActionSelectionResponseParser.Parse(raw);
        if (raw is not null && decision is null)
            _sawmill.Warning("LLM action selection response failed validation; discarding.");

        return decision;
    }

    public async Task<string?> GenerateLineAsync(DialogueContext context, CancellationToken cancellationToken)
    {
        var raw = await RequestChatCompletionAsync(
            LlmRole.Dialogue,
            PromptBuilder.BuildDialogueSystemPrompt(),
            PromptBuilder.BuildDialogueUserPrompt(context),
            cancellationToken);

        var line = LineResponseParser.Parse(raw);
        if (raw is not null && line is null)
            _sawmill.Warning("LLM line response failed validation; discarding.");

        return line;
    }

    /// <summary>AI Players 0.4 Milestone 4: resolves a logical <see cref="LlmRole"/> to the model that should
    /// actually be requested - the role's own override CVar if set, else the shared <c>ai_players.llm.model</c>
    /// default, so an existing single-model deployment needs zero configuration changes.</summary>
    private string ResolveModel(LlmRole role)
    {
        var roleModel = role switch
        {
            LlmRole.CognitiveDecision => _cfg.GetCVar(MalinovAiPlayerCVars.AiPlayersLlmModelCognitiveDecision),
            LlmRole.ActionSelection => _cfg.GetCVar(MalinovAiPlayerCVars.AiPlayersLlmModelActionSelection),
            LlmRole.MemoryEvaluation => _cfg.GetCVar(MalinovAiPlayerCVars.AiPlayersLlmModelMemoryEvaluation),
            LlmRole.Dialogue => _cfg.GetCVar(MalinovAiPlayerCVars.AiPlayersLlmModelDialogue),
            _ => string.Empty,
        };

        return string.IsNullOrWhiteSpace(roleModel) ? _cfg.GetCVar(MalinovAiPlayerCVars.AiPlayersLlmModel) : roleModel;
    }

    /// <summary>
    /// Sends a chat-completion request and returns the raw <c>choices[0].message.content</c> string, or
    /// null on any failure. Shared by every public method - schema validation is their job, not this one's.
    /// </summary>
    private async Task<string?> RequestChatCompletionAsync(LlmRole role, string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var endpoint = _cfg.GetCVar(MalinovAiPlayerCVars.AiPlayersLlmEndpoint);
        var model = ResolveModel(role);

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
                // Qwen3 (and other hybrid-reasoning models sharing its chat template) reads a literal
                // "/no_think" anywhere in the user turn as a soft switch that skips its extended
                // chain-of-thought before answering - documented behaviour of the model itself, not an
                // API parameter, so it works uniformly across any OpenAI-compatible backend rather than
                // needing a backend-specific request field. A live test against local qwen3:1.7b (thinking
                // on by default) saw every request blow past AiPlayersLlmTimeoutSeconds; models that don't
                // recognize the marker just see harmless trailing text; every response here must be plain
                // JSON per the system prompt regardless, so unrequested reasoning text was never wanted.
                new { role = "user", content = userPrompt + "\n/no_think" },
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
