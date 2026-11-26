using System.Globalization;
using System.Text.Json;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public static partial class RunnerSlotService
{
    internal const string CustodyHostStillPresent = "custody_host_still_present";
    private const string CustodyReleaseMarker = "custody-release:";
    private const string PendingCustodyReleasePrefix = PendingPrefix + CustodyReleaseMarker;
    private static readonly TimeSpan CustodyReleaseRetryInterval = TimeSpan.FromMinutes(2);

    internal static Task<Guid> RecordCustodyReleaseAsync(AppDbContext db, string? runnerId,
        VerificationExecutionBinding binding, DateTime now, CancellationToken ct)
    {
        var runner = RunnerRequestIntent.CanonicalRunnerId(runnerId) ?? PhoneHomeRunnerDirectory.LocalRunnerId;
        var generation = binding.Generation;
        var retry = new CustodyReleaseRetry(runner, generation.AcceptedStartedAt, binding.ExecutionId,
            now + CustodyReleaseRetryInterval);
        var message = FormattableString.Invariant(
            $"Linux custody release on runner '{runner}' for generation {generation.AcceptedStartedAt:O} was withheld: {CustodyHostStillPresent}");
        var intent = NewIntent(generation.SessionId, message, retry.Marker);
        intent.CreatedAt = now;
        // The identity stays in the terminal marker so a late copy of a refusal cannot reopen it.
        return RecordPendingOnceAsync(db, intent, ct, retry.Identity);
    }

    private static async Task<bool> RetryCustodyReleaseAsync(PhoneHomeRunnerDirectory directory, AppDbContext db,
        Guid intentId, Guid sessionId, TimeProvider clock, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var current = await LockIntentAsync(db, intentId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (current is null || current.Kind != AgentIncidentKind.RunnerSlotReleaseIntent || current.SessionId != sessionId
            || CustodyReleaseRetry.Parse(current.FailureReason) is not { } retry || retry.NotBefore > now)
            return false;

        var execution = await db.VerificationExecutions.AsNoTracking().SingleOrDefaultAsync(e =>
            e.Id == retry.ExecutionId && e.SessionId == sessionId && e.AcceptedStartedAt == retry.Generation, ct);
        var binding = execution is null ? null : JsonSerializer.Deserialize<VerificationExecutionBinding>(execution.BindingJson);
        var finished = false;
        if (execution is not null && binding is { Backend: VerificationCustodyBackends.LinuxCgroup, CustodyContractVersion: 1 }
            && binding.RunnerStoreId != Guid.Empty
            && binding.ExecutionId == retry.ExecutionId && binding.Source.TaskId == execution.TaskId
            && binding.Source.SourceOperationId == execution.SourceLandingOperationId
            && binding.Generation == new VerificationSessionGeneration(sessionId, retry.Generation))
        {
            try
            {
                var status = await directory.Resolve(retry.RunnerId).RecoverDeadVerificationCustodyAsync(binding, ct);
                finished = CustodyReleaseConfirmed(binding, status);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Unavailable runners and missing or invalid answers still owe the release.
            }
        }

        // Keep the row locked through the command and write. Reconnects, startup and failed
        // operator requests can all trigger extra passes between the scheduled sweeps.
        var marker = finished
            ? ReconciledMarker + ":" + retry.Identity + retry.ExecutionId.ToString("D")
            : (retry with { NotBefore = now + CustodyReleaseRetryInterval }).Marker;
        await MarkAsync(db, intentId, marker, ct);
        await transaction.CommitAsync(ct);
        return finished;
    }

    private static bool CustodyReleaseConfirmed(VerificationExecutionBinding binding, VerificationCustodyStatus status)
    {
        if (status.Binding != binding || status.Host is not { } host || host.RunnerStoreId != binding.RunnerStoreId)
            return false;
        if (status.Receipt is null)
            return status.State == VerificationCustodyState.Unknown && status.Reason == "released_unknown";
        return status.State == VerificationCustodyState.Exited
            && new VerificationReceiptPolicy().Validate(status.Receipt, binding, host).Disposition == status.State;
    }

    private sealed record CustodyReleaseRetry(string RunnerId, DateTime Generation, Guid ExecutionId, DateTime NotBefore)
    {
        public string Identity => FormattableString.Invariant($"{CustodyReleaseMarker}{RunnerId}:{Generation.Ticks}/");
        public string Marker => FormattableString.Invariant($"{PendingPrefix}{Identity}{ExecutionId:D}/{NotBefore.Ticks}");

        public static CustodyReleaseRetry? Parse(string? marker)
        {
            if (marker?.StartsWith(PendingCustodyReleasePrefix, StringComparison.Ordinal) != true)
                return null;
            var details = marker.LastIndexOf(':');
            if (details <= PendingCustodyReleasePrefix.Length)
                return null;
            var fields = marker[(details + 1)..].Split('/');
            return fields.Length == 3 && TryParseUtc(fields[0], out var generation)
                && Guid.TryParseExact(fields[1], "D", out var executionId) && TryParseUtc(fields[2], out var notBefore)
                ? new(marker[PendingCustodyReleasePrefix.Length..details], generation, executionId, notBefore)
                : null;
        }

        private static bool TryParseUtc(string text, out DateTime value)
        {
            value = default;
            if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks > DateTime.MaxValue.Ticks)
                return false;
            value = new DateTime(ticks, DateTimeKind.Utc);
            return true;
        }
    }
}
