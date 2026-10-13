using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Antiphon.Resilience;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration"), Category("Slow"), NotInParallel]
public class StaleWorkingTranscriptWatchdogTests
{
    // Frozen case -> mutation -> red bindings: T18..T35 / PC-41..83,101..110
    // in 2026-10-12-card-1205-test-design.md. Each method below checks observable
    // HTTP, durable rows, registered host, options rejection or structured logs.
    [Test, Arguments(120), Arguments(180)]
    public async Task C1205_Interval_boundary_controls_probe_and_log(int interval)
    {
        await using var h = await Harness.CreateAsync(interval: interval);
        await h.PassAndJoinAsync(); h.HttpGets.ShouldBe(1);
        h.Clock.Advance(TimeSpan.FromSeconds(119));
        await h.PassAndJoinAsync(); h.HttpGets.ShouldBe(1);
        h.Clock.Advance(TimeSpan.FromSeconds(interval - 120));
        await h.PassAndJoinAsync(); h.HttpGets.ShouldBe(1);
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        await h.PassAndJoinAsync(); h.HttpGets.ShouldBe(2);
        var due = h.Logs.Where(e => e.Message.Contains("probe due")).ToArray();
        var result = h.Logs.Where(e => e.Message.Contains("probe result")).ToArray();
        due.Length.ShouldBe(2); result.Length.ShouldBe(2);
        foreach (var e in due.Concat(result))
            foreach (var key in new[] { "SessionId", "Generation", "ServerEpoch", "Reason", "ElapsedAgeSeconds" })
                e.State.Keys.ShouldContain(key);
        foreach (var e in result)
            foreach (var key in new[] { "PreStoredSequence", "PostStoredSequence", "AddedCount", "DurationMs" })
                e.State.Keys.ShouldContain(key);
        h.Entry(h.Id).Success.ShouldBe(h.Clock.GetTimestamp());
    }

    [Test, Arguments(6), Arguments(-6)]
    public async Task C1205_Wall_clock_jumps_do_not_change_due_time(int hours)
    {
        await using var h = await Harness.CreateAsync();
        await h.PassAndJoinAsync(); h.HttpGets.ShouldBe(1);
        h.Clock.Offset = TimeSpan.FromHours(hours);
        await h.PassAndJoinAsync(); h.HttpGets.ShouldBe(1);
        h.Clock.Advance(TimeSpan.FromSeconds(120));
        await h.PassAndJoinAsync(); h.HttpGets.ShouldBe(2);
    }

    [Test, Arguments("keepalive"), Arguments("cache"), Arguments("queue"), Arguments("unrelated")]
    public async Task C1205_Healthy_keepalives_do_not_reset_probe_debt(string activity)
    {
        await using var h = await Harness.CreateAsync(count: 2);
        await h.PassAndJoinAsync(); h.HttpGets.ShouldBe(2);
        var stamp = h.Entry(h.Id).Success;
        h.Clock.Advance(TimeSpan.FromSeconds(60));
        switch (activity)
        {
            case "keepalive":
                await using (var frames = h.Client.StreamEventsAsync(default).GetAsyncEnumerator())
                {
                    (await frames.MoveNextAsync()).ShouldBeTrue();
                    frames.Current.SubscriptionReady.ShouldBeTrue();
                }
                h.Handler.Keepalives.ShouldBe(1); break;
            case "cache": (await h.Store.ReadAsync(h.Id, default)).Working.ShouldBeTrue(); break;
            case "queue": await h.Queue.FlushIfIdleAsync(h.Id, default); break;
            case "unrelated":
                await h.Runtime.PersistTranscriptAsync(h.Ids.Last(), [h.Event(h.Ids.Last(), 2, "AssistantText")]); break;
        }
        h.Entry(h.Id).Success.ShouldBe(stamp);
        h.Clock.Advance(TimeSpan.FromSeconds(60));
        await h.PassAndJoinAsync(); h.Handler.GetsFor(h.Id).ShouldBe(2);
    }

    [Test]
    public async Task C1205_Fair_pages_and_single_flight_bound_work()
    {
        await using var h = await Harness.CreateAsync(count: 5, page: 2, workers: 2);
        h.Handler.Held.Add(h.Id);
        await h.PassAsync(); h.AssertBaselineProbe(); await h.Handler.WaitForAsync(h.Id);
        await h.JoinExceptAsync(h.Id);
        h.Discovered.ShouldBeLessThanOrEqualTo(2);
        for (var pass = 0; pass < 4; pass++)
        {
            await h.PassAsync(); await h.JoinExceptAsync(h.Id);
            h.Discovered.ShouldBeLessThanOrEqualTo(2);
        }
        h.Handler.Order.ShouldContain(h.Ids.Last());
        h.Handler.GetsFor(h.Id).ShouldBe(1);
        h.Handler.Release.TrySetResult(); await h.JoinAsync();
        h.Handler.FailIds.Add(h.Id);
        h.Clock.Advance(TimeSpan.FromSeconds(120));
        for (var pass = 0; pass < 5; pass++) await h.PassAndJoinAsync();
        h.Handler.Order.Count(id => id == h.Ids.Last()).ShouldBeGreaterThan(1);
    }

    [Test]
    public async Task C1205_Concurrency_and_generation_are_bounded()
    {
        await using var h = await Harness.CreateAsync(count: 6, workers: 2);
        foreach (var id in h.Ids) h.Handler.Held.Add(id);
        await h.PassAsync(); h.AssertBaselineProbe(); await h.Handler.WaitForCountAsync(2);
        h.Active.Length.ShouldBe(2); h.Handler.Peak.ShouldBe(2);
        var old = h.Active.First();
        await h.ChangeGenerationAsync(old.Id);
        for (var pass = 0; pass < 3; pass++) await h.PassAsync();
        h.Active.Length.ShouldBe(2);
        h.Handler.GetsFor(old.Id).ShouldBe(1, "replacement must wait for owned old-generation work");
        h.Pending.Select(p => (p.Id, p.Generation)).Except(h.Entries.Select(e => (e.Id, e.Generation))).ShouldBeEmpty();
        h.Logs.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("capacity deferred"));
        h.Handler.Release.TrySetResult(); await h.JoinAsync();
        for (var pass = 0; pass < 4; pass++) await h.PassAndJoinAsync();
        h.Handler.GetsFor(old.Id).ShouldBe(2);
        h.Handler.Peak.ShouldBeLessThanOrEqualTo(2);
        h.Handler.PeakById.Values.ShouldAllBe(peak => peak == 1);
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task C1205_Capacity_overflow_remains_visible(bool churn)
    {
        await using var h = await Harness.CreateAsync(count: 1025);
        foreach (var id in h.Ids) h.Handler.Held.Add(id);
        for (var pass = 0; pass < 9; pass++) await h.PassAsync();
        h.AssertBaselineProbe();
        await h.Handler.WaitForCountAsync(4);
        h.Entries.Length.ShouldBe(1024);
        h.Pending.Length.ShouldBeLessThanOrEqualTo(1024);
        h.Logs.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("directory overflow")
            && Equals(e.State["SessionId"], h.Ids.Last()));
        if (churn)
        {
            var victim = h.Pending.First().Id;
            await h.ChangeGenerationAsync(victim);
            await h.SetStatusAsync(h.Pending.Skip(1).First().Id, SessionStatus.Stopped);
            for (var pass = 0; pass < 9; pass++) await h.PassAsync();
            h.Pending.Select(e => (e.Id, e.Generation)).Except(h.Entries.Select(e => (e.Id, e.Generation))).ShouldBeEmpty();
        }
        h.Handler.Held.Clear(); h.Handler.Release.TrySetResult(); await h.JoinAsync();
        // A confirmed terminal entry frees room for the overflow ID on a later page.
        await h.SetStatusAsync(h.Id, SessionStatus.Stopped);
        for (var pass = 0; pass < 18; pass++) await h.PassAndJoinAsync();
        h.Handler.Order.ShouldContain(h.Ids.Last());
        h.Entries.Length.ShouldBeLessThanOrEqualTo(1024);
    }

    [Test]
    public async Task C1205_Failed_probe_retains_debt_and_retries()
    {
        await using var h = await Harness.CreateAsync();
        h.Handler.FailIds.Add(h.Id);
        await h.PassAndJoinAsync(); h.HttpGets.ShouldBe(1);
        var initial = h.Entry(h.Id);
        initial.Success.ShouldBeNull(); initial.Attempt.ShouldNotBeNull();
        h.Flush.Accepted.ShouldBeEmpty();
        h.Logs.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("probe unknown"));
        h.Clock.Advance(TimeSpan.FromSeconds(14)); await h.PassAndJoinAsync(); h.HttpGets.ShouldBe(1);
        h.Handler.FailIds.Clear(); h.Idle(h.Id);
        h.Clock.Advance(TimeSpan.FromSeconds(1)); await h.PassAndJoinAsync(); h.HttpGets.ShouldBe(2);
        h.Flush.Accepted.ShouldBe(new[] { h.Id });
        (await h.WorkingAsync(h.Id)).ShouldBeFalse();
        h.Entry(h.Id).DiscoveredAt.ShouldBe(initial.DiscoveredAt);
        h.Entry(h.Id).Success.ShouldBe(h.Clock.GetTimestamp());
    }

    [Test]
    public async Task C1205_Overdue_operation_is_logged_and_joined()
    {
        await using var h = await Harness.CreateAsync();
        h.Idle(h.Id); h.Fault.HoldSave = true;
        await h.PassAsync(); h.AssertBaselineProbe(); await h.Fault.SaveReached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var pass = h.Passes;
        h.Clock.Advance(TimeSpan.FromSeconds(15)); await h.PassAsync();
        h.Passes.ShouldBe(pass + 1);
        h.HttpGets.ShouldBe(1);
        h.Logs.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("probe overdue"));
        var stop = h.StopAsync();
        try { stop.IsCompleted.ShouldBeFalse("owned non-cooperative save must be joined"); }
        finally { h.Fault.SaveRelease.TrySetResult(); await stop; }
        h.Active.ShouldBeEmpty();
    }

    [Test]
    public async Task C1205_Hosted_restart_discovers_working_sessions_immediately()
    {
        await using var h = await Harness.CreateAsync();
        Guid? epoch = null;
        for (var boot = 0; boot < 2; boot++)
        {
            await using var host = new WatchdogHost(h);
            var hosts = host.Services.GetServices<IHostedService>()
                .Where(s => s.GetType().Name == "StaleWorkingTranscriptHostedService").ToArray();
            hosts.Length.ShouldBe(1, "Program must register the independent transcript recovery host");
            await ((Task)Property(hosts[0], "FirstPass")!).WaitAsync(TimeSpan.FromSeconds(15));
            var recovery = host.Services.GetRequiredService(RecoveryType!);
            await CallAsync(recovery, "JoinCurrentProbesAsync");
            h.Handler.GetsFor(h.Id).ShouldBe(boot + 1, "cold epoch is due without advancing the interval");
            var current = (Guid)Property(Call(recovery, "Observe")!, "ServerEpoch")!;
            if (epoch is not null) current.ShouldNotBe(epoch.Value);
            epoch = current;
            await host.Services.GetRequiredService<IHost>().StopAsync();
        }
    }

    [Test, Arguments("feature"), Arguments("transport")]
    public async Task C1205_Disabled_setting_logs_the_missing_bound(string disabled)
    {
        await using var h = await Harness.CreateAsync();
        await using var host = new WatchdogHost(h, new Dictionary<string, string?>
        {
            ["StaleWorkingTranscript:Enabled"] = disabled == "feature" ? "false" : "true",
            ["SessionRunner:Enabled"] = disabled == "transport" ? "false" : "true"
        });
        _ = host.Services;
        var recoveryHost = host.Services.GetServices<IHostedService>()
            .SingleOrDefault(s => s.GetType().Name == "StaleWorkingTranscriptHostedService");
        if (recoveryHost is not null)
            await ((Task)Property(recoveryHost, "FirstPass")!).WaitAsync(TimeSpan.FromSeconds(15));
        h.HttpGets.ShouldBe(0);
        h.Logs.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("periodic recovery bound disabled")).ShouldBe(1);
    }

    [Test]
    [Arguments("IntervalSeconds", -1), Arguments("IntervalSeconds", 0), Arguments("IntervalSeconds", 29), Arguments("IntervalSeconds", 3601)]
    [Arguments("PollSeconds", 0), Arguments("PollSeconds", 31)]
    [Arguments("ProbeTimeoutSeconds", 0), Arguments("ProbeTimeoutSeconds", 31)]
    [Arguments("MaxConcurrentProbes", 0), Arguments("MaxConcurrentProbes", 5)]
    [Arguments("PageSize", 0), Arguments("PageSize", 129)]
    public async Task C1205_Invalid_settings_are_rejected(string setting, int value)
    {
        await using var h = await Harness.CreateAsync();
        await using var host = new WatchdogHost(h, new Dictionary<string, string?>
            { [$"StaleWorkingTranscript:{setting}"] = value.ToString() });
        var error = Should.Throw<OptionsValidationException>(() => _ = host.Services);
        error.Failures.ShouldContain(message => message.Contains(setting));
        h.HttpGets.ShouldBe(0);
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task C1205_Valid_settings_limits_are_accepted(bool maximum)
    {
        await using var h = await Harness.CreateAsync(count: 2);
        var values = new Dictionary<string, string?>
        {
            ["StaleWorkingTranscript:IntervalSeconds"] = maximum ? "3600" : "30",
            ["StaleWorkingTranscript:PollSeconds"] = maximum ? "30" : "1",
            ["StaleWorkingTranscript:ProbeTimeoutSeconds"] = maximum ? "30" : "1",
            ["StaleWorkingTranscript:MaxConcurrentProbes"] = maximum ? "4" : "1",
            ["StaleWorkingTranscript:PageSize"] = maximum ? "128" : "1"
        };
        await using var host = new WatchdogHost(h, values);
        var recoveryHost = host.Services.GetServices<IHostedService>()
            .SingleOrDefault(s => s.GetType().Name == "StaleWorkingTranscriptHostedService");
        if (recoveryHost is not null)
            await ((Task)Property(recoveryHost, "FirstPass")!).WaitAsync(TimeSpan.FromSeconds(15));
        if (RecoveryType is not null)
            await CallAsync(host.Services.GetRequiredService(RecoveryType), "JoinCurrentProbesAsync");
        h.HttpGets.ShouldBe(maximum ? 2 : 1);
        var options = host.Services.GetRequiredService(typeof(IOptions<>).MakeGenericType(SettingsType!));
        var settings = Property(options, "Value")!;
        foreach (var (key, value) in values)
            ((int)Property(settings, key.Split(':')[1])!).ShouldBe(int.Parse(value!));
    }

    [Test]
    [Arguments("no-note"), Arguments("remote"), Arguments("missing"), Arguments("stopped")]
    [Arguments("idle"), Arguments("cold"), Arguments("generation")]
    public async Task C1205_Discovery_requires_positive_local_working_evidence(string variant)
    {
        await using var h = await Harness.CreateAsync(count: 2);
        if (variant == "remote")
        {
            await using var db = h.Db();
            await db.AgentSessions.Where(s => s.Id == h.Id).ExecuteUpdateAsync(set => set
                .SetProperty(s => s.RunnerId, "foreign").SetProperty(s => s.RunnerStoreId, Guid.NewGuid()).SetProperty(s => s.RunnerCwd, "/foreign"));
        }
        if (variant == "missing") { await using var db = h.Db(); await db.AgentSessions.Where(s => s.Id == h.Id).ExecuteDeleteAsync(); }
        if (variant == "stopped") await h.SetStatusAsync(h.Id, SessionStatus.Stopped);
        if (variant == "idle") { await using var db = h.Db(); await db.TranscriptEntries.Where(t => t.AgentSessionId == h.Id).ExecuteDeleteAsync(); }
        await h.PassAndJoinAsync();
        h.Handler.GetsFor(h.Ids.Last()).ShouldBe(1, "cold local Working control requires no completion note");
        h.Handler.GetsFor(h.Id).ShouldBe(variant is "remote" or "missing" or "stopped" or "idle" ? 0 : 1);
        if (variant == "generation")
        {
            await h.ChangeGenerationAsync(h.Id);
            await h.PassAndJoinAsync();
            h.Handler.GetsFor(h.Id).ShouldBe(2, "generation change loses successful-check freshness");
        }
    }

    [Test]
    public async Task C1205_Discovery_failure_retains_visible_debt()
    {
        await using var h = await Harness.CreateAsync();
        await h.PassAndJoinAsync(); h.HttpGets.ShouldBe(1);
        var stamp = h.Entry(h.Id).Success;
        h.Clock.Advance(TimeSpan.FromSeconds(120));
        h.Fault.FailDiscovery = true; h.Handler.FailIds.Add(h.Id);
        await h.PassAndJoinAsync();
        h.Fault.DiscoveryFaults.ShouldBe(1);
        h.Entry(h.Id).Success.ShouldBe(stamp);
        h.Logs.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("discovery failed"));
        h.Handler.FailIds.Clear(); h.Clock.Advance(TimeSpan.FromSeconds(15));
        await h.PassAndJoinAsync(); h.HttpGets.ShouldBe(3);
        h.Entry(h.Id).Success.ShouldBe(h.Clock.GetTimestamp());
    }

    [Test, Arguments("headers"), Arguments("body"), Arguments("backoff")]
    public async Task C1205_Probe_budget_covers_headers_body_and_retries(string mode)
    {
        await using var h = await Harness.CreateAsync(resilient: true);
        h.Handler.Mode = mode;
        var timersBefore = h.Clock.Inner.Events.Count;
        await h.PassAsync(); h.AssertBaselineProbe(); await h.Handler.WaitForAsync(h.Id);
        h.HttpGets.ShouldBe(1);
        if (mode == "body") await h.Handler.BodyReached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        if (mode == "backoff")
            await h.Clock.Inner.WaitForTimerAsync(e => e.DueTime == TimeSpan.FromSeconds(30), timersBefore)
                .WaitAsync(TimeSpan.FromSeconds(15));
        h.Clock.Advance(TimeSpan.FromSeconds(10)); await h.JoinAsync();
        h.Active.ShouldBeEmpty(); h.Flush.Accepted.ShouldBeEmpty();
        h.Entry(h.Id).Success.ShouldBeNull(); h.HttpGets.ShouldBe(1);
        h.Logs.ShouldContain(e => e.Message.Contains("probe result") && Equals(e.State["Outcome"], "Unknown"));
        if (mode is "headers" or "body") h.Handler.Cancellations.ShouldBe(1);
    }

    [Test]
    public async Task C1205_Recovered_idle_schedules_only_coalesced_work()
    {
        await using var h = await Harness.CreateAsync(count: 2);
        var adapter = new FakeAgentProtocolAdapter();
        h.Runtime.Register(h.Id, adapter);
        var submitted = Signal(); var release = Signal();
        adapter.OnSubmitted = async body => { submitted.TrySetResult(); await release.Task; };
        await h.Queue.EnqueueAsync(h.Id, "C1205 deferred note", MessageSendMode.WhenIdle, default, deliverIfIdle: false);
        h.Idle(h.Id);
        using var workerStop = new CancellationTokenSource();
        var worker = new Antiphon.Server.Infrastructure.Orchestration.CompletionNoteWorkHostedService(
            h.Provider.GetRequiredService<IServiceScopeFactory>(), h.Flush, new SpecialistFailureQueue(),
            h.Clock, NullLogger<Antiphon.Server.Infrastructure.Orchestration.CompletionNoteWorkHostedService>.Instance);
        Task? flushWork = null;
        try
        {
            // Invoke the production flush loop alone; the recovery scanner is S5's owner.
            flushWork = (Task)typeof(Antiphon.Server.Infrastructure.Orchestration.CompletionNoteWorkHostedService)
                .GetMethod("FlushAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(worker, [workerStop.Token])!;
            await h.PassAndJoinAsync(); h.HttpGets.ShouldBe(2);
            await submitted.Task.WaitAsync(TimeSpan.FromSeconds(15));
            using (await h.Store.BeginWriteAsync(h.Id, default)) { }
            h.Flush.Accepted.ShouldContain(h.Id);
            // Publications while the actual input callback is held coalesce through the real queue.
            h.Flush.TryEnqueue(h.Id); h.Flush.TryEnqueue(h.Id);
            h.Flush.PendingCount.ShouldBe(1);
            h.Clock.Advance(TimeSpan.FromSeconds(120));
            await h.PassAndJoinAsync(); h.Handler.GetsFor(h.Ids.Last()).ShouldBe(2);
        }
        finally
        {
            workerStop.Cancel(); release.TrySetResult();
            if (flushWork is not null)
                try { await flushWork.WaitAsync(TimeSpan.FromSeconds(15)); } catch (OperationCanceledException) { }
            worker.Dispose();
        }
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task C1205_Ownership_cleanup_is_bounded_and_conservative(bool uncertain)
    {
        await using var h = await Harness.CreateAsync(count: 5, page: 2);
        for (var pass = 0; pass < 3; pass++) await h.PassAndJoinAsync();
        h.HttpGets.ShouldBe(5); h.Entries.Length.ShouldBe(5);
        foreach (var id in h.Ids) await h.SetStatusAsync(id, SessionStatus.Stopped);
        h.Fault.FailReconcile = uncertain;
        var before = h.Entries.Length;
        await h.PassAsync();
        h.Reconciled.ShouldBeLessThanOrEqualTo(2);
        if (uncertain)
        {
            h.Fault.ReconcileFaults.ShouldBe(1);
            h.Entries.Length.ShouldBe(before);
            h.Logs.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("reconciliation failed"));
        }
        else h.Entries.Length.ShouldBe(before - 2);
    }

    [Test, Arguments("working"), Arguments("unknown"), Arguments("idle")]
    public async Task C1205_Recovery_does_not_manufacture_idle_or_release(string outcome)
    {
        await using var h = await Harness.CreateAsync();
        if (outcome == "unknown") h.Handler.FailIds.Add(h.Id);
        if (outcome == "idle") h.Idle(h.Id);
        await h.PassAndJoinAsync(); h.HttpGets.ShouldBe(1);
        var rows = await h.RowsAsync(h.Id);
        rows.Count(r => r.Kind == "TurnEnd").ShouldBe(outcome == "idle" ? 1 : 0);
        rows.ShouldNotContain(r => r.Kind == "SessionRestartBoundary");
        h.Handler.Posts.ShouldBeEmpty();
        await using var db = h.Db();
        (await db.AgentSessions.SingleAsync(s => s.Id == h.Id)).Status.ShouldBe(SessionStatus.Running);
        h.Flush.Accepted.Count.ShouldBe(outcome == "idle" ? 1 : 0);
        (await h.WorkingAsync(h.Id)).ShouldBe(outcome != "idle");
    }

    private static Type? RecoveryType => typeof(AgentSessionRuntime).Assembly.GetType("Antiphon.Server.Application.Services.StaleWorkingTranscriptRecovery");
    private static Type? SettingsType => typeof(AgentSessionRuntime).Assembly.GetType("Antiphon.Server.Application.Settings.StaleWorkingTranscriptSettings");
    private static object? Property(object value, string name) => value.GetType().GetProperty(name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value);
    private static object? Call(object value, string name, params object[] args) => value.GetType().GetMethod(name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(value, args);
    private static Task CallAsync(object value, string name, params object[] args) => (Task)Call(value, name, args)!;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class WatchdogHost(Harness h, Dictionary<string, string?>? values = null) : AntiphonWebAppFactory
    {
        protected override string ConnectionString => h.Schema.ConnectionString;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            var configuration = new Dictionary<string, string?> { ["SessionRunner:Enabled"] = "true" };
            foreach (var pair in values ?? []) configuration[pair.Key] = pair.Value;
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));
        }
        protected override void ApplyTestOverrides(IServiceCollection services)
        {
            var unrelated = services.Where(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType?.Name != "StaleWorkingTranscriptHostedService").ToArray();
            foreach (var descriptor in unrelated) services.Remove(descriptor);
            services.RemoveAll<ISessionRunnerClient>(); services.AddSingleton<ISessionRunnerClient>(h.Client);
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(h.Clock);
            services.AddSingleton<ILoggerProvider>(h.LogProvider);
        }
    }

    private sealed record Entry(Guid Id, DateTime Generation, long? Success, long? Attempt, long DiscoveredAt);
    private sealed class Harness : IAsyncDisposable
    {
        public required IsolatedTestSchema Schema;
        public required ServiceProvider Provider;
        public required ProbeHandler Handler;
        public required HttpClient Http;
        public required SessionRunnerHttpClient Client;
        public required OffsetClock Clock;
        public required RecordingFlushes Flush;
        public required LogProvider LogProvider;
        public required Faults Fault;
        public object? Recovery;
        public readonly CancellationTokenSource Stop = new();
        public Guid[] Ids = [];
        public Guid Id => Ids[0];
        public int HttpGets => Handler.Order.Count;
        public RecordingLogEntry[] Logs => LogProvider.Events.ToArray();
        public SessionStateStore Store => Provider.GetRequiredService<SessionStateStore>();
        public AgentSessionRuntime Runtime => Provider.GetRequiredService<AgentSessionRuntime>();
        public SessionMessageQueueService Queue => Provider.GetRequiredService<SessionMessageQueueService>();
        public Entry Entry(Guid id) => Entries.Single(e => e.Id == id);
        public Entry[] Entries => ReadEntries("Directory");
        public Entry[] Pending => ReadEntries("Pending");
        public Entry[] Active => ReadEntries("Active");
        public int Passes => Recovery is null ? 0 : (int)Property(Call(Recovery, "Observe")!, "Passes")!;
        public int Discovered => Recovery is null ? 0 : (int)Property(Call(Recovery, "Observe")!, "DiscoveredThisPass")!;
        public int Reconciled => Recovery is null ? 0 : (int)Property(Call(Recovery, "Observe")!, "ReconciledThisPass")!;
        private Entry[] ReadEntries(string name) => Recovery is null ? [] : ((System.Collections.IEnumerable)Property(Call(Recovery, "Observe")!, name)!)
            .Cast<object>().Select(e => new Entry((Guid)Property(e, "SessionId")!, (DateTime)Property(e, "Generation")!,
                (long?)Property(e, "LastSuccess"), (long?)Property(e, "LastAttempt"), (long)Property(e, "DiscoveredAt")!)).ToArray();
        public AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Schema.ConnectionString)
            .AddInterceptors(Fault).Options);
        public static async Task<Harness> CreateAsync(int count = 1, int interval = 120, int page = 128, int workers = 4, bool resilient = false)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var clock = new OffsetClock(); var fault = new Faults(); var log = new LogProvider(); var flush = new RecordingFlushes();
            var handler = new ProbeHandler(); var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Resilience:AttemptTimeoutSeconds"] = "30" }).Build();
            var services = new ServiceCollection().AddLogging(b => b.AddProvider(log));
            services.AddSingleton<TimeProvider>(clock); services.AddSingleton<IResilienceJitter>(new FixedResilienceJitter(0.5));
            services.AddAntiphonResilience(config);
            services.AddHttpClient(ResilienceClientNames.RunnerRead).ConfigurePrimaryHttpMessageHandler(() => handler);
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString).AddInterceptors(fault));
            services.AddSingleton(Options.Create(new AgentSessionSettings()));
            services.AddSingleton(Options.Create(new SessionStateSettings()));
            services.AddSingleton<ISessionStateLoader, SessionStateLoader>(); services.AddSingleton<SessionStateStore>();
            services.AddSingleton<IEventBus, MockEventBus>(); services.AddSingleton<CompletionNoteFlushQueue>(flush);
            var http = new HttpClient(handler, false) { Timeout = Timeout.InfiniteTimeSpan };
            var runnerSettings = Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.invalid", RequestTimeoutSeconds = 120 });
            services.AddSingleton<ISessionRunnerClient>(sp => new SessionRunnerHttpClient(http,
                resilient ? sp.GetRequiredService<IHttpClientFactory>() : new PlainFactory(handler), runnerSettings, time: clock));
            services.AddSingleton<AgentSessionRuntime>(); services.AddSingleton<SessionMessageQueueService>();
            var provider = services.BuildServiceProvider();
            var h = new Harness { Schema = schema, Provider = provider, Handler = handler, Http = http, Client = (SessionRunnerHttpClient)provider.GetRequiredService<ISessionRunnerClient>(),
                Clock = clock, Flush = flush, Fault = fault, LogProvider = log };
            try
            {
                h.Ids = Enumerable.Range(1, count).Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:D12}")).ToArray();
                await using var db = h.Db();
                foreach (var id in h.Ids)
                {
                    var generation = clock.GetUtcNow().UtcDateTime;
                    db.AgentSessions.Add(new AgentSession { Id = id, DefinitionName = "test", Status = SessionStatus.Running,
                        Cwd = Path.GetTempPath(), StartedAt = generation, CreatedAt = generation, LastSeenAt = generation });
                    var prompt = h.Event(id, 1, "UserPrompt");
                    db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = id, Kind = prompt.Kind,
                        Sequence = 1, Uuid = prompt.Uuid, Timestamp = prompt.Timestamp!.Value.UtcDateTime, Text = prompt.Text, CreatedAt = generation });
                    handler.Snapshots[id] = new(id, [prompt], 1, AcceptedStartedAt: generation);
                }
                await db.SaveChangesAsync();
                if (RecoveryType is not null)
                {
                    var settings = Activator.CreateInstance(SettingsType!)!;
                    SettingsType!.GetProperty("IntervalSeconds")!.SetValue(settings, interval);
                    SettingsType.GetProperty("PageSize")!.SetValue(settings, page);
                    SettingsType.GetProperty("MaxConcurrentProbes")!.SetValue(settings, workers);
                    var options = typeof(Options).GetMethod("Create")!.MakeGenericMethod(SettingsType).Invoke(null, [settings])!;
                    h.Recovery = ActivatorUtilities.CreateInstance(provider, RecoveryType, options);
                }
                return h;
            }
            catch { await h.DisposeAsync(); throw; }
        }
        // Test-first only: pre-S4 owns cache warmup, which performs no transcript pull.
        // The final implementation removes this adapter; assertions use actual HTTP/DB outcomes.
        public async Task PassAsync()
        {
            if (Recovery is null) { await Store.WarmAsync(default); return; }
            await CallAsync(Recovery, "TickAsync", Stop.Token);
        }
        public async Task PassAndJoinAsync() { await PassAsync(); await JoinAsync(); }
        public void AssertBaselineProbe() { if (Recovery is null) HttpGets.ShouldBeGreaterThan(0, "independent evidence recovery must probe cold Working sessions"); }
        public Task JoinAsync() => Recovery is null ? Task.CompletedTask : CallAsync(Recovery, "JoinCurrentProbesAsync").WaitAsync(TimeSpan.FromSeconds(20));
        public Task JoinExceptAsync(Guid id) => Recovery is null ? Task.CompletedTask : CallAsync(Recovery, "JoinCurrentProbesExceptAsync", id).WaitAsync(TimeSpan.FromSeconds(20));
        public Task StopAsync() => Recovery is null ? Task.CompletedTask : CallAsync(Recovery, "StopAsync", CancellationToken.None);
        public SessionRunnerTranscriptEvent Event(Guid id, long seq, string kind) => new(id, seq, kind, $"{id:N}-{seq}-{kind}", null,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(seq), "assistant", kind == "UserPrompt" ? "working" : "done", null, null, null, null, null);
        public void Idle(Guid id)
        {
            var snapshot = Handler.Snapshots[id];
            Handler.Snapshots[id] = snapshot with { Entries = [snapshot.Entries[0], Event(id, 2, "TurnEnd")], LastSequence = 2 };
        }
        public async Task ChangeGenerationAsync(Guid id)
        {
            await using var db = Db(); var row = await db.AgentSessions.SingleAsync(s => s.Id == id);
            row.StartedAt = row.StartedAt.AddSeconds(1); await db.SaveChangesAsync();
            Handler.Snapshots[id] = Handler.Snapshots[id] with { AcceptedStartedAt = row.StartedAt };
        }
        public async Task SetStatusAsync(Guid id, SessionStatus status)
        { await using var db = Db(); await db.AgentSessions.Where(s => s.Id == id).ExecuteUpdateAsync(set => set.SetProperty(s => s.Status, status)); }
        public async Task<bool> WorkingAsync(Guid id)
        { await using var db = Db(); return await SessionMessageQueueService.IsWorkingAsync(db, id, default); }
        public async Task<List<TranscriptEntry>> RowsAsync(Guid id)
        { await using var db = Db(); return await db.TranscriptEntries.Where(t => t.AgentSessionId == id).OrderBy(t => t.Sequence).ToListAsync(); }
        public async ValueTask DisposeAsync()
        {
            Stop.Cancel(); Handler.Release.TrySetResult(); Fault.SaveRelease.TrySetResult();
            await StopAsync(); Stop.Dispose(); Http.Dispose(); await Provider.DisposeAsync(); await Schema.DisposeAsync();
        }
    }

    private sealed class OffsetClock : TimeProvider
    {
        public ControlledTimeProvider Inner { get; } = new();
        public TimeSpan Offset;
        public override DateTimeOffset GetUtcNow() => Inner.GetUtcNow() + Offset;
        public override long GetTimestamp() => Inner.GetTimestamp();
        public override long TimestampFrequency => Inner.TimestampFrequency;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => Inner.CreateTimer(callback, state, dueTime, period);
        public void Advance(TimeSpan time) => Inner.Advance(time);
    }
    private sealed class PlainFactory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, false) { Timeout = Timeout.InfiniteTimeSpan }; }
    private sealed class RecordingFlushes : CompletionNoteFlushQueue
    {
        public ConcurrentQueue<Guid> Accepted { get; } = new();
        public int PendingCount => ((System.Collections.IDictionary)typeof(CompletionNoteFlushQueue).GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this)!).Count;
        public override bool TryEnqueue(Guid id) { Accepted.Enqueue(id); return base.TryEnqueue(id); }
    }
    private sealed class LogProvider : ILoggerProvider
    {
        public ConcurrentQueue<RecordingLogEntry> Events { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Events);
        public void Dispose() { }
        private sealed class CapturingLogger(ConcurrentQueue<RecordingLogEntry> events) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter) =>
                events.Enqueue(new(level, formatter(state, ex), ex, state is IEnumerable<KeyValuePair<string, object?>> pairs ? pairs.ToDictionary(p => p.Key, p => p.Value) : new Dictionary<string, object?>()));
        }
    }
    private sealed class Faults : DbCommandInterceptor, ISaveChangesInterceptor
    {
        public bool FailDiscovery, FailReconcile, HoldSave;
        public int DiscoveryFaults, ReconcileFaults;
        public TaskCompletionSource SaveReached { get; } = Signal();
        public TaskCompletionSource SaveRelease { get; } = Signal();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (FailDiscovery && command.CommandText.Contains("stale-working.discovery"))
            { FailDiscovery = false; DiscoveryFaults++; throw new IOException("planned discovery failure"); }
            if (FailReconcile && command.CommandText.Contains("stale-working.reconcile"))
            { FailReconcile = false; ReconcileFaults++; throw new IOException("planned reconciliation failure"); }
            return ValueTask.FromResult(result);
        }
        public async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (HoldSave && data.Context!.ChangeTracker.Entries<TranscriptEntry>().Any(e => e.State == EntityState.Added && e.Entity.Kind == "TurnEnd"))
            { SaveReached.TrySetResult(); await SaveRelease.Task; }
            return result;
        }
    }
    private sealed class ProbeHandler : HttpMessageHandler
    {
        public ConcurrentDictionary<Guid, SessionRunnerTranscriptDto> Snapshots { get; } = new();
        public ConcurrentQueue<Guid> Order { get; } = new();
        public ConcurrentQueue<string> Posts { get; } = new();
        public HashSet<Guid> Held { get; } = [];
        public HashSet<Guid> FailIds { get; } = [];
        public Dictionary<Guid, int> PeakById { get; } = [];
        private readonly Dictionary<Guid, int> _activeById = [];
        private readonly object _gate = new();
        private int _active;
        private TaskCompletionSource _changed = Signal();
        public int Peak, Keepalives, Cancellations;
        public string Mode = "normal";
        public TaskCompletionSource Release { get; } = Signal();
        public TaskCompletionSource BodyReached { get; } = Signal();
        public int GetsFor(Guid id) => Order.Count(value => value == id);
        public async Task WaitForAsync(Guid id)
        {
            while (true)
            { Task changed; lock (_gate) { if (Order.Contains(id)) return; changed = _changed.Task; } await changed.WaitAsync(TimeSpan.FromSeconds(15)); }
        }
        public async Task WaitForCountAsync(int count)
        {
            while (true)
            { Task changed; lock (_gate) { if (Order.Count >= count) return; changed = _changed.Task; } await changed.WaitAsync(TimeSpan.FromSeconds(15)); }
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/events")
            { Keepalives++; return new(HttpStatusCode.OK) { Content = new StringContent(": keepalive\n\n", Encoding.UTF8, "text/event-stream") }; }
            if (request.Method != HttpMethod.Get) { Posts.Enqueue(request.RequestUri.AbsolutePath); return new(HttpStatusCode.OK) { Content = new StringContent("{}") }; }
            var id = Guid.Parse(request.RequestUri.AbsolutePath.Split('/')[2]);
            var snapshot = Snapshots[id];
            lock (_gate)
            {
                Order.Enqueue(id); _active++; Peak = Math.Max(Peak, _active);
                _activeById[id] = _activeById.GetValueOrDefault(id) + 1;
                PeakById[id] = Math.Max(PeakById.GetValueOrDefault(id), _activeById[id]);
                _changed.TrySetResult(); _changed = Signal();
            }
            try
            {
                if (Mode == "headers" || Held.Contains(id)) await Release.Task.WaitAsync(ct);
                if (FailIds.Contains(id)) throw new HttpRequestException("planned probe failure");
                if (Mode == "backoff")
                { var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable); response.Headers.RetryAfter = new(TimeSpan.FromSeconds(30)); return response; }
                if (Mode == "body") return new(HttpStatusCode.OK) { Content = new HeldBody(this) };
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") };
            }
            catch (OperationCanceledException) { Interlocked.Increment(ref Cancellations); throw; }
            finally { lock (_gate) { _active--; _activeById[id]--; } }
        }
        private sealed class HeldBody(ProbeHandler owner) : HttpContent
        {
            protected override bool TryComputeLength(out long length) { length = 0; return false; }
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, default);
            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
            {
                owner.BodyReached.TrySetResult();
                try { await owner.Release.Task.WaitAsync(ct); }
                catch (OperationCanceledException) { Interlocked.Increment(ref owner.Cancellations); throw; }
            }
        }
    }
}
