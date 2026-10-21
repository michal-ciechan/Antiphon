using System.Reflection;
using Antiphon.Tests.AgentTui;
using Antiphon.Tests.Agents;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests;

/// <summary>CARD-0050 S5: non-git process-spawning classes share a 1-wide lane; CARD-1421: git-only classes a 2-4-wide lane.</summary>
[Category("Unit")]
public class ProcessSpawnLimitTests
{
    [Test]
    public void Caps_concurrent_process_spawning_tests_at_one()
    {
        new ProcessSpawnLimit().Limit.ShouldBe(1);
    }

    // A floor, not a census: the strict lane (pty, pwsh, cmd, dotnet worker, fake agents, web host, session runtime).
    private static readonly Type[] StrictClasses =
    [
        typeof(LandSourceIdentityTests),
        typeof(AgentTaskLandPublicationTests),
        typeof(AgentTaskLandTargetRaceTests),
        typeof(AgentTaskLandPersistenceFailureTests),
        typeof(AgentTaskLandVerifierTests),
        typeof(AgentTaskDispatchBaseGuardTests),
        typeof(DelegationScopeHoldTests),
        typeof(CardReviewServiceIntegrationTests),
        typeof(AgentTaskLandCleanupSafetyTests),
        typeof(Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests),
        typeof(AgentTaskLandRecoveryTests),
        typeof(AgentTaskLandConcurrencyTests),
        typeof(AgentTaskLandBoundaryTests),
        typeof(AgentTaskLandCheckpointMatrixTests),
        typeof(AgentTaskLandIdentityMatrixTests),
        typeof(AgentTaskLandRemovalMatrixTests),
        typeof(AgentTaskLandPreparationIdentityTests),
        typeof(AgentTaskLocalMergeSafetyTests),
        typeof(AgentTaskLandAdmissionTests),
        typeof(Antiphon.Tests.Infrastructure.LandingAdmissionControlTests),
        typeof(Antiphon.Tests.Infrastructure.LandingRemovalControlTests),
        typeof(Antiphon.Tests.Infrastructure.LandingSourceBoundaryControlTests),
        typeof(AgentTaskLandStageOutcomeTests),
        typeof(DelegationWorktreeTests),
        typeof(Antiphon.Tests.Infrastructure.WorktreeRemovalAuthorityTests),
        typeof(Antiphon.Tests.Infrastructure.LandingGitTests),
        typeof(Antiphon.Tests.Infrastructure.GitDiffSpikeTests),
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
        typeof(TestClassificationPolicyTests),
        typeof(TestDbFixtureLazyInitializationTests),
        typeof(CheckCompactionCrashTests),
        typeof(CheckNoteDeliveryHandoffTests),
        typeof(Antiphon.Tests.Scripts.RunCheckpointScriptTests),
        typeof(Antiphon.Tests.Scripts.BuildSlotScriptTests),
        typeof(Antiphon.Tests.Checkpoints.CheckpointTempLifecycleTests),
        typeof(Antiphon.Tests.Checkpoints.CheckpointTempUsageTests),
        typeof(Antiphon.Tests.Checkpoints.CheckpointRecoveryLinuxTests),
        typeof(Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests),
        typeof(Antiphon.Tests.Checkpoints.BuildSlotClientTests),
        typeof(Antiphon.Tests.Checkpoints.CheckpointSlotExecutorTests),
    ];

    // CARD-1421 Appendix B: strict-roster classes whose only children are git, in fixture-owned repositories.
    private static readonly Type[] GitOnlyClasses =
    [
        typeof(AgentTaskDispatchBaseGuardTests),
        typeof(AgentTaskLandAdmissionTests),
        typeof(AgentTaskLandBoundaryTests),
        typeof(AgentTaskLandCleanupSafetyTests),
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

    [Test]
    public void No_class_carries_both_limiters()
    {
        StrictClasses.Intersect(GitOnlyClasses).ShouldBeEmpty("a class belongs to one roster");
        foreach (var type in StrictClasses.Concat(GitOnlyClasses))
        {
            var strict = Attribute.GetCustomAttribute(type, typeof(ParallelLimiterAttribute<ProcessSpawnLimit>)) is not null;
            var git = Attribute.GetCustomAttribute(type, typeof(ParallelLimiterAttribute<GitProcessSpawnLimit>)) is not null;
            (strict && git).ShouldBeFalse($"{type.Name} carries both limiters");
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
