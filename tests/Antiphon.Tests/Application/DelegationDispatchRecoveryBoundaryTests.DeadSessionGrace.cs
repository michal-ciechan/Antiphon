using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1161. The dead-session grace is <c>TimeProvider.GetElapsedTime</c> from the in-memory
/// stamp. Wall time and elapsed time are stepped independently between two sweeps (one sweep
/// when the state is fresh). The counting runner's session list stays empty, and bind recovery
/// is not wired.
/// </summary>
public partial class DelegationDispatchRecoveryBoundaryTests
{
    [Test]
    [Arguments("forward-jump-ordinary", "ordinary", 1, 300, 2, "Dispatched")]
    [Arguments("forward-jump-hold", "hold", 1, 300, 2, "Dispatched")]
    [Arguments("forward-jump-working", "working", 1, 300, 2, "Working")]
    [Arguments("rollback-ordinary", "ordinary", 300, 60, 2, "Failed")]
    [Arguments("rollback-hold", "hold", 300, 60, 2, "Blocked")]
    [Arguments("negative-ordinary", "ordinary", -1, 300, 2, "Dispatched")]
    [Arguments("negative-hold", "hold", -1, 300, 2, "Dispatched")]
    [Arguments("inside-ordinary", "ordinary", 60, 60, 2, "Dispatched")]
    [Arguments("aligned-ordinary", "ordinary", 300, 300, 2, "Failed")]
    [Arguments("aligned-hold", "hold", 300, 300, 2, "Blocked")]
    [Arguments("aligned-working", "working", 300, 300, 2, "Failed")]
    [Arguments("exact-ordinary", "ordinary", 180, 180, 2, "Failed")]
    [Arguments("restart-ordinary", "ordinary", 600, 600, 1, "Dispatched")]
    public async Task C1161_Dead_session_grace_is_monotonic(
        string name, string shape, int elapsedSeconds, int wallSeconds, int sweeps, string expect)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedAsync(schema.ConnectionString, Shape(shape));
        var clock = new SplitClock(DateTimeOffset.UtcNow);
        var seen = new DeadSessionFirstSeenState();
        var runner = new CountingRunner();
        var stopper = new RecordingSessionStopper();
        if (sweeps == 1)
        {
            clock.Elapsed = TimeSpan.FromSeconds(elapsedSeconds);
            clock.Wall = TimeSpan.FromSeconds(wallSeconds);
        }

        await using (var host = OpenSweep(schema.ConnectionString, runner, stopper, clock, seen))
        {
            await host.SweepAsync();
            if (sweeps == 2)
            {
                clock.Elapsed = TimeSpan.FromSeconds(elapsedSeconds);
                clock.Wall = TimeSpan.FromSeconds(wallSeconds);
                await host.SweepAsync();
            }
        }

        seen.IsTracking(seeded.TaskId).ShouldBe(expect is "Dispatched" or "Working", name);
        Quiet(runner, stopper, name);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
        task.Status.ShouldBe(Expect(expect), name);
        if (expect is "Dispatched" or "Working")
        {
            (await db.AgentTaskEvents.CountAsync(e =>
                    e.AgentTaskId == seeded.TaskId && e.Type == AgentTaskEventType.Blocked))
                .ShouldBe(0, name);
        }

        if (expect == "Blocked")
        {
            task.FailureReason.ShouldBe(AgentTaskDispatcher.DispatchLaunchAbsentReason, name);
            task.Attempt.ShouldBe(1, name);
        }
    }

    private static AbsentShape Shape(string shape) => shape switch
    {
        "ordinary" => new AbsentShape { SessionReason = "process vanished" },
        "hold" => new AbsentShape(),
        "working" => new AbsentShape
        {
            Status = AgentTaskStatus.Working,
            SessionReason = "process vanished",
        },
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
    };

    private static AgentTaskStatus Expect(string expect) => expect switch
    {
        "Dispatched" => AgentTaskStatus.Dispatched,
        "Working" => AgentTaskStatus.Working,
        "Failed" => AgentTaskStatus.Failed,
        "Blocked" => AgentTaskStatus.Blocked,
        _ => throw new ArgumentOutOfRangeException(nameof(expect), expect, null),
    };

    /// <summary>
    /// CARD-1161 V-2. The runtime bullet after the certificate-age pin, and the
    /// <c>DeadSessionFailGraceMinutes</c> summary, name this gate.
    /// </summary>
    [Test]
    public void C1161_Owner_names_the_monotonic_grace()
    {
        var runtime = ReadRepo("docs/session-runtime-invariants.md");
        const string lead = "- **The dead-session grace is monotonic elapsed time (CARD-1161).**";
        var bullet = Slice(runtime, lead, "\n- **");
        var pinAt = runtime.IndexOf(
            "DelegationDispatchRecoveryBoundaryTests.C1153_Certificate_age_is_monotonic",
            StringComparison.Ordinal);
        var bulletAt = runtime.IndexOf(lead, StringComparison.Ordinal);
        pinAt.ShouldBeGreaterThanOrEqualTo(0);
        bulletAt.ShouldBeGreaterThan(pinAt);
        runtime[pinAt..bulletAt].ShouldNotContain("\n- **");

        var settings = ReadRepo("server/Application/Settings/DelegationSettings.cs");
        const string property = "public int DeadSessionFailGraceMinutes { get; set; } = 3;";
        var propertyAt = settings.IndexOf(property, StringComparison.Ordinal);
        propertyAt.ShouldBeGreaterThanOrEqualTo(0);
        var summaryAt = settings.LastIndexOf("/// <summary>", propertyAt, StringComparison.Ordinal);
        summaryAt.ShouldBeGreaterThanOrEqualTo(0);
        var summary = settings[summaryAt..(propertyAt + property.Length)];

        foreach (var phrase in new[]
        {
            "CARD-1161",
            "GetElapsedTime",
            "DeadSessionFirstSeenState",
            "DeadSessionFailGraceMinutes",
            "Blocked",
            "FailDeadSessionTasksAsync",
            "ShouldHold",
            "C1161_Dead_session_grace_is_monotonic",
            "C1161_Owner_names_the_monotonic_grace",
        })
            bullet.ShouldContain(phrase, Case.Sensitive);

        foreach (var phrase in new[]
        {
            "CARD-1161",
            "GetElapsedTime",
            "DeadSessionFirstSeenState",
            "DeadSessionFailGraceMinutes",
            "Blocked",
        })
            summary.ShouldContain(phrase, Case.Sensitive);
    }

    private static string Slice(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        from.ShouldBeGreaterThanOrEqualTo(0, start);
        var to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        to.ShouldBeGreaterThan(from, end);
        return text[from..to];
    }

    private static string ReadRepo(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Antiphon.sln")))
                return File.ReadAllText(Path.Combine(dir, relative)).Replace("\r\n", "\n");
            dir = Path.GetDirectoryName(dir)!;
        }

        throw new DirectoryNotFoundException(relative);
    }
}
