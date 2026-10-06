using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Application.TaskParkPublicationTests;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TaskParkRunnerIdentityTests
{
    [Test]
    public async Task C1065_RemoteIdentityCaptureBindsEpisodeBeforePublication()
    {
        await using (var w = await PublicationWorld.CreateAsync(remote: true))
        {
            var endpoint = new Uri(w.Origin).AbsoluteUri;
            await w.GitTextAsync(w.Path, "remote", "set-url", "--push", "origin", endpoint);
            var tip = await w.CommitAsync("runner-only.txt");
            var runnerIdentity = RunnerWorkspaceParkService.RepositoryIdentity(await w.Git.CommonDirectoryAsync(w.Path, default));
            var owner = w.Directory.Owner!;
            foreach (var changed in new[] { owner with { RunnerId = "other" }, owner with { RunnerStoreId = Guid.NewGuid() }, owner with { RunnerCwd = w.Repository } })
            {
                w.Directory.Owner = changed;
                (await CaptureAsync(w)).Reason.ShouldBe("park_runner_changed", "G-218 owner/store/cwd");
                w.Directory.Client.IdentityCalls.Count.ShouldBe(0, "G-218 no wrong-owner wire");
                (await w.RowAsync()).RepositoryIdentity.ShouldBeNull();
            }
            w.Directory.Owner = owner;
            w.Directory.IdentityCapability = false;
            (await CaptureAsync(w)).Reason.ShouldBe("park_identity_unsupported");
            w.Directory.Client.IdentityCalls.Count.ShouldBe(0);
            w.Directory.IdentityCapability = true;
            w.Fixture.Options.Enabled = false;
            (await CaptureAsync(w)).Captured.ShouldBeFalse("default-off gate");
            w.Directory.Client.IdentityCalls.Count.ShouldBe(0);
            w.Fixture.Options.Enabled = true;

            (await CaptureAsync(w)).Captured.ShouldBeTrue("G-219 capture accepts alternate URL for admitted repository");
            var captured = await w.RowAsync();
            captured.State.ShouldBe(AgentTaskParkState.Requested);
            captured.RepositoryIdentity.ShouldBe(runnerIdentity);
            captured.EndpointFingerprint.ShouldBe(BlockedTaskParkingService.Digest(endpoint));
            captured.EndpointFingerprint.ShouldNotBe(w.Source.Remote.EndpointFingerprint, "G-219 different actual push endpoint");
            captured.PublicationReceiptId.ShouldBeNull();
            w.Directory.Client.Calls.ShouldBe(0, "capture is read-only");
            w.Directory.Resolved.ShouldAllBe(r => r == owner.RunnerId, "G-218 only bound runner");
            var read = w.Directory.Client.IdentityCalls.ShouldHaveSingleItem();
            read.SessionId.ShouldBe(w.Fixture.SessionId);
            read.Path.ShouldBe(w.Path);
            read.ExpectedRunnerStoreId.ShouldBe(captured.RunnerStoreId!.Value);
            read.ExpectedAcceptedStartedAt.ShouldBe(captured.AcceptedStartedAt!.Value);
            w.Directory.Client.BeforePublication = async command =>
            {
                await using var db = w.Fixture.Db();
                await using var tx = await db.Database.BeginTransactionAsync();
                await db.Database.ExecuteSqlInterpolatedAsync($"""SELECT "Id" FROM "AgentTasks" WHERE "Id" = {w.Fixture.TaskId} FOR UPDATE NOWAIT""");
                var intent = await db.AgentTaskParks.SingleAsync(p => p.Id == w.ParkId);
                intent.RepositoryIdentity.ShouldBe(runnerIdentity, "G-219 persisted before wire");
                intent.EndpointFingerprint.ShouldBe(BlockedTaskParkingService.Digest(endpoint));
                (command.Prepare ?? command.Verify!.Request).Binding.EndpointFingerprint.ShouldBe(intent.EndpointFingerprint);
                await tx.CommitAsync();
            };
            var published = await w.PrepareAsync();
            published.Outcome.ShouldBe(TaskParkPublicationOutcome.Published, "G-219: " + published.Reason);
            published.Evidence!.SourceSha.ShouldBe(tip);
            (await w.RemoteShaAsync()).ShouldBe(tip);
            (await w.VerifyAsync()).Evidence.ShouldBe(published.Evidence);
            (await w.RowAsync()).RunnerSeatReleaseId.ShouldBeNull();
        }

        foreach (var variant in new[] { "endpoint", "unknown", "store", "generation", "owner-after-read", "cut", "working" })
        {
            await using var w = await PublicationWorld.CreateAsync(remote: true);
            using var cut = new CancellationTokenSource();
            w.Directory.Client.AfterIdentity = result =>
            {
                if (variant == "cut") cut.Cancel();
                if (variant == "owner-after-read") w.Directory.Owner = w.Directory.Owner! with { RunnerId = "replacement" };
                return Task.FromResult(variant switch
                {
                    "endpoint" => result with { Identity = result.Identity! with { EndpointRepository = "https://example.invalid/foreign/repo.git" } },
                    "unknown" => result with { Outcome = WorkspaceRepositoryIdentityOutcome.Unknown },
                    "store" => result with { Identity = result.Identity! with { RunnerStoreId = Guid.NewGuid() } },
                    "generation" => result with { Identity = result.Identity! with { AcceptedStartedAt = result.Identity.AcceptedStartedAt.AddSeconds(1) } },
                    _ => result
                });
            };
            if (variant == "working") await w.ChangeTaskAsync(t => t.Status = AgentTaskStatus.Working);
            if (variant == "cut")
                await Should.ThrowAsync<OperationCanceledException>(async () => await CaptureAsync(w, cut.Token));
            else
            {
                var result = await CaptureAsync(w);
                result.Captured.ShouldBeFalse("G-220/G-221/G-222 " + variant);
                result.Reason.ShouldBe(variant switch
                {
                    "endpoint" => "park_endpoint_unadmitted", "unknown" => "park_identity_unavailable",
                    "working" => "park_episode_changed", _ => "park_runner_changed"
                });
            }
            var row = await w.RowAsync();
            row.RepositoryIdentity.ShouldBeNull("G-221/G-222 no partial intent: " + variant);
            row.EndpointFingerprint.ShouldBeNull();
            row.PublicationReceiptId.ShouldBeNull();
            w.Directory.Client.Calls.ShouldBe(0, "no publication: " + variant);
            if (variant == "working") w.Directory.Client.IdentityCalls.Count.ShouldBe(0);
        }
        await using (var w = await PublicationWorld.CreateAsync())
        {
            (await CaptureAsync(w)).Captured.ShouldBeTrue();
            (await w.RowAsync()).RepositoryIdentity.ShouldBe(LocalTaskParkPublisher.Identity(w.Source.CanonicalCommonDirectory));
            w.Directory.Client.IdentityCalls.Count.ShouldBe(0, "G-223 local capture");
            w.Directory.Client.Calls.ShouldBe(0, "G-223 local publication client");
            w.Directory.Resolved.ShouldBeEmpty("G-223 local never resolves a remote client");
        }
    }

    [Test]
    public async Task C1065_LocalEvidenceMapsToTypedRunnerProofWithoutFiction()
    {
        foreach (var mode in new[] { WorkspaceMode.ReadOnly, WorkspaceMode.Worktree })
        {
            await using var w = await PublicationWorld.CreateAsync(mode);
            var evidence = (await w.PrepareAsync()).Evidence.ShouldNotBeNull();
            var proof = evidence.ToRunnerReceipt();
            proof.SourceMode.ShouldBe(mode == WorkspaceMode.ReadOnly ? WorkspaceParkSourceMode.NoSourceChanges : WorkspaceParkSourceMode.Published, "G-224 exact typed mode");
            if (mode == WorkspaceMode.ReadOnly) proof.RemoteSha.ShouldBeNull("G-224 no fabricated publication");
            else proof.RemoteSha.ShouldBe(await w.RemoteShaAsync());
            TaskParkPublicationEvidence.From(proof).ShouldBe(evidence, "G-224 round-trip source evidence");

            var request = new TerminalSeatReleaseRequest(proof.Request.Binding.ActionId,
                new(proof.Request.Binding.RunnerStoreId, proof.Request.Binding.AcceptedStartedAt, "binding", 1),
                "qualification", proof, ParkVersion: 2);
            ISessionRunnerClient defaultClient = new FakeSessionRunnerClient();
            (await defaultClient.ReleaseTerminalSeatAsync(w.Fixture.SessionId, request, default)).Outcome
                .ShouldBe(TerminalSeatReleaseOutcome.Unsupported, "G-225 default client");
            foreach (var phoneHome in new[] { false, true })
            {
                await using var wire = await TaskParkProofWire.CreateAsync(phoneHome);
                foreach (var client in new ISessionRunnerClient[] { wire.Client,
                    new RoutingSessionRunnerClient(wire.Directory), new RunnerScopedSessionRunnerClient(wire.Directory, "fixture") })
                {
                    wire.OmitSourceModes = true;
                    var before = wire.ReleaseCalls;
                    (await client.ReleaseTerminalSeatAsync(w.Fixture.SessionId, request, default)).Outcome
                        .ShouldBe(TerminalSeatReleaseOutcome.Unsupported, "G-225 missing feature");
                    wire.Received.ShouldBeEmpty("G-225 zero release wire calls, no downgrade");
                    wire.ReleaseCalls.ShouldBe(before, "G-225 capability refusal precedes the wire");
                    wire.OmitSourceModes = false;
                    await client.ReleaseTerminalSeatAsync(w.Fixture.SessionId, request, default);
                    wire.Received.ShouldHaveSingleItem().ShouldBe(request, "G-226 exact runtime-received version/mode/null SHA");
                    wire.Received.Clear();
                }
                wire.ForceCalls.ShouldBe(0);
            }
        }
    }

    // Reflection keeps the red-first test compilable against S4, which has no capture entry point.
    // Replaced with a typed call when S4c implements it.
    private static async Task<(bool Captured, string Reason)> CaptureAsync(PublicationWorld w, CancellationToken ct = default)
    {
        await using var db = w.Fixture.Db();
        var method = typeof(TaskParkPublicationService).GetMethod("CaptureSourceIdentityAsync");
        method.ShouldNotBeNull("G-219 authoritative capture must exist before publication");
        var pending = (Task)method.Invoke(w.Service(db), [w.ParkId, ct])!;
        await pending;
        var value = pending.GetType().GetProperty("Result")!.GetValue(pending)!;
        return ((bool)value.GetType().GetProperty("Captured")!.GetValue(value)!, (string)value.GetType().GetProperty("Reason")!.GetValue(value)!);
    }
}
