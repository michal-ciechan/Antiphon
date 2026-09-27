using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using System.Text.Json;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class AgentTaskWorktreeBaseCreateTests
{
    [Test]
    [Arguments("both_flags")]
    [Arguments("shared")]
    [Arguments("readonly")]
    [Arguments("onagent_task")]
    [Arguments("onagent_fresh")]
    [Arguments("task_without_card")]
    public async Task T0442_V11_invalid_override_shape_refuses_before_insert(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v11");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = AgentTaskDispatchBaseGuardTests.CreateContext(schema);
        var card = await AgentTaskDispatchBaseGuardTests.SeedCardAsync(db, "CARD-0442");
        var source = await AgentTaskDispatchBaseGuardTests.SeedKeptSiblingAsync(db, repo, card.Id, "A");
        await db.SaveChangesAsync();
        var before = await db.AgentTasks.CountAsync();
        var shortId = DelegationReportFormatter.Short(source.Id);
        var request = Request(scenario == "task_without_card" ? null : card.Id);
        request = scenario switch
        {
            "both_flags" => request with { WorktreeBaseTask = shortId, FreshWorktree = true },
            "shared" => request with { Workspace = WorkspaceMode.Shared, FreshWorktree = true },
            "readonly" => request with { Workspace = WorkspaceMode.ReadOnly, FreshWorktree = true },
            "onagent_task" => request with { AgentId = Guid.NewGuid(), WorktreeBaseTask = shortId },
            "onagent_fresh" => request with { AgentId = Guid.NewGuid(), FreshWorktree = true },
            "task_without_card" => request with { WorktreeBaseTask = shortId },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        await using var provider = AgentTaskDispatchBaseGuardTests.CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
        var error = await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(request,
            new AgentTaskService.Caller(null, null, repo.Path), CancellationToken.None));
        error.Code.ShouldBe(scenario == "task_without_card" ? "worktree_base_card_required" : "worktree_base_mode");
        (await db.AgentTasks.CountAsync()).ShouldBe(before);
        (await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim().ShouldNotBeEmpty();
    }

    [Test]
    [Arguments("cross_card")]
    [Arguments("cross_board")]
    [Arguments("nested_repo")]
    [Arguments("separate_clone")]
    [Arguments("destination_mismatch")]
    public async Task T0442_V12_explicit_source_cannot_cross_custody_or_destination(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v12");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await repo.GitAsync("branch", "release");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = AgentTaskDispatchBaseGuardTests.CreateContext(schema);
        var card = await AgentTaskDispatchBaseGuardTests.SeedCardAsync(db, "CARD-0442");
        var sourceCard = scenario is "cross_card" or "cross_board"
            ? await AgentTaskDispatchBaseGuardTests.SeedCardAsync(db,
                scenario == "cross_card" ? "CARD-0999" : "CARD-0442")
            : card;
        var source = await AgentTaskDispatchBaseGuardTests.SeedKeptSiblingAsync(db, repo,
            sourceCard.Id, "A");
        var sourceSha = (await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim();
        if (scenario is "nested_repo" or "separate_clone")
        {
            var foreign = scenario == "nested_repo"
                ? Path.Combine(repo.Path, "nested-" + Guid.NewGuid().ToString("N")[..8])
                : Path.Combine(repo.WorktreeRoot, "clone-" + Guid.NewGuid().ToString("N")[..8]);
            (await ScratchGitRepo.GitInAsync(repo.Path, "clone", repo.Path, foreign)).Ok.ShouldBeTrue();
            source.RepoPath = foreign;
        }
        if (scenario == "destination_mismatch") source.MergeTargetRef = "release";
        await db.SaveChangesAsync();
        var before = await db.AgentTasks.CountAsync();
        await using var provider = AgentTaskDispatchBaseGuardTests.CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
        var request = Request(card.Id) with
        {
            WorktreeBaseTask = DelegationReportFormatter.Short(source.Id),
            MergeTargetRef = scenario == "destination_mismatch" ? "master" : null,
        };
        var error = await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(request,
            new AgentTaskService.Caller(null, null, repo.Path), CancellationToken.None));
        error.Code.ShouldBe("worktree_base_source_invalid");
        (await db.AgentTasks.CountAsync()).ShouldBe(before);
        (await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim().ShouldBe(sourceSha);

        if (scenario == "destination_mismatch")
        {
            source.MergeTargetRef = null;
            await db.SaveChangesAsync();
            var accepted = await service.CreateAsync(request,
                new AgentTaskService.Caller(null, null, repo.Path), CancellationToken.None);
            accepted.WorktreeBase!.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
            accepted.WorktreeBase.SourceTaskId.ShouldBe(source.Id);
        }
    }

    [Test]
    [Arguments("full_guid")]
    [Arguments("unique_short")]
    [Arguments("missing")]
    [Arguments("ambiguous_short")]
    public async Task T0442_V13_source_identifier_has_existing_guid_and_short_id_rules(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v13");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = AgentTaskDispatchBaseGuardTests.CreateContext(schema);
        var card = await AgentTaskDispatchBaseGuardTests.SeedCardAsync(db, "CARD-0442");
        var source = await AgentTaskDispatchBaseGuardTests.SeedKeptSiblingAsync(db, repo, card.Id, "A");
        var id = scenario == "full_guid" ? source.Id.ToString("D")
            : scenario == "unique_short" ? DelegationReportFormatter.Short(source.Id)
            : scenario == "missing" ? "deadbeef" : "aaaaaaaa";
        if (scenario == "ambiguous_short")
        {
            foreach (var suffix in new[] { "0000-0000-0000-000000000001",
                         "0000-0000-0000-000000000002" })
            {
                var otherId = Guid.Parse("aaaaaaaa-" + suffix);
                db.AgentTasks.Add(new AgentTask
                {
                    Id = otherId, RootTaskId = otherId, Title = "same short id",
                    Goal = "same short id", Role = AgentTaskRole.Code,
                    AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Low,
                    Workspace = WorkspaceMode.Worktree, WorkingDirectory = repo.Path,
                    RepoPath = repo.Path, CardId = card.Id,
                    Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
                    CreatedAt = DateTime.UtcNow.AddHours(-1), CompletedAt = DateTime.UtcNow,
                });
            }
        }
        await db.SaveChangesAsync();
        var before = await db.AgentTasks.CountAsync();
        await using var provider = AgentTaskDispatchBaseGuardTests.CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
        var request = Request(card.Id) with { WorktreeBaseTask = id };
        var caller = new AgentTaskService.Caller(null, null, repo.Path);
        if (scenario == "missing")
            await Should.ThrowAsync<NotFoundException>(() => service.CreateAsync(request, caller, CancellationToken.None));
        else if (scenario == "ambiguous_short")
        {
            var error = await Should.ThrowAsync<ConflictException>(() => service.CreateAsync(request, caller,
                CancellationToken.None));
            error.Message.ShouldContain("more than one task");
        }
        else
        {
            var created = await service.CreateAsync(request, caller, CancellationToken.None);
            created.WorktreeBase!.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
            created.WorktreeBase.SourceTaskId.ShouldBe(source.Id);
            db.ChangeTracker.Clear();
            (await db.AgentTasks.SingleAsync(t => t.Id == created.Id))
                .RequestedWorktreeBaseTaskId.ShouldBe(source.Id);
        }
        (await db.AgentTasks.CountAsync()).ShouldBe(before + (scenario is "full_guid" or "unique_short" ? 1 : 0));
    }

    [Test]
    [Arguments("two_tips")]
    [Arguments("four_tips")]
    public async Task T0442_V14_divergence_refuses_before_create_and_both_overrides_work(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v14");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = AgentTaskDispatchBaseGuardTests.CreateContext(schema);
        var card = await AgentTaskDispatchBaseGuardTests.SeedCardAsync(db, "CARD-0442");
        var a = await AgentTaskDispatchBaseGuardTests.SeedKeptSiblingAsync(db, repo, card.Id, "A", startRef: "master");
        var x = await AgentTaskDispatchBaseGuardTests.SeedKeptSiblingAsync(db, repo, card.Id, "X", startRef: "master");
        AgentTask? b = null;
        AgentTask? y = null;
        if (scenario == "four_tips")
        {
            b = await AgentTaskDispatchBaseGuardTests.SeedKeptSiblingAsync(db, repo, card.Id, "B",
                startRef: a.WorktreeBranch);
            y = await AgentTaskDispatchBaseGuardTests.SeedKeptSiblingAsync(db, repo, card.Id, "Y",
                startRef: x.WorktreeBranch);
            a.CompletedAt = DateTime.UtcNow;
            b.CompletedAt = DateTime.UtcNow.AddMinutes(-10);
        }
        await db.SaveChangesAsync();
        var maxima = new[] { b ?? a, y ?? x };
        var before = await db.AgentTasks.CountAsync();
        await using var provider = AgentTaskDispatchBaseGuardTests.CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
        var request = Request(card.Id);
        var caller = new AgentTaskService.Caller(null, null, repo.Path);
        var conflict = await Should.ThrowAsync<ConflictException>(() => service.CreateAsync(request, caller,
            CancellationToken.None));
        conflict.Code.ShouldBe("worktree_base_ambiguous");
        foreach (var tip in maxima)
        {
            conflict.Message.ShouldContain(DelegationReportFormatter.Short(tip.Id));
            conflict.Message.ShouldContain(tip.WorktreeBranch!);
            conflict.Message.ShouldContain((await repo.GitReadAsync("rev-parse", tip.WorktreeBranch!)).Trim());
        }
        conflict.Message.ShouldContain("-BaseTask");
        conflict.Message.ShouldContain("-FreshWorktree");
        (await db.AgentTasks.CountAsync()).ShouldBe(before);
        foreach (var tip in maxima)
        {
            var selected = await service.CreateAsync(request with
            {
                WorktreeBaseTask = DelegationReportFormatter.Short(tip.Id),
            }, caller, CancellationToken.None);
            selected.WorktreeBase!.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
            selected.WorktreeBase.SourceTaskId.ShouldBe(tip.Id);
        }
        var fresh = await service.CreateAsync(request with { FreshWorktree = true }, caller,
            CancellationToken.None);
        fresh.WorktreeBase!.Decision.ShouldBe(CardWorktreeBaseDecision.Target);
        fresh.WorktreeBase.CandidateWarnings.ShouldContain(w => w.Contains(maxima[0].WorktreeBranch!, StringComparison.Ordinal));
        fresh.WorktreeBase.CandidateWarnings.ShouldContain(w => w.Contains(maxima[1].WorktreeBranch!, StringComparison.Ordinal));
    }

    [Test]
    [Arguments("revoked_capability")]
    [Arguments("directory_denied")]
    [Arguments("quota")]
    [Arguments("provider_signin")]
    [Arguments("concurrency")]
    [Arguments("routing_pin")]
    [Timeout(90_000)]
    public async Task T0442_V17_existing_create_guards_precede_source_selection(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v17");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = AgentTaskDispatchBaseGuardTests.CreateContext(schema);
        var card = await AgentTaskDispatchBaseGuardTests.SeedCardAsync(db, "CARD-0442");
        var source = await AgentTaskDispatchBaseGuardTests.SeedKeptSiblingAsync(
            db, repo, card.Id, "A", startRef: "master");
        var sourceSha = (await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim();
        var request = Request(card.Id) with
        {
            Goal = "V17 " + scenario,
            WorktreeBaseTask = DelegationReportFormatter.Short(source.Id),
        };
        AgentTaskService.Caller caller = new(null, null, repo.Path);
        string? token = null;
        string? temporaryPath = null;
        try
        {
            if (scenario == "revoked_capability")
            {
                token = Guid.NewGuid().ToString("N");
                db.DelegationCapabilities.Add(new DelegationCapability
                {
                    Id = Guid.NewGuid(), Name = "revoked fixture",
                    TokenHash = AgentTaskService.HashToken(token),
                    RootsJson = JsonSerializer.Serialize(new[] { repo.Path }),
                    CreatedAt = DateTime.UtcNow.AddMinutes(-1), RevokedAt = DateTime.UtcNow,
                });
            }
            if (scenario == "directory_denied")
            {
                temporaryPath = Directory.CreateTempSubdirectory("c442-denied-root").FullName;
                caller = new AgentTaskService.Caller(null, null, temporaryPath,
                    CapabilityId: Guid.NewGuid(), CapabilityName: "narrow-root");
                request = request with { WorkingDirectory = repo.Path };
            }
            if (scenario == "quota")
            {
                request = request with { AgentKind = AgentKind.Codex };
                db.SubscriptionUsageSamples.Add(new SubscriptionUsageSample
                {
                    Id = Guid.NewGuid(), Provider = AgentKind.Codex,
                    SubscriptionKey = "Codex", PlanLabel = "fixture",
                    RemainingPercent = 3, ResetsAt = DateTime.UtcNow.AddHours(36),
                    ObservedAt = DateTime.UtcNow, AgentSessionId = Guid.NewGuid(),
                    SourceCommand = "/status", ParseStatus = SubscriptionUsageParseStatus.Parsed,
                    RawExcerpt = "seeded",
                });
            }
            if (scenario == "provider_signin")
            {
                temporaryPath = Directory.CreateTempSubdirectory("c442-empty-grok-home").FullName;
                request = request with { AgentKind = AgentKind.Grok };
            }
            if (scenario == "concurrency")
            {
                var occupantId = Guid.NewGuid();
                db.AgentTasks.Add(new AgentTask
                {
                    Id = occupantId, RootTaskId = occupantId,
                    Title = "occupied code", Goal = "occupied code",
                    Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Shared,
                    WorkingDirectory = repo.WorktreeRoot,
                    Status = AgentTaskStatus.Working, CreatedAt = DateTime.UtcNow,
                });
            }
            await db.SaveChangesAsync();
            if (scenario == "routing_pin")
            {
                await new RoutingPinService(db, TimeProvider.System,
                    NullLogger<RoutingPinService>.Instance).UpsertAsync(
                        new PutRoutingPinRequest(AgentTaskRole.Code, Card: card.Identifier,
                            Provenance: RoutingPinProvenance.Human,
                            Strength: RoutingPinStrength.Required, AgentKind: AgentKind.Codex,
                            Reason: "fixture required Codex"), null, CancellationToken.None);
                request = request with { AgentKind = AgentKind.ClaudeCode };
            }
            var before = await db.AgentTasks.CountAsync();
            var selectable = await new AgentTaskWorktreeBaseResolver(db, new LandingGit(),
                Options.Create(new GitSettings { DefaultBranch = "master" })).ResolveAsync(
                    new AgentTask
                    {
                        Id = Guid.NewGuid(), CardId = card.Id, RepoPath = repo.Path,
                        Workspace = WorkspaceMode.Worktree,
                        RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Task,
                        RequestedWorktreeBaseTaskId = source.Id,
                    }, CancellationToken.None);
            selectable.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
            selectable.SourceTaskId.ShouldBe(source.Id);
            var inspection = new GuardCountingGit();
            var service = CreateV17Service(db, repo, scenario, temporaryPath, inspection);
            switch (scenario)
            {
                case "revoked_capability":
                    (await Should.ThrowAsync<ForbiddenException>(() =>
                        service.AuthenticateAsync(token, CancellationToken.None)))
                        .Message.ShouldContain("revoked");
                    break;
                case "directory_denied":
                    await Should.ThrowAsync<ValidationException>(() =>
                        service.CreateAsync(request, caller, CancellationToken.None));
                    break;
                case "quota":
                    (await Should.ThrowAsync<SubscriptionQuotaLowException>(() =>
                        service.CreateAsync(request, caller, CancellationToken.None)))
                        .Code.ShouldBe("subscription_quota_low");
                    break;
                case "provider_signin":
                    (await Should.ThrowAsync<ProviderSignInRequiredException>(() =>
                        service.CreateAsync(request, caller, CancellationToken.None)))
                        .Code.ShouldBe("provider_sign_in_required");
                    break;
                case "concurrency":
                    await Should.ThrowAsync<ConcurrencyLimitException>(() =>
                        service.CreateAsync(request, caller, CancellationToken.None));
                    break;
                case "routing_pin":
                    (await Should.ThrowAsync<RoutingPinConflictException>(() =>
                        service.CreateAsync(request, caller, CancellationToken.None)))
                        .Code.ShouldBe("routing_pin_conflict");
                    break;
            }
            (await db.AgentTasks.CountAsync()).ShouldBe(before);
            inspection.Calls.ShouldBe(0, "guard refusal must precede source Git inspection");
            (await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim().ShouldBe(sourceSha);
            var worktreeList = (await ScratchGitRepo.GitInAsync(repo.Path,
                "worktree", "list", "--porcelain")).StdOut;
            worktreeList.Split("worktree ", StringSplitOptions.None).Length.ShouldBe(2);
        }
        finally
        {
            if (temporaryPath is not null)
                Directory.Delete(temporaryPath, recursive: true);
        }
    }

    private static AgentTaskService CreateV17Service(AppDbContext db, ScratchGitRepo repo,
        string scenario, string? grokHome, ILandingGit inspection)
    {
        var settings = Options.Create(new DelegationSettings
        {
            AllowedRoots = [repo.Path], MaxOpenTasks = 1,
            MaxTasksPerRoot = 40, MaxDepth = 5,
        });
        var registry = new AgentRegistrySettings();
        if (scenario == "provider_signin")
        {
            registry.GrokCredentialProbeEnabled = true;
            registry.Definitions["grok"] = new AgentDefinition
            {
                Kind = "Grok", Exe = "grok",
                Env = new Dictionary<string, string> { ["GROK_HOME"] = grokHome! },
            };
        }
        var quota = scenario == "quota"
            ? new SubscriptionQuotaGate(new SubscriptionUsageReader(db, TimeProvider.System),
                Options.Create(new SubscriptionQuotaGateSettings()), TimeProvider.System,
                NullLogger<SubscriptionQuotaGate>.Instance)
            : null;
        var pins = scenario == "routing_pin"
            ? new RoutingPinService(db, TimeProvider.System, NullLogger<RoutingPinService>.Instance)
            : null;
        var openGate = scenario == "concurrency" ? new DelegationOpenGate(db, settings) : null;
        return new AgentTaskService(db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            settings, new MockEventBus(), new RecordingSessionStopper(), TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            quotaGate: quota, routingPins: pins, registrySettings: Options.Create(registry),
            openGate: openGate,
            baseResolver: new AgentTaskWorktreeBaseResolver(db, inspection,
                Options.Create(new GitSettings { DefaultBranch = "master" })));
    }

    private sealed class GuardCountingGit : LandingGit
    {
        public int Calls { get; private set; }

        public override Task<LandingGitResult> RunAsync(string repository,
            IReadOnlyList<string> args, CancellationToken ct)
        {
            Calls++;
            return base.RunAsync(repository, args, ct);
        }
    }

    private static CreateAgentTaskRequest Request(Guid? cardId) => new(
        "Build the card", Title: "Worktree continuation", Role: AgentTaskRole.Code,
        Workspace: WorkspaceMode.Worktree, Card: cardId?.ToString("D"));
}
