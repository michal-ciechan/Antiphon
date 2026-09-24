using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0659 V-3. The default host is chosen from the kind the routing pins and walks actually
/// settled on, never from the raw request: a pin that lands on Codex stays on the desktop, one that
/// lands on Grok/Claude takes the default, a Required-pin conflict refuses before any placement,
/// and an exhausted walk stays local. SourceLanding uses the SELECTED host's custody: a valid
/// Mutation shape can take the default, the dispatch gate admits it, and custody admission asks
/// that runner and refuses rather than falling back.
/// </summary>
[Category("Integration")]
public sealed class DefaultRunnerPinTests
{
    [Test]
    public async Task Kind_pin_is_resolved_before_host()
    {
        foreach (var (row, pin, request, hold, expectedKind, expectedRunner, reason) in new (string, PutRoutingPinRequest, CreateAgentTaskRequest, string?, AgentKind, string?, string)[]
                 {
                     ("preferred pin on Codex stays local",
                         Pin(RoutingPinStrength.Preferred, (AgentKind.Codex, AgentModelLevel.High)),
                         Code("c659 prefer codex"), null, AgentKind.Codex, null, "kind_not_supported"),
                     ("required pin on Codex stays local",
                         Pin(RoutingPinStrength.Required, (AgentKind.Codex, AgentModelLevel.High)),
                         Code("c659 require codex"), null, AgentKind.Codex, null, "kind_not_supported"),
                     ("preferred pin on Grok takes the default",
                         Pin(RoutingPinStrength.Preferred, (AgentKind.Grok, AgentModelLevel.High)),
                         Code("c659 prefer grok"), null, AgentKind.Grok, "server2", "eligible"),
                     ("required pin on Claude takes the default",
                         Pin(RoutingPinStrength.Required, (AgentKind.ClaudeCode, AgentModelLevel.High)),
                         Code("c659 require claude"), null, AgentKind.ClaudeCode, "server2", "eligible"),
                     ("multi-candidate: held Claude head, final Grok takes the default",
                         Pin(RoutingPinStrength.Required, (AgentKind.ClaudeCode, AgentModelLevel.Frontier), (AgentKind.Grok, AgentModelLevel.Frontier)),
                         Code("c659 walk to grok"), "fable", AgentKind.Grok, "server2", "eligible"),
                     ("multi-candidate: held Claude head, final Codex stays local",
                         Pin(RoutingPinStrength.Required, (AgentKind.ClaudeCode, AgentModelLevel.Frontier), (AgentKind.Codex, AgentModelLevel.Frontier)),
                         Code("c659 walk to codex"), "fable", AgentKind.Codex, null, "kind_not_supported"),
                     ("ignored Codex pin: the request's Claude takes the default",
                         Pin(RoutingPinStrength.Required, (AgentKind.Codex, AgentModelLevel.High)),
                         Code("c659 ignore pin", AgentKind.ClaudeCode) with { IgnoreRoutingPin = true },
                         null, AgentKind.ClaudeCode, "server2", "eligible"),
                 })
        {
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: "server2");
            await using var db = kit.Context();
            await Pins(db).UpsertAsync(pin, null, CancellationToken.None);
            if (hold is not null)
                await SeedHoldAsync(db, AgentKind.ClaudeCode, hold);

            var created = await RoutedService(kit, db).CreateAsync(request, kit.Caller, CancellationToken.None);

            var saved = await kit.ReadAsync(created.Id);
            saved.Task.AgentKind.ShouldBe(expectedKind, row);
            saved.Task.RunnerId.ShouldBe(expectedRunner, row);
            saved.Task.Workspace.ShouldBe(WorkspaceMode.Worktree, row);
            saved.Created.ShouldContain(
                $"runner source=default requested=unset default=server2 selected={expectedRunner ?? "local"} reason={reason}",
                Case.Sensitive, row);
            DefaultRunnerKit.Occurrences(saved.Created, "runner source=").ShouldBe(1, row);
            saved.Warnings.ShouldNotContain(w => w.Contains("Default runner", StringComparison.Ordinal), row);
            kit.Directory.ResolveCalls.Count.ShouldBe(expectedRunner is null ? 0 : 1,
                row + ": only a compatible pinned kind consults the readiness gate");
        }
    }

    [Test]
    public async Task Agent_pin_reuses_workspace_with_local_sentinel()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: "server2");
        var standing = await kit.SeedStandingAgentAsync();
        await using var db = kit.Context();
        await Pins(db).UpsertAsync(new PutRoutingPinRequest(
            AgentTaskRole.Code, Provenance: RoutingPinProvenance.Human, Strength: RoutingPinStrength.Preferred,
            AgentId: standing.Id, Reason: "c659 seat pin"), null, CancellationToken.None);
        var service = RoutedService(kit, db);

        foreach (var (row, runnerId, audit) in new (string, string?, string)[]
                 {
                     ("-Local with a configured agent pin", "local",
                         "runner source=explicit-local requested=local default=server2 selected=local reason=local_requested"),
                     ("omitted runner with a configured agent pin", null,
                         "runner source=default requested=unset default=server2 selected=local reason=existing_process"),
                 })
        {
            var created = await service.CreateAsync(
                new CreateAgentTaskRequest("c659 seat " + Guid.NewGuid().ToString("N"), Role: AgentTaskRole.Code, RunnerId: runnerId),
                kit.Caller, CancellationToken.None);

            var saved = await kit.ReadAsync(created.Id);
            saved.Task.AgentId.ShouldBe(standing.Id, row);
            saved.Task.Workspace.ShouldBe(WorkspaceMode.Shared,
                row + ": CARD-0644 reuses the pinned agent's checkout; the local sentinel must not defeat that");
            saved.Task.RunnerId.ShouldBeNull(row);
            saved.Created.ShouldContain(audit, Case.Sensitive, row);
            DefaultRunnerKit.Occurrences(saved.Created, "runner source=").ShouldBe(1, row);
        }

        kit.Directory.ResolveCalls.ShouldBeEmpty("an agent-pinned task never consults the readiness gate");
    }

    [Test]
    public async Task Required_pin_conflict_is_unchanged()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: "server2");
        await using var db = kit.Context();
        await Pins(db).UpsertAsync(
            Pin(RoutingPinStrength.Required, (AgentKind.ClaudeCode, AgentModelLevel.Frontier), (AgentKind.ClaudeCode, AgentModelLevel.High)),
            null, CancellationToken.None);
        var service = RoutedService(kit, db);
        var before = await kit.TaskCountAsync();

        // A request that disagrees with a Required pin is refused exactly as before, with or
        // without a runner in play.
        foreach (var runnerId in new string?[] { null, "local", "server2" })
        {
            var refused = await Should.ThrowAsync<RoutingPinConflictException>(() => service.CreateAsync(
                Code("c659 conflict", AgentKind.Grok) with { RunnerId = runnerId }, kit.Caller, CancellationToken.None));
            refused.Code.ShouldBe("routing_pin_conflict", runnerId ?? "<omitted>");
        }

        (await kit.TaskCountAsync()).ShouldBe(before, "a pin conflict inserts nothing");
        kit.Directory.ResolveCalls.ShouldBeEmpty("a pin conflict refuses before any runner selection");

        // Every Required candidate held: the exhausted walk is Blocked on the desktop, and the
        // failed head is not presented as a selected remote candidate.
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "fable");
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "opus");
        var blocked = await service.CreateAsync(Code("c659 exhausted"), kit.Caller, CancellationToken.None);

        var saved = await kit.ReadAsync(blocked.Id);
        saved.Task.Status.ShouldBe(AgentTaskStatus.Blocked);
        saved.Task.FailureReason.ShouldNotBeNull().ShouldStartWith(ComplexityRoutingService.RoutingExhaustedPrefix);
        saved.Task.RunnerId.ShouldBeNull("routing exhaustion keeps the task local");
        saved.Created.ShouldContain(
            "runner source=default requested=unset default=server2 selected=local reason=routing_exhausted", Case.Sensitive);
        kit.Directory.ResolveCalls.ShouldBeEmpty("an exhausted walk never consults the readiness gate");

        // Explicit remote + Codex still refuses at create.
        await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(
            new CreateAgentTaskRequest("c659 codex remote", Role: AgentTaskRole.Review, AgentKind: AgentKind.Codex,
                Workspace: WorkspaceMode.Worktree, RunnerId: "server2"),
            kit.Caller, CancellationToken.None));
    }

    [Test]
    public async Task SourceLanding_uses_selected_host_custody()
    {
        var kit = DefaultRunnerKit.Create("unused", defaultRunnerId: "server2");
        var phoneHome = new PhoneHomeLaunchPolicy(Options.Create(kit.PhoneHome));
        var policy = new DefaultRunnerRoutingPolicy(kit.Settings, phoneHome, kit.Directory);
        var unset = RunnerRequestIntent.Parse(null);

        // The valid SourceLanding shape (fresh Worker/Mutation/Worktree, Grok or Claude) takes the
        // eligible default like any other fresh task: create admission and dispatch agree.
        foreach (var kind in new[] { AgentKind.Grok, AgentKind.ClaudeCode })
        {
            var decision = policy.Decide(unset, new DefaultRunnerShape(
                WorkspaceMode.Worktree, kind, AgentTaskKind.Worker, AgentTaskRole.Mutation,
                ExistingProcess: false, SourceLanding: true, RoutingExhausted: false)).ShouldNotBeNull();
            decision.SelectedRunnerId.ShouldBe("server2", kind.ToString());
            decision.AuditSegment.ShouldBe(
                "runner source=default requested=unset default=server2 selected=server2 reason=eligible", kind.ToString());
            decision.Warn.ShouldBeFalse(kind.ToString());
        }

        // An invalid SourceLanding role never selects a runner (create refuses that shape anyway).
        policy.Decide(unset, new DefaultRunnerShape(
                WorkspaceMode.Worktree, AgentKind.Grok, AgentTaskKind.Worker, AgentTaskRole.Code,
                ExistingProcess: false, SourceLanding: true, RoutingExhausted: false))
            .ShouldNotBeNull().SelectedRunnerId.ShouldBeNull("a non-Mutation SourceLanding shape stays local");
        // Codex SourceLanding stays local.
        policy.Decide(unset, new DefaultRunnerShape(
                WorkspaceMode.Worktree, AgentKind.Codex, AgentTaskKind.Worker, AgentTaskRole.Mutation,
                ExistingProcess: false, SourceLanding: true, RoutingExhausted: false))
            .ShouldNotBeNull().Reason.ShouldBe("kind_not_supported");

        // The dispatch gate admits the runner-bound SourceLanding Mutation that create admitted
        // (Cut B's runner custody), so an explicit or selected remote Mutation is not refused at
        // launch after create accepted it.
        var pool = new Agent
        {
            Id = Guid.NewGuid(),
            RunnerId = "server2",
            IsPoolDelegate = true,
            WorkingDirectory = kit.RepoRoot,
        };
        foreach (var kind in new[] { AgentKind.Grok, AgentKind.ClaudeCode })
        {
            Should.NotThrow(() => phoneHome.RefuseUnsupportedStart(pool, cardStart: false, delegatedTask: true,
                worktree: true, sourceLanding: true, onAgent: false, SessionBackend.PtyHost, kind, customWrapper: null),
                kind.ToString());
        }
        // Everything else about the remote shape is still refused at the gate.
        Should.Throw<ConflictException>(() => phoneHome.RefuseUnsupportedStart(pool, false, delegatedTask: true,
                worktree: false, sourceLanding: true, false, SessionBackend.PtyHost, AgentKind.Grok, null))
            .Code.ShouldBe("phone_home_worktree_refused");
        Should.Throw<ConflictException>(() => phoneHome.RefuseUnsupportedStart(pool, false, delegatedTask: true,
                worktree: true, sourceLanding: true, false, SessionBackend.PtyHost, AgentKind.Codex, null))
            .Code.ShouldBe("phone_home_kind_refused");
        // A runner-bound NAMED agent still has no SourceLanding shape.
        var named = new Agent { Id = Guid.NewGuid(), RunnerId = "server2", WorkingDirectory = kit.RepoRoot };
        Should.Throw<ConflictException>(() => phoneHome.RefuseUnsupportedStart(named, false, delegatedTask: false,
                worktree: false, sourceLanding: true, false, SessionBackend.PtyHost, AgentKind.Grok, null))
            .Code.ShouldBe("phone_home_sourcelanding_refused");

        // Custody admission asks the SELECTED runner, and a runner without custody is a refusal,
        // never a quiet fall back to the desktop's store.
        var admission = new SourceLandingAdmission(null!, null!, kit.Directory);
        var noCustody = await Should.ThrowAsync<ConflictException>(
            () => admission.RequireSupportAsync("server2", CancellationToken.None));
        noCustody.Code.ShouldBe("verification_custody_unsupported_backend");
        kit.Directory.ResolveCalls[^1].ShouldBe("server2");
        kit.Directory.ResolveCalls.ShouldNotContain((string?)null, "custody was never asked of the desktop");

        // An offline selected runner is the directory's own refusal, not a local answer.
        var offline = DefaultRunnerKit.Create("unused", defaultRunnerId: "server2", eligible: false);
        (await Should.ThrowAsync<ServiceUnavailableException>(
                () => new SourceLandingAdmission(null!, null!, offline.Directory).RequireSupportAsync("server2", CancellationToken.None)))
            .Code.ShouldBe(PhoneHomeProblemTypes.Unavailable);
    }

    private static CreateAgentTaskRequest Code(string goal, AgentKind? kind = null) =>
        new(goal + " " + Guid.NewGuid().ToString("N")[..8], Role: AgentTaskRole.Code, AgentKind: kind);

    private static PutRoutingPinRequest Pin(RoutingPinStrength strength, params (AgentKind Kind, AgentModelLevel Level)[] candidates) =>
        new(
            AgentTaskRole.Code,
            Provenance: RoutingPinProvenance.Human,
            Strength: strength,
            Candidates: candidates.Select(c => new RoutingCandidateRequest(c.Kind, c.Level)).ToList(),
            Reason: "c659 pin");

    private static RoutingPinService Pins(AppDbContext db) =>
        new(db, TimeProvider.System, NullLogger<RoutingPinService>.Instance);

    private static async Task SeedHoldAsync(AppDbContext db, AgentKind kind, string alias)
    {
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            ModelAlias = alias,
            Source = ModelAvailabilitySource.Manual,
            HitAt = DateTime.UtcNow,
            Reason = "c659 hold",
        });
        await db.SaveChangesAsync();
    }

    /// <summary>The kit's service plus the real pin/walk/availability services.</summary>
    internal static AgentTaskService RoutedService(DefaultRunnerKit kit, AppDbContext db)
    {
        var settings = Options.Create(kit.Settings);
        var availability = new ModelAvailability(db, TimeProvider.System, NullLogger<ModelAvailability>.Instance);
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            settings,
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            modelAvailability: availability,
            routingPins: Pins(db),
            complexityRouting: new ComplexityRoutingService(db, settings, TimeProvider.System, availability),
            phoneHome: new PhoneHomeLaunchPolicy(Options.Create(kit.PhoneHome)),
            runners: kit.Directory);
    }
}
