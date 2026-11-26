// W2-nightly S12: host parts built from delegates (plan 2026-11-21-w2-nightly-port-slices.md, section 2). The C487 harness
// (scripts/lib/c487-nightly-tests.ps1, W2-nightly S17; nightly-tests-impl.ps1 Get-NightlyRunHost before) built the NightlyHost
// of a nightly tests run from its seams with them; unit 62 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md
// deleted that harness, and the test seams (SeamedNightlyRun, NightlyTestsWorld) build them from C# delegates: the trace file
// (AppendingTraceWriter), a launcher (DelegateLauncher, whose log writer the live-lane discovery shares) and the probe
// (DelegateHostProbe). The clock of the UtcNow seam (DelegateTimeProvider) went with its last caller. Plain C#: the delegates
// are the caller's; no PowerShell types. ASCII only.
#nullable disable
using System;
using System.IO;
using System.Text;

namespace Antiphon.Nightly
{
    /// <summary>
    /// The trace file of the TracePath seam (Write-NightlyTrace): each line appended as ASCII with the platform newline,
    /// the file opened for that line only, so PowerShell writers of the same file interleave.
    /// </summary>
    public sealed class AppendingTraceWriter : TextWriter
    {
        private readonly string _path;

        /// <summary>A writer appending to path (a full path).</summary>
        public AppendingTraceWriter(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A trace path is required.", nameof(path));
            _path = path;
        }

        /// <summary>The trace path.</summary>
        public string Path => _path;

        /// <inheritdoc />
        public override Encoding Encoding => Encoding.ASCII;

        /// <inheritdoc />
        public override void Write(char value) => File.AppendAllText(_path, value.ToString(), Encoding.ASCII);

        /// <inheritdoc />
        public override void Write(string value)
        {
            if (!string.IsNullOrEmpty(value)) File.AppendAllText(_path, value, Encoding.ASCII);
        }

        /// <inheritdoc />
        public override void WriteLine(string value) => File.AppendAllText(_path, (value ?? "") + NewLine, Encoding.ASCII);
    }

    /// <summary>
    /// W2-nightly S17: a host probe whose answers delegates give (the HostPlatform, DockerInfo, DiskFree and
    /// ToolchainVersions seams of a caller that is still PowerShell); a null delegate answers as the fallback probe.
    /// </summary>
    public sealed class DelegateHostProbe : INightlyHostProbe
    {
        private readonly Func<string> _platform;
        private readonly Func<NightlyProbeRow> _docker;
        private readonly Func<NightlyProbeRow> _diskFree;
        private readonly Func<string, string> _toolVersion;
        private readonly INightlyHostProbe _fallback;

        /// <summary>Each delegate may be null; fallback null is the production probe (NightlyHostProbe).</summary>
        public DelegateHostProbe(Func<string> platform, Func<NightlyProbeRow> docker, Func<NightlyProbeRow> diskFree, Func<string, string> toolVersion,
            INightlyHostProbe fallback)
        {
            _platform = platform;
            _docker = docker;
            _diskFree = diskFree;
            _toolVersion = toolVersion;
            _fallback = fallback ?? new NightlyHostProbe();
        }

        /// <inheritdoc />
        public string Platform() => _platform != null ? _platform() : _fallback.Platform();

        /// <inheritdoc />
        public NightlyProbeRow Docker() => _docker != null ? _docker() : _fallback.Docker();

        /// <inheritdoc />
        public NightlyProbeRow DiskFree() => _diskFree != null ? _diskFree() : _fallback.DiskFree();

        /// <inheritdoc />
        public string ToolVersion(string tool) => _toolVersion != null ? _toolVersion(tool) : _fallback.ToolVersion(tool);
    }

    /// <summary>
    /// A launcher whose launches a delegate answers (the StartProcess seam of Invoke-NightlyOwnedProcess): the delegate's
    /// record is read as the result (NightlyLaunchResult.FromRecord) and its LogText becomes the launch's log, as the seam
    /// path wrote it. WaitCleanup is the waitCleanup delegate over the record, else CleanupComplete, else ChildrenExited
    /// (Wait-NightlyOwnedCleanup under the seam); NativeActiveCount is the activeCount delegate, else 0.
    /// </summary>
    public sealed class DelegateLauncher : INightlyLauncher
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly Func<NightlyLaunch, object> _start;
        private readonly Func<object, bool> _waitCleanup;
        private readonly Func<int> _activeCount;
        private readonly TextWriter _error;

        /// <summary>start answers each launch; the others may be null. error takes a log that could not be written (null: dropped).</summary>
        public DelegateLauncher(Func<NightlyLaunch, object> start, Func<object, bool> waitCleanup, Func<int> activeCount, TextWriter error)
        {
            _start = start ?? throw new ArgumentNullException(nameof(start));
            _waitCleanup = waitCleanup;
            _activeCount = activeCount;
            _error = error;
        }

        /// <inheritdoc />
        public NightlyLaunchResult Run(NightlyLaunch launch)
        {
            if (launch == null) throw new ArgumentNullException(nameof(launch));
            var result = NightlyLaunchResult.FromRecord(_start(launch));
            var failure = WriteLog(launch.LogPath, result.LogText);
            if (failure != null) _error?.WriteLine("ERROR: " + failure);
            return result;
        }

        /// <inheritdoc />
        public bool WaitCleanup(NightlyLaunchResult result)
        {
            if (result == null) return false;
            if (_waitCleanup != null) return _waitCleanup(result.Record ?? result);
            return result.CleanupComplete ?? result.ChildrenExited;
        }

        /// <inheritdoc />
        public int NativeActiveCount => _activeCount?.Invoke() ?? 0;

        /// <summary>
        /// The seam path's log (Set-Content -Encoding UTF8): logText and a newline in logPath, UTF-8 without a byte-order
        /// mark, when both are set. Returns the failure's message, or null.
        /// </summary>
        public static string WriteLog(string logPath, string logText)
        {
            if (string.IsNullOrEmpty(logPath) || string.IsNullOrEmpty(logText)) return null;
            try
            {
                File.WriteAllText(logPath, logText + Environment.NewLine, Utf8NoBom);
                return null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                return e.Message;
            }
        }
    }
}
