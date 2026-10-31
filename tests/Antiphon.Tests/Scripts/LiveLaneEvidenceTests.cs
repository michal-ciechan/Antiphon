using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport.LiveClaude;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>C680 and completed-artifact sanitizer witnesses. No native process or provider call.</summary>
[Category("Unit"), Timeout(300_000)]
[ParallelLimiter<ProcessSpawnLimit>]
public class LiveLaneEvidenceTests
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string OtherSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly (string Case, string Test, string Cp, string Kind)[] Roster =
    [
        ("control-success", "EvidenceIsSanitized", "CP-20", "success"),
        ("control-assertion", "EvidenceIsSanitized", "CP-20", "assertion"),
        ("control-skip", "EvidenceIsSanitized", "CP-20", "skip"),
        ("off-success", "EvidenceIsSanitized", "CP-20", "success"),
        ("off-assertion", "EvidenceIsSanitized", "CP-20", "assertion"),
        ("off-skip", "EvidenceIsSanitized", "CP-20", "skip"),
        ("slash-success", "EvidenceIsSanitized", "CP-20", "success"),
        ("slash-assertion", "EvidenceIsSanitized", "CP-20", "assertion"),
        ("slash-skip", "EvidenceIsSanitized", "CP-20", "skip"),
        ("submit-success", "EvidenceIsSanitized", "CP-20", "success"),
        ("submit-assertion", "EvidenceIsSanitized", "CP-20", "assertion"),
        ("submit-skip", "EvidenceIsSanitized", "CP-20", "skip"),
        ("c514-success", "EvidenceIsSanitized", "CP-20", "success"),
        ("c514-assertion", "EvidenceIsSanitized", "CP-20", "assertion"),
        ("c514-skip", "EvidenceIsSanitized", "CP-20", "skip"),
        ("enc-lf", "EvidenceIsSanitized", "CP-20", "success"),
        ("enc-crlf", "EvidenceIsSanitized", "CP-20", "success"),
        ("enc-ansi", "EvidenceIsSanitized", "CP-20", "success"),
        ("enc-osc8", "EvidenceIsSanitized", "CP-20", "success"),
        ("enc-json", "EvidenceIsSanitized", "CP-20", "success"),
        ("enc-xml", "EvidenceIsSanitized", "CP-20", "success"),
        ("enc-double", "EvidenceIsSanitized", "CP-20", "success"),
        ("c680-composer", "C680EvidenceIsSanitized", "CP-26", "diagnostic"),
        ("c680-draft", "C680EvidenceIsSanitized", "CP-26", "diagnostic"),
        ("c680-last-screen", "C680EvidenceIsSanitized", "CP-26", "diagnostic"),
        ("c680-transcript", "C680EvidenceIsSanitized", "CP-26", "diagnostic"),
        ("c680-observations", "C680EvidenceIsSanitized", "CP-26", "diagnostic"),
        ("c680-success", "C680EvidenceIsSanitized", "CP-26", "success"),
        ("c680-assertion", "C680EvidenceIsSanitized", "CP-26", "assertion"),
        ("c680-skip", "C680EvidenceIsSanitized", "CP-26", "skip"),
        ("c680-exception", "C680EvidenceIsSanitized", "CP-26", "exception")
    ];

    [Test]
    public void C680EvidenceIsSanitized()
    {
        var root = Path.Combine("/tmp", "c1545-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = Guid.NewGuid();
            var diag = new ClaudeDialogCanaryDiagnostics(root, Path.Combine(root, "sessions"),
                Path.Combine(root, "cwd"), session, "v", "m", "Bash");
            var raw = LiveClaudeEvidenceSentinels.Link + "\n" + LiveClaudeEvidenceSentinels.Screen + "\n"
                + LiveClaudeEvidenceSentinels.Secret + "\n" + LiveClaudeEvidenceSentinels.Device;
            diag.Composer(raw, empty: true, settled: true);
            diag.Draft(raw, complete: true, settled: true);
            diag.Observe(new SessionRunnerSnapshotDto(session, raw, raw, 1, DateTime.UtcNow),
                new SessionRunnerTranscriptDto(session, [
                    new SessionRunnerTranscriptEvent(session, 1, TranscriptKinds.UserPrompt, null, null, null, null, raw, null, null, null, null, null)
                ], 1), blocked: false, submitted: true, called: false);
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                if (LiveClaudeEvidenceSanitizer.ContainsSentinel(text))
                    throw new InvalidOperationException("C1545 V-7/diagnostics payloadHasSentinel");
            }
            var composer = Directory.EnumerateFiles(root, "composer-screen.txt", SearchOption.AllDirectories).Single();
            if (!File.ReadAllText(composer).Contains("C1545-EVIDENCE case=c680-composer", StringComparison.Ordinal))
                throw new InvalidOperationException("C1545 V-7/suppress-marker actualSinkHasCaseMarker");
            var markers = new List<string>();
            foreach (var row in Roster.Where(row => row.Cp == "CP-26"))
            {
                string? screen = row.Case == "c680-exception" ? null : raw;
                if (row.Case == "c680-exception")
                {
                    try { throw new InvalidOperationException(LiveClaudeEvidenceSentinels.Secret); }
                    catch (Exception ex) { screen = ex.Message; }
                }
                var text = LiveClaudeEvidence.Projected(row.Case, row.Test, row.Kind, "portable", null, screen, null, null);
                if (LiveClaudeEvidenceSanitizer.ContainsSentinel(text) || text.Contains("SENTINEL-", StringComparison.Ordinal))
                    throw new InvalidOperationException("C1545 V-7/diagnostics payloadHasSentinel");
                if (!text.Contains("C1545-EVIDENCE case=" + row.Case + " test=" + row.Test, StringComparison.Ordinal))
                    throw new InvalidOperationException("C1545 V-7/suppress-marker actualSinkHasCaseMarker");
                markers.Add(text.Split('\n')[0]);
            }
            var unsafeRun = LiveClaudeEvidence.Projected("c680-run", "C680EvidenceIsSanitized", "success",
                "bad " + LiveClaudeEvidenceSentinels.Secret, null, null, null, null);
            if (!unsafeRun.Contains("run=rejected", StringComparison.Ordinal) || LiveClaudeEvidenceSanitizer.ContainsSentinel(unsafeRun))
                throw new InvalidOperationException("C1545 V-7/unsafe-run-id unsafeRunIdRejectedOrProjected");
            var poisoned = new SanitizedEvidence(markers[0], markers[0] + "\n" + LiveClaudeEvidenceSentinels.Secret);
            var receipt = LiveClaudeEvidence.SerializeReceipt([poisoned], "portable");
            var summary = LiveClaudeEvidence.SerializeSummary([poisoned], "portable");
            if (LiveClaudeEvidenceSanitizer.ContainsSentinel(receipt) || LiveClaudeEvidenceSanitizer.ContainsSentinel(summary))
                throw new InvalidOperationException("C1545 V-7/receipt payloadHasSentinel");
            ProveDiagnosticMutants();
            ProveSerializerMutants();
            BindReceipt(FakeContext(root, markers), markers);
            var live = Environment.GetEnvironmentVariable("ANTIPHON_LIVE_EVIDENCE_CONTEXT");
            if (!string.IsNullOrWhiteSpace(live)) BindReceipt(live, markers);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Test]
    public void ArtifactGateRequiresPresence()
    {
        var script = Path.Combine(FindRepo(), "scripts", "lib", "live-lane-evidence.ps1");
        if (!File.Exists(script))
            throw new InvalidOperationException("C1545 V-8/entry required evidence scanner is absent");
        var root = Path.Combine("/tmp", "c1545-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var roster = Run(script, "Roster", null, null);
            var expected = Roster.Select(row => row.Case + " " + row.Test + " " + row.Cp).ToArray();
            var actual = roster.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (roster.Code != 0 || !expected.SequenceEqual(actual))
                throw new InvalidOperationException("C1545 V-8/roster " + roster.Text);
            var clean = Build(Path.Combine(root, "clean"), cleanFiles: true);
            Expect(script, "Scan", clean.Context, 0, "EVIDENCE-SCAN clean", null);
            Expect(script, "Publish", clean.Context, 0, "publicationCount=1", null);
            if (!File.Exists(clean.Public)) throw new InvalidOperationException("C1545 V-8/publish public summary missing");
            Prove(script, "PC-48", "Scan", Path.Combine(root, "absent.json"),
                "Exit-Evidence 2 'context-absent' # GUARD context-present",
                "$Path = [string]$env:C1545_DECOY_CONTEXT # GUARD context-present",
                2, "context-absent", 0, "EVIDENCE-SCAN clean",
                new Dictionary<string, string> { ["C1545_DECOY_CONTEXT"] = clean.Context });
            var otherMarker = "C1545-EVIDENCE case=control-success test=OtherTest kind=success run=portable";
            Prove(script, "PC-49", "Scan", Variant(clean, Path.Combine(root, "wrong-id"), node =>
            {
                node["cases"]![0]!["test"] = "OtherTest";
                File.WriteAllText(clean.Trx, Trx("OtherTest", clean.Marker));
                File.WriteAllText(clean.Receipt, otherMarker + "\n");
                File.WriteAllText(clean.Summary, otherMarker + "\n");
            }), "if ($hits.Count -ne 1) { return 'marker-missing' } # GUARD marker-missing",
                "if ($false) { return 'marker-missing' } # GUARD marker-missing",
                2, "marker-missing", 0, "EVIDENCE-SCAN clean", null);
            File.WriteAllText(clean.Trx, Trx("EvidenceIsSanitized", clean.Marker));
            File.WriteAllText(clean.Receipt, clean.Marker + "\n");
            File.WriteAllText(clean.Summary, clean.Marker + "\n");
            Prove(script, "PC-50", "Scan", Variant(clean, Path.Combine(root, "receipt-gap"), _ =>
                File.WriteAllText(clean.Receipt, "C1545-EVIDENCE-DOCUMENT kind=receipt run=portable count=0\n")),
                "if (-not $receipt.Text.Contains($marker)) { return 'receipt-marker-missing' } # GUARD receipt-marker",
                "if ($false) { return 'receipt-marker-missing' } # GUARD receipt-marker",
                2, "receipt-marker-missing", 0, "EVIDENCE-SCAN clean", null);
            File.WriteAllText(clean.Receipt, clean.Marker + "\n");
            Prove(script, "PC-51", "Scan", Variant(clean, Path.Combine(root, "summary-gap"), _ =>
                File.WriteAllText(clean.Summary, "C1545-EVIDENCE-DOCUMENT kind=summary run=portable count=0\n")),
                "if (-not $summary.Text.Contains($marker)) { return 'summary-marker-missing' } # GUARD summary-marker",
                "if ($false) { return 'summary-marker-missing' } # GUARD summary-marker",
                2, "summary-marker-missing", 0, "EVIDENCE-SCAN clean", null);
            File.WriteAllText(clean.Summary, clean.Marker + "\n");
            Prove(script, "PC-52", "Scan", Variant(clean, Path.Combine(root, "empty-cases"), node => node["cases"] = new JsonArray()),
                "if ($cases.Count -eq 0) { return 'zero-case' } # GUARD zero-case",
                "if ($false) { return 'zero-case' } # GUARD zero-case",
                2, "zero-case", 0, "EVIDENCE-SCAN clean", null);
            Prove(script, "PC-53", "Scan", Variant(clean, Path.Combine(root, "missing-trx"), node =>
                node["producers"]![0]!["trx"] = Path.Combine(root, "missing.trx")),
                "if (-not $trxRead.Ok) { return $trxRead.Reason } # GUARD missing-trx",
                "if (-not $trxRead.Ok) { return '' } # GUARD missing-trx",
                2, "missing-trx", 0, "EVIDENCE-SCAN clean", null);
            Prove(script, "PC-54", "Scan", Variant(clean, Path.Combine(root, "empty-receipt"), _ =>
                File.WriteAllBytes(clean.Receipt, [])),
                "if (-not $receipt.Ok) { return 'empty-artifact' } # GUARD empty-artifact",
                "if (-not $receipt.Ok) { return '' } # GUARD empty-artifact",
                2, "empty-artifact", 0, "EVIDENCE-SCAN clean", null);
            File.WriteAllText(clean.Receipt, clean.Marker + "\n");
            var dirReceipt = Path.Combine(root, "receipt-dir");
            Directory.CreateDirectory(dirReceipt);
            Prove(script, "PC-55", "Scan", Variant(clean, Path.Combine(root, "unreadable"), node => node["receipt"] = dirReceipt),
                "if (-not $receipt.Ok -and $receipt.Reason -eq 'unreadable') { return 'unreadable' } # GUARD unreadable",
                "if (-not $receipt.Ok -and $receipt.Reason -eq 'unreadable') { return '' } # GUARD unreadable",
                2, "unreadable", 0, "EVIDENCE-SCAN clean", null);
            var nested = Path.Combine(clean.Row, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "leak.txt"), LiveClaudeEvidenceSentinels.Screen);
            Prove(script, "PC-56", "Scan", clean.Context,
                "[IO.SearchOption]::AllDirectories", "[IO.SearchOption]::TopDirectoryOnly",
                2, "sentinel-present", 0, "EVIDENCE-SCAN clean", null);
            File.Delete(Path.Combine(nested, "leak.txt"));
            foreach (var (name, payload) in new (string Name, string Payload)[]
            {
                ("json", "\\u0053ENTINEL-LINK-3a7c132f"),
                ("xml", "&#x53;ENTINEL-SCREEN-3a7c132f"),
                ("ansi", "SENTINEL\u001b[31m-SCREEN-3a7c132f"),
                ("osc8", "\u001b]8;;https://claude.ai/code/session/\\u0053ENTINEL-LINK-3a7c132f\u001b\\")
            })
            {
                File.WriteAllText(Path.Combine(nested, "enc.txt"), payload);
                Prove(script, "PC-57", "Scan", clean.Context,
                    "# GUARD decode\n    $decoded = $text",
                    "return $text # GUARD decode\n    $decoded = $text",
                    2, "sentinel-present", 0, "EVIDENCE-SCAN clean", null);
                Console.WriteLine("C1545 PC-57 cycle " + name);
                File.Delete(Path.Combine(nested, "enc.txt"));
            }
            Expect(script, "Scan", clean.Context, 0, "EVIDENCE-SCAN clean", null);
            Prove(script, "PC-58", "Scan", clean.Context,
                "# GUARD console-absence-only",
                "if ($consoleRead.Text -notmatch 'C1545-EVIDENCE') { return 'marker-missing' }",
                0, "EVIDENCE-SCAN clean", 2, "marker-missing", null);
            Prove(script, "PC-59", "Scan", Variant(clean, Path.Combine(root, "zero-results"), _ =>
                File.WriteAllText(clean.Trx, Trx("EvidenceIsSanitized", clean.Marker, executed: 0))),
                "if ($map.Executed -lt 1) { return 'zero-results' } # GUARD zero-results",
                "if ($false) { return 'zero-results' } # GUARD zero-results",
                2, "zero-results", 0, "EVIDENCE-SCAN clean", null);
            File.WriteAllText(clean.Trx, Trx("EvidenceIsSanitized", clean.Marker));
            Prove(script, "PC-60", "Scan", Variant(clean, Path.Combine(root, "zero-summary"), _ =>
                File.WriteAllText(clean.Console, "  succeeded: 0\n")),
                "if (-not (Test-ConsoleSummary $consoleRead.Text)) { return 'zero-summary' } # GUARD zero-summary",
                "if ($false) { return 'zero-summary' } # GUARD zero-summary",
                2, "zero-summary", 0, "EVIDENCE-SCAN clean", null);
            File.WriteAllText(clean.Console, "  succeeded: 1\n");
            Prove(script, "PC-61", "Scan", Variant(clean, Path.Combine(root, "stale-source"), node =>
                node["producers"]![0]!["source"] = OtherSha),
                "if ($source -ne $expectedSource) { return 'stale-source' } # GUARD stale-source",
                "if ($false) { return 'stale-source' } # GUARD stale-source",
                2, "stale-source", 0, "EVIDENCE-SCAN clean", null);
            Prove(script, "PC-62", "Scan", Variant(clean, Path.Combine(root, "stale-build"), node =>
                node["producers"]![0]!["build"] = "stale"),
                "if ($build -ne 'verified' -or [string](Get-Prop $producer 'buildCommit') -ne $expectedSource) { return 'stale-build' } # GUARD stale-build",
                "if ($false) { return 'stale-build' } # GUARD stale-build",
                2, "stale-build", 0, "EVIDENCE-SCAN clean", null);
            Prove(script, "PC-63", "Scan", Variant(clean, Path.Combine(root, "cross-run"), node =>
                node["producers"]![0]!["runId"] = "other-run"),
                "if ($runId -ne $expectedRun -or [string]::IsNullOrWhiteSpace($expectedRun)) { return 'cross-run' } # GUARD cross-run",
                "if ($false) { return 'cross-run' } # GUARD cross-run",
                2, "cross-run", 0, "EVIDENCE-SCAN clean", null);
            Prove(script, "PC-64", "Scan", Variant(clean, Path.Combine(root, "running"), node =>
                node["producers"]![0]!["state"] = "running"),
                "if ($state -ne 'green' -and $state -ne 'red') { return 'producer-running' } # GUARD producer-running",
                "if ($false) { return 'producer-running' } # GUARD producer-running",
                2, "producer-running", 0, "EVIDENCE-SCAN clean", null);
            var rows = BuildCompleted(Path.Combine(root, "rows"));
            Expect(script, "CompletedRows", rows.Context, 0, "EVIDENCE-SCAN clean", null);
            Prove(script, "PC-65", "CompletedRows", rows.Context,
                "requireReport = $false # GUARD report-deferred",
                "requireReport = $true # GUARD report-deferred",
                0, "EVIDENCE-SCAN clean", 2, "report-missing", null);
            File.Delete(clean.Public);
            File.WriteAllText(Path.Combine(nested, "leak.txt"), LiveClaudeEvidenceSentinels.Screen);
            Prove(script, "PC-66", "Publish", clean.Context,
                "if ($reason) { return $reason } # GUARD publish-after-scan",
                "if ($false) { return $reason } # GUARD publish-after-scan",
                2, "sentinel-present", 0, "publicationCount=1", null);
            if (File.Exists(clean.Public) && RanHealthyPublishWasBlocked(script, clean))
                throw new InvalidOperationException("C1545 V-8/scanner-red publicationCount");
            File.Delete(Path.Combine(nested, "leak.txt"));
            if (File.Exists(clean.Public)) File.Delete(clean.Public);
            foreach (var (pc, fault, needle) in new (string Pc, string Fault, string Needle)[]
            {
                ("context-write", "context-write", "write-fault"),
                ("artifact-read", "artifact-read", "unreadable"),
                ("receipt-write", "receipt-write", "write-fault"),
                ("summary-write", "summary-write", "write-fault"),
                ("publish-write", "publish-write", "write-fault")
            })
            {
                var ctx = Variant(clean, Path.Combine(root, "fault-" + fault), node => node["fault"] = fault);
                Prove(script, "PC-69", "Publish", ctx,
                    "if ($fault -eq '" + fault + "') { return '" + needle + "' } # GUARD " + fault,
                    "if ($false) { return '" + needle + "' } # GUARD " + fault,
                    2, needle, 0, "publicationCount=1", null);
                Console.WriteLine("C1545 PC-69 cycle " + pc);
                if (File.Exists(clean.Public)) File.Delete(clean.Public);
            }
            Prove(script, "PC-70", "Scan", Variant(clean, Path.Combine(root, "child-red"), node =>
            {
                node["producers"]![0]!["state"] = "red";
                node["producers"]![0]!["exit"] = "1";
            }), "if ($state -eq 'red' -or $exitCode -ne 0) { return 'child-red' } # GUARD child-red",
                "if ($false) { return 'child-red' } # GUARD child-red",
                2, "child-red", 0, "EVIDENCE-SCAN clean", null);
            var mutantExits = new List<int>();
            foreach (var raw in new[] { "1", "2", "3", "8" })
            {
                var ctx = Variant(clean, Path.Combine(root, "raw-" + raw), node => node["rawExit"] = int.Parse(raw));
                Expect(script, "Publish", ctx, 2, "nonzero-exit", null);
                mutantExits.Add(Run(Mutate(script, root, "PC-71",
                    "if ($rawExit -eq 1 -or $rawExit -eq 2 -or $rawExit -eq 3 -or $rawExit -eq 8) { return 'nonzero-exit' } # GUARD nonzero-exit",
                    "if ($false) { return 'nonzero-exit' } # GUARD nonzero-exit"), "Publish", ctx, null).Code);
                if (File.Exists(clean.Public)) File.Delete(clean.Public);
            }
            if (mutantExits.Any(code => code != 0))
                throw new InvalidOperationException("C1545 PC-71 mutant " + string.Join(',', mutantExits));
            foreach (var raw in new[] { "1", "2", "3", "8" })
                Expect(script, "Publish", Variant(clean, Path.Combine(root, "raw-restored-" + raw), node => node["rawExit"] = int.Parse(raw)),
                    2, "nonzero-exit", null);
            Console.WriteLine("C1545 PC-71 assertion-red; restored-green nonzero-exit 1/2/3/8");
            Prove(script, "PC-68", "Publish", Variant(clean, Path.Combine(root, "wait75"), node => node["phase"] = "wait75"),
                "if ($phase -eq 'wait75') { return 'wait75' } # GUARD wait75",
                "if ($false) { return 'wait75' } # GUARD wait75",
                2, "wait75", 0, "publicationCount=1", null);
            if (File.Exists(clean.Public)) File.Delete(clean.Public);
            var report = Path.Combine(root, "report.json");
            File.WriteAllText(report, "{\"commit\":\"" + OtherSha + "\"}");
            var outer = Variant(clean, Path.Combine(root, "outer"), node => node["report"] = report);
            Prove(script, "PC-67", "OuterPublish", outer,
                "Exit-Evidence 2 'stale-source' # GUARD final-report-source",
                "if ($false) { Exit-Evidence 2 'stale-source' } # GUARD final-report-source",
                2, "stale-source", 0, "publicationCount=1", null);
            if (File.Exists(clean.Public)) File.Delete(clean.Public);
            File.WriteAllText(report, "{\"commit\":\"" + Sha + "\"}");
            File.WriteAllText(Path.Combine(nested, "leak.txt"), LiveClaudeEvidenceSentinels.Device);
            Prove(script, "PC-72", "OuterPublish", outer,
                "if ($reason) { Exit-Evidence 2 $reason } # GUARD scanner-before-publish",
                "if ($false) { Exit-Evidence 2 $reason } # GUARD scanner-before-publish",
                2, "sentinel-present", 0, "publicationCount=1", null);
            File.Delete(Path.Combine(nested, "leak.txt"));
            var duplicate = Variant(clean, Path.Combine(root, "duplicate"), node =>
            {
                var cases = node["cases"]!.AsArray();
                cases.Add(JsonNode.Parse(cases[0]!.ToJsonString())!);
            });
            Prove(script, "duplicate-uid", "Scan", duplicate,
                "if ($seen.ContainsKey($key)) { return 'duplicate-uid' } # GUARD duplicate-uid",
                "if ($false) { return 'duplicate-uid' } # GUARD duplicate-uid",
                2, "duplicate-uid", 0, "EVIDENCE-SCAN clean", null);
            File.WriteAllText(Path.Combine(nested, "leak.txt"), LiveClaudeEvidenceSentinels.Secret);
            var replaced = Variant(clean, Path.Combine(root, "sentinels-replaced"), node =>
                node["sentinels"] = new JsonArray("NOPE-XYZ"));
            Expect(script, "Scan", replaced, 2, "sentinel-present", null);
            Expect(script, "Scan", Variant(clean, Path.Combine(root, "sentinels-empty"), node =>
                node["sentinels"] = new JsonArray()), 2, "sentinel-present", null);
            Prove(script, "SM-D3-floor", "Scan", replaced,
                "$needles = @($script:SentinelFragments)\n    if ($custom.Count -gt 0) { $needles += $custom }",
                "if ($custom.Count -gt 0) { return @($custom) }\n    $needles = @($script:SentinelFragments)",
                2, "sentinel-present", 0, "EVIDENCE-SCAN clean", null);
            File.Delete(Path.Combine(nested, "leak.txt"));
            var named = Path.Combine(clean.Row, LiveClaudeEvidenceSentinels.Secret + ".txt");
            File.WriteAllBytes(named, []);
            Expect(script, "Scan", clean.Context, 2, "sentinel-present", null);
            File.Delete(named);
            var namedDir = Path.Combine(clean.Row, LiveClaudeEvidenceSentinels.Screen);
            Directory.CreateDirectory(namedDir);
            Expect(script, "Scan", clean.Context, 2, "sentinel-present", null);
            Directory.Delete(namedDir);
            ProveOmittedProducer(script, root);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void ProveOmittedProducer(string script, string root)
    {
        var results = Path.Combine(root, "omitted-results");
        var run = "run-omitted";
        var runDir = Path.Combine(results, run);
        var row = Path.Combine(runDir, "rows", "CP-20");
        Directory.CreateDirectory(row);
        var marker = "C1545-EVIDENCE case=control-success test=EvidenceIsSanitized kind=success run=portable";
        File.WriteAllText(Path.Combine(row, "run.trx"), Trx("EvidenceIsSanitized", marker));
        File.WriteAllText(Path.Combine(row, "console.log"), "  succeeded: 1\n" + LiveClaudeEvidenceSentinels.Secret + "\n");
        var repo = Path.Combine(root, "omitted-repo");
        Directory.CreateDirectory(Path.Combine(repo, "out"));
        File.WriteAllText(Path.Combine(repo, "out", "checkpoint-build-source.json"),
            "{\"version\":1,\"commit\":\"" + Sha + "\"}");
        File.WriteAllText(Path.Combine(runDir, "state.json"),
            "{\"runId\":\"" + run + "\",\"rows\":[{\"id\":\"CP-20\",\"state\":\"green\",\"exitCode\":0,\"line\":\"CHECKPOINT CP-20 commit="
            + Sha + " buildSource=verified\"}]}");
        var report = Path.Combine(root, "omitted-report.json");
        File.WriteAllText(report, "{\"commit\":\"" + Sha + "\"}");
        var cleanRow = Path.Combine(root, "omitted-clean", "rows", "CP-20");
        Directory.CreateDirectory(cleanRow);
        File.WriteAllText(Path.Combine(cleanRow, "run.trx"), Trx("EvidenceIsSanitized", marker));
        File.WriteAllText(Path.Combine(cleanRow, "console.log"), "  succeeded: 1\n");
        var receipt = Path.Combine(root, "omitted-receipt.txt");
        var summary = Path.Combine(root, "omitted-summary.txt");
        var publication = Path.Combine(root, "omitted-public.txt");
        File.WriteAllText(receipt, marker + "\n");
        File.WriteAllText(summary, marker + "\n");
        var context = Path.Combine(root, "omitted-context.json");
        WriteContext(context, new
        {
            source = Sha,
            repo,
            checkpointRun = run,
            resultsRoot = results,
            phase = "",
            rawExit = "",
            cases = new[] { new { @case = "control-success", test = "EvidenceIsSanitized", cp = "CP-20" } },
            producers = new[]
            {
                new
                {
                    id = "CP-20", state = "green", exit = "0", source = Sha, build = "verified",
                    buildCommit = Sha, runId = run,
                    trx = Path.Combine(cleanRow, "run.trx"),
                    console = Path.Combine(cleanRow, "console.log"),
                    rowDir = cleanRow
                }
            },
            outputs = new[] { new { id = "CP-20", path = "out" } },
            receipt,
            summary,
            publicSummary = publication,
            report,
            requireReport = false
        });
        Expect(script, "Scan", context, 2, "sentinel-present", null);
        Expect(script, "OuterPublish", context, 2, "sentinel-present", null);
        if (File.Exists(publication)) File.Delete(publication);
    }

    private static bool RanHealthyPublishWasBlocked(string script, Fixture clean)
    {
        if (!File.Exists(clean.Public)) return false;
        var text = File.ReadAllText(clean.Public);
        File.Delete(clean.Public);
        return text.Contains(LiveClaudeEvidenceSentinels.Screen, StringComparison.Ordinal);
    }

    private static void ProveDiagnosticMutants()
    {
        var repo = FindRepo();
        var diagnostics = File.ReadAllText(Path.Combine(repo, "tests/Antiphon.Tests/Agents/ClaudeDialogCanaryDiagnostics.cs"));
        var watchdog = File.ReadAllText(Path.Combine(repo, "tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs"));
        if (RawSinks(diagnostics, watchdog) != 0
            || !diagnostics.Contains("LiveClaudeEvidence.WriteDiagnostic", StringComparison.Ordinal)
            || !diagnostics.Contains("LiveClaudeEvidence.AppendDiagnostic", StringComparison.Ordinal)
            || !watchdog.Contains("LiveClaudeEvidence.Success", StringComparison.Ordinal))
            throw new InvalidOperationException("C1545 V-7/raw-file unprotectedC680SinkCount");
        var cycles = new (string Pc, string From, string To, bool Watch)[]
        {
            ("PC-42", "LiveClaudeEvidence.Success(\"c680-submit\"", "Console.WriteLine(\"SCREEN:\\n\" + tool); LiveClaudeEvidence.Success(\"c680-submit\"", true),
            ("PC-42", "LiveClaudeEvidence.Success(\"c680-native\"", "true.ShouldBeTrue(\"screen:\\n\" + tool); LiveClaudeEvidence.Success(\"c680-native\"", true),
            ("PC-42", "LiveClaudeEvidence.Success(\"c680-submit\"", "throw new SkipTestException(\"screen:\\n\" + tool); LiveClaudeEvidence.Success(\"c680-submit\"", true),
            ("PC-42", "LiveClaudeEvidence.WriteDiagnostic", "File.WriteAllText", false),
            ("PC-42", "LiveClaudeEvidence.AppendDiagnostic", "File.AppendAllText", false),
            ("SM-C1", "LiveClaudeEvidence.Success(\"c680-native\"", "Console.Out.WriteLine(snapshot.RenderedScreen); LiveClaudeEvidence.Success(\"c680-native\"", true),
            ("SM-C2", "LiveClaudeEvidence.Success(\"c680-submit\"", "throw new SkipTestException(snapshot.RenderedScreen); LiveClaudeEvidence.Success(\"c680-submit\"", true),
            ("SM-C1b", "LiveClaudeEvidence.Success(\"c680-native\"", "Console.Error.WriteLine(after.RenderedScreen); LiveClaudeEvidence.Success(\"c680-native\"", true)
        };
        foreach (var cycle in cycles)
        {
            var source = cycle.Watch ? watchdog : diagnostics;
            if (source.Split(cycle.From).Length != 2)
                throw new InvalidOperationException("C1545 " + cycle.Pc + " anchor");
            var mutated = source.Replace(cycle.From, cycle.To, StringComparison.Ordinal);
            var count = cycle.Watch ? RawSinks(diagnostics, mutated) : RawSinks(mutated, watchdog);
            if (count < 1) throw new InvalidOperationException("C1545 PC-42 did not reach expected assertion");
            Console.WriteLine("C1545 PC-42 assertion-red; restored-green " + cycle.To);
        }
    }

    private static int RawSinks(string diagnostics, string watchdog) =>
        CountOf(diagnostics, "File.WriteAllText(") + CountOf(diagnostics, "File.AppendAllText(")
        + CountOf(watchdog, "Console.WriteLine") + CountOf(watchdog, "screen:\\n");

    private static int CountOf(string text, string needle) => text.Split(needle).Length - 1;

    private static void ProveSerializerMutants()
    {
        var source = File.ReadAllText(Path.Combine(FindRepo(), "tests/Shared/LiveClaude/LiveClaudeEvidenceSanitizer.cs"));
        ProveCompile("PC-43", source, ".Append(Classify(capture.Screen))", ".Append(capture.Screen)", leaked: true, marker: true);
        ProveCompile("PC-44", source,
            "var marker = \"C1545-EVIDENCE case=\" + capture.CaseId + \" test=\" + capture.TestId\n            + \" kind=\" + capture.Kind + \" run=\" + run;",
            "var marker = \"\";", leaked: false, marker: false);
        ProveSerialize("PC-45", source, "SerializeReceipt");
        ProveSerialize("PC-46", source, "SerializeSummary");
        ProveRun("PC-47", source);
    }

    private static void ProveCompile(string pc, string source, string from, string to, bool leaked, bool marker)
    {
        if (source.Split(from).Length != 2) throw new InvalidOperationException("C1545 " + pc + " anchor");
        var dir = Path.Combine("/tmp", "c1545-" + pc);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "out.txt");
        InvokeWrite(Compile(source.Replace(from, to, StringComparison.Ordinal)), path);
        var text = File.ReadAllText(path);
        var red = leaked ? LiveClaudeEvidenceSanitizer.ContainsSentinel(text) : !text.Contains("C1545-EVIDENCE", StringComparison.Ordinal);
        if (!red || (marker && leaked && !LiveClaudeEvidenceSanitizer.ContainsSentinel(text)))
            throw new InvalidOperationException("C1545 " + pc + " did not reach expected assertion");
        InvokeWrite(Compile(source), path);
        var restored = File.ReadAllText(path);
        if (LiveClaudeEvidenceSanitizer.ContainsSentinel(restored) || !restored.Contains("C1545-EVIDENCE case=c680-composer", StringComparison.Ordinal))
            throw new InvalidOperationException("C1545 " + pc + " restore failed");
        Console.WriteLine("C1545 " + pc + " assertion-red; restored-green");
        Directory.Delete(dir, true);
    }

    private static void ProveSerialize(string pc, string source, string method)
    {
        const string from = "lines.Add(evidence.Marker)";
        const string to = "lines.Add(evidence.Text)";
        if (source.Split(from).Length != 2) throw new InvalidOperationException("C1545 " + pc + " anchor");
        var leaked = InvokeSerialize(Compile(source.Replace(from, to, StringComparison.Ordinal)), method);
        if (!leaked.Contains(LiveClaudeEvidenceSentinels.Secret, StringComparison.Ordinal))
            throw new InvalidOperationException("C1545 " + pc + " did not reach expected assertion");
        var restored = InvokeSerialize(Compile(source), method);
        if (restored.Contains(LiveClaudeEvidenceSentinels.Secret, StringComparison.Ordinal))
            throw new InvalidOperationException("C1545 " + pc + " restore failed");
        Console.WriteLine("C1545 " + pc + " assertion-red; restored-green");
    }

    private static void ProveRun(string pc, string source)
    {
        const string from = "var run = IsToken(capture.RunId) ? capture.RunId! : \"rejected\";";
        const string to = "var run = capture.RunId ?? \"\";";
        if (source.Split(from).Length != 2) throw new InvalidOperationException("C1545 " + pc + " anchor");
        var leaked = InvokeProject(Compile(source.Replace(from, to, StringComparison.Ordinal)), "bad " + LiveClaudeEvidenceSentinels.Secret);
        if (!LiveClaudeEvidenceSanitizer.ContainsSentinel(leaked))
            throw new InvalidOperationException("C1545 " + pc + " did not reach expected assertion");
        var restored = InvokeProject(Compile(source), "bad " + LiveClaudeEvidenceSentinels.Secret);
        if (LiveClaudeEvidenceSanitizer.ContainsSentinel(restored) || !restored.Contains("run=rejected", StringComparison.Ordinal))
            throw new InvalidOperationException("C1545 " + pc + " restore failed");
        Console.WriteLine("C1545 " + pc + " assertion-red; restored-green unsafe-run-id");
    }

    private static void BindReceipt(string contextPath, IReadOnlyList<string> localMarkers)
    {
        var ctx = JsonSerializer.Deserialize<EvidenceFile>(File.ReadAllText(contextPath), JsonOptions)
            ?? throw new InvalidOperationException("C1545 V-7/context");
        if (string.IsNullOrWhiteSpace(ctx.CheckpointRun) || string.IsNullOrWhiteSpace(ctx.ResultsRoot)
            || string.IsNullOrWhiteSpace(ctx.ExportPath) || string.IsNullOrWhiteSpace(ctx.Receipt)
            || string.IsNullOrWhiteSpace(ctx.Summary))
            throw new InvalidOperationException("C1545 V-7/context-absent");
        using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(ctx.ResultsRoot, ctx.CheckpointRun, "state.json")));
        if (state.RootElement.GetProperty("runId").GetString() != ctx.CheckpointRun)
            throw new InvalidOperationException("C1545 V-7/cross-run");
        var matched = false;
        foreach (var row in state.RootElement.GetProperty("rows").EnumerateArray())
        {
            if (row.GetProperty("id").GetString() != "CP-20") continue;
            matched = true;
            if (row.GetProperty("state").GetString() != "green")
                throw new InvalidOperationException("C1545 V-7/child-red");
        }
        if (!matched) throw new InvalidOperationException("C1545 V-7/producer-running");
        var export = File.ReadAllText(ctx.ExportPath);
        foreach (var row in Roster.Where(row => row.Cp == "CP-20"))
        {
            if (!export.Contains("C1545-EVIDENCE case=" + row.Case + " test=" + row.Test + " ", StringComparison.Ordinal))
                throw new InvalidOperationException("C1545 V-7/receipt-marker-missing " + row.Case);
        }
        var events = new List<SanitizedEvidence>();
        foreach (var line in File.ReadAllLines(ctx.ExportPath).Concat(localMarkers))
        {
            if (LiveClaudeEvidence.MarkerLine(line)) events.Add(new SanitizedEvidence(line, line));
        }
        var run = LiveClaudeEvidenceSanitizer.IsToken(ctx.RunToken) ? ctx.RunToken : "portable";
        var receipt = LiveClaudeEvidence.SerializeReceipt(events, run);
        var summary = LiveClaudeEvidence.SerializeSummary(events, run);
        if (LiveClaudeEvidenceSanitizer.ContainsSentinel(receipt) || LiveClaudeEvidenceSanitizer.ContainsSentinel(summary))
            throw new InvalidOperationException("C1545 V-7/summary payloadHasSentinel");
        File.WriteAllText(ctx.Receipt, receipt);
        File.WriteAllText(ctx.Summary, summary);
        if (!string.IsNullOrWhiteSpace(ctx.PublicSummary)) File.WriteAllText(ctx.PublicSummary, summary);
    }

    private static string FakeContext(string root, IReadOnlyList<string> localMarkers)
    {
        var runDir = Path.Combine(root, "fake-run");
        Directory.CreateDirectory(runDir);
        var export = Path.Combine(root, "export.txt");
        var lines = Roster.Where(row => row.Cp == "CP-20").Select(row =>
            LiveClaudeEvidenceSanitizer.Project(new EvidenceCapture(row.Case, row.Test, row.Kind, "portable",
                LiveClaudeEvidenceSentinels.Link, LiveClaudeEvidenceSentinels.Screen, null, null, null)).Marker);
        File.WriteAllText(export, string.Join('\n', lines.Concat(localMarkers)) + "\n");
        File.WriteAllText(Path.Combine(runDir, "state.json"),
            "{\"runId\":\"fake-run\",\"rows\":[{\"id\":\"CP-20\",\"state\":\"green\",\"exitCode\":0,\"line\":\"CHECKPOINT CP-20 commit="
            + Sha + " buildSource=verified\"}]}");
        var context = Path.Combine(root, "context.json");
        File.WriteAllText(context, JsonSerializer.Serialize(new EvidenceFile
        {
            CheckpointRun = "fake-run",
            ResultsRoot = root,
            ExportPath = export,
            Receipt = Path.Combine(root, "receipt.txt"),
            Summary = Path.Combine(root, "summary.txt"),
            PublicSummary = Path.Combine(root, "public.txt"),
            RunToken = "portable",
            Source = Sha
        }, JsonOptions));
        return context;
    }

    private sealed class Fixture
    {
        public string Context { get; init; } = "";
        public string Row { get; init; } = "";
        public string Trx { get; init; } = "";
        public string Console { get; init; } = "";
        public string Receipt { get; init; } = "";
        public string Summary { get; init; } = "";
        public string Public { get; init; } = "";
        public string Marker { get; init; } = "";
    }

    private static Fixture Build(string root, bool cleanFiles)
    {
        var row = Path.Combine(root, "rows", "CP-20");
        Directory.CreateDirectory(Path.Combine(row, "nested"));
        var marker = "C1545-EVIDENCE case=control-success test=EvidenceIsSanitized kind=success run=portable";
        var trx = Path.Combine(row, "run.trx");
        var console = Path.Combine(row, "console.log");
        var receipt = Path.Combine(root, "receipt.txt");
        var summary = Path.Combine(root, "summary.txt");
        var publication = Path.Combine(root, "public.txt");
        File.WriteAllText(trx, Trx("EvidenceIsSanitized", marker));
        File.WriteAllText(console, "  succeeded: 1\n");
        File.WriteAllText(Path.Combine(row, "report.html"), "<html>banner</html>");
        File.WriteAllText(Path.Combine(row, "nested", "note.txt"), "banner only");
        File.WriteAllText(receipt, marker + "\n");
        File.WriteAllText(summary, marker + "\n");
        var context = Path.Combine(root, "context.json");
        WriteContext(context, new
        {
            source = Sha,
            checkpointRun = "run1",
            cases = new[] { new { @case = "control-success", test = "EvidenceIsSanitized", cp = "CP-20" } },
            producers = new[]
            {
                new
                {
                    id = "CP-20", state = "green", exit = "0", source = Sha, build = "verified",
                    buildCommit = Sha, runId = "run1", trx, console, rowDir = row
                }
            },
            receipt, summary, publicSummary = publication, requireReport = false
        });
        return new Fixture { Context = context, Row = row, Trx = trx, Console = console, Receipt = receipt, Summary = summary, Public = publication, Marker = marker };
    }

    private static Fixture BuildCompleted(string root)
    {
        var repo = Path.Combine(root, "repo");
        var results = Path.Combine(root, "results");
        var row = Path.Combine(results, "run1", "rows", "CP-20");
        Directory.CreateDirectory(row);
        Directory.CreateDirectory(Path.Combine(repo, "out"));
        var marker = "C1545-EVIDENCE case=control-success test=EvidenceIsSanitized kind=success run=portable";
        File.WriteAllText(Path.Combine(row, "run.trx"), Trx("EvidenceIsSanitized", marker));
        File.WriteAllText(Path.Combine(row, "console.log"), "  succeeded: 1\n");
        File.WriteAllText(Path.Combine(repo, "out", "checkpoint-build-source.json"), "{\"version\":1,\"commit\":\"" + Sha + "\"}");
        File.WriteAllText(Path.Combine(results, "run1", "state.json"),
            "{\"runId\":\"run1\",\"rows\":[{\"id\":\"CP-20\",\"state\":\"green\",\"exitCode\":0,\"line\":\"CHECKPOINT CP-20 commit="
            + Sha + " buildSource=verified\"}]}");
        var receipt = Path.Combine(root, "receipt.txt");
        var summary = Path.Combine(root, "summary.txt");
        var publication = Path.Combine(root, "public.txt");
        File.WriteAllText(receipt, marker + "\n");
        File.WriteAllText(summary, marker + "\n");
        File.WriteAllText(publication, marker + "\n");
        var context = Path.Combine(root, "context.json");
        WriteContext(context, new
        {
            source = Sha, repo, checkpointRun = "run1", resultsRoot = results, requirePublic = true,
            cases = new[] { new { @case = "control-success", test = "EvidenceIsSanitized", cp = "CP-20" } },
            outputs = new[] { new { id = "CP-20", path = "out" } },
            receipt, summary, publicSummary = publication
        });
        return new Fixture { Context = context, Row = row, Receipt = receipt, Summary = summary, Public = publication, Marker = marker };
    }

    private static string Variant(Fixture fixture, string path, Action<JsonNode> edit)
    {
        var node = JsonNode.Parse(File.ReadAllText(fixture.Context))!;
        edit(node);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, node.ToJsonString());
        return path;
    }

    private static void WriteContext(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value));
    }

    private static string Trx(string test, string marker, int executed = 1) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
        + "<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\">"
        + "<Results><UnitTestResult testId=\"11111111-1111-1111-1111-111111111111\" outcome=\"Passed\">"
        + "<Output><StdOut>" + marker + "\n</StdOut></Output></UnitTestResult></Results>"
        + "<TestDefinitions><UnitTest id=\"11111111-1111-1111-1111-111111111111\">"
        + "<TestMethod name=\"" + test + "\" /></UnitTest></TestDefinitions>"
        + "<ResultSummary><Counters total=\"1\" executed=\"" + executed + "\" passed=\"1\" failed=\"0\" /></ResultSummary>"
        + "</TestRun>";

    private static void Expect(string script, string caseName, string? context, int code, string needle,
        IReadOnlyDictionary<string, string>? env)
    {
        var ran = Run(script, caseName, context, env);
        if (ran.Code != code || !ran.Text.Contains(needle, StringComparison.Ordinal))
            throw new InvalidOperationException("C1545 V-8 expected " + code + " " + needle + " actual " + ran.Code + " " + ran.Text);
    }

    private static void Prove(string script, string pc, string caseName, string context, string from, string to,
        int healthyCode, string healthyNeedle, int mutantCode, string mutantNeedle, IReadOnlyDictionary<string, string>? env)
    {
        Expect(script, caseName, context, healthyCode, healthyNeedle, env);
        var copy = Mutate(script, Path.GetDirectoryName(context)!, pc, from, to);
        var mutated = Run(copy, caseName, context, env);
        if (mutated.Code != mutantCode || !mutated.Text.Contains(mutantNeedle, StringComparison.Ordinal))
            throw new InvalidOperationException("C1545 " + pc + " mutant " + mutated.Code + " " + mutated.Text);
        Expect(script, caseName, context, healthyCode, healthyNeedle, env);
        Console.WriteLine("C1545 " + pc + " assertion-red; restored-green " + healthyNeedle);
    }

    private static string Mutate(string script, string directory, string pc, string from, string to)
    {
        var source = File.ReadAllText(script);
        if (source.Split(from).Length != 2) throw new InvalidOperationException("C1545 " + pc + " anchor missing");
        var copy = Path.Combine(directory, pc + "-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(copy, source.Replace(from, to, StringComparison.Ordinal));
        return copy;
    }

    private static (int Code, string Text) Run(string script, string caseName, string? context,
        IReadOnlyDictionary<string, string>? env)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("-Case");
        start.ArgumentList.Add(caseName);
        if (!string.IsNullOrWhiteSpace(context))
        {
            start.ArgumentList.Add("-Context");
            start.ArgumentList.Add(context);
        }
        if (env is not null)
        {
            foreach (var pair in env) start.Environment[pair.Key] = pair.Value;
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("C1545 V-8/pwsh");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("C1545 V-8/pwsh timeout");
        }
        return (process.ExitCode, stdout + "\n" + stderr);
    }

    private static Assembly Compile(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Distinct().Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("c1545-c680-" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var emit = compilation.Emit(bytes);
        if (!emit.Success)
            throw new InvalidOperationException("C1545 mutant compile error: " + string.Join(";",
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id)));
        bytes.Position = 0;
        var load = new AssemblyLoadContext("c1545-c680", isCollectible: true);
        var assembly = load.LoadFromStream(bytes);
        return assembly;
    }

    private static void InvokeWrite(Assembly assembly, string path)
    {
        assembly.GetType("Antiphon.TestSupport.LiveClaude.LiveClaudeEvidence")!
            .GetMethod("WriteDiagnostic")!
            .Invoke(null, [path, "c680-composer", LiveClaudeEvidenceSentinels.Screen]);
    }

    private static string InvokeProject(Assembly assembly, string runId)
    {
        var captureType = assembly.GetType("Antiphon.TestSupport.LiveClaude.EvidenceCapture")!;
        var capture = Activator.CreateInstance(captureType, "c680-composer", "C680EvidenceIsSanitized", "diagnostic", runId,
            LiveClaudeEvidenceSentinels.Link, LiveClaudeEvidenceSentinels.Screen, LiveClaudeEvidenceSentinels.Secret,
            LiveClaudeEvidenceSentinels.Device, null);
        var projected = assembly.GetType("Antiphon.TestSupport.LiveClaude.LiveClaudeEvidenceSanitizer")!
            .GetMethod("Project")!.Invoke(null, [capture]);
        return (string)projected!.GetType().GetProperty("Text")!.GetValue(projected)!;
    }

    private static string InvokeSerialize(Assembly assembly, string method)
    {
        var evidenceType = assembly.GetType("Antiphon.TestSupport.LiveClaude.SanitizedEvidence")!;
        var marker = "C1545-EVIDENCE case=c680-composer test=C680EvidenceIsSanitized kind=diagnostic run=portable";
        var evidence = Activator.CreateInstance(evidenceType, marker, marker + "\n" + LiveClaudeEvidenceSentinels.Secret);
        var listType = typeof(List<>).MakeGenericType(evidenceType);
        var list = Activator.CreateInstance(listType)!;
        listType.GetMethod("Add")!.Invoke(list, [evidence]);
        return (string)assembly.GetType("Antiphon.TestSupport.LiveClaude.LiveClaudeEvidence")!
            .GetMethod(method)!.Invoke(null, [list, "portable"])!;
    }

    private static string FindRepo()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("C1545 V-8/repo");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class EvidenceFile
    {
        [JsonPropertyName("checkpointRun")] public string? CheckpointRun { get; set; }
        [JsonPropertyName("resultsRoot")] public string? ResultsRoot { get; set; }
        [JsonPropertyName("exportPath")] public string? ExportPath { get; set; }
        [JsonPropertyName("receipt")] public string? Receipt { get; set; }
        [JsonPropertyName("summary")] public string? Summary { get; set; }
        [JsonPropertyName("publicSummary")] public string? PublicSummary { get; set; }
        [JsonPropertyName("runToken")] public string? RunToken { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
    }
}
