// Compiled file checks and reads of the test-phase evidence: the path identity taken before every evidence read and
// write, the atomic text write, and the bounded, deadline-limited reads used for sidecars, TRX and JSONL files
// (Write-TestPhaseTextAtomic, Open-TestPhaseReadStream, Read-TestPhaseBoundedBytes and the Read-TestPhaseJsonl*
// readers of test-phase-evidence.ps1, ported in W2-nightly S07). Plain C#: the JSONL records are what
// ConvertFrom-Json reads (NightlyJson.ParseText with dates). Kind is the same identity on both platforms (lstat on
// Linux) for the compiled readers (QualificationTiming). ASCII-only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Antiphon.TestPhase;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Nightly
{
    /// <summary>A bounded read: the bytes (null when refused), whether the whole file was read, and the refusal kind.</summary>
    public sealed class TestPhaseBytesRead
    {
        public byte[] Bytes { get; set; }
        public bool Complete { get; set; }
        public bool Refused { get; set; }
        public string Kind { get; set; } = "";

        internal static TestPhaseBytesRead Refusal(string kind) =>
            new TestPhaseBytesRead { Bytes = null, Complete = false, Refused = true, Kind = kind };
    }

    /// <summary>An opened evidence stream (Open-TestPhaseReadStream): the stream, or a refusal and its kind.</summary>
    public sealed class TestPhaseReadStream
    {
        public Stream Stream { get; set; }
        public bool Refused { get; set; }
        public string Kind { get; set; } = "";
    }

    /// <summary>JSONL records read from one file: what ConvertFrom-Json read for each record line, and the line counts.</summary>
    public sealed class TestPhaseJsonlRead
    {
        public object[] Records { get; set; } = new object[0];
        public int Torn { get; set; }
        public int Malformed { get; set; }
        public bool Complete { get; set; }
        public string Path { get; set; } = "";
    }

    /// <summary>The clock a read budget is spent on: a stopwatch, or a test's own clock.</summary>
    public interface ITestPhaseWatch
    {
        long ElapsedMilliseconds { get; }
        bool IsRunning { get; }
        void Start();
        void Stop();
        /// <summary>Called after a full re-read spent the budget (a test's clock may jump here).</summary>
        void OnFullRead();
    }

    /// <summary>A stopwatch as a read-budget clock.</summary>
    public sealed class TestPhaseStopwatch : ITestPhaseWatch
    {
        public TestPhaseStopwatch(Stopwatch inner) { Inner = inner ?? new Stopwatch(); }

        public Stopwatch Inner { get; }
        public long ElapsedMilliseconds => Inner.ElapsedMilliseconds;
        public bool IsRunning => Inner.IsRunning;
        public void Start() => Inner.Start();
        public void Stop() => Inner.Stop();
        public void OnFullRead() { }
    }

    /// <summary>
    /// The whole-summary re-read budget of ConvertTo-TestPhaseSummaryRows: one clock and a byte allowance shared by
    /// every shard re-read in full.
    /// </summary>
    public sealed class TestPhaseReadBudget
    {
        public ITestPhaseWatch Watch { get; set; }
        public int Milliseconds { get; set; } = TestPhaseFiles.SummaryBudgetMilliseconds;
        public int BytesRemaining { get; set; } = 8388608;

        /// <summary>A budget on a Stopwatch (null: a new one, not started) or a test clock.</summary>
        public static TestPhaseReadBudget For(object watch, int milliseconds, int bytesRemaining)
        {
            ITestPhaseWatch clock;
            if (watch == null) clock = new TestPhaseStopwatch(new Stopwatch());
            else if (watch is ITestPhaseWatch own) clock = own;
            else if (watch is Stopwatch stopwatch) clock = new TestPhaseStopwatch(stopwatch);
            else throw new ArgumentException("read-budget-watch " + watch.GetType().FullName);
            return new TestPhaseReadBudget { Watch = clock, Milliseconds = milliseconds, BytesRemaining = bytesRemaining };
        }
    }

    public static class TestPhaseFiles
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// The whole-summary re-read budget (Get-TestPhaseSummaryBudgetMilliseconds, A6): the former per-shard 5000 ms
        /// ceiling, so 387 shards cannot delay the receipt by about 32 minutes. The 200 ms / 262144 inline bound stays
        /// on its own watch.
        /// </summary>
        public const int SummaryBudgetMilliseconds = 5000;

        private static object Identity(string kind, long size)
        {
            var o = PsValue.Record(("Kind", kind), ("Size", size));
            return o;
        }

        /// <summary>
        /// Get-TestPhasePathIdentity on Windows: 'missing', 'unreadable', 'unsupported' (directory or link) or
        /// 'regular' with its length. A queued evidence write is a regular file of its queued length. As
        /// Get-Item without -Force, a hidden or system file reports length 0.
        /// </summary>
        public static object PathIdentity(string path)
        {
            string kind = Kind(path, out long size);
            return Identity(kind, size);
        }

        /// <summary>
        /// PathIdentity's kind and size without the wrapper object. On Linux: lstat, 'regular' for a regular
        /// file, 'unsupported' for anything else (a link, FIFO, directory), 'missing' for ENOENT.
        /// </summary>
        public static string Kind(string path, out long size)
        {
            size = 0;
            if (string.IsNullOrWhiteSpace(path)) return "missing";
            long queued = EvidenceFiles.PendingLength(path);
            if (queued >= 0) { size = queued; return "regular"; }
            if (!OperatingSystem.IsWindows()) return LinuxKind(path, out size);
            return WindowsKind(path, out size);
        }

        private static string LinuxKind(string path, out long size)
        {
            size = 0;
            if (FileIdentity.LStat(path, out LinuxStat stat) != 0) return Marshal.GetLastWin32Error() == 2 ? "missing" : "unreadable";
            size = stat.Size;
            return (stat.Mode & 0xF000) == 0x8000 ? "regular" : "unsupported";
        }

        private static string WindowsKind(string path, out long size)
        {
            size = 0;
            // A file (the usual answer): existence, attributes and length from one look at it. Anything else
            // (a directory, nothing there, a path File.Exists refuses such as one ending in a separator, an
            // error) is answered by the calls below, as before.
            try
            {
                var info = new FileInfo(path);
                string full = info.FullName;
                char last = full.Length > 0 ? full[full.Length - 1] : (char)0;
                if (last != Path.DirectorySeparatorChar && last != Path.AltDirectorySeparatorChar && info.Exists)
                {
                    FileAttributes seen = info.Attributes;
                    if ((seen & FileAttributes.Directory) == 0)
                    {
                        if ((seen & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) return "unsupported";
                        if ((seen & (FileAttributes.Hidden | FileAttributes.System)) == 0) size = info.Length;
                        return "regular";
                    }
                }
            }
            catch { }
            if (!File.Exists(path) && !Directory.Exists(path)) return "missing";
            FileAttributes attrs;
            try { attrs = File.GetAttributes(path); }
            catch { return "unreadable"; }
            if ((attrs & FileAttributes.Directory) == FileAttributes.Directory) return "unsupported";
            if ((attrs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) return "unsupported";
            if ((attrs & (FileAttributes.Hidden | FileAttributes.System)) == 0)
            {
                try { size = new FileInfo(path).Length; } catch { size = 0; }
            }
            return "regular";
        }

        // ---- writes ----

        /// <summary>
        /// Write-TestPhaseTextAtomic: the whole text (UTF-8, no byte-order mark) through a temporary file renamed
        /// over the path, so a reader sees the old or the new text. An existing directory, link or unreadable path is
        /// left alone. While evidence writes are deferred the write is queued instead (EvidenceFiles.WriteAtomic).
        /// </summary>
        public static void WriteTextAtomic(string path, string text)
        {
            if (EvidenceFiles.Buffered) { EvidenceFiles.WriteAtomic(path, text, true); return; }
            var kind = Kind(path, out _);
            if (kind == "unsupported" || kind == "unreadable") return;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !File.Exists(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, text ?? "", Utf8NoBom);
            if (kind == "regular")
            {
                var bak = path + ".bak";
                File.Replace(tmp, path, bak);
                RemoveQuietly(bak);
            }
            else File.Move(tmp, path);
        }

        // Remove-NightlyFileQuietly.
        private static void RemoveQuietly(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
            catch { }
        }

        // ---- bounded reads ----

        /// <summary>
        /// Read-TestPhaseBoundedBytes for a real stopwatch on Windows: at most maxBytes, stopping at the deadline on
        /// watch; Complete is false when bytes were left or the deadline passed. Refusals carry the identity kind,
        /// 'budget', 'unreadable', or 'deferred-write-failed'. A queued evidence write is waited on first. A failed
        /// write refuses and leaves the failure stored, so the read is not the previous bytes reported as complete.
        /// </summary>
        public static TestPhaseBytesRead BoundedRead(string path, int maxBytes, int deadlineMilliseconds, Stopwatch watch)
        {
            if (maxBytes < 1) maxBytes = 1;
            if (deadlineMilliseconds < 1) deadlineMilliseconds = 1;
            if (watch == null) watch = Stopwatch.StartNew();
            string kind = Kind(path, out _);
            if (kind != "regular") return TestPhaseBytesRead.Refusal(kind);
            if (watch.ElapsedMilliseconds >= deadlineMilliseconds) return TestPhaseBytesRead.Refusal("budget");
            EvidenceFiles.Wait(path);
            if (EvidenceFiles.PathFailed(path)) return TestPhaseBytesRead.Refusal("deferred-write-failed");
            FileStream stream;
            try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
            catch { return TestPhaseBytesRead.Refusal("unreadable"); }
            try { return StreamBytes(stream, maxBytes, deadlineMilliseconds, () => watch.ElapsedMilliseconds); }
            catch { return TestPhaseBytesRead.Refusal("unreadable"); }
            finally { stream.Dispose(); }
        }

        /// <summary>
        /// Read-TestPhaseBoundedBytes: BoundedRead, on any platform and on any read clock (a test's clock or the
        /// budget's). A failed deferred write is a refusal on every host and stays stored for the run summary. On Linux
        /// the file is opened without following a link and without blocking on a FIFO (OpenReadStream).
        /// </summary>
        public static TestPhaseBytesRead ReadBoundedBytes(string path, int maxBytes, int deadlineMilliseconds, ITestPhaseWatch watch)
        {
            TestPhaseReads.Warm();
            if (maxBytes < 1) maxBytes = 1;
            if (deadlineMilliseconds < 1) deadlineMilliseconds = 1;
            if (watch == null) { var started = new TestPhaseStopwatch(new Stopwatch()); started.Start(); watch = started; }
            if (EvidenceFiles.Buffered)
            {
                EvidenceFiles.Wait(path);
                if (EvidenceFiles.PathFailed(path)) return TestPhaseBytesRead.Refusal("deferred-write-failed");
            }
            if (watch is TestPhaseStopwatch stopwatch && OperatingSystem.IsWindows())
                return BoundedRead(path, maxBytes, deadlineMilliseconds, stopwatch.Inner);
            var identity = Kind(path, out _);
            if (identity != "regular") return TestPhaseBytesRead.Refusal(identity);
            if (watch.ElapsedMilliseconds >= deadlineMilliseconds) return TestPhaseBytesRead.Refusal("budget");
            var opened = OpenReadStream(path);
            if (opened.Refused || opened.Stream == null)
                return TestPhaseBytesRead.Refusal(string.IsNullOrWhiteSpace(opened.Kind) ? "unreadable" : opened.Kind);
            try
            {
                var clock = watch;
                return StreamBytes(opened.Stream, maxBytes, deadlineMilliseconds, () => clock.ElapsedMilliseconds);
            }
            catch { return TestPhaseBytesRead.Refusal("unreadable"); }
            finally { try { opened.Stream.Dispose(); } catch { } }
        }

        /// <summary>ReadBoundedBytes on a stopwatch (null: one started now).</summary>
        public static TestPhaseBytesRead ReadBoundedBytes(string path, int maxBytes, int deadlineMilliseconds, Stopwatch watch) =>
            ReadBoundedBytes(path, maxBytes, deadlineMilliseconds, new TestPhaseStopwatch(watch ?? Stopwatch.StartNew()));

        // Read-TestPhaseStreamBytes: at most maxBytes; Complete is false at the deadline or when a byte is left.
        private static TestPhaseBytesRead StreamBytes(Stream stream, int maxBytes, int deadlineMilliseconds, Func<long> elapsed)
        {
            var buffer = new byte[8192];
            var ms = new MemoryStream();
            bool complete = true;
            while (ms.Length < maxBytes)
            {
                if (elapsed() >= deadlineMilliseconds) { complete = false; break; }
                int room = maxBytes - (int)ms.Length;
                int want = Math.Min(8192, room);
                int count = stream.Read(buffer, 0, want);
                if (count <= 0) break;
                ms.Write(buffer, 0, count);
            }
            if (complete && ms.Length >= maxBytes)
            {
                if (elapsed() >= deadlineMilliseconds) complete = false;
                else if (stream.Read(buffer, 0, 1) > 0) complete = false;
            }
            return new TestPhaseBytesRead { Bytes = ms.ToArray(), Complete = complete, Refused = false, Kind = "regular" };
        }

        /// <summary>
        /// Open-TestPhaseReadStream: a read stream that never blocks on what is not a regular file. Windows: shared
        /// read (others may write or delete). Linux: open(O_RDONLY | O_NONBLOCK | O_CLOEXEC | O_NOFOLLOW), then fstat
        /// refuses anything that is not a regular file ('unsupported'). Queued writes of the path land first.
        /// </summary>
        public static TestPhaseReadStream OpenReadStream(string path)
        {
            var refused = new TestPhaseReadStream { Stream = null, Refused = true, Kind = "unreadable" };
            EvidenceFiles.FlushKeeping(path ?? "");
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    return new TestPhaseReadStream
                    {
                        Stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
                        Refused = false,
                        Kind = "regular",
                    };
                }
                catch { return refused; }
            }
            int fd = -1;
            SafeFileHandle handle = null;
            try
            {
                fd = FileIdentity.Open(path, 657408);
                if (fd < 0) return refused;
                if (FileIdentity.FStat(fd, out LinuxStat stat) != 0) return refused;
                if ((stat.Mode & 0xF000) != 0x8000) return new TestPhaseReadStream { Stream = null, Refused = true, Kind = "unsupported" };
                handle = new SafeFileHandle((IntPtr)fd, true);
                fd = -1;
                var stream = new FileStream(handle, FileAccess.Read, 8192, false);
                handle = null;
                return new TestPhaseReadStream { Stream = stream, Refused = false, Kind = "regular" };
            }
            catch { return refused; }
            finally
            {
                if (handle != null) { try { handle.Dispose(); } catch { } }
                else if (fd >= 0) { try { FileIdentity.Close(fd); } catch { } }
            }
        }
    }

    /// <summary>The JSONL readers of test-phase-evidence.ps1 (Read-TestPhaseJsonl*, ConvertTo-TestPhaseJsonlRecords).</summary>
    public static class TestPhaseReads
    {
        private static int warmed;

        /// <summary>
        /// Initialize-TestPhaseReadWarmup's part for the compiled readers: the JSON reader and the JSONL split run once
        /// before a measured call, so a fresh process does not spend the first read budget compiling them.
        /// </summary>
        internal static void Warm()
        {
            if (System.Threading.Interlocked.Exchange(ref warmed, 1) != 0) return;
            try
            {
                var bytes = Encoding.UTF8.GetBytes("{\"recordKind\":\"header\",\"epochUtc\":\"2026-10-29T00:00:00.0000000Z\",\"n\":1.5}\n{\"x\":");
                Records(new TestPhaseBytesRead { Bytes = bytes, Complete = false, Refused = false, Kind = "regular" }, 4000, 200, null);
            }
            catch { }
        }

        /// <summary>
        /// ConvertTo-TestPhaseJsonlRecords: each non-blank line that is one JSON object with a recordKind is a record (as
        /// ConvertFrom-Json reads it). Any other line is malformed, except the last line with content, which is torn. An
        /// incomplete read drops its unterminated tail and counts one torn line; so does reaching the line limit or the
        /// deadline.
        /// </summary>
        public static TestPhaseJsonlRead Records(TestPhaseBytesRead read, int maxLines, int deadlineMilliseconds, ITestPhaseWatch watch)
        {
            Warm();
            var records = new List<object>();
            int torn = 0;
            int malformed = 0;
            if (watch == null) { var started = new TestPhaseStopwatch(new Stopwatch()); started.Start(); watch = started; }
            if (deadlineMilliseconds < 1) deadlineMilliseconds = 1;
            if (read == null || read.Refused || read.Bytes == null)
                return new TestPhaseJsonlRead { Records = new object[0], Torn = 1, Malformed = 0, Complete = false };
            bool complete = read.Complete;
            if (!complete) torn = 1;
            var text = Encoding.UTF8.GetString(read.Bytes);
            // $text.EndsWith("`n") is a current-culture comparison in PowerShell.
            if (!complete && !text.EndsWith("\n", StringComparison.CurrentCulture))
            {
                var cut = text.LastIndexOf('\n');
                text = cut < 0 ? "" : text.Substring(0, cut + 1);
            }
            if (maxLines < 1) maxLines = 1;
            var parts = text.Split(new[] { "\n" }, StringSplitOptions.None);
            int limit = parts.Length;
            if (limit > 0 && text.EndsWith("\n", StringComparison.CurrentCulture)) limit--;
            int lines = 0;
            for (int i = 0; i < limit; i++)
            {
                if (watch.ElapsedMilliseconds >= deadlineMilliseconds) { complete = false; torn = 1; break; }
                if (lines >= maxLines) { complete = false; torn = 1; break; }
                var line = parts[i].TrimEnd('\r');
                lines++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                object parsed = null;
                try { parsed = NightlyJson.ParseText(line, 1024, true); } catch { parsed = null; }
                if (parsed != null && !string.IsNullOrWhiteSpace(PsValue.Str(PsValue.Prop(parsed, "recordKind"))))
                {
                    records.Add(parsed);
                    continue;
                }
                bool lastContent = true;
                for (int j = i + 1; j < limit; j++)
                {
                    if (!string.IsNullOrWhiteSpace(parts[j].TrimEnd('\r'))) { lastContent = false; break; }
                }
                if (lastContent) torn++; else malformed++;
            }
            if (lines >= maxLines) { complete = false; if (torn < 1) torn = 1; }
            return new TestPhaseJsonlRead { Records = records.ToArray(), Torn = torn, Malformed = malformed, Complete = complete };
        }

        /// <summary>
        /// Read-TestPhaseJsonlBounded: one bounded read (bytes, lines, deadline on watch; null: a stopwatch started
        /// now) and its records, with Complete.
        /// </summary>
        public static TestPhaseJsonlRead JsonlBounded(string path, int maxBytes, int maxLines, int deadlineMilliseconds, ITestPhaseWatch watch)
        {
            Warm();
            if (watch == null) watch = new TestPhaseStopwatch(Stopwatch.StartNew());
            var read = TestPhaseFiles.ReadBoundedBytes(path, maxBytes, deadlineMilliseconds, watch);
            var parsed = Records(read, maxLines, deadlineMilliseconds, watch);
            return new TestPhaseJsonlRead { Records = parsed.Records, Complete = parsed.Complete, Path = path ?? "" };
        }

        /// <summary>JsonlBounded on a stopwatch (null: one started now).</summary>
        public static TestPhaseJsonlRead JsonlBounded(string path, int maxBytes, int maxLines, int deadlineMilliseconds, Stopwatch watch) =>
            JsonlBounded(path, maxBytes, maxLines, deadlineMilliseconds, new TestPhaseStopwatch(watch ?? Stopwatch.StartNew()));

        /// <summary>
        /// Read-TestPhaseJsonlForSummary: the inline bounded read (262144 bytes, 4000 lines, 200 ms on its own watch);
        /// when that is incomplete and the shared budget has time and bytes left, one full re-read (the remaining
        /// bytes, 20000 lines) on the budget's clock, whose bytes are charged to the budget. Null budget: a fresh
        /// 5000 ms, 8 MiB allowance.
        /// </summary>
        public static TestPhaseJsonlRead JsonlForSummary(string path, TestPhaseReadBudget budget)
        {
            Warm();
            var inline = new TestPhaseStopwatch(Stopwatch.StartNew());
            var read = TestPhaseFiles.ReadBoundedBytes(path, 262144, 200, inline);
            var parsed = Records(read, 4000, 200, inline);
            if (!parsed.Complete)
            {
                ITestPhaseWatch watch = null;
                int deadline = TestPhaseFiles.SummaryBudgetMilliseconds;
                int maxBytes = 8388608;
                if (budget != null)
                {
                    watch = budget.Watch;
                    deadline = budget.Milliseconds;
                    maxBytes = budget.BytesRemaining;
                }
                if (watch == null) watch = new TestPhaseStopwatch(new Stopwatch());
                if (maxBytes > 0 && watch.ElapsedMilliseconds < deadline)
                {
                    if (!watch.IsRunning) watch.Start();
                    try
                    {
                        var full = TestPhaseFiles.ReadBoundedBytes(path, maxBytes, deadline, watch);
                        parsed = Records(full, 20000, deadline, watch);
                        if (budget != null && full != null && full.Bytes != null)
                        {
                            var left = budget.BytesRemaining - full.Bytes.Length;
                            budget.BytesRemaining = left < 0 ? 0 : left;
                        }
                    }
                    finally
                    {
                        if (watch.IsRunning) watch.Stop();
                    }
                    if (budget != null && budget.Watch != null) budget.Watch.OnFullRead();
                }
            }
            return new TestPhaseJsonlRead { Records = parsed.Records, Torn = parsed.Torn, Malformed = parsed.Malformed, Path = path ?? "" };
        }
    }
}
