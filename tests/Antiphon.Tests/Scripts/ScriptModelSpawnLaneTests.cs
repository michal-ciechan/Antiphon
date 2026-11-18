using System.Diagnostics;
using System.Reflection;
using Antiphon.Tests.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1984 S1a. The lane resolver, the temp-path scanner and the process-start walk are
/// stubs, so W-1 (except rows whose expected width is already 1), W-2, W-3, the W-4
/// negative control and the W-5 controls fail for that reason.
/// </summary>
[Category("Unit")]
public class ScriptModelSpawnLaneTests
{
    // D-5 keep list for RollingVolumeRecycleScriptTests. S1's floor is HostJq only, so these
    // names are not consulted until S2 adds that class. Each one starts a child whose working
    // directory is the shared checkout and that writes there (deploy-server2.ps1 -Rolling,
    // the jq roster's .antiphon/c973-jq-* evidence, or the host-proof pwsh writer).
    private static readonly HashSet<string> StrictKeep =
    [
        "RollingVolumeRecycleScriptTests.C1087_Read_failures_name_their_cause",
        "RollingVolumeRecycleScriptTests.C1087_Project_resolves_by_repository_identity",
        "RollingVolumeRecycleScriptTests.C1693_Retire_temp_census_ignores_standing_runner_churn_between_passes",
        "RollingVolumeRecycleScriptTests.C1087_Census_filters_open_work_and_pending_lands",
        "RollingVolumeRecycleScriptTests.C1087_Check_census_phase_is_read_only",
        "RollingVolumeRecycleScriptTests.C1008_Present_or_unknown_temp_keeps_null_refusal",
        "RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse",
        "RollingVolumeRecycleScriptTests.C1353_Wrapper_waits_for_a_bound_land_then_proceeds",
        "RollingVolumeRecycleScriptTests.C1008_Same_sha_and_partial_retries_are_safe",
        "RollingVolumeRecycleScriptTests.C1008_Wrapper_option_manifest_is_strict",
        "RollingVolumeRecycleScriptTests.C1008_Legacy_rolling_and_jq_rosters_remain_present",
        "RollingVolumeRecycleScriptTests.C1008_Legacy_rolling_and_jq_rosters_remain_absent",
        "RollingVolumeRecycleScriptTests.C1008_Legacy_rolling_and_jq_rosters_remain_missing_shell",
        "RollingVolumeRecycleScriptTests.C1008_Legacy_rolling_and_jq_rosters_remain_failing_shell",
        "RollingVolumeRecycleScriptTests.C1008_Wrapper_refusal_receipts_do_not_leak_secrets",
        "RollingVolumeRecycleScriptTests.C1008_Retired_absent_null_is_accepted",
        "RollingVolumeRecycleScriptTests.C1008_Wrapper_retired_absent_null_is_accepted",
        "RollingVolumeRecycleScriptTests.C1317_Census_tolerates_a_task_that_settles_without_a_land",
        "RollingVolumeRecycleScriptTests.C1319_Census_tolerates_a_pass0_settled_task_that_leaves_before_pass1",
        "RollingVolumeRecycleScriptTests.C1324_Census_tolerates_a_no_land_task_that_appears_in_pass1",
        "RollingVolumeRecycleScriptTests.C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance",
        "RollingVolumeRecycleScriptTests.C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail",
        "RollingVolumeRecycleScriptTests.C1361_Wrapper_census_tolerates_open_status_changes_between_passes",
        "RollingVolumeRecycleScriptTests.C1697_Wrapper_standing_census_compares_only_rows_bound_to_server2",
        "RollingVolumeRecycleScriptTests.C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts",
        "RollingVolumeRecycleScriptTests.C1374_Wrapper_census_recovers_transient_reads",
        "RollingVolumeRecycleScriptTests.C1008_Host_proof_admits_only_an_authenticated_private_origin",
        "RollingVolumeRecycleScriptTests.C1008_Host_proof_file_omits_credentials",
    ];

    // S1 floor. S2 appends the three Rolling classes.
    private static readonly string[] Families = ["HostJqPrerequisiteScriptTests"];

    [Test]
    [Arguments(24, "", "", false, 6)]
    [Arguments(4, "", "", false, 2)]
    [Arguments(8, "", "", false, 2)]
    [Arguments(16, "", "", false, 4)]
    [Arguments(64, "", "", false, 6)]
    [Arguments(24, "2", "", false, 2)]
    [Arguments(24, "6", "", false, 6)]
    [Arguments(24, "12", "", false, 6)]
    [Arguments(24, "6", "1", false, 1)]
    [Arguments(24, "6", "0", false, 1)]
    [Arguments(24, "6", "9", false, 8)]
    [Arguments(24, "6", "x", false, 6)]
    [Arguments(24, "6", "", true, 1)]
    public void Lane_width_is_six_on_a_24_core_linux_host(
        int processorCount, string slotCpu, string overrideValue, bool isWindows, int expected)
    {
        var slot = slotCpu.Length == 0 ? null : slotCpu;
        var over = overrideValue.Length == 0 ? null : overrideValue;
        ScriptModelSpawnLimit.Resolve(processorCount, over, slot, isWindows).ShouldBe(expected);
    }

    private static readonly TaskCompletionSource<long> ProbeA =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static readonly TaskCompletionSource<long> ProbeB =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static int ProbeGaveUp;

    [Test]
    [Timeout(150_000)]
    [ParallelLimiter<ScriptModelSpawnLimit>]
    public async Task Probe_A_overlaps_probe_B() => await Overlap(ProbeA, ProbeB);

    [Test]
    [Timeout(150_000)]
    [ParallelLimiter<ScriptModelSpawnLimit>]
    public async Task Probe_B_overlaps_probe_A() => await Overlap(ProbeB, ProbeA);

    private static async Task Overlap(TaskCompletionSource<long> mine, TaskCompletionSource<long> peer)
    {
        var started = Stopwatch.GetTimestamp();
        mine.TrySetResult(started);
        if (Volatile.Read(ref ProbeGaveUp) != 0)
            throw new InvalidOperationException("peer already gave up");
        var completed = await Task.WhenAny(peer.Task, Task.Delay(TimeSpan.FromSeconds(120))).ConfigureAwait(false);
        if (completed != peer.Task)
        {
            Interlocked.Exchange(ref ProbeGaveUp, 1);
            throw new TimeoutException("peer did not arrive within 120s");
        }

        if (Volatile.Read(ref ProbeGaveUp) != 0)
            throw new InvalidOperationException("peer already gave up");
        var other = peer.Task.Result;
        var delta = Stopwatch.GetElapsedTime(Math.Min(started, other), Math.Max(started, other));
        delta.ShouldBeLessThan(TimeSpan.FromSeconds(1));
    }

    [Test]
    public void Script_model_families_use_the_wide_lane()
    {
        var misses = new List<string>();
        foreach (var name in Families)
        {
            var type = typeof(ScriptModelSpawnLaneTests).Assembly.GetType("Antiphon.Tests.Scripts." + name);
            if (type is null)
            {
                misses.Add(name + " was not found");
                continue;
            }

            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<TestAttribute>() is null)
                    continue;
                var limiters = LimiterNames(method);
                var key = type.Name + "." + method.Name;
                if (limiters.Count > 1)
                    misses.Add(key + " carries " + string.Join(" and ", limiters));
                else if (limiters.Count == 1 && limiters[0] == nameof(ScriptModelSpawnLimit))
                    continue;
                else if (limiters.Count == 1 && limiters[0] == nameof(ProcessSpawnLimit) && StrictKeep.Contains(key))
                    continue;
                else if (limiters.Count == 0 && !ReachesProcessStart(method))
                    continue;
                else
                    misses.Add(key + " carries [" + string.Join(", ", limiters) + "] and is not on the wide lane or the D-5 keep list");
            }
        }

        misses.ShouldBeEmpty(string.Join(Environment.NewLine, misses));
    }

    [Test]
    public void Unlimited_script_model_methods_reach_no_process_start()
    {
        var hits = new List<string>();
        foreach (var name in Families)
        {
            var type = typeof(ScriptModelSpawnLaneTests).Assembly.GetType("Antiphon.Tests.Scripts." + name);
            if (type is null)
                continue;
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<TestAttribute>() is null || LimiterNames(method).Count > 0)
                    continue;
                if (ReachesProcessStart(method))
                    hits.Add(type.Name + "." + method.Name);
            }
        }

        hits.ShouldBeEmpty(string.Join(Environment.NewLine, hits));
    }

    [Test]
    public void Unlimited_script_model_methods_reject_a_method_that_reaches_process_start()
    {
        var method = typeof(ProcessStartFixture).GetMethod(nameof(ProcessStartFixture.Reach), BindingFlags.Public | BindingFlags.Static);
        method.ShouldNotBeNull();
        ReachesProcessStart(method).ShouldBeTrue();
    }

    private static class ProcessStartFixture
    {
        public static void Reach()
        {
            using var process = Process.Start(new ProcessStartInfo("true") { UseShellExecute = false });
            _ = process;
        }
    }

    [Test]
    public void Wide_lane_sources_name_no_fixed_temp_path()
    {
        var findings = new List<string>();
        foreach (var file in WideLaneSourceFiles())
        {
            foreach (var finding in FixedTempFindings(File.ReadAllText(file)))
                findings.Add(Path.GetRelativePath(CheckpointFixtures.RepoRoot, file) + ": " + finding);
        }

        findings.ShouldBeEmpty(string.Join(Environment.NewLine, findings));
    }

    [Test]
    [Arguments("Directory.CreateDirectory(\"/tmp/foo\")")]
    [Arguments("mkdir -p /tmp/foo")]
    [Arguments("\"/tmp/antiphon-pty-hosts-x\"")]
    [Arguments("\"/tmp/c1105-audit-x\"")]
    [Arguments("x.ShouldContain(\"ok\"); File.WriteAllText(\"/tmp/foo\", \"\")")]
    public void Fixed_temp_literal_is_a_finding(string source)
    {
        FixedTempFindings(source).ShouldNotBeEmpty(source);
    }

    // S1a stub. Returning no findings makes every control row red and leaves the scan green.
    internal static List<string> FixedTempFindings(string source)
    {
        _ = source;
        return [];
    }

    // S1a stub. A method that reaches Process.Start is not rejected, so the negative control is red.
    internal static bool ReachesProcessStart(MethodBase method)
    {
        _ = method;
        return false;
    }

    private static List<string> LimiterNames(MethodInfo method)
    {
        var names = new List<string>();
        foreach (var attribute in method.GetCustomAttributes(inherit: false))
        {
            var type = attribute.GetType();
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(ParallelLimiterAttribute<>))
                continue;
            names.Add(type.GetGenericArguments()[0].Name);
        }

        return names;
    }

    internal static IEnumerable<string> WideLaneSourceFiles()
    {
        var root = CheckpointFixtures.RepoRoot;
        var classes = new[]
        {
            "tests/Antiphon.Tests/Scripts/HostJqPrerequisiteScriptTests.cs",
            "tests/Antiphon.Tests/Scripts/RollingProductionMountTests.cs",
            "tests/Antiphon.Tests/Scripts/RollingVolumeRecycleCensusChurnTests.cs",
            "tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs",
        };
        foreach (var relative in classes)
            yield return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

        var fixtures = Path.Combine(root, "scripts", "fixtures");
        foreach (var prefix in new[] { "c1008-", "c1087-", "c1105-", "c1192-" })
        {
            foreach (var file in Directory.EnumerateFiles(fixtures, prefix + "*").OrderBy(path => path, StringComparer.Ordinal))
                yield return file;
        }
    }
}
