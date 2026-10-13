using System.Diagnostics;
using Antiphon.Checkpoints;
using Shouldly;

namespace Antiphon.Tests.Checkpoints;

// CARD-1199 review 6b8368dc F1: the CARD-added fixture children that capture output run here,
// so one stated limit covers them. The shared CheckpointProcessRunner applies that limit to
// process exit and both output drains together and, on Linux, kills the child's process group on
// expiry, so a child blocked before exit or a descendant holding a pipe fails the fixture step
// instead of hanging it. Git hardening is off: fixtures keep the installed Git behaviour.
internal static class CheckpointFixtureProcess
{
    public sealed record Output(int Exit, string Stdout, string Stderr);

    public static Output Run(ProcessStartInfo start, int timeoutMs)
    {
        var name = start.FileName;
        var result = CheckpointProcessRunner.Run(start, timeoutMs, git: false);
        if (result.TimedOut)
            throw new TimeoutException(name + " fixture step exceeded " + timeoutMs + " ms (exit and output drains)");
        return new Output(result.Exit, result.Stdout, result.Stderr);
    }

    // Installed Git in a scratch repository; exit 0 is required.
    public static string Git(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root };
        start.Environment.Remove("GIT_DIR");
        start.Environment.Remove("GIT_WORK_TREE");
        start.Environment.Remove("GIT_INDEX_FILE");
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        var output = Run(start, 30_000);
        output.Exit.ShouldBe(0, output.Stdout + output.Stderr);
        return output.Stdout;
    }
}
