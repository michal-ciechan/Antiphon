using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Checkpoints;
using Antiphon.Tests.Application;
using Antiphon.Tests.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0835 source qualification (and CARD-1035, CARD-1958) through the row and validate verbs, which
/// scripts/run-checkpoint.ps1 and scripts/validate-checkpoint-receipt.ps1 launch since unit 20 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, in process. Each case runs in its own seeded git repository
/// (real git, the tool's own source reader); a FakeDriver plays dotnet and does what the deleted pwsh shim did (record the
/// call and its directory, edit or move the source at a named phase, copy a fixture TRX), a scripted handler the build-slot
/// broker. The pre-unit-20 source.json envelope is still qualified, from a receipt built out of a clean row's report.
/// </summary>
[Category("Integration")]
[Category("C835")]
public sealed class RunCheckpointSourceScriptTests
{
    [Test, ParallelLimiter<ScriptModelSpawnLimit>]
    public async Task C1035_ChildCwdMatchesCertifiedRoot()
    {
        using var native = new Fixture(seed: "repository A");
        using var certified = new Fixture(seed: "repository B", repoName: "certified source with spaces");
        native.Head.ShouldNotBe(certified.Head, "distinct-repository-heads");

        void AssertChildren(int count, params string[] phases)
        {
            certified.Observations.Count.ShouldBe(count, "exact-child-launch-count");
            for (var i = 0; i < count; i++)
            {
                var child = certified.Observations[i];
                child.Phase.ShouldBe(phases[i], "child-phase-matches-request");
                child.Directory.ShouldBe(certified.Repo, "child-cwd-matches-certified-root");
                child.Marker.ShouldBe("repository B", "child-reads-certified-marker");
                child.Arguments.ShouldContain("sample", "literal-relative-project");
                if (phases[i] == "run")
                    child.Arguments.ShouldContain("/*/*/C585SampleTests/*", "literal-filter-argv");
            }
        }

        async Task AssertReceiptAsync(Result result, string build)
        {
            result.Exit.ShouldBe(0, result.Output);
            result.Line.ShouldContain("build=" + build);
            result.Line.ShouldContain("executed=3 passed=3 failed=0 skipped=0");
            result.Line.ShouldContain("dirty=0 source=" + certified.Head + " sourceState=clean buildSource=verified");
            result.Source.GetProperty("start").GetProperty("commit").GetString().ShouldBe(certified.Head);
            result.Source.GetProperty("end").GetProperty("commit").GetString().ShouldBe(certified.Head);
            (await certified.ValidateAsync(result.Evidence)).Exit.ShouldBe(0, "certified-receipt-valid");
        }

        // This process's own directory is neither repository: the row runs in the one --repo-root names.
        var built = await certified.RunAsync(expectedSha: certified.Head);
        // Check independently observed children before trusting their successful receipt.
        AssertChildren(2, "build", "run");
        await AssertReceiptAsync(built, "ok");
        using (var stamp = JsonDocument.Parse(await File.ReadAllTextAsync(certified.Stamp)))
        {
            stamp.RootElement.GetProperty("repositoryRoot").GetString().ShouldBe(certified.Repo);
            stamp.RootElement.GetProperty("project").GetString().ShouldBe(Path.Combine(certified.Repo, "sample"));
            stamp.RootElement.GetProperty("outputPath").GetString()
                .ShouldBe(Path.GetFullPath(Path.Combine(certified.Repo, "sample", "bin-c835")));
        }
        File.Exists(native.Stamp).ShouldBeFalse("no-stamp-in-native-repository");

        var reused = await certified.RunAsync(expectedSha: certified.Head, noBuild: true);
        AssertChildren(3, "build", "run", "run");
        await AssertReceiptAsync(reused, "reused");
        File.Exists(native.Stamp).ShouldBeFalse("reuse-leaves-native-repository-untouched");
    }

    [Test, ParallelLimiter<ScriptModelSpawnLimit>]
    public async Task C835_DiagnosticReceipts()
    {
        // The tool's source reader, end to end: clean, tracked and untracked identities agree across the line and the
        // report's source evidence.
        using var fixture = new Fixture();
        var clean = await fixture.RunAsync();
        clean.Exit.ShouldBe(0, clean.Output);
        clean.Source.GetProperty("state").GetString().ShouldBe("clean", "clean-receipt");
        clean.Line.ShouldContain("dirty=0 source=" + fixture.Head + " sourceState=clean buildSource=verified");
        clean.Source.GetProperty("start").GetProperty("commit").GetString().ShouldBe(fixture.Head);
        fixture.Write("tracked.txt", "edited");
        var tracked = await fixture.RunAsync();
        tracked.Exit.ShouldBe(0, tracked.Output);
        tracked.Source.GetProperty("state").GetString().ShouldBe("dirty", "tracked-diagnostic");
        tracked.Line.ShouldContain("dirty=1 source=" + fixture.Head + "+dirty:", Case.Sensitive,
            "dirty-receipt-agrees-with-source-evidence");
        fixture.Write("new.txt", "new bytes");
        var untracked = await fixture.RunAsync();
        untracked.Exit.ShouldBe(0, untracked.Output);
        untracked.Line.ShouldContain("dirty=2 source=" + fixture.Head + "+dirty:");
        untracked.Source.GetProperty("start").GetProperty("dirtyFiles").GetInt32().ShouldBe(2);
        var failed = await fixture.RunAsync(trx: "c585-failures.trx");
        failed.Exit.ShouldBe(1, failed.Output);
        failed.Line.ShouldContain("sourceState=dirty", Case.Sensitive, "dirty-test-failure-is-still-failure");
    }

    [Test, ParallelLimiter<ScriptModelSpawnLimit>]
    public async Task C835_StrictAdmission()
    {
        using var fixture = new Fixture();
        var control = await fixture.RunAsync(expectedSha: fixture.Head);
        control.Exit.ShouldBe(0, control.Output);
        var calls = fixture.Calls;
        var wrong = await fixture.RunAsync(expectedSha: new string('f', 40), useSlot: true);
        wrong.Exit.ShouldBe(2, wrong.Output);
        wrong.Line.ShouldContain("reason=source_mismatch", Case.Sensitive, "wrong-sha-no-driver");
        fixture.Calls.ShouldBe(calls, "wrong-sha-no-driver");
        fixture.SlotCalls.ShouldBe(0, "wrong-sha-no-lease");
        var sha256 = await fixture.RunAsync(expectedSha: new string('a', 64));
        sha256.Exit.ShouldBe(2, sha256.Output);
        sha256.Line.ShouldContain("reason=source_mismatch", Case.Sensitive,
            "64-character-sha-is-valid-shape-but-not-the-head");
        fixture.Calls.ShouldBe(calls, "64-character-mismatch-no-driver");
        fixture.Write("tracked.txt", "dirty");
        var dirty = await fixture.RunAsync(expectedSha: fixture.Head, useSlot: true);
        dirty.Exit.ShouldBe(2, dirty.Output);
        dirty.Line.ShouldContain("reason=source_dirty", Case.Sensitive, "dirty-no-driver");
        fixture.Calls.ShouldBe(calls, "dirty-no-driver");
        fixture.SlotCalls.ShouldBe(0, "dirty-preflight-no-lease");
        var git = Path.Combine(fixture.Repo, ".git");
        var displaced = Path.Combine(fixture.External, "displaced-git");
        Directory.Move(git, displaced);
        try
        {
            var unknown = await fixture.RunAsync(expectedSha: fixture.Head);
            unknown.Exit.ShouldBe(2, unknown.Output);
            unknown.Source.GetProperty("state").GetString().ShouldBe("unknown");
            unknown.Line.ShouldContain("reason=source_unknown", Case.Sensitive, "unknown-no-driver");
            fixture.Calls.ShouldBe(calls, "unknown-no-driver");
        }
        finally { Directory.Move(displaced, git); }

        using var slotFixture = new Fixture();
        var slotDrift = await slotFixture.RunAsync(expectedSha: slotFixture.Head, useSlot: true, slotDrift: true);
        slotDrift.Exit.ShouldBe(2, slotDrift.Output);
        slotFixture.SlotCalls.ShouldBe(1, "slot-edit-acquired-once");
        slotFixture.Calls.ShouldBe(0, "slot-edit-no-driver");
        slotDrift.Source.GetProperty("state").GetString().ShouldBe("changed");
        slotDrift.Line.ShouldContain("reason=source_changed", Case.Sensitive, "post-slot-observation-refuses-drift");
    }

    [Test, ParallelLimiter<ScriptModelSpawnLimit>]
    public async Task C835_DriftAndReuse()
    {
        using var fixture = new Fixture();
        var built = await fixture.RunAsync(expectedSha: fixture.Head);
        built.Exit.ShouldBe(0, built.Output);
        var reused = await fixture.RunAsync(expectedSha: fixture.Head, noBuild: true);
        reused.Exit.ShouldBe(0, reused.Output);
        reused.Line.ShouldContain("build=reused", Case.Sensitive, "matching-stamp-reused");
        reused.Line.ShouldContain("buildSource=verified", Case.Sensitive, "matching-stamp-verified");
        File.Exists(fixture.Stamp).ShouldBeTrue("successful-build-stamp");
        File.Delete(fixture.Stamp);
        var calls = fixture.Calls;
        var missing = await fixture.RunAsync(expectedSha: fixture.Head, noBuild: true);
        missing.Exit.ShouldBe(2, missing.Output);
        missing.Line.ShouldContain("reason=build_source_mismatch", Case.Sensitive, "missing-stamp-no-tests");
        fixture.Calls.ShouldBe(calls, "missing-stamp-no-tests");

        using (var tampered = new Fixture())
        {
            (await tampered.RunAsync(expectedSha: tampered.Head)).Exit.ShouldBe(0);
            var stamp = JsonNode.Parse(await File.ReadAllTextAsync(tampered.Stamp))!.AsObject();
            stamp["fingerprint"] = new string('f', 64);
            await File.WriteAllTextAsync(tampered.Stamp, stamp.ToJsonString());
            var before = tampered.Calls;
            var mismatch = await tampered.RunAsync(expectedSha: tampered.Head, noBuild: true);
            mismatch.Exit.ShouldBe(2, mismatch.Output);
            mismatch.Line.ShouldContain("reason=build_source_mismatch", Case.Sensitive, "invalid-stamp-no-tests");
            tampered.Calls.ShouldBe(before);
            stamp["fingerprint"] = mismatch.Source.GetProperty("start").GetProperty("fingerprint").GetString();
            stamp["sourceState"] = "dirty";
            await File.WriteAllTextAsync(tampered.Stamp, stamp.ToJsonString());
            (await tampered.RunAsync(expectedSha: tampered.Head, noBuild: true)).Exit.ShouldBe(2, "dirty-stamp-no-tests");
            tampered.Calls.ShouldBe(before);
        }

        using (var failedRebuild = new Fixture())
        {
            (await failedRebuild.RunAsync(expectedSha: failedRebuild.Head)).Exit.ShouldBe(0);
            var failure = await failedRebuild.RunAsync(expectedSha: failedRebuild.Head, buildExit: 37);
            failure.Exit.ShouldBe(2);
            File.Exists(failedRebuild.Stamp).ShouldBeFalse("failed-rebuild-invalidates-stamp");
            var before = failedRebuild.Calls;
            (await failedRebuild.RunAsync(expectedSha: failedRebuild.Head, noBuild: true)).Exit.ShouldBe(2);
            failedRebuild.Calls.ShouldBe(before, "failed-rebuild-cannot-reuse-stale-output");
        }

        // Drift across the run's boundaries: during the build, during the run, HEAD moving, a dirty tree restored.
        using var driftFixture = new Fixture();
        var drift = await driftFixture.RunAsync(driftPhase: "run");
        drift.Exit.ShouldBe(2, drift.Output);
        drift.Source.GetProperty("state").GetString().ShouldBe("changed", "driver-drift-is-changed");
        drift.Line.ShouldContain("executed=3 passed=3", Case.Sensitive, "driver-drift-retains-counts");
        using var sameCountFixture = new Fixture();
        sameCountFixture.Write("tracked.txt", "first dirty value");
        var sameCount = await sameCountFixture.RunAsync(driftPhase: "same-count-run");
        sameCount.Exit.ShouldBe(2, sameCount.Output);
        sameCount.Source.GetProperty("start").GetProperty("dirtyFiles").GetInt32().ShouldBe(1);
        sameCount.Source.GetProperty("end").GetProperty("dirtyFiles").GetInt32().ShouldBe(1);
        sameCount.Source.GetProperty("start").GetProperty("fingerprint").GetString()
            .ShouldNotBe(sameCount.Source.GetProperty("end").GetProperty("fingerprint").GetString(),
                "same-count-content-change-has-new-fingerprint");
        sameCount.Source.GetProperty("state").GetString().ShouldBe("changed", "same-count-driver-drift-is-changed");
        using var buildDriftFixture = new Fixture();
        var buildDrift = await buildDriftFixture.RunAsync(driftPhase: "build");
        buildDrift.Exit.ShouldBe(2, buildDrift.Output);
        buildDrift.Source.GetProperty("state").GetString().ShouldBe("changed", "build-drift");
        using var headDriftFixture = new Fixture();
        var headDrift = await headDriftFixture.RunAsync(driftPhase: "head-run");
        headDrift.Exit.ShouldBe(2, headDrift.Output);
        headDrift.Source.GetProperty("state").GetString().ShouldBe("changed", "head-movement");
        using var restoreFixture = new Fixture();
        restoreFixture.Write("tracked.txt", "dirty before driver");
        var restored = await restoreFixture.RunAsync(driftPhase: "restore-run");
        restored.Exit.ShouldBe(2, restored.Output);
        restored.Source.GetProperty("start").GetProperty("dirtyFiles").GetInt32().ShouldBe(1);
        restored.Source.GetProperty("end").GetProperty("dirtyFiles").GetInt32().ShouldBe(0);
        restored.Source.GetProperty("state").GetString().ShouldBe("changed", "dirty-to-clean-is-source-drift");
    }

    [Test, ParallelLimiter<ScriptModelSpawnLimit>]
    public async Task C835_TerminalEvidence()
    {
        using var fixture = new Fixture();
        var build = await fixture.RunAsync(buildExit: 37);
        build.Exit.ShouldBe(2, build.Output);
        build.Line.ShouldContain("build=failed", Case.Sensitive, "failed-build-receipt");
        build.Source.GetProperty("end").ValueKind.ShouldBe(JsonValueKind.Object, "failed-build-has-observed-end");
        var missing = await fixture.RunAsync(trx: null);
        missing.Exit.ShouldBe(2, missing.Output);
        missing.Line.ShouldContain("sourceState=clean", Case.Sensitive, "missing-trx-preserves-source");
        missing.Report.GetProperty("exitCode").GetInt32().ShouldBe(2);
        var zero = await fixture.RunAsync(trx: "c585-zero.trx");
        zero.Exit.ShouldBe(3, zero.Output);
        zero.Report.GetProperty("rows")[0].GetProperty("executed").GetInt32().ShouldBe(0, "zero-count-persists");

        // A driver that throws is an interruption: no observed end, source unknown, never eligible.
        var interrupted = await fixture.RunAsync(interruptRun: true);
        interrupted.Exit.ShouldBe(2, interrupted.Output);
        interrupted.Output.ShouldContain("driver interruption", Case.Sensitive);
        interrupted.Source.GetProperty("end").ValueKind.ShouldBe(JsonValueKind.Null, "handled-driver-interruption-has-no-observed-end");
        interrupted.Source.GetProperty("state").GetString().ShouldBe("unknown");
        interrupted.Line.ShouldContain("reason=source_unknown", Case.Sensitive);
        (await fixture.ValidateAsync(interrupted.Evidence)).Exit.ShouldBe(2, "handled-interruption-is-not-eligible-review-evidence");
    }

    // The pre-unit-20 source.json envelope (version 1) keeps its verdicts: one built from a clean row's report qualifies,
    // and each change below refuses with the script's reason.
    [Test, ParallelLimiter<ScriptModelSpawnLimit>]
    public async Task C835_ReceiptValidation()
    {
        using var fixture = new Fixture();
        var clean = await fixture.RunAsync(expectedSha: fixture.Head);
        clean.Exit.ShouldBe(0, clean.Output);
        (await fixture.ValidateAsync(clean.Evidence)).Exit.ShouldBe(0, "the row's report qualifies");
        var envelope = fixture.Envelope(clean);
        var valid = await fixture.ValidateAsync(envelope);
        valid.Exit.ShouldBe(0, valid.Output);
        var tampered = Path.Combine(fixture.External, "tampered.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(envelope))!.AsObject();
        json["receipt"] = clean.Line + " dirty=0";
        await File.WriteAllTextAsync(tampered, json.ToJsonString());
        var duplicate = await fixture.ValidateAsync(tampered);
        duplicate.Exit.ShouldBe(2, "duplicate-receipt-token: " + duplicate.Output);
        duplicate.Output.ShouldContain("reason=duplicate_receipt_token", Case.Sensitive, "duplicate-receipt-token");
        json.Remove("start");
        await File.WriteAllTextAsync(tampered, json.ToJsonString());
        var legacy = await fixture.ValidateAsync(tampered);
        legacy.Exit.ShouldBe(2, "legacy-receipt-ineligible: " + legacy.Output);
        legacy.Output.ShouldContain("reason=source_ineligible", Case.Sensitive, "legacy-receipt-ineligible");

        async Task RefusesAsync(string label, string reason, Action<JsonObject> change)
        {
            var copy = JsonNode.Parse(await File.ReadAllTextAsync(envelope))!.AsObject();
            change(copy);
            await File.WriteAllTextAsync(tampered, copy.ToJsonString());
            var checkedReceipt = await fixture.ValidateAsync(tampered);
            checkedReceipt.Output.ShouldContain("reason=" + reason, Case.Sensitive, label);
            checkedReceipt.Exit.ShouldBe(2, label + ": " + checkedReceipt.Output);
        }
        await RefusesAsync("dirty-source-clean-receipt-ineligible", "source_ineligible", value =>
        {
            value["start"]!["dirtyFiles"] = 1;
            value["end"]!["dirtyFiles"] = 1;
            value["state"] = "dirty";
        });
        fixture.Write("tracked.txt", "dirty");
        var dirty = await fixture.RunAsync();
        dirty.Exit.ShouldBe(0, dirty.Output);
        var dirtyReport = await fixture.ValidateAsync(dirty.Evidence);
        dirtyReport.Output.ShouldContain("reason=report_source_ineligible", Case.Sensitive, "dirty-report-ineligible");
        dirtyReport.Exit.ShouldBe(2, "dirty-report-ineligible: " + dirtyReport.Output);
        var dirtyVerdict = await fixture.ValidateAsync(fixture.Envelope(dirty));
        dirtyVerdict.Output.ShouldContain("reason=source_ineligible", Case.Sensitive, "dirty-receipt-ineligible");
        dirtyVerdict.Exit.ShouldBe(2, "dirty-receipt-ineligible: " + dirtyVerdict.Output);
        await RefusesAsync("changed-source", "source_ineligible", value =>
        {
            value["state"] = "changed";
            value["end"]!["fingerprint"] = new string('f', 64);
        });
        await RefusesAsync("unknown-source", "source_ineligible", value =>
        {
            value["state"] = "unknown";
            value["end"] = null;
        });
        await RefusesAsync("wrong-sha", "source_ineligible", value => value["start"]!["commit"] = new string('f', 40));
        await RefusesAsync("build-source-mismatch", "source_ineligible", value => value["buildSource"] = "mismatch");
        await RefusesAsync("selected-receipt-ineligible", "receipt_failed", value =>
        {
            value["exitCode"] = 1;
            value["failed"] = 1;
            var passed = (int)value["executed"]! - 1;
            value["passed"] = passed;
            value["receipt"] = clean.Line.Replace("passed=3", "passed=" + passed, StringComparison.Ordinal)
                .Replace("failed=0", "failed=1", StringComparison.Ordinal);
        });
        await RefusesAsync("malformed-counts", "receipt_failed", value => value["passed"] = 1);
        await RefusesAsync("receipt-sha-disagreement", "receipt_disagreement", value =>
            value["receipt"] = clean.Line.Replace("source=" + fixture.Head, "source=" + new string('f', 40), StringComparison.Ordinal));
        await RefusesAsync("receipt-name", "receipt_name", value =>
            value["receipt"] = clean.Line.Replace("CHECKPOINT CP-2 ", "CHECKPOINT CP-3 ", StringComparison.Ordinal));
        await RefusesAsync("receipt-counts", "receipt_counts", value =>
            value["receipt"] = clean.Line.Replace("passed=3", "passed=0", StringComparison.Ordinal));
        await RefusesAsync("schema-version", "source_ineligible", value => value["version"] = 0);
        await RefusesAsync("missing-end", "source_ineligible", value => value["end"] = null);
        await RefusesAsync("start-capture-unknown", "source_ineligible", value => value["start"]!["captureStatus"] = "unknown");
        await RefusesAsync("end-capture-unknown", "source_ineligible", value => value["end"]!["captureStatus"] = "unknown");
        await RefusesAsync("end-sha", "source_ineligible", value => value["end"]!["commit"] = new string('f', 40));
        await RefusesAsync("end-dirty", "source_ineligible", value => value["end"]!["dirtyFiles"] = 1);
        await RefusesAsync("state-dirty", "source_ineligible", value => value["state"] = "dirty");
        await RefusesAsync("fingerprint-shape", "source_ineligible", value => value["start"]!["fingerprint"] = "bad");
        await RefusesAsync("fingerprint-disagreement", "source_ineligible", value => value["end"]!["fingerprint"] = new string('f', 64));
        var compact = JsonNode.Parse(await File.ReadAllTextAsync(envelope))!.ToJsonString();
        var duplicateProperty = compact.Replace("\"version\":1", "\"version\":1,\"version\":1", StringComparison.Ordinal);
        duplicateProperty.ShouldNotBe(compact, "duplicate-json-fixture");
        await File.WriteAllTextAsync(tampered, duplicateProperty);
        var duplicateJson = await fixture.ValidateAsync(tampered);
        duplicateJson.Exit.ShouldBe(2, "duplicate-json-property: " + duplicateJson.Output);
        duplicateJson.Output.ShouldContain("reason=duplicate_json_property", Case.Sensitive, "duplicate-json-property");
        var badSha = await fixture.ValidateAsync(envelope, "bad");
        badSha.Exit.ShouldBe(2, "expected-sha-invalid: " + badSha.Output);
        badSha.Output.ShouldContain("reason=expected_sha_invalid", Case.Sensitive, "expected-sha-invalid");
    }

    [Test, ParallelLimiter<ScriptModelSpawnLimit>]
    [Arguments("linux", "rostered-skip", 0)]
    [Arguments("ubuntu", "rostered-skip", 0)]
    [Arguments("darwin", "rostered-skip", 0)]
    [Arguments("linux", "unlisted-skip", 2)]
    [Arguments("linux", "native-roster", 2)]
    [Arguments("windows", "rostered-skip", 2)]
    [Arguments("linux", "rostered-pass", 0)]
    [Arguments("unknown", "rostered-skip", 2)]
    [Arguments("linux", "missing-trx", 2)]
    [Arguments("linux", "wrong-count", 2)]
    [Arguments("linux", "mixed-skips", 2)]
    [Arguments("linux", "wrong-sha", 2)]
    [Arguments("linux", "uncommitted-roster", 2)]
    [Arguments("linux", "missing-definition", 2)]
    [Arguments("linux", "duplicate-definition", 2)]
    [Arguments("linux", "duplicate-result", 2)]
    [Arguments("linux", "failed-result", 2)]
    [Arguments("linux", "missing-skipped", 2)]
    [Arguments("linux", "receipt-trx", 2)]
    public async Task C1958_Receipt_skip_allowance_is_closed(string platform, string variant, int expected)
    {
        using var fixture = new Fixture();
        var clean = await fixture.RunAsync(expectedSha: fixture.Head);
        clean.Exit.ShouldBe(0, clean.Output);
        var report = JsonNode.Parse(await File.ReadAllTextAsync(clean.Evidence))!.AsObject();
        var trxPath = Path.Combine(fixture.External, "placement.trx");
        const string rosterMethod = "C1210_Windows_linux_only_outcomes_are_exact";
        var method = variant == "native-roster" ? "C1058_Open_descriptor_must_match_canonical_leaf" :
            variant is "unlisted-skip" or "uncommitted-roster" ? "Unlisted_test" : rosterMethod;
        var className = variant == "native-roster" ? "Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests" :
            "Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests";
        var outcome = variant == "rostered-pass" ? "Passed" : "NotExecuted";
        var skipped = outcome == "NotExecuted" ? 1 : 0;
        var executed = outcome == "Passed" ? 2 : variant == "rostered-skip" ? 0 : 1;
        var passed = executed;
        if (variant == "failed-result") passed--;
        if (variant == "mixed-skips") skipped++;
        var extraResult = variant == "mixed-skips"
            ? "<UnitTestResult testId=\"other\" outcome=\"NotExecuted\" />" : "";
        if (variant == "duplicate-result") extraResult = "<UnitTestResult testId=\"placement\" outcome=\"NotExecuted\" />";
        var definition = $"<UnitTest id=\"placement\"><TestMethod className=\"{className}\" name=\"{method}\" /></UnitTest>";
        var definitions = variant == "missing-definition" ? "" : definition;
        if (variant == "duplicate-definition") definitions += definition;
        var controlResult = executed == 0 ? "" :
            $"<UnitTestResult testId=\"control\" outcome=\"{(variant == "failed-result" ? "Failed" : "Passed")}\" />";
        await File.WriteAllTextAsync(trxPath, $"""
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                {controlResult}
                <UnitTestResult testId="placement" testName="untrusted display name" outcome="{outcome}" />
                {extraResult}
              </Results>
              <TestDefinitions>
                <UnitTest id="control"><TestMethod className="Antiphon.Tests.Scripts.Control" name="Pass" /></UnitTest>
                {definitions}
                <UnitTest id="other"><TestMethod className="Antiphon.Tests.Scripts.Other" name="Skip" /></UnitTest>
              </TestDefinitions>
              <ResultSummary><Counters total="{executed + skipped}" executed="{executed}" passed="{passed}" failed="{(variant == "failed-result" ? 1 : 0)}" notExecuted="{skipped}" /></ResultSummary>
            </TestRun>
            """);
        if (variant == "missing-trx") File.Delete(trxPath);
        if (variant == "uncommitted-roster") fixture.Write("tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs",
            File.ReadAllText(Path.Combine(fixture.Repo, "tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs"))
                .Replace(rosterMethod, "Unlisted_test", StringComparison.Ordinal));
        var line = clean.Line[..clean.Line.IndexOf(" executed=", StringComparison.Ordinal)] +
            $" executed={executed} passed={passed} failed={(variant == "failed-result" ? 1 : 0)} skipped={skipped} trx={trxPath} slot=granted waited=0s dirty=0 source={fixture.Head} sourceState=clean buildSource=verified";
        var row = report["rows"]![0]!.AsObject();
        row["executed"] = executed;
        row["passed"] = passed;
        row["failed"] = variant == "failed-result" ? 1 : 0;
        row["skipped"] = variant == "wrong-count" ? skipped + 1 : skipped;
        row["trx"] = trxPath;
        row["line"] = line;
        if (variant == "wrong-sha") row["source"]!["start"]!["commit"] = new string('f', 40);
        if (variant == "missing-skipped") row["skipped"] = null;
        if (variant == "receipt-trx") row["line"] = line.Replace(trxPath, "other.trx", StringComparison.Ordinal);
        report["host"]!["os"] = platform switch
        {
            "linux" => "Debian GNU/Linux 12 (bookworm)", "ubuntu" => "Ubuntu 24.04.3 LTS",
            "darwin" => "Darwin 24.6.0", "windows" => "Microsoft Windows 10.0.26100", _ => "unexpected",
        };
        var evidence = Path.Combine(fixture.External, "placement.json");
        await File.WriteAllTextAsync(evidence, report.ToJsonString());
        var result = await fixture.ValidateAsync(evidence);
        result.Exit.ShouldBe(expected, variant + ": " + result.Output);
        result.Output.ShouldContain(expected == 0 ? "CHECKPOINT SOURCE VALID" : "CHECKPOINT SOURCE INVALID");
    }

    private sealed record Observation(string Phase, string Directory, string Marker, string[] Arguments);

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "c835-row-" + Guid.NewGuid().ToString("N"));
        public string Repo { get; }
        public string External => Path.Combine(Root, "external");
        public string Head { get; }
        private string _outputPath = "bin-c835/";
        public string Stamp => Path.Combine(Repo, "sample", _outputPath, "checkpoint-build-source.json");
        public int Calls { get; private set; }
        public int SlotCalls { get; private set; }
        public List<Observation> Observations { get; } = [];
        private int _round;
        private static string ProjectRoot => DelegateScriptRunner.RepoRoot;

        // One seeded repository per seed text per test run, copied into each fixture without seven git processes per
        // fixture. Fixtures with the same seed share one commit and HEAD; a test that needs a distinct HEAD uses a
        // distinct seed (as C1035 does). A copy's index stat data no longer matches its files, so git compares their
        // contents and still finds the tree clean. A seed that fails to build is dropped: the next fixture retries.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<(string Directory, string Head)>> Seeds =
            new(StringComparer.Ordinal);
        // A live seed root holds its owner file open (exclusive) for the life of its process; the sweep below deletes
        // only a root whose owner file it can take, so a host that is still running (or paused) keeps its seeds.
        private static FileStream? _seedOwner;
        private static readonly object SeedRootGate = new();
        private static string? _seedRoot;

        private static string SeedRoot()
        {
            lock (SeedRootGate)
            {
                return _seedRoot ??= NewSeedRoot();
            }
        }

        private static string NewSeedRoot()
        {
            // Seed roots a killed test host left behind (ProcessExit never ran) are swept after half a day.
            IEnumerable<string> stale = [];
            try { stale = Directory.EnumerateDirectories(Path.GetTempPath(), "c835-seeds-*").ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            foreach (var candidate in stale)
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(candidate) >= DateTime.UtcNow.AddHours(-12)) continue;
                    try { using var probe = new FileStream(Path.Combine(candidate, ".owner"), FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
                    catch (FileNotFoundException) { }
                    GitFixtureCleanup.Delete(candidate);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            var root = Path.Combine(Path.GetTempPath(), "c835-seeds-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                _seedOwner = new FileStream(Path.Combine(root, ".owner"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            }
            catch
            {
                GitFixtureCleanup.Delete(root);
                throw;
            }
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                _seedOwner?.Dispose();
                GitFixtureCleanup.Delete(root);
            };
            return root;
        }

        private static (string Directory, string Head) Seed(string seed)
        {
            var lazy = Seeds.GetOrAdd(seed, text => new Lazy<(string, string)>(() => Seeded(text)));
            try { return lazy.Value; }
            catch
            {
                Seeds.TryRemove(new KeyValuePair<string, Lazy<(string Directory, string Head)>>(seed, lazy));
                throw;
            }
        }

        private static (string Directory, string Head) Seeded(string seed)
        {
            var repo = Path.Combine(SeedRoot(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repo);
            void Put(string path, string value)
            {
                var target = Path.Combine(repo, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, value);
            }
            string Git(params string[] args)
            {
                var result = RunGit(repo, args, Path.Combine(SeedRoot(), "global.gitconfig"));
                result.Exit.ShouldBe(0, "seed git " + string.Join(' ', args) + ": " + result.Output);
                return result.Output;
            }
            Git("init", "-q");
            Git("config", "user.name", "Checkpoint Test");
            Git("config", "user.email", "checkpoint@example.invalid");
            Git("config", "core.autocrlf", "false");
            // No background maintenance writing this .git while fixtures copy it.
            Git("config", "maintenance.auto", "false");
            Put(".gitignore", "bin-*/\nobj/\n.antiphon/\n");
            Put("tracked.txt", seed);
            Put("tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs",
                File.ReadAllText(Path.Combine(ProjectRoot, "tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs")));
            Put("sample/sample.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Git("add", ".");
            Git("commit", "-qm", "seed");
            var head = Git("rev-parse", "HEAD").Trim();
            head.ShouldMatch("^[0-9a-f]{40,64}$", "seed repository committed");
            return (repo, head);
        }

        private static void CopyTree(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (var directory in Directory.EnumerateDirectories(source))
                CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }

        public Fixture(string seed = "seed", string repoName = "source")
        {
            Repo = Path.Combine(Root, repoName);
            // The seed first, so a failed seed leaves nothing under Root; any later failure removes Root.
            var seeded = Seed(seed);
            Head = seeded.Head;
            try
            {
                Directory.CreateDirectory(External);
                File.WriteAllText(Path.Combine(External, "global.gitconfig"), "");
                CopyTree(seeded.Directory, Repo);
            }
            catch
            {
                GitFixtureCleanup.Delete(Root);
                throw;
            }
        }

        public void Write(string path, string value)
        {
            var target = Path.Combine(Repo, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, value);
        }

        // The deleted pwsh shim's behaviour: record the call and where it ran, drift at the named phase, fail the build
        // with buildExit, copy the fixture TRX into the run's results directory.
        private FakeDriver Driver(string? trx, int buildExit, string? driftPhase, bool interruptRun)
        {
            var driver = new FakeDriver();
            driver.When(_ => true, (request, _) =>
            {
                var phase = request.Arguments[0];
                Calls++;
                Observations.Add(new Observation(phase, request.WorkingDirectory,
                    File.ReadAllText(Path.Combine(request.WorkingDirectory, "tracked.txt")), request.Arguments.ToArray()));
                if (driftPhase == phase) Write("tracked.txt", "changed during driver");
                if (driftPhase == "head-" + phase) RunGit(Repo, ["commit", "--allow-empty", "-qm", "move head during driver"], Path.Combine(External, "global.gitconfig"));
                if (driftPhase == "restore-" + phase) RunGit(Repo, ["restore", "--", "tracked.txt"], Path.Combine(External, "global.gitconfig"));
                if (driftPhase == "same-count-" + phase) Write("tracked.txt", "second dirty value");
                if (phase == "build") return Task.FromResult(new DriverResult(buildExit, "", ""));
                if (interruptRun) throw new IOException("C835 driver interruption");
                if (trx is not null) File.Copy(Path.Combine(ProjectRoot, "scripts", "fixtures", trx), CheckpointFixtures.TrxFile(request));
                return Task.FromResult(new DriverResult(0, "", ""));
            });
            return driver;
        }

        public async Task<Result> RunAsync(string? expectedSha = null, bool noBuild = false,
            string? trx = "c585-green.trx", int buildExit = 0, string? driftPhase = null,
            bool useSlot = false, bool slotDrift = false, bool interruptRun = false)
        {
            var round = ++_round;
            // Rebuilds require fresh output ownership. A no-build row deliberately keeps the most recently selected output
            // so stamp reuse and refusal stay real.
            if (!noBuild) _outputPath = round == 1 ? "bin-c835/" : $"bin-c835-{round}/";
            var resultRoot = Path.Combine(External, "results-" + round);
            var arguments = new List<string>
            {
                "row", "--repo-root", Repo, "--name", "CP-2", "--project", "sample", "--output-path", _outputPath,
                "--filter", "/*/*/C585SampleTests/*", "--results-root", resultRoot, "--min-executed", "1",
            };
            if (!useSlot) arguments.Add("--no-slot");
            if (expectedSha is not null) arguments.AddRange(["--expected-source-sha", expectedSha]);
            if (noBuild) arguments.Add("--no-build");
            var output = new StringWriter();
            var exit = await Antiphon.Checkpoints.Program.RunAsync(arguments.ToArray(), new CheckpointApp.Runtime
            {
                Output = output, Driver = Driver(trx, buildExit, driftPhase, interruptRun), EnvironmentLookup = _ => null,
                RowStamp = () => "round-" + round, SlotHandler = new Broker(this, slotDrift), SlotHolders = new ThisProcess(),
            });
            var text = output.ToString();
            if (!Directory.Exists(resultRoot))
                throw new InvalidOperationException("Checkpoint produced no results: " + text);
            var evidence = Directory.GetFiles(resultRoot, "report.json", SearchOption.AllDirectories).Single();
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(evidence));
            var line = text.Split('\n').Select(value => value.TrimEnd('\r')).Single(value => value.StartsWith("CHECKPOINT CP-2 commit=", StringComparison.Ordinal));
            return new Result(exit, text, line, evidence, json.RootElement.GetProperty("source").Clone(), json.RootElement.Clone());
        }

        /// <summary>The source.json envelope (version 1) run-checkpoint.ps1 wrote before unit 20, from a row's report.</summary>
        public string Envelope(Result result)
        {
            var row = result.Report.GetProperty("rows")[0];
            var envelope = JsonNode.Parse(result.Source.GetRawText())!.AsObject();
            envelope["version"] = 1;
            envelope["name"] = "CP-2";
            envelope["reason"] = null;
            envelope["receipt"] = result.Line;
            envelope["exitCode"] = result.Exit;
            foreach (var key in new[] { "executed", "passed", "failed", "skipped" })
                envelope[key] = row.GetProperty(key).GetInt32();
            envelope["timings"] = null;
            var path = Path.Combine(External, "source-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, envelope.ToJsonString());
            return path;
        }

        public async Task<(int Exit, string Output)> ValidateAsync(string evidence, string? expectedSha = null)
        {
            var output = new StringWriter();
            var exit = await Antiphon.Checkpoints.Program.RunAsync(
                ["validate", "--repo-root", Repo, "--evidence", evidence, "--expected-source-sha", expectedSha ?? Head],
                new CheckpointApp.Runtime { Output = output });
            return (exit, output.ToString());
        }

        public void Dispose() => GitFixtureCleanup.Delete(Root);

        // git with no global or system config (an empty global file, no system file), in the case's own repository
        // whatever the host exported.
        private static (int Exit, string Output) RunGit(string cwd, IReadOnlyList<string> args, string globalConfig)
        {
            if (!File.Exists(globalConfig)) File.WriteAllText(globalConfig, "");
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo("git") { WorkingDirectory = cwd,
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
            string[] redirects = ["GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY",
                "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_NAMESPACE", "GIT_SHALLOW_FILE", "GIT_GRAFT_FILE",
                "GIT_REPLACE_REF_BASE", "GIT_NO_REPLACE_OBJECTS", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT"];
            foreach (var name in process.StartInfo.Environment.Keys.Where(key =>
                         redirects.Contains(key, StringComparer.OrdinalIgnoreCase)
                         || key.StartsWith("GIT_CONFIG_KEY_", StringComparison.OrdinalIgnoreCase)
                         || key.StartsWith("GIT_CONFIG_VALUE_", StringComparison.OrdinalIgnoreCase)).ToList())
                process.StartInfo.Environment.Remove(name);
            process.StartInfo.Environment["GIT_CONFIG_GLOBAL"] = globalConfig;
            process.StartInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(90_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("git " + string.Join(' ', args) + " exceeded 90 s");
            }
            return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
        }

        private sealed class ThisProcess : ILeaseHolderSource
        {
            public ILeaseHolder Open() => new Holder();

            private sealed class Holder : ILeaseHolder
            {
                public int Pid => Environment.ProcessId;
                public string? ProcessStartUtc => "2026-11-24T12:00:00.0000000Z";
                public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            }
        }

        // The broker: an enabled listing, then an unlimited grant per acquire (counted); slotDrift edits the source while
        // the row waits for it.
        private sealed class Broker(Fixture fixture, bool slotDrift) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Method == HttpMethod.Get)
                    return Task.FromResult(Json("""{"enabled":true,"budget":2,"maxCpuCount":4}"""));
                fixture.SlotCalls++;
                if (slotDrift) fixture.Write("tracked.txt", "changed while waiting for slot");
                return Task.FromResult(Json("""{"unlimited":true,"maxCpuCount":4}"""));
            }

            private static HttpResponseMessage Json(string body) =>
                new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed record Result(int Exit, string Output, string Line, string Evidence, JsonElement Source, JsonElement Report);
}
