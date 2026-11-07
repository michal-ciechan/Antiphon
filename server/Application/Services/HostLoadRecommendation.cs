using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Services;

/// <summary>Pure advisory calculation. No reservation, budget or admission state can be changed here.</summary>
public sealed class HostLoadRecommendation
{
    public HostLoadAdvice Evaluate(HostLoadPressureInput input, HostLoadCapacityInput capacity,
        IReadOnlyList<HostLoadLaunchObservation> launches, DateTimeOffset now, bool projectSupplied, bool roleSupplied)
    {
        var scoped = projectSupplied && roleSupplied;
        var missing = new List<string>();
        if (!projectSupplied) missing.Add("project"); if (!roleSupplied) missing.Add("role");
        var identityKnown = input.BindingKnown && input.PhysicalHostKey is not null
            && input.PhysicalHostKey == input.ExpectedPhysicalHostKey;
        var fresh = Fresh(input.SampledAt, now);
        var latest = Valid(input.LatestCpu);
        var commandKnown = identityKnown && fresh && latest && Valid(input.Average15s)
            && input.Valid15s == 3 && input.Expected15s == 3;
        var dispatchKnown = identityKnown && fresh && latest && Valid(input.Average60s)
            && input.Valid60s == 12 && input.Expected60s == 12;
        double? command = commandKnown ? Math.Max(input.LatestCpu!.Value, input.Average15s!.Value) : null;
        double? dispatch = dispatchKnown ? Math.Max(input.LatestCpu!.Value, input.Average60s!.Value) : null;
        var debt = launches.Where(l => !l.Represented && !l.StartupFailed)
            .DistinctBy(l => (l.TaskId, l.Attempt, l.WindowOrdinal)).Count() * 10d;
        var commandState = command is null ? "unknown" : command < 85 ? "eligible" : "hold";
        var dispatchState = dispatch is null ? "unknown" : dispatch < 75 ? "eligible" : "hold";
        var commandReasons = command is null ? new[] { fresh ? "sample_unknown" : "sample_stale" }
            : command >= 85 ? ["cpu_high"] : Array.Empty<string>();
        var dispatchReasons = dispatch is null ? new[] { fresh ? "sample_unknown" : "sample_stale" }
            : dispatch >= 75 ? ["cpu_high"] : Array.Empty<string>();
        var reasons = new List<string>(dispatchReasons);
        var headroom = dispatch is null ? 0 : Math.Max(0, (int)Math.Floor((75 - dispatch.Value - debt) / 10));
        var capacityKnown = Fresh(capacity.ObservedAt, now) && capacity.HostRemaining is >= 0
            && capacity.RunnerSeatsRemaining is >= 0 && (!scoped || (capacity.ProjectRemaining is >= 0 && capacity.StageRemaining is >= 0));
        var additional = headroom;
        if (!capacityKnown) { additional = 0; reasons.Add("capacity_unknown"); }
        else
        {
            additional = Math.Min(additional, capacity.HostRemaining!.Value);
            additional = Math.Min(additional, capacity.RunnerSeatsRemaining!.Value);
            if (scoped)
            {
                additional = Math.Min(additional, capacity.ProjectRemaining!.Value);
                additional = Math.Min(additional, capacity.StageRemaining!.Value);
            }
        }
        if (debt > 0) reasons.Add("startup_debt");
        return new(new(command, dispatch, commandState, dispatchState, commandReasons, dispatchReasons),
            new(additional, scoped ? "projectRole" : "hostOnly", missing, dispatch, debt, headroom,
                capacity.HostRemaining, capacity.RunnerSeatsRemaining, capacity.ProjectRemaining, capacity.StageRemaining,
                capacity.ObservedAt, reasons));
    }
    private static bool Valid(double? value) => value is { } number && double.IsFinite(number) && number >= 0 && number <= 100;
    private static bool Fresh(DateTimeOffset? at, DateTimeOffset now) => at is { } time
        && now - time <= TimeSpan.FromSeconds(15) && time - now <= TimeSpan.FromSeconds(1);
}
