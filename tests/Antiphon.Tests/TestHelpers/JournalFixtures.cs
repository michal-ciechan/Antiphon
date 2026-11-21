using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Nightly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// The executed-journal witnesses' fixtures (W2-nightly S08; they were ManifestPublicationHarness.ps1's helpers). The
/// publication itself is the production C# (Antiphon.Nightly/TestPhasePublication.cs); what is here builds inputs, reads
/// outputs, keeps the pinned pre-journal writer as the byte oracle, and starts the still-PowerShell production entries
/// (scripts/summarize-test-phases.ps1, scripts/nightly-tests.ps1).
/// </summary>
internal static class JournalFixtures
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    internal const string Edition = "Core";
    internal const string Version = "in-process";

    /// <summary>The writer environment of an in-process writer (the edition and version of this host).</summary>
    internal static string Writer() => TestPhaseJournal.WriterEnvironment(Edition, Version);

    /// <summary>An in-process publication's scope: no process timing root and no commit seams unless given.</summary>
    internal static TestPhasePublicationScope Scope(string qualificationTimingRoot = "", object? barrier = null, object? fault = null, string writer = "") =>
        new(qualificationTimingRoot, Edition, Version, barrier, fault) { WriterEnvironment = writer };

    /// <summary>A commit fault seam (Stage, Remaining) as PowerShell's [pscustomobject] was.</summary>
    internal static PlainObject Fault(string stage, int remaining) => new(("Stage", stage), ("Remaining", remaining));

    /// <summary>Open-TestPhaseExecutedContext -LogRoot (LogRoot/timing unless the scope names a timing root).</summary>
    internal static TestPhaseJournalContext Open(string logRoot, string runId, string attempt, string suite, string source,
        TestPhasePublicationScope? scope = null, string timingRoot = "") =>
        TestPhasePublication.Open(logRoot, timingRoot, runId, attempt, suite, source, "", false, false, scope ?? Scope());

    internal static void Close(TestPhaseJournalContext? ctx) => TestPhasePublication.Close(ctx);

    /// <summary>Complete-TestPhaseExecutedProjection -Context [-Terminal].</summary>
    internal static void Complete(TestPhaseJournalContext ctx, string terminal = "", TestPhasePublicationScope? scope = null) =>
        TestPhasePublication.CompleteProjection(ctx, terminal, scope ?? Scope());

    /// <summary>Complete-TestPhaseExecutedProjection -LogRoot: a project-only context, projected and closed.</summary>
    internal static void ProjectOnly(string logRoot)
    {
        var ctx = TestPhasePublication.CompleteProjection(logRoot, "", "", Scope());
        if (ctx is null || ctx.Refused) throw new InvalidOperationException("project-only-refused " + ctx?.Reason);
    }

    /// <summary>The TRX hashes a run of publications computed (Write-TestPhaseExecutedManifest hashes each TRX once).</summary>
    internal sealed class Hashes
    {
        public int Count;
    }

    /// <summary>
    /// Publish-NightlyChunkObservation (Publish-MpChunk): the chunk from its TRX as Write-TestPhaseExecutedManifest builds
    /// it (that half moves in S09), published by the production TestPhasePublication.PublishChunk into the bound context,
    /// else one it opens, projects and closes. A null exit code, coverage and pass is the null verdict.
    /// </summary>
    internal static bool Publish(TestPhaseJournalContext? bound, string logRoot, string trxPath, string suite, string chunk, string runId,
        string attempt, double hostWallSeconds, object? exitCode, object? coverageComplete, object? testsPassed,
        TestPhasePublicationScope? scope = null, Hashes? hashes = null)
    {
        var fresh = FreshFromTrx(logRoot, trxPath, suite, chunk, runId, attempt, hostWallSeconds, exitCode, coverageComplete, testsPassed, hashes);
        return TestPhasePublication.PublishChunk(bound, logRoot, fresh, suite, chunk, runId, attempt, scope ?? Scope());
    }

    /// <summary>Write-TestPhaseExecutedManifest's fresh chunk: the TRX outcome rows, their passed count and digest.</summary>
    internal static OrderedDictionary FreshFromTrx(string logRoot, string trxPath, string suite, string chunk, string runId, string attempt,
        double hostWallSeconds, object? exitCode, object? coverageComplete, object? testsPassed, Hashes? hashes)
    {
        var rows = TrxRows(trxPath);
        var trxMissing = TestPhaseFiles.Kind(trxPath, out _) != "regular";
        var outcomeRows = TestPhaseSummary.OutcomeRows(rows, "TestId", "Outcome", false);
        var passed = TestPhaseSummary.PassedCount(outcomeRows);
        var digest = "";
        if (!trxMissing)
        {
            if (hashes is not null) hashes.Count++;
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

    /// <summary>
    /// Invoke-LegacyExecutedManifest: the pre-journal writer (the harness's pinned copy), the byte oracle. Every publication
    /// re-reads the flat manifest, re-settles every other chunk and writes the whole document again. timing: the
    /// process timing root the harness set to write the oracle elsewhere (else LogRoot/timing).
    /// </summary>
    internal static void LegacyPublish(string logRoot, string trxPath, string suite, string chunk, string runId, string attempt,
        double hostWallSeconds, bool nullVerdict = false, int exitCode = 0, bool coverage = false, bool passed = false, string timing = "")
    {
        var fresh = FreshFromTrx(logRoot, trxPath, suite, chunk, runId, attempt, hostWallSeconds,
            nullVerdict ? null : exitCode, nullVerdict ? null : coverage, nullVerdict ? null : passed, null);
        var dir = string.IsNullOrWhiteSpace(timing) ? Path.Combine(logRoot, "timing") : timing;
        var path = Path.Combine(dir, "groups-executed.json");
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

    internal static bool Has(object o, string name)
    {
        PsValue.Property(o, name, out var found);
        return found;
    }

    // ---- inputs (New-MpTrx, New-MpResultRow, Write-MpDefinedTrx, Write-MpLegacyPrefix) ----

    internal static void WriteTrx(string path, params string[] rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun><Results>" + string.Concat(rows) + "</Results></TestRun>", Utf8NoBom);
    }

    internal static string ResultRow(string id, string outcome) =>
        "<UnitTestResult testId=\"" + id + "\" testName=\"" + id + "\" outcome=\"" + outcome + "\" />";

    /// <summary>Write-MpDefinedTrx: one test of a class (its definition carries the class name).</summary>
    internal static void WriteDefinedTrx(string path, string className, string outcome)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun><TestDefinitions><UnitTest id=\"t1\" name=\"Plain\"><TestMethod className=\""
            + className + "\" name=\"Plain\" /></UnitTest></TestDefinitions><Results><UnitTestResult testId=\"t1\" testName=\"Plain\" outcome=\""
            + outcome + "\" /></Results></TestRun>", Utf8NoBom);
    }

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

    internal static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Utf8NoBom);
    }

    // ---- outputs (Get-MpSha, Read-MpChunkIds, Get-MpJournalRows) ----

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
            .Where(f => Regex.IsMatch(Path.GetFileName(f), "^[0-9]{8}\\.json$"));
    }

    internal sealed record JournalRow(string Kind, string Suite, string Chunk);

    /// <summary>
    /// Get-MpJournalRows: the current generation's records as an independent reader sees them on disk (each record file
    /// parsed on its own; the suite and chunk from their base64 keys).
    /// </summary>
    internal static List<JournalRow> JournalRows(string timing)
    {
        var rows = new List<JournalRow>();
        var journal = Path.Combine(timing, "groups-executed.journal");
        var currentPath = Path.Combine(journal, "current.json");
        if (!File.Exists(currentPath)) return rows;
        using var current = JsonDocument.Parse(File.ReadAllText(currentPath));
        var generation = Path.Combine(journal, current.RootElement.GetProperty("generation").GetString() ?? "");
        if (!Directory.Exists(generation)) return rows;
        foreach (var file in Directory.GetFiles(generation, "*.json").Where(f => Regex.IsMatch(Path.GetFileName(f), "^[0-9]{8}\\.json$")).Order(StringComparer.OrdinalIgnoreCase))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            rows.Add(new JournalRow(Str(root, "kind"), FromKey(root, "freshSuiteKey"), FromKey(root, "freshChunkKey")));
        }
        return rows;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string FromKey(JsonElement e, string name)
    {
        var text = Str(e, name);
        return string.IsNullOrWhiteSpace(text) ? "" : Encoding.UTF8.GetString(Convert.FromBase64String(text));
    }

    /// <summary>The path of the current generation's directory, or "" without a pointer.</summary>
    internal static string CurrentGeneration(string timing)
    {
        var currentPath = Path.Combine(timing, "groups-executed.journal", "current.json");
        if (!File.Exists(currentPath)) return "";
        using var current = JsonDocument.Parse(File.ReadAllText(currentPath));
        return current.RootElement.GetProperty("generation").GetString() ?? "";
    }

    // ---- the still-PowerShell production entries ----

    /// <summary>pwsh -NoProfile -NonInteractive -File scripts/(script) arguments, with extra environment; its exit and output.</summary>
    internal static Task<(int ExitCode, string Text)> RunEntry(string script, IEnumerable<string> arguments, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo("pwsh") { WorkingDirectory = C1514Support.RepoRoot() };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(C1514Support.RepoRoot(), "scripts", script));
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
        return C1514Support.RunProcess(start, cancellationToken);
    }

    /// <summary>scripts/summarize-test-phases.ps1 -RunDirectory dir -Top 5 (Invoke-TestPhaseRunSummary and the progress line).</summary>
    internal static async Task<string> Summarize(string runDirectory, CancellationToken cancellationToken, bool recover = false, string timingRoot = "")
    {
        var arguments = new List<string> { "-RunDirectory", runDirectory, "-Top", "5" };
        if (recover) arguments.Add("-RecoverExecutedManifest");
        if (!string.IsNullOrWhiteSpace(timingRoot)) arguments.AddRange(["-TimingRoot", timingRoot]);
        var ran = await RunEntry("summarize-test-phases.ps1", arguments, cancellationToken);
        if (ran.ExitCode != 0) throw new InvalidOperationException("summarize-test-phases exit=" + ran.ExitCode + " " + ran.Text);
        return ran.Text;
    }

    /// <summary>
    /// The writer environment of a pwsh that runs the production entries: the metadata a recovery writes when it opens an
    /// empty journal (so an in-process writer can stand in for that host and be resumed by it).
    /// </summary>
    internal static async Task<string> EntryWriter(string work, CancellationToken cancellationToken)
    {
        var probe = Path.Combine(work, "writer-probe");
        Directory.CreateDirectory(probe);
        await Summarize(probe, cancellationToken, recover: true);
        var generation = CurrentGeneration(Path.Combine(probe, "timing"));
        var meta = Path.Combine(probe, "timing", "groups-executed.journal", generation, "metadata.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(meta));
        return doc.RootElement.GetProperty("writerEnvironment").GetString() ?? throw new InvalidOperationException("writer-probe-empty");
    }

    // ---- culture ----

    /// <summary>Initialize-MpCulture: every case ran under the invariant culture.</summary>
    internal static Task Invariant(Action body)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try { body(); }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
        return Task.CompletedTask;
    }

    /// <summary>Initialize-MpCulture around an asynchronous case (the culture flows with the async context).</summary>
    internal static async Task InvariantAsync(Func<Task> body)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try { await body(); }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }
}
