using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskWorktreeBaseCreateTests
{
    [Test]
    [Arguments("both_flags")]
    [Arguments("shared")]
    [Arguments("readonly")]
    [Arguments("onagent_task")]
    [Arguments("onagent_fresh")]
    [Arguments("task_without_card")]
    public async Task T0442_V11(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var owner = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        var service = world.Services.GetRequiredService<AgentTaskService>();
        var request = name switch
        {
            "both_flags" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, Card: "CARD-0442")
                { WorktreeBaseTask = owner.Id.ToString("D"), FreshWorktree = true },
            "shared" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Shared, Card: "CARD-0442")
                { FreshWorktree = true },
            "readonly" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.ReadOnly, Card: "CARD-0442")
                { WorktreeBaseTask = owner.Id.ToString("D") },
            "onagent_task" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
                FollowUpOnTask: owner.Id.ToString("N")[..8], Card: "CARD-0442") { WorktreeBaseTask = owner.Id.ToString("D") },
            "onagent_fresh" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
                FollowUpOnTask: owner.Id.ToString("N")[..8], Card: "CARD-0442") { FreshWorktree = true },
            _ => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree)
                { WorktreeBaseTask = owner.Id.ToString("D"), Card = null },
        };
        var ex = await Should.ThrowAsync<ValidationException>(() =>
            service.CreateAsync(request, world.Caller(), CancellationToken.None));
        ex.StatusCode.ShouldBe(422);
        await using var db = world.CreateDb();
        (await db.AgentTasks.CountAsync(t => t.Status == AgentTaskStatus.Queued)).ShouldBe(0);
    }

    [Test]
    [Arguments("cross_card")]
    [Arguments("cross_board")]
    [Arguments("nested_repo")]
    [Arguments("separate_clone")]
    [Arguments("destination_mismatch")]
    public async Task T0442_V12(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var owner = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        if (name is "cross_card" or "cross_board")
        {
            var other = await world.SeedOtherCardAsync(name == "cross_board" ? "CARD-0443" : "CARD-0499");
            await using var db = world.CreateDb();
            var live = await db.AgentTasks.FindAsync(owner.Id);
            live!.CardId = other.Id;
            await db.SaveChangesAsync();
        }
        else if (name == "destination_mismatch")
        {
            await using var db = world.CreateDb();
            var live = await db.AgentTasks.FindAsync(owner.Id);
            live!.MergeTargetRef = "release";
            await db.SaveChangesAsync();
        }
        else if (name == "nested_repo")
        {
            var nested = Path.Combine(world.Repo.Path, "vendor", "lib");
            Directory.CreateDirectory(nested);
            (await ScratchGitRepo.GitInAsync(nested, "init", "-b", "master")).Ok.ShouldBeTrue();
            (await ScratchGitRepo.GitInAsync(nested, "config", "user.email", "t@t")).Ok.ShouldBeTrue();
            (await ScratchGitRepo.GitInAsync(nested, "config", "user.name", "t")).Ok.ShouldBeTrue();
            await File.WriteAllTextAsync(Path.Combine(nested, "n.txt"), "n\n");
            (await ScratchGitRepo.GitInAsync(nested, "add", "n.txt")).Ok.ShouldBeTrue();
            (await ScratchGitRepo.GitInAsync(nested, "commit", "-m", "n")).Ok.ShouldBeTrue();
            owner = await world.SeedSucceededAsync("nested", "n2.txt", "n2\n", repoPath: nested);
        }
        else
        {
            var clone = Directory.CreateTempSubdirectory("c442-sep").FullName;
            (await ScratchGitRepo.GitInAsync(world.Repo.Path, "clone", world.Repo.Path, clone)).Ok.ShouldBeTrue();
            owner = await world.SeedSucceededAsync("clone", "c.txt", "c\n", repoPath: clone);
        }

        var service = world.Services.GetRequiredService<AgentTaskService>();
        var ex = await Should.ThrowAsync<HttpException>(() => service.CreateAsync(
            new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, Card: "CARD-0442")
                { WorktreeBaseTask = owner.Id.ToString("D") },
            world.Caller(), CancellationToken.None));
        ex.StatusCode.ShouldBeOneOf(409, 422);
        await using var db2 = world.CreateDb();
        (await db2.AgentTasks.CountAsync(t => t.Status == AgentTaskStatus.Queued)).ShouldBe(0);
        if (name == "destination_mismatch")
        {
            var accepted = await service.CreateAsync(
                new CreateAgentTaskRequest("ok", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
                    Card: "CARD-0442", MergeTargetRef: "release")
                    { WorktreeBaseTask = owner.Id.ToString("D") },
                world.Caller(), CancellationToken.None);
            accepted.WorktreeBase!.SourceTaskId.ShouldBe(owner.Id);
        }
    }

    [Test]
    [Arguments("full_guid")]
    [Arguments("unique_short")]
    [Arguments("missing")]
    [Arguments("ambiguous_short")]
    public async Task T0442_V13(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var owner = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        var service = world.Services.GetRequiredService<AgentTaskService>();
        if (name == "ambiguous_short")
        {
            var n = owner.Id.ToString("N");
            var otherId = Guid.Parse(n[..8] + "aaaaaaaaaaaaaaaaaaaaaaaa");
            await using (var db = world.CreateDb())
            {
                db.AgentTasks.Add(new AgentTask
                {
                    Id = otherId, RootTaskId = otherId, Title = "twin", Goal = "twin",
                    Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium,
                    Workspace = WorkspaceMode.Worktree, WorkingDirectory = world.Repo.Path, RepoPath = world.Repo.Path,
                    CardId = world.Card.Id, Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
                    CreatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            var ex = await Should.ThrowAsync<HttpException>(() => service.CreateAsync(
                new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, Card: "CARD-0442")
                    { WorktreeBaseTask = n[..8] },
                world.Caller(), CancellationToken.None));
            ex.StatusCode.ShouldBe(409);
            return;
        }

        var id = name switch
        {
            "full_guid" => owner.Id.ToString("D"),
            "unique_short" => owner.Id.ToString("N")[..8],
            _ => Guid.NewGuid().ToString("D"),
        };
        var request = new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, Card: "CARD-0442")
            { WorktreeBaseTask = id };
        if (name == "missing")
        {
            await Should.ThrowAsync<NotFoundException>(() =>
                service.CreateAsync(request, world.Caller(), CancellationToken.None));
            return;
        }

        var created = await service.CreateAsync(request, world.Caller(), CancellationToken.None);
        created.WorktreeBase.ShouldNotBeNull();
        created.WorktreeBase!.SourceTaskId.ShouldBe(owner.Id);
    }

    [Test]
    [Arguments("two_tips")]
    [Arguments("four_tips")]
    public async Task T0442_V14(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        if (name == "two_tips")
            await world.SeedTwoTipsAsync();
        else
            await world.SeedFourTipsAsync();
        var service = world.Services.GetRequiredService<AgentTaskService>();
        var ex = await Should.ThrowAsync<ConflictException>(() => service.CreateAsync(
            new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, Card: "CARD-0442"),
            world.Caller(), CancellationToken.None));
        ex.Code.ShouldBe(AgentTaskWorktreeBaseResolver.AmbiguousCode);
        ex.Extensions.ShouldNotBeNull();
        ex.Extensions!.ContainsKey("recovery").ShouldBeTrue();
        await using var db = world.CreateDb();
        (await db.AgentTasks.CountAsync(t => t.Status == AgentTaskStatus.Queued)).ShouldBe(0);

        var first = await db.AgentTasks.AsNoTracking()
            .Where(t => t.Status == AgentTaskStatus.Succeeded)
            .OrderBy(t => t.CompletedAt)
            .FirstAsync();
        var chosen = await service.CreateAsync(
            new CreateAgentTaskRequest("pick", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, Card: "CARD-0442")
                { WorktreeBaseTask = first.Id.ToString("D"), IgnoreConcurrencyLimit = true },
            world.Caller(), CancellationToken.None);
        chosen.WorktreeBase!.SourceTaskId.ShouldBe(first.Id);
        chosen.WorktreeBase.Decision.ShouldBe("Continue");

        var fresh = await service.CreateAsync(
            new CreateAgentTaskRequest("fresh", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, Card: "CARD-0442")
                { FreshWorktree = true, IgnoreConcurrencyLimit = true },
            world.Caller(), CancellationToken.None);
        fresh.WorktreeBase!.Decision.ShouldBe("Target");
        fresh.WorktreeBase.SourceTaskId.ShouldBeNull();
    }

    [Test]
    [Arguments("revoked_capability")]
    [Arguments("directory_denied")]
    [Arguments("quota")]
    [Arguments("provider_signin")]
    [Arguments("concurrency")]
    [Arguments("routing_pin")]
    public async Task T0442_V17(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync(
            extraCreateGates: true,
            delegation: name == "concurrency"
                ? new Antiphon.Server.Application.Settings.DelegationSettings { MaxConcurrentTasks = 512, MaxOpenTasks = 1 }
                : null);
        var owner = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        var service = world.Services.GetRequiredService<AgentTaskService>();
        if (name == "directory_denied")
        {
            var ex = await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(
                new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
                    WorkingDirectory: "C:\\not-allowed-c442", Card: "CARD-0442")
                    { WorktreeBaseTask = owner.Id.ToString("D") },
                world.Caller(), CancellationToken.None));
            ex.StatusCode.ShouldBe(422);
            await world.AssertNoLaunchAsync(owner.Id);
            return;
        }

        if (name == "revoked_capability")
        {
            const string raw = "c442-revoked-token";
            await using var db = world.CreateDb();
            db.DelegationCapabilities.Add(new DelegationCapability
            {
                Id = Guid.NewGuid(),
                Name = "revoked-c442",
                TokenHash = AgentTaskService.HashToken(raw),
                RootsJson = "[\"C:\\\\src\\\\Antiphon\"]",
                RevokedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            var ex = await Should.ThrowAsync<ForbiddenException>(() =>
                service.AuthenticateAsync(raw, CancellationToken.None));
            ex.Message.ShouldContain("revoked");
            return;
        }

        if (name == "quota")
        {
            await using var db = world.CreateDb();
            db.SubscriptionUsageSamples.Add(new SubscriptionUsageSample
            {
                Id = Guid.NewGuid(),
                Provider = AgentKind.ClaudeCode,
                SubscriptionKey = "ClaudeCode",
                PlanLabel = "test",
                RemainingPercent = 3,
                ResetsAt = DateTime.UtcNow.AddHours(36),
                ObservedAt = DateTime.UtcNow,
                AgentSessionId = Guid.NewGuid(),
                SourceCommand = "/status",
                ParseStatus = SubscriptionUsageParseStatus.Parsed,
                RawExcerpt = "seeded",
            });
            await db.SaveChangesAsync();
            var ex = await Should.ThrowAsync<HttpException>(() => service.CreateAsync(
                new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, Card: "CARD-0442")
                    { WorktreeBaseTask = owner.Id.ToString("D") },
                world.Caller(), CancellationToken.None));
            ex.StatusCode.ShouldBe(409);
            ex.Code.ShouldBe("subscription_quota_low");
            return;
        }

        if (name == "provider_signin")
        {
            var created = await service.CreateAsync(
                new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
                    Card: "CARD-0442", AgentKind: AgentKind.Grok, AllowUnauthenticatedProvider: false)
                    { WorktreeBaseTask = owner.Id.ToString("D") },
                world.Caller(), CancellationToken.None);
            created.Id.ShouldNotBe(Guid.Empty);
            return;
        }

        if (name == "concurrency")
        {
            await world.CreateNextAsync();
            var ex = await Should.ThrowAsync<HttpException>(() => service.CreateAsync(
                new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, Card: "CARD-0442")
                    { WorktreeBaseTask = owner.Id.ToString("D") },
                world.Caller(), CancellationToken.None));
            ex.StatusCode.ShouldBe(409);
            ex.Code.ShouldBe("concurrency_limit");
            return;
        }

        await using (var db = world.CreateDb())
        {
            db.RoutingPins.Add(new RoutingPin
            {
                Id = Guid.NewGuid(),
                CardId = world.Card.Id,
                Role = AgentTaskRole.Code,
                Provenance = RoutingPinProvenance.Human,
                Strength = RoutingPinStrength.Required,
                CandidatesJson = """[{"agentKind":"Grok","modelLevel":"High"}]""",
                Reason = "c442 pin",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var pinEx = await Should.ThrowAsync<HttpException>(() => service.CreateAsync(
            new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
                Card: "CARD-0442", AgentKind: AgentKind.ClaudeCode)
                { WorktreeBaseTask = owner.Id.ToString("D") },
            world.Caller(), CancellationToken.None));
        pinEx.StatusCode.ShouldBe(409);
        pinEx.Code.ShouldBe("routing_pin_conflict");
    }
}
