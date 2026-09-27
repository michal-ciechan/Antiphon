using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0494: one schema and one DI graph, two or three real git roots, independently leased.
/// Production lookup is by <c>task.ActiveLandingId</c>; a cross-owner active op is visible here.
/// </summary>
internal sealed class TwoOwnerLandingWorld : IAsyncDisposable
{
    private readonly string _root;
    private readonly List<LandingGitFixture> _fixtures = [];
    public TracingLandingGit Git { get; }
    public LandingProtocolHarness.SaveFault Fault { get; } = new();
    public LandingProtocolHarness.ControlledVerifier Verifier { get; } = new();
    public IsolatedTestSchema Schema { get; private set; } = null!;
    public ServiceProvider Services { get; private set; } = null!;
    public AgentTaskLandQueue Queue { get; } = new();
    public int TraceCount => Git.Commands.Count;

    private TwoOwnerLandingWorld(string root)
    {
        _root = root;
        Git = new TracingLandingGit(Path.Combine(root, "home"));
    }

    public static async Task<TwoOwnerLandingWorld> CreateAsync()
    {
        var world = new TwoOwnerLandingWorld(Path.Combine(Path.GetTempPath(), "antiphon-c494-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(world._root);
        world.Schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        world.BuildServices();
        return world;
    }

    public IReadOnlyList<(string Directory, string[] Arguments)> TraceSince(int start) =>
        Git.Commands.Skip(start).ToList();

    public async Task<Owner> CreateOwnerAsync()
    {
        var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        _fixtures.Add(fixture);
        var owner = new Owner(fixture);
        await using var db = CreateContext();
        db.AgentTasks.Add(new AgentTask
        {
            Id = fixture.TaskId, RootTaskId = fixture.TaskId, Title = "C494 owner", Goal = "fixture",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = fixture.Repository, RepoPath = fixture.Repository, WorktreePath = fixture.Source,
            WorktreeBranch = fixture.SourceRef[11..], MergeTargetRef = "master", Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return owner;
    }

    public async Task ArrangeLegacyAsync(Owner owner)
    {
        await AddFeatureAsync(owner);
        await StopAtAsync(owner, LandPhase.Prepared);
        await using var db = CreateContext();
        var op = await db.AgentTaskLandings.SingleAsync(o => o.TaskId == owner.TaskId && o.Active);
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == op.ApprovalLandRequestId);
        op.SchemaVersion = 1;
        op.ApprovalLandRequestId = null;
        op.ReviewedSourceSha = null;
        request.SchemaVersion = 1;
        request.ExpectedSourceSha = null;
        request.ReviewEvidenceId = null;
        await db.SaveChangesAsync();
        owner.OperationId = op.Id;
        owner.RequestId = request.Id;
        owner.OriginalSha = op.OriginalSourceSha;
        owner.CreatedAt = op.CreatedAt;
        owner.HistoricalLocal = request.LocalBeforeSha.ShouldNotBeNull();
        owner.HistoricalRemote = request.RemoteSourceSha.ShouldNotBeNull();
        owner.HistoricalCandidate = request.CandidateSourceSha.ShouldNotBeNull();
        owner.RecoveryRefPrefix = op.RecoveryRefPrefix;
    }

    public async Task ArrangeVerifiedAsync(Owner owner)
    {
        await AddFeatureAsync(owner);
        await StopAtAsync(owner, LandPhase.Verified);
        await using var db = CreateContext();
        var op = await db.AgentTaskLandings.SingleAsync(o => o.TaskId == owner.TaskId && o.Active);
        op.Phase.ShouldBe(LandPhase.Verified);
        op.SchemaVersion.ShouldBe(3);
        owner.OperationId = op.Id;
        owner.RequestId = op.ApprovalLandRequestId.ShouldNotBeNull();
        owner.VerifiedSha = op.VerifiedSourceSha.ShouldNotBeNull();
        owner.OriginalSha = op.OriginalSourceSha;
    }

    public async Task ArrangeIncompleteAsync(Owner owner)
    {
        await AddFeatureAsync(owner);
        await StopAtAsync(owner, LandPhase.Prepared);
        await using var db = CreateContext();
        var op = await db.AgentTaskLandings.SingleAsync(o => o.TaskId == owner.TaskId && o.Active);
        op.SchemaVersion.ShouldBe(3);
        op.SourceRemoteSha.ShouldNotBeNull();
        op.SourceRemoteSha = null;
        await db.SaveChangesAsync();
        owner.OperationId = op.Id;
        owner.RequestId = op.ApprovalLandRequestId.ShouldNotBeNull();
        owner.OriginalSha = op.OriginalSourceSha;
    }

    public Task RunServiceAsync(Owner owner) => RunCoreAsync(owner);

    public async Task<LandingProtocolResult> ProbeAsync(Owner owner)
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await context.AgentTasks.SingleAsync(t => t.Id == owner.TaskId);
        var request = await context.AgentTaskLandRequests.SingleAsync(r => r.Id == owner.RequestId);
        await using var lease = (await scope.ServiceProvider.GetRequiredService<IRepositoryMutationLease>()
            .TryAcquireAsync(owner.Fixture.Repository, CancellationToken.None)).ShouldNotBeNull();
        return await scope.ServiceProvider.GetRequiredService<AgentTaskLandingProtocol>()
            .RunAsync(task, lease, request, CancellationToken.None);
    }

    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>(
        TestDbFixture.CreateDbContextOptions(Schema.ConnectionString))
        .AddInterceptors(Fault, new CommitFault(Fault)).Options);

    public async ValueTask DisposeAsync()
    {
        if (Services is not null) await Services.DisposeAsync();
        if (Schema is not null) await Schema.DisposeAsync();
        foreach (var fixture in _fixtures) await fixture.DisposeAsync();
        DeleteOwned(_root);
    }

    private async Task AddFeatureAsync(Owner owner)
    {
        await File.WriteAllTextAsync(Path.Combine(owner.Fixture.Source, "feature.txt"), owner.TaskId.ToString("N") + "\n");
        await owner.Fixture.RequiredAsync(owner.Fixture.Source, "add", "feature.txt");
        await owner.Fixture.RequiredAsync(owner.Fixture.Source, "commit", "-m", "feature");
        owner.FeatureSha = (await owner.Fixture.RequiredAsync(owner.Fixture.Source, "rev-parse", "HEAD")).Trim();
        owner.SeedSha = owner.Fixture.SeedSha;
    }

    private async Task StopAtAsync(Owner owner, LandPhase phase)
    {
        Fault.Rearm();
        Fault.Phase = phase;
        Fault.RequestResolution = null;
        Fault.TerminalCut = null;
        Fault.AfterCommit = true;
        Fault.AfterSave = false;
        await RequestAsync(owner);
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => RunCoreAsync(owner));
        Fault.Triggered.ShouldBeTrue();
        Disarm();
    }

    private void Disarm()
    {
        Fault.Phase = null;
        Fault.RequestResolution = null;
        Fault.TerminalCut = null;
        Fault.AfterCommit = false;
        Fault.AfterSave = false;
        Fault.Rearm();
    }

    private async Task RequestAsync(Owner owner)
    {
        await using var scope = Services.CreateAsyncScope();
        await CreateLand(scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider)
            .RequestAsync(owner.TaskId, new LandAgentTaskRequest("/*/*/Required/*", owner.FeatureSha, null), CancellationToken.None);
        Queue.Release(owner.TaskId);
    }

    private async Task<LandRunResult> RunCoreAsync(Owner owner)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            return await CreateLand(db, scope.ServiceProvider).RunAsync(owner.TaskId, null, CancellationToken.None);
        }
        finally
        {
            Queue.Release(owner.TaskId);
        }
    }

    private void BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILandingGit>(Git);
        services.AddSingleton<ILandingVerifier>(Verifier);
        services.AddScoped(_ => CreateContext());
        services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Path.Combine(_root, "land-trees") });
        services.AddScoped<AgentTaskLandingProtocol>();
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private AgentTaskLandService CreateLand(AppDbContext db, IServiceProvider services)
    {
        var tasks = new AgentTaskService(db, new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings { MaxTasksPerRoot = 40, MaxDepth = 5 }), new MockEventBus(),
            new RecordingSessionStopper(), TimeProvider.System, NullLogger<AgentTaskService>.Instance);
        return new AgentTaskLandService(db, services.GetRequiredService<DelegationWorktreeService>(),
            tasks, Queue, null!, new MockEventBus(), TimeProvider.System,
            Options.Create(new DelegationSettings()), NullLogger<AgentTaskLandService>.Instance,
            services.GetRequiredService<AgentTaskLandingProtocol>(),
            Services.GetRequiredService<IRepositoryMutationLease>(), Git);
    }

    private static void DeleteOwned(string root)
    {
        if (!Directory.Exists(root)) return;
        var full = Path.GetFullPath(root);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(full).StartsWith("antiphon-c494-", StringComparison.Ordinal))
            throw new InvalidOperationException("Fixture disposal escaped owned root");
        foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(full, true);
    }

    internal sealed class Owner(LandingGitFixture fixture)
    {
        public LandingGitFixture Fixture { get; } = fixture;
        public Guid TaskId => Fixture.TaskId;
        public Guid OperationId { get; set; }
        public Guid RequestId { get; set; }
        public string FeatureSha { get; set; } = "";
        public string SeedSha { get; set; } = "";
        public string VerifiedSha { get; set; } = "";
        public string OriginalSha { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string HistoricalLocal { get; set; } = "";
        public string HistoricalRemote { get; set; } = "";
        public string HistoricalCandidate { get; set; } = "";
        public string RecoveryRefPrefix { get; set; } = "";
    }

    internal sealed class TracingLandingGit : LandingGit
    {
        private readonly string _home;
        public List<(string Directory, string[] Arguments)> Commands { get; } = [];

        public TracingLandingGit(string home)
        {
            _home = home;
            Directory.CreateDirectory(home);
            File.WriteAllText(Path.Combine(home, "empty-config"), "");
            Directory.CreateDirectory(Path.Combine(home, "no-hooks"));
        }

        protected override void ConfigureProcess(ProcessStartInfo start)
        {
            start.Environment["HOME"] = _home;
            start.Environment["GIT_EDITOR"] = ":";
            start.Environment["GIT_SEQUENCE_EDITOR"] = ":";
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(_home, "empty-config");
            start.Environment["GIT_AUTHOR_NAME"] = "C494 Fixture";
            start.Environment["GIT_AUTHOR_EMAIL"] = "fixture@example.invalid";
            start.Environment["GIT_COMMITTER_NAME"] = "C494 Fixture";
            start.Environment["GIT_COMMITTER_EMAIL"] = "fixture@example.invalid";
            start.Environment["GIT_CONFIG_COUNT"] = "3";
            start.Environment["GIT_CONFIG_KEY_0"] = "commit.gpgSign";
            start.Environment["GIT_CONFIG_VALUE_0"] = "false";
            start.Environment["GIT_CONFIG_KEY_1"] = "credential.helper";
            start.Environment["GIT_CONFIG_VALUE_1"] = "";
            start.Environment["GIT_CONFIG_KEY_2"] = "core.hooksPath";
            start.Environment["GIT_CONFIG_VALUE_2"] = Path.Combine(_home, "no-hooks");
        }

        public override async Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Commands.Add((repository, arguments.ToArray()));
            return await base.RunAsync(repository, arguments, ct);
        }

        public override async Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments,
            Func<int, long, CancellationToken, Task> started, CancellationToken ct)
        {
            Commands.Add((repository, arguments.ToArray()));
            return await base.RunOwnedAsync(repository, arguments, started, ct);
        }
    }

    private sealed class CommitFault(LandingProtocolHarness.SaveFault fault) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(System.Data.Common.DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        { fault.Committing(); return ValueTask.FromResult(result); }

        public override async Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction,
            TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { fault.Committed(); await fault.AcknowledgedAsync(eventData.Context); }
    }
}
