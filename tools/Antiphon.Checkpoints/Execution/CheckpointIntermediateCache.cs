using System.Security.Cryptography;
using System.Text.Json;

namespace Antiphon.Checkpoints;

// Cache authority is independent of the disposable output ledger. A matching partition is
// reusable only after a clean build sealed its contents. The gate covers all builds and hosts
// in the executor, including dotnet run's evaluation of the project's restored assets.
internal sealed class CheckpointIntermediateCache(string root, SourceObservation source, FileStream gate) : IDisposable
{
    internal const string RootProperty = "AntiphonCheckpointIntermediateRoot";
    internal const string ProjectProps = "tools/Antiphon.Checkpoints/CheckpointIntermediates.props";
    internal const string GraphProject = "tools/Antiphon.Checkpoints/CheckpointBuildGraph.proj";
    private static readonly HashSet<string> LayoutOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        "ArtifactsPath", "UseArtifactsOutput", "UseArtifactsIntermediateOutput", "BaseIntermediateOutputPath",
        "IntermediateOutputPath", "MSBuildProjectExtensionsPath", "CustomBeforeDirectoryBuildProps",
        "CustomAfterDirectoryBuildProps", RootProperty, "AntiphonCheckpointRepositoryRoot",
        "CheckpointGraphEntry", "CheckpointGraphWalkCap",
    };

    internal static CheckpointIntermediateCache? TryOpen(string root, SourceObservation source, string? expected)
    {
        if (expected is null || source.Commit != expected || source.CaptureStatus != "known"
            || source.DirtyFiles != 0 || string.IsNullOrEmpty(source.Fingerprint)
            || !File.Exists(Path.Combine(root, ProjectProps)) || !File.Exists(Path.Combine(root, GraphProject)))
            return null;
        try
        {
            var home = Home(root);
            return SafeWrite.OpenGate(Path.Combine(home, "run.lock"), SafeWrite.Under(root), out var stream) is null
                && stream is not null ? new(root, source, stream) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // A concurrent run or an unavailable cache can build in its fresh output.
        }
    }

    public void Dispose() => gate.Dispose();

    private static string Home(string root) => Path.Combine(root, ".antiphon", "checkpoint-obj", "obj");

    internal static string PathFor(string root, string project, IReadOnlyList<string> properties, SourceObservation source)
    {
        var fullProject = Path.GetFullPath(project, root);
        if (ProjectWriteRoots.TryFindProjectFile(fullProject, out var file, out _))
            fullProject = file!;
        var keys = properties.Select(token =>
        {
            var equals = token.IndexOf('=');
            return token[..equals].ToUpperInvariant() + token[equals..];
        }).Order(StringComparer.Ordinal).ToArray();
        var path = Path.GetRelativePath(root, fullProject).Replace(Path.DirectorySeparatorChar, '/');
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { path, keys, source.Commit, source.Fingerprint });
        return Path.Combine(Home(root), Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant());
    }

    internal Entry? Prepare(string project, IReadOnlyList<string> properties)
    {
        if (OverridesLayout(properties))
            return null;
        var path = PathFor(root, project, properties, source);
        var sealPath = path + ".seal";
        if (SafeWrite.Preflight(root, [path, sealPath]) is not null
            || File.Exists(sealPath) && OutputCustody.Stat(sealPath) is not { IsRegular: true, Links: 1 })
            return null;
        FileStream? seal = null;
        try
        {
            if (SafeWrite.OpenGate(sealPath, SafeWrite.Under(root), out seal) is not null || seal is null)
                return null;
            var ready = Directory.Exists(path)
                ? MatchesSeal(seal, path)
                : seal.Length == 0 && SafeWrite.CreateFreshDirectory(path, SafeWrite.Under(root)) is null;
            if (!ready)
                return null;

            var entry = new Entry(path, seal);
            seal = null;
            return entry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            seal?.Dispose();
        }
    }

    private static bool OverridesLayout(IReadOnlyList<string> properties)
    {
        foreach (var token in properties)
        {
            foreach (var assignment in token["--property:".Length..].Split(';', ','))
            {
                var equals = assignment.IndexOf('=');
                if (equals > 0 && LayoutOverrides.Contains(assignment[..equals].Trim()))
                    return true;
            }
        }
        return false;
    }

    private static bool MatchesSeal(FileStream seal, string path)
    {
        Span<byte> recorded = stackalloc byte[32];
        if (seal.Length != recorded.Length)
            return false;
        seal.ReadExactly(recorded);
        return recorded.SequenceEqual(Digest(path));
    }

    private static byte[] Digest(string root)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        while (pending.TryPop(out var directory))
        {
            foreach (var item in directory.EnumerateFileSystemInfos())
            {
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("linked checkpoint intermediate");
                if (item is DirectoryInfo child)
                    pending.Push(child);
                else
                    files.Add(Path.GetRelativePath(root, item.FullName), ReceiptFile.Digest(CheckpointCleanupDefaults.Context, item.FullName).Sha256);
            }
        }
        return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(files));
    }

    internal sealed class Entry(string path, FileStream seal) : IDisposable
    {
        internal string Path { get; } = path;

        internal void Invalidate()
        {
            seal.SetLength(0);
            seal.Flush(flushToDisk: true);
        }

        internal void Publish()
        {
            try
            {
                var digest = Digest(Path);
                seal.Position = 0;
                seal.Write(digest);
                seal.Flush(flushToDisk: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The already qualified owned build remains usable. An incomplete seal never
                // authorizes a later build to reuse this partition.
            }
        }

        public void Dispose() => seal.Dispose();
    }
}
