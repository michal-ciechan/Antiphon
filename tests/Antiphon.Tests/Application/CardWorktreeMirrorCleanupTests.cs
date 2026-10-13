using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CardWorktreeMirrorCleanupTests
{
    // case: independently bound mirror H behind published P; mutation: use P as expected mirror HEAD;
    // red: physical directory/registration removal and imported receipt's H fail.
    [Test] public async Task C1017_MirrorBehindPushedTip()
    {
        await using var f = await CompletedCardMirrorFixture.CreateAsync();
        await f.Card.Host.Fixture.PushIndependentAsync("published-ahead");
        await f.Card.Host.Fixture.RequiredAsync(f.Binding.CommonDirectory, "fetch", "origin", "master");
        await f.RemovedAsync();
        (await f.CommandAsync()).Proof.ExpectedHeadSha.ShouldBe(f.Card.Host.Fixture.SeedSha);
        (await f.CommandAsync()).Proof.ObservedTargetSha.ShouldNotBe(f.Card.Host.Fixture.SeedSha);
    }
    // case: reopen after local completion; mutation: authorize using terminal task status alone;
    // red: mirror evidence is removed or a command is journaled after reopen.
    [Test] public async Task C1017_ReopenBetweenEndpoints()
    {
        await using var f = await CompletedCardMirrorFixture.CreateAsync(); await f.DiscoverAsync();
        await using (var db = f.Card.Host.CreateContext())
            await db.CardWorktreeCleanupEndpoints.Where(e => e.Target.TaskId == f.Card.TaskId && e.EndpointIdentity == "local")
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.State, CardWorktreeCleanupEndpointState.Complete));
        await f.Card.MoveAsync(f.Card.ReviewColumnId); await f.HeldAsync("done_generation_revoked");
    }
    // case: two stores/attempts for one task; mutation: collapse endpoints to runner name/current path;
    // red: binding-specific endpoints and independent debt count fail.
    [Test] public async Task C1017_MultipleMirrorsRemainDistinct()
    {
        await using var f = await CompletedCardMirrorFixture.CreateAsync();
        await f.AddBindingAsync(f.Binding with { BindingId = Guid.NewGuid(), PreparationId = Guid.NewGuid(),
            RunnerStoreId = Guid.NewGuid().ToString("D"), WorktreePath = f.Mirror + "-old" });
        await f.DiscoverAsync(); await f.RemovedAsync();
        await using var db = f.Card.Host.CreateContext();
        var rows = await db.CardWorktreeCleanupEndpoints.Where(e => e.Target.TaskId == f.Card.TaskId && e.MirrorBindingId != null).ToListAsync();
        rows.Count.ShouldBe(2); rows.Count(e => e.State == CardWorktreeCleanupEndpointState.Complete).ShouldBe(1);
        rows.Single(e => e.WorktreePath != f.Mirror).OperationId.ShouldBeNull();
    }
    // case matrix: terminal, floor, attempt, active consumer, recovery and production gates;
    // mutation: omit each admission predicate; red: reason/no-journal/sentinel oracle fails.
    [Test] public async Task C1032_AdmissionCurrentEligibility()
    {
        foreach (var vector in new[] { "Queued", "Working", "Blocked", "settling", "attempt", "consumer", "recovery", "disabled", "cutoff", "store" })
        {
            await using var f = await CompletedCardMirrorFixture.CreateAsync(); await f.DiscoverAsync();
            var reason = vector switch { "settling" => "settling", "attempt" => "task_snapshot_changed", "consumer" => "live_owner",
                "recovery" => "recovery_debt", "disabled" => "execute_disabled", "cutoff" => "cutoff_absent", "store" => "runner_store_mismatch", _ => "terminal_required" };
            if (Enum.TryParse<AgentTaskStatus>(vector, out var status)) await f.Card.ChangeTaskAsync(t => t.Status = status);
            if (vector == "settling") await f.Card.SetCompletedAgeAsync(TimeSpan.FromMinutes(119));
            if (vector == "attempt") await f.Card.ChangeTaskAsync(t => t.Attempt++);
            if (vector == "disabled") f.Card.Residue.Execute = false;
            if (vector == "cutoff") f.Card.SetCutoff(null);
            await using (var db = f.Card.Host.CreateContext())
            {
                if (vector == "consumer") db.AgentTasks.Add(new AgentTask { Id = Guid.NewGuid(), Title = "consumer", Goal = "fixture", Status = AgentTaskStatus.Working,
                    Workspace = WorkspaceMode.Worktree, WorktreePath = f.Mirror, WorktreeBranch = f.Binding.SourceFullRef, CreatedAt = DateTime.UtcNow });
                if (vector == "recovery") db.AgentTaskLandRequests.Add(new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = f.Card.TaskId, IsPending = true, CreatedAt = DateTime.UtcNow });
                if (vector == "store") await db.CardWorktreeCleanupEndpoints.Where(e => e.Id == f.EndpointId)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.RunnerStoreId, Guid.NewGuid().ToString("D")));
                await db.SaveChangesAsync();
            }
            await f.HeldAsync(reason);
        }
        await using var positive = await CompletedCardMirrorFixture.CreateAsync();
        await positive.Card.SetCompletedAgeAsync(TimeSpan.FromMinutes(120)); await positive.RemovedAsync();
    }
    // case matrix: external canonical report and deliverables must survive all deletion roots;
    // mutation: use existence only/reuse stale pointer proof; red: no-intent and exact bytes fail.
    [Test] public async Task C1032_AdmissionArtifactSurvival()
    {
        foreach (var vector in new[] { "missing", "truncated", "inside", "deliverable", "remote-report", "changed-pointer" })
        {
            await using var f = await CompletedCardMirrorFixture.CreateAsync();
            var task = await f.TaskAsync();
            if (vector == "missing") File.Delete(task.ResultFilePath!);
            if (vector == "truncated") await File.WriteAllTextAsync(task.ResultFilePath!, "truncated");
            if (vector == "inside") { var path = Path.Combine(f.Mirror, ".antiphon", "task.md"); await File.WriteAllTextAsync(path, task.Result); await f.Card.ChangeTaskAsync(t => t.ResultFilePath = path); }
            if (vector == "deliverable") await f.Card.ChangeTaskAsync(t => t.DeliverablePath = f.Sentinel);
            if (vector == "remote-report") await File.WriteAllTextAsync(Path.Combine(f.Mirror, ".antiphon", "task-" + task.Id.ToString("N")[..8] + ".md"), "remote-only report");
            if (vector == "changed-pointer") f.BeforeIntent = async _ => await f.Card.ChangeTaskAsync(t => t.DeliverablePath = f.Sentinel);
            await f.HeldAsync(vector == "changed-pointer" ? "artifact_changed" : "artifact_unpreserved");
        }
    }
    // case: committed intent observed before real socket send; reopen wins either side;
    // mutation: send inside admission transaction or revoke issued command; red: independent writer/receipt fails.
    [Test] public async Task C1032_AdmissionIntentOrdering()
    {
        await using (var before = await CompletedCardMirrorFixture.CreateAsync())
        { before.BeforeIntent = _ => before.Card.MoveAsync(before.Card.ReviewColumnId); await before.HeldAsync("done_generation_revoked"); }
        await using var f = await CompletedCardMirrorFixture.CreateAsync();
        f.AfterIntent = async _ =>
        {
            var row = await f.EndpointAsync(); row.OperationId.ShouldNotBeNull(); row.ImmutableRequest.ShouldNotBeNull();
            await f.Card.MoveAsync(f.Card.ReviewColumnId).WaitAsync(TimeSpan.FromSeconds(5));
        };
        await f.RemovedAsync();
    }
    // case: actual server landing maps original mirror H onto published rebased P;
    // mutation: reject valid authority or accept unrelated/mutated landing; red: physical success/refusal fails.
    [Test] public async Task C1032_AdmissionBoundRebaseProof()
    {
        await using var f = await CompletedCardMirrorFixture.CreateAsync();
        var g = f.Card.Host.Fixture;
        await File.WriteAllTextAsync(Path.Combine(f.Card.Tree, "feature.txt"), "feature\n");
        await g.RequiredAsync(f.Card.Tree, "add", "feature.txt"); await g.RequiredAsync(f.Card.Tree, "commit", "-m", "feature");
        var head = (await g.RequiredAsync(f.Card.Tree, "rev-parse", "HEAD")).Trim();
        await g.RequiredAsync(f.Card.Tree, "push", "origin", "HEAD:" + f.Binding.SourceFullRef);
        await g.RequiredAsync(f.Binding.CommonDirectory, "fetch", "origin", f.Binding.SourceFullRef);
        await g.RequiredAsync(f.Mirror, "reset", "--hard", head);
        await g.PushIndependentAsync("target-moved");
        await f.Card.Host.RequestAsync(expectedSourceSha: head); await f.Card.Host.RunAsync();
        var op = (await f.Card.Host.OperationAsync()).ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue(); op.OriginalSourceSha.ShouldBe(head); op.VerifiedSourceSha.ShouldNotBe(head);
        await g.RequiredAsync(f.Binding.CommonDirectory, "fetch", "origin", "master");
        await f.RemovedAsync(); (await f.CommandAsync()).Proof.ProofKind.ShouldBe("Rebased");
    }

    [Test] public Task C1017_ReceiptMustMatchOperation() => ReceiptVectorsAsync("operationId");
    [Test] public Task C1017_ReceiptMustMatchEndpoint() => ReceiptVectorsAsync("endpointId", "targetId", "cardId", "doneRevisionId");
    [Test] public Task C1017_ReceiptMustMatchStore() => ReceiptVectorsAsync("runnerId", "runnerStoreId", "repositoryIdentity", "commonDirectory", "gitDirectory", "worktreePath");
    [Test] public Task C1017_ReceiptMustMatchTaskAttempt() => ReceiptVectorsAsync("taskId", "attempt", "bindingId", "preparationId", "sourceFullRef", "creationBaselineSha");
    // case: ACK without receipt; mutation: trust Complete flag; red: fresh observer sees false Complete.
    [Test] public async Task C1017_AckIsNotRemoval()
    {
        await using var f = await CompletedCardMirrorFixture.CreateAsync(); f.AlterReceipt = r => r with { Receipt = null };
        (await f.TryAsync()).Reason.ShouldBe("receipt_mismatch"); (await f.EndpointAsync()).State.ShouldNotBe(CardWorktreeCleanupEndpointState.Complete);
    }
    // case: unconfigured peer with real transport; mutation: fall back to legacy remove; red: physical evidence gone.
    [Test] public async Task C1017_UnsupportedNeverFallsBack()
    {
        await using var f = await CompletedCardMirrorFixture.CreateAsync();
        f.Runtime.CompletedCardWorkspace = null;
        await f.HeldAsync("unsupported_operation");
    }
    // case: genuine receipt positive and digest/component/missing identity vectors;
    // mutation: skip one receipt comparison; red: fresh DB observer false completion/import.
    [Test] public async Task C1032_ReceiptCompleteBoundReceipt()
    {
        await ReceiptVectorsAsync("schemaVersion", "expectedHeadSha", "preservationProofDigest", "artifactProofDigest", "disposalPolicyDigest", "disposition", "setAsideIdentity", "journalIdentity", "directoryRemoved", "registrationRemoved");
        await using var positive = await CompletedCardMirrorFixture.CreateAsync(); await positive.RemovedAsync();
    }
    private static async Task ReceiptVectorsAsync(params string[] fields)
    {
        foreach (var field in fields)
        {
            await using var f = await CompletedCardMirrorFixture.CreateAsync();
            f.AlterReceipt = r =>
            {
                var json = System.Text.Json.JsonSerializer.SerializeToNode(r.Receipt, PhoneHomeFraming.Json)!.AsObject();
                var old = json[field]!.ToJsonString();
                json[field] = old == "true" ? System.Text.Json.Nodes.JsonValue.Create(false)
                    : old.StartsWith('"') ? System.Text.Json.Nodes.JsonValue.Create(field.EndsWith("Id") && Guid.TryParse(json[field]!.GetValue<string>(), out _) ? Guid.NewGuid().ToString("D") : "mismatch")
                    : System.Text.Json.Nodes.JsonValue.Create(999);
                return r with { Receipt = json.Deserialize<CompletedCardWorkspaceReceipt>(PhoneHomeFraming.Json) };
            };
            var reply = await f.TryAsync(); reply.Complete.ShouldBeFalse(field); reply.Reason.ShouldBe("receipt_mismatch", field);
            var row = await f.EndpointAsync(); row.State.ShouldNotBe(CardWorktreeCleanupEndpointState.Complete, field); row.ImportedReceipt.ShouldBeNull(field);
        }
    }
}
