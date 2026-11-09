using System.Reflection;
using System.Reflection.Emit;
using Antiphon.Tests.AgentTui;
using Antiphon.Tests.Agents;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests;

/// <summary>CARD-0050 S5: non-git process-spawning classes share a 1-wide lane; CARD-1421: git-only classes a 2-4-wide lane; CARD-1747: checkpoint-tool classes a 2-wide lane.</summary>
[Category("Unit")]
public class ProcessSpawnLimitTests
{
    [Test]
    public void Caps_concurrent_process_spawning_tests_at_one()
    {
        new ProcessSpawnLimit().Limit.ShouldBe(1);
    }

    [Test]
    public void C1514_Checkpoint_workers_are_separate_from_the_git_lane()
    {
        var matrix = typeof(AgentTaskLandCheckpointMatrixTests);
        Attribute.GetCustomAttribute(matrix, typeof(ParallelLimiterAttribute<GitProcessSpawnLimit>))
            .ShouldNotBeNull("the 41 Git-only cases must use the existing Git lane");
        Attribute.GetCustomAttribute(matrix, typeof(ParallelLimiterAttribute<ProcessSpawnLimit>)).ShouldBeNull();
        var worker = matrix.Assembly.GetType("Antiphon.Tests.Application.AgentTaskLandCheckpointWorkerTests")
            .ShouldNotBeNull("both real worker-death cases need their own strict class");
        Attribute.GetCustomAttribute(worker, typeof(ParallelLimiterAttribute<ProcessSpawnLimit>)).ShouldNotBeNull();
        Attribute.GetCustomAttribute(worker, typeof(ParallelLimiterAttribute<GitProcessSpawnLimit>)).ShouldBeNull();
        const string method = "C665_WorkerDeathKeepsRecordedTreeAcrossActualRegistrationDrop";
        matrix.GetMethod(method).ShouldBeNull();
        worker.GetMethod(method).ShouldNotBeNull()
            .GetCustomAttributes(typeof(ArgumentsAttribute), inherit: true).Length.ShouldBe(2, "both worker cuts remain");
    }

    // A floor, not a census: the strict lane (pty, pwsh, cmd, dotnet worker, fake agents, web host, session runtime).
    private static readonly Type[] StrictClasses =
    [
        typeof(AgentTaskLandPublicationTests),
        typeof(AgentTaskLandVerifierTests),
        typeof(CardReviewServiceIntegrationTests),
        typeof(Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests),
        typeof(AgentTaskLandRecoveryTests),
        typeof(AgentTaskLandCheckpointWorkerTests),
        typeof(C1514LandingSplitBudgetTests),
        typeof(Antiphon.Tests.Infrastructure.LandingAdmissionControlTests),
        typeof(Antiphon.Tests.Infrastructure.LandingRemovalControlTests),
        typeof(Antiphon.Tests.Infrastructure.LandingSourceBoundaryControlTests),
        typeof(AgentTaskLandStageOutcomeTests),
        typeof(DelegationWorktreeTests),
        typeof(Antiphon.Tests.Infrastructure.WorktreeRemovalAuthorityTests),
        typeof(Antiphon.Tests.Infrastructure.RepositoryMutationLeaseTests),
        typeof(SessionRunnerRuntimeTests),
        typeof(RawPtyAdapterTests),
        typeof(CodexAdapterLocalShellTests),
        typeof(ClaudeAdapterLocalShellTests),
        typeof(CodexAdapterIntegrationTests),
        typeof(ClaudeAdapterIntegrationTests),
        typeof(RunnerProcessProbeTests),
        typeof(AgentSessionServiceIntegrationTests),
        typeof(SessionMessageQueuePtyIntegrationTests),
        typeof(SessionMessageQueueGrokPtyIntegrationTests),
        typeof(DelegationBriefCeilingPtyTests),
        typeof(GrokDelegateEndToEndTests),
        typeof(SlashCommandMenuReconciliationTests),
        typeof(ClaudeRealCliStubProxyCanaryTests),
        typeof(GrokRealCliStubProxyCanaryTests),
        typeof(CodexRealCliStubProxyCanaryTests),
        typeof(TestDurationTripwireTests),
        typeof(SessionQueueReceiptPlumbingTests),
        typeof(TaskSupervisionReceiptTests),
        typeof(TestClassificationPolicyTests),
        typeof(TestDbFixtureLazyInitializationTests),
        typeof(CheckCompactionCrashTests),
        typeof(CheckNoteDeliveryHandoffTests),
        typeof(Antiphon.Tests.Scripts.RunCheckpointScriptTests),
        typeof(Antiphon.Tests.Scripts.BuildSlotScriptTests),
        typeof(Antiphon.Tests.Scripts.NativeLinuxPolicyExecutionTests),
        typeof(Antiphon.Tests.Scripts.NativeLinuxPolicyMatcherParityTests),
        // CARD-1747: checkpoint-tool classes name CheckpointToolSpawnLimit.
        // These seven start a child with the shared checkout as its working directory
        // (RepeatHost output, run-checkpoint.ps1, or dotnet against RepoRoot). They stay on
        // the strict lane so two of them cannot write one checkout.
        typeof(Antiphon.Tests.Checkpoints.RerunFilterHostTests),
        typeof(Antiphon.Tests.Checkpoints.CheckpointSourceExecutionTests),
        typeof(Antiphon.Tests.Checkpoints.CheckpointRepeatBuildIsolationTests),
        typeof(Antiphon.Tests.Checkpoints.CheckpointTerminalReceiptTests),
        typeof(Antiphon.Tests.Checkpoints.CheckpointRepeatHostTests),
        typeof(Antiphon.Tests.Checkpoints.CheckpointStatePublicationTests),
        // CARD-1748: its two script cases run test-run-checkpoint.ps1 under pwsh from RepoRoot.
        typeof(Antiphon.Tests.Checkpoints.PhaseJournalKillDiagnosticTests),
        // CARD-1421: git-only, but its stale-lock assertions read the host-wide git census. CARD-1436
        // (master 76cd76fa) stopped counting a git that exits mid-census; a live git older than the lock
        // still counts, so the class stays strict and is not moved (repair task 4721be7c).
        typeof(AgentTaskLandIndexLockTests),
        // CARD-1421 repair (Review b529639c D-1): these three call DirectoryLink.TryCreate, which runs
        // Scripts/Fixtures/directory-link.ps1 through ScriptHarnessProcess on Windows, so they start pwsh.
        typeof(Antiphon.Tests.Infrastructure.WorktreeGuardedCleanupTests),
        typeof(AgentTaskLandHalfResetWindowsTests),
        typeof(AgentTaskLandHoldVisibilityTests),
        // CARD-1610 S4a: runs scripts/delegate.ps1 under pwsh per case.
        typeof(TaskCeilingScriptTests),
        // CARD-1745: each witness starts pwsh for the Linux SHA gate harness.
        typeof(LinuxServerRestartGateTests),
    ];

    // CARD-1421 Appendix B: strict-roster classes whose only children are git, in fixture-owned repositories.
    private static readonly Type[] GitOnlyClasses =
    [
        typeof(C0824WorkspaceSlotRoleGateTests),
        typeof(AgentTaskDispatchBaseGuardTests),
        typeof(AgentTaskLandAdmissionTests),
        typeof(AgentTaskLandBoundaryTests),
        typeof(AgentTaskLandCleanupSafetyTests),
        typeof(AgentTaskLandCheckpointMatrixTests),
        typeof(AgentTaskLandConcurrencyTests),
        typeof(AgentTaskLandIdentityMatrixTests),
        typeof(AgentTaskLandPersistenceFailureTests),
        typeof(AgentTaskLandPreparationIdentityTests),
        typeof(AgentTaskLandRemovalMatrixTests),
        typeof(AgentTaskLandTargetRaceTests),
        typeof(AgentTaskLocalMergeSafetyTests),
        typeof(DelegationScopeHoldTests),
        typeof(Antiphon.Tests.Infrastructure.GitDiffSpikeTests),
        typeof(LandSourceIdentityTests),
        typeof(Antiphon.Tests.Infrastructure.LandingGitTests),
    ];

    [Test]
    public void Process_spawning_classes_carry_the_limiter()
    {
        foreach (var type in StrictClasses)
        {
            Attribute.GetCustomAttribute(type, typeof(ParallelLimiterAttribute<ProcessSpawnLimit>))
                .ShouldNotBeNull($"{type.Name} must carry [ParallelLimiter<ProcessSpawnLimit>]");
        }
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(4)]
    [Arguments(6)]
    [Arguments(8)]
    [Arguments(64)]
    public void Git_lane_limit_is_bounded_between_two_and_four_by_default(int processorCount)
    {
        var limit = GitProcessSpawnLimit.Resolve(processorCount, null);
        limit.ShouldBe(Math.Clamp(processorCount / 2, 2, 4));
        limit.ShouldBeInRange(2, 4);
        new GitProcessSpawnLimit().Limit.ShouldBeInRange(1, 8);
    }

    [Test]
    [Arguments("1", 1)]
    [Arguments("3", 3)]
    [Arguments("8", 8)]
    [Arguments("9", 8)]
    [Arguments("0", 1)]
    [Arguments(" 2 ", 2)]
    [Arguments("garbage", 3)]
    [Arguments("", 3)]
    public void Git_lane_limit_honours_the_override(string env, int expected)
    {
        GitProcessSpawnLimit.Resolve(6, env).ShouldBe(expected);
    }

    [Test]
    public void Git_only_classes_carry_the_git_limiter()
    {
        foreach (var type in GitOnlyClasses)
        {
            Attribute.GetCustomAttribute(type, typeof(ParallelLimiterAttribute<GitProcessSpawnLimit>))
                .ShouldNotBeNull($"{type.Name} must carry [ParallelLimiter<GitProcessSpawnLimit>]");
        }
    }

    // Checkpoint-tool classes that start a child in the shared checkout. The census below
    // keeps these on ProcessSpawnLimit and requires every other limited Checkpoints type
    // to name CheckpointToolSpawnLimit.
    private static readonly Type[] SharedCheckoutCheckpointClasses =
    [
        typeof(Antiphon.Tests.Checkpoints.RerunFilterHostTests),
        typeof(Antiphon.Tests.Checkpoints.CheckpointSourceExecutionTests),
        typeof(Antiphon.Tests.Checkpoints.CheckpointRepeatBuildIsolationTests),
        typeof(Antiphon.Tests.Checkpoints.CheckpointTerminalReceiptTests),
        typeof(Antiphon.Tests.Checkpoints.CheckpointRepeatHostTests),
        typeof(Antiphon.Tests.Checkpoints.CheckpointStatePublicationTests),
        typeof(Antiphon.Tests.Checkpoints.PhaseJournalKillDiagnosticTests),
    ];

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(4)]
    [Arguments(8)]
    [Arguments(24)]
    [Arguments(64)]
    public void Checkpoint_tool_lane_limit_stays_at_two_build_slots(int processorCount)
    {
        var limit = CheckpointToolSpawnLimit.Resolve(processorCount, null);
        limit.ShouldBe(CheckpointToolSpawnLimit.DefaultLimit);
        limit.ShouldBe(2);
        new CheckpointToolSpawnLimit().Limit.ShouldBeInRange(
            CheckpointToolSpawnLimit.MinimumLimit, CheckpointToolSpawnLimit.MaximumLimit);
    }

    [Test]
    [Arguments("1", 1)]
    [Arguments("2", 2)]
    [Arguments("4", 4)]
    [Arguments("5", 4)]
    [Arguments("0", 1)]
    [Arguments(" 3 ", 3)]
    [Arguments("garbage", 2)]
    [Arguments("", 2)]
    public void Checkpoint_tool_lane_limit_honours_the_override(string env, int expected)
    {
        CheckpointToolSpawnLimit.Resolve(24, env).ShouldBe(expected);
    }

    [Test]
    public void Checkpoint_tool_classes_carry_their_own_limiter()
    {
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var misses = new List<string>();
        var limited = 0;
        foreach (var type in CheckpointCandidateTypes())
        {
            var shared = SharedCheckoutCheckpointClasses.Contains(type);
            var strict = HasSpawnLimiter(type, typeof(ProcessSpawnLimit));
            var tool = HasSpawnLimiter(type, typeof(CheckpointToolSpawnLimit));
            if (strict || tool)
            {
                limited++;
                if (shared)
                {
                    if (!strict)
                        misses.Add(type.FullName + " shares the checkout and must keep [ParallelLimiter<ProcessSpawnLimit>]");
                    if (tool)
                        misses.Add(type.FullName + " shares the checkout and must not carry [ParallelLimiter<CheckpointToolSpawnLimit>]");
                }
                else
                {
                    if (!tool)
                        misses.Add(type.FullName + " must carry [ParallelLimiter<CheckpointToolSpawnLimit>]");
                    if (strict)
                        misses.Add(type.FullName + " must not keep [ParallelLimiter<ProcessSpawnLimit>]");
                }
            }

            foreach (var method in type.GetMethods(declared))
            {
                var methodStrict = HasSpawnLimiter(method, typeof(ProcessSpawnLimit));
                var methodTool = HasSpawnLimiter(method, typeof(CheckpointToolSpawnLimit));
                if (!methodStrict && !methodTool)
                    continue;
                limited++;
                if (shared)
                {
                    if (!methodStrict || methodTool)
                        misses.Add(type.FullName + "." + method.Name + " shares the checkout and must keep the strict limiter");
                }
                else if (!methodTool || methodStrict)
                {
                    misses.Add(type.FullName + "." + method.Name + " must carry only [ParallelLimiter<CheckpointToolSpawnLimit>]");
                }
            }
        }

        limited.ShouldBeGreaterThan(0, "the Checkpoints namespace has process-limited classes");
        misses.ShouldBeEmpty(string.Join(Environment.NewLine, misses));
    }

    [Test]
    public void Process_spawn_guard_still_covers_classes_outside_the_checkpoint_tool_lane()
    {
        foreach (var type in StrictClasses)
        {
            HasSpawnLimiter(type, typeof(ProcessSpawnLimit))
                .ShouldBeTrue($"{type.Name} must carry [ParallelLimiter<ProcessSpawnLimit>]");
            HasSpawnLimiter(type, typeof(CheckpointToolSpawnLimit))
                .ShouldBeFalse($"{type.Name} is outside the checkpoint-tool lane");
        }

        SharedCheckoutCheckpointClasses.Except(StrictClasses).ShouldBeEmpty(
            "every shared-checkout checkpoint class stays on the strict roster");
    }

    [Test]
    public void No_class_carries_both_limiters()
    {
        StrictClasses.Intersect(GitOnlyClasses).ShouldBeEmpty("a class belongs to one roster");
        StrictClasses.Intersect(SharedCheckoutCheckpointClasses).Count().ShouldBe(SharedCheckoutCheckpointClasses.Length);
        var seen = new HashSet<Type>();
        var both = new List<string>();
        foreach (var type in StrictClasses.Concat(GitOnlyClasses).Concat(CheckpointCandidateTypes()))
        {
            if (!seen.Add(type))
                continue;
            var lanes = new[]
            {
                HasSpawnLimiter(type, typeof(ProcessSpawnLimit)) ? "ProcessSpawnLimit" : null,
                HasSpawnLimiter(type, typeof(GitProcessSpawnLimit)) ? "GitProcessSpawnLimit" : null,
                HasSpawnLimiter(type, typeof(CheckpointToolSpawnLimit)) ? "CheckpointToolSpawnLimit" : null,
            }.Where(name => name is not null).ToList();
            if (lanes.Count > 1)
                both.Add(type.FullName + " carries " + string.Join(" and ", lanes));
        }

        both.ShouldBeEmpty(string.Join(Environment.NewLine, both));
    }

    private static bool HasSpawnLimiter(MemberInfo member, Type limit) =>
        Attribute.GetCustomAttribute(member, typeof(ParallelLimiterAttribute<>).MakeGenericType(limit)) is not null;

    private static IEnumerable<Type> CheckpointCandidateTypes()
    {
        foreach (var type in LoadableTypes(typeof(ProcessSpawnLimitTests).Assembly))
        {
            if (type.Namespace != "Antiphon.Tests.Checkpoints" || type.IsNested)
                continue;
            yield return type;
        }
    }

    // CARD-1421 repair (Review b529639c D-1): DirectoryLink runs Scripts/Fixtures/directory-link.ps1 through
    // ScriptHarnessProcess on Windows, so a class that reaches it starts pwsh and is not git-only. The scan
    // reflects over the compiled type: partial-class siblings, async state machines, lambdas and local
    // functions all compile into the class or its nested types, so none of them can hide a reference.
    private static readonly Type[] PwshBackedHelpers =
    [
        typeof(DirectoryLink),
        typeof(DirectoryLinkCommand),
        typeof(ScriptHarnessProcess),
    ];

    [Test]
    public void Git_lane_classes_do_not_reach_the_pwsh_backed_link_helpers()
    {
        var gitLane = LoadableTypes(typeof(ProcessSpawnLimitTests).Assembly)
            .Where(type => !type.IsNested
                && Attribute.GetCustomAttribute(type, typeof(ParallelLimiterAttribute<GitProcessSpawnLimit>)) is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
        gitLane.Count.ShouldBeGreaterThanOrEqualTo(GitOnlyClasses.Length, "the git roster is a floor of the git lane");

        var offenders = gitLane
            .SelectMany(type => HelperReferences(type).Select(reference => $"{type.Name}: {reference}"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();
        offenders.ShouldBeEmpty(
            "a git-lane class reaches a pwsh-backed helper and belongs on [ParallelLimiter<ProcessSpawnLimit>]:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(type => type is not null).Select(type => type!); }
    }

    /// <summary>Every pwsh-backed helper the type, its nested types, their signatures, locals and IL reach.</summary>
    private static IEnumerable<string> HelperReferences(Type root)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var pending = new Stack<Type>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var type = pending.Pop();
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                pending.Push(nested);
            foreach (var field in type.GetFields(all))
                if (ReachesHelper(field.FieldType, out var hit))
                    yield return $"field {field.Name} is {hit}";
            foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                if (method is MethodInfo info && ReachesHelper(info.ReturnType, out var returned))
                    yield return $"{method.Name} returns {returned}";
                foreach (var parameter in method.GetParameters())
                    if (ReachesHelper(parameter.ParameterType, out var accepted))
                        yield return $"{method.Name}({parameter.Name}) is {accepted}";
                var body = method.GetMethodBody();
                if (body is null)
                    continue;
                foreach (var local in body.LocalVariables)
                    if (ReachesHelper(local.LocalType, out var held))
                        yield return $"{method.Name} holds a {held}";
                foreach (var member in ReferencedMembers(method, body))
                {
                    if (member is Type referencedType)
                    {
                        if (ReachesHelper(referencedType, out var named))
                            yield return $"{method.Name} names {named}";
                    }
                    else if (member.DeclaringType is { } owner && ReachesHelper(owner, out var called))
                        yield return $"{method.Name} calls {called}.{member.Name}";
                }
            }
        }
    }

    private static bool ReachesHelper(Type type, out string hit)
    {
        hit = "";
        var current = type;
        while (current.HasElementType)
            current = current.GetElementType()!;
        if (current.IsGenericParameter)
            return false;
        if (current.IsConstructedGenericType)
        {
            foreach (var argument in current.GetGenericArguments())
                if (ReachesHelper(argument, out hit))
                    return true;
            current = current.GetGenericTypeDefinition();
        }
        for (var outer = current; outer is not null; outer = outer.DeclaringType)
        {
            if (PwshBackedHelpers.Contains(outer))
            {
                hit = outer.Name;
                return true;
            }
        }
        return false;
    }

    private static readonly Dictionary<short, OpCode> OpCodeByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => opCode.Value);

    /// <summary>The methods, fields and types a body's IL names through metadata tokens.</summary>
    private static IEnumerable<MemberInfo> ReferencedMembers(MethodBase method, MethodBody body)
    {
        var il = body.GetILAsByteArray();
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
            if (opCode.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
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

    [Test]
    public void Live_runner_send_input_method_carries_the_limiter()
    {
        var method = typeof(AgentSessionRuntimeTests).GetMethod(
            nameof(AgentSessionRuntimeTests.Backend_runtime_can_send_input_to_live_runner_session_after_restart),
            BindingFlags.Instance | BindingFlags.Public);
        method.ShouldNotBeNull();
        Attribute.GetCustomAttribute(method, typeof(ParallelLimiterAttribute<ProcessSpawnLimit>))
            .ShouldNotBeNull("the one method that starts a pty-host must carry the limiter; the rest of the class is fakes");
    }
}
