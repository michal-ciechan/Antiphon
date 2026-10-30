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
        foreach (var vector in new[] { "duplicate", "unknown", "truncated", "hardlink", "symlink", "writable", "digest", "flush" })
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
    }

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

    private static string ReceiptPath(C1008HostFixture fixture) =>
        Path.Combine(fixture.Root, "server", "retirement", VolumeRetirementFixture.Operation, "receipt.json");

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
