using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0398 S1/S3/S4: hashed-at-rest capability principal, scoped create, revoke/rotate,
/// Codex session pin, hold/quota fail-fast. Isolated schema (Gotcha #24).
/// </summary>
[Category("Integration")]
public class DelegationCapabilityTests
{
    [Test]
    public async Task capability_create_into_first_root_succeeds_with_empty_AllowedRoots()
    {
        await using var h = await Harness.CreateAsync();
        var issued = await h.IssueAsync([h.FirstRoot]);
        var caller = await h.Tasks.AuthenticateAsync(issued.Token, CancellationToken.None);

        var created = await h.Tasks.CreateAsync(Worker(h.FirstRoot), caller, CancellationToken.None);

        created.NoReplyRouting.ShouldBeTrue();
        caller.SessionId.ShouldBeNull();
        caller.CapabilityId.ShouldBe(issued.Id);
        var row = await h.Db.AgentTasks.SingleAsync(t => t.Id == created.Id);
        row.ReplyTo.ShouldBe(AgentTaskReplyTo.None);
        row.ParentSessionId.ShouldBeNull();
        row.WorkingDirectory.ShouldBe(Path.GetFullPath(h.FirstRoot));
    }

    [Test]
    public async Task capability_second_root_is_accepted()
    {
        await using var h = await Harness.CreateAsync();
        var issued = await h.IssueAsync([h.FirstRoot, h.SecondRoot]);
        var caller = await h.Tasks.AuthenticateAsync(issued.Token, CancellationToken.None);

        var created = await h.Tasks.CreateAsync(Worker(h.SecondRoot), caller, CancellationToken.None);

        (await h.Db.AgentTasks.SingleAsync(t => t.Id == created.Id))
            .WorkingDirectory.ShouldBe(Path.GetFullPath(h.SecondRoot));
    }

    [Test]
    public async Task capability_create_outside_its_roots_is_422_names_capability_does_not_advise_AllowedRoots()
    {
        await using var h = await Harness.CreateAsync();
        var issued = await h.IssueAsync([h.FirstRoot], name: "codex-antiphon");
        var caller = await h.Tasks.AuthenticateAsync(issued.Token, CancellationToken.None);

        var ex = await Should.ThrowAsync<ValidationException>(
            () => h.Tasks.CreateAsync(Worker(h.OutsideRoot), caller, CancellationToken.None));

        var detail = Detail(ex);
        detail.ShouldContain("codex-antiphon");
        detail.ShouldNotContain("AllowedRoots");
        detail.ShouldNotContain("Add it to Delegation:AllowedRoots");
    }

    [Test]
    public async Task capability_create_outside_its_roots_is_422_even_when_AllowedRoots_contains_that_path()
    {
        await using var h = await Harness.CreateAsync(allowedRoots: null);
        h.Settings.AllowedRoots = [h.OutsideRoot];
        var issued = await h.IssueAsync([h.FirstRoot]);
        var caller = await h.Tasks.AuthenticateAsync(issued.Token, CancellationToken.None);

        var ex = await Should.ThrowAsync<ValidationException>(
            () => h.Tasks.CreateAsync(Worker(h.OutsideRoot), caller, CancellationToken.None));

        Detail(ex).ShouldContain(issued.Name);
        (await h.Db.AgentTasks.CountAsync(t => t.Goal == "Do the thing.")).ShouldBe(0);
    }

    [Test]
    public async Task capability_prefix_neighbour_is_not_within_root()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var parent = Directory.CreateTempSubdirectory("cap-prefix");
        try
        {
            var root = Directory.CreateDirectory(Path.Combine(parent.FullName, "antiphon")).FullName;
            var neighbour = Directory.CreateDirectory(Path.Combine(parent.FullName, "antiphon-evil")).FullName;
            await using var db = NewDb(schema);
            var caps = Caps(db);
            var issued = await caps.IssueAsync(new IssueDelegationCapabilityRequest("prefix-cap", [root]), CancellationToken.None);
            var tasks = Tasks(db);
            var caller = await tasks.AuthenticateAsync(issued.Token, CancellationToken.None);

            var ex = await Should.ThrowAsync<ValidationException>(
                () => tasks.CreateAsync(Worker(neighbour), caller, CancellationToken.None));
            Detail(ex).ShouldContain("prefix-cap");
        }
        finally
        {
            parent.Delete(true);
        }
    }

    [Test]
    public async Task token_less_create_into_capability_root_is_still_422()
    {
        await using var h = await Harness.CreateAsync();
        await h.IssueAsync([h.FirstRoot]);

        var ex = await Should.ThrowAsync<ValidationException>(
            () => h.Tasks.CreateAsync(
                Worker(h.FirstRoot),
                new AgentTaskService.Caller(null, null, string.Empty),
                CancellationToken.None));

        Detail(ex).ShouldContain("AllowedRoots");
    }

    [Test]
    public async Task authenticate_raw_token_returns_MayDelegate_and_first_root()
    {
        await using var h = await Harness.CreateAsync();
        var issued = await h.IssueAsync([h.FirstRoot, h.SecondRoot]);
        (await h.Db.DelegationCapabilities.SingleAsync(c => c.Id == issued.Id)).LastUsedAt.ShouldBeNull();

        var caller = await h.Tasks.AuthenticateAsync(issued.Token, CancellationToken.None);

        caller.MayDelegate.ShouldBeTrue();
        caller.SessionId.ShouldBeNull();
        caller.CapabilityId.ShouldBe(issued.Id);
        Path.GetFullPath(caller.WorkingDirectory).ShouldBe(Path.GetFullPath(h.FirstRoot));
        var row = await h.Db.DelegationCapabilities.SingleAsync(c => c.Id == issued.Id);
        row.LastUsedAt.ShouldNotBeNull();
        row.LastUsedAt.Value.ShouldBeGreaterThanOrEqualTo(row.CreatedAt);
    }

    [Test]
    public async Task issue_stores_hash_not_raw()
    {
        await using var h = await Harness.CreateAsync();
        var issued = await h.IssueAsync([h.FirstRoot]);
        var row = await h.Db.DelegationCapabilities.SingleAsync(c => c.Id == issued.Id);
        row.TokenHash.ShouldBe(AgentTaskService.HashToken(issued.Token));
        row.TokenHash.ShouldNotBe(issued.Token);
        issued.Token.Length.ShouldBe(64);
    }

    [Test]
    public async Task board_constraint_card_on_another_board_is_422()
    {
        await using var h = await Harness.CreateAsync();
        var (allowedBoard, _) = await h.SeedBoardAsync();
        var (otherBoard, otherCard) = await h.SeedBoardAsync();
        var issued = await h.IssueAsync([h.FirstRoot], boardId: allowedBoard.Id);
        var caller = await h.Tasks.AuthenticateAsync(issued.Token, CancellationToken.None);

        var ex = await Should.ThrowAsync<ValidationException>(
            () => h.Tasks.CreateAsync(
                Worker(h.FirstRoot) with { Card = otherCard.Id.ToString() },
                caller,
                CancellationToken.None));

        Detail(ex).ShouldContain(issued.Name);
        otherBoard.Id.ShouldNotBe(allowedBoard.Id);
    }

    [Test]
    public async Task revoked_bearer_is_403_on_the_next_authenticate()
    {
        await using var h = await Harness.CreateAsync();
        var issued = await h.IssueAsync([h.FirstRoot]);
        await h.Caps.RevokeAsync(issued.Id, CancellationToken.None);

        var ex = await Should.ThrowAsync<ForbiddenException>(
            () => h.Tasks.AuthenticateAsync(issued.Token, CancellationToken.None));
        ex.Message.ShouldContain("capability revoked");
        ex.StatusCode.ShouldBe(403);
    }

    [Test]
    public async Task rotated_old_bearer_is_403_new_bearer_works()
    {
        await using var h = await Harness.CreateAsync();
        var issued = await h.IssueAsync([h.FirstRoot]);
        var rotated = await h.Caps.RotateAsync(issued.Id, CancellationToken.None);

        var old = await Should.ThrowAsync<ForbiddenException>(
            () => h.Tasks.AuthenticateAsync(issued.Token, CancellationToken.None));
        old.StatusCode.ShouldBe(403);

        var caller = await h.Tasks.AuthenticateAsync(rotated.Token, CancellationToken.None);
        var created = await h.Tasks.CreateAsync(Worker(h.FirstRoot), caller, CancellationToken.None);
        created.Id.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task capability_child_worker_token_cannot_create()
    {
        await using var h = await Harness.CreateAsync();
        var issued = await h.IssueAsync([h.FirstRoot]);
        var caller = await h.Tasks.AuthenticateAsync(issued.Token, CancellationToken.None);
        var child = await h.Tasks.CreateAsync(Worker(h.FirstRoot), caller, CancellationToken.None);
        AgentTaskService.RawTokens.TryGetValue(child.Id, out var childToken).ShouldBeTrue();

        var worker = await h.Tasks.AuthenticateAsync(childToken!, CancellationToken.None);
        worker.MayDelegate.ShouldBeFalse();
        var ex = await Should.ThrowAsync<ForbiddenException>(
            () => h.Tasks.CreateAsync(
                Worker(h.FirstRoot) with { Goal = "worker fan-out" },
                worker,
                CancellationToken.None));
        ex.Message.ShouldContain("Workers cannot delegate");
        (await h.Db.AgentTasks.CountAsync(t => t.Goal == "worker fan-out")).ShouldBe(0);
    }

    [Test]
    public async Task codex_session_hash_still_MayDelegate()
    {
        await using var h = await Harness.CreateAsync();
        var raw = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        raw = raw[..64];
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        h.Db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "codex-named",
            AgentKind = AgentKind.Codex,
            Status = SessionStatus.Running,
            Cwd = h.FirstRoot,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
            DelegationTokenHash = AgentTaskService.HashToken(raw),
        });
        await h.Db.SaveChangesAsync();

        var caller = await h.Tasks.AuthenticateAsync(raw, CancellationToken.None);
        caller.MayDelegate.ShouldBeTrue();
        caller.SessionId.ShouldBe(sessionId);
        caller.CapabilityId.ShouldBeNull();
        Path.GetFullPath(caller.WorkingDirectory).ShouldBe(Path.GetFullPath(h.FirstRoot));
    }

    [Test]
    public async Task capability_caller_orchestrator_kind_Codex_is_still_422()
    {
        await using var h = await Harness.CreateAsync();
        var issued = await h.IssueAsync([h.FirstRoot]);
        var caller = await h.Tasks.AuthenticateAsync(issued.Token, CancellationToken.None);

        var ex = await Should.ThrowAsync<ValidationException>(
            () => h.Tasks.CreateAsync(
                new CreateAgentTaskRequest(
                    Goal: "own this chunk",
                    Kind: AgentTaskKind.Orchestrator,
                    WorkingDirectory: h.FirstRoot) { AgentKind = AgentKind.Codex },
                caller,
                CancellationToken.None));
        var detail = Detail(ex);
        detail.ShouldContain("orchestrator");
        detail.ShouldContain("Codex");
    }

    [Test]
    public async Task issue_validation_rejects_name_filesystem_root_and_root_count()
    {
        await using var h = await Harness.CreateAsync();

        await Should.ThrowAsync<ValidationException>(
            () => h.Caps.IssueAsync(new IssueDelegationCapabilityRequest("bad name", [h.FirstRoot]), CancellationToken.None));
        await Should.ThrowAsync<ValidationException>(
            () => h.Caps.IssueAsync(new IssueDelegationCapabilityRequest("root-cap", [@"C:\"]), CancellationToken.None));
        await Should.ThrowAsync<ValidationException>(
            () => h.Caps.IssueAsync(new IssueDelegationCapabilityRequest("zero", []), CancellationToken.None));

        var nine = Enumerable.Range(0, 9)
            .Select(_ => Directory.CreateTempSubdirectory("cap-nine").FullName)
            .ToArray();
        try
        {
            await Should.ThrowAsync<ValidationException>(
                () => h.Caps.IssueAsync(new IssueDelegationCapabilityRequest("nine", nine), CancellationToken.None));
        }
        finally
        {
            foreach (var dir in nine)
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        await Should.ThrowAsync<ValidationException>(
            () => h.Caps.IssueAsync(
                new IssueDelegationCapabilityRequest("missing", [Path.Combine(h.FirstRoot, "no-such-dir")]),
                CancellationToken.None));

        (await h.Db.DelegationCapabilities.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task capability_does_not_enter_RawTokens()
    {
        await using var h = await Harness.CreateAsync();
        var issued = await h.IssueAsync([h.FirstRoot]);
        AgentTaskService.RawTokens.ContainsKey(issued.Id).ShouldBeFalse();
    }

    [Test]
    public async Task event_detail_has_name_and_roots_never_token_or_hash()
    {
        await using var h = await Harness.CreateAsync();
        var issued = await h.IssueAsync([h.FirstRoot], name: "audit-cap");
        var rotated = await h.Caps.RotateAsync(issued.Id, CancellationToken.None);
        await h.Caps.RevokeAsync(issued.Id, CancellationToken.None);
        var secrets = new[]
        {
            issued.Token,
            rotated.Token,
            AgentTaskService.HashToken(issued.Token),
            AgentTaskService.HashToken(rotated.Token),
        };

        var events = await h.Db.DelegationCapabilityEvents
            .Where(e => e.CapabilityId == issued.Id)
            .ToListAsync();
        events.Select(e => e.Type).ShouldBe(
            [DelegationCapabilityEventType.Issued, DelegationCapabilityEventType.Rotated, DelegationCapabilityEventType.Revoked],
            ignoreOrder: true);
        foreach (var ev in events)
        {
            ev.Detail.ShouldContain("audit-cap");
            ev.Detail.ShouldContain(Path.GetFullPath(h.FirstRoot));
            foreach (var secret in secrets)
                ev.Detail.ShouldNotContain(secret);
        }
    }

    [Test]
    public async Task capability_create_Kind_Codex_succeeds_under_Claude_kind_wide_hold()
    {
        await using var h = await Harness.CreateAsync();
        await SeedHoldAsync(h.Db, AgentKind.ClaudeCode, ModelAlias.KindWide);
        var issued = await h.IssueAsync([h.FirstRoot]);
        var caller = await h.Tasks.AuthenticateAsync(issued.Token, CancellationToken.None);

        var created = await h.Tasks.CreateAsync(
            Worker(h.FirstRoot) with { AgentKind = AgentKind.Codex, Goal = "codex under hold" },
            caller,
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.Codex);
        (await h.Db.AgentTasks.SingleAsync(t => t.Id == created.Id)).AgentKind.ShouldBe(AgentKind.Codex);
    }

    [Test]
    public async Task capability_create_default_kind_is_409_model_disabled_under_Claude_hold_not_Codex()
    {
        await using var h = await Harness.CreateAsync();
        await SeedHoldAsync(h.Db, AgentKind.ClaudeCode, ModelAlias.KindWide);
        var issued = await h.IssueAsync([h.FirstRoot]);
        var caller = await h.Tasks.AuthenticateAsync(issued.Token, CancellationToken.None);

        var ex = await Should.ThrowAsync<ModelDisabledException>(
            () => h.Tasks.CreateAsync(
                Worker(h.FirstRoot) with { Goal = "default kind under hold" },
                caller,
                CancellationToken.None));
        ex.Code.ShouldBe("model_disabled");
        (await h.Db.AgentTasks.CountAsync(t => t.Goal == "default kind under hold")).ShouldBe(0);
    }

    [Test]
    public async Task capability_create_low_Codex_quota_is_409_not_rerouted()
    {
        await using var h = await Harness.CreateAsync();
        var (agentId, profileId) = await SeedStandingCodexAsync(h.Db, h.FirstRoot);
        await SeedUsageSampleAsync(h.Db, AgentKind.Codex, profileId.ToString("D"), remaining: 3);
        var issued = await h.IssueAsync([h.FirstRoot]);
        var caller = await h.Tasks.AuthenticateAsync(issued.Token, CancellationToken.None);
        var tasks = Tasks(h.Db, quotaGate: CreateGate(h.Db));

        var ex = await Should.ThrowAsync<SubscriptionQuotaLowException>(
            () => tasks.CreateAsync(
                Worker(h.FirstRoot) with { Goal = "low quota", AgentId = agentId },
                caller,
                CancellationToken.None));
        ex.Code.ShouldBe("subscription_quota_low");
        (await h.Db.AgentTasks.CountAsync(t => t.Goal == "low quota")).ShouldBe(0);
    }

    private static string Detail(ValidationException ex) =>
        string.Join(" ", ex.Errors.SelectMany(e => e.Value));

    private static CreateAgentTaskRequest Worker(string directory) => new(
        Goal: "Do the thing.",
        Kind: AgentTaskKind.Worker,
        Role: AgentTaskRole.Docs,
        WorkingDirectory: directory);

    private static async Task SeedHoldAsync(AppDbContext db, AgentKind kind, string alias)
    {
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            ModelAlias = alias,
            Source = ModelAvailabilitySource.Manual,
            DisabledUntil = null,
            HitAt = DateTime.UtcNow,
            Reason = "kind-wide hold",
        });
        await db.SaveChangesAsync();
    }

    private static async Task<(Guid AgentId, Guid ProfileId)> SeedStandingCodexAsync(AppDbContext db, string directory)
    {
        var now = DateTime.UtcNow;
        var profile = new AgentTuiProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = $"codex-cap-{Guid.NewGuid():N}"[..24],
            Kind = AgentKind.Codex,
            IsEnabled = true,
            Source = AgentTuiProfileSource.Operator,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Codex standing cap",
            Slug = $"cap-{Guid.NewGuid():N}"[..16],
            WorkingDirectory = directory,
            Details = "Standing Codex for CARD-0398 V-35.",
            Status = AgentStatus.Idle,
            ModelLevel = AgentModelLevel.High,
            Kind = AgentKind.Codex,
            TuiProfileId = profile.Id,
            AlwaysOn = false,
            IsPoolDelegate = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AgentTuiProfiles.Add(profile);
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return (agent.Id, profile.Id);
    }

    private static async Task SeedUsageSampleAsync(AppDbContext db, AgentKind provider, string key, double remaining)
    {
        var now = DateTime.UtcNow;
        db.SubscriptionUsageSamples.Add(new SubscriptionUsageSample
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            SubscriptionKey = key,
            PlanLabel = "SuperPlan",
            RemainingPercent = remaining,
            ResetsAt = now.AddHours(36),
            ObservedAt = now,
            AgentSessionId = Guid.NewGuid(),
            SourceCommand = "/status",
            ParseStatus = SubscriptionUsageParseStatus.Parsed,
            RawExcerpt = "seeded",
        });
        await db.SaveChangesAsync();
    }

    private static SubscriptionQuotaGate CreateGate(AppDbContext db) =>
        new(
            new SubscriptionUsageReader(db, TimeProvider.System),
            Options.Create(new SubscriptionQuotaGateSettings()),
            TimeProvider.System,
            NullLogger<SubscriptionQuotaGate>.Instance);

    private static AppDbContext NewDb(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static DelegationCapabilityService Caps(AppDbContext db) =>
        new(db, TimeProvider.System, NullLogger<DelegationCapabilityService>.Instance);

    private static AgentTaskService Tasks(
        AppDbContext db,
        DelegationSettings? settings = null,
        SubscriptionQuotaGate? quotaGate = null)
    {
        settings ??= new DelegationSettings { AllowedRoots = [] };
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(settings),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            quotaGate: quotaGate,
            modelAvailability: new ModelAvailability(db, TimeProvider.System, NullLogger<ModelAvailability>.Instance));
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly List<string> _dirs = [];

        private Harness(IsolatedTestSchema schema, AppDbContext db, DelegationSettings settings)
        {
            _schema = schema;
            Db = db;
            Settings = settings;
            FirstRoot = TempDir("cap-first");
            SecondRoot = TempDir("cap-second");
            OutsideRoot = TempDir("cap-outside");
            Caps = DelegationCapabilityTests.Caps(db);
            Tasks = DelegationCapabilityTests.Tasks(db, settings);
        }

        public AppDbContext Db { get; }
        public DelegationSettings Settings { get; }
        public DelegationCapabilityService Caps { get; }
        public AgentTaskService Tasks { get; }
        public string FirstRoot { get; }
        public string SecondRoot { get; }
        public string OutsideRoot { get; }

        public static async Task<Harness> CreateAsync(IReadOnlyList<string>? allowedRoots = null)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var db = NewDb(schema);
            var settings = new DelegationSettings { AllowedRoots = allowedRoots is null ? [] : [.. allowedRoots] };
            return new Harness(schema, db, settings);
        }

        public Task<DelegationCapabilityIssuedDto> IssueAsync(
            IReadOnlyList<string> roots, string? name = null, Guid? boardId = null, Guid? projectId = null) =>
            Caps.IssueAsync(
                new IssueDelegationCapabilityRequest(
                    name ?? $"cap-{Guid.NewGuid():N}"[..20],
                    roots,
                    boardId,
                    projectId),
                CancellationToken.None);

        public async Task<(Board Board, Card Card)> SeedBoardAsync()
        {
            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = $"cap-project-{Guid.NewGuid():N}",
                GitRepositoryUrl = "https://example.test/cap.git",
                CreatedAt = now,
                UpdatedAt = now,
            };
            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = $"Cap board {Guid.NewGuid():N}",
                MaxConcurrentSessions = 1,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var column = new BoardColumn
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                StateKey = "backlog",
                Name = "Backlog",
                ColumnOrder = 0,
                CardStatus = CardStatus.Backlog,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var card = new Card
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                BoardColumnId = column.Id,
                Identifier = $"CARD-{(cardNumber++):0000}",
                Title = "Capability board constraint",
                Description = "test",
                CreatedAt = now,
                UpdatedAt = now,
            };
            Db.AddRange(project, board, column, card);
            await Db.SaveChangesAsync();
            return (board, card);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _schema.DisposeAsync();
            foreach (var dir in _dirs)
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        private string TempDir(string prefix)
        {
            var dir = Directory.CreateTempSubdirectory(prefix).FullName;
            _dirs.Add(dir);
            return dir;
        }

        private static int cardNumber = 3900;
    }
}
