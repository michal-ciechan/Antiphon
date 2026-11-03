namespace Antiphon.SessionRunner;

/// <summary>CARD-0824 S1. Non-git filesystem work of the slot pool, so a test can inject a failure.</summary>
public interface IWorkspaceSlotFileSystem
{
    void CreateDirectory(string path);

    /// <summary>Copies a file and verifies the destination bytes equal the source.</summary>
    void CopyVerified(string source, string destination);
}

/// <summary>One process holding a slot: its cwd or an open descriptor is under the slot.</summary>
public sealed record WorkspaceSlotHolder(int Pid, string Argv0, string Kind);

/// <summary>CARD-0824 S1 §3.4. Processes whose cwd or open handle lies under a path.</summary>
public interface IWorkspaceSlotProcessProbe
{
    IReadOnlyList<WorkspaceSlotHolder> Holders(string path);
}

/// <summary>
/// CARD-1677 D-7. A holder scan that says whether it saw every process it could read. <see cref="Complete"/>
/// is false when the process table could not be enumerated (or off Linux): retention keeps the tree then.
/// </summary>
public sealed record HolderScan(bool Complete, IReadOnlyList<WorkspaceSlotHolder> Holders);

/// <summary>CARD-1677 S1. The fail-closed holder scan retention uses; the slot pool keeps its fail-open probe.</summary>
public interface IWorkspaceHolderScan
{
    HolderScan Scan(string path);
}

/// <summary>CARD-0824 S1 §3.2a. Volume free space and bounded pool measurement.</summary>
public interface IWorkspaceSlotDiskProbe
{
    (long FreeBytes, long TotalBytes) Volume(string path);

    long Measure(string path);
}

public sealed class PhysicalWorkspaceSlotFileSystem : IWorkspaceSlotFileSystem
{
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void CopyVerified(string source, string destination)
    {
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.Copy(source, destination, overwrite: true);
        if (!File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(destination)))
            throw new IOException("Retained evidence " + destination + " does not match its source.");
    }
}

/// <summary>
/// Linux <c>/proc/*/cwd</c> and <c>/proc/*/fd/*</c> scan. A process of another uid is unreadable and
/// skipped, which scopes the scan to the runner's own uid. Other hosts report no holders (S3 owns
/// Windows Restart Manager attribution).
/// </summary>
public sealed class ProcWorkspaceSlotProcessProbe : IWorkspaceSlotProcessProbe, IWorkspaceHolderScan
{
    private readonly string _procRoot;
    private readonly bool _isLinux;

    public ProcWorkspaceSlotProcessProbe() : this("/proc")
    {
    }

    /// <summary>CARD-1677 S-3 test seam: an unreadable proc root makes <see cref="Scan"/> incomplete.</summary>
    internal ProcWorkspaceSlotProcessProbe(string procRoot = "/proc", bool? isLinux = null)
    {
        _procRoot = procRoot;
        _isLinux = isLinux ?? OperatingSystem.IsLinux();
    }

    /// <summary>CARD-0824 S1 slot path: unchanged fail-open result (an unreadable table reports no holders).</summary>
    public IReadOnlyList<WorkspaceSlotHolder> Holders(string path) => Probe(path).Holders;

    /// <summary>CARD-1677 D-7: the same scan, but an unreadable table or a non-Linux host is incomplete.</summary>
    public HolderScan Scan(string path) => new(true, Probe(path).Holders); // CARD-1677 red shim: fail-open

    private HolderScan Probe(string path)
    {
        if (!_isLinux || !Directory.Exists(_procRoot))
            return new HolderScan(false, []);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var self = Environment.ProcessId;
        var holders = new List<WorkspaceSlotHolder>();
        List<string> entries;
        try { entries = Directory.EnumerateDirectories(_procRoot).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new HolderScan(false, []); }
        // A table that does not list this process is not a process table: the scan proves nothing.
        var sawSelf = entries.Any(entry => Path.GetFileName(entry) == self.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var entry in entries)
        {
            if (!int.TryParse(Path.GetFileName(entry), out var pid) || pid == self)
                continue;
            string? kind = null;
            if (Under(ReadLink(entry + "/cwd"), root))
                kind = "cwd";
            else
            {
                try
                {
                    foreach (var fd in Directory.EnumerateFileSystemEntries(entry + "/fd"))
                    {
                        if (Under(ReadLink(fd), root))
                        {
                            kind = "fd";
                            break;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            if (kind is not null)
                holders.Add(new WorkspaceSlotHolder(pid, Argv0(entry), kind));
        }

        return new HolderScan(sawSelf, holders);
    }

    private static string? ReadLink(string path)
    {
        try { return new FileInfo(path).LinkTarget; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static bool Under(string? target, string root) =>
        target is not null && (target == root || target.StartsWith(root + "/", StringComparison.Ordinal));

    private static string Argv0(string entry)
    {
        try
        {
            var raw = File.ReadAllText(entry + "/cmdline");
            var end = raw.IndexOf('\0');
            return end < 0 ? raw : raw[..end];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }
}

public sealed class PhysicalWorkspaceSlotDiskProbe : IWorkspaceSlotDiskProbe
{
    private static readonly TimeSpan MeasureBudget = TimeSpan.FromSeconds(60);

    public (long FreeBytes, long TotalBytes) Volume(string path)
    {
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)) ?? "/");
        return (drive.AvailableFreeSpace, drive.TotalSize);
    }

    /// <summary>Bounded <c>du</c>: stops at the 60 s budget and returns what it counted so far.</summary>
    public long Measure(string path)
    {
        if (!Directory.Exists(path))
            return 0;
        var deadline = DateTime.UtcNow + MeasureBudget;
        long total = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
        {
            try { total += file.Length; } catch (IOException) { }
            if (DateTime.UtcNow > deadline)
                break;
        }

        return total;
    }
}
