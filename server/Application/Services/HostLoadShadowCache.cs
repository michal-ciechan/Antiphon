using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Services;

/// <summary>Nullable projection only. Consumers keep connectivity, eligibility and budgets independent.</summary>
public sealed class HostLoadShadowCache(TimeProvider time)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTimeOffset At, HostLoadAdvice Advice)> _values = new(StringComparer.Ordinal);
    public void Set(string runnerId, HostLoadAdvice advice)
    {
        lock (_gate)
        {
            if (_values.Count >= 128 && !_values.ContainsKey(runnerId)) _values.Remove(_values.MinBy(p => p.Value.At).Key);
            _values[runnerId] = (time.GetUtcNow(), advice);
        }
    }
    public HostLoadAdvice? Get(string runnerId)
    {
        lock (_gate)
        {
            if (!_values.TryGetValue(runnerId, out var value)) return null;
            if (time.GetUtcNow() - value.At <= TimeSpan.FromSeconds(15)) return value.Advice;
            return value.Advice with { Decision = new(null, null, "unknown", "unknown", ["sample_stale"], ["sample_stale"]),
                Recommendation = value.Advice.Recommendation with { RecommendedAdditionalTasks = 0, Reasons = ["sample_stale"] } };
        }
    }
    public Antiphon.SessionRunner.Contracts.RunnerLoadShadowDto? Status(string runnerId)
    {
        var advice = Get(runnerId);
        if (advice is null) return null;
        lock (_gate)
            return new(_values[runnerId].At, true, advice.Decision.CommandPressure, advice.Decision.DispatchPressure,
                advice.Decision.Command, advice.Decision.Dispatch, advice.Recommendation.RecommendedAdditionalTasks);
    }
}
