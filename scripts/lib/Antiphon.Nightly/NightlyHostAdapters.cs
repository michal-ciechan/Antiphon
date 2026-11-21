// W2-nightly S12: host parts for a caller that is still PowerShell (plan 2026-11-21-w2-nightly-port-slices.md, section 2).
// nightly-tests-impl.ps1 (Get-NightlyRunHost) builds the NightlyHost of a chunk or an owned launch from its seams: the
// UtcNow seam as a clock (DelegateTimeProvider), the TracePath seam as the trace (AppendingTraceWriter), and the
// StartProcess, WaitCleanup and NativeActiveCount seams as a launcher (DelegateLauncher). Without seams it uses
// TimeProvider.System, no trace and OwnedLauncher. Plain C#: the delegates are the caller's; no PowerShell types. ASCII only.
#nullable disable
using System;
using System.IO;
using System.Text;

namespace Antiphon.Nightly
{
    /// <summary>A clock read from a delegate (Get-NightlyUtcNow over the UtcNow seam). Timestamps stay the system's.</summary>
    public sealed class DelegateTimeProvider : TimeProvider
    {
        private readonly Func<DateTime> _now;

        /// <summary>now answers the current time; a local time is converted, any other kind is taken as UTC.</summary>
        public DelegateTimeProvider(Func<DateTime> now)
        {
            _now = now ?? throw new ArgumentNullException(nameof(now));
        }

        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow()
        {
            var now = _now();
            return now.Kind == DateTimeKind.Local
                ? new DateTimeOffset(now).ToUniversalTime()
                : new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));
        }
    }

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
