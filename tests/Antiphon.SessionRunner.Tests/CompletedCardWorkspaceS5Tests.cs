using System.Reflection;
using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

public sealed partial class CompletedCardWorkspaceRemovalTests
{
    private sealed class RemovalRig : IAsyncDisposable
    {
        public CompletedCardRunnerFixture Fixture { get; } = new();
        public RunnerWorkspaceRemovalJournal Journal { get; }
        public RunnerWorkspaceUseGate Gate { get; }
        public RunnerWorkspaceService Workspace { get; }
        public object Executor { get; }
        public CompletedCardWorkspaceCommand Command { get; }
        public RemovalRig()
        {
            var f = Fixture;
            Journal = new(f.State, f.StoreId);
            Gate = new(Journal); Gate.CompleteCensus();
            Workspace = f.Workspace(gated: false, publishedBranch: "main");
            Workspace.WorkspaceUseGate = Gate;
            Executor = new RunnerCompletedCardWorkspaceRemoval(Workspace);
            Command = f.Command() with { Proof = f.Command().Proof with { DestinationFingerprint = f.Binding.RepositoryIdentity },
                ArtifactPointers = [Path.Combine(f.Root, "reports", "result")] };
            File.WriteAllText(Command.ArtifactPointers[0], "external result");
            File.AppendAllText(Path.Combine(f.Binding.CommonDirectory, "info", "exclude"), "\nbin/\n.antiphon/\n");
        }
        public async Task<JsonElement> Remove(CompletedCardWorkspaceCommand? command = null)
        {
            var method = Executor.GetType().GetMethod("RemoveAsync", [typeof(CompletedCardWorkspaceCommand),
                typeof(RunnerWorkspaceRemovalJournal), typeof(RunnerWorkspaceUseGate), typeof(CancellationToken)]);
            method.ShouldNotBeNull("S5 guarded destructive entry point must exist");
            var task = (Task)method!.Invoke(Executor, [command ?? Command, Journal, Gate, CancellationToken.None])!;
            await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task);
            return JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        public void Hook(string name, object value)
        {
            var property = Executor.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            property.ShouldNotBeNull("S5 deterministic cut seam " + name);
            property!.SetValue(Executor, value);
        }
        public void Preserved()
        {
            File.ReadAllText(Path.Combine(Fixture.Checkout, "source")).ShouldBe("source");
            Directory.Exists(Fixture.Binding.GitDirectory).ShouldBeTrue();
            File.ReadAllText(Command.ArtifactPointers[0]).ShouldBe("external result");
            File.ReadAllText(Path.Combine(Fixture.Root, "outside", "sentinel")).ShouldBe("preserve");
        }
        public ValueTask DisposeAsync() => Fixture.DisposeAsync();
    }

    private static void Held(JsonElement result, string reason)
    {
        result.GetProperty("reason").GetString().ShouldBe(reason);
        result.GetProperty("complete").GetBoolean().ShouldBeFalse();
    }

    // PC-53/242: bypass full TaskId/BindingId -> expected identity refusal is missing before Git.
    [Test] public Task C1017_RunnerTaskIdentity() => IdentityVectors(b => b with { TaskId = Guid.Parse("deadbeef-0000-4000-8000-000000000099") },
        b => b with { BindingId = Guid.NewGuid() }, b => b with { PreparationId = Guid.NewGuid() });
    // PC-54: bypass attempt -> exact refusal before Git fails.
    [Test] public Task C1017_RunnerAttemptIdentity() => IdentityVectors(b => b with { Attempt = 2 });
    // PC-55: bypass store/runner comparison -> exact refusal before Git fails.
    [Test] public Task C1017_RunnerStoreIdentity() => IdentityVectors(b => b with { RunnerStoreId = Guid.NewGuid().ToString("D") },
        b => b with { RunnerId = "foreign" });
    // PC-56/240/241: bypass repository/common/admin comparator -> refusal before physical reads fails.
    [Test] public Task C1017_RunnerRepositoryIdentity() => IdentityVectors(b => b with { RepositoryIdentity = "foreign" },
        b => b with { CommonDirectory = Path.GetDirectoryName(b.CommonDirectory)! }, b => b with { GitDirectory = b.CommonDirectory });
    // PC-57/243: canonical, sibling-prefix, outside, reusable/borrowed paths cannot replace recorded ordinary identity.
    [Test] public Task C1017_RunnerPathIdentity() => IdentityVectors(b => b with { WorktreePath = b.CommonDirectory },
        b => b with { WorktreePath = b.WorktreePath + "-foreign" }, b => b with { WorktreePath = Path.GetTempPath() },
        b => b with { WorktreePath = Path.Combine(Path.GetDirectoryName(b.WorktreePath)!, "slot") });

    private static async Task IdentityVectors(params Func<CompletedCardWorkspaceBinding, CompletedCardWorkspaceBinding>[] vectors)
    {
        foreach (var vector in vectors)
        {
            await using var r = new RemovalRig();
            var starts = r.Fixture.GitStarts;
            var binding = vector(r.Command.Binding);
            var expected = binding.RunnerStoreId != r.Command.Binding.RunnerStoreId || binding.RunnerId != r.Command.Binding.RunnerId
                ? "runner_store_mismatch" : binding.PreparationId != r.Command.Binding.PreparationId ? "unbound_mirror" : "foreign_mirror_owner";
            Held(await r.Remove(r.Command with { Binding = binding }), expected);
            r.Fixture.GitStarts.ShouldBe(starts, "binding refusal before external Git"); r.Preserved();
            r.Journal.Read()!.Count.ShouldBe(0);
        }
    }

    // PC-58: bypass symbolic branch guard -> detached/equal-SHA branch refusal fails.
    [Test] public async Task C1017_RunnerBranchIdentity()
    {
        foreach (var detached in new[] { false, true })
        {
            await using var r = new RemovalRig(); var f = r.Fixture;
            if (detached) CompletedCardRunnerFixture.Git(f.Checkout, "checkout", "--detach", f.Sha);
            else CompletedCardRunnerFixture.Git(f.Checkout, "checkout", "-b", "foreign");
            Held(await r.Remove(), "source_ref_mismatch"); r.Preserved();
        }
    }
    // PC-59: bypass expected HEAD -> proof read is reached despite mismatch.
    [Test] public async Task C1017_RunnerHeadIdentity()
    {
        await using var r = new RemovalRig();
        var remoteReads = 0; r.Fixture.BeforeGitStart = p => { if (p.ArgumentList.Contains("ls-remote")) remoteReads++; };
        Held(await r.Remove(r.Command with { Proof = r.Command.Proof with { ExpectedHeadSha = new string('a', 40) } }), "head_mismatch");
        remoteReads.ShouldBe(0); r.Preserved();
    }
    // PC-60/244..247: missing/mismatched/stale/unsupported proof must not authorize deletion.
    [Test] public async Task C1017_RunnerPreservationProof()
    {
        var vectors = new (Func<CompletedCardWorkspaceProof, CompletedCardWorkspaceProof> Change, string Reason)[]
        {
            (p => p with { ProofIdentity = "" }, "required.ProofIdentity"),
            (p => p with { DestinationFingerprint = "foreign" }, "preservation_destination_mismatch"),
            (p => p with { DestinationRef = "refs/heads/feat/card-task-deadbeef" }, "preservation_destination_mismatch"),
            (p => p with { ObservedTargetSha = new string('a', 40) }, "stale_preservation_proof"),
            (p => p with { ProofKind = "Rebased", RebasedSourceSha = p.ExpectedHeadSha, ProofIdentity = "unrelated-landing" }, "preservation_proof_unsupported"),
        };
        foreach (var vector in vectors)
        {
            await using var r = new RemovalRig(); Held(await r.Remove(r.Command with { Proof = vector.Change(r.Command.Proof) }), vector.Reason); r.Preserved();
        }
        await using var good = new RemovalRig();
        (await good.Remove()).GetProperty("complete").GetBoolean().ShouldBeTrue();
    }
    // PC-108: forged ordinary fields over a verification/unknown creation have no binding authority.
    [Test] public async Task C1017_RunnerSourceLandingExcluded()
    {
        await using var r = new RemovalRig();
        File.Delete(r.Workspace.OpenBindingStore().RecordPath(r.Command.Binding.PreparationId));
        Held(await r.Remove(), "unbound_mirror"); r.Preserved();
    }
    // PC-61/248: accept staged, unstaged or untracked content -> initial dirty refusal fails.
    [Test] public async Task C1017_RunnerDirtySource()
    {
        foreach (var state in new[] { "staged", "unstaged", "untracked" })
        {
            await using var r = new RemovalRig(); var f = r.Fixture;
            var path = Path.Combine(f.Checkout, state == "untracked" ? "new" : "source");
            File.WriteAllText(path, state);
            if (state == "staged") CompletedCardRunnerFixture.Git(f.Checkout, "add", "source");
            Held(await r.Remove(), "dirty_source"); File.ReadAllText(path).ShouldBe(state); r.Journal.Read()!.Count.ShouldBe(0);
        }
    }
    // PC-62/249..252: protected minima, nested repos, lock and links override disposal policy.
    [Test] public async Task C1017_RunnerIgnoredPolicy()
    {
        foreach (var state in new[] { "protected", "nested", "locked", "link", "evidence" })
        {
            await using var r = new RemovalRig(); var f = r.Fixture;
            Directory.CreateDirectory(Path.Combine(f.Checkout, "bin")); string expected;
            switch (state)
            {
                case "protected": File.WriteAllText(Path.Combine(f.Checkout, "bin", ".env"), "secret fixture"); expected = "protected_content"; break;
                case "nested": CompletedCardRunnerFixture.Git(Path.Combine(f.Checkout, "bin"), "init"); expected = "nested_repository"; break;
                case "locked": CompletedCardRunnerFixture.Git(f.Repository, "worktree", "lock", f.Checkout); expected = "locked_registration"; break;
                case "link": File.CreateSymbolicLink(Path.Combine(f.Checkout, "bin", "link"), Path.Combine(f.Root, "outside", "sentinel")); expected = "linked_content"; break;
                default: Directory.CreateDirectory(Path.Combine(f.Checkout, ".antiphon")); File.WriteAllText(Path.Combine(f.Checkout, ".antiphon", "test.trx"), "evidence"); expected = "evidence_requires_admission"; break;
            }
            Held(await r.Remove(r.Command with { PolicyPatterns = ["**"] }), expected); r.Preserved();
        }
    }
    // PC-70: raw Force arm must fail contract validation before any deletion.
    [Test] public async Task C1017_RunnerNoForceBypass()
    {
        await using var r = new RemovalRig();
        File.WriteAllText(Path.Combine(r.Fixture.Checkout, "source"), "dirty");
        var json = JsonSerializer.Serialize(r.Command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var raw = json[..^1] + ",\"force\":true}";
        CompletedCardWorkspaceContract.Validate(raw).Reason.ShouldBe("force_forbidden");
        Held(await r.Remove(), "dirty_source"); File.ReadAllText(Path.Combine(r.Fixture.Checkout, "source")).ShouldBe("dirty");
    }
    // PC-64/253: retained live/pooled owner cannot be inferred absent from terminal task status.
    [Test] public async Task C1017_RunnerProcessOwnerHolds()
    {
        await using (var leased = new RemovalRig())
        {
            await using var use = await leased.Gate.AcquireUseAsync(leased.Fixture.Checkout, CancellationToken.None);
            Held(await leased.Remove().WaitAsync(TimeSpan.FromSeconds(10)), "workspace_in_use"); leased.Preserved();
        }
        foreach (var pooled in new[] { false, true })
        {
            await using var r = new RemovalRig(); var child = r.Fixture.Child();
            r.Gate.RecordOwner(r.Fixture.Checkout, Guid.NewGuid(), () => !child.HasExited, pooled ? "pooled" : "terminal-row");
            Held(await r.Remove(), "workspace_in_use"); r.Preserved(); child.HasExited.ShouldBeFalse();
        }
    }
    // PC-109: unknown census and unreadable owner observation must fail closed.
    [Test] public async Task C1017_UnknownProcessOwnershipHolds()
    {
        foreach (var unknownCensus in new[] { false, true })
        {
            await using var r = new RemovalRig();
            if (unknownCensus) r.Gate.MarkCensusUnknown();
            else r.Gate.RecordOwner(r.Fixture.Checkout, Guid.NewGuid(), () => throw new IOException("unknown"));
            Held(await r.Remove(), "workspace_in_use"); r.Preserved();
        }
    }
    // PC-65/66: actual runtime launch and adoption honor the persisted removal intent.
    [Test] public async Task C1017_RunnerStartFence()
    {
        await using var r = new RemovalRig(); var f = r.Fixture;
        r.Hook("BeforeMutationForTest", (Action<string>)(_ => throw new IOException("cut")));
        Held(await r.Remove(), "removal_interrupted");
        f.Runtime.WorkspaceUseGate = r.Gate;
        var ex = await Should.ThrowAsync<PhoneHomeAdmissionException>(() => f.Runtime.StartAsync(f.Launch(Guid.NewGuid()), CancellationToken.None));
        ex.Code.ShouldBe("workspace_retiring_or_unknown"); f.Effects.ShouldBe(0); r.Preserved();
    }
    [Test] public async Task C1017_RunnerReadoptionFence()
    {
        await using var r = new RemovalRig(); var f = r.Fixture;
        r.Hook("BeforeMutationForTest", (Action<string>)(_ => throw new IOException("cut")));
        Held(await r.Remove(), "removal_interrupted");
        var child = f.Child(); var id = Guid.NewGuid(); f.SeedManifest(child, id);
        var restartedGate = new RunnerWorkspaceUseGate(new(f.State, f.StoreId));
        f.Runtime.WorkspaceUseGate = restartedGate;
        await f.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        restartedGate.HasOwnershipConflict(f.Checkout).ShouldBeTrue(); child.HasExited.ShouldBeFalse(); f.Effects.ShouldBe(0);
    }
    // PC-67: first physical mutation must observe full external command and set-aside identity.
    [Test] public async Task C1017_RunnerIntentBeforeIo()
    {
        await using var r = new RemovalRig(); var observed = false;
        r.Hook("BeforeMutationForTest", (Action<string>)(step =>
        {
            if (step != "rename") return;
            var record = new RunnerWorkspaceRemovalJournal(r.Fixture.State, r.Fixture.StoreId).Read()!.Single();
            record.Command.OperationId.ShouldBe(r.Command.OperationId); record.Command.Binding.TaskId.ShouldBe(r.Command.Binding.TaskId);
            record.Command.DoneRevisionId.ShouldBe(r.Command.DoneRevisionId);
            using var json = JsonDocument.Parse(File.ReadAllText(r.Journal.JournalPath));
            json.RootElement.GetProperty("intents")[0].GetProperty("setAsidePath").GetString().ShouldNotBeNullOrWhiteSpace(); observed = true;
        }));
        (await r.Remove()).GetProperty("complete").GetBoolean().ShouldBeTrue(); observed.ShouldBeTrue();
    }
    // PC-69/254..257: change source, policy, owner, admin or remote after initial read -> final guard detects it.
    [Test] public async Task C1017_RunnerFreshFinalInspection()
    {
        foreach (var state in new[] { "source", "protected", "owner", "admin", "proof" })
        {
            await using var r = new RemovalRig(); var f = r.Fixture;
            r.Hook("AfterInitialInspectionForTest", (Func<Task>)(() =>
            {
                switch (state)
                {
                    case "source": File.WriteAllText(Path.Combine(f.Checkout, "source"), "late"); break;
                    case "protected": Directory.CreateDirectory(Path.Combine(f.Checkout, "bin")); File.WriteAllText(Path.Combine(f.Checkout, "bin", ".env"), "late"); break;
                    case "owner": r.Gate.RecordOwner(f.Checkout, Guid.NewGuid(), () => true); break;
                    case "admin": File.WriteAllText(RunnerWorkspaceBindingStore.OwnerStampPath(f.Binding.GitDirectory), "foreign"); break;
                    case "proof": CompletedCardRunnerFixture.Git(f.Origin, "update-ref", "-d", "refs/heads/main"); break;
                }
                return Task.CompletedTask;
            }));
            var reason = state switch { "source" => "dirty_source", "protected" => "protected_content", "owner" => "workspace_in_use", "admin" => "foreign_mirror_owner", _ => "stale_preservation_proof" };
            Held(await r.Remove(), reason);
            File.ReadAllText(Path.Combine(f.Checkout, "source")).ShouldBe(state == "source" ? "late" : "source");
            Directory.Exists(f.Binding.GitDirectory).ShouldBeTrue();
        }
    }
    // PC-258..260: returning a DTO or pruning all registrations or deleting refs breaks physical/ref oracles.
    [Test] public async Task C1032_RemovalEligiblePhysical()
    {
        await using var r = new RemovalRig(); var f = r.Fixture;
        Directory.CreateDirectory(Path.Combine(f.Checkout, "bin")); File.WriteAllText(Path.Combine(f.Checkout, "bin", "cache"), "disposable");
        var stale = Path.Combine(f.Root, "stale");
        CompletedCardRunnerFixture.Git(f.Repository, "worktree", "add", "--detach", stale, f.Sha);
        var staleAdmin = CompletedCardRunnerFixture.Git(stale, "rev-parse", "--path-format=absolute", "--git-dir").Trim(); Directory.Delete(stale, true);
        var refs = CompletedCardRunnerFixture.Git(f.Repository, "show-ref"); var originRefs = CompletedCardRunnerFixture.Git(f.Origin, "show-ref");
        var answer = await r.Remove(); answer.GetProperty("complete").GetBoolean().ShouldBeTrue();
        Directory.Exists(f.Checkout).ShouldBeFalse(); Directory.Exists(f.Binding.GitDirectory).ShouldBeFalse();
        Directory.Exists(staleAdmin).ShouldBeTrue();
        CompletedCardRunnerFixture.Git(f.Repository, "show-ref").ShouldBe(refs); CompletedCardRunnerFixture.Git(f.Origin, "show-ref").ShouldBe(originRefs);
        File.ReadAllText(r.Command.ArtifactPointers[0]).ShouldBe("external result"); File.ReadAllText(Path.Combine(f.Root, "outside", "sentinel")).ShouldBe("preserve");
        (await r.Remove()).GetProperty("receipt").GetRawText().ShouldBe(answer.GetProperty("receipt").GetRawText());
    }
    // PC-68: operation-id replay with changed valid digest must refuse even after saved completion.
    [Test] public async Task C1017_RunnerDigestReplay()
    {
        await using var r = new RemovalRig(); var first = await r.Remove();
        first.GetProperty("complete").GetBoolean().ShouldBeTrue();
        Held(await r.Remove(r.Command with { ReportDigest = "changed" }), "operation_digest_mismatch");
        (await r.Remove()).GetProperty("receipt").GetRawText().ShouldBe(first.GetProperty("receipt").GetRawText());
    }
    // PC-261..263: cut at each physical boundary, reconstruct journal/gate and resume exact owned components.
    [Test] public async Task C1032_RemovalComponentRecovery()
    {
        foreach (var cut in new[] { "rename", "unregister", "delete", "receipt" })
        {
            await using var r = new RemovalRig(); var f = r.Fixture; var hit = false;
            r.Hook("BeforeMutationForTest", (Action<string>)(step => { if (step == cut) { hit = true; throw new IOException("fixture cut"); } }));
            Held(await r.Remove(), "removal_interrupted"); hit.ShouldBeTrue();
            var persisted = File.ReadAllText(r.Journal.JournalPath);
            persisted.ShouldContain("setAsidePath");
            var executor = new RunnerCompletedCardWorkspaceRemoval(r.Workspace);
            var journal = new RunnerWorkspaceRemovalJournal(f.State, f.StoreId); var gate = new RunnerWorkspaceUseGate(journal); gate.CompleteCensus();
            dynamic dyn = executor;
            dynamic result = await dyn.RemoveAsync(r.Command, journal, gate, CancellationToken.None);
            ((bool)result.Complete).ShouldBeTrue(); Directory.Exists(f.Checkout).ShouldBeFalse(); Directory.Exists(f.Binding.GitDirectory).ShouldBeFalse();
        }
    }
    // PC-264..266: missing without receipt, original replacement and occupied sibling preserve foreign bytes.
    [Test] public async Task C1032_RemovalOriginalReplacement()
    {
        await using (var absent = new RemovalRig())
        {
            Directory.Delete(absent.Fixture.Checkout, true); Held(await absent.Remove(), "stale_mirror_binding");
            Directory.Exists(absent.Fixture.Binding.GitDirectory).ShouldBeTrue();
        }
        await using var r = new RemovalRig(); var f = r.Fixture;
        r.Hook("BeforeMutationForTest", (Action<string>)(step => { if (step == "unregister") throw new IOException("cut after rename"); }));
        Held(await r.Remove(), "removal_interrupted"); Directory.CreateDirectory(f.Checkout);
        File.WriteAllText(Path.Combine(f.Checkout, "replacement"), "foreign replacement");
        r.Hook("BeforeMutationForTest", (Action<string>)(_ => { }));
        (await r.Remove()).GetProperty("complete").GetBoolean().ShouldBeTrue();
        File.ReadAllText(Path.Combine(f.Checkout, "replacement")).ShouldBe("foreign replacement");
    }
    // PC-63: swap a listed child to an outside link at delete entry; only link may be removed.
    [Test] public async Task C1017_RunnerNoFollow()
    {
        await using var r = new RemovalRig(); var f = r.Fixture;
        Directory.CreateDirectory(Path.Combine(f.Checkout, "bin")); File.WriteAllText(Path.Combine(f.Checkout, "bin", "cache"), "disposable");
        r.Hook("BeforeDeleteEntryForTest", (Action<string>)(path =>
        {
            if (Path.GetFileName(path) != "bin") return;
            Directory.Delete(path, true); Directory.CreateSymbolicLink(path, Path.Combine(f.Root, "outside"));
        }));
        (await r.Remove()).GetProperty("complete").GetBoolean().ShouldBeTrue();
        File.ReadAllText(Path.Combine(f.Root, "outside", "sentinel")).ShouldBe("preserve");
    }
}
