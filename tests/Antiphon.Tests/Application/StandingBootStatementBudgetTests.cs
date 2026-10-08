using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1156 S5: <see cref="FullCommandCounter"/> on every context the sweep, the writer, the
/// attention projection and the prune open (including the runtime's persistence context), plus
/// runner transcript-pull counters, over an isolated PostgreSQL database through
/// <c>StandingBootWatchFixture</c>. Design V-11 in
/// <c>docs/superpowers/plans/2026-10-08-card-1156-alwayson-boot-watchdog-test-design.md</c>.
/// The expected totals are measured exact totals at the S5 baseline, every roster is printed, and
/// a measured total that differs from the design pin is a design revision explained in the
/// design's "As built: S5" note, never a tolerance to widen.
/// </summary>
[Category("Integration")]
public class StandingBootStatementBudgetTests
{
    /// <summary>
    /// V-11a. One isolated one-session sweep per argument. Pulls are runner transcript requests.
    /// Transaction begin/commit are not <c>DbCommand</c> executions and are not counted.
    /// </summary>
    [Test]
    // Measured 2026-10-08 on the server2 mirror. Cheap paths: the live-candidates read 1, then the
    // model-reply EXISTS (unarmed) or the rows past the watched prompt (armed) = 2. Cold standing
    // path: candidates, rows, recorded-key pre-check, [pull: an empty persist, 0 SQL], rows,
    // owner list, task exclusion, model-reply EXISTS, prompt rows = 8; writer: session FOR UPDATE
    // SKIP LOCKED, owner, tasks, dedup, EXISTS, prompt rows, INSERT = 15. Without a runtime the
    // post-pull rows read is skipped (14). A recorded episode stops at the pre-check (3).
    [Arguments("boot-deadline-disabled", 0, 0)]
    [Arguments("no-live-sessions", 1, 0)]
    [Arguments("healthy-unarmed-answered", 2, 0)]
    [Arguments("armed-before-deadline", 2, 0)]
    [Arguments("first-detected-stage", 15, 1)]
    [Arguments("same-recorded-episode", 3, 0)]
    [Arguments("first-operator-stage", 15, 1)]
    [Arguments("runtime-absent-detection", 14, 0)]
    public async Task C1156_Boot_watch_statement_budgets(string path, int expected, int expectedPulls)
    {
        var counter = new FullCommandCounter();
        var options = path switch
        {
            "healthy-unarmed-answered" => new StandingBootWatchOptions { Arm = false },
            "armed-before-deadline" => new StandingBootWatchOptions { PromptAge = TimeSpan.FromMinutes(5) },
            _ => new StandingBootWatchOptions(),
        };
        await using var f = await StandingBootWatchFixture.CreateAsync(options with { Interceptors = [counter] });
        var runtime = path != "runtime-absent-detection";
        int expectedAct;
        string[] expectedStages;

        switch (path)
        {
            case "boot-deadline-disabled":
                f.Settings.BootModelWaitDeadlineMinutes = 0;
                (expectedAct, expectedStages) = (0, []);
                break;
            case "no-live-sessions":
                await EndSessionAsync(f);
                (expectedAct, expectedStages) = (0, []);
                break;
            case "healthy-unarmed-answered":
                await f.AddEntryAsync(TranscriptKinds.AssistantText, "answered", f.PromptAt.AddMinutes(1));
                (expectedAct, expectedStages) = (0, []);
                break;
            case "armed-before-deadline":
                (expectedAct, expectedStages) = (0, []);
                break;
            case "first-detected-stage":
            case "runtime-absent-detection":
                (expectedAct, expectedStages) = (1, ["detected"]);
                break;
            case "same-recorded-episode":
                (await f.SweepAsync()).ShouldBe(1, "control: the first tick records the episode: " + f.Warnings());
                (expectedAct, expectedStages) = (0, ["detected"]);
                break;
            case "first-operator-stage":
                (await f.SweepAsync()).ShouldBe(1, "control: the detected stage is on record first: " + f.Warnings());
                f.At(f.PromptAt.AddMinutes(21));
                (expectedAct, expectedStages) = (1, ["detected", "operator"]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(path), path, null);
        }

        var pullsBefore = f.Runner.TranscriptPulls;
        counter.Reset();
        (await f.SweepAsync(runtime)).ShouldBe(expectedAct, $"path {path}: " + f.Warnings());
        var total = counter.Total;
        var roster = counter.Roster();
        var pulls = f.Runner.TranscriptPulls - pullsBefore;
        Console.WriteLine($"C1156-BUDGET {path} total={total} pulls={pulls}");
        Console.WriteLine(roster);

        total.ShouldBe(expected, $"path {path}{Environment.NewLine}{roster}");
        pulls.ShouldBe(expectedPulls, $"path {path}: runner transcript pulls");
        f.Runner.Lists.ShouldBe(0, "the sweep never lists the runner");
        f.Warnings().ShouldNotContain("Boot-reply sweep failed");
        f.Warnings().ShouldNotContain("Could not record");
        (await f.ReceiptsAsync()).Select(r => r.FailureReason!.Split(";stage=")[1]).ShouldBe(expectedStages, $"path {path}: receipts");
        f.AssertNothingDestructive();
    }

    /// <summary>
    /// V-11b. The standing projection's own commands inside <c>GetAsync</c> (the projection's roster,
    /// measured directly, must appear as one contiguous slice of <c>GetAsync</c>'s roster, starting at
    /// the AlwaysOn pointer read) and the prune's additional commands over its inherited age DELETE
    /// and cap GROUP BY, for zero, one and two candidate sessions. A projection candidate is a live
    /// session an AlwaysOn agent points at; a prune candidate is a live session holding a
    /// <c>standingBoot:v1;</c> receipt. Zero writes and zero runner calls beyond <c>GetAsync</c>'s
    /// inherited list in every argument.
    /// </summary>
    [Test]
    // Measured 2026-10-08 on the server2 mirror. Projection: AlwaysOn pointers 1 (then the early
    // return), live sessions, open tasks, receipts, then model-reply EXISTS and prompt rows per
    // candidate = 1 / 6 / 8. Prune (S4 as built: candidates are live sessions holding a receipt,
    // no receipts loaded): the candidate read 1, then EXISTS and prompt rows per candidate =
    // 1 / 3 / 5 over its inherited 2 (design said 1 / 5 / 7).
    [Arguments("zero-candidates", 1, 1)]
    [Arguments("one-candidate", 6, 3)]
    [Arguments("two-candidates", 8, 5)]
    public async Task C1156_Attention_and_pruning_statement_budgets(string candidates, int helper, int pruneDelta)
    {
        await using var f = await StandingBootWatchFixture.CreateAsync(
            new StandingBootWatchOptions { AlwaysOn = candidates != "zero-candidates" });
        var expectedSessions = new List<Guid>();
        if (candidates != "zero-candidates")
            expectedSessions.Add(f.SessionId);
        if (candidates == "two-candidates")
            expectedSessions.Add(await AddStandingSessionAsync(f));
        if (candidates == "zero-candidates")
        {
            // Control: the non-AlwaysOn session is overdue and keeps the legacy diagnostic, which
            // is neither a projection candidate nor a prune candidate.
            (await f.SweepAsync()).ShouldBe(1, f.Warnings());
            (await f.ReceiptsAsync()).ShouldBeEmpty();
        }
        else
        {
            (await f.SweepAsync()).ShouldBe(expectedSessions.Count, "control: one receipt per candidate: " + f.Warnings());
        }

        // Projection: measured directly, then found as a contiguous slice of GetAsync.
        var direct = new FullCommandCounter();
        IReadOnlyList<Guid> projected;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(f.ConnectionString, direct)))
        {
            var result = await StandingBootAttentionProjection.ProjectAsync(
                db, new DelegationSettings(), f.Clock.GetUtcNow().UtcDateTime, NullLogger.Instance, CancellationToken.None);
            projected = result.Items.Select(i => i.SessionId!.Value).ToList();
        }

        var helperRoster = direct.Commands;
        Console.WriteLine($"C1156-BUDGET projection {candidates} total={helperRoster.Count}");
        Console.WriteLine(direct.Roster());
        projected.ShouldBe(expectedSessions, ignoreOrder: true, "control: one row per candidate");
        helperRoster.Count.ShouldBe(helper, $"projection {candidates}{Environment.NewLine}{direct.Roster()}");
        helperRoster[0].ShouldContain("FROM \"Agents\"", customMessage: "the projection starts at the pointer read");
        helperRoster[0].ShouldContain("\"AlwaysOn\"", customMessage: "the projection starts at the pointer read");

        var counter = new FullCommandCounter();
        var lists = f.Runner.Lists;
        List<AttentionItemDto> items;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(f.ConnectionString, counter)))
        {
            items = (await AttentionServiceTests.BuildService(f.Runner, timeProvider: f.Clock, db: db)
                .GetAsync(CancellationToken.None)).Items.ToList();
            db.ChangeTracker.HasChanges().ShouldBeFalse("nothing is staged during a GET");
        }

        var all = counter.Commands;
        Console.WriteLine($"C1156-BUDGET getasync {candidates} total={all.Count}");
        Console.WriteLine(counter.Roster());
        all.Where(IsWrite).ShouldBeEmpty("the projection is read-only:\n" + counter.Roster());
        (f.Runner.Lists - lists).ShouldBe(1, "no runner call beyond GetAsync's inherited list");
        var start = IndexOfSlice(all, helperRoster);
        start.ShouldBeGreaterThanOrEqualTo(0,
            $"the projection's {helper} commands appear contiguously in GetAsync{Environment.NewLine}{counter.Roster()}");
        IndexOfSlice(all.Skip(start + 1).ToList(), [helperRoster[0]])
            .ShouldBe(-1, "the pointer read runs once per GetAsync");
        items.Where(i => i.Kind == AttentionKind.LivenessProbeFailed && i.TaskId is null
                && i.Headline.StartsWith("Standing boot stall", StringComparison.Ordinal))
            .Select(i => i.SessionId!.Value)
            .ShouldBe(expectedSessions, ignoreOrder: true, "GetAsync shows the projected rows");

        // Prune: the inherited age DELETE and cap GROUP BY (no agent over the cap), plus the delta.
        var prune = new FullCommandCounter();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"c1156-budget-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            await using var harness = AgentSupervisionTests.BuildHarness(
                tempRoot, [], connectionString: f.ConnectionString, configureDb: o => o.AddInterceptors(prune));
            var supervisor = harness.Supervisor();
            prune.Reset();
            (await supervisor.PruneIncidentsAsync(CancellationToken.None)).ShouldBe(0, "nothing is old or over the cap");
            harness.Runner.KillCalls.ShouldBe(0, "the prune touches no session");
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort temp cleanup.
            }
        }

        var pruned = prune.Commands;
        Console.WriteLine($"C1156-BUDGET prune {candidates} total={pruned.Count}");
        Console.WriteLine(prune.Roster());
        var inherited = pruned.Where(sql => sql.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)).ToList();
        inherited.Count.ShouldBe(2, $"the inherited age DELETE and cap GROUP BY{Environment.NewLine}{prune.Roster()}");
        (pruned.Count - inherited.Count).ShouldBe(pruneDelta, $"prune {candidates}{Environment.NewLine}{prune.Roster()}");
        pruned.Except(inherited).Where(IsWrite).ShouldBeEmpty("the protected-key reads write nothing");
        (await f.ReceiptsAsync()).Count.ShouldBe(candidates == "zero-candidates" ? 0 : 1, "the prune kept the current receipt");
        f.AssertNothingDestructive();
    }

    private static int IndexOfSlice(IReadOnlyList<string> all, IReadOnlyList<string> slice)
    {
        for (var start = 0; start + slice.Count <= all.Count; start++)
        {
            var match = true;
            for (var i = 0; i < slice.Count && match; i++)
                match = all[start + i] == slice[i];
            if (match)
                return start;
        }

        return -1;
    }

    private static bool IsWrite(string sql)
    {
        var head = sql.TrimStart();
        return head.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("MERGE", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task EndSessionAsync(StandingBootWatchFixture f)
    {
        await using var db = f.Read();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId);
        session.Status = SessionStatus.Stopped;
        session.EndedAt = StandingBootWatchFixture.Pg(f.Now0.AddMinutes(-1));
        await db.SaveChangesAsync();
    }

    /// <summary>A second taskless AlwaysOn agent and its live, unanswered, unarmed session.</summary>
    private static async Task<Guid> AddStandingSessionAsync(StandingBootWatchFixture f)
    {
        var (sessionId, agentId) = (Guid.NewGuid(), Guid.NewGuid());
        var cwd = Path.GetTempPath();
        var name = $"sbb-{agentId:N}"[..16];
        await using var db = f.Read();
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = name,
            Slug = name,
            WorkingDirectory = cwd,
            Details = "CARD-1156 second standing agent.",
            Status = AgentStatus.Running,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            AlwaysOn = true,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = f.StartedAt,
            UpdatedAt = f.StartedAt,
        });
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "standing-boot-second",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = f.StartedAt,
            StartedAt = f.StartedAt,
            LastSeenAt = f.StartedAt,
            StandingAgentId = agentId,
        });
        db.TranscriptEntries.Add(StandingBootWatchFixture.Entry(
            sessionId, 1, TranscriptKinds.UserPrompt, "the second standing brief", f.PromptAt));
        await db.SaveChangesAsync();
        return sessionId;
    }
}
