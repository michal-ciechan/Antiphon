using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1610 S4a. A declared ceiling reaches the server only through an explicit create: it is
/// validated before any side effect, bound to the Requested columns, echoed, resolved at dispatch,
/// kept by Retry and never inherited. A create without it keeps its handoff digest.
/// </summary>
[Category("Integration")]
public sealed class TaskCeilingCreateBindingTests
{
    // HandoffRequestDigest of the fixed no-ceiling request below, computed at the S4a Code base
    // 5376ac529 (CARD-0824 S1 landed) before any S4a DTO edit (CP-0).
    private const string PinnedBaseDigest = "UNPINNED";

    [Test]
    public void C1610_Absent_ceiling_keeps_handoff_digest()
    {
        var request = new CreateAgentTaskRequest("c1610 digest pin", Title: "pin", Role: AgentTaskRole.Code,
            Workspace: WorkspaceMode.Worktree, ExpectedMinutes: 30);
        AgentTaskService.HandoffRequestDigest(request).ShouldBe(PinnedBaseDigest);

        var element = JsonSerializer.SerializeToElement(request);
        element.TryGetProperty("CeilingMinutes", out _).ShouldBeFalse();
        element.TryGetProperty("CeilingReason", out _).ShouldBeFalse();

        var json = JsonSerializer.Serialize(request);
        var withCeiling = JsonSerializer.Deserialize<CreateAgentTaskRequest>(
            json[..^1] + ",\"CeilingMinutes\":600,\"CeilingReason\":\"r\"}")!;
        AgentTaskService.HandoffRequestDigest(withCeiling).ShouldNotBe(PinnedBaseDigest);
    }
}
