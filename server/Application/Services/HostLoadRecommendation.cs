using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Services;

// API skeleton for the committed red-first recommendation witnesses.
public sealed class HostLoadRecommendation
{
    public HostLoadAdvice Evaluate(HostLoadPressureInput input, HostLoadCapacityInput capacity,
        IReadOnlyList<HostLoadLaunchObservation> launches, DateTimeOffset now, bool projectSupplied, bool roleSupplied)
        => new(new(null, null, "unknown", "unknown", [], []), new(0, "hostOnly", [], null, 0, 0,
            capacity.HostRemaining, capacity.RunnerSeatsRemaining, capacity.ProjectRemaining, capacity.StageRemaining, capacity.ObservedAt, []));
}
