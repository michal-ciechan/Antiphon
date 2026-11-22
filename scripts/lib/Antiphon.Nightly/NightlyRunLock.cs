// W2-nightly S18: the per-task nightly run lock, <StateRoot>\run.lock (nightly-common.ps1 Get-NightlyLockPath ...
// Exit-NightlyExclusiveLock; CARD-0487, CARD-1386 dead-owner reclaim, CARD-1389 the reclaim race and the boot id,
// CARD-1396 the shared reopen). The C# run core (NightlyRun.cs) takes it; nightly-run-impl.ps1 called it through the
// nightly-common seams (New-ReleaseGateLockSeams in release-gate.ps1) until S20. The old PowerShell functions stay unused until S27. The record bytes are the ones
// "[ordered]@{...} | ConvertTo-Json -Compress" wrote, so the still-PowerShell readers (Read-NightlyLockRecord,
// rc-qualification-status.ps1, Read-RcQualificationRecord) read them unchanged. OS observation goes through
// INightlyProcessTable and the clock; nothing here is static state. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>
    /// A lock file's record (Read-NightlyLockRecord): the run lock's pid, processStartedAt, startedAt, runId, parentPid,
    /// parentStartedAt, bootId and hostId, or the native lock's pid, processStartedAt, runId, lane, continuationId and
    /// acquiredAt. A value reads as PowerShell 7's ConvertFrom-Json and Get-NightlyLockFieldText read it: text trimmed,
    /// a date string (ConvertFrom-Json makes it a DateTime) in UTC round-trip form, a number as its digits. The
    /// properties are null for an absent or JSON-null field.
    /// </summary>
    public sealed class NightlyLockRecord
    {
        // ConvertFrom-Json (Newtonsoft, DateParseHandling.DateTime) turns these strings into DateTime values.
        private static readonly Regex IsoDate = new Regex(
            @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})?$", RegexOptions.CultureInvariant);
        private static readonly Regex MicrosoftDate = new Regex(@"^/Date\((-?\d+)([+-]\d{4})?\)/$", RegexOptions.CultureInvariant);
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.CultureInvariant);

        // The field's text as Get-NightlyLockFieldText reads it (null for JSON null) and as [string] reads it.
        private readonly Dictionary<string, (string Text, string Raw)> _fields =
            new Dictionary<string, (string Text, string Raw)>(StringComparer.OrdinalIgnoreCase);

        private NightlyLockRecord(bool legacy) { Legacy = legacy; }

        /// <summary>The record is the pre-JSON text form "pid startedAt runId".</summary>
        public bool Legacy { get; }

        /// <summary>pid (0 when absent or not an integer).</summary>
        public int Pid => FieldPid("pid");

        /// <summary>parentPid (0 when absent or not an integer).</summary>
        public int ParentPid => FieldPid("parentPid");

        /// <summary>processStartedAt: the owner's process start in UTC round-trip form.</summary>
        public string ProcessStartedAt => Value("processStartedAt");

        /// <summary>startedAt: when the run lock was taken.</summary>
        public string StartedAt => Value("startedAt");

        /// <summary>runId.</summary>
        public string RunId => Value("runId");

        /// <summary>parentStartedAt: the owner's parent's process start, null when it was not known.</summary>
        public string ParentStartedAt => Value("parentStartedAt");

        /// <summary>bootId (run lock).</summary>
        public string BootId => Value("bootId");

        /// <summary>hostId (run lock).</summary>
        public string HostId => Value("hostId");

        /// <summary>lane (native lock).</summary>
        public string Lane => Value("lane");

        /// <summary>continuationId (native lock).</summary>
        public string ContinuationId => Value("continuationId");

        /// <summary>acquiredAt (native lock).</summary>
        public string AcquiredAt => Value("acquiredAt");

        /// <summary>The record has the field (even when it is JSON null).</summary>
        public bool Has(string name) => name != null && _fields.ContainsKey(name);

        /// <summary>The field's text, null when it is absent or JSON null.</summary>
        public string Value(string name) => name != null && _fields.TryGetValue(name, out var field) ? field.Text : null;

        /// <summary>Get-NightlyLockFieldText: the field's text, empty when it is absent or JSON null.</summary>
        public string Text(string name) => Value(name) ?? "";

        /// <summary>Get-NightlyLockFieldPid: the field as an integer, 0 when it is absent, blank or not an integer.</summary>
        public int FieldPid(string name)
        {
            var text = Text(name);
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        }

        /// <summary>Get-NightlyLockOptionalPid: the field as an integer, or "" when it is absent, blank or not an integer.</summary>
        public object OptionalPid(string name)
        {
            if (name == null || !_fields.TryGetValue(name, out var field) || field.Raw == null) return "";
            if (string.IsNullOrWhiteSpace(field.Raw)) return "";
            return int.TryParse(field.Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? (object)n : "";
        }

        /// <summary>
        /// Read-NightlyLockRecord: the record at the path, or null when it is missing, cannot be opened or is JSON that
        /// does not parse. The file is opened with FileShare.ReadWrite so the owner's write handle (FileAccess.Write,
        /// FileShare.Read) does not refuse the read on Windows; a FileShare.None holder still does. Text that is not
        /// JSON is the legacy "pid startedAt runId" form.
        /// </summary>
        public static NightlyLockRecord Read(string lockPath)
        {
            if (string.IsNullOrEmpty(lockPath) || !(File.Exists(lockPath) || Directory.Exists(lockPath))) return null;
            string raw;
            try
            {
                using var stream = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8, true);
                raw = reader.ReadToEnd();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException)
            {
                return null;
            }
            var trimmed = raw.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal)) return FromJson(raw);
            var parts = Whitespace.Split(trimmed, 3);
            var record = new NightlyLockRecord(true);
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid);
            var pidText = pid.ToString(CultureInfo.InvariantCulture);
            record._fields["pid"] = (pidText, pidText);
            record._fields["startedAt"] = parts.Length > 1 ? (parts[1].Trim(), parts[1]) : (null, null);
            record._fields["runId"] = parts.Length > 2 ? (parts[2].Trim(), parts[2]) : (null, null);
            record._fields["parentPid"] = ("0", "0");
            record._fields["parentStartedAt"] = (null, null);
            return record;
        }

        /// <summary>
        /// ConvertFrom-NightlyLockBytes: the JSON record in the bytes (a UTF-8 byte-order mark skipped), or null when they
        /// are empty, are not a JSON object or do not parse.
        /// </summary>
        public static NightlyLockRecord FromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            if (start >= bytes.Length) return null;
            var text = new UTF8Encoding(false).GetString(bytes, start, bytes.Length - start);
            if (string.IsNullOrWhiteSpace(text) || !text.Trim().StartsWith("{", StringComparison.Ordinal)) return null;
            return FromJson(text);
        }

        /// <summary>The JSON object in the text, or null when it does not parse, is not an object or repeats a name (ConvertFrom-Json refuses that).</summary>
        public static NightlyLockRecord FromJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                using var document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
                var record = new NightlyLockRecord(false);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (record._fields.ContainsKey(property.Name)) return null;
                    record._fields[property.Name] = FieldOf(property.Value);
                }
                return record;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static (string Text, string Raw) FieldOf(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return (null, null);
                case JsonValueKind.String:
                    var s = value.GetString();
                    var date = DateText(s);
                    return date != null ? (date, date) : (s.Trim(), s);
                case JsonValueKind.True:
                    return ("True", "True");
                case JsonValueKind.False:
                    return ("False", "False");
                case JsonValueKind.Number:
                    var number = value.TryGetInt64(out var whole)
                        ? whole.ToString(CultureInfo.InvariantCulture)
                        : value.GetDouble().ToString(CultureInfo.InvariantCulture);
                    return (number, number);
                default:
                    var other = value.GetRawText();
                    return (other.Trim(), other);
            }
        }

        // A string ConvertFrom-Json makes a DateTime, as Get-NightlyLockFieldText writes it back:
        // ToUniversalTime().ToString('o') (no zone reads as local time, as PowerShell's ToUniversalTime does).
        private static string DateText(string s)
        {
            if (s == null) return null;
            if (IsoDate.IsMatch(s))
            {
                if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) return null;
                return parsed.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            }
            var ms = MicrosoftDate.Match(s);
            if (ms.Success && long.TryParse(ms.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var milliseconds))
            {
                try { return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime.ToString("o", CultureInfo.InvariantCulture); }
                catch (ArgumentOutOfRangeException) { return null; }
            }
            return null;
        }
    }

    /// <summary>
    /// The outcome of taking a lock: Ok, the reason (acquired, reclaimed, hop, or the refusal), whether this attempt now
    /// owns the lock, the record it holds or found, and (native lock) the lock path.
    /// </summary>
    public sealed class NightlyLockResult
    {
        public NightlyLockResult(bool ok, string reason, bool ownsLock, NightlyLockRecord record, string path = null)
        {
            Ok = ok;
            Reason = reason ?? "";
            OwnsLock = ownsLock;
            Record = record;
            Path = path;
        }

        /// <summary>The caller may proceed.</summary>
        public bool Ok { get; }

        /// <summary>acquired, reclaimed or hop when Ok; the refusal otherwise.</summary>
        public string Reason { get; }

        /// <summary>This attempt owns the lock (a continuation inherits it without owning it).</summary>
        public bool OwnsLock { get; }

        /// <summary>The record written, or the one found; null when there was none.</summary>
        public NightlyLockRecord Record { get; }

        /// <summary>The lock file (the native lock reports it).</summary>
        public string Path { get; }
    }

    /// <summary>Resolve-NightlyStaleLock's verdict on a lock another attempt holds: reacquire, reclaim or refuse, and why.</summary>
    public sealed record NightlyLockDecision(string Action, string Reason);

    /// <summary>
    /// The per-task run lock. One instance per run holds the owner's handle between Enter and Exit, as
    /// $script:NightlyOwnedLock and $script:NightlyLockStream did.
    /// </summary>
    public sealed class NightlyRunLock : IDisposable
    {
        /// <summary>The durable reason a dead owner's lock was replaced, beside run.lock; Exit never removes it.</summary>
        public const string ReclaimRecordName = "lock-reclaim.json";

        private readonly TimeProvider _clock;
        private readonly INightlyProcessTable _processes;
        private readonly Action<string> _runLine;
        private readonly Action<string, string> _trace;
        private FileStream _stream;

        /// <summary>A lock on the host's clock and process table; run lines go to its output, traces to its trace.</summary>
        public NightlyRunLock(NightlyHost host)
            : this(Require(host).Clock, host.Processes,
                message => host.Out.WriteLine("[" + host.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "] " + message),
                host.WriteTrace)
        {
        }

        /// <summary>
        /// A lock on this clock and process table. runLine receives a run line's message (Write-NightlyRunLine adds the
        /// time), trace a trace line's kind and message (Write-NightlyTrace); either may be null.
        /// </summary>
        public NightlyRunLock(TimeProvider clock, INightlyProcessTable processes, Action<string> runLine, Action<string, string> trace)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _processes = processes ?? throw new ArgumentNullException(nameof(processes));
            _runLine = runLine ?? (_ => { });
            _trace = trace ?? ((_, _) => { });
        }

        private static NightlyHost Require(NightlyHost host) => host ?? throw new ArgumentNullException(nameof(host));

        /// <summary>This attempt owns the lock (acquired, reacquired or reclaimed), until Exit.</summary>
        public bool OwnsLock { get; private set; }

        /// <summary>The owner's handle is open (FileAccess.Write, FileShare.Read), so readers can read the record.</summary>
        public bool HoldsHandle => _stream != null;

        /// <summary>
        /// Open-NightlySharedLockStream: reopens the reclaimed record for the owner after the exclusive compare and
        /// publish (FileMode.Open, FileAccess.Write, FileShare.Read). A test replaces it to fail the reopen.
        /// </summary>
        public Func<string, FileStream> OpenShared { get; set; } = path => new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);

        /// <summary>Get-NightlyLockPath: run.lock under the state root.</summary>
        public static string LockPath(string stateRoot) => Path.Combine(stateRoot ?? "", "run.lock");

        private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

        private static string Utc(DateTime value) => value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

        private bool Alive(int pid) => pid > 0 && _processes.IsAlive(pid);

        /// <summary>
        /// Enter-NightlyExclusiveLock. With a continueRunId the self-reexec child proves it inherited the lock: the record
        /// names that run, the continuing parent is alive and is the record's pid or parentPid, and its start time (when
        /// given) matches the parent's live start, the record's processStartedAt, startedAt or parentStartedAt. Otherwise
        /// an atomic CreateNew takes the lock and keeps the handle; a lock that exists is reacquired (this process and run),
        /// reclaimed from a dead owner (Resolve) or refused.
        /// </summary>
        public NightlyLockResult Enter(string stateRoot, string runId, string continueRunId = "", int continueParentPid = 0, string continueParentStartedAt = "")
        {
            if (string.IsNullOrWhiteSpace(stateRoot)) throw new ArgumentException("StateRoot is required", nameof(stateRoot));
            if (string.IsNullOrEmpty(runId)) throw new ArgumentException("RunId is required", nameof(runId));
            Directory.CreateDirectory(stateRoot);
            var lockPath = LockPath(stateRoot);
            var parent = _processes.Parent(_processes.CurrentPid) ?? NightlyParent.None;
            if (!string.IsNullOrWhiteSpace(continueRunId)) return EnterContinuation(lockPath, continueRunId, continueParentPid, continueParentStartedAt);

            var bootId = _processes.BootId() ?? "";
            var hostId = _processes.HostId() ?? "";
            var bytes = RecordBytes(runId, bootId, hostId, parent);
            try
            {
                var stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                try
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
                CloseHandle();
                _stream = stream;
                OwnsLock = true;
                return new NightlyLockResult(true, "acquired", true, NightlyLockRecord.FromBytes(bytes));
            }
            catch (IOException)
            {
                var existing = NightlyLockRecord.Read(lockPath);
                var decision = Resolve(existing, runId, bootId, hostId);
                if (decision.Action == "reacquire")
                {
                    OwnsLock = true;
                    return new NightlyLockResult(true, "acquired", true, existing);
                }
                if (decision.Action == "reclaim") return EnterReclaimed(stateRoot, lockPath, existing, runId, decision.Reason, bootId, hostId, parent);
                return new NightlyLockResult(false, decision.Reason, false, existing);
            }
        }

        private NightlyLockResult EnterContinuation(string lockPath, string continueRunId, int continueParentPid, string continueParentStartedAt)
        {
            var existing = NightlyLockRecord.Read(lockPath);
            if (existing == null) return new NightlyLockResult(false, "missing-lock", false, null);
            var parentAlive = Alive(continueParentPid);
            var parentStart = parentAlive ? _processes.StartTimeUtc(continueParentPid) : null;
            var startText = parentStart.HasValue ? Utc(parentStart.Value) : "";
            var runOk = string.Equals(existing.Text("runId"), continueRunId, StringComparison.OrdinalIgnoreCase);
            var pidOk = existing.Pid == continueParentPid || existing.ParentPid == continueParentPid;
            var startOk = true;
            if (!string.IsNullOrWhiteSpace(continueParentStartedAt))
            {
                // Bind the owner timestamp captured at acquisition across the hop.
                var recordStart = existing.Text("processStartedAt");
                startOk = string.Equals(startText, continueParentStartedAt, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(recordStart) && string.Equals(recordStart, continueParentStartedAt, StringComparison.OrdinalIgnoreCase))
                    || string.Equals(existing.Text("startedAt"), continueParentStartedAt, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(existing.Text("parentStartedAt"), continueParentStartedAt, StringComparison.OrdinalIgnoreCase);
            }
            if (!(parentAlive && runOk && pidOk && startOk)) return new NightlyLockResult(false, "hop-identity", false, existing);
            OwnsLock = false;
            return new NightlyLockResult(true, "hop", false, existing);
        }

        /// <summary>
        /// Resolve-NightlyStaleLock. A recycled pid after a Windows reboot (same host, both boot ids registry decimals that
        /// differ) is reclaimed before the live-owner check; any other boot change refuses (CARD-1389 D-2: a Linux boot_id
        /// UUID, or a lock written with a LastBootUpTime boot id, stays lock-held until removed by hand). A live owner is
        /// reacquired only by this process and run. A dead owner is reclaimed only when its record names a parent that is
        /// dead too and the same boot and host (dead-owner-same-boot), or neither (dead-owner-legacy).
        /// </summary>
        public NightlyLockDecision Resolve(NightlyLockRecord existing, string runId, string bootId, string hostId)
        {
            if (existing == null) return new NightlyLockDecision("refuse", "lock-held");
            var recordBoot = existing.Text("bootId");
            var recordHost = existing.Text("hostId");
            var currentBoot = string.IsNullOrWhiteSpace(bootId) ? "" : bootId.Trim();
            var currentHost = string.IsNullOrWhiteSpace(hostId) ? "" : hostId.Trim();
            var bootBoth = !string.IsNullOrWhiteSpace(recordBoot) && !string.IsNullOrWhiteSpace(currentBoot);
            var hostBoth = !string.IsNullOrWhiteSpace(recordHost) && !string.IsNullOrWhiteSpace(currentHost);
            if (hostBoth && bootBoth && string.Equals(recordHost, currentHost, StringComparison.Ordinal) && !string.Equals(recordBoot, currentBoot, StringComparison.Ordinal))
            {
                if (IsDecimal(recordBoot) && IsDecimal(currentBoot)) return new NightlyLockDecision("reclaim", "dead-owner-rebooted");
                return new NightlyLockDecision("refuse", "lock-held");
            }
            var ownerPid = existing.FieldPid("pid");
            if (Alive(ownerPid))
            {
                var samePid = ownerPid == _processes.CurrentPid;
                var sameRun = string.Equals(existing.Text("runId"), runId, StringComparison.OrdinalIgnoreCase);
                return samePid && sameRun ? new NightlyLockDecision("reacquire", "acquired") : new NightlyLockDecision("refuse", "live-owner");
            }
            if (ownerPid <= 0) return new NightlyLockDecision("refuse", "lock-held");
            if ((hostBoth && !string.Equals(recordHost, currentHost, StringComparison.Ordinal)) || (!string.IsNullOrWhiteSpace(recordHost) && string.IsNullOrWhiteSpace(currentHost)))
                return new NightlyLockDecision("refuse", "lock-held");
            if (!string.IsNullOrWhiteSpace(recordBoot) && string.IsNullOrWhiteSpace(currentBoot)) return new NightlyLockDecision("refuse", "lock-held");
            var parentPid = existing.FieldPid("parentPid");
            if (parentPid <= 0 || Alive(parentPid)) return new NightlyLockDecision("refuse", "lock-held");
            if (bootBoth && hostBoth && string.Equals(recordBoot, currentBoot, StringComparison.Ordinal) && string.Equals(recordHost, currentHost, StringComparison.Ordinal))
                return new NightlyLockDecision("reclaim", "dead-owner-same-boot");
            if (string.IsNullOrWhiteSpace(recordBoot) && string.IsNullOrWhiteSpace(recordHost)) return new NightlyLockDecision("reclaim", "dead-owner-legacy");
            return new NightlyLockDecision("refuse", "lock-held");
        }

        private static bool IsDecimal(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            foreach (var c in value)
                if (c < '0' || c > '9') return false;
            return true;
        }

        /// <summary>
        /// Enter-NightlyReclaimedLock (CARD-1389 D-1, D-2; CARD-1396). Rewrites the record through one exclusive open
        /// (FileShare.None) after checking it is still the snapshot (pid, runId, bootId, hostId), so a second attempt with
        /// the same snapshot can neither unlink nor overwrite the winner. lock-reclaim.json and the run line are published
        /// only after the new bytes are flushed; a failed publish writes the previous bytes back. The owner then reopens the
        /// record with FileShare.Read so readers can read it; a failed reopen keeps ownership (a live pid still refuses
        /// contenders) and is reported as reclaim-shared-reopen-failed. Nothing is ever deleted.
        /// </summary>
        public NightlyLockResult EnterReclaimed(string stateRoot, string lockPath, NightlyLockRecord existing, string runId, string reason,
            string bootId, string hostId, NightlyParent parent)
        {
            var bytes = RecordBytes(runId, bootId, hostId, parent ?? NightlyParent.None);
            FileStream exclusive;
            try
            {
                exclusive = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return Denied(lockPath);
            }
            byte[] previous;
            try
            {
                previous = ReadAll(exclusive);
                if (!SameIdentity(NightlyLockRecord.FromBytes(previous), existing))
                {
                    exclusive.Dispose();
                    return Denied(lockPath);
                }
                try
                {
                    exclusive.SetLength(0);
                    exclusive.Position = 0;
                    exclusive.Write(bytes, 0, bytes.Length);
                    exclusive.Flush();
                }
                catch (Exception)
                {
                    // As the PowerShell did: any failed rewrite puts the previous bytes back.
                    Restore(exclusive, previous);
                    exclusive.Dispose();
                    return Denied(lockPath);
                }
                try
                {
                    WriteReclaimRecord(stateRoot, reason, existing, runId, bootId, hostId);
                }
                catch (Exception)
                {
                    // Any failed publish (the record or the run line) puts the previous bytes back and refuses.
                    Restore(exclusive, previous);
                    exclusive.Dispose();
                    return new NightlyLockResult(false, "lock-held", false, existing);
                }
            }
            catch
            {
                exclusive.Dispose();
                throw;
            }
            // Readers share the handle after the exclusive compare and publish.
            exclusive.Dispose();
            FileStream shared = null;
            try
            {
                shared = OpenShared(lockPath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // The published record stays ours. A live pid still refuses a contender, so ownership does not change
                // when the shared handle cannot be retaken. The line is the only signal; a log failure must not replace
                // that Ok result.
                var detail = (e.Message ?? "unreadable").Replace("\r", " ").Replace("\n", " ");
                var line = "reclaim-shared-reopen-failed " + detail;
                try { _runLine(line); } catch (Exception) { }
                try { _trace("lock", line); } catch (Exception) { }
            }
            CloseHandle();
            _stream = shared;
            OwnsLock = true;
            return new NightlyLockResult(true, "reclaimed", true, NightlyLockRecord.FromBytes(bytes));
        }

        // Get-NightlyReclaimDenied: re-read once; a readable live owner is live-owner, anything else lock-held.
        private NightlyLockResult Denied(string lockPath)
        {
            var raced = NightlyLockRecord.Read(lockPath);
            if (raced != null && Alive(raced.FieldPid("pid"))) return new NightlyLockResult(false, "live-owner", false, raced);
            return new NightlyLockResult(false, "lock-held", false, raced);
        }

        // Test-NightlyReclaimIdentity.
        private static bool SameIdentity(NightlyLockRecord left, NightlyLockRecord right)
        {
            if (left == null || right == null) return false;
            if (left.FieldPid("pid") != right.FieldPid("pid")) return false;
            foreach (var name in new[] { "runId", "bootId", "hostId" })
                if (!string.Equals(left.Text(name), right.Text(name), StringComparison.Ordinal)) return false;
            return true;
        }

        private static byte[] ReadAll(Stream stream)
        {
            long length;
            try { length = stream.Length; } catch (IOException) { length = 0; }
            if (length < 0) length = 0;
            var bytes = new byte[length];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var n = stream.Read(bytes, offset, bytes.Length - offset);
                if (n <= 0) break;
                offset += n;
            }
            if (offset < bytes.Length) Array.Resize(ref bytes, offset);
            return bytes;
        }

        private static void Restore(Stream stream, byte[] previous)
        {
            try
            {
                stream.SetLength(0);
                stream.Position = 0;
                if (previous != null && previous.Length > 0) stream.Write(previous, 0, previous.Length);
                stream.Flush();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException) { }
        }

        // Write-NightlyLockReclaim: lock-reclaim.json (Write-NightlyAtomicJson), then the run line "<reason> <previous run id>".
        private void WriteReclaimRecord(string stateRoot, string reason, NightlyLockRecord existing, string newRunId, string bootId, string hostId)
        {
            var body = new JsonObject
            {
                ["reason"] = reason,
                ["at"] = UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["previousPid"] = OptionalPidNode(existing, "pid"),
                ["previousParentPid"] = OptionalPidNode(existing, "parentPid"),
                ["previousRunId"] = Text(existing, "runId"),
                ["previousProcessStartedAt"] = Text(existing, "processStartedAt"),
                ["previousBootId"] = Text(existing, "bootId"),
                ["previousHostId"] = Text(existing, "hostId"),
                ["newRunId"] = newRunId ?? "",
                ["bootId"] = string.IsNullOrWhiteSpace(bootId) ? "" : bootId.Trim(),
                ["hostId"] = string.IsNullOrWhiteSpace(hostId) ? "" : hostId.Trim(),
            };
            HealthJson.WriteAtomic(Path.Combine(stateRoot, ReclaimRecordName), body);
            _runLine(reason + " " + Text(existing, "runId"));
        }

        private static string Text(NightlyLockRecord record, string name) => record == null ? "" : record.Text(name);

        private static JsonNode OptionalPidNode(NightlyLockRecord record, string name)
        {
            var value = record == null ? "" : record.OptionalPid(name);
            return value is int n ? JsonValue.Create(n) : JsonValue.Create((string)value);
        }

        /// <summary>
        /// New-NightlyLockRecord as written: the ASCII bytes of ConvertTo-Json -Compress of pid, processStartedAt,
        /// startedAt, runId, parentPid, parentStartedAt (null when unknown), bootId and hostId.
        /// </summary>
        public byte[] RecordBytes(string runId, string bootId, string hostId, NightlyParent parent)
        {
            var pid = _processes.CurrentPid;
            var processStart = _processes.StartTimeUtc(pid);
            var parentId = parent?.Id ?? 0;
            var parentStart = parent?.StartTimeUtc;
            var json = new StringBuilder(256).Append('{');
            LockJson.Number(json, "pid", pid).Append(',');
            LockJson.String(json, "processStartedAt", processStart.HasValue ? Utc(processStart.Value) : "").Append(',');
            LockJson.String(json, "startedAt", UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append(',');
            LockJson.String(json, "runId", runId ?? "").Append(',');
            LockJson.Number(json, "parentPid", parentId).Append(',');
            LockJson.String(json, "parentStartedAt", parentStart.HasValue ? Utc(parentStart.Value) : null).Append(',');
            LockJson.String(json, "bootId", string.IsNullOrWhiteSpace(bootId) ? "" : bootId.Trim()).Append(',');
            LockJson.String(json, "hostId", string.IsNullOrWhiteSpace(hostId) ? "" : hostId.Trim());
            return LockJson.Bytes(json.Append('}'));
        }

        /// <summary>
        /// Exit-NightlyExclusiveLock: when this attempt owns the lock, closes the handle and deletes run.lock if it still
        /// names this process (and this run, when one is given). lock-reclaim.json stays.
        /// </summary>
        public void Exit(string stateRoot, string runId)
        {
            if (!OwnsLock) return;
            var lockPath = LockPath(stateRoot);
            var existing = NightlyLockRecord.Read(lockPath);
            var same = existing != null && existing.Pid == _processes.CurrentPid
                && (string.IsNullOrWhiteSpace(runId) || string.Equals(existing.Text("runId"), runId, StringComparison.OrdinalIgnoreCase));
            CloseHandle();
            if (same) LockJson.DeleteQuietly(lockPath);
            OwnsLock = false;
        }

        /// <summary>Closes the owner's handle without deleting anything and drops ownership.</summary>
        public void Dispose()
        {
            CloseHandle();
            OwnsLock = false;
        }

        private void CloseHandle()
        {
            var stream = _stream;
            _stream = null;
            if (stream == null) return;
            try { stream.Dispose(); } catch (IOException) { }
        }
    }

    /// <summary>The lock records' JSON: ConvertTo-Json -Compress text (Newtonsoft's default escaping), ASCII bytes.</summary>
    internal static class LockJson
    {
        internal static StringBuilder Number(StringBuilder json, string name, int value) =>
            Name(json, name).Append(value.ToString(CultureInfo.InvariantCulture));

        internal static StringBuilder String(StringBuilder json, string name, string value)
        {
            Name(json, name);
            return value == null ? json.Append("null") : json.Append(EvidencePolicy.JsonString(value));
        }

        private static StringBuilder Name(StringBuilder json, string name) => json.Append(EvidencePolicy.JsonString(name)).Append(':');

        // [System.Text.Encoding]::ASCII.GetBytes: a character above 0x7F becomes '?'.
        internal static byte[] Bytes(StringBuilder json) => Encoding.ASCII.GetBytes(json.ToString());

        // Remove-Item -Force -ErrorAction SilentlyContinue.
        internal static void DeleteQuietly(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// A process table over delegates: how release-gate.ps1 hands the nightly-common seams (or the compiled table) to the
    /// locks. Plain delegates, no PowerShell types; a null delegate answers as an unknown process.
    /// </summary>
    public sealed class NightlyDelegateProcessTable : INightlyProcessTable
    {
        private readonly Func<int, bool> _isAlive;
        private readonly Func<int, DateTime?> _startTimeUtc;
        private readonly Func<int, NightlyParent> _parent;
        private readonly Func<string> _bootId;
        private readonly Func<string> _hostId;

        public NightlyDelegateProcessTable(int currentPid, Func<int, bool> isAlive, Func<int, DateTime?> startTimeUtc, Func<int, NightlyParent> parent,
            Func<string> bootId, Func<string> hostId)
        {
            CurrentPid = currentPid;
            _isAlive = isAlive;
            _startTimeUtc = startTimeUtc;
            _parent = parent;
            _bootId = bootId;
            _hostId = hostId;
        }

        /// <inheritdoc />
        public int CurrentPid { get; }

        /// <inheritdoc />
        public DateTime? StartTimeUtc(int pid) => _startTimeUtc?.Invoke(pid);

        /// <inheritdoc />
        public bool IsAlive(int pid) => pid > 0 && _isAlive != null && _isAlive(pid);

        /// <inheritdoc />
        public NightlyParent Parent(int pid) => _parent?.Invoke(pid) ?? NightlyParent.None;

        /// <inheritdoc />
        public string BootId() => _bootId?.Invoke() ?? "";

        /// <inheritdoc />
        public string HostId() => _hostId?.Invoke() ?? "";
    }

    /// <summary>A clock over a delegate (Get-NightlyUtcNow and its UtcNow seam). A time without a zone reads as local, as ToUniversalTime reads it.</summary>
    public sealed class NightlyDelegateClock : TimeProvider
    {
        private readonly Func<DateTime> _utcNow;

        public NightlyDelegateClock(Func<DateTime> utcNow)
        {
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow()
        {
            var now = _utcNow();
            return new DateTimeOffset(now.Kind == DateTimeKind.Utc ? now : now.ToUniversalTime(), TimeSpan.Zero);
        }
    }
}
