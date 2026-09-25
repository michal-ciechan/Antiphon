using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0710 V-8. Launch projection, named agents, verification workspaces and probes use the
/// selected runner's entry, never another runner's store or the desktop client.
/// </summary>
[Category("Integration")]
public sealed class MultiRunnerProjectionTests
{
    [Test]
    public async Task Task_launch_uses_its_entry_and_store()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Pair(secretA, secretB));
        host.Capacity = 2;
        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", storeId: storeA, secret: secretA);
        await using var peerB = await host.ConnectPeerAsync(runnerId: "runner-b", storeId: storeB, secret: secretB);
        host.Directory.MarkRecovered(await host.WaitLiveAsync(runnerId: "runner-a"));
        host.Directory.MarkRecovered(await host.WaitLiveAsync(runnerId: "runner-b"));
        var policy = new PhoneHomeLaunchPolicy(Options.Create(Pair(secretA, secretB)));
        var spec = Spec();

        var projectedA = policy.Project(spec, Agent("runner-a"), null);
        var projectedB = policy.Project(spec, Agent("runner-b"), null);
        projectedA.Cwd.ShouldBe("/work/a");
        projectedB.Cwd.ShouldBe("/work/b");
        projectedA.Env["GROK_HOME"].ShouldBe("/state/runner-a/grok");
        projectedB.Env["GROK_HOME"].ShouldBe("/state/runner-b/grok");
        projectedA.Env["ANTIPHON_API"].ShouldNotBe(projectedB.Env["ANTIPHON_API"]);
        host.Directory.GetLiveStoreId("runner-a").ShouldBe(storeA);
        host.Directory.GetLiveStoreId("runner-b").ShouldBe(storeB);

        await host.Directory.Resolve("runner-b").ListAsync(CancellationToken.None);
        peerB.RequestCount(PhoneHomeOperation.List).ShouldBeGreaterThan(0);
        peerA.RequestCount(PhoneHomeOperation.List).ShouldBe(0);
    }

    [Test]
    public void Named_agent_uses_its_entry()
    {
        var policy = new PhoneHomeLaunchPolicy(Options.Create(Pair("a", "b")));
        var pinned = Agent("runner-a");
        pinned.Id = Guid.NewGuid();
        var projected = policy.Project(Spec(), pinned, null);
        projected.Cwd.ShouldBe("/work/a");
        projected.Env["CLAUDE_CONFIG_DIR"].ShouldBe("/state/runner-a/claude");
        policy.BoundRunnerId(pinned).ShouldBe("runner-a");
    }

    [Test]
    public async Task Verification_workspace_uses_its_entry()
    {
        var transports = new Dictionary<string, RecordingTransport>(StringComparer.Ordinal);
        var directory = new SplitDirectory(transports);
        var workspaces = new VerificationWorkspaceDirectory(directory, Options.Create(Pair("a", "b")), new LocalWorkspace());
        var created = Guid.NewGuid();
        transports["runner-a"] = new RecordingTransport
        {
            Creation = new PhoneHomeVerificationCreateResponse(Coordinates(created, "/work/a"), "abc"),
        };
        var result = await workspaces.Resolve("runner-a").CreateAsync("/work/a/repo", "1", "abc", CancellationToken.None);
        result.InitialSha.ShouldBe("abc");
        transports["runner-a"].Creates.ShouldBe(1);
        transports.TryGetValue("runner-b", out var other);
        (other?.Creates ?? 0).ShouldBe(0);

        transports["runner-a"].Creation = new PhoneHomeVerificationCreateResponse(Coordinates(created, "/work/b"), "abc");
        await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
            workspaces.Resolve("runner-a").CreateAsync("/work/a/repo", "1", "abc", CancellationToken.None));
    }

    private static VerificationCreationCoordinates Coordinates(Guid id, string root) => new(
        root + "/repo", root + "/repo", root + "/worktrees/task-1", root + "/repo/worktrees/task-1",
        "feat/card-1", id);

    [Test]
    public void Provider_probes_and_diagnostics_use_target_entry()
    {
        var policy = new PhoneHomeLaunchPolicy(Options.Create(Pair("a", "b")));
        policy.ChildGrokHomeFor("runner-a").ShouldBe("/state/runner-a/grok");
        policy.ChildGrokHomeFor("runner-b").ShouldBe("/state/runner-b/grok");
        policy.RunnerRepositoryFor("runner-a").ShouldBe("/work/a/repo");
        policy.RunnerRepositoryFor("runner-b").ShouldBe("/work/b/repo");
        policy.ClaudeAuthProbeEnabledFor("runner-a").ShouldBeTrue();
        policy.ClaudeAuthProbeEnabledFor("runner-b").ShouldBeFalse();
        var projected = policy.Project(Spec(), Agent("runner-b"), null);
        projected.Env.Values.ShouldNotContain(value => value.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    private static AgentLaunchSpec Spec() => new(
        DefinitionName: "grok", Kind: AgentKind.Grok, Exe: "grok", Args: [],
        Env: new Dictionary<string, string>(), Cwd: "/desktop", Cols: 80, Rows: 24);

    private static Agent Agent(string runnerId) => new()
    {
        Id = Guid.NewGuid(),
        Name = runnerId,
        Slug = runnerId,
        WorkingDirectory = "/work",
        Details = "projection",
        Status = AgentStatus.Idle,
        Kind = AgentKind.Grok,
        ModelLevel = AgentModelLevel.Medium,
        RunnerId = runnerId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static PhoneHomeRunnerSettings Pair(string secretA, string secretB) => new()
    {
        Enabled = true,
        Runners = new Dictionary<string, PhoneHomeRunnerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["runner-a"] = Entry("runner-a", secretA, "/work/a", claudeProbe: true),
            ["runner-b"] = Entry("runner-b", secretB, "/work/b", claudeProbe: false),
        },
    };

    private static PhoneHomeRunnerEntry Entry(string name, string secret, string workspace, bool claudeProbe) => new()
    {
        Enabled = true,
        DisplayName = name,
        AllowDelegatedTasks = true,
        HostWorkspaceRoot = "/work",
        RunnerWorkspace = workspace,
        RunnerRepository = workspace + "/repo",
        CallbackOrigin = "https://" + name + ".test",
        SharedSecret = secret,
        ChildGrokHome = "/state/" + name + "/grok",
        ChildClaudeHome = "/state/" + name + "/claude",
        ClaudeAuthProbeEnabled = claudeProbe,
    };

    private sealed class SplitDirectory(Dictionary<string, RecordingTransport> transports) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local { get; } = new PhoneHomeTestHost.RecordingLocalClient();
        public IReadOnlyList<string> KnownRunnerIds => ["runner-a", "runner-b"];
        public Guid? GetLiveStoreId(string? runnerId) => runnerId == "runner-b"
            ? Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
            : Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerDescriptor?>(null);
        public ISessionRunnerClient Resolve(string? runnerId)
        {
            var id = runnerId ?? "runner-a";
            if (!transports.TryGetValue(id, out var transport))
                transports[id] = transport = new RecordingTransport();
            return transport;
        }
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Missing.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable("unused"));
    }

    private sealed class RecordingTransport : ISessionRunnerClient, IVerificationWorkspaceTransport
    {
        public int Creates { get; private set; }
        public PhoneHomeVerificationCreateResponse Creation { get; set; } = new(
            new VerificationCreationCoordinates("/work/a/repo", "/work/a/repo", "/work/a/worktrees/task-1", "/work/a/repo/worktrees/task-1", "feat/card-1", Guid.NewGuid()),
            "abc");
        public Task<PhoneHomeVerificationCreateResponse> CreateAsync(PhoneHomeVerificationCreateRequest request, CancellationToken ct)
        {
            Creates++;
            return Task.FromResult(Creation);
        }
        public Task<PhoneHomeVerificationValidateResponse> ValidateAsync(PhoneHomeVerificationValidateRequest request, CancellationToken ct) =>
            Task.FromResult(new PhoneHomeVerificationValidateResponse(true, null));
        public Task<PhoneHomeVerificationInspectResponse> InspectAsync(PhoneHomeVerificationInspectRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<PhoneHomeVerificationReadRestorationResponse> ReadRestorationAsync(PhoneHomeVerificationReadRestorationRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<PhoneHomeVerificationRemoveResponse> RemoveAsync(PhoneHomeVerificationRemoveRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) => Task.FromResult<RunnerCapabilitiesDto?>(null);
        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => Empty();
        private static async IAsyncEnumerable<SessionRunnerEvent> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class LocalWorkspace : IVerificationWorkspace
    {
        public Task<VerificationWorkspaceCreation> CreateAsync(string repositoryPath, string identifier, string landedSha, CancellationToken ct) =>
            throw new InvalidOperationException("desktop workspace must not be used");
        public Task<VerificationWorkspaceValidation> ValidateAsync(VerificationCreationCoordinates coordinates, string landedSha, CancellationToken ct) =>
            throw new InvalidOperationException("desktop workspace must not be used");
        public Task<VerificationWorkspaceInspection> InspectAsync(string worktreePath, CancellationToken ct) =>
            throw new InvalidOperationException("desktop workspace must not be used");
        public Task<byte[]?> ReadRestorationAsync(string commonGitDirectory, Guid sourceOperationId, Guid taskId, CancellationToken ct) =>
            throw new InvalidOperationException("desktop workspace must not be used");
        public Task<VerificationWorkspaceRemoval> RemoveAsync(VerificationCreationCoordinates coordinates, string expectedSha, IReadOnlyList<string> expectedOutputs, CancellationToken ct) =>
            throw new InvalidOperationException("desktop workspace must not be used");
    }
}
