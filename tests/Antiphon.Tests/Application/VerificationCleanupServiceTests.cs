using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0604 D-19 (Cut B), S11. The two branches cleanup grew for a task bound to a REMOTE
/// runner, each of which has a silent failure mode if it falls through to the desktop:
///
/// <list type="bullet">
/// <item>G-33: custody is read from the runner the task is bound to. The local client never held
/// the execution, so it would answer a confident "unsupported backend" and the cleanup would
/// mark a perfectly good execution Unknown.</item>
/// <item>G-38: the snapshot lives on the runner, so its removal goes through the workspace seam.
/// <c>IWorktreeManager.TryRemoveAsync</c> against a desktop path that was never created would
/// report a clean removal of nothing -- a false "residue: null".</item>
/// </list>
///
/// <para>Both need the real database (the service takes <c>AppDbContext</c> and takes row locks),
/// so this class is Integration and sits outside CP-19, whose rows are the no-Postgres ones.</para>
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class VerificationCleanupServiceTests
{
    private const string RemoteRunner = "server2";

    [Test]
    public async Task Remote_task_custody_read_uses_bound_runner()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var binding = await world.ReserveAsync();
        await world.TerminalAsync();
        await BindToRemoteRunnerAsync(world);

        var asked = new List<(VerificationExecutionBinding Binding, bool Seal)>();
        var runner = (FakeSessionRunnerClient)world.Runner;
        runner.VerificationCustody = (b, seal, _) =>
        {
            asked.Add((b, seal));
            return Task.FromResult(new VerificationCustodyStatus(b, VerificationCustodyState.Unknown, "host_lost"));
        };
        var runners = new SingleRunnerDirectory(runner, RemoteRunner);

        await using var scope = world.Host.Services.CreateAsyncScope();
        var result = await Service(world, scope, runners, new RecordingWorkspaceDirectory())
            .CleanupAsync(world.TaskId, default);

        // The read happened, it happened once, and it named the runner the task is bound to.
        // A regression to the local client shows up here as a null in Resolved.
        runners.Resolved.ShouldBe([RemoteRunner]);
        asked.Count.ShouldBe(1);
        asked[0].Binding.ShouldBe(binding);
        asked[0].Seal.ShouldBeTrue("cleanup seals the execution it is retiring");
        result.Residue.ShouldNotBeNull();
        result.Residue.ShouldContain("host_lost");

        await using var db = world.Host.CreateContext();
        var execution = await db.VerificationExecutions.SingleAsync(e => e.Id == binding.ExecutionId);
        execution.ReceiptBytes.ShouldBeNull("an unreachable custody read never becomes terminal evidence");
        execution.CustodyReason.ShouldContain("Unknown");
    }

    [Test]
    public async Task Remote_removal_goes_through_workspace_seam()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var binding = await world.ReserveAsync();
        await world.TerminalAsync();
        await world.SeedImportedReceiptAsync(binding, world.ValidReceipt(binding));
        var restoration = await world.RestorationBytesAsync(new VerificationOutput("evidence/report.md", new string('b', 64)));
        await BindToRemoteRunnerAsync(world);
        var snapshot = await world.PathAsync();

        var workspace = new RecordingWorkspaceDirectory
        {
            Restoration = restoration,
            Removal = new(Unregistered: true, DirectoryGone: true, BranchDeleted: true, Residue: null),
        };
        var runners = new SingleRunnerDirectory(
            (FakeSessionRunnerClient)world.Runner, RemoteRunner);
        world.Host.Fixture.Git.Trace.Clear();

        await using var scope = world.Host.Services.CreateAsyncScope();
        var result = await Service(world, scope, runners, workspace).CleanupAsync(world.TaskId, default);

        result.Residue.ShouldBeNull();
        result.DirectoryGone.ShouldBeTrue();
        result.BranchDeleted.ShouldBeTrue();

        // It went to the bound runner's workspace, with the seal's own coordinates and the
        // producer's own expected-output list -- never a caller-supplied path.
        workspace.Resolved.ShouldBe([RemoteRunner]);
        workspace.RemoveCalls.Count.ShouldBe(1);
        workspace.RemoveCalls[0].Coordinates.WorktreePath.ShouldBe(snapshot);
        workspace.RemoveCalls[0].ExpectedOutputs.ShouldBe(["evidence/report.md"]);
        workspace.ReadRestorationCalls.Count.ShouldBe(1);
        workspace.ReadRestorationCalls[0].TaskId.ShouldBe(world.TaskId);

        // And the desktop was not touched: no local git removal, and the snapshot directory the
        // local manager would have deleted is still there.
        world.Host.Fixture.Git.Trace.Any(PostLandMutationWorld.IsDestructive)
            .ShouldBeFalse("a remote snapshot must not be removed through the local worktree manager");
        Directory.Exists(snapshot).ShouldBeTrue();

        await using var db = world.Host.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.VerificationDirectoryRemoved.ShouldBeTrue();
        task.VerificationBranchRemoved.ShouldBeTrue();
        task.VerificationCleanupResidue.ShouldBeNull();
    }

    // A remote task whose runner never wrote a restoration record has no authority to remove
    // anything, and must not fall back to the local removal that would succeed on nothing.
    [Test]
    public async Task Remote_removal_without_a_restoration_record_refuses()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var binding = await world.ReserveAsync();
        await world.TerminalAsync();
        await world.SeedImportedReceiptAsync(binding, world.ValidReceipt(binding));
        await BindToRemoteRunnerAsync(world);
        var snapshot = await world.PathAsync();

        var workspace = new RecordingWorkspaceDirectory { Restoration = null };
        var runners = new SingleRunnerDirectory((FakeSessionRunnerClient)world.Runner, RemoteRunner);
        world.Host.Fixture.Git.Trace.Clear();

        await using var scope = world.Host.Services.CreateAsyncScope();
        var result = await Service(world, scope, runners, workspace).CleanupAsync(world.TaskId, default);

        result.Residue.ShouldBe("verification_restoration_missing_or_mismatched");
        workspace.RemoveCalls.ShouldBeEmpty();
        world.Host.Fixture.Git.Trace.Any(PostLandMutationWorld.IsDestructive).ShouldBeFalse();
        Directory.Exists(snapshot).ShouldBeTrue();
    }

    private static VerificationCleanupService Service(PostLandMutationWorld world,
        AsyncServiceScope scope, ISessionRunnerDirectory runners, IVerificationWorkspaceDirectory workspaces) =>
        new(scope.ServiceProvider.GetRequiredService<AppDbContext>(), runners,
            world.Host.Services.GetRequiredService<IRepositoryMutationLease>(),
            world.Host.Services.GetRequiredService<IWorktreeManager>(), TimeProvider.System, workspaces);

    /// <summary>
    /// Bind the already-provisioned task to a remote runner. The reservation above ran against
    /// the local runner on purpose: the binding's backend and store are then unambiguously the
    /// ones the execution really had, so the assertions below are about WHO is asked, not about
    /// what a remote reservation would have written.
    /// </summary>
    private static async Task BindToRemoteRunnerAsync(PostLandMutationWorld world)
    {
        await using var db = world.Host.CreateContext();
        await db.AgentTasks.Where(t => t.Id == world.TaskId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RunnerId, RemoteRunner));
    }

    private sealed class RecordingWorkspaceDirectory : IVerificationWorkspaceDirectory, IVerificationWorkspace
    {
        public List<string?> Resolved { get; } = [];
        public List<(VerificationCreationCoordinates Coordinates, string ExpectedSha, IReadOnlyList<string> ExpectedOutputs)>
            RemoveCalls { get; } = [];
        public List<(string CommonGitDirectory, Guid OperationId, Guid TaskId)> ReadRestorationCalls { get; } = [];
        public byte[]? Restoration { get; set; }
        public VerificationWorkspaceRemoval Removal { get; set; } = new(false, false, false, "not_configured");

        public IVerificationWorkspace Resolve(string? runnerId)
        {
            Resolved.Add(runnerId);
            return this;
        }

        public Task<byte[]?> ReadRestorationAsync(string commonGitDirectory, Guid sourceOperationId,
            Guid taskId, CancellationToken ct)
        {
            ReadRestorationCalls.Add((commonGitDirectory, sourceOperationId, taskId));
            return Task.FromResult(Restoration);
        }

        public Task<VerificationWorkspaceRemoval> RemoveAsync(VerificationCreationCoordinates coordinates,
            string expectedSha, IReadOnlyList<string> expectedOutputs, CancellationToken ct)
        {
            RemoveCalls.Add((coordinates, expectedSha, expectedOutputs));
            return Task.FromResult(Removal);
        }

        public Task<VerificationWorkspaceCreation> CreateAsync(string repositoryPath, string identifier,
            string landedSha, CancellationToken ct) =>
            throw new NotSupportedException("cleanup never creates a workspace");

        public Task<VerificationWorkspaceValidation> ValidateAsync(VerificationCreationCoordinates coordinates,
            string landedSha, CancellationToken ct) =>
            throw new NotSupportedException("cleanup never validates a workspace");

        public Task<VerificationWorkspaceInspection> InspectAsync(string worktreePath, CancellationToken ct) =>
            throw new NotSupportedException("cleanup never inspects a workspace");
    }
}
