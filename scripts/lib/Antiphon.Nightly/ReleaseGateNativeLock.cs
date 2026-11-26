// W2-nightly S18: CARD-0599 D-4's shared native-run lock, <CoordinationRoot>\native-run.lock, ported from release-gate.ps1
// (New-ReleaseGateLockOwner, Enter-ReleaseGateNativeLock, Exit-ReleaseGateNativeLock); since unit 12 of
// 2026-11-22-remaining-ps1-to-csharp.md deleted that library, callers (NightlyRun; the live lane through Antiphon.LiveLane's ILiveLaneNativeLock seam since W2 slice S21) use this class. One atomic create-new lock serialises the master and RC lanes. A live owner is never stolen on age: the
// caller records deferred-busy and waits for its next slot; a dead owner's lock is reported stale and is never reclaimed
// here (docs/release-gates.md: manual recovery). The record bytes are the ones "[ordered]@{...} | ConvertTo-Json -Compress"
// wrote, read unchanged by Read-NightlyLockRecord and RcQualificationRecords (rc-qualification-status, qualification-stop). OS observation
// goes through INightlyProcessTable and the clock. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Antiphon.Nightly
{
    /// <summary>The shared native-run lock of one process ($script:ReleaseGateOwnsNativeLock): whether it owns the lock it took.</summary>
    public sealed class ReleaseGateNativeLock
    {
        private readonly TimeProvider _clock;
        private readonly INightlyProcessTable _processes;

        /// <summary>A lock on the host's clock and process table.</summary>
        public ReleaseGateNativeLock(NightlyHost host)
            : this((host ?? throw new ArgumentNullException(nameof(host))).Clock, host.Processes)
        {
        }

        /// <summary>A lock on this clock and process table.</summary>
        public ReleaseGateNativeLock(TimeProvider clock, INightlyProcessTable processes)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        }

        /// <summary>
        /// A lock on the system clock and this machine's process table: what Enter-/Exit-ReleaseGateNativeLock used for a
        /// caller without nightly seams (the live lane, whose run takes it through ILiveLaneNativeLock). Enter and Exit on the same object.
        /// </summary>
        public static ReleaseGateNativeLock Production() => new ReleaseGateNativeLock(TimeProvider.System, new NightlyProcessTable());

        /// <summary>This process took the lock and has not released it.</summary>
        public bool OwnsLock { get; private set; }

        /// <summary>Get-ReleaseGateNativeLockPath: native-run.lock under the coordination root (default C:\Antiphon\verification).</summary>
        public static string LockPath(string coordinationRoot) => ReleaseGatePublication.NativeLockPath(coordinationRoot);

        /// <summary>
        /// Enter-ReleaseGateNativeLock. With continueOwnerPid above 0 a self-reexec hop proves it inherited the lock: the
        /// record names this run and lane, the continuing owner is alive and is the record's pid, and its start time (when
        /// given) is the record's processStartedAt; it inherits without owning (hop) or refuses (shared-hop-identity,
        /// missing-shared-lock). Otherwise CreateNew takes the lock and closes the handle at once, so a contender can read
        /// who holds it: a live holder refuses deferred-busy, a dead or unreadable one stale-shared-lock.
        /// </summary>
        public NightlyLockResult Enter(string coordinationRoot, string runId, string lane, string continuationId = "", int continueOwnerPid = 0,
            string continueOwnerStartedAt = "")
        {
            if (string.IsNullOrEmpty(runId)) throw new ArgumentException("RunId is required", nameof(runId));
            if (string.IsNullOrEmpty(lane)) throw new ArgumentException("Lane is required", nameof(lane));
            var lockPath = LockPath(coordinationRoot);
            var directory = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            if (continueOwnerPid > 0)
            {
                var existing = NightlyLockRecord.Read(lockPath);
                if (existing == null) return new NightlyLockResult(false, "missing-shared-lock", false, null);
                var alive = _processes.IsAlive(continueOwnerPid);
                var runOk = string.Equals(existing.Text("runId"), runId, StringComparison.OrdinalIgnoreCase);
                var pidOk = existing.Pid == continueOwnerPid;
                var startOk = string.IsNullOrWhiteSpace(continueOwnerStartedAt) || ReleaseGateCredit.SameInstant(existing.ProcessStartedAt, continueOwnerStartedAt);
                var laneOk = string.Equals(existing.Text("lane"), lane, StringComparison.OrdinalIgnoreCase);
                if (!(alive && runOk && pidOk && startOk && laneOk)) return new NightlyLockResult(false, "shared-hop-identity", false, existing);
                return new NightlyLockResult(true, "hop", false, existing);
            }

            var bytes = OwnerBytes(runId, lane, continuationId);
            try
            {
                // CreateNew is the exclusion; the handle is closed immediately afterwards so the owner record stays
                // READABLE. A contender must be able to say who holds the lock and prove the owner is alive - it cannot
                // do that against a file it cannot open.
                using (var stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
                OwnsLock = true;
                return new NightlyLockResult(true, "acquired", true, NightlyLockRecord.FromBytes(bytes), lockPath);
            }
            catch (IOException)
            {
                var existing = NightlyLockRecord.Read(lockPath);
                var alive = existing != null && existing.Pid > 0 && _processes.IsAlive(existing.Pid);
                return new NightlyLockResult(false, alive ? "deferred-busy" : "stale-shared-lock", false, existing, lockPath);
            }
        }

        /// <summary>
        /// New-ReleaseGateLockOwner (D-4) as written: owner identity is pid + process start + run id + continuation id, so
        /// a self-reexec hop can prove it inherited the lock instead of stealing it. The ASCII bytes of
        /// ConvertTo-Json -Compress of pid, processStartedAt, runId, lane, continuationId and acquiredAt.
        /// </summary>
        public byte[] OwnerBytes(string runId, string lane, string continuationId)
        {
            var pid = _processes.CurrentPid;
            var start = _processes.StartTimeUtc(pid);
            var json = new StringBuilder(256).Append('{');
            LockJson.Number(json, "pid", pid).Append(',');
            LockJson.String(json, "processStartedAt", start.HasValue ? start.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : "").Append(',');
            LockJson.String(json, "runId", runId ?? "").Append(',');
            LockJson.String(json, "lane", lane ?? "").Append(',');
            LockJson.String(json, "continuationId", continuationId ?? "").Append(',');
            LockJson.String(json, "acquiredAt", _clock.GetUtcNow().UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
            return LockJson.Bytes(json.Append('}'));
        }

        /// <summary>
        /// Exit-ReleaseGateNativeLock: when this process owns the lock, deletes native-run.lock if it still names this
        /// process (and this run, when one is given). Released only after the run's owned children are accounted for.
        /// </summary>
        public void Exit(string coordinationRoot, string runId = "")
        {
            if (!OwnsLock) return;
            var lockPath = LockPath(coordinationRoot);
            var existing = NightlyLockRecord.Read(lockPath);
            var same = existing != null && existing.Pid == _processes.CurrentPid
                && (string.IsNullOrWhiteSpace(runId) || string.Equals(existing.Text("runId"), runId, StringComparison.OrdinalIgnoreCase));
            if (same) LockJson.DeleteQuietly(lockPath);
            OwnsLock = false;
        }
    }
}
