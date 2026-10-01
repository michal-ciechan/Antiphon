using Antiphon.HostCleanup;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class HostCleanupPolicyTests
{
    [Test]
    public void Unknown_family_is_reported_without_delete()
    {
        var f = new HostCleanupFixture();
        var result = f.Planner().Decide(f.Candidate("/tmp/antiphon-random"));
        result.Reason.ShouldBe("family_unknown", "C826.Unknown_family_is_reported_without_delete");
        result.Disposition.ShouldBe(CleanupDisposition.Unknown);
    }

    [Test]
    public void Runtime_deny_overrides_owned_family()
    {
        var f = new HostCleanupFixture();
        var old = f.Clock.UtcNow.AddHours(-25);
        foreach (var denied in new[] { ".git", ".claude", ".codex", ".grok", "build-slots", "runner-state", "reports" })
        {
            var candidate = f.Candidate(entries: [HostCleanupFixture.File(
                HostCleanupFixture.CheckpointPath + "/" + denied + "/sentinel", old, 1, 1)]);
            var decision = f.Planner().Decide(candidate);
            decision.Reason.ShouldBe("protected_runtime", $"C826.Runtime_deny_overrides_owned_family:{denied}");
        }
        f.ExtraDenied.Add(HostCleanupFixture.CheckpointPath + "/configured-runtime");
        f.Planner().Decide(f.Candidate()).Reason.ShouldBe("protected_runtime",
            "C826.Runtime_deny_overrides_owned_family:configured-alias");
    }

    [Test]
    public void Parent_containing_runtime_state_is_kept()
    {
        var f = new HostCleanupFixture();
        var child = HostCleanupFixture.Directory(HostCleanupFixture.CheckpointPath + "/.git");
        f.Planner().Decide(f.Candidate(entries: [child])).Reason.ShouldBe("protected_runtime",
            "C826.Parent_containing_runtime_state_is_kept");
    }

    [Test]
    public void Child_of_protected_root_is_kept()
    {
        var f = new HostCleanupFixture();
        var path = "/tmp/antiphon-pty-hosts/" + CleanupPath.Leaf(HostCleanupFixture.CheckpointPath);
        f.Planner().Decide(f.Candidate(path)).Reason.ShouldBe("protected_runtime",
            "C826.Child_of_protected_root_is_kept");
    }

    [Test]
    public void Traversal_cannot_escape_configured_root()
    {
        var f = new HostCleanupFixture();
        foreach (var path in new[] { "/tmp/../etc/passwd", "/tmp/./c723-x", "/tmp//c723-x",
            "//tmp/c723-x", "/tmpx/c723-x" })
        {
            var result = f.Planner().Decide(f.Candidate(path));
            result.Disposition.ShouldNotBe(CleanupDisposition.Eligible,
                "C826.Traversal_cannot_escape_configured_root");
        }
    }

    [Test]
    public void Link_at_any_depth_is_kept()
    {
        var f = new HostCleanupFixture();
        foreach (var path in new[] { HostCleanupFixture.CheckpointPath,
            HostCleanupFixture.CheckpointPath + "/nested", HostCleanupFixture.CheckpointPath + "/nested/deeper" })
        {
            var entry = HostCleanupFixture.Directory(path, link: true);
            f.Planner().Decide(f.Candidate(entries: [entry])).Reason.ShouldBe("linked_entry",
                "C826.Link_at_any_depth_is_kept");
        }
    }

    [Test]
    public void Foreign_mount_is_kept()
    {
        var f = new HostCleanupFixture();
        var entry = HostCleanupFixture.Directory(HostCleanupFixture.CheckpointPath + "/mount", mount: true);
        f.Planner().Decide(f.Candidate(entries: [entry])).Reason.ShouldBe("mount_crossing",
            "C826.Foreign_mount_is_kept");
    }

    [Test]
    public void New_nested_file_keeps_old_directory()
    {
        var f = new HostCleanupFixture();
        var fresh = HostCleanupFixture.File(HostCleanupFixture.CheckpointPath + "/nested/.hidden",
            f.Clock.UtcNow.AddHours(-1), 1, 1);
        var candidate = f.Candidate(created: f.Clock.UtcNow.AddHours(-48), entries: [fresh]);
        f.Planner().Decide(candidate).Reason.ShouldBe("recent_write",
            "C826.New_nested_file_keeps_old_directory");
    }

    [Test]
    public void Exactly_twenty_four_hours_is_kept()
    {
        var f = new HostCleanupFixture();
        var cutoff = f.Clock.UtcNow.AddHours(-24);
        var equal = f.Candidate(created: cutoff.AddTicks(-1),
            entries: [HostCleanupFixture.File(HostCleanupFixture.CheckpointPath + "/file", cutoff, 1, 1)]);
        f.Planner().Decide(equal).Reason.ShouldBe("recent_write",
            "C826.Exactly_twenty_four_hours_is_kept:equal");
        var older = equal with { Entries = [HostCleanupFixture.File(
            HostCleanupFixture.CheckpointPath + "/file", cutoff.AddTicks(-1), 1, 1)] };
        f.Planner().Decide(older).Disposition.ShouldBe(CleanupDisposition.Eligible,
            "C826.Exactly_twenty_four_hours_is_kept:minus-tick");
    }

    [Test]
    public void Future_timestamp_is_kept()
    {
        var f = new HostCleanupFixture();
        var future = HostCleanupFixture.File(HostCleanupFixture.CheckpointPath + "/future",
            f.Clock.UtcNow.AddHours(1), 1, 1);
        f.Planner().Decide(f.Candidate(entries: [future])).Reason.ShouldBe("recent_write",
            "C826.Future_timestamp_is_kept");
    }

    [Test]
    public void Empty_root_requires_old_valid_marker()
    {
        var f = new HostCleanupFixture();
        var empty = f.Candidate(entries: []);
        f.Planner().Decide(empty).Disposition.ShouldBe(CleanupDisposition.Eligible,
            "C826.Empty_root_requires_old_valid_marker:valid");
        f.Planner().Decide(empty with { HasValidMarker = false }).Disposition.ShouldNotBe(
            CleanupDisposition.Eligible, "C826.Empty_root_requires_old_valid_marker:unmarked");
        f.Planner().Decide(empty with { CreatedUtc = f.Clock.UtcNow }).Reason.ShouldBe("recent_write",
            "C826.Empty_root_requires_old_valid_marker:fresh");
    }

    [Test]
    public void Unreadable_entry_is_not_empty()
    {
        var f = new HostCleanupFixture();
        var unreadable = HostCleanupFixture.Directory(HostCleanupFixture.CheckpointPath + "/private", readable: false);
        f.Planner().Decide(f.Candidate(entries: [unreadable])).Disposition.ShouldBe(CleanupDisposition.Unknown,
            "C826.Unreadable_entry_is_not_empty");
    }

    [Test]
    public void Traversal_budget_exhaustion_is_unknown()
    {
        var f = new HostCleanupFixture { Limits = new CleanupLimits(MaxDescendantsPerCandidate: 2) };
        var candidate = f.Candidate() with { TraversedCount = 3 };
        f.Planner().Decide(candidate).Disposition.ShouldBe(CleanupDisposition.Unknown,
            "C826.Traversal_budget_exhaustion_is_unknown");
    }

    [Test]
    public void Newest_write_includes_hidden_files_and_marker()
    {
        var f = new HostCleanupFixture();
        foreach (var name in new[] { ".hidden", ".checkpoint-test-root.json" })
        {
            var entry = HostCleanupFixture.File(HostCleanupFixture.CheckpointPath + "/" + name,
                f.Clock.UtcNow.AddHours(-1), 1, 1);
            f.Planner().Decide(f.Candidate(entries: [entry])).Reason.ShouldBe("recent_write",
                "C826.Newest_write_includes_hidden_files_and_marker");
        }
    }

    [Test]
    public void Configuration_cannot_relax_hard_guards()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new CleanupLimits(MaxAttempts: 0).Validate(),
            "C826.Configuration_cannot_relax_hard_guards:limits");
        Should.Throw<ArgumentException>(() => new CleanupFamilyRegistry([]),
            "C826.Configuration_cannot_relax_hard_guards:roots");
        Should.Throw<ArgumentException>(() => new CleanupFamilyRegistry(
            [new CleanupRoot("/tmp/*", "storage-a", CleanupFamily.Probe)]),
            "C826.Configuration_cannot_relax_hard_guards:glob");
    }

    [Test]
    public void Unregistered_msbuild_testcontainers_and_tmp_are_unknown()
    {
        var f = new HostCleanupFixture();
        foreach (var name in new[] { "MSBuildTemp123", "testcontainers", "standalone.tmp" })
            f.Planner().Decide(f.Candidate("/tmp/" + name)).Disposition.ShouldBe(CleanupDisposition.Unknown,
                "C826.Unregistered_msbuild_testcontainers_and_tmp_are_unknown");
    }

    [Test]
    public void Private_cache_requires_exact_package_and_scratch_ownership()
    {
        var f = new HostCleanupFixture();
        f.Roots.Clear();
        f.Roots.Add(new CleanupRoot("/work/cache", "storage-a", CleanupFamily.PrivateCache));
        var candidate = f.Candidate("/work/cache/private-pair") with { PrivateCachePairOwned = false };
        f.Planner().Decide(candidate).Reason.ShouldBe("private_cache_pair_required",
            "C826.Private_cache_requires_exact_package_and_scratch_ownership:unowned");
        f.Planner().Decide(candidate with { PrivateCachePairOwned = true }).Disposition.ShouldBe(
            CleanupDisposition.Eligible, "C826.Private_cache_requires_exact_package_and_scratch_ownership:pair");
    }

    [Test]
    public void Retained_evidence_is_never_scratch()
    {
        var f = new HostCleanupFixture();
        var candidate = f.Candidate() with { IsRetainedEvidence = true };
        f.Planner().Decide(candidate).Reason.ShouldBe("retained_evidence",
            "C826.Retained_evidence_is_never_scratch");
    }

    [Test]
    public void Per_host_roots_are_explicit_and_nonoverlapping()
    {
        Should.Throw<ArgumentException>(() => new CleanupFamilyRegistry(
            [new CleanupRoot("/tmp", "storage-a", CleanupFamily.Checkpoint),
             new CleanupRoot("/tmp/nested", "storage-a", CleanupFamily.Probe)]),
            "C826.Per_host_roots_are_explicit_and_nonoverlapping");
    }

    [Test]
    public void All_registered_owned_families_can_be_eligible()
    {
        var examples = new (CleanupFamily family, string path)[]
        {
            (CleanupFamily.Checkpoint, HostCleanupFixture.CheckpointPath),
            (CleanupFamily.Probe, "/tmp/c527-probe-1"),
            (CleanupFamily.LandVerify, "/tmp/antiphon-land-verify-1"),
            (CleanupFamily.TaskScratch, "/tmp/task-1"),
            (CleanupFamily.WorkScratch, "/tmp/work-1"),
            (CleanupFamily.BuildOutput, "/tmp/bin-c826"),
            (CleanupFamily.PrivateCache, "/tmp/private-pair"),
            (CleanupFamily.TestSandbox, "/tmp/sandbox-1")
        };
        foreach (var (family, path) in examples)
        {
            var f = new HostCleanupFixture();
            f.Roots.Clear();
            f.Roots.Add(new CleanupRoot("/tmp", "storage-a", family));
            var candidate = f.Candidate(path, entries: []) with { PrivateCachePairOwned = true };
            f.Planner().Decide(candidate).Disposition.ShouldBe(CleanupDisposition.Eligible,
                $"C826.All_registered_owned_families_can_be_eligible:{family}");
        }
    }

    [Test]
    public void Older_owned_root_is_eligible()
    {
        var f = new HostCleanupFixture();
        var decision = f.Planner().Decide(f.Candidate());
        decision.Disposition.ShouldBe(CleanupDisposition.Eligible,
            "C826.Older_owned_root_is_eligible");
        decision.ReservedBytes.ShouldBe(10);
    }

    [Test]
    public void Daily_plan_uses_injected_observations_only()
    {
        var f = new HostCleanupFixture();
        var a = f.Candidate();
        var b = f.Candidate("/tmp/c723-ffffffffffffffffffffffffffffffff") with
        { Identity = new CleanupIdentity("storage-a", "file-2", "generation-2") };
        var first = f.Plan(b, a).Decisions.Select(x => (x.Path, x.Reason)).ToArray();
        var second = f.Plan(a, b).Decisions.Select(x => (x.Path, x.Reason)).ToArray();
        first.ShouldBe(second, "C826.Daily_plan_uses_injected_observations_only");
    }
}
