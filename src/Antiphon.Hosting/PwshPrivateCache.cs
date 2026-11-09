using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Antiphon.Hosting;

/// <summary>
/// One process-private XDG cache so concurrent pwsh starts do not share a JIT profile.
/// CARD-1793: the runner gives each agent session its own owned cache, and every install
/// removes family directories whose recorded owner is no longer a live process.
/// CARD-1813: the owner token is pid plus a start generation, unmarked directories expire,
/// and a stamp limits the scan to once per <see cref="SweepInterval"/>. On Linux the
/// generation is /proc stat starttime (clock ticks since boot), which does not move when
/// the wall clock steps. Windows keeps <see cref="Process.StartTime"/>.
/// </summary>
internal static class PwshPrivateCache
{
    internal const string VariableName = "XDG_CACHE_HOME";
    internal const string ProbeVariable = "ANTIPHON_PWSH_CACHE_PROBE";
    internal const string RootVariable = "ANTIPHON_PWSH_CACHE_ROOT";
    internal const string DirectoryPrefix = "antiphon-pwsh-cache-";
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan UnmarkedGrace = TimeSpan.FromMinutes(10);

    // Windows Process.StartTime can disagree by a few milliseconds across readers.
    // Fifty milliseconds still rejects pid reuse: the replacement cannot have started
    // until the recorded process had already exited. Linux compares /proc starttime exactly.
    internal static readonly long StartMatchToleranceTicks = TimeSpan.FromMilliseconds(50).Ticks;

    // Tests replace the platform start read. Null uses /proc starttime on Linux and
    // Process.StartTime on Windows.
    internal static Func<int, StartIdentity>? StartIdentitySource;

    // Tests supply /proc/<pid>/stat text. FileNotFoundException stands for a missing stat
    // (hidepid or no /proc). Null reads the real file.
    internal static Func<int, string>? ProcStatReader;

    // Tests report that a pid whose stat is missing may still be alive. Null uses kill(pid, 0).
    internal static Func<int, bool>? LinuxProcessPresence;

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

            var choice = ChooseRoot();
            if (choice.Sweep)
                SweepStale(choice.Path);
            var directory = Path.Combine(choice.Path, DirectoryName());
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
            else if (IsLive(owner))
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
    // Mutation sets TMPDIR, TMP and TEMP (or this override) on the test host so a sweep mutant
    // cannot see /tmp. A missing path is created. A symlink, a file, or an access error skips
    // the sweep and creates under the process temp root.
    internal static string CacheRoot() => ChooseRoot().Path;

    // Where a witness looks. The configured override when it is set, otherwise the process
    // temp root. Independent of ChooseRoot, so a lookup that hard-codes the temp parent
    // fails while the override names a real directory.
    internal static string ConfiguredCacheParent()
    {
        var configured = Environment.GetEnvironmentVariable(RootVariable);
        var parent = string.IsNullOrWhiteSpace(configured) ? Path.GetTempPath() : configured;
        return Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private readonly record struct RootChoice(string Path, bool Sweep);

    private static RootChoice ChooseRoot()
    {
        var configured = Environment.GetEnvironmentVariable(RootVariable);
        if (string.IsNullOrWhiteSpace(configured))
            return new(Path.GetTempPath(), true);

        try
        {
            var full = Path.GetFullPath(configured);
            if (IsUnusableRoot(full))
                return new(Path.GetTempPath(), false);
            Directory.CreateDirectory(full);
            if (IsUnusableRoot(full))
                return new(Path.GetTempPath(), false);
            return new(full, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return new(Path.GetTempPath(), false);
        }
    }

    private static bool IsUnusableRoot(string full)
    {
        var link = new FileInfo(full);
        if (link.LinkTarget is not null)
            return true;
        if (!link.Exists && !Directory.Exists(full))
            return false;
        var attributes = File.GetAttributes(full);
        return (attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0;
    }

    private static bool IsLive(OwnerRecord owner)
    {
        if (owner.Pid <= 0)
            return false;
        var identity = ReadStart(owner.Pid);
        return identity.Kind switch
        {
            StartReadKind.Absent => false,
            StartReadKind.Unknown => true,
            _ => StampMatches(owner, identity),
        };
    }

    // Linux records /proc/<pid>/stat field 22 (clock ticks since boot) and compares it exactly.
    // A wall-clock step does not move that field. Windows keeps Process.StartTime. An old
    // pid-only marker, and on Linux an old pid@wallTicks marker, stays while the pid exists.
    private static bool StampMatches(OwnerRecord owner, StartIdentity identity)
    {
        if (owner.KernelTicks is long kernel)
            return identity.KernelTicks == kernel;
        if (owner.WallTicks is long wall)
        {
            if (identity.KernelTicks is not null)
                return true;
            if (identity.WallTicks is long observed)
                return SameStart(wall, observed);
            return true;
        }

        return true;
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

    // "pid" (legacy), "pid@wallTicks" (Windows, and Linux markers written before kernel ticks),
    // or "pid@k<starttime>" (Linux /proc stat field 22). Anything else is garbled and kept.
    // The k prefix keeps an older reader, which only accepts a bare integer after @, from
    // treating the kernel generation as a wall-clock instant and deleting a live cache.
    private static OwnerRecord ParseOwner(string text)
    {
        var separator = text.IndexOf('@');
        var pidText = separator >= 0 ? text[..separator] : text;
        if (!int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
            return OwnerRecord.Garbled;

        if (separator < 0)
            return new OwnerRecord(OwnerKind.Owned, pid, null, null);

        var suffix = text[(separator + 1)..];
        if (suffix.Length >= 2 && suffix[0] == 'k'
            && long.TryParse(suffix[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var kernel)
            && kernel >= 0)
            return new OwnerRecord(OwnerKind.Owned, pid, kernel, null);

        if (!long.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var wall) || wall <= 0)
            return OwnerRecord.Garbled;
        return new OwnerRecord(OwnerKind.Owned, pid, null, wall);
    }

    // Write then move, so a concurrent sweep never reads a truncated pid. Retry the replace:
    // on Windows a concurrent reader can refuse the first MoveFileEx.
    private static void WriteOwner(string directory, int ownerPid, DateTime? startedUtc)
    {
        var token = ownerPid.ToString(CultureInfo.InvariantCulture);
        var identity = ReadStart(ownerPid);
        if (identity.Kind == StartReadKind.Present)
        {
            if (identity.KernelTicks is long kernel)
                token += "@k" + kernel.ToString(CultureInfo.InvariantCulture);
            else if (WallToken(startedUtc, identity) is long wall)
                token += "@" + wall.ToString(CultureInfo.InvariantCulture);
        }

        var temporary = Path.Combine(directory, MarkerName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(temporary, token);
        MoveIntoPlace(temporary, Path.Combine(directory, MarkerName));
    }

    // Linux production ignores a caller-supplied wall clock: AssignOwner reads /proc itself.
    // Windows prefers the caller's StartTime, then the live process. An injected wall-only
    // source supplies the ticks directly.
    private static long? WallToken(DateTime? startedUtc, StartIdentity identity)
    {
        if (OperatingSystem.IsLinux() && StartIdentitySource is null)
            return null;
        if (StartIdentitySource is null && startedUtc is DateTime supplied)
            return NormalizeTicks(supplied);
        return identity.WallTicks;
    }

    private static StartIdentity ReadStart(int pid)
    {
        if (StartIdentitySource is { } source)
            return source(pid);
        if (pid <= 0)
            return StartIdentity.Absent;
        return OperatingSystem.IsLinux() ? ReadLinuxStart(pid) : ReadWindowsStart(pid);
    }

    private static StartIdentity ReadLinuxStart(int pid)
    {
        try
        {
            var stat = ProcStatReader is { } read
                ? read(pid)
                : File.ReadAllText("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/stat");
            return TryParseProcStartTicks(stat, out var ticks)
                ? StartIdentity.Kernel(ticks)
                : StartIdentity.Unknown;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // A missing stat is still treated as dead. LinuxProcessPresence is observed so a
            // witness can show that a live pid does not keep the cache yet.
            if (LinuxProcessPresence is { } presence)
                _ = presence(pid);
            return StartIdentity.Absent;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return StartIdentity.Unknown;
        }
    }

    private static StartIdentity ReadWindowsStart(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return new StartIdentity(StartReadKind.Present, null, NormalizeTicks(process.StartTime));
        }
        catch (ArgumentException)
        {
            return StartIdentity.Absent;
        }
        catch (InvalidOperationException)
        {
            // The process existed but its start could not be read. Keeping the cache is the safe side.
            return StartIdentity.Unknown;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return StartIdentity.Unknown;
        }
    }

    internal static bool TryReadKernelStartTicks(int pid, out long ticks)
    {
        ticks = 0;
        if (!OperatingSystem.IsLinux() || pid <= 0)
            return false;
        var identity = ReadLinuxStart(pid);
        if (identity.KernelTicks is not long value)
            return false;
        ticks = value;
        return true;
    }

    // comm (field 2) can contain spaces and parentheses. Only the final ')' ends it.
    // Field 22, starttime, is then fields[19]. Same parse as LinuxProcessIdentity.
    internal static bool TryParseProcStartTicks(string stat, out long ticks)
    {
        ticks = 0;
        var endName = stat.LastIndexOf(')');
        if (endName < 0 || endName + 2 >= stat.Length)
            return false;
        var fields = stat[(endName + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 19 && fields[0].Length == 1
            && long.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out ticks)
            && ticks >= 0;
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
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(stamp);
            return age >= TimeSpan.Zero && age < SweepInterval;
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
                && IsLive(owner);
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

    private static bool SameStart(long recordedTicks, long observedTicks)
    {
        var delta = recordedTicks - observedTicks;
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

    private readonly record struct OwnerRecord(OwnerKind Kind, int Pid, long? KernelTicks, long? WallTicks)
    {
        internal static OwnerRecord Absent { get; } = new(OwnerKind.Absent, 0, null, null);

        internal static OwnerRecord Unreadable { get; } = new(OwnerKind.Unreadable, 0, null, null);

        internal static OwnerRecord Garbled { get; } = new(OwnerKind.Garbled, 0, null, null);
    }
}
