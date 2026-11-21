using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Antiphon.Nightly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// The journal cases' publication, in-process (W2-nightly S07). The journal head is compiled
/// (Antiphon.Nightly/TestPhaseJournal.cs); the publication around it (Open-TestPhaseExecutedContext,
/// Write-TestPhaseExecutedManifest, Add-TestPhaseExecutedChunk, Complete-TestPhaseExecutedProjection) is still PowerShell
/// until S08. These are line-for-line transcriptions of those functions on the compiled API, so the cases drive the
/// compiled head exactly as the PowerShell publication does. S08 replaces this class with the production C#.
/// </summary>
internal static class JournalPublicationStandIn
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>The writer environment of an in-process writer (the edition and version of this host).</summary>
    internal static string Writer() => TestPhaseJournal.WriterEnvironment("Core", "in-process");

    /// <summary>What the cases count during a publication (the harness's function wrappers counted the same).</summary>
    internal sealed class Counters
    {
        public bool Counting;
        public string PublishedChunk = "";
        public int PriorRows;
        public int FlatWrites;
        public int HashCount;
    }

    /// <summary>Open-TestPhaseExecutedContext (LogRoot/timing).</summary>
    internal static TestPhaseJournalContext Open(string logRoot, string runId, string attempt, string suite, string source,
        bool projectOnly = false, bool recover = false)
    {
        if (string.IsNullOrWhiteSpace(source)) source = "unknown";
        var timing = TestPhaseJournal.ExecutedTimingRoot(logRoot, "", "");
        if (string.IsNullOrWhiteSpace(timing)) return TestPhaseJournal.RefusedContext("timing-root-missing");
        if (TestPhaseJournal.UnsafeDirectory(Path.Combine(logRoot, "timing"))) return TestPhaseJournal.RefusedContext("unsafe-root");
        if (File.Exists(timing)) return TestPhaseJournal.RefusedContext("timing-root-is-file");
        TestPhaseJournal.WriteDirectory(timing);
        var journal = TestPhaseJournal.JournalRoot(timing);
        TestPhaseJournal.WriteDirectory(journal);
        FileStream writerLock;
        try { writerLock = File.Open(Path.Combine(journal, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return TestPhaseJournal.RefusedContext("writer-lock-held"); }
        var ctx = TestPhaseJournal.OpenContext(timing, logRoot, runId, attempt, suite, source, "", writerLock, Writer());
        ctx.ProjectOnly = projectOnly;
        var current = TestPhaseJournal.ReadJsonFile(Path.Combine(journal, "current.json"));
        if (current is null)
        {
            var flat = Path.Combine(timing, "groups-executed.json");
            if (TestPhaseFiles.Kind(flat, out _) == "regular")
            {
                var parsed = TestPhaseJournal.ReadJsonFile(flat);
                var valid = parsed is not null && Has(parsed, "version") && Convert.ToInt64(PsValue.Prop(parsed, "version"), CultureInfo.InvariantCulture) == 1
                    && (Has(parsed, "chunks") || Has(parsed, "chunk"));
                TestPhaseJournal.InitializeSeedGeneration(ctx, flat, "", !valid);
            }
            else TestPhaseJournal.InitializeEmptyGeneration(ctx, "");
        }
        else
        {
            var generation = PsValue.Str(PsValue.Prop(current, "generation"));
            var bound = TestPhaseText.OrdinalEqual(PsValue.Str(PsValue.Prop(current, "timingRoot")), timing) && !string.IsNullOrWhiteSpace(generation);
            if (!bound)
            {
                ctx.Unresolved = true;
                ctx.CorruptionReason = "pointer-binding";
                TestPhaseJournal.InitializeEmptyGeneration(ctx, generation);
            }
            else TestPhaseJournal.ImportExistingGeneration(ctx, generation, projectOnly, recover);
        }
        return ctx;
    }

    /// <summary>Close-TestPhaseExecutedContext (no deferred writes here).</summary>
    internal static void Close(TestPhaseJournalContext? ctx)
    {
        if (ctx is null) return;
        ctx.Lock?.Dispose();
        ctx.Lock = null;
        ctx.Closed = true;
    }

    /// <summary>
    /// Publish-NightlyChunkObservation / Write-TestPhaseExecutedManifest into an open context: the chunk from its TRX,
    /// settled and committed as the next journal record.
    /// </summary>
    internal static bool Publish(TestPhaseJournalContext ctx, string logRoot, string trxPath, string suite, string chunk, string runId,
        string attempt, double hostWallSeconds, object? exitCode, object? coverageComplete, object? testsPassed, Counters? counters = null)
    {
        var fresh = FreshFromTrx(logRoot, trxPath, suite, chunk, runId, attempt, hostWallSeconds, exitCode, coverageComplete, testsPassed, counters);
        return AddExecutedChunk(ctx, fresh, suite, chunk, runId, attempt, counters);
    }

    /// <summary>Write-TestPhaseExecutedManifest's fresh chunk: the TRX outcome rows, their passed count and digest.</summary>
    internal static OrderedDictionary FreshFromTrx(string logRoot, string trxPath, string suite, string chunk, string runId, string attempt,
        double hostWallSeconds, object? exitCode, object? coverageComplete, object? testsPassed, Counters? counters)
    {
        var rows = TrxRows(trxPath);
        var trxMissing = TestPhaseFiles.Kind(trxPath, out _) != "regular";
        var outcomeRows = TestPhaseSummary.OutcomeRows(rows, "TestId", "Outcome", false);
        var passed = TestPhaseSummary.PassedCount(outcomeRows);
        var digest = "";
        if (!trxMissing)
        {
            if (counters is { Counting: true }) counters.HashCount++;
            try { digest = NightlyClassNames.FileSha256(trxPath); } catch (IOException) { digest = ""; }
        }
        return TestPhaseJournal.FreshChunk(chunk, runId, attempt, suite, hostWallSeconds, trxMissing, passed, outcomeRows,
            TestPhaseText.RelativePath(logRoot, trxPath), digest, exitCode, coverageComplete, testsPassed);
    }

    /// <summary>Read-TestPhaseTrxRows: the complete TRX's timing rows, or none.</summary>
    internal static object[] TrxRows(string trxPath)
    {
        if (TestPhaseFiles.Kind(trxPath, out _) != "regular") return [];
        var opened = TestPhaseFiles.OpenReadStream(trxPath);
        if (opened.Refused || opened.Stream is null) return [];
        try
        {
            var read = Antiphon.TestPhase.TrxTimingScanner.Read(opened.Stream, Stopwatch.StartNew(), 200, 262144, 16384);
            if (read is null || !read.Complete) return [];
            return NightlyTrx.TimingRows(read, trxPath);
        }
        catch (Exception) { return []; }
        finally { opened.Stream.Dispose(); }
    }

    /// <summary>Add-TestPhaseExecutedChunk without the outage path (no fault seam is set in these cases).</summary>
    internal static bool AddExecutedChunk(TestPhaseJournalContext ctx, OrderedDictionary fresh, string suite, string chunk, string runId,
        string attempt, Counters? counters)
    {
        if (ctx.Closed || ctx.Refused) throw new InvalidOperationException("context-closed");
        var current = TestPhaseJournal.ReadJsonFile(Path.Combine(TestPhaseJournal.JournalRoot(ctx.TimingRoot), "current.json"));
        if (current is null || !TestPhaseText.OrdinalEqual(PsValue.Str(PsValue.Prop(current, "generation")), ctx.Generation))
            throw new InvalidOperationException("stale-context");
        // Get-TestPhaseNextHeader
        var headerRun = runId;
        var headerAttempt = attempt;
        var headerSuite = suite;
        if (ctx.HeaderReady)
        {
            if (!string.IsNullOrWhiteSpace(ctx.HeaderRun)) headerRun = ctx.HeaderRun;
            if (ctx.HeaderAttemptSeen) headerAttempt = ctx.HeaderAttempt;
            if (!string.IsNullOrWhiteSpace(ctx.HeaderSuite)) headerSuite = ctx.HeaderSuite;
        }
        var freshJson = TestPhaseJson.Text(fresh, 0);
        var parsed = NightlyJson.ParseText(freshJson, 1024, true) ?? throw new InvalidOperationException("fresh-not-json");
        Settling(counters, parsed);
        var settled = TestPhaseJournal.SettledChunk(parsed);
        var settledJson = TestPhaseJson.Text(settled, 0);
        var publicationId = Guid.NewGuid().ToString("n");
        var sequence = ctx.NextSequence;
        var envelope = TestPhaseJournal.EnvelopeText("chunk", sequence, publicationId, ctx, "", suite, chunk, PsValue.Str(settled["suite"]),
            PsValue.Str(settled["chunk"]), headerRun, headerAttempt, headerSuite, freshJson, settledJson);
        CommitFile(Path.Combine(TestPhaseJournal.JournalRoot(ctx.TimingRoot), ctx.Generation, sequence.ToString("00000000", CultureInfo.InvariantCulture) + ".json"), envelope);
        ctx.Records.Add(new TestPhaseJournalRecord
        {
            Kind = "chunk", Sequence = sequence, FreshJson = freshJson, SettledJson = settledJson, FreshSuite = suite, FreshChunk = chunk,
            SettledSuite = PsValue.Str(settled["suite"]), SettledChunk = PsValue.Str(settled["chunk"]), HeaderRun = headerRun,
            HeaderAttempt = headerAttempt, HeaderSuite = headerSuite, PublicationId = publicationId,
        });
        ctx.LastCommitted = sequence;
        ctx.NextSequence = sequence + 1;
        ctx.NeedsRecovery = false;
        ctx.RescanAttempted = false;
        TestPhaseJournal.UpdateContextHeader(ctx, headerRun, headerAttempt, headerSuite);
        return true;
    }

    // The harness counted settlements of an item whose chunk is not the one published (prior rows).
    private static void Settling(Counters? counters, object item)
    {
        if (counters is not { Counting: true }) return;
        if (PsValue.Str(PsValue.Prop(item, "chunk")) == counters.PublishedChunk) return;
        counters.PriorRows += PsValue.AsArray(PsValue.Prop(item, "outcomes")).Count(o => o is not null);
    }

    // Write-TestPhaseCommitFile without the seams: a temporary file renamed to the record, then read back.
    private static void CommitFile(string finalPath, string text)
    {
        var dir = Path.GetDirectoryName(finalPath)!;
        TestPhaseJournal.WriteDirectory(dir);
        var tmp = Path.Combine(dir, "." + Guid.NewGuid().ToString("n") + ".tmp");
        using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var bytes = Utf8NoBom.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }
        if (File.Exists(finalPath)) throw new IOException("commit-exists");
        File.Move(tmp, finalPath);
        if (File.ReadAllText(finalPath) != text) throw new IOException("commit-unreadable");
    }

    /// <summary>Complete-TestPhaseExecutedProjection: the flat manifest, the publication state and (terminal) the seal.</summary>
    internal static void Complete(TestPhaseJournalContext ctx, string terminal = "", Counters? counters = null)
    {
        var text = TestPhaseJournal.ProjectedDocument(ctx);
        var terminalSet = !string.IsNullOrWhiteSpace(terminal);
        if (text is null && !ctx.Unresolved && ctx.Gaps.Count == 0 && !terminalSet) return;
        if (text is not null)
        {
            if (counters is { Counting: true }) counters.FlatWrites++;
            TestPhaseFiles.WriteTextAtomic(Path.Combine(ctx.TimingRoot, "groups-executed.json"), text);
        }
        var statePath = Path.Combine(ctx.TimingRoot, "groups-executed.state.json");
        TestPhaseFiles.WriteTextAtomic(statePath, StateText(ctx, text));
        if (!terminalSet) return;
        var seal = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
        {
            { "version", 1 }, { "generation", ctx.Generation }, { "sequence", ctx.LastCommitted }, { "terminal", terminal },
            { "projectionDigest", text is null ? "" : TestPhaseRecords.Sha256Text(text) }, { "stateDigest", TestPhaseText.Sha256File(statePath) },
        };
        TestPhaseFiles.WriteTextAtomic(Path.Combine(TestPhaseJournal.JournalRoot(ctx.TimingRoot), ctx.Generation, "seal.json"), TestPhaseJson.Text(seal, 0));
    }

    /// <summary>Complete-TestPhaseExecutedProjection -LogRoot: a project-only context, projected and closed.</summary>
    internal static void ProjectOnly(string logRoot)
    {
        var ctx = Open(logRoot, "", "", "", "unknown", projectOnly: true);
        if (ctx.Refused) throw new InvalidOperationException("project-only-refused " + ctx.Reason);
        try { Complete(ctx); }
        finally { Close(ctx); }
    }

    // Get-TestPhasePublicationStateText.
    private static string StateText(TestPhaseJournalContext ctx, string? projection)
    {
        var quality = new List<string>();
        if (ctx.Unresolved) quality.Add("publication-corruption");
        if (ctx.Gaps.Count > 0 || ctx.PendingGaps.Count > 0) quality.Add("publication-gap");
        var state = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
        {
            { "version", 1 }, { "generation", ctx.Generation }, { "sequence", ctx.LastCommitted }, { "root", ctx.TimingRoot },
            { "publicationComplete", quality.Count == 0 }, { "unresolved", ctx.Unresolved }, { "reason", ctx.CorruptionReason },
            { "projectionDigest", projection is null ? "" : TestPhaseRecords.Sha256Text(projection) }, { "quality", quality.ToArray() },
        };
        return TestPhaseJson.Text(state, 0);
    }

    /// <summary>
    /// Invoke-LegacyExecutedManifest: the pre-journal writer (the harness's pinned copy). Every publication re-reads the
    /// flat manifest, re-settles every other chunk and writes the whole document again.
    /// </summary>
    internal static void LegacyPublish(string logRoot, string trxPath, string suite, string chunk, string runId, string attempt,
        double hostWallSeconds, bool nullVerdict = false, int exitCode = 0, bool coverage = false, bool passed = false)
    {
        var fresh = FreshFromTrx(logRoot, trxPath, suite, chunk, runId, attempt, hostWallSeconds,
            nullVerdict ? null : exitCode, nullVerdict ? null : coverage, nullVerdict ? null : passed, null);
        var path = Path.Combine(logRoot, "timing", "groups-executed.json");
        var kept = new List<object>();
        var topRun = runId;
        var topAttempt = attempt;
        var topSuite = suite;
        if (TestPhaseFiles.Kind(path, out _) == "regular")
        {
            var existing = NightlyJson.ParseText(File.ReadAllText(path), 1024, true) ?? throw new InvalidOperationException("legacy-flat-not-json");
            if (Has(existing, "runId") && !string.IsNullOrWhiteSpace(PsValue.Str(PsValue.Prop(existing, "runId")))) topRun = PsValue.Str(PsValue.Prop(existing, "runId"));
            if (Has(existing, "attempt")) topAttempt = PsValue.Str(PsValue.Prop(existing, "attempt"));
            if (Has(existing, "suite") && !string.IsNullOrWhiteSpace(PsValue.Str(PsValue.Prop(existing, "suite")))) topSuite = PsValue.Str(PsValue.Prop(existing, "suite"));
            object[] prior = [];
            if (Has(existing, "chunks") && PsValue.Prop(existing, "chunks") is not null) prior = PsValue.AsArray(PsValue.Prop(existing, "chunks"));
            else if (Has(existing, "chunk")) prior = [existing];
            foreach (var item in prior)
            {
                if (item is null) continue;
                var same = PsValue.Eq(PsValue.Str(PsValue.Prop(item, "chunk")), chunk, true) && PsValue.Eq(PsValue.Str(PsValue.Prop(item, "suite")), suite, true);
                if (same) continue;
                kept.Add(TestPhaseJournal.SettledChunk(item));
            }
        }
        kept.Add(fresh);
        var manifest = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
        {
            { "version", 1 }, { "runId", topRun }, { "attempt", topAttempt }, { "suite", topSuite }, { "manufacturedPassed", false },
            { "chunks", kept.ToArray() },
        };
        TestPhaseFiles.WriteTextAtomic(path, TestPhaseJson.Text(manifest, 0));
    }

    private static bool Has(object o, string name)
    {
        PsValue.Property(o, name, out var found);
        return found;
    }

    // ---- fixtures (New-MpTrx, New-MpResultRow, Write-MpLegacyPrefix, Get-MpSha, Read-MpChunkIds) ----

    internal static void WriteTrx(string path, params string[] rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun><Results>" + string.Concat(rows) + "</Results></TestRun>", Utf8NoBom);
    }

    internal static string ResultRow(string id, string outcome) =>
        "<UnitTestResult testId=\"" + id + "\" testName=\"" + id + "\" outcome=\"" + outcome + "\" />";

    internal static void WriteLegacyPrefix(string path, int chunks, int outcomesPerChunk = 1, int wideOutcomes = 0, int wideChunks = 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sb = new StringBuilder("{\"version\":1,\"runId\":\"run-v1\",\"attempt\":\"1\",\"suite\":\"antiphon\",\"manufacturedPassed\":false,\"chunks\":[");
        var outcomeIndex = 0;
        for (var c = 0; c < chunks; c++)
        {
            if (c > 0) sb.Append(',');
            var count = wideChunks > 0 && c < wideChunks ? wideOutcomes : outcomesPerChunk;
            sb.Append("{\"chunk\":\"p").Append(c.ToString("0000", CultureInfo.InvariantCulture))
                .Append("\",\"runId\":\"run-v1\",\"attempt\":\"1\",\"suite\":\"antiphon\",\"hostWallSeconds\":1.25,\"trxMissing\":true,\"manufacturedPassed\":false,\"passedCount\":")
                .Append(count.ToString(CultureInfo.InvariantCulture)).Append(",\"outcomes\":[");
            for (var o = 0; o < count; o++)
            {
                if (o > 0) sb.Append(',');
                sb.Append("{\"testId\":\"t").Append(outcomeIndex.ToString(CultureInfo.InvariantCulture)).Append("\",\"outcome\":\"Passed\"}");
                outcomeIndex++;
            }
            sb.Append("],\"trx\":\"\",\"trxDigest\":\"\",\"exitCode\":0,\"coverageComplete\":false,\"testsPassed\":false}");
        }
        sb.Append("]}");
        File.WriteAllText(path, sb.ToString(), Utf8NoBom);
    }

    internal static string Sha(string path) => File.Exists(path) ? NightlyClassNames.FileSha256(path) : "";

    internal static List<string> ChunkIds(string path)
    {
        var ids = new List<string>();
        if (!File.Exists(path)) return ids;
        var doc = NightlyJson.ParseText(File.ReadAllText(path), 1024, true) ?? throw new InvalidOperationException("flat-not-json");
        object[] prior = [];
        if (Has(doc, "chunks") && PsValue.Prop(doc, "chunks") is not null) prior = PsValue.AsArray(PsValue.Prop(doc, "chunks"));
        else if (Has(doc, "chunk")) prior = [doc];
        foreach (var item in prior) if (item is not null) ids.Add(PsValue.Str(PsValue.Prop(item, "chunk")));
        return ids;
    }

    /// <summary>The value a record of the journal holds under name, as ConvertFrom-Json reads it.</summary>
    internal static object? EnvelopeField(string envelopeText, string name) =>
        PsValue.Prop(NightlyJson.ParseText(envelopeText, 1024, true) ?? new PlainObject(), name);

    internal static IEnumerable<string> RecordFiles(string timing)
    {
        var root = Path.Combine(timing, "groups-executed.journal");
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), "^[0-9]{8}\\.json$"));
    }
}
