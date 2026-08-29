using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0161: herdr's agent.prompt must never appear on the typed client surface — S1 measured a
/// false agent_prompt_stalled on a successful delivery.
/// </summary>
public class HerdrClientSurfaceTests
{
    [Test]
    public void HerdrClient_public_surface_never_sends_agent_prompt()
    {
        var methods = typeof(HerdrClient).GetMethods()
            .Where(m => m.DeclaringType == typeof(HerdrClient) && m.IsPublic)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        methods.ShouldNotContain("AgentPromptAsync");
        methods.Any(n => n.Contains("Prompt", StringComparison.OrdinalIgnoreCase)
                         && !n.Contains("Report", StringComparison.OrdinalIgnoreCase))
            .ShouldBeFalse("no Prompt* wrapper may call herdr agent.prompt");

        // Positive pin: the S2 typed surface we rely on is present.
        methods.ShouldContain("PaneSendTextAsync");
        methods.ShouldContain("PaneSendKeysAsync");
        methods.ShouldContain("PaneGetAsync");
        methods.ShouldContain("AgentStartAsync");
        methods.ShouldContain("AgentListAsync");
        methods.ShouldContain("AgentRenameAsync");
    }

    [Test]
    public void Herdr_pane_inspect_dto_round_trips_the_card0213_shape()
    {
        var dto = new HerdrPaneInspectDto(
            "w2:p3", "w2", "w2:t1", "label", "title",
            Agent: "grok",
            AgentStatus: "idle",
            ShellPid: 1,
            ShellName: "pwsh",
            Foreground: [new HerdrForegroundProcessDto(42, "grok.exe", ["grok", "--session-id", Guid.Empty.ToString("D")], @"D:\src", DateTime.UnixEpoch)],
            NativeSessionId: Guid.Empty,
            NativeSessionSource: HerdrNativeSessionSources.Argv,
            BoundToSessionId: null,
            BoundOrigin: null);
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<HerdrPaneInspectDto>(
            System.Text.Json.JsonSerializer.Serialize(dto));
        roundTrip.ShouldNotBeNull();
        roundTrip!.PaneId.ShouldBe("w2:p3");
        roundTrip.NativeSessionSource.ShouldBe("argv");
        roundTrip.Foreground.ShouldHaveSingleItem().Pid.ShouldBe(42);
        roundTrip.BoundToSessionId.ShouldBeNull();
    }
}
