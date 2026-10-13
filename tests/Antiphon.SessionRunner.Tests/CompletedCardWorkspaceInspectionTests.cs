using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

public sealed partial class CompletedCardWorkspaceRemovalTests
{
    // PC-233/234: remove during inspection or enroll an unknown preparation -> digest/count fails.
    [Test]
    public async Task C1032_InspectionReadOnlyBound()
    {
        await using var f = new CompletedCardRunnerFixture();
        var service = f.Workspace(gated: false, publishedBranch: "main");
        dynamic inspector = Inspector(service);
        var before = InspectionDigest(f.Root);
        dynamic observed = await inspector.InspectAsync(new CompletedCardWorkspaceInspectRequest(1, f.Binding), CancellationToken.None);
        ((string?)observed.Reason).ShouldBeNull();
        ((string)observed.HeadSha).ShouldBe(f.Sha);
        ((bool)observed.Eligible).ShouldBeTrue();
        InspectionDigest(f.Root).ShouldBe(before, "repositoryAndTreeDigestAfter");
        var store = service.OpenBindingStore();
        var count = Directory.GetFiles(store.StateRoot).Length;
        var starts = f.GitStarts;
        var vectors = new (CompletedCardWorkspaceBinding Binding, string Reason)[]
        {
            (f.Binding with { TaskId = Guid.NewGuid() }, "foreign_mirror_owner"),
            (f.Binding with { Attempt = 2 }, "foreign_mirror_owner"),
            (f.Binding with { BindingId = Guid.NewGuid() }, "foreign_mirror_owner"),
            (f.Binding with { PreparationId = Guid.NewGuid() }, "unbound_mirror"),
            (f.Binding with { RunnerStoreId = Guid.NewGuid().ToString("D") }, "runner_store_mismatch"),
            (f.Binding with { RunnerId = "other" }, "runner_store_mismatch"),
            (f.Binding with { RepositoryIdentity = "other" }, "foreign_mirror_owner"),
            (f.Binding with { CommonDirectory = f.Origin }, "foreign_mirror_owner"),
            (f.Binding with { GitDirectory = f.Origin }, "foreign_mirror_owner"),
            (f.Binding with { WorktreePath = f.Repository }, "foreign_mirror_owner"),
            (f.Binding with { SourceFullRef = "refs/heads/other" }, "foreign_mirror_owner"),
            (f.Binding with { CreationBaselineSha = new string('a', 40) }, "foreign_mirror_owner"),
        };
        foreach (var vector in vectors)
        {
            dynamic refused = await inspector.InspectAsync(new CompletedCardWorkspaceInspectRequest(1, vector.Binding), CancellationToken.None);
            ((string)refused.Reason).ShouldBe(vector.Reason);
            ((bool)refused.Eligible).ShouldBeFalse();
            f.GitStarts.ShouldBe(starts, "identity guard must precede external Git reads");
        }
        dynamic unknownVersion = await inspector.InspectAsync(new CompletedCardWorkspaceInspectRequest(2, f.Binding), CancellationToken.None);
        ((string)unknownVersion.Reason).ShouldBe("schema_version");
        f.GitStarts.ShouldBe(starts);
        // A real legacy tree does not acquire ownership from caller-supplied coordinates.
        CompletedCardRunnerFixture.Git(f.Origin, "branch", "feat/card-task-cafebabe");
        var legacy = await service.MirrorAsync(new("feat/card-task-cafebabe", f.Sha, "task-cafebabe"), CancellationToken.None);
        legacy.Binding.ShouldBeNull();
        var legacyAdmin = CompletedCardRunnerFixture.Git(legacy.Path, "rev-parse", "--path-format=absolute", "--git-dir").Trim();
        File.Exists(RunnerWorkspaceBindingStore.OwnerStampPath(legacyAdmin)).ShouldBeFalse();
        var legacyBefore = InspectionDigest(f.Root);
        dynamic unbound = await inspector.InspectAsync(new CompletedCardWorkspaceInspectRequest(1,
            f.Binding with { TaskId = Guid.Parse("cafebabe-0000-4000-8000-000000000001"),
                PreparationId = Guid.NewGuid(), BindingId = Guid.NewGuid(), WorktreePath = legacy.Path,
                SourceFullRef = "refs/heads/feat/card-task-cafebabe", GitDirectory = legacyAdmin }), CancellationToken.None);
        ((string)unbound.Reason).ShouldBe("unbound_mirror");
        Directory.GetFiles(store.StateRoot).Length.ShouldBe(count, "bindingCountAfter");
        InspectionDigest(f.Root).ShouldBe(legacyBefore);
        // Torn external creation storage is a hold, not permission to enroll it again.
        File.WriteAllText(store.RecordPath(f.Binding.PreparationId), "{");
        dynamic torn = await inspector.InspectAsync(new CompletedCardWorkspaceInspectRequest(1, f.Binding), CancellationToken.None);
        ((string)torn.Reason).ShouldBe("recipient_store_unreadable");
    }

    // PC-235..238: unknown/default, precedence, protected minima and .antiphon specificity.
    [Test]
    public async Task C1032_InspectionPolicyParity()
    {
        var type = typeof(RunnerWorkspaceService).Assembly.GetType("Antiphon.SessionRunner.RunnerWorkspaceIgnoredContent");
        type.ShouldNotBeNull("S4 requires the runner-local ignored-content classifier");
        dynamic classifier = Activator.CreateInstance(type!, new object[] { new[] { "**/bin/**", ".antiphon/**", "**/.env*" } })!;
        var witnesses = new (string Path, string Expected)[]
        {
            ("unknown.synthetic", "Protected"), ("bin/cache.dat", "Disposable"),
            (".antiphon/checkpoints/run.trx", "Evidence"), (".antiphon/task-12345678.md", "Evidence"),
            ("bin/.env", "Protected"), ("bin/appsettings.Development.json", "Protected"),
            ("bin/.antiphon/unknown", "Protected"), (".antiphon/report.md", "Protected"),
            (".antiphon/deliverables/result", "Protected"), ("../bin/cache.dat", "Protected"),
            ("/bin/cache.dat", "Protected"), ("C:/bin/cache.dat", "Protected"),
            ("BIN\\.ANTIPHON\\unknown", "Protected"),
        };
        foreach (var witness in witnesses)
            ((object)classifier.Classify(witness.Path)).ToString().ShouldBe(witness.Expected, witness.Path);
        // Physical inventory is read without following links/nested repositories and is fail closed.
        foreach (var vector in new[] { "dirty", "untracked", "protected", "evidence", "nested", "locked", "link" })
        {
            await using var f = new CompletedCardRunnerFixture();
            CompletedCardRunnerFixture.Git(f.Checkout, "config", "user.name", "c1032");
            File.WriteAllText(Path.Combine(f.Binding.GitDirectory, "info-exclude-unused"), "unused");
            File.AppendAllText(Path.Combine(f.Binding.CommonDirectory, "info", "exclude"), "\nbin/\n.antiphon/\n");
            string expected;
            switch (vector)
            {
                case "dirty": File.WriteAllText(Path.Combine(f.Checkout, "source"), "changed"); expected = "dirty_source"; break;
                case "untracked": File.WriteAllText(Path.Combine(f.Checkout, "new-source"), "new"); expected = "dirty_source"; break;
                case "locked": CompletedCardRunnerFixture.Git(f.Repository, "worktree", "lock", f.Checkout); expected = "locked_registration"; break;
                default:
                    Directory.CreateDirectory(Path.Combine(f.Checkout, "bin"));
                    if (vector == "nested") { CompletedCardRunnerFixture.Git(Path.Combine(f.Checkout, "bin"), "init"); expected = "nested_repository"; }
                    else if (vector == "link") { File.CreateSymbolicLink(Path.Combine(f.Checkout, "bin", "link"), Path.Combine(f.Root, "outside", "sentinel")); expected = "linked_content"; }
                    else if (vector == "evidence") { Directory.CreateDirectory(Path.Combine(f.Checkout, ".antiphon")); File.WriteAllText(Path.Combine(f.Checkout, ".antiphon", "test.trx"), "evidence"); expected = "evidence_requires_admission"; }
                    else { File.WriteAllText(Path.Combine(f.Checkout, "bin", ".env"), "preserve"); expected = "protected_content"; }
                    break;
            }
            var before = InspectionDigest(f.Root);
            dynamic inspector = Inspector(f.Workspace(gated: false, publishedBranch: "main"));
            dynamic result = await inspector.InspectAsync(new CompletedCardWorkspaceInspectRequest(1, f.Binding), CancellationToken.None);
            ((string)result.Reason).ShouldBe(expected, vector);
            ((bool)result.Eligible).ShouldBeFalse();
            InspectionDigest(f.Root).ShouldBe(before, vector);
        }
    }

    // PC-239: return creation baseline instead of actual HEAD -> advanced-HEAD equality fails.
    [Test]
    public async Task C1032_InspectionSourceProof()
    {
        await using var f = new CompletedCardRunnerFixture();
        var service = f.Workspace(gated: false, publishedBranch: "main");
        dynamic inspector = Inspector(service);
        CompletedCardRunnerFixture.Git(f.Checkout, "config", "user.name", "c1032");
        CompletedCardRunnerFixture.Git(f.Checkout, "config", "user.email", "c1032@localhost");
        File.WriteAllText(Path.Combine(f.Checkout, "source"), "advanced");
        CompletedCardRunnerFixture.Git(f.Checkout, "commit", "-am", "advance mirror");
        var actual = CompletedCardRunnerFixture.Git(f.Checkout, "rev-parse", "HEAD").Trim();
        actual.ShouldNotBe(f.Binding.CreationBaselineSha);
        dynamic observed = await inspector.InspectAsync(new CompletedCardWorkspaceInspectRequest(1, f.Binding), CancellationToken.None);
        ((string)observed.HeadSha).ShouldBe(actual, "observedHead");
        ((string?)observed.Reason).ShouldBeNull();
        var proof = f.Command().Proof with { ExpectedHeadSha = actual, DestinationFingerprint = f.Binding.RepositoryIdentity };
        ((string?)await inspector.CheckProofAsync(f.Binding, proof, CancellationToken.None)).ShouldBe("source_not_preserved");
        // Leave the fixture origin's main ref writable without relaxing Git receive safety.
        CompletedCardRunnerFixture.Git(f.Origin, "checkout", "--detach", f.Sha);
        CompletedCardRunnerFixture.Git(f.Checkout, "push", "origin", "HEAD:refs/heads/main");
        proof = proof with { ObservedTargetSha = actual, PreservedSourceSha = actual };
        ((string?)await inspector.CheckProofAsync(f.Binding, proof, CancellationToken.None)).ShouldBeNull();
        // H behind P is valid if fresh P actually contains H. Stale and task-only refs refuse.
        var behind = proof with { ExpectedHeadSha = f.Sha };
        ((string?)await inspector.CheckProofAsync(f.Binding, behind, CancellationToken.None)).ShouldBe("head_mismatch");
        CompletedCardRunnerFixture.Git(f.Checkout, "reset", "--hard", f.Sha);
        ((string?)await inspector.CheckProofAsync(f.Binding, behind, CancellationToken.None)).ShouldBeNull();
        foreach (var vector in new[]
        {
            behind with { DestinationRef = f.Binding.SourceFullRef },
            behind with { DestinationFingerprint = "foreign" },
            behind with { ProofKind = "Unknown" },
            behind with { ObservedTargetSha = f.Sha },
            behind with { ProofKind = "Rebased", RebasedSourceSha = actual },
        })
            ((string?)await inspector.CheckProofAsync(f.Binding, vector, CancellationToken.None)).ShouldNotBeNull();
        CompletedCardRunnerFixture.Git(f.Origin, "update-ref", "refs/heads/main", f.Sha);
        ((string?)await inspector.CheckProofAsync(f.Binding, behind, CancellationToken.None)).ShouldBe("stale_preservation_proof");
    }

    private static dynamic Inspector(RunnerWorkspaceService service)
    {
        var type = typeof(RunnerWorkspaceService).Assembly.GetType("Antiphon.SessionRunner.RunnerCompletedCardWorkspaceRemoval");
        type.ShouldNotBeNull("S4 requires real read-only bound inspection; no success DTO substitute");
        return Activator.CreateInstance(type!, service)!;
    }

    private static string InspectionDigest(string root)
    {
        var builder = new StringBuilder();
        void Visit(string path)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path).Order(StringComparer.Ordinal))
            {
                var attributes = File.GetAttributes(entry);
                builder.Append(Path.GetRelativePath(root, entry)).Append(':');
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    builder.Append(new FileInfo(entry).LinkTarget);
                else if (attributes.HasFlag(FileAttributes.Directory)) Visit(entry);
                else builder.Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry))));
            }
        }
        Visit(root);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
