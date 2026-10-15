using Antiphon.HostCleanup.Checkpoints;
namespace Antiphon.Checkpoints;

public static class RepoPaths
{
    public static string FindRoot(string? start = null, CheckpointCleanupContext? context = null)
    {
        context ??= CheckpointCleanupDefaults.Context;
        var dir = new DirectoryInfo(Path.GetFullPath(start ?? context.Io.CurrentDirectory));
        while (dir is not null)
        {
            if (context.Io.FileExists(Path.Combine(dir.FullName, "Antiphon.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new ManifestValidationException("repoRoot", "could not find Antiphon.sln walking up from " + (start ?? context.Io.CurrentDirectory));
    }

    public static string RunId(CheckpointCleanupContext? context = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..4];
        return (context ?? CheckpointCleanupDefaults.Context).Time.GetLocalNow().ToString("yyyyMMdd-HHmmss") + "-" + suffix;
    }
}
