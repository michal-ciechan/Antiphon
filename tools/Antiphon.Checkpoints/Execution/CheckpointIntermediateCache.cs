using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints;

// Cache authority is independent of the disposable output ledger. A matching partition is
// reusable only after a clean build sealed its contents. The gate covers all builds and hosts
// in the executor, including dotnet run's evaluation of the project's restored assets.
internal sealed class CheckpointIntermediateCache(string root, SourceObservation source, FileStream gate,
    Action<string>? refuse, CheckpointIntermediateCache.SealIo sealIo) : IDisposable
{
    private FileStream? heldGate = gate;
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

    internal static CheckpointIntermediateCache? TryOpen(string root, SourceObservation source, string? expected,
        Action<string>? refuse = null, SealIo? sealIo = null, bool replay = false,
        CancellationToken cancellationToken = default)
    {
        if (expected is null || source.Commit != expected || source.CaptureStatus != "known"
            || source.DirtyFiles != 0 || string.IsNullOrEmpty(source.Fingerprint))
            return null;
        FileStream? stream = null;
        try
        {
            var home = Home(root);
            var lockPath = Path.Combine(home, "run.lock");
            if (replay)
            {
                if (SafeWrite.Preflight(root, [lockPath]) is not null)
                    return null;
            }
            else
            {
                EnsureCacheDirectory(home, root);
            }
            // The lock name is never replaced. An untrusted path is left in place and this run builds cold.
            var refusal = TrustedPath(lockPath, out var absent);
            if (refusal is not null)
            {
                Note(refuse, refusal);
                return null;
            }

            if (absent && replay)
                return null;
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

            if (replay ? !CheckReplayProps(root, cancellationToken) : !WriteProps(root, refuse))
                return null;
            var opened = new CheckpointIntermediateCache(root, source, stream, refuse, sealIo ?? new SealIo());
            stream = null;
            return opened;
        }
        catch (CheckpointCacheRefusal) when (!replay)
        {
            throw;
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            Note(refuse, ex.GetType().Name);
            return null; // A concurrent run or an unavailable cache can build in its fresh output.
        }
        finally
        {
            CloseGate(stream, refuse);
        }
    }

    private static bool CheckReplayProps(string root, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, ProjectProps);
        var expected = Encoding.UTF8.GetBytes(PropsText);
        return SafeWrite.Preflight(root, [path]) is null
            && TrustedPath(path, out var absent) is null && !absent
            && OutputCustody.Stat(path)?.Size == (ulong)expected.Length
            && ReceiptFile.ReadBytes(path, cancellationToken).AsSpan().SequenceEqual(expected);
    }

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
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            Note(refuse, ex.GetType().Name);
            return false;
        }
    }

    public void Dispose() => CloseGate(Interlocked.Exchange(ref heldGate, null), refuse);

    private static void CloseGate(FileStream? stream, Action<string>? refuse)
    {
        try { stream?.Dispose(); }
        catch (Exception ex) when (IsCacheFailure(ex)) { Note(refuse, "gate-close: " + ex.GetType().Name); }
    }

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

    internal Entry? Prepare(string project, IReadOnlyList<string> properties, IReadOnlySet<string>? inUse = null,
        bool requireSealed = false)
    {
        if (OverridesLayout(properties))
            return null;

        try
        {
            var path = PathFor(root, project, properties, source);
            if (inUse?.Contains(path) == true)
                return null;
            var sealPath = path + ".seal";
            CheckPaths(path, sealPath);
            var partition = ReadKind(path, "directory");
            var sealEntry = ReadKind(sealPath, "file");
            if (partition.Kind == "absent" && sealEntry.Kind == "absent")
                return requireSealed ? null : new Entry(this, path, default, default);
            if (partition.Kind == "absent" || sealEntry.Kind == "absent")
                throw new CheckpointCacheRefusal("partial-partition");

            var sealIdentity = Identity(sealEntry);
            using var seal = OpenExisting(sealPath);
            if (!RequireTrustedHandle(seal).SameAs(sealIdentity))
                throw new CheckpointCacheRefusal("seal-identity-changed");
            if (!MatchesSeal(seal, path))
                throw new CheckpointCacheRefusal("invalid-seal");

            return new Entry(this, path, Identity(partition), sealIdentity);
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            Report("prepare", ex);
            return null;
        }
    }

    internal static bool IsCacheFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ObjectDisposedException or CheckpointCacheRefusal;

    private void Report(string phase, Exception ex) =>
        Note(refuse, phase + ": " + (ex is CheckpointCacheRefusal refusal ? refusal.Message : ex.GetType().Name));

    private static void Note(Action<string>? refuse, string reason)
    {
        try { refuse?.Invoke("checkpoint cache refused: " + reason); }
        catch (Exception ex) when (IsCacheFailure(ex)) { }
    }

    private void CheckPaths(params string[] paths)
    {
        if (SafeWrite.Preflight(root, paths) is string reason)
            throw new CheckpointCacheRefusal(reason);
    }

    private static FilesystemIdentity Identity(CheckpointStat stat) => new(stat.Device, stat.Inode, stat.Created);

    private static CheckpointStat ReadKind(string path, string kind)
    {
        var stat = CheckpointCleanupDefaults.Context.Io.CustodyStat(path);
        if (stat.Kind == "absent")
            return stat;
        if (stat.Kind != kind || !Identity(stat).IsBound)
            throw new CheckpointCacheRefusal(kind == "file" ? "untrusted-seal" : "untrusted-partition");
        if (stat.Owner != OutputCustody.Owner)
            throw new CheckpointCacheRefusal("foreign-owner");
        if (kind == "file" && stat.Links != 1)
            throw new CheckpointCacheRefusal("multiple-links");
        return stat;
    }

    private static FilesystemIdentity RequireTrustedHandle(FileStream stream)
    {
        if (CheckpointNativeIo.Untrusted(stream.SafeFileHandle, out var identity) is string reason)
            throw new CheckpointCacheRefusal(reason);
        return identity;
    }

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

    internal static bool OverridesLayout(IReadOnlyList<string> properties)
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

    internal class SealIo
    {
        internal virtual void Write(FileStream stream, ReadOnlySpan<byte> payload) => stream.Write(payload);
        internal virtual void Flush(FileStream stream) => stream.Flush(flushToDisk: true);
        internal virtual void Close(FileStream stream) => stream.Dispose();
        internal virtual void Replace(string temporary, string destination) =>
            CheckpointNativeIo.ReplaceSameVolumeFile(temporary, destination);
    }

    // Only the attempt that installed the invalid seal may publish from it.
    internal sealed class Entry(CheckpointIntermediateCache cache, string path,
        FilesystemIdentity partition, FilesystemIdentity originalSeal)
    {
        private FilesystemIdentity ownedPartition = partition;
        private FilesystemIdentity invalidSeal;

        internal string Path { get; } = path;
        private string SealPath => Path + ".seal";

        internal bool Invalidate(CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (originalSeal.IsBound)
                {
                    invalidSeal = cache.ReplaceSeal(this, originalSeal, 32, ReadOnlySpan<byte>.Empty, cancellationToken);
                }
                else
                {
                    cache.CheckPaths(Path, SealPath);
                    if (ReadKind(Path, "directory").Kind != "absent" || ReadKind(SealPath, "file").Kind != "absent")
                        throw new CheckpointCacheRefusal("partition-already-present");
                    if (SafeWrite.CreateFreshDirectory(Path, SafeWrite.Under(cache.Root)) is string reason)
                        throw new CheckpointCacheRefusal(reason);
                    ownedPartition = Identity(ReadKind(Path, "directory"));
                    if (!ownedPartition.IsBound)
                        throw new CheckpointCacheRefusal("unbound-partition");
                    invalidSeal = cache.WriteSeal(SealPath, ReadOnlySpan<byte>.Empty, out _);
                }

                return true;
            }
            catch (Exception ex) when (IsCacheFailure(ex))
            {
                cache.Report("invalidate", ex);
                return false;
            }
        }

        internal void Publish(CancellationToken cancellationToken = default)
        {
            var expected = invalidSeal;
            invalidSeal = default;
            if (!expected.IsBound || cancellationToken.IsCancellationRequested)
                return;
            try
            {
                cache.CheckPaths(Path, SealPath);
                RequirePartition();
                var digest = Digest(Path);
                cache.ReplaceSeal(this, expected, 0, digest, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Retain the invalid seal if the work was canceled before publication.
            }
            catch (Exception ex) when (IsCacheFailure(ex))
            {
                cache.Report("publish", ex);
            }
        }

        internal void RequirePartition()
        {
            if (!ownedPartition.SameAs(Identity(ReadKind(Path, "directory"))))
                throw new CheckpointCacheRefusal("partition-identity-changed");
        }
    }

    private string Root => root;

    private FilesystemIdentity ReplaceSeal(Entry entry, FilesystemIdentity expectedSeal, long expectedLength,
        ReadOnlySpan<byte> payload, CancellationToken cancellationToken)
    {
        var destination = entry.Path + ".seal";
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var ownedTemp = default(FilesystemIdentity);
        try
        {
            CheckPaths(entry.Path, destination, temporary);
            entry.RequirePartition();
            var staged = WriteSeal(temporary, payload, out ownedTemp);
            CheckPaths(entry.Path, destination, temporary);
            entry.RequirePartition();
            var target = ReadKind(destination, "file");
            if (!expectedSeal.SameAs(Identity(target)) || target.Size != expectedLength)
                throw new CheckpointCacheRefusal("seal-identity-or-length-changed");
            var temp = ReadKind(temporary, "file");
            if (!staged.SameAs(Identity(temp)) || temp.Size != payload.Length)
                throw new CheckpointCacheRefusal("temporary-identity-or-length-changed");
            cancellationToken.ThrowIfCancellationRequested();
            sealIo.Replace(temporary, destination);
            ownedTemp = default;
            return staged;
        }
        finally
        {
            if (ownedTemp.IsBound)
            {
                try { CheckpointCleanupDefaults.Context.Io.DeleteAnchored(temporary, ownedTemp, directory: false); }
                catch (Exception ex) when (IsCacheFailure(ex)) { Report("temporary-cleanup", ex); }
            }
        }
    }

    private FilesystemIdentity WriteSeal(string path, ReadOnlySpan<byte> payload, out FilesystemIdentity identity)
    {
        identity = default;
        var reason = SafeWrite.OpenCreate(path, SafeWrite.Under(root), out var stream);
        if (reason is not null || stream is null)
            throw new CheckpointCacheRefusal(reason ?? "unopened");
        // Obtain the native handle before buffering any bytes. Even if the close
        // reports an error, a second stream flush cannot reach the live seal.
        var handle = stream.SafeFileHandle;
        var phase = "seal-handle";
        var failed = false;
        try
        {
            identity = RequireTrustedHandle(stream);
            phase = "seal-write";
            sealIo.Write(stream, payload);
            phase = "seal-flush";
            sealIo.Flush(stream);
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            failed = true;
            Report(phase, ex);
            throw;
        }
        finally
        {
            try { sealIo.Close(stream); }
            catch (Exception ex) when (IsCacheFailure(ex))
            {
                Report("seal-close", ex);
                if (!failed)
                    throw;
            }
            finally { handle.Dispose(); }
        }
        return identity;
    }
}

internal sealed class CheckpointCacheRefusal(string code) : Exception(code);
