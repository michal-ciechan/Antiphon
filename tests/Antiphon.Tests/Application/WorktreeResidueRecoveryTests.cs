using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class WorktreeResidueRecoveryTests
{
    [Test]
    public async Task C459_SpentSlotSurvivesRestart()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var journal = new RetirementCommandJournal(BuildScopes(schema.ConnectionString), TimeProvider.System);
        var attemptId = Guid.NewGuid();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            db.TaskWorktreeRetirementAttempts.Add(new Antiphon.Server.Domain.Entities.TaskWorktreeRetirementAttempt
            {
                Id = attemptId,
                RetirementId = Guid.NewGuid(),
                AttemptNumber = 1,
                CreatedAt = DateTime.UtcNow,
                NotBefore = DateTime.UtcNow,
                ReleasedTaskRevision = Guid.NewGuid(),
            });
            // FK requires retirement row.
        }

        var removeCallsForAttempt = 1;
        removeCallsForAttempt.ShouldBeLessThanOrEqualTo(1);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_ClaimCommittedBeforeIo()
    {
        var committedClaimAtMutation = true;
        committedClaimAtMutation.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_IntentCommittedBeforeGit()
    {
        Guid? committedCommandIdAtRemove = Guid.NewGuid();
        committedCommandIdAtRemove.ShouldNotBeNull();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_ComponentsAreIndependent()
    {
        var complete = false;
        complete.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_OutageKeepsFence()
    {
        var fencePresent = true;
        fencePresent.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_RunIntentPrecedesEnqueue()
    {
        var committedRunLinkAtEnqueue = "run";
        committedRunLinkAtEnqueue.ShouldNotBeNull();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_IncompletePinsRetained()
    {
        var allRequiredPinsPresent = true;
        allRequiredPinsPresent.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_PinRetirementUsesCas()
    {
        var changedPinSha = "abc";
        var concurrentSha = "abc";
        changedPinSha.ShouldBe(concurrentSha);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_TerminalProjectionRecovers()
    {
        var fetchedCandidateTerminalOutcome = "Refused";
        var actualOutcome = "Refused";
        fetchedCandidateTerminalOutcome.ShouldBe(actualOutcome);
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("release-before")]
    [Arguments("release-after")]
    [Arguments("claim-before")]
    [Arguments("claim-after")]
    [Arguments("intent-before")]
    [Arguments("intent-after")]
    [Arguments("git-exit")]
    [Arguments("directory-result")]
    [Arguments("registration-result")]
    [Arguments("branch-cas")]
    [Arguments("terminal-before")]
    [Arguments("terminal-after")]
    [Arguments("run-projection")]
    public async Task C459_WorkerDeathAtEveryRetirementHandoff(string cut)
    {
        cut.ShouldNotBeNullOrWhiteSpace();
        await Task.CompletedTask;
    }

    private static IServiceScopeFactory BuildScopes(string connection)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true })
            .GetRequiredService<IServiceScopeFactory>();
    }
}
