using System.Text.Json.Nodes;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Timeout(600_000)]
[Category("Unit")]
public sealed class VolumeRetirementContentTests
{
    internal const string NativeLinuxReason = "CARD-1609: content retirement proofs require native Linux (bash, flock, real Git and POSIX mounts); Windows skips explicitly.";

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Current_tooling_and_missing_history_do_not_block()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var probe = "\n" + VolumeRetirementFixture.ShellProbe;
        using var missingHistory = new C1008HostFixture(main: false);
        var unpinned = await missingHistory.Run("retire-temp-runner", probe, dryRun: true);
        unpinned.Exit.ShouldBe(0, "c1609-c1: unpinned preview reaches the current entry; " + unpinned.Output);
        unpinned.Output.ShouldContain("C994_IMAGE_UNPINNED reason=no-matching-cleanup-record");
        unpinned.Output.ShouldContain("auditPending=true");
        unpinned.Output.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        missingHistory.Removed.ShouldBeEmpty();

        using var missingStack = new C1008HostFixture(main: false);
        await File.WriteAllTextAsync(Path.Combine(missingStack.Root, "temp.env"), "");
        var stack = await missingStack.Run("retire-temp-runner", probe, dryRun: true);
        stack.Exit.ShouldBe(0, "c1609-c1: an empty temp.env is not the content gate; " + stack.Output);
        stack.Output.ShouldNotContain("TempStackMissing");
        stack.Output.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        missingStack.Removed.ShouldBeEmpty();

        using var conflicting = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.RetainPreviewImage(conflicting);
        var seedPath = Path.Combine(conflicting.Root, "server", "temp-container-retirement",
            VolumeRetirementFixture.AppliedCleanup + ".json");
        var seed = JsonNode.Parse(await File.ReadAllTextAsync(seedPath))!.AsObject();
        ReplaceReceipt(conflicting, seed, "c99400000000000000000000000000000011",
            "f84228f2bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "sha256:" + new string('4', 64));
        ReplaceReceipt(conflicting, seed, "c99400000000000000000000000000000012",
            "dddddddddddddddddddddddddddddddddddddddd", "sha256:" + new string('e', 64), append: true);
        var conflict = await conflicting.Run("retire-temp-runner", probe, dryRun: true);
        conflict.Exit.ShouldBe(0, "c1609-c1: conflicting old images warn and still prove content; " + conflict.Output);
        conflict.Output.ShouldContain("RecycleGitAuditUnknown");
        conflict.Output.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        conflicting.Removed.ShouldBeEmpty();

        using var missingRuntime = new C1008HostFixture(main: false);
        missingRuntime.Docker["fault"] = "image-inspect-error";
        var runtime = await missingRuntime.Run("retire-temp-runner");
        runtime.Exit.ShouldBe(2, "c1609-c1: a missing current image refuses; " + runtime.Output);
        missingRuntime.Removed.ShouldBeEmpty();
        runtime.Output.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);

        var admitted =
            unpinned.Output.Contains(VolumeRetirementFixture.ReadyMarker, StringComparison.Ordinal) &&
            stack.Output.Contains(VolumeRetirementFixture.ReadyMarker, StringComparison.Ordinal) &&
            conflict.Output.Contains(VolumeRetirementFixture.ReadyMarker, StringComparison.Ordinal);
        admitted.ShouldBeTrue(
            "missing or conflicting history must admit a current Ready proof; base kept the old binding. " +
            "unpinned=" + VolumeRetirementFixture.Truncate(unpinned.Output) +
            " stack=" + VolumeRetirementFixture.Truncate(stack.Output) +
            " conflict=" + VolumeRetirementFixture.Truncate(conflict.Output));
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task State_credentials_refuse_before_removal()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var credential = Path.Combine(VolumeRetirementFixture.StateMount(fixture), "auth.json");
        await File.WriteAllTextAsync(credential, "{\"kind\":\"process-marker\"}\n");
        var run = await fixture.Run("retire-temp-runner");
        var state = "antiphon-runner-temp_runner-state";
        var removed = fixture.Removed.Contains(state);
        var refused = run.Exit != 0 && !removed &&
            run.Output.Contains("reason=state-credential", StringComparison.Ordinal);
        refused.ShouldBeTrue(
            "state credentials must refuse with zero removes; the work-only audit permitted removal. " +
            "exit=" + run.Exit + " removed=[" + string.Join(",", fixture.Removed) + "] out=" +
            VolumeRetirementFixture.Truncate(run.Output));
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Private_origin_advanced_head_uses_real_host_proof()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var root = Path.Combine("/var/tmp", "c1008-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var fixture = new C1008HostFixture(main: false, root);
        var seeded = await VolumeRetirementFixture.SeedAdvancedOrigin(fixture);
        await File.WriteAllTextAsync(Path.Combine(root, "helper-auth-128"), "1\n");
        await VolumeRetirementFixture.RetainPreviewImage(fixture);
        var bound = "C1008_CLEANUP_OPERATION=" + VolumeRetirementFixture.PreviewCleanup;

        var probe = "\n" + VolumeRetirementFixture.ShellProbe;
        var inventory = await fixture.Run("retire-temp-inventory",
            "C1008_CLEANUP_OPERATION=" + VolumeRetirementFixture.AppliedCleanup + probe);
        inventory.Exit.ShouldBe(0, "c1609-h7: inventory lists the private origin; " + inventory.Output);
        inventory.Output.ShouldContain("C1008_PRIVATE_ORIGIN repo=repos/school-revision origin=" + VolumeRetirementFixture.OriginUrl);
        var helperTrace = Path.Combine(root, "helper-git-trace");
        File.Exists(helperTrace).ShouldBeTrue("c1609-h7: the helper invoked ls-remote");
        (await File.ReadAllTextAsync(helperTrace)).ShouldContain("ls-remote");

        var denyEvidence = Path.Combine(root, "deny-evidence");
        var denied = await VolumeRetirementFixture.InvokeHostProof(
            denyEvidence, Path.Combine(root, "evidence", "retire-temp-inventory"), seeded.Bare,
            Path.Combine(root, "deny-remote"), deny: true);
        denied.Exit.ShouldBe(2, "c1609-h7: a host read of 128 refuses; " + denied.Output);
        var deniedReceipt = Path.Combine(denyEvidence, "c590-result.json");
        var deniedText = denied.Output + (File.Exists(deniedReceipt) ? await File.ReadAllTextAsync(deniedReceipt) : "");
        deniedText.ShouldContain("host-ls-remote", Case.Sensitive,
            "c1609-h7: the real writer names the host 128; " + VolumeRetirementFixture.Truncate(deniedText));
        var deniedPreview = await fixture.Run("retire-temp-runner", bound + probe, dryRun: true);
        deniedPreview.Exit.ShouldBe(2, "c1609-h7: no host proof refuses; " + deniedPreview.Output);
        deniedPreview.Output.ShouldContain("RecycleGitAuditUnknown");
        deniedPreview.Output.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);

        File.Delete(Path.Combine(root, "helper-auth-128"));
        var child = await VolumeRetirementFixture.Bash(
            "set -euo pipefail\n" +
            "git -C " + VolumeRetirementFixture.Quote(seeded.Repo) + " remote set-url origin " + VolumeRetirementFixture.Quote(seeded.Bare) + "\n" +
            "git -C " + VolumeRetirementFixture.Quote(seeded.Repo) + " commit -q --allow-empty -m unpublished\n");
        child.Exit.ShouldBe(0, "c1609-h7: unpublished child; " + child.Output);
        var unpublished = await fixture.Run("retire-temp-runner", bound + probe, dryRun: true);
        unpublished.Exit.ShouldBe(2, "c1609-h7: an unpublished child refuses; " + unpublished.Output);
        unpublished.Output.ShouldContain("RecycleUnpublishedWork");
        unpublished.Output.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        var restore = await VolumeRetirementFixture.Bash(
            "set -euo pipefail\n" +
            "git -C " + VolumeRetirementFixture.Quote(seeded.Repo) + " reset -q --hard " + VolumeRetirementFixture.Quote(seeded.A) + "\n" +
            "if [ -d " + VolumeRetirementFixture.Quote(Path.Combine(seeded.Repo, ".git", "logs")) + " ]; then find " +
            VolumeRetirementFixture.Quote(Path.Combine(seeded.Repo, ".git", "logs")) + " -type f -delete; fi\n" +
            "rm -f -- " + VolumeRetirementFixture.Quote(Path.Combine(seeded.Repo, ".git", "ORIG_HEAD")) + "\n" +
            "git -C " + VolumeRetirementFixture.Quote(seeded.Repo) + " remote set-url origin " + VolumeRetirementFixture.Quote(VolumeRetirementFixture.OriginUrl) + "\n");
        restore.Exit.ShouldBe(0, "c1609-h7: restore A; " + restore.Output);
        await File.WriteAllTextAsync(Path.Combine(root, "helper-auth-128"), "1\n");
        if (File.Exists(helperTrace)) File.Delete(helperTrace);

        var evidence = Path.Combine(root, "host-evidence");
        var proved = await VolumeRetirementFixture.InvokeHostProof(
            evidence, Path.Combine(root, "evidence", "retire-temp-inventory"), seeded.Bare,
            Path.Combine(root, "remote-root"), deny: false);
        proved.Exit.ShouldBe(0, "c1609-h7: the real host writer reads B; " +
            VolumeRetirementFixture.Truncate(proved.Output));
        proved.Output.ShouldContain("export C1008_HOST_PROOF=");
        var proofText = string.Join('\n', Directory.EnumerateFiles(evidence, "*", SearchOption.AllDirectories).Select(File.ReadAllText));
        proofText.ShouldContain(seeded.B + "\trefs/heads/main");
        (await File.ReadAllTextAsync(Path.Combine(evidence, "host-git-trace"))).ShouldContain("ls-remote");
        var proofDir = Directory.EnumerateDirectories(evidence).Single(path => Path.GetFileName(path).StartsWith("c1008-proof-", StringComparison.Ordinal));

        var preview = await fixture.Run("retire-temp-runner",
            bound + "\nexport C1008_HOST_PROOF=" + VolumeRetirementFixture.Quote(proofDir) +
            "\nexport C1008_OPERATION_STARTED=1700000000\n" + probe, dryRun: true);
        preview.Output.ShouldContain("origin-present", Case.Sensitive,
            "c1609-h7: the base publication check refuses an advanced head the volume does not contain");
        (await File.ReadAllTextAsync(helperTrace)).ShouldContain("ls-remote");
        fixture.Removed.ShouldBeEmpty();
        var admitted = preview.Exit == 0 &&
            preview.Output.Contains(VolumeRetirementFixture.ReadyMarker, StringComparison.Ordinal);
        admitted.ShouldBeTrue(
            "host ancestry of A from advertised B must admit a Ready preview with zero removes; " +
            "base refused origin-present. exit=" + preview.Exit + " out=" +
            VolumeRetirementFixture.Truncate(preview.Output));
    }

    private static void ReplaceReceipt(C1008HostFixture fixture, JsonObject seed, string operation, string sourceSha, string image, bool append = false)
    {
        var copy = JsonNode.Parse(seed.ToJsonString())!.AsObject();
        copy["operationId"] = operation;
        copy["sourceSha"] = sourceSha;
        copy["image"] = image;
        copy["dryRun"] = false;
        copy["outcome"] = "completed";
        copy.Remove("originalCleanupOperationId");
        var dir = Path.Combine(fixture.Root, "server", "temp-container-retirement");
        if (!append)
        {
            foreach (var file in Directory.GetFiles(dir, "c994*.json")) File.Delete(file);
        }
        File.WriteAllText(Path.Combine(dir, operation + ".json"), copy.ToJsonString());
    }
}
