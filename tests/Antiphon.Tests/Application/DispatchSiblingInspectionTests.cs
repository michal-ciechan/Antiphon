using System.Data.Common;
using Antiphon.TestSupport;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<GitProcessSpawnLimit>]
public class DispatchSiblingInspectionTests
{
    [Test]
    [Arguments("zero inventory")]
    [Arguments("inventory below candidates")]
    [Arguments("valid boundary")]
    [Arguments("scoped worker")]
    public async Task C1349_InspectionConfiguration(string scenario)
    {
        if (scenario != "scoped worker")
        {
            var services = new ServiceCollection();
            services.AddSingleton<IValidateOptions<GitSettings>, GitInspectionSettingsValidator>();
            services.AddOptions<GitSettings>().Configure(s =>
                s.WorktreeBaseMaxInventoryRows = scenario == "zero inventory" ? 0
                    : scenario == "inventory below candidates" ? 15 : 16);
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            if (scenario == "valid boundary")
                provider.GetRequiredService<IOptions<GitSettings>>().Value.WorktreeBaseMaxInventoryRows.ShouldBe(16);
            else Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<GitSettings>>().Value);
            return;
        }
        var contexts = new System.Collections.Concurrent.ConcurrentBag<AppDbContext>();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var repo = new ScratchGitRepo("c1349-scopes");
        await repo.CommitFileAsync("seed.txt", "seed");
        var servicesForWorker = new ServiceCollection();
        servicesForWorker.AddLogging();
        servicesForWorker.AddScoped(_ =>
        {
            var context = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            contexts.Add(context);
            return context;
        });
        servicesForWorker.AddDelegationWorktreeGraph();
        await using var workerProvider = servicesForWorker.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var queue = workerProvider.GetRequiredService<CardSiblingInspectionQueue>();
        var host = new Antiphon.Server.Infrastructure.Orchestration.CardSiblingInspectionHostedService(queue);
        await host.StartAsync(default);
        var ids = new List<Guid>();
        await using (var db = Context(schema))
        {
            var card = await CardAsync(db);
            for (var i = 0; i < 2; i++)
            {
                var task = NewRequest(repo, card.Id);
                task.RootTaskId = task.Id; task.Status = AgentTaskStatus.Queued;
                task.Title = "scoped"; task.Goal = "no worker launch"; task.CreatedAt = DateTime.UtcNow;
                db.AgentTasks.Add(task); ids.Add(task.Id);
                await db.SaveChangesAsync();
                queue.TryEnqueue(CardSiblingInspectionIntent.From(task, "master")).ShouldBeTrue();
            }
        }
        await queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(15));
        contexts.Count.ShouldBe(2);
        contexts.Select(c => c.ContextId.InstanceId).Distinct().Count().ShouldBe(2);
        foreach (var context in contexts)
            await Should.ThrowAsync<ObjectDisposedException>(() => context.AgentTasks.CountAsync());
        await using var observer = Context(schema);
        (await observer.AgentTasks.Where(t => ids.Contains(t.Id)).ToListAsync())
            .ShouldAllBe(t => t.Status == AgentTaskStatus.Queued && t.AgentSessionId == null && t.WorktreePath == null);
        await host.StopAsync(default);
        queue.Active.ShouldBe(0); queue.Outstanding.ShouldBe(0);
    }

    [Test]
    [Arguments("64 ancestors")]
    [Arguments("duplicate refs/tips")]
    [Arguments("moved merged SHA")]
    [Arguments("prefix-neighbor ref")]
    [Arguments("invalid OID")]
    [Arguments("absent local ref")]
    [Arguments("batch error")]
    public async Task C1349_ContainedInventory(string scenario)
    {
        using var repo = new ScratchGitRepo("c1349-contained");
        await repo.CommitFileAsync("seed.txt", "seed");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = await CardAsync(db);
        AgentTask? source = null;
        if (scenario is "64 ancestors" or "duplicate refs/tips")
        {
            for (var i = 0; i < 64; i++)
            {
                var id = Guid.NewGuid();
                var branch = "feat/card-task-" + id.ToString("N")[..8];
                if (scenario == "duplicate refs/tips" && source is not null) branch = source.WorktreeBranch!;
                else await repo.GitAsync("branch", branch, "master");
                source = new AgentTask
                {
                    Id = id, RootTaskId = id, Title = "contained", Goal = "contained",
                    Workspace = WorkspaceMode.Worktree, CardId = card.Id,
                    RepoPath = repo.Path, WorkingDirectory = repo.Path, WorktreeBranch = branch,
                    Status = AgentTaskStatus.Succeeded, CreatedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow,
                };
                db.AgentTasks.Add(source);
            }
        }
        else source = await SourceAsync(db, repo, card.Id, "source", "master");
        await db.SaveChangesAsync();
        if (scenario is "absent local ref" or "prefix-neighbor ref")
            await repo.GitAsync("branch", "-D", source!.WorktreeBranch!);
        var target = (await repo.GitReadAsync("rev-parse", "master")).Trim();
        var trace = new RecordingGit
        {
            Rewrite = (args, result) =>
            {
                if (args[0] != "for-each-ref") return result;
                if (scenario == "batch error") return new LandingGitResult(2, "", "fixture batch failure");
                if (scenario == "invalid OID") return new LandingGitResult(0, "invalid refs/heads/" + source!.WorktreeBranch + "\n", "");
                if (scenario == "prefix-neighbor ref") return new LandingGitResult(0, target + " refs/heads/" + source!.WorktreeBranch + "/neighbor\n", "");
                if (scenario == "moved merged SHA" && args.Any(arg => arg.StartsWith("--merged=")))
                    return new LandingGitResult(0, target + " refs/heads/" + source!.WorktreeBranch + "\n", "");
                return result;
            },
        };
        var request = NewRequest(repo, card.Id);
        var selected = await ResolveAsync(db, request, trace);
        if (scenario is "invalid OID" or "batch error")
        {
            trace.Rewritten.ShouldBeGreaterThan(0);
            selected.Reason.ShouldBe("git_inspection_error");
            selected.SourceTaskId.ShouldBeNull();
        }
        else if (scenario == "moved merged SHA")
        {
            trace.Rewritten.ShouldBeGreaterThan(0);
            selected.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
            selected.SourceTaskId.ShouldBe(source!.Id);
            trace.Commands.ShouldContain(c => c[0] == "cherry");
        }
        else
        {
            selected.Decision.ShouldBe(CardWorktreeBaseDecision.Target, scenario);
            selected.Incomplete.ShouldBeFalse();
            trace.Commands.Count(c => c[0] == "for-each-ref").ShouldBe(2);
            trace.Commands.ShouldNotContain(c => c[0] == "cherry" || c[0] == "log");
            selected.GitCommands.ShouldBeLessThanOrEqualTo(4);
        }
        trace.Commands.Clear();
        var guard = await new CardSiblingInspectionService(db, trace, Options.Create(new GitSettings()),
            TimeProvider.System, new GitProcessGate()).InspectAsync(request, default);
        guard.Hold.ShouldBeNull();
        if (scenario is "invalid OID" or "batch error") guard.Reason.ShouldBe("git_inspection_error");
        else if (scenario == "moved merged SHA") guard.Warnings.ShouldHaveSingleItem().TaskId.ShouldBe(source!.Id);
        else
        {
            guard.Reason.ShouldBeNull();
            guard.Warnings.ShouldBeEmpty();
            trace.Commands.ShouldNotContain(c => c[0] == "cherry" || c[0] == "log");
            guard.GitCommands.ShouldBeLessThanOrEqualTo(5);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1349_DocumentationSourceIsPreserved(bool pending)
    {
        using var repo = new ScratchGitRepo("c1349-docs");
        await repo.CommitFileAsync("seed.txt", "seed");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = await CardAsync(db);
        var source = await SourceAsync(db, repo, card.Id, "plan", "master", false);
        source.Role = AgentTaskRole.Plan;
        await repo.GitAsync("checkout", source.WorktreeBranch!);
        await repo.CommitFileAsync("plan.md", "# Unlanded implementation instructions\n");
        var sha = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("checkout", "master");
        if (pending)
        {
            var land = new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = source.Id,
                IsPending = true, State = LandRequestState.Queued, ExpectedSourceSha = sha, RequestedAt = DateTime.UtcNow };
            source.CurrentLandRequestId = land.Id;
            db.AgentTaskLandRequests.Add(land);
        }
        await db.SaveChangesAsync();
        var request = NewRequest(repo, card.Id);
        var result = await ResolveAsync(db, request);
        result.Decision.ShouldBe(pending ? CardWorktreeBaseDecision.WaitForLand : CardWorktreeBaseDecision.Continue);
        if (!pending) result.SourceSha.ShouldBe(sha);
        var trace = new RecordingGit();
        var guard = await new CardSiblingInspectionService(db, trace, Options.Create(new GitSettings()),
            TimeProvider.System, new GitProcessGate()).InspectAsync(request, default);
        if (pending) guard.Hold.ShouldNotBeNull().TaskId.ShouldBe(source.Id);
        else guard.Warnings.ShouldHaveSingleItem().FullTipSha.ShouldBe(sha);
        guard.GitCommands.ShouldBeLessThanOrEqualTo(12);
        request.AgentSessionId.ShouldBeNull();
        request.WorktreePath.ShouldBeNull();
    }

    [Test]
    [Arguments("DB deadline")]
    [Arguments("Git-gate deadline")]
    [Arguments("in-command deadline")]
    [Arguments("cumulative phases")]
    [Arguments("command cap")]
    [Arguments("expensive-candidate cap")]
    [Arguments("inventory cap")]
    [Arguments("caller cancellation")]
    public async Task C1349_InspectionLimits(string scenario)
    {
        using var repo = new ScratchGitRepo("c1349-limits");
        await repo.CommitFileAsync("seed.txt", "seed");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var seed = Context(schema);
        var card = await CardAsync(seed);
        var count = scenario == "inventory cap" ? 257 : scenario == "expensive-candidate cap" ? 17 : 1;
        var first = await SourceAsync(seed, repo, card.Id, "first", "master");
        for (var i = 1; i < count; i++)
        {
            if (scenario == "expensive-candidate cap") await SourceAsync(seed, repo, card.Id, "source" + i, "master");
            else
            {
                var id = Guid.NewGuid();
                seed.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, CardId = card.Id,
                    Title = "alias", Goal = "alias", Workspace = WorkspaceMode.Worktree,
                    Status = AgentTaskStatus.Succeeded, WorktreeBranch = first.WorktreeBranch,
                    WorkingDirectory = repo.Path, RepoPath = repo.Path, CreatedAt = DateTime.UtcNow });
            }
        }
        await seed.SaveChangesAsync();
        foreach (var legacy in new[] { false, true })
        {
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var audit = new InventoryAudit(clock, scenario == "DB deadline");
            var options = new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions(schema.ConnectionString))
                .AddInterceptors(audit).Options;
            await using var db = new AppDbContext(options);
            var gate = new GitProcessGate(1);
            var git = new BudgetGit(clock, scenario);
            var config = new GitSettings { WorktreeBaseMaxGitCommands = scenario == "command cap" ? 2 : 128 };
            var request = NewRequest(repo, card.Id);
            using var caller = new CancellationTokenSource();
            async Task<(string? Reason, bool Incomplete)> ReadAsync()
            {
                if (legacy)
                {
                    var r = await new CardSiblingInspectionService(db, git, Options.Create(config), clock, gate)
                        .InspectAsync(request, caller.Token);
                    return (r.Reason, r.Reason is not null);
                }
                var result = await new AgentTaskWorktreeBaseResolver(db, git, Options.Create(config), clock, gate)
                    .ResolveAsync(request, caller.Token);
                return (result.Reason, result.Incomplete);
            }
            if (scenario == "caller cancellation")
            {
                caller.Cancel();
                await Should.ThrowAsync<OperationCanceledException>(() => ReadAsync());
                git.Calls.ShouldBe(0);
                continue;
            }
            (string? Reason, bool Incomplete) result;
            if (scenario == "Git-gate deadline")
            {
                using var held = await gate.EnterAsync(default);
                var pending = ReadAsync();
                using var escape = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (gate.Waiting == 0)
                {
                    pending.IsCompleted.ShouldBeFalse();
                    escape.Token.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
                clock.Advance(TimeSpan.FromSeconds(5));
                result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
                gate.Waiting.ShouldBe(0, "deadline must cancel the waiter while the fixture still owns its lease");
                gate.Started.ShouldBe(1);
            }
            else result = await ReadAsync();
            result.Incomplete.ShouldBeTrue();
            result.Reason.ShouldBe(scenario switch
            {
                "command cap" => "git_command_limit",
                "expensive-candidate cap" or "inventory cap" => "candidate_limit",
                _ => "inspection_timeout",
            });
            git.Calls.ShouldBeLessThanOrEqualTo(config.WorktreeBaseMaxGitCommands);
            if (scenario == "command cap") git.Calls.ShouldBe(2);
            if (scenario == "in-command deadline") git.CanceledInCommand.ShouldBeTrue();
            if (scenario == "cumulative phases") git.Calls.ShouldBe(3);
            if (scenario == "DB deadline")
            {
                audit.DeadlineReached.ShouldBeTrue();
                audit.CanceledInQuery.ShouldBeTrue();
                git.Calls.ShouldBe(0);
            }
            if (scenario == "inventory cap")
            {
                audit.InventoryQuery.ShouldNotBeNull();
                audit.InventoryQuery.ShouldContain("LIMIT");
                audit.InventoryLimit.ShouldBe(257);
                git.Calls.ShouldBe(0);
            }
        }
    }

    [Test]
    [Arguments("pending beyond raw cap")]
    [Arguments("candidate cap")]
    [Arguments("command cap")]
    [Arguments("deadline")]
    [Arguments("Git error")]
    [Arguments("dedup alias")]
    [Arguments("unrelated StartRef")]
    [Arguments("unresolved StartRef")]
    public async Task C1349_PendingLandSurvivesLimits(string scenario)
    {
        using var repo = new ScratchGitRepo("c1349-pending");
        await repo.CommitFileAsync("seed.txt", "seed");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = await CardAsync(db);
        var source = await SourceAsync(db, repo, card.Id, "pending-plan", "master");
        source.Role = AgentTaskRole.Plan;
        var sha = (await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim();
        var land = new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = source.Id,
            IsPending = true, State = LandRequestState.Running, ExpectedSourceSha = sha, RequestedAt = DateTime.UtcNow };
        source.CurrentLandRequestId = land.Id;
        db.AgentTaskLandRequests.Add(land);
        if (scenario == "candidate cap")
            await SourceAsync(db, repo, card.Id, "other", "master");
        if (scenario is "pending beyond raw cap" or "dedup alias")
        {
            for (var i = 1; i <= (scenario == "dedup alias" ? 1 : 256); i++)
            {
                var id = Guid.Parse("00000000-0000-0000-0000-" + i.ToString("D12"));
                db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id,
                    CardId = card.Id, Title = "alias", Goal = "alias", RepoPath = repo.Path,
                    WorkingDirectory = repo.Path, Workspace = WorkspaceMode.Worktree,
                    WorktreeBranch = source.WorktreeBranch, Status = AgentTaskStatus.Succeeded,
                    CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow });
            }
        }
        await db.SaveChangesAsync();
        foreach (var mode in new[] { RequestedWorktreeBaseMode.Auto, RequestedWorktreeBaseMode.Task, RequestedWorktreeBaseMode.Target })
        {
            var request = NewRequest(repo, card.Id);
            request.RootTaskId = request.Id;
            request.Title = "pending boundary";
            request.Goal = "must remain held";
            request.WorkingDirectory = repo.Path;
            request.Role = AgentTaskRole.Code;
            request.CreatedAt = DateTime.UtcNow;
            request.RequestedWorktreeBaseMode = mode;
            request.RequestedWorktreeBaseTaskId = mode == RequestedWorktreeBaseMode.Task ? source.Id : null;
            request.WorktreeBaseRequestedRef = scenario switch
            {
                "unrelated StartRef" => "master",
                "unresolved StartRef" => "missing-ref",
                _ => null,
            };
            db.AgentTasks.Add(request);
            await db.SaveChangesAsync();
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            ILandingGit git = scenario == "deadline" ? new BudgetGit(clock, "in-command deadline") : new RecordingGit
            {
                Rewrite = (args, result) => scenario == "Git error" && args[0] == "for-each-ref"
                    ? new LandingGitResult(2, "", "injected batch failure") : result,
            };
            await using var provider = AgentTaskDispatchBaseGuardTests.CreateProvider(schema.ConnectionString,
                repo.WorktreeRoot, git: git, clock: clock,
                maxCandidates: scenario == "candidate cap" ? 1 : 16,
                maxGitCommands: scenario == "command cap" ? 1 : 128);
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickInspectedAsync(default);
            await using var fresh = Context(schema);
            var held = await fresh.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == request.Id);
            held.Status.ShouldBe(AgentTaskStatus.Queued, scenario + " " + mode);
            held.WorktreePath.ShouldBeNull();
            held.AgentSessionId.ShouldBeNull();
            (await fresh.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == request.Id && e.Type == AgentTaskEventType.Held)).ShouldBeTrue();
            await fresh.AgentTasks.Where(t => t.Id == request.Id).ExecuteUpdateAsync(update => update.SetProperty(t => t.Status, AgentTaskStatus.Canceled));
        }
    }

    [Test]
    [Arguments("identical/chain")]
    [Arguments("64 divergent")]
    [Arguments("enrichment deadline")]
    public async Task C1349_ReductionRemainsBounded(string scenario)
    {
        using var repo = new ScratchGitRepo("c1349-reduction");
        await repo.CommitFileAsync("seed.txt", "seed");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = await CardAsync(db);
        var rows = new List<AgentTask>();
        var count = scenario == "64 divergent" ? 64 : scenario == "identical/chain" ? 4 : 2;
        for (var i = 0; i < count; i++)
        {
            var parent = scenario == "identical/chain" && i > 0 ? rows[i - 1].WorktreeBranch! : "master";
            rows.Add(await SourceAsync(db, repo, card.Id, "source" + i, parent,
                commit: scenario != "identical/chain" || i != 2));
        }
        await db.SaveChangesAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var fired = false;
        var git = new RecordingGit
        {
            Rewrite = (args, result) =>
            {
                if (scenario == "enrichment deadline" && args[0] == "merge-base")
                {
                    fired = true;
                    clock.Advance(TimeSpan.FromSeconds(5));
                }
                return result;
            },
        };
        var result = await new CardSiblingInspectionService(db, git, Options.Create(new GitSettings()),
            clock, new GitProcessGate()).InspectAsync(NewRequest(repo, card.Id), default);
        result.GitCommands.ShouldBeLessThanOrEqualTo(128);
        if (scenario == "identical/chain")
        {
            var warning = result.Warnings.ShouldHaveSingleItem();
            warning.TaskId.ShouldBe(rows[3].Id);
            warning.CoveredCount.ShouldBe(3);
        }
        else
        {
            result.Warnings.Count.ShouldBe(scenario == "64 divergent" ? 16 : 2);
            result.Warnings.ShouldAllBe(w => w.CoveredCount == 0);
            (result.Warnings.Count + result.OmittedCandidates).ShouldBe(count);
            result.Reason.ShouldBe(scenario == "64 divergent" ? "candidate_limit" : "inspection_timeout");
            if (scenario == "enrichment deadline") fired.ShouldBeTrue();
        }
    }

    /// <summary>
    /// The C540 gate flake: enrichment spends <see cref="GitSettings.WorktreeBaseInspectionTimeoutSeconds"/>
    /// (5 s) after the candidates are inspected. The product keeps the reduced warnings and adds
    /// <c>sibling-inspection-incomplete</c>. The delivery wait must treat that as settled.
    /// </summary>
    [Test]
    public async Task C540_LoadedHostInspectionTimeoutSettlesTheReducedSiblingWait()
    {
        using var repo = new ScratchGitRepo("c540-inspection-budget");
        await repo.CommitFileAsync("seed.txt", "seed");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = await CardAsync(db);
        var left = await SourceAsync(db, repo, card.Id, "containing", "master");
        var right = await SourceAsync(db, repo, card.Id, "divergent", "master");
        await db.SaveChangesAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var slowed = false;
        var git = new RecordingGit
        {
            Rewrite = (args, result) =>
            {
                // Ancestor checks run only in the enrichment loop, after the warnings exist.
                if (!slowed && args.Count > 0 && args[0] == "merge-base")
                {
                    slowed = true;
                    clock.Advance(TimeSpan.FromSeconds(new GitSettings().WorktreeBaseInspectionTimeoutSeconds));
                }
                return result;
            },
        };
        var request = NewRequest(repo, card.Id);
        var result = await new CardSiblingInspectionService(db, git, Options.Create(new GitSettings()),
            clock, new GitProcessGate()).InspectAsync(request, default);
        slowed.ShouldBeTrue("the witness must inject the slow enrichment, not a fast inspection");
        result.Reason.ShouldBe("inspection_timeout");
        result.OmittedCandidates.ShouldBe(0);
        result.Warnings.Select(w => w.TaskId).Order().ShouldBe(new[] { left.Id, right.Id }.Order());
        var incomplete =
            $"Same-card warning inspection incomplete: {result.Reason}; {result.RawRows} raw, {result.ExpensiveCandidates} candidates, {result.InspectedCandidates} inspected, {result.OmittedCandidates} omitted, {result.GitCommands} commands.";
        var expected = result.Warnings.Select(w => DispatchBaseNotificationPayload.SiblingKey(w.TaskId)).ToArray();
        DispatchWaitDiagnostic.ReducedSiblingDispatchReady(
            result.Warnings.Select(w => (DispatchBaseNotificationPayload.SiblingKey(w.TaskId), (string?)w.WarningDetail(request.Id)))
                .Append(("sibling-inspection-incomplete", incomplete)).ToList(),
            expected).ShouldBeTrue(
            "a loaded host that spends the 5 s inspection budget still settles the reduced sibling warnings");
    }

    private sealed class BudgetGit(FakeTimeProvider clock, string scenario) : LandingGit
    {
        public int Calls { get; private set; }
        public bool CanceledInCommand { get; private set; }
        public override async Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> args, CancellationToken ct)
        {
            Calls++;
            if (scenario == "in-command deadline")
            {
                clock.Advance(TimeSpan.FromSeconds(5));
                CanceledInCommand = ct.IsCancellationRequested;
                ct.ThrowIfCancellationRequested();
            }
            if (scenario == "cumulative phases") clock.Advance(TimeSpan.FromSeconds(2));
            return await base.RunAsync(repository, args, ct);
        }
    }

    private sealed class InventoryAudit(FakeTimeProvider clock, bool expire) : DbCommandInterceptor
    {
        public bool DeadlineReached { get; private set; }
        public bool CanceledInQuery { get; private set; }
        public string? InventoryQuery { get; private set; }
        public int? InventoryLimit { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("FROM \"AgentTasks\"", StringComparison.Ordinal))
            {
                if (expire && !DeadlineReached)
                {
                    DeadlineReached = true;
                    clock.Advance(TimeSpan.FromSeconds(5));
                    CanceledInQuery = ct.IsCancellationRequested;
                }
                if (!command.CommandText.Contains("JOIN", StringComparison.Ordinal))
                {
                    InventoryQuery = command.CommandText;
                    var parameter = command.Parameters.Cast<DbParameter>().FirstOrDefault(p => p.Value is int i && i == 257);
                    InventoryLimit = parameter?.Value as int?;
                }
            }
            return base.ReaderExecutingAsync(command, eventData, result, ct);
        }
    }

    private sealed class RecordingGit : LandingGit
    {
        public List<string[]> Commands { get; } = [];
        public Func<IReadOnlyList<string>, LandingGitResult, LandingGitResult>? Rewrite { get; init; }
        public int Rewritten { get; private set; }
        public override async Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> args, CancellationToken ct)
        {
            Commands.Add(args.ToArray());
            var original = await base.RunAsync(repository, args, ct);
            var result = Rewrite?.Invoke(args, original) ?? original;
            if (result != original) Rewritten++;
            return result;
        }
    }

    private static AppDbContext Context(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    internal static async Task<Card> CardAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = "c442-" + Guid.NewGuid().ToString("N"),
            GitRepositoryUrl = "https://example.test/c442.git", CreatedAt = now, UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, Name = "c442",
            MaxConcurrentSessions = 1, CreatedAt = now, UpdatedAt = now,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(), BoardId = board.Id, StateKey = "backlog", Name = "Backlog",
            ColumnOrder = 0, CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now,
        };
        var card = new Card
        {
            Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id,
            Identifier = "CARD-0442", Title = "Worktree continuation",
            Description = "Fixture", CreatedAt = now, UpdatedAt = now,
        };
        db.AddRange(project, board, column, card);
        await db.SaveChangesAsync();
        return card;
    }

    internal static async Task<AgentTask> SourceAsync(AppDbContext db, ScratchGitRepo repo,
        Guid cardId, string label, string startRef, bool commit = true)
    {
        var id = Guid.NewGuid();
        var branch = "feat/card-task-" + id.ToString("N")[..8];
        await repo.GitAsync("checkout", "-b", branch, startRef);
        if (commit) await repo.CommitFileAsync(label + ".txt", label + "\n");
        await repo.GitAsync("checkout", "master");
        var task = new AgentTask
        {
            Id = id, RootTaskId = id, Title = label, Goal = label,
            Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Low, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = repo.Path, RepoPath = repo.Path, CardId = cardId,
            WorktreeBranch = branch, Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            CompletedAt = DateTime.UtcNow.AddMinutes(-30),
        };
        db.AgentTasks.Add(task);
        return task;
    }

    private static AgentTask NewRequest(ScratchGitRepo repo, Guid? cardId) => new()
    {
        Id = Guid.NewGuid(), CardId = cardId, RepoPath = repo.Path,
        Workspace = WorkspaceMode.Worktree,
    };

    private static Task<CardWorktreeBaseSelection> ResolveAsync(AppDbContext db, AgentTask request,
        ILandingGit? git = null) =>
        new AgentTaskWorktreeBaseResolver(db, git ?? new LandingGit(),
            Options.Create(new GitSettings { DefaultBranch = "master" }))
            .ResolveAsync(request, CancellationToken.None);

}
