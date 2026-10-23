using System.Diagnostics;
using System.Text.Json;
using Antiphon.Observability;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Observability.Tests;

[Category("Unit")]
public sealed class LiveTimingTests
{
    [Test]
    public void V1_Startup_records_monotonic_duration_and_correlated_span()
    {
        var log = new CaptureLogger();
        using var timing = new LiveTiming(log);
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == LiveTiming.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { lock (stopped) stopped.Add(a); }
        };
        ActivitySource.AddActivityListener(listener);
        using var parent = new Activity("request").Start();
        using (var scope = timing.BeginStartup(StartupOperation.Migrate))
        {
            Thread.SpinWait(10_000);
            scope.Complete();
        }
        var snapshot = timing.ReadStartup();
        snapshot.RunId.ShouldNotBeNullOrWhiteSpace();
        var row = snapshot.Phases.Single();
        row.Phase.ShouldBe("db-migrate");
        row.Operation.ShouldBe("migrate");
        row.Outcome.ShouldBe("completed");
        row.ElapsedMs.ShouldBeGreaterThan(0);
        Activity span;
        lock (stopped) span = stopped.Single(a => a.GetTagItem("run.id")?.ToString() == snapshot.RunId);
        span.ParentSpanId.ShouldBe(parent.SpanId);
        span.GetTagItem("phase").ShouldBe("db-migrate");
        span.Status.ShouldBe(ActivityStatusCode.Ok);
        log.Rows.Single().Text.ShouldContain("completed");
    }

    [Test]
    public void V2_Incomplete_scope_is_failed_and_disposal_is_idempotent()
    {
        var log = new CaptureLogger();
        using var timing = new LiveTiming(log);
        var scope = timing.BeginStartup(StartupOperation.Seed);
        scope.Dispose();
        scope.Complete();
        scope.Dispose();
        timing.ReadStartup().Phases.Single().Outcome.ShouldBe("failed-or-canceled");
        log.Rows.Count.ShouldBe(1);
    }

    [Test]
    public void V3_External_calls_warn_only_over_threshold_and_do_not_fill_startup_history()
    {
        var log = new CaptureLogger();
        using var timing = new LiveTiming(log);
        timing.RecordExternal(ExternalOperation.Database, TimeSpan.FromMilliseconds(999), true);
        timing.RecordExternal(ExternalOperation.Git, TimeSpan.FromMilliseconds(1001), false);
        timing.RecordExternal(ExternalOperation.Http, TimeSpan.FromMilliseconds(1100), true);
        timing.RecordExternal(ExternalOperation.Filesystem, TimeSpan.FromMilliseconds(1200), true);
        log.Rows.Count.ShouldBe(3);
        log.Rows.All(x => x.Level == LogLevel.Warning).ShouldBeTrue();
        log.Rows[0].Text.ShouldContain("git");
        log.Rows[0].Text.ShouldContain("failed-or-canceled");
        timing.ReadStartup().Phases.ShouldBeEmpty();
    }

    [Test]
    public void V4_Sink_failure_does_not_replace_application_outcome_and_snapshot_is_bounded()
    {
        using var timing = new LiveTiming(new CaptureLogger { Throw = true });
        for (var i = 0; i < 100; i++)
            using (var phase = timing.BeginStartup(StartupOperation.Setup)) phase.Complete();
        timing.ReadStartup().Phases.Count.ShouldBe(64);
        JsonSerializer.Serialize(timing.ReadStartup()).ShouldNotContain("secret-canary");
    }
}

internal sealed class CaptureLogger : ILogger<LiveTiming>
{
    public List<(LogLevel Level, string Text)> Rows { get; } = [];
    public bool Throw { get; init; }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (Throw) throw new IOException("secret-canary");
        Rows.Add((level, formatter(state, exception)));
    }
}
