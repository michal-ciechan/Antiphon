using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Antiphon.Hosting;

/// <summary>
/// One process-private XDG cache so concurrent pwsh starts do not share a JIT profile.
/// CARD-1793: the runner gives each agent session its own owned cache, and every install
/// removes family directories whose recorded owner is no longer a live process.
/// CARD-1813: the owner token is pid plus process-start ticks, unmarked directories expire,
/// and a stamp limits the scan to once per <see cref="SweepInterval"/>.
/// </summary>
internal static class PwshPrivateCache
{
    internal const string VariableName = "XDG_CACHE_HOME";
    internal const string ProbeVariable = "ANTIPHON_PWSH_CACHE_PROBE";
    internal const string RootVariable = "ANTIPHON_PWSH_CACHE_ROOT";
    internal const string DirectoryPrefix = "antiphon-pwsh-cache-";
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan UnmarkedGrace = TimeSpan.FromMinutes(10);

    // Two processes reading Process.StartTime disagree by a few milliseconds on Linux.
    // Fifty milliseconds still rejects pid reuse: the replacement cannot have started
    // until the recorded process had already exited.
    internal static readonly long StartMatchToleranceTicks = TimeSpan.FromMilliseconds(50).Ticks;

    // Tests replace the platform start read. Null uses /proc starttime on Linux and
    // Process.StartTime on Windows. The repair wires this; until then the platform read stands.
    internal static Func<int, StartIdentity>? StartIdentitySource;

    internal readonly record struct StartIdentity(StartReadKind Kind, long? KernelTicks, long? WallTicks)
    {
        internal static StartIdentity Absent { get; } = new(StartReadKind.Absent, null, null);

        internal static StartIdentity Unknown { get; } = new(StartReadKind.Unknown, null, null);

        internal static StartIdentity Kernel(long ticks, long? wallTicks = null) =>
            new(StartReadKind.Present, ticks, wallTicks);
    }

    internal enum StartReadKind
    {
        Absent,
        Unknown,
        Present,
    }

    private const string MarkerName = "owner.pid";
    private const string StampName = "antiphon-pwsh-cache-sweep.stamp";
    private const long MicrosecondTicks = 10;
    private const int MoveAttempts = 5;
    private static readonly object Gate = new();
    private static string? _created;

    static PwshPrivateCache() =>
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Release();

    [ModuleInitializer]
    internal static void InstallAtStartup() => Install();

    // The probe runs from Main. Environment.Exit during the module initializer
    // deadlocks the runtime after the directory has already been removed.
    internal static int CompleteProbe(string outputPath)
    {
        var created = Install();
        var code = WriteProbe(created, outputPath);
        Release();
        return code;
    }

    internal static string Install()
    {
        lock (Gate)
        {
            var current = Environment.GetEnvironmentVariable(VariableName);
            if (OwnedByThisProcess(current))
                return Path.GetFullPath(current!);

            var root = CacheRoot();
            SweepStale(root);
            var directory = Path.Combine(root, DirectoryName());
            Directory.CreateDirectory(directory);
            WriteOwner(directory, Environment.ProcessId, null);
            Environment.SetEnvironmentVariable(VariableName, directory);
            _created = directory;
            return directory;
        }
    }

    internal static void Release()
    {
        lock (Gate)
        {
            var directory = _created;
            if (directory is null)
                return;
            _created = null;
            if (OwnedByThisProcess(directory))
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            }

            if (string.Equals(Environment.GetEnvironmentVariable(VariableName), directory, StringComparison.Ordinal))
                Environment.SetEnvironmentVariable(VariableName, null);
        }
    }

    /// <summary>
    /// CARD-1793: a cache for one agent session. The caller names the owner (the runner until the
    /// pty-host pid is known) and deletes it at session end; the sweep covers a lost owner.
    /// </summary>
    internal static string CreateOwned(string label, int ownerPid)
    {
        var directory = Path.Combine(CacheRoot(), DirectoryPrefix + label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        WriteOwner(directory, ownerPid, null);
        return directory;
    }

    internal static void AssignOwner(string directory, int ownerPid, DateTime? startedUtc = null)
    {
        try
        {
            WriteOwner(directory, ownerPid, startedUtc);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    internal static void DeleteOwned(string directory)
    {
        if (IsFamilyDirectory(CacheRoot(), directory))
            TryDelete(directory);
    }

    /// <summary>
    /// A SIGKILLed owner never runs <see cref="Release"/>. Remove each family directory whose
    /// owner is dead, or whose pid was reused by a process with a different start. An unmarked
    /// directory is kept until <see cref="UnmarkedGrace"/> so a creator can finish writing the
    /// marker. An unreadable or garbled marker is kept. A fresh <see cref="StampName"/> skips
    /// the scan. Links are never followed.
    /// </summary>
    internal static int SweepStale(string root)
    {
        if (SweepIsCurrent(root))
            return 0;

        string[] candidates;
        try
        {
            candidates = Directory.GetDirectories(root, DirectoryPrefix + "*");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return 0;
        }

        var removed = 0;
        foreach (var directory in candidates)
        {
            if (!IsFamilyDirectory(root, directory))
                continue;

            var owner = ReadOwner(directory);
            if (owner.Kind is OwnerKind.Unreadable or OwnerKind.Garbled)
                continue;
            if (owner.Kind == OwnerKind.Absent)
            {
                if (!UnmarkedExpired(directory))
                    continue;
            }
            else if (IsLive(owner.Pid, owner.StartedUtcTicks))
            {
                continue;
            }

            if (TryDelete(directory))
                removed++;
        }

        TouchStamp(root);
        return removed;
    }

    // ANTIPHON_PWSH_CACHE_ROOT, when it is an ordinary directory, is the sweep and create root.
    // Mutation sets it (or TMPDIR/TMP/TEMP) on the test host so a sweep mutant cannot see /tmp.
    // A missing path is created. A reparse point falls back to the process temp root.
    internal static string CacheRoot()
    {
        var configured = Environment.GetEnvironmentVariable(RootVariable);
        if (string.IsNullOrWhiteSpace(configured))
            return Path.GetTempPath();

        try
        {
            var full = Path.GetFullPath(configured);
            Directory.CreateDirectory(full);
            var attributes = File.GetAttributes(full);
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                return Path.GetTempPath();
            return full;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return Path.GetTempPath();
        }
    }

    private static bool IsLive(int pid, long? startedUtcTicks)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return startedUtcTicks is not long expected || SameStart(expected, process.StartTime);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Process enumeration can fail closed on Windows. Keeping the directory is the safe side.
            return true;
        }
    }

    private static bool IsFamilyDirectory(string root, string directory)
    {
        try
        {
            var full = Path.GetFullPath(directory);
            if (!string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.Ordinal)
                || !Path.GetFileName(full).StartsWith(DirectoryPrefix, StringComparison.Ordinal))
                return false;
            var attributes = File.GetAttributes(full);
            return (attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static OwnerRecord ReadOwner(string directory)
    {
        try
        {
            var path = Path.Combine(directory, MarkerName);
            if (!File.Exists(path))
                return OwnerRecord.Absent;
            return ParseOwner(File.ReadAllText(path).Trim());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return OwnerRecord.Unreadable;
        }
    }

    // "pid" (legacy) or "pid@startTicks". Anything else is garbled and kept.
    private static OwnerRecord ParseOwner(string text)
    {
        var separator = text.IndexOf('@');
        var pidText = separator >= 0 ? text[..separator] : text;
        if (!int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
            return OwnerRecord.Garbled;

        long? ticks = null;
        if (separator >= 0)
        {
            if (!long.TryParse(text[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                return OwnerRecord.Garbled;
            ticks = parsed;
        }

        return new OwnerRecord(OwnerKind.Owned, pid, ticks);
    }

    // Write then move, so a concurrent sweep never reads a truncated pid. Retry the replace:
    // on Windows a concurrent reader can refuse the first MoveFileEx.
    private static void WriteOwner(string directory, int ownerPid, DateTime? startedUtc)
    {
        var ticks = startedUtc is DateTime supplied ? NormalizeTicks(supplied) : TryStartTicks(ownerPid);
        var token = ownerPid.ToString(CultureInfo.InvariantCulture);
        if (ticks is long value)
            token += "@" + value.ToString(CultureInfo.InvariantCulture);

        var temporary = Path.Combine(directory, MarkerName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(temporary, token);
        MoveIntoPlace(temporary, Path.Combine(directory, MarkerName));
    }

    private static long? TryStartTicks(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return NormalizeTicks(process.StartTime);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static void MoveIntoPlace(string temporary, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, destination, overwrite: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                if (attempt >= MoveAttempts)
                    throw;
                System.Threading.Thread.Sleep(20);
            }
        }
    }

    private static bool TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool SweepIsCurrent(string root)
    {
        try
        {
            var stamp = Path.Combine(root, StampName);
            if (!File.Exists(stamp))
                return false;
            return DateTime.UtcNow - File.GetLastWriteTimeUtc(stamp) < SweepInterval;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static void TouchStamp(string root)
    {
        var temporary = Path.Combine(root, StampName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, "1");
            File.Move(temporary, Path.Combine(root, StampName), overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool UnmarkedExpired(string directory)
    {
        try
        {
            return DateTime.UtcNow - Directory.GetLastWriteTimeUtc(directory) >= UnmarkedGrace;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static int WriteProbe(string directory, string outputPath)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("[Console]::Out.Write($env:XDG_CACHE_HOME)");
        using var process = Process.Start(start);
        if (process is null)
            return 2;

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }

            process.WaitForExit(5_000);
            return 2;
        }

        var output = stdout.GetAwaiter().GetResult().Trim();
        _ = stderr.GetAwaiter().GetResult();
        try
        {
            File.WriteAllText(outputPath, output);
        }
        catch (IOException)
        {
            return 2;
        }
        catch (UnauthorizedAccessException)
        {
            return 2;
        }

        return process.ExitCode == 0 && string.Equals(output, directory, StringComparison.Ordinal) ? 0 : 2;
    }

    private static bool OwnedByThisProcess(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }

        var root = Path.GetFullPath(CacheRoot());
        var relative = Path.GetRelativePath(root, full);
        if (relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
            return false;

        var prefix = DirectoryPrefix + ProcessToken() + "-";
        if (!Path.GetFileName(full).StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var marker = Path.Combine(full, MarkerName);
        try
        {
            if (!File.Exists(marker))
                return false;
            var owner = ParseOwner(File.ReadAllText(marker).Trim());
            return owner.Kind == OwnerKind.Owned
                && owner.Pid == Environment.ProcessId
                && (owner.StartedUtcTicks is null || SameStart(owner.StartedUtcTicks.Value, Process.GetCurrentProcess().StartTime));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool SameStart(long recordedTicks, DateTime observed)
    {
        var delta = recordedTicks - NormalizeTicks(observed);
        if (delta < 0)
            delta = -delta;
        return delta <= StartMatchToleranceTicks;
    }

    private static long NormalizeTicks(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return utc.Ticks - utc.Ticks % MicrosecondTicks;
    }

    private static string DirectoryName() =>
        DirectoryPrefix + ProcessToken() + "-" + Guid.NewGuid().ToString("N");

    private static string ProcessToken() =>
        Environment.ProcessId.ToString(CultureInfo.InvariantCulture);

    private enum OwnerKind
    {
        Absent,
        Unreadable,
        Garbled,
        Owned,
    }

    private readonly record struct OwnerRecord(OwnerKind Kind, int Pid, long? StartedUtcTicks)
    {
        internal static OwnerRecord Absent { get; } = new(OwnerKind.Absent, 0, null);

        internal static OwnerRecord Unreadable { get; } = new(OwnerKind.Unreadable, 0, null);

        internal static OwnerRecord Garbled { get; } = new(OwnerKind.Garbled, 0, null);
    }
}
