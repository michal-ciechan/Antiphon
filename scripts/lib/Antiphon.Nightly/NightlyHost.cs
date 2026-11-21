// W2-nightly S01: the per-run host of the nightly C# (plan 2026-11-21-w2-nightly-port-slices.md, section 2). One
// NightlyHost carries every observation of the operating system a run makes: the clock, git, the launcher, the process
// table, the host probe and the three writers. It replaces $script:NightlySeams (nightly-common.ps1 Import-NightlySeams):
// the CLI builds NightlyHost.Production(...), a test builds one from fakes (NightlyFixture in
// tests/Antiphon.Tests/Scripts/Nightly). Nothing here is static state, so tests run side by side. Seams replace OS
// observation only, never decisions, and never cross a process. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Antiphon.Nightly
{
    /// <summary>A process's parent (Get-NightlyParentProcess): Id 0 and no start time when it cannot be read.</summary>
    public sealed record NightlyParent(int Id, DateTime? StartTimeUtc)
    {
        /// <summary>The unknown parent.</summary>
        public static NightlyParent None { get; } = new NightlyParent(0, null);
    }

    /// <summary>The process observations a run makes (the IsProcessAlive, GetProcessStartTimeUtc, GetParentProcess, GetBootId and GetHostId seams).</summary>
    public interface INightlyProcessTable
    {
        /// <summary>This process's id ($PID).</summary>
        int CurrentPid { get; }

        /// <summary>Get-NightlyProcessStartUtc: the process's start time in UTC, null when it is not running or cannot be read.</summary>
        DateTime? StartTimeUtc(int pid);

        /// <summary>Test-NightlyProcessAlive: false for an id of 0 or less and for an id that names no running process.</summary>
        bool IsAlive(int pid);

        /// <summary>Get-NightlyParentProcess: the parent's id and start time, NightlyParent.None when they cannot be read.</summary>
        NightlyParent Parent(int pid);

        /// <summary>Get-NightlyBootId: this boot's identity, empty when the host does not expose one.</summary>
        string BootId();

        /// <summary>Get-NightlyHostId: this machine's identity, empty when the host does not expose one.</summary>
        string HostId();
    }

    /// <summary>One preflight probe row: Docker (ExitCode, Version, Detail) or free disk (ExitCode, FreeBytes, MinimumFreeBytes).</summary>
    public sealed class NightlyProbeRow
    {
        /// <summary>0 when the probe passed.</summary>
        public int ExitCode { get; set; }

        /// <summary>Docker: the version text.</summary>
        public string Version { get; set; }

        /// <summary>Docker: what was probed.</summary>
        public string Detail { get; set; }

        /// <summary>Disk: the free bytes.</summary>
        public long? FreeBytes { get; set; }

        /// <summary>Disk: the floor the free bytes are held to.</summary>
        public long? MinimumFreeBytes { get; set; }
    }

    /// <summary>The host observations a run makes (the HostPlatform, DockerInfo, DiskFree and ToolchainVersions seams).</summary>
    public interface INightlyHostProbe
    {
        /// <summary>Get-NightlyExecutionPlatform's observation: "windows", "linux" or empty. The caller refuses anything else.</summary>
        string Platform();

        /// <summary>The Docker preflight row.</summary>
        NightlyProbeRow Docker();

        /// <summary>The free-disk preflight row.</summary>
        NightlyProbeRow DiskFree();

        /// <summary>Get-ToolchainFloorVersionText: the raw version text of "git" or "dotnet-sdk"; empty for another tool or when the tool cannot run.</summary>
        string ToolVersion(string tool);
    }

    /// <summary>
    /// One run's host. Every member is required: a test that forgets one fails to compile instead of observing the real
    /// machine. Out and Error are the run's stdout and stderr; Trace receives the trace lines (Write-NightlyTrace).
    /// </summary>
    public sealed class NightlyHost
    {
        /// <summary>The clock (Get-NightlyUtcNow): TimeProvider.System in production, FakeTimeProvider in tests.</summary>
        public required TimeProvider Clock { get; init; }

        /// <summary>git (Invoke-NightlyGit's seam). Called through RunGit, which traces it.</summary>
        public required NightlyGit Git { get; init; }

        /// <summary>Starts children. Called through Launch, which traces it.</summary>
        public required INightlyLauncher Launcher { get; init; }

        /// <summary>The process table.</summary>
        public required INightlyProcessTable Processes { get; init; }

        /// <summary>The host probe.</summary>
        public required INightlyHostProbe Probe { get; init; }

        /// <summary>The run's standard output.</summary>
        public required TextWriter Out { get; init; }

        /// <summary>The run's standard error.</summary>
        public required TextWriter Error { get; init; }

        /// <summary>The trace (the TracePath seam): TextWriter.Null in production.</summary>
        public required TextWriter Trace { get; init; }

        /// <summary>
        /// The production host: the system clock, NightlyNativeGit.Run, a WatchedLauncher that first lands queued
        /// evidence writes (their failures go to error as warnings), the real process table and host probe, and no trace.
        /// </summary>
        public static NightlyHost Production(TextWriter output, TextWriter error)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (error == null) throw new ArgumentNullException(nameof(error));
            return new NightlyHost
            {
                Clock = TimeProvider.System,
                Git = NightlyNativeGit.Run,
                Launcher = new WatchedLauncher(() =>
                {
                    foreach (var failure in EvidenceFiles.Flush()) error.WriteLine("WARNING: deferred-evidence-write-failed: " + failure);
                }),
                Processes = new NightlyProcessTable(),
                Probe = new NightlyHostProbe(),
                Out = output,
                Error = error,
                Trace = TextWriter.Null,
            };
        }

        /// <summary>The clock's now as a UTC DateTime (Get-NightlyUtcNow).</summary>
        public DateTime UtcNow => Clock.GetUtcNow().UtcDateTime;

        /// <summary>Write-NightlyTrace: one line "&lt;UtcNow round-trip&gt; &lt;kind&gt; &lt;message&gt;".</summary>
        public void WriteTrace(string kind, string message)
        {
            Trace.WriteLine(UtcNow.ToString("o", CultureInfo.InvariantCulture) + " " + kind + " " + message);
        }

        /// <summary>Invoke-NightlyGit: traces "git &lt;arguments&gt;", then runs Git.</summary>
        public NightlyGitResult RunGit(string workingDirectory, params string[] arguments)
        {
            WriteTrace("git", string.Join(" ", arguments ?? Array.Empty<string>()));
            return Git(workingDirectory, arguments ?? Array.Empty<string>());
        }

        /// <summary>Traces the command line ("exec" for Watched custody, "proc" for Owned), then runs it on Launcher.</summary>
        public NightlyLaunchResult Launch(NightlyLaunch launch)
        {
            if (launch == null) throw new ArgumentNullException(nameof(launch));
            WriteTrace(launch.Custody == NightlyCustody.Owned ? "proc" : "exec", launch.CommandText);
            return Launcher.Run(launch);
        }
    }

    /// <summary>The production process table: this machine's processes, boot and identity.</summary>
    public sealed class NightlyProcessTable : INightlyProcessTable
    {
        /// <inheritdoc />
        public int CurrentPid => Environment.ProcessId;

        /// <inheritdoc />
        public DateTime? StartTimeUtc(int pid)
        {
            if (pid <= 0) return null;
            try
            {
                using var process = Process.GetProcessById(pid);
                return process.StartTime.ToUniversalTime();
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is System.ComponentModel.Win32Exception || e is NotSupportedException)
            {
                return null;
            }
        }

        /// <inheritdoc />
        public bool IsAlive(int pid)
        {
            if (pid <= 0) return false;
            try
            {
                using var process = Process.GetProcessById(pid);
                return true;
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            {
                return false;
            }
        }

        /// <inheritdoc />
        public NightlyParent Parent(int pid)
        {
            try
            {
                // The process's own record on Windows, /proc on Linux.
                var parent = NightlyProcesses.ParentProcessId(pid);
                if (parent < 0) return NightlyParent.None;
                return new NightlyParent(parent, StartTimeUtc(parent));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException || e is InvalidOperationException || e is System.ComponentModel.Win32Exception)
            {
                return NightlyParent.None;
            }
        }

        /// <inheritdoc />
        public string BootId()
        {
            if (OperatingSystem.IsWindows())
            {
                var value = ReadRegistry(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", "BootId");
                if (value == null) return "";
                try { return Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture); }
                catch (Exception e) when (e is FormatException || e is InvalidCastException || e is OverflowException) { return ""; }
            }
            if (OperatingSystem.IsLinux()) return ReadTrimmed("/proc/sys/kernel/random/boot_id");
            return "";
        }

        /// <inheritdoc />
        public string HostId()
        {
            if (OperatingSystem.IsWindows())
            {
                var value = ReadRegistry(@"SOFTWARE\Microsoft\Cryptography", "MachineGuid") as string;
                return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
            }
            if (OperatingSystem.IsLinux())
            {
                foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
                {
                    var text = ReadTrimmed(path);
                    if (text.Length > 0) return text;
                }
            }
            return "";
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static object ReadRegistry(string key, string name)
        {
            try
            {
                using var opened = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
                return opened?.GetValue(name);
            }
            catch (Exception e) when (e is System.Security.SecurityException || e is UnauthorizedAccessException || e is IOException)
            {
                return null;
            }
        }

        private static string ReadTrimmed(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path).Trim() : ""; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return ""; }
        }
    }

    /// <summary>
    /// The production host probe. Docker and DiskFree return the rows nightly-tests-impl.ps1 records when no seam is
    /// set: the run does not probe either today (Docker "unprobed-default", 50 GB free over a 10 GB floor).
    /// </summary>
    public sealed class NightlyHostProbe : INightlyHostProbe
    {
        private const long GiB = 1024L * 1024L * 1024L;

        /// <inheritdoc />
        public string Platform()
        {
            if (Path.DirectorySeparatorChar == '\\') return "windows";
            if (OperatingSystem.IsLinux()) return "linux";
            return "";
        }

        /// <inheritdoc />
        public NightlyProbeRow Docker() => new NightlyProbeRow { ExitCode = 0, Version = "unprobed-default", Detail = "caller host" };

        /// <inheritdoc />
        public NightlyProbeRow DiskFree() => new NightlyProbeRow { ExitCode = 0, FreeBytes = 50 * GiB, MinimumFreeBytes = 10 * GiB };

        /// <inheritdoc />
        public string ToolVersion(string tool) => ToolchainFloor.VersionText(tool);
    }
}
