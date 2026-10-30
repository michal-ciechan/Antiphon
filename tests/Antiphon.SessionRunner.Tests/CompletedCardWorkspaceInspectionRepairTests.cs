using System.Diagnostics;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;
using System.Text;

namespace Antiphon.SessionRunner.Tests;

public sealed partial class CompletedCardWorkspaceRemovalTests
{
    [Test]
    [Arguments("assume-clean", "tracked_index_flags")]
    [Arguments("assume-dirty", "tracked_index_flags")]
    [Arguments("skip-clean", "tracked_index_flags")]
    [Arguments("skip-dirty", "tracked_index_flags")]
    [Arguments("both-flags", "tracked_index_flags")]
    [Arguments("flagged-newline-path", "tracked_index_flags")]
    [Arguments("ignored-bare", "nested_repository")]
    [Arguments("bare", "nested_repository")]
    [Arguments("admin-index.lock", "repository_locked")]
    [Arguments("common-index.lock", "repository_locked")]
    [Arguments("admin-HEAD.lock", "repository_locked")]
    [Arguments("common-packed-refs.lock", "repository_locked")]
    [Arguments("common-shallow.lock", "repository_locked")]
    [Arguments("common-gc.pid", "repository_locked")]
    [Arguments("admin-gc.pid", "repository_locked")]
    [Arguments("common-refs/heads/side.lock", "repository_locked")]
    [Arguments("admin-custom.lock", "repository_locked")]
    [Arguments("merge", "unfinished_operation")]
    [Arguments("rebase-merge", "unfinished_operation")]
    [Arguments("rebase-apply", "unfinished_operation")]
    [Arguments("CHERRY_PICK_HEAD", "unfinished_operation")]
    [Arguments("REVERT_HEAD", "unfinished_operation")]
    [Arguments("BISECT_LOG", "unfinished_operation")]
    [Arguments("sequencer", "unfinished_operation")]
    [Arguments("common-MERGE_HEAD", "unfinished_operation")]
    [Arguments("tracked", "dirty_source")]
    [Arguments("staged", "dirty_source")]
    [Arguments("untracked", "dirty_source")]
    [Arguments("protected", "protected_content")]
    [Arguments("evidence", "evidence_requires_admission")]
    [Arguments("link", "linked_content")]
    [Arguments("nested", "nested_repository")]
    [Arguments("ignored-nested", "nested_repository")]
    [Arguments("gitlink", "submodule_content")]
    [Arguments("locked-registration", "locked_registration")]
    [Arguments("prunable-registration", "stale_mirror_binding")]
    [Arguments("sparse", "sparse_checkout")]
    [Arguments("detached", "source_ref_mismatch")]
    [Arguments("unborn", "inspection_unreadable")]
    [Arguments("shallow", "shallow_repository")]
    [Arguments("other-repository", "stale_mirror_binding")]
    [Arguments("case-twin", "case_ambiguous_content")]
    public async Task C1032_InspectionHoldShapes(string shape, string expectedReason)
    {
        await using var f = new CompletedCardRunnerFixture();
        var flaggedPath = "source";
        CompletedCardRunnerFixture.Git(f.Checkout, "config", "user.name", "c1032");
        CompletedCardRunnerFixture.Git(f.Checkout, "config", "user.email", "c1032@localhost");
        File.AppendAllText(Path.Combine(f.Binding.CommonDirectory, "info", "exclude"), "\nbin/\n.antiphon/\n");
        var bin = Path.Combine(f.Checkout, "bin");
        if (shape.Contains("bare"))
        {
            var nested = shape == "ignored-bare" ? Path.Combine(bin, "nested.git") : Path.Combine(f.Checkout, "nested.git");
            Directory.CreateDirectory(nested);
            CompletedCardRunnerFixture.Git(nested, "init", "--bare");
            CompletedCardRunnerFixture.Git(f.Checkout, "push", nested, "HEAD:refs/heads/retained");
            CompletedCardRunnerFixture.Git(nested, "rev-parse", "refs/heads/retained").Trim().ShouldBe(f.Sha);
        }
        else if (shape.Contains("assume") || shape.StartsWith("skip-") || shape == "both-flags" || shape == "flagged-newline-path")
        {
            if (shape == "flagged-newline-path")
            {
                flaggedPath = OperatingSystem.IsWindows() ? "space path" : "line\nbreak path";
                File.WriteAllText(Path.Combine(f.Checkout, flaggedPath), "retained");
                CompletedCardRunnerFixture.Git(f.Checkout, "add", "--", flaggedPath);
                CompletedCardRunnerFixture.Git(f.Checkout, "commit", "-m", "flagged filename");
            }
            if (shape.StartsWith("skip-") || shape == "both-flags")
                CompletedCardRunnerFixture.Git(f.Checkout, "update-index", "--skip-worktree", "--", flaggedPath);
            if (!shape.StartsWith("skip-"))
                CompletedCardRunnerFixture.Git(f.Checkout, "update-index", "--assume-unchanged", "--", flaggedPath);
            if (!shape.EndsWith("clean")) File.WriteAllText(Path.Combine(f.Checkout, flaggedPath), "hidden source edit");
            CompletedCardRunnerFixture.Git(f.Checkout, "status", "--porcelain").ShouldBeEmpty("Git status suppresses flagged paths");
        }
        else if (shape.StartsWith("admin-") || shape.StartsWith("common-"))
        {
            var marker = shape[(shape.IndexOf('-') + 1)..];
            var directory = shape.StartsWith("admin-") ? f.Binding.GitDirectory : f.Binding.CommonDirectory;
            var path = Path.Combine(directory, marker);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, marker == "MERGE_HEAD" ? f.Sha + "\n" : "held fixture marker");
        }
        else switch (shape)
        {
            case "merge":
                CompletedCardRunnerFixture.Git(f.Checkout, "branch", "inspection-side");
                CompletedCardRunnerFixture.Git(f.Checkout, "checkout", "inspection-side");
                CompletedCardRunnerFixture.Git(f.Checkout, "commit", "--allow-empty", "-m", "side");
                CompletedCardRunnerFixture.Git(f.Checkout, "checkout", "feat/card-task-deadbeef");
                CompletedCardRunnerFixture.Git(f.Checkout, "merge", "--no-ff", "--no-commit", "inspection-side");
                File.Exists(Path.Combine(f.Binding.GitDirectory, "MERGE_HEAD")).ShouldBeTrue();
                CompletedCardRunnerFixture.Git(f.Checkout, "status", "--porcelain").ShouldBeEmpty();
                break;
            case "rebase-merge": case "rebase-apply": case "sequencer":
                Directory.CreateDirectory(Path.Combine(f.Binding.GitDirectory, shape)); break;
            case "CHERRY_PICK_HEAD": case "REVERT_HEAD": case "BISECT_LOG":
                File.WriteAllText(Path.Combine(f.Binding.GitDirectory, shape), f.Sha + "\n"); break;
            case "tracked": File.WriteAllText(Path.Combine(f.Checkout, "source"), "changed"); break;
            case "staged":
                File.WriteAllText(Path.Combine(f.Checkout, "source"), "staged");
                CompletedCardRunnerFixture.Git(f.Checkout, "add", "source"); break;
            case "untracked": File.WriteAllText(Path.Combine(f.Checkout, "unknown"), "new"); break;
            case "protected": Directory.CreateDirectory(bin); File.WriteAllText(Path.Combine(bin, ".env"), "retained"); break;
            case "evidence":
                Directory.CreateDirectory(Path.Combine(f.Checkout, ".antiphon"));
                File.WriteAllText(Path.Combine(f.Checkout, ".antiphon", "result.trx"), "evidence"); break;
            case "link":
                Directory.CreateDirectory(bin);
                Directory.CreateSymbolicLink(Path.Combine(bin, "link"), Path.Combine(f.Root, "outside")); break;
            case "nested": case "ignored-nested":
                var nested = shape == "nested" ? Path.Combine(f.Checkout, "nested") : Path.Combine(bin, "nested");
                Directory.CreateDirectory(nested); CompletedCardRunnerFixture.Git(nested, "init"); break;
            case "gitlink":
                CompletedCardRunnerFixture.Git(f.Checkout, "update-index", "--add", "--cacheinfo", "160000," + f.Sha + ",module");
                CompletedCardRunnerFixture.Git(f.Checkout, "commit", "-m", "unpopulated gitlink"); break;
            case "locked-registration": CompletedCardRunnerFixture.Git(f.Repository, "worktree", "lock", f.Checkout); break;
            case "prunable-registration":
                // A missing gitfile leaves a present directory but a prunable registration.
                File.Delete(Path.Combine(f.Checkout, ".git")); break;
            case "sparse": CompletedCardRunnerFixture.Git(f.Checkout, "config", "core.sparseCheckout", "true"); break;
            case "detached": CompletedCardRunnerFixture.Git(f.Checkout, "checkout", "--detach"); break;
            case "unborn":
                CompletedCardRunnerFixture.Git(f.Checkout, "update-ref", "-d", f.Binding.SourceFullRef); break;
            case "shallow":
                CompletedCardRunnerFixture.Git(f.Repository, "fetch", "--depth=1", "origin", "main");
                CompletedCardRunnerFixture.Git(f.Checkout, "rev-parse", "--is-shallow-repository").Trim().ShouldBe("true"); break;
            case "other-repository":
                CompletedCardRunnerFixture.OverwriteGitFile(Path.Combine(f.Checkout, ".git"),
                    Encoding.UTF8.GetBytes("gitdir: " + Path.Combine(f.Origin, ".git") + "\n")); break;
            case "case-twin":
                Directory.CreateDirectory(bin);
                if (OperatingSystem.IsWindows()) await RequireWindowsCaseSensitiveDirectoryAsync(bin);
                File.WriteAllText(Path.Combine(bin, "Twin"), "first");
                try
                {
                    using var twin = new FileStream(Path.Combine(bin, "twin"), FileMode.CreateNew, FileAccess.Write);
                    twin.Write(Encoding.UTF8.GetBytes("second"));
                }
                catch (IOException) when (File.Exists(Path.Combine(bin, "twin")))
                {
                    // Once fsutil admitted the Windows fixture, a failed arrangement is a failure.
                    if (OperatingSystem.IsWindows()) throw;
                    throw new SkipTestException("Case-twin inspection requires a case-sensitive scratch filesystem");
                }
                Directory.GetFiles(bin).Length.ShouldBe(2, "requires case-sensitive scratch filesystem"); break;
            default: throw new ArgumentException(shape);
        }
        var service = f.Workspace(gated: false, publishedBranch: "main");
        var calls = new List<string[]>();
        f.BeforeGitStart = psi => { AssertInspectionGitRead(psi); calls.Add(psi.ArgumentList.ToArray()); };
        var actualHead = shape is "unborn" or "other-repository" or "prunable-registration" ? null
            : CompletedCardRunnerFixture.Git(f.Checkout, "rev-parse", "HEAD").Trim();
        var before = InspectionDigest(f.Root);
        dynamic result = await Inspector(service).InspectAsync(new CompletedCardWorkspaceInspectRequest(1, f.Binding), CancellationToken.None);
        InspectionDigest(f.Root).ShouldBe(before, shape + " files/index/refs/store unchanged");
        ((string?)result.Reason).ShouldBe(expectedReason, shape);
        ((bool)result.Eligible).ShouldBeFalse(shape);
        if (actualHead is not null) ((string)result.HeadSha).ShouldBe(actualHead, "report actual inspected HEAD");
        if (expectedReason == "tracked_index_flags")
        {
            var paths = (IReadOnlyList<string>)result.HeldPaths;
            paths.ShouldContain(flaggedPath, "flagged paths must be reported without splitting filenames");
        }
        calls.Count.ShouldBeGreaterThan(0);
        Directory.Exists(f.State).ShouldBeFalse("inspection must not persist a retirement intent");
    }

    private static async Task RequireWindowsCaseSensitiveDirectoryAsync(string directory)
    {
        // Only this fixture's empty scratch directory changes; no elevation or host setting is used.
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "fsutil.exe"))
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { "file", "setCaseSensitiveInfo", directory, "enable" })
            start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        try { process.Start().ShouldBeTrue(); }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            throw new SkipTestException("CARD-1032 Windows case-twin capability unavailable: fsutil.exe is missing; per-directory case sensitivity is required.");
        }
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            throw new TimeoutException("fsutil case-sensitivity capability probe exceeded ten seconds");
        }
        var diagnostic = ((await stdout) + (await stderr)).Trim();
        if (process.ExitCode != 0)
            throw new SkipTestException("CARD-1032 Windows case-twin capability unavailable: fsutil file setCaseSensitiveInfo <scratch> enable exited "
                + process.ExitCode + ": " + diagnostic
                + "; this account/filesystem must support per-directory case sensitivity. The Windows coverage obligation remains required.");
    }

    [Test]
    public async Task C1032_InspectionReadOnlyGitCalls()
    {
        await using var f = new CompletedCardRunnerFixture();
        var service = f.Workspace(gated: false, publishedBranch: "main");
        var commands = new List<string>();
        f.BeforeGitStart = psi => { AssertInspectionGitRead(psi); commands.Add(psi.ArgumentList.Contains("ls-remote") ? "ls-remote" : psi.ArgumentList[1]); };
        File.SetLastWriteTimeUtc(Path.Combine(f.Checkout, "source"), DateTime.UtcNow.AddDays(-1));
        var before = InspectionDigest(f.Root);
        var inspector = new RunnerCompletedCardWorkspaceRemoval(service);
        var inspection = await inspector.InspectAsync(new(1, f.Binding), CancellationToken.None);
        inspection.Eligible.ShouldBeTrue();
        inspection.HeadSha.ShouldBe(f.Sha);
        var proof = f.Command().Proof with { DestinationFingerprint = f.Binding.RepositoryIdentity };
        (await inspector.CheckProofAsync(f.Binding, proof, CancellationToken.None)).ShouldBeNull();
        commands.ShouldContain("diff-index"); commands.ShouldContain("ls-remote"); commands.ShouldContain("merge-base");
        InspectionDigest(f.Root).ShouldBe(before, "mtime-only inspection and proof never refresh index or write files");
        Directory.Exists(f.State).ShouldBeFalse("inspection must not create retirement intent");
    }

    private static void AssertInspectionGitRead(ProcessStartInfo psi)
    {
        psi.Environment["GIT_OPTIONAL_LOCKS"].ShouldBe("0", "every inspection Git call disables optional locks");
        psi.ArgumentList[0].ShouldBe("--no-replace-objects");
        psi.Environment["GIT_NO_REPLACE_OBJECTS"].ShouldBe("1");
        psi.Environment["GIT_GRAFT_FILE"].ShouldBe(OperatingSystem.IsWindows() ? "NUL" : "/dev/null");
        var count = int.Parse(psi.Environment["GIT_CONFIG_COUNT"]!);
        var config = Enumerable.Range(0, count).ToDictionary(i => psi.Environment["GIT_CONFIG_KEY_" + i]!,
            i => psi.Environment["GIT_CONFIG_VALUE_" + i]);
        config["core.fsmonitor"].ShouldBe("false");
        config["core.hooksPath"].ShouldBe(OperatingSystem.IsWindows() ? "NUL" : "/dev/null");
        config["core.attributesFile"].ShouldBe(OperatingSystem.IsWindows() ? "NUL" : "/dev/null");
        var args = psi.ArgumentList.Skip(1).ToArray();
        if (args[0].StartsWith("--git-dir=", StringComparison.Ordinal))
        {
            args[0].ShouldBe("--git-dir=" + (OperatingSystem.IsWindows() ? "NUL" : "/dev/null"));
            args[1].ShouldBe("ls-remote", "only the repository-independent remote read uses a null git-dir");
            args = args.Skip(1).ToArray();
        }
        else args[0].ShouldNotBe("ls-remote", "remote reads must not load repository configuration");
        var allowed = args[0] switch
        {
            "rev-parse" => args.Skip(1).SequenceEqual(new[] { "HEAD" })
                || args.Skip(1).SequenceEqual(new[] { "--is-shallow-repository" })
                || args.Skip(1).SequenceEqual(new[] { "--path-format=absolute", "--git-dir" })
                || args.Skip(1).SequenceEqual(new[] { "--path-format=absolute", "--git-common-dir" }),
            "remote" => args.Skip(1).SequenceEqual(new[] { "get-url", "origin" }),
            "symbolic-ref" => args.Skip(1).SequenceEqual(new[] { "-q", "HEAD" }),
            "worktree" => args.Skip(1).SequenceEqual(new[] { "list", "--porcelain", "-z" }),
            "diff-index" => args.Skip(1).SequenceEqual(new[] { "--cached", "--name-only", "-z", "--no-ext-diff", "--no-textconv", "HEAD", "--" }),
            "ls-files" => args.Skip(1).SequenceEqual(new[] { "--others", "--ignored", "--exclude-standard", "-z" })
                || args.Skip(1).SequenceEqual(new[] { "--others", "--exclude-standard", "-z" })
                || args.Skip(1).SequenceEqual(new[] { "-v", "-z" })
                || args.Skip(1).SequenceEqual(new[] { "--stage", "-z" }),
            "config" => args.Skip(1).SequenceEqual(new[] { "--bool", "--get", "core.sparseCheckout" }),
            "ls-remote" => args.Length == 4 && args[1] == "--refs" && args[3] == "refs/heads/main"
                && RepositoryCloneSource.TryNormalize(args[2], out var identity) && identity == args[2],
            "merge-base" => args.Length == 4 && args[1] == "--is-ancestor",
            _ => false,
        };
        allowed.ShouldBeTrue("read-only Git argument allowlist: " + string.Join(' ', args));
    }
}
