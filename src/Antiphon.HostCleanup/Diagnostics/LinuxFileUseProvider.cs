using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.HostCleanup.Diagnostics;

// Read-only /proc observations. An open fd/mapping does not prove that it prevented unlink.
public sealed class LinuxFileUseProvider(LinuxFileUseSource? source = null) : IFileUseProvider
{
    private readonly LinuxFileUseSource source = source ?? new();
    public FileUseObservation Observe(FileUseRequest request, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) return new(FileUseStatus.Unavailable, FileUseCoverage.None, [], [FileUseReason.UnsupportedPlatform]);
        var owners = new List<FileUseOwner>(); var reasons = new HashSet<FileUseReason>(); var omitted = 0; var bytes = 0; var scanned = 0;
        string Read(string path, int cap)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes >= 8 * 1024 * 1024) throw new FileUseInputLimitException();
            var allowance = Math.Min(cap, 8 * 1024 * 1024 - bytes);
            try
            {
                var value = source.Read(path, allowance);
                var consumed = Encoding.UTF8.GetByteCount(value);
                if (consumed > allowance) throw new FileUseInputLimitException();
                bytes += consumed;
                return value;
            }
            catch (FileUseInputLimitException) { bytes += allowance; throw; }
        }
        ProcIdentity Identity(int pid) => ParseIdentity(pid, Read($"/proc/{pid}/stat", 8192));
        var requested = new[] { request.Path }.Concat(request.AdditionalPaths).Take(128).ToArray();
        if (request.AdditionalPaths.Count >= 128) { reasons.Add(FileUseReason.ResourceLimit); omitted += request.AdditionalPaths.Count + 1 - 128; }
        var resources = requested.Select(path => (Path: Canonical(path), Identity: LinuxFileIdentity.Of(path))).ToArray();
        if (resources.All(r => r.Identity.Kind == "absent")) return new(FileUseStatus.PathGone, FileUseCoverage.None, [], [FileUseReason.PathGone]);
        if (resources.Any(r => r.Identity.Kind is "unknown" or "symlink")) { reasons.Add(FileUseReason.AccessDenied); }
        try
        {
            foreach (var pid in source.Processes().Take(4097).OrderDescending())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (scanned++ >= 4096) { reasons.Add(FileUseReason.ProcessLimit); omitted++; break; }
                if (owners.Count >= 32) { reasons.Add(FileUseReason.OwnerLimit); omitted++; break; }
                ProcIdentity? initial = null; string? matchPath = null, matchKind = null;
                void RetainIdentity(FileUseOwnerState state)
                {
                    if (initial is not null && matchPath is not null)
                        owners.Add(new(pid, "[unavailable]", initial.Start, null, null, initial.ParentPid, [], null,
                            state.ToString(), matchKind!, matchPath) { State = state });
                }
                try
                {
                    initial = Identity(pid);
                    try
                    {
                        var count = 0;
                        foreach (var fd in source.Fds(pid))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (count++ >= 4096) { reasons.Add(FileUseReason.FdLimit); omitted++; break; }
                            try
                            {
                                var link = source.Link(fd);
                                if (link is null) continue;
                                var deleted = link.EndsWith(" (deleted)", StringComparison.Ordinal);
                                var linkedPath = deleted ? link[..^10] : link;
                                foreach (var target in resources)
                                {
                                    if (!Within(linkedPath, target.Path, target.Identity.Kind == "directory")) continue;
                                    var observed = LinuxFileIdentity.Of(fd, follow: true);
                                    if (target.Identity.Kind == "file" && (observed.Inode == 0 || target.Identity.Inode != observed.Inode || target.Identity.Device != observed.Device)) continue;
                                    if (deleted && target.Identity.Kind != "absent") continue;
                                    matchPath = target.Path; matchKind = deleted ? "DeletedOpenFile" : "OpenFile"; break;
                                }
                            }
                            catch (IOException) { }
                        }
                    }
                    catch (UnauthorizedAccessException) { reasons.Add(FileUseReason.AccessDenied); }
                    var modules = new HashSet<string>(StringComparer.Ordinal);
                    try
                    {
                        var maps = Read($"/proc/{pid}/maps", 1024 * 1024);
                        foreach (var line in maps.Split('\n'))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var fields = line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);
                            if (fields.Length < 6 || !ulong.TryParse(fields[4], out var inode)) continue;
                            var path = DecodeMapPath(fields[5]); var deleted = path.EndsWith(" (deleted)", StringComparison.Ordinal);
                            if (deleted) path = path[..^10];
                            if (modules.Count < 256 && path.StartsWith('/')) modules.Add(path);
                            var device = fields[3].Split(':');
                            if (device.Length != 2 || !uint.TryParse(device[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var major) || !uint.TryParse(device[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var minor)) continue;
                            foreach (var target in resources)
                            {
                                if (!Within(path, target.Path, target.Identity.Kind == "directory")) continue;
                                if (target.Identity.Kind == "file" && (inode == 0 || inode != target.Identity.Inode || (((ulong)major << 32) | minor) != target.Identity.Device)) continue;
                                if (deleted && target.Identity.Kind != "absent") continue;
                                if (matchPath is null) { matchPath = target.Path; matchKind = deleted ? "DeletedMappedFile" : "MappedFile"; }
                            }
                        }
                    }
                    catch (UnauthorizedAccessException) { reasons.Add(FileUseReason.AccessDenied); }
                    catch (FileUseInputLimitException) { reasons.Add(FileUseReason.InputLimit); omitted++; }
                    if (matchPath is null)
                    {
                        // A process cwd is directory evidence only, never a file holder.
                        if (resources.Any(r => r.Identity.Kind == "directory"))
                        {
                            var cwd = source.Link($"/proc/{pid}/cwd");
                            var target = resources.FirstOrDefault(r => r.Identity.Kind == "directory" && cwd is not null && Within(cwd, r.Path, true));
                            if (target.Path is not null) { matchPath = target.Path; matchKind = "WorkingDirectory"; }
                        }
                    }
                    if (matchPath is null) continue;
                    var name = Read($"/proc/{pid}/comm", 1024).TrimEnd('\r', '\n');
                    string? argv;
                    try { argv = Read($"/proc/{pid}/cmdline", 65536); }
                    catch (FileUseInputLimitException) { argv = null; reasons.Add(FileUseReason.RedactionUnavailable); omitted++; }
                    var executable = source.Link($"/proc/{pid}/exe");
                    var ancestors = new List<FileUseAncestor>(); var chain = new List<ProcIdentity>(); var parent = initial.ParentPid;
                    var visited = new HashSet<int> { pid };
                    while (parent > 0 && visited.Add(parent))
                    {
                        if (ancestors.Count >= 8) { reasons.Add(FileUseReason.AncestorLimit); omitted++; break; }
                        try
                        {
                            var identity = Identity(parent); var parentName = Read($"/proc/{parent}/comm", 1024).TrimEnd('\r', '\n');
                            // Ancestor payload is deliberately not read; no ancestry argument can establish ownership.
                            if (Identity(parent).Start != identity.Start) { reasons.Add(FileUseReason.IdentityChanged); break; }
                            ancestors.Add(new(parent, identity.Start, parentName, null)); chain.Add(identity); parent = identity.ParentPid;
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                        { reasons.Add(error is UnauthorizedAccessException ? FileUseReason.AccessDenied : FileUseReason.ExitedDuringCapture); break; }
                    }
                    if (Identity(pid).Start != initial.Start) { reasons.Add(FileUseReason.IdentityChanged); RetainIdentity(FileUseOwnerState.IdentityChanged); continue; }
                    bool? owned = null; var attribution = "NoVerifiedOwnedGeneration";
                    if (request.OwnedGenerations.Any(g => g.Pid == pid && g.NativeStart == initial.Start) || chain.Any(a => request.OwnedGenerations.Any(g => g.Pid == a.Pid && g.NativeStart == a.Start)))
                    { owned = true; attribution = "VerifiedOwnedGeneration"; }
                    else if (request.ForeignGenerations.Any(g => g.Pid == pid && g.NativeStart == initial.Start)) { owned = false; attribution = "VerifiedForeignGeneration"; }
                    DateTimeOffset? started = null;
                    try { using var process = Process.GetProcessById(pid); started = process.StartTime.ToUniversalTime(); }
                    catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception) { reasons.Add(FileUseReason.ExitedDuringCapture); }
                    // Recheck after every source of PID-based enrichment, including StartTime.
                    if (Identity(pid).Start != initial.Start) { reasons.Add(FileUseReason.IdentityChanged); RetainIdentity(FileUseOwnerState.IdentityChanged); continue; }
                    owners.Add(new(pid, name, initial.Start, started, argv, initial.ParentPid, ancestors, owned, attribution, matchKind!, matchPath) { ProvenModules = modules.ToArray(), ProvenExecutable = executable });
                }
                catch (UnauthorizedAccessException) { reasons.Add(FileUseReason.AccessDenied); RetainIdentity(FileUseOwnerState.MetadataUnavailable); }
                catch (FileUseInputLimitException) { reasons.Add(FileUseReason.InputLimit); omitted++; }
                catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException)
                { reasons.Add(FileUseReason.ExitedDuringCapture); RetainIdentity(FileUseOwnerState.ExitedDuringCapture); }
                if (bytes >= 8 * 1024 * 1024) { reasons.Add(FileUseReason.InputLimit); omitted++; break; }
            }
        }
        catch (UnauthorizedAccessException) { return new(scanned == 0 ? FileUseStatus.Unavailable : FileUseStatus.Partial, scanned == 0 ? FileUseCoverage.None : FileUseCoverage.Partial, owners, [.. reasons, FileUseReason.AccessDenied], omitted); }
        if (source.GetType() == typeof(LinuxFileUseSource)) reasons.Add(FileUseReason.NamespaceLimited);
        var partial = reasons.Count > 0;
        return new(partial ? FileUseStatus.Partial : owners.Count > 0 ? FileUseStatus.Observed : FileUseStatus.NoOwnersObserved,
            partial ? FileUseCoverage.Partial : FileUseCoverage.Complete, owners, reasons.ToArray(), omitted)
        { PathKind = resources[0].Identity.Kind, ResourceIdentity = $"{resources[0].Identity.Device:x}:{resources[0].Identity.Inode:x}" };
    }

    private static string Canonical(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd('/');
        return full.Length == 0 ? "/" : full;
    }
    private static bool Within(string path, string root, bool directory) => path == root || directory && path.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal);
    private static string DecodeMapPath(string value) => Regex.Replace(value, @"\\([0-7]{3})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 8)).ToString(), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private sealed record ProcIdentity(int Pid, int ParentPid, string Start);
    private static ProcIdentity ParseIdentity(int pid, string raw)
    {
        var closing = raw.LastIndexOf(')'); if (closing < 0) throw new IOException("invalid stat");
        var fields = raw[(closing + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 20 || !int.TryParse(fields[1], out var parent)) throw new IOException("invalid stat");
        return new(pid, parent, fields[19]);
    }
}

// A read-only seam; fixtures exercise actual proc records, absence and generation changes.
public class LinuxFileUseSource
{
    public virtual IEnumerable<int> Processes() => Directory.EnumerateDirectories("/proc").Select(Path.GetFileName).Where(n => int.TryParse(n, out _)).Select(n => int.Parse(n!));
    public virtual IEnumerable<string> Fds(int pid) => Directory.EnumerateFileSystemEntries($"/proc/{pid}/fd");
    public virtual string? Link(string path) => new FileInfo(path).LinkTarget;
    public virtual string Read(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[maximumBytes]; var count = 0;
        while (count < bytes.Length) { var got = stream.Read(bytes, count, bytes.Length - count); if (got == 0) break; count += got; }
        // A read exactly at the bound is conservatively partial; no lookahead exceeds the input budget.
        if (count >= maximumBytes) throw new FileUseInputLimitException();
        return Encoding.UTF8.GetString(bytes, 0, count);
    }
}
public sealed class FileUseInputLimitException : IOException;

internal readonly record struct LinuxFileIdentity(string Kind, ulong Device, ulong Inode)
{
    internal static LinuxFileIdentity Of(string path, bool follow = false)
    {
        if (!OperatingSystem.IsLinux()) return new("unknown", 0, 0);
        if (Statx(-100, path, follow ? 0 : 0x100, 0x103, out var stat) != 0)
            return new(Marshal.GetLastPInvokeError() is 2 or 20 ? "absent" : "unknown", 0, 0);
        return new((stat.Mode & 0xF000) switch { 0x8000 => "file", 0x4000 => "directory", 0xA000 => "symlink", _ => "other" }, ((ulong)stat.DevMajor << 32) | stat.DevMinor, stat.Inode);
    }
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct NativeStat
    {
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(136)] public uint DevMajor;
        [FieldOffset(140)] public uint DevMinor;
    }
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)] private static extern int Statx(int fd, string path, int flags, uint mask, out NativeStat stat);
}
