using Antiphon.Checkpoints.Coverage;
using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints;

// Output and confinement content stamps can include bundled native executables.
// Hash at most 1 GiB through a verified handle using a fixed 64 KiB buffer. The
// receipt reader keeps its independent 64 MiB cap; the three-second deadline and
// regular-file, single-link, no-follow and containment checks are shared unchanged.
internal static class OutputContent
{
    public const int Limit = 1024 * 1024 * 1024;

    public static (long Length, string Sha256) Hash(CheckpointCleanupContext context, string path)
    {
        try
        {
            return ConfinedFileReader.Hash(context, Path.GetDirectoryName(Path.GetFullPath(path))!, path, Limit);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException)
        {
            throw new IOException("output-read-refused: " + path + ": " + ex.Message, ex);
        }
    }
}
