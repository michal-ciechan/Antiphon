using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

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
            var reportHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Command.ArtifactPointers[0]))).ToLowerInvariant();
            Command = Command with { ReportDigest = reportHash, ArtifactProofDigest = reportHash,
                DisposalPolicyDigest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("fixture CardDone policy"))).ToLowerInvariant() };
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
    [Test] public async Task C1017_RunnerRepositoryIdentity()
    {
        await IdentityVectors(b => b with { RepositoryIdentity = "foreign" },
            b => b with { CommonDirectory = Path.GetDirectoryName(b.CommonDirectory)! }, b => b with { GitDirectory = b.CommonDirectory });
        foreach (var otherCommon in new[] { true, false })
        {
            await using var r = new RemovalRig(); var f = r.Fixture;
            var repo = f.Repository;
            if (otherCommon)
            {
                repo = Path.Combine(f.Root, "foreign-repository");
                CompletedCardRunnerFixture.Git(f.Root, "clone", f.Origin, repo);
            }
            var foreign = Path.Combine(f.Root, "foreign-worktree");
            CompletedCardRunnerFixture.Git(repo, "worktree", "add", "--detach", foreign, f.Sha);
            CompletedCardRunnerFixture.Git(foreign, "rev-parse", "HEAD").Trim().ShouldBe(f.Sha);
            var foreignSource = File.ReadAllBytes(Path.Combine(foreign, "source"));
            CompletedCardRunnerFixture.OverwriteGitFile(Path.Combine(f.Checkout, ".git"), File.ReadAllBytes(Path.Combine(foreign, ".git")));
            var proofReads = 0; f.BeforeGitStart = psi => { if (psi.ArgumentList.Contains("ls-remote")) proofReads++; };
            Held(await r.Remove(), "stale_mirror_binding"); proofReads.ShouldBe(0);
            File.ReadAllBytes(Path.Combine(foreign, "source")).ShouldBe(foreignSource);
            Directory.Exists(f.Binding.GitDirectory).ShouldBeTrue(); r.Preserved();
        }
    }
    // PC-57/243: canonical, sibling-prefix, outside, reusable/borrowed paths cannot replace recorded ordinary identity.
    [Test] public async Task C1017_RunnerPathIdentity()
    {
        await IdentityVectors(b => b with { WorktreePath = b.CommonDirectory },
            b => b with { WorktreePath = b.WorktreePath + "-foreign" }, b => b with { WorktreePath = Path.GetTempPath() },
            b => b with { WorktreePath = Path.Combine(Path.GetDirectoryName(b.WorktreePath)!, "slot") });
        await using var r = new RemovalRig(); var f = r.Fixture;
        CompletedCardRunnerFixture.Git(f.Origin, "branch", "feat/card-task-cafebabe", f.Sha);
        var second = await r.Workspace.MirrorAsync(new("feat/card-task-cafebabe", f.Sha, "task-cafebabe"), CancellationToken.None);
        var source = File.ReadAllBytes(Path.Combine(second.Path, "source"));
        Held(await r.Remove(r.Command with { Binding = r.Command.Binding with { WorktreePath = second.Path } }), "foreign_mirror_owner");
        File.ReadAllBytes(Path.Combine(second.Path, "source")).ShouldBe(source);
        foreach (var path in new[] { f.Repository, second.Path + "-prefix", Path.Combine(f.Root, "slot"), Path.Combine(f.Root, "outside") })
        {
            Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "keep"), "foreign bytes");
            Held(await r.Remove(r.Command with { Binding = r.Command.Binding with { WorktreePath = path } }), "foreign_mirror_owner");
            File.ReadAllText(Path.Combine(path, "keep")).ShouldBe("foreign bytes");
        }
        r.Preserved();
    }

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
        CompletedCardRunnerFixture.Git(r.Fixture.Checkout, "config", "user.name", "c1032");
        CompletedCardRunnerFixture.Git(r.Fixture.Checkout, "config", "user.email", "c1032@localhost");
        File.WriteAllText(Path.Combine(r.Fixture.Checkout, "new-source"), "unique committed head");
        CompletedCardRunnerFixture.Git(r.Fixture.Checkout, "add", "new-source");
        CompletedCardRunnerFixture.Git(r.Fixture.Checkout, "commit", "-m", "unique source");
        CompletedCardRunnerFixture.Git(r.Fixture.Checkout, "rev-parse", "HEAD").Trim().ShouldNotBe(r.Command.Proof.ExpectedHeadSha);
        Held(await r.Remove(), "head_mismatch");
        remoteReads.ShouldBe(0); r.Preserved();
    }
    // PC-60/244..247: missing/mismatched/stale/unsupported proof must not authorize deletion.
    // Caller 2026-10-13: bound-rebase POSITIVE moves to S7 C1032_AdmissionBoundRebaseProof.
    // S5 pins the fail-closed Rebased negative, including a claimed foreign landing identity.
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
        File.WriteAllText(Path.Combine(good.Fixture.Origin, "source"), "advanced published target");
        CompletedCardRunnerFixture.Git(good.Fixture.Origin, "commit", "-am", "advance preserved P");
        var published = CompletedCardRunnerFixture.Git(good.Fixture.Origin, "rev-parse", "HEAD").Trim();
        published.ShouldNotBe(good.Command.Proof.ExpectedHeadSha);
        CompletedCardRunnerFixture.Git(good.Fixture.Repository, "fetch", "origin", "main");
        var behind = good.Command with { Proof = good.Command.Proof with { ObservedTargetSha = published, PreservedSourceSha = published } };
        (await good.Remove(behind)).GetProperty("complete").GetBoolean().ShouldBeTrue("inspected H behind preserved P is eligible");
    }
    // PC-108: forged ordinary fields over a verification/unknown creation have no binding authority.
    [Test] public async Task C1017_RunnerSourceLandingExcluded()
    {
        await using var r = new RemovalRig(); var f = r.Fixture;
        var created = await r.Workspace.CreateVerificationAsync(new("task-abcdef12", f.Sha,
            "feat/card-task-abcdef12"), CancellationToken.None);
        var path = created.Coordinates.WorktreePath;
        var source = File.ReadAllBytes(Path.Combine(path, "source"));
        var forged = r.Command.Binding with { TaskId = Guid.Parse("abcdef12-0000-4000-8000-000000000001"),
            PreparationId = Guid.NewGuid(), BindingId = Guid.NewGuid(), WorktreePath = path,
            GitDirectory = created.Coordinates.WorktreeGitDirectory, SourceFullRef = "refs/heads/feat/card-task-abcdef12" };
        Held(await r.Remove(r.Command with { Binding = forged }), "unbound_mirror");
        File.ReadAllBytes(Path.Combine(path, "source")).ShouldBe(source);
        Directory.Exists(created.Coordinates.WorktreeGitDirectory).ShouldBeTrue();
        File.Exists(Path.Combine(f.Root, "work", "verification-creations", "task-abcdef12.json")).ShouldBeTrue(); r.Preserved();
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
        foreach (var state in new[] { "protected", "nested", "bare", "locked", "link", "dangling", "unknown", "evidence" })
        {
            await using var r = new RemovalRig(); var f = r.Fixture;
            Directory.CreateDirectory(Path.Combine(f.Checkout, "bin")); string expected;
            switch (state)
            {
                case "protected": File.WriteAllText(Path.Combine(f.Checkout, "bin", ".env"), "secret fixture"); expected = "protected_content"; break;
                case "nested": CompletedCardRunnerFixture.Git(Path.Combine(f.Checkout, "bin"), "init"); expected = "nested_repository"; break;
                case "bare": CompletedCardRunnerFixture.Git(Path.Combine(f.Checkout, "bin"), "init", "--bare"); expected = "nested_repository"; break;
                case "unknown": Directory.CreateDirectory(Path.Combine(f.Checkout, "bin", ".antiphon")); File.WriteAllText(Path.Combine(f.Checkout, "bin", ".antiphon", "invalid"), "invalid"); expected = "protected_content"; break;
                case "dangling": File.CreateSymbolicLink(Path.Combine(f.Checkout, "bin", "link"), Path.Combine(f.Root, "outside", "absent")); expected = "linked_content"; break;
                case "locked": CompletedCardRunnerFixture.Git(f.Repository, "worktree", "lock", f.Checkout); expected = "locked_registration"; break;
                case "link": File.CreateSymbolicLink(Path.Combine(f.Checkout, "bin", "link"), Path.Combine(f.Root, "outside", "sentinel")); expected = "linked_content"; break;
                default: Directory.CreateDirectory(Path.Combine(f.Checkout, ".antiphon")); File.WriteAllText(Path.Combine(f.Checkout, ".antiphon", "test.trx"), "evidence"); expected = "evidence_requires_admission"; break;
            }
            var request = r.Command with { PolicyPatterns = ["**"] };
            if (state == "evidence") request = request with { Disposition = "Ordinary" };
            Held(await r.Remove(request), state == "evidence" ? "disposition" : expected); r.Preserved();
            if (state == "evidence") File.ReadAllText(Path.Combine(f.Checkout, ".antiphon", "test.trx")).ShouldBe("evidence");
        }
    }
    // PC-70: raw Force arm must fail contract validation before any deletion.
    [Test] public async Task C1017_RunnerNoForceBypass()
    {
        await using var r = new RemovalRig();
        File.WriteAllText(Path.Combine(r.Fixture.Checkout, "source"), "dirty");
        var json = JsonSerializer.Serialize(r.Command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var raw = json[..^1] + ",\"force\":true}";
        var entry = r.Executor.GetType().GetMethod("RemoveJsonAsync");
        entry.ShouldNotBeNull("raw guarded command entry must validate Force before typed admission");
        var call = (Task)entry!.Invoke(r.Executor, [raw, r.Journal, r.Gate, CancellationToken.None])!;
        await call;
        var rawResult = JsonSerializer.SerializeToElement(call.GetType().GetProperty("Result")!.GetValue(call),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Held(rawResult, "force_forbidden");
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
            if (unknownCensus)
            {
                var root = HerdrPaneSidecar.DirectoryFor(r.Fixture.Settings.SessionLogPath); Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "torn.json"), "{");
                r.Fixture.Runtime.WorkspaceUseGate = r.Gate;
                await r.Fixture.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
            }
            else r.Gate.RecordOwner(r.Fixture.Checkout, Guid.NewGuid(), () => throw new IOException("unknown"));
            Held(await r.Remove(), "workspace_in_use"); r.Preserved();
        }
    }
    // PC-65/66: actual runtime launch and adoption honor the persisted removal intent.
    [Test] public async Task C1017_RunnerStartFence()
    {
        await using (var first = new RemovalRig())
        {
            var entered = NewBarrier(); var release = NewBarrier();
            first.Fixture.Runtime.WorkspaceUseGate = first.Gate;
            first.Fixture.EffectBarrier = async () => { entered.SetResult(); await release.Task; };
            var start = first.Fixture.Runtime.StartAsync(first.Fixture.Launch(Guid.NewGuid()), CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try { Held(await first.Remove(), "workspace_in_use"); first.Preserved(); first.Journal.Read()!.Count.ShouldBe(0); }
            finally { release.TrySetResult(); await Should.ThrowAsync<FenceEffectException>(() => start); }
        }
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
        foreach (var missing in new[] { "report", "artifacts", "policy", "pointers" })
        {
            await using var held = new RemovalRig();
            Directory.CreateDirectory(Path.Combine(held.Fixture.Checkout, ".antiphon"));
            var evidence = Path.Combine(held.Fixture.Checkout, ".antiphon", "run.trx"); File.WriteAllText(evidence, "held evidence");
            var request = missing switch { "report" => held.Command with { ReportDigest = "opaque" },
                "artifacts" => held.Command with { ArtifactProofDigest = "opaque" },
                "policy" => held.Command with { DisposalPolicyDigest = "opaque" }, _ => held.Command with { ArtifactPointers = [] } };
            Held(await held.Remove(request), "evidence_requires_admission"); File.ReadAllText(evidence).ShouldBe("held evidence"); held.Preserved();
        }
        // S7 report-loss repair: generic external artifact authority has no
        // per-report manifest, so these remote task-report bytes are not covered.
        foreach (var name in new[] { "task-deadbeef.md", "TASK-DEADBEEF.MD", "TaSk-dEaDbEeF.mD" })
        {
            await using var held = new RemovalRig();
            Directory.CreateDirectory(Path.Combine(held.Fixture.Checkout, ".antiphon"));
            var report = Path.Combine(held.Fixture.Checkout, ".antiphon", name);
            const string bytes = "complete evidence";
            File.WriteAllText(report, bytes);
            Held(await held.Remove(), "artifact_unpreserved");
            File.ReadAllText(report).ShouldBe(bytes); held.Preserved();
            held.Journal.Read()!.Any(i => i.Receipt is not null || i.SetAsidePath is not null).ShouldBeFalse();
        }
        await using var r = new RemovalRig(); var f = r.Fixture;
        Directory.CreateDirectory(Path.Combine(f.Checkout, "bin")); File.WriteAllText(Path.Combine(f.Checkout, "bin", "cache"), "disposable");
        var stale = Path.Combine(f.Root, "stale");
        CompletedCardRunnerFixture.Git(f.Repository, "worktree", "add", "--detach", stale, f.Sha);
        var staleAdmin = CompletedCardRunnerFixture.Git(stale, "rev-parse", "--path-format=absolute", "--git-dir").Trim(); Directory.Delete(stale, true);
        var refs = CompletedCardRunnerFixture.Git(f.Repository, "show-ref"); var originRefs = CompletedCardRunnerFixture.Git(f.Origin, "show-ref");
        Directory.CreateDirectory(Path.Combine(f.Checkout, ".antiphon", "checkpoints", "owned"));
        // Eligible evidence-disposal positive has no unpreserved task report.
        File.WriteAllText(Path.Combine(f.Checkout, ".antiphon", "complete.trx"), "complete evidence");
        File.WriteAllText(Path.Combine(f.Checkout, ".antiphon", "checkpoints", "owned", "run.trx"), "scratch evidence");
        var answer = await r.Remove(); answer.GetProperty("complete").GetBoolean().ShouldBeTrue(answer.GetRawText());
        var receipt = answer.GetProperty("receipt");
        receipt.GetProperty("taskId").GetGuid().ShouldBe(r.Command.Binding.TaskId);
        receipt.GetProperty("doneRevisionId").GetGuid().ShouldBe(r.Command.DoneRevisionId);
        receipt.GetProperty("operationId").GetGuid().ShouldBe(r.Command.OperationId);
        receipt.GetProperty("directoryRemoved").GetBoolean().ShouldBeTrue();
        receipt.GetProperty("registrationRemoved").GetBoolean().ShouldBeTrue();
        r.Journal.Read()!.Single().Receipt!.TaskId.ShouldBe(r.Command.Binding.TaskId);
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
        var saved = File.ReadAllText(r.Journal.JournalPath);
        var receipt = JsonNode.Parse(saved)!["intents"]![0]!["receipt"]!.AsObject();
        foreach (var field in receipt.Select(p => p.Key).ToArray())
        {
            var altered = JsonNode.Parse(saved)!;
            var value = altered["intents"]![0]!["receipt"]![field]!;
            altered["intents"]![0]!["receipt"]![field] = value.GetValueKind() switch
            {
                JsonValueKind.String => JsonValue.Create(Guid.TryParse(value.GetValue<string>(), out _) ? Guid.NewGuid().ToString() : "foreign"),
                JsonValueKind.Number => JsonValue.Create(value.GetValue<int>() + 1),
                JsonValueKind.True or JsonValueKind.False => JsonValue.Create(!value.GetValue<bool>()),
                _ => throw new InvalidOperationException(field),
            };
            File.WriteAllText(r.Journal.JournalPath, altered.ToJsonString());
            Held(await r.Remove(), "recipient_store_unreadable");
            Directory.Exists(r.Fixture.Checkout).ShouldBeFalse();
        }
        File.WriteAllText(r.Journal.JournalPath, saved);
        (await r.Remove()).GetProperty("complete").GetBoolean().ShouldBeTrue();
    }
    // PC-261..263: parent kills/joins its worker at each durable state/effect,
    // then fresh objects recover the exact command; remaining action allowance cannot reset.
    [Test] public async Task C1032_RemovalComponentRecovery()
    {
        var cuts = new[] { "state:Intent", "state:Prepared", "state:Moved", "state:Unregistering",
            "state:Unregistered", "state:Deleting", "state:Deleted", "state:Complete",
            "effect:rename", "effect:unregister", "effect:delete", "partial-delete" };
        foreach (var cut in cuts)
        {
            await using var r = new RemovalRig(); var f = r.Fixture;
            r.Hook("AfterJournalStateForTest", (Action<string>)(_ => { }));
            r.Hook("AfterMutationForTest", (Action<string>)(_ => { }));
            Directory.CreateDirectory(Path.Combine(f.Checkout, "bin"));
            File.WriteAllText(Path.Combine(f.Checkout, "bin", "cache-1"), "one");
            File.WriteAllText(Path.Combine(f.Checkout, "bin", "cache-2"), "two");
            var inputPath = Path.Combine(f.Root, "crash-input.json");
            File.WriteAllText(inputPath, JsonSerializer.Serialize(new CompletedCardRemovalCrashWorker.Input(
                f.Root, f.State, f.StoreId, f.Repository, f.Origin, r.Command, cut)));
            var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, WorkingDirectory = f.Root };
            psi.ArgumentList.Add("exec"); psi.ArgumentList.Add(typeof(CompletedCardWorkspaceRemovalTests).Assembly.Location);
            psi.Environment[CompletedCardRemovalCrashWorker.InputEnvironment] = inputPath;
            using var child = Process.Start(psi)!;
            var childStartIdentity = CompletedCardRemovalCrashWorker.StartIdentity(child);
            var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            JsonElement marker = default;
            try
            {
                var deadline = Stopwatch.StartNew();
                while (!File.Exists(inputPath + ".ready") && !child.HasExited && deadline.Elapsed < TimeSpan.FromSeconds(20))
                    await Task.Delay(25);
                child.HasExited.ShouldBeFalse("worker must pause at " + cut);
                File.Exists(inputPath + ".ready").ShouldBeTrue("worker cut " + cut);
                marker = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(inputPath + ".ready"));
                marker.GetProperty("pid").GetInt32().ShouldBe(child.Id);
                marker.GetProperty("startIdentity").GetString().ShouldBe(childStartIdentity, "exact kernel birth identity");
                marker.GetProperty("operationId").GetGuid().ShouldBe(r.Command.OperationId);
            }
            finally
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                await Task.WhenAll(stdout, stderr);
            }
            child.HasExited.ShouldBeTrue();
            var journal = new RunnerWorkspaceRemovalJournal(f.State, f.StoreId);
            var before = journal.Read()!.Single();
            before.Command.OperationId.ShouldBe(r.Command.OperationId);
            before.Command.Binding.TaskId.ShouldBe(r.Command.Binding.TaskId);
            before.Command.DoneRevisionId.ShouldBe(r.Command.DoneRevisionId);
            if (cut == "effect:rename")
            {
                before.Phase.ShouldBe("Prepared"); Directory.Exists(f.Checkout).ShouldBeFalse();
                Directory.Exists(before.SetAsidePath).ShouldBeTrue();
            }
            if (cut == "effect:unregister")
            {
                before.Phase.ShouldBe("Unregistering"); Directory.Exists(f.Binding.GitDirectory).ShouldBeFalse();
            }
            if (cut == "partial-delete")
            {
                before.Phase.ShouldBe("Deleting");
                File.Exists(Path.Combine(before.SetAsidePath!, "bin", "cache-1")).ShouldBeFalse();
                File.ReadAllText(Path.Combine(before.SetAsidePath!, "bin", "cache-2")).ShouldBe("two");
            }
            var restoredGate = new RunnerWorkspaceUseGate(journal);
            var restarted = f.NewRuntime(); restarted.WorkspaceUseGate = restoredGate;
            await restarted.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
            dynamic executor = new RunnerCompletedCardWorkspaceRemoval(r.Workspace);
            dynamic result = await executor.RemoveAsync(r.Command, journal, restoredGate, CancellationToken.None);
            ((bool)result.Complete).ShouldBeTrue(cut + ": " + (string?)result.Reason);
            var after = journal.Read()!.Single(); after.Phase.ShouldBe("Complete");
            after.RemainingAllowance.ShouldBeLessThanOrEqualTo(before.RemainingAllowance);
            if (cut is "partial-delete" or "state:Deleting" or "state:Deleted" or "state:Complete" or "effect:delete")
                after.RemainingAllowance.ShouldBe(before.RemainingAllowance, "same spent delete step cannot reset or recharge on restart");
            Directory.Exists(f.Checkout).ShouldBeFalse(); Directory.Exists(f.Binding.GitDirectory).ShouldBeFalse();
            File.ReadAllText(r.Command.ArtifactPointers[0]).ShouldBe("external result");
            File.ReadAllText(Path.Combine(f.Root, "outside", "sentinel")).ShouldBe("preserve");
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
        await using (var occupied = new RemovalRig())
        {
            var aside = occupied.Command.Binding.WorktreePath + ".cleanup-" + occupied.Command.OperationId.ToString("N");
            Directory.CreateDirectory(aside); File.WriteAllText(Path.Combine(aside, "foreign"), "foreign sibling");
            Held(await occupied.Remove(), "set_aside_occupied");
            File.ReadAllText(Path.Combine(aside, "foreign")).ShouldBe("foreign sibling"); occupied.Preserved();
        }
        await using (var stale = new RemovalRig())
        {
            stale.Hook("BeforeMutationForTest", (Action<string>)(step => { if (step == "unregister") throw new IOException("cut"); }));
            Held(await stale.Remove(), "removal_interrupted");
            var aside = stale.Journal.Read()!.Single().SetAsidePath!;
            CompletedCardRunnerFixture.Git(stale.Fixture.Origin, "update-ref", "-d", "refs/heads/main");
            stale.Hook("BeforeMutationForTest", (Action<string>)(_ => { }));
            Held(await stale.Remove(), "stale_preservation_proof");
            File.ReadAllText(Path.Combine(aside, "source")).ShouldBe("source");
            Directory.Exists(stale.Fixture.Binding.GitDirectory).ShouldBeTrue();
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
    // PC-583: ignored FIFO must refuse without reading it or retaining shared admission.
    [NativeLinuxOnly("Linux named-pipe inventory witness; native FIFO required")]
    [Test] public async Task C1032_RemovalLinuxIgnoredFifoRefuses()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux named-pipe inventory witness; native FIFO required");
        await using var r = new RemovalRig(); var f = r.Fixture;
        Directory.CreateDirectory(Path.Combine(f.Checkout, "bin"));
        var fifo = Path.Combine(f.Checkout, "bin", "pipe");
        CreateFifo(fifo, 0x180).ShouldBe(0);
        var inputPath = Path.Combine(f.Root, "fifo-input.json");
        File.WriteAllText(inputPath, JsonSerializer.Serialize(new CompletedCardRemovalCrashWorker.Input(
            f.Root, f.State, f.StoreId, f.Repository, f.Origin, r.Command, "fifo-refusal")));
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, WorkingDirectory = f.Root };
        psi.ArgumentList.Add("exec"); psi.ArgumentList.Add(typeof(CompletedCardWorkspaceRemovalTests).Assembly.Location);
        psi.Environment[CompletedCardRemovalCrashWorker.InputEnvironment] = inputPath;
        using var child = Process.Start(psi)!;
        var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        var hung = false;
        try
        {
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12)); }
            catch (TimeoutException) { hung = true; }
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(stdout, stderr);
        }
        hung.ShouldBeFalse("ignored FIFO pinned inventory beyond cancellation; owned child killed and joined");
        child.ExitCode.ShouldBe(0, await stderr);
        var result = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(inputPath + ".result"));
        result.GetProperty("Reason").GetString().ShouldBe("unproven_content");
        result.GetProperty("Complete").GetBoolean().ShouldBeFalse();
        result.GetProperty("admissionReleased").GetBoolean().ShouldBeTrue();
        r.Journal.Read()!.Single().Phase.ShouldBe("Intent");
        File.GetAttributes(fifo); r.Preserved();
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int CreateFifo(string path, uint mode);

    // PC-584: same bytes under a replacement identity must remain on disk.
    [Test] public Task C1032_RemovalLateFileIdentityRefuses() => LateFileRefuses(replace: true);
    // PC-585: changed bytes after the final inventory must remain on disk.
    [Test] public async Task C1032_RemovalLateFileContentRefuses()
    {
        await LateFileRefuses(replace: false);
        if (!OperatingSystem.IsWindows()) return;
        // Native PC-587 variants: a gate callback runs after hash/identity validation
        // while the disposition handle is still held. No sleep selects the writer.
        foreach (var effect in new[] { "write", "rename" })
        {
            await using var r = new RemovalRig(); var attempted = false;
            r.Hook("AfterDeleteValidationForTest", (Action<string>)(path =>
            {
                if (Path.GetFileName(path) != "source") return;
                if (effect == "write")
                    Should.Throw<IOException>(() => { using var writer = File.Open(path, FileMode.Open,
                        FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); });
                else Should.Throw<IOException>(() => File.Move(path, path + ".foreign"));
                attempted = true;
            }));
            (await r.Remove()).GetProperty("complete").GetBoolean().ShouldBeTrue();
            attempted.ShouldBeTrue("native writer/rename must attempt the held validated file");
            Directory.Exists(r.Fixture.Checkout).ShouldBeFalse();
            File.ReadAllText(r.Command.ArtifactPointers[0]).ShouldBe("external result");
        }
    }
    private static async Task LateFileRefuses(bool replace)
    {
        await using var r = new RemovalRig(); var changed = false; string? target = null;
        r.Hook("BeforeDeleteEntryForTest", (Action<string>)(path =>
        {
            if (Path.GetFileName(path) != "source") return;
            target = path;
            if (replace)
            {
                // Keep the old inode alive to prevent reuse from masking replacement.
                File.Move(path, Path.Combine(r.Fixture.Root, "old-source"));
                File.WriteAllText(path, "source");
            }
            else File.WriteAllText(path, "CHANGE"); // same length; size guard cannot mask hash guard
            changed = true;
        }));
        Held(await r.Remove(), "changed_since_inventory"); changed.ShouldBeTrue();
        File.ReadAllText(target!).ShouldBe(replace ? "source" : "CHANGE");
        r.Journal.Read()!.Single().Receipt.ShouldBeNull();
        await using var lease = await r.Gate.AcquireUseAsync(Path.Combine(r.Fixture.Root, "outside"), CancellationToken.None);
        File.ReadAllText(r.Command.ArtifactPointers[0]).ShouldBe("external result");
    }

    // PC-586: replacement root with identical names/bytes cannot inherit removal authority.
    [Test] public async Task C1032_RemovalLateRootIdentityRefuses()
    {
        await using var r = new RemovalRig(); var changed = false; string? target = null;
        r.Hook("BeforeDeleteEntryForTest", (Action<string>)(path =>
        {
            if (changed || path != r.Journal.Read()!.Single().SetAsidePath) return;
            target = path; Directory.Move(path, Path.Combine(r.Fixture.Root, "old-root"));
            Directory.CreateDirectory(path);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(r.Fixture.Root, "old-root")))
                File.Move(file, Path.Combine(path, Path.GetFileName(file))); // preserve child inodes; isolate root identity
            changed = true;
        }));
        Held(await r.Remove(), "changed_since_inventory"); changed.ShouldBeTrue();
        File.ReadAllText(Path.Combine(target!, "source")).ShouldBe("source");
        r.Journal.Read()!.Single().Receipt.ShouldBeNull();
    }

}
