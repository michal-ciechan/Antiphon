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
public sealed class CodexCliAdmissionTests
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
    public async Task C959_Create_refuses_bad_versions()
    {
        foreach (var remote in new[] { false, true })
        foreach (var (version, error, age, code) in new (string?, string?, TimeSpan, string?)[]
        {
            ("0.156.1", null, TimeSpan.Zero, "codex_cli_version_too_old"),
            ("0.159.0", null, TimeSpan.Zero, "codex_cli_version_too_old"),
            (null, "invalid_output", TimeSpan.Zero, "codex_cli_version_unknown"),
            ("banana", null, TimeSpan.Zero, "codex_cli_version_unknown"),
            ("", null, TimeSpan.Zero, "codex_cli_version_unknown"),
            (null, "timeout", TimeSpan.Zero, "codex_cli_version_unknown"),
            ("0.160.0", null, TimeSpan.FromMinutes(15) + TimeSpan.FromTicks(1), "codex_cli_version_stale"),
            ("0.160.0", "nonzero_exit", TimeSpan.Zero, "codex_cli_version_unknown"),
            ("0.159.1", null, TimeSpan.Zero, null), ("0.160.0", null, TimeSpan.Zero, null),
            ("0.160.0", null, TimeSpan.FromMinutes(15), null),
            ("0.160.0", null, -TimeSpan.FromMinutes(1), null),
            ("0.160.0", null, -TimeSpan.FromMinutes(1) - TimeSpan.FromTicks(1), "codex_cli_version_unknown"),
            ("0.159.1-beta.1", null, TimeSpan.Zero, "codex_cli_version_too_old"),
            ("0.160.0-beta.1", null, TimeSpan.Zero, null),
        })
        {
            await using var k = await Kit.CreateAsync();
            var selected = remote ? k.Remote : k.Local;
            selected.Sample = new(version, T - age, error, new string('a', 64));
            var request = k.Request(remote);
            var failure = await FailureAsync(() => k.Service.CreateAsync(request, k.Caller, CancellationToken.None));
            (failure?.Code).ShouldBe(code, "C959-v15-code " + remote + "/" + version + "/" + error);
            if (code is not null)
            {
                failure!.StatusCode.ShouldBe(409, "C959-pc-128");
                failure.Extensions!["runnerId"].ShouldBe(remote ? "runner-a" : "desktop", "C959-pc-132");
                failure.Extensions["model"].ShouldBe("gpt-6.1-sol");
                failure.Extensions["requiredVersion"].ShouldBe("0.159.1");
                (await k.Db.AgentTasks.CountAsync()).ShouldBe(0, "C959-pc-131");
                (await k.Db.AgentSessions.CountAsync()).ShouldBe(0);
            }
            (remote ? k.Local : k.Remote).Requests.ShouldBeEmpty("C959-pc-133");
            selected.Requests.Count.ShouldBe(1, "C959-v15-one-probe");
        }
        await using (var httpKit = await Kit.CreateAsync())
        {
            httpKit.Local.Sample = new("0.156.1", T, null, new string('a', 64));
            await using var host = await PhoneHomeTestHost.StartAsync(httpKit.Clock,
                mapEndpoints: app => app.MapPost("/c959/create", async (CreateAgentTaskRequest request, CancellationToken ct) =>
                    await httpKit.Service.CreateAsync(request, httpKit.Caller, ct)));
            using var response = await host.Http.PostAsJsonAsync("/c959/create", httpKit.Request(false));
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict, "C959-v15-http-409");
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            problem.GetProperty("code").GetString().ShouldBe("codex_cli_version_too_old", "C959-v15-http-code");
            foreach (var (name, value) in new[] { ("runnerId", "desktop"), ("model", "gpt-6.1-sol"),
                ("requiredVersion", "0.159.1"), ("observedVersion", "0.156.1"), ("reason", "below_floor") })
                problem.GetProperty(name).GetString().ShouldBe(value, "C959-v15-http-extension " + name);
            problem.GetProperty("maxAgeMinutes").GetInt32().ShouldBe(15);
            (await httpKit.Db.AgentTasks.CountAsync()).ShouldBe(0, "C959-v15-http-no-insert");
        }
        await using var legacy = await Kit.CreateAsync();
        legacy.Local.Sample = null;
        (await CodeAsync(() => legacy.Service.CreateAsync(legacy.Request(false), legacy.Caller, CancellationToken.None))).ShouldBe("codex_cli_version_unknown", "C959-v15-omitted");
    }

    [Test]
    public async Task C959_Existing_refusals_and_flags_keep_precedence()
    {
        await using var k = await Kit.CreateAsync();
        k.Local.Sample = k.Remote.Sample = new("0.156.1", T, null, new string('a', 64));
        var request = k.Request(true) with { AllowUnauthenticatedProvider = true };
        (await CodeAsync(() => k.Service.CreateAsync(request, k.Caller, CancellationToken.None))).ShouldBe("codex_cli_version_too_old", "C959-pc-137");
        k.Remote.AuthPresent = false;
        (await CodeAsync(() => k.Service.CreateAsync(request with { AllowUnauthenticatedProvider = false }, k.Caller, CancellationToken.None))).ShouldBe("provider_sign_in_required", "C959-pc-135");
        k.Remote.Requests.Clear();
        k.Db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(), Kind = AgentKind.Codex, ModelAlias = "gpt-6.1-sol",
            Source = ModelAvailabilitySource.Manual, HitAt = T.UtcDateTime,
        });
        await k.Db.SaveChangesAsync();
        (await CodeAsync(() => k.Service.CreateAsync(request, k.Caller, CancellationToken.None))).ShouldBe("model_disabled", "C959-pc-134");
        k.Remote.Requests.ShouldBeEmpty("C959-v16-auth-hold-before-probe");
        (await CodeAsync(() => k.Service.CreateAsync(request with { IgnoreModelDisabled = true }, k.Caller, CancellationToken.None))).ShouldBe("codex_cli_version_too_old", "C959-pc-138");
        await k.Db.ModelAvailabilityHolds.ExecuteDeleteAsync();
        k.Remote.AuthPresent = null;
        k.Remote.Sample = new("0.160.0", T, null, new string('a', 64));
        (await k.Service.CreateAsync(k.Request(true), k.Caller, CancellationToken.None)).Status
            .ShouldBe(AgentTaskStatus.Queued, "C959-pc-136");
    }

    [Test]
    public async Task C959_Queued_downgrade_blocks_before_claim()
    {
        using (var git = new ScratchGitRepo("c959-late-downgrade"))
        {
            await git.CommitFileAsync("seed.txt", "C959 late descriptor\n");
            await using var late = await DispatchKit.BuildAsync(git);
            late.Client.Probe = _ => Task.FromResult<RunnerCodexCliVersionDto?>(new(
                late.Client.Requests.Last().ResolutionCwd == git.Path ? "0.160.0" : "0.156.1",
                DateTimeOffset.UtcNow, null, new string('a',64)));
            var created = await late.CreateAsync();
            await late.TickAsync();
            await using var db = late.Context();
            var row = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
            row.Status.ShouldBe(AgentTaskStatus.Blocked, "C959-v17-late-downgrade-blocked");
            row.FailureReason.ShouldStartWith("codex_cli_version_too_old", customMessage: "C959-v17-late-descriptor-reprobe");
            late.Client.Requests.Count.ShouldBe(3, "C959-v17-exact-cwd-preflight");
            late.Factory.Created.ShouldBeEmpty("C959-v17-late-no-input");
        }
        foreach (var sample in new[]
        {
            new RunnerCodexCliVersionDto("0.156.1", T, null, new string('a',64)),
            new RunnerCodexCliVersionDto(null, T, "nonzero_exit", new string('a',64)),
            new RunnerCodexCliVersionDto("0.160.0", T.AddMinutes(-16), null, new string('a',64)),
        })
        {
            await using var k = await DispatchKit.BuildAsync();
            var created = await k.CreateAsync();
            k.Client.Sample = sample;
            await k.TickAsync();
            await using var db = k.Context();
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
            task.Status.ShouldBe(AgentTaskStatus.Blocked, "C959-v17-blocked");
            task.FailureReason.ShouldStartWith("codex_cli_version_", customMessage: "C959-pc-141");
            task.AgentSessionId.ShouldBeNull("C959-pc-140");
            k.Factory.Created.ShouldBeEmpty("C959-pc-139");
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Blocked)).ShouldBe(1);
            var existing = await db.AgentSessions.SingleAsync(s => s.Id == k.Harness.SessionId);
            existing.Status.ShouldBe(SessionStatus.Running, "C959-pc-143");
            k.Client.Sample = new("0.160.0", T, null, new string('a',64));
            await k.TickAsync();
            k.Factory.Created.ShouldBeEmpty("C959-pc-142");
        }
    }

    [Test]
    public async Task C959_Exact_profile_model_wins()
    {
        await using var k = await Kit.CreateAsync();
        k.Local.Sample = new("0.156.1", T, null, new string('a',64));
        var id = await k.ProfileAgentAsync("gpt-6.1-sol", "/fixture/codex-b", "--model");
        foreach (var level in new[] { AgentModelLevel.Low, AgentModelLevel.Frontier })
        {
            k.Db.ChangeTracker.Clear();
            var agent = await k.Db.Agents.SingleAsync(a => a.Id == id);
            agent.ModelLevel = level;
            await k.Db.SaveChangesAsync();
            (await CodeAsync(() => k.Service.CreateAsync(k.Request(false) with { AgentId = id }, k.Caller, CancellationToken.None))).ShouldBe("codex_cli_version_too_old", "C959-pc-147");
        }
        var pin = await k.Db.Agents.SingleAsync(a => a.Id == id);
        pin.ModelId = "gpt-6-sol";
        pin.ModelLevel = AgentModelLevel.High;
        await k.Db.SaveChangesAsync();
        (await k.Service.CreateAsync(k.Request(false) with { AgentId = id }, k.Caller, CancellationToken.None)).Status
            .ShouldBe(AgentTaskStatus.Queued, "C959-pc-148");
        var revision = await k.Db.AgentTuiProfileRevisions.SingleAsync();
        revision.ModelArgumentName = "";
        pin.ModelId = "gpt-6.1-sol";
        await k.Db.SaveChangesAsync();
        (await CodeAsync(() => k.Service.CreateAsync(k.Request(false) with { AgentId = id }, k.Caller, CancellationToken.None))).ShouldBe("model_argument_unsupported", "C959-pc-149");
        pin.ModelId = null;
        await k.Db.SaveChangesAsync();
        k.Local.Requests.Clear();
        (await k.Service.CreateAsync(k.Request(false) with { AgentId = id }, k.Caller, CancellationToken.None)).Status
            .ShouldBe(AgentTaskStatus.Queued, "C959-pc-150");
        k.Local.Requests.ShouldBeEmpty();
        var profileless = id;
        pin.ModelId = "gpt-6-sol";
        pin.TuiProfileId = null;
        await k.Db.SaveChangesAsync();
        k.Local.Sample = new("0.156.1", T, null, new string('a',64));
        (await CodeAsync(() => k.Service.CreateAsync(k.Request(false) with { AgentId = profileless }, k.Caller, CancellationToken.None)))
            .ShouldBe("codex_cli_version_too_old", "C959-v18-profileless-actual-model");
    }

    [Test]
    public async Task C959_Profile_launcher_uses_its_own_evidence()
    {
        await using var k = await Kit.CreateAsync();
        var id = await k.ProfileAgentAsync("gpt-6.1-sol", "/fixture/codex-b", "--model");
        k.Local.Sample = new("0.156.1", T, null, new string('b',64));
        (await CodeAsync(() => k.Service.CreateAsync(k.Request(false) with { AgentId = id }, k.Caller, CancellationToken.None))).ShouldBe("codex_cli_version_too_old", "C959-pc-153");
        k.Local.Requests.Single().Executable.ShouldBe("/fixture/codex-b", "C959-v19-selected");
        k.Local.Sample = new("0.160.0", T, null, new string('b',64));
        (await k.Service.CreateAsync(k.Request(false) with { AgentId = id }, k.Caller, CancellationToken.None)).Status
            .ShouldBe(AgentTaskStatus.Queued, "C959-pc-154");
        var revision = await k.Db.AgentTuiProfileRevisions.SingleAsync();
        revision.NonSecretEnvironmentJson = "{\"PATH\":\"B:A\",\"PATHEXT\":\".CMD;.EXE\"}";
        await k.Db.SaveChangesAsync();
        k.Local.Requests.Clear();
        await k.Service.CreateAsync(k.Request(false) with { AgentId = id }, k.Caller, CancellationToken.None);
        k.Local.Requests.Single().Path.ShouldBe("B:A", "C959-pc-156");
        k.Local.Requests.Single().PathExt.ShouldBe(".CMD;.EXE", "C959-pc-157");
        revision.NonSecretEnvironmentJson = "{\"NODE_OPTIONS\":\"--require=/fixture/untrusted-loader\"}";
        await k.Db.SaveChangesAsync();
        k.Local.Requests.Clear();
        (await CodeAsync(() => k.Service.CreateAsync(k.Request(false) with { AgentId = id }, k.Caller, CancellationToken.None)))
            .ShouldBe("codex_cli_version_unknown", "C959-v19-loader-refusal");
        k.Local.Requests.ShouldBeEmpty("C959-v19-loader-no-probe");
        revision.NonSecretEnvironmentJson = "{}";
        await k.Db.SaveChangesAsync();
        k.Local.Sample = null;
        (await CodeAsync(() => k.Service.CreateAsync(k.Request(false) with { AgentId = id }, k.Caller, CancellationToken.None))).ShouldBe("codex_cli_version_unknown", "C959-pc-155");
    }

    [Test]
    public async Task C959_Override_is_scoped_expiring_and_audited()
    {
        var member = typeof(DelegationSettings).GetProperty("CodexCliVersionOverrides");
        member.ShouldNotBeNull("C959-v20-scope operator setting must exist");
        await using var k = await Kit.CreateAsync();
        k.Local.Sample = new("0.156.1", T, null, new string('a',64));
        void Override(string runner = "desktop", string model = "gpt-6.1-sol", string code = "codex_cli_version_too_old", string reason = "C959 isolated qualification", DateTimeOffset? expiry = null)
        {
            var json = JsonSerializer.Serialize(new[] { new { RunnerId = runner, Model = model, AllowedRefusalCodes = new[] { code }, Reason = reason, ExpiresAtUtc = expiry ?? T.AddMinutes(5) } });
            member!.SetValue(k.Settings, JsonSerializer.Deserialize(json, member.PropertyType));
        }
        foreach (var (runner, model, code) in new[]
        {
            ("runner-a", "gpt-6.1-sol", "codex_cli_version_too_old"),
            ("desktop", "gpt-6-sol", "codex_cli_version_too_old"),
            ("desktop", "gpt-6.1-sol", "codex_cli_version_unknown"),
        })
        {
            Override(runner, model, code);
            (await CodeAsync(() => k.Service.CreateAsync(k.Request(false), k.Caller, CancellationToken.None))).ShouldBe("codex_cli_version_too_old", "C959-v20-wrong-scope");
        }
        Override();
        var task = await k.Service.CreateAsync(k.Request(false), k.Caller, CancellationToken.None);
        var warning = await k.Db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning);
        warning.Detail.ShouldContain("desktop", customMessage: "C959-pc-172");
        warning.Detail.ShouldContain("gpt-6.1-sol");
        warning.Detail.ShouldContain("codex_cli_version_too_old");
        warning.Detail.ShouldContain("2026-10-03T12:05:00");
        warning.Detail.ShouldContain("C959 isolated qualification");
        k.Local.Sample.CodexCliVersion.ShouldBe("0.156.1", "C959-pc-174");
        k.Clock.Advance(TimeSpan.FromMinutes(5));
        (await CodeAsync(() => k.Service.CreateAsync(k.Request(false), k.Caller, CancellationToken.None))).ShouldBe("codex_cli_version_too_old", "C959-pc-163");
        foreach (var invalid in new[] { "runner", "model", "code", "reason", "expiry", "lifetime", "offset", "long-reason" })
        {
            Override(invalid == "runner" ? "*" : "desktop", invalid == "model" ? "*" : "gpt-6.1-sol",
                invalid == "code" ? "model_disabled" : "codex_cli_version_too_old", invalid == "reason" ? "" : invalid == "long-reason" ? new string('r',1001) : "reason",
                invalid == "offset" ? k.Clock.GetUtcNow().AddMinutes(5).ToOffset(TimeSpan.FromHours(1)) : invalid == "expiry" ? default(DateTimeOffset) : invalid == "lifetime" ? k.Clock.GetUtcNow().AddHours(24).AddTicks(1) : k.Clock.GetUtcNow().AddMinutes(5));
            new DelegationSettingsValidator(k.Clock).Validate(null, k.Settings).Failed.ShouldBeTrue("C959-v20-invalid " + invalid);
        }
        Override(expiry: k.Clock.GetUtcNow().AddHours(24));
        new DelegationSettingsValidator(k.Clock).Validate(null, k.Settings).Failed.ShouldBeFalse("C959-pc-169");
        k.Settings.CodexCliVersionOverrides.Add(k.Settings.CodexCliVersionOverrides.Single());
        new DelegationSettingsValidator(k.Clock).Validate(null, k.Settings).Failed.ShouldBeTrue("C959-pc-170");
        foreach (var code in new[] { "codex_cli_version_unknown", "codex_cli_version_stale" })
        {
            k.Local.Sample = code.EndsWith("unknown", StringComparison.Ordinal) ? null
                : new("0.160.0", k.Clock.GetUtcNow().AddMinutes(-16), null, new string('a',64));
            Override(code: code, expiry: k.Clock.GetUtcNow().AddMinutes(5));
            var allowed = await k.Service.CreateAsync(k.Request(false), k.Caller, CancellationToken.None);
            (await k.Db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == allowed.Id && e.Type == AgentTaskEventType.Warning))
                .Detail.ShouldContain(code, customMessage: "C959-v20-distinct-code " + code);
        }
    }

    [Test]
    public async Task C959_Compatible_launch_keeps_model_and_runner()
    {
        await using (var refused = await DispatchKit.BuildAsync())
        {
            refused.Client.Sample = new("0.159.0", T, null, new string('a',64));
            (await CodeAsync(() => refused.CreateAsync())).ShouldNotBeNull("C959-v21-admission-before-delivery");
        }
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
            var queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.ExecutionTaskId == task.Id);
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
            row.RunnerId.ShouldBeNull("C959-pc-185 " + vector);
            queued.Body.ShouldBe(expected, "C959-pc-186 " + vector);
            var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == row.AgentSessionId);
            queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == queued.Id);
            queued.LastDeliveryGeneration.ShouldBe(SessionGeneration.Normalize(session.StartedAt), "C959-v21-generation " + vector);
            var receipts = await db.TranscriptEntries.AsNoTracking().Where(e => e.AgentSessionId == row.AgentSessionId && e.Kind == TranscriptKinds.UserPrompt).ToListAsync();
            receipts.Count.ShouldBe(1, "C959-v21-no-duplicate " + vector);
            receipts.Single().Text.ShouldBe(expected, "C959-v21-receipt " + vector);
            receipts.Single().Sequence.ShouldBeGreaterThan(queued.LastDeliveryBaselineSequence ?? 0, "C959-v21-baseline " + vector);
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning && e.Detail!.Contains("codex_cli_version"))).ShouldBe(0, "C959-pc-189 " + vector);
        }
        foreach (var version in new[] { "0.159.1", "0.160.0" })
        foreach (var busy in new[] { false, true })
            await CodexCliRemoteDeliveryFixture.RunAsync(Body, version, busy);
    }

    [Test]
    public async Task C959_Retry_and_cancellation_recheck()
    {
        foreach (var version in new[] { "0.159.1", "0.160.0" })
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
                foreach (var (sample, code) in new (RunnerCodexCliVersionDto?, string)[]
                {
                    (new("0.156.1", clock.GetUtcNow(), null, new string('a',64)), "codex_cli_version_too_old"),
                    (new("0.160.0", clock.GetUtcNow().AddMinutes(-16), null, new string('a',64)), "codex_cli_version_stale"),
                    (null, "codex_cli_version_unknown"),
                })
                {
                    recovered.Client.Sample = sample;
                    (await CodeAsync(() => Retry())).ShouldBe(code, "C959-pc-193 " + vector);
                    await using var db = recovered.Context();
                    (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId)).Status
                        .ShouldBe(AgentTaskStatus.Failed, "C959-v22-state " + vector);
                    (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Retried)).ShouldBe(0);
                    recovered.Factory.Created.ShouldBeEmpty();
                }
                var directory = (Directory)recovered.Harness.Provider.GetRequiredService<ISessionRunnerDirectory>();
                directory.Local = new LegacyClient(recovered.Client);
                try
                {
                    (await CodeAsync(() => Retry())).ShouldBe("codex_cli_version_unknown", "C959-pc-198 " + vector);
                    await using var db = recovered.Context();
                    (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId)).Status.ShouldBe(AgentTaskStatus.Failed);
                    (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Retried)).ShouldBe(0);
                }
                finally { directory.Local = recovered.Client; }
                recovered.Client.Sample = new("0.156.1", clock.GetUtcNow(), null, new string('a',64));
                recovered.Harness.Delegation.CodexCliVersionOverrides = [new()
                {
                    RunnerId = "desktop", Model = "gpt-6.1-sol", AllowedRefusalCodes = ["codex_cli_version_too_old"],
                    Reason = "C959 expired retry qualification", ExpiresAtUtc = clock.GetUtcNow(),
                }];
                (await CodeAsync(() => Retry())).ShouldBe("codex_cli_version_too_old", "C959-pc-197 " + vector);
                recovered.Harness.Delegation.CodexCliVersionOverrides.Clear();
                recovered.Client.Sample = new(version, clock.GetUtcNow(), null, new string('a',64));
                (await Retry()).Status.ShouldBe(AgentTaskStatus.Queued, "C959-v22-explicit-retry " + vector);
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
        await using var k = await Kit.CreateAsync();
        await using (var bounded = await Kit.CreateAsync())
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bounded.Local.Probe = async ct => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return null; };
            var attempt = CodeAsync(() => bounded.Service.CreateAsync(bounded.Request(false), bounded.Caller, CancellationToken.None));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            bounded.Clock.Advance(TimeSpan.FromSeconds(8));
            (await attempt.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe("codex_cli_version_unknown", "C959-pc-196");
            (await bounded.Db.AgentTasks.CountAsync()).ShouldBe(0, "C959-v22-deadline-no-insert");
        }
        using var cancel = new CancellationTokenSource();
        k.Local.Probe = async ct => { cancel.Cancel(); await Task.Delay(Timeout.Infinite, ct); return null; };
        await Should.ThrowAsync<OperationCanceledException>(() => k.Service.CreateAsync(k.Request(false), k.Caller, cancel.Token));
        (await k.Db.AgentTasks.CountAsync()).ShouldBe(0, "C959-pc-195");
    }

    private static async Task<string?> CodeAsync(Func<Task> call) => (await FailureAsync(call))?.Code;

    private static async Task<HttpException?> FailureAsync(Func<Task> call)
    {
        try { await call(); return null; }
        catch (HttpException ex) { return ex; }
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
        public static async Task<Kit> CreateAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            return new() { Schema = schema, Db = new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)) };
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
        public async Task<AgentTaskCreatedDto> CreateAsync()
        {
            using var scope = Harness.Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(new(Body, Title: Body, Role: AgentTaskRole.Docs,
                AgentKind: Kind, ModelLevel: AgentModelLevel.High, Workspace: Git is null ? WorkspaceMode.Shared : WorkspaceMode.Worktree, RunnerId: "local"),
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
        public CancellationTokenSource? CancelAfterClaim { get; init; }
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
        public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) => Task.FromResult<RunnerCapabilitiesDto?>(new("test", "test", "test", false, Version: "d40c1670", Platform: "linux", CodexCliVersion: "0.160.0", CodexCliVersionCheckedAtUtc: T, CodexCliLauncherFingerprint: new string('a',64)));
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
    internal static ISessionRunnerDirectory CurrentLocalRunnerFixture() => new Directory(
        new Client { Sample = new("0.160.0", DateTimeOffset.UtcNow, null, new string('a',64)) }, new Client());
    private static string FindRoot()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path is not null && !File.Exists(Path.Combine(path.FullName, "Antiphon.sln"))) path = path.Parent;
        return path!.FullName;
    }
}
