using System.Diagnostics;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;

namespace Antiphon.Tests.Checkpoints;

// CARD-1199 review 6b8368dc F1: eight repaired fixture sites run here: OutputCleanupTests.Git and
// its parent-symlink and wrapper-preflight pwsh probes, CheckpointTaskOwnershipTests ownership-loss
// git init, CheckpointOutputConfinementTests.Git and SeedBuild, CheckpointShapeConflictTests.GitOut/Git
// and CheckpointTrackedBaselineTests.GitOut/Git. Other fixture launches and their gaps are in the
// repair 13k census of docs/superpowers/plans/2026-10-13-card-1199-inherited-readers.md.
// The shared CheckpointProcessRunner applies the caller's limit to process exit and both output
// drains together and, on Linux, kills the child's process group on expiry, so a child blocked
// before exit or a descendant holding a pipe fails the fixture step (CP-23). Git hardening is off:
// these sites keep the installed Git behaviour.
internal static class CheckpointFixtureProcess
{
    public sealed record Output(int Exit, string Stdout, string Stderr);

    public static Output Run(ProcessStartInfo start, int timeoutMs) =>
        Run(start, timeoutMs, CancellationToken.None);

    public static Output Run(ProcessStartInfo start, int timeoutMs, CancellationToken cancellationToken)
    {
        var name = start.FileName;
        // CARD-1620: one process record per fixture child (git, dotnet, pwsh) so a slow row
        // attributes its spawn-to-drain time instead of leaving it as unassigned remainder.
        using var process = TestTiming.BeginProcess(Path.GetFileName(name), start.ArgumentList.Count, "checkpoint-fixture");
        try
        {
            var result = CheckpointProcessRunner.Run(start, timeoutMs, git: false, cancellationToken);
            if (result.TimedOut)
                throw new TimeoutException(name + " fixture step exceeded " + timeoutMs + " ms (exit and output drains)");
            return new Output(result.Exit, result.Stdout, result.Stderr);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
    }

    // C1233_TokenCallers: preserve each caller's token deadline across exit, drains and custody.
    public static Task<Output> RunAsync(ProcessStartInfo start, CancellationToken cancellationToken) =>
        Task.Run(() => Run(start, Timeout.Infinite, cancellationToken), cancellationToken);

    // rc run 97150a0b g109: a child that loops over a known list of steps appends one byte to
    // progressPath per finished step. It gets `startup` to finish its first step (process start,
    // script load and that step) and then `perStep` between consecutive steps, instead of one fixed
    // deadline over the whole loop, whose length scales with host load. Expiry names the phase.
    public static async Task<Output> RunStepsAsync(ProcessStartInfo start, string progressPath,
        IReadOnlyList<string> steps, TimeSpan startup, TimeSpan perStep, TimeProvider? time = null)
    {
        var deadline = new CheckpointStepDeadline(steps.Count, startup, perStep, time ?? TimeProvider.System);
        using var cancel = new CancellationTokenSource();
        var run = RunAsync(start, cancel.Token);
        while (!run.IsCompleted)
        {
            await Task.WhenAny(run, Task.Delay(200)).ConfigureAwait(false);
            var progress = new FileInfo(progressPath);
            deadline.Observe(progress.Exists ? (int)Math.Min(progress.Length, int.MaxValue) : 0);
            if (!run.IsCompleted && deadline.Expired)
            {
                cancel.Cancel();
                break;
            }
        }
        try
        {
            var output = await run.ConfigureAwait(false);
            Console.WriteLine(Path.GetFileName(start.FileName) + " step child: " + deadline.Describe(steps));
            return output;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw new TimeoutException(Path.GetFileName(start.FileName) + " fixture child " + deadline.Describe(steps));
        }
    }

    // Installed Git in a scratch repository; exit 0 is required.
    public static string Git(string root, params string[] args) =>
        Git(root, environment: null, args);

    // Extra child variables are opt-in. A null environment keeps the historical three removals only.
    public static string Git(string root, IReadOnlyDictionary<string, string>? environment, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root };
        start.Environment.Remove("GIT_DIR");
        start.Environment.Remove("GIT_WORK_TREE");
        start.Environment.Remove("GIT_INDEX_FILE");
        if (environment is not null)
        {
            foreach (var pair in environment)
                start.Environment[pair.Key] = pair.Value;
        }
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        var output = Run(start, 30_000);
        output.Exit.ShouldBe(0, output.Stdout + output.Stderr);
        return output.Stdout;
    }
}
