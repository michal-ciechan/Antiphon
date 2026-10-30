using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class TaskCeilingHoldTests
{
    [Test]
    [Arguments(AgentTaskRole.Review)]
    [Arguments(AgentTaskRole.Code)]
    [Arguments(AgentTaskRole.Deploy)]
    [Arguments(AgentTaskRole.Plan)]
    [Arguments(AgentTaskRole.Check)]
    public async Task C1610_Working_ceiling_holds(AgentTaskRole role)
    {
        await using var h = await TaskCeilingHarness.CreateAsync(role);
        await h.SweepAsync();
        var task = await h.TaskAsync();
        task.Status.ShouldBe(AgentTaskStatus.Working, "a working ceiling breach asks for a decision");
        task.CompletedAt.ShouldBeNull();
        task.FailureReason.ShouldBeNull();
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    [Arguments(TranscriptKinds.AssistantText)]
    [Arguments(TranscriptKinds.ToolCall)]
    public async Task C1610_Phase_beats_ceiling(string kind)
    {
        await using var h = await TaskCeilingHarness.CreateAsync(ageMinutes: 1000, kind: kind,
            lastAgeMinutes: kind == TranscriptKinds.ToolCall ? 91 : 21);
        await h.SweepAsync();
        var task = await h.TaskAsync();
        task.Status.ShouldBe(AgentTaskStatus.Failed);
        task.FailureReason.ShouldNotBeNull();
        task.FailureReason.ShouldContain(kind == TranscriptKinds.ToolCall ? "90-minute deadline" : "20-minute deadline",
            customMessage: "a breached real phase wins even when the ceiling fraction is larger");
        h.Stopper.Killed.ShouldBeEmpty();
    }
}
