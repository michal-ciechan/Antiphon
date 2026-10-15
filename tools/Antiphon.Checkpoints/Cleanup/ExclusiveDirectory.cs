using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints;

public static class ExclusiveDirectory
{
    public static string? Create(string path) => Create(CheckpointCleanupDefaults.Context, path);
    public static string? Create(CheckpointCleanupContext context, string path)
    {
        var error = context.Io.CreateExclusiveDirectory(path);
        return error == 0 ? null : error is 17 or 80 or 183 ? "pre-existing-output" : "output-create-failed:" + error;
    }
}
