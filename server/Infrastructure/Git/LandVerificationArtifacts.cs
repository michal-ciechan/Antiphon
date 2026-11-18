using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// Persistent build outputs, outside the disposable land worktree. Callers hold L then R.
/// Nothing here adopts an unmarked existing directory; only <see cref="Prune"/> deletes, and only
/// owned set-aside generations under the root (CARD-1994).
/// </summary>
public sealed class LandVerificationArtifacts
{
    private const string Quarantined = "quarantined";
    private const int KeepSetAside = 2;

    internal string? SdkIdentity { get; init; }

    public async Task<Generation> PrepareAsync(string common, string worktree, bool reuse,
        Action<string> diagnostic, CancellationToken ct, string? target = null)
    {
        var sdk = SdkIdentity ?? await ReadSdkAsync(worktree, ct);
        var toolchain = $"{sdk}|{RuntimeInformation.OSDescription}|{RuntimeInformation.ProcessArchitecture}|Debug";
        return Prepare(common, toolchain, reuse, diagnostic, target);
    }

    internal string RootFor(string common, string toolchain) => Path.Combine(common, "antiphon", "land-verify",
        "v1", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(toolchain))).ToLowerInvariant());

    internal Generation Prepare(string common, string toolchain, bool reuse, Action<string> diagnostic, string? target = null)
    {
        common = Path.TrimEndingDirectorySeparator(Path.GetFullPath(common));
        if (OperatingSystem.IsWindows()) common = common.ToLowerInvariant();
        var owner = JsonSerializer.Serialize(new { version = 1, repository = common, toolchain });
        if (!reuse) return Cold(owner, diagnostic, "reuse-disabled");
        try
        {
            var root = RootFor(common, toolchain);
            RequireNoLinks(root);
            if (!Path.Exists(root)) CreateOwned(root, owner);
            RequireOwner(root, owner);
            var currentFile = Path.Combine(root, PointerFor(target));
            if (Path.Exists(currentFile))
            {
                RequireNoLinks(currentFile);
                var id = ReadSmall(currentFile);
                if (!Guid.TryParseExact(id, "N", out _)) throw new IOException("invalid-generation");
                var current = Path.Combine(root, id);
                RequireOwner(current, owner);
                // CARD-1994: the mark first. Only a generation that will be reused is scanned.
                var state = ReadSmall(Path.Combine(current, "state"));
                if (state == "ready")
                {
                    RequireUnlinkedTree(current);
                    WriteState(current, "building");
                    diagnostic("land_artifacts warm " + current);
                    Prune(root, owner, current, diagnostic);
                    return new(current, owner);
                }
                if (state is not ("building" or Quarantined)) throw new IOException("invalid-state");
                // Still building: the last build was killed or crashed, or never recorded its exit.
                WriteState(current, Quarantined);
                diagnostic("land_artifacts quarantined " + current);
            }
            var generationId = Guid.NewGuid().ToString("N");
            var path = Path.Combine(root, generationId);
            CreateOwned(path, owner);
            WriteState(path, "building");
            WriteAtomic(currentFile, generationId);
            diagnostic("land_artifacts new " + path);
            Prune(root, owner, path, diagnostic);
            return new(path, owner);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Cold(owner, diagnostic, "unusable-owned-root:" + ex.GetType().Name);
        }
    }

    /// <summary>
    /// CARD-1994: one current pointer per target lane in the same root. Master (and no target) keeps the
    /// v1 <c>current</c> pointer, every <c>rc/*</c> line shares <c>current-rc</c>, any other target gets its own.
    /// </summary>
    internal static string PointerFor(string? target)
    {
        var branch = target is null ? null
            : target.StartsWith("refs/heads/", StringComparison.Ordinal) ? target["refs/heads/".Length..] : target;
        if (string.IsNullOrEmpty(branch) || branch == "master") return "current";
        if (branch.StartsWith("rc/", StringComparison.Ordinal)) return "current-rc";
        return "current-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(branch)))[..16].ToLowerInvariant();
    }

    /// <summary>
    /// CARD-1994: keeps the newest <see cref="KeepSetAside"/> set-aside generations and deletes older ones.
    /// Callers hold the land execution lock, so no other land is building here. Only owned, unlinked,
    /// generation-named direct children of the root that no pointer names are candidates; a failure
    /// to prune is a diagnostic, never a cold land.
    /// </summary>
    private static void Prune(string root, string owner, string active, Action<string> diagnostic)
    {
        try
        {
            var referenced = new HashSet<string>(StringComparer.Ordinal) { Path.GetFileName(active) };
            foreach (var pointer in Directory.EnumerateFiles(root, "current*"))
            {
                var name = Path.GetFileName(pointer);
                if (name != "current" && !name.StartsWith("current-", StringComparison.Ordinal)) continue;
                referenced.Add(ReadSmall(pointer));
            }
            var candidates = new List<(string Path, DateTime At)>();
            foreach (var entry in Directory.EnumerateDirectories(root))
            {
                var id = Path.GetFileName(entry);
                if (!Guid.TryParseExact(id, "N", out _) || referenced.Contains(id)) continue;
                var path = Path.Combine(root, id);
                if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), root, StringComparison.Ordinal)) continue;
                try
                {
                    RequireNoLinks(path);
                    RequireOwner(path, owner);
                    var stateFile = Path.Combine(path, "state");
                    if (ReadSmall(stateFile) is not (Quarantined or "building")) continue;
                    candidates.Add((path, File.GetLastWriteTimeUtc(stateFile)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    diagnostic($"land_artifacts retained {path} reason={ex.Message}");
                }
            }
            foreach (var (path, _) in candidates.OrderByDescending(c => c.At)
                         .ThenByDescending(c => c.Path, StringComparer.Ordinal).Skip(KeepSetAside))
            {
                Directory.Delete(path, recursive: true);
                diagnostic("land_artifacts pruned " + path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostic("land_artifacts prune-skipped reason=" + ex.GetType().Name);
        }
    }

    private static Generation Cold(string owner, Action<string> diagnostic, string reason)
    {
        var path = Path.Combine(Path.GetTempPath(), "antiphon-land-cold-" + Guid.NewGuid().ToString("N"));
        CreateOwned(path, owner);
        WriteState(path, "building");
        diagnostic($"land_artifacts cold reason={reason} path={path}");
        return new(path, owner);
    }

    private static void CreateOwned(string path, string owner)
    {
        RequireNoLinks(path);
        if (Path.Exists(path)) throw new IOException("pre-existing-output");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var marker = new FileStream(Path.Combine(path, "owner.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        marker.Write(Encoding.UTF8.GetBytes(owner));
    }

    internal static string CreateResultsDirectory(string output)
    {
        var path = Path.Combine(output, "results-" + Guid.NewGuid().ToString("N"));
        CreateOwned(path, """{"owner":"antiphon-land-results","version":1}""");
        return path;
    }

    private static void RequireOwner(string path, string owner)
    {
        RequireNoLinks(Path.Combine(path, "owner.json"));
        if (ReadSmall(Path.Combine(path, "owner.json")) != owner) throw new IOException("foreign-owner");
    }

    internal static void RequireNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("linked-output");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void RequireUnlinkedTree(string path)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("linked-output");
            if ((attributes & FileAttributes.Directory) != 0) RequireUnlinkedTree(entry);
            else RequireRegularFile(entry);
        }
    }

    private static string ReadSmall(string path)
    {
        RequireNoLinks(path);
        RequireRegularFile(path);
        if (new FileInfo(path).Length > 4096) throw new IOException("oversized-marker");
        return File.ReadAllText(path);
    }

    private static void WriteState(string path, string state) => WriteAtomic(Path.Combine(path, "state"), state);

    // Reject hard links and nonregular files too, before any content read can follow or block on one.
    private static void RequireRegularFile(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            if (Statx(-100, path, 0x100, 0x5, out var stat) != 0 || (stat.Mask & 0x5) != 0x5
                || stat.Links != 1 || (stat.Mode & 0xf000) != 0x8000)
                throw new IOException("unowned-file-type-or-links");
            return;
        }
        if (OperatingSystem.IsWindows())
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!GetFileInformationByHandle(handle, out var info) || info.Links != 1
                || (info.Attributes & (0x400 | 0x10 | 0x40)) != 0)
                throw new IOException("unowned-file-type-or-links");
            return;
        }
        throw new IOException("unsupported-artifact-custody-platform");
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct FileStat
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint Links;
        [FieldOffset(28)] public ushort Mode;
    }
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int directory, string path, int flags, uint mask, out FileStat stat);
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh,
            WriteLow, WriteHigh, VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);

    private static void WriteAtomic(string path, string value)
    {
        RequireNoLinks(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(Encoding.UTF8.GetBytes(value));
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static async Task<string> ReadSdkAsync(string worktree, CancellationToken ct)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = worktree, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("--version");
        using var process = Process.Start(start) ?? throw new IOException("sdk-identity-unavailable");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(ct); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, error);
            throw;
        }
        await Task.WhenAll(output, error);
        var sdk = (await output).Trim();
        if (process.ExitCode != 0 || sdk.Length is 0 or > 200) throw new IOException("sdk-identity-unavailable");
        return sdk;
    }

    public sealed class Generation
    {
        private readonly string _owner;
        internal Generation(string path, string owner) { Path = path; _owner = owner; }
        public string Path { get; }
        public void Complete()
        {
            RequireOwner(Path, _owner);
            WriteState(Path, "ready");
        }
    }
}
