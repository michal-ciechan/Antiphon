using System.Diagnostics;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0889 N3. Cooperative cancellation is a cancel file published by <see cref="Register"/>;
/// the noncooperative path cancels the harness caller token and expects that token back.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ScriptHarnessCancellationTests
{
    private const int GuardSeconds = 300;

    [Test]
    [Arguments("before-ready")]
    [Arguments("partial-log")]
    [Arguments("release-held")]
    public async Task C578_cancellation_reaches_the_held_phase_and_joins_owned_processes(string phase)
    {
        var nonce = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "c889-cancel-" + nonce);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "nonce"), nonce);
        using var publish = new CancellationTokenSource();
        var run = Start(directory, phase, CancellationToken.None);
        try
        {
            await WaitForHoldingAsync(directory, nonce, phase, run);
            using (Register(publish.Token, directory, nonce))
            {
                publish.Cancel();
                var result = await run;
                var ack = ReadAck(directory);
                var journal = File.ReadAllText(Path.Combine(directory, "identity.journal"));
                var buildLog = File.ReadAllText(Path.Combine(directory, "build.log"));
                var wrapper = File.ReadAllText(Path.Combine(directory, "wrapper.stdout"));
                var presence = File.ReadAllText(Path.Combine(directory, "run.presence"));
                ack["nonce"].ShouldBe(nonce, "harness-cancel-reaches-phase");
                ack["phase"].ShouldBe(phase, "harness-cancel-reaches-phase");
                ack["parentObserved"].ShouldBe("cancel", "harness-cancel-reaches-phase");
                ack["shimExit"].ShouldBe("130", "harness-cancel-cooperative");
                ack["cleanup"].ShouldBe("cooperative", "harness-cancel-cooperative");
                ack["descriptor"].ShouldBe("caller", "harness-cancel-cooperative");
                buildLog.Contains("DOTNET build EXIT CODE:", StringComparison.Ordinal).ShouldBeFalse("harness-cancel-no-completion-receipt");
                (wrapper + result.Stdout).Contains("CHECKPOINT CP-1 EXIT CODE:", StringComparison.Ordinal)
                    .ShouldBeFalse("harness-cancel-no-completion-receipt");
                presence.ShouldContain("entry=False", Case.Sensitive, "harness-cancel-no-completion-receipt");
                presence.ShouldContain("log=False", Case.Sensitive, "harness-cancel-no-completion-receipt");
                presence.ShouldContain("trx=False", Case.Sensitive, "harness-cancel-no-completion-receipt");
                ack["wrapperFirst"].ShouldBe("true", "harness-cancel-no-completion-receipt");
                result.Stdout.ShouldContain("FAIL C578 FailedBuild cancelled", Case.Sensitive, "harness-cancel-no-completion-receipt");
                result.Stdout.ShouldContain("PASS C578 FailedBuild owned processes exited", Case.Sensitive, "harness-cancel-no-completion-receipt");
                result.Stdout.ShouldContain("C578_BUILD_STARTED_" + nonce, Case.Sensitive, "harness-both-pipes-drained");
                result.Stderr.ShouldContain("C578_BUILD_ERROR_" + nonce, Case.Sensitive, "harness-both-pipes-drained");
                ack["subscriptions"].ShouldBe("0", "harness-subscriptions-disposed");
                AssertJournalDead(journal, "harness-cancel-cooperative");
            }
        }
        finally
        {
            Rescue(directory);
            TryDelete(directory);
        }
    }

    [Test]
    public async Task C578_cancellation_falls_back_to_outer_owned_tree_cleanup()
    {
        var nonce = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "c889-cancel-" + nonce);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "nonce"), nonce);
        var before = SnapshotOwnedDirectories();
        using var cancel = new CancellationTokenSource();
        var run = Start(directory, "before-ready", cancel.Token, ignoreCancel: true);
        try
        {
            await WaitForHoldingAsync(directory, nonce, "before-ready", run);
            cancel.Cancel();
            var error = await Should.ThrowAsync<OperationCanceledException>(async () => await run);
            error.CancellationToken.ShouldBe(cancel.Token, "harness-fallback-joins-owned-tree");
            var journal = File.ReadAllText(Path.Combine(directory, "identity.journal"));
            JournalNamesDistinctProcesses(journal).ShouldBeTrue("harness-fallback-joins-owned-tree");
            AssertJournalDead(journal, "harness-fallback-joins-owned-tree");
            File.ReadAllText(Path.Combine(directory, "phase.ack"))
                .Contains("cleanup=cooperative", StringComparison.Ordinal)
                .ShouldBeFalse("harness-fallback-joins-owned-tree");
            var diagnostic = error.Data["ScriptHarnessDiagnostics"] as string ?? "";
            diagnostic.ShouldContain("C578_BUILD_STARTED_" + nonce, Case.Sensitive, "harness-both-pipes-drained");
            diagnostic.ShouldContain("C578_BUILD_ERROR_" + nonce, Case.Sensitive, "harness-both-pipes-drained");
            diagnostic.Contains("retained results=", StringComparison.Ordinal).ShouldBeFalse("harness-fallback-joins-owned-tree");
            SnapshotOwnedDirectories().Except(before).ShouldBeEmpty("harness-fallback-joins-owned-tree");
        }
        finally
        {
            Rescue(directory);
            TryDelete(directory);
        }
    }

    [Test]
    public async Task C578_token_registration_publishes_a_nonce_cancel_file_once()
    {
        var nonce = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "c889-cancel-" + nonce);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "nonce"), nonce);
        var cancelPath = Path.Combine(directory, "cancel");
        try
        {
            using var first = new CancellationTokenSource();
            var registration = Register(first.Token, directory, nonce);
            File.Exists(cancelPath).ShouldBeFalse("harness-cancel-published");
            first.Cancel();
            File.ReadAllText(cancelPath).Trim().ShouldBe(nonce, "harness-cancel-published");
            Directory.GetFiles(directory, "cancel").Length.ShouldBe(1, "harness-cancel-published");
            using var second = new CancellationTokenSource();
            using var again = Register(second.Token, directory, Guid.NewGuid().ToString("N"));
            second.Cancel();
            File.ReadAllText(cancelPath).Trim().ShouldBe(nonce, "harness-cancel-published");
            registration.Dispose();
            again.Dispose();
        }
        finally
        {
            TryDelete(directory);
        }
        await Task.CompletedTask;
    }

    private static Task<ScriptHarnessResult> Start(string directory, string phase, CancellationToken token, bool ignoreCancel = false)
    {
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "test-run-checkpoint.ps1");
        var args = new List<string> { "-C889Descriptor", directory, "-C889HoldPhase", phase };
        if (ignoreCancel) args.Add("-C889IgnoreCancel");
        var options = ScriptHarnessOptions.Default with { AdditionalArguments = args };
        return ScriptHarnessProcess.RunAsync(
            "test-run-checkpoint.ps1", "C578", "C578_FailedBuildKeepsLogAndExit", script, options, token);
    }

    internal static CancellationTokenRegistration Register(CancellationToken token, string directory, string nonce)
    {
        var path = Path.Combine(directory, "cancel");
        return token.Register(() =>
        {
            if (File.Exists(path)) return;
            File.WriteAllText(path, nonce);
        });
    }

    private static async Task WaitForHoldingAsync(string directory, string nonce, string phase, Task run)
    {
        var ackPath = Path.Combine(directory, "phase.ack");
        var watch = Stopwatch.StartNew();
        using var watcher = new FileSystemWatcher(directory) { Filter = "phase.ack", EnableRaisingEvents = true };
        while (watch.Elapsed.TotalSeconds < GuardSeconds)
        {
            if (TryReadHolding(ackPath, nonce, phase)) return;
            if (run.IsCompleted) throw new InvalidOperationException("harness ended before parentObserved=holding\n" + await ReadFault(run));
            var signaled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            FileSystemEventHandler handler = (_, _) => signaled.TrySetResult();
            watcher.Created += handler;
            watcher.Changed += handler;
            try
            {
                if (TryReadHolding(ackPath, nonce, phase)) return;
                using var slice = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await signaled.Task.WaitAsync(slice.Token); }
                catch (OperationCanceledException) { }
            }
            finally
            {
                watcher.Created -= handler;
                watcher.Changed -= handler;
            }
        }
        throw new TimeoutException("holding ack absent");
    }

    private static bool TryReadHolding(string path, string nonce, string phase)
    {
        if (!File.Exists(path)) return false;
        var text = File.ReadAllText(path);
        return text.Contains("parentObserved=holding", StringComparison.Ordinal)
            && text.Contains("nonce=" + nonce, StringComparison.Ordinal)
            && text.Contains("phase=" + phase, StringComparison.Ordinal);
    }

    private static async Task<string> ReadFault(Task run)
    {
        try { await run; return ""; }
        catch (Exception ex) { return ex.ToString(); }
    }

    private static Dictionary<string, string> ReadAck(string directory)
    {
        var ack = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(Path.Combine(directory, "phase.ack")))
        {
            var split = line.Split('=', 2);
            if (split.Length == 2) ack[split[0]] = split[1];
        }
        return ack;
    }

    private static void AssertJournalDead(string journal, string message)
    {
        foreach (var identity in ParseJournal(journal))
            Executing(identity.Pid, identity.StartTicks).ShouldBeFalse(message);
    }

    private static bool JournalNamesDistinctProcesses(string journal)
    {
        var rows = ParseJournal(journal).ToList();
        var wrapper = rows.FirstOrDefault(row => row.Role == "wrapper");
        var shim = rows.FirstOrDefault(row => row.Role == "shim");
        return wrapper.Role == "wrapper" && shim.Role == "shim" && wrapper.Pid != shim.Pid;
    }

    private static IEnumerable<(string Role, int Pid, long StartTicks)> ParseJournal(string journal)
    {
        foreach (var line in journal.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            if (parts.Length == 3 && int.TryParse(parts[1], out var pid) && long.TryParse(parts[2], out var ticks))
                yield return (parts[0], pid, ticks);
        }
    }

    private static bool Executing(int pid, long startTicks)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited) return false;
            return Math.Abs(process.StartTime.ToUniversalTime().Ticks - startTicks) <= TimeSpan.TicksPerSecond;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void Rescue(string directory)
    {
        var journalPath = Path.Combine(directory, "identity.journal");
        if (!File.Exists(journalPath)) return;
        foreach (var identity in ParseJournal(File.ReadAllText(journalPath)))
        {
            if (!Executing(identity.Pid, identity.StartTicks)) continue;
            try
            {
                using var process = Process.GetProcessById(identity.Pid);
                process.Kill();
                process.WaitForExit(15000);
            }
            catch (ArgumentException) { }
        }
    }

    private static HashSet<string> SnapshotOwnedDirectories()
    {
        var root = Path.GetTempPath();
        return Directory.Exists(root)
            ? Directory.GetDirectories(root)
                .Select(Path.GetFileName)
                .Where(name => name != null && (name.StartsWith("c578-nightly-", StringComparison.Ordinal) || name.StartsWith("antiphon-script-control-", StringComparison.Ordinal)))
                .Select(name => name!)
                .ToHashSet(StringComparer.Ordinal)
            : [];
    }

    private static void TryDelete(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        catch (IOException) { }
    }
}
