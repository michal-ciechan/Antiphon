// The executed-manifest publication of test-phase-evidence.ps1, compiled (W2-nightly S08): opening and closing the
// writer context, the next header, the commit file, the envelope append, the prefix rescan, gap records and their
// replay, the executed-chunk append, the publication state, the projection (Complete-TestPhaseExecutedProjection), the
// live prefix reader (Read-TestPhaseExecutedPrefix) and recovery (Invoke-TestPhaseExecutedRecovery); with the journal
// half of Write-TestPhaseExecutedManifest (PublishChunk: the bound context, else one opened, published, projected and
// closed) and the failure marker they write. The journal head they work on is TestPhaseJournal.cs (S07).
//
// Plain C#: PowerShell's script scope reaches these as a TestPhasePublicationScope (the process timing root, the
// host's edition and version for the writer environment, the commit seams); the caller keeps its own bound context
// ($script:TestPhaseExecutedContext). Bytes written (records, gaps, state, seal, projection) are those the PowerShell
// functions wrote: recovery re-reads old runs. Evidence writes go through EvidenceFiles while a nightly run defers
// them (EvidenceFiles.Buffered). ASCII-only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Antiphon.Nightly
{
    /// <summary>
    /// The inputs a publication call reads from PowerShell's script scope: $script:QualificationTimingRoot, the
    /// host's $PSVersionTable.PSEdition and .PSVersion (the writer environment) and the commit seams
    /// $script:TestPhaseCommitBarrier and $script:TestPhaseCommitFault (null in production). The manifests and the
    /// summary (TestPhaseManifests.cs, TestPhaseRunSummary.cs) read the same scope and its two read clocks.
    /// </summary>
    public sealed class TestPhasePublicationScope
    {
        public TestPhasePublicationScope() { }

        public TestPhasePublicationScope(string qualificationTimingRoot, string hostEdition, string hostVersion, object commitBarrier, object commitFault)
        {
            QualificationTimingRoot = qualificationTimingRoot ?? "";
            HostEdition = hostEdition ?? "";
            HostVersion = hostVersion ?? "";
            CommitBarrier = commitBarrier;
            CommitFault = commitFault;
        }

        public string QualificationTimingRoot { get; set; } = "";
        public string HostEdition { get; set; } = "";
        public string HostVersion { get; set; } = "";
        /// <summary>Wait-TestPhaseCommitBarrier's seam (Stage, Signal, Release; or a Func&lt;string, bool&gt;).</summary>
        public object CommitBarrier { get; set; }
        /// <summary>Test-TestPhaseCommitFault's seam (Stage, Remaining).</summary>
        public object CommitFault { get; set; }
        /// <summary>
        /// The writer environment a context records. Blank (production): this host's, from HostEdition and
        /// HostVersion when the context opens. A test's in-process writer standing in for another host names its.
        /// </summary>
        public string WriterEnvironment { get; set; } = "";
        /// <summary>
        /// The TRX read clock of the manifests and the summary ($script:TestPhaseTrxReadWatch): null (production) is a
        /// stopwatch started for each TRX; a test's stopped stopwatch never reaches the 200 ms deadline.
        /// </summary>
        public Stopwatch TrxReadWatch { get; set; }
        /// <summary>
        /// The summary's shared re-read clock ($script:TestPhaseSummaryReReadWatch): a Stopwatch or an ITestPhaseWatch;
        /// null (production) is a new stopwatch for each summary.
        /// </summary>
        public object SummaryReReadWatch { get; set; }

        internal string Writer() =>
            string.IsNullOrWhiteSpace(WriterEnvironment) ? TestPhaseJournal.WriterEnvironment(HostEdition, HostVersion) : WriterEnvironment;
    }

    /// <summary>A gap record not yet committed (Add-TestPhaseGapRecord's pending entry); replayed in order.</summary>
    public sealed class TestPhasePendingGap
    {
        public string Reason { get; set; } = "";
        public string Suite { get; set; } = "";
        public string Chunk { get; set; } = "";
        public string RunId { get; set; } = "";
        public string Attempt { get; set; } = "";
        public string HeaderRun { get; set; } = "";
        public string HeaderAttempt { get; set; } = "";
        public string HeaderSuite { get; set; } = "";
        public string PublicationId { get; set; } = "";
    }

    /// <summary>Read-TestPhaseExecutedPrefix's result: the live journal's committed prefix, read without the writer.</summary>
    public sealed class TestPhaseExecutedPrefix
    {
        public string Generation { get; set; } = "";
        public int LastSequence { get; set; }
        public string RecordKind { get; set; } = "none";
        public string[] Quality { get; set; } = new string[0];
        public bool Complete { get; set; }
        public bool PublicationComplete { get; set; }
        public string[] Chunks { get; set; } = new string[0];
        public bool Refused { get; set; }
        public string Reason { get; set; } = "";
        public bool Unresolved { get; set; }
        public string Terminal { get; set; } = "";
    }

    /// <summary>A refused commit step; the message is the reason the PowerShell functions threw.</summary>
    public sealed class TestPhaseCommitException : IOException
    {
        public TestPhaseCommitException(string message) : base(message) { }
    }

    public static class TestPhasePublication
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly TestPhasePublicationScope NoScope = new TestPhasePublicationScope();

        private static TestPhasePublicationScope Of(TestPhasePublicationScope scope) => scope ?? NoScope;

        private static string GenerationDirectory(TestPhaseJournalContext context) =>
            Path.Combine(TestPhaseJournal.JournalRoot(context.TimingRoot), context.Generation ?? "");

        private static string RecordPath(TestPhaseJournalContext context, int sequence) =>
            Path.Combine(GenerationDirectory(context), sequence.ToString("00000000", CultureInfo.CurrentCulture) + ".json");

        // Test-NightlyEvidencePath -PathType Leaf: a file, seeing queued writes.
        private static bool EvidenceFileExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return EvidenceFiles.Buffered ? EvidenceFiles.FileExists(path) : File.Exists(path);
        }

        private static string ReadEvidenceText(string path) =>
            EvidenceFiles.Buffered ? EvidenceFiles.ReadAllText(path) : File.ReadAllText(path);

        private static void WriteEvidenceText(string path, string text)
        {
            if (EvidenceFiles.Buffered) { EvidenceFiles.WriteAllText(path, text); return; }
            File.WriteAllText(path, text ?? "", Utf8NoBom);
        }

        // $o.PSObject.Properties.Name -contains 'name' (case-insensitive).
        private static bool Has(object o, string name)
        {
            if (o == null) return false;
            PsValue.Property(o, name, out var found);
            return found;
        }

        private static string Text(object o, string name) => PsValue.Str(PsValue.Prop(o, name));

        // ---- the failure marker ----

        /// <summary>
        /// Write-TestPhaseFailureMarker: the code in timing/phase-summary-failed (the process timing root when set,
        /// else RunDirectory/timing). An unwritable timing directory does not change the caller's verdict: nothing
        /// throws.
        /// </summary>
        public static void WriteFailureMarker(string runDirectory, string qualificationTimingRoot, string code)
        {
            try
            {
                var dir = !string.IsNullOrWhiteSpace(qualificationTimingRoot) ? qualificationTimingRoot
                    : string.IsNullOrEmpty(runDirectory) ? "" : Path.Combine(runDirectory, "timing");
                if (string.IsNullOrEmpty(dir)) return;
                if (!File.Exists(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                WriteEvidenceText(Path.Combine(dir, "phase-summary-failed"), code ?? "");
            }
            catch (Exception)
            {
                // An unwritable timing directory must not change the caller's verdict.
            }
        }

        // ---- open and close ----

        /// <summary>
        /// Open-TestPhaseExecutedContext: the writer context of the timing root (TestPhaseJournal.ExecutedTimingRoot),
        /// holding its writer.lock. Refused (closed) for no root, an unsafe root (a link, a file, a reparse point), a
        /// root that is a file, or a held lock. A journal without current.json starts a generation: seeded with the
        /// legacy flat manifest when one is there (a malformed one is kept as a predecessor and leaves the generation
        /// unresolved), else empty. A pointer bound to another root starts an empty, unresolved one (pointer-binding).
        /// Otherwise the generation it names is imported (TestPhaseJournal.ImportExistingGeneration). The caller binds
        /// a writer context ($script:TestPhaseExecutedContext) itself.
        /// </summary>
        public static TestPhaseJournalContext Open(string logRoot, string timingRoot, string runId, string attempt, string suite, string source,
            string invocationId, bool projectOnly, bool recover, TestPhasePublicationScope scope)
        {
            scope = Of(scope);
            if (string.IsNullOrWhiteSpace(source)) source = "unknown";
            var timing = TestPhaseJournal.ExecutedTimingRoot(logRoot, timingRoot, scope.QualificationTimingRoot);
            if (string.IsNullOrWhiteSpace(timing)) return TestPhaseJournal.RefusedContext("timing-root-missing");
            var probe = timing;
            if (!string.IsNullOrWhiteSpace(timingRoot)) probe = timingRoot;
            else if (!string.IsNullOrWhiteSpace(scope.QualificationTimingRoot)) probe = scope.QualificationTimingRoot;
            else if (!string.IsNullOrWhiteSpace(logRoot)) probe = Path.Combine(logRoot, "timing");
            if (TestPhaseJournal.UnsafeDirectory(probe)) return TestPhaseJournal.RefusedContext("unsafe-root");
            if (EvidenceFileExists(timing)) return TestPhaseJournal.RefusedContext("timing-root-is-file");
            TestPhaseJournal.WriteDirectory(timing);
            var journal = TestPhaseJournal.JournalRoot(timing);
            TestPhaseJournal.WriteDirectory(journal);
            FileStream writerLock;
            try { writerLock = File.Open(Path.Combine(journal, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (Exception) { return TestPhaseJournal.RefusedContext("writer-lock-held"); }
            try { return Bind(timing, journal, logRoot, runId, attempt, suite, source, invocationId, projectOnly, recover, writerLock, scope.Writer()); }
            catch (Exception)
            {
                // A generation that cannot be opened (a legacy version that is no number) releases the lock it took.
                writerLock.Dispose();
                throw;
            }
        }

        // Open-TestPhaseExecutedContext once writer.lock is held: the context and its generation.
        private static TestPhaseJournalContext Bind(string timing, string journal, string logRoot, string runId, string attempt, string suite,
            string source, string invocationId, bool projectOnly, bool recover, FileStream writerLock, string writer)
        {
            var context = TestPhaseJournal.OpenContext(timing, logRoot, runId, attempt, suite, source, invocationId, writerLock, writer);
            context.ProjectOnly = projectOnly;
            var current = TestPhaseJournal.ReadJsonFile(Path.Combine(journal, "current.json"));
            if (current == null)
            {
                var flat = Path.Combine(timing, "groups-executed.json");
                if (TestPhaseFiles.Kind(flat, out _) == "regular")
                {
                    var parsed = TestPhaseJournal.ReadJsonFile(flat);
                    var valid = false;
                    if (parsed != null && Has(parsed, "version") && PsOps.ToInt(PsValue.Prop(parsed, "version")) == 1)
                    {
                        if (Has(parsed, "chunks") || Has(parsed, "chunk")) valid = true;
                    }
                    TestPhaseJournal.InitializeSeedGeneration(context, flat, "", !valid);
                }
                else TestPhaseJournal.InitializeEmptyGeneration(context, "");
            }
            else
            {
                var generation = Text(current, "generation");
                var bound = TestPhaseText.OrdinalEqual(Text(current, "timingRoot"), timing) && !string.IsNullOrWhiteSpace(generation);
                if (!bound)
                {
                    context.Unresolved = true;
                    context.CorruptionReason = "pointer-binding";
                    TestPhaseJournal.InitializeEmptyGeneration(context, generation);
                }
                else TestPhaseJournal.ImportExistingGeneration(context, generation, projectOnly, recover);
            }
            return context;
        }

        /// <summary>
        /// Test-TestPhaseJournalWriteFailure: true when a failed deferred write (as "path: message") is under the
        /// context's journal (or the journal root cannot be named).
        /// </summary>
        public static bool JournalWriteFailure(TestPhaseJournalContext context, string[] failures)
        {
            string journal;
            try { journal = Path.GetFullPath(TestPhaseJournal.JournalRoot(context.TimingRoot)); }
            catch (Exception) { return true; }
            if (string.IsNullOrWhiteSpace(journal)) return true;
            var prefix = journal.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            foreach (var item in failures ?? new string[0])
            {
                var text = item ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;
                var path = text;
                var split = text.IndexOf(": ", StringComparison.CurrentCulture);
                if (split > 0) path = text.Substring(0, split);
                try { path = Path.GetFullPath(path); } catch (Exception) { }
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Close-TestPhaseExecutedContext: releases writer.lock and closes the context. While a nightly run defers
        /// evidence writes, the queue lands first; a failed write leaves the context unresolved (commit-unpublished,
        /// deferred-evidence-write-failed), and a failed write under its journal keeps the lock and the context open.
        /// The caller unbinds a closed context it bound.
        /// </summary>
        public static void Close(TestPhaseJournalContext context)
        {
            if (context == null) return;
            if (EvidenceFiles.Buffered && context.Lock != null)
            {
                var named = new List<string>();
                foreach (var failure in EvidenceFiles.Flush())
                    if (!string.IsNullOrWhiteSpace(failure)) named.Add(failure);
                if (named.Count > 0)
                {
                    context.Reason = "commit-unpublished";
                    context.Unresolved = true;
                    if (!context.Gaps.Contains("deferred-evidence-write-failed")) context.Gaps.Add("deferred-evidence-write-failed");
                    // A failed journal write keeps writer.lock. Any other path is already retained for the
                    // verdict, so release the lock and let a later chunk in this process open.
                    if (JournalWriteFailure(context, named.ToArray())) return;
                }
            }
            if (context.Lock != null)
            {
                try { context.Lock.Dispose(); } catch (Exception) { }
                context.Lock = null;
            }
            context.Closed = true;
        }

        // Get-TestPhaseNextHeader: the header a record carries (the context's once it has one).
        private static void NextHeader(TestPhaseJournalContext context, string runId, string attempt, string suite,
            out string run, out string tried, out string suiteName)
        {
            run = runId ?? "";
            tried = attempt ?? "";
            suiteName = suite ?? "";
            if (!context.HeaderReady) return;
            if (!string.IsNullOrWhiteSpace(context.HeaderRun)) run = context.HeaderRun;
            if (context.HeaderAttemptSeen) tried = context.HeaderAttempt ?? "";
            if (!string.IsNullOrWhiteSpace(context.HeaderSuite)) suiteName = context.HeaderSuite;
        }

        // ---- commit ----

        /// <summary>
        /// Write-TestPhaseCommitFile: writes a record once. During a nightly run with no commit seam the record is
        /// queued on the evidence file system and read back after its own flush; otherwise a temporary file is written,
        /// flushed (to the operating system, not forced to the disk), renamed to the record and read back, with the
        /// barrier and fault seams between the steps. Throws (commit-exists, commit-unpublished, commit-unreadable,
        /// commit-barrier-timeout, commit-fault-*) without leaving the temporary file.
        /// </summary>
        public static void WriteCommitFile(string finalPath, string text, TestPhasePublicationScope scope)
        {
            scope = Of(scope);
            text = text ?? "";
            var dir = Path.GetDirectoryName(finalPath);
            TestPhaseJournal.WriteDirectory(dir);
            var barrier = scope.CommitBarrier;
            var fault = scope.CommitFault;
            if (EvidenceFiles.Buffered && barrier == null && fault == null)
            {
                if (EvidenceFileExists(finalPath)) throw new TestPhaseCommitException("commit-exists");
                EvidenceFiles.WriteAllText(finalPath, text);
                var unpublished = EvidenceFiles.Flush(finalPath);
                if (unpublished.Length > 0) throw new TestPhaseCommitException("commit-unpublished " + string.Join("; ", unpublished));
                var onDisk = File.ReadAllText(finalPath);
                if (!PsValue.Eq(onDisk, text, true)) throw new TestPhaseCommitException("commit-unreadable");
                return;
            }
            if (!TestPhaseJournal.WaitCommitBarrier(barrier, "BeforeTempComplete")) throw new TestPhaseCommitException("commit-barrier-timeout");
            if (TestPhaseJournal.CommitFault(fault, "CreateTemp")) throw new TestPhaseCommitException("commit-fault-temp");
            var tmp = Path.Combine(dir ?? "", "." + Guid.NewGuid().ToString("n") + ".tmp");
            FileStream stream = null;
            try
            {
                stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var bytes = Utf8NoBom.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
                if (TestPhaseJournal.CommitFault(fault, "Flush")) throw new TestPhaseCommitException("commit-fault-flush");
                // Flushed to the operating system, not forced to the disk: after a crash the latest record may be
                // missing or empty, which the prefix reader reports as a shorter prefix or as corruption.
                stream.Flush();
                stream.Dispose();
                stream = null;
                if (!TestPhaseJournal.WaitCommitBarrier(barrier, "AfterFlushBeforeRename")) throw new TestPhaseCommitException("commit-barrier-timeout");
                if (TestPhaseJournal.CommitFault(fault, "Rename")) throw new TestPhaseCommitException("commit-fault-rename");
                if (File.Exists(finalPath)) throw new TestPhaseCommitException("commit-exists");
                File.Move(tmp, finalPath);
                tmp = "";
                if (!TestPhaseJournal.WaitCommitBarrier(barrier, "AfterRenameBeforeAck")) throw new TestPhaseCommitException("commit-barrier-timeout");
                if (TestPhaseJournal.CommitFault(fault, "Acknowledge")) throw new TestPhaseCommitException("commit-fault-ack");
                var readBack = File.ReadAllText(finalPath);
                if (!PsValue.Eq(readBack, text, true)) throw new TestPhaseCommitException("commit-unreadable");
            }
            catch (Exception)
            {
                if (stream != null) { try { stream.Dispose(); } catch (Exception) { } }
                if (!string.IsNullOrWhiteSpace(tmp) && File.Exists(tmp))
                {
                    try { File.Delete(tmp); } catch (Exception) { }
                }
                throw;
            }
        }

        /// <summary>
        /// Add-TestPhaseJournalEnvelope: commits the record at this sequence, retried once within 2 s. A record already
        /// there with a good digest and this publication id is this publication (true); any other is commit-conflict.
        /// False (the context's Reason set to the last failure) when it did not commit.
        /// </summary>
        public static bool AppendEnvelope(TestPhaseJournalContext context, string text, string publicationId, int sequence, TestPhasePublicationScope scope)
        {
            var final = RecordPath(context, sequence);
            var watch = Stopwatch.StartNew();
            var attempt = 0;
            var lastError = "";
            while (attempt < 2)
            {
                if (attempt > 0 && watch.Elapsed.TotalSeconds > 2) break;
                attempt++;
                try
                {
                    if (EvidenceFileExists(final))
                    {
                        var existing = ReadEvidenceText(final);
                        if (TestPhaseText.RecordDigest(existing) && existing.Contains(publicationId ?? "")) return true;
                        throw new TestPhaseCommitException("commit-conflict");
                    }
                    WriteCommitFile(final, text, scope);
                    return true;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message ?? "";
                }
            }
            context.Reason = lastError;
            return false;
        }

        // ---- recovery inside a writer ----

        /// <summary>
        /// Invoke-TestPhasePrefixRescan: after a failed publication, once until the next success, the generation's
        /// prefix is re-read from disk (records, sequences, gap memory; a broken prefix leaves it unresolved).
        /// </summary>
        public static void PrefixRescan(TestPhaseJournalContext context)
        {
            if (!context.NeedsRecovery) return;
            if (context.RescanAttempted) return;
            context.RescanAttempted = true;
            context.PrefixRescans++;
            var prefix = TestPhaseJournal.ReadGenerationPrefix(GenerationDirectory(context));
            context.Records = prefix.Records;
            context.LastCommitted = prefix.LastSequence;
            context.NextSequence = prefix.NextSequence;
            TestPhaseJournal.ImportGapMemory(context, prefix.Records);
            if (prefix.Corrupt)
            {
                context.Unresolved = true;
                if (string.IsNullOrWhiteSpace(context.CorruptionReason)) context.CorruptionReason = prefix.Reason;
            }
        }

        /// <summary>
        /// Add-TestPhaseGapRecord: the context needs recovery and has a publication gap; a gap record (the chunk that
        /// did not publish, under its header) is committed as the next record, or kept pending (once per publication
        /// id) when it cannot be. True when committed.
        /// </summary>
        public static bool AddGapRecord(TestPhaseJournalContext context, string reason, string suite, string chunk, string runId, string attempt,
            string headerRun, string headerAttempt, string headerSuite, string publicationId, TestPhasePublicationScope scope)
        {
            reason = reason ?? "";
            suite = suite ?? "";
            chunk = chunk ?? "";
            context.NeedsRecovery = true;
            if (!context.Gaps.Contains("publication-gap")) context.Gaps.Add("publication-gap");
            if (string.IsNullOrWhiteSpace(headerRun)) headerRun = runId ?? "";
            if (string.IsNullOrWhiteSpace(headerAttempt)) headerAttempt = attempt ?? "";
            if (string.IsNullOrWhiteSpace(headerSuite)) headerSuite = suite;
            if (string.IsNullOrWhiteSpace(publicationId)) publicationId = Guid.NewGuid().ToString("n");
            var sequence = context.NextSequence;
            var envelope = TestPhaseJournal.EnvelopeText("gap", sequence, publicationId, context, reason, suite, chunk, suite, chunk, headerRun,
                headerAttempt, headerSuite, "", "");
            var ok = AppendEnvelope(context, envelope, publicationId, sequence, scope);
            if (!ok)
            {
                if (context.PendingGaps == null) context.PendingGaps = new List<object>();
                var already = false;
                foreach (var pending in context.PendingGaps)
                {
                    if (PsValue.Eq(PendingId(pending), publicationId, true)) { already = true; break; }
                }
                if (!already)
                {
                    context.PendingGaps.Add(new TestPhasePendingGap
                    {
                        Reason = reason, Suite = suite, Chunk = chunk, RunId = runId ?? "", Attempt = attempt ?? "",
                        HeaderRun = headerRun ?? "", HeaderAttempt = headerAttempt ?? "", HeaderSuite = headerSuite ?? "", PublicationId = publicationId,
                    });
                }
                return false;
            }
            context.Records.Add(new TestPhaseJournalRecord
            {
                Kind = "gap", Sequence = sequence, FreshJson = "", SettledJson = "", FreshSuite = suite, FreshChunk = chunk, SettledSuite = suite,
                SettledChunk = chunk, HeaderRun = headerRun ?? "", HeaderAttempt = headerAttempt ?? "", HeaderSuite = headerSuite ?? "",
                PublicationId = publicationId,
            });
            context.LastCommitted = sequence;
            context.NextSequence = sequence + 1;
            return true;
        }

        private static string PendingId(object pending) =>
            pending is TestPhasePendingGap gap ? gap.PublicationId ?? "" : Text(pending, "PublicationId");

        private static TestPhasePendingGap Pending(object pending)
        {
            if (pending is TestPhasePendingGap gap) return gap;
            return new TestPhasePendingGap
            {
                Reason = Text(pending, "Reason"), Suite = Text(pending, "Suite"), Chunk = Text(pending, "Chunk"), RunId = Text(pending, "RunId"),
                Attempt = Text(pending, "Attempt"), HeaderRun = Text(pending, "HeaderRun"), HeaderAttempt = Text(pending, "HeaderAttempt"),
                HeaderSuite = Text(pending, "HeaderSuite"), PublicationId = Text(pending, "PublicationId"),
            };
        }

        /// <summary>Submit-TestPhasePendingGaps: the pending gap records committed in order; false at the first that is not.</summary>
        public static bool SubmitPendingGaps(TestPhaseJournalContext context, TestPhasePublicationScope scope)
        {
            if (context.PendingGaps == null) return true;
            while (context.PendingGaps.Count > 0)
            {
                var pending = Pending(context.PendingGaps[0]);
                var ok = AddGapRecord(context, pending.Reason, pending.Suite, pending.Chunk, pending.RunId, pending.Attempt, pending.HeaderRun,
                    pending.HeaderAttempt, pending.HeaderSuite, pending.PublicationId, scope);
                if (!ok) return false;
                if (context.PendingGaps.Count > 0 && PsValue.Eq(PendingId(context.PendingGaps[0]), pending.PublicationId, true))
                    context.PendingGaps.RemoveAt(0);
            }
            return true;
        }

        /// <summary>
        /// Add-TestPhaseExecutedChunk: commits a fresh chunk document (and its settled form) as the context's next
        /// record under the next header. A closed or refused context throws context-closed, a context whose generation
        /// is no longer current throws stale-context. Pending gaps are committed first; when they cannot be, or the
        /// record cannot be, the failure marker is written (and for the record a gap is added) and the result is false.
        /// </summary>
        public static bool AddExecutedChunk(TestPhaseJournalContext context, object fresh, string suite, string chunk, string runId, string attempt,
            TestPhasePublicationScope scope)
        {
            scope = Of(scope);
            suite = suite ?? "";
            chunk = chunk ?? "";
            if (context.Closed || context.Refused) throw new InvalidOperationException("context-closed");
            PrefixRescan(context);
            var current = TestPhaseJournal.ReadJsonFile(Path.Combine(TestPhaseJournal.JournalRoot(context.TimingRoot), "current.json"));
            if (current == null || !TestPhaseText.OrdinalEqual(Text(current, "generation"), context.Generation)) throw new InvalidOperationException("stale-context");
            if (context.PendingGaps != null && context.PendingGaps.Count > 0)
            {
                if (!SubmitPendingGaps(context, scope))
                {
                    WriteFailureMarker(context.LogRoot, scope.QualificationTimingRoot, "phase-summary-failed");
                    return false;
                }
            }
            NextHeader(context, runId, attempt, suite, out var headerRun, out var headerAttempt, out var headerSuite);
            var freshJson = TestPhaseJson.Text(fresh, 0);
            var parsed = NightlyJson.ParseText(freshJson, 1024, true);
            var settled = TestPhaseJournal.SettledChunk(parsed);
            foreach (var row in PsValue.AsArray(settled["outcomes"])) if (row != null) context.SettledRows++;
            var settledJson = TestPhaseJson.Text(settled, 0);
            var publicationId = Guid.NewGuid().ToString("n");
            var sequence = context.NextSequence;
            var settledSuite = PsValue.Str(settled["suite"]);
            var settledChunk = PsValue.Str(settled["chunk"]);
            var envelope = TestPhaseJournal.EnvelopeText("chunk", sequence, publicationId, context, "", suite, chunk, settledSuite, settledChunk,
                headerRun, headerAttempt, headerSuite, freshJson, settledJson);
            if (!AppendEnvelope(context, envelope, publicationId, sequence, scope))
            {
                WriteFailureMarker(context.LogRoot, scope.QualificationTimingRoot, "phase-summary-failed");
                AddGapRecord(context, context.Reason, suite, chunk, runId, attempt, headerRun, headerAttempt, headerSuite, "", scope);
                return false;
            }
            if (!TestPhaseJournal.WaitCommitBarrier(scope.CommitBarrier, "AfterAckBeforeProjection")) throw new TestPhaseCommitException("commit-barrier-timeout");
            context.Records.Add(new TestPhaseJournalRecord
            {
                Kind = "chunk", Sequence = sequence, FreshJson = freshJson, SettledJson = settledJson, FreshSuite = suite, FreshChunk = chunk,
                SettledSuite = settledSuite, SettledChunk = settledChunk, HeaderRun = headerRun, HeaderAttempt = headerAttempt,
                HeaderSuite = headerSuite, PublicationId = publicationId,
            });
            context.LastCommitted = sequence;
            context.NextSequence = sequence + 1;
            context.NeedsRecovery = false;
            context.RescanAttempted = false;
            TestPhaseJournal.UpdateContextHeader(context, headerRun, headerAttempt, headerSuite);
            return true;
        }

        // ---- the publication state and the projection ----

        /// <summary>
        /// Get-TestPhasePublicationStateText: groups-executed.state.json's text - the generation, its last sequence,
        /// the root, whether the publication is complete (no corruption, no committed or pending gap), and the
        /// projection's digest ("" for no projection).
        /// </summary>
        public static string PublicationStateText(TestPhaseJournalContext context, string projection)
        {
            var quality = new List<string>();
            if (context.Unresolved && !quality.Contains("publication-corruption")) quality.Add("publication-corruption");
            var pending = context.PendingGaps == null ? 0 : context.PendingGaps.Count;
            if ((context.Gaps.Count > 0 || pending > 0) && !quality.Contains("publication-gap")) quality.Add("publication-gap");
            var state = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
            {
                { "version", 1 },
                { "generation", context.Generation ?? "" },
                { "sequence", context.LastCommitted },
                { "root", context.TimingRoot ?? "" },
                { "publicationComplete", quality.Count == 0 },
                { "unresolved", context.Unresolved },
                { "reason", context.CorruptionReason ?? "" },
                { "projectionDigest", projection == null ? "" : TestPhaseRecords.Sha256Text(projection) },
                { "quality", quality.ToArray() },
            };
            return TestPhaseJson.Text(state, 0);
        }

        /// <summary>
        /// Write-TestPhasePublicationState: writes the state and returns its file's digest. The projection is a [string]
        /// parameter there: no projection is "", whose digest the state then carries.
        /// </summary>
        public static string WritePublicationState(TestPhaseJournalContext context, string projection)
        {
            var text = PublicationStateText(context, projection ?? "");
            var path = Path.Combine(context.TimingRoot, "groups-executed.state.json");
            TestPhaseFiles.WriteTextAtomic(path, text);
            return TestPhaseText.Sha256File(path);
        }

        /// <summary>
        /// Complete-TestPhaseExecutedProjection -LogRoot -TimingRoot: a project-only context opened, projected and
        /// closed. Returns it (refused when it could not open).
        /// </summary>
        public static TestPhaseJournalContext CompleteProjection(string logRoot, string timingRoot, string terminal, TestPhasePublicationScope scope)
        {
            var context = Open(logRoot, timingRoot, "", "", "", "unknown", "", true, false, scope);
            if (context == null || context.Refused) return context;
            try { return Complete(context, terminal, scope); }
            finally { Close(context); }
        }

        /// <summary>
        /// Complete-TestPhaseExecutedProjection -Context: pending gaps committed when they can be, then the flat manifest
        /// (groups-executed.json) projected from the journal and the publication state written - nothing when there is
        /// no projection, no corruption, no gap and no terminal. A terminal also seals the generation (seal.json: the
        /// terminal, the last sequence, the projection's and the state's digests). A refused context is returned as is.
        /// </summary>
        public static TestPhaseJournalContext CompleteProjection(TestPhaseJournalContext context, string terminal, TestPhasePublicationScope scope)
        {
            if (context == null || context.Refused) return context;
            return Complete(context, terminal, scope);
        }

        private static TestPhaseJournalContext Complete(TestPhaseJournalContext context, string terminal, TestPhasePublicationScope scope)
        {
            if (context.PendingGaps != null && context.PendingGaps.Count > 0) SubmitPendingGaps(context, scope);
            var text = TestPhaseJournal.ProjectedDocument(context);
            var terminalSet = !string.IsNullOrWhiteSpace(terminal);
            if (text == null && !context.Unresolved && context.Gaps.Count == 0 && !terminalSet) return context;
            if (text != null)
            {
                context.ProjectionWrites++;
                TestPhaseFiles.WriteTextAtomic(Path.Combine(context.TimingRoot, "groups-executed.json"), text);
            }
            var stateDigest = WritePublicationState(context, text);
            if (terminalSet)
            {
                var seal = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
                {
                    { "version", 1 },
                    { "generation", context.Generation ?? "" },
                    { "sequence", context.LastCommitted },
                    { "terminal", terminal ?? "" },
                    { "projectionDigest", text == null ? "" : TestPhaseRecords.Sha256Text(text) },
                    { "stateDigest", stateDigest ?? "" },
                };
                TestPhaseFiles.WriteTextAtomic(Path.Combine(GenerationDirectory(context), "seal.json"), TestPhaseJson.Text(seal, 0));
            }
            return context;
        }

        // ---- readers and recovery ----

        /// <summary>
        /// Read-TestPhaseExecutedPrefix: the committed prefix of the generation current.json names, read without the
        /// writer lock: its last sequence and record kind, the chunks it committed, publication-corruption for a
        /// broken prefix, and the seal's terminal. Complete is an intact, sealed, non-empty prefix. Refused only when
        /// there is no timing root.
        /// </summary>
        public static TestPhaseExecutedPrefix ReadExecutedPrefix(string logRoot, string timingRoot, string qualificationTimingRoot)
        {
            var timing = TestPhaseJournal.ExecutedTimingRoot(logRoot, timingRoot, qualificationTimingRoot);
            var empty = new TestPhaseExecutedPrefix();
            if (string.IsNullOrWhiteSpace(timing))
            {
                empty.Refused = true;
                empty.Reason = "timing-root-missing";
                return empty;
            }
            var journal = TestPhaseJournal.JournalRoot(timing);
            var current = TestPhaseJournal.ReadJsonFile(Path.Combine(journal, "current.json"));
            if (current == null) return empty;
            var generation = Text(current, "generation");
            var genDir = Path.Combine(journal, generation);
            var prefix = TestPhaseJournal.ReadGenerationPrefix(genDir);
            var kind = "none";
            if (prefix.Records.Count > 0) kind = TestPhaseJournalRead.Field(prefix.Records[prefix.Records.Count - 1], "Kind");
            var ids = new List<string>();
            foreach (var rec in prefix.Records)
                if (PsValue.Eq(TestPhaseJournalRead.Field(rec, "Kind"), "chunk", true)) ids.Add(TestPhaseJournalRead.Field(rec, "FreshChunk"));
            var quality = new List<string>();
            if (prefix.Corrupt) quality.Add("publication-corruption");
            var terminal = "";
            var seal = TestPhaseJournal.ReadJsonFile(Path.Combine(genDir, "seal.json"));
            if (seal != null && Has(seal, "terminal")) terminal = Text(seal, "terminal");
            return new TestPhaseExecutedPrefix
            {
                Generation = generation,
                LastSequence = prefix.LastSequence,
                RecordKind = kind,
                Quality = quality.ToArray(),
                Complete = !prefix.Corrupt && prefix.Records.Count > 0 && !string.IsNullOrWhiteSpace(terminal),
                PublicationComplete = !prefix.Corrupt,
                Chunks = ids.ToArray(),
                Refused = false,
                Reason = prefix.Reason ?? "",
                Unresolved = prefix.Corrupt,
                Terminal = terminal,
            };
        }

        /// <summary>
        /// Invoke-TestPhaseExecutedRecovery: takes the writer lock (refused for a live writer) as the generation's own
        /// writer (run, attempt and source from its metadata where not given) and rebuilds the flat projection and the
        /// state from the committed journal. Never seals: an intact sealed generation is restored only when the rebuilt
        /// digests match its seal (else Reason seal-digest-mismatch and nothing is written).
        /// </summary>
        public static TestPhaseJournalContext ExecutedRecovery(string logRoot, string timingRoot, string runId, string attempt, string suite, string source,
            TestPhasePublicationScope scope)
        {
            scope = Of(scope);
            var timing = TestPhaseJournal.ExecutedTimingRoot(logRoot, timingRoot, scope.QualificationTimingRoot);
            var run = runId ?? "";
            var tried = attempt ?? "";
            var from = source ?? "";
            if (!string.IsNullOrWhiteSpace(timing))
            {
                var journal = TestPhaseJournal.JournalRoot(timing);
                var current = TestPhaseJournal.ReadJsonFile(Path.Combine(journal, "current.json"));
                if (current != null && !string.IsNullOrWhiteSpace(Text(current, "generation")))
                {
                    var meta = TestPhaseJournal.ReadJsonFile(Path.Combine(journal, Text(current, "generation"), "metadata.json"));
                    if (meta != null)
                    {
                        if (string.IsNullOrWhiteSpace(run)) run = Text(meta, "runId");
                        if (string.IsNullOrWhiteSpace(tried)) tried = Text(meta, "attempt");
                        if (string.IsNullOrWhiteSpace(from)) from = Text(meta, "source");
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(from)) from = "unknown";
            var context = Open(logRoot, timingRoot, run, tried, suite, from, "", false, true, scope);
            if (context == null || context.Refused) return context;
            try
            {
                var seal = TestPhaseJournal.ReadJsonFile(Path.Combine(GenerationDirectory(context), "seal.json"));
                if (seal != null && !context.Unresolved)
                {
                    var preview = TestPhaseJournal.ProjectedDocument(context);
                    var projectionDigest = preview == null ? "" : TestPhaseRecords.Sha256Text(preview);
                    // The state text of this preview as Get-TestPhasePublicationStateText gave it (no projection: "").
                    var stateDigest = TestPhaseRecords.Sha256Text(PublicationStateText(context, preview));
                    var projectionMatch = TestPhaseText.OrdinalEqual(projectionDigest, Text(seal, "projectionDigest"));
                    var stateMatch = TestPhaseText.OrdinalEqual(stateDigest, Text(seal, "stateDigest"));
                    if (!projectionMatch || !stateMatch)
                    {
                        context.Reason = "seal-digest-mismatch";
                        return context;
                    }
                }
                Complete(context, "", scope);
                return context;
            }
            finally { Close(context); }
        }

        // ---- one chunk's publication ----

        /// <summary>
        /// Write-TestPhaseExecutedManifest's publication: the fresh chunk is committed into the bound context (the
        /// process's open writer) when there is one; otherwise a context is opened (source unknown) for it, the chunk
        /// committed, the journal projected when there is anything to project, and the context closed. A refused open
        /// or a failed commit writes the failure marker; neither throws. True when the chunk committed.
        /// </summary>
        public static bool PublishChunk(TestPhaseJournalContext bound, string logRoot, object fresh, string suite, string chunk, string runId,
            string attempt, TestPhasePublicationScope scope)
        {
            scope = Of(scope);
            var persistent = bound != null && !bound.Closed && !bound.Refused;
            TestPhaseJournalContext opened = null;
            try
            {
                if (!persistent)
                {
                    opened = Open(logRoot, "", runId, attempt, suite, "unknown", "", false, false, scope);
                    if (opened == null || opened.Refused)
                    {
                        WriteFailureMarker(logRoot, scope.QualificationTimingRoot, "phase-summary-failed");
                        opened = null;
                        return false;
                    }
                    bound = opened;
                }
                var committed = false;
                try { committed = AddExecutedChunk(bound, fresh, suite, chunk, runId, attempt, scope); }
                catch (Exception)
                {
                    WriteFailureMarker(logRoot, scope.QualificationTimingRoot, "phase-summary-failed");
                    committed = false;
                }
                if (!persistent)
                {
                    var project = committed || bound.HasSeed || bound.Unresolved || bound.Gaps.Count > 0 || bound.Records.Count > 0;
                    if (project) CompleteProjection(bound, "", scope);
                }
                return committed;
            }
            finally
            {
                if (opened != null) Close(opened);
            }
        }
    }
}
