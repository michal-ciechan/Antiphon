using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Tests.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1984. W-1 pins the width formula, W-2 proves the lane overlaps, W-3 pins the family
/// floor, W-4 walks compiled bodies, and W-5 rejects a fixed temp path. Round 2 (review B-1):
/// W-10 rejects an executable written in process, and W-11 rejects a checkout-writing driver.
/// </summary>
[Category("Unit")]
public class ScriptModelSpawnLaneTests
{
    // D-5 keep list. Each one starts a child whose working directory is the shared checkout and
    // that writes there (deploy-server2.ps1 -Rolling, the jq roster's .antiphon/c973-jq-*
    // evidence, or the host-proof pwsh writer). Round 2 (W-11) returned the two HostJq methods
    // that run the real deploy-server2.ps1 -Rolling through C1025RolloutFixture.
    private static readonly HashSet<string> StrictKeep =
    [
        "HostJqPrerequisiteScriptTests.C1025_Preflight_precedes_every_phase",
        "HostJqPrerequisiteScriptTests.C1025_Preflight_is_fresh_for_each_entry",
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

    // S1 floor was HostJq. S2 appends the three Rolling classes.
    private static readonly string[] Families =
    [
        "HostJqPrerequisiteScriptTests",
        "RollingProductionMountTests",
        "RollingVolumeRecycleCensusChurnTests",
        "RollingVolumeRecycleScriptTests",
    ];

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

    // W-10 (round 2, review B-1). A Process.Start anywhere in this process forks a copy of every open
    // descriptor that lives until that child's exec, and Linux refuses to exec a file that is open for
    // writing anywhere (ETXTBSY). A wide-lane method must not write a file in process that a child then
    // execs; FixtureExecutable creates executables from a child instead. A walked body is a finding when
    // it writes in process and, in the same body, sets a unix mode, names a chmod, or calls a mode
    // helper, or when a "#!" literal or a string-returning "#!" builder reaches an in-process write
    // before any FixtureExecutable call. FixtureExecutable itself is not walked.
    [Test]
    public void Wide_lane_methods_write_no_executable_in_process()
    {
        var findings = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var method in WideLaneMethods())
        {
            foreach (var finding in InProcessExecutableWrites(method))
                findings.Add(finding);
        }

        findings.ShouldBeEmpty(string.Join(Environment.NewLine, findings));
    }

    [Test]
    [Arguments(nameof(ExecutableWriteFixture.Write_mode_then_start))]
    [Arguments(nameof(ExecutableWriteFixture.Write_then_child_chmod))]
    [Arguments(nameof(ExecutableWriteFixture.Write_shebang))]
    [Arguments(nameof(ExecutableWriteFixture.Write_built_script_then_start))]
    [Arguments(nameof(ExecutableWriteFixture.Copy_then_mode_helper))]
    [Arguments(nameof(ExecutableWriteFixture.Write_shebang_async))]
    public void In_process_executable_write_is_a_finding(string name)
    {
        InProcessExecutableWrites(FixtureMethod(typeof(ExecutableWriteFixture), name)).ShouldNotBeEmpty(name);
    }

    [Test]
    [Arguments(nameof(ExecutableWriteFixture.Materialize_then_start))]
    [Arguments(nameof(ExecutableWriteFixture.Write_then_interpret))]
    [Arguments(nameof(ExecutableWriteFixture.Materialize_built_script_and_write_data))]
    public void Out_of_process_or_interpreted_script_is_not_a_finding(string name)
    {
        InProcessExecutableWrites(FixtureMethod(typeof(ExecutableWriteFixture), name)).ShouldBeEmpty(name);
    }

    // Walked, never run.
    private static class ExecutableWriteFixture
    {
        public static void Write_mode_then_start(string path)
        {
            File.WriteAllText(path, "exit 0\n");
            File.SetUnixFileMode(path, (UnixFileMode)Convert.ToInt32("755", 8));
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false });
        }

        public static void Write_then_child_chmod(string path)
        {
            File.WriteAllText(path, "exit 0\n");
            using var chmod = Process.Start(new ProcessStartInfo("chmod") { UseShellExecute = false, ArgumentList = { "0755", path } });
        }

        public static void Write_shebang(string path) => File.WriteAllText(path, "#!/usr/bin/bash\nexit 0\n");

        public static void Write_built_script_then_start(string path)
        {
            File.WriteAllText(path, Script());
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false });
        }

        public static void Copy_then_mode_helper(string source, string path)
        {
            File.Copy(source, path);
            MakeExecutable(path);
        }

        public static async Task Write_shebang_async(string path) => await File.WriteAllTextAsync(path, "#!/bin/sh\nexit 0\n");

        public static void Materialize_then_start(string path)
        {
            FixtureExecutable.Write(path, "#!/usr/bin/bash\nexit 0\n");
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false });
        }

        public static void Write_then_interpret(string path)
        {
            File.WriteAllText(path, "exit 0\n");
            using var process = Process.Start(new ProcessStartInfo("/usr/bin/bash") { UseShellExecute = false, ArgumentList = { path } });
        }

        public static void Materialize_built_script_and_write_data(string root)
        {
            FixtureExecutable.Write(Path.Combine(root, "tool"), Script());
            File.WriteAllText(Path.Combine(root, "state.json"), "{}");
        }

        private static string Script() => "#!/usr/bin/bash\nexit 0\n";

        private static void MakeExecutable(string path) => File.SetUnixFileMode(path, (UnixFileMode)Convert.ToInt32("755", 8));
    }

    // W-11 (round 2). C1008WrapperFixture runs the real scripts/deploy-server2.ps1 -Rolling with the shared
    // checkout as its working directory, and that script writes .antiphon/rolling-server2/c727<hex> there,
    // so its callers keep the strict lane (D-5).
    private static readonly string[] CheckoutWriters = ["C1008WrapperFixture"];

    [Test]
    public void Wide_lane_methods_reach_no_checkout_writer()
    {
        var findings = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var method in WideLaneMethods())
        {
            foreach (var writer in ReachedCheckoutWriters(method))
                findings.Add(method.DeclaringType!.Name + "." + method.Name + " reaches " + writer);
        }

        findings.ShouldBeEmpty(string.Join(Environment.NewLine, findings));
    }

    [Test]
    public void Checkout_writer_reach_is_a_finding()
    {
        ReachedCheckoutWriters(FixtureMethod(typeof(CheckoutWriterFixture), nameof(CheckoutWriterFixture.Reach))).ShouldNotBeEmpty();
    }

    private static class CheckoutWriterFixture
    {
        public static async Task Reach()
        {
            using var wrapper = new C1008WrapperFixture();
            await wrapper.Run("retire-temp");
        }
    }

    private static MethodBase FixtureMethod(Type type, string name)
    {
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
        method.ShouldNotBeNull(name);
        return method;
    }

    private static IEnumerable<MethodInfo> WideLaneMethods()
    {
        foreach (var name in Families)
        {
            var type = typeof(ScriptModelSpawnLaneTests).Assembly.GetType("Antiphon.Tests.Scripts." + name);
            if (type is null)
                continue;
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<TestAttribute>() is not null
                    && LimiterNames(method).SequenceEqual([nameof(ScriptModelSpawnLimit)]))
                    yield return method;
            }
        }
    }

    internal static List<string> InProcessExecutableWrites(MethodBase root)
    {
        var findings = new List<string>();
        var helpers = new Dictionary<(Module Module, int Token), (bool ModeHelper, bool ScriptBuilder)>();
        foreach (var body in ReachedBodies(root))
        {
            var writes = new List<string>();
            var marks = new List<string>();
            var pending = false;
            foreach (var operand in Operands(body))
            {
                if (operand is string literal)
                {
                    if (literal.StartsWith("#!", StringComparison.Ordinal))
                        pending = true;
                    if (IsChmodLiteral(literal))
                        marks.Add("names chmod");
                    continue;
                }

                if (operand is not MethodBase called)
                    continue;
                if (IsFixtureExecutable(called.DeclaringType))
                {
                    pending = false;
                }
                else if (IsInProcessWrite(called))
                {
                    writes.Add(called.DeclaringType!.Name + "." + called.Name);
                    if (pending)
                        marks.Add("writes a \"#!\" script");
                    pending = false;
                }
                else if (IsModeSetter(called))
                {
                    marks.Add("sets a unix mode");
                }
                else if (InWalkScope(called))
                {
                    var helper = Helper(called, helpers);
                    if (helper.ModeHelper)
                        marks.Add("calls mode helper " + called.Name);
                    if (helper.ScriptBuilder)
                        pending = true;
                }
            }

            if (writes.Count > 0 && marks.Count > 0)
            {
                findings.Add((body.DeclaringType?.FullName ?? "?") + "." + body.Name + ": "
                    + string.Join(", ", writes.Distinct()) + " in process and " + string.Join(", ", marks.Distinct()));
            }
        }

        return findings;
    }

    internal static List<string> ReachedCheckoutWriters(MethodBase root)
    {
        var reached = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var body in ReachedBodies(root))
        {
            foreach (var operand in Operands(body))
            {
                var type = operand switch
                {
                    Type named => named,
                    MemberInfo member => member.DeclaringType,
                    _ => null,
                };
                for (var current = type; current is not null; current = current.DeclaringType)
                {
                    if (CheckoutWriters.Contains(current.Name, StringComparer.Ordinal))
                        reached.Add(current.Name);
                }
            }
        }

        return [.. reached];
    }

    private static (bool ModeHelper, bool ScriptBuilder) Helper(
        MethodBase method, Dictionary<(Module Module, int Token), (bool ModeHelper, bool ScriptBuilder)> cache)
    {
        if (cache.TryGetValue((method.Module, method.MetadataToken), out var known))
            return known;
        var own = new Stack<MethodBase>();
        own.Push(method);
        EnqueueCompilerMethods(method, own);
        bool mode = false, shebang = false;
        foreach (var part in own)
        {
            foreach (var operand in Operands(part))
            {
                if (operand is string literal && literal.StartsWith("#!", StringComparison.Ordinal))
                    shebang = true;
                else if (operand is MethodBase called && IsModeSetter(called))
                    mode = true;
            }
        }

        var result = (mode, shebang && method is MethodInfo { ReturnType: var returns } && returns == typeof(string));
        cache[(method.Module, method.MetadataToken)] = result;
        return result;
    }

    private static IEnumerable<MethodBase> ReachedBodies(MethodBase root)
    {
        var pending = new Stack<MethodBase>();
        var seen = new HashSet<(Module Module, int Token)>();
        pending.Push(root);
        EnqueueCompilerMethods(root, pending);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add((current.Module, current.MetadataToken)) || IsFixtureExecutable(current.DeclaringType))
                continue;
            var body = current.GetMethodBody();
            if (body is null)
                continue;
            yield return current;
            foreach (var member in ProcessSpawnLimitTests.ReferencedMembers(current, body))
            {
                if (member is MethodBase called && InWalkScope(called))
                {
                    pending.Push(called);
                    EnqueueCompilerMethods(called, pending);
                }
            }
        }
    }

    private static readonly Dictionary<short, OpCode> OpCodeByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => opCode.Value);

    /// <summary>The string literals and the members a body's IL names, in IL order.</summary>
    private static IEnumerable<object> Operands(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
            yield break;
        var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var offset = 0;
        while (offset < il.Length)
        {
            short value = il[offset++];
            if (value == 0xFE)
                value = unchecked((short)(0xFE00 | il[offset++]));
            if (!OpCodeByValue.TryGetValue(value, out var opCode))
                throw new InvalidOperationException($"{method.DeclaringType?.Name}.{method.Name}: unknown IL opcode 0x{value:X4} at {offset - 1}");
            var operandSize = opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => 4,
            };
            if (opCode.OperandType == OperandType.InlineString)
            {
                yield return method.Module.ResolveString(BitConverter.ToInt32(il, offset));
            }
            else if (opCode.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
            {
                MemberInfo? member = null;
                try { member = method.Module.ResolveMember(BitConverter.ToInt32(il, offset), typeArguments, methodArguments); }
                catch (ArgumentException) { }
                if (member is not null)
                    yield return member;
            }

            offset += operandSize;
        }
    }

    private static readonly HashSet<string> FileWrites = new(StringComparer.Ordinal)
    {
        "WriteAllText", "WriteAllTextAsync", "WriteAllBytes", "WriteAllBytesAsync", "WriteAllLines", "WriteAllLinesAsync",
        "AppendAllText", "AppendAllTextAsync", "AppendAllLines", "AppendAllLinesAsync", "AppendText", "Create", "CreateText",
        "OpenWrite", "Copy", "Replace",
    };

    private static readonly HashSet<string> FileInfoWrites = new(StringComparer.Ordinal)
    {
        "Create", "CreateText", "OpenWrite", "AppendText", "CopyTo",
    };

    private static bool IsInProcessWrite(MethodBase method)
    {
        var owner = method.DeclaringType;
        if (owner == typeof(File))
            return FileWrites.Contains(method.Name);
        if (owner == typeof(FileInfo))
            return FileInfoWrites.Contains(method.Name);
        return method is ConstructorInfo && (owner == typeof(FileStream) || owner == typeof(StreamWriter));
    }

    private static bool IsModeSetter(MethodBase method) =>
        (method.DeclaringType == typeof(File) && method.Name == "SetUnixFileMode")
        || (method.DeclaringType == typeof(FileSystemInfo) && method.Name == "set_UnixFileMode")
        || (method.DeclaringType == typeof(FileStreamOptions) && method.Name == "set_UnixCreateMode");

    private static bool IsChmodLiteral(string literal) =>
        literal == "chmod" || literal.EndsWith("/chmod", StringComparison.Ordinal) || literal.StartsWith("chmod ", StringComparison.Ordinal);

    private static bool IsFixtureExecutable(Type? type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (current == typeof(FixtureExecutable))
                return true;
        }

        return false;
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

    private static readonly Regex TempLiteralRegex = new(
        @"(?<![A-Za-z0-9_.\/])(?:/tmp|/dev/shm)/[A-Za-z0-9_.$*\-]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] ComparisonCalls =
    [
        "ShouldContain", "ShouldNotContain", "ShouldStartWith", "Contains",
        "StartsWith", "startsWith", "includes", "IndexOf",
    ];

    private static readonly string[] ExactTempTokens =
    [
        "/tmp/antiphon-pty-hosts",
        "/tmp/donor",
        "/tmp/escape",
        "/tmp/c994-command-sentinel",
    ];

    private static readonly Regex GuidInStatement = new(
        @"\{[^};]*Guid[^};]*\}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MktempOperand = new(
        @"mktemp\s+-d(?:\s+-p\s+\S+)?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// W-5 rule R1. A finding is <c>/tmp/&lt;name&gt;</c> or <c>/dev/shm/&lt;name&gt;</c> that is not
    /// a comment, a unique suffix, an <c>mkdtemp</c>/<c>mktemp -d</c> argument, a same-file stem of
    /// one of those, a comparison or case-arm, or an exact allowed token.
    /// </summary>
    internal static List<string> FixedTempFindings(string source)
    {
        var blanked = BlankFullLineComments(source);
        var matches = TempLiteralRegex.Matches(blanked);
        if (matches.Count == 0)
            return [];

        var stems = new List<string>();
        foreach (Match match in matches)
        {
            if (IsCreationArgument(blanked, match.Index))
            {
                var stem = UniqueStem(match.Value);
                if (stem.Length > 0)
                    stems.Add(stem);
            }
        }

        var findings = new List<string>();
        foreach (Match match in matches)
        {
            if (IsAllowedTempLiteral(blanked, match, stems))
                continue;
            findings.Add(LineNumber(blanked, match.Index) + ":" + match.Value);
        }

        return findings;
    }

    internal static bool ReachesProcessStart(MethodBase method)
    {
        var pending = new Stack<MethodBase>();
        var seen = new HashSet<(Module Module, int Token)>();
        pending.Push(method);
        EnqueueCompilerMethods(method, pending);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add((current.Module, current.MetadataToken)))
                continue;
            var body = current.GetMethodBody();
            if (body is null)
                continue;
            foreach (var member in ProcessSpawnLimitTests.ReferencedMembers(current, body))
            {
                if (IsSpawnMember(member))
                    return true;
                if (member is MethodBase called && InWalkScope(called))
                {
                    pending.Push(called);
                    EnqueueCompilerMethods(called, pending);
                }
            }
        }

        return false;
    }

    private static bool IsAllowedTempLiteral(string source, Match match, List<string> stems)
    {
        var literal = match.Value;
        if (HasUniqueSuffix(literal) || GuidFollows(source, match.Index + match.Length))
            return true;
        if (IsCreationArgument(source, match.Index))
            return true;
        foreach (var stem in stems)
        {
            if (literal.StartsWith(stem, StringComparison.Ordinal))
                return true;
        }

        if (IsComparisonOrCaseArm(source, match.Index))
            return true;
        return ExactTempTokens.Contains(literal, StringComparer.Ordinal);
    }

    private static bool HasUniqueSuffix(string literal) =>
        literal.Contains("XXXX", StringComparison.Ordinal)
        || literal.Contains("$RANDOM", StringComparison.Ordinal)
        || literal.Contains("$BASHPID", StringComparison.Ordinal)
        || literal.Contains("$$", StringComparison.Ordinal);

    private static string UniqueStem(string literal)
    {
        var cut = literal.Length;
        foreach (var marker in new[] { "$RANDOM", "$BASHPID", "XXXX", "$$" })
        {
            var at = literal.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0 && at < cut)
                cut = at;
        }

        return cut == 0 ? "" : literal[..cut];
    }

    private static bool GuidFollows(string source, int from)
    {
        var lineEnd = source.IndexOf('\n', from);
        if (lineEnd < 0)
            lineEnd = source.Length;
        var semi = source.IndexOf(';', from, lineEnd - from);
        var region = source[from..(semi >= 0 ? semi : lineEnd)];
        return region.Contains("Guid.NewGuid()", StringComparison.Ordinal) || GuidInStatement.IsMatch(region);
    }

    private static bool IsCreationArgument(string source, int literalIndex)
    {
        foreach (var name in OpenCallNames(source, literalIndex))
        {
            if (name is "mkdtemp" or "mkdtempSync")
                return true;
        }

        var lineStart = source.LastIndexOf('\n', Math.Max(0, literalIndex - 1));
        var prefix = source[(lineStart + 1)..literalIndex];
        return MktempOperand.IsMatch(prefix);
    }

    private static bool IsComparisonOrCaseArm(string source, int literalIndex)
    {
        var names = OpenCallNames(source, literalIndex);
        if (names.Count > 0 && ComparisonCalls.Contains(names[^1], StringComparer.Ordinal))
            return true;
        return IsCaseArm(source, literalIndex);
    }

    private static bool IsCaseArm(string source, int literalIndex)
    {
        var lineStart = source.LastIndexOf('\n', Math.Max(0, literalIndex - 1)) + 1;
        var lineEnd = source.IndexOf('\n', literalIndex);
        if (lineEnd < 0)
            lineEnd = source.Length;
        var before = source[lineStart..literalIndex];
        var depth = 0;
        foreach (var c in before)
        {
            if (c == '(')
                depth++;
            else if (c == ')')
                depth--;
        }

        if (depth > 0)
            return false;
        var after = source[literalIndex..lineEnd];
        var stop = after.IndexOf(';');
        var region = stop >= 0 ? after[..stop] : after;
        return region.Contains(')');
    }

    private static List<string> OpenCallNames(string source, int end)
    {
        var stack = new List<string>();
        var quote = '\0';
        for (var i = 0; i < end; i++)
        {
            var c = source[i];
            if (quote != '\0')
            {
                if (quote != '\'' && c == '\\')
                {
                    i++;
                    continue;
                }

                if (c == quote)
                    quote = '\0';
                continue;
            }

            if (c is '\'' or '"' or '`')
            {
                quote = c;
                continue;
            }

            if (c == '(')
                stack.Add(CallName(source, i));
            else if (c == ')' && stack.Count > 0)
                stack.RemoveAt(stack.Count - 1);
        }

        return stack;
    }

    private static string CallName(string source, int parenIndex)
    {
        var i = parenIndex - 1;
        while (i >= 0 && char.IsWhiteSpace(source[i]))
            i--;
        var end = i + 1;
        while (i >= 0 && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
            i--;
        return source[(i + 1)..end];
    }

    private static string BlankFullLineComments(string source)
    {
        var builder = new StringBuilder(source.Length);
        var start = 0;
        while (start <= source.Length)
        {
            var end = source.IndexOf('\n', start);
            if (end < 0)
                end = source.Length;
            var line = source[start..end];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('#'))
                builder.Append(' ', line.Length);
            else
                builder.Append(line);
            if (end >= source.Length)
                break;
            builder.Append('\n');
            start = end + 1;
        }

        return builder.ToString();
    }

    private static int LineNumber(string source, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < source.Length; i++)
        {
            if (source[i] == '\n')
                line++;
        }

        return line;
    }

    private static bool IsSpawnMember(MemberInfo member)
    {
        if (member is Type type)
            return IsScriptHarness(type);
        var owner = member.DeclaringType;
        if (owner is null)
            return false;
        if (IsScriptHarness(owner))
            return true;
        if (owner == typeof(Process) && member.Name == "Start")
            return true;
        return owner == typeof(ProcessStartInfo) && member is ConstructorInfo;
    }

    private static bool IsScriptHarness(Type type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (current.Name == "ScriptHarnessProcess")
                return true;
        }

        return false;
    }

    private static bool InWalkScope(MethodBase method)
    {
        var ns = method.DeclaringType?.Namespace;
        if (string.IsNullOrEmpty(ns))
            ns = Outermost(method.DeclaringType)?.Namespace;
        return ns is not null
            && (ns == "Antiphon.Tests" || ns.StartsWith("Antiphon.Tests.", StringComparison.Ordinal)
                || ns == "Antiphon.TestSupport" || ns.StartsWith("Antiphon.TestSupport.", StringComparison.Ordinal));
    }

    private static Type? Outermost(Type? type)
    {
        while (type?.DeclaringType is not null)
            type = type.DeclaringType;
        return type;
    }

    private static void EnqueueCompilerMethods(MethodBase method, Stack<MethodBase> pending)
    {
        var owner = method.DeclaringType;
        if (owner is null || method.Name.StartsWith('<'))
            return;
        var token = "<" + method.Name + ">";
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var sibling in owner.GetMethods(flags))
        {
            if (sibling.Name.StartsWith(token, StringComparison.Ordinal))
                pending.Push(sibling);
        }

        foreach (var nested in owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            var owned = nested.Name.StartsWith(token, StringComparison.Ordinal);
            foreach (var nestedMethod in nested.GetMethods(flags).Cast<MethodBase>().Concat(nested.GetConstructors(flags)))
            {
                if (owned || nestedMethod.Name.StartsWith(token, StringComparison.Ordinal))
                    pending.Push(nestedMethod);
            }
        }
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
