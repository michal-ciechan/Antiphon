using System.Diagnostics;
using Shouldly;

namespace Antiphon.Tests.Checkpoints;

// CARD-1199 review 6b8368dc F1: the CARD-added fixture children that capture output run here,
// so one stated limit covers them.
internal static class CheckpointFixtureProcess
{
    public sealed record Output(int Exit, string Stdout, string Stderr);

    public static Output Run(ProcessStartInfo start, int timeoutMs)
    {
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;
        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(timeoutMs).ShouldBeTrue(stdout + stderr);
        return new Output(process.ExitCode, stdout, stderr);
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
