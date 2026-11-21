// The executed-manifest journal head of test-phase-evidence.ps1, compiled (W2-nightly S07): time zones, the writer
// environment, the unsafe-directory check, the commit barrier and fault seams, the refused and open contexts, the
// journal root, seed rows, header and cache, envelope text, the generation prefix and metadata, empty and seed
// generations, the projected document, gap memory and existing-generation import; with the executed-chunk settlement
// (ConvertTo-TestPhaseExecutedChunk) and the header update they need. The publication that works on the context these
// return (open, commit, append, gaps, projection and recovery) is TestPhasePublication.cs (S08).
//
// Plain C#: JSON documents are read as ConvertFrom-Json reads them (NightlyJson.ParseText with dates); a document
// that is not one strict JSON object reads as unreadable. Bytes written (envelopes, metadata, pointer, seed cache,
// seed, projection) are those the PowerShell functions wrote: recovery re-reads old runs. Evidence writes go through
// EvidenceFiles while a nightly run defers them (EvidenceFiles.Buffered). ASCII-only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Antiphon.TestPhase;

namespace Antiphon.Nightly
{
    /// <summary>
    /// The executed-manifest writer's state (New-TestPhaseRefusedContext / New-TestPhaseOpenContext). Mutable: the
    /// publication functions read and set these properties.
    /// </summary>
    public sealed class TestPhaseJournalContext
    {
        public bool Refused { get; set; }
        public string Reason { get; set; } = "";
        public bool Closed { get; set; }
        public string Id { get; set; } = "";
        /// <summary>The held writer.lock stream (null when refused or closed).</summary>
        public IDisposable Lock { get; set; }
        public string TimingRoot { get; set; } = "";
        public string Generation { get; set; } = "";
        public string LogRoot { get; set; } = "";
        public string RunId { get; set; } = "";
        public string Attempt { get; set; } = "";
        public string Suite { get; set; } = "";
        public string Source { get; set; } = "";
        public string InvocationId { get; set; } = "";
        public bool HasSeed { get; set; }
        public bool HeaderReady { get; set; }
        public string HeaderRun { get; set; } = "";
        public string HeaderAttempt { get; set; } = "";
        public string HeaderSuite { get; set; } = "";
        public bool HeaderAttemptSeen { get; set; }
        /// <summary>The committed records of this generation (TestPhaseJournalRecord, or a publication's own record).</summary>
        public List<object> Records { get; set; } = new List<object>();
        /// <summary>The seed's settled rows (TestPhaseSeedRow).</summary>
        public List<object> SeedRows { get; set; } = new List<object>();
        public string SeedPath { get; set; } = "";
        public int LastCommitted { get; set; }
        public int NextSequence { get; set; } = 1;
        public bool Unresolved { get; set; }
        public string CorruptionReason { get; set; } = "";
        public List<string> Gaps { get; set; } = new List<string>();
        public bool ProjectOnly { get; set; }
        public string WriterEnvironment { get; set; } = "";
        public bool NeedsRecovery { get; set; }
        public bool RescanAttempted { get; set; }
        /// <summary>The gap records not yet committed (TestPhasePendingGap), in order.</summary>
        public List<object> PendingGaps { get; set; } = new List<object>();
        /// <summary>Diagnostics (the cost witnesses read them): outcome rows this context's appends settled.</summary>
        public int SettledRows { get; set; }
        /// <summary>Diagnostics: flat projections (groups-executed.json) this context wrote.</summary>
        public int ProjectionWrites { get; set; }
        /// <summary>Diagnostics: the prefix re-reads after a failed publication (TestPhasePublication.PrefixRescan).</summary>
        public int PrefixRescans { get; set; }
    }

    /// <summary>One settled seed row: the settled chunk document and its suite and chunk.</summary>
    public sealed class TestPhaseSeedRow
    {
        public string SettledJson { get; set; } = "";
        public string SettledSuite { get; set; } = "";
        public string SettledChunk { get; set; } = "";
    }

    /// <summary>Import-TestPhaseSeedRows' result: the settled rows and the document's header.</summary>
    public sealed class TestPhaseSeedImport
    {
        public List<object> Rows { get; set; } = new List<object>();
        public string Run { get; set; } = "";
        public string Attempt { get; set; } = "";
        public string Suite { get; set; } = "";
        public bool AttemptSeen { get; set; }
    }

    /// <summary>
    /// Read-TestPhaseGenerationPrefix's result: the records up to the first missing or broken one, the next sequence,
    /// whether (and why) the prefix broke, and the last sequence read.
    /// </summary>
    public sealed class TestPhaseGenerationPrefix
    {
        public List<object> Records { get; set; } = new List<object>();
        public int NextSequence { get; set; } = 1;
        public bool Corrupt { get; set; }
        public string Reason { get; set; } = "";
        public int LastSequence { get; set; }
    }

    public static class TestPhaseJournal
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        // ---- time zones ----

        /// <summary>
        /// Find-TestPhaseTimeZoneById: the system zone with this id, or null. A finder (a test's seam) answers instead
        /// when given; anything but a TimeZoneInfo from it is null.
        /// </summary>
        public static TimeZoneInfo FindTimeZoneById(string id, Func<string, TimeZoneInfo> finder)
        {
            if (finder != null) return finder(id ?? "");
            try { return TimeZoneInfo.FindSystemTimeZoneById(id ?? ""); }
            catch (Exception) { return null; }
        }

        /// <summary>ConvertTo-TestPhaseWindowsTimeZoneId: the Windows id of an IANA id, or "".</summary>
        public static string WindowsTimeZoneId(string ianaId)
        {
            try { return TimeZoneInfo.TryConvertIanaIdToWindowsId(ianaId ?? "", out var windows) ? windows ?? "" : ""; }
            catch (Exception) { return ""; }
        }

        /// <summary>ConvertTo-TestPhaseIanaTimeZoneId: the IANA id of a Windows id, or "".</summary>
        public static string IanaTimeZoneId(string windowsId)
        {
            try { return TimeZoneInfo.TryConvertWindowsIdToIanaId(windowsId ?? "", out var iana) ? iana ?? "" : ""; }
            catch (Exception) { return ""; }
        }

        /// <summary>Resolve-TestPhaseTimeZone with the system lookup.</summary>
        public static TimeZoneInfo ResolveTimeZone(string id) => ResolveTimeZone(id, null);

        /// <summary>
        /// Resolve-TestPhaseTimeZone: Local for a blank id; else the zone found by that id, by its Windows id (an IANA
        /// id where the lookup takes Windows ids only), or by its IANA id; null when none is found.
        /// </summary>
        public static TimeZoneInfo ResolveTimeZone(string id, Func<string, TimeZoneInfo> finder)
        {
            if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Local;
            var direct = FindTimeZoneById(id, finder);
            if (direct != null) return direct;
            var windows = WindowsTimeZoneId(id);
            if (!string.IsNullOrWhiteSpace(windows))
            {
                var mapped = FindTimeZoneById(windows, finder);
                if (mapped != null) return mapped;
            }
            var iana = IanaTimeZoneId(id);
            if (!string.IsNullOrWhiteSpace(iana))
            {
                var mapped = FindTimeZoneById(iana, finder);
                if (mapped != null) return mapped;
            }
            return null;
        }

        /// <summary>Get-TestPhasePinnedTimeZone: the zone the TZ variable names, else Local.</summary>
        public static TimeZoneInfo PinnedTimeZone()
        {
            string requested;
            try { requested = Environment.GetEnvironmentVariable("TZ") ?? ""; } catch (Exception) { requested = ""; }
            return PinnedTimeZone(requested, null);
        }

        /// <summary>Get-TestPhasePinnedTimeZone for a requested id: its resolved zone, else Local.</summary>
        public static TimeZoneInfo PinnedTimeZone(string requested, Func<string, TimeZoneInfo> finder)
        {
            if (string.IsNullOrWhiteSpace(requested)) return TimeZoneInfo.Local;
            return ResolveTimeZone(requested, finder) ?? TimeZoneInfo.Local;
        }

        // ---- the writer environment and the root checks ----

        /// <summary>
        /// Get-TestPhaseWriterEnvironment: edition|version|framework|culture|UI culture|local zone id|its current
        /// offset. The edition and version are the writer's host ($PSVersionTable.PSEdition and .PSVersion under
        /// PowerShell); a blank edition is Desktop. The local zone is the process zone in effect, so two hosts that
        /// share a TZ string but not a process zone differ at the resume comparison.
        /// </summary>
        public static string WriterEnvironment(string edition, string version)
        {
            var hostEdition = string.IsNullOrEmpty(edition) ? "Desktop" : edition;
            string framework;
            try { framework = RuntimeInformation.FrameworkDescription ?? ""; }
            catch (Exception) { framework = Environment.Version.ToString(); }
            var culture = "";
            var ui = "";
            try { culture = Thread.CurrentThread.CurrentCulture.Name ?? ""; } catch (Exception) { }
            try { ui = Thread.CurrentThread.CurrentUICulture.Name ?? ""; } catch (Exception) { }
            var tz = TimeZoneInfo.Local;
            var offset = tz.GetUtcOffset(DateTime.Now);
            var sign = offset.Ticks < 0 ? "-" : "+";
            var off = sign + Math.Abs(offset.Hours).ToString("00", CultureInfo.CurrentCulture) + ":" + Math.Abs(offset.Minutes).ToString("00", CultureInfo.CurrentCulture);
            return hostEdition + "|" + (version ?? "") + "|" + framework + "|" + culture + "|" + ui + "|" + (tz.Id ?? "") + "|" + off;
        }

        /// <summary>
        /// Test-TestPhaseUnsafeDirectory: true for a blank path, an existing non-directory, a directory link or reparse
        /// point, and on Linux anything lstat does not call a directory; false for a missing path or a plain directory.
        /// </summary>
        public static bool UnsafeDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            if (!File.Exists(path) && !Directory.Exists(path)) return false;
            if (!Directory.Exists(path)) return true;
            FileAttributes attributes;
            try { attributes = new DirectoryInfo(path).Attributes; }
            catch (Exception) { return true; }
            if ((attributes & FileAttributes.ReparsePoint) != 0) return true;
            if (OperatingSystem.IsWindows()) return false;
            if (FileIdentity.LStat(path, out LinuxStat stat) != 0) return true;
            return (stat.Mode & 0xF000) != 0x4000;
        }

        // ---- the commit seams ----

        /// <summary>
        /// Wait-TestPhaseCommitBarrier: when the barrier (a test seam: Stage, Signal, Release) names this stage, writes
        /// the stage to Signal and waits for the Release file. False when the release does not come within 120 s
        /// (the caller throws commit-barrier-timeout); true otherwise, and at once for no barrier or another stage.
        /// An in-process seam may be a Func&lt;string, bool&gt; instead: it is asked at every stage and answers the same.
        /// </summary>
        public static bool WaitCommitBarrier(object barrier, string stage)
        {
            if (barrier == null) return true;
            if (barrier is Func<string, bool> wait) return wait(stage ?? "");
            if (!PsValue.Eq(SeamText(barrier, "Stage"), stage ?? "", true)) return true;
            var signal = SeamText(barrier, "Signal");
            var release = SeamText(barrier, "Release");
            if (!string.IsNullOrWhiteSpace(signal))
            {
                var parent = Path.GetDirectoryName(signal);
                if (!string.IsNullOrEmpty(parent) && !File.Exists(parent) && !Directory.Exists(parent)) Directory.CreateDirectory(parent);
                File.WriteAllText(signal, stage ?? "", Utf8NoBom);
            }
            if (string.IsNullOrWhiteSpace(release)) return true;
            var watch = Stopwatch.StartNew();
            while (!File.Exists(release))
            {
                if (watch.ElapsedMilliseconds > 120000) return false;
                Thread.Sleep(20);
            }
            return true;
        }

        /// <summary>
        /// Test-TestPhaseCommitFault: true (and one fewer Remaining) when the fault (a test seam: Stage, Remaining)
        /// names this stage and has faults left.
        /// </summary>
        public static bool CommitFault(object fault, string stage)
        {
            if (fault == null) return false;
            if (!PsValue.Eq(SeamText(fault, "Stage"), stage ?? "", true)) return false;
            PsValue.Property(fault, "Remaining", out var hasRemaining);
            var remaining = hasRemaining ? PsOps.ToInt(PsValue.Prop(fault, "Remaining")) : 0;
            if (remaining <= 0) return false;
            SetSeamValue(fault, "Remaining", remaining - 1);
            return true;
        }

        // [string]$seam.Name when the property exists, else "".
        private static string SeamText(object seam, string name)
        {
            PsValue.Property(seam, name, out var found);
            return found ? PsValue.Str(PsValue.Prop(seam, name)) : "";
        }

        // $seam.Name = value.
        private static void SetSeamValue(object seam, string name, object value)
        {
            if (PsRuntime.Loaded && PsValue.IsRecord(seam)) { PsFields.SetValue(seam, name, value); return; }
            if (seam is PlainObject plain) { plain[name] = value; return; }
            if (seam is IDictionary dictionary) { dictionary[name] = value; return; }
            var property = seam.GetType().GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
            if (property != null && property.CanWrite) { property.SetValue(seam, Convert.ChangeType(value, property.PropertyType, CultureInfo.InvariantCulture)); return; }
            var field = seam.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
            if (field != null) field.SetValue(seam, Convert.ChangeType(value, field.FieldType, CultureInfo.InvariantCulture));
        }

        // ---- contexts and roots ----

        /// <summary>New-TestPhaseRefusedContext: a closed, refused context with a fresh id.</summary>
        public static TestPhaseJournalContext RefusedContext(string reason) => new TestPhaseJournalContext
        {
            Refused = true,
            Reason = reason ?? "",
            Closed = true,
            Id = Guid.NewGuid().ToString("n"),
        };

        /// <summary>New-TestPhaseOpenContext: an open context holding the writer lock, before any generation.</summary>
        public static TestPhaseJournalContext OpenContext(string timingRoot, string logRoot, string runId, string attempt, string suite,
            string source, string invocationId, IDisposable writerLock, string writerEnvironment)
        {
            var context = RefusedContext("");
            context.Refused = false;
            context.Closed = false;
            context.Lock = writerLock;
            context.TimingRoot = timingRoot ?? "";
            context.LogRoot = logRoot ?? "";
            context.RunId = runId ?? "";
            context.Attempt = attempt ?? "";
            context.Suite = suite ?? "";
            context.Source = source ?? "";
            context.InvocationId = invocationId ?? "";
            context.WriterEnvironment = writerEnvironment ?? "";
            return context;
        }

        /// <summary>
        /// Resolve-TestPhaseExecutedTimingRoot: the full path of the explicit timing root, else the process's
        /// qualification timing root, else LogRoot/timing; "" when none is set.
        /// </summary>
        public static string ExecutedTimingRoot(string logRoot, string timingRoot, string qualificationTimingRoot)
        {
            var selected = "";
            if (!string.IsNullOrWhiteSpace(timingRoot)) selected = timingRoot;
            else if (!string.IsNullOrWhiteSpace(qualificationTimingRoot)) selected = qualificationTimingRoot;
            else if (!string.IsNullOrWhiteSpace(logRoot)) selected = Path.Combine(logRoot, "timing");
            if (string.IsNullOrWhiteSpace(selected)) return "";
            return Path.GetFullPath(selected);
        }

        /// <summary>Get-TestPhaseJournalRoot: TimingRoot/groups-executed.journal.</summary>
        public static string JournalRoot(string timingRoot)
        {
            if (string.IsNullOrEmpty(timingRoot)) throw new ArgumentException("journal-root-timing-root-empty");
            return Path.Combine(timingRoot, "groups-executed.journal");
        }

        private static string GenerationDirectory(TestPhaseJournalContext context, string generation) =>
            Path.Combine(JournalRoot(context.TimingRoot), generation ?? "");

        // ---- evidence files (deferred while a nightly run defers them) ----

        /// <summary>Test-NightlyEvidencePath: a file or directory, seeing queued writes.</summary>
        public static bool EvidencePathExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (EvidenceFiles.Buffered) return EvidenceFiles.PathExists(path);
            return File.Exists(path) || Directory.Exists(path);
        }

        private static string ReadEvidenceText(string path) =>
            EvidenceFiles.Buffered ? EvidenceFiles.ReadAllText(path) : File.ReadAllText(path);

        private static string[] ReadEvidenceLines(string path) =>
            EvidenceFiles.Buffered ? EvidenceFiles.ReadAllLines(path) : File.ReadAllLines(path);

        private static void WriteEvidenceText(string path, string text)
        {
            if (EvidenceFiles.Buffered) { EvidenceFiles.WriteAllText(path, text); return; }
            File.WriteAllText(path, text ?? "", Utf8NoBom);
        }

        private static void WriteEvidenceLines(string path, string[] lines)
        {
            if (EvidenceFiles.Buffered) { EvidenceFiles.WriteAllLines(path, lines); return; }
            File.WriteAllLines(path, lines ?? new string[0], Utf8NoBom);
        }

        /// <summary>
        /// Read-TestPhaseJsonFile: the file as ConvertFrom-Json reads it (custom objects, object[] arrays, Int64 or
        /// Double numbers, ISO date-times as DateTime), or null when it is missing, unreadable or not one strict JSON
        /// object.
        /// </summary>
        public static object ReadJsonFile(string path)
        {
            if (!EvidencePathExists(path)) return null;
            try { return NightlyJson.ParseText(ReadEvidenceText(path), 1024, true); }
            catch (Exception) { return null; }
        }

        /// <summary>Test-TestPhasePathExists: a file or a directory at the path.</summary>
        public static bool PathExists(string path) => !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));

        /// <summary>Write-TestPhaseDirectory: creates the directory unless something is there.</summary>
        public static void WriteDirectory(string path)
        {
            if (!PathExists(path)) Directory.CreateDirectory(path);
        }

        // Copy-TestPhaseBytes: queued writes land first, then the bytes are copied over the destination.
        private static void CopyBytes(string source, string destination)
        {
            WriteDirectory(Path.GetDirectoryName(destination));
            EvidenceFiles.FlushKeeping(null);
            File.Copy(source, destination, true);
        }

        // Save-TestPhaseMalformedLegacy: the malformed flat manifest's bytes kept under predecessors/<generation>.
        private static void SaveMalformedLegacy(string timingRoot, string generation)
        {
            var flat = Path.Combine(timingRoot, "groups-executed.json");
            if (!EvidencePathExists(flat)) return;
            var dest = Path.Combine(JournalRoot(timingRoot), "predecessors", generation, "groups-executed.json");
            if (EvidencePathExists(dest)) return;
            CopyBytes(flat, dest);
        }

        // ---- the executed chunk ----

        /// <summary>
        /// ConvertTo-TestPhaseExecutedChunk -Item: the settled form of a chunk document as ConvertFrom-Json read it
        /// (each field converted as PowerShell's casts do; a value absent stays at its default), keys in manifest
        /// order. Settling a settled document gives the same document.
        /// </summary>
        public static OrderedDictionary SettledChunk(object item) =>
            ExecutedChunk(item, "", "", "", "", 0.0, false, 0, null, "", "", null, null, null);

        /// <summary>ConvertTo-TestPhaseExecutedChunk with the values of a chunk that just ran.</summary>
        public static OrderedDictionary FreshChunk(string chunk, string runId, string attempt, string suite, double hostWallSeconds,
            bool trxMissing, int passedCount, object outcomes, string trx, string trxDigest, object exitCode, object coverageComplete,
            object testsPassed) =>
            ExecutedChunk(null, chunk, runId, attempt, suite, hostWallSeconds, trxMissing, passedCount, outcomes, trx, trxDigest, exitCode,
                coverageComplete, testsPassed);

        private static OrderedDictionary ExecutedChunk(object item, string chunk, string runId, string attempt, string suite, double hostWallSeconds,
            bool trxMissing, int passedCount, object outcomes, string trx, string trxDigest, object exitCode, object coverageComplete,
            object testsPassed)
        {
            // One {testId, outcome} row per test (TestPhaseSummary.OutcomeRows).
            var prior = new List<object>();
            if (outcomes != null) prior.AddRange(TestPhaseSummary.OutcomeRows(PsValue.AsArray(outcomes), "testId", "outcome", true));
            var chunkId = chunk ?? "";
            var run = runId ?? "";
            var tried = attempt ?? "";
            var suiteName = suite ?? "";
            var wall = hostWallSeconds;
            var missing = trxMissing;
            var passed = passedCount;
            var trxPath = trx ?? "";
            var digest = trxDigest ?? "";
            var exit = exitCode;
            var coverage = coverageComplete;
            var tests = testsPassed;
            if (item != null)
            {
                if (Has(item, "chunk")) chunkId = PsValue.Str(PsValue.Prop(item, "chunk"));
                if (Has(item, "runId")) run = PsValue.Str(PsValue.Prop(item, "runId"));
                if (Has(item, "attempt")) tried = PsValue.Str(PsValue.Prop(item, "attempt"));
                if (Has(item, "suite")) suiteName = PsValue.Str(PsValue.Prop(item, "suite"));
                if (Has(item, "hostWallSeconds") && PsValue.Prop(item, "hostWallSeconds") != null) wall = PsValue.Double(PsValue.Prop(item, "hostWallSeconds"));
                if (Has(item, "trxMissing")) missing = PsValue.IsTrue(PsValue.Prop(item, "trxMissing"));
                if (Has(item, "passedCount") && PsValue.Prop(item, "passedCount") != null) passed = PsOps.ToInt(PsValue.Prop(item, "passedCount"));
                if (Has(item, "trx")) trxPath = PsValue.Str(PsValue.Prop(item, "trx"));
                if (Has(item, "trxDigest")) digest = PsValue.Str(PsValue.Prop(item, "trxDigest"));
                if (Has(item, "exitCode")) exit = PsValue.Prop(item, "exitCode");
                if (Has(item, "coverageComplete")) coverage = PsValue.Prop(item, "coverageComplete");
                if (Has(item, "testsPassed")) tests = PsValue.Prop(item, "testsPassed");
                prior = new List<object>();
                if (Has(item, "outcomes") && PsValue.Prop(item, "outcomes") != null)
                    prior.AddRange(TestPhaseSummary.OutcomeRows(PsValue.AsArray(PsValue.Prop(item, "outcomes")), "testId", "outcome", true));
            }
            return new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
            {
                { "chunk", chunkId },
                { "runId", run },
                { "attempt", tried },
                { "suite", suiteName },
                { "hostWallSeconds", wall },
                { "trxMissing", missing },
                { "manufacturedPassed", false },
                { "passedCount", passed },
                { "outcomes", prior.ToArray() },
                { "trx", trxPath },
                { "trxDigest", digest },
                { "exitCode", exit },
                { "coverageComplete", coverage },
                { "testsPassed", tests },
            };
        }

        // $o.PSObject.Properties.Name -contains 'name' (case-insensitive).
        private static bool Has(object o, string name)
        {
            if (o == null) return false;
            PsValue.Property(o, name, out var found);
            return found;
        }

        // ---- seed rows, header and cache ----

        /// <summary>
        /// Import-TestPhaseSeedRows: the settled rows of a legacy manifest as ConvertFrom-Json read it (its chunks, or
        /// the document itself when it is one chunk), and its runId, attempt (and whether it had one) and suite.
        /// </summary>
        public static TestPhaseSeedImport ImportSeedRows(object parsed)
        {
            var import = new TestPhaseSeedImport();
            object[] prior = new object[0];
            if (parsed != null && Has(parsed, "chunks") && PsValue.Prop(parsed, "chunks") != null) prior = PsValue.AsArray(PsValue.Prop(parsed, "chunks"));
            else if (parsed != null && Has(parsed, "chunk")) prior = new[] { parsed };
            foreach (var item in prior)
            {
                if (item == null) continue;
                var settled = SettledChunk(item);
                import.Rows.Add(new TestPhaseSeedRow
                {
                    SettledJson = TestPhaseJson.Text(settled, 0),
                    SettledSuite = PsValue.Str(settled["suite"]),
                    SettledChunk = PsValue.Str(settled["chunk"]),
                });
            }
            if (parsed != null && Has(parsed, "runId")) import.Run = PsValue.Str(PsValue.Prop(parsed, "runId"));
            if (parsed != null && Has(parsed, "attempt"))
            {
                import.Attempt = PsValue.Str(PsValue.Prop(parsed, "attempt"));
                import.AttemptSeen = true;
            }
            if (parsed != null && Has(parsed, "suite")) import.Suite = PsValue.Str(PsValue.Prop(parsed, "suite"));
            return import;
        }

        /// <summary>Set-TestPhaseSeedHeader: the context's header and seed rows from an import.</summary>
        public static void SetSeedHeader(TestPhaseJournalContext context, TestPhaseSeedImport cache)
        {
            context.HeaderReady = true;
            context.HeaderRun = cache.Run ?? "";
            context.HeaderAttempt = cache.Attempt ?? "";
            context.HeaderSuite = cache.Suite ?? "";
            context.HeaderAttemptSeen = cache.AttemptSeen;
            context.SeedRows = cache.Rows;
            context.HasSeed = true;
        }

        /// <summary>
        /// Write-TestPhaseSeedCache: generation/seed-cache.txt, a header line (H, base64 run, attempt and suite, the
        /// attempt flag) and one line per seed row (base64 suite, base64 chunk, settled document), tab-separated.
        /// </summary>
        public static void WriteSeedCache(TestPhaseJournalContext context)
        {
            if (!context.HasSeed) return;
            var genDir = GenerationDirectory(context, context.Generation);
            WriteDirectory(genDir);
            var lines = new List<string>();
            var flag = context.HeaderAttemptSeen ? "1" : "0";
            lines.Add("H\t" + TestPhaseText.ToB64(context.HeaderRun ?? "") + "\t" + TestPhaseText.ToB64(context.HeaderAttempt ?? "") + "\t"
                + TestPhaseText.ToB64(context.HeaderSuite ?? "") + "\t" + flag);
            foreach (var row in context.SeedRows ?? new List<object>())
            {
                lines.Add(TestPhaseText.ToB64(TestPhaseJournalRead.Field(row, "SettledSuite")) + "\t"
                    + TestPhaseText.ToB64(TestPhaseJournalRead.Field(row, "SettledChunk")) + "\t" + TestPhaseJournalRead.Field(row, "SettledJson"));
            }
            WriteEvidenceLines(Path.Combine(genDir, "seed-cache.txt"), lines.ToArray());
        }

        /// <summary>
        /// Read-TestPhaseSeedCache: the generation's seed cache into the context's header and seed rows; false (the
        /// context untouched) when the cache is missing or not in that form.
        /// </summary>
        public static bool ReadSeedCache(TestPhaseJournalContext context, string generation)
        {
            var path = Path.Combine(GenerationDirectory(context, generation), "seed-cache.txt");
            if (!EvidencePathExists(path)) return false;
            var lines = ReadEvidenceLines(path);
            if (lines.Length < 1) return false;
            var header = lines[0] ?? "";
            // $header.StartsWith('H' + "`t") is a current-culture comparison in PowerShell.
            if (!header.StartsWith("H\t", StringComparison.CurrentCulture)) return false;
            var parts = header.Split('\t');
            if (parts.Length < 5) return false;
            var rows = new List<object>();
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i] ?? "";
                if (string.IsNullOrEmpty(line)) continue;
                var first = line.IndexOf('\t');
                if (first < 0) return false;
                var second = line.IndexOf('\t', first + 1);
                if (second < 0) return false;
                rows.Add(new TestPhaseSeedRow
                {
                    SettledJson = line.Substring(second + 1),
                    SettledSuite = TestPhaseText.FromB64(line.Substring(0, first)),
                    SettledChunk = TestPhaseText.FromB64(line.Substring(first + 1, second - first - 1)),
                });
            }
            context.HeaderReady = true;
            context.HeaderRun = TestPhaseText.FromB64(parts[1]);
            context.HeaderAttempt = TestPhaseText.FromB64(parts[2]);
            context.HeaderSuite = TestPhaseText.FromB64(parts[3]);
            context.HeaderAttemptSeen = PsValue.Eq(parts[4], "1", true);
            context.SeedRows = rows;
            context.HasSeed = true;
            context.SeedPath = Path.Combine(GenerationDirectory(context, generation), "seed.json");
            return true;
        }

        // ---- envelopes and the generation ----

        /// <summary>
        /// New-TestPhaseEnvelopeText: the journal envelope of the context's generation and root with its own SHA-256 in
        /// "digest" (TestPhaseRecords.Envelope). The context is the journal context, or any object with Generation,
        /// TimingRoot and WriterEnvironment.
        /// </summary>
        public static string EnvelopeText(string kind, int sequence, string publicationId, object context, string reason, string suite,
            string chunk, string settledSuite, string settledChunk, string headerRun, string headerAttempt, string headerSuite,
            string freshJson, string settledJson)
        {
            string generation, timingRoot, writer;
            if (context is TestPhaseJournalContext journal)
            {
                generation = journal.Generation;
                timingRoot = journal.TimingRoot;
                writer = journal.WriterEnvironment;
            }
            else
            {
                generation = PsValue.Str(PsValue.Prop(context, "Generation"));
                timingRoot = PsValue.Str(PsValue.Prop(context, "TimingRoot"));
                writer = PsValue.Str(PsValue.Prop(context, "WriterEnvironment"));
            }
            return TestPhaseRecords.Envelope(kind ?? "", sequence, publicationId ?? "", generation ?? "", timingRoot ?? "", reason ?? "",
                suite ?? "", chunk ?? "", settledSuite ?? "", settledChunk ?? "", headerRun ?? "", headerAttempt ?? "", headerSuite ?? "",
                freshJson ?? "", settledJson ?? "", writer ?? "");
        }

        /// <summary>
        /// Read-TestPhaseGenerationPrefix: every record re-read from disk (queued writes included) with its digest
        /// re-checked (TestPhaseJournalRead.ReadPrefix), up to the first missing record or the first break
        /// (unreadable-record, bad-digest, malformed-record, sequence-mismatch).
        /// </summary>
        public static TestPhaseGenerationPrefix ReadGenerationPrefix(string generationDir)
        {
            var loaded = new List<object>();
            var step = TestPhaseJournalRead.ReadPrefix(generationDir, 1, EvidenceFiles.Buffered, loaded);
            var last = 0;
            if (loaded.Count > 0) last = ((TestPhaseJournalRecord)loaded[loaded.Count - 1]).Sequence;
            return new TestPhaseGenerationPrefix
            {
                Records = loaded,
                NextSequence = step.Expected,
                Corrupt = step.Corrupt,
                Reason = step.Reason ?? "",
                LastSequence = last,
            };
        }

        /// <summary>
        /// Write-TestPhaseGenerationMetadata: generation/metadata.json, then current.json pointing at this generation
        /// with the metadata's digest.
        /// </summary>
        public static void WriteGenerationMetadata(TestPhaseJournalContext context, string parentGeneration, string seedDigest)
        {
            var genDir = GenerationDirectory(context, context.Generation);
            WriteDirectory(genDir);
            var meta = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
            {
                { "version", 1 },
                { "generation", context.Generation ?? "" },
                { "runId", context.RunId ?? "" },
                { "attempt", context.Attempt ?? "" },
                { "suite", context.Suite ?? "" },
                { "source", context.Source ?? "" },
                { "invocationId", context.InvocationId ?? "" },
                { "writerEnvironment", context.WriterEnvironment ?? "" },
                { "seedDigest", seedDigest ?? "" },
                { "parentGeneration", parentGeneration ?? "" },
                { "unresolved", context.Unresolved },
                { "corruptionReason", context.CorruptionReason ?? "" },
            };
            var path = Path.Combine(genDir, "metadata.json");
            TestPhaseFiles.WriteTextAtomic(path, TestPhaseJson.Text(meta, 0));
            var digest = TestPhaseText.Sha256File(path);
            var pointer = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
            {
                { "version", 1 },
                { "timingRoot", context.TimingRoot ?? "" },
                { "generation", context.Generation ?? "" },
                { "metadataDigest", digest ?? "" },
            };
            TestPhaseFiles.WriteTextAtomic(Path.Combine(JournalRoot(context.TimingRoot), "current.json"), TestPhaseJson.Text(pointer, 0));
        }

        /// <summary>Initialize-TestPhaseEmptyGeneration: a new generation with no seed and no records.</summary>
        public static void InitializeEmptyGeneration(TestPhaseJournalContext context, string parentGeneration)
        {
            context.Generation = Guid.NewGuid().ToString("n");
            context.HasSeed = false;
            context.SeedPath = "";
            context.SeedRows = new List<object>();
            context.Records = new List<object>();
            context.LastCommitted = 0;
            context.NextSequence = 1;
            context.HeaderReady = false;
            WriteGenerationMetadata(context, parentGeneration, "");
        }

        /// <summary>
        /// Initialize-TestPhaseSeedGeneration: a new generation seeded with the legacy flat manifest's bytes (seed.json,
        /// its settled rows in the seed cache, its digest in the metadata). A malformed legacy manifest seeds nothing:
        /// its bytes are kept under predecessors and the generation is unresolved (malformed-legacy).
        /// </summary>
        public static void InitializeSeedGeneration(TestPhaseJournalContext context, string legacyPath, string parentGeneration, bool malformed)
        {
            context.Generation = Guid.NewGuid().ToString("n");
            context.Records = new List<object>();
            context.LastCommitted = 0;
            context.NextSequence = 1;
            var genDir = GenerationDirectory(context, context.Generation);
            WriteDirectory(genDir);
            if (malformed)
            {
                context.Unresolved = true;
                if (string.IsNullOrWhiteSpace(context.CorruptionReason)) context.CorruptionReason = "malformed-legacy";
                SaveMalformedLegacy(context.TimingRoot, context.Generation);
                context.HasSeed = false;
                context.HeaderReady = false;
                WriteGenerationMetadata(context, parentGeneration, "");
                return;
            }
            var seedPath = Path.Combine(genDir, "seed.json");
            CopyBytes(legacyPath, seedPath);
            context.SeedPath = seedPath;
            context.HasSeed = true;
            var parsed = ReadJsonFile(legacyPath);
            var cache = ImportSeedRows(parsed);
            SetSeedHeader(context, cache);
            WriteSeedCache(context);
            var digest = TestPhaseText.Sha256File(seedPath);
            WriteGenerationMetadata(context, parentGeneration, digest);
        }

        // ---- the projection ----

        /// <summary>
        /// Get-TestPhaseProjectedDocument: the flat manifest the journal projects (the reduced chunks, the last one in
        /// its fresh form, under the last chunk's header); the seed's own text when no chunk was committed; null when
        /// there is neither.
        /// </summary>
        public static string ProjectedDocument(TestPhaseJournalContext context)
        {
            var reduced = TestPhaseJournalRead.Reduce(context);
            if (reduced.ChunkCount == 0)
            {
                if (context.HasSeed && !string.IsNullOrWhiteSpace(context.SeedPath) && EvidencePathExists(context.SeedPath))
                    return ReadEvidenceText(context.SeedPath);
                return null;
            }
            var parts = new string[reduced.Entries.Count];
            for (var i = 0; i < reduced.Entries.Count; i++)
                parts[i] = i == reduced.Entries.Count - 1 ? reduced.Entries[i].Fresh ?? "" : reduced.Entries[i].Json ?? "";
            return "{\"version\":1,\"runId\":" + TestPhaseJson.String(reduced.HeaderRun ?? "") + ",\"attempt\":" + TestPhaseJson.String(reduced.HeaderAttempt ?? "")
                + ",\"suite\":" + TestPhaseJson.String(reduced.HeaderSuite ?? "") + ",\"manufacturedPassed\":false,\"chunks\":[" + string.Join(",", parts) + "]}";
        }

        /// <summary>Import-TestPhaseGapMemory: a committed gap record leaves publication-gap on the context.</summary>
        public static void ImportGapMemory(TestPhaseJournalContext context, IEnumerable records)
        {
            if (records == null) return;
            foreach (var rec in records)
            {
                if (rec == null) continue;
                if (PsValue.Eq(TestPhaseJournalRead.Field(rec, "Kind"), "gap", true) && !context.Gaps.Contains("publication-gap"))
                    context.Gaps.Add("publication-gap");
            }
        }

        /// <summary>
        /// Update-TestPhaseContextHeader: the context's header as the projection reads it back (each value written
        /// as a JSON string and read again as ConvertFrom-Json reads it, so an ISO date-time settles to its date text).
        /// </summary>
        public static void UpdateContextHeader(TestPhaseJournalContext context, string run, string attempt, string suite)
        {
            context.HeaderReady = true;
            context.HeaderRun = SettleString(run);
            context.HeaderAttempt = SettleString(attempt);
            context.HeaderSuite = SettleString(suite);
            context.HeaderAttemptSeen = true;
        }

        // [string]((@{ v = $text } | JSON) | ConvertFrom-Json).v. A string ConvertFrom-Json would read as a
        // Microsoft "/Date(ms)/" date stays as written.
        private static string SettleString(string text)
        {
            var mini = new OrderedDictionary(StringComparer.OrdinalIgnoreCase) { { "v", text ?? "" } };
            var parsed = NightlyJson.ParseText(TestPhaseJson.Text(mini, 0), 1024, true);
            return parsed == null ? text ?? "" : PsValue.Str(PsValue.Prop(parsed, "v"));
        }

        /// <summary>
        /// Import-TestPhaseExistingGeneration: binds the context to the generation current.json names. ProjectOnly reads
        /// it as it is. A writer resumes it when its prefix is intact and its metadata names the same run, attempt,
        /// source and writer environment, and it is not sealed (or Recover resumes an intact sealed one). Otherwise a
        /// successor generation is seeded with the old generation's projection (an empty one when there is none), and
        /// the old generation is left as it was.
        /// </summary>
        public static void ImportExistingGeneration(TestPhaseJournalContext context, string generation, bool projectOnly, bool recover)
        {
            var journal = JournalRoot(context.TimingRoot);
            var genDir = Path.Combine(journal, generation ?? "");
            var meta = ReadJsonFile(Path.Combine(genDir, "metadata.json"));
            if (meta == null)
            {
                context.Unresolved = true;
                context.CorruptionReason = "metadata-unreadable";
                InitializeEmptyGeneration(context, generation);
                return;
            }
            var prefix = ReadGenerationPrefix(genDir);
            string M(string name) => PsValue.Str(PsValue.Prop(meta, name));
            var same = TestPhaseText.OrdinalEqual(M("runId"), context.RunId) && TestPhaseText.OrdinalEqual(M("attempt"), context.Attempt)
                && TestPhaseText.OrdinalEqual(M("source"), context.Source) && TestPhaseText.OrdinalEqual(M("writerEnvironment"), context.WriterEnvironment);
            var sealedGeneration = EvidencePathExists(Path.Combine(genDir, "seal.json"));
            var resumeSealed = recover && sealedGeneration && !prefix.Corrupt && same;
            var metaUnresolved = PsValue.IsTrue(PsValue.Prop(meta, "unresolved"));
            if (projectOnly)
            {
                context.Generation = generation ?? "";
                context.Records = prefix.Records;
                context.LastCommitted = prefix.LastSequence;
                context.NextSequence = prefix.NextSequence;
                context.Unresolved = metaUnresolved || prefix.Corrupt;
                context.CorruptionReason = M("corruptionReason");
                if (prefix.Corrupt && string.IsNullOrWhiteSpace(context.CorruptionReason)) context.CorruptionReason = prefix.Reason;
                ReadSeedCache(context, generation);
                ImportGapMemory(context, prefix.Records);
                return;
            }
            if ((prefix.Corrupt || !same || sealedGeneration) && !resumeSealed)
            {
                var snapshot = RefusedContext("");
                snapshot.Refused = false;
                snapshot.TimingRoot = context.TimingRoot;
                snapshot.Generation = generation ?? "";
                snapshot.Records = prefix.Records;
                snapshot.WriterEnvironment = context.WriterEnvironment;
                ReadSeedCache(snapshot, generation);
                var parentText = ProjectedDocument(snapshot);
                var reduced = TestPhaseJournalRead.Reduce(snapshot);
                context.Unresolved = metaUnresolved || prefix.Corrupt;
                if (prefix.Corrupt) context.CorruptionReason = prefix.Reason;
                else if (metaUnresolved) context.CorruptionReason = M("corruptionReason");
                else context.CorruptionReason = "";
                if (string.IsNullOrWhiteSpace(parentText))
                {
                    InitializeEmptyGeneration(context, generation);
                    return;
                }
                context.Generation = Guid.NewGuid().ToString("n");
                var genNew = Path.Combine(journal, context.Generation);
                WriteDirectory(genNew);
                var newSeed = Path.Combine(genNew, "seed.json");
                WriteEvidenceText(newSeed, parentText);
                context.SeedPath = newSeed;
                context.HasSeed = true;
                context.Records = new List<object>();
                context.LastCommitted = 0;
                context.NextSequence = 1;
                var rows = new List<object>();
                for (var i = 0; i < reduced.Entries.Count; i++)
                {
                    var entry = reduced.Entries[i];
                    rows.Add(new TestPhaseSeedRow
                    {
                        SettledJson = i == reduced.Entries.Count - 1 ? entry.Fresh ?? "" : entry.Json ?? "",
                        SettledSuite = entry.Suite ?? "",
                        SettledChunk = entry.Chunk ?? "",
                    });
                }
                context.SeedRows = rows;
                context.HeaderReady = true;
                context.HeaderRun = reduced.HeaderRun ?? "";
                context.HeaderAttempt = reduced.HeaderAttempt ?? "";
                context.HeaderSuite = reduced.HeaderSuite ?? "";
                context.HeaderAttemptSeen = true;
                WriteSeedCache(context);
                UpdateContextHeader(context, context.HeaderRun, context.HeaderAttempt, context.HeaderSuite);
                WriteGenerationMetadata(context, generation, TestPhaseText.Sha256File(newSeed));
                return;
            }
            context.Generation = generation ?? "";
            context.Records = prefix.Records;
            context.LastCommitted = prefix.LastSequence;
            context.NextSequence = prefix.NextSequence;
            context.Unresolved = metaUnresolved;
            context.CorruptionReason = M("corruptionReason");
            ReadSeedCache(context, generation);
            ImportGapMemory(context, prefix.Records);
            if (prefix.Records.Count > 0)
            {
                object last = null;
                foreach (var rec in prefix.Records)
                    if (PsValue.Eq(TestPhaseJournalRead.Field(rec, "Kind"), "chunk", true)) last = rec;
                if (last != null)
                {
                    UpdateContextHeader(context, TestPhaseJournalRead.Field(last, "HeaderRun"), TestPhaseJournalRead.Field(last, "HeaderAttempt"),
                        TestPhaseJournalRead.Field(last, "HeaderSuite"));
                }
            }
        }

        // ---- publication quality (a reader: the summary and the deferred-write witnesses) ----

        /// <summary>
        /// Add-TestPhaseDeferredEvidenceToken: deferred-evidence-write-failed when a deferred evidence write failed
        /// (retained or still queued).
        /// </summary>
        public static void AddDeferredEvidenceToken(List<string> tokens)
        {
            if (tokens == null) return;
            try
            {
                var any = false;
                foreach (var item in EvidenceFiles.PeekFailures())
                    if (!string.IsNullOrWhiteSpace(item)) { any = true; break; }
                if (any && !tokens.Contains("deferred-evidence-write-failed")) tokens.Add("deferred-evidence-write-failed");
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Get-TestPhasePublicationQuality: the publication tokens of a run's timing directory (timingRoot: the
        /// process's qualification timing root, else RunDirectory/timing). Nothing when neither the flat manifest nor
        /// the journal exists; publication-state-missing without the state file; publication-state-mismatch when the
        /// state does not match the journal pointer, the prefix's last sequence, the root or the flat manifest's digest;
        /// publication-corruption for a broken prefix; then the state's own quality tokens, and the deferred-write
        /// token. knownPrefix: the prefix the caller already read for the current generation.
        /// </summary>
        public static string[] PublicationQuality(string runDirectory, string qualificationTimingRoot, TestPhaseGenerationPrefix knownPrefix)
        {
            var tokens = new List<string>();
            try
            {
                var timing = !string.IsNullOrWhiteSpace(qualificationTimingRoot) ? qualificationTimingRoot : Path.Combine(runDirectory ?? "", "timing");
                var flat = Path.Combine(timing, "groups-executed.json");
                var currentPath = Path.Combine(JournalRoot(timing), "current.json");
                var hasFlat = TestPhaseFiles.Kind(flat, out _) == "regular";
                var hasJournal = TestPhaseFiles.Kind(currentPath, out _) == "regular";
                if (!hasFlat && !hasJournal)
                {
                    AddDeferredEvidenceToken(tokens);
                    return tokens.ToArray();
                }
                var statePath = Path.Combine(timing, "groups-executed.state.json");
                if (TestPhaseFiles.Kind(statePath, out _) != "regular")
                {
                    tokens.Add("publication-state-missing");
                    AddDeferredEvidenceToken(tokens);
                    return tokens.ToArray();
                }
                var state = ReadJsonFile(statePath);
                if (state == null)
                {
                    tokens.Add("publication-state-mismatch");
                    AddDeferredEvidenceToken(tokens);
                    return tokens.ToArray();
                }
                var mismatch = false;
                if (hasJournal)
                {
                    var current = ReadJsonFile(currentPath);
                    if (current == null || !TestPhaseText.OrdinalEqual(PsValue.Str(PsValue.Prop(current, "generation")), PsValue.Str(PsValue.Prop(state, "generation"))))
                        mismatch = true;
                    else
                    {
                        var prefix = knownPrefix ?? ReadGenerationPrefix(Path.Combine(JournalRoot(timing), PsValue.Str(PsValue.Prop(current, "generation"))));
                        if (PsOps.ToInt(PsValue.Prop(state, "sequence")) != prefix.LastSequence) mismatch = true;
                        if (prefix.Corrupt && !tokens.Contains("publication-corruption")) tokens.Add("publication-corruption");
                    }
                }
                string rootFull;
                try { rootFull = Path.GetFullPath(timing); } catch (Exception) { rootFull = timing; }
                if (!TestPhaseText.OrdinalEqual(PsValue.Str(PsValue.Prop(state, "root")), rootFull)) mismatch = true;
                if (hasFlat && !TestPhaseText.OrdinalEqual(PsValue.Str(PsValue.Prop(state, "projectionDigest")), TestPhaseText.Sha256File(flat))) mismatch = true;
                if (mismatch) tokens.Add("publication-state-mismatch");
                if (Has(state, "quality") && PsValue.Prop(state, "quality") != null)
                {
                    foreach (var token in PsValue.AsArray(PsValue.Prop(state, "quality")))
                    {
                        var text = PsValue.Str(token);
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        if (!tokens.Contains(text)) tokens.Add(text);
                    }
                }
            }
            catch (Exception)
            {
                if (!tokens.Contains("publication-state-mismatch")) tokens.Add("publication-state-mismatch");
            }
            AddDeferredEvidenceToken(tokens);
            return tokens.ToArray();
        }
    }
}
