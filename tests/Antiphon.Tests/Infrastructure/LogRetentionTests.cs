using Antiphon.Server.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-0043 — "log retention is 14 hours, not 14 days".
///
/// Two halves, and the tests below pin both because either one alone is useless:
///
/// 1. <b>Volume.</b> Measured over the whole retained window on 2026-08-13 (14 files,
///    <c>server/logs/antiphon-*.log</c>, 2026-08-12 01:04 -> 2026-08-13 07:48 = 30.74 h,
///    1 316 776 105 bytes), the server wrote <b>40.9 MB/hour</b> and <b>99.92%</b> of it was two
///    sources at Information: <c>Microsoft.EntityFrameworkCore.Database.Command</c> (73.8%, full SQL
///    per query) and <c>System.Net.Http.HttpClient.ISessionRunnerClient.*</c> (26.2%, four lines per
///    session-runner poll). Everything else together was 0.97 MB — 0.032 MB/hour.
///
/// 2. <b>Retention.</b> <c>retainedFileCountLimit</c> counts FILES. At 40.9 MB/hour a 100 MB file
///    filled in 2.4 hours on average and 21 minutes under load, so "14" was 5-45 HOURS of history.
///    Time-based retention states the intent, but what actually DELIVERS 5 days is the volume cut:
///    5 days only fits on disk while the rate stays under
///    <see cref="FileLogRetentionPolicy.MaxMegabytesPerHourFor"/>.
///
/// The levels live in configuration, not code, so a debugging session can raise a source back to
/// Information without a rebuild — and <see cref="Turning_a_source_down_is_configuration_not_code"/>
/// keeps it that way.
/// </summary>
public class LogRetentionTests
{
    /// <summary>Whole-window rate before the change: 1 316 776 105 B over 30.7381 h.</summary>
    private const double MeasuredPreChangeMbPerHour = 40.85;

    /// <summary>Same window, counting only what survives the two overrides: 1 021 252 B over 30.7381 h.</summary>
    private const double MeasuredPostChangeMbPerHour = 0.032;

    /// <summary>
    /// Busiest measured 47 minutes (antiphon-20260813_002.log, 128.9 MB/hour before). The average is
    /// not the number retention has to survive — this is.
    /// </summary>
    private const double MeasuredPostChangePeakMbPerHour = 0.92;

    private const string EfCommandSource = "Microsoft.EntityFrameworkCore.Database.Command";
    private const string HttpClientSource = "System.Net.Http.HttpClient.ISessionRunnerClient.LogicalHandler";

    // ── The policy ───────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Default_policy_retains_five_days_and_caps_the_disk_at_the_old_budget()
    {
        var policy = FileLogRetentionPolicy.Default;

        policy.RetainedFileTimeLimit.ShouldBe(TimeSpan.FromDays(5));
        policy.FileSizeLimitBytes.ShouldBe(100L * 1024 * 1024);
        // The count cap is a DISK backstop, not the retention rule, and it is deliberately the same
        // 1.4 GB the count-only policy bounded us to — the disk budget does not grow.
        policy.RetainedFileCountLimit.ShouldBe(14);
        policy.MaxDiskBytes.ShouldBe(1400L * 1024 * 1024);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Five_days_fits_the_disk_budget_at_the_measured_post_change_rate()
    {
        var policy = FileLogRetentionPolicy.Default;

        policy.FitsDaysAtRate(5, MeasuredPostChangeMbPerHour).ShouldBeTrue();
        // ...and at the busiest window measured, not just the average.
        policy.FitsDaysAtRate(5, MeasuredPostChangePeakMbPerHour).ShouldBeTrue();

        // 1400 MB / (24 x 5) — the sustained rate at which the count cap starts evicting inside the
        // 5-day window again. 12x headroom over the measured peak; 365x over the average.
        policy.MaxMegabytesPerHourFor(5).ShouldBe(11.666, tolerance: 0.01);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Five_days_did_NOT_fit_at_the_measured_pre_change_rate()
    {
        // The half of the card that retention alone could not solve: at 40.9 MB/hour, 5 days is
        // 4.8 GB. Raising retainedFileTimeLimit without cutting volume would have changed nothing —
        // the count cap would still have evicted everything past ~34 hours.
        FileLogRetentionPolicy.Default.FitsDaysAtRate(5, MeasuredPreChangeMbPerHour).ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Retention_values_are_configurable_so_a_re_armed_source_can_buy_more_disk()
    {
        var policy = FileLogRetentionPolicy.FromConfiguration(Config(new()
        {
            ["Serilog:FileSizeLimitMb"] = "250",
            ["Serilog:RetainedFileCountLimit"] = "40",
            ["Serilog:RetainedFileTimeLimitDays"] = "7",
        }));

        policy.FileSizeLimitBytes.ShouldBe(250L * 1024 * 1024);
        policy.RetainedFileCountLimit.ShouldBe(40);
        policy.RetainedFileTimeLimit.ShouldBe(TimeSpan.FromDays(7));
        // 10 GB of backstop: enough to keep 5 days with EF command logging re-armed.
        policy.FitsDaysAtRate(5, MeasuredPreChangeMbPerHour).ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("")]
    [Arguments("0")]
    [Arguments("-3")]
    [Arguments("lots")]
    public async Task A_bad_retention_value_falls_back_to_the_default_rather_than_unbounding_the_log(
        string value)
    {
        var policy = FileLogRetentionPolicy.FromConfiguration(Config(new()
        {
            ["Serilog:FileSizeLimitMb"] = value,
            ["Serilog:RetainedFileCountLimit"] = value,
            ["Serilog:RetainedFileTimeLimitDays"] = value,
        }));

        policy.ShouldBe(FileLogRetentionPolicy.Default);
        await Task.CompletedTask;
    }

    // ── The shipped configuration ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Shipped_config_drops_the_two_measured_noise_sources_and_keeps_our_own()
    {
        var events = new List<LogEvent>();
        using var log = new LoggerConfiguration()
            .ReadFrom.Configuration(ShippedServerConfiguration())
            .WriteTo.Sink(new CollectingSink(events))
            .CreateLogger();

        log.ForContext(Constants.SourceContextPropertyName, EfCommandSource)
            .Information("Executed DbCommand");
        log.ForContext(Constants.SourceContextPropertyName, HttpClientSource)
            .Information("Start processing HTTP request");

        // 99.92% of the volume, gone — but only at Information. A failing query still lands.
        events.ShouldBeEmpty();

        log.ForContext(Constants.SourceContextPropertyName, EfCommandSource)
            .Warning("Command failed");
        events.Count.ShouldBe(1);
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("Antiphon.Server.Application.Services.DelegationService")]
    [Arguments("Antiphon.Server.Application.Services.SessionMessageQueueService")]
    [Arguments("Antiphon.Server.Application.Services.AgentSessionService")]
    [Arguments("Antiphon.Server.Infrastructure.Supervision.AlertDigestFlushHostedService")]
    [Arguments("Antiphon.Server.Infrastructure.Orchestration.OrchestratorHostedService")]
    [Arguments("Antiphon.Server.Infrastructure.Agents.SessionRunner.SessionRunnerEventPump")]
    public async Task Shipped_config_keeps_delegation_session_and_supervision_at_Information(
        string source)
    {
        // The hard constraint on this card: the silent-failure class this project keeps hitting is
        // diagnosed from these sources after the fact. The "Antiphon" override pins them at
        // Information explicitly, so a future blanket turn-down of Default cannot take them with it.
        var events = new List<LogEvent>();
        using var log = new LoggerConfiguration()
            .ReadFrom.Configuration(ShippedServerConfiguration())
            .WriteTo.Sink(new CollectingSink(events))
            .CreateLogger();

        log.ForContext(Constants.SourceContextPropertyName, source).Information("still here");

        events.Count.ShouldBe(1, $"{source} must stay at Information");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Turning_a_source_down_is_configuration_not_code()
    {
        // Re-arming EF command logging for one debugging session must be a config edit (or
        // Serilog__MinimumLevel__Override__... in the environment), never a rebuild.
        var program = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "server", "Program.cs"));

        program.ShouldNotContain(
            "MinimumLevel.Override(",
            customMessage: "Source levels must come from Serilog:MinimumLevel:Override in configuration, "
                         + "not from code - a hardcoded override cannot be re-armed without a rebuild.");
        program.ShouldContain("ReadFrom.Configuration(ctx.Configuration)");
    }

    [Test]
    public async Task Server_file_sink_retains_by_time_with_the_count_cap_as_backstop()
    {
        var program = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "server", "Program.cs"));

        program.ShouldContain(
            "retainedFileTimeLimit: retention.RetainedFileTimeLimit",
            customMessage: "Retention must be time-based; retainedFileCountLimit counts files, not days.");
        program.ShouldContain("retainedFileCountLimit: retention.RetainedFileCountLimit");
    }

    [Test]
    public async Task Session_runner_file_sink_also_retains_by_time()
    {
        // Its own Serilog file measured 5-140 KB/day, so its 14-file/50 MB backstop is 700 MB it will
        // never touch. It keeps 14 days rather than 5 precisely because that is free — see the
        // comment there. What matters is that the retention rule is TIME, not a file count.
        var program = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "src", "Antiphon.SessionRunner", "Program.cs"));

        program.ShouldContain("retainedFileTimeLimit:");
    }

    [Test]
    public async Task Fake_gateway_no_longer_logs_every_health_poll_at_Information()
    {
        // 99.6% of the 57 MB unrolled logs/fake-gateway.log was three Microsoft.AspNetCore sources
        // writing a request lifecycle for each of 130 072 /health polls.
        var settingsPath = Path.Combine(
            RepoRoot, "src", "Antiphon.Messaging.FakeGateway", "appsettings.json");
        File.Exists(settingsPath).ShouldBeTrue(
            "the fake gateway shipped with NO settings file, so it ran at the framework default of Information");

        var config = new ConfigurationBuilder().AddJsonFile(settingsPath).Build();
        config["Logging:LogLevel:Microsoft.AspNetCore"].ShouldBe("Warning");
        // The recorder's own logs are the point of the file - they stay.
        config["Logging:LogLevel:Default"].ShouldBe("Information");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Daemon_supervisor_rotates_and_prunes_its_capture_log()
    {
        // logs/fake-gateway.log and logs/session-runner.log are cmd.exe '>>' captures with no
        // retention at all. run-daemon.ps1 rotates them at (re)launch - the only moment the handle
        // is free - and prunes rolls by age and count.
        var script = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "scripts", "run-daemon.ps1"));

        script.ShouldContain("Invoke-LogRotation");
        script.ShouldContain("$LogRetainDays");
        script.ShouldContain("$LogRetainCount");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static IConfiguration ShippedServerConfiguration() =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot, "server", "appsettings.json"))
            .Build();

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;

            return dir?.FullName
                ?? throw new DirectoryNotFoundException(
                    "Could not locate repo root (Antiphon.sln) from test base dir.");
        }
    }

    private sealed class CollectingSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}
