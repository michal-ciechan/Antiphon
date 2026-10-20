using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// Persistent build outputs, outside the disposable land worktree. Callers hold L then R.
/// Nothing here deletes an old generation or adopts an unmarked existing directory.
/// </summary>
public sealed class LandVerificationArtifacts
{
    internal string? SdkIdentity { get; init; }

    public async Task<Generation> PrepareAsync(string common, string worktree, bool reuse,
        Action<string> diagnostic, CancellationToken ct)
    {
        var sdk = SdkIdentity ?? await ReadSdkAsync(worktree, ct);
        var toolchain = $"{sdk}|{RuntimeInformation.OSDescription}|{RuntimeInformation.ProcessArchitecture}|Debug";
        return Prepare(common, toolchain, reuse, diagnostic);
    }

    internal string RootFor(string common, string toolchain) => Path.Combine(common, "antiphon", "land-verify",
        "v1", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(toolchain))).ToLowerInvariant());

    internal Generation Prepare(string common, string toolchain, bool reuse, Action<string> diagnostic)
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
            var currentFile = Path.Combine(root, "current");
            if (Path.Exists(currentFile))
            {
                RequireNoLinks(currentFile);
                var id = ReadSmall(currentFile);
                if (!Guid.TryParseExact(id, "N", out _)) throw new IOException("invalid-generation");
                var current = Path.Combine(root, id);
                RequireOwner(current, owner);
                RequireUnlinkedTree(current);
                var stateFile = Path.Combine(current, "state");
                var state = ReadSmall(stateFile);
                if (state == "ready")
                {
                    WriteState(current, "building");
                    diagnostic("land_artifacts warm " + current);
                    return new(current, owner);
                }
                if (state != "building") throw new IOException("invalid-state");
                diagnostic("land_artifacts quarantined " + current);
            }
            var generationId = Guid.NewGuid().ToString("N");
            var path = Path.Combine(root, generationId);
            CreateOwned(path, owner);
            WriteState(path, "building");
            WriteAtomic(currentFile, generationId);
            diagnostic("land_artifacts new " + path);
            return new(path, owner);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Cold(owner, diagnostic, "unusable-owned-root:" + ex.GetType().Name);
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
