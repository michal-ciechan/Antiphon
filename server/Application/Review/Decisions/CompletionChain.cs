using System.Diagnostics;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review.Decisions;

/// <summary>The answer of the first entry that answered, and every call the chain made.</summary>
public sealed record CompletionChainResult(string Text, IReadOnlyList<DecisionCall> Calls, DecisionServedBy ServedBy)
{
    public decimal CostUsd => Calls.Sum(c => c.CostUsd);
}

/// <summary>
/// Runs a named completion chain (design 7.4) like <see cref="DecisionChain"/>: each entry in order,
/// skipped when its provider is not configured, rate-limited, timed out, unavailable or wrong-shaped;
/// a rejected request fails the chain with no fallback. There is no per-question escalation: one
/// completion is one answer.
/// </summary>
public sealed class CompletionChain(
    IEnumerable<ICompletionModel> models,
    IOptions<ReviewCompletionSettings> settings,
    ILogger<CompletionChain> logger)
{
    private readonly IReadOnlyList<ICompletionModel> _models = models.ToList();

    /// <summary>True when <paramref name="chain"/> has at least one entry.</summary>
    public bool IsConfigured(string chain) => settings.Value.Chains.TryGetValue(chain, out var entries) && entries.Count > 0;

    public async Task<CompletionChainResult> CompleteAsync(string chain, CompletionRequest request, CancellationToken ct)
    {
        var options = settings.Value;
        if (!options.Chains.TryGetValue(chain, out var entries) || entries.Count == 0)
            throw new DecisionChainException($"Completion chain '{chain}' is not configured.", [], DecisionFailureKind.NotConfigured);

        var calls = new List<DecisionCall>();
        string[] asked = [request.Expect];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (!options.Providers.TryGetValue(entry.Provider, out var provider)
                || _models.FirstOrDefault(m => m.Serves(provider.Dialect)) is not { } model)
            {
                calls.Add(new DecisionCall(chain, index, entry.Provider, entry.Transport, null, "not-configured", asked, 0, 0, 0m, "actual", TimeSpan.Zero));
                continue;
            }

            var timeout = TimeSpan.FromSeconds(Math.Max(1, provider.TimeoutSeconds));
            var started = Stopwatch.GetTimestamp();
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(timeout);
                CompletionResult result;
                try
                {
                    result = await model.CompleteAsync(request, new CompletionOptions(entry.Provider, provider.Model, timeout), budget.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new CompletionProviderException(DecisionFailureKind.Timeout, $"No answer within {timeout.TotalSeconds:0}s.");
                }

                calls.Add(new DecisionCall(chain, index, entry.Provider, entry.Transport, result.ServedBy.ResolvedModel, "ok", asked,
                    result.Usage.InputTokens, result.Usage.OutputTokens, result.CostUsd, result.CostBasis, Stopwatch.GetElapsedTime(started)));
                return new CompletionChainResult(result.Text, calls, result.ServedBy);
            }
            catch (CompletionProviderException ex)
            {
                calls.Add(new DecisionCall(chain, index, entry.Provider, entry.Transport, null, ex.Kind.ToString(), asked, 0, 0, 0m, "actual",
                    Stopwatch.GetElapsedTime(started)));
                if (ex.Kind == DecisionFailureKind.Rejected)
                    throw new DecisionChainException($"Provider '{entry.Provider}' rejected the completion ({ex.StatusCode}): {ex.Message}", calls, ex.Kind);
                logger.LogWarning("Completion chain {Chain} entry {Entry} ({Provider}) skipped: {Kind} {Message}",
                    chain, index, entry.Provider, ex.Kind, ex.Message);
            }
        }

        throw new DecisionChainException(
            $"Completion chain '{chain}' has no usable entry: {string.Join(", ", calls.Select(c => $"{c.Provider}={c.Outcome}"))}.", calls);
    }
}
