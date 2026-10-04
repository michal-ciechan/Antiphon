using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Dormant native pin inspection. Owns no files and performs no writes. Publication,
/// durable fencing and custody are separate S3a work, required before activation.
/// </summary>
public sealed class AgentPinWorkspaceStore
{
    public const int PathSchemaVersion = 1;
    // Well above the bounded pin corpus, while keeping inspection of foreign files bounded.
    public const int MaxInspectionBytes = 1024 * 1024;
    private readonly Guid _runnerStoreId;
    private readonly Action<string>? _beforeRead;

    public AgentPinWorkspaceStore(Guid runnerStoreId) : this(runnerStoreId, null) { }

    // Deterministic native-I/O failure/component-swap boundary, not a product bypass.
    internal AgentPinWorkspaceStore(Guid runnerStoreId, Action<string>? beforeRead)
    {
        if (runnerStoreId == Guid.Empty) throw new ArgumentException("Runner store identity is required.", nameof(runnerStoreId));
        _runnerStoreId = runnerStoreId;
        _beforeRead = beforeRead;
    }

    public async Task<AgentPinInspection> InspectAsync(AgentPinInspectRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.SchemaVersion != PathSchemaVersion) return Refuse("pin_schema_unsupported");
        if (request.AgentId == Guid.Empty) return Refuse("pin_owner_invalid");
        if (request.RunnerStoreId != _runnerStoreId) return Refuse("pin_store_mismatch");
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            return Refuse("pin_platform_unsupported");
        if (string.IsNullOrWhiteSpace(request.Cwd) || request.Cwd.IndexOf('\0') >= 0
            || !Path.IsPathFullyQualified(request.Cwd)) return Refuse("pin_cwd_invalid");

        try
        {
            // Inspect the original components BEFORE GetFullPath collapses dot aliases:
            // /safe/link/.. must not hide traversal through a symlink.
            var cwdState = CheckDirectories(request.Cwd);
            if (cwdState is not null) return PathResult(cwdState, missingCwd: true);
            var cwd = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Cwd));
            var target = Path.Combine(cwd, ".antiphon", "pins", request.AgentId.ToString("N"), "antiphon.md");
            var state = CheckTarget(target);
            if (state is not null) return PathResult(state, missingCwd: false, cwd, target);

            _beforeRead?.Invoke(target);
            // A host inspection is not authority to follow a subsequently substituted link.
            cwdState = CheckDirectories(request.Cwd);
            if (cwdState is not null) return PathResult(cwdState, missingCwd: true);
            state = CheckTarget(target);
            if (state is not null) return PathResult(state, missingCwd: false, cwd, target);

            var before = new FileInfo(target);
            var length = before.Length;
            if (length > MaxInspectionBytes) return Refuse("pin_file_too_large");
            var modified = before.LastWriteTimeUtc;
            await using var stream = new AgentPinPosixReader().Open(target);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[8192];
            long read = 0;
            int count;
            while ((count = await stream.ReadAsync(buffer, ct)) != 0)
            {
                read += count;
                // Length can grow after opening; checking the initial length alone is not a bound.
                if (read > MaxInspectionBytes) return Refuse("pin_file_too_large");
                hasher.AppendData(buffer, 0, count);
            }
            var hash = hasher.GetHashAndReset();

            // Recheck after the read too. A stable substituted component yields no path/hash.
            // These checks do not claim ACL isolation against same-user swap-and-restore races.
            cwdState = CheckDirectories(request.Cwd);
            if (cwdState is not null) return PathResult(cwdState, missingCwd: true);
            state = CheckTarget(target);
            if (state is not null) return PathResult(state, missingCwd: false, cwd, target);
            var after = new FileInfo(target);
            if (read != length || after.Length != length || after.LastWriteTimeUtc != modified)
                return new(AgentPinInspectionStatus.Unavailable, "pin_file_changed", request.AgentId, _runnerStoreId);

            return new(AgentPinInspectionStatus.Observed, null, request.AgentId, _runnerStoreId,
                cwd, target, Convert.ToHexStringLower(hash), read);
        }
        catch (AgentPinNonRegularFileException)
        {
            return Refuse("pin_path_not_file");
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return Refuse("pin_platform_unsupported");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Exists() would collapse access failure into absence. Keep it unresolved.
            return new(AgentPinInspectionStatus.Unavailable, "pin_io_unavailable", request.AgentId, _runnerStoreId);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Refuse("pin_cwd_invalid");
        }

        AgentPinInspection Refuse(string reason) =>
            new(AgentPinInspectionStatus.Refused, reason, request.AgentId, _runnerStoreId);

        AgentPinInspection PathResult(string reason, bool missingCwd, string? cwd = null, string? target = null) =>
            reason == "missing"
                ? new(missingCwd ? AgentPinInspectionStatus.MissingCwd : AgentPinInspectionStatus.MissingFile,
                    null, request.AgentId, _runnerStoreId, cwd, target)
                : Refuse(reason);
    }

    private static string? CheckTarget(string target)
    {
        var directoryState = CheckDirectories(Path.GetDirectoryName(target)!);
        if (directoryState is not null) return directoryState;
        var attributes = Attributes(target);
        if (attributes is null) return "missing";
        if ((attributes & FileAttributes.ReparsePoint) != 0) return "pin_path_link";
        if ((attributes & FileAttributes.Directory) != 0) return "pin_path_not_file";
        return null;
    }

    private static string? CheckDirectories(string path)
    {
        var root = Path.GetPathRoot(path)!;
        var current = root;
        foreach (var part in path[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                current = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current)) ?? root;
                continue;
            }
            current = Path.Combine(current, part);
            var attributes = Attributes(current);
            if (attributes is null) return "missing";
            if ((attributes & FileAttributes.ReparsePoint) != 0) return "pin_path_link";
            if ((attributes & FileAttributes.Directory) == 0) return "pin_path_not_directory";
        }
        return null;
    }

    private static FileAttributes? Attributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}
