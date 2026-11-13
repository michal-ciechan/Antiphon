using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>CARD-1884. One process the top-consumer scan could read. A vanished pid is omitted.</summary>
internal readonly record struct HostProcessObservation(
    int Pid,
    long StartTicks,
    long CpuTicks,
    long TicksPerSecond,
    long? RssBytes,
    string Name,
    string Role);

/// <summary>CARD-1884. A bounded read of the host pid namespace. The cap is the number of processes read.</summary>
internal interface IHostProcessTable
{
    IReadOnlyList<HostProcessObservation> Read(int maxProcesses, IReadOnlyCollection<int> retainPids);
}

internal static class HostTopConsumerLimits
{
    public const int TopCount = 8;
    public const int ScanCap = 512;
    public static readonly TimeSpan MinScanInterval = TimeSpan.FromSeconds(5);
}

internal static class HostProcessTables
{
    public static IHostProcessTable Create() => Select(OperatingSystem.IsWindows(), OperatingSystem.IsLinux());

    public static IHostProcessTable Select(bool isWindows, bool isLinux)
    {
        if (isLinux && !isWindows) return new LinuxHostProcessTable("/proc");
        if (isWindows && !isLinux) return new WindowsHostProcessTable();
        return EmptyHostProcessTable.Instance;
    }
}

internal sealed class EmptyHostProcessTable : IHostProcessTable
{
    public static readonly EmptyHostProcessTable Instance = new();

    public IReadOnlyList<HostProcessObservation> Read(int maxProcesses, IReadOnlyCollection<int> retainPids) => [];
}

/// <summary>Keeps retained pids, then the highest pids, and stops at <paramref name="max"/>.</summary>
internal static class HostProcessSelection
{
    public static List<int> Choose(IReadOnlyList<int> pids, IReadOnlyCollection<int> retain, int max)
    {
        var chosen = new List<int>(Math.Min(Math.Max(max, 0), pids.Count));
        if (max <= 0 || pids.Count == 0) return chosen;
        var present = new HashSet<int>(pids);
        var taken = new HashSet<int>();
        foreach (var pid in retain)
        {
            if (chosen.Count >= max) break;
            if (pid <= 0 || !present.Contains(pid) || !taken.Add(pid)) continue;
            chosen.Add(pid);
        }

        for (var i = pids.Count - 1; i >= 0 && chosen.Count < max; i--)
        {
            var pid = pids[i];
            if (taken.Add(pid)) chosen.Add(pid);
        }

        return chosen;
    }
}

internal static class HostConsumerClassifier
{
    public static (string Name, string Role) Classify(string comm, IReadOnlyList<string> argBasenames)
    {
        var stems = new List<string>(1 + argBasenames.Count);
        Add(stems, comm);
        foreach (var arg in argBasenames) Add(stems, arg);
        string? role = null;
        string? named = null;
        var dotnet = false;
        var testVerb = false;
        var buildVerb = false;
        foreach (var stem in stems)
        {
            if (stem.Equals("dotnet", StringComparison.OrdinalIgnoreCase)) dotnet = true;
            if (stem.Equals("test", StringComparison.OrdinalIgnoreCase)) testVerb = true;
            if (stem.Equals("build", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("msbuild", StringComparison.OrdinalIgnoreCase)) buildVerb = true;
            var match = Match(stem);
            if (match is null || role is not null) continue;
            role = match;
            if (HostConsumerRules.IsName(stem)) named = stem;
        }

        if (role is null && dotnet && testVerb) role = HostConsumerRules.Test;
        if (role is null && dotnet && buildVerb) role = HostConsumerRules.Build;
        var name = named;
        if (name is null && comm.Length >= 15)
        {
            foreach (var stem in stems)
            {
                if (HostConsumerRules.IsName(stem) && stem.StartsWith(comm, StringComparison.Ordinal))
                {
                    name = stem;
                    break;
                }
            }
        }

        if (name is null) name = HostConsumerRules.IsName(comm) ? comm : "unknown";
        return (name, role ?? HostConsumerRules.Other);
    }

    private static void Add(List<string> stems, string token)
    {
        var stem = Stem(token);
        if (stem.Length > 0) stems.Add(stem);
    }

    private static string Stem(string token)
    {
        if (token.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) token = token[..^4];
        else if (token.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) token = token[..^4];
        return token;
    }

    private static string? Match(string stem)
    {
        if (stem.Equals("VBCSCompiler", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("csc", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("vbc", StringComparison.OrdinalIgnoreCase))
            return HostConsumerRules.Compiler;
        if (stem.Equals("MSBuild", StringComparison.OrdinalIgnoreCase)) return HostConsumerRules.Build;
        if (stem.Equals("testhost", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("vstest.console", StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
            return HostConsumerRules.Test;
        if (stem.Equals("Antiphon.SessionRunner", StringComparison.OrdinalIgnoreCase))
            return HostConsumerRules.AntiphonRunner;
        if (stem.Equals("Antiphon.PtyHost", StringComparison.OrdinalIgnoreCase))
            return HostConsumerRules.PtyHost;
        if (stem.Equals("dockerd", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("containerd", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("containerd-shim", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("docker-proxy", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("docker-init", StringComparison.OrdinalIgnoreCase))
            return HostConsumerRules.Docker;
        if (stem.Equals("grok", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("claude", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("codex", StringComparison.OrdinalIgnoreCase))
            return HostConsumerRules.AgentCli;
        return null;
    }
}

internal static class LinuxProcessStat
{
    public static HostProcessObservation? Parse(int pid, string stat, ReadOnlySpan<byte> cmdline, int pageSize, long ticksPerSecond)
    {
        if (pid <= 0 || pageSize <= 0 || ticksPerSecond <= 0) return null;
        var open = stat.IndexOf('(');
        var close = stat.LastIndexOf(')');
        if (open < 0 || close <= open || close + 2 >= stat.Length) return null;
        var comm = stat[(open + 1)..close];
        var fields = stat.AsSpan(close + 2);
        if (!Field(fields, 11, out var utime) || !Field(fields, 12, out var stime)
            || !Field(fields, 19, out var startTicks) || !Field(fields, 21, out var rss))
            return null;
        var basenames = ArgBasenames(cmdline);
        var (name, role) = HostConsumerClassifier.Classify(comm, basenames);
        long? rssBytes = rss >= 0 && rss <= long.MaxValue / pageSize ? rss * pageSize : null;
        return new HostProcessObservation(pid, startTicks, utime + stime, ticksPerSecond, rssBytes, name, role);
    }

    public static string[] ArgBasenames(ReadOnlySpan<byte> cmdline)
    {
        var list = new List<string>(4);
        var start = 0;
        for (var i = 0; i <= cmdline.Length && list.Count < 4; i++)
        {
            if (i != cmdline.Length && cmdline[i] != 0) continue;
            if (i > start)
            {
                var token = Encoding.UTF8.GetString(cmdline.Slice(start, i - start));
                var slash = token.LastIndexOfAny(['/', '\\']);
                var name = slash >= 0 ? token[(slash + 1)..] : token;
                if (name.Length > 64) name = name[..64];
                if (name.Length > 0) list.Add(name);
            }

            start = i + 1;
        }

        return list.ToArray();
    }

    private static bool Field(ReadOnlySpan<char> fields, int index, out long value)
    {
        value = 0;
        var seen = -1;
        var start = 0;
        for (var i = 0; i <= fields.Length; i++)
        {
            if (i != fields.Length && fields[i] != ' ') continue;
            if (i > start)
            {
                seen++;
                if (seen == index)
                    return long.TryParse(fields[start..i], NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
            }

            start = i + 1;
        }

        return false;
    }
}

internal sealed class LinuxHostProcessTable : IHostProcessTable
{
    private readonly string _root;
    private readonly int _pageSize;
    private readonly long _ticksPerSecond;

    public LinuxHostProcessTable(string root)
        : this(root, 0, 0)
    {
    }

    internal LinuxHostProcessTable(string root, int pageSize, long ticksPerSecond)
    {
        _root = root;
        _pageSize = pageSize;
        _ticksPerSecond = ticksPerSecond;
    }

    public IReadOnlyList<HostProcessObservation> Read(int maxProcesses, IReadOnlyCollection<int> retainPids)
    {
        var pageSize = _pageSize > 0 ? _pageSize : Environment.SystemPageSize;
        var ticks = _ticksPerSecond > 0 ? _ticksPerSecond : TicksPerSecond();
        if (pageSize <= 0 || ticks <= 0) return [];
        List<int> pids;
        try
        {
            pids = ListPids();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var chosen = HostProcessSelection.Choose(pids, retainPids, maxProcesses);
        var list = new List<HostProcessObservation>(chosen.Count);
        foreach (var pid in chosen)
        {
            try
            {
                var dir = Path.Combine(_root, pid.ToString(CultureInfo.InvariantCulture));
                var stat = File.ReadAllText(Path.Combine(dir, "stat"));
                var cmdline = Array.Empty<byte>();
                if (NeedsCommandLine(stat))
                {
                    try { cmdline = ReadCmdline(Path.Combine(dir, "cmdline")); }
                    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
                    {
                    }
                }

                if (LinuxProcessStat.Parse(pid, stat, cmdline, pageSize, ticks) is { } row)
                    list.Add(row);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
            {
            }
        }

        return list;
    }

    private List<int> ListPids()
    {
        var pids = new List<int>();
        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            var name = Path.GetFileName(dir);
            if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid > 0)
                pids.Add(pid);
        }

        pids.Sort();
        return pids;
    }

    private static bool NeedsCommandLine(string stat)
    {
        var open = stat.IndexOf('(');
        var close = stat.LastIndexOf(')');
        if (open < 0 || close <= open) return false;
        var comm = stat.AsSpan(open + 1, close - open - 1);
        if (comm.Length >= 15) return true;
        return comm.Equals("dotnet", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] ReadCmdline(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[1024];
        var read = stream.Read(buffer, 0, buffer.Length);
        if (read == buffer.Length)
        {
            var last = Array.LastIndexOf(buffer, (byte)0);
            if (last >= 0) read = last;
        }

        return buffer.AsSpan(0, read).ToArray();
    }

    private static long TicksPerSecond()
    {
        if (!OperatingSystem.IsLinux()) return 0;
        var ticks = Sysconf(2);
        return ticks > 0 ? ticks : 0;
    }

    [DllImport("libc", EntryPoint = "sysconf")]
    private static extern long Sysconf(int name);
}

/// <summary>
/// CARD-1884. One toolhelp snapshot for image names, then one limited-information handle per
/// selected pid. Image name only. Command lines and the process environment are not read.
/// </summary>
internal sealed class WindowsHostProcessTable : IHostProcessTable
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const long TicksPerSecond = 10_000_000;

    public IReadOnlyList<HostProcessObservation> Read(int maxProcesses, IReadOnlyCollection<int> retainPids)
    {
        if (!OperatingSystem.IsWindows()) return [];
        Dictionary<int, string> names;
        try
        {
            names = SnapshotNames();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return [];
        }

        var ids = new List<int>(names.Count);
        foreach (var pid in names.Keys) ids.Add(pid);
        ids.Sort();
        var chosen = HostProcessSelection.Choose(ids, retainPids, maxProcesses);
        var list = new List<HostProcessObservation>(chosen.Count);
        foreach (var pid in chosen)
        {
            if (!names.TryGetValue(pid, out var exe)) continue;
            if (!TryTimes(pid, out var start, out var cpu, out var rss)) continue;
            var (name, role) = HostConsumerClassifier.Classify(exe, []);
            list.Add(new HostProcessObservation(pid, start, cpu, TicksPerSecond, rss, name, role));
        }

        return list;
    }

    private static Dictionary<int, string> SnapshotNames()
    {
        var result = new Dictionary<int, string>();
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) return result;
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        if (!Process32FirstW(snapshot, ref entry)) return result;
        do
        {
            if (entry.Pid > 0) result[(int)entry.Pid] = entry.Exe ?? "";
        }
        while (Process32NextW(snapshot, ref entry));
        return result;
    }

    private static bool TryTimes(int pid, out long startTicks, out long cpuTicks, out long? rss)
    {
        startTicks = 0;
        cpuTicks = 0;
        rss = null;
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return false;
        try
        {
            if (!GetProcessTimes(handle, out var creation, out _, out var kernel, out var user)) return false;
            startTicks = creation;
            cpuTicks = kernel + user;
            var counters = new ProcessMemoryCounters { cb = (uint)Marshal.SizeOf<ProcessMemoryCounters>() };
            if (K32GetProcessMemoryInfo(handle, ref counters, counters.cb))
                rss = (long)counters.WorkingSetSize;
            return true;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Pid;
        public UIntPtr Heap;
        public uint Module, Threads, ParentPid;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(Microsoft.Win32.SafeHandles.SafeFileHandle snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(Microsoft.Win32.SafeHandles.SafeFileHandle snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(IntPtr handle, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool K32GetProcessMemoryInfo(IntPtr handle, ref ProcessMemoryCounters counters, uint size);
}
