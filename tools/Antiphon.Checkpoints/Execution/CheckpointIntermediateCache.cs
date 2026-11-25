using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints;

// Cache authority is independent of the disposable output ledger. A matching partition is
// reusable only after a clean build sealed its contents. The gate covers all builds and hosts
// in the executor, including dotnet run's evaluation of the project's restored assets.
internal sealed class CheckpointIntermediateCache(string root, SourceObservation source, FileStream gate, Action<string>? refuse = null) : IDisposable
{
    internal const string RootProperty = "AntiphonCheckpointIntermediateRoot";
    // Generated beside the cache. A tracked props file that sets BaseIntermediateOutputPath
    // is refused by the land-graph pre-check, including when nothing imports it.
    internal const string ProjectProps = ".antiphon/checkpoint-obj/CheckpointIntermediates.props";
    internal const string PropsText = """
        <Project>
          <!-- Generated into the untracked worktree cache. Imported before the SDK selects its artifacts obj paths.
               Each project (including nested apphost builds) selects its own path in the run's partition. -->
          <PropertyGroup Condition="'$(AntiphonCheckpointIntermediateRoot)' != ''">
            <_CheckpointRelativeProject>$([MSBuild]::MakeRelative('$(AntiphonCheckpointRepositoryRoot)', '$(MSBuildProjectFullPath)'))</_CheckpointRelativeProject>
            <_CheckpointUseAppHost>true</_CheckpointUseAppHost>
            <_CheckpointUseAppHost Condition="'$(UseAppHost)' == 'false'">false</_CheckpointUseAppHost>
            <BaseIntermediateOutputPath Condition="!$([System.IO.Path]::IsPathRooted('$(_CheckpointRelativeProject)')) And !$(_CheckpointRelativeProject.StartsWith('..'))">$(AntiphonCheckpointIntermediateRoot)/$(_CheckpointRelativeProject)/apphost-$(_CheckpointUseAppHost)/</BaseIntermediateOutputPath>
          </PropertyGroup>
        </Project>
        """;
    private static readonly HashSet<string> LayoutOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        "ArtifactsPath", "UseArtifactsOutput", "UseArtifactsIntermediateOutput", "BaseIntermediateOutputPath",
        "IntermediateOutputPath", "MSBuildProjectExtensionsPath", "CustomBeforeDirectoryBuildProps",
        "CustomAfterDirectoryBuildProps", RootProperty, "AntiphonCheckpointRepositoryRoot",
    };

    internal static CheckpointIntermediateCache? TryOpen(string root, SourceObservation source, string? expected, Action<string>? refuse = null)
    {
        if (expected is null || source.Commit != expected || source.CaptureStatus != "known"
            || source.DirtyFiles != 0 || string.IsNullOrEmpty(source.Fingerprint))
            return null;
        FileStream? stream = null;
        try
        {
            var home = Home(root);
            EnsureCacheDirectory(home, root);
            var lockPath = Path.Combine(home, "run.lock");
            // The lock name is never replaced. An untrusted path is left in place and this run builds cold.
            var refusal = TrustedPath(lockPath, out var absent);
            if (refusal is not null)
            {
                Note(refuse, refusal);
                return null;
            }

            if (absent && CreateBlank(lockPath, root) is string created)
            {
                Note(refuse, created);
                return null;
            }

            stream = OpenExisting(lockPath);
            if (CheckpointNativeIo.Untrusted(stream.SafeFileHandle) is string handleRefusal)
            {
                Note(refuse, handleRefusal);
                return null;
            }

            if (!WriteProps(root, refuse))
                return null;
            var opened = new CheckpointIntermediateCache(root, source, stream, refuse);
            stream = null;
            return opened;
        }
        catch (CheckpointCacheRefusal)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Note(refuse, ex.GetType().Name);
            return null; // A concurrent run or an unavailable cache can build in its fresh output.
        }
        finally
        {
            stream?.Dispose();
        }
    }

    internal void Report(Exception ex) =>
        Note(refuse, ex is CheckpointCacheRefusal refusal ? refusal.Message : ex.GetType().Name);

    private static bool WriteProps(string root, Action<string>? refuse)
    {
        try
        {
            PublishNewBytes(root, Path.Combine(root, ProjectProps), Encoding.UTF8.GetBytes(PropsText));
            return true;
        }
        catch (CheckpointCacheRefusal)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Note(refuse, ex.GetType().Name);
            return false;
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
        FileStream? seal = null;
        try
        {
            var preflight = SafeWrite.Preflight(root, [path, sealPath]);
            if (preflight == "link-component")
                throw new CheckpointCacheRefusal(preflight);
            if (preflight is not null)
                return null;
            // A FIFO or other non-regular seal is refused before open. Opening it for read can block.
            var refusal = TrustedPath(sealPath, out var absent);
            if (refusal is not null)
            {
                Note(refuse, refusal);
                return null;
            }

            if (absent && CreateBlank(sealPath, root) is string created)
            {
                Note(refuse, created);
                return null;
            }

            seal = OpenExisting(sealPath);
            if (CheckpointNativeIo.Untrusted(seal.SafeFileHandle) is string handleRefusal)
            {
                Note(refuse, handleRefusal);
                return null;
            }

            if (Directory.Exists(path))
            {
                if (!MatchesSeal(seal, path))
                    return null;
            }
            else if (seal.Length != 0 || SafeWrite.CreateFreshDirectory(path, SafeWrite.Under(root)) is not null)
            {
                return null;
            }

            var entry = new Entry(path, seal);
            seal = null;
            return entry;
        }
        catch (CheckpointCacheRefusal)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Note(refuse, ex.GetType().Name);
            return null;
        }
        finally
        {
            seal?.Dispose();
        }
    }

    private static void Note(Action<string>? refuse, string reason) =>
        refuse?.Invoke("checkpoint cache refused: " + reason);

    // Absent is safe to create. Anything else must already be one regular link owned by this process.
    private static string? TrustedPath(string path, out bool absent)
    {
        absent = false;
        var raw = CheckpointCleanupDefaults.Context.Io.Stat(path);
        if (raw.Kind == "absent")
        {
            absent = true;
            return null;
        }

        if (OutputCustody.Stat(path) is not { } stat)
            return "unreadable-link-count";
        if (stat.IsDirectory)
            return "directory";
        if (!stat.IsRegular)
            return "not-regular";
        if (stat.Links != 1)
            return "multiple-links";
        if (stat.Owner != OutputCustody.Owner)
            return "foreign-owner";
        return null;
    }

    private static string? CreateBlank(string path, string root)
    {
        var reason = SafeWrite.OpenCreate(path, SafeWrite.Under(root), out var blank);
        if (reason == "link-component")
            throw new CheckpointCacheRefusal(reason);
        if (reason is not null || blank is null)
            return reason ?? "unopened";
        blank.Dispose();
        return null;
    }

    private static FileStream OpenExisting(string path)
    {
        var opened = CheckpointCleanupDefaults.Context.Io.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (opened is FileStream file)
            return file;
        opened.Dispose();
        throw new IOException("cache write refused: unopened");
    }

    private static void EnsureCacheDirectory(string directory, string root)
    {
        var reason = SafeWrite.CreateDirectory(directory, SafeWrite.Under(root));
        if (reason is not null)
            throw new CheckpointCacheRefusal(reason);
        if (OutputCustody.Stat(directory) is not { IsDirectory: true } stat)
            throw new CheckpointCacheRefusal("not-directory");
        if (stat.Owner != OutputCustody.Owner)
            throw new CheckpointCacheRefusal("foreign-owner");
    }

    private static void RefuseReplaceable(string root, string path)
    {
        var reason = SafeWrite.Refusal(path, SafeWrite.Under(root));
        if (reason == "link-component")
            throw new CheckpointCacheRefusal(reason);
        if (reason is not null && reason != "unrecorded-existing")
            throw new IOException("cache write refused: " + reason);
        if (IsRealDirectory(path))
            throw new CheckpointCacheRefusal("directory");
        if (File.Exists(path) && OutputCustody.Stat(path) is null)
            throw new CheckpointCacheRefusal("unreadable-link-count");
    }

    private static bool IsRealDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        if (!info.Exists || info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            return false;
        return (info.Attributes & FileAttributes.Directory) != 0;
    }

    private static bool SameSingleLink(string path, ReadOnlySpan<byte> payload)
    {
        if (OutputCustody.Stat(path) is not { IsRegular: true, Links: 1, Size: var size } || size != (ulong)payload.Length)
            return false;
        return File.ReadAllBytes(path).AsSpan().SequenceEqual(payload);
    }

    // Exclusive create plus rename replaces the directory entry. Another hard link keeps the old inode.
    private static void PublishNewBytes(string root, string destination, ReadOnlySpan<byte> payload)
    {
        var parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(parent))
            throw new IOException("cache write refused: path-unsafe");
        EnsureCacheDirectory(parent, root);
        RefuseReplaceable(root, destination);
        if (SameSingleLink(destination, payload))
            return;

        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        FileStream? stream = null;
        try
        {
            var reason = SafeWrite.OpenCreate(temporary, SafeWrite.Under(root), out stream);
            if (reason == "link-component")
                throw new CheckpointCacheRefusal(reason);
            if (reason is not null || stream is null)
                throw new IOException("cache write refused: " + (reason ?? "unopened"));
            stream.Write(payload);
            stream.Flush(flushToDisk: true);
            stream.Dispose();
            stream = null;
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            stream?.Dispose();
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
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
        private FileStream? held = seal;

        internal string Path { get; } = path;

        internal void Invalidate() => Replace(ReadOnlySpan<byte>.Empty);

        internal void Publish()
        {
            try
            {
                Replace(Digest(Path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CheckpointCacheRefusal)
            {
                // The already qualified owned build remains usable. An incomplete seal never
                // authorizes a later build to reuse this partition.
            }
        }

        // The handle was fstat'd before use. Rewriting it cannot follow a later directory entry.
        private void Replace(ReadOnlySpan<byte> payload)
        {
            if (held is null)
                throw new IOException("cache write refused: seal-closed");
            held.Position = 0;
            held.SetLength(0);
            held.Write(payload);
            held.Flush(flushToDisk: true);
        }

        public void Dispose()
        {
            held?.Dispose();
            held = null;
        }
    }
}

internal sealed class CheckpointCacheRefusal(string code) : Exception(code);
