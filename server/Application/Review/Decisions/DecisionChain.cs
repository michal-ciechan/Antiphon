using System.Diagnostics;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review.Decisions;

/// <summary>One provider call in a chain, as the <c>llm_call</c> record will hold it (design 7.4).</summary>
public sealed record DecisionCall(
    string Chain,
    int Entry,
    string Provider,
    string Transport,
    string? ResolvedModel,
    string Outcome,
    IReadOnlyList<string> Questions,
    long InputTokens,
    long OutputTokens,
    decimal CostUsd,
    string CostBasis,
    TimeSpan Latency,
    string? Detail = null);

/// <summary>The merged answers of a chain, one per question, and every call it made.</summary>
public sealed record DecisionChainResult(IReadOnlyDictionary<string, DecisionAnswer> Answers, IReadOnlyList<DecisionCall> Calls)
{
    public decimal CostUsd => Calls.Sum(c => c.CostUsd);
}

/// <summary>
/// A chain stopped: a provider rejected the request (400/401/403/422, fail loudly), or every entry
/// was skipped. The node fails and an attention item opens (design 7.4).
/// </summary>
public sealed class DecisionChainException(string message, IReadOnlyList<DecisionCall> calls, DecisionFailureKind? kind = null)
    : Exception(message)
{
    public IReadOnlyList<DecisionCall> Calls { get; } = calls;
    public DecisionFailureKind? Kind { get; } = kind;
}

/// <summary>
/// Runs a named decision chain (design 7.4). Each entry is tried in order. An entry is skipped when
/// its provider is not configured, rate-limited after one backoff, timed out, unavailable or returned
/// the wrong shape. A rejected request fails the whole chain with no fallback. Answers below the
/// provider's confidence threshold, and refused answers, escalate that question only.
/// </summary>
public sealed class DecisionChain(
    IEnumerable<IDecisionModel> models,
    IOptions<ReviewDecisionSettings> settings,
    TimeProvider time,
    ILogger<DecisionChain> logger)
{
    private readonly IReadOnlyList<IDecisionModel> _models = models.ToList();

    public async Task<DecisionChainResult> DecideAsync(string chain, DecisionRequest request, CancellationToken ct)
    {
        var options = settings.Value;
        if (!options.Chains.TryGetValue(chain, out var entries) || entries.Count == 0)
            throw new DecisionChainException($"Decision chain '{chain}' is not configured.", [], DecisionFailureKind.NotConfigured);

        var calls = new List<DecisionCall>();
        var accepted = new Dictionary<string, DecisionAnswer>(StringComparer.Ordinal);
        var fallback = new Dictionary<string, DecisionAnswer>(StringComparer.Ordinal);
        var remaining = request.Questions.ToList();

        for (var index = 0; index < entries.Count && remaining.Count > 0; index++)
        {
            var entry = entries[index];
            var asked = remaining.Select(q => q.Name).ToList();
            if (!options.Providers.TryGetValue(entry.Provider, out var provider))
            {
                calls.Add(Skipped(chain, index, entry, asked, "not-configured"));
                continue;
            }

            var model = _models.FirstOrDefault(m => m.Serves(provider.Dialect));
            if (model is null)
            {
                calls.Add(Skipped(chain, index, entry, asked, "not-configured"));
                continue;
            }

            var result = await TryEntryAsync(chain, index, entry, provider, model, request with { Questions = remaining }, asked, calls, ct);
            if (result is null)
                continue;

            var escalate = new List<DecisionQuestion>();
            foreach (var question in remaining)
            {
                if (!result.Answers.TryGetValue(question.Name, out var answer))
                    answer = new DecisionAnswer(question.Name, question.Kind, null, null, null, Refused: true);
                answer = answer with { ServedBy = result.ServedBy };
                var confident = !answer.Refused && (answer.Confidence ?? 0) >= provider.ConfidenceThreshold;
                if (confident)
                {
                    accepted[question.Name] = answer;
                    continue;
                }

                // Keep the most confident non-refused answer as the fallback for the end of the chain.
                if (!fallback.TryGetValue(question.Name, out var kept) || kept.Refused
                    || (!answer.Refused && (answer.Confidence ?? 0) > (kept.Confidence ?? 0)))
                    fallback[question.Name] = answer;
                escalate.Add(question);
            }

            remaining = escalate;
        }

        if (accepted.Count == 0 && fallback.Count == 0)
        {
            throw new DecisionChainException(
                $"Decision chain '{chain}' has no usable entry: {string.Join(", ", calls.Select(Describe))}.",
                calls);
        }

        // Questions still escalated at the end of the chain keep their best answer (or the refusal).
        foreach (var question in remaining)
        {
            accepted[question.Name] = fallback.TryGetValue(question.Name, out var best)
                ? best
                : new DecisionAnswer(question.Name, question.Kind, null, null, null, Refused: true);
        }

        return new DecisionChainResult(accepted, calls);
    }

    private async Task<DecisionResult?> TryEntryAsync(
        string chain,
        int index,
        ReviewDecisionChainEntry entry,
        ReviewDecisionProvider provider,
        IDecisionModel model,
        DecisionRequest request,
        IReadOnlyList<string> asked,
        List<DecisionCall> calls,
        CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(1, provider.TimeoutSeconds));
        var decisionOptions = new DecisionOptions(entry.Provider, provider.Model, timeout);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(timeout);
                DecisionResult result;
                try
                {
                    result = await model.DecideAsync(request, decisionOptions, budget.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new DecisionProviderException(DecisionFailureKind.Timeout, $"No answer within {timeout.TotalSeconds:0}s.");
                }

                calls.Add(new DecisionCall(chain, index, entry.Provider, entry.Transport, result.ServedBy.ResolvedModel, "ok", asked,
                    result.Usage.InputTokens, result.Usage.OutputTokens, result.CostUsd, result.CostBasis, Stopwatch.GetElapsedTime(started)));
                return result;
            }
            catch (DecisionProviderException ex)
            {
                var outcome = Outcome(ex.Kind);
                calls.Add(new DecisionCall(chain, index, entry.Provider, entry.Transport, null, outcome, asked, 0, 0, 0m, "actual",
                    Stopwatch.GetElapsedTime(started), Trim(ex.Message)));
                if (ex.Kind == DecisionFailureKind.Rejected)
                {
                    logger.LogError("Decision chain {Chain} entry {Entry} ({Provider}) rejected the request: {Status} {Message}",
                        chain, index, entry.Provider, ex.StatusCode, ex.Message);
                    var status = ex.StatusCode is int code ? $" ({code})" : "";
                    throw new DecisionChainException(
                        $"Provider '{entry.Provider}' rejected the request{status}: {ex.Message}", calls, ex.Kind);
                }

                if (ex.Kind == DecisionFailureKind.RateLimited && attempt == 0 && !provider.ProxyManagedRetry)
                {
                    var backoff = TimeSpan.FromMilliseconds(Math.Max(0, settings.Value.RateLimitBackoffMilliseconds));
                    if (backoff > TimeSpan.Zero)
                        await Task.Delay(backoff, time, ct);
                    continue;
                }

                logger.LogWarning("Decision chain {Chain} entry {Entry} ({Provider}) skipped: {Outcome} {Message}",
                    chain, index, entry.Provider, outcome, ex.Message);
                return null;
            }
        }

        return null;
    }

    private static DecisionCall Skipped(string chain, int index, ReviewDecisionChainEntry entry, IReadOnlyList<string> asked, string outcome) =>
        new(chain, index, entry.Provider, entry.Transport, null, outcome, asked, 0, 0, 0m, "actual", TimeSpan.Zero);

    private static string Describe(DecisionCall call) =>
        string.IsNullOrWhiteSpace(call.Detail) ? $"{call.Provider}={call.Outcome}" : $"{call.Provider}={call.Outcome} ({call.Detail})";

    private static string Trim(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= 240 ? flat : flat[..240];
    }

    private static string Outcome(DecisionFailureKind kind) => kind switch
    {
        DecisionFailureKind.NotConfigured => "not-configured",
        DecisionFailureKind.RateLimited => "rate-limited",
        DecisionFailureKind.Timeout => "timeout",
        DecisionFailureKind.Unavailable => "unavailable",
        DecisionFailureKind.SchemaFailure => "schema-failure",
        DecisionFailureKind.Rejected => "rejected",
        _ => kind.ToString(),
    };
}
