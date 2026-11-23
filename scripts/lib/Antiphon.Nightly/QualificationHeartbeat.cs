// W2-nightly S10: the qualification supervisor heartbeat, compiled (was scripts/lib/qualification-supervisor-heartbeat.ps1).
// Detection only: nothing here stops, signals or restarts a process. Four parts:
//   - the heartbeat: the idle clock over the chunk's process subtree, its record, the file the supervisor writes and the
//     reader of the diagnostic stream (Update, Json, Write, Read, ReadDiagnosticProgress), and the supervisor's loop
//     state with its clock and process table seams (QualificationHeartbeatMonitor, IQualificationProcessTable);
//   - the active-chunk identity (active-chunk.json) the executor writes at every native chunk start (Active) and end
//     (Idle), and its reader (WriteActiveChunkIdentity, ActiveChunkWriter, ReadActiveChunkIdentity);
//   - the planned cost of the active chunk (PlannedCost): the planned seconds the identity, the planned index, the
//     suite's plan and the journal's generation prefix (TestPhaseJournal.ReadGenerationPrefix) all bind, or unknown;
//   - the idle budget a planned cost gives (IdleBudget).
// Plain C#: no PowerShell types, so a PowerShell caller and a plain one get the same objects. The binding files are
// read as ConvertFrom-Json read them (property names case-insensitive, a name repeated in another case or empty
// refused, comments and trailing commas accepted). ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>One row of the process table: the process, its parent and its CPU seconds (kernel plus user).</summary>
    public sealed record QualificationProcess(int Pid, int ParentPid, double CpuSeconds);

    /// <summary>The process table the heartbeat observes (Get-QualificationProcessTable).</summary>
    public interface IQualificationProcessTable
    {
        /// <summary>Every process this host can see.</summary>
        IReadOnlyList<QualificationProcess> Snapshot();
    }

    /// <summary>
    /// The production process table: every process with its parent (the process's own record on Windows, /proc on
    /// Linux; 0 when unreadable) and its total processor time (0 when unreadable).
    /// </summary>
    public sealed class QualificationProcessTable : IQualificationProcessTable
    {
        /// <inheritdoc />
        public IReadOnlyList<QualificationProcess> Snapshot()
        {
            var rows = new List<QualificationProcess>();
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    int pid;
                    try { pid = process.Id; }
                    catch (InvalidOperationException) { continue; }
                    var parent = NightlyProcesses.ParentProcessId(pid);
                    if (parent < 0) parent = 0;
                    var cpu = 0.0;
                    try { cpu = process.TotalProcessorTime.TotalSeconds; }
                    catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception || e is NotSupportedException || e is UnauthorizedAccessException) { cpu = 0.0; }
                    rows.Add(new QualificationProcess(pid, parent, cpu));
                }
            }
            return rows;
        }
    }

    /// <summary>One CPU sample of a heartbeat subject.</summary>
    public sealed record QualificationCpuSample(int Pid, double CpuSeconds);

    /// <summary>The idle budget (Get-QualificationHeartbeatIdleBudget): minutes and where they came from.</summary>
    public sealed record QualificationIdleBudget(double Minutes, string Source);

    /// <summary>
    /// One heartbeat (supervisor-heartbeat.json). Times are round-trip UTC text; the carried fields
    /// (lastCompletedUid, ...) are null until a diagnostic names them.
    /// </summary>
    public sealed class QualificationHeartbeatRecord
    {
        public double CpuSeconds { get; set; }
        public long Bytes { get; set; }
        public double IdleMinutes { get; set; }
        public double Trx { get; set; }
        public string Newest { get; set; } = "";
        public bool HangSuspected { get; set; }
        public string HangReason { get; set; } = "";
        public int[] SuspectPids { get; set; } = new int[0];
        public double IdleBudgetMinutes { get; set; }
        public string IdleBudgetSource { get; set; } = "";
        public string IdleSinceUtc { get; set; }
        public string ObservedAtUtc { get; set; } = "";
        public QualificationCpuSample[] HostCpu { get; set; } = new QualificationCpuSample[0];
        public string LastCompletedUid { get; set; }
        public string LastCompletedAtUtc { get; set; }
        public string LastDiagGrowthAtUtc { get; set; }
        public string InFlightUid { get; set; }
        public string LastPhase { get; set; }
    }

    /// <summary>What a read of the chunk's diagnostic stream found (Read-QualificationDiagnosticProgress).</summary>
    public sealed class QualificationDiagnosticProgress
    {
        /// <summary>A test node state line was read.</summary>
        public bool Grew { get; set; }

        /// <summary>Where the next read starts.</summary>
        public long Offset { get; set; }

        public string LastCompletedUid { get; set; }

        /// <summary>Never set by the reader (kept for the record's shape).</summary>
        public string LastCompletedAtUtc { get; set; }

        public string InFlightUid { get; set; }
        public string LastPhase { get; set; }
    }

    /// <summary>One heartbeat's inputs (Update-QualificationSupervisorHeartbeat's parameters).</summary>
    public sealed class QualificationHeartbeatInput
    {
        public DateTime NowUtc { get; set; }
        public IReadOnlyList<QualificationProcess> Processes { get; set; } = new QualificationProcess[0];
        public int ChunkRootPid { get; set; }
        public IReadOnlyList<int> ExcludedPids { get; set; } = new int[0];
        public QualificationHeartbeatRecord Previous { get; set; }
        public double Trx { get; set; }
        public long Bytes { get; set; }
        public string Newest { get; set; } = "";
        public double? PlannedSeconds { get; set; }
        public double IdleCpuSecondsPerMinute { get; set; } = 5;
        public double DefaultIdleMinutes { get; set; } = 20;
        public double IdleFloorMinutes { get; set; } = 10;
        public QualificationDiagnosticProgress DiagnosticProgress { get; set; }
    }

    /// <summary>
    /// active-chunk.json as Read-QualificationActiveChunkIdentity accepted it, with the SHA-256 of exactly the bytes read
    /// (IdentityDigest).
    /// </summary>
    public sealed class QualificationActiveChunk
    {
        public string Format { get; set; }
        public string State { get; set; }
        public long Generation { get; set; }
        public string RunId { get; set; }
        public string Attempt { get; set; }
        public string Suite { get; set; }
        public string Chunk { get; set; }
        public string PlanFile { get; set; }
        public string PlanDigest { get; set; }
        public string SourceDigest { get; set; }
        public string PolicyDigest { get; set; }
        public string CostFileDigest { get; set; }
        public string AssemblyDigest { get; set; }
        public string IdentityDigest { get; set; }
    }

    /// <summary>An active-chunk write: Written, or the refusal (Write-QualificationActiveChunkIdentity's warning text).</summary>
    public sealed record QualificationActiveChunkWrite(bool Written, string Reason);

    /// <summary>The planned cost of the active chunk: Seconds, or null and the refusal (Get-QualificationGroupPlannedSeconds).</summary>
    public sealed record QualificationPlannedCost(double? Seconds, string Reason);

    /// <summary>
    /// The supervisor's heartbeat loop (QualificationSupervise, the qualification-supervise command): the clock and the process table are seams; the previous
    /// heartbeat is kept between polls, and only once its file is written.
    /// </summary>
    public sealed class QualificationHeartbeatMonitor
    {
        private readonly TimeProvider _clock;
        private readonly IQualificationProcessTable _processes;

        public QualificationHeartbeatMonitor(TimeProvider clock, IQualificationProcessTable processes, double defaultIdleMinutes)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _processes = processes ?? throw new ArgumentNullException(nameof(processes));
            DefaultIdleMinutes = defaultIdleMinutes;
        }

        /// <summary>The system clock and this host's process table.</summary>
        public static QualificationHeartbeatMonitor Production(double defaultIdleMinutes) =>
            new QualificationHeartbeatMonitor(TimeProvider.System, new QualificationProcessTable(), defaultIdleMinutes);

        /// <summary>The idle budget of a chunk with no planned cost (qualification-supervise --hang-minutes).</summary>
        public double DefaultIdleMinutes { get; }

        /// <summary>The last heartbeat written; null before the first.</summary>
        public QualificationHeartbeatRecord Previous { get; private set; }

        /// <summary>
        /// One poll: a heartbeat of the chunk root's subtree (the excluded pids and their subtrees left out) at the clock's
        /// now, written to path. Throws when the table or the write fails; the previous heartbeat then stays.
        /// </summary>
        public QualificationHeartbeatRecord Beat(string path, int chunkRootPid, int[] excludedPids, double trx, long bytes, string newest)
        {
            var beat = QualificationHeartbeat.Update(new QualificationHeartbeatInput
            {
                NowUtc = _clock.GetUtcNow().UtcDateTime,
                Processes = _processes.Snapshot(),
                ChunkRootPid = chunkRootPid,
                ExcludedPids = excludedPids ?? new int[0],
                Previous = Previous,
                Trx = trx,
                Bytes = bytes,
                Newest = newest ?? "",
                PlannedSeconds = null,
                DefaultIdleMinutes = DefaultIdleMinutes,
            });
            QualificationHeartbeat.Write(beat, path);
            Previous = beat;
            return beat;
        }
    }

    public static class QualificationHeartbeat
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        // ---- the idle budget and the heartbeat ----

        /// <summary>
        /// Get-QualificationHeartbeatIdleBudget: the planned seconds in minutes, at least floorMinutes; defaultMinutes
        /// ("default-unknown-group") when the cost is unknown.
        /// </summary>
        public static QualificationIdleBudget IdleBudget(double? plannedSeconds) => IdleBudget(plannedSeconds, 10, 20);

        public static QualificationIdleBudget IdleBudget(double? plannedSeconds, double floorMinutes, double defaultMinutes)
        {
            if (plannedSeconds == null) return new QualificationIdleBudget(defaultMinutes, "default-unknown-group");
            var minutes = plannedSeconds.Value / 60.0;
            if (minutes < floorMinutes) minutes = floorMinutes;
            return new QualificationIdleBudget(minutes, "plannedSeconds");
        }

        /// <summary>
        /// Get-QualificationHeartbeatSubjects: the chunk root's subtree in the table, depth first (children pushed in table
        /// order, so the last-listed child is visited first); an excluded process and its subtree are left out. Empty when
        /// the root is 0 or not in the table.
        /// </summary>
        public static List<QualificationProcess> Subjects(IReadOnlyList<QualificationProcess> processes, int chunkRootPid, IEnumerable<int> excludedPids)
        {
            var byPid = new Dictionary<int, QualificationProcess>();
            var children = new Dictionary<int, List<int>>();
            foreach (var proc in processes ?? new QualificationProcess[0])
            {
                if (proc == null) continue;
                byPid[proc.Pid] = proc;
                if (!children.TryGetValue(proc.ParentPid, out var list)) children[proc.ParentPid] = list = new List<int>();
                list.Add(proc.Pid);
            }
            var excluded = new HashSet<int>(excludedPids ?? new int[0]);
            var subjects = new List<QualificationProcess>();
            if (chunkRootPid <= 0 || !byPid.ContainsKey(chunkRootPid)) return subjects;
            var pending = new Stack<int>();
            pending.Push(chunkRootPid);
            var seen = new HashSet<int>();
            while (pending.Count > 0)
            {
                var pid = pending.Pop();
                if (!seen.Add(pid)) continue;
                if (excluded.Contains(pid)) continue;
                subjects.Add(byPid[pid]);
                if (children.TryGetValue(pid, out var kids))
                    foreach (var child in kids) pending.Push(child);
            }
            return subjects;
        }

        /// <summary>
        /// Update-QualificationSupervisorHeartbeat. The chunk is quiet when the subjects' CPU per minute since the previous
        /// heartbeat is under IdleCpuSecondsPerMinute and neither the TRX count, the chunk-log bytes nor the diagnostic
        /// stream grew; it hangs once it has been quiet for the idle budget. The supervisor's own CPU never counts: its
        /// pid is excluded.
        /// </summary>
        public static QualificationHeartbeatRecord Update(QualificationHeartbeatInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var now = input.NowUtc.ToUniversalTime();
            var budget = IdleBudget(input.PlannedSeconds, input.IdleFloorMinutes, input.DefaultIdleMinutes);
            var subjects = Subjects(input.Processes, input.ChunkRootPid, input.ExcludedPids);
            var previous = input.Previous;
            var priorCpu = new Dictionary<int, double>();
            if (previous?.HostCpu != null)
                foreach (var row in previous.HostCpu)
                    if (row != null) priorCpu[row.Pid] = Text15(row.CpuSeconds);
            var previousAt = ParseTime(previous?.ObservedAtUtc);
            var cpuSeconds = 0.0;
            var delta = 0.0;
            var samples = new List<QualificationCpuSample>();
            var subjectIds = new List<int>();
            foreach (var proc in subjects)
            {
                var current = Text15(proc.CpuSeconds);
                cpuSeconds += current;
                if (priorCpu.TryGetValue(proc.Pid, out var prior)) delta += current - prior;
                subjectIds.Add(proc.Pid);
                samples.Add(new QualificationCpuSample(proc.Pid, current));
            }
            var trxChanged = false;
            var bytesChanged = false;
            var elapsedMinutes = 0.0;
            var measurable = false;
            if (previousAt != null)
            {
                elapsedMinutes = (now - previousAt.Value).TotalMinutes;
                if (elapsedMinutes > 0)
                {
                    measurable = true;
                    if (Text15(previous.Trx) != input.Trx) trxChanged = true;
                    if (WholeText15(previous.Bytes) != input.Bytes) bytesChanged = true;
                }
            }
            var cpuPerMinute = 0.0;
            if (measurable && elapsedMinutes > 0) cpuPerMinute = delta / elapsedMinutes;
            var progress = input.DiagnosticProgress;
            var diagGrew = progress != null && progress.Grew;
            var quiet = measurable && cpuPerMinute < input.IdleCpuSecondsPerMinute && !trxChanged && !bytesChanged && !diagGrew;
            var lastCompletedUid = previous?.LastCompletedUid;
            var lastCompletedAtUtc = previous?.LastCompletedAtUtc;
            var lastDiagGrowthAtUtc = previous?.LastDiagGrowthAtUtc;
            var inFlightUid = previous?.InFlightUid;
            var lastPhase = previous?.LastPhase;
            var nowText = now.ToString("o", Invariant);
            if (diagGrew)
            {
                lastDiagGrowthAtUtc = nowText;
                if (!string.IsNullOrWhiteSpace(progress.LastCompletedUid))
                {
                    lastCompletedUid = progress.LastCompletedUid;
                    lastCompletedAtUtc = nowText;
                }
                if (!string.IsNullOrWhiteSpace(progress.InFlightUid)) inFlightUid = progress.InFlightUid;
                if (!string.IsNullOrWhiteSpace(progress.LastPhase)) lastPhase = progress.LastPhase;
            }
            DateTime? idleSince = null;
            var idleMinutes = 0.0;
            if (quiet)
            {
                idleSince = ParseTime(previous.IdleSinceUtc) ?? previousAt;
                idleMinutes = (now - idleSince.Value).TotalMinutes;
            }
            var hang = quiet && idleMinutes >= budget.Minutes;
            var suspect = new SortedSet<int>();
            if (hang)
            {
                if (subjectIds.Count == 0 && input.ChunkRootPid > 0) suspect.Add(input.ChunkRootPid);
                foreach (var pid in subjectIds) suspect.Add(pid);
            }
            var suspectSorted = new int[suspect.Count];
            suspect.CopyTo(suspectSorted);
            var reason = "";
            if (hang)
            {
                var listed = string.Join(",", Array.ConvertAll(suspectSorted, p => p.ToString(Invariant)));
                var idleText = string.Format(Invariant, "{0:0.##}", idleMinutes);
                var rateText = string.Format(Invariant, "{0:0.##}", cpuPerMinute);
                var limitText = string.Format(Invariant, "{0:0.##}", input.IdleCpuSecondsPerMinute);
                var budgetText = string.Format(Invariant, "{0:0.##}", budget.Minutes);
                reason = subjectIds.Count == 0
                    ? "no test-host process under the chunk command; no trx growth and no chunk-log bytes for " + idleText + " minutes; cpu " + rateText + "/min under " + limitText + "/min; budget " + budgetText + " minutes (" + budget.Source + "); suspectPids " + listed
                    : "test hosts idle for " + idleText + " minutes: no trx growth, no chunk-log bytes, cpu " + rateText + "/min under " + limitText + "/min; budget " + budgetText + " minutes (" + budget.Source + "); suspectPids " + listed;
            }
            return new QualificationHeartbeatRecord
            {
                CpuSeconds = cpuSeconds,
                Bytes = input.Bytes,
                IdleMinutes = idleMinutes,
                Trx = input.Trx,
                Newest = input.Newest ?? "",
                HangSuspected = hang,
                HangReason = reason,
                SuspectPids = suspectSorted,
                IdleBudgetMinutes = budget.Minutes,
                IdleBudgetSource = budget.Source,
                IdleSinceUtc = idleSince?.ToUniversalTime().ToString("o", Invariant),
                ObservedAtUtc = nowText,
                HostCpu = samples.ToArray(),
                LastCompletedUid = lastCompletedUid,
                LastCompletedAtUtc = lastCompletedAtUtc,
                LastDiagGrowthAtUtc = lastDiagGrowthAtUtc,
                InFlightUid = inFlightUid,
                LastPhase = lastPhase,
            };
        }

        // ConvertTo-QualificationHeartbeatDouble of a number: its [string] (15 significant digits) read back. The CPU
        // samples, the sums and deltas over them and the previous counters are taken through it.
        private static double Text15(double value) =>
            double.TryParse(value.ToString("G15", Invariant), NumberStyles.Float, Invariant, out var parsed) ? parsed : 0.0;

        // [int64] of that (half to even).
        private static long WholeText15(long value) => (long)Math.Round(Text15(value), MidpointRounding.ToEven);

        // ConvertFrom-QualificationHeartbeatTime: round-trip or other invariant text, read as UTC.
        private static DateTime? ParseTime(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (DateTime.TryParse(text, Invariant, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)) return parsed;
            return null;
        }

        /// <summary>ConvertTo-QualificationSupervisorHeartbeatJson: one compact line, the field order fixed.</summary>
        public static string Json(QualificationHeartbeatRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var sb = new StringBuilder("{");
            sb.Append("\"cpuSeconds\":").Append(Number(record.CpuSeconds));
            sb.Append(",\"bytes\":").Append(WholeText15(record.Bytes).ToString(Invariant));
            sb.Append(",\"idleMinutes\":").Append(Number(record.IdleMinutes));
            sb.Append(",\"trx\":").Append(Number(record.Trx));
            sb.Append(",\"newest\":\"").Append(Escape(record.Newest)).Append('"');
            sb.Append(",\"hangSuspected\":").Append(record.HangSuspected ? "true" : "false");
            sb.Append(",\"hangReason\":\"").Append(Escape(record.HangReason)).Append('"');
            sb.Append(",\"suspectPids\":[").Append(string.Join(",", Array.ConvertAll(record.SuspectPids ?? new int[0], p => p.ToString(Invariant)))).Append(']');
            sb.Append(",\"idleBudgetMinutes\":").Append(Number(record.IdleBudgetMinutes));
            sb.Append(",\"idleBudgetSource\":\"").Append(Escape(record.IdleBudgetSource)).Append('"');
            sb.Append(",\"idleSinceUtc\":").Append(Optional(record.IdleSinceUtc));
            sb.Append(",\"observedAtUtc\":\"").Append(Escape(record.ObservedAtUtc)).Append('"');
            sb.Append(",\"hostCpu\":[");
            var first = true;
            foreach (var row in record.HostCpu ?? new QualificationCpuSample[0])
            {
                if (row == null) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"pid\":").Append(row.Pid.ToString(Invariant)).Append(",\"cpuSeconds\":").Append(Number(row.CpuSeconds)).Append('}');
            }
            sb.Append(']');
            sb.Append(",\"lastCompletedUid\":").Append(Optional(record.LastCompletedUid));
            sb.Append(",\"lastCompletedAtUtc\":").Append(Optional(record.LastCompletedAtUtc));
            sb.Append(",\"lastDiagGrowthAtUtc\":").Append(Optional(record.LastDiagGrowthAtUtc));
            sb.Append(",\"inFlightUid\":").Append(Optional(record.InFlightUid));
            sb.Append(",\"lastPhase\":").Append(Optional(record.LastPhase));
            return sb.Append('}').ToString();
        }

        private static string Number(double value) => value.ToString("G15", Invariant);

        private static string Optional(string text) => string.IsNullOrWhiteSpace(text) ? "null" : "\"" + Escape(text) + "\"";

        // ConvertTo-QualificationHeartbeatJsonString: the quote, the backslash, \n \r \t and \u00xx for the other
        // control characters; everything else as is.
        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (ch == '"') sb.Append("\\\"");
                else if (ch == '\\') sb.Append("\\\\");
                else if (ch == '\n') sb.Append("\\n");
                else if (ch == '\r') sb.Append("\\r");
                else if (ch == '\t') sb.Append("\\t");
                else if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4", Invariant));
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Write-QualificationSupervisorHeartbeat: the JSON to path + ".tmp" (UTF-8, no byte-order mark), then moved over
        /// path. The directory is created when missing.
        /// </summary>
        public static void Write(QualificationHeartbeatRecord record, string path)
        {
            var json = Json(record);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
            var temp = path + ".tmp";
            File.WriteAllText(temp, json, Utf8NoBom);
            File.Move(temp, path, true);
        }

        /// <summary>Read-QualificationSupervisorHeartbeat: the record a heartbeat file holds (strict UTF-8).</summary>
        public static QualificationHeartbeatRecord Read(string path)
        {
            var text = File.ReadAllText(path, new UTF8Encoding(false, true));
            var root = BindingJson.Parse(text);
            var record = new QualificationHeartbeatRecord
            {
                CpuSeconds = BindingJson.Tolerant(BindingJson.Get(root, "cpuSeconds")),
                Bytes = (long)Math.Round(BindingJson.Tolerant(BindingJson.Get(root, "bytes")), MidpointRounding.ToEven),
                IdleMinutes = BindingJson.Tolerant(BindingJson.Get(root, "idleMinutes")),
                Trx = BindingJson.Tolerant(BindingJson.Get(root, "trx")),
                Newest = BindingJson.Str(BindingJson.Get(root, "newest")),
                HangSuspected = BindingJson.Get(root, "hangSuspected") is { ValueKind: JsonValueKind.True },
                HangReason = BindingJson.Str(BindingJson.Get(root, "hangReason")),
                IdleBudgetMinutes = BindingJson.Tolerant(BindingJson.Get(root, "idleBudgetMinutes")),
                IdleBudgetSource = BindingJson.Str(BindingJson.Get(root, "idleBudgetSource")),
                IdleSinceUtc = BindingJson.Text(BindingJson.Get(root, "idleSinceUtc")),
                ObservedAtUtc = BindingJson.Str(BindingJson.Get(root, "observedAtUtc")),
                LastCompletedUid = BindingJson.Text(BindingJson.Get(root, "lastCompletedUid")),
                LastCompletedAtUtc = BindingJson.Text(BindingJson.Get(root, "lastCompletedAtUtc")),
                LastDiagGrowthAtUtc = BindingJson.Text(BindingJson.Get(root, "lastDiagGrowthAtUtc")),
                InFlightUid = BindingJson.Text(BindingJson.Get(root, "inFlightUid")),
                LastPhase = BindingJson.Text(BindingJson.Get(root, "lastPhase")),
            };
            var pids = new List<int>();
            foreach (var item in BindingJson.Items(BindingJson.Get(root, "suspectPids")))
                if (item.ValueKind != JsonValueKind.Null) pids.Add((int)Math.Round(BindingJson.Tolerant(item), MidpointRounding.ToEven));
            record.SuspectPids = pids.ToArray();
            var samples = new List<QualificationCpuSample>();
            foreach (var item in BindingJson.Items(BindingJson.Get(root, "hostCpu")))
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                samples.Add(new QualificationCpuSample((int)Math.Round(BindingJson.Tolerant(BindingJson.Get(item, "pid")), MidpointRounding.ToEven),
                    BindingJson.Tolerant(BindingJson.Get(item, "cpuSeconds"))));
            }
            record.HostCpu = samples.ToArray();
            return record;
        }

        private static readonly Regex UidPattern = new Regex(@"TestNodeUid\s*\{\s*Value\s*=\s*([^\s\}]+)\s*\}", RegexOptions.CultureInvariant);
        private static readonly Regex StatePattern = new Regex("(Passed|Failed|Timeout|Cancelled|Skipped|InProgress|Discovered)TestNodeStateProperty", RegexOptions.CultureInvariant);

        /// <summary>Read-QualificationDiagnosticProgress with its 1 MiB read.</summary>
        public static QualificationDiagnosticProgress ReadDiagnosticProgress(string path, long offset, string heartbeatPath) =>
            ReadDiagnosticProgress(path, offset, heartbeatPath, 1048576);

        /// <summary>
        /// Read-QualificationDiagnosticProgress: the test node state lines added to the chunk's diagnostic stream after
        /// offset (whole lines only, unless the read reaches the end), at most maxBytes. The heartbeat file itself is never
        /// progress. Offset moves past what was read.
        /// </summary>
        public static QualificationDiagnosticProgress ReadDiagnosticProgress(string path, long offset, string heartbeatPath, int maxBytes)
        {
            var result = new QualificationDiagnosticProgress { Offset = offset };
            if (!string.IsNullOrWhiteSpace(heartbeatPath))
            {
                string left, right;
                try { left = Path.GetFullPath(path); } catch (Exception) { left = path; }
                try { right = Path.GetFullPath(heartbeatPath); } catch (Exception) { right = heartbeatPath; }
                if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase)) return result;
            }
            if (!File.Exists(path)) return result;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = stream.Length;
            if (offset < 0) offset = 0;
            if (offset > length) offset = length;
            var available = length - offset;
            if (available <= 0)
            {
                result.Offset = offset;
                return result;
            }
            var take = maxBytes;
            if (available < take) take = (int)available;
            stream.Seek(offset, SeekOrigin.Begin);
            var buffer = new byte[take];
            var read = stream.Read(buffer, 0, take);
            if (read <= 0) return result;
            var reachedEof = offset + read >= length;
            var use = read;
            if (!reachedEof)
            {
                var lastNl = -1;
                for (var i = 0; i < read; i++) if (buffer[i] == 10) lastNl = i;
                if (lastNl < 0) return result;
                use = lastNl + 1;
            }
            var text = Encoding.UTF8.GetString(buffer, 0, use);
            var grew = false;
            foreach (var line in text.Split('\n'))
            {
                var uid = UidPattern.Match(line);
                var state = StatePattern.Match(line);
                if (!uid.Success || !state.Success) continue;
                grew = true;
                var uidText = uid.Groups[1].Value;
                var stateText = state.Groups[1].Value;
                result.LastPhase = stateText;
                if (stateText == "Passed" || stateText == "Failed" || stateText == "Timeout" || stateText == "Cancelled" || stateText == "Skipped")
                    result.LastCompletedUid = uidText;
                else if (stateText == "InProgress")
                    result.InFlightUid = uidText;
            }
            result.Grew = grew;
            result.Offset = offset + use;
            return result;
        }

        // ---- the active-chunk identity ----

        private static readonly Regex SuitePattern = new Regex("^[a-z0-9][a-z0-9-]{0,40}$");
        private static readonly Regex RunPattern = new Regex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$");
        private static readonly Regex AttemptPattern = new Regex("^[1-9][0-9]*$");
        private static readonly Regex ChunkPattern = new Regex("^[A-Za-z0-9][A-Za-z0-9._-]{0,200}$");
        private static readonly Regex Sha256Pattern = new Regex("^[a-f0-9]{64}$");
        private static readonly Regex Sha1Pattern = new Regex("^[a-f0-9]{40}$");
        private static readonly Regex GenerationPattern = new Regex("^[a-f0-9]{32}$");
        private static readonly Regex PlanFilePattern = new Regex(@"^groups-planned(?:-index|-[a-z0-9][a-z0-9-]{0,40})?\.json$");
        private static readonly Regex RecordFilePattern = new Regex(@"^[0-9]{8}\.json$");
        private static readonly string[] IdentityStringFields =
        {
            "format", "state", "runId", "attempt", "suite", "chunk", "planFile", "planDigest",
            "sourceDigest", "policyDigest", "costFileDigest", "assemblyDigest",
        };
        private static readonly string[] BindingFields = { "sourceDigest", "policyDigest", "costFileDigest", "assemblyDigest" };
        private const string IdentityFormat = "antiphon-qualification-active-chunk-v1";

        /// <summary>
        /// Read-QualificationActiveChunkIdentity: directory/active-chunk.json when it is a regular file holding a valid
        /// identity (the twelve text fields under their exact names, a positive whole generation, the run, attempt, chunk
        /// and suite patterns, state Active or Idle, the plan file of its suite, a SHA-256 plan digest, no identityDigest),
        /// stamped with the SHA-256 of exactly the bytes read; null otherwise. A queued evidence write is read from memory.
        /// </summary>
        public static QualificationActiveChunk ReadActiveChunkIdentity(string directory)
        {
            try
            {
                var path = Path.Combine(directory, "active-chunk.json");
                if (TestPhaseFiles.Kind(path, out _) != "regular") return null;
                var bytes = EvidenceFiles.ReadAllBytes(path);
                var root = BindingJson.Parse(TestPhaseRecords.DecodeText(bytes));
                if (root.ValueKind != JsonValueKind.Object) return null;
                var exact = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in root.EnumerateObject()) exact.Add(property.Name);
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var field in IdentityStringFields)
                {
                    // The exact spelling, holding text that ConvertFrom-Json kept as text (not a date).
                    if (!exact.Contains(field)) return null;
                    var value = BindingJson.Get(root, field);
                    if (value?.ValueKind != JsonValueKind.String) return null;
                    var text = value.Value.GetString();
                    if (NightlyJson.ReadsAsDate(text)) return null;
                    values[field] = text;
                }
                var generation = BindingJson.Get(root, "generation");
                if (!BindingJson.IsWhole(generation)) return null;
                var generationValue = generation.Value.GetInt64();
                if (!RunPattern.IsMatch(values["runId"]) || !AttemptPattern.IsMatch(values["attempt"]) || !ChunkPattern.IsMatch(values["chunk"])) return null;
                if (values["format"] != IdentityFormat || (values["state"] != "Active" && values["state"] != "Idle") || generationValue <= 0
                    || !SuitePattern.IsMatch(values["suite"]) || values["planFile"] != "groups-planned-" + values["suite"] + ".json"
                    || !Sha256Pattern.IsMatch(values["planDigest"])) return null;
                if (BindingJson.Has(root, "identityDigest")) return null;
                return new QualificationActiveChunk
                {
                    Format = values["format"],
                    State = values["state"],
                    Generation = generationValue,
                    RunId = values["runId"],
                    Attempt = values["attempt"],
                    Suite = values["suite"],
                    Chunk = values["chunk"],
                    PlanFile = values["planFile"],
                    PlanDigest = values["planDigest"],
                    SourceDigest = values["sourceDigest"],
                    PolicyDigest = values["policyDigest"],
                    CostFileDigest = values["costFileDigest"],
                    AssemblyDigest = values["assemblyDigest"],
                    IdentityDigest = TestPhaseRecords.Sha256Hex(bytes),
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The active-chunk writer a run binds (NightlyRunState.ActiveChunk): Write-QualificationActiveChunkIdentity for
        /// each chunk start and end, its refusal to warning ("active-identity-unavailable: reason"). It keeps the parses
        /// of the suite plans and the planned index while their bytes are unchanged.
        /// </summary>
        public static Action<NightlyActiveChunk> ActiveChunkWriter(Action<string> warning)
        {
            var snapshots = new PlanSnapshots();
            return active =>
            {
                if (active == null) return;
                var written = WriteActiveChunkIdentity(snapshots, active.Directory, active.Suite, active.Chunk, active.RunId, active.Attempt, active.State);
                if (!written.Written) warning?.Invoke("active-identity-unavailable: " + written.Reason);
            };
        }

        /// <summary>WriteActiveChunkIdentity in the Active state.</summary>
        public static QualificationActiveChunkWrite WriteActiveChunkIdentity(string directory, string suiteId, string chunkId, string runId, string attempt) =>
            WriteActiveChunkIdentity(new PlanSnapshots(), directory, suiteId, chunkId, runId, attempt, "Active");

        /// <summary>
        /// Write-QualificationActiveChunkIdentity: directory/active-chunk.json naming the suite's chunk, bound to the
        /// suite's plan (groups-planned-&lt;suite&gt;.json, by digest) and to the planned index's row for the suite. The
        /// generation counts up within one run and attempt. Refused (nothing written) when an argument is outside its
        /// pattern or the plan, the index and the arguments disagree. During a run the write is queued (EvidenceFiles).
        /// </summary>
        public static QualificationActiveChunkWrite WriteActiveChunkIdentity(string directory, string suiteId, string chunkId, string runId, string attempt, string state) =>
            WriteActiveChunkIdentity(new PlanSnapshots(), directory, suiteId, chunkId, runId, attempt, state);

        private static QualificationActiveChunkWrite WriteActiveChunkIdentity(PlanSnapshots snapshots, string directory, string suiteId, string chunkId, string runId, string attempt, string state)
        {
            try
            {
                if ((state != "Active" && state != "Idle")
                    || !SuitePattern.IsMatch(suiteId ?? "") || !RunPattern.IsMatch(runId ?? "") || !AttemptPattern.IsMatch(attempt ?? "") || !ChunkPattern.IsMatch(chunkId ?? ""))
                    throw new InvalidOperationException("active-identity-schema");
                var file = "groups-planned-" + suiteId + ".json";
                var snapshot = snapshots.Read(Path.Combine(directory ?? "", file), false);
                var plan = snapshot.Root;
                var indexPath = Path.Combine(directory ?? "", "groups-planned-index.json");
                PlanSnapshot indexSnapshot;
                try { indexSnapshot = snapshots.Read(indexPath, true); }
                catch (Exception) { indexSnapshot = null; }
                JsonElement index;
                List<JsonElement> rows;
                if (indexSnapshot == null)
                {
                    index = ReadBindingJson(indexPath);
                    rows = SuiteRows(index, suiteId);
                }
                else
                {
                    index = indexSnapshot.Root;
                    rows = indexSnapshot.Rows(suiteId);
                }
                snapshot.Chunks.TryGetValue(chunkId, out var chunkCount);
                if (rows.Count != 1 || !BindingJson.Same(BindingJson.Get(rows[0], "file"), file)
                    || !BindingJson.Same(BindingJson.Get(index, "runId"), runId) || BindingJson.Str(BindingJson.Get(index, "attempt")) != attempt
                    || !BindingJson.Same(BindingJson.Get(plan, "runId"), runId) || BindingJson.Str(BindingJson.Get(plan, "attempt")) != attempt
                    || !BindingJson.Same(BindingJson.Get(plan, "suite"), suiteId) || chunkCount != 1)
                    throw new InvalidOperationException("active-plan-binding");
                var old = ReadActiveChunkIdentity(directory);
                object generation = 1;
                if (old != null && old.RunId == runId && old.Attempt == attempt) generation = old.Generation + 1;
                var record = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
                {
                    { "format", IdentityFormat }, { "state", state }, { "generation", generation }, { "runId", runId }, { "attempt", attempt },
                    { "suite", suiteId }, { "chunk", chunkId }, { "planFile", file }, { "planDigest", snapshot.Digest },
                };
                foreach (var field in BindingFields)
                {
                    var planValue = BindingJson.Str(BindingJson.Get(plan, field));
                    if (BindingJson.Str(BindingJson.Get(rows[0], field)) != planValue) throw new InvalidOperationException("active-plan-" + field);
                    record[field] = planValue;
                }
                // Rewritten at every batch start and end: queued during a run (EvidenceFiles), atomic either way. A
                // directory in its place refuses at once (its rename would only be retried).
                var activePath = Path.Combine(directory ?? "", "active-chunk.json");
                if (!EvidenceFiles.Buffered && Directory.Exists(activePath)) throw new InvalidOperationException("active-identity-unwritable " + activePath);
                EvidenceFiles.WriteAtomic(activePath, TestPhaseRecords.ActiveChunkJson(record), false);
                return new QualificationActiveChunkWrite(true, "");
            }
            catch (Exception e)
            {
                return new QualificationActiveChunkWrite(false, e.Message);
            }
        }

        private static List<JsonElement> SuiteRows(JsonElement index, string suiteId)
        {
            var rows = new List<JsonElement>();
            foreach (var row in BindingJson.Items(BindingJson.Get(index, "suites")))
                if (BindingJson.Same(BindingJson.Get(row, "suite"), suiteId)) rows.Add(row);
            return rows;
        }

        // A parsed binding file, keyed by the digest of the bytes it was parsed from. The plan counts its groups per
        // chunk; the index keeps the rows of each suite asked for.
        private sealed class PlanSnapshot
        {
            private readonly Dictionary<string, List<JsonElement>> _rows = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);

            public JsonElement Root { get; init; }
            public string Digest { get; init; }
            public Dictionary<string, int> Chunks { get; init; }

            public List<JsonElement> Rows(string suiteId)
            {
                lock (_rows)
                {
                    if (!_rows.TryGetValue(suiteId, out var rows)) _rows[suiteId] = rows = SuiteRows(Root, suiteId);
                    return rows;
                }
            }
        }

        // Read-QualificationPlanSnapshot: the suite's plan is written once and re-validated on every active-chunk write
        // (twice per chunk); its parse is reused while its bytes are unchanged (the key is the SHA-256 the record
        // publishes as planDigest, taken from the same bytes that are parsed). At most 16 are kept.
        private sealed class PlanSnapshots
        {
            private readonly Dictionary<string, PlanSnapshot> _kept = new Dictionary<string, PlanSnapshot>(StringComparer.Ordinal);

            public PlanSnapshot Read(string path, bool index)
            {
                if (TestPhaseFiles.Kind(path, out _) != "regular") throw new InvalidOperationException("binding-file-unavailable");
                var kind = index ? "index|" : "plan|";
                var key = kind + path + "|" + EvidenceFiles.CachedSha256(path);
                lock (_kept)
                {
                    if (_kept.TryGetValue(key, out var kept)) return kept;
                }
                var bytes = EvidenceFiles.ReadAllBytes(path);
                var digest = TestPhaseRecords.Sha256Hex(bytes);
                key = kind + path + "|" + digest;
                lock (_kept)
                {
                    if (_kept.TryGetValue(key, out var kept)) return kept;
                }
                var root = BindingJson.Parse(TestPhaseRecords.DecodeText(bytes));
                Dictionary<string, int> chunks = null;
                if (!index)
                {
                    chunks = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var group in BindingJson.Items(BindingJson.Get(root, "groups")))
                    {
                        var chunk = BindingJson.Str(BindingJson.Get(group, "chunk"));
                        chunks.TryGetValue(chunk, out var count);
                        chunks[chunk] = count + 1;
                    }
                }
                var snapshot = new PlanSnapshot { Root = root, Digest = digest, Chunks = chunks };
                lock (_kept)
                {
                    if (_kept.Count >= 16) _kept.Clear();
                    _kept[key] = snapshot;
                }
                return snapshot;
            }
        }

        // Read-QualificationPlanJson: a regular file's text (a queued write from memory), parsed.
        private static JsonElement ReadBindingJson(string path)
        {
            if (TestPhaseFiles.Kind(path, out _) != "regular") throw new InvalidOperationException("binding-file-unavailable");
            return BindingJson.Parse(EvidenceFiles.ReadAllText(path));
        }

        // Get-NightlyFileSha256: the file's digest as a reader sees it; a missing path throws.
        private static string FileSha256(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !EvidenceFiles.PathExists(path)) throw new InvalidOperationException("missing file for hash " + path);
            return EvidenceFiles.Sha256(path);
        }

        // Sync-NightlyDeferredEvidenceWrites: queued evidence writes land first (a failure stays stored for the run verdict).
        private static void LandQueuedWrites()
        {
            if (EvidenceFiles.Buffered) EvidenceFiles.Flush();
        }

        // ---- the planned cost ----

        /// <summary>PlannedCost with the identity unchecked.</summary>
        public static QualificationPlannedCost PlannedCost(string path, string chunkId) => PlannedCost(path, chunkId, null, null, null, null);

        /// <summary>PlannedCost with no observer.</summary>
        public static QualificationPlannedCost PlannedCost(string path, string chunkId, string suiteId, string runId, string attempt) =>
            PlannedCost(path, chunkId, suiteId, runId, attempt, null);

        /// <summary>
        /// Get-QualificationGroupPlannedSeconds: the planned seconds of chunkId in the plan the active identity binds, read
        /// from the timing root of path (a groups-planned*.json file). Unknown (null, with the refusal) unless the identity
        /// is Active for that chunk with proven digests; the planned index, the suite's plan (by digest) and the identity
        /// agree; the chunk is not settled (its timing sidecar, the journal); and the identity did not change while it was
        /// read. suiteId, runId and attempt, when not null, must equal the identity's. identityRead (a test seam) runs
        /// before the identity's bytes are compared again.
        /// </summary>
        public static QualificationPlannedCost PlannedCost(string path, string chunkId, string suiteId, string runId, string attempt, Action<string> identityRead)
        {
            LandQueuedWrites();
            try
            {
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(chunkId)) throw new InvalidOperationException("missing-path-or-chunk");
                if (TestPhaseFiles.Kind(path, out _) != "regular" || !PlanFilePattern.IsMatch(Path.GetFileName(path))) throw new InvalidOperationException("supplied-plan-unavailable");
                var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                var activePath = Path.Combine(dir, "active-chunk.json");
                var active = ReadActiveChunkIdentity(dir);
                if (active == null || active.State != "Active") throw new InvalidOperationException("active-unavailable");
                if (suiteId != null && (suiteId.Length == 0 || suiteId != active.Suite)) throw new InvalidOperationException("identity-suite");
                if (runId != null && (runId.Length == 0 || runId != active.RunId)) throw new InvalidOperationException("identity-runId");
                if (attempt != null && (attempt.Length == 0 || attempt != active.Attempt)) throw new InvalidOperationException("identity-attempt");
                if (active.Chunk != chunkId) throw new InvalidOperationException("identity-chunk");
                foreach (var field in BindingFields)
                {
                    var pattern = field == "sourceDigest" ? Sha1Pattern : Sha256Pattern;
                    if (!pattern.IsMatch(ActiveField(active, field) ?? "")) throw new InvalidOperationException("plan-binding-unproven-" + field);
                }
                var index = ReadBindingJson(Path.Combine(dir, "groups-planned-index.json"));
                if (!VersionOne(BindingJson.Get(index, "version")) || BindingJson.Get(index, "suites")?.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("index-schema");
                if (!BindingJson.Same(BindingJson.Get(index, "runId"), active.RunId) || BindingJson.Str(BindingJson.Get(index, "attempt")) != active.Attempt) throw new InvalidOperationException("index-identity");
                var rows = SuiteRows(index, active.Suite);
                if (rows.Count != 1) throw new InvalidOperationException("index-suite");
                var row = rows[0];
                if (!BindingJson.Same(BindingJson.Get(row, "file"), active.PlanFile) || !BindingJson.Same(BindingJson.Get(row, "runId"), active.RunId)
                    || BindingJson.Str(BindingJson.Get(row, "attempt")) != active.Attempt) throw new InvalidOperationException("index-plan");
                var planPath = Path.Combine(dir, active.PlanFile);
                var plan = ReadBindingJson(planPath);
                if (!VersionOne(BindingJson.Get(plan, "version")) || BindingJson.Get(plan, "groups")?.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("plan-schema");
                if (FileSha256(planPath) != active.PlanDigest) throw new InvalidOperationException("plan-digest");
                if (!BindingJson.Same(BindingJson.Get(plan, "suite"), active.Suite) || !BindingJson.Same(BindingJson.Get(plan, "runId"), active.RunId)
                    || BindingJson.Str(BindingJson.Get(plan, "attempt")) != active.Attempt) throw new InvalidOperationException("plan-identity");
                foreach (var field in BindingFields)
                {
                    var bound = ActiveField(active, field);
                    if (BindingJson.Str(BindingJson.Get(row, field)) != bound || BindingJson.Str(BindingJson.Get(plan, field)) != bound) throw new InvalidOperationException("plan-binding-" + field);
                }
                var groups = new List<JsonElement>();
                foreach (var group in BindingJson.Items(BindingJson.Get(plan, "groups")))
                    if (BindingJson.Same(BindingJson.Get(group, "chunk"), chunkId)) groups.Add(group);
                if (groups.Count != 1) throw new InvalidOperationException("plan-chunk");
                // A failed Idle write cannot make a previous settled chunk look live.
                var sidecar = Path.Combine(dir, active.Suite + "-" + active.Chunk + ".timing.json");
                if (EvidenceFiles.PathExists(sidecar))
                {
                    var timing = ReadBindingJson(sidecar);
                    var lastPhase = BindingJson.Get(timing, "lastPhase");
                    var phase = lastPhase?.ValueKind == JsonValueKind.String ? lastPhase.Value.GetString() : null;
                    if (phase == "complete" || phase == "timeout" || phase == "cancelled" || phase == "teardown" || BindingJson.Truthy(BindingJson.Get(timing, "completedAt")))
                        throw new InvalidOperationException("chunk-settled");
                }
                AssertJournalUnsettled(dir, active);
                identityRead?.Invoke(activePath);
                if (FileSha256(activePath) != active.IdentityDigest) throw new InvalidOperationException("active-changed");
                var seconds = BindingJson.Double(BindingJson.Get(groups[0], "plannedSeconds"));
                if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) throw new InvalidOperationException("plan-cost");
                return new QualificationPlannedCost(seconds, "");
            }
            catch (Exception e)
            {
                return new QualificationPlannedCost(null, e.Message);
            }
        }

        /// <summary>PlannedCost's seconds (null when unknown).</summary>
        public static double? PlannedSeconds(string path, string chunkId) => PlannedCost(path, chunkId).Seconds;

        private static string ActiveField(QualificationActiveChunk active, string field) => field switch
        {
            "sourceDigest" => active.SourceDigest,
            "policyDigest" => active.PolicyDigest,
            "costFileDigest" => active.CostFileDigest,
            "assemblyDigest" => active.AssemblyDigest,
            _ => null,
        };

        // A whole-number version 1 ([int] or [long] -eq 1).
        private static bool VersionOne(JsonElement? value) => BindingJson.IsWhole(value) && value.Value.GetInt64() == 1;

        private static string[] RecordFiles(string generation)
        {
            var names = new List<string>();
            foreach (var file in Directory.GetFiles(generation))
                if (RecordFilePattern.IsMatch(Path.GetFileName(file))) names.Add(file);
            names.Sort(StringComparer.Ordinal);
            return names.ToArray();
        }

        /// <summary>
        /// Assert-QualificationJournalUnsettled: once the timing root has a journal (groups-executed.journal), its current
        /// generation must be readable, bound to this run (pointer, metadata digest and identity), unseeded and unsealed,
        /// its numbered records a gap-free prefix of chunk records of this run none of which settled the active chunk, and
        /// its publication state (when written) complete at that prefix; the journal must not change while it is read.
        /// An absent journal (older plan producers) is no evidence either way. Throws the refusal.
        /// </summary>
        private static void AssertJournalUnsettled(string directory, QualificationActiveChunk active)
        {
            LandQueuedWrites();
            var journal = Path.Combine(directory, "groups-executed.journal");
            if (!EvidenceFiles.PathExists(journal)) return;
            var pointerPath = Path.Combine(journal, "current.json");
            var pointerDigest = FileSha256(pointerPath);
            var pointer = ReadBindingJson(pointerPath);
            var pointerGeneration = BindingJson.Get(pointer, "generation");
            var metadataDigest = BindingJson.Get(pointer, "metadataDigest");
            if (!VersionOne(BindingJson.Get(pointer, "version"))
                || pointerGeneration?.ValueKind != JsonValueKind.String || !GenerationPattern.IsMatch(pointerGeneration.Value.GetString())
                || metadataDigest?.ValueKind != JsonValueKind.String || !Sha256Pattern.IsMatch(metadataDigest.Value.GetString())
                || !BindingJson.Same(BindingJson.Get(pointer, "timingRoot"), directory)) throw new InvalidOperationException("journal-pointer");
            var generationName = pointerGeneration.Value.GetString();
            var generation = Path.Combine(journal, generationName);
            foreach (var path in new[] { journal, generation })
                if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("journal-generation-unavailable");
            var metaPath = Path.Combine(generation, "metadata.json");
            var meta = ReadBindingJson(metaPath);
            if (FileSha256(metaPath) != metadataDigest.Value.GetString()) throw new InvalidOperationException("journal-metadata-digest");
            if (!VersionOne(BindingJson.Get(meta, "version")) || !BindingJson.Same(BindingJson.Get(meta, "generation"), generationName)
                || !BindingJson.Same(BindingJson.Get(meta, "runId"), active.RunId) || !BindingJson.Same(BindingJson.Get(meta, "attempt"), active.Attempt)
                || !BindingJson.Same(BindingJson.Get(meta, "source"), active.SourceDigest)) throw new InvalidOperationException("journal-metadata-identity");
            // Recovered seed rows are not in the numbered prefix. Until their complete custody can be established this
            // lookup must retain the unknown-cost budget.
            if (!BindingJson.IsFalse(BindingJson.Get(meta, "unresolved")) || BindingJson.Truthy(BindingJson.Get(meta, "corruptionReason"))
                || BindingJson.Truthy(BindingJson.Get(meta, "seedDigest")) || BindingJson.Truthy(BindingJson.Get(meta, "parentGeneration")))
                throw new InvalidOperationException("journal-liveness-unknown");
            if (EvidenceFiles.PathExists(Path.Combine(generation, "seal.json"))) throw new InvalidOperationException("journal-terminal");
            var files = RecordFiles(generation);
            for (var i = 0; i < files.Length; i++)
            {
                if (Path.GetFileName(files[i]) != (i + 1).ToString("00000000", Invariant) + ".json" || TestPhaseFiles.Kind(files[i], out _) != "regular")
                    throw new InvalidOperationException("journal-gap");
                var envelope = ReadBindingJson(files[i]);
                if (!BindingJson.LooseEquals(BindingJson.Get(envelope, "version"), 1) || !BindingJson.Same(BindingJson.Get(envelope, "root"), directory))
                    throw new InvalidOperationException("journal-record-binding");
            }
            var prefix = TestPhaseJournal.ReadGenerationPrefix(generation);
            if (prefix.Corrupt || prefix.LastSequence != files.Length) throw new InvalidOperationException("journal-unavailable");
            foreach (var item in prefix.Records)
            {
                var record = (TestPhaseJournalRecord)item;
                if (record.Kind != "chunk" || record.HeaderRun != active.RunId || record.HeaderAttempt != active.Attempt) throw new InvalidOperationException("journal-identity");
                if ((record.FreshSuite == active.Suite && record.FreshChunk == active.Chunk) || (record.SettledSuite == active.Suite && record.SettledChunk == active.Chunk))
                    throw new InvalidOperationException("chunk-settled");
            }
            var statePath = Path.Combine(directory, "groups-executed.state.json");
            if (EvidenceFiles.PathExists(statePath))
            {
                var publication = ReadBindingJson(statePath);
                var quality = BindingJson.Get(publication, "quality");
                if (!BindingJson.LooseEquals(BindingJson.Get(publication, "version"), 1) || !BindingJson.Same(BindingJson.Get(publication, "generation"), generationName)
                    || !BindingJson.Same(BindingJson.Get(publication, "root"), directory) || !BindingJson.LooseEquals(BindingJson.Get(publication, "sequence"), prefix.LastSequence)
                    || !BindingJson.IsTrue(BindingJson.Get(publication, "publicationComplete")) || !BindingJson.IsFalse(BindingJson.Get(publication, "unresolved"))
                    || quality?.ValueKind != JsonValueKind.Array || quality.Value.GetArrayLength() != 0) throw new InvalidOperationException("journal-state-unavailable");
            }
            var after = RecordFiles(generation);
            if (string.Join("\0", files) != string.Join("\0", after) || FileSha256(pointerPath) != pointerDigest
                || FileSha256(metaPath) != metadataDigest.Value.GetString() || EvidenceFiles.PathExists(Path.Combine(generation, "seal.json")))
                throw new InvalidOperationException("journal-changed");
        }

        // ---- the binding files as ConvertFrom-Json read them ----

        private static class BindingJson
        {
            private static readonly JsonDocumentOptions Options = new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 1024,
            };

            /// <summary>The document's root (detached), refused where ConvertFrom-Json refused: an empty name, or a name repeated in any case.</summary>
            public static JsonElement Parse(string text)
            {
                using var document = JsonDocument.Parse(text ?? "", Options);
                Check(document.RootElement);
                return document.RootElement.Clone();
            }

            private static void Check(JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Name.Length == 0) throw new InvalidOperationException("json-empty-name");
                        if (!names.Add(property.Name)) throw new InvalidOperationException("json-repeated-name " + property.Name);
                        Check(property.Value);
                    }
                }
                else if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in element.EnumerateArray()) Check(item);
                }
            }

            /// <summary>$object.name: the property (any case) of an object, else null; JSON null is null.</summary>
            public static JsonElement? Get(JsonElement? element, string name)
            {
                if (element == null || element.Value.ValueKind != JsonValueKind.Object) return null;
                foreach (var property in element.Value.EnumerateObject())
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                        return property.Value.ValueKind == JsonValueKind.Null ? null : property.Value;
                return null;
            }

            /// <summary>Whether an object has the property (any case), even one holding null.</summary>
            public static bool Has(JsonElement element, string name)
            {
                if (element.ValueKind != JsonValueKind.Object) return false;
                foreach (var property in element.EnumerateObject())
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }

            /// <summary>@($value): an array's items, a scalar or object as one item, nothing for null.</summary>
            public static IEnumerable<JsonElement> Items(JsonElement? value)
            {
                if (value == null) yield break;
                if (value.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in value.Value.EnumerateArray()) yield return item;
                    yield break;
                }
                yield return value.Value;
            }

            /// <summary>A whole number ConvertFrom-Json read as [long] (no fraction or exponent, in range).</summary>
            public static bool IsWhole(JsonElement? value)
            {
                if (value == null || value.Value.ValueKind != JsonValueKind.Number) return false;
                var raw = value.Value.GetRawText();
                return raw.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && value.Value.TryGetInt64(out _);
            }

            /// <summary>[string]$value: text as is, numbers invariant, True/False, an array's items joined by spaces, null empty.</summary>
            public static string Str(JsonElement? value)
            {
                if (value == null) return "";
                var v = value.Value;
                switch (v.ValueKind)
                {
                    case JsonValueKind.String: return v.GetString();
                    case JsonValueKind.Number:
                        if (IsWhole(v)) return v.GetInt64().ToString(Invariant);
                        return v.GetDouble().ToString("G15", Invariant);
                    case JsonValueKind.True: return "True";
                    case JsonValueKind.False: return "False";
                    case JsonValueKind.Array:
                        var parts = new List<string>();
                        foreach (var item in v.EnumerateArray()) parts.Add(Str(item.ValueKind == JsonValueKind.Null ? (JsonElement?)null : item));
                        return string.Join(" ", parts);
                    default: return v.GetRawText();
                }
            }

            /// <summary>Text or null: a text value as is, another scalar as [string], null and empty arrays or objects null.</summary>
            public static string Text(JsonElement? value)
            {
                if (value == null) return null;
                return value.Value.ValueKind == JsonValueKind.String ? value.Value.GetString() : Str(value);
            }

            /// <summary>$value -ceq text for a scalar: the same text ([string] of a number or boolean). Null, arrays and objects never match.</summary>
            public static bool Same(JsonElement? value, string text)
            {
                if (value == null || text == null) return false;
                switch (value.Value.ValueKind)
                {
                    case JsonValueKind.String:
                    case JsonValueKind.Number:
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        return string.Equals(Str(value), text, StringComparison.Ordinal);
                    default:
                        return false;
                }
            }

            /// <summary>
            /// $value -eq number (the right side converted to the left's type): a number of that value, the number's
            /// text (any case), or a boolean of its truth. Null, arrays and objects never equal.
            /// </summary>
            public static bool LooseEquals(JsonElement? value, long number)
            {
                if (value == null) return false;
                switch (value.Value.ValueKind)
                {
                    case JsonValueKind.Number: return value.Value.GetDouble() == number;
                    case JsonValueKind.String: return string.Equals(value.Value.GetString(), number.ToString(Invariant), StringComparison.OrdinalIgnoreCase);
                    case JsonValueKind.True: return number != 0;
                    case JsonValueKind.False: return number == 0;
                    default: return false;
                }
            }

            /// <summary>$value -eq $true.</summary>
            public static bool IsTrue(JsonElement? value) => LooseBool(value, true);

            /// <summary>$value -eq $false.</summary>
            public static bool IsFalse(JsonElement? value) => LooseBool(value, false);

            // $value -eq <bool> (the bool converted to the left's type): the boolean, 1 or 0, "True" or "False" in any case.
            private static bool LooseBool(JsonElement? value, bool flag)
            {
                if (value == null) return false;
                switch (value.Value.ValueKind)
                {
                    case JsonValueKind.True: return flag;
                    case JsonValueKind.False: return !flag;
                    case JsonValueKind.Number: return value.Value.GetDouble() == (flag ? 1.0 : 0.0);
                    case JsonValueKind.String: return string.Equals(value.Value.GetString(), flag ? "True" : "False", StringComparison.OrdinalIgnoreCase);
                    default: return false;
                }
            }

            /// <summary>ConvertTo-QualificationHeartbeatDouble: a number, or text that reads invariantly as one; else 0.</summary>
            public static double Tolerant(JsonElement? value)
            {
                if (value == null) return 0.0;
                if (value.Value.ValueKind == JsonValueKind.Number) return value.Value.GetDouble();
                var text = Str(value);
                return double.TryParse(text, NumberStyles.Float, Invariant, out var parsed) ? parsed : 0.0;
            }

            /// <summary>PowerShell truth: non-empty text, a non-zero number, true, a non-empty array, any object.</summary>
            public static bool Truthy(JsonElement? value)
            {
                if (value == null) return false;
                var v = value.Value;
                switch (v.ValueKind)
                {
                    case JsonValueKind.String: return v.GetString().Length != 0;
                    case JsonValueKind.Number: return v.GetDouble() != 0;
                    case JsonValueKind.True: return true;
                    case JsonValueKind.False: return false;
                    case JsonValueKind.Array:
                        var count = v.GetArrayLength();
                        if (count == 0) return false;
                        if (count > 1) return true;
                        var only = v[0];
                        return only.ValueKind == JsonValueKind.Array ? only.GetArrayLength() > 0 : Truthy(only.ValueKind == JsonValueKind.Null ? (JsonElement?)null : only);
                    default: return true;
                }
            }

            /// <summary>[double]$value: a number, text read invariantly (thousands separators allowed), true 1; null 0. Anything else throws.</summary>
            public static double Double(JsonElement? value)
            {
                if (value == null) return 0.0;
                var v = value.Value;
                switch (v.ValueKind)
                {
                    case JsonValueKind.Number: return v.GetDouble();
                    case JsonValueKind.String:
                        var text = v.GetString();
                        if (string.IsNullOrWhiteSpace(text)) return 0.0;
                        return double.Parse(text, NumberStyles.Float | NumberStyles.AllowThousands, Invariant);
                    case JsonValueKind.True: return 1.0;
                    case JsonValueKind.False: return 0.0;
                    default: throw new InvalidCastException("not-a-number");
                }
            }
        }
    }
}
