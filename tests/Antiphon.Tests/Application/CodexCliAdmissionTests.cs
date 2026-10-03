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
            ("0.159.1-beta.1", null, TimeSpan.Zero, "codex_cli_version_too_old"),
            ("0.160.0-beta.1", null, TimeSpan.Zero, null),
        })
        {
            await using var k = await Kit.CreateAsync();
            var selected = remote ? k.Remote : k.Local;
            selected.Sample = new(version, T - age, error, new string('a', 64));
            var request = k.Request(remote);
            var failure = await FailureAsync(() => k.Service.CreateAsync(request, k.Caller, CancellationToken.None));
            failure?.Code.ShouldBe(code, "C959-v15-code " + remote + "/" + version + "/" + error);
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
        await using var legacy = await Kit.CreateAsync();
        legacy.Local.Sample = null;
        (await FailureAsync(() => legacy.Service.CreateAsync(legacy.Request(false), legacy.Caller, CancellationToken.None)))
            ?.Code.ShouldBe("codex_cli_version_unknown", "C959-v15-omitted");
    }

    [Test]
    public async Task C959_Existing_refusals_and_flags_keep_precedence()
    {
        await using var k = await Kit.CreateAsync();
        k.Local.Sample = k.Remote.Sample = new("0.156.1", T, null, new string('a', 64));
        var request = k.Request(true) with { AllowUnauthenticatedProvider = true };
        (await FailureAsync(() => k.Service.CreateAsync(request, k.Caller, CancellationToken.None)))
            ?.Code.ShouldBe("codex_cli_version_too_old", "C959-pc-137");
        k.Remote.AuthPresent = false;
        (await FailureAsync(() => k.Service.CreateAsync(request with { AllowUnauthenticatedProvider = false }, k.Caller, CancellationToken.None)))
            ?.Code.ShouldBe("provider_sign_in_required", "C959-pc-135");
        k.Remote.Requests.Clear();
        k.Db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(), Kind = AgentKind.Codex, ModelAlias = "gpt-6.1-sol",
            Source = ModelAvailabilitySource.Manual, HitAt = T.UtcDateTime,
        });
        await k.Db.SaveChangesAsync();
        (await FailureAsync(() => k.Service.CreateAsync(request, k.Caller, CancellationToken.None)))
            ?.Code.ShouldBe("model_disabled", "C959-pc-134");
        k.Remote.Requests.ShouldBeEmpty("C959-v16-auth-hold-before-probe");
        (await FailureAsync(() => k.Service.CreateAsync(request with { IgnoreModelDisabled = true }, k.Caller, CancellationToken.None)))
            ?.Code.ShouldBe("codex_cli_version_too_old", "C959-pc-138");
        await k.Db.ModelAvailabilityHolds.ExecuteDeleteAsync();
        k.Remote.AuthPresent = null;
        k.Remote.Sample = new("0.160.0", T, null, new string('a', 64));
        (await k.Service.CreateAsync(k.Request(true), k.Caller, CancellationToken.None)).Status
            .ShouldBe(AgentTaskStatus.Queued, "C959-pc-136");
    }

    [Test]
    public async Task C959_Queued_downgrade_blocks_before_claim()
    {
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
            task.FailureReason.ShouldStartWith("codex_cli_version_", "C959-pc-141");
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
            (await FailureAsync(() => k.Service.CreateAsync(k.Request(false) with { AgentId = id }, k.Caller, CancellationToken.None)))
                ?.Code.ShouldBe("codex_cli_version_too_old", "C959-pc-147");
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
        (await FailureAsync(() => k.Service.CreateAsync(k.Request(false) with { AgentId = id }, k.Caller, CancellationToken.None)))
            ?.Code.ShouldBe("model_argument_unsupported", "C959-pc-149");
        pin.ModelId = null;
        await k.Db.SaveChangesAsync();
        k.Local.Requests.Clear();
        (await k.Service.CreateAsync(k.Request(false) with { AgentId = id }, k.Caller, CancellationToken.None)).Status
            .ShouldBe(AgentTaskStatus.Queued, "C959-pc-150");
        k.Local.Requests.ShouldBeEmpty();
    }

    [Test]
    public async Task C959_Profile_launcher_uses_its_own_evidence()
    {
        await using var k = await Kit.CreateAsync();
        var id = await k.ProfileAgentAsync("gpt-6.1-sol", "/fixture/codex-b", "--model");
        k.Local.Sample = new("0.156.1", T, null, new string('b',64));
        (await FailureAsync(() => k.Service.CreateAsync(k.Request(false) with { AgentId = id }, k.Caller, CancellationToken.None)))
            ?.Code.ShouldBe("codex_cli_version_too_old", "C959-pc-153");
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
        k.Local.Sample = null;
        (await FailureAsync(() => k.Service.CreateAsync(k.Request(false) with { AgentId = id }, k.Caller, CancellationToken.None)))
            ?.Code.ShouldBe("codex_cli_version_unknown", "C959-pc-155");
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
            (await FailureAsync(() => k.Service.CreateAsync(k.Request(false), k.Caller, CancellationToken.None)))
                ?.Code.ShouldBe("codex_cli_version_too_old", "C959-v20-wrong-scope");
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
        (await FailureAsync(() => k.Service.CreateAsync(k.Request(false), k.Caller, CancellationToken.None)))
            ?.Code.ShouldBe("codex_cli_version_too_old", "C959-pc-163");
        foreach (var invalid in new[] { "runner", "model", "code", "reason", "expiry", "lifetime" })
        {
            Override(invalid == "runner" ? "*" : "desktop", invalid == "model" ? "*" : "gpt-6.1-sol",
                invalid == "code" ? "model_disabled" : "codex_cli_version_too_old", invalid == "reason" ? "" : "reason",
                invalid == "expiry" ? default(DateTimeOffset) : invalid == "lifetime" ? k.Clock.GetUtcNow().AddHours(24).AddTicks(1) : k.Clock.GetUtcNow().AddMinutes(5));
            new DelegationSettingsValidator().Validate(null, k.Settings).Failed.ShouldBeTrue("C959-v20-invalid " + invalid);
        }
    }

    [Test]
    public async Task C959_Compatible_launch_keeps_model_and_runner()
    {
        await using (var refused = await DispatchKit.BuildAsync())
        {
            refused.Client.Sample = new("0.159.0", T, null, new string('a',64));
            (await FailureAsync(() => refused.CreateAsync())).ShouldNotBeNull("C959-v21-admission-before-delivery");
        }
        foreach (var version in new[] { "0.159.1", "0.160.0" })
        {
            await using var k = await DispatchKit.BuildAsync();
            k.Client.Sample = new(version, T, null, new string('a',64));
            var task = await k.CreateAsync();
            await k.TickAsync();
            await k.Harness.Provider.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
            k.Factory.Created.Count.ShouldBe(1, "C959-pc-190");
            var adapter = k.Factory.Created.Single();
            var args = adapter.StartedArgs.ToList();
            args.Count(a => a == "--model").ShouldBe(1, "C959-pc-184");
            args[args.IndexOf("--model") + 1].ShouldBe("gpt-6.1-sol");
            await using var db = k.Context();
            var row = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            row.RunnerId.ShouldBeNull("C959-pc-185");
            var queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.ExecutionTaskId == task.Id);
            var receipt = await db.TranscriptEntries.AsNoTracking().SingleOrDefaultAsync(e => e.AgentSessionId == row.AgentSessionId && e.Kind == TranscriptKinds.UserPrompt && e.Text == queued.Body);
            receipt.ShouldNotBeNull("C959-v21-receipt");
            queued.Body.ShouldContain(Body, customMessage: "C959-pc-187");
            queued.Body.ShouldContain("[antiphon-task:" + task.Id.ToString("N")[..8]);
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning && e.Detail!.Contains("codex_cli_version"))).ShouldBe(0, "C959-pc-189");
        }
    }

    [Test]
    public async Task C959_Retry_and_cancellation_recheck()
    {
        await using var k = await Kit.CreateAsync();
        var created = await k.Service.CreateAsync(k.Request(false), k.Caller, CancellationToken.None);
        var task = await k.Db.AgentTasks.SingleAsync(t => t.Id == created.Id);
        task.Status = AgentTaskStatus.Failed;
        await k.Db.SaveChangesAsync();
        k.Local.Sample = new("0.156.1", T, null, new string('a',64));
        (await FailureAsync(() => k.Service.RetryAsync(task.Id, CancellationToken.None)))?.Code
            .ShouldBe("codex_cli_version_too_old", "C959-pc-193");
        task.Status.ShouldBe(AgentTaskStatus.Failed, "C959-v22-state");
        (await k.Db.AgentTaskEvents.CountAsync(e => e.Type == AgentTaskEventType.Retried)).ShouldBe(0);
        k.Local.Sample = null;
        (await FailureAsync(() => k.Service.RetryAsync(task.Id, CancellationToken.None)))?.Code
            .ShouldBe("codex_cli_version_unknown", "C959-pc-198");
        k.Local.Sample = new("0.159.1", T, null, new string('a',64));
        (await k.Service.RetryAsync(task.Id, CancellationToken.None)).Status.ShouldBe(AgentTaskStatus.Queued);
        using var cancel = new CancellationTokenSource();
        k.Local.Probe = async ct => { cancel.Cancel(); await Task.Delay(Timeout.Infinite, ct); return null; };
        await Should.ThrowAsync<OperationCanceledException>(() => k.Service.CreateAsync(k.Request(false), k.Caller, cancel.Token));
        (await k.Db.AgentTasks.CountAsync()).ShouldBe(1, "C959-pc-195");
    }

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
            ModelLevel: AgentModelLevel.High, Workspace: WorkspaceMode.Shared, RunnerId: remote ? "runner-a" : "local");
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
        public AppDbContext Context() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));
        public static async Task<DispatchKit> BuildAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var client = new Client();
            Factory? factory = null;
            var h = await BridgeQueueHarness.CreateAsync(new()
            {
                AlwaysOn = false, ConnectionString = schema.ConnectionString,
                Delegation = new() { DefaultWorkerWorkspace = WorkspaceMode.Shared, MaxConcurrentTasks = 20, RolePolicy = new(), PoolIdleRetireMinutes = 525600 },
                ConfigureServices = services =>
                {
                    services.AddSingleton<ISessionRunnerDirectory>(new Directory(client, new Client()));
                    services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(Registry()));
                    services.AddSingleton<IOptions<AgentRegistrySettings>>(Options.Create(Registry()));
                    services.AddSingleton<IAgentProtocolAdapterFactory>(sp => factory = new(sp.GetRequiredService<AgentSessionRuntime>(), schema.ConnectionString));
                    services.AddSingleton<DelegationWorkspaceResolver>();
                    services.AddDelegationWorktreeGraph(new GitSettings());
                    services.AddScoped<AgentTaskService>();
                    services.AddScoped<IDelegateSessionStopper>(sp => sp.GetRequiredService<AgentSessionService>());
                    services.AddScoped<AgentTaskDispatcher>();
                },
            });
            return new() { Schema = schema, Harness = h, Client = client, Factory = (Factory)h.Provider.GetRequiredService<IAgentProtocolAdapterFactory>() };
        }
        public async Task<AgentTaskSummaryDto> CreateAsync()
        {
            using var scope = Harness.Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(new(Body, Role: AgentTaskRole.Docs,
                AgentKind: AgentKind.Codex, ModelLevel: AgentModelLevel.High, Workspace: WorkspaceMode.Shared, RunnerId: "local"),
                new(null, null, Path.Combine(Harness.TempRoot, "workspace")), CancellationToken.None);
        }
        public async Task TickAsync()
        {
            using var scope = Harness.Provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
        }
        public async ValueTask DisposeAsync() { await Harness.DisposeAsync(); await Schema.DisposeAsync(); }
    }
    private sealed class Factory(AgentSessionRuntime runtime, string connection) : IAgentProtocolAdapterFactory
    {
        public List<FakeAgentProtocolAdapter> Created { get; } = [];
        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            var adapter = new FakeAgentProtocolAdapter { RegisterOnStart = runtime };
            adapter.OnSubmitted = async text =>
            {
                await BridgeQueueHarness.InsertEntryAsync(adapter.StartedSessionId!.Value, TranscriptKinds.UserPrompt, text,
                    timestamp: DateTime.UtcNow, connectionString: connection, createdAtUtc: DateTime.UtcNow);
                await BridgeQueueHarness.InsertEntryAsync(adapter.StartedSessionId.Value, TranscriptKinds.TurnEnd,
                    stopReason: "end_turn", connectionString: connection);
            };
            Created.Add(adapter);
            return adapter;
        }
    }
    private sealed class Directory(Client local, Client remote) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => local;
        public IReadOnlyList<string> KnownRunnerIds => ["runner-a"];
        public ISessionRunnerClient Resolve(string? runnerId) => string.IsNullOrWhiteSpace(runnerId) ? local : remote;
        public Guid? GetLiveStoreId(string? runnerId) => null;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) => Task.FromResult<RunnerInventory>(new RunnerInventory.Available([]));
    }
    private sealed class Client : ISessionRunnerClient
    {
        public RunnerCodexCliVersionDto? Sample { get; set; } = new("0.160.0", T, null, new string('a',64));
        public bool? AuthPresent { get; set; } = true;
        public Func<CancellationToken, Task<RunnerCodexCliVersionDto?>>? Probe { get; set; }
        public List<RunnerCodexCliProbeRequest> Requests { get; } = [];
        public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) => Task.FromResult<RunnerCapabilitiesDto?>(new("test", "test", "test", false, "d40c1670", Platform: "linux", CodexCliVersion: "0.160.0", CodexCliVersionCheckedAtUtc: T, CodexCliLauncherFingerprint: new string('a',64)));
        public Task<RunnerCodexCliVersionDto?> GetCodexCliVersionAsync(RunnerCodexCliProbeRequest request, CancellationToken ct)
        { Requests.Add(request); return Probe is null ? Task.FromResult(Sample) : Probe(ct); }
        public Task<RunnerProviderAuthDto?> GetProviderAuthAsync(string provider, CancellationToken ct) => Task.FromResult<RunnerProviderAuthDto?>(new(provider, AuthPresent, null, null, T, null));
        public Task<SessionRunnerSessionDto> StartAsync(Guid id, AgentLaunchSpec spec, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        public Task<SessionRunnerSessionDto> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task SendInputAsync(Guid id, string input, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> KillAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) { await Task.CompletedTask; yield break; }
    }
    private static AgentRegistrySettings Registry() => new()
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
