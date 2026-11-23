using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// The recover-repository-children command (Antiphon.Nightly.RepositoryChildRecovery, which replaced
/// scripts/recover-repository-children.ps1 in unit 23 of the 2026-11-22 plan) run in this process as the CLI runs it: real git,
/// the real file system and the process identity read now. A test may replace git (the Linux alias case). Nothing launches a
/// script; the recovery reads process start times in this process, so a record seeded here compares against the same reader.
/// </summary>
internal static class RepositoryRecoveryCommand
{
    internal static (int Exit, string Output, string Error) Run(string repository, params string[] options) =>
        RunWithGit(repository, NightlyNativeGit.Run, options);

    internal static (int Exit, string Output, string Error) RunWithGit(string repository, NightlyGit git, params string[] options)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new NightlyHost
        {
            Clock = TimeProvider.System,
            Git = git,
            Launcher = new RecordingLauncher(),
            Processes = new NightlyProcessTable(),
            Probe = new NightlyHostProbe(),
            Out = output,
            Error = error,
            Trace = TextWriter.Null,
        };
        var exit = RepositoryChildRecovery.Run(RepositoryChildRecoveryOptions.Parse(["--repository", repository, .. options]), host,
            new System.IO.Abstractions.FileSystem(), RepositoryChildRecovery.Identify);
        (int Exit, string Output, string Error) result = (exit, output.ToString(), error.ToString());
        Console.WriteLine($"C452_RECOVERY exit={result.Exit} stdout={result.Output} stderr={result.Error}");
        return result;
    }

    /// <summary>A process's start identity as the recovery reads it (in this process).</summary>
    internal static long StartTicks(int processId)
    {
        using var process = System.Diagnostics.Process.GetProcessById(processId);
        return process.StartTime.ToUniversalTime().Ticks;
    }
}
