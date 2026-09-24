using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Records queue, fence and capacity observations. S2 does not deliver a prompt or page anyone.
/// </summary>
public sealed class ExpectationWatchdogService
{
    private readonly AppDbContext _db;
    private readonly ExpectationSnapshotReader _reader;
    private readonly ExpectationLedger _ledger;
    private readonly TimeProvider _time;
    private readonly IExpectationCatchUp _catchUp;

    public ExpectationWatchdogService(
        AppDbContext db,
        ExpectationLedger ledger,
        TimeProvider time,
        DelegationSettings? delegation = null,
        IExpectationCatchUp? catchUp = null)
    {
        _db = db;
        _reader = new ExpectationSnapshotReader(db, delegation);
        _ledger = ledger;
        _time = time;
        _catchUp = catchUp ?? NoExpectationCatchUp.Instance;
    }

    public async Task<ExpectationScanResult> ScanAsync(
        ExpectationDirectiveSettings directive,
        ExpectationProbeInput probes,
        CancellationToken ct)
    {
        var asOf = DateTime.SpecifyKind(_time.GetUtcNow().UtcDateTime, DateTimeKind.Utc);
        var digest = ExpectationDirectiveDigest.Compute(directive);
        var snapshot = await _reader.ReadAsync(directive, digest, asOf, probes, ct);
        var evaluation = ExpectationWatchdogPolicy.Evaluate(snapshot, directive);
        if (snapshot.ProbeUnknown || evaluation.ObservationUnknown || !snapshot.DirectiveActive)
        {
            await _ledger.RecordObservationAsync(
                directive.Id,
                digest,
                asOf,
                successful: false,
                snapshot.ProbeError ?? evaluation.ProbeError ?? "observation preserved",
                ct);
            _db.ChangeTracker.Clear();
            return new ExpectationScanResult(evaluation, 0);
        }

        if (snapshot.ConfigChanged)
        {
            await _ledger.ResolveDigestMismatchesAsync(directive.Id, digest, asOf, ct);
            _db.ChangeTracker.Clear();
        }

        foreach (var condition in evaluation.StalledPipelines.Where(condition => condition.IsDue))
            await OpenAsync(directive.Id, digest, condition, asOf, ct);
        if (evaluation.DispatchFence is { IsDue: true } fence)
            await OpenAsync(directive.Id, digest, fence, asOf, ct);
        foreach (var condition in evaluation.ScopedFences.Where(condition => condition.IsDue))
            await OpenAsync(directive.Id, digest, condition, asOf, ct);
        foreach (var lane in evaluation.Capacity.Where(lane => lane.InDeficit))
        {
            await _ledger.OpenEpisodeAsync(
                new ExpectationEpisodeOpen(
                    directive.Id,
                    digest,
                    ExpectationEpisodeKind.CapacityDeficit,
                    lane.SubjectKey,
                    string.IsNullOrWhiteSpace(lane.Evidence) ? "capacity deficit" : lane.Evidence,
                    asOf,
                    asOf,
                    null),
                ct);
            _db.ChangeTracker.Clear();
        }

        await _ledger.RecordObservationAsync(directive.Id, digest, asOf, successful: true, error: null, ct);
        _db.ChangeTracker.Clear();
        return new ExpectationScanResult(evaluation, 0);
    }

    private async Task OpenAsync(
        string directiveId,
        string digest,
        ExpectationCondition condition,
        DateTime asOf,
        CancellationToken ct)
    {
        await _ledger.OpenEpisodeAsync(
            new ExpectationEpisodeOpen(
                directiveId,
                digest,
                condition.Kind,
                condition.SubjectKey,
                condition.Evidence,
                asOf,
                asOf,
                null),
            ct);
        _db.ChangeTracker.Clear();
    }
}
