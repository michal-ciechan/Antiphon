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

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Every_ref_recovery_tip_and_tag_object_requires_publication()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (_, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        var planted = await VolumeRetirementFixture.Bash(
            "set -euo pipefail\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) + " checkout -q -b private\n" +
            "printf 'secret\\n' > " + VolumeRetirementFixture.Quote(outer) + "/secret.txt\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) + " add secret.txt\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) + " -c commit.gpgsign=false commit -qm private\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) + " checkout -q main\n");
        planted.Exit.ShouldBe(0, "c1609-c3: private branch; " + planted.Output);
        var refused = await VolumeRetirementFixture.Preview(fixture);
        var refusedText = refused.Output + await VolumeRetirementFixture.AuditText(fixture);
        refused.Exit.ShouldBe(2, "c1609-c3: private branch; " + VolumeRetirementFixture.Truncate(refusedText));
        refusedText.ShouldContain("RecycleUnpublishedWork");
        refusedText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();

        var restore = await VolumeRetirementFixture.Bash(
            "git -C " + VolumeRetirementFixture.Quote(outer) + " branch -D private");
        restore.Exit.ShouldBe(0, "c1609-c3: drop private; " + restore.Output);
        var tagged = await VolumeRetirementFixture.Bash(
            "set -euo pipefail\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) +
            " -c user.name=Fixture -c user.email=fixture@example.invalid tag -a public-peeled -m 'tag body stays local'\n");
        tagged.Exit.ShouldBe(0, "c1609-c3: annotated tag; " + tagged.Output);
        var tagRun = await VolumeRetirementFixture.Preview(fixture);
        var tagText = tagRun.Output + await VolumeRetirementFixture.AuditText(fixture);
        tagRun.Exit.ShouldBe(2, "c1609-c3: unadvertised tag object; " + VolumeRetirementFixture.Truncate(tagText));
        tagText.ShouldContain("RecycleUnpublishedWork");
        tagText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();

        var dropTag = await VolumeRetirementFixture.Bash(
            "git -C " + VolumeRetirementFixture.Quote(outer) + " tag -d public-peeled");
        dropTag.Exit.ShouldBe(0, "c1609-c3: drop tag; " + dropTag.Output);
        var reflogOnly = await VolumeRetirementFixture.Preview(fixture);
        var reflogText = reflogOnly.Output + await VolumeRetirementFixture.AuditText(fixture);
        reflogOnly.Exit.ShouldBe(2, "c1609-c3: HEAD reflog still names the private commit; " + VolumeRetirementFixture.Truncate(reflogText));
        reflogText.ShouldContain("RecycleUnpublishedWork");
        reflogText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
        var expire = await VolumeRetirementFixture.Bash(
            "git -C " + VolumeRetirementFixture.Quote(outer) + " reflog expire --expire=now --all");
        expire.Exit.ShouldBe(0, "c1609-c3: expire reflog; " + expire.Output);
        var admitted = await VolumeRetirementFixture.Preview(fixture);
        var admittedText = admitted.Output + await VolumeRetirementFixture.AuditText(fixture);
        admitted.Exit.ShouldBe(0, "c1609-c3: published main; " + VolumeRetirementFixture.Truncate(admittedText));
        admittedText.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Worktree_content_and_admin_closure_are_complete()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (_, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        await File.WriteAllTextAsync(Path.Combine(outer, "dirty.txt"), "dirty\n");
        var dirty = await VolumeRetirementFixture.Preview(fixture);
        var dirtyText = dirty.Output + await VolumeRetirementFixture.AuditText(fixture);
        dirty.Exit.ShouldBe(2, "c1609-c4: untracked; " + VolumeRetirementFixture.Truncate(dirtyText));
        dirtyText.ShouldContain("RecycleWorktreeDirty");
        dirtyText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
        File.Delete(Path.Combine(outer, "dirty.txt"));

        var staged = await VolumeRetirementFixture.Bash(
            "set -euo pipefail\n" +
            "printf 'staged\\n' > " + VolumeRetirementFixture.Quote(outer) + "/staged.txt\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) + " add staged.txt\n");
        staged.Exit.ShouldBe(0, "c1609-c4: stage; " + staged.Output);
        var stagedRun = await VolumeRetirementFixture.Preview(fixture);
        var stagedText = stagedRun.Output + await VolumeRetirementFixture.AuditText(fixture);
        stagedRun.Exit.ShouldBe(2, "c1609-c4: staged; " + VolumeRetirementFixture.Truncate(stagedText));
        stagedText.ShouldContain("RecycleWorktreeDirty");
        fixture.Removed.ShouldBeEmpty();
        var unstage = await VolumeRetirementFixture.Bash(
            "git -C " + VolumeRetirementFixture.Quote(outer) + " reset -q && rm -f -- " +
            VolumeRetirementFixture.Quote(Path.Combine(outer, "staged.txt")));
        unstage.Exit.ShouldBe(0, "c1609-c4: unstage; " + unstage.Output);

        var clean = await VolumeRetirementFixture.Preview(fixture);
        (clean.Output + await VolumeRetirementFixture.AuditText(fixture)).ShouldContain(VolumeRetirementFixture.ReadyMarker);
        clean.Exit.ShouldBe(0, "c1609-c4: clean control; " + clean.Output);
        fixture.Removed.ShouldBeEmpty();
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Evidence_names_and_filesystem_shapes_fail_closed()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (_, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        var evidence = Path.Combine(VolumeRetirementFixture.WorkMount(fixture), "test-evidence", "nested");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "HEAD"), "ref: refs/heads/main\n");
        var refused = await VolumeRetirementFixture.Preview(fixture);
        var text = refused.Output + await VolumeRetirementFixture.AuditText(fixture);
        refused.Exit.ShouldBe(2, "c1609-c5: evidence HEAD; " + VolumeRetirementFixture.Truncate(text));
        text.ShouldContain("reason=evidence-name");
        text.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();

        File.Delete(Path.Combine(evidence, "HEAD"));
        await File.WriteAllTextAsync(Path.Combine(evidence, "notes.txt"), "harmless\n");
        var admitted = await VolumeRetirementFixture.Preview(fixture);
        var admittedText = admitted.Output + await VolumeRetirementFixture.AuditText(fixture);
        admitted.Exit.ShouldBe(0, "c1609-c5: harmless evidence; " + VolumeRetirementFixture.Truncate(admittedText));
        admittedText.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
        _ = outer;
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task All_target_roles_are_classified()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var clean = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.SeedPublishedOuter(clean);
        var ok = await VolumeRetirementFixture.Preview(clean);
        var okText = ok.Output + await VolumeRetirementFixture.AuditText(clean);
        ok.Exit.ShouldBe(0, "c1609-c6: recognized layout; " + VolumeRetirementFixture.Truncate(okText));
        okText.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        clean.Removed.ShouldBeEmpty();

        using var dind = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.SeedPublishedOuter(dind);
        var store = Path.Combine(VolumeRetirementFixture.DindMount(dind), "custom-store");
        Directory.CreateDirectory(store);
        await File.WriteAllTextAsync(Path.Combine(store, "data"), "persistent\n");
        var bad = await VolumeRetirementFixture.Preview(dind);
        var badText = bad.Output + await VolumeRetirementFixture.AuditText(dind);
        bad.Exit.ShouldBe(2, "c1609-c6: unknown dind store; " + VolumeRetirementFixture.Truncate(badText));
        badText.ShouldContain("reason=dind-unknown");
        badText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        dind.Removed.ShouldBeEmpty();
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Read_failures_and_incomplete_inventory_refuse()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (_, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        await File.WriteAllTextAsync(Path.Combine(outer, ".git", "packed-refs"), "not a packed-refs file\n");
        var refused = await VolumeRetirementFixture.Preview(fixture);
        var text = refused.Output + await VolumeRetirementFixture.AuditText(fixture);
        refused.Exit.ShouldBe(2, "c1609-c7: truncated packed-refs; " + VolumeRetirementFixture.Truncate(text));
        text.ShouldContain("RecycleGitAuditUnknown");
        text.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Audit_identity_and_namespace_are_stable()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        var run = await VolumeRetirementFixture.Preview(fixture);
        run.Exit.ShouldBe(0, "c1609-c8: preview; " + run.Output);
        var trace = string.Join('\n', fixture.Trace.Select(row => string.Join(' ', row)));
        trace.ShouldContain("--user");
        trace.ShouldContain("1654:1654");
        trace.ShouldContain("readonly");
        trace.ShouldContain("target=/work");
        trace.ShouldContain("--cap-drop");
        run.Output.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task No_content_or_credentials_are_executed_or_exported()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (_, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        var hook = Path.Combine(outer, ".git", "hooks", "post-checkout");
        await File.WriteAllTextAsync(hook, "#!/bin/sh\nprintf 'HOOK-SENTINEL-1609\\n' > " +
            VolumeRetirementFixture.Quote(Path.Combine(fixture.Root, "hook-ran")) + "\n");
        var mode = await VolumeRetirementFixture.Bash("chmod 0755 " + VolumeRetirementFixture.Quote(hook));
        mode.Exit.ShouldBe(0, "c1609-c9: hook mode; " + mode.Output);
        var run = await VolumeRetirementFixture.Preview(fixture);
        var text = run.Output + await VolumeRetirementFixture.AuditText(fixture);
        File.Exists(Path.Combine(fixture.Root, "hook-ran")).ShouldBeFalse("c1609-c9: a repository hook must not run");
        text.ShouldNotContain("HOOK-SENTINEL-1609");
        text.ShouldContain("reason=hook");
        text.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Absent_work_does_not_skip_other_volumes()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        fixture.Docker["volumes"]!.AsObject().Remove("antiphon-runner-temp_work");
        await File.WriteAllTextAsync(Path.Combine(VolumeRetirementFixture.StateMount(fixture), "auth.json"), "{}\n");
        var refused = await VolumeRetirementFixture.Preview(fixture);
        var text = refused.Output + await VolumeRetirementFixture.AuditText(fixture);
        refused.Exit.ShouldBe(2, "c1609-c10: absent work still scans state; " + VolumeRetirementFixture.Truncate(text));
        text.ShouldContain("reason=state-credential");
        text.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
        fixture.Docker["volumes"]!.AsObject().ContainsKey("antiphon-runner-temp_work").ShouldBeFalse(
            "c1609-c10: inspecting an absent work volume must not create it");

        using var empty = new C1008HostFixture(main: false);
        foreach (var role in new[] { "work", "runner-tmp", "dind-data", "runner-state" })
            empty.Docker["volumes"]!.AsObject().Remove("antiphon-runner-temp_" + role);
        var noop = await VolumeRetirementFixture.Preview(empty);
        noop.Exit.ShouldBe(0, "c1609-c10: all absent is a no-op; " + noop.Output);
        noop.Output.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        empty.Removed.ShouldBeEmpty();
        foreach (var role in new[] { "work", "runner-tmp", "dind-data", "runner-state" })
            empty.Docker["volumes"]!.AsObject().ContainsKey("antiphon-runner-temp_" + role).ShouldBeFalse();
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Preview_lists_every_offender_without_effects()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (_, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture, "work-repo");
        var tmpRepo = Path.Combine(VolumeRetirementFixture.TmpMount(fixture), "scratch-repo");
        var planted = await VolumeRetirementFixture.Bash($$"""
            set -euo pipefail
            git -C {{VolumeRetirementFixture.Quote(outer)}} checkout -q -b retained
            printf 'unpublished-work\n' > {{VolumeRetirementFixture.Quote(outer)}}/kept.txt
            git -C {{VolumeRetirementFixture.Quote(outer)}} add kept.txt
            git -C {{VolumeRetirementFixture.Quote(outer)}} -c commit.gpgsign=false commit -qm unpublished-work
            work_oid=$(git -C {{VolumeRetirementFixture.Quote(outer)}} rev-parse HEAD)
            git -C {{VolumeRetirementFixture.Quote(outer)}} checkout -q main
            printf 'dirty-work\n' > {{VolumeRetirementFixture.Quote(outer)}}/dirty.txt
            git init -q -b main {{VolumeRetirementFixture.Quote(tmpRepo)}}
            git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} config user.name Fixture
            git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} config user.email fixture@example.invalid
            git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} config commit.gpgsign false
            printf 'tmp\n' > {{VolumeRetirementFixture.Quote(tmpRepo)}}/note.txt
            git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} add note.txt
            git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} -c commit.gpgsign=false commit -qm tmp-base
            git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} remote add origin {{VolumeRetirementFixture.Quote(Path.Combine(fixture.Root, "work-repo.git"))}}
            printf 'fetch-private\n' > {{VolumeRetirementFixture.Quote(tmpRepo)}}/private.txt
            git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} add private.txt
            git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} -c commit.gpgsign=false commit -qm fetch-private
            fetch_oid=$(git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} rev-parse HEAD)
            git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} reset -q --hard HEAD~1
            printf '%s\t\tfetch of private\n' "$fetch_oid" > {{VolumeRetirementFixture.Quote(tmpRepo)}}/.git/FETCH_HEAD
            printf 'work=%s\nfetch=%s\n' "$work_oid" "$fetch_oid"
            """);
        planted.Exit.ShouldBe(0, "c1609-c11: plant offenders; " + planted.Output);
        var workOid = planted.Output.Split('\n').Single(line => line.StartsWith("work=", StringComparison.Ordinal))[5..];
        var fetchOid = planted.Output.Split('\n').Single(line => line.StartsWith("fetch=", StringComparison.Ordinal))[6..];
        var evidence = Path.Combine(VolumeRetirementFixture.WorkMount(fixture), "test-evidence");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "HEAD"), "ref: refs/heads/main\n");
        await File.WriteAllTextAsync(Path.Combine(VolumeRetirementFixture.StateMount(fixture), "auth.json"), "{}\n");
        var broken = Path.Combine(VolumeRetirementFixture.WorkMount(fixture), "repos", "broken");
        Directory.CreateDirectory(Path.Combine(broken, ".git"));
        await File.WriteAllTextAsync(Path.Combine(broken, ".git", "HEAD"), "not-a-ref\n");

        var run = await VolumeRetirementFixture.Preview(fixture);
        var text = run.Output + await VolumeRetirementFixture.AuditText(fixture);
        run.Exit.ShouldNotBe(0, "c1609-c11: findings refuse; " + VolumeRetirementFixture.Truncate(text));
        text.ShouldContain(workOid);
        text.ShouldContain(fetchOid);
        text.ShouldContain("\"kind\":\"reflog\"");
        text.ShouldContain("\"kind\":\"fetch-head\"");
        text.ShouldContain("\"kind\":\"dirty\"");
        text.ShouldContain("\"kind\":\"evidence\"");
        text.ShouldContain("\"kind\":\"state\"");
        text.ShouldContain("\"complete\":false");
        text.ShouldContain("repos/broken");
        text.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Fetch_head_requires_proven_objects_not_fresh_names()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (bare, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        var published = await VolumeRetirementFixture.Bash(
            "set -euo pipefail\n" +
            "oid=$(git -C " + VolumeRetirementFixture.Quote(outer) + " rev-parse HEAD)\n" +
            "blob=$(git -C " + VolumeRetirementFixture.Quote(outer) + " rev-parse HEAD:readme.txt)\n" +
            "printf '%s\\t\\tbranch main of published\\n%s\\tnot-for-merge\\tblob readme\\n' \"$oid\" \"$blob\" > " +
            VolumeRetirementFixture.Quote(Path.Combine(outer, ".git", "FETCH_HEAD")) + "\n" +
            "printf 'oid=%s\nblob=%s\n' \"$oid\" \"$blob\"\n");
        published.Exit.ShouldBe(0, "c1609-c12: published fetch head; " + published.Output);
        var oid = published.Output.Split('\n').Single(line => line.StartsWith("oid=", StringComparison.Ordinal))[4..];
        var blob = published.Output.Split('\n').Single(line => line.StartsWith("blob=", StringComparison.Ordinal))[5..];
        var ok = await VolumeRetirementFixture.Preview(fixture);
        var okText = ok.Output + await VolumeRetirementFixture.AuditText(fixture);
        ok.Exit.ShouldBe(0, "c1609-c12: published entries; " + VolumeRetirementFixture.Truncate(okText));
        okText.ShouldContain("published-fetch-head");
        okText.ShouldContain(oid);
        okText.ShouldContain(blob);
        okText.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();

        var mixed = await VolumeRetirementFixture.Bash($$"""
            set -euo pipefail
            git -C {{VolumeRetirementFixture.Quote(outer)}} checkout -q -b hidden
            printf 'hidden\n' > {{VolumeRetirementFixture.Quote(outer)}}/hidden.txt
            git -C {{VolumeRetirementFixture.Quote(outer)}} add hidden.txt
            git -C {{VolumeRetirementFixture.Quote(outer)}} -c commit.gpgsign=false commit -qm hidden
            hidden=$(git -C {{VolumeRetirementFixture.Quote(outer)}} rev-parse HEAD)
            git -C {{VolumeRetirementFixture.Quote(outer)}} checkout -q main
            git -C {{VolumeRetirementFixture.Quote(outer)}} branch -D hidden
            printf '%s\t\tbranch main of published\n%s\tnot-for-merge\thidden commit\n' {{VolumeRetirementFixture.Quote(oid)}} "$hidden" > {{VolumeRetirementFixture.Quote(Path.Combine(outer, ".git", "FETCH_HEAD"))}}
            printf 'hidden=%s\n' "$hidden"
            """);
        mixed.Exit.ShouldBe(0, "c1609-c12: mixed fetch head; " + mixed.Output);
        var hidden = mixed.Output.Split('\n').Single(line => line.StartsWith("hidden=", StringComparison.Ordinal))[7..];
        var mixedRun = await VolumeRetirementFixture.Preview(fixture);
        var mixedText = mixedRun.Output + await VolumeRetirementFixture.AuditText(fixture);
        mixedRun.Exit.ShouldBe(2, "c1609-c12: mixed entries; " + VolumeRetirementFixture.Truncate(mixedText));
        mixedText.ShouldContain(oid);
        mixedText.ShouldContain(hidden);
        mixedText.ShouldContain("published-fetch-head");
        mixedText.ShouldContain("unpublished");
        mixedText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();

        var reset = await VolumeRetirementFixture.Bash(
            "set -euo pipefail\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) + " reset -q --hard origin/main\n" +
            "rm -rf -- " + VolumeRetirementFixture.Quote(Path.Combine(outer, ".git", "logs")) + "\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) + " checkout -q main\n");
        reset.Exit.ShouldBe(0, "c1609-c12: reset; " + reset.Output);
        await File.WriteAllTextAsync(Path.Combine(outer, ".git", "FETCH_HEAD"), "not-an-object\t\tbad\n");
        var bad = await VolumeRetirementFixture.Preview(fixture);
        var badText = bad.Output + await VolumeRetirementFixture.AuditText(fixture);
        bad.Exit.ShouldBe(2, "c1609-c12: malformed; " + VolumeRetirementFixture.Truncate(badText));
        badText.ShouldContain("reason=fetch-head-malformed");
        badText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
        _ = bare;
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Only_exact_known_seed_fixtures_are_disposable()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (_, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        var template = Path.Combine(outer, ".antiphon", "test-output", "card-1605", "template");
        var exact = await SeedTemplate(template, "exact");
        exact.Exit.ShouldBe(0, "c1609-c13: exact seed; " + exact.Output);
        var ok = await VolumeRetirementFixture.Preview(fixture);
        var okText = ok.Output + await VolumeRetirementFixture.AuditText(fixture);
        ok.Exit.ShouldBe(0, "c1609-c13: exact seed is disposable; " + VolumeRetirementFixture.Truncate(okText));
        okText.ShouldContain("disposable-fixture classifier=v1");
        okText.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();

        foreach (var kind in new[] { "gpgsig", "encoding", "custom" })
        {
            var replaced = await SeedTemplate(template, kind);
            replaced.Exit.ShouldBe(0, "c1609-c13: " + kind + "; " + replaced.Output);
            var refused = await VolumeRetirementFixture.Preview(fixture);
            var text = refused.Output + await VolumeRetirementFixture.AuditText(fixture);
            refused.Exit.ShouldBe(2, "c1609-c13: " + kind + " header refuses; " + VolumeRetirementFixture.Truncate(text));
            text.ShouldContain("reason=seed-bytes");
            text.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
            text.ShouldNotContain("HEADER-SENTINEL-1609");
            fixture.Removed.ShouldBeEmpty();
        }

        var wrongPath = Path.Combine(outer, "cases", "other", "main");
        var moved = await VolumeRetirementFixture.Bash(
            "rm -rf -- " + VolumeRetirementFixture.Quote(template) + "\n");
        moved.Exit.ShouldBe(0, "c1609-c13: remove template; " + moved.Output);
        var offPath = await SeedTemplate(wrongPath, "exact");
        offPath.Exit.ShouldBe(0, "c1609-c13: off-path seed; " + offPath.Output);
        var off = await VolumeRetirementFixture.Preview(fixture);
        var offText = off.Output + await VolumeRetirementFixture.AuditText(fixture);
        off.Exit.ShouldBe(2, "c1609-c13: path is not disposable; " + VolumeRetirementFixture.Truncate(offText));
        offText.ShouldContain("reason=no-remote-unclassified");
        offText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Gone_tracking_retains_original_commit_protection()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (bare, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        var reachable = await VolumeRetirementFixture.Bash($$"""
            set -euo pipefail
            git -C {{VolumeRetirementFixture.Quote(outer)}} checkout -q -b feature
            printf 'feature\n' > {{VolumeRetirementFixture.Quote(outer)}}/feature.txt
            git -C {{VolumeRetirementFixture.Quote(outer)}} add feature.txt
            git -C {{VolumeRetirementFixture.Quote(outer)}} -c commit.gpgsign=false commit -qm feature
            git -C {{VolumeRetirementFixture.Quote(outer)}} push -q origin HEAD:refs/heads/feature
            git -C {{VolumeRetirementFixture.Quote(outer)}} checkout -q main
            git -C {{VolumeRetirementFixture.Quote(outer)}} merge -q --no-ff feature -m merge-feature
            git -C {{VolumeRetirementFixture.Quote(outer)}} push -q origin HEAD:refs/heads/main
            git -C {{VolumeRetirementFixture.Quote(bare)}} update-ref -d refs/heads/feature
            git -C {{VolumeRetirementFixture.Quote(outer)}} fetch -q origin
            git -C {{VolumeRetirementFixture.Quote(outer)}} remote prune origin
            """);
        reachable.Exit.ShouldBe(0, "c1609-c14: reachable original; " + reachable.Output);
        var passed = await VolumeRetirementFixture.Preview(fixture);
        var passedText = passed.Output + await VolumeRetirementFixture.AuditText(fixture);
        passed.Exit.ShouldBe(0, "c1609-c14: original still reachable; " + VolumeRetirementFixture.Truncate(passedText));
        passedText.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();

        var rebased = await VolumeRetirementFixture.Bash($$"""
            set -euo pipefail
            git -C {{VolumeRetirementFixture.Quote(outer)}} checkout -q -b replay
            printf 'replay\n' > {{VolumeRetirementFixture.Quote(outer)}}/replay.txt
            git -C {{VolumeRetirementFixture.Quote(outer)}} add replay.txt
            git -C {{VolumeRetirementFixture.Quote(outer)}} -c commit.gpgsign=false commit -qm replay
            tree=$(git -C {{VolumeRetirementFixture.Quote(outer)}} rev-parse HEAD^{tree})
            parent=$(git -C {{VolumeRetirementFixture.Quote(outer)}} rev-parse HEAD^)
            alt=$(git -C {{VolumeRetirementFixture.Quote(outer)}} commit-tree "$tree" -p "$parent" -m replay)
            git -C {{VolumeRetirementFixture.Quote(outer)}} update-ref refs/heads/replay "$alt"
            git -C {{VolumeRetirementFixture.Quote(outer)}} checkout -q main
            printf 'alt=%s\n' "$alt"
            """);
        rebased.Exit.ShouldBe(0, "c1609-c14: rebased twin; " + rebased.Output);
        var alt = rebased.Output.Split('\n').Single(line => line.StartsWith("alt=", StringComparison.Ordinal))[4..];
        var refused = await VolumeRetirementFixture.Preview(fixture);
        var refusedText = refused.Output + await VolumeRetirementFixture.AuditText(fixture);
        refused.Exit.ShouldBe(2, "c1609-c14: twin is not the original; " + VolumeRetirementFixture.Truncate(refusedText));
        refusedText.ShouldContain(alt);
        refusedText.ShouldContain("recovery=publish-the-original-commit-to-an-origin-archive-branch-then-preview-again");
        refusedText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();

        var archive = await VolumeRetirementFixture.Bash(
            "set -euo pipefail\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) + " push -q origin " +
            VolumeRetirementFixture.Quote(alt) + ":refs/heads/archive\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) + " fetch -q origin\n");
        archive.Exit.ShouldBe(0, "c1609-c14: archive branch; " + archive.Output);
        var again = await VolumeRetirementFixture.Preview(fixture);
        var againText = again.Output + await VolumeRetirementFixture.AuditText(fixture);
        again.Exit.ShouldBe(0, "c1609-c14: archive head publishes the original; " + VolumeRetirementFixture.Truncate(againText));
        againText.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
    }

    private static async Task<(int Exit, string Output)> SeedTemplate(string path, string kind)
    {
        var header = kind switch
        {
            "gpgsig" => "printf 'gpgsig -----BEGIN PGP SIGNATURE-----\\n HEADER-SENTINEL-1609\\n -----END PGP SIGNATURE-----\\n'\n",
            "encoding" => "printf 'encoding HEADER-SENTINEL-1609\\n'\n",
            "custom" => "printf 'x-card-1609 HEADER-SENTINEL-1609\\n'\n",
            _ => ""
        };
        return await VolumeRetirementFixture.Bash($$"""
            set -euo pipefail
            rm -rf -- {{VolumeRetirementFixture.Quote(path)}}
            git init -q -b main {{VolumeRetirementFixture.Quote(path)}}
            git -C {{VolumeRetirementFixture.Quote(path)}} config user.name Fixture
            git -C {{VolumeRetirementFixture.Quote(path)}} config user.email fixture@example.invalid
            git -C {{VolumeRetirementFixture.Quote(path)}} config commit.gpgsign false
            printf '.antiphon/\n' > {{VolumeRetirementFixture.Quote(path)}}/.gitignore
            printf 'benign fixture' > {{VolumeRetirementFixture.Quote(path)}}/seed.txt
            git -C {{VolumeRetirementFixture.Quote(path)}} add .gitignore seed.txt
            tree=$(git -C {{VolumeRetirementFixture.Quote(path)}} write-tree)
            {
              printf 'tree %s\n' "$tree"
              {{header}}
              printf 'author Fixture <fixture@example.invalid> 1000000000 +0000\n'
              printf 'committer Fixture <fixture@example.invalid> 1000000001 +0000\n'
              printf '\nseed\n'
            } > {{VolumeRetirementFixture.Quote(path)}}/.git/COMMIT_EDITMSG.c1609
            oid=$(git -C {{VolumeRetirementFixture.Quote(path)}} hash-object --literally -t commit -w --stdin < {{VolumeRetirementFixture.Quote(path)}}/.git/COMMIT_EDITMSG.c1609)
            rm -rf -- {{VolumeRetirementFixture.Quote(path)}}/.git/logs
            git -C {{VolumeRetirementFixture.Quote(path)}} update-ref refs/heads/main "$oid"
            git -C {{VolumeRetirementFixture.Quote(path)}} symbolic-ref HEAD refs/heads/main
            git -C {{VolumeRetirementFixture.Quote(path)}} read-tree HEAD
            printf 'oid=%s\n' "$oid"
            """);
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
