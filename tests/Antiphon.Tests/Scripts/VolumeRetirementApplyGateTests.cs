using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1609. Production volume deletion stays off until the operator names this
// operation. The token is enabled-<operation id>, so a leftover shell value from
// an earlier rollout cannot authorize a new one. Resume repeats the same id.
[Timeout(600_000)]
[Category("Unit")]
public sealed class VolumeRetirementApplyGateTests
{
    private const string EnabledTypo = "enabled";
    private const string WrongOperation = "c100800000000000000000000000000000099";

    [Test]
    public void Apply_gate_is_checked_before_every_volume_removal()
    {
        var remote = Read("scripts/c590-remote.sh");
        Count(remote, "docker volume rm --").ShouldBe(1,
            "apply-gate: retirement has one volume-rm statement, and it takes the -- form");
        var recycle = Function(remote, "c1008_recycle");
        const string earlyGate = """
            if [ "${C1008_DRY_RUN:-0}" != 1 ]; then
                c1609_apply_gate
            fi
        """;
        recycle.ShouldContain(earlyGate, Case.Sensitive,
            "apply-gate: a non-dry-run entry calls the gate once, immediately after context validation");
        Count(recycle, "c1609_apply_gate").ShouldBe(1,
            "apply-gate: c1008_recycle has one gate call, so a later copy cannot authorize deletion");
        var gate = recycle.IndexOf("c1609_apply_gate", StringComparison.Ordinal);
        var removal = recycle.IndexOf("docker volume rm --", StringComparison.Ordinal);
        var stop = recycle.IndexOf("docker stop --time", StringComparison.Ordinal);
        var containerRm = recycle.IndexOf("docker rm --", StringComparison.Ordinal);
        var lockCall = recycle.IndexOf("\n        c1008_lock\n", StringComparison.Ordinal);
        var previewUse = recycle.IndexOf("c1609_require_ready_preview", StringComparison.Ordinal);
        var protectedCall = recycle.IndexOf("c1609_refuse_protected_targets", StringComparison.Ordinal);
        (gate >= 0 && stop > gate && containerRm > gate && lockCall > gate && previewUse > gate &&
            protectedCall > gate && removal > gate).ShouldBeTrue(
            "apply-gate: the gate and the protected-name refusal precede the lock, preview consume, stop, rm, and volume rm");
        var gateBody = Function(remote, "c1609_apply_gate");
        gateBody.ShouldContain("write_result false 'apply-gate-off' 2", Case.Sensitive);
        gateBody.ShouldContain("enabled-${C1008_OPERATION}", Case.Sensitive);
        gateBody.ShouldNotContain("c1008_refuse", Case.Sensitive);
        gateBody.ShouldNotContain("docker volume rm", Case.Sensitive);

        Function(remote, "case_retire_temp_runner").ShouldContain("c1008_recycle \"$TEMP_PROJECT\"", Case.Sensitive);
        Function(remote, "case_deploy_parent").ShouldContain("c1008_recycle \"$HOST_PROJECT\"", Case.Sensitive);
        remote.ShouldContain("retire-temp-runner) case_retire_temp_runner", Case.Sensitive);
        // Dry-run returns inside c1008_recycle before this call. Resume and manual are
        // modes of the same function, so they reach the same statement.
        remote.ShouldContain("deploy-parent) c1008_recycle \"$HOST_PROJECT\"", Case.Sensitive);
        remote.ShouldContain("retire-temp-runner)", Case.Sensitive);

        // c590 leftovers remove only ^c590[0-9a-f]{12}_ volumes. That statement is not
        // the retirement apply loop and stays outside the gate.
        var leftovers = Function(remote, "retire_c590_leftovers");
        leftovers.ShouldContain("docker volume rm ", Case.Sensitive);
        leftovers.ShouldNotContain("docker volume rm --", Case.Sensitive);
        leftovers.ShouldNotContain("c1609_apply_gate", Case.Sensitive);

        var bridge = Read("scripts/c590-real.ps1");
        bridge.ShouldContain("^enabled-c1008[0-9a-f]{32}$", Case.Sensitive);
        bridge.ShouldContain("export ANTIPHON_VOLUME_RETIREMENT_APPLY='$applyGate'", Case.Sensitive);
        Read("scripts/deploy-server2.ps1").ShouldContain("Invoke-HostCase -Case 'retire-temp-runner'", Case.Sensitive);
        Read("scripts/c1609-manual.ps1").ShouldContain("-Case retire-temp-runner", Case.Sensitive);

        var runbook = Read("docs/docker-stack.md");
        var gateRow = runbook.Split('\n').Single(line => line.StartsWith("| 9. Retire temp |", StringComparison.Ordinal));
        var preview = gateRow.IndexOf("-Phase retire-temp -DryRun", StringComparison.Ordinal);
        var enabled = gateRow.IndexOf("-RetirementProof <operation-id>", StringComparison.Ordinal);
        (preview >= 0 && enabled > preview).ShouldBeTrue("apply-gate: gate 9 previews before the enabled second rollout");
        foreach (var pin in new[]
                 {
                     "Apply is off by default", "including already-absent cleanup", "apply-gate-off",
                     "ANTIPHON_VOLUME_RETIREMENT_APPLY", "enabled-<operation-id>", "exit 2"
                 })
            gateRow.ShouldContain(pin, Case.Sensitive, "apply-gate: " + pin);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Scripted_apply_refuses_until_the_operation_token_matches()
    {
        C1008HostFixture.RequireNativeLinux();
        using (var host = new C1008HostFixture())
        {
            var blocked = await host.Run("deploy-parent", "C1609_FIXTURE_APPLY_GATE=off");
            blocked.Exit.ShouldBe(2, "apply-gate: host apply is off; " + blocked.Output);
            blocked.Output.ShouldContain("DIAGNOSIS=apply-gate-off", Case.Sensitive);
            AssertNoVolumeRemoval(host, "host apply off");
            AssertNoContainerStop(host, "host apply off");
            File.Exists(Journal(host)).ShouldBeFalse("apply-gate: host apply off writes no recycle journal");
            Directory.Exists(Path.Combine(host.Root, "server", "recycle"))
                .ShouldBeFalse("apply-gate: host apply off writes no journal directory");
        }

        await RefuseTemp(null);
        await RefuseTemp(EnabledTypo);
        await RefuseTemp("1");
        await RefuseTemp("true");
        await RefuseTemp("enabled-" + WrongOperation);

        using var enabled = new C1008HostFixture(main: false);
        await Preview(enabled);
        var applied = await enabled.Run("retire-temp-runner", EnabledToken);
        applied.Exit.ShouldBe(0, "apply-gate: the matching operation token deletes; " + applied.Output);
        CountRemoved(enabled).ShouldBe(4, "apply-gate: the matching token deletes the four temp volumes");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Resume_refuses_until_the_operation_token_matches()
    {
        C1008HostFixture.RequireNativeLinux();
        using var fixture = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.ReadyPreview(fixture);
        // Load first. A later source of c1008-retirement.sh would replace this
        // hook with the real intent writer, and the cut would delete.
        var interrupted = await fixture.Run("retire-temp-runner",
            "c1609_load_retirement\n" + EnabledToken + "\nc1609_target_intent() { return 1; }");
        interrupted.Exit.ShouldNotBe(0, "apply-gate: the intent cut still fails closed; " + interrupted.Output);
        AssertNoVolumeRemoval(fixture, "resume setup");
        fixture.ReloadDocker();
        var journalBefore = await File.ReadAllBytesAsync(Journal(fixture));
        var traceBefore = fixture.Trace.Length;

        var blocked = await fixture.Run("retire-temp-runner", "C1008_RESUME=1\nC1609_FIXTURE_APPLY_GATE=off");
        blocked.Exit.ShouldBe(2, "apply-gate: resume is off; " + blocked.Output);
        blocked.Output.ShouldContain("DIAGNOSIS=apply-gate-off", Case.Sensitive);
        AssertNoVolumeRemoval(fixture, "resume off");
        AssertNoNewContainerStop(fixture, traceBefore, "resume off");
        (await File.ReadAllBytesAsync(Journal(fixture))).ShouldBe(journalBefore,
            "apply-gate: resume off does not rewrite the journal");
        fixture.ReloadDocker();

        var resumed = await fixture.Run("retire-temp-runner", "C1008_RESUME=1\n" + EnabledToken);
        resumed.Exit.ShouldBe(0, "apply-gate: resume with the same operation token finishes; " + resumed.Output);
        CountRemoved(fixture).ShouldBe(4, "apply-gate: resume deletes the four temp volumes once");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Manual_apply_refuses_until_the_operation_token_matches()
    {
        C1008HostFixture.RequireNativeLinux();
        using (var blockedFixture = new C1008HostFixture(main: false))
        {
            var context = await ManualApplyContext(blockedFixture);
            var blocked = await blockedFixture.Run("retire-temp-runner", context + "\nC1609_FIXTURE_APPLY_GATE=off");
            blocked.Exit.ShouldBe(2, "apply-gate: manual apply is off; " + blocked.Output);
            blocked.Output.ShouldContain("DIAGNOSIS=apply-gate-off", Case.Sensitive);
            AssertNoVolumeRemoval(blockedFixture, "manual off");
        }

        using var enabled = new C1008HostFixture(main: false);
        var enabledContext = await ManualApplyContext(enabled);
        var applied = await enabled.Run("retire-temp-runner", enabledContext + "\n" + EnabledToken);
        applied.Exit.ShouldBe(0, "apply-gate: manual apply with the operation token deletes; " + applied.Output);
        CountRemoved(enabled).ShouldBe(4, "apply-gate: manual apply deletes the four temp volumes");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Refused_apply_keeps_the_ready_preview()
    {
        C1008HostFixture.RequireNativeLinux();
        using var fixture = new C1008HostFixture(main: false);
        await Preview(fixture);
        var receiptPath = Path.Combine(fixture.Root, "server", "retirement", VolumeRetirementFixture.Operation, "receipt.json");
        var before = await File.ReadAllTextAsync(receiptPath);
        var blocked = await fixture.Run("retire-temp-runner", "C1609_FIXTURE_APPLY_GATE=off");
        blocked.Exit.ShouldBe(2, "apply-gate: a refused apply stays off; " + blocked.Output);
        blocked.Output.ShouldContain("DIAGNOSIS=apply-gate-off", Case.Sensitive);
        AssertNoVolumeRemoval(fixture, "refused apply");
        AssertNoContainerStop(fixture, "refused apply");
        File.Exists(Journal(fixture)).ShouldBeFalse("apply-gate: a refused apply writes no recycle journal");
        (await File.ReadAllTextAsync(receiptPath)).ShouldBe(before,
            "apply-gate: a refused apply does not consume the Ready preview");
        var receipt = JsonNode.Parse(before)!;
        receipt["consumed"]!.GetValue<bool>().ShouldBeFalse();
        receipt["state"]!.GetValue<string>().ShouldBe("Ready");

        fixture.ReloadDocker();
        var applied = await fixture.Run("retire-temp-runner", EnabledToken);
        applied.Exit.ShouldBe(0, "apply-gate: the same Ready preview still authorizes the matching token; " + applied.Output);
        CountRemoved(fixture).ShouldBe(4, "apply-gate: the token apply deletes the four temp volumes");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task State_volume_named_in_the_plan_is_refused_before_delete()
    {
        C1008HostFixture.RequireNativeLinux();
        using var fixture = new C1008HostFixture();
        var run = await fixture.Run("deploy-parent", sourceTransform: source => source.Replace(
            "C1008_TARGETS=(\"${C1008_PROJECT}_work\" \"${C1008_PROJECT}_runner-tmp\" \"${C1008_PROJECT}_dind-data\")",
            "C1008_TARGETS=(\"${C1008_PROJECT}_work\" \"${C1008_PROJECT}_runner-state\" \"${C1008_PROJECT}_runner-tmp\" \"${C1008_PROJECT}_dind-data\")",
            StringComparison.Ordinal));
        run.Exit.ShouldBe(2, "apply-gate: a state volume in the plan refuses; " + run.Output);
        run.Output.ShouldContain("DIAGNOSIS=RecycleProtectedVolume", Case.Sensitive);
        AssertNoVolumeRemoval(fixture, "protected plan");
        AssertNoContainerStop(fixture, "protected plan");
        File.Exists(Journal(fixture)).ShouldBeFalse("apply-gate: a protected-name refusal writes no journal");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Recheck_target_refuses_a_changed_fingerprint()
    {
        C1008HostFixture.RequireNativeLinux();
        using var fixture = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.ReadyPreview(fixture);
        var unchanged = await Recheck(fixture, "");
        unchanged.Exit.ShouldBe(0, "apply-gate: an unchanged target passes recheck; " + unchanged.Output);
        var changed = await Recheck(fixture,
            "jq -cS '.volumes[\"antiphon-runner-temp_work\"].CreatedAt=\"replacement\"' \"$C1008_FIXTURE_ROOT/docker.json\" > \"$root/docker-next\"\n" +
            "mv \"$root/docker-next\" \"$C1008_FIXTURE_ROOT/docker.json\"");
        changed.Exit.ShouldNotBe(0, "apply-gate: recheck refuses a fingerprint that changed after the preview; " + changed.Output);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Redeploy_old_without_switch_skips_recycle()
    {
        using var drifted = new C1008WrapperFixture();
        drifted.MainDrained();
        drifted.State["allowClear"] = true;
        var run = await drifted.Run("redeploy-old");
        run.Exit.ShouldBe(0, "apply-gate: default redeploy-old leaves the old runner up; " + run.Output);
        run.Output.ShouldContain("C1008_RECYCLE skipped=redeploy-old reason=not-requested", Case.Sensitive);
        var parent = run.Trace.Single(row => row["kind"]?.GetValue<string>() == "case" &&
            row["name"]?.GetValue<string>() == "deploy-parent");
        var recycle = parent["recycle"];
        (recycle is null || recycle.GetValueKind() == JsonValueKind.Null).ShouldBeTrue(
            "apply-gate: default redeploy-old passes no recycle context; " + parent.ToJsonString());
    }

    private const string EnabledToken = "export ANTIPHON_VOLUME_RETIREMENT_APPLY=\"enabled-${C1008_OPERATION}\"";

    private static async Task RefuseTemp(string? token)
    {
        using var fixture = new C1008HostFixture(main: false);
        var preview = await Preview(fixture);
        var extra = token is null
            ? "C1609_FIXTURE_APPLY_GATE=off"
            : "export ANTIPHON_VOLUME_RETIREMENT_APPLY=" + VolumeRetirementFixture.Quote(token);
        var applied = await fixture.Run("retire-temp-runner", extra);
        var label = token ?? "off";
        applied.Exit.ShouldBe(2, "apply-gate: " + label + "; " + applied.Output);
        applied.Output.ShouldContain("DIAGNOSIS=apply-gate-off", Case.Sensitive);
        preview.Output.ShouldNotContain("DIAGNOSIS=apply-gate-off", Case.Sensitive, "apply-gate: preview is not an apply");
        AssertNoVolumeRemoval(fixture, label);
    }

    private static async Task<(int Exit, string Output)> Preview(C1008HostFixture fixture)
    {
        var preview = await fixture.Run("retire-temp-runner", dryRun: true);
        preview.Exit.ShouldBe(0, "apply-gate: preview stays available; " + preview.Output);
        preview.Output.ShouldContain("C1609_RETIREMENT state=Ready", Case.Sensitive);
        var names = preview.Output.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("C1008_PREVIEW remove=", StringComparison.Ordinal))
            .Select(line => line["C1008_PREVIEW remove=".Length..])
            .ToArray();
        names.Length.ShouldBe(4, "apply-gate: preview names the four temp volumes");
        preview.Output.ShouldContain("would delete: " + string.Join(' ', names), Case.Sensitive);
        fixture.Removed.ShouldBeEmpty("apply-gate: preview removes nothing");
        return preview;
    }

    private static void AssertNoVolumeRemoval(C1008HostFixture fixture, string label)
    {
        fixture.Removed.ShouldBeEmpty("apply-gate: " + label + " calls no volume rm");
        fixture.Trace.Any(row => row.Length >= 2 && row[0] == "volume" && row[1] == "rm")
            .ShouldBeFalse("apply-gate: " + label + " trace has no volume rm");
    }

    private static void AssertNoContainerStop(C1008HostFixture fixture, string label) =>
        AssertNoNewContainerStop(fixture, 0, label);

    private static void AssertNoNewContainerStop(C1008HostFixture fixture, int traceStart, string label)
    {
        fixture.Trace.Skip(traceStart).Any(row => row.Length > 0 && (row[0] == "stop" || row[0] == "rm"))
            .ShouldBeFalse("apply-gate: " + label + " trace has no container stop or rm");
    }

    private static string Journal(C1008HostFixture fixture) =>
        Path.Combine(fixture.Root, "server", "recycle", VolumeRetirementFixture.Operation + ".json");

    private static Task<(int Exit, string Output)> Recheck(C1008HostFixture fixture, string mutation)
    {
        var root = Path.Combine(fixture.Root, "server", "retirement", VolumeRetirementFixture.Operation);
        return VolumeRetirementFixture.Bash($$"""
            set -euo pipefail
            C1008_FIXTURE_ROOT={{VolumeRetirementFixture.Quote(fixture.Root)}}; export C1008_FIXTURE_ROOT
            SERVER2_ROOT="$C1008_FIXTURE_ROOT/server"
            C1008_OPERATION={{VolumeRetirementFixture.Operation}}
            C1008_PROJECT=antiphon-runner-temp
            C1008_PROJECT_ID=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1
            C1008_RETIRED_AT=2026-10-03T09:30:00Z
            C1008_TARGETS=(antiphon-runner-temp_work antiphon-runner-temp_runner-tmp antiphon-runner-temp_dind-data antiphon-runner-temp_runner-state)
            root={{VolumeRetirementFixture.Quote(root)}}
            docker() { bash {{VolumeRetirementFixture.Quote(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/fixtures/c1008-fake-docker.sh"))}} "$@"; }
            sudo() { [ "$1" = -n ] && shift; "$@"; }
            c1008_volume() { docker volume inspect "$1" | jq -c '.[0]'; }
            source {{VolumeRetirementFixture.Quote(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c1008-retirement.sh"))}}
            {{mutation}}
            c1609_recheck_target antiphon-runner-temp_work
            """);
    }

    private static int CountRemoved(C1008HostFixture fixture) =>
        fixture.Removed.Count(name => name.StartsWith("antiphon-runner-temp_", StringComparison.Ordinal));

    private static async Task<string> ManualApplyContext(C1008HostFixture fixture)
    {
        await VolumeRetirementFixture.ReadyPreview(fixture);
        var receipt = Path.Combine(fixture.Root, "server", "retirement", VolumeRetirementFixture.Operation, "receipt.json");
        var scripted = JsonNode.Parse(await File.ReadAllTextAsync(receipt))!;
        var grantPath = Path.Combine(fixture.Root, "server", "manual-authority", VolumeRetirementFixture.Operation + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(grantPath)!);
        var now = DateTimeOffset.UtcNow;
        var stamp = now.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var grant = new JsonObject
        {
            ["actor"] = "commissioned-fixture-delegate",
            ["authorityRef"] = "fixture-task-apply-gate",
            ["expiresAt"] = now.AddMinutes(15).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["issuedAt"] = stamp,
            ["operationId"] = VolumeRetirementFixture.Operation,
            ["schema"] = 1,
            ["targets"] = scripted["targets"]!.DeepClone()
        };
        await WriteCanonical(grantPath, grant);
        var attestation = Path.Combine(fixture.Root, "manual.json");
        var intent = new JsonObject
        {
            ["schema"] = 1, ["mode"] = "manual", ["actor"] = "commissioned-fixture-delegate",
            ["authorityRef"] = "fixture-task-apply-gate", ["reason"] = "exercise the apply gate",
            ["utc"] = stamp, ["operationId"] = VolumeRetirementFixture.Operation, ["phase"] = "Preview",
            ["targets"] = scripted["targets"]!.DeepClone(), ["previewDigest"] = null,
            ["fingerprints"] = scripted["fingerprints"]!.DeepClone()
        };
        await WriteCanonical(attestation, intent);
        var context = "C1008_MODE=manual\nC1008_MANUAL_AUTHORITY_FILE=" + VolumeRetirementFixture.Quote(grantPath) +
            "\nC1008_ATTESTATION_FILE=" + VolumeRetirementFixture.Quote(attestation);
        await VolumeRetirementFixture.ReadyPreview(fixture, context);
        intent["phase"] = "Apply";
        intent["previewDigest"] = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(receipt))).ToLowerInvariant();
        await WriteCanonical(attestation, intent);
        return context;
    }

    private static Task WriteCanonical(string path, JsonNode value)
    {
        JsonNode Sort(JsonNode node) => node switch
        {
            JsonObject obj => new JsonObject(obj.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value is null ? null : Sort(pair.Value)))),
            JsonArray array => new JsonArray(array.Select(item => item is null ? null : Sort(item)).ToArray()),
            _ => node.DeepClone()
        };
        return File.WriteAllTextAsync(path, Sort(value).ToJsonString(new JsonSerializerOptions
        { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, relative));

    private static int Count(string text, string token)
    {
        var count = 0;
        var from = 0;
        while ((from = text.IndexOf(token, from, StringComparison.Ordinal)) >= 0)
        {
            count++;
            from += token.Length;
        }
        return count;
    }

    private static string Function(string text, string name)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = Array.FindIndex(lines, line => line.StartsWith(name + "() {", StringComparison.Ordinal));
        start.ShouldBeGreaterThanOrEqualTo(0, "function " + name + " is missing");
        var end = Array.FindIndex(lines, start + 1, line => line == "}");
        end.ShouldBeGreaterThan(start, "function " + name + " never closes");
        return string.Join('\n', lines[start..(end + 1)]);
    }
}
