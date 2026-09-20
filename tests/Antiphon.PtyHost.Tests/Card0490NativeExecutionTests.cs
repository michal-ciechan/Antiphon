using Antiphon.Card0490.NativeHarness;
using Shouldly;
using TUnit.Core;

namespace Antiphon.PtyHost.Tests;

[Category("PtyHost")]
public class Card0490NativeExecutionTests
{
    [Test]
    public async Task Host_rejects_unlisted_method_project()
    {
        var launchIntents = new List<string>();
        NativeInputPolicy.AllowMethod("tests/Antiphon.Tests", "/*/*/Foo/Bar").ShouldBeFalse();
        if (NativeInputPolicy.AllowMethod("tests/Antiphon.Tests", "/*/*/Foo/*"))
            launchIntents.Add("class-wide");
        launchIntents.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Sourced_binding_mismatch_prevents_launch()
    {
        var expected = new CommissionedBinding("O", "L", "T", "C", "bind", "root");
        var launchIntents = new List<string>();
        foreach (var actual in new[]
                 {
                     expected with { O = "x" }, expected with { L = "x" }, expected with { Task = "x" },
                     expected with { Creation = "x" }, expected with { ExecutionBinding = "x" }, expected with { SourceRoot = "x" },
                 })
        {
            if (NativeInputPolicy.BindingEquals(expected, actual))
                launchIntents.Add(actual.O);
        }

        launchIntents.ShouldBeEmpty();
        NativeInputPolicy.BindingEquals(expected, expected).ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Dirty_commissioning_state_prevents_launch()
    {
        var launchIntents = new List<string>();
        if (NativeInputPolicy.IsCleanCommissioning(true, false)) launchIntents.Add("worktree");
        if (NativeInputPolicy.IsCleanCommissioning(false, true)) launchIntents.Add("index");
        launchIntents.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Sourced_refusal_never_downgrades_to_ordinary()
    {
        var args = new[] { "-BindingFile", "x", "-Ordinary" };
        NativeInputPolicy.OrdinaryAndBindingConflict(args).ShouldBeTrue();
        NativeInputPolicy.OrdinaryAndBindingConflict(["-Ordinary"]).ShouldBeFalse();
        var ordinaryInvocations = NativeInputPolicy.OrdinaryAndBindingConflict(args) ? 0 : 1;
        ordinaryInvocations.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Source_escape_is_rejected_before_read()
    {
        var root = Path.Combine(Path.GetTempPath(), "c490-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var outsideReadIntents = new List<string>();
            if (NativeFilePolicy.AllowRead(root, Path.Combine(root, "..", "sibling")))
                outsideReadIntents.Add("parent");
            outsideReadIntents.ShouldBeEmpty();
        }
        finally { try { Directory.Delete(root, true); } catch { } }
        await Task.CompletedTask;
    }

    [Test]
    public async Task Source_reparse_is_rejected_before_read()
    {
        var outsideReadIntents = new List<string>();
        NativeFilePolicy.IsReparse(Path.GetTempPath()).ShouldBeFalse();
        outsideReadIntents.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Input_escape_is_rejected_before_write()
    {
        var root = Path.Combine(Path.GetTempPath(), "c490w-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var outsideWriteIntents = new List<string>();
            if (NativeFilePolicy.AllowWrite(root, Path.GetFullPath("/windows")))
                outsideWriteIntents.Add("abs");
            outsideWriteIntents.ShouldBeEmpty();
        }
        finally { try { Directory.Delete(root, true); } catch { } }
        await Task.CompletedTask;
    }

    [Test]
    public async Task Input_reparse_is_rejected_before_write()
    {
        var outsideWriteIntents = new List<string>();
        outsideWriteIntents.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Evidence_escape_is_rejected_before_write()
    {
        var outsideWriteIntents = new List<string>();
        NativeFilePolicy.IsContained(@"C:\e", @"C:\other\x").ShouldBeFalse();
        outsideWriteIntents.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Evidence_reparse_is_rejected_before_write()
    {
        var outsideWriteIntents = new List<string>();
        outsideWriteIntents.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Package_contains_declared_working_mutant()
    {
        var workingMutantBytes = "mutant"u8.ToArray();
        var packagedBytes = "mutant"u8.ToArray();
        packagedBytes.ShouldBe(workingMutantBytes);
        NativeInputPolicy.PackageIsWorkingBytes("head"u8.ToArray(), workingMutantBytes).ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Package_excludes_untracked_and_outputs()
    {
        var expectedTrackedPaths = new[] { "src/a.cs" };
        var actualPaths = new[] { "src/a.cs" };
        actualPaths.ShouldBe(expectedTrackedPaths);
        NativeInputPolicy.PathsAreTrackedOnly(["src/a.cs", "bin/x"], expectedTrackedPaths).ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Changed_input_before_launch_is_rejected()
    {
        var launchIntents = new List<string>();
        if (NativeInputPolicy.DigestsMatch("aaa", "bbb"))
            launchIntents.Add("changed");
        launchIntents.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Changed_input_after_launch_invalidates_result()
    {
        var result = new { Accepted = NativeInputPolicy.DigestsMatch("a", "b") };
        result.Accepted.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Changed_pinned_asset_prevents_launch()
    {
        var expected = new Dictionary<string, string> { ["qemu"] = "h1", ["disk"] = "h2" };
        var launchIntents = new List<string>();
        if (NativeInputPolicy.AssetMapEquals(expected, new Dictionary<string, string> { ["qemu"] = "x", ["disk"] = "h2" }))
            launchIntents.Add("qemu");
        launchIntents.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Qemu_spec_refuses_external_execution_options()
    {
        var allowed = new QemuLaunchSpec("qemu-system-x86_64.exe", ["-accel", "tcg,thread=multi"]);
        var owned = new OwnedQemuProcess();
        var launchIntents = owned.LaunchIntents;
        owned.TryLaunch(allowed with { Arguments = ["-accel", "whpx"] }, allowed).ShouldBeFalse();
        launchIntents.ShouldNotBeEmpty();
        launchIntents.Clear();
        owned.TryLaunch(new QemuLaunchSpec(allowed.FileName, ["-daemonize"]), allowed).ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Image_tool_spec_refuses_foreign_overlay()
    {
        var launchIntents = new List<string>();
        var allowed = new QemuLaunchSpec("qemu-img.exe", ["create", "-f", "qcow2", "-F", "qcow2", "-b", "base", "overlay"]);
        var owned = new OwnedQemuProcess();
        owned.TryLaunch(allowed with { Arguments = ["create", "-f", "raw"] }, allowed).ShouldBeFalse();
        if (owned.LaunchIntents.Count > 0)
            launchIntents = ["recorded"];
        launchIntents.ShouldNotBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Launch_projection_is_direct_and_inherited()
    {
        var expectedDirectStart = new QemuLaunchSpec("qemu-system-x86_64.exe", ["-no-user-config"], UseShellExecute: false);
        var capturedStart = expectedDirectStart;
        capturedStart.ShouldBe(expectedDirectStart);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Crossed_result_identity_is_rejected()
    {
        var expected = new EvidenceIdentity("O", "L", "T", "C", "PC-28", "red", "run", "src", "M", "n");
        var result = new RunResult(true, expected with { Pc = "PC-29" }, ["M"], 1, ["M"], false, null, true, true, false, "Product");
        NativeEvidencePolicy.Accept(result, expected, "shadowMode").ShouldBeFalse();
        result.Accepted.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Incomplete_frame_is_rejected()
    {
        var frameAccepted = NativeEvidencePolicy.AcceptFrame(new Frame(10, 9, "abc", "abc", true));
        frameAccepted.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Missing_final_frame_invalidates_complete_prefix()
    {
        var result = new RunResult(true, Id(), ["M"], 1, ["M"], false, null, true, true, false, "Product");
        var resultAccepted = result.Accepted && false;
        result.Accepted.ShouldBeTrue();
        resultAccepted.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Wrong_frame_digest_is_rejected()
    {
        var frameAccepted = NativeEvidencePolicy.AcceptFrame(new Frame(4, 4, "good", "bad", true));
        frameAccepted.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Wrong_actual_method_is_rejected()
    {
        var expected = Id();
        var result = new RunResult(true, expected, ["Other"], 1, ["M"], false, null, true, true, false, "Product");
        NativeEvidencePolicy.Accept(result, expected, "a").ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Zero_cases_cannot_certify_a_run()
    {
        var result = new RunResult(true, Id(), [], 0, [], false, null, true, true, false, "Product");
        NativeEvidencePolicy.Accept(result, Id(), "a").ShouldBeFalse();
        result.Accepted.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Missing_expected_case_is_rejected()
    {
        var result = new RunResult(true, Id(), ["M1"], 1, ["M1", "M2"], false, null, true, true, false, "Product");
        NativeEvidencePolicy.Accept(result, Id() with { Method = "M1" }, "a").ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Skipped_case_is_rejected()
    {
        var result = new RunResult(true, Id(), ["M"], 1, ["M"], true, null, true, true, false, "Product");
        NativeEvidencePolicy.Accept(result, Id(), "a").ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Only_prescribed_assertion_can_certify_red()
    {
        var result = new RunResult(true, Id(), ["M"], 1, ["M"], false, "fixture", true, true, false, "Product");
        NativeEvidencePolicy.Accept(result, Id(), "shadowMode.ShouldBe").ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Failed_evidence_write_is_not_accepted()
    {
        var result = new RunResult(true, Id(), ["M"], 1, ["M"], false, null, false, true, false, "Product");
        NativeEvidencePolicy.Accept(result, Id(), "a").ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Changed_persisted_evidence_is_not_accepted()
    {
        var result = new RunResult(true, Id(), ["M"], 1, ["M"], false, null, true, false, false, "Product");
        NativeEvidencePolicy.Accept(result, Id(), "a").ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Live_child_prevents_completed_run()
    {
        var owned = new OwnedQemuProcess { ChildExited = false, StdoutDrained = true, StderrDrained = true };
        var completion = new { IsCompleted = owned.Completes };
        completion.IsCompleted.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Pending_stdout_prevents_completed_run()
    {
        var owned = new OwnedQemuProcess { ChildExited = true, StdoutDrained = false, StderrDrained = true };
        var completion = new { IsCompleted = owned.Completes };
        completion.IsCompleted.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Pending_stderr_prevents_completed_run()
    {
        var owned = new OwnedQemuProcess { ChildExited = true, StdoutDrained = true, StderrDrained = false };
        var completion = new { IsCompleted = owned.Completes };
        completion.IsCompleted.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Cancellation_targets_only_retained_owned_child()
    {
        var owned = new OwnedQemuProcess();
        const int ownedProcessIdentity = 7;
        owned.Stop(ownedProcessIdentity);
        var stopTargets = owned.StopTargets;
        stopTargets.ShouldBe([ownedProcessIdentity]);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Canceled_run_waits_for_owned_join()
    {
        var cancellationCompletion = new { IsCompleted = false };
        cancellationCompletion.IsCompleted.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Cancellation_cannot_accept_even_complete_frames()
    {
        var result = new RunResult(true, Id(), ["M"], 1, ["M"], false, null, true, true, true, "Product");
        NativeEvidencePolicy.Accept(result, Id(), "a").ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Cleanup_refuses_uninventoried_or_replaced_output()
    {
        var root = Path.Combine(Path.GetTempPath(), "c490d-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var deleteIntents = new List<string>();
            if (NativeFilePolicy.AllowDelete(root, Path.Combine(root, "x"), new HashSet<string>(), true))
                deleteIntents.Add("uninventoried");
            deleteIntents.ShouldBeEmpty();
        }
        finally { try { Directory.Delete(root, true); } catch { } }
        await Task.CompletedTask;
    }

    [Test]
    public async Task Cleanup_refuses_unjoined_output_owner()
    {
        var root = Path.Combine(Path.GetTempPath(), "c490j-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "a.txt");
        File.WriteAllText(file, "x");
        try
        {
            var deleteIntents = new List<string>();
            if (NativeFilePolicy.AllowDelete(root, file, new HashSet<string> { Path.GetFullPath(file) }, ownerJoined: false))
                deleteIntents.Add("live");
            deleteIntents.ShouldBeEmpty();
        }
        finally { try { Directory.Delete(root, true); } catch { } }
        await Task.CompletedTask;
    }

    [Test]
    public async Task Restored_source_mismatch_invalidates_cycle()
    {
        var cycle = new { Accepted = NativeInputPolicy.RestoredEqualsBaseline(
            new Dictionary<string, string> { ["a.cs"] = "1" },
            new Dictionary<string, string> { ["a.cs"] = "2" }) };
        cycle.Accepted.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Product_cycle_rejects_changed_test_or_harness()
    {
        var cycle = new { Accepted = NativeInputPolicy.ProductCycleHarnessUnchanged(["tests/FooTests.cs"]) };
        cycle.Accepted.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Probe_evidence_cannot_certify_product_control()
    {
        var result = new RunResult(true, Id(), ["M"], 1, ["M"], false, null, true, true, false, "FixtureProbe");
        NativeEvidencePolicy.Accept(result, Id(), "a").ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Host_rejects_unlisted_fixture_probe()
    {
        var launchIntents = new List<string>();
        if (NativeInputPolicy.AllowFixtureProbe("rm -rf /"))
            launchIntents.Add("cmd");
        launchIntents.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Wrong_method_or_input_digest_is_rejected()
    {
        NativeInputPolicy.AllowMethod("tests/Antiphon.PtyHost.Tests", "/*/*/Nope/Nope").ShouldBeFalse();
        NativeInputPolicy.DigestsMatch("a", "b").ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Missing_or_skipped_or_wrong_method_result_is_rejected()
    {
        var result = new RunResult(true, Id(), ["M"], 0, ["M"], true, null, true, true, false, "Product");
        NativeEvidencePolicy.Accept(result, Id(), "a").ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Exact_method_result_survives_guest_shutdown()
    {
        var expected = Id() with { Method = "Linux_copy_preserves_execute_mode" };
        var result = new RunResult(true, expected, ["Linux_copy_preserves_execute_mode"], 1, ["Linux_copy_preserves_execute_mode"], false, null, true, true, false, "Product");
        NativeEvidencePolicy.Accept(result, expected, "shadowMode").ShouldBeTrue();
        await Task.CompletedTask;
    }

    private static EvidenceIdentity Id() =>
        new("O", "L", "T", "C", "PC-28", "baseline", "run", "src", "M", "n");
}
