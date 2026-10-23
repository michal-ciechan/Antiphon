using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints.Coverage;

// Compatibility facade; the verified handle reader has one library owner.
public static class ConfinedFileReader
{
    public const int DocumentLimit = CheckpointFileReader.DocumentLimit;
    public const int SourceLimit = CheckpointFileReader.SourceLimit;
    public const long ContentLimit = CheckpointFileReader.ContentLimit;
    public static (long Length, string Sha256) Digest(string root, string path, long limit, CancellationToken cancellationToken = default) =>
        CheckpointFileReader.Digest(CheckpointCleanupDefaults.Context, root, path, limit, cancellationToken);
    public static string? Refusal(bool isRegularFile, ulong linkCount, string resolvedPath, string root) =>
        CheckpointFileReader.Refusal(isRegularFile, linkCount, resolvedPath, root);
    public static string? Refusal(CheckpointCleanupContext context, bool isRegularFile, ulong linkCount, string resolvedPath, string root) =>
        CheckpointFileReader.Refusal(context, isRegularFile, linkCount, resolvedPath, root);
    public static string Read(string root, string path, int limit, Action? afterVerification = null) =>
        CheckpointFileReader.Read(root, path, limit, afterVerification);
    public static string Read(string root, string path, int limit, out int byteCount, Action? afterVerification = null) =>
        CheckpointFileReader.Read(root, path, limit, out byteCount, afterVerification);
    public static byte[] ReadBytes(string root, string path, int limit, Action? afterVerification = null, CancellationToken cancellationToken = default) =>
        CheckpointFileReader.ReadBytes(root, path, limit, afterVerification, cancellationToken);
    public static byte[] ReadBytes(CheckpointCleanupContext context, string root, string path, int limit,
        Action? afterVerification = null, CancellationToken cancellationToken = default) =>
        CheckpointFileReader.ReadBytes(context, root, path, limit, afterVerification, cancellationToken);
}
