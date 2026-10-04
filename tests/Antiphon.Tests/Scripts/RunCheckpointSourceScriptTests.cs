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
    public async Task C1035_ChildCwdMatchesCertifiedRoot()
    {
        using var native = new Fixture(seed: "repository A");
        using var certified = new Fixture(seed: "repository B", repoName: "certified source with spaces");
        native.Head.ShouldNotBe(certified.Head, "distinct-repository-heads");

        void AssertChildren(int count, params string[] phases)
        {
            var observations = File.ReadAllLines(certified.Observations);
            observations.Length.ShouldBe(count, "exact-child-launch-count");
            for (var i = 0; i < count; i++)
            {
                using var observation = JsonDocument.Parse(observations[i]);
                var child = observation.RootElement;
                child.GetProperty("phase").GetString().ShouldBe(phases[i], "child-phase-matches-request");
                child.GetProperty("cwd").GetString().ShouldBe(certified.Repo,
                    "child-cwd-matches-certified-root");
                child.GetProperty("marker").GetString().ShouldBe("repository B", "child-reads-certified-marker");
                child.GetProperty("arguments").EnumerateArray().Select(value => value.GetString())
                    .ShouldContain("sample", "literal-relative-project");
                if (phases[i] == "run")
                    child.GetProperty("arguments").EnumerateArray().Select(value => value.GetString())
                        .ShouldContain("/*/*/C585SampleTests/*", "literal-filter-argv");
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

        var built = await certified.RunAsync(expectedSha: certified.Head, nativeCwd: native.Repo);
        // Check independently observed children before trusting their successful receipt.
        AssertChildren(2, "build", "run");
        await AssertReceiptAsync(built, "ok");
        using (var stamp = JsonDocument.Parse(await File.ReadAllTextAsync(certified.Stamp)))
        {
            stamp.RootElement.GetProperty("repositoryRoot").GetString().ShouldBe(certified.Repo);
            stamp.RootElement.GetProperty("project").GetString().ShouldBe(Path.Combine(certified.Repo, "sample"));
            stamp.RootElement.GetProperty("outputPath").GetString()
                .ShouldBe(Path.GetFullPath(Path.Combine(certified.Repo, "sample", "bin-c835/")));
        }
        File.Exists(native.Stamp).ShouldBeFalse("no-stamp-in-native-repository");

        var reused = await certified.RunAsync(expectedSha: certified.Head, noBuild: true, nativeCwd: native.Repo);
        AssertChildren(3, "build", "run", "run");
        await AssertReceiptAsync(reused, "reused");
        File.Exists(native.Stamp).ShouldBeFalse("reuse-leaves-native-repository-untouched");
        foreach (var path in Directory.GetFiles(certified.External, "parent-*.json"))
        {
            using var parent = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            parent.RootElement.GetProperty("native").GetString().ShouldBe(native.Repo, "divergent-native-parent");
            parent.RootElement.GetProperty("location").GetString().ShouldBe(certified.Repo, "certified-powershell-location");
        }
        Directory.GetFiles(certified.External, "parent-*.json").Length.ShouldBe(2);
    }

    [Test]
    public async Task C835_DiagnosticReceipts()
    {
        using var fixture = new Fixture();
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
        var slotDrift = await slotFixture.RunAsync(expectedSha: slotFixture.Head, useSlot: true,
            slotDrift: true);
        slotDrift.Exit.ShouldBe(2, slotDrift.Output);
        slotFixture.SlotCalls.ShouldBe(1, "slot-edit-acquired-once");
        slotFixture.Calls.ShouldBe(0, "slot-edit-no-driver");
        slotDrift.Source.GetProperty("state").GetString().ShouldBe("changed");
        slotDrift.Line.ShouldContain("reason=source_changed", Case.Sensitive,
            "post-slot-observation-refuses-drift");
    }

    [Test]
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
            (await tampered.RunAsync(expectedSha: tampered.Head, noBuild: true)).Exit.ShouldBe(2,
                "dirty-stamp-no-tests");
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
        sameCount.Source.GetProperty("state").GetString().ShouldBe("changed",
            "same-count-driver-drift-is-changed");
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
        restored.Source.GetProperty("state").GetString().ShouldBe("changed",
            "dirty-to-clean-is-source-drift");
    }

    [Test]
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
        using var fixture = new Fixture();
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

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "c835-script-" + Guid.NewGuid().ToString("N"));
        public string Repo { get; }
        public string External => Path.Combine(Root, "external");
        public string Observations => Path.Combine(External, "children.jsonl");
        public string Head { get; }
        public string Stamp => Path.Combine(Repo, "sample", "bin-c835", "checkpoint-build-source.json");
        public int Calls => File.Exists(Path.Combine(External, "calls.txt"))
            ? File.ReadAllLines(Path.Combine(External, "calls.txt")).Length : 0;
        public int SlotCalls => File.Exists(Path.Combine(External, "slot-calls.txt"))
            ? File.ReadAllLines(Path.Combine(External, "slot-calls.txt")).Length : 0;
        private int _round;
        private static string ProjectRoot => DelegateScriptRunner.RepoRoot;

        public Fixture(string seed = "seed", string repoName = "source")
        {
            Repo = Path.Combine(Root, repoName);
            Directory.CreateDirectory(Repo);
            Directory.CreateDirectory(External);
            Run("git", Repo, ["init", "-q"]);
            Run("git", Repo, ["config", "user.name", "Checkpoint Test"]);
            Run("git", Repo, ["config", "user.email", "checkpoint@example.invalid"]);
            Run("git", Repo, ["config", "core.autocrlf", "false"]);
            Write(".gitignore", "bin-*/\nobj/\n.antiphon/\n");
            Write("tracked.txt", seed);
            Write("sample/sample.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Run("git", Repo, ["add", "."]);
            Run("git", Repo, ["commit", "-qm", "seed"]);
            Head = Run("git", Repo, ["rev-parse", "HEAD"]).Output.Trim();
            File.WriteAllText(Path.Combine(External, "shim.ps1"), """
                param()
                $items = @($args)
                Add-Content -LiteralPath $env:C835_CALLS -Value ([string]$items[0])
                $phase = [string]$items[0]
                if ($env:C1035_OBSERVATIONS) {
                    $cwd = [Environment]::CurrentDirectory
                    $marker = [IO.File]::ReadAllText([IO.Path]::Combine($cwd, 'tracked.txt'))
                    $observation = @{ phase = $phase; cwd = $cwd; marker = $marker; arguments = $items }
                    Add-Content -LiteralPath $env:C1035_OBSERVATIONS -Value ($observation | ConvertTo-Json -Compress)
                }
                if ($env:C835_DRIFT -eq $phase) {
                    Set-Content -LiteralPath (Join-Path $env:C835_REPO 'tracked.txt') -Value 'changed during driver'
                }
                if ($env:C835_DRIFT -eq ('head-' + $phase)) {
                    git -C $env:C835_REPO commit --allow-empty -qm 'move head during driver'
                }
                if ($env:C835_DRIFT -eq ('restore-' + $phase)) {
                    git -C $env:C835_REPO restore -- tracked.txt
                }
                if ($env:C835_DRIFT -eq ('same-count-' + $phase)) {
                    Set-Content -LiteralPath (Join-Path $env:C835_REPO 'tracked.txt') -Value 'second dirty value'
                }
                if ($phase -eq 'build') { exit [int]$env:C835_BUILD_EXIT }
                $result = ''
                for ($i = 0; $i -lt $items.Count; $i++) {
                    if ($items[$i] -eq '--results-directory') { $result = [string]$items[$i + 1] }
                }
                if ($env:C835_TRX -and $result) { Copy-Item -LiteralPath $env:C835_TRX -Destination (Join-Path $result 'run.trx') }
                exit 0
                """);
            File.WriteAllText(Path.Combine(External, "slot-shim.ps1"), """
                param([string]$Method, [string]$Uri, [string]$BodyJson)
                Add-Content -LiteralPath $env:C835_SLOT_CALLS -Value $Method
                if ($env:C835_SLOT_DRIFT -eq '1') {
                    Set-Content -LiteralPath (Join-Path $env:C835_REPO 'tracked.txt') -Value 'changed while waiting for slot'
                }
                return @{ Status = 200; Body = '{"unlimited":true,"maxCpuCount":4}' }
                """);
        }

        public void Write(string path, string value)
        {
            var target = Path.Combine(Repo, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, value);
        }

        public async Task<Result> RunAsync(string? expectedSha = null, bool noBuild = false,
            string? trx = "c585-green.trx", int buildExit = 0, string? driftPhase = null,
            bool useSlot = false, bool slotDrift = false, string? nativeCwd = null)
        {
            var round = ++_round;
            var resultRoot = Path.Combine(External, "results-" + round);
            var args = new List<string> { "-NoProfile", "-NonInteractive", "-File",
                Path.Combine(ProjectRoot, "scripts", "run-checkpoint.ps1"), "-Name", "CP-2",
                "-Project", "sample", "-OutputPath", "bin-c835/", "-Filter", "/*/*/C585SampleTests/*",
                "-ResultsRoot", resultRoot, "-MinExecuted", "1", "-DotnetShim",
                Path.Combine(External, "shim.ps1") };
            if (!useSlot) args.Add("-NoSlot");
            if (expectedSha is not null) args.AddRange(["-ExpectedSourceSha", expectedSha]);
            if (noBuild) args.Add("-NoBuild");
            var environment = new Dictionary<string, string?>
            {
                ["C835_CALLS"] = Path.Combine(External, "calls.txt"),
                ["C835_REPO"] = Repo,
                ["C835_BUILD_EXIT"] = buildExit.ToString(),
                ["C835_TRX"] = trx is null ? null : Path.Combine(ProjectRoot, "scripts", "fixtures", trx),
                ["C835_DRIFT"] = driftPhase,
                ["C835_SLOT_CALLS"] = Path.Combine(External, "slot-calls.txt"),
                ["C835_SLOT_DRIFT"] = slotDrift ? "1" : null,
                ["C589_SLOT_SHIM"] = Path.Combine(External, "slot-shim.ps1"),
                ["C585_STAMP"] = "round-" + round,
                ["ANTIPHON_BUILD_SLOTS_URL"] = "http://127.0.0.1:1/build-slots",
                ["C1035_OBSERVATIONS"] = nativeCwd is null ? null : Observations,
            };
            if (nativeCwd is not null)
            {
                var wrapper = Path.Combine(External, "wrapper.ps1");
                File.WriteAllText(wrapper, """
                    param([string]$Repository, [string]$ArgumentsFile, [string]$ParentObservation)
                    $ErrorActionPreference = 'Stop'
                    $tokens = @(Get-Content -Raw -LiteralPath $ArgumentsFile | ConvertFrom-Json)
                    $scriptPath = $tokens[3]
                    $parameters = @{}
                    for ($i = 4; $i -lt $tokens.Count; $i++) {
                        $name = ([string]$tokens[$i]).TrimStart('-')
                        if ($name -in @('NoSlot', 'NoBuild')) { $parameters[$name] = $true }
                        else { $parameters[$name] = $tokens[++$i] }
                    }
                    Push-Location -LiteralPath $Repository
                    try {
                        @{ native = [Environment]::CurrentDirectory; location = (Get-Location).Path } |
                            ConvertTo-Json -Compress | Set-Content -LiteralPath $ParentObservation
                        & $scriptPath @parameters
                        exit $LASTEXITCODE
                    } finally { Pop-Location }
                    """);
                var argumentsFile = Path.Combine(External, "arguments-" + round + ".json");
                await File.WriteAllTextAsync(argumentsFile, JsonSerializer.Serialize(args));
                args = ["-NoProfile", "-NonInteractive", "-File", wrapper, "-Repository", Repo,
                    "-ArgumentsFile", argumentsFile, "-ParentObservation", Path.Combine(External, "parent-" + round + ".json")];
            }
            var result = await RunAsync("pwsh", nativeCwd ?? Repo, args, environment);
            if (!Directory.Exists(resultRoot))
                throw new InvalidOperationException("Checkpoint produced no results: " + result.Output);
            var evidence = Directory.GetFiles(resultRoot, "source.json", SearchOption.AllDirectories).Single();
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(evidence));
            var lines = result.Output.Split('\n');
            var line = lines.Single(value => value.StartsWith("CHECKPOINT CP-2 commit=", StringComparison.Ordinal));
            return new Result(result.Exit, result.Output, line, evidence, json.RootElement.Clone());
        }

        public Task<(int Exit, string Output)> ValidateAsync(string evidence, string? expectedSha = null) =>
            RunAsync("pwsh", Repo, ["-NoProfile", "-NonInteractive", "-File",
                Path.Combine(ProjectRoot, "scripts", "validate-checkpoint-receipt.ps1"),
                "-Evidence", evidence, "-ExpectedSourceSha", expectedSha ?? Head], null);

        public Task<(int Exit, string Output)> CheckSourceHelperAsync(string evidence, string expectedSha)
        {
            static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
            var helper = Path.Combine(ProjectRoot, "scripts", "lib", "checkpoint-source.ps1");
            var command = ". " + Quote(helper) + "; $e = Get-Content -LiteralPath " + Quote(evidence) +
                " -Raw | ConvertFrom-Json; if (Test-CheckpointSourceEvidence $e " + Quote(expectedSha) +
                ") { exit 0 } else { exit 1 }";
            return RunAsync("pwsh", Repo, ["-NoProfile", "-NonInteractive", "-Command", command], null);
        }

        public void Dispose() => GitFixtureCleanup.Delete(Root);

        private static (int Exit, string Output) Run(string file, string cwd, IReadOnlyList<string> args) =>
            RunAsync(file, cwd, args, null).GetAwaiter().GetResult();

        private static async Task<(int Exit, string Output)> RunAsync(string file, string cwd,
            IReadOnlyList<string> args, IReadOnlyDictionary<string, string?>? environment)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(file) { WorkingDirectory = cwd,
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
            if (environment is not null)
                foreach (var (key, value) in environment) process.StartInfo.Environment[key] = value;
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try
            {
                await process.WaitForExitAsync(cancel.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                await Task.WhenAll(stdout, stderr);
                throw;
            }
            return (process.ExitCode, await stdout + await stderr);
        }
    }

    private sealed record Result(int Exit, string Output, string Line, string Evidence, JsonElement Source);
}
