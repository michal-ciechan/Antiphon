using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.SessionRunner;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed partial class CardWorktreeMirrorCleanupTests
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
        foreach (var vector in new[] { "Queued", "Working", "Blocked", "settling", "attempt", "late-attempt", "consumer", "recovery", "disabled", "cutoff", "store" })
        {
            await using var f = await CompletedCardMirrorFixture.CreateAsync(); await f.DiscoverAsync();
            var reason = vector switch { "settling" => "settling", "attempt" => "task_snapshot_changed", "consumer" => "live_owner",
                "recovery" => "recovery_debt", "disabled" => "execute_disabled", "cutoff" => "cutoff_absent", "store" => "runner_store_mismatch", "late-attempt" => "task_snapshot_changed", _ => "terminal_required" };
            if (Enum.TryParse<AgentTaskStatus>(vector, out var status)) await f.Card.ChangeTaskAsync(t => t.Status = status);
            if (vector == "settling") await f.Card.SetCompletedAgeAsync(TimeSpan.FromMinutes(119));
            if (vector == "attempt") await f.Card.ChangeTaskAsync(t => t.Attempt++);
            if (vector == "late-attempt") f.BeforeIntent = _ => f.Card.ChangeTaskAsync(t => t.Attempt++);
            if (vector == "disabled") f.Card.Residue.Execute = false;
            if (vector == "cutoff") f.Card.SetCutoff(null);
            await using (var db = f.Card.Host.CreateContext())
            {
                if (vector == "consumer") db.AgentTasks.Add(new AgentTask { Id = Guid.NewGuid(), Title = "consumer", Goal = "fixture", Status = AgentTaskStatus.Working,
                    Workspace = WorkspaceMode.Worktree, WorktreePath = f.Mirror, WorktreeBranch = f.Binding.SourceFullRef, CreatedAt = DateTime.UtcNow });
                if (vector == "recovery") db.AgentTaskLandRequests.Add(new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = f.Card.TaskId, IsPending = true, RequestedAt = DateTime.UtcNow });
                if (vector == "store")
                {
                    var replacementStore = Guid.NewGuid().ToString("D");
                    await db.CardWorktreeCleanupEndpoints.Where(e => e.Id == f.EndpointId)
                        .ExecuteUpdateAsync(s => s.SetProperty(e => e.RunnerStoreId, replacementStore));
                }
                await db.SaveChangesAsync();
            }
            await f.HeldAsync(reason);
        }
        await using var positive = await CompletedCardMirrorFixture.CreateAsync();
        await positive.Card.SetCompletedAgeAsync(TimeSpan.FromMinutes(120)); await positive.RemovedAsync();
    }
    // case matrix: missing, changed and internal artifact pointers refuse admission;
    // mutation: use existence only/reuse stale pointer proof; red: no-intent and exact bytes fail.
    [Test] public async Task C1032_AdmissionArtifactSurvival()
    {
        foreach (var vector in new[] { "missing", "truncated", "inside", "deliverable", "remote-report", "changed-pointer" })
        {
            await using var f = await CompletedCardMirrorFixture.CreateAsync();
            var task = await f.TaskAsync();
            if (vector == "missing") File.Delete(task.ResultFilePath!);
            if (vector == "truncated") await File.WriteAllTextAsync(task.ResultFilePath!, "truncated");
            if (vector == "inside") { var path = Path.Combine(f.Mirror, ".antiphon", "checkpoints", "canonical.trx"); await File.WriteAllTextAsync(path, task.Result); await f.Card.ChangeTaskAsync(t => t.ResultFilePath = path); }
            if (vector == "deliverable") await f.Card.ChangeTaskAsync(t => t.DeliverablePath = f.Sentinel);
            if (vector == "remote-report") await File.WriteAllTextAsync(Path.Combine(f.Mirror, ".antiphon", "task-" + task.Id.ToString("N")[..8] + ".md"), "remote-only report");
            if (vector == "changed-pointer") f.BeforeIntent = async _ => await f.Card.ChangeTaskAsync(t => t.DeliverablePath = f.Sentinel);
            await f.HeldAsync(vector == "changed-pointer" ? "artifact_changed" : "artifact_unpreserved");
        }
    }
    // Literal shape table pins runner evidence recognition; real producer/recipient
    // cases must retain unique report bytes before issuing a removal command.
    [Test] public async Task C1032_ReportRecognitionCaseTable()
    {
        var classifier = new RunnerWorkspaceIgnoredContent();
        foreach (var (path, evidence) in new (string, bool)[]
        {
            (".antiphon/task-deadbeef.md", true), (".antiphon/TASK-DEADBEEF.MD", true),
            (".antiphon/TaSk-dEaDbEeF.mD", true), (".ANTIPHON/task-deadbeef.md", true),
            ("./.antiphon/task-deadbeef.md", true), (".antiphon\\task-deadbeef.md", true),
            (".antiphon/task-zzzzzzzz.md", true), (".antiphon/task-ｄeadbeef.md", true),
            (".antiphon/task-deadbeef.md\n", true), (".antiphon/task-deadbeef.md\r\n", false),
            (".antiphon/task-deadbeef.txt", false), (".antiphon/task-deadbeef.md/", false),
            (".antiphon/task-deadbeef.md/child", false), (".antiphon/task-deadbeef.md:stream", false),
            (".antiphon/task-deadbeef.md.", false), (".antiphon/task-deadbeef.md ", false),
            (".antiphon/task-deadbee.md", false), (".antiphon/task-deadbeeff.md", false),
            (".antiphon/task-deadbeef-brief.md", false), (".antiphon/task-deadbeef.mｄ", false),
            (".antiphon/ｔask-deadbeef.md", false), (".antiphοn/task-deadbeef.md", false),
            ("nested/.antiphon/task-deadbeef.md", false), ("../.antiphon/task-deadbeef.md", false),
        })
        {
            (classifier.Classify(path) == RunnerIgnoredContentKind.Evidence).ShouldBe(evidence, path);
            CompletedCardWorkspaceReport.IsTaskReportPath(path).ShouldBe(evidence, path);
            CompletedCardWorkspaceReport.IsTaskReportPath(path, isDirectory: true).ShouldBeFalse(path);
        }

        foreach (var name in new[] { "task-deadbeef.md", "TASK-DEADBEEF.MD", "TaSk-dEaDbEeF.mD", "task-zzzzzzzz.md", "task-ｄeadbeef.md", "task-deadbeef.md\n" })
        {
            await using var f = await CompletedCardMirrorFixture.CreateAsync();
            var path = Path.Combine(f.Mirror, ".antiphon", name);
            var bytes = "unique report " + Guid.NewGuid().ToString("D") + "\n";
            await File.WriteAllTextAsync(path, bytes);
            var result = await f.TryAsync();
            // Assert survival first: historical red describes physical report loss.
            File.Exists(path).ShouldBeTrue("report bytes must survive: " + name);
            (await File.ReadAllTextAsync(path)).ShouldBe(bytes);
            result.Complete.ShouldBeFalse(name); result.Reason.ShouldBe("artifact_unpreserved", name);
            Directory.Exists(f.Binding.GitDirectory).ShouldBeTrue();
            (await File.ReadAllTextAsync(f.Sentinel)).ShouldBe("mirror evidence\n");
            f.Journal.Read()!.Count.ShouldBe(0);
            var endpoint = await f.EndpointAsync();
            endpoint.OperationId.ShouldBeNull(); endpoint.ImportedReceipt.ShouldBeNull();
        }
    }

    // Gate after committed producer intent, before real phone-home removal: the
    // recipient must refuse a report absent from the producer's earlier inventory.
    [Test] public async Task C1032_ReportAddedAfterProducerInspection()
    {
        foreach (var name in new[] { "task-deadbeef.md", "TASK-DEADBEEF.MD", "TaSk-dEaDbEeF.mD" })
        {
            await using var f = await CompletedCardMirrorFixture.CreateAsync();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.AfterIntent = async ct => { entered.SetResult(); await release.Task.WaitAsync(ct); };
            var path = Path.Combine(f.Mirror, ".antiphon", name);
            var bytes = "late unique report " + Guid.NewGuid().ToString("D") + "\n";
            var pending = f.TryAsync();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                (await f.EndpointAsync()).ImmutableRequest.ShouldNotBeNull();
                File.Exists(path).ShouldBeFalse();
                await File.WriteAllTextAsync(path, bytes);
            }
            finally { release.TrySetResult(); }
            var result = await pending;
            File.Exists(path).ShouldBeTrue("late report bytes must survive: " + name);
            (await File.ReadAllTextAsync(path)).ShouldBe(bytes);
            result.Complete.ShouldBeFalse(name); result.Reason.ShouldBe("artifact_unpreserved", name);
            Directory.Exists(f.Binding.GitDirectory).ShouldBeTrue();
            (await File.ReadAllTextAsync(f.Sentinel)).ShouldBe("mirror evidence\n");
            var endpoint = await f.EndpointAsync();
            endpoint.OperationId.ShouldNotBeNull(); endpoint.ImportedReceipt.ShouldBeNull();
            endpoint.State.ShouldNotBe(CardWorktreeCleanupEndpointState.Complete);
            f.Journal.Read()!.Any(i => i.Receipt is not null || i.SetAsidePath is not null).ShouldBeFalse();
        }
    }

    // Positive control: generic admitted TRX evidence remains disposable when no
    // remote task report exists; assert actual removal and independently read receipt.
    [Test] public async Task C1032_ReportAbsentAllowsCleanup()
    {
        await using var f = await CompletedCardMirrorFixture.CreateAsync();
        Directory.GetFiles(Path.Combine(f.Mirror, ".antiphon"), "*", SearchOption.TopDirectoryOnly).ShouldBeEmpty();
        await f.RemovedAsync();
    }
    // case: reopen before intent blocks issuance; reopen after intent permits the issued command;
    // mutation: send inside admission transaction or revoke issued command; red: independent writer/receipt fails.
    [Test] public async Task C1032_AdmissionIntentOrdering()
    {
        await using (var before = await CompletedCardMirrorFixture.CreateAsync())
        { before.BeforeIntent = _ => before.Card.MoveAsync(before.Card.ReviewColumnId); await before.HeldAsync("done_generation_revoked"); }
        await using (var io = await CompletedCardMirrorFixture.CreateAsync())
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            io.BeforeInspection = async ct => { entered.SetResult(); await release.Task.WaitAsync(ct); };
            var pending = io.TryAsync();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await io.Card.MoveAsync(io.Card.ReviewColumnId).WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally { release.TrySetResult(); }
            (await pending).Reason.ShouldBe("done_generation_revoked");
            (await File.ReadAllTextAsync(io.Sentinel)).ShouldBe("mirror evidence\n");
        }
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
        f.AfterIntent = async ct =>
        {
            var command = await f.CommandAsync();
            var client = (ICompletedCardWorkspaceClient)f.Wire.Directory.Resolve(f.Binding.RunnerId);
            foreach (var changed in new[]
            {
                command with { CardId = Guid.NewGuid() }, command with { DoneRevisionId = Guid.NewGuid() },
                command with { TargetId = Guid.NewGuid() }, command with { EndpointId = Guid.NewGuid() },
                command with { OperationId = Guid.NewGuid() },
                command with { ReportDigest = new string('a', 64) }, command with { ArtifactProofDigest = new string('b', 64) },
                command with { DisposalPolicyDigest = new string('c', 64) },
                command with { Proof = command.Proof with { ProofIdentity = "caller-supplied-map" } },
            })
            {
                var refusal = await client.RemoveCompletedCardWorkspaceAsync(changed, ct);
                refusal.Complete.ShouldBeFalse(); refusal.Reason.ShouldBe("preservation_proof_unsupported");
                (await File.ReadAllTextAsync(f.Sentinel, ct)).ShouldBe("mirror evidence\n"); f.Journal.Read()!.Count.ShouldBe(0);
            }
        };
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
        await f.HeldAsync(PhoneHomeProblemTypes.UnsupportedOperation);
    }
    // case: genuine receipt positive and digest/component/missing identity vectors;
    // mutation: skip one receipt comparison; red: fresh DB observer false completion/import.
    [Test] public async Task C1032_ReceiptCompleteBoundReceipt()
    {
        await ReceiptVectorsAsync("schemaVersion", "expectedHeadSha", "preservationProofDigest", "artifactProofDigest", "disposalPolicyDigest", "disposition", "setAsideIdentity", "journalIdentity", "directoryRemoved", "registrationRemoved");
        await using (var fault = await CompletedCardMirrorFixture.CreateAsync())
        {
            fault.FailReceiptImport = true;
            await Should.ThrowAsync<IOException>(() => fault.TryAsync());
            var row = await fault.EndpointAsync(); row.State.ShouldBe(CardWorktreeCleanupEndpointState.IntentRecorded);
            row.ImportedReceipt.ShouldBeNull(); row.ImmutableRequest.ShouldNotBeNull();
        }
        await using var positive = await CompletedCardMirrorFixture.CreateAsync(); await positive.RemovedAsync();
    }
    // F4/VP-09: the active landing changed to a foreign task (with its recovery prefix), store path,
    // repository or earlier generation must not map H onto P: after the real inspection the unmapped
    // H observation refuses unlanded_work with no intent, no removal request, an empty journal and
    // intact evidence; mutation: select active landings without the task predicate; red: rebased
    // removal completes. The restored row is the positive (real removal).
    [Test] public async Task C1032_WitnessLandingOwnership()
    {
        await using var f = await CompletedCardMirrorFixture.CreateAsync();
        var id = await RebasedLandingAsync(f); var original = await LandingAsync(f, id);
        var foreignTask = Guid.NewGuid();
        await using (var db = f.Card.Host.CreateContext())
        {
            db.AgentTasks.Add(new AgentTask { Id = foreignTask, Title = "foreign", Goal = "fixture", Status = AgentTaskStatus.Succeeded,
                Workspace = WorkspaceMode.Worktree, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var bindingCreated = f.Card.Host.Clock.GetUtcNow().UtcDateTime.AddHours(-4);
        foreach (var (vector, change) in new (string, Action<AgentTaskLanding>)[]
        {
            ("foreign-task", o => { o.TaskId = foreignTask; o.RecoveryRefPrefix = $"refs/antiphon/land/{foreignTask:N}/{o.Id:N}"; }),
            ("foreign-store-path", o => o.WorktreePath = f.Card.Tree + "-foreign"),
            ("foreign-repository", o => o.RepositoryPath = f.Card.Tree + "-foreign-repository"),
            ("earlier-generation", o => o.CreatedAt = bindingCreated.AddMinutes(-1)),
        })
        {
            await ChangeLandingAsync(f, original, change);
            await WithheldAsync(f, vector);
            await ChangeLandingAsync(f, original, null);
        }
        await f.RemovedAsync();
        (await f.CommandAsync()).Proof.ProofIdentity.ShouldStartWith("landed-v1:" + id.ToString("D") + ":");
    }
    // F4/VP-10: 13 null/empty/false vectors across 11 landing-evidence fields outside the narrower
    // eligibility checks (unvaried fields: F4 selection disclosure) refuse after real inspection with
    // no removal request; mutation: drop HasPublication; red: rebased removal completes.
    [Test] public async Task C1032_WitnessLandingCompleteness()
    {
        await using var f = await CompletedCardMirrorFixture.CreateAsync();
        var id = await RebasedLandingAsync(f); var original = await LandingAsync(f, id);
        foreach (var (field, change) in new (string, Action<AgentTaskLanding>)[]
        {
            ("observedRemoteTargetSha:null", o => o.ObservedRemoteTargetSha = null),
            ("observedRemoteTargetSha:empty", o => o.ObservedRemoteTargetSha = ""),
            ("verifiedAt:null", o => o.VerifiedAt = null),
            ("confirmationMethod:null", o => o.ConfirmationMethod = null),
            ("confirmationMethod:empty", o => o.ConfirmationMethod = ""),
            ("recoveryRefPrefix:empty", o => o.RecoveryRefPrefix = ""),
            ("commonDirectory:empty", o => o.CommonDirectory = ""),
            ("gitDirectory:empty", o => o.GitDirectory = ""),
            ("targetBeforeSha:empty", o => o.TargetBeforeSha = ""),
            ("destinationFullRef:empty", o => o.DestinationFullRef = ""),
            ("sourcePinned:false", o => o.SourcePinned = false),
            ("targetPinned:false", o => o.TargetPinned = false),
            ("preparedPinned:false", o => o.PreparedPinned = false),
        })
        {
            await ChangeLandingAsync(f, original, change);
            await WithheldAsync(f, field);
            await ChangeLandingAsync(f, original, null);
        }
        await f.RemovedAsync(); (await f.CommandAsync()).Proof.ProofKind.ShouldBe("Rebased");
    }
    // F4/VP-13: a durable intent whose persisted request bytes change after writing is held
    // with no removal request; mutation: replay on schema validity alone; red: the hold is passed
    // (scratch red: F4 Code evidence). Restored original bytes replay to real removal.
    [Test] public async Task C1032_WitnessPersistedReplayTamper()
    {
        await using var f = await CompletedCardMirrorFixture.CreateAsync();
        f.AfterIntent = _ => throw new IOException("fixture stop after durable intent");
        await Should.ThrowAsync<IOException>(() => f.TryAsync());
        f.AfterIntent = null; f.RemovalRequests.ShouldBe(0);
        var written = await f.EndpointAsync();
        written.State.ShouldBe(CardWorktreeCleanupEndpointState.IntentRecorded);
        var original = written.ImmutableRequest.ShouldNotBeNull();
        foreach (var (field, tamper) in new (string, Action<System.Text.Json.Nodes.JsonObject>)[]
        {
            ("reportDigest", n => n["reportDigest"] = new string('a', 64)),
            ("disposalPolicyDigest", n => n["disposalPolicyDigest"] = new string('c', 64)),
            ("binding.worktreePath", n => n["binding"]!["worktreePath"] = f.Mirror + "-other"),
            ("proof.expectedHeadSha", n => n["proof"]!["expectedHeadSha"] = new string('d', 40)),
        })
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject(); tamper(node);
            var json = node.ToJsonString(); var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            CompletedCardWorkspaceContract.Validate(json).IsValid.ShouldBeTrue(field);
            CompletedCardWorkspaceContract.CanonicalDigest(json).ShouldNotBe(written.RequestDigest, field);
            await SetRequestAsync(f, bytes);
            var result = await f.TryAsync();
            result.Complete.ShouldBeFalse(field); result.Reason.ShouldBe("immutable_request_unreadable", field);
            f.RemovalRequests.ShouldBe(0, field); f.Journal.Read()!.Count.ShouldBe(0, field);
            Directory.Exists(f.Binding.GitDirectory).ShouldBeTrue(field);
            (await File.ReadAllTextAsync(f.Sentinel)).ShouldBe("mirror evidence\n", field);
            var row = await f.EndpointAsync();
            row.ImmutableRequest.ShouldBe(bytes, field); row.RequestDigest.ShouldBe(written.RequestDigest, field);
            row.OperationId.ShouldBe(written.OperationId, field); row.ImportedReceipt.ShouldBeNull(field);
            row.State.ShouldBe(CardWorktreeCleanupEndpointState.IntentRecorded, field);
        }
        await SetRequestAsync(f, original);
        await f.RemovedAsync(); f.RemovalRequests.ShouldBe(1);
        (await f.CommandAsync()).OperationId.ShouldBe(written.OperationId!.Value);
    }
    // F4/VP-15: after a real successful inspection and durable intent, capability withdrawal makes
    // the client preflight return unsupported before any removal frame is sent; then injected
    // unsupported, unknown and no-reason refusals. Each is refused with mirror, admin and sentinel kept,
    // an empty journal and no receipt; mutation: treat unsupported removal as success; red:
    // Complete=true. The restored runtime then completes real removal.
    [Test] public async Task C1032_WitnessUnsupportedRemovalAfterInspection()
    {
        await using var f = await CompletedCardMirrorFixture.CreateAsync();
        var service = f.Runtime.CompletedCardWorkspace.ShouldNotBeNull();
        f.AfterIntent = _ => { f.Runtime.CompletedCardWorkspace = null; return Task.CompletedTask; };
        await UnsupportedHeldAsync(f, "runtime-unsupported", PhoneHomeProblemTypes.UnsupportedOperation, 1);
        f.AfterIntent = null; f.Runtime.CompletedCardWorkspace = service;
        var issued = (await f.EndpointAsync()).OperationId.ShouldNotBeNull();
        var requests = 1;
        foreach (var (vector, reply, reason) in new (string, CompletedCardWorkspaceRemovalResponse, string)[]
        {
            ("reply-unsupported", new(false, PhoneHomeProblemTypes.UnsupportedOperation, null), PhoneHomeProblemTypes.UnsupportedOperation),
            ("reply-unknown", new(false, "unknown_removal_outcome", null), "unknown_removal_outcome"),
            ("reply-absent", new(false, null, null), "receipt_mismatch"),
        })
        {
            f.InterceptRemoval = _ => reply;
            await UnsupportedHeldAsync(f, vector, reason, ++requests);
            (await f.EndpointAsync()).OperationId.ShouldBe(issued, vector);
        }
        f.InterceptRemoval = null;
        await f.RemovedAsync(); f.RemovalRequests.ShouldBe(requests + 1);
    }
    private static async Task UnsupportedHeldAsync(CompletedCardMirrorFixture f, string vector, string reason, int requests)
    {
        var result = await f.TryAsync();
        result.Complete.ShouldBeFalse(vector); result.Reason.ShouldBe(reason, vector); result.Receipt.ShouldBeNull(vector);
        f.RemovalRequests.ShouldBe(requests, vector);
        Directory.Exists(f.Mirror).ShouldBeTrue(vector); Directory.Exists(f.Binding.GitDirectory).ShouldBeTrue(vector);
        (await File.ReadAllTextAsync(f.Sentinel)).ShouldBe("mirror evidence\n", vector); f.Journal.Read()!.Count.ShouldBe(0, vector);
        var row = await f.EndpointAsync();
        row.OperationId.ShouldNotBeNull(vector); row.ImportedReceipt.ShouldBeNull(vector);
        row.State.ShouldBe(CardWorktreeCleanupEndpointState.Refused, vector); row.Reason.ShouldBe(reason, vector);
    }
    // Actual server landing maps mirror H onto rebased P after an independent target move.
    private static async Task<Guid> RebasedLandingAsync(CompletedCardMirrorFixture f)
    {
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
        (await f.TaskAsync()).ActiveLandingId.ShouldBe(op.Id);
        await g.RequiredAsync(f.Binding.CommonDirectory, "fetch", "origin", "master");
        return op.Id;
    }
    // Applies one vector; a null change restores the captured original row.
    private static async Task ChangeLandingAsync(CompletedCardMirrorFixture f, AgentTaskLanding original, Action<AgentTaskLanding>? change)
    {
        await using var db = f.Card.Host.CreateContext();
        var row = await db.AgentTaskLandings.SingleAsync(o => o.Id == original.Id);
        if (change is null) db.Entry(row).CurrentValues.SetValues(original); else change(row);
        await db.SaveChangesAsync();
    }
    private static async Task<AgentTaskLanding> LandingAsync(CompletedCardMirrorFixture f, Guid id)
    { await using var db = f.Card.Host.CreateContext(); return await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == id); }
    private static async Task WithheldAsync(CompletedCardMirrorFixture f, string vector)
    {
        var result = await f.TryAsync();
        result.Complete.ShouldBeFalse(vector); result.Reason.ShouldBe("unlanded_work", vector);
        f.RemovalRequests.ShouldBe(0, vector); f.Journal.Read()!.Count.ShouldBe(0, vector);
        Directory.Exists(f.Binding.GitDirectory).ShouldBeTrue(vector);
        (await File.ReadAllTextAsync(f.Sentinel)).ShouldBe("mirror evidence\n", vector);
        var row = await f.EndpointAsync(); row.OperationId.ShouldBeNull(vector); row.ImportedReceipt.ShouldBeNull(vector);
    }
    private static async Task SetRequestAsync(CompletedCardMirrorFixture f, byte[] bytes)
    {
        await using var db = f.Card.Host.CreateContext();
        (await db.CardWorktreeCleanupEndpoints.Where(e => e.Id == f.EndpointId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ImmutableRequest, bytes))).ShouldBe(1);
    }
    private static async Task ReceiptVectorsAsync(params string[] fields)
    {
        foreach (var field in fields)
        foreach (var shape in new[] { "altered", "missing" })
        {
            await using var f = await CompletedCardMirrorFixture.CreateAsync();
            f.AlterReceipt = r =>
            {
                var json = System.Text.Json.JsonSerializer.SerializeToNode(r.Receipt, PhoneHomeFraming.Json)!.AsObject();
                var old = json[field]!.ToJsonString();
                if (shape == "missing") json.Remove(field);
                else json[field] = old == "true" ? System.Text.Json.Nodes.JsonValue.Create(false)
                    : old.StartsWith('"') ? System.Text.Json.Nodes.JsonValue.Create(field.EndsWith("Id") && Guid.TryParse(json[field]!.GetValue<string>(), out _) ? Guid.NewGuid().ToString("D") : "mismatch")
                    : System.Text.Json.Nodes.JsonValue.Create(999);
                return r with { Receipt = json.Deserialize<CompletedCardWorkspaceReceipt>(PhoneHomeFraming.Json) };
            };
            var reply = await f.TryAsync(); reply.Complete.ShouldBeFalse(field); reply.Reason.ShouldBe("receipt_mismatch", field);
            var row = await f.EndpointAsync(); row.State.ShouldNotBe(CardWorktreeCleanupEndpointState.Complete, field); row.ImportedReceipt.ShouldBeNull(field);
        }
    }
}
