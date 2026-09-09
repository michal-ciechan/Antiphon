using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using static Antiphon.Server.Application.Services.DelegateCheckProbe;

namespace Antiphon.Server.Application.Services;

public sealed record SpecialistQualificationFixture(string Shape, string Nonce, string Goal);

/// <summary>Two bounded evidence questions. Syntax alone is never behavioral qualification.</summary>
public static class SpecialistQualificationContract
{
    public static SpecialistQualificationFixture[] CreateBatch(DateTime now) => [Create("produced", now), Create("boot", now)];

    private static SpecialistQualificationFixture Create(string shape, DateTime now)
    {
        var produced = shape == "produced";
        var nonce = (produced ? "violet-" : "copper-") + Guid.NewGuid().ToString("N")[..12];
        var checkedTask = new AgentTask { Id = Guid.NewGuid() };
        var task = new CheckTaskFacts(checkedTask.Id, DelegationReportFormatter.Short(checkedTask.Id), nonce + " parser",
            AgentTaskKind.Worker, AgentKind.ClaudeCode, AgentTaskRole.Code, AgentModelLevel.Low, AgentTaskStatus.Working,
            false, 1, 2, now.AddMinutes(-4), null, TimeSpan.FromMinutes(4), 10, 1, false, null);
        var session = new CheckSessionFacts(Guid.NewGuid(), SessionStatus.Running, !produced, produced ? 3 : 1,
            now.AddMinutes(-3), TimeSpan.FromMinutes(3), produced ? null : new(now.AddMinutes(-3), TimeSpan.FromMinutes(3)));
        var text = produced ? $"Changed {nonce}.cs; seven focused tests passed; integration verification pending."
            : $"YOUR BRIEF IS NOT IN THIS MESSAGE. Read C:\\fixture\\{nonce}.md. This prompt was delivered; there are no model rows.";
        var facts = new CheckFacts(now, task, session,
            [new(1, produced ? TranscriptKinds.AssistantText : TranscriptKinds.UserPrompt, text, now.AddMinutes(-3))],
            produced ? new("C:\\fixture", CheckGitEvidenceScope.TaskBranch, "master..fixture", [$"abc1234 change {nonce}.cs"], 1, 0, null) : null,
            [], [], Deadline: produced ? null : new("BootModelWait", TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3), true,
                "The provider has not answered the delivered prompt; the harness kills and retries this boot once."));
        return new(shape, nonce, CheckInterpretation.BuildGoal(checkedTask, 1, RenderDigest(facts))
            + "\nFor this qualification reading, include the distinctive topic identifier from the captured facts.");
    }

    public static bool Validate(SpecialistQualificationFixture fixture, string? reading)
    {
        if (string.IsNullOrWhiteSpace(reading) || !reading.Contains(fixture.Nonce, StringComparison.Ordinal)) return false;
        var lower = reading.ToLowerInvariant();
        if (lower.Contains("task is complete") || lower.Contains("task complete") || lower.Contains("integration passed")) return false;
        return fixture.Shape switch
        {
            "produced" => reading.StartsWith("On track", StringComparison.Ordinal)
                && (lower.Contains("change") || lower.Contains("commit") || lower.Contains("seven") || lower.Contains("7"))
                && (lower.Contains("pending") || lower.Contains("remain") || lower.Contains("await")),
            "boot" => reading.StartsWith("Needs attention", StringComparison.Ordinal)
                && (lower.Contains("provider") || lower.Contains("model"))
                && (lower.Contains("no response") || lower.Contains("not answered") || lower.Contains("silent") || lower.Contains("unanswered"))
                && (lower.Contains("retry") || lower.Contains("retries") || lower.Contains("deadline"))
                && !lower.Contains("brief is missing") && !lower.Contains("fetch") && !lower.Contains("queued"),
            _ => false,
        };
    }
}
