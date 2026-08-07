using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Antiphon.E2E.Fixtures;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.E2E;

/// <summary>
/// The delegation pipeline against a REAL Claude and a REAL Antiphon server.
///
/// What is genuinely exercised, end to end:
///  * a real Claude session, launched with the ANTIPHON_* environment, discovers the
///    antiphon-delegate skill from its cwd and decides to invoke it;
///  * its delegate.ps1 call reaches the live server over HTTP and creates a task — with a role and
///    a shape (worker vs sub-orchestrator) the MODEL chose, which is the entire
///    "auto-decide how complex this is" mechanism;
///  * the dispatcher claims that task, creates the delegate's session and agent, resolves the tier
///    to a --model alias, and queues the brief;
///  * a real Claude runs that brief and produces a report;
///  * the reply path correlates that report to the task by marker, settles it, and delivers a
///    bounded note into the ORCHESTRATOR's session queue.
///
/// One seam is substituted: spawning the delegate's pty is normally the session-runner daemon's
/// job, and a daemon is not available inside a WebApplicationFactory test. The test plays that
/// role — it launches the real Claude the dispatcher asked for, in the directory it asked for, with
/// the args it asked for, and feeds the resulting transcript back the way the runner would. The
/// daemon's own spawn path is covered by the session-runner suites; what is unique to delegation is
/// everything either side of it, and that is all real here.
///
/// Opt-in headed: ANTIPHON_HEADED_TESTS=1 + claude on PATH; self-skips otherwise.
/// </summary>
[Category("Headed")]
[Category("HeadedCanary")]
[NotInParallel("Headed")]
public class DelegationPipelineE2ETests
{
    private static readonly TimeSpan TurnTimeout = TimeSpan.FromMinutes(3);

    [Test]
    public async Task A_real_claude_delegates_through_the_skill_and_the_report_comes_home()
    {
        SkipIfNotEligible();

        var fixture = new AntiphonAppFixture();
        await fixture.InitializeAsync();
        using var repo = new ScratchRepo();
        try
        {
            ConfigureDelegationRoots(fixture, repo.Path);

            // ---- the orchestrator -----------------------------------------------------------
            // A session row for the caller, so the task's report has somewhere to be delivered.
            var orchestratorSessionId = await SeedSessionAsync(fixture, repo.Path);
            var orchestratorTaskId = await SeedOrchestratorTaskAsync(fixture, repo.Path, orchestratorSessionId);
            var token = AgentTaskService.RawTokens[orchestratorTaskId];

            await using var orchestrator = new PtyAgentRunner();
            var (app, args) = BuildLaunch(
                ResolveClaudeOrThrow(), "--dangerously-skip-permissions", "--model", "sonnet");
            await orchestrator.StartAsync(
                app, args, cwd: repo.Path, env: DelegateEnv(fixture, orchestratorSessionId, orchestratorTaskId, token),
                cols: 120, rows: 30);

            if (!await WaitUntilUsableAsync(orchestrator))
                throw new SkipTestException("real Claude TUI did not reach a usable state");
            orchestrator.ClearLiveBuffer();

            // The instruction never names the script or the role — the model must find the skill
            // and classify the work itself. That is the behaviour under test.
            await SubmitAsync(
                orchestrator,
                "Use your antiphon-delegate skill to hand off this work to another agent: "
                + "update README.md so the install section says pwsh 7 instead of cmd. "
                + "Delegate it, then stop and end your turn. Do not edit any files yourself.");

            // ---- the callback ---------------------------------------------------------------
            var task = await WaitForDelegatedTaskAsync(fixture, orchestratorTaskId, TimeSpan.FromMinutes(6));
            task.ShouldNotBeNull(
                "a real Claude must be able to invoke the skill and reach the API. Screen:\n"
                + orchestrator.SnapshotScreen());

            Console.WriteLine($"Model chose: kind={task!.Kind} role={task.Role} tier={task.ModelLevel}");
            task.Kind.ShouldBe(AgentTaskKind.Worker, "a one-file doc edit is a worker, not a sub-orchestrator");
            task.Role.ShouldBeOneOf(AgentTaskRole.Docs, AgentTaskRole.Code, AgentTaskRole.Custom);
            task.Workspace.ShouldBe(WorkspaceMode.Shared, "shared is the default — isolation is opt-in");
            task.WorkingDirectory.ShouldBe(repo.Path, "no -Dir given, so it inherits the caller's directory");
            task.ParentTaskId.ShouldBe(orchestratorTaskId);
            task.ParentSessionId.ShouldBe(orchestratorSessionId, "the report must come back to its caller");

            // ---- dispatch -------------------------------------------------------------------
            // The dispatcher's own hosted service is running in this app, so the task may already be
            // claimed; ticking again is a harmless no-op that also covers the case where it hasn't.
            var dispatched = await DispatchAsync(fixture, task.Id);
            dispatched.Status.ShouldBe(
                AgentTaskStatus.Dispatched,
                $"the dispatcher must claim the task. FailureReason: {dispatched.FailureReason ?? "(none)"}");
            dispatched.AgentSessionId.ShouldNotBeNull("the dispatcher creates the delegate's session");
            dispatched.AgentId.ShouldNotBeNull("an ephemeral agent is created at the task's tier");

            var brief = await ReadQueuedBriefAsync(fixture, dispatched.AgentSessionId!.Value);
            brief.Contains(DelegationReportFormatter.TaskMarker(task.Id), StringComparison.Ordinal)
                .ShouldBeTrue("the marker is how the reply correlates back to this task");
            brief.Contains("how to report back", StringComparison.OrdinalIgnoreCase)
                .ShouldBeTrue("every delegate is given the reporting contract, server-side");

            // ---- the delegate ---------------------------------------------------------------
            // Standing in for the session-runner: launch the Claude the dispatcher asked for, in
            // the directory it asked for, and give it the brief the server composed.
            await using var delegateSession = new PtyAgentRunner();
            var (dApp, dArgs) = BuildLaunch(
                ResolveClaudeOrThrow(), "--dangerously-skip-permissions",
                "--model", ModelLevelAliases.ForClaude(task.ModelLevel));
            await delegateSession.StartAsync(
                dApp, dArgs, cwd: task.WorkingDirectory,
                env: DelegateEnv(fixture, dispatched.AgentSessionId.Value, task.Id, token: null),
                cols: 120, rows: 30);

            if (!await WaitUntilUsableAsync(delegateSession))
                throw new SkipTestException("delegate Claude TUI did not reach a usable state");
            delegateSession.ClearLiveBuffer();

            await SubmitAsync(delegateSession, brief);

            // Quiet-period detection rather than a status-line regex: Claude's spinner fires every
            // ~65ms while it works, so a sustained gap is a reliable "turn finished" signal and does
            // not depend on the TUI's wording or the terminal width.
            (await new ClaudeDoneDetector { QuietPeriod = TimeSpan.FromSeconds(6), MaxWait = TurnTimeout }
                    .WaitAsync(delegateSession))
                .ShouldBeTrue("the delegate must complete a turn. Screen:\n" + delegateSession.SnapshotScreen());

            var report = ExtractFinalMessage(delegateSession.SnapshotScreen());
            report.ShouldNotBeNullOrWhiteSpace("the delegate's final message IS the report");

            // ---- the report comes home ------------------------------------------------------
            await IngestTurnAsync(fixture, dispatched.AgentSessionId.Value, brief, report);
            await SettleAsync(fixture, dispatched.AgentSessionId.Value);

            var settled = await GetTaskAsync(fixture, task.Id);
            settled.Status.ShouldBeOneOf(AgentTaskStatus.Succeeded, AgentTaskStatus.Blocked);
            settled.Result.ShouldNotBeNullOrWhiteSpace("the task keeps the delegate's report verbatim");

            var note = await ReadDeliveredNoteAsync(fixture, orchestratorSessionId);
            note.ShouldNotBeNull("the orchestrator must receive its delegate's report");
            note!.Contains(DelegationReportFormatter.Short(task.Id), StringComparison.Ordinal)
                .ShouldBeTrue("the note identifies which task finished");
            note.Contains('\r').ShouldBeFalse("a CR would fragment the note into several turns");
            note.Length.ShouldBeLessThanOrEqualTo(
                25_000, "the note is bounded — that is what keeps the orchestrator's context small");

            Console.WriteLine($"Note delivered to the orchestrator ({note.Length} chars):\n{note}");
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Test]
    public async Task A_real_claude_sends_a_sub_orchestrator_for_work_that_needs_decomposing()
    {
        // The other half of the classification: getting the ROLE right but the SHAPE wrong still
        // wastes a tier. Separate test so a failure here is unambiguous.
        SkipIfNotEligible();

        var fixture = new AntiphonAppFixture();
        await fixture.InitializeAsync();
        using var repo = new ScratchRepo();
        try
        {
            ConfigureDelegationRoots(fixture, repo.Path);
            var sessionId = await SeedSessionAsync(fixture, repo.Path);
            var rootTaskId = await SeedOrchestratorTaskAsync(fixture, repo.Path, sessionId);
            var token = AgentTaskService.RawTokens[rootTaskId];

            await using var runner = new PtyAgentRunner();
            var (app, args) = BuildLaunch(
                ResolveClaudeOrThrow(), "--dangerously-skip-permissions", "--model", "sonnet");
            await runner.StartAsync(
                app, args, cwd: repo.Path, env: DelegateEnv(fixture, sessionId, rootTaskId, token),
                cols: 120, rows: 30);

            if (!await WaitUntilUsableAsync(runner))
                throw new SkipTestException("real Claude TUI did not reach a usable state");
            runner.ClearLiveBuffer();

            await SubmitAsync(
                runner,
                "Use your antiphon-delegate skill to hand off this whole piece of work: migrate this "
                + "project from Postgres 17 to 18 — schema changes, the docker compose file, the "
                + "connection strings, the docs, and a full test pass. Delegate it as ONE handoff, "
                + "then stop and end your turn.");

            var task = await WaitForDelegatedTaskAsync(fixture, rootTaskId, TimeSpan.FromMinutes(6));
            task.ShouldNotBeNull("the skill must be invoked. Screen:\n" + runner.SnapshotScreen());

            Console.WriteLine($"Model chose: kind={task!.Kind} role={task.Role} tier={task.ModelLevel}");
            task.Kind.ShouldBe(
                AgentTaskKind.Orchestrator,
                "a multi-step migration is a chunk that needs its own decomposition");
            // A sub-orchestrator must never be dispatched cheap — decomposition is expensive thinking.
            ((int)task.ModelLevel).ShouldBeLessThanOrEqualTo((int)AgentModelLevel.High);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    // ---- server-side helpers (the test drives the real DI container) ---------------------------

    private static void ConfigureDelegationRoots(AntiphonAppFixture fixture, string root)
    {
        var settings = fixture.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<DelegationSettings>>().Value;
        settings.AllowedRoots.Add(root);
        settings.ApiBaseUrl = fixture.BaseAddress;
    }

    private static async Task<Guid> SeedSessionAsync(AntiphonAppFixture fixture, string cwd)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        };
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    /// <summary>
    /// The root orchestrator's own task row — it is what gives the live Claude a token with the
    /// create scope, and what its delegate hangs off.
    /// </summary>
    private static async Task<Guid> SeedOrchestratorTaskAsync(
        AntiphonAppFixture fixture, string cwd, Guid sessionId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
        var created = await service.CreateAsync(
            new Server.Application.Dtos.CreateAgentTaskRequest(
                Goal: "Coordinate this run.",
                Kind: AgentTaskKind.Orchestrator,
                Role: AgentTaskRole.Plan),
            new AgentTaskService.Caller(null, null, cwd),
            CancellationToken.None);

        // Bind it to the live session so its children inherit the right reply target.
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
        row.AgentSessionId = sessionId;
        row.Status = AgentTaskStatus.Working;
        await db.SaveChangesAsync();
        return created.Id;
    }

    private static async Task<AgentTask?> WaitForDelegatedTaskAsync(
        AntiphonAppFixture fixture, Guid parentTaskId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await db.AgentTasks.AsNoTracking()
                .FirstOrDefaultAsync(t => t.ParentTaskId == parentTaskId);
            if (task is not null)
                return task;
            await Task.Delay(2_000);
        }
        return null;
    }

    private static async Task<AgentTask> DispatchAsync(AntiphonAppFixture fixture, Guid taskId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        await dispatcher.TickAsync(CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
    }

    private static async Task<string> ReadQueuedBriefAsync(AntiphonAppFixture fixture, Guid sessionId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId)
            .OrderBy(m => m.Sequence)
            .FirstOrDefaultAsync();
        message.ShouldNotBeNull("the dispatcher queues the brief through the message queue");
        return message!.Body;
    }

    /// <summary>Write the transcript rows the session runner would have persisted for this turn.</summary>
    private static async Task IngestTurnAsync(
        AntiphonAppFixture fixture, Guid sessionId, string prompt, string report)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seq = await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;

        db.TranscriptEntries.Add(Entry(sessionId, ++seq, TranscriptKinds.UserPrompt, prompt));
        db.TranscriptEntries.Add(Entry(sessionId, ++seq, TranscriptKinds.AssistantText, report));
        var end = Entry(sessionId, ++seq, TranscriptKinds.TurnEnd, null);
        end.StopReason = "end_turn";
        db.TranscriptEntries.Add(end);
        await db.SaveChangesAsync();
    }

    private static async Task SettleAsync(AntiphonAppFixture fixture, Guid sessionId)
    {
        var replies = fixture.Services.GetRequiredService<AgentTaskReplyService>();
        await replies.OnTurnEndAsync(sessionId, CancellationToken.None);
    }

    private static async Task<AgentTask> GetTaskAsync(AntiphonAppFixture fixture, Guid taskId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
    }

    private static async Task<string?> ReadDeliveredNoteAsync(AntiphonAppFixture fixture, Guid parentSessionId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == parentSessionId && m.Origin == QueuedMessageOrigin.Delegation)
            .OrderByDescending(m => m.Sequence)
            .Select(m => m.Body)
            .FirstOrDefaultAsync();
    }

    private static TranscriptEntry Entry(Guid sessionId, long sequence, string kind, string? text) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = sessionId,
        Sequence = sequence,
        Kind = kind,
        Text = text,
        CreatedAt = DateTime.UtcNow,
    };

    // ---- pty helpers ---------------------------------------------------------------------------

    /// <summary>The env contract — the delegate learns who it is from here, never from arguments.</summary>
    private static Dictionary<string, string> DelegateEnv(
        AntiphonAppFixture fixture, Guid sessionId, Guid taskId, string? token)
    {
        var env = new Dictionary<string, string>
        {
            // Neutralise nested-Claude markers: these tests often run from inside a Claude session,
            // and a child that sees them does not persist its transcript.
            ["CLAUDE_CODE_CHILD_SESSION"] = "",
            ["CLAUDE_CODE_SESSION_ID"] = "",
            ["CLAUDE_CODE_BRIDGE_SESSION_ID"] = "",
            ["ANTIPHON_API"] = fixture.BaseAddress,
            ["ANTIPHON_SESSION_ID"] = sessionId.ToString("D"),
            ["ANTIPHON_AGENT_ID"] = Guid.Empty.ToString("D"),
            ["ANTIPHON_TASK_ID"] = taskId.ToString("D"),
        };
        if (token is not null)
            env["ANTIPHON_TASK_TOKEN"] = token;
        return env;
    }

    /// <summary>
    /// The last assistant message on screen. Deliberately loose: we assert the report EXISTS and
    /// comes home intact, never its wording — a model is not a deterministic fixture.
    /// </summary>
    private static string ExtractFinalMessage(string screen)
    {
        var lines = screen.ReplaceLineEndings("\n").Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Trim().Length > 0)
            .ToList();

        // Claude's TUI prefixes assistant output with a bullet; fall back to the tail of the screen.
        var body = lines.Where(l => !l.TrimStart().StartsWith('>') && !l.Contains("? for shortcuts"))
            .TakeLast(30)
            .ToList();
        return string.Join("\n", body).Trim();
    }

    /// <summary>
    /// Type a body into the TUI and submit it — the same three-step discipline
    /// <c>SessionMessageQueueService.DeliverAsync</c> uses in production, and for the same reasons:
    /// line endings normalised to LF (a CR mid-body acts as Enter and submits the fragment before
    /// it), then a SEPARATE carriage return after a pause. Body and CR in one write are treated as
    /// a bracketed paste and the CR is folded into a literal newline — the text sits in the
    /// composer, unsent, and the turn never happens.
    /// </summary>
    private static async Task SubmitAsync(PtyAgentRunner runner, string body)
    {
        await runner.WriteAsync(body.ReplaceLineEndings("\n"));
        await Task.Delay(1_000);
        await runner.WriteAsync("\r");
    }

    /// <summary>
    /// Reach a usable prompt, answering the trust-folder dialog on the way.
    ///
    /// A scratch directory is unknown to Claude, so its first screen is "Is this a project you
    /// created or one you trust?" — a modal that swallows every keystroke until answered. Without
    /// this the session looks ready, accepts the instruction, and silently does nothing.
    /// </summary>
    private static async Task<bool> WaitUntilUsableAsync(PtyAgentRunner runner)
    {
        if (!await new ClaudeReadyDetector().WaitAsync(runner))
            return false;

        // POLL for the dialog rather than checking once. It can render after the first quiet window
        // (the quiet window is exactly when it is sitting there waiting), and a single check that
        // lands a moment too early leaves the session looking ready while every keystroke is
        // swallowed — the instruction goes nowhere and the test fails much later, misleadingly.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            if (!LooksLikeTrustPrompt(runner.SnapshotScreen()))
            {
                // Give a late-rendering dialog one more chance to appear before declaring victory.
                await Task.Delay(3_000);
                if (!LooksLikeTrustPrompt(runner.SnapshotScreen()))
                    return true;
            }

            // Default selection is already "Yes, I trust this folder" — Enter accepts it.
            await runner.WriteAsync("\r");
            await Task.Delay(4_000);
        }

        return false;
    }

    private static bool LooksLikeTrustPrompt(string screen)
    {
        var compact = new string(screen.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return compact.Contains("doyoutrustthisfolder")
            || compact.Contains("isthisaprojectyoucreated")
            || compact.Contains("yesitrustthisfolder");
    }

    private static void SkipIfNotEligible()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("Headed tests require Windows ConPTY");
        if (Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS") != "1")
            throw new SkipTestException("Set ANTIPHON_HEADED_TESTS=1 to opt in to headed-claude tests");
        if (ResolveClaude() is null)
            throw new SkipTestException("claude not found on PATH; cannot run headed tests");
    }

    private static string ResolveClaudeOrThrow() =>
        ResolveClaude() ?? throw new InvalidOperationException("claude not found on PATH");

    private static string? ResolveClaude()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var name in new[] { "claude.exe", "claude.cmd", "claude.bat", "claude.ps1" })
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static (string App, string[] Args) BuildLaunch(string claude, params string[] extraArgs)
    {
        if (claude.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var args = new List<string> { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", claude };
            args.AddRange(extraArgs);
            return ("pwsh.exe", args.ToArray());
        }
        if (claude.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return (claude, extraArgs);

        var cmdArgs = new List<string> { "/d", "/c", claude };
        cmdArgs.AddRange(extraArgs);
        return (Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmdArgs.ToArray());
    }

    /// <summary>
    /// A throwaway git repo carrying the real skill and script, so the live Claude discovers them
    /// from its cwd exactly as an agent would in production.
    /// </summary>
    private sealed class ScratchRepo : IDisposable
    {
        public string Path { get; }

        public ScratchRepo()
        {
            Path = Directory.CreateTempSubdirectory("antiphon-delegation-e2e").FullName;
            var repoRoot = FindRepoRoot();

            var skillDir = System.IO.Path.Combine(Path, ".claude", "skills", "antiphon-delegate");
            Directory.CreateDirectory(skillDir);
            File.Copy(
                System.IO.Path.Combine(repoRoot, ".claude", "skills", "antiphon-delegate", "SKILL.md"),
                System.IO.Path.Combine(skillDir, "SKILL.md"));

            var scriptDir = System.IO.Path.Combine(Path, "scripts");
            Directory.CreateDirectory(scriptDir);
            File.Copy(
                System.IO.Path.Combine(repoRoot, "scripts", "delegate.ps1"),
                System.IO.Path.Combine(scriptDir, "delegate.ps1"));

            File.WriteAllText(
                System.IO.Path.Combine(Path, "README.md"),
                "# Scratch\n\n## Install\n\nRun `cmd /c setup.bat` to install.\n");

            Run("init");
            Run("add", ".");
            Run("-c", "user.email=test@antiphon.local", "-c", "user.name=Antiphon Test",
                "commit", "-m", "scratch");
        }

        private void Run(params string[] args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = Path,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(30_000);
        }

        private static string FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null)
            {
                if (File.Exists(System.IO.Path.Combine(dir, "Antiphon.sln")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { /* a live pty's lock must not fail the test */ }
            catch (UnauthorizedAccessException) { /* git's read-only object files */ }
        }
    }
}
