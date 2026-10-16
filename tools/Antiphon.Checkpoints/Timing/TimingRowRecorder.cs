namespace Antiphon.Checkpoints;

public sealed class TimingOutcome
{
    public string Outcome { get; init; } = "unknown";
    public int? ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public bool Canceled { get; init; }
    public string? FailurePhase { get; init; }
    public string SetKind { get; init; } = "checkpoint-row";
    public string? SetId { get; init; }
    public TrxParseResult? Trx { get; init; }
    public TimingMeasurement? Measurement { get; init; }
    public double? BuildSeconds { get; init; }
    public bool CountsUnknown { get; init; }
    public string? Category { get; init; }
}

public sealed class TimingRowRecorder
{
    private readonly TimingJournal _journal;
    private readonly RowRequest _request;
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
    private bool _startWritten;

    private TimingRowRecorder(TimingJournal journal, RowRequest request)
    {
        _journal = journal;
        _request = request;
    }

    public static TimingRowRecorder? TryCreate(RowRequest request)
    {
        var root = request.TimingRoot;
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetEnvironmentVariable("ANTIPHON_TEST_TIMING_ROOT");
        if (string.IsNullOrWhiteSpace(root))
            return null;
        var journal = request.TimingJournal ?? TimingJournal.Open(root);
        return new TimingRowRecorder(journal, request);
    }

    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        if (_startWritten)
            return;
        _startWritten = true;
        await AppendAsync(new TimingEvent
        {
            EventId = EventId("run-started"),
            EventKind = "run-started",
            SetKind = "run",
            SetId = _request.TimingRunId ?? _request.Name,
            Outcome = "started",
            StartedAt = _started,
            SelectedCount = null,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task CompletedAsync(TimingOutcome outcome, CancellationToken cancellationToken)
    {
        var measurement = outcome.Measurement;
        var category = outcome.Category;
        TimingCategoryJoin? join = null;
        if (!string.IsNullOrWhiteSpace(_request.TimingDiscoveryJson) && !string.IsNullOrWhiteSpace(_request.TimingAssemblyHash))
        {
            join = TimingMeasurements.Join(TimingMeasurements.ParseDiscovery(_request.TimingDiscoveryJson), _request.TimingAssemblyHash);
            category = join.Accepted ? null : "unavailable";
        }
        else if (category is null && outcome.SetKind != "command")
            category = "unavailable";

        await AppendAsync(Completion(outcome, measurement, category), cancellationToken).ConfigureAwait(false);
        if (join is { Accepted: true })
        {
            foreach (var item in join.Cases)
            {
                foreach (var name in item.Categories.DefaultIfEmpty("unavailable"))
                {
                    await AppendAsync(Completion(outcome, measurement, name, "category", item.ExpandedId + "/" + name, item.ExpandedId), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        await AppendAsync(new TimingEvent
        {
            EventId = EventId("run-completed"),
            EventKind = "run-completed",
            SetKind = "run",
            SetId = _request.TimingRunId ?? _request.Name,
            Outcome = outcome.Outcome,
            ExitCode = outcome.ExitCode,
            TimedOut = outcome.TimedOut,
            StartedAt = _started,
            EndedAt = DateTimeOffset.UtcNow,
            ProducingBuildId = _request.ProducingBuildId,
            TimingPersistence = "complete",
        }, cancellationToken).ConfigureAwait(false);
    }

    private TimingEvent Completion(TimingOutcome outcome, TimingMeasurement? measurement, string? category,
        string? setKind = null, string? setId = null, string? membership = null) => new()
    {
        EventId = EventId((setKind ?? outcome.SetKind) + ":" + (setId ?? outcome.SetId ?? _request.Name)),
        EventKind = "set-completed",
        SetKind = setKind ?? outcome.SetKind,
        SetId = setId ?? outcome.SetId ?? _request.Name,
        Category = category,
        ClassName = outcome.Trx?.SlowClasses.FirstOrDefault()?.ClassName,
        MembershipDigest = membership,
        Outcome = outcome.Outcome,
        ExitCode = outcome.ExitCode,
        TimedOut = outcome.TimedOut,
        Canceled = outcome.Canceled,
        FailurePhase = outcome.FailurePhase,
        StartedAt = _started,
        EndedAt = DateTimeOffset.UtcNow,
        WallSeconds = measurement?.HostWallSeconds,
        CaseWindowSeconds = measurement?.CaseWindowSeconds,
        CaseDurationSumSeconds = measurement?.CaseDurationSumSeconds,
        StartupSeconds = measurement?.StartupSeconds,
        TeardownSeconds = measurement?.TeardownSeconds,
        MeasurementKind = measurement?.MeasurementKind,
        UnavailableReason = measurement?.UnavailableReason,
        BuildSeconds = outcome.BuildSeconds,
        SelectedCount = outcome.CountsUnknown ? null : outcome.Trx?.Executed,
        ObservedCount = outcome.CountsUnknown ? null : outcome.Trx?.Executed,
        PassedCount = outcome.CountsUnknown ? null : outcome.Trx?.Passed,
        FailedCount = outcome.CountsUnknown ? null : outcome.Trx?.Failed,
        SkippedCount = outcome.CountsUnknown ? null : outcome.Trx?.Skipped,
        UnknownCount = outcome.CountsUnknown ? null : 0,
        ProducingBuildId = _request.ProducingBuildId,
        Project = _request.Project,
        CandidateSha = _request.Commit,
        TimingPersistence = "complete",
    };

    private async Task AppendAsync(TimingEvent ev, CancellationToken cancellationToken)
    {
        ev.RunId = string.IsNullOrWhiteSpace(_request.TimingRunId) ? "row" : _request.TimingRunId;
        ev.Attempt = _request.TimingAttempt <= 0 ? 1 : _request.TimingAttempt;
        ev.Producer = string.IsNullOrWhiteSpace(_request.TimingProducer) ? "tool" : _request.TimingProducer;
        ev.HostId = Environment.MachineName;
        ev.Os = OperatingSystem.IsWindows() ? "windows" : "linux";
        ev.Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        ev.RuntimeVersion = Environment.Version.ToString();
        ev.DurabilityClass = "configured-root";
        await _journal.AppendAsync(ev, cancellationToken).ConfigureAwait(false);
    }

    private string EventId(string suffix)
    {
        var identity = (_request.TimingRunId ?? "row") + "|" + _request.TimingAttempt.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "|" + (_request.TimingProducer ?? "tool") + "|" + suffix;
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)))[..32];
    }
}
