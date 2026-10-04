using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[Category("C835")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunCheckpointSourceScriptTests
{
    [Test]
    public async Task C835_DiagnosticReceipts()
    {
        using var fixture = new CheckpointSourceScriptFixture();
        var clean = await fixture.RunAsync();
        clean.Exit.ShouldBe(0, clean.Output);
        clean.Source.GetProperty("state").GetString().ShouldBe("clean", "clean-receipt");
        clean.Line.ShouldContain("dirty=0 source=" + fixture.Head + " sourceState=clean buildSource=verified");
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(clean.Evidence)!, "git.txt"))
            .ShouldContain("source=" + fixture.Head + " sourceState=clean buildSource=verified");
        clean.Source.GetProperty("start").GetProperty("commit").GetString().ShouldBe(fixture.Head);
        fixture.Write("tracked.txt", "edited");
        var tracked = await fixture.RunAsync();
        tracked.Exit.ShouldBe(0, tracked.Output);
        tracked.Source.GetProperty("state").GetString().ShouldBe("dirty", "tracked-diagnostic");
        tracked.Line.ShouldContain("dirty=1 source=" + fixture.Head + "+dirty:", Case.Sensitive,
            "dirty-receipt-agrees-with-source-json");
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(tracked.Evidence)!, "git.txt"))
            .ShouldContain("source=" + fixture.Head + "+dirty:");
        fixture.Write("new.txt", "new bytes");
        var untracked = await fixture.RunAsync();
        untracked.Exit.ShouldBe(0, untracked.Output);
        untracked.Line.ShouldContain("dirty=2 source=" + fixture.Head + "+dirty:");
        untracked.Source.GetProperty("start").GetProperty("dirtyFiles").GetInt32().ShouldBe(2);
        var failed = await fixture.RunAsync(trx: "c585-failures.trx");
        failed.Exit.ShouldBe(1, failed.Output);
        failed.Line.ShouldContain("sourceState=dirty", Case.Sensitive, "dirty-test-failure-is-still-failure");
    }

    [Test]
    public async Task C835_StrictAdmission()
    {
        using var fixture = new CheckpointSourceScriptFixture();
        var control = await fixture.RunAsync(expectedSha: fixture.Head);
        control.Exit.ShouldBe(0, control.Output);
        var calls = fixture.Calls;
        var wrong = await fixture.RunAsync(expectedSha: new string('f', 40), useSlot: true);
        wrong.Exit.ShouldBe(2, "wrong-sha-no-lease " + wrong.Output);
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
        dirty.Exit.ShouldBe(2, "dirty-preflight-no-lease " + dirty.Output);
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

        using var slotFixture = new CheckpointSourceScriptFixture();
        var slotDrift = await slotFixture.RunAsync(expectedSha: slotFixture.Head, useSlot: true,
            slotDrift: true);
        slotDrift.Exit.ShouldBe(2, "slot-edit-no-driver " + slotDrift.Output);
        slotFixture.SlotCalls.ShouldBe(1, "slot-edit-acquired-once");
        slotFixture.Calls.ShouldBe(0, "slot-edit-no-driver");
        slotDrift.Source.GetProperty("state").GetString().ShouldBe("changed");
        slotDrift.Line.ShouldContain("reason=source_changed", Case.Sensitive,
            "post-slot-observation-refuses-drift");
    }

    [Test]
    public async Task C835_DriftAndReuse()
    {
        using var fixture = new CheckpointSourceScriptFixture { SessionMode = true };
        var built = await fixture.RunAsync(expectedSha: fixture.Head);
        built.Exit.ShouldBe(0, "S01 " + (built.Output));
        var reused = await fixture.RunAsync(expectedSha: fixture.Head, noBuild: true);
        reused.Exit.ShouldBe(0, "S02 " + (reused.Output));
        reused.Line.ShouldContain("build=reused", Case.Sensitive, "S03 " + ("matching-stamp-reused"));
        reused.Line.ShouldContain("buildSource=verified", Case.Sensitive, "S04 " + ("matching-stamp-verified"));
        File.Exists(fixture.Stamp).ShouldBeTrue( "S05 " + ("successful-build-stamp"));
        File.Delete(fixture.Stamp);
        var calls = fixture.Calls;
        var missing = await fixture.RunAsync(expectedSha: fixture.Head, noBuild: true);
        missing.Exit.ShouldBe(2, "S06 invalid-stamp-no-tests " + (missing.Output));
        missing.Line.ShouldContain("reason=build_source_mismatch", Case.Sensitive, "S07 " + ("invalid-stamp-no-tests missing-stamp-no-tests"));
        fixture.Calls.ShouldBe(calls, "S08 " + ("invalid-stamp-no-tests missing-stamp-no-tests"));

        fixture.ResetScenario();
        var tampered = fixture;

        {
            (await tampered.RunAsync(expectedSha: tampered.Head)).Exit.ShouldBe(0, "S09 ");
            var stamp = JsonNode.Parse(await File.ReadAllTextAsync(tampered.Stamp))!.AsObject();
            stamp["fingerprint"] = new string('f', 64);
            await File.WriteAllTextAsync(tampered.Stamp, stamp.ToJsonString());
            var before = tampered.Calls;
            var mismatch = await tampered.RunAsync(expectedSha: tampered.Head, noBuild: true);
            mismatch.Exit.ShouldBe(2, "S10 invalid-stamp-no-tests " + (mismatch.Output));
            mismatch.Line.ShouldContain("reason=build_source_mismatch", Case.Sensitive, "S11 " + ("invalid-stamp-no-tests"));
            tampered.Calls.ShouldBe(before, "S12 invalid-stamp-no-tests ");
            stamp["fingerprint"] = mismatch.Source.GetProperty("start").GetProperty("fingerprint").GetString();
            stamp["sourceState"] = "dirty";
            await File.WriteAllTextAsync(tampered.Stamp, stamp.ToJsonString());
            (await tampered.RunAsync(expectedSha: tampered.Head, noBuild: true)).Exit.ShouldBe(2, "S13 " + ("invalid-stamp-no-tests dirty-stamp-no-tests"));
            tampered.Calls.ShouldBe(before, "S14 invalid-stamp-no-tests ");
        }

        fixture.ResetScenario();
        var failedRebuild = fixture;

        {
            (await failedRebuild.RunAsync(expectedSha: failedRebuild.Head)).Exit.ShouldBe(0, "S15 ");
            var failure = await failedRebuild.RunAsync(expectedSha: failedRebuild.Head, buildExit: 37);
            failure.Exit.ShouldBe(2, "S16 ");
            File.Exists(failedRebuild.Stamp).ShouldBeFalse( "S17 " + ("failed-rebuild-invalidates-stamp"));
            var before = failedRebuild.Calls;
            (await failedRebuild.RunAsync(expectedSha: failedRebuild.Head, noBuild: true)).Exit.ShouldBe(2, "S18 ");
            failedRebuild.Calls.ShouldBe(before, "S19 " + ("failed-rebuild-cannot-reuse-stale-output"));
        }

        fixture.ResetScenario();
        var driftFixture = fixture;
        var drift = await driftFixture.RunAsync(driftPhase: "run");
        drift.Exit.ShouldBe(2, "S20 driver-drift-is-changed " + (drift.Output));
        drift.Source.GetProperty("state").GetString().ShouldBe("changed", "S21 " + ("driver-drift-is-changed"));
        drift.Line.ShouldContain("executed=3 passed=3", Case.Sensitive, "S22 " + ("driver-drift-retains-counts"));
        fixture.ResetScenario();
        var sameCountFixture = fixture;
        sameCountFixture.Write("tracked.txt", "first dirty value");
        var sameCount = await sameCountFixture.RunAsync(driftPhase: "same-count-run");
        sameCount.Exit.ShouldBe(2, "S23 " + (sameCount.Output));
        sameCount.Source.GetProperty("start").GetProperty("dirtyFiles").GetInt32().ShouldBe(1, "S24 ");
        sameCount.Source.GetProperty("end").GetProperty("dirtyFiles").GetInt32().ShouldBe(1, "S25 ");
        sameCount.Source.GetProperty("start").GetProperty("fingerprint").GetString()
            .ShouldNotBe(sameCount.Source.GetProperty("end").GetProperty("fingerprint").GetString(), "S26 " + ("same-count-content-change-has-new-fingerprint"));
        sameCount.Source.GetProperty("state").GetString().ShouldBe("changed", "S27 " + ("same-count-driver-drift-is-changed"));
        fixture.ResetScenario();
        var buildDriftFixture = fixture;
        var buildDrift = await buildDriftFixture.RunAsync(driftPhase: "build");
        buildDrift.Exit.ShouldBe(2, "S28 " + (buildDrift.Output));
        buildDrift.Source.GetProperty("state").GetString().ShouldBe("changed", "S29 " + ("build-drift"));
        fixture.ResetScenario();
        var headDriftFixture = fixture;
        var headDrift = await headDriftFixture.RunAsync(driftPhase: "head-run");
        headDrift.Exit.ShouldBe(2, "S30 " + (headDrift.Output));
        headDrift.Source.GetProperty("state").GetString().ShouldBe("changed", "S31 " + ("head-movement"));
        fixture.ResetScenario();
        var restoreFixture = fixture;
        restoreFixture.Write("tracked.txt", "dirty before driver");
        var restored = await restoreFixture.RunAsync(driftPhase: "restore-run");
        restored.Exit.ShouldBe(2, "S32 " + (restored.Output));
        restored.Source.GetProperty("start").GetProperty("dirtyFiles").GetInt32().ShouldBe(1, "S33 ");
        restored.Source.GetProperty("end").GetProperty("dirtyFiles").GetInt32().ShouldBe(0, "S34 ");
        restored.Source.GetProperty("state").GetString().ShouldBe("changed", "S35 " + ("dirty-to-clean-is-source-drift"));
    }

    [Test]
    public async Task C835_TerminalEvidence()
    {
        using var fixture = new CheckpointSourceScriptFixture();
        var build = await fixture.RunAsync(buildExit: 37);
        build.Exit.ShouldBe(2, build.Output);
        build.Line.ShouldContain("build=failed", Case.Sensitive, "failed-build-receipt");
        build.Source.GetProperty("end").ValueKind.ShouldBe(JsonValueKind.Object, "failed-build-has-observed-end");
        var missing = await fixture.RunAsync(trx: null);
        missing.Exit.ShouldBe(2, missing.Output);
        missing.Line.ShouldContain("sourceState=clean", Case.Sensitive, "missing-trx-preserves-source");
        missing.Source.GetProperty("exitCode").GetInt32().ShouldBe(2);
        var malformed = await fixture.RunAsync(trx: "c585-zero.trx");
        malformed.Exit.ShouldBe(3, malformed.Output);
        malformed.Source.GetProperty("executed").GetInt32().ShouldBe(0, "zero-count-persists");

        File.Delete(fixture.Stamp);
        Directory.CreateDirectory(fixture.Stamp);
        File.WriteAllText(Path.Combine(fixture.Stamp, "block"), "keep the stamp directory nonempty");
        var interrupted = await fixture.RunAsync();
        interrupted.Exit.ShouldBe(2, interrupted.Output);
        interrupted.Output.ShouldContain("driver interruption", Case.Sensitive);
        interrupted.Source.GetProperty("end").ValueKind.ShouldBe(JsonValueKind.Null,
            "handled-driver-interruption-has-no-observed-end");
        interrupted.Source.GetProperty("state").GetString().ShouldBe("unknown");
        interrupted.Line.ShouldContain("reason=source_unknown", Case.Sensitive);
        (await fixture.ValidateAsync(interrupted.Evidence)).Exit.ShouldBe(2,
            "handled-interruption-is-not-eligible-review-evidence");
    }

    [Test]
    public async Task C835_ReceiptValidation()
    {
        using var fixture = new CheckpointSourceScriptFixture();
        var clean = await fixture.RunAsync(expectedSha: fixture.Head);
        clean.Exit.ShouldBe(0, clean.Output);
        var valid = await fixture.ValidateAsync(clean.Evidence);
        valid.Exit.ShouldBe(0, valid.Output);
        var tampered = Path.Combine(fixture.External, "tampered.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(clean.Evidence))!.AsObject();
        json["receipt"] = clean.Line + " dirty=0";
        await File.WriteAllTextAsync(tampered, json.ToJsonString());
        var duplicate = await fixture.ValidateAsync(tampered);
        duplicate.Exit.ShouldBe(2, "duplicate-receipt-token: " + duplicate.Output);
        duplicate.Output.ShouldContain("reason=duplicate_receipt_token", Case.Sensitive, "duplicate-receipt-token");
        json["receipt"] = clean.Line;
        json.Remove("start");
        await File.WriteAllTextAsync(tampered, json.ToJsonString());
        var legacy = await fixture.ValidateAsync(tampered);
        legacy.Exit.ShouldBe(2, "legacy-receipt-ineligible: " + legacy.Output);
        legacy.Output.ShouldContain("reason=source_ineligible", Case.Sensitive, "legacy-receipt-ineligible");

        async Task RefusesAsync(string label, string reason, Action<JsonObject> change)
        {
            var copy = JsonNode.Parse(await File.ReadAllTextAsync(clean.Evidence))!.AsObject();
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
        var dirtyVerdict = await fixture.ValidateAsync(dirty.Evidence);
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
            value["receipt"] = clean.Line
                .Replace("passed=" + clean.Source.GetProperty("passed").GetInt32(), "passed=" + passed, StringComparison.Ordinal)
                .Replace("failed=0", "failed=1", StringComparison.Ordinal);
        });
        await RefusesAsync("malformed-counts", "receipt_failed", value => value["passed"] = 1);
        await RefusesAsync("receipt-sha-disagreement", "receipt_disagreement", value =>
            value["receipt"] = clean.Line.Replace("source=" + fixture.Head,
                "source=" + new string('f', 40), StringComparison.Ordinal));
        await RefusesAsync("receipt-name", "receipt_name", value =>
            value["receipt"] = clean.Line.Replace("CHECKPOINT CP-2 ", "CHECKPOINT CP-3 ", StringComparison.Ordinal));
        await RefusesAsync("receipt-counts", "receipt_counts", value =>
            value["receipt"] = clean.Line.Replace("passed=" + clean.Source.GetProperty("passed").GetInt32(),
                "passed=0", StringComparison.Ordinal));
        await RefusesAsync("schema-version", "source_ineligible", value => value["version"] = 0);
        await RefusesAsync("missing-end", "source_ineligible", value => value["end"] = null);
        await RefusesAsync("start-capture-unknown", "source_ineligible", value =>
            value["start"]!["captureStatus"] = "unknown");
        await RefusesAsync("end-capture-unknown", "source_ineligible", value =>
            value["end"]!["captureStatus"] = "unknown");
        await RefusesAsync("end-sha", "source_ineligible", value =>
            value["end"]!["commit"] = new string('f', 40));
        await RefusesAsync("end-dirty", "source_ineligible", value => value["end"]!["dirtyFiles"] = 1);
        await RefusesAsync("state-dirty", "source_ineligible", value => value["state"] = "dirty");
        await RefusesAsync("fingerprint-shape", "source_ineligible", value =>
            value["start"]!["fingerprint"] = "bad");
        await RefusesAsync("fingerprint-disagreement", "source_ineligible", value =>
            value["end"]!["fingerprint"] = new string('f', 64));
        var compact = JsonNode.Parse(await File.ReadAllTextAsync(clean.Evidence))!.ToJsonString();
        var duplicateProperty = compact.Replace("\"version\":1", "\"version\":1,\"version\":1", StringComparison.Ordinal);
        duplicateProperty.ShouldNotBe(compact, "duplicate-json-fixture");
        await File.WriteAllTextAsync(tampered, duplicateProperty);
        var duplicateJson = await fixture.ValidateAsync(tampered);
        duplicateJson.Exit.ShouldBe(2, "duplicate-json-property: " + duplicateJson.Output);
        duplicateJson.Output.ShouldContain("reason=duplicate_json_property", Case.Sensitive, "duplicate-json-property");
        var badSha = await fixture.ValidateAsync(clean.Evidence, "bad");
        badSha.Exit.ShouldBe(2, "expected-sha-invalid: " + badSha.Output);
        badSha.Output.ShouldContain("reason=expected_sha_invalid", Case.Sensitive, "expected-sha-invalid");
        var helperShape = JsonNode.Parse(await File.ReadAllTextAsync(clean.Evidence))!.AsObject();
        helperShape["start"]!["commit"] = "bad";
        helperShape["end"]!["commit"] = "bad";
        await File.WriteAllTextAsync(tampered, helperShape.ToJsonString());
        var helperVerdict = await fixture.CheckSourceHelperAsync(tampered, "bad");
        helperVerdict.Exit.ShouldBe(1, "source-helper-expected-sha-shape: " + helperVerdict.Output);
    }

}
