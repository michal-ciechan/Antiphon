using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// Each crossed coordinate remains otherwise valid. A mutation-capable downstream
// fake exposes the first unauthorized deletion even when real Git has another guard.
[Category("Unit")]
public sealed class LandingRemovalPolicyControlTests
{
    [Test]
    [Arguments(1, ".antiphon/report.md")]
    [Arguments(1, ".claude/settings.json")]
    [Arguments(1, "bin-private/keep.txt")]
    [Arguments(2, ".antiphon/report.md")]
    [Arguments(2, ".claude/settings.json")]
    [Arguments(2, "bin-private/keep.txt")]
    [Arguments(1, "head")]
    [Arguments(2, "head")]
    public async Task C448_V18_EachContentReadingRefusesBeforeItsNextCommand(int reading, string change)
    {
        using var f = new RemovalFixture();
        f.AfterInspection = count =>
        {
            if (count < reading) return;
            if (change == "head") f.InspectedSha = RemovalFixture.Other;
            else
            {
                f.IgnoredPath = change;
                var path = Path.Combine(f.Source, change);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "new private bytes");
            }
        };
        var result = await f.RemoveAsync();
        f.Mutations.ShouldBeEmpty("each content/identity reading must refuse before deletion");
        f.InspectionCount.ShouldBe(reading, "a later guard cannot replace the refusal at this reading");
        result.IsClean.ShouldBeFalse();
        if (change != "head") File.ReadAllText(Path.Combine(f.Source, change)).ShouldBe("new private bytes");
        f.BranchPresent.ShouldBeTrue();
    }

    [Test]
    public async Task C448_V18_DurableAuthorityIsReadAfterTheFinalInspection()
    {
        using var f = new RemovalFixture();
        f.AfterInspection = count =>
        {
            if (count != 2) return;
            f.Operation.TaskId = Guid.NewGuid();
            f.Operation.RecoveryRefPrefix = $"refs/antiphon/land/{f.Operation.TaskId:N}/{f.Operation.Id:N}";
        };
        var result = await f.RemoveAsync();
        f.InspectionCount.ShouldBe(2);
        f.Mutations.ShouldBeEmpty("a receipt change during final inspection must prevent directory deletion");
        result.IsClean.ShouldBeFalse();
        File.ReadAllText(Path.Combine(f.Source, "keep.txt")).ShouldBe("private work");
        f.BranchPresent.ShouldBeTrue();
    }

    [Test]
    [Arguments("valid")]
    [Arguments("head")]
    [Arguments("target")]
    [Arguments("symbolic")]
    [Arguments("dirty")]
    [Arguments("status-error")]
    [Arguments("sequencer")]
    [Arguments("checkout")]
    public async Task C448_V11_EachTargetDecisionRefusesIndependently(string change)
    {
        using var f = new RemovalFixture();
        f.LocalChange = change;
        f.Operation.TargetCheckoutRecorded = true;
        f.Operation.TargetCheckoutPath = f.Root;
        var protocol = new AgentTaskLandingProtocol(null!, f, f, null!, null!, TimeProvider.System);
        // Exercise the existing decision directly without adding a production API for tests.
        // Real-Git boundary cases separately assert the target index, files and remote state.
        var method = typeof(AgentTaskLandingProtocol).GetMethod("CheckTargetAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Exception? failure = null;
        try { await (Task)method.Invoke(protocol, [f.Operation, RemovalFixture.Sha, CancellationToken.None])!; }
        catch (Exception ex) { failure = ex; }
        if (change == "valid") failure.ShouldBeNull();
        else failure.ShouldNotBeNull("the target decision must refuse this independently changed component: " + change)
            .GetType().Name.ShouldBe("LandingRefusal");
        f.Mutations.ShouldBeEmpty();
        File.ReadAllText(Path.Combine(f.Source, "keep.txt")).ShouldBe("private work");
    }

    [Test]
    [Arguments("valid")]
    [Arguments("head")]
    [Arguments("target")]
    [Arguments("symbolic")]
    [Arguments("target-symbolic")]
    [Arguments("dirty")]
    [Arguments("status-error")]
    [Arguments("sequencer")]
    [Arguments("checkout")]
    [Arguments("ancestry")]
    public async Task C448_V25_LocalAuthorityChecksTheCurrentParent(string change)
    {
        using var f = new RemovalFixture();
        f.Request = f.Request with { Purpose = WorktreeRemovalPurpose.LocalMerge,
            TargetCheckoutRecorded = true, TargetCheckoutPath = f.Root };
        f.LocalChange = change;
        var result = await f.RemoveAsync();
        if (change == "valid")
        {
            result.IsClean.ShouldBeTrue();
            f.Mutations.Count.ShouldBe(2);
        }
        else
        {
            f.Mutations.ShouldBeEmpty("local merge proof cannot authorize deletion after a parent change: " + change);
            result.IsClean.ShouldBeFalse();
            File.ReadAllText(Path.Combine(f.Source, "keep.txt")).ShouldBe("private work");
            f.BranchPresent.ShouldBeTrue();
        }
    }

    [Test]
    [Arguments("valid")]
    [Arguments("task")]
    [Arguments("operation")]
    [Arguments("repository")]
    [Arguments("path")]
    [Arguments("git-directory")]
    [Arguments("common-directory")]
    [Arguments("source-ref")]
    [Arguments("target-ref")]
    [Arguments("target-sha")]
    [Arguments("deletion-sha")]
    [Arguments("verified-sha")]
    [Arguments("cleanup-intent")]
    [Arguments("phase")]
    [Arguments("inactive")]
    [Arguments("schema")]
    [Arguments("unconfirmed")]
    [Arguments("operation-namespace")]
    [Arguments("destination")]
    [Arguments("fingerprint")]
    [Arguments("confirmation-method")]
    [Arguments("observed-sha")]
    [Arguments("lease")]
    public async Task C448_V36_EachAuthorityCoordinatePrecedesMutation(string change)
    {
        using var f = new RemovalFixture();
        if (change != "valid")
        {
            using var safe = new RemovalFixture();
            (await safe.RemoveAsync()).IsClean.ShouldBeTrue("the unchanged command path must be reachable");
            safe.Mutations.Count.ShouldBe(2);
        }
        var op = f.Operation;
        switch (change)
        {
            case "task": op.TaskId = Guid.NewGuid(); op.RecoveryRefPrefix = $"refs/antiphon/land/{op.TaskId:N}/{op.Id:N}"; break;
            case "operation": op.Id = Guid.NewGuid(); op.RecoveryRefPrefix = $"refs/antiphon/land/{op.TaskId:N}/{op.Id:N}"; break;
            case "repository": op.RepositoryPath = Path.Combine(f.Root, "other"); break;
            case "path": op.WorktreePath = Path.Combine(f.Root, "other"); break;
            case "git-directory": op.GitDirectory = Path.Combine(f.Root, "other"); break;
            case "common-directory": op.CommonDirectory = Path.Combine(f.Root, "other"); break;
            case "source-ref": op.SourceFullRef = "refs/heads/other"; break;
            case "target-ref": op.TargetFullRef = op.DestinationFullRef = "refs/heads/other"; break;
            case "target-sha": op.TargetBeforeSha = RemovalFixture.Other; break;
            case "deletion-sha": op.ExpectedDeletionSha = RemovalFixture.Other; break;
            case "verified-sha": op.VerifiedSourceSha = op.RebasedSourceSha = RemovalFixture.Other; op.PreparedPinned = true; break;
            case "cleanup-intent": op.CleanupStartedAt = null; break;
            case "phase": op.Phase = LandPhase.PublicationConfirmed; break;
            case "inactive": op.Active = false; break;
            case "schema": op.SchemaVersion = 999; break;
            case "unconfirmed": op.RemoteConfirmedAt = null; break;
            case "operation-namespace": op.RecoveryRefPrefix += "/crossed"; break;
            case "destination": op.DestinationFullRef = "refs/heads/other"; break;
            case "fingerprint": op.RemoteFingerprint = "malformed"; break;
            case "confirmation-method": op.ConfirmationMethod = "report says pushed"; break;
            case "observed-sha": op.ObservedRemoteTargetSha = "unknown"; break;
            case "lease": f.ValidLease = false; break;
        }
        var result = await f.RemoveAsync();
        if (change == "valid")
        {
            result.IsClean.ShouldBeTrue();
            f.Mutations[0].ShouldBe(["worktree", "remove", "--", f.Source]);
            f.Mutations[1].ShouldBe(["update-ref", "--no-deref", "-d", f.Request.Source.SourceFullRef, RemovalFixture.Sha]);
        }
        else
        {
            f.Mutations.ShouldBeEmpty("crossed authority must refuse before issuing any destructive request: " + change);
            result.IsClean.ShouldBeFalse();
            File.ReadAllText(Path.Combine(f.Source, "keep.txt")).ShouldBe("private work");
            f.BranchPresent.ShouldBeTrue();
        }
    }

    private sealed class RemovalFixture : ILandingGit, IRepositoryMutationLease, IWorktreeRemovalEvidence, IDisposable
    {
        public static readonly string Sha = new('a', 40);
        public static readonly string Other = new('b', 40);
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "c448-removal-policy-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "source");
        public AgentTaskLanding Operation { get; }
        public WorktreeRemovalRequest Request { get; set; }
        public string? LocalChange { get; set; }
        public List<string[]> Mutations { get; } = [];
        public bool BranchPresent { get; set; } = true;
        public bool ValidLease { get; set; } = true;
        public Action<int>? AfterInspection { get; set; }
        public int InspectionCount { get; private set; }
        public string InspectedSha { get; set; } = Sha;
        public string? IgnoredPath { get; set; }
        public RemovalFixture()
        {
            Directory.CreateDirectory(Source);
            File.WriteAllText(Path.Combine(Source, "keep.txt"), "private work");
            Operation = new() { Id = Guid.NewGuid(), TaskId = Guid.NewGuid(), Active = true,
                SourceFullRef = "refs/heads/source", TargetFullRef = "refs/heads/master", DestinationFullRef = "refs/heads/master",
                RepositoryPath = Root, CommonDirectory = Root, WorktreePath = Source, GitDirectory = Path.Combine(Root, "admin"),
                OriginalSourceSha = Sha, VerifiedSourceSha = Sha, TargetBeforeSha = Sha, ObservedRemoteTargetSha = Sha,
                ExpectedDeletionSha = Sha, SourcePinned = true, TargetPinned = true, VerificationPassed = true,
                RemoteFingerprint = new string('c', 64), RemoteConfirmedAt = DateTime.UtcNow, VerifiedAt = DateTime.UtcNow,
                CleanupStartedAt = DateTime.UtcNow, Publication = LandPublicationOutcome.Landed,
                Phase = LandPhase.CleanupStarted, ConfirmationMethod = "push-endpoint-read-fetch-ancestry" };
            Operation.RecoveryRefPrefix = $"refs/antiphon/land/{Operation.TaskId:N}/{Operation.Id:N}";
            Request = new(WorktreeRemovalPurpose.Publication,
                new(Operation.TaskId, Root, Source, Operation.SourceFullRef, Operation.TargetFullRef), Root,
                Operation.GitDirectory, Sha, Sha, Operation.Id, new Lease(Root));
        }
        public Task<WorktreeRemoval> RemoveAsync() => new GuardedWorktreeRemoval(this, this, this).RemoveAsync(Request, default);
        public Task<AgentTaskLanding?> ReadAsync(Guid id, CancellationToken ct) => Task.FromResult<AgentTaskLanding?>(Operation);
        public bool Owns(RepositoryLease lease, string commonDirectory) => ValidLease && ReferenceEquals(lease, Request.Lease) && commonDirectory == Root;
        public Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct) => Task.FromResult<RepositoryLease?>(Request.Lease);
        public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) => Task.FromResult(Root);
        public Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination, string sourceSha, string observationRef, CancellationToken ct)
            => Task.FromResult(new LandingRemoteObservation(Sha, true, null));
        public Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef, string observationPrefix, CancellationToken ct)
            => Task.FromResult(new LandingSourceObservation(Sha, observationPrefix + "/pin", new string('c', 64), null));
        public Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct)
        {
            InspectionCount++;
            AfterInspection?.Invoke(InspectionCount);
            return Task.FromResult(new LandSourceInspection(new(coordinates, Root, Source, Request.GitDirectory,
                coordinates.SourceFullRef, InspectedSha, InspectedSha, "", IgnoredPath is null ? [] : [IgnoredPath]), null));
        }
        public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> args, CancellationToken ct)
        {
            LandingGitResult result;
            if (args[0] == "worktree" && args[1] == "list")
                result = new(0, Directory.Exists(Source)
                    ? $"worktree {Source}\0HEAD {Sha}\0branch {Request.Source.SourceFullRef}\0\0"
                    : $"worktree {Root}\0HEAD {Sha}\0branch refs/heads/master\0\0", "");
            else if (args[0] == "worktree" && args[1] == "remove")
            {
                Mutations.Add(args.ToArray());
                Directory.Delete(Source, recursive: true);
                result = new(0, "", "");
            }
            else if (args[0] == "update-ref" && args.Contains("-d"))
            { Mutations.Add(args.ToArray()); BranchPresent = false; result = new(0, "", ""); }
            else if (args[0] == "show-ref" && args.Contains("--exists")) result = new(BranchPresent ? 0 : 2, "", "");
            else if (args[0] == "show-ref")
                result = new(0, args[^1].EndsWith("/target-before", StringComparison.Ordinal) ? Operation.TargetBeforeSha
                    : args[^1].EndsWith("/prepared", StringComparison.Ordinal) ? Operation.RebasedSourceSha! : Sha, "");
            else if (args[0] == "symbolic-ref") result = args[^1] == "HEAD"
                ? new(0, LocalChange == "symbolic" ? "refs/heads/other" : Request.Source.TargetFullRef, "")
                : new(LocalChange == "target-symbolic" ? 0 : 1, "", "");
            else if (args[0] == "rev-parse") result = new(0,
                LocalChange == "head" && args.Contains("HEAD^{commit}")
                    || LocalChange == "target" && args.Contains(Request.Source.TargetFullRef + "^{commit}") ? Other : Sha, "");
            else if (args[0] == "status") result = new(LocalChange == "status-error" ? 128 : 0,
                LocalChange == "dirty" ? " M keep.txt\0" : "", "");
            else if (args[0] == "merge-base") result = new(LocalChange == "ancestry" ? 1 : 0, "", "");
            else throw new InvalidOperationException("Unexpected cleanup command: " + args[0]);
            return Task.FromResult(result);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
        private sealed class Lease(string common) : RepositoryLease
        { public override string CommonDirectory => common; public override ValueTask DisposeAsync() => ValueTask.CompletedTask; }
        public Task<LandingGitResult> RunOwnedAsync(string r, IReadOnlyList<string> a, Func<int,long,CancellationToken,Task> s, CancellationToken c) => throw new NotSupportedException();
        public Task<bool?> IsProcessAliveAsync(int p, long s, CancellationToken c) => throw new NotSupportedException();
        public Task<string> CanonicalDirectoryAsync(string p, CancellationToken c) => Task.FromResult(p);
        public Task<bool> HasActiveSequencerAsync(string r, CancellationToken c) => Task.FromResult(LocalChange == "sequencer");
        public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string r, CancellationToken c)
            => Task.FromResult<IReadOnlyList<LandingRegistration>>([new(LocalChange == "checkout" ? Path.Combine(Root, "other") : Root,
                Request.Source.TargetFullRef, Sha, false, false)]);
        public Task<LandingDestination> DestinationAsync(string r, string t, CancellationToken c) => throw new NotSupportedException();
        public Task<LandingGitResult> PinAsync(string r,string p,string s,CancellationToken c) => throw new NotSupportedException();
        public Task<LandingGitResult> PushAsync(string r,LandingDestination d,string s,CancellationToken c) => throw new NotSupportedException();
        public Task<LandingGitResult> PushOwnedAsync(string r,LandingDestination d,string s,Func<int,long,CancellationToken,Task> a,CancellationToken c) => throw new NotSupportedException();
    }
}
