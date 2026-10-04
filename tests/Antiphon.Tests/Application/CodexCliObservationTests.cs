using System.Net;
using System.Text;
using Antiphon.Agents.Pty;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Antiphon.Server.Infrastructure.Git;
using System.Reflection;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[NotInParallel(["MessageQueue", "AgentQueue"])]
public sealed class CodexCliObservationTests
{
    [Test]
    public void C959_Ladder_and_exact_models_share_floor()
    {
        var metadata = typeof(ModelLevelAliases).GetMethod("MinimumCodexCliVersion", BindingFlags.Public | BindingFlags.Static);
        string? Floor(AgentKind kind, string? model) => metadata?.Invoke(null, [kind, model])?.ToString();
        foreach (var (level, model, floor) in new[]
        {
            (AgentModelLevel.High, "gpt-6.1-sol", "0.159.1"),
            (AgentModelLevel.Medium, "gpt-6.1-sol", "0.159.1"),
            ((AgentModelLevel)999, "gpt-6.1-sol", "0.159.1"),
            (AgentModelLevel.Frontier, "gpt-6-astra", (string?)null),
            (AgentModelLevel.Low, "gpt-5.6-luna", (string?)null),
        })
        {
            var entry = ModelLevelAliases.ForCodexEntry(level);
            entry.ModelId.ShouldBe(model, "C959-pc-126 entry " + level);
            (entry.MinimumCliVersion?.ToString()).ShouldBe(floor, "C959-pc-127 entry " + level);
            ModelLevelAliases.ForCodex(level).ShouldBe(model, "C959-v14-existing-alias " + level);
            Floor(AgentKind.Codex, model).ShouldBe(floor, "C959-v14-floor " + level);
        }
        Floor(AgentKind.Codex, "GPT-6.1-SOL").ShouldBe("0.159.1", "C959-v14-exact-canonical");
        foreach (var model in new[] { "gpt-6-sol", "gpt-5.6-terra", "gpt-5.6-sol", "gpt-6-astra",
                     "gpt-5.6-luna", "wrapper-owned", "", null })
            Floor(AgentKind.Codex, model).ShouldBeNull("C959-v14-unrelated " + model);
        foreach (var kind in new[] { AgentKind.ClaudeCode, AgentKind.Grok, AgentKind.Raw })
            Floor(kind, "gpt-6.1-sol").ShouldBeNull("C959-v14-other-kind " + kind);
    }

    private static readonly DateTimeOffset T = DateTimeOffset.Parse("2026-10-03T12:00:00Z");
    private const string Body = "C959 delivery α\nsecond line\nEND-C959";

    [Test]
    public void C959_Launcher_descriptors_are_observations()
    {
        var revision = Guid.NewGuid();
        var descriptor = CodexCliProbeDescriptor.FromSpec("runner-a", "gpt-6.1-sol", revision,
            "/fixture/node", ["package/codex.js", "--model", "gpt-6.1-sol"],
            new Dictionary<string, string> { ["PATH"] = "B:A", ["PATHEXT"] = ".CMD;.EXE" }, "/fixture/cwd");
        descriptor.Request!.Executable.ShouldBe("/fixture/node", "C959-pc-153");
        descriptor.Request.CodexJsPrefix.ShouldBe("package/codex.js", "C959-pc-206");
        descriptor.Request.Path.ShouldBe("B:A", "C959-pc-156");
        descriptor.Request.PathExt.ShouldBe(".CMD;.EXE", "C959-pc-157");
        descriptor.Request.ResolutionCwd.ShouldBe("/fixture/cwd", "C959-pc-205");
        var policy = new PhoneHomeLaunchPolicy(Options.Create(new PhoneHomeRunnerSettings {
            Enabled = true, AllowedRunnerId = "runner-a", HostWorkspaceRoot = "/host/repo",
            RunnerWorkspace = "/runner/workspace", RunnerRepository = "/runner/repo",
            CallbackOrigin = "https://antiphon.test", AllowDelegatedTasks = true,
        }));
        var remote = policy.Project(new AgentLaunchSpec("codex", AgentKind.Codex, @"C:\tools\codex.cmd",
            ["--model", "gpt-6.1-sol"], new Dictionary<string, string> { ["PATH"] = "B:A" }, "/host/repo", 80, 24),
            new Agent { Id = Guid.NewGuid(), Kind = AgentKind.Codex, RunnerId = "runner-a", WorkingDirectory = "/host/repo" },
            "/runner/worktrees/selected");
        var projected = CodexCliProbeDescriptor.FromSpec("runner-a", "gpt-6.1-sol", null,
            remote.Exe, remote.Args, remote.Env, remote.Cwd);
        projected.Request!.Executable.ShouldBe("codex", "C959-pc-159");
        projected.Request.ResolutionCwd.ShouldBe("/runner/worktrees/selected", "C959-pc-159 cwd");
        foreach (var unsafeValue in new[] { "NUL\0sentinel", "{{key:C959}}", "${secret:C959}", new string('X', 32769) })
            CodexCliProbeDescriptor.FromSpec("desktop", "gpt-6.1-sol", null, "/fixture/codex", [],
                new Dictionary<string,string> { ["PATH"] = unsafeValue }, "/fixture").Request
                .ShouldBeNull("C959-descriptor-unrepresentable-input");
        foreach (var loader in new[] { "NODE_OPTIONS", "NODE_PATH", "LD_PRELOAD", "LD_LIBRARY_PATH", "DOTNET_STARTUP_HOOKS" })
        {
            var unverified = CodexCliProbeDescriptor.FromSpec("desktop", "gpt-6.1-sol", null,
                "/fixture/codex", [], new Dictionary<string, string> { [loader] = "C959-loader" }, "/fixture");
            unverified.Request.ShouldBeNull("C959-pc-073 " + loader);
            unverified.Error.ShouldBe("launcher_unverified", "C959-pc-073 " + loader);
        }
    }

    [Test]
    public async Task C959_Compatible_launch_keeps_model_and_runner()
    {
        // Claude is first: its measured paste path must retain the literal LF body.
        foreach (var kind in new[] { AgentKind.ClaudeCode, AgentKind.Codex })
        foreach (var busy in new[] { false, true })
        foreach (var worktree in kind == AgentKind.Codex ? new[] { true, false } : new[] { false })
        foreach (var version in kind == AgentKind.Codex ? new[] { "0.159.1", "0.160.0" } : new[] { "0.160.0" })
        {
            var vector = $"{kind}/busy={busy}/worktree={worktree}/{version}";
            using var git = worktree ? new ScratchGitRepo("c959-launch") : null;
            if (git is not null) await git.CommitFileAsync("seed.txt", "C959 isolated worktree\n");
            await using var k = await DispatchKit.BuildAsync(git, kind, busy);
            k.Client.Sample = new(version, DateTimeOffset.UtcNow, null, new string('a',64));
            var task = await k.CreateAsync();
            await k.TickAsync();
            k.Factory.ReadyHold?.TrySetResult(true);
            await k.Harness.Provider.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
            k.Factory.Created.Count.ShouldBe(1, "C959-pc-190");
            var adapter = k.Factory.Created.Single();
            await using var db = k.Context();
            var row = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            var queued = (await db.SessionQueuedMessages.AsNoTracking().SingleOrDefaultAsync(q => q.ExecutionTaskId == task.Id))
                .ShouldNotBeNull("C959-pc-186 durable handoff exists");
            var frozen = k.Boundary.Briefs[task.Id];
            frozen.Full.ShouldContain(Body, customMessage: "C959-v21-oracle-literal " + vector);
            frozen.Full.ShouldNotContain("\r", customMessage: "C959-v21-oracle-LF " + vector);
            var expected = frozen.Wire;
            if (busy)
            {
                adapter.SubmittedBodies.ShouldBeEmpty("C959-pc-188 busy recipient " + vector);
                queued.Status.ShouldBe(QueuedMessageStatus.Pending, "C959-v21-busy-persisted " + vector);
                await k.Harness.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn", sessionId: row.AgentSessionId);
                await k.Harness.Queue.FlushSessionAsync(row.AgentSessionId!.Value, CancellationToken.None);
            }
            if (kind == AgentKind.ClaudeCode)
            {
                var bodyWrite = adapter.Inputs.Single(i => i.Contains("[antiphon-task:"));
                bodyWrite.ShouldNotContain("\r", customMessage: "C959-pc-215 " + vector);
                var index = adapter.Inputs.ToList().IndexOf(bodyWrite);
                adapter.Inputs[index + 1].ShouldBe("\r", "C959-pc-215 " + vector);
                adapter.SubmittedBodies.Single().ShouldBe(expected, "C959-pc-187 " + vector);
                bodyWrite.ShouldBe("\u001b[200~" + expected + "\u001b[201~", "C959-pc-214 " + vector);
                adapter.SubmittedBodies.Single().ShouldContain(Body, customMessage: "C959-v21-claude-lf " + vector);
                k.Client.Requests.ShouldBeEmpty("C959-v21-ungated-kind " + vector);
            }
            else
            {
                k.Harness.Provider.GetRequiredService<PtyDeliveryProfile>().Ceilings.ForAgentKind(kind)
                    .BriefInlineMaxBytes.ShouldBe(0, "C959-pc-209 " + vector);
                File.Exists(frozen.Path).ShouldBeTrue("C959-v21-spill-exists " + vector);
                var bytes = await File.ReadAllBytesAsync(frozen.Path!);
                bytes.ShouldBe(Encoding.UTF8.GetBytes(frozen.Full), "C959-pc-210 " + vector);
                Encoding.UTF8.GetString(bytes).ShouldContain(Body, customMessage: "C959-pc-187 " + vector);
                expected.ShouldNotContain("\n", customMessage: "C959-pc-212 " + vector);
                expected.ShouldNotContain("\r", customMessage: "C959-pc-212 " + vector);
                adapter.SubmittedBodies.Single().ShouldBe(expected, "C959-v21-pointer " + vector);
                var args = adapter.StartedArgs.ToList();
                args.Count(a => a == "--model").ShouldBe(1, "C959-pc-184 " + vector);
                args[args.IndexOf("--model") + 1].ShouldBe("gpt-6.1-sol", "C959-pc-184 " + vector);
            }
            k.Client.Requests.ShouldBeEmpty("C959-inert-delivery " + vector);
            row.RunnerId.ShouldBeNull("C959-pc-185 " + vector);
            queued.Body.ShouldBe(expected, "C959-pc-186 " + vector);
            var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == row.AgentSessionId);
            queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == queued.Id);
            queued.LastDeliveryGeneration.ShouldBe(SessionGeneration.Normalize(session.StartedAt), "C959-v21-generation " + vector);
            var receipts = await db.TranscriptEntries.AsNoTracking().Where(e => e.AgentSessionId == row.AgentSessionId && e.Kind == TranscriptKinds.UserPrompt).ToListAsync();
            receipts.Count.ShouldBe(1, "C959-v21-no-duplicate " + vector);
            receipts.Single().Text.ShouldBe(expected, "C959-v21-receipt " + vector);
            receipts.Single().Sequence.ShouldBeGreaterThan(queued.LastDeliveryBaselineSequence ?? 0, "C959-v21-baseline " + vector);
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning && e.Detail!.Contains("codex_cli_version"))).ShouldBe(0, "C959-no-cli-warning " + vector);
        }
        foreach (var version in new[] { "0.159.1", "0.160.0" })
        foreach (var busy in new[] { false, true })
            await CodexCliRemoteDeliveryFixture.RunAsync(Body, version, busy);
        foreach (var negative in new[] { "ack-only", "clipped", "other-session" })
            await CodexCliRemoteDeliveryFixture.RunAsync(Body,
                new RunnerCodexCliVersionDto("0.156.1", DateTimeOffset.UtcNow, null, new string('a',64)),
                busy: true, negative: negative);
    }

    [Test]
    public async Task C959_Retry_and_cancellation_recheck()
    {
        foreach (var version in new[] { "0.156.1", "unknown" })
        foreach (var busy in new[] { false, true })
        foreach (var worktree in new[] { false, true })
        {
            var vector = $"lost-claim/{version}/busy={busy}/worktree={worktree}";
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            using var git = worktree ? new ScratchGitRepo("c959-recovery") : null;
            if (git is not null) await git.CommitFileAsync("seed.txt", "C959 isolated recovery\n");
            using var crash = new CancellationTokenSource();
            var clock = new RecoveryClock();
            var roots = new List<string>();
            try
            {
                Guid taskId, lostSession, helperSession, helperAgent;
                await using (var lost = await DispatchKit.BuildAsync(git, schema: schema, clock: clock,
                    preserve: true, cancelAfterClaim: crash))
                {
                    roots.Add(lost.Harness.TempRoot);
                    taskId = (await lost.CreateAsync()).Id;
                    await Should.ThrowAsync<OperationCanceledException>(() => lost.TickAsync(crash.Token));
                    await using var db = lost.Context();
                    var claimed = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
                    claimed.Status.ShouldBe(AgentTaskStatus.Dispatched, "C959-v22-lost-committed " + vector);
                    lostSession = claimed.AgentSessionId!.Value;
                    lost.Factory.Created.ShouldBeEmpty("C959-v22-no-volatile-launch " + vector);
                    (await db.SessionQueuedMessages.CountAsync(q => q.ExecutionTaskId == taskId)).ShouldBe(0);
                    (await db.TranscriptEntries.CountAsync(e => e.AgentSessionId == lostSession)).ShouldBe(0,
                        "C959-v22-lost-claim-no-receipt " + vector);
                    helperSession = lost.Harness.SessionId;
                    helperAgent = lost.Harness.AgentId;
                }
                // The first provider is disposed. Only durable database facts cross this boundary.
                await using var recovered = await DispatchKit.BuildAsync(git, busy: busy, schema: schema,
                    clock: clock, preserve: true, attachSession: helperSession, attachAgent: helperAgent);
                roots.Add(recovered.Harness.TempRoot);
                clock.Advance(TimeSpan.FromMinutes(recovered.Harness.Delegation.DeliveryFailTimeoutMinutes + 1));
                using (var scope = recovered.Harness.Provider.CreateScope())
                    (await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().FailNeverStartedAsync(CancellationToken.None))
                        .ShouldBe(1, "C959-pc-221 " + vector);
                await using (var db = recovered.Context())
                {
                    var failed = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
                    failed.Status.ShouldBe(AgentTaskStatus.Failed, "C959-pc-221 durable " + vector);
                    failed.FailureReason.ShouldContain("Boot prompt was never delivered", customMessage: "C959-pc-221 reason " + vector);
                    (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Failed))
                        .ShouldBe(1, "C959-pc-221 event " + vector);
                }
                async Task<AgentTaskSummaryDto> Retry()
                {
                    using var scope = recovered.Harness.Provider.CreateScope();
                    return await scope.ServiceProvider.GetRequiredService<AgentTaskService>().RetryAsync(taskId, CancellationToken.None);
                }
                recovered.Client.Sample = version == "unknown" ? null
                    : new(version, clock.GetUtcNow(), null, new string('a',64));
                recovered.Client.Requests.Clear();
                (await Retry()).Status.ShouldBe(AgentTaskStatus.Queued, "C959-v22-explicit-retry " + vector);
                recovered.Client.Requests.ShouldBeEmpty("C959-pc-238 " + vector);
                await recovered.TickAsync();
                recovered.Factory.ReadyHold?.TrySetResult(true);
                await recovered.Harness.Provider.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
                await using var finalDb = recovered.Context();
                var row = await finalDb.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
                row.AgentSessionId.ShouldNotBe(lostSession, "C959-v22-retry-session " + vector);
                var adapter = recovered.Factory.Created.Single();
                if (busy)
                {
                    adapter.SubmittedBodies.ShouldBeEmpty("C959-v22-busy " + vector);
                    await recovered.Harness.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn", sessionId: row.AgentSessionId);
                    await recovered.Harness.Queue.FlushSessionAsync(row.AgentSessionId!.Value, CancellationToken.None);
                }
                var frozen = recovered.Boundary.Briefs[taskId];
                frozen.Full.ShouldContain(Body, customMessage: "C959-v22-literal " + vector);
                frozen.Full.ShouldNotContain("\r");
                (await File.ReadAllBytesAsync(frozen.Path!)).ShouldBe(Encoding.UTF8.GetBytes(frozen.Full), "C959-pc-194 spill " + vector);
                var args = adapter.StartedArgs.ToList();
                args.Count(a => a == "--model").ShouldBe(1, "C959-v22-model " + vector);
                args[args.IndexOf("--model") + 1].ShouldBe("gpt-6.1-sol", "C959-v22-model " + vector);
                adapter.SubmittedBodies.Single().ShouldBe(frozen.Wire, "C959-pc-194 recipient " + vector);
                var queued = await finalDb.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.ExecutionTaskId == taskId);
                var session = await finalDb.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == row.AgentSessionId);
                queued.LastDeliveryGeneration.ShouldBe(SessionGeneration.Normalize(session.StartedAt), "C959-v22-generation " + vector);
                var receipts = await finalDb.TranscriptEntries.AsNoTracking().Where(e => e.AgentSessionId == row.AgentSessionId && e.Kind == TranscriptKinds.UserPrompt).ToListAsync();
                receipts.Count.ShouldBe(1, "C959-v22-no-duplicate " + vector);
                receipts.Single().Text.ShouldBe(frozen.Wire, "C959-pc-194 receipt " + vector);
                receipts.Single().Sequence.ShouldBeGreaterThan(queued.LastDeliveryBaselineSequence ?? 0, "C959-v22-baseline " + vector);
                (await finalDb.TranscriptEntries.CountAsync(e => e.AgentSessionId == lostSession && e.Kind == TranscriptKinds.UserPrompt)).ShouldBe(0,
                    "C959-v22-old-session " + vector);
                (await finalDb.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Retried)).ShouldBe(1);
            }
            finally
            {
                foreach (var root in roots)
                    if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, recursive: true);
            }
        }
        using var cancel = new CancellationTokenSource();
        await using var k = await Kit.CreateAsync(cancel);
        await Should.ThrowAsync<OperationCanceledException>(() => k.Service.CreateAsync(k.Request(false), k.Caller, cancel.Token));
        (await k.Db.AgentTasks.AsNoTracking().CountAsync()).ShouldBe(0, "C959-v22-save-cancellation-no-insert");
        k.Local.Requests.ShouldBeEmpty("C959-v22-cancellation-no-probe");
    }

    [Test]
    public async Task C959_Probe_failure_never_refuses()
    {
        foreach (var error in new[] { "timeout", "nonzero_exit", "cleanup_unconfirmed" })
            await NeverRefusesAsync(now => new(null, now, error, new string('a', 64)), 225, error);
    }

    [Test]
    public async Task C959_Stale_sample_never_refuses()
    {
        foreach (var age in new[] { TimeSpan.FromMinutes(15) + TimeSpan.FromTicks(1), TimeSpan.FromMinutes(16) })
            await NeverRefusesAsync(now => new("0.160.0", now - age, null, new string('a', 64)), 228, age.ToString());
    }

    [Test]
    public async Task C959_Unknown_version_never_refuses()
    {
        foreach (var (name, sample) in new (string, Func<DateTimeOffset, RunnerCodexCliVersionDto?>)[]
        {
            ("legacy", _ => null),
            ("null-version", now => new(null, now, null, new string('a', 64))),
            ("malformed-version", now => new("banana", now, null, new string('a', 64))),
            ("missing-time", _ => new("0.160.0", null, null, new string('a', 64))),
            ("fingerprint", now => new("0.160.0", now, null, "bad")),
            ("clock-skew", now => new("0.160.0", now.AddMinutes(2), null, new string('a', 64))),
        }) await NeverRefusesAsync(sample, 231, name);
    }

    [Test]
    public async Task C959_Old_version_with_floor_never_refuses()
    {
        foreach (var version in new[] { "0.156.1", "0.159.0", "0.159.1-beta.1" })
        {
            await NeverRefusesAsync(now => new(version, now, null, new string('a', 64)), 234, version);
            foreach (var (level, exact, expected) in new[] {
                (AgentModelLevel.Medium, (string?)null, "gpt-6.1-sol"),
                (AgentModelLevel.Frontier, (string?)null, "gpt-6-astra"),
                (AgentModelLevel.Low, (string?)null, "gpt-5.6-luna"),
                (AgentModelLevel.High, "gpt-6.1-sol", "gpt-6.1-sol"),
            })
            {
                await using var dispatch = await DispatchKit.BuildAsync();
                dispatch.Client.Sample = new(version, DateTimeOffset.UtcNow, null, new string('a',64));
                AgentTaskCreatedDto? created = null;
                Exception? failure = null;
                try { created = await dispatch.CreateAsync(level, exact); await dispatch.TickAsync(); }
                catch (Exception ex) { failure = ex; }
                failure.ShouldBeNull($"C959-old-model-selection/{version}/{level}/{exact}");
                await AssertDeliveryAsync(dispatch, created!.Id, $"C959-old-model-selection/{version}/{level}/{exact}", expected);
                dispatch.Client.Requests.ShouldBeEmpty("C959-old-model-no-probe");
                if (exact is null)
                    foreach (var busy in new[] { false, true })
                        await CodexCliRemoteDeliveryFixture.RunAsync(Body,
                            new RunnerCodexCliVersionDto(version, DateTimeOffset.UtcNow, null, new string('a',64)), busy, level);
            }
        }
    }

    private static async Task NeverRefusesAsync(Func<DateTimeOffset, RunnerCodexCliVersionDto?> sample, int guard, string vector)
    {
        foreach (var remote in new[] { false, true })
        {
            await using var create = await Kit.CreateAsync();
            var selected = remote ? create.Remote : create.Local;
            selected.Sample = sample(create.Clock.GetUtcNow());
            AgentTaskCreatedDto? result = null;
            Exception? failure = null;
            try { result = await create.Service.CreateAsync(create.Request(remote), create.Caller, CancellationToken.None); }
            catch (Exception ex) { failure = ex; }
            failure.ShouldBeNull((guard switch { 225 => "C959-pc-225", 228 => "C959-pc-228", 231 => "C959-pc-231", _ => "C959-pc-234" })
                + $" create/{remote}/{vector}");
            var row = await create.Db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == result!.Id);
            row.Status.ShouldBe(AgentTaskStatus.Queued, $"C959-pc-{guard} persisted/{vector}");
            row.RunnerId.ShouldBe(remote ? "runner-a" : null, $"C959-pc-185 create/{vector}");
            selected.Requests.ShouldBeEmpty($"C959-pc-237 create/{vector}");
            var advertised = await selected.GetCapabilitiesAsync(CancellationToken.None);
            advertised!.CodexCliVersion.ShouldBe(selected.Sample?.CodexCliVersion, $"C959-advertised-sample/{vector}");
            await using var observed = await PhoneHomeTestHost.StartAsync(create.Clock);
            observed.Local.Capabilities = advertised;
            await using var peer = await observed.ConnectPeerAsync(capabilities: advertised);
            observed.Directory.MarkRecovered(await observed.WaitLiveAsync());
            using var list = await observed.Http.GetAsync("/api/session-runners");
            list.EnsureSuccessStatusCode();
            var rows = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement;
            var status = JsonSerializer.SerializeToElement(observed.Directory.Status(observed.AllowedRunnerId),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var error = guard == 225 ? vector : vector == "fingerprint" ? "launcher_mismatch"
                : vector == "clock-skew" ? "clock_skew" : null;
            bool? stale = guard == 228 ? true : guard == 234 ? false : null;
            foreach (var shape in rows.EnumerateArray().Append(status))
            {
                shape.GetProperty("codexCliVersion").GetString().ShouldBe(selected.Sample?.CodexCliVersion, "C959-observed-version " + vector);
                shape.GetProperty("codexCliVersionError").GetString().ShouldBe(error, "C959-observed-error " + vector);
                var staleValue = shape.GetProperty("codexCliVersionStale");
                (staleValue.ValueKind == JsonValueKind.Null ? (bool?)null : staleValue.GetBoolean())
                    .ShouldBe(stale, "C959-observed-freshness " + vector);
            }
        }
        // A separate schema for cold dispatch: change the observation after create.
        using var coldGit = guard == 234 ? new ScratchGitRepo("c959-old-cold") : null;
        if (coldGit is not null) await coldGit.CommitFileAsync("seed.txt", "C959 old observation, cold worktree\n");
        await using (var dispatch = await DispatchKit.BuildAsync(coldGit))
        {
            var created = await dispatch.CreateAsync();
            dispatch.Client.Requests.Clear();
            dispatch.Client.Sample = sample(DateTimeOffset.UtcNow);
            Exception? failure = null;
            try { await dispatch.TickAsync(); } catch (Exception ex) { failure = ex; }
            failure.ShouldBeNull($"C959-pc-{guard + 2} dispatch/{vector}");
            await AssertDeliveryAsync(dispatch, created.Id,
                (guard switch { 225 => "C959-pc-227", 228 => "C959-pc-230", 231 => "C959-pc-233", _ => "C959-pc-236" }) + "/" + vector);
            dispatch.Client.Requests.ShouldBeEmpty($"C959-pc-239 cold and C959-pc-253 final/{vector}");
            if (coldGit is not null)
            {
                dispatch.Factory.Created.Single().StartedCwd.ShouldNotBe(coldGit.Path, "C959-pc-253 final cwd changed");
                System.IO.Directory.Exists(dispatch.Factory.Created.Single().StartedCwd).ShouldBeTrue("C959-pc-253 actual worktree");
            }
        }
        // The failed task comes from the actual watchdog after a committed, lost launch.
        var clock = new RecoveryClock();
        using var crash = new CancellationTokenSource();
        await using var retry = await DispatchKit.BuildAsync(clock: clock, cancelAfterClaim: crash);
        var first = await retry.CreateAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => retry.TickAsync(crash.Token));
        clock.Advance(TimeSpan.FromMinutes(retry.Harness.Delegation.DeliveryFailTimeoutMinutes + 1));
        using (var scope = retry.Harness.Provider.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().FailNeverStartedAsync(CancellationToken.None))
                .ShouldBe(1, $"C959-pc-221/{vector}");
        await using (var db = retry.Context())
            (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == first.Id)).Status.ShouldBe(AgentTaskStatus.Failed);
        retry.Client.Sample = sample(clock.GetUtcNow());
        retry.Client.Requests.Clear();
        retry.Boundary.CancelAfterClaim = null;
        retry.Boundary.Briefs.Clear();
        Exception? retryFailure = null;
        try
        {
            using var scope = retry.Harness.Provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskService>().RetryAsync(first.Id, CancellationToken.None);
        }
        catch (Exception ex) { retryFailure = ex; }
        retryFailure.ShouldBeNull((guard switch { 225 => "C959-pc-226", 228 => "C959-pc-229", 231 => "C959-pc-232", _ => "C959-pc-235" })
            + $" retry/{vector}");
        await using (var db = retry.Context())
        {
            (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == first.Id)).Status
                .ShouldBe(AgentTaskStatus.Queued, $"C959-pc-{guard + 1} durable/{vector}");
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == first.Id && e.Type == AgentTaskEventType.Retried))
                .ShouldBe(1, $"C959-pc-{guard + 1} event/{vector}");
        }
        retry.Client.Requests.ShouldBeEmpty($"C959-pc-238 retry/{vector}");
        await retry.TickAsync();
        await AssertDeliveryAsync(retry, first.Id, $"C959-pc-194 retry/{vector}");
        retry.Client.Requests.ShouldBeEmpty($"C959-no-probe-after-retry/{vector}");
        await AssertWarmReuseAsync(sample, vector);
        foreach (var busy in new[] { false, true })
            await CodexCliRemoteDeliveryFixture.RunAsync(Body, sample(DateTimeOffset.UtcNow), busy);
    }

    private static async Task AssertWarmReuseAsync(Func<DateTimeOffset, RunnerCodexCliVersionDto?> sample, string vector)
    {
        await using var warm = await DispatchKit.BuildAsync();
        await using var db = warm.Context();
        await db.Agents.Where(a => a.Id == warm.Harness.AgentId).ExecuteUpdateAsync(u => u
            .SetProperty(a => a.Kind, AgentKind.Codex).SetProperty(a => a.ModelLevel, AgentModelLevel.High));
        await db.AgentSessions.Where(s => s.Id == warm.Harness.SessionId).ExecuteUpdateAsync(u => u
            .SetProperty(s => s.AgentKind, AgentKind.Codex).SetProperty(s => s.EffectiveModelId, "gpt-6.1-sol"));
        warm.Client.Sample = sample(DateTimeOffset.UtcNow);
        AgentTaskCreatedDto created;
        using (var scope = warm.Harness.Provider.CreateScope())
            created = await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(
                new(Body, Title: Body, Role: AgentTaskRole.Docs, AgentKind: AgentKind.Codex,
                    ModelLevel: AgentModelLevel.High, Workspace: WorkspaceMode.Shared, RunnerId: "local", AgentId: warm.Harness.AgentId),
                new(null, null, Path.Combine(warm.Harness.TempRoot, "workspace")), CancellationToken.None);
        // Freeze independent producer inputs before the real reuse handoff.
        var before = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        var full = DelegationReportFormatter.BuildBrief(before, warm.Harness.Delegation,
            warm.Boundary.Ceilings.ForAgentKind(AgentKind.Codex).ReplyInlineMaxChars, refocus: false);
        var path = Path.Combine(before.WorkingDirectory, ".antiphon", $"task-{DelegationReportFormatter.Short(created.Id)}-brief.md");
        var wire = DelegationReportFormatter.BuildBriefPointer(before, warm.Harness.Delegation, path, full.Length, AgentKind.Codex).TrimEnd();
        await warm.TickAsync();
        warm.Client.Requests.ShouldBeEmpty("C959-pc-240 warm pinned " + vector);
        warm.Factory.Created.ShouldBeEmpty("C959-warm-no-cold-launch " + vector);
        var row = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        row.AgentSessionId.ShouldBe(warm.Harness.SessionId, "C959-warm-session " + vector);
        var receipts = await db.TranscriptEntries.AsNoTracking().Where(e => e.AgentSessionId == warm.Harness.SessionId && e.Kind == TranscriptKinds.UserPrompt).ToListAsync();
        receipts.Count.ShouldBe(1, "C959-warm-real-receipt " + vector);
        receipts.Single().Text.ShouldBe(wire, "C959-warm-complete-W " + vector);
        File.Exists(path).ShouldBeTrue("C959-warm-E-exists " + vector);
        (await File.ReadAllBytesAsync(path)).ShouldBe(Encoding.UTF8.GetBytes(full), "C959-warm-complete-E " + vector);
    }

    private static async Task AssertDeliveryAsync(DispatchKit kit, Guid taskId, string label, string expectedModel = "gpt-6.1-sol")
    {
        await kit.Harness.Provider.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        await using var db = kit.Context();
        var row = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        var receipts = await db.TranscriptEntries.AsNoTracking().Where(e => e.AgentSessionId == row.AgentSessionId && e.Kind == TranscriptKinds.UserPrompt).ToListAsync();
        receipts.Count.ShouldBe(1, label + " complete selected-session receipt");
        var frozen = kit.Boundary.Briefs[taskId];
        File.Exists(frozen.Path).ShouldBeTrue(label + " E exists");
        (await File.ReadAllBytesAsync(frozen.Path!)).ShouldBe(Encoding.UTF8.GetBytes(frozen.Full), label + " E bytes");
        frozen.Full.ShouldContain(Body, customMessage: label + " literal B");
        receipts.Single().Text.ShouldBe(frozen.Wire, label + " complete W");
        var queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.ExecutionTaskId == taskId);
        receipts.Single().Sequence.ShouldBeGreaterThan(queued.LastDeliveryBaselineSequence ?? 0, label + " baseline");
        var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == row.AgentSessionId);
        queued.LastDeliveryGeneration.ShouldBe(SessionGeneration.Normalize(session.StartedAt), label + " generation");
        row.RunnerId.ShouldBeNull(label + " selected desktop");
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Blocked)).ShouldBe(0, label + " no block");
        var args = kit.Factory.Created.Single().StartedArgs.ToList();
        args.Count(a => a == "--model").ShouldBe(1, label + " single model");
        args[args.IndexOf("--model") + 1].ShouldBe(expectedModel, label + " unchanged model");
    }

    private static async Task<string?> CodeAsync(Func<Task> call) => (await FailureAsync(call))?.Code;

    private static async Task<HttpException?> FailureAsync(Func<Task> call)
    {
        try { await call(); return null; }
        catch (HttpException ex) { return ex; }
    }

    private sealed class CancelTaskSave(CancellationTokenSource cancellation) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AgentTask>().Any(e => e.State == EntityState.Added))
            {
                cancellation.Cancel();
                ct.ThrowIfCancellationRequested();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Kit : IAsyncDisposable
    {
        public required IsolatedTestSchema Schema { get; init; }
        public required AppDbContext Db { get; init; }
        public FakeTimeProvider Clock { get; } = new(T);
        public DelegationSettings Settings { get; } = new() { DefaultWorkerWorkspace = WorkspaceMode.Shared };
        public Client Local { get; } = new();
        public Client Remote { get; } = new();
        public string Root => FindRoot();
        public AgentTaskService.Caller Caller => new(null, null, Root);
        public AgentTaskService Service => new(Db, new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(Settings), new MockEventBus(), new RecordingSessionStopper(), Clock, NullLogger<AgentTaskService>.Instance,
            modelAvailability: new ModelAvailability(Db, Clock, NullLogger<ModelAvailability>.Instance),
            registrySettings: Options.Create(Registry()), phoneHome: new PhoneHomeLaunchPolicy(Options.Create(new PhoneHomeRunnerSettings
            {
                Enabled = true, AllowedRunnerId = "runner-a", AllowDelegatedTasks = true, HostWorkspaceRoot = Root,
                CallbackOrigin = "https://antiphon.test", SharedSecret = "x",
            })), runners: new Directory(Local, Remote));
        public CreateAgentTaskRequest Request(bool remote) => new(Body, Role: AgentTaskRole.Code, AgentKind: AgentKind.Codex,
            ModelLevel: AgentModelLevel.High, Workspace: remote ? WorkspaceMode.Worktree : WorkspaceMode.Shared, RunnerId: remote ? "runner-a" : "local");
        public static async Task<Kit> CreateAsync(CancellationTokenSource? cancelBeforeTaskSave = null)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            if (cancelBeforeTaskSave is not null) options.AddInterceptors(new CancelTaskSave(cancelBeforeTaskSave));
            return new() { Schema = schema, Db = new(options.Options) };
        }
        public async Task<Guid> ProfileAgentAsync(string? model, string exe, string? argument)
        {
            var profile = new AgentTuiProfile { Id = Guid.NewGuid(), DisplayName = "C959", Kind = AgentKind.Codex, IsEnabled = true, SourceDefinitionName = "codex", CreatedAt = T.UtcDateTime, UpdatedAt = T.UtcDateTime };
            var revision = new AgentTuiProfileRevision { Id = Guid.NewGuid(), ProfileId = profile.Id, RevisionNumber = 1, Executable = exe, ModelArgumentName = argument, AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged, CreatedAt = T.UtcDateTime };
            Db.AddRange(profile, revision);
            await Db.SaveChangesAsync();
            profile.ActiveRevisionId = revision.Id;
            var agent = new Agent { Id = Guid.NewGuid(), Name = "C959", Slug = "c959-" + Guid.NewGuid().ToString("N"), WorkingDirectory = Root, Kind = AgentKind.Codex, ModelLevel = AgentModelLevel.High, ModelId = model, TuiProfileId = profile.Id, Status = AgentStatus.Idle, CreatedAt = T.UtcDateTime, UpdatedAt = T.UtcDateTime };
            Db.Add(agent);
            await Db.SaveChangesAsync();
            return agent.Id;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Schema.DisposeAsync(); }
    }

    private sealed class DispatchKit : IAsyncDisposable
    {
        public required IsolatedTestSchema Schema { get; init; }
        public required BridgeQueueHarness Harness { get; init; }
        public required Client Client { get; init; }
        public required Factory Factory { get; init; }
        public ScratchGitRepo? Git { get; init; }
        public AgentKind Kind { get; init; } = AgentKind.Codex;
        public required BriefBoundary Boundary { get; init; }
        public bool OwnsSchema { get; init; } = true;
        public AppDbContext Context() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));
        public static async Task<DispatchKit> BuildAsync(ScratchGitRepo? git = null, AgentKind kind = AgentKind.Codex, bool busy = false,
            IsolatedTestSchema? schema = null, TimeProvider? clock = null, bool preserve = false,
            CancellationTokenSource? cancelAfterClaim = null, Guid? attachSession = null, Guid? attachAgent = null)
        {
            var ownsSchema = schema is null;
            schema ??= await TestDbFixture.CreateIsolatedSchemaAsync();
            clock ??= TimeProvider.System;
            var client = new Client { Sample = new("0.160.0", clock.GetUtcNow(), null, new string('a',64)), ConfirmAbsentProcess = true };
            Factory? factory = null;
            var boundary = new BriefBoundary(schema.ConnectionString) { CancelAfterClaim = cancelAfterClaim };
            var h = await BridgeQueueHarness.CreateAsync(new()
            {
                AlwaysOn = false, ConnectionString = schema.ConnectionString,
                TimeProvider = clock, PreserveDatabaseOnDispose = preserve,
                AttachSessionId = attachSession, AttachAgentId = attachAgent,
                Delegation = new() { DefaultWorkerWorkspace = WorkspaceMode.Shared, MaxConcurrentTasks = 20, RolePolicy = new(), PoolIdleRetireMinutes = 525600 },
                ConfigureServices = services =>
                {
                    services.AddSingleton<ISessionRunnerDirectory>(new Directory(client, new Client()));
                    // Factory-started adapters own live processes. An unstarted, lost launch
                    // has no process on this isolated client; its kill/status responses confirm absence.
                    services.AddSingleton<ISessionRunnerClient>(client);
                    services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(Registry()));
                    services.AddSingleton<IOptions<AgentRegistrySettings>>(Options.Create(Registry()));
                    services.AddSingleton<IAgentProtocolAdapterFactory>(sp => factory = new(sp.GetRequiredService<AgentSessionRuntime>(), schema.ConnectionString, clock)
                    { ReadyHold = busy ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null });
                    services.AddSingleton<LandDeliveryBoundary>(boundary);
                    if (busy) services.AddSingleton<IEventBus>(sp => new BusyEventBus(
                        sp.GetRequiredService<MockEventBus>(), schema.ConnectionString));
                    services.AddSingleton(sp =>
                    {
                        var settings = sp.GetRequiredService<IOptions<DelegationSettings>>().Value;
                        boundary.Settings = settings;
                        boundary.Ceilings = settings.CeilingsFor(PtyBackend.ModernConPty, "existing measured modern profile for attached fake");
                        var profile = new PtyDeliveryProfile(sp.GetRequiredService<IServiceScopeFactory>(),
                            NullLogger<PtyDeliveryProfile>.Instance, Options.Create(settings), backendOverride: "inbox");
                        typeof(PtyDeliveryProfile).GetField("_ceilings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(profile, boundary.Ceilings);
                        return profile;
                    });
                    services.AddSingleton<SessionDeliveryProfile>();
                    services.AddSingleton<DelegationWorkspaceResolver>();
                    if (git is not null) services.RemoveAll<IWorktreeManager>();
                    services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = git?.WorktreeRoot ?? Path.Combine(Path.GetTempPath(), "c959-unused") });
                    services.AddScoped<AgentTaskService>();
                    services.AddScoped<IDelegateSessionStopper>(sp => sp.GetRequiredService<AgentSessionService>());
                    services.AddScoped<AgentTaskDispatcher>();
                },
            });
            return new() { Schema = schema, OwnsSchema = ownsSchema, Harness = h, Client = client, Factory = (Factory)h.Provider.GetRequiredService<IAgentProtocolAdapterFactory>(), Git = git, Kind = kind, Boundary = boundary };
        }
        public async Task<AgentTaskCreatedDto> CreateAsync(AgentModelLevel level = AgentModelLevel.High, string? exactModel = null)
        {
            Guid? agentId = null;
            if (exactModel is not null)
            {
                await using var db = Context();
                var now = DateTime.UtcNow;
                var profile = new AgentTuiProfile { Id = Guid.NewGuid(), DisplayName = "C959 exact",
                    Kind = AgentKind.Codex, IsEnabled = true, SourceDefinitionName = "codex", CreatedAt = now, UpdatedAt = now };
                var revision = new AgentTuiProfileRevision { Id = Guid.NewGuid(), ProfileId = profile.Id, RevisionNumber = 1,
                    Executable = "codex", ModelArgumentName = "--model", AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged, CreatedAt = now };
                db.AddRange(profile, revision); await db.SaveChangesAsync();
                profile.ActiveRevisionId = revision.Id;
                var agent = new Agent { Id = Guid.NewGuid(), Name = "C959 exact", Slug = "c959-" + Guid.NewGuid().ToString("N"),
                    WorkingDirectory = Git?.Path ?? Path.Combine(Harness.TempRoot, "workspace"), Kind = AgentKind.Codex,
                    ModelLevel = level, ModelId = exactModel, TuiProfileId = profile.Id,
                    Status = AgentStatus.Idle, CreatedAt = now, UpdatedAt = now };
                db.Add(agent); await db.SaveChangesAsync(); agentId = agent.Id;
            }
            using var scope = Harness.Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(new(Body, Title: Body, Role: AgentTaskRole.Docs,
                AgentKind: Kind, ModelLevel: level, Workspace: Git is null ? WorkspaceMode.Shared : WorkspaceMode.Worktree, RunnerId: "local", AgentId: agentId),
                new(null, null, Git?.Path ?? Path.Combine(Harness.TempRoot, "workspace")), CancellationToken.None);
        }
        public async Task TickAsync(CancellationToken ct = default)
        {
            using var scope = Harness.Provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        }
        public async ValueTask DisposeAsync() { await Harness.DisposeAsync(); if (OwnsSchema) await Schema.DisposeAsync(); }
    }
    private sealed record FrozenBrief(string Full, string Wire, string? Path);

    // Freeze committed producer inputs before launch scheduling or queue handoff.
    // Neither queued.Body, the file nor a transcript supplies the oracle.
    private sealed class BriefBoundary(string connection) : LandDeliveryBoundary
    {
        public CancellationTokenSource? CancelAfterClaim { get; set; }
        public DelegationSettings Settings { get; set; } = new();
        public PtyDeliveryCeilings Ceilings { get; set; } = null!;
        public Dictionary<Guid, FrozenBrief> Briefs { get; } = [];
        public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary != "dispatch-warning-claim-committed") return;
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
            var limits = Ceilings.ForAgentKind(task.AgentKind);
            var full = DelegationReportFormatter.BuildBrief(task, Settings, limits.ReplyInlineMaxChars, refocus: false);
            var path = task.AgentKind == AgentKind.ClaudeCode ? null : Path.Combine(task.WorkingDirectory,
                ".antiphon", $"task-{DelegationReportFormatter.Short(task.Id)}-brief.md");
            var wire = path is null ? full.TrimEnd() : DelegationReportFormatter.BuildBriefPointer(task, Settings,
                path, full.Length, task.AgentKind).TrimEnd();
            Briefs.Add(taskId, new(full, wire, path));
            if (CancelAfterClaim is not null)
            {
                CancelAfterClaim.Cancel();
                ct.ThrowIfCancellationRequested();
            }
        }
    }

    // Shift durable watchdog time while retaining real queue/launch timers.
    private sealed class RecoveryClock : TimeProvider
    {
        private TimeSpan _offset;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
        public void Advance(TimeSpan delta) => _offset += delta;
    }

    // A cold launch writes its own restart boundary. Mark activity AFTER that boundary,
    // at SessionStarted and before the real launch queue's boot flush.
    private sealed class BusyEventBus(MockEventBus inner, string connection) : IEventBus
    {
        public Task PublishToAllAsync(string name, object payload, CancellationToken ct = default) =>
            inner.PublishToAllAsync(name, payload, ct);
        public async Task PublishToGroupAsync(string group, string name, object payload, CancellationToken ct = default)
        {
            if (name == "SessionStarted")
            {
                var id = (Guid)payload.GetType().GetProperty("sessionId")!.GetValue(payload)!;
                await BridgeQueueHarness.InsertEntryAsync(id, TranscriptKinds.TurnEnd,
                    stopReason: "end_turn", connectionString: connection);
                await BridgeQueueHarness.InsertEntryAsync(id, TranscriptKinds.AssistantText,
                    "C959 activity after TurnEnd", connectionString: connection);
            }
            await inner.PublishToGroupAsync(group, name, payload, ct);
        }
    }

    private sealed class Factory(AgentSessionRuntime runtime, string connection, TimeProvider clock) : IAgentProtocolAdapterFactory
    {
        public TaskCompletionSource<bool>? ReadyHold { get; init; }
        public List<FakeAgentProtocolAdapter> Created { get; } = [];
        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            var adapter = new FakeAgentProtocolAdapter { RegisterOnStart = runtime, ReadyHold = ReadyHold };
            adapter.OnSubmitted = async text =>
            {
                await BridgeQueueHarness.InsertEntryAsync(adapter.StartedSessionId!.Value, TranscriptKinds.UserPrompt, text,
                    timestamp: clock.GetUtcNow().UtcDateTime, connectionString: connection, createdAtUtc: clock.GetUtcNow().UtcDateTime);
                await BridgeQueueHarness.InsertEntryAsync(adapter.StartedSessionId.Value, TranscriptKinds.TurnEnd,
                    stopReason: "end_turn", connectionString: connection);
            };
            Created.Add(adapter);
            return adapter;
        }
    }
    private sealed class Directory(Client local, Client remote) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local { get; set; } = local;
        public IReadOnlyList<string> KnownRunnerIds => ["runner-a"];
        public ISessionRunnerClient Resolve(string? runnerId) => string.IsNullOrWhiteSpace(runnerId) ? local : remote;
        public Guid? GetLiveStoreId(string? runnerId) => null;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) => Task.FromResult<RunnerInventory>(new RunnerInventory.Available([]));
    }
    // Deliberately omits the additive typed probe: use the real default interface body.
    private sealed class LegacyClient(Client inner) : ISessionRunnerClient
    {
        public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) => inner.GetCapabilitiesAsync(ct);
        public Task<SessionRunnerSessionDto> StartAsync(Guid id, AgentLaunchSpec spec, CancellationToken ct) => inner.StartAsync(id, spec, ct);
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => inner.ListAsync(ct);
        public Task<SessionRunnerSessionDto> GetAsync(Guid id, CancellationToken ct) => inner.GetAsync(id, ct);
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id, CancellationToken ct) => inner.GetBufferAsync(id, ct);
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct) => inner.GetSnapshotAsync(id, ct);
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id, CancellationToken ct) => inner.GetTranscriptAsync(id, ct);
        public Task SendInputAsync(Guid id, string input, CancellationToken ct) => inner.SendInputAsync(id, input, ct);
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => inner.ClearLiveBufferAsync(id, ct);
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => inner.ResizeAsync(id, cols, rows, ct);
        public Task<SessionRunnerSessionDto> KillAsync(Guid id, CancellationToken ct) => inner.KillAsync(id, ct);
        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => inner.StreamEventsAsync(ct);
    }
    private sealed class Client : ISessionRunnerClient
    {
        public RunnerCodexCliVersionDto? Sample { get; set; } = new("0.160.0", T, null, new string('a',64));
        public bool? AuthPresent { get; set; } = true;
        public Func<CancellationToken, Task<RunnerCodexCliVersionDto?>>? Probe { get; set; }
        public bool ConfirmAbsentProcess { get; init; }
        public List<RunnerCodexCliProbeRequest> Requests { get; } = [];
        public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) => Task.FromResult<RunnerCapabilitiesDto?>(new("test", "test", "test", false, Version: "d40c1670", Platform: "linux", CodexCliVersion: Sample?.CodexCliVersion, CodexCliVersionCheckedAtUtc: Sample?.CodexCliVersionCheckedAtUtc, CodexCliVersionError: Sample?.CodexCliVersionError, CodexCliLauncherFingerprint: Sample?.CodexCliLauncherFingerprint));
        public Task<RunnerCodexCliVersionDto?> GetCodexCliVersionAsync(RunnerCodexCliProbeRequest request, CancellationToken ct)
        { Requests.Add(request); return Probe is null ? Task.FromResult(Sample) : Probe(ct); }
        public Task<RunnerProviderAuthDto?> GetProviderAuthAsync(string provider, CancellationToken ct) => Task.FromResult<RunnerProviderAuthDto?>(new(provider, AuthPresent, null, null, T, null));
        public Task<SessionRunnerSessionDto> StartAsync(Guid id, AgentLaunchSpec spec, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        public Task<SessionRunnerSessionDto> GetAsync(Guid id, CancellationToken ct) => ConfirmAbsentProcess
            ? Task.FromResult(Absent(id)) : throw new NotSupportedException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id, CancellationToken ct) => ConfirmAbsentProcess
            ? Task.FromResult(new SessionRunnerTranscriptDto(id, [], 0)) : throw new NotSupportedException();
        public Task SendInputAsync(Guid id, string input, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> KillAsync(Guid id, CancellationToken ct) => ConfirmAbsentProcess
            ? Task.FromResult(Absent(id)) : throw new NotSupportedException();
        private static SessionRunnerSessionDto Absent(Guid id) => new(id, null, DateTime.UtcNow, "Exited", 0, AgentExitReason.Unknown, 0);
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) { await Task.CompletedTask; yield break; }
    }
    internal static AgentRegistrySettings Registry() => new()
    {
        DefaultDefinition = "codex", Definitions = { ["codex"] = new() { Kind = "Codex", Exe = "codex" }, ["claude"] = new() { Kind = "ClaudeCode", Exe = "claude" } },
    };
    private static string FindRoot()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path is not null && !File.Exists(Path.Combine(path.FullName, "Antiphon.sln"))) path = path.Parent;
        return path!.FullName;
    }
}
