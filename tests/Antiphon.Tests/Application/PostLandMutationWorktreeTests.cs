using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PostLandMutationWorktreeTests
{
    [Test]
    public async Task C478_V02_RebasedSnapshotAfterSourceRemoval()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        var c = world.Host.Fixture.SeedSha;
        task.SourceLandingSha.ShouldBe(c);
        task.WorktreeBaseSha.ShouldBe(c);
        (await world.Host.Fixture.RequiredAsync(task.WorktreePath!, "rev-parse", "HEAD")).Trim().ShouldBe(c);
        await File.WriteAllTextAsync(Path.Combine(world.Host.Fixture.Repository, "later-r.txt"), "later R\n");
        await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "add", ".");
        await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "commit", "-m", "advance target to R");
        var r = (await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "rev-parse", "HEAD")).Trim();
        r.ShouldNotBe(c);
        await using var lease = await world.Host.Services.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(task.RepoPath!, default);
        await scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().ValidateVerificationAsync(task, lease!, default);
        (await world.Host.Fixture.RequiredAsync(task.WorktreePath!, "rev-parse", "HEAD")).Trim().ShouldBe(c);
    }

    [Test]
    public async Task C478_V03_CreateRestartAndMissingCommit()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.WorktreePath.ShouldContain(DelegationReportFormatter.Short(world.TaskId));
        task.MergeTargetRef.ShouldBeNull();
        await using var lease = await world.Host.Services.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(task.RepoPath!, default);
        await scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().ValidateVerificationAsync(task, lease!, default);
        task.SourceLandingSha = new string('0', 40);
        await Should.ThrowAsync<ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().ValidateVerificationAsync(task, lease!, default));
    }

    [Test]
    public async Task C478_V05_SettlementNeverPublishesSnapshot()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        await File.AppendAllTextAsync(Path.Combine(task.WorktreePath!, "keep.txt"), "mutant\n");
        var before = await world.Host.Fixture.RequiredAsync(task.WorktreePath!, "diff");
        var outcome = await scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().TryMergeBackAsync(task, default);
        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.LeftForHuman);
        (await world.Host.Fixture.RequiredAsync(task.WorktreePath!, "diff")).ShouldBe(before);
        await using var lease = await world.Host.Services.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(task.RepoPath!, default);
        await Should.ThrowAsync<ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<AgentTaskLandingProtocol>().RunAsync(task, lease!, default));
    }

    [Test]
    public async Task C478_G056_ExactL()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var db = world.Host.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.WorktreeBaseSha.ShouldBe(task.SourceLandingSha);
        (await world.Host.Fixture.RequiredAsync(task.WorktreePath!, "rev-parse", "HEAD")).Trim().ShouldBe(task.SourceLandingSha);
    }

    [Test]
    public async Task C478_G074_NoLandRequest() => await C478_V05_SettlementNeverPublishesSnapshot();

    [Test]
    [Arguments(AgentKind.ClaudeCode)]
    [Arguments(AgentKind.Codex)]
    [Arguments(AgentKind.Grok)]
    public async Task C478_V04_FreshLaunchAndConcurrentCode(AgentKind kind)
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var db = world.Host.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.WorktreePath.ShouldNotBe(world.Host.Fixture.Source);
        task.WorktreePath.ShouldContain(DelegationReportFormatter.Short(world.TaskId));
        task.MergeTargetRef.ShouldBeNull();
        var (dispatcher, provider) = LaunchHarness();
        using (provider)
        {
            var spec = LaunchSpec(dispatcher, task, kind);
            spec.Cwd.ShouldBe(task.WorktreePath);
            var text = kind switch
            {
                AgentKind.Codex => CodexInstructions(spec.Args),
                AgentKind.Grok => spec.GrokRulesPayload.ShouldNotBeNull().Content,
                _ => spec.Args[spec.Args.ToList().IndexOf("--append-system-prompt") + 1],
            };
            text.ShouldContain("[bundle:stage-mutation v");
            text.Split("[bundle:stage-mutation v").Length.ShouldBe(2);
            text.ShouldNotContain("[bundle:stage-code");
        }
        await ConcurrentNextCardCodeAsync();
    }

    [Test]
    public async Task C478_G057_NotRemoteTip() => await C478_V02_RebasedSnapshotAfterSourceRemoval();

    [Test]
    public async Task C478_G058_NoInheritedTarget()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var db = world.Host.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.MergeTargetRef.ShouldBeNull();
        (await db.AgentTasks.SingleAsync(t => t.Id == world.Host.Fixture.TaskId)).MergeTargetRef.ShouldBe("master");
    }

    [Test]
    public async Task C478_G059_FreshPath()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var db = world.Host.CreateContext();
        var mutation = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        var source = await db.AgentTasks.SingleAsync(t => t.Id == world.Host.Fixture.TaskId);
        mutation.WorktreePath.ShouldNotBe(source.WorktreePath);
        mutation.WorktreePath.ShouldContain(DelegationReportFormatter.Short(world.TaskId));
    }

    [Test]
    public async Task C478_G060_FreshBranch()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var db = world.Host.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.WorktreeBranch.ShouldBe("feat/card-task-" + DelegationReportFormatter.Short(world.TaskId));
        task.WorktreeBranch.ShouldNotBe((await db.AgentTasks.SingleAsync(t => t.Id == world.Host.Fixture.TaskId)).WorktreeBranch);
    }

    [Test] public Task C478_G061_FreshProcess() => C478_V04_FreshLaunchAndConcurrentCode(AgentKind.ClaudeCode);
    [Test] public Task C478_G062_Bundle() => C478_V04_FreshLaunchAndConcurrentCode(AgentKind.Codex);

    [Test]
    public async Task C478_G063_CreateLease()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var leases = world.Host.Services.GetRequiredService<IRepositoryMutationLease>();
        await using var held = await leases.TryAcquireAsync(world.Host.Fixture.Repository, default);
        held.ShouldNotBeNull();
        (await leases.TryAcquireAsync(world.Host.Fixture.Repository, default)).ShouldBeNull();
    }

    [Test]
    public async Task C478_G064_RetryIdentity()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        var original = task.WorktreePath;
        task.WorktreePath = world.Host.Fixture.Source;
        await using var lease = await world.Host.Services.GetRequiredService<IRepositoryMutationLease>()
            .TryAcquireAsync(task.RepoPath!, default);
        await Should.ThrowAsync<ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().ValidateVerificationAsync(task, lease!, default));
        Directory.Exists(original!).ShouldBeTrue();
    }

    [Test]
    public async Task C478_G065_RetryHead()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        await File.AppendAllTextAsync(Path.Combine(task.WorktreePath!, "keep.txt"), "moved-head\n");
        await world.Host.Fixture.RequiredAsync(task.WorktreePath!, "add", ".");
        await world.Host.Fixture.RequiredAsync(task.WorktreePath!, "commit", "-m", "move HEAD");
        await using var lease = await world.Host.Services.GetRequiredService<IRepositoryMutationLease>()
            .TryAcquireAsync(task.RepoPath!, default);
        await Should.ThrowAsync<ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().ValidateVerificationAsync(task, lease!, default));
        (await world.Host.Fixture.RequiredAsync(task.WorktreePath!, "rev-parse", "HEAD")).Trim().ShouldNotBe(task.SourceLandingSha);
    }

    [Test]
    public async Task C478_G066_RetryGit()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        var creation = System.Text.Json.JsonSerializer.Deserialize<Antiphon.SessionRunner.Contracts.VerificationCreationCoordinates>(task.VerificationCreationJson!)!;
        task.VerificationCreationJson = System.Text.Json.JsonSerializer.Serialize(creation with
        { WorktreeGitDirectory = Path.Combine(world.Host.Fixture.Root, "stranger.git") });
        await using var lease = await world.Host.Services.GetRequiredService<IRepositoryMutationLease>()
            .TryAcquireAsync(task.RepoPath!, default);
        await Should.ThrowAsync<ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().ValidateVerificationAsync(task, lease!, default));
    }

    [Test] public Task C478_G067_MissingCommit() => C478_V03_CreateRestartAndMissingCommit();
    [Test] public Task C478_G071_CallerSettlement() => C478_V05_SettlementNeverPublishesSnapshot();
    [Test] public Task C478_G072_LowerSettlement() => C478_V05_SettlementNeverPublishesSnapshot();
    [Test] public Task C478_G073_NoLocalMerge() => C478_V05_SettlementNeverPublishesSnapshot();
    [Test] public Task C478_G075_NoLandExecution() => C478_V05_SettlementNeverPublishesSnapshot();
    [Test] public Task C478_G076_NoProgress() => C478_V05_SettlementNeverPublishesSnapshot();
    [Test] public Task C478_G083_CreationFailure() => C478_G064_RetryIdentity();

    [Test]
    public async Task C478_G077_NoSettlementDelete()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        await scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().TryMergeBackAsync(task, default);
        Directory.Exists(task.WorktreePath!).ShouldBeTrue();
        (await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "show-ref", "--verify",
            "refs/heads/" + task.WorktreeBranch)).ShouldNotBeNull();
    }

    [Test]
    public async Task C478_G078_InterruptedCustody()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var db = world.Host.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.Status = AgentTaskStatus.Failed;
        await db.SaveChangesAsync();
        task.SourceLandingOperationId.ShouldBe(world.Operation);
        task.SourceLandingSha.ShouldBe(world.Host.Fixture.SeedSha);
        Directory.Exists(task.WorktreePath!).ShouldBeTrue();
    }

    [Test]
    public async Task C478_G079_EvidenceOutside()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var evidence = await world.EvidencePathAsync();
        var tree = await world.PathAsync();
        Path.GetFullPath(evidence).StartsWith(Path.GetFullPath(tree) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        File.Exists(evidence).ShouldBeTrue();
    }

    [Test]
    public async Task C478_G080_OrdinaryCode()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var db = world.Host.CreateContext();
        var source = await db.AgentTasks.SingleAsync(t => t.Id == world.Host.Fixture.TaskId);
        source.Role.ShouldBe(AgentTaskRole.Code);
        source.MergeTargetRef.ShouldBe("master");
        source.SourceLandingOperationId.ShouldBeNull();
    }

    [Test] public Task C478_G081_RetryRegistration() => C478_G064_RetryIdentity();
    [Test] public Task C478_G082_ProviderCap() => ConcurrentNextCardCodeAsync();

    [Test]
    public async Task C478_G084_NoSilentRetry()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using (var db = world.Host.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            task.Status = AgentTaskStatus.Failed;
            await db.SaveChangesAsync();
        }
        await using var observer = world.Host.CreateContext();
        (await observer.AgentTasks.CountAsync(t => t.SourceLandingOperationId == world.Operation)).ShouldBe(1);
    }

    private static async Task ConcurrentNextCardCodeAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var factory = new Antiphon.Tests.Application.MutationDispatchTestsCapture();
        await using var bridge = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            Delegation = new DelegationSettings { MaxConcurrentTasks = 6, SerialiseSharedWriters = true },
            ConfigureServices = services =>
            {
                services.AddSingleton<IAgentProtocolAdapterFactory>(factory);
                services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
                    new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(new()
                    {
                        DefaultDefinition = "claude",
                        Definitions = { ["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "fixture-only" } },
                    }));
                services.RemoveAll<IWorktreeManager>();
                services.AddDelegationWorktreeGraph();
                services.AddSingleton<DelegationWorkspaceResolver>();
                services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
                services.AddScoped<AgentTaskService>();
                services.AddSingleton<AgentTaskReplyService>();
                services.AddScoped<AgentTaskDispatcher>();
            },
        });
        bridge.Provider.GetRequiredService<IOptions<GitSettings>>().Value.WorktreeBasePath =
            Path.Combine(bridge.TempRoot, "worktrees");
        await GitAsync(bridge.TempRoot, "init", "-b", "master");
        await GitAsync(bridge.TempRoot, "config", "user.name", "C478");
        await GitAsync(bridge.TempRoot, "config", "user.email", "c478@example.test");
        await File.WriteAllTextAsync(Path.Combine(bridge.TempRoot, "base.txt"), "master");
        await GitAsync(bridge.TempRoot, "add", "base.txt");
        await GitAsync(bridge.TempRoot, "commit", "-m", "base");
        var projectId = Guid.NewGuid();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            db.Projects.Add(new Project
            {
                Id = projectId, Name = "c478-v04", GitRepositoryUrl = "https://example.test/c478.git",
                LocalRepositoryPath = bridge.TempRoot, BaseBranch = "master",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            var board = new Board { Id = Guid.NewGuid(), ProjectId = projectId, Name = "c478", MaxConcurrentSessions = 2,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "In progress", StateKey = "inprogress",
                CardStatus = CardStatus.InProgress, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            var a = new Card { Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id, Identifier = "CARD-0478",
                Title = "mutation", Status = CardStatus.InProgress, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            var b = new Card { Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id, Identifier = "CARD-0479",
                Title = "code", Status = CardStatus.InProgress, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            db.AddRange(board, column, a, b);
            var mutationId = Guid.NewGuid();
            var codeId = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = mutationId, RootTaskId = mutationId, Role = AgentTaskRole.Mutation, Kind = AgentTaskKind.Worker,
                Status = AgentTaskStatus.Working, Title = "Mutation", Goal = "battery", ProjectId = projectId,
                CardId = a.Id, WorkingDirectory = bridge.TempRoot, RepoPath = bridge.TempRoot,
                Workspace = WorkspaceMode.Shared, AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Frontier,
                Ephemeral = true, CreatedAt = DateTime.UtcNow,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = codeId, RootTaskId = codeId, Role = AgentTaskRole.Code, Kind = AgentTaskKind.Worker,
                Status = AgentTaskStatus.Queued, Title = "Code", Goal = "next card", ProjectId = projectId,
                CardId = b.Id, WorkingDirectory = bridge.TempRoot, RepoPath = bridge.TempRoot,
                Workspace = WorkspaceMode.Worktree, AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Frontier,
                Ephemeral = true, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            using var tick = bridge.Provider.CreateScope();
            await tick.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(default);
            await bridge.Provider.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var dispatched = await verify.AgentTasks.SingleAsync(t => t.Id == codeId);
            dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched, dispatched.FailureReason);
            dispatched.WorktreePath.ShouldNotBeNull();
            (await verify.AgentTasks.SingleAsync(t => t.Id == mutationId)).Status.ShouldBe(AgentTaskStatus.Working);
            factory.Count.ShouldBe(1);
        }
    }

    private static async Task GitAsync(string directory, params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git")
        { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, await error);
        _ = await output;
    }

    private static (AgentTaskDispatcher Dispatcher, ServiceProvider Provider) LaunchHarness()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings()));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.GrokCredentialProbeEnabled = false;
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
            s.Definitions["grok"] = new AgentDefinition { Kind = "Grok", Exe = "grok", ArgsTemplate = ["--always-approve", "--no-alt-screen"] };
            s.Definitions["codex"] = new AgentDefinition
            {
                Kind = "Codex", Exe = "codex",
                ArgsTemplate = ["--no-alt-screen", "--dangerously-bypass-approvals-and-sandbox"],
            };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-c478-wt") });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(), provider);
    }

    private static AgentLaunchSpec LaunchSpec(AgentTaskDispatcher dispatcher, AgentTask task, AgentKind kind)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(), Name = "c478-" + DelegationReportFormatter.Short(task.Id),
            Slug = "c478-" + DelegationReportFormatter.Short(task.Id), WorkingDirectory = task.WorktreePath ?? task.WorkingDirectory,
            Kind = kind, IsPoolDelegate = true,
        };
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = kind switch { AgentKind.Grok => "grok", AgentKind.Codex => "codex", _ => "claude" },
            AgentKind = kind, Status = SessionStatus.Starting, Cwd = agent.WorkingDirectory, Cols = 120, Rows = 30,
        };
        return dispatcher.BuildLaunchSpec(task, agent, session);
    }

    private static string CodexInstructions(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (args[i] == "-c" && args[i + 1].StartsWith("developer_instructions=", StringComparison.Ordinal))
                return args[i + 1]["developer_instructions=".Length..];
        throw new InvalidOperationException("missing Codex developer_instructions");
    }
}

internal sealed class MutationDispatchTestsCapture : IAgentProtocolAdapterFactory
{
    public int Count { get; private set; }
    public IAgentProtocolAdapter Create(AgentKind kind)
    {
        Count++;
        return new Antiphon.Tests.Agents.FakeAgentProtocolAdapter();
    }
}
