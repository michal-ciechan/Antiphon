using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-0448, CARD-0452, CARD-0661, CARD-1092. The server's child journal and lease fence against the recover-repository-children
/// command (Antiphon.Nightly.RepositoryChildRecovery, which replaced scripts/recover-repository-children.ps1 in unit 23 of the
/// 2026-11-22 plan), run in this process on real repositories, records and processes.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RepositoryChildRecoveryTests
{
    private const string Execute = "--execute";
    private const string Confirm = "--confirm-descendants-exited";

    [Test]
    public async Task C452_R3_FirstSaveCrashLeavesNoAdmissionFence()
    {
        using var repo = new ScratchGitRepo("antiphon-c452-first");
        var git = new LandingGit();
        var common = await git.CommonDirectoryAsync(repo.Path, CancellationToken.None);
        var staged = await CrashBeforeReplaceAsync(repo.Path, "first", repo.WorktreeRoot);
        var expectedStaging = Path.Combine(common, "antiphon", "children-staging") + Path.DirectorySeparatorChar;
        staged.StartsWith(expectedStaging, StringComparison.Ordinal).ShouldBeTrue();
        var stagedBytes = await File.ReadAllBytesAsync(staged);
        JsonSerializer.Deserialize<RepositoryChildJournal.ChildRecord>(stagedBytes).ShouldNotBeNull();
        Directory.EnumerateFiles(Path.Combine(common, "antiphon", "children")).ShouldBeEmpty();
        var provider = new RepositoryMutationLease(git);
        await using (var admission = await provider.TryAcquireAsync(repo.Path, CancellationToken.None))
            admission.ShouldNotBeNull();
        Recover(repo.Path).Exit.ShouldBe(0);
        Recover(repo.Path, Execute, Confirm).Exit.ShouldBe(0);
        (await File.ReadAllBytesAsync(staged)).ShouldBe(stagedBytes);
    }

    [Test]
    public async Task C452_R3_UpdateCrashKeepsLastCommittedFence()
    {
        foreach (var cut in new[] { "started", "completed" })
        {
            using var repo = new ScratchGitRepo("antiphon-c452-update");
            var git = new LandingGit();
            var common = await git.CommonDirectoryAsync(repo.Path, CancellationToken.None);
            var staged = await CrashBeforeReplaceAsync(repo.Path, cut, repo.WorktreeRoot);
            var stagedBytes = await File.ReadAllBytesAsync(staged);
            var record = Directory.EnumerateFiles(Path.Combine(common, "antiphon", "children"), "*.json").Single();
            var oldBytes = await File.ReadAllBytesAsync(record);
            var old = JsonSerializer.Deserialize<RepositoryChildJournal.ChildRecord>(oldBytes).ShouldNotBeNull();
            if (cut == "started") old.ProcessId.ShouldBeNull();
            else { old.ProcessId.ShouldNotBeNull(); old.Completed.ShouldBeFalse(); }
            await using (var fenced = await new RepositoryMutationLease(git).TryAcquireAsync(repo.Path, CancellationToken.None))
                fenced.ShouldBeNull();
            (await File.ReadAllBytesAsync(record)).ShouldBe(oldBytes);
            Recover(repo.Path).Exit.ShouldBe(3);
            Recover(repo.Path, Execute).Exit.ShouldBe(3);
            var confirmed = Recover(repo.Path, Execute, Confirm);
            confirmed.Exit.ShouldBe(cut == "started" ? 3 : 0);
            if (cut == "started") (await File.ReadAllBytesAsync(record)).ShouldBe(oldBytes);
            else
            {
                File.Exists(record).ShouldBeFalse();
                await using var admission = await new RepositoryMutationLease(git).TryAcquireAsync(repo.Path, CancellationToken.None);
                admission.ShouldNotBeNull();
            }
            (await File.ReadAllBytesAsync(staged)).ShouldBe(stagedBytes);
        }
    }

    [Test]
    public async Task C452_R3_UnknownInsideChildrenRemainsFenced()
    {
        foreach (var name in new[] { "injected.json.tmp", "injected.json" })
        {
            using var repo = new ScratchGitRepo("antiphon-c452-unknown");
            var git = new LandingGit();
            var common = await git.CommonDirectoryAsync(repo.Path, CancellationToken.None);
            var children = Path.Combine(common, "antiphon", "children");
            Directory.CreateDirectory(children);
            var sentinel = Path.Combine(children, name);
            var bytes = System.Text.Encoding.UTF8.GetBytes("unrecognized child record");
            await File.WriteAllBytesAsync(sentinel, bytes);
            var staging = Path.Combine(common, "antiphon", "children-staging");
            Directory.CreateDirectory(staging);
            var sibling = Path.Combine(staging, "unrelated.tmp");
            await File.WriteAllTextAsync(sibling, "owned staging bytes");
            await using (var fenced = await new RepositoryMutationLease(git).TryAcquireAsync(repo.Path, CancellationToken.None))
                fenced.ShouldBeNull();
            foreach (var options in new[] { Array.Empty<string>(), [Execute], [Execute, Confirm] })
            {
                var run = Recover(repo.Path, options);
                run.Exit.ShouldBe(3);
                run.Output.ShouldContain("retained (unknown): " + sentinel);
                (await File.ReadAllBytesAsync(sentinel)).ShouldBe(bytes);
                (await File.ReadAllTextAsync(sibling)).ShouldBe("owned staging bytes");
            }
        }
    }

    [Test]
    [Arguments("alive")]
    [Arguments("reused")]
    [Arguments("unknown")]
    [Arguments("malformed")]
    [Arguments("torn")]
    [Arguments("wrong-repository")]
    [Arguments("busy")]
    public async Task C448_D1_RecoveryPreservesLiveOrAmbiguousEvidence(string state)
    {
        using var repo = new ScratchGitRepo("antiphon-journal-recovery");
        var git = new LandingGit();
        var common = await git.CommonDirectoryAsync(repo.Path, CancellationToken.None);
        var provider = new RepositoryMutationLease(git);
        await using var busy = state == "busy" ? await provider.TryAcquireAsync(repo.Path, CancellationToken.None) : null;
        var journal = await RepositoryChildJournal.BeginAsync(repo.Path, CancellationToken.None);
        using var child = StartSleeper();
        try
        {
            // Linux Process.StartTime is derived from wall-clock boot time and shifts across separate readers (CARD-0668).
            // The recovery runs in this process, so the identity is seeded with this process's own reading.
            if (state != "unknown")
                await journal.StartedAsync(child.Id, RepositoryRecoveryCommand.StartTicks(child.Id)
                    + (state == "reused" ? TimeSpan.TicksPerSecond : 0), CancellationToken.None);
            var path = Directory.EnumerateFiles(Path.Combine(common, "antiphon", "children"), "*.json").Single();
            if (state == "malformed") await File.WriteAllTextAsync(path, "invalid json");
            if (state == "torn") { File.Move(path, path + ".tmp"); path += ".tmp"; }
            if (state == "wrong-repository")
                await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(
                    new RepositoryChildJournal.ChildRecord(1, repo.WorktreeRoot, child.Id, child.StartTime.ToUniversalTime().Ticks)));
            var retainedHash = SHA256.HashData(await File.ReadAllBytesAsync(path));
            var recoverable = state == "reused";
            var run = Recover(repo.Path, Execute, Confirm);
            run.Exit.ShouldBe(recoverable ? 0 : 3, run.Error + run.Output);
            if (state == "alive") run.Output.ShouldContain("retained (alive): " + path);
            if (state == "busy")
            {
                run.Output.ShouldBeEmpty("a held lease refuses before any record is read");
                run.Error.ShouldStartWith("WARNING: Repository recovery refused: ");
            }
            Directory.EnumerateFiles(Path.Combine(common, "antiphon", "children")).Count().ShouldBe(recoverable ? 0 : 1);
            if (!recoverable) SHA256.HashData(await File.ReadAllBytesAsync(path)).ShouldBe(retainedHash);
            File.Exists(Path.Combine(common, "antiphon", "landing.lock")).ShouldBeTrue();
            child.HasExited.ShouldBeFalse("recovery never kills a live or reused PID");
            await using var admission = await provider.TryAcquireAsync(repo.Path, CancellationToken.None);
            if (recoverable) admission.ShouldNotBeNull();
            else admission.ShouldBeNull();
            if (state == "busy")
            {
                File.Delete(path); // This fixture owns the prior live record; isolate the lock control.
                var completed = await RepositoryChildJournal.BeginAsync(repo.Path, CancellationToken.None);
                var completedPath = Directory.EnumerateFiles(Path.Combine(common, "antiphon", "children"), "*.json").Single();
                await File.WriteAllTextAsync(completedPath, JsonSerializer.Serialize(
                    new RepositoryChildJournal.ChildRecord(1, common, 999999, null, Completed: true)));
                var completedBytes = await File.ReadAllBytesAsync(completedPath);
                RecoverChildren(repo.Path, 3, Execute, Confirm);
                (await File.ReadAllBytesAsync(completedPath)).ShouldBe(completedBytes);
                child.HasExited.ShouldBeFalse();
                await busy!.DisposeAsync();
                RecoverChildren(repo.Path, 0, Execute, Confirm);
            }
        }
        finally
        {
            if (!child.HasExited) child.Kill(true);
            await child.WaitForExitAsync();
        }
    }

    // CARD-0661. A fast child's owner saw its exact handle exit before reading a start identity and
    // saved Completed=true with no StartTicks; the owner then crashed before draining its output.
    // The server keeps the record fencing admission (a restart cannot prove descendant exit), and
    // the explicit command recovers it only after --execute --confirm-descendants-exited. It must never
    // look up the recorded PID's start time: that PID may be reused and Linux start times are
    // unstable across readers (CARD-0668). Incomplete or foreign completed records stay retained.
    [Test]
    [Arguments("completed")]
    [Arguments("completed-without-pid")]
    [Arguments("completed-wrong-repository")]
    public async Task C661_CompletedRecordIsRecoveredOnlyUnderExplicitConfirmation(string state)
    {
        using var repo = new ScratchGitRepo("antiphon-journal-completed");
        var git = new LandingGit();
        var common = await git.CommonDirectoryAsync(repo.Path, CancellationToken.None);
        await RepositoryChildJournal.BeginAsync(repo.Path, CancellationToken.None);
        var start = new ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("--version");
        int exitedId;
        using (var exited = Process.Start(start)!)
        {
            await exited.StandardOutput.ReadToEndAsync();
            await exited.WaitForExitAsync();
            exitedId = exited.Id;
        }
        var path = Directory.EnumerateFiles(Path.Combine(common, "antiphon", "children"), "*.json").Single();
        await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(new RepositoryChildJournal.ChildRecord(1,
            state == "completed-wrong-repository" ? repo.WorktreeRoot : common,
            state == "completed-without-pid" ? null : exitedId, null, Completed: true)));
        var retainedBytes = await File.ReadAllBytesAsync(path);
        using var unrelated = StartSleeper();
        try
        {

        // Server path: every fresh provider (as after a restart) stays fenced and names the recovery.
        foreach (var provider in new[] { new RepositoryMutationLease(git), new RepositoryMutationLease(git) })
        {
            await using (var fenced = await provider.TryAcquireAsync(repo.Path, CancellationToken.None))
                fenced.ShouldBeNull("a completed root cannot prove its descendants exited");
            (await provider.DescribeUnavailableAsync(repo.Path, CancellationToken.None))
                .ShouldNotBeNull().ShouldContain("scripts/antiphon recover-repository-children");
        }
        File.Exists(path).ShouldBeTrue("the server never clears a child record on its own");

        // Command path: preview and unconfirmed execution retain it.
        RecoverChildren(repo.Path, 3);
        (await File.ReadAllBytesAsync(path)).ShouldBe(retainedBytes);
        RecoverChildren(repo.Path, 3, Execute);
        (await File.ReadAllBytesAsync(path)).ShouldBe(retainedBytes);
        File.Exists(path).ShouldBeTrue("recovery requires explicit descendant confirmation");
        unrelated.HasExited.ShouldBeFalse();

        var recoverable = state == "completed";
        var confirmed = Recover(repo.Path, Execute, Confirm);
        confirmed.Exit.ShouldBe(recoverable ? 0 : 3, confirmed.Error + confirmed.Output);
        confirmed.Output.ShouldContain(recoverable ? "recovered (completed): " + path : "retained (unknown): " + path);
        File.Exists(path).ShouldBe(!recoverable);
        await using var admission = await new RepositoryMutationLease(git).TryAcquireAsync(repo.Path, CancellationToken.None);
        if (recoverable) admission.ShouldNotBeNull("a confirmed completed record no longer fences the repository");
        else admission.ShouldBeNull("an incomplete or foreign completed record stays retained");
        if (!recoverable) (await File.ReadAllBytesAsync(path)).ShouldBe(retainedBytes);
        unrelated.HasExited.ShouldBeFalse();
        }
        finally
        {
            if (!unrelated.HasExited) unrelated.Kill(entireProcessTree: true);
            await unrelated.WaitForExitAsync();
        }
    }

    // Windows qualification 28bbb58e: git --path-format=absolute printed
    // C:/Users/.../real parent/admin while the canonical path was C:\Users\...\real parent\admin.
    [Test]
    public void C452_R2_ForwardSlashDriveLetterGitOutputMatchesCanonicalForm()
    {
        const string raw = "C:/Users/lndco/AppData/Local/Temp/antiphon-c452-alias-trees/real parent/admin";
        const string canonical = "C:\\Users\\lndco\\AppData\\Local\\Temp\\antiphon-c452-alias-trees\\real parent\\admin";
        SameGitCommonPath(raw, canonical).ShouldBeTrue();
    }

    private static bool SameGitCommonPath(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() || HasWindowsDrive(left) || HasWindowsDrive(right)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(FoldGitSeparators(left), FoldGitSeparators(right), comparison);
    }

    private static string FoldGitSeparators(string path) => path.Replace('\\', '/').TrimEnd('/');

    private static bool HasWindowsDrive(string path) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';

    [Test]
    public async Task C452_R2_AliasRecoveryUsesCanonicalCommonDirectory()
    {
        using var repo = new ScratchGitRepo("antiphon-c452-alias");
        await repo.CommitFileAsync("seed.txt", "seed");
        var realParent = Path.Combine(repo.WorktreeRoot, "real parent");
        var aliasParent = Path.Combine(repo.WorktreeRoot, "alias parent");
        Directory.CreateDirectory(realParent);
        var admin = Path.Combine(realParent, "admin");
        Directory.Move(Path.Combine(repo.Path, ".git"), admin);
        if (OperatingSystem.IsWindows())
            (await RunProcessAsync("cmd.exe", ["/c", "mklink", "/J", aliasParent, realParent])).Exit.ShouldBe(0);
        else Directory.CreateSymbolicLink(aliasParent, realParent);
        var realCheckout = Path.Combine(realParent, "checkout");
        if (OperatingSystem.IsWindows())
            (await RunProcessAsync("cmd.exe", ["/c", "mklink", "/J", realCheckout, repo.Path])).Exit.ShouldBe(0);
        else Directory.CreateSymbolicLink(realCheckout, repo.Path);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(repo.Path, ".git"), "gitdir: " + Path.Combine(aliasParent, "admin") + "\n");
            var linked = Path.Combine(repo.WorktreeRoot, "linked");
            await repo.GitAsync("worktree", "add", "-b", "linked", linked, "HEAD");
            var raw = (await repo.GitReadAsync("rev-parse", "--path-format=absolute", "--git-common-dir")).Trim();
            var git = new LandingGit();
            var canonical = await git.CommonDirectoryAsync(repo.Path, CancellationToken.None);
            Console.WriteLine($"C452_ALIAS raw={JsonSerializer.Serialize(raw)} canonical={JsonSerializer.Serialize(canonical)} expected={JsonSerializer.Serialize(Path.GetFullPath(admin))}");
            canonical.ShouldBe(Path.GetFullPath(admin));
            // Git may resolve the alias and still print '/' (--path-format=absolute on
            // Windows). LandingGit and the recovery command already canonicalize that
            // spelling. On Linux, git realpaths before rev-parse, so the command is shown
            // the alias through its git seam and must canonicalize it the same way.
            Antiphon.Nightly.NightlyGit recoveryGit = Antiphon.Nightly.NightlyNativeGit.Run;
            if (!raw.Contains("alias parent", StringComparison.Ordinal))
            {
                SameGitCommonPath(raw, canonical).ShouldBeTrue();
                if (OperatingSystem.IsLinux())
                {
                    var alias = Path.Combine(aliasParent, "admin");
                    recoveryGit = (directory, arguments) =>
                        arguments.SequenceEqual(["rev-parse", "--path-format=absolute", "--git-common-dir"])
                            ? new Antiphon.Nightly.NightlyGitResult(0, alias + Environment.NewLine)
                            : Antiphon.Nightly.NightlyNativeGit.Run(directory, arguments);
                }
            }
            else raw.ShouldContain("alias parent");
            (int Exit, string Output, string Error) RecoverAliased(params string[] options) =>
                RepositoryRecoveryCommand.RunWithGit(repo.Path, recoveryGit, options);
            await RepositoryChildJournal.BeginAsync(repo.Path, CancellationToken.None);
            var record = Directory.EnumerateFiles(Path.Combine(canonical, "antiphon", "children"), "*.json").Single();
            var complete = new RepositoryChildJournal.ChildRecord(1, canonical, 999999, null, Completed: true);
            await File.WriteAllTextAsync(record, JsonSerializer.Serialize(complete));
            var bytes = await File.ReadAllBytesAsync(record);
            RecoverAliased().Exit.ShouldBe(3);
            (await File.ReadAllBytesAsync(record)).ShouldBe(bytes);
            RecoverAliased(Execute).Exit.ShouldBe(3);
            (await File.ReadAllBytesAsync(record)).ShouldBe(bytes);
            var confirmed = RecoverAliased(Execute, Confirm);
            confirmed.Exit.ShouldBe(0, confirmed.Error + confirmed.Output);
            File.Exists(record).ShouldBeFalse();
            File.Exists(Path.Combine(canonical, "antiphon", "landing.lock")).ShouldBeTrue();
            var provider = new RepositoryMutationLease(git);
            foreach (var path in new[] { repo.Path, linked, Path.Combine(aliasParent, "checkout") })
            {
                await using var lease = await provider.TryAcquireAsync(path, CancellationToken.None);
                lease.ShouldNotBeNull();
            }
            using var foreign = new ScratchGitRepo("antiphon-c452-foreign");
            var foreignCommon = await git.CommonDirectoryAsync(foreign.Path, CancellationToken.None);
            var foreignRecord = new RepositoryChildJournal.ChildRecord(1, foreignCommon, 999999, null, Completed: true);
            await File.WriteAllTextAsync(record, JsonSerializer.Serialize(foreignRecord));
            var foreignBytes = await File.ReadAllBytesAsync(record);
            RecoverAliased(Execute, Confirm).Exit.ShouldBe(3);
            (await File.ReadAllBytesAsync(record)).ShouldBe(foreignBytes);
            Directory.Exists(foreign.Path).ShouldBeTrue();
            File.Delete(record);
            await using var after = await provider.TryAcquireAsync(repo.Path, CancellationToken.None);
            after.ShouldNotBeNull();
        }
        finally
        {
            Directory.Delete(realCheckout, recursive: false);
            Directory.Delete(aliasParent, recursive: false);
        }
    }

    // CARD-1092. A tagged journal stores the dashed JSON guid. The preview suffix and the lease
    // fence sentence both print that same id as 32 hex digits.
    [Test]
    public async Task C1092_TaggedJournalPreviewUsesTheFenceTaskGuid()
    {
        using var repo = new ScratchGitRepo("antiphon-c1092-suffix");
        var git = new LandingGit();
        var taskId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        await RepositoryChildJournal.BeginAsync(repo.Path,
            new RepositoryChildTag(taskId, RepositoryChildPurposes.RemotePrepPush), CancellationToken.None);
        var common = await git.CommonDirectoryAsync(repo.Path, CancellationToken.None);
        var recordPath = Directory.EnumerateFiles(Path.Combine(common, "antiphon", "children"), "*.json").Single();
        var json = await File.ReadAllTextAsync(recordPath);
        json.ShouldContain(taskId.ToString("D"));
        json.ShouldNotContain(taskId.ToString("N"));

        var described = await new RepositoryMutationLease(git).DescribeUnavailableAsync(repo.Path, CancellationToken.None);
        var fenceSuffix = "(purpose=" + RepositoryChildPurposes.RemotePrepPush + " task=" + taskId.ToString("N") + ")";
        described.ShouldNotBeNull().ShouldContain(fenceSuffix);
        described.ShouldNotContain(taskId.ToString("D"));
        described.ShouldEndWith("; run scripts/antiphon recover-repository-children");

        var preview = Recover(repo.Path);
        preview.Exit.ShouldBe(3, preview.Error + preview.Output);
        var commandSuffix = " purpose=" + RepositoryChildPurposes.RemotePrepPush + " task=" + taskId.ToString("N");
        preview.Output.ShouldContain("retained (unknown): " + recordPath + commandSuffix);
        preview.Output.ShouldNotContain(taskId.ToString("D"));
        preview.Output.ShouldContain(Antiphon.Nightly.RepositoryChildRecovery.Hint);
    }

    private static (int Exit, string Output, string Error) Recover(string repository, params string[] options) =>
        RepositoryRecoveryCommand.Run(repository, options);

    private static void RecoverChildren(string repository, int expectedExit, params string[] options)
    {
        var result = Recover(repository, options);
        result.Exit.ShouldBe(expectedExit, result.Error + result.Output);
    }

    private static Process StartSleeper()
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 90" }) start.ArgumentList.Add(arg);
        return Process.Start(start)!;
    }

    private static async Task<string> CrashBeforeReplaceAsync(string repository, string cut, string readyRoot)
    {
        var ready = Path.Combine(readyRoot, "journal-ready-" + Guid.NewGuid().ToString("N") + ".json");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(RepositoryChildRecoveryTests).Assembly.Location);
        start.Environment[RepositoryJournalCrashWorker.Marker] = JsonSerializer.Serialize(
            new RepositoryJournalCrashWorker.Request(repository, ready, cut));
        using var child = Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!File.Exists(ready))
            {
                if (child.HasExited) throw new InvalidOperationException("Journal worker exited before barrier: " + await error);
                await Task.Delay(50, budget.Token);
            }
            using var evidence = JsonDocument.Parse(await File.ReadAllTextAsync(ready));
            evidence.RootElement.GetProperty("worker").GetInt32().ShouldBe(child.Id);
            var temporary = evidence.RootElement.GetProperty("temporary").GetString().ShouldNotBeNull();
            var priorHash = evidence.RootElement.GetProperty("previousHash").GetString();
            File.Exists(temporary).ShouldBeTrue();
            child.Kill(entireProcessTree: false);
            await child.WaitForExitAsync();
            if (priorHash is not null)
            {
                var common = await new LandingGit().CommonDirectoryAsync(repository, CancellationToken.None);
                var record = Directory.EnumerateFiles(Path.Combine(common, "antiphon", "children"), "*.json").Single();
                Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(record))).ShouldBe(priorHash);
            }
            return temporary;
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            await Task.WhenAll(output, error);
        }
    }

    private static async Task<(int Exit, string Output, string Error)> RunProcessAsync(string executable, string[] arguments,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var (key, value) in environment) start.Environment[key] = value;
        using var child = Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await child.WaitForExitAsync(budget.Token); }
        catch (OperationCanceledException)
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            throw;
        }
        return (child.ExitCode, await output, await error);
    }
}
