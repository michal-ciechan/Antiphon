using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Timeout(600_000)]
[Category("Unit")]
public sealed class VolumeRetirementReceiptTests
{
    internal const string NativeLinuxReason = "CARD-1609: content retirement proofs require native Linux (bash, flock, real Git and POSIX mounts); Windows skips explicitly.";

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Preview_receipt_is_required_and_consumed()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var run = await fixture.Run("retire-temp-runner");
        var deleted = fixture.Removed.Any(name => name.StartsWith("antiphon-runner-temp_", StringComparison.Ordinal));
        var refused = run.Exit != 0 && !deleted &&
            run.Output.Contains("reason=preview-required", StringComparison.Ordinal);
        refused.ShouldBeTrue(
            "low-level apply without a Ready preview must not delete; base deleted the volumes. " +
            "exit=" + run.Exit + " removed=[" + string.Join(",", fixture.Removed) + "] out=" +
            VolumeRetirementFixture.Truncate(run.Output));
    }
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Receipt_shape_custody_and_digest_are_strict()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.ReadyPreview(fixture);
        var receipt = ReceiptPath(fixture);
        var original = await File.ReadAllTextAsync(receipt);
        foreach (var vector in new[] { "duplicate", "unknown", "truncated", "hardlink", "symlink", "writable", "digest", "flush", "flush-directory" })
        {
            await File.WriteAllTextAsync(receipt, original);
            var mutation = vector switch
            {
                "duplicate" => "sed -i 's/\"schema\":2/\"schema\":1,\"schema\":2/' \"$file\"",
                "unknown" => "jq -cS '.allowDelete=true' \"$file\" > \"$root/mutated\"; mv \"$root/mutated\" \"$file\"",
                "truncated" => "printf '{' > \"$file\"",
                "hardlink" => "ln \"$file\" \"$root/alias\"",
                "symlink" => "mv \"$file\" \"$root/real\"; ln -s \"$root/real\" \"$file\"",
                "writable" => "chmod 0777 \"$root\"",
                "digest" => "C1008_EXPECTED_PROOF_DIGEST=" + new string('0', 64),
                "flush" => "sync() { return 1; }",
                "flush-directory" => "sync() { [ ! -d \"${@: -1}\" ] || return 1; command sync \"$@\"; }",
                _ => throw new InvalidOperationException(vector)
            };
            var check = await CheckPreview(fixture, mutation);
            check.Exit.ShouldNotBe(0, "J2 must refuse " + vector + "; " + check.Output);
            fixture.Removed.ShouldBeEmpty();
            var restored = await VolumeRetirementFixture.Bash("rm -f -- " + VolumeRetirementFixture.Quote(receipt) + " " +
                VolumeRetirementFixture.Quote(Path.Combine(Path.GetDirectoryName(receipt)!, "alias")) + "; chmod 0700 " +
                VolumeRetirementFixture.Quote(Path.GetDirectoryName(receipt)!));
            restored.Exit.ShouldBe(0, restored.Output);
        }
        await File.WriteAllTextAsync(receipt, original);
        var valid = await CheckPreview(fixture);
        valid.Exit.ShouldBe(0, "J2 valid host receipt; " + valid.Output);
        JsonNode.Parse(await File.ReadAllTextAsync(receipt))!["consumed"]!.GetValue<bool>().ShouldBeTrue();
        (await CheckPreview(fixture)).Exit.ShouldNotBe(0, "J2 consumed receipt cannot authorize twice");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Volume_incarnation_and_operation_are_bound()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.ReadyPreview(fixture);
        var file = ReceiptPath(fixture);
        var original = await File.ReadAllTextAsync(file);
        foreach (var field in new[] { "Name", "CreatedAt", "Mountpoint", "Labels", "role", "operationId", "phaseNonce", "daemon", "inode" })
        {
            await File.WriteAllTextAsync(file, original);
            var mutation = field switch
            {
                "operationId" => "C1008_OPERATION=c100800000000000000000000000000000002",
                "phaseNonce" => "jq -cS '.phaseNonce=\"foreign\"' \"$file\" > \"$root/mutated\"; mv \"$root/mutated\" \"$file\"",
                "daemon" => "docker() { if [ \"$1\" = info ]; then printf 'other-daemon\\n'; else return 2; fi; }",
                "role" => "jq -cS '.targets[0]=\"antiphon-runner_work\"' \"$file\" > \"$root/mutated\"; mv \"$root/mutated\" \"$file\"",
                "inode" => "mount=\"$C1008_FIXTURE_ROOT/volumes/antiphon-runner-temp_work/_data\"; mv \"$mount\" \"$mount.old\"; mkdir \"$mount\"",
                "Labels" => "jq -cS '.volumes[\"antiphon-runner-temp_work\"].Labels[\"foreign\"]=\"owner\"' \"$C1008_FIXTURE_ROOT/docker.json\" > \"$root/docker-next\"; mv \"$root/docker-next\" \"$C1008_FIXTURE_ROOT/docker.json\"",
                _ => "jq -cS '.volumes[\"antiphon-runner-temp_work\"]." + field + "=\"replacement\"' \"$C1008_FIXTURE_ROOT/docker.json\" > \"$root/docker-next\"; mv \"$root/docker-next\" \"$C1008_FIXTURE_ROOT/docker.json\""
            };
            var check = await CheckPreview(fixture, mutation);
            check.Exit.ShouldNotBe(0, "J3 must refuse changed " + field + "; " + check.Output);
            await File.WriteAllTextAsync(fixture.StatePath, fixture.Docker.ToJsonString());
            if (field == "inode")
            {
                var mount = VolumeRetirementFixture.WorkMount(fixture);
                Directory.Delete(mount);
                Directory.Move(mount + ".old", mount);
                // Renaming changes ctime: a new legitimate preview is needed for
                // this restored incarnation, so keep the inode vector last.
            }
            fixture.Removed.ShouldBeEmpty();
        }
        await File.WriteAllTextAsync(file, original);
        // A second fixture supplies an unchanged positive control; the inode cut
        // above intentionally invalidates the first fixture's protected preview.
        using var unchanged = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.ReadyPreview(unchanged);
        var control = await CheckPreview(unchanged);
        control.Exit.ShouldBe(0, "J3 unchanged incarnations; " + control.Output);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Freshness_and_policy_version_are_rechecked()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.ReadyPreview(fixture);
        var file = ReceiptPath(fixture);
        var original = await File.ReadAllTextAsync(file);
        var ready = DateTimeOffset.Parse(JsonNode.Parse(original)!["readyAt"]!.GetValue<string>()).ToUnixTimeSeconds();
        foreach (var fakeNow in new[] { ready + 901, ready - 61 })
        {
            await File.WriteAllTextAsync(file, original);
            var check = await CheckPreview(fixture, $$"""
                date() {
                    if [ "$*" = '-u +%s' ]; then printf '%s\n' '{{fakeNow}}'; else command date "$@"; fi
                }
                """);
            check.Exit.ShouldNotBe(0, "J4 real immutable receipt expires or exceeds clock skew; " + check.Output);
        }
        foreach (var edit in new[] { ".policyVersion=99", ".schema=1" })
        {
            await File.WriteAllTextAsync(file, original);
            var check = await CheckPreview(fixture, "jq " + VolumeRetirementFixture.Quote(edit) +
                " \"$file\" > \"$root/mutated\"; mv \"$root/mutated\" \"$file\"; cp \"$file\" \"$root/preview.json\"");
            check.Exit.ShouldNotBe(0, "J4 stale/future/unsupported proof refuses " + edit + "; " + check.Output);
            fixture.Removed.ShouldBeEmpty();
        }
        await File.WriteAllTextAsync(file, original);
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(file)!, "preview.json"), original);
        (await CheckPreview(fixture)).Exit.ShouldBe(0, "J4 current compatible proof is usable");

        // Admission freshness is checked before the first expensive content
        // observation. Advancing the clock during that observation must not turn
        // the admitted Preview into an execution deadline.
        using var longApply = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.ReadyPreview(longApply);
        var longReady = JsonNode.Parse(await File.ReadAllTextAsync(ReceiptPath(longApply)))!;
        var admittedAt = DateTimeOffset.Parse(longReady["readyAt"]!.GetValue<string>()).ToUnixTimeSeconds();
        var beforeAudits = longApply.Trace.Count(args => args.Length > 0 && args[0] == "run" && args.Contains("--entrypoint"));
        var longRun = await longApply.Run("retire-temp-runner", LongAuditClock(admittedAt));
        longRun.Exit.ShouldBe(0, "J4 an unexpired Preview admits a long Apply through the real host entry: " + longRun.Output);
        longApply.Removed.Length.ShouldBe(4, "J4 admitted long Apply completes all four exact targets");
        JsonNode.Parse(await File.ReadAllTextAsync(ReceiptPath(longApply)))!["state"]!.GetValue<string>()
            .ShouldBe("Completed");
        (longApply.Trace.Count(args => args.Length > 0 && args[0] == "run" && args.Contains("--entrypoint")) - beforeAudits)
            .ShouldBe(1, "J4 an Apply with no intervening container effects inventories content once under the held lock");

        using var expiredEntry = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.ReadyPreview(expiredEntry);
        var expiredReady = JsonNode.Parse(await File.ReadAllTextAsync(ReceiptPath(expiredEntry)))!;
        var expiredAt = DateTimeOffset.Parse(expiredReady["readyAt"]!.GetValue<string>()).ToUnixTimeSeconds() + 901;
        var marker = Path.Combine(expiredEntry.Root, "audit-entered");
        var expiredRun = await expiredEntry.Run("retire-temp-runner", $$"""
            date() { if [ "$*" = '-u +%s' ]; then printf '%s\n' '{{expiredAt}}'; else command date "$@"; fi; }
            C1609_ADMITTED_PID="$BASHPID"; C1609_ADMITTED_AT={{expiredAt - 901}}
            C1609_ADMITTED_DIGEST="$(sha256sum {{VolumeRetirementFixture.Quote(ReceiptPath(expiredEntry))}} | cut -d' ' -f1)"
            eval "$(declare -f c1008_audit_checked | sed '1s/c1008_audit_checked/c1609_original_audit_checked/')"
            c1008_audit_checked() { touch {{VolumeRetirementFixture.Quote(marker)}}; c1609_original_audit_checked; }
            """);
        expiredRun.Exit.ShouldNotBe(0, "J4 an already expired Preview cannot admit Apply");
        expiredEntry.Removed.ShouldBeEmpty();
        File.Exists(marker).ShouldBeFalse("J4 expired admission refuses before any expensive Apply audit");

        using var uncertainClock = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.ReadyPreview(uncertainClock);
        var uncertainReady = JsonNode.Parse(await File.ReadAllTextAsync(ReceiptPath(uncertainClock)))!;
        var uncertainAt = DateTimeOffset.Parse(uncertainReady["readyAt"]!.GetValue<string>()).ToUnixTimeSeconds();
        var uncertain = await uncertainClock.Run("retire-temp-runner", LongAuditClock(uncertainAt, advanceMonotonic: false));
        uncertain.Exit.ShouldNotBe(0, "J4 wall-clock jumps without matching monotonic time cannot extend authority: " + uncertain.Output);
        uncertainClock.Removed.ShouldBeEmpty();
        JsonNode.Parse(await File.ReadAllTextAsync(ReceiptPath(uncertainClock)))!["consumed"]!.GetValue<bool>()
            .ShouldBeFalse("J4 uncertain clock leaves the Preview unconsumed");
    }

    private static string LongAuditClock(long admittedAt, bool advanceMonotonic = true) => $$"""
        printf '%s\n' '{{admittedAt}}' > "$C1008_FIXTURE_ROOT/phase-clock"
        date() {
            if [ "$*" = '-u +%s' ]; then cat "$C1008_FIXTURE_ROOT/phase-clock"; else command date "$@"; fi
        }
        eval "$(declare -f c1008_audit_checked | sed '1s/c1008_audit_checked/c1609_original_audit_checked/')"
        c1008_audit_checked() {
            c1609_original_audit_checked
            if [ '{{(advanceMonotonic ? "1" : "0")}}' = 1 ] && ! declare -F c1609_real_monotonic_ms >/dev/null; then
                eval "$(declare -f c1609_monotonic_ms | sed '1s/c1609_monotonic_ms/c1609_real_monotonic_ms/')"
                c1609_monotonic_ms() { local value; value="$(c1609_real_monotonic_ms)"; printf '%s\n' "$((value + 901000))"; }
            fi
            printf '%s\n' '{{admittedAt + 901}}' > "$C1008_FIXTURE_ROOT/phase-clock"
        }
        """;

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Legacy_receipts_are_history_only()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.ReadyPreview(fixture);
        var file = ReceiptPath(fixture);
        var original = await File.ReadAllTextAsync(file);
        foreach (var legacy in new[] { "{\"schema\":1,\"state\":\"Completed\"}", "{\"schema\":2,\"state\":\"Ready\",\"consumed\":false}", "PASS" })
        {
            await File.WriteAllTextAsync(file, legacy);
            var check = await CheckPreview(fixture);
            check.Exit.ShouldNotBe(0, "J9 historical or copied PASS is not Ready; " + check.Output);
            (await File.ReadAllTextAsync(file)).ShouldBe(legacy, "J9 never upgrade history to authorization");
        }
        await File.WriteAllTextAsync(file, original);
        var current = await CheckPreview(fixture,
            "mkdir -p \"$SERVER2_ROOT/temp-container-retirement\"; printf '{\"schema\":1,\"image\":\"conflict\"}' > \"$SERVER2_ROOT/temp-container-retirement/old.json\"");
        current.Exit.ShouldBe(0, current.Output);
        fixture.Removed.ShouldBeEmpty();
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Manual_attestation_is_exact_and_cannot_waive_findings()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.ReadyPreview(fixture);
        var receipt = ReceiptPath(fixture);
        var scripted = await File.ReadAllTextAsync(receipt);
        var missing = await CheckPreview(fixture, "C1008_MODE=manual");
        missing.Exit.ShouldNotBe(0, "J10 manual without attestation/commissioned authority must refuse before consuming Ready; " + missing.Output);
        (await File.ReadAllTextAsync(receipt)).ShouldBe(scripted);
        fixture.Removed.ShouldBeEmpty();

        var attestation = Path.Combine(fixture.Root, "manual.json");
        var grantPath = Path.Combine(fixture.Root, "server", "manual-authority", VolumeRetirementFixture.Operation + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(grantPath)!);
        var grant = new JsonObject
        {
            ["actor"] = "commissioned-fixture-delegate", ["authorityRef"] = "fixture-task-J10",
            ["expiresAt"] = DateTimeOffset.UtcNow.AddMinutes(15).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["issuedAt"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["operationId"] = VolumeRetirementFixture.Operation, ["schema"] = 1,
            ["targets"] = JsonNode.Parse(scripted)!["targets"]!.DeepClone()
        };
        await WriteAttestation(grantPath, grant);
        var intent = new JsonObject
        {
            ["schema"] = 1, ["mode"] = "manual", ["actor"] = "commissioned-fixture-delegate",
            ["authorityRef"] = "fixture-task-J10", ["reason"] = "exercise exact manual retirement",
            ["utc"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["operationId"] = VolumeRetirementFixture.Operation, ["phase"] = "Preview",
            ["targets"] = JsonNode.Parse(scripted)!["targets"]!.DeepClone(), ["previewDigest"] = null,
            ["fingerprints"] = JsonNode.Parse(scripted)!["fingerprints"]!.DeepClone()
        };
        await WriteAttestation(attestation, intent);
        var context = "C1008_MODE=manual\nC1008_MANUAL_AUTHORITY_FILE=" + VolumeRetirementFixture.Quote(grantPath) +
            "\nC1008_ATTESTATION_FILE=" + VolumeRetirementFixture.Quote(attestation);
        await VolumeRetirementFixture.ReadyPreview(fixture, context);
        var manual = await File.ReadAllTextAsync(receipt);
        JsonNode.Parse(manual)!["mode"]!.GetValue<string>().ShouldBe("manual", "J10 manual provenance survives the real writer");
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            await File.ReadAllBytesAsync(receipt))).ToLowerInvariant();
        intent["phase"] = "Apply";
        intent["previewDigest"] = digest;
        var valid = intent.ToJsonString();
        foreach (var vector in new[] { "missing", "actor", "authority", "digest", "operation", "target", "fingerprint", "reason", "stale", "mode", "unsigned" })
        {
            var changed = JsonNode.Parse(valid)!;
            var extra = context;
            switch (vector)
            {
                case "missing": extra += "\nunset C1008_ATTESTATION_FILE"; break;
                case "actor": changed["actor"] = "somebody-else"; break;
                case "authority": extra += "\nunset C1008_MANUAL_AUTHORITY_FILE"; break;
                case "digest": changed["previewDigest"] = new string('0', 64); break;
                case "operation": changed["operationId"] = "c1008" + new string('f', 32); break;
                case "target": changed["targets"]![0] = "antiphon-runner-cache-npm-content"; break;
                case "fingerprint": changed["fingerprints"]!["antiphon-runner-temp_work"]!["CreatedAt"] = "replacement"; break;
                case "reason": changed["reason"] = " "; break;
                case "stale": changed["utc"] = "2000-01-01T00:00:00Z"; break;
                case "mode": changed["mode"] = "scripted"; break;
                case "unsigned": changed["actor"] = "<actor>"; break;
            }
            await WriteAttestation(attestation, changed);
            var rejected = await CheckPreview(fixture, extra);
            rejected.Exit.ShouldNotBe(0, "J10 attestation " + vector + "; " + rejected.Output);
            (await File.ReadAllTextAsync(receipt)).ShouldBe(manual, "J10 refusal cannot rewrite Ready");
            fixture.Removed.ShouldBeEmpty();
        }
        await WriteAttestation(attestation, JsonNode.Parse(valid)!);
        var accepted = await fixture.Run("retire-temp-runner", context);
        accepted.Exit.ShouldBe(0, "J10 exact commissioned attestation consumes the same receipt; " + accepted.Output);
        JsonNode.Parse(await File.ReadAllTextAsync(receipt))!["applyAttestation"]!["actor"]!
            .GetValue<string>().ShouldBe("commissioned-fixture-delegate");
        fixture.Removed.Count(name => name.StartsWith("antiphon-runner-temp_", StringComparison.Ordinal)).ShouldBe(4);
        fixture.ReloadDocker();
        var completedManual = await File.ReadAllTextAsync(receipt);
        var unauthenticatedResume = await fixture.Run("retire-temp-runner", "C1008_RESUME=1");
        unauthenticatedResume.Exit.ShouldNotBe(0, "J10 scripted Resume cannot consume a manual operation; " + unauthenticatedResume.Output);
        (await File.ReadAllTextAsync(receipt)).ShouldBe(completedManual);
        fixture.ReloadDocker();
        var manualResume = await fixture.Run("retire-temp-runner", context + "\nC1008_RESUME=1");
        manualResume.Exit.ShouldBe(0, "J10 fresh commissioned authority may reobserve completed manual intents; " + manualResume.Output);
        fixture.ReloadDocker();
        fixture.Removed.Length.ShouldBe(4, "J10 manual resume never repeats a delete");
        using var unsafeFixture = new C1008HostFixture(main: false);
        var (_, repo) = await VolumeRetirementFixture.SeedPublishedOuter(unsafeFixture);
        await VolumeRetirementFixture.ReadyPreview(unsafeFixture);
        var unsafeReceipt = ReceiptPath(unsafeFixture);
        var unsafePreview = JsonNode.Parse(await File.ReadAllTextAsync(unsafeReceipt))!;
        var unsafeAttestation = Path.Combine(unsafeFixture.Root, "manual.json");
        var unsafeGrantPath = Path.Combine(unsafeFixture.Root, "server", "manual-authority", VolumeRetirementFixture.Operation + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(unsafeGrantPath)!);
        var unsafeGrant = grant.DeepClone();
        unsafeGrant["targets"] = unsafePreview["targets"]!.DeepClone();
        await WriteAttestation(unsafeGrantPath, unsafeGrant);
        var unsafeIntent = intent.DeepClone();
        unsafeIntent["phase"] = "Preview";
        unsafeIntent["previewDigest"] = null;
        unsafeIntent["targets"] = unsafePreview["targets"]!.DeepClone();
        unsafeIntent["fingerprints"] = unsafePreview["fingerprints"]!.DeepClone();
        await WriteAttestation(unsafeAttestation, unsafeIntent);
        var unsafeContext = "C1008_MODE=manual\nC1008_MANUAL_AUTHORITY_FILE=" + VolumeRetirementFixture.Quote(unsafeGrantPath) +
            "\nC1008_ATTESTATION_FILE=" + VolumeRetirementFixture.Quote(unsafeAttestation);
        await VolumeRetirementFixture.ReadyPreview(unsafeFixture, unsafeContext);
        var cleanManual = await File.ReadAllTextAsync(unsafeReceipt);
        await File.AppendAllTextAsync(Path.Combine(repo, "readme.txt"), "unpublished dirty bytes\n");
        // This method-scoped PC changes only the owned child's audit. The clean
        // authority control above prevents an authority refusal hiding dirty work.
        var bypass = Environment.GetEnvironmentVariable("C1609_J10_MANUAL_BYPASS") == "1"
            ? "\nc1008_audit_readonly() { printf 'repositories=1\\n'; }" : "";
        var refused = await unsafeFixture.Run("retire-temp-runner",
            unsafeContext + bypass, dryRun: true);
        refused.Exit.ShouldNotBe(0, "J10 manual authority cannot waive content findings; " + refused.Output);
        refused.Output.ShouldContain("RecycleWorktreeDirty");
        unsafeFixture.Removed.ShouldBeEmpty();
        (await File.ReadAllTextAsync(unsafeReceipt)).ShouldBe(cleanManual, "J10 unsafe manual preview preserves the prior Ready receipt");
        unsafeIntent["phase"] = "Apply";
        unsafeIntent["previewDigest"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            await File.ReadAllBytesAsync(unsafeReceipt))).ToLowerInvariant();
        await WriteAttestation(unsafeAttestation, unsafeIntent);
        var dirtyApply = await unsafeFixture.Run("retire-temp-runner", unsafeContext);
        dirtyApply.Exit.ShouldNotBe(0, "J10 exact Apply attestation cannot waive dirty content; " + dirtyApply.Output);
        dirtyApply.Output.ShouldContain("RecycleWorktreeDirty", customMessage: dirtyApply.Output);
        unsafeFixture.Removed.ShouldBeEmpty();
        JsonNode.Parse(await File.ReadAllTextAsync(unsafeReceipt))!["consumed"]!.GetValue<bool>()
            .ShouldBeFalse("J10 content refusal cannot consume Ready");
        var lifecycle = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(
            Path.GetDirectoryName(unsafeReceipt)!, "lifecycle.json")))!;
        lifecycle["active"]!.GetValue<bool>().ShouldBeFalse("J10 owning shell finishes its refusing phase");
        lifecycle["complete"]!.GetValue<bool>().ShouldBeTrue();
        lifecycle["children"]!.AsArray().All(child => child!["joined"]!.GetValue<bool>()).ShouldBeTrue();

        using var publicManual = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.ReadyPreview(publicManual);
        var entry = await RunEntryCases(publicManual.Root, "c1609-manual-cases.mjs", 180);
        entry.Exit.ShouldBe(0, "J10 public manual template, authenticated Preview/Apply and refusal controls: " + entry.Output);
        entry.Output.ShouldContain("C1609_MANUAL passed");
    }

    private static Task WriteAttestation(string path, JsonNode value)
    {
        JsonNode Sort(JsonNode node) => node switch
        {
            JsonObject obj => new JsonObject(obj.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value is null ? null : Sort(pair.Value)))),
            JsonArray array => new JsonArray(array.Select(item => item is null ? null : Sort(item)).ToArray()),
            _ => node.DeepClone()
        };
        return File.WriteAllTextAsync(path, Sort(value).ToJsonString(new System.Text.Json.JsonSerializerOptions
        { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
    }

    private static string ReceiptPath(C1008HostFixture fixture) =>
        Path.Combine(fixture.Root, "server", "retirement", VolumeRetirementFixture.Operation, "receipt.json");

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Changed_content_and_remote_proof_refuse()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var results = new List<string>();
        foreach (var vector in new[] { "published-ref", "dirty", "remote-loss", "unchanged" })
        {
            using var fixture = new C1008HostFixture(main: false);
            var (bare, repo) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
            await VolumeRetirementFixture.ReadyPreview(fixture);
            if (vector == "published-ref")
            {
                // The OID was already published: a new location still changes
                // the safety inventory and needs another preview.
                var edit = await VolumeRetirementFixture.Bash("git -C " + VolumeRetirementFixture.Quote(repo) +
                    " branch new-location HEAD");
                edit.Exit.ShouldBe(0, edit.Output);
            }
            if (vector == "dirty") await File.AppendAllTextAsync(Path.Combine(repo, "readme.txt"), "retained bytes");
            if (vector == "remote-loss") Directory.Move(bare, bare + ".unavailable");
            var applied = await fixture.Run("retire-temp-runner", VolumeRetirementFixture.ShellProbe);
            var safe = vector == "unchanged"
                ? applied.Exit == 0 && fixture.Removed.Length == 4
                : applied.Exit != 0 && fixture.Removed.Length == 0;
            if (!safe) results.Add(vector + " exit=" + applied.Exit + " removed=" + string.Join(",", fixture.Removed) +
                " out=" + VolumeRetirementFixture.Truncate(applied.Output));
        }
        results.ShouldBeEmpty("J5 fresh content/publication and exact preview inventory; " + string.Join(" | ", results));
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Every_delete_rechecks_full_live_guards()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var errors = new List<string>();
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c1008-retirement.sh");
        using var fixture = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.ReadyPreview(fixture);
        fixture.Docker["fault"] = "c1609-late-temp-busy";
        var applied = await fixture.Run("retire-temp-runner", VolumeRetirementFixture.ShellProbe);
        if (applied.Exit == 0 || !fixture.Removed.SequenceEqual(new[] { "antiphon-runner-temp_runner-state" }))
            errors.Add("between-delete busy guard: exit=" + applied.Exit + " removed=" + string.Join(",", fixture.Removed));
        fixture.Trace.Any(row => row.Contains("down") && row.Contains("-v")).ShouldBeFalse(
            "J6 a broad Compose removal cannot check the boundary between exact targets");
        foreach (var vector in new[] { "stable-fourth", "exhausted", "over-budget", "malformed", "bound" })
        {
            var run = await VolumeRetirementFixture.Bash($$"""
                set -euo pipefail
                source {{VolumeRetirementFixture.Quote(script)}}
                CASE_DIR={{VolumeRetirementFixture.Quote(Path.Combine(fixture.Root, "census"))}}
                mkdir -p "$CASE_DIR"
                : > "$CASE_DIR/c1609-census.jsonl"
                ticks=0; reads=0; sleeps=''
                c1609_monotonic_ms() { printf '%s\n' "$ticks"; }
                sleep() { sleeps="$sleeps $1"; ticks=$((ticks + $1 * 1000)); }
                c1008_tasks_collect_attempt() {
                    reads=$((reads + 1))
                    C1008_TASK_ERROR='RecycleTaskCensusUnknown cause=Unstable'
                    case '{{vector}}' in
                        stable-fourth) [ "$reads" -ne 4 ] || { C1008_TASK_ERROR=''; return 0; } ;;
                        over-budget) ticks=$((ticks + 100000)); [ "$reads" -ne 2 ] || { C1008_TASK_ERROR=''; return 0; } ;;
                        malformed) C1008_TASK_ERROR='RecycleTaskCensusUnknown cause=Malformed' ;;
                        bound) C1008_TASK_ERROR=RecycleLandInFlight ;;
                    esac
                    return 2
                }
                code=0; c1609_census_v2_loop || code=$?
                printf 'RESULT code=%s reads=%s sleeps=%s cause=%s\n' "$code" "$reads" "$sleeps" "$C1008_TASK_ERROR"
                jq -sc '{attempts:length,final:.[-1].cause}' "$CASE_DIR/c1609-census.jsonl"
                """);
            run.Exit.ShouldBe(0, "J6 census harness executes; " + run.Output);
            var expected = vector switch
            {
                "stable-fourth" => "code=0 reads=4 sleeps= 2 5 10",
                "exhausted" => "code=2 reads=4 sleeps= 2 5 10",
                "over-budget" => "code=2 reads=2 sleeps= 2",
                _ => "code=2 reads=1 sleeps="
            };
            if (!run.Output.Contains(expected, StringComparison.Ordinal)) errors.Add(vector + ": " + run.Output);
            var expectedAttempts = vector is "stable-fourth" or "exhausted" ? 4 : vector == "over-budget" ? 2 : 1;
            if (!run.Output.Contains("\"attempts\":" + expectedAttempts, StringComparison.Ordinal))
                errors.Add(vector + ": incomplete attempt journal: " + run.Output);
        }
        errors.ShouldBeEmpty("J6 effect-boundary guards, bounded retries and complete attempt journal; " + string.Join(" | ", errors));
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Journal_intents_and_partial_resume_are_exact()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        // The v1 absent-work adapter must never suppress a fresh v2 role
        // failure. Main retains its cached-audit recovery contract.
        foreach (var project in new[] { "antiphon-runner", "antiphon-runner-temp" })
        {
            var checkedAudit = await VolumeRetirementFixture.Bash($$"""
                set -euo pipefail
                source <(sed -n '/^c1008_audit_checked() {/,/^}/p' {{VolumeRetirementFixture.Quote(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"))}})
                C1008_PROJECT={{project}}; TEMP_PROJECT=antiphon-runner-temp; C1008_RESUME=1
                C1008_RECORD="$(jq -cn --arg name "$C1008_PROJECT"_work '{phase:"removing",volumes:{($name):{outcome:"pending"} },audit:"old-audit"}')"
                c1008_volume() { printf null; }
                c1008_audit() { printf 'audit check=metadata status=2 repo=. reason=role-unknown\n'; return 2; }
                c1008_save() { :; }
                c1008_refuse() { printf 'REFUSED %s\n' "$1"; exit 2; }
                c1008_audit_checked
                printf 'RESULT=%s\n' "$C1008_AUDIT"
                """);
            checkedAudit.Exit.ShouldBe(project == "antiphon-runner-temp" ? 2 : 0,
                "J8 temp resume must execute fresh role proof; main keeps its v1 adapter; " + checkedAudit.Output);
            checkedAudit.Output.ShouldContain(project == "antiphon-runner-temp" ? "reason=role-unknown" : "RESULT=old-audit");
        }
        var failures = new List<string>();
        foreach (var cut in new[] { "before-intent", "before-rm", "after-rm", "replacement", "unattributed-absence" })
        {
            using var fixture = new C1008HostFixture(main: false);
            await VolumeRetirementFixture.ReadyPreview(fixture);
            var hook = cut == "before-intent" || cut == "unattributed-absence"
                ? "c1609_target_intent() { return 1; }"
                : cut == "after-rm"
                    ? "c1609_target_result() { return 1; }"
                    : "docker() { if [ \"$1 $2\" = 'volume rm' ]; then return 1; fi; bash " +
                      VolumeRetirementFixture.Quote(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/fixtures/c1008-fake-docker.sh")) + " \"$@\"; }";
            var interrupted = await fixture.Run("retire-temp-runner", "c1609_load_retirement\n" + hook);
            interrupted.Exit.ShouldNotBe(0, "J8 injected persistence/effect cut " + cut + "; " + interrupted.Output);
            fixture.ReloadDocker();
            var record = JsonNode.Parse(await File.ReadAllTextAsync(ReceiptPath(fixture)))!;
            var intents = record["intents"]?.AsArray();
            if (cut is "before-intent" or "unattributed-absence")
                (intents?.Count ?? 0).ShouldBe(0, "J8 a failed durable intent cannot authorize removal");
            else
            {
                intents!.Count.ShouldBe(1);
                intents[0]!["name"]!.GetValue<string>().ShouldBe("antiphon-runner-temp_runner-state");
                intents[0]!["result"]!.GetValue<string>().ShouldBe("pending");
            }
            fixture.Removed.Length.ShouldBe(cut == "after-rm" ? 1 : 0);
            if (cut == "replacement")
                fixture.Docker["volumes"]!["antiphon-runner-temp_runner-state"]!["CreatedAt"] = "2026-10-30T00:00:00Z";
            if (cut == "unattributed-absence")
                fixture.Docker["volumes"]!.AsObject().Remove("antiphon-runner-temp_runner-state");
            var resumed = await fixture.Run("retire-temp-runner", "C1008_RESUME=1");
            if (cut is "replacement" or "unattributed-absence")
            {
                if (resumed.Exit == 0 || fixture.Removed.Length != 0)
                    failures.Add(cut + " must refuse without another effect: " + resumed.Output);
                continue;
            }
            if (resumed.Exit != 0 || fixture.Removed.Distinct().Count() != 4 || fixture.Removed.Length != 4)
            {
                failures.Add(cut + " must reprove and finish exactly once: " + resumed.Output);
                continue;
            }
            record = JsonNode.Parse(await File.ReadAllTextAsync(ReceiptPath(fixture)))!;
            record["state"]!.GetValue<string>().ShouldBe("Completed");
            record["intents"]!.AsArray().Count.ShouldBe(4);
            record["effects"]!.AsArray().Count.ShouldBe(4);
            record["intents"]!.AsArray().All(row => row!["result"]!.GetValue<string>() is "removed" or "removed-observed").ShouldBeTrue();
            fixture.ReloadDocker();
            var again = await fixture.Run("retire-temp-runner", "C1008_RESUME=1");
            again.Exit.ShouldBe(0, "J8 final receipt copy/replay does not repeat deletions; " + again.Output);
            fixture.Removed.Length.ShouldBe(4);
        }
        failures.ShouldBeEmpty("J8 crash/resume safety; " + string.Join(" | ", failures));
        using var closeFixture = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.ReadyPreview(closeFixture,
            "C1008_OPERATION=c100800000000000000000000000000000002");
        await VolumeRetirementFixture.ReadyPreview(closeFixture);
        var close = await RunCloseCases(closeFixture.Root);
        close.Exit.ShouldBe(0, "J8 actual public Close, same host locks and complete census: " + close.Output);
        close.Output.ShouldContain("C1609_CLOSE passed");
    }

    private static async Task<(int Exit, string Output)> RunCloseCases(string root)
        => await RunEntryCases(root, "c1609-close-cases.mjs", 120);

    private static async Task<(int Exit, string Output)> RunEntryCases(string root, string driver, int seconds)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("node")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = DelegateScriptRunner.RepoRoot
        };
        psi.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/fixtures", driver));
        psi.ArgumentList.Add(root);
        using var timing = TestTiming.BeginProcess("node", psi.ArgumentList.Count, "cli");
        using var child = System.Diagnostics.Process.Start(psi)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        // New Close matrix: bounded initial collection, not a change to the
        // existing crash/resume drivers' 30s bounds. Per-call phases are retained.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        try { await child.WaitForExitAsync(deadline.Token); }
        catch { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } throw; }
        return (child.ExitCode, await stdout + await stderr);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Lock_order_excludes_supported_writers()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var pc = Environment.GetEnvironmentVariable("C1609_J7_SKIP_ROLLOUT_LOCK") == "1";
        Console.WriteLine("J7 skip-rollout positive control=" + pc);
        using var holder = new C1008HostFixture(main: false);
        var held = Path.Combine(holder.Root, "held");
        var release = Path.Combine(holder.Root, "release");
        var first = holder.Run("retire-temp-runner", $$"""
            eval "$C1008_CACHE_LOCK_SOURCE_BODY"
            c1008_status_proof() {
                touch {{VolumeRetirementFixture.Quote(held)}}
                while [ ! -f {{VolumeRetirementFixture.Quote(release)}} ]; do sleep 0.02; done
                write_result true '' 0
            }
            """, dryRun: true);
        var fixtures = new List<C1008HostFixture>();
        var runs = new List<Task<(int Exit, string Output)>>();
        try
        {
            await WaitForBarrier(held, first);
            foreach (var entry in new[] { "deploy-temp-runner", "preview", "resume" })
            {
                var contender = new C1008HostFixture(main: false);
                fixtures.Add(contender);
                var requested = Path.Combine(contender.Root, "requested");
                var acquired = Path.Combine(contender.Root, "acquired");
                var extra = $$"""
                    SERVER2_ROOT={{VolumeRetirementFixture.Quote(Path.Combine(holder.Root, "server"))}}
                    eval "$C1008_CACHE_LOCK_SOURCE_BODY"
                    flock() {
                        printf '%s\n' "$3" >> {{VolumeRetirementFixture.Quote(requested)}}
                        command flock "$@" || return $?
                        printf '%s\n' "$3" >> {{VolumeRetirementFixture.Quote(acquired)}}
                    }
                    c1008_status_proof() { write_result true '' 0; }
                    c1609_temp_retirement_clearable() { write_result true '' 0; }
                    """;
                if (entry == "resume") extra += "\nC1008_RESUME=1";
                // Test-only method-scoped PC. Production source and the shared
                // fixture remain unchanged; only this owned child loses FD 8.
                if (pc) extra += "\nc1008_rollout_lock() { :; }";
                var run = contender.Run(entry == "preview" || entry == "resume" ? "retire-temp-runner" : entry,
                    extra, dryRun: entry == "preview");
                runs.Add(run);
                await WaitForBarrier(requested, run);
                File.ReadAllLines(requested).ShouldBe(new[] { "8" }, "J7 rollout precedes cache: " + entry);
                File.Exists(acquired).ShouldBeFalse("J7 supported writer waits behind the held rollout: " + entry);
                contender.Trace.ShouldBeEmpty("J7 no Docker read or effect before admission: " + entry);
            }
            using var timed = new C1008HostFixture(main: false);
            var timeout = await timed.Run("deploy-temp-runner", $$"""
                SERVER2_ROOT={{VolumeRetirementFixture.Quote(Path.Combine(holder.Root, "server"))}}
                flock() { command flock -w 0.1 "$3"; }
                """);
            timeout.Exit.ShouldBe(2, "J7 real flock timeout; " + timeout.Output);
            timeout.Output.ShouldContain("RecycleRolloutBusy");
            timed.Trace.ShouldBeEmpty("J7 timeout has no Docker calls");
            timed.Removed.ShouldBeEmpty();
        }
        finally
        {
            File.WriteAllText(release, "release");
            var settled = await first;
            // Await all supported writers even when the positive control fails.
            try { await Task.WhenAll(runs); }
            finally { foreach (var fixture in fixtures) fixture.Dispose(); }
            settled.Exit.ShouldBe(0, "J7 holder joined; " + settled.Output);
        }

        // A cache-only holder never needs rollout. A waiter may own rollout
        // while waiting on cache; releasing cache must let that waiter finish.
        using var cache = new C1008HostFixture(main: false);
        using var waiting = new C1008HostFixture(main: false);
        var cacheHeld = Path.Combine(cache.Root, "cache-held");
        var cacheRelease = Path.Combine(cache.Root, "cache-release");
        var requestedCache = Path.Combine(waiting.Root, "cache-requested");
        var acquiredCache = Path.Combine(waiting.Root, "cache-acquired");
        var cacheRun = cache.Run("retire-temp-runner", $$"""
            eval "$C1008_CACHE_LOCK_SOURCE_BODY"
            LANE=host
            c849_lock
            touch {{VolumeRetirementFixture.Quote(cacheHeld)}}
            while [ ! -f {{VolumeRetirementFixture.Quote(cacheRelease)}} ]; do sleep 0.02; done
            write_result true '' 0
            """);
        Task<(int Exit, string Output)>? waitingRun = null;
        try
        {
            await WaitForBarrier(cacheHeld, cacheRun);
            waitingRun = waiting.Run("retire-temp-runner", $$"""
                SERVER2_ROOT={{VolumeRetirementFixture.Quote(Path.Combine(cache.Root, "server"))}}
                eval "$C1008_CACHE_LOCK_SOURCE_BODY"
                flock() {
                    command flock "$@" || return $?
                    printf '%s\n' "$3" >> {{VolumeRetirementFixture.Quote(acquiredCache)}}
                }
                original_cache_lock=$(declare -f c849_lock)
                eval "$(printf '%s' "$original_cache_lock" | sed 's/^c849_lock/c849_lock_original/')"
                c849_lock() { touch {{VolumeRetirementFixture.Quote(requestedCache)}}; c849_lock_original; }
                c1008_status_proof() { write_result true '' 0; }
                """, dryRun: true);
            await WaitForBarrier(requestedCache, waitingRun);
            File.ReadAllLines(acquiredCache).ShouldBe(new[] { "8" }, "J7 waiter owns rollout and waits only for cache");
            waitingRun.IsCompleted.ShouldBeFalse("J7 cache barrier holds the waiter");
        }
        finally
        {
            File.WriteAllText(cacheRelease, "release");
            var settled = await cacheRun;
            var resumed = waitingRun is null ? (Exit: 0, Output: "not started") : await waitingRun;
            settled.Exit.ShouldBe(0, "J7 cache-only holder joined; " + settled.Output);
            resumed.Exit.ShouldBe(0, "J7 no cache-to-rollout inversion; " + resumed.Output);
        }
    }

    private static async Task WaitForBarrier(string path, Task<(int Exit, string Output)> run)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!File.Exists(path) && !run.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(15))
            await Task.Delay(20);
        File.Exists(path).ShouldBeTrue("J7 real flock barrier: " + Path.GetFileName(path) +
            (run.IsCompleted ? "; " + (await run).Output : "; child still waiting"));
    }

    private static async Task<(int Exit, string Output)> CheckPreview(C1008HostFixture fixture, string mutation = "")
    {
        return await VolumeRetirementFixture.Bash($$"""
            set -euo pipefail
            C1008_FIXTURE_ROOT={{VolumeRetirementFixture.Quote(fixture.Root)}}; export C1008_FIXTURE_ROOT
            SERVER2_ROOT="$C1008_FIXTURE_ROOT/server"
            C1008_OPERATION={{VolumeRetirementFixture.Operation}}
            C1008_PROJECT=antiphon-runner-temp
            C1008_RETIRED_AT=2026-10-03T09:30:00Z
            C1008_TARGETS=(antiphon-runner-temp_work antiphon-runner-temp_runner-tmp antiphon-runner-temp_dind-data antiphon-runner-temp_runner-state)
            CASE_DIR="$C1008_FIXTURE_ROOT/evidence/retire-temp-runner"
            docker() { bash {{VolumeRetirementFixture.Quote(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/fixtures/c1008-fake-docker.sh"))}} "$@"; }
            sudo() { [ "$1" = -n ] && shift; "$@"; }
            c1008_volume() { docker volume inspect "$1" | jq -c '.[0]'; }
            source {{VolumeRetirementFixture.Quote(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c1008-retirement.sh"))}}
            root="$SERVER2_ROOT/retirement/$C1008_OPERATION"; file="$root/receipt.json"
            {{mutation}}
            c1609_require_ready_preview
            """);
    }
}
