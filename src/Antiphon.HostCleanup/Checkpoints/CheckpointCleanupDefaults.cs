using System.Diagnostics;
using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints;

// One immutable composition per process. A fresh explicit context has its own registry.
public static class CheckpointCleanupDefaults
{
    public static CheckpointCleanupContext Context { get; } = new(new CheckpointNativeIo(), new CheckpointNativeGitRunner());
}

public sealed class CheckpointNativeGitRunner : ICheckpointGitRunner
{
    private readonly Func<ProcessStartInfo, int, bool, CheckpointProcessRunner.Result> _run;

    public CheckpointNativeGitRunner(Func<ProcessStartInfo, int, bool, CheckpointProcessRunner.Result>? run = null) =>
        _run = run ?? ((start, timeout, git) => CheckpointProcessRunner.Run(start, timeout, git: git));

    public CheckpointGitResult Run(CheckpointGitRequest request)
    {
        var start = new ProcessStartInfo(request.Executable) { WorkingDirectory = request.WorkingDirectory };
        foreach (var argument in request.Arguments) start.ArgumentList.Add(argument);
        var result = _run(start, request.TimeoutMilliseconds, true);
        return new(result.Exit, result.Stdout, result.Stderr, result.TimedOut);
    }
}

// ExclusiveDirectory.cs is also compiled alone by the PowerShell native facade.
public static partial class ExclusiveDirectory
{
    public static string? Create(CheckpointCleanupContext context, string path)
    {
        var error = context.Io.CreateExclusiveDirectory(path);
        return error == 0 ? null : error is 17 or 80 or 183 ? "pre-existing-output" : "output-create-failed:" + error;
    }
}
