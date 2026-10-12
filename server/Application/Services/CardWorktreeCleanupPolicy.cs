using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1033 D-2. CardDone destruction is fail-closed: Execute, a non-preview run,
/// and a Done revision strictly after the configured UTC cutoff. Equality stays historical.
/// </summary>
public static class CardWorktreeCleanupPolicy
{
    public static string? DestructiveAdmissionRefusal(WorktreeResidueSettings settings, DateTime doneAtUtc, bool preview)
    {
        if (preview) return "preview";
        if (!settings.Execute) return "execute_disabled";
        if (!WorktreeResidueSettings.TryReadCardDoneAfterUtc(settings.CardDoneAfterUtc, out var cutoff, out _))
            return "cutoff_invalid";
        if (cutoff is null) return "cutoff_absent";
        if (InstantTicks(doneAtUtc) <= cutoff.Value.Ticks) return "historical_cohort";
        return null;
    }

    public static string? SettlingRefusal(DateTime? completedAtUtc, DateTime nowUtc, int minSettledMinutes)
    {
        if (completedAtUtc is null) return "settling";
        var age = InstantTicks(nowUtc) - InstantTicks(completedAtUtc.Value);
        if (age < TimeSpan.FromMinutes(minSettledMinutes).Ticks) return "settling";
        return null;
    }

    /// <summary>Npgsql timestamp values are UTC instants even when Kind is Unspecified. Do not shift them.</summary>
    private static long InstantTicks(DateTime value) =>
        (value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc)).Ticks;
}
