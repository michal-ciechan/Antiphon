using System.Diagnostics;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WorkspaceParkPublicationTests
{
    [Test]
    public async Task C1065_CleanCommittedTipIsPublishedExactly()
    {
        foreach (var relation in new[] { "behind", "equal", "missing" })
        {
            using var world = await World.CreateAsync();
            var tip = await world.CommitAsync("work.txt", "committed WIP");
            if (relation == "equal") await world.PushAsync();
            if (relation == "missing") await world.GitAsync(world.Origin, "update-ref", "-d", World.FullRef);
            await File.WriteAllTextAsync(Path.Combine(world.Mirror, "run.generated"), "ignored receipt");
            var result = await world.Publisher().PrepareAsync(world.Request, CancellationToken.None);
            result.Outcome.ShouldBe(WorkspaceParkOutcome.Published, "G-8 " + relation);
            var receipt = result.Receipt.ShouldNotBeNull();
            (await world.RemoteShaAsync()).ShouldBe(tip, "G-8 exact owned remote ref");
            receipt.Request.ShouldBe(world.Request, "complete immutable publication coordinates");
            receipt.SourceSha.ShouldBe(tip);
            receipt.RemoteSha.ShouldBe(tip);
            receipt.RemoteBeforeSha.ShouldBe(relation == "missing" ? null : relation == "equal" ? tip : world.BaseSha);
            receipt.Clean.ShouldBeTrue();
            receipt.DescendsFromBaseline.ShouldBeTrue();
            receipt.ReceiptId.ShouldNotBe(Guid.Empty);
            world.Commands.Count(x => x[0] == "push").ShouldBe(relation == "equal" ? 0 : 1);
            world.Commands.Count(x => x[0] == "ls-remote").ShouldBe(2, "fresh proof even on equal/missing ref");
            world.Commands.ShouldNotContain(x => x[0] == "fetch" || x[0] == "reset" || x[0] == "merge"
                || x[0] == "commit" || x[0] == "add");
            var verified = await world.Publisher().VerifyAsync(receipt, CancellationToken.None);
            verified.Receipt.ShouldBe(receipt);
            await world.CommitAsync("later.txt", "source changed after receipt");
            var stale = await world.Publisher().VerifyAsync(receipt, CancellationToken.None);
            AssertHeld(stale, "park_source_changed", "receipt verification is read-only");
            (await world.RemoteShaAsync()).ShouldBe(tip);
        }

        // Two independently constructed publishers contend on the common repository lease.
        // The first is paused at actual push entry; the second must never reach that boundary.
        using var locked = await World.CreateAsync();
        await locked.CommitAsync("work.txt", "lease owner");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = locked.Publisher(async (boundary, ct) =>
        {
            if (boundary != WorkspaceParkBoundary.BeforePush) return;
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
        }).PrepareAsync(locked.Request, CancellationToken.None);
        WorkspaceParkResult firstResult;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var second = await locked.Publisher().PrepareAsync(locked.Request, CancellationToken.None);
            AssertHeld(second, "park_repository_lease_busy", "G-200");
            locked.Commands.Count(x => x[0] == "push").ShouldBe(0, "G-200 no competing mutation");
        }
        finally
        {
            release.TrySetResult();
            firstResult = await first;
        }
        firstResult.Outcome.ShouldBe(WorkspaceParkOutcome.Published, "G-200 owner completes");
        (await locked.RemoteShaAsync()).ShouldBe(await locked.HeadAsync());
    }

    [Test]
    public async Task C1065_DirtyOrUnsafeSourceCannotPublishReceipt()
    {
        foreach (var dirty in new[] { "unstaged", "staged", "untracked", "ignore-policy", "submodule" })
        {
            using var world = await World.CreateAsync();
            if (dirty == "submodule")
            {
                await world.GitAsync(world.Mirror, "-c", "protocol.file.allow=always", "submodule", "add", world.Origin, "module");
                await world.GitAsync(world.Mirror, "commit", "-am", "add submodule");
                await File.WriteAllTextAsync(Path.Combine(world.Mirror, "module", "source.txt"), "dirty module");
                await world.GitAsync(world.Mirror, "config", "submodule.module.ignore", "all");
            }
            else
            {
                var file = dirty == "ignore-policy" ? ".gitignore" : dirty == "untracked" ? "new.cs" : "source.txt";
                await File.WriteAllTextAsync(Path.Combine(world.Mirror, file), "unpublished source");
                if (dirty == "staged") await world.GitAsync(world.Mirror, "add", file);
            }
            var witness = await world.SourceSnapshotAsync();
            var result = await world.Publisher().PrepareAsync(world.Request, CancellationToken.None);
            var guard = dirty == "submodule" ? "G-11" : dirty == "untracked" ? "G-10" : "G-9";
            AssertHeld(result, "park_dirty", guard + " " + dirty);
            (await world.SourceSnapshotAsync()).ShouldBe(witness, guard + " bytes/index retained");
            await AssertNoPushAsync(world, guard);
        }

        foreach (var flag in new[] { "--assume-unchanged", "--skip-worktree" })
        {
            using var world = await World.CreateAsync();
            await world.GitAsync(world.Mirror, "update-index", flag, "source.txt");
            await File.WriteAllTextAsync(Path.Combine(world.Mirror, "source.txt"), "hidden source");
            AssertHeld(await world.Publisher().PrepareAsync(world.Request, CancellationToken.None),
                "park_index_unverifiable", "hidden tracked bytes");
            (await File.ReadAllTextAsync(Path.Combine(world.Mirror, "source.txt"))).ShouldBe("hidden source");
            await AssertNoPushAsync(world, "hidden source");
        }

        using (var world = await World.CreateAsync())
        {
            var outside = world.Request with { Path = Path.Combine(world.Root, "task-deadbeef") };
            var service = world.Publisher();
            service.ValidateLexicalTarget(outside).ShouldBe("park_path_unowned", "G-13 actual admission decision");
            AssertHeld(await service.PrepareAsync(outside, CancellationToken.None), "park_path_unowned", "G-13");
            world.Commands.ShouldBeEmpty("G-13 refuses before Git");
            foreach (var request in new[]
            {
                world.Request with { Binding = world.Request.Binding with { BaselineSha = world.BaseSha[..12] } },
                world.Request with { Binding = world.Request.Binding with { FullRef = "refs/heads/main" } },
                world.Request with { Binding = world.Request.Binding with { FullRef = World.Branch } },
            })
            {
                service.ValidateLexicalTarget(request).ShouldBe("park_invalid_target", "G-17 actual target decision");
                AssertHeld(await service.PrepareAsync(request, CancellationToken.None), "park_invalid_target", "G-17");
                world.Commands.ShouldBeEmpty("G-17 before Git");
            }
        }

        using (var world = await World.CreateAsync())
        {
            // The link points at this repository's correctly named/branched worktree outside
            // the allowed root: only resolved containment, not common ownership, can veto it.
            var outside = Path.Combine(world.Root, "task-deadbeef");
            await world.GitAsync(world.Repo, "worktree", "move", world.Mirror, outside);
            await CreateDirectoryLinkAsync(world.Mirror, outside);
            AssertHeld(await world.Publisher().PrepareAsync(world.Request, CancellationToken.None),
                "park_root_changed", "G-14");
            world.Commands.ShouldBeEmpty("G-14 resolved escape refuses before Git");
            (await world.GitAsync(outside, "rev-parse", "HEAD")).Trim().ShouldBe(world.BaseSha);
            Directory.Delete(world.Mirror);
        }

        using (var foreign = await World.CreateAsync())
        using (var owner = await World.CreateAsync())
        {
            await owner.GitAsync(owner.Repo, "worktree", "remove", owner.Mirror);
            await foreign.GitAsync(foreign.Repo, "worktree", "move", foreign.Mirror, owner.Mirror);
            var request = foreign.Request with { Path = owner.Mirror };
            AssertHeld(await owner.Publisher().PrepareAsync(request, CancellationToken.None),
                "park_repository_unowned", "G-15");
            owner.Commands.ShouldNotContain(x => x[0] == "push", "G-15");
        }

        foreach (var branch in new[] { "detached", "other" })
        {
            using var world = await World.CreateAsync();
            if (branch == "detached") await world.GitAsync(world.Mirror, "checkout", "--detach");
            else await world.GitAsync(world.Mirror, "checkout", "-b", "other");
            AssertHeld(await world.Publisher().PrepareAsync(world.Request, CancellationToken.None),
                "park_not_on_branch", "G-16 " + branch);
            await AssertNoPushAsync(world, "G-16");
        }

        foreach (var marker in new[] { "rebase-merge", "rebase-apply", "sequencer", "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD" })
        {
            using var world = await World.CreateAsync();
            var gitDir = (await world.GitAsync(world.Mirror, "rev-parse", "--absolute-git-dir")).Trim();
            var markerPath = Path.Combine(gitDir, marker);
            if (marker.Contains('-') || marker == "sequencer") Directory.CreateDirectory(markerPath);
            else await File.WriteAllTextAsync(markerPath, world.BaseSha + "\n");
            AssertHeld(await world.Publisher().PrepareAsync(world.Request, CancellationToken.None),
                "park_sequencer_active", "G-18 " + marker);
            (File.Exists(markerPath) || Directory.Exists(markerPath)).ShouldBeTrue("G-18 marker retained");
            await AssertNoPushAsync(world, "G-18");
        }

        using (var world = await World.CreateAsync())
        {
            await world.CommitAsync("runner.txt", "runner branch");
            await world.PushAsync();
            await world.GitAsync(world.Repo, "commit", "--allow-empty", "-m", "divergent baseline");
            var other = (await world.GitAsync(world.Repo, "rev-parse", "HEAD")).Trim();
            var request = world.Request with { Binding = world.Request.Binding with { BaselineSha = other } };
            AssertHeld(await world.Publisher().PrepareAsync(request, CancellationToken.None), "park_baseline_diverged", "G-19");
            world.Commands.ShouldNotContain(x => x[0] == "push", "G-19");
        }

        foreach (var relation in new[] { "divergent", "behind" })
        {
            using var world = await World.CreateAsync();
            if (relation == "divergent") await world.CommitAsync("runner.txt", "runner branch");
            await world.GitAsync(world.Repo, "commit", "--allow-empty", "-m", "remote writer");
            var other = (await world.GitAsync(world.Repo, "rev-parse", "HEAD")).Trim();
            await world.GitAsync(world.Repo, "push", "origin", "main:" + World.FullRef);
            var result = await world.Publisher().PrepareAsync(world.Request, CancellationToken.None);
            AssertHeld(result, "park_remote_not_ancestor", "G-20 " + relation);
            RunnerWorkspaceParkService.AncestryDecision(1, "park_remote_not_ancestor")!.Reason
                .ShouldBe("park_remote_not_ancestor", "G-20 actual decision before Git push masks it");
            world.Commands.ShouldNotContain(x => x[0] == "push", "G-20");
            (await world.RemoteShaAsync()).ShouldBe(other, "G-20 remote preserved");
        }
    }

    [Test]
    public async Task C1065_PushAckWithoutExactRemoteProofIsHeld()
    {
        using (var world = await World.CreateAsync())
        {
            await world.CommitAsync("runner.txt", "runner branch");
            string? other = null;
            var result = await world.Publisher(async (boundary, _) =>
            {
                if (boundary != WorkspaceParkBoundary.BeforePush) return;
                await world.GitAsync(world.Repo, "commit", "--allow-empty", "-m", "concurrent remote writer");
                other = (await world.GitAsync(world.Repo, "rev-parse", "HEAD")).Trim();
                await world.GitAsync(world.Repo, "push", "origin", "main:" + World.FullRef);
            }).PrepareAsync(world.Request, CancellationToken.None);
            AssertHeld(result, "park_push_rejected", "G-21");
            (await world.RemoteShaAsync()).ShouldBe(other, "G-21 FF push preserves concurrent writer");
            world.Commands.Single(x => x[0] == "push").ShouldNotContain(x => x.StartsWith("--force") || x.StartsWith('+'));
        }

        foreach (var fault in new[] { "head", "status", "ancestry", "remote", "start" })
        {
            using var world = await World.CreateAsync();
            world.BeforeStart = psi =>
            {
                var args = psi.ArgumentList;
                var fail = fault switch
                {
                    "head" => args.Contains("HEAD^{commit}"),
                    "status" => args[0] == "status",
                    "ancestry" => args[0] == "merge-base",
                    "remote" => args[0] == "ls-remote",
                    _ => args[0] == "status"
                };
                if (!fail) return;
                if (fault == "start") throw new IOException("synthetic private endpoint must not escape");
                ReplaceCommand(psi, "rev-parse", "--verify", "refs/heads/not-present");
            };
            var publisher = world.Publisher();
            if (fault == "head" || fault == "status")
            {
                // Assert the actual first inspection decision as well: a later ancestry or
                // equality check must not mask removal of this read-error guard (PC-22).
                var decision = await publisher.InspectSourceAsync(world.Mirror, world.Request.Binding, CancellationToken.None);
                decision.Failure.ShouldNotBeNull("G-22 first read decision").Outcome
                    .ShouldBe(WorkspaceParkOutcome.Unknown, "G-22 first read decision");
            }
            var result = await publisher.PrepareAsync(world.Request, CancellationToken.None);
            result.Outcome.ShouldBe(WorkspaceParkOutcome.Unknown, "G-22 " + fault);
            result.Receipt.ShouldBeNull("G-22");
            result.Reason.ShouldBe("park_inspection_unavailable", "G-22 bounded refusal");
        }

        foreach (var failure in new[] { "nonzero", "canceled" })
        {
            using var world = await World.CreateAsync();
            await world.CommitAsync("work.txt", "push outcome uncertain");
            using var cancel = new CancellationTokenSource();
            // Real push succeeds, but the publisher's own child fails/is canceled. This
            // distinguishes the push-error guard from the subsequent exact-ref guard.
            world.BeforeStart = psi =>
            {
                if (psi.ArgumentList[0] != "push") return;
                ReplaceCommand(psi, "rev-parse", "--verify", "refs/heads/not-present");
                if (failure == "canceled") cancel.Cancel();
            };
            var result = await world.Publisher(async (boundary, _) =>
            {
                if (boundary == WorkspaceParkBoundary.BeforePush) await world.PushAsync();
            }).PrepareAsync(world.Request, cancel.Token);
            AssertHeld(result, "park_push_rejected", "G-23 " + failure);
            (await world.RemoteShaAsync()).ShouldBe(await world.HeadAsync(), "G-23 independently published control");
        }

        using (var world = await World.CreateAsync())
        {
            await world.CommitAsync("work.txt", "unpublished");
            world.BeforeStart = psi =>
            {
                if (psi.ArgumentList[0] == "push") ReplaceCommand(psi, "rev-parse", "HEAD");
            };
            AssertHeld(await world.Publisher().PrepareAsync(world.Request, CancellationToken.None),
                "park_publish_unconfirmed", "G-24 lying ACK");
            (await world.RemoteShaAsync()).ShouldBe(world.BaseSha, "G-24 remote witness");
        }

        foreach (var initial in new[] { "behind", "equal", "missing" })
        foreach (var final in new[] { "missing", "moved", "unavailable" })
        {
            using var world = await World.CreateAsync();
            await world.CommitAsync("work.txt", "tip");
            if (initial == "equal") await world.PushAsync();
            if (initial == "missing") await world.GitAsync(world.Origin, "update-ref", "-d", World.FullRef);
            var guard = initial == "equal" ? "G-25" : "G-24";
            var result = await world.Publisher(async (boundary, _) =>
            {
                if (boundary != WorkspaceParkBoundary.BeforeFinalObservation) return;
                if (final == "unavailable") world.BeforeStart = psi =>
                {
                    if (psi.ArgumentList[0] == "ls-remote") ReplaceCommand(psi, "rev-parse", "--verify", "refs/heads/not-present");
                };
                else if (final == "missing") await world.GitAsync(world.Origin, "update-ref", "-d", World.FullRef);
                else await world.GitAsync(world.Origin, "update-ref", World.FullRef, world.BaseSha);
            }).PrepareAsync(world.Request, CancellationToken.None);
            result.Receipt.ShouldBeNull(guard + " " + initial + "/" + final);
            result.Outcome.ShouldBe(final == "unavailable" ? WorkspaceParkOutcome.Unknown : WorkspaceParkOutcome.Held, guard);
            world.Commands.Count(x => x[0] == "ls-remote").ShouldBe(2, guard + " fresh exact read");
        }

        foreach (var point in new[] { WorkspaceParkBoundary.AfterPush, WorkspaceParkBoundary.AfterFinalObservation })
        foreach (var drift in new[] { "endpoint", "head", "status", "branch" })
        {
            using var world = await World.CreateAsync();
            var published = await world.CommitAsync("work.txt", "published tip");
            var guard = drift == "endpoint" ? "G-26" : drift == "head" ? "G-27" : drift == "status" ? "G-28" : "G-16";
            var result = await world.Publisher(async (boundary, _) =>
            {
                if (boundary != point) return;
                switch (drift)
                {
                    case "endpoint":
                        var replacement = Path.Combine(world.Root, "replacement.git");
                        await world.GitAsync(world.Root, "clone", "--bare", world.Origin, replacement);
                        await world.GitAsync(world.Repo, "remote", "set-url", "--push", "origin", replacement);
                        break;
                    case "head": await world.CommitAsync("later.txt", "new source"); break;
                    case "status": await File.WriteAllTextAsync(Path.Combine(world.Mirror, "late.cs"), "late bytes"); break;
                    case "branch": await world.GitAsync(world.Mirror, "checkout", "-b", "replacement"); break;
                }
            }).PrepareAsync(world.Request, CancellationToken.None);
            var reason = drift switch
            {
                "endpoint" => "park_endpoint_changed", "head" => "park_source_changed",
                "status" => "park_dirty", _ => "park_not_on_branch"
            };
            AssertHeld(result, reason, guard + " " + point);
            (await world.RemoteShaAsync()).ShouldBe(published, guard + " no overwrite or cleanup");
        }
    }

    private static void AssertHeld(WorkspaceParkResult result, string reason, string guard)
    {
        result.Outcome.ShouldBe(WorkspaceParkOutcome.Held, guard);
        result.Reason.ShouldBe(reason, guard);
        result.Receipt.ShouldBeNull(guard);
    }

    private static async Task AssertNoPushAsync(World world, string guard)
    {
        world.Commands.ShouldNotContain(x => x[0] == "push", guard);
        (await world.RemoteShaAsync()).ShouldBe(world.BaseSha, guard + " remote preserved");
    }

    private static void ReplaceCommand(ProcessStartInfo psi, params string[] args)
    {
        psi.ArgumentList.Clear();
        foreach (var arg in args) psi.ArgumentList.Add(arg);
    }

    private static async Task CreateDirectoryLinkAsync(string link, string target)
    {
        try { Directory.CreateSymbolicLink(link, target); }
        catch (Exception ex) when (OperatingSystem.IsWindows() && ex is IOException or UnauthorizedAccessException)
        {
            var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var arg in new[] { "/d", "/c", "mklink", "/J", link, target }) psi.ArgumentList.Add(arg);
            await World.RunAsync(psi);
        }
    }

    private sealed class World : IDisposable
    {
        public const string Branch = "feat/card-task-deadbeef";
        public const string FullRef = "refs/heads/" + Branch;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "c1065-" + Guid.NewGuid().ToString("N"));
        public string Origin => Path.Combine(Root, "origin.git");
        public string Repo => Path.Combine(Root, "repo");
        public string Work => Path.Combine(Root, "work").Replace('\\', '/');
        public string Mirror => Work + "/worktrees/task-deadbeef";
        public string BaseSha { get; private set; } = "";
        public WorkspaceParkRequest Request { get; private set; } = null!;
        public List<string[]> Commands { get; } = [];
        public Action<ProcessStartInfo>? BeforeStart { get; set; }

        public static async Task<World> CreateAsync()
        {
            var w = new World();
            try
            {
                Directory.CreateDirectory(w.Root);
                Directory.CreateDirectory(w.Repo);
                Directory.CreateDirectory(Path.GetDirectoryName(w.Mirror)!);
                await File.WriteAllTextAsync(Path.Combine(w.Root, "empty.gitconfig"), "");
                await w.GitAsync(w.Root, "init", "--bare", "--initial-branch=main", w.Origin);
                await w.GitAsync(w.Repo, "init", "--initial-branch=main");
                await w.GitAsync(w.Repo, "config", "user.name", "c1065");
                await w.GitAsync(w.Repo, "config", "user.email", "c1065@localhost");
                await File.WriteAllTextAsync(Path.Combine(w.Repo, "source.txt"), "base");
                await File.WriteAllTextAsync(Path.Combine(w.Repo, ".gitignore"), "*.generated\n");
                await w.GitAsync(w.Repo, "add", ".");
                await w.GitAsync(w.Repo, "commit", "-m", "base");
                w.BaseSha = (await w.GitAsync(w.Repo, "rev-parse", "HEAD")).Trim();
                await w.GitAsync(w.Repo, "remote", "add", "origin", w.Origin);
                await w.GitAsync(w.Repo, "push", "origin", "main", "main:" + FullRef);
                await w.GitAsync(w.Repo, "worktree", "add", "-b", Branch, w.Mirror, w.BaseSha);
                w.Request = new(w.Mirror, new(Guid.NewGuid(), Guid.NewGuid(),
                    Guid.Parse("deadbeef-0000-4000-8000-000000000001"), 1, Guid.NewGuid(), Guid.NewGuid(),
                    "isolated-runner", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
                    "report-digest", RunnerWorkspaceParkService.RepositoryIdentity(Path.Combine(w.Repo, ".git")),
                    RunnerWorkspaceParkService.Fingerprint(w.Origin), FullRef, w.BaseSha));
                return w;
            }
            catch { w.Dispose(); throw; }
        }

        public RunnerWorkspaceParkService Publisher(Func<WorkspaceParkBoundary, CancellationToken, Task>? barrier = null) =>
            new(new RunnerWorkspaceService(Repo, Work, Origin, psi =>
            {
                Isolate(psi);
                Commands.Add(psi.ArgumentList.ToArray());
                BeforeStart?.Invoke(psi);
                return Process.Start(psi);
            }, TimeSpan.FromSeconds(15))) { BoundaryAsync = barrier };

        public async Task<string> CommitAsync(string file, string content)
        {
            await File.WriteAllTextAsync(Path.Combine(Mirror, file), content);
            await GitAsync(Mirror, "add", file);
            await GitAsync(Mirror, "commit", "-m", "truthful WIP");
            return await HeadAsync();
        }

        public Task<string> PushAsync() => GitAsync(Mirror, "push", "origin", FullRef + ":" + FullRef);
        public async Task<string> HeadAsync() => (await GitAsync(Mirror, "rev-parse", "HEAD")).Trim();
        public async Task<string> RemoteShaAsync() => (await GitAsync(Origin, "rev-parse", FullRef)).Trim();
        public async Task<string> SourceSnapshotAsync() =>
            await GitAsync(Mirror, "diff", "HEAD", "--binary", "--ignore-submodules=none")
            + await GitAsync(Mirror, "status", "--porcelain=v1", "--untracked-files=all", "--ignore-submodules=none")
            + string.Join("|", Directory.EnumerateFiles(Mirror).Where(p => Path.GetFileName(p) != ".git")
                .OrderBy(p => p).Select(p => Path.GetFileName(p) + ":" + File.ReadAllText(p)));

        public Task<string> GitAsync(string cwd, params string[] args)
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = cwd, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);
            Isolate(psi);
            return RunAsync(psi);
        }

        private void Isolate(ProcessStartInfo psi)
        {
            psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            psi.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(Root, "empty.gitconfig");
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            psi.Environment["GIT_CONFIG_COUNT"] = "0";
        }

        public static async Task<string> RunAsync(ProcessStartInfo psi)
        {
            using var process = Process.Start(psi) ?? throw new IOException("owned child did not start");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(timeout.Token); }
            catch
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(stdout, stderr);
                throw;
            }
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0) throw new IOException("scratch Git failed: " + error);
            return output;
        }

        public void Dispose()
        {
            if (!Directory.Exists(Root)) return;
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
