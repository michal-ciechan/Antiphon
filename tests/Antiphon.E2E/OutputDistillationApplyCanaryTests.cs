using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Antiphon.E2E.Fixtures;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

/// <summary>Real providers only. Never invoked without a current operator-authored decision.</summary>
[Explicit]
[Category("Headed")]
[Category("HeadedCanary")]
[NotInParallel("Headed")]
[ParallelLimiter<ProcessSpawnLimit>]
public class OutputDistillationApplyCanaryTests
{
    private const string Middle = "The violet lantern marks the distinctive middle of this report.";

    [Test]
    public async Task Real_apply_and_long_fallback_reach_parent_and_files_survive_cleanup()
    {
        DistillerCanaryGuard.ValidateOptIns(Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS"),
            Environment.GetEnvironmentVariable("ANTIPHON_DISTILLER_APPLY_CANARY"));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        var ct = deadline.Token;
        var approval = await DistillerCanaryApproval.ReadAsync(
            Environment.GetEnvironmentVariable("ANTIPHON_DISTILLER_CANARY_APPROVAL_FILE"), DateTimeOffset.UtcNow, ct);
        OperatingSystem.IsWindows().ShouldBeTrue();
        ConPtyRedistributable.TryLocate(out _, out var why).ShouldBeTrue(why);
        var root = Path.Combine(AntiphonAppFixture.FindRepositoryRoot(), ".antiphon", "acceptance", "card-0419", Guid.NewGuid().ToString("N"));
        var options = new DistillerCanaryOptions(root, "c419-" + Guid.NewGuid().ToString("N"), approval);
        Directory.CreateDirectory(options.Repo);
        await GitAsync(options.Repo, ct, "init", "-b", "main");
        Directory.CreateDirectory(Path.Combine(options.Repo, "docs"));
        await File.WriteAllTextAsync(Path.Combine(options.Repo, "docs", "canary-evidence.md"), "# Canary deliverable\nBenign evidence fixture.\n", ct);
        await File.WriteAllTextAsync(Path.Combine(options.Repo, "short-input.txt"), Report(5500), ct);
        await File.WriteAllTextAsync(Path.Combine(options.Repo, "long-input.txt"), Report(21000), ct);
        await GitAsync(options.Repo, ct, "add", ".");
        await GitAsync(options.Repo, ct, "-c", "user.name=Canary", "-c", "user.email=canary@example.invalid", "commit", "-m", "benign canary fixtures");
        var app = new AntiphonAppFixture { DistillerCanary = options, UseMockExecutor = false, UsePrebuiltFrontend = false,
            DiagnosticsDirectory = Path.Combine(root, "logs") };
        var samples = new List<Sample>();
        var agentIds = new List<Guid>();
        Guid parentId = Guid.Empty;
        var initialized = false;
        Exception? failure = null;
        try
        {
            await app.InitializeAsync(); initialized = true; app.EnsureSessionRunnerReachable();
            var config = app.Services.GetRequiredService<IConfiguration>();
            DistillerCanaryGuard.ValidateResources(config.GetConnectionString("DefaultConnection")!, app.OwnedDatabase,
                config["SessionRunner:BaseUrl"]!, app.OwnedRunnerUrl,
                Path.Combine(app.OwnedRunnerDirectory, "logs"), Path.Combine(app.OwnedRunnerDirectory, "logs"),
                config["AntiphonMessaging:BootstrapServers"]!);
            app.Services.GetRequiredService<IAntiphonMessagingProducer>().ShouldBeOfType<RefusingCanaryMessaging>();
            var settings = app.Services.GetRequiredService<IOptions<DelegationSettings>>().Value;
            settings.OutputDistillerMode.ShouldBe(OutputDistillerMode.Apply); settings.DistillMinChars.ShouldBe(4000);
            settings.DistillMaxRawChars.ShouldBe(20000); settings.OutputDistillerWaitSeconds.ShouldBe(45);
            (await app.Services.GetRequiredService<PtyDeliveryProfile>().RefreshAsync(ct)).ReplyInlineMaxChars.ShouldBe(14400);
            using (var scope = app.Services.CreateScope())
            {
                var agent = await scope.ServiceProvider.GetRequiredService<AgentService>().CreateAsync(new(
                    "c419-parent-" + Guid.NewGuid().ToString("N"), options.Repo,
                    Details: "Canary parent. Acknowledge task completions briefly. Never poll tasks, dispatch work, or read report files unless explicitly instructed.",
                    ModelLevel: AgentModelLevel.Low, RemoteControlEnabled: false), ct);
                agentIds.Add(agent.Id);
                var started = await scope.ServiceProvider.GetRequiredService<AgentControlService>().StartAsync(agent.Id,
                    new(Prompt: "Acknowledge readiness for the canary. Do not read or poll any task reports."), ct);
                parentId = Guid.Parse(started.PersistentSessionId!);
                var specialist = await scope.ServiceProvider.GetRequiredService<OutputDistillerProvisioner>().EnsureAsync(ct);
                specialist.ShouldNotBeNull(); agentIds.Add(specialist.Id);
                approval.ValidateModel(specialist.Kind.ToString(), DispatchModelAlias.Resolve(specialist.Kind, AgentModelLevel.Low, specialist.ModelId));
            }
            await UntilAsync(async () => (await TranscriptAsync(app, parentId, ct)).Any(t => t.Kind == TranscriptKinds.TurnEnd), ct);
            await UntilAsync(async () => {
                using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var seat = await db.Agents.AsNoTracking().SingleAsync(a => a.Slug == options.SpecialistSlug, ct);
                if (!Guid.TryParse(seat.PersistentSessionId, out var sid)) return false;
                var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sid, ct);
                if (session?.Status != SessionStatus.Running) return false;
                approval.ValidateModel(session.AgentKind.ToString(), session.EffectiveModelId ?? "");
                return !await SessionMessageQueueService.IsWorkingAsync(db, sid, ct);
            }, ct);
            // Approval availability must still be fresh immediately before starting paid source work.
            approval.Validate(DateTimeOffset.UtcNow);
            await WriteJsonAsync(Path.Combine(root, "loaded-identities.json"), new {
                approval.ApprovalReference, commit = await GitAsync(AntiphonAppFixture.FindRepositoryRoot(), ct, "rev-parse", "HEAD"),
                server = Binary(typeof(Program).Assembly.Location), runner = Binary(Path.Combine(AppContext.BaseDirectory, "Antiphon.SessionRunner.exe")),
                loadedServerMvid = typeof(Program).Assembly.ManifestModule.ModuleVersionId,
                runnerIdentity = await File.ReadAllTextAsync(Path.Combine(app.OwnedRunnerDirectory, "runner.json"), ct),
                app.BaseAddress, app.OwnedRunnerUrl, app.OwnedRunnerDirectory,
                mode = settings.OutputDistillerMode.ToString(), settings.DistillMinChars, settings.DistillMaxRawChars, settings.OutputDistillerWaitSeconds
            }, ct);
            samples.Add(await RunSourceAsync(app, options, parentId, false, ct));
            samples.Add(await RunSourceAsync(app, options, parentId, true, ct));
            await AssertOneDistillTaskAsync(app, options.SpecialistSlug, ct);
            foreach (var sample in samples)
            {
                if (sample.Task.AgentId is { } agentId)
                {
                    using var scope = app.Services.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<AgentControlService>().StopAsync(agentId, ct);
                }
                var path = sample.Task.WorktreePath;
                path.ShouldNotBeNull();
                Path.GetFullPath(path!).StartsWith(Path.Combine(root, "worktrees") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
                if (Directory.Exists(path)) await GitAsync(options.Repo, ct, "worktree", "remove", "--force", path!);
                (await File.ReadAllTextAsync(sample.Task.ResultFilePath!, ct)).ShouldBe(sample.Task.Result);
            }
            await app.RestartCanaryHostAsync();
            // Two real completion scan opportunities after all work is settled; no interrupted-intent claim.
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
            await AssertOneDistillTaskAsync(app, options.SpecialistSlug, ct);
            var after = await TranscriptAsync(app, parentId, ct);
            foreach (var sample in samples)
            {
                after.Count(t => t.Kind == TranscriptKinds.UserPrompt && (t.Text ?? "").StartsWith(sample.Note.NoteHeader!, StringComparison.Ordinal)).ShouldBe(1);
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var row = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == sample.Task.Id, ct);
                row.ResultFilePath.ShouldBe(sample.Task.ResultFilePath);
                (await File.ReadAllTextAsync(row.ResultFilePath!, ct)).ShouldBe(row.Result);
                (await db.OutputDistillations.CountAsync(d => d.TaskId == row.Id, ct)).ShouldBe(1);
            }
        }
        catch (Exception ex) { failure = ex; throw; }
        finally
        {
            // Export while the real DB/transcript are still accessible, even when acceptance failed.
            try
            {
                if (initialized)
                {
                    using (var evidenceScope = app.Services.CreateScope())
                    {
                        var evidenceDb = evidenceScope.ServiceProvider.GetRequiredService<AppDbContext>();
                        await WriteJsonAsync(Path.Combine(root, "ledger.json"),
                            await evidenceDb.OutputDistillations.AsNoTracking().ToListAsync(), CancellationToken.None);
                        await WriteJsonAsync(Path.Combine(root, "task-results.json"),
                            await evidenceDb.AgentTasks.AsNoTracking().Select(t => new {
                                t.Id, t.Role, t.Status, t.ParentSessionId, t.AgentSessionId, t.Result,
                                t.ResultFilePath, t.FailureCode, t.WorktreePath, t.CostUsd
                            }).ToListAsync(), CancellationToken.None);
                    }
                    // Never serialize task launch environments, tokens or provider configuration.
                    await WriteJsonAsync(Path.Combine(root, "samples.json"), samples.Select(s => new {
                        s.Task.Id, s.Task.ParentSessionId, s.Task.AgentSessionId, s.Task.WorktreePath,
                        s.Task.Result, s.Task.DistilledResult, s.Task.ResultFilePath, s.Note, s.Ledger,
                        s.Prompt, s.ReadHash, s.ReadLength, s.ToolEvidence
                    }), CancellationToken.None);
                    if (parentId != Guid.Empty) await WriteJsonAsync(Path.Combine(root, "parent-transcript.json"),
                        await TranscriptAsync(app, parentId, CancellationToken.None), CancellationToken.None);
                    foreach (var id in agentIds)
                    {
                        using var scope = app.Services.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<AgentControlService>().StopAsync(id, CancellationToken.None);
                    }
                }
            }
            finally
            {
                try { await app.DisposeAsync(); }
                finally { await WriteJsonAsync(Path.Combine(root, "teardown.json"), new { app.RunnerCensusCompleted, app.SessionLeakVerdict,
                    failure = failure?.Message }, CancellationToken.None); }
            }
        }
        app.RunnerCensusCompleted.ShouldBeTrue(); app.SessionLeakVerdict.ShouldBeNull();
        var positive = samples[0];
        new DistillerCanaryEvidence(positive.Task.Id, parentId, positive.Note.Id, positive.Ledger.TaskId,
            positive.Ledger.QueuedMessageId!.Value, positive.Prompt.AgentSessionId, positive.Task.Result!, positive.Note.ContentDigest!,
            positive.Task.ResultFilePath!, await File.ReadAllTextAsync(positive.Task.ResultFilePath!, ct), positive.Task.DistilledResult!,
            positive.Note.NoteHeader!, positive.Ledger.Mode.ToString(), positive.Ledger.Outcome.ToString(),
            positive.Ledger.DeadlineAt!.Value, positive.Ledger.DecisionAt!.Value, positive.Prompt.Kind, positive.Prompt.Sequence,
            positive.Prompt.Timestamp ?? positive.Prompt.CreatedAt, positive.Prompt.Text!, positive.Note.DeliveryVerdict!.Value.ToString(),
            positive.Ledger.AvailabilityKind!.Value.ToString(), positive.Ledger.AvailabilityAlias!, approval.ExpectedDistillerModelAlias,
            positive.ReadHash, positive.ReadLength, positive.ToolEvidence, true, 1, 1, app.RunnerCensusCompleted)
            .Validate(Middle, ["next: review", "handoff: Review the canary evidence.", "--- deliverable ---"]);
        var evidencePath = Path.Combine(AntiphonAppFixture.FindRepositoryRoot(), "docs", "investigations",
            $"{DateTimeOffset.UtcNow:yyyy-MM-dd}-card-0419-apply-acceptance.md");
        await File.WriteAllTextAsync(evidencePath,
            $"# CARD-0419 isolated Apply acceptance\n\nPassed at {DateTimeOffset.UtcNow:O}. Approval: {approval.ApprovalReference}.\n\n"
            + $"Parent `{parentId}`; retained evidence `{root}` (loaded binary hashes/MVID, runner PID/start time, transcripts, ledger and teardown).\n\n"
            + string.Join("\n", samples.Select(s => $"- Source `{s.Task.Id}`, note `{s.Note.Id}`, ledger `{s.Ledger.Id}`: {s.Ledger.Outcome}; "
                + $"raw {s.Task.Result!.Length} UTF-16 characters / {Encoding.UTF8.GetByteCount(s.Task.Result)} UTF-8 bytes; "
                + $"SHA-256 `{s.ReadHash}`; file `{s.Task.ResultFilePath}`; UserPrompt sequence {s.Prompt.Sequence}; "
                + $"source cost ${s.Task.CostUsd}, distiller observed cost ${s.Ledger.CostUsd}."))
            + "\n\nBoth parents' file-read acknowledgements matched. Files survived worktree removal and host restart; no duplicate completion or specialist task. "
            + "Owned runner census completed without leaks. Production was not changed. CARD-0392 interrupted-request recovery and human rollout/fallback acceptance remain separate release gates.\n", ct);
    }

    private static async Task AssertOneDistillTaskAsync(AntiphonAppFixture app, string slug, CancellationToken ct)
    {
        using var scope = app.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AgentTasks.CountAsync(
            t => t.Role == AgentTaskRole.Distill && t.AgentName == slug, ct)).ShouldBe(1);
    }

    private static async Task<Sample> RunSourceAsync(AntiphonAppFixture app, DistillerCanaryOptions options, Guid parent, bool longReport, CancellationToken ct)
    {
        Guid taskId;
        using (var scope = app.Services.CreateScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(new(
                $"Read {(longReport ? "long-input.txt" : "short-input.txt")} and emit its complete content as your final response, then the real report token from this brief. "
                + "Do not shorten, pre-spill, write files, commit, push, or dispatch. Retain the stage block. This explicitly overrides advice to pre-spill long reports.",
                Title: "canary full report", Role: AgentTaskRole.Review, AgentKind: AgentKind.ClaudeCode, Workspace: WorkspaceMode.Worktree,
                WorkingDirectory: options.Repo), new(null, parent, options.Repo), ct);
            taskId = created.Id;
        }
        AgentTask? task = null; SessionQueuedMessage? note = null; OutputDistillationRecord? ledger = null;
        await UntilAsync(async () => {
            using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
            if (task.Status is AgentTaskStatus.Failed or AgentTaskStatus.Blocked or AgentTaskStatus.Canceled)
                throw new InvalidOperationException("Source did not succeed: " + task.Status);
            note = await db.SessionQueuedMessages.AsNoTracking().SingleOrDefaultAsync(m => m.SourceTaskId == taskId && m.ContentDigest != null, ct);
            ledger = await db.OutputDistillations.AsNoTracking().SingleOrDefaultAsync(d => d.TaskId == taskId, ct);
            if (ledger is not null && ledger.Outcome != (longReport ? DistillationOutcome.SkippedLong : DistillationOutcome.Applied))
                throw new InvalidOperationException($"Canary fallback is not acceptance: {ledger.Outcome}/{ledger.Reason}");
            return task.Status == AgentTaskStatus.Succeeded && ledger is not null && note?.DeliveryVerdict is DeliveryVerdict.Delivered or DeliveryVerdict.LateConfirmed;
        }, ct);
        task!.ReplyTo.ShouldBe(AgentTaskReplyTo.Session); task.ResultFilePath.ShouldNotBeNull();
        task.Result!.Length.ShouldBeGreaterThanOrEqualTo(longReport ? 20001 : 4000);
        if (!longReport) task.Result.Length.ShouldBeLessThanOrEqualTo(14400);
        (await File.ReadAllTextAsync(task.ResultFilePath!, ct)).ShouldBe(task.Result);
        var transcript = await TranscriptAsync(app, parent, ct);
        var prompt = transcript.Where(t => t.Kind == TranscriptKinds.UserPrompt && t.Text == note!.Body).ShouldHaveSingleItem();
        prompt.Text!.ShouldContain(task.ResultFilePath!); prompt.Text.ShouldNotContain(Middle);
        if (longReport) { prompt.Text.ShouldContain("THIS REPORT IS AN EXCERPT"); ledger!.DistillTaskId.ShouldBeNull(); }
        else { ledger!.DistillTaskId.ShouldNotBeNull(); prompt.Text.ShouldContain(task.DistilledResult!); }
        var baseline = transcript.Max(t => t.Sequence);
        await app.Services.GetRequiredService<SessionMessageQueueService>().EnqueueAsync(parent,
            "Read the exact UTF-8 file at the following JSON-encoded path using your filesystem tools: " + JsonSerializer.Serialize(task.ResultFilePath)
            + ". Compute its .NET UTF-16 string length and SHA-256 over its exact UTF-8 bytes. Return CANARY_FILE_READ <length> <sha256> only. Do not poll the task API.",
            MessageSendMode.WhenIdle, ct);
        Match? ack = null; List<TranscriptEntry> read = [];
        await UntilAsync(async () => {
            read = (await TranscriptAsync(app, parent, ct)).Where(t => t.Sequence > baseline).ToList();
            ack = Regex.Match(string.Join("\n", read.Where(t => t.Kind == TranscriptKinds.AssistantText).Select(t => t.Text)),
                @"CANARY_FILE_READ\s+(\d+)\s+([a-fA-F0-9]{64})");
            return ack.Success && read.Any(t => t.Kind == TranscriptKinds.TurnEnd);
        }, ct);
        var hash = Hash(Encoding.UTF8.GetBytes(task.Result));
        ack!.Groups[1].Value.ShouldBe(task.Result.Length.ToString()); ack.Groups[2].Value.ToLowerInvariant().ShouldBe(hash);
        var fileCalls = read.Where(t => t.Kind == TranscriptKinds.ToolCall && t.ToolUseId is not null
            && (t.ToolInput ?? "").Contains(Path.GetFileName(task.ResultFilePath!), StringComparison.OrdinalIgnoreCase))
            .Select(t => t.ToolUseId).ToHashSet();
        var tool = read.Any(t => t.Kind == TranscriptKinds.ToolResult && fileCalls.Contains(t.ToolUseId)
            && t.ToolIsError != true && (t.Text ?? "").Contains(hash, StringComparison.OrdinalIgnoreCase));
        tool.ShouldBeTrue("the file-naming tool call and its successful result must independently show the file hash");
        using (var scope = app.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct))
                .LastPolledResultHash.ShouldBeNull();
        return new(task, note!, ledger!, prompt, hash, task.Result.Length, tool);
    }
    private sealed record Sample(AgentTask Task, SessionQueuedMessage Note, OutputDistillationRecord Ledger,
        TranscriptEntry Prompt, string ReadHash, int ReadLength, bool ToolEvidence);
    private static string Report(int length)
    {
        var head = "Completed the canary report.\n";
        var tail = "\nCaveat: operator review is still required.\n--- next stage ---\nnext: review\nhandoff: Review the canary evidence.\nartifact: docs/canary-evidence.md";
        var padding = string.Concat(Enumerable.Repeat("Ordinary explanatory prose supports the conclusion. ", length / 30));
        var first = head + padding[..(length / 2)] + "\n" + Middle + "\n";
        return first + padding[..(length - first.Length - tail.Length)] + tail;
    }
    private static async Task<List<TranscriptEntry>> TranscriptAsync(AntiphonAppFixture app, Guid session, CancellationToken ct)
    {
        // Pull the documented server transcript front door to trigger normal persisted sync.
        using var response = await app.HttpClient.GetAsync($"/api/sessions/{session}/transcript?since=0", ct);
        response.EnsureSuccessStatusCode();
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == session).OrderBy(t => t.Sequence).ToListAsync(ct);
    }
    private static async Task UntilAsync(Func<Task<bool>> check, CancellationToken ct)
    { while (!await check()) await Task.Delay(500, ct); }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static object Binary(string path) => new { path, hash = Hash(File.ReadAllBytes(path)),
        informationalVersion = FileVersionInfo.GetVersionInfo(path).ProductVersion };
    private static Task WriteJsonAsync(string path, object value, CancellationToken ct) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), ct);
    private static async Task<string> GitAsync(string cwd, CancellationToken ct, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi)!; var stdout = p.StandardOutput.ReadToEndAsync(ct); var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct); p.ExitCode.ShouldBe(0, await stderr); return (await stdout).Trim();
    }
}
