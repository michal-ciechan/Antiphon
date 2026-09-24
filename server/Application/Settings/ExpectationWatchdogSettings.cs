using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// Explicit standing directive. Ships disabled with an empty list. S6 binds this section.
/// Candidate subscription keys are identifiers, never credentials. No production ids belong here.
/// </summary>
public sealed class ExpectationWatchdogSettings
{
    public const string SectionName = "ExpectationWatchdog";

    public bool Enabled { get; set; }

    public List<ExpectationDirectiveSettings> Directives { get; set; } = [];
}

public sealed class ExpectationDirectiveSettings
{
    public string Id { get; set; } = string.Empty;
    public Guid AgentId { get; set; }
    public Guid BoardId { get; set; }
    public Guid AuditCardId { get; set; }
    public Guid OperatorChannelId { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset? ActiveUntilUtc { get; set; }
    public List<ExpectationTargetSettings> Targets { get; set; } = [];
}

public sealed class ExpectationTargetSettings
{
    /// <summary>Null is the local runner. Any other value must be a configured runner id.</summary>
    public string? RunnerId { get; set; }

    public int InFlightTarget { get; set; }

    public List<ExpectationCandidateSettings> Candidates { get; set; } = [];
}

public sealed class ExpectationCandidateSettings
{
    public AgentKind AgentKind { get; set; }
    public AgentModelLevel ModelLevel { get; set; }
    public string SubscriptionKey { get; set; } = string.Empty;
}
