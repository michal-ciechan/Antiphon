using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>An immutable answer to one question in one task attempt.</summary>
public sealed class AgentTaskDecisionQuestion
{
    public Guid Id { get; set; }
    public Guid AgentTaskId { get; set; }
    public int Attempt { get; set; }
    public Guid AgentSessionId { get; set; }
    public Guid RequestId { get; set; }
    public string CanonicalPayloadJson { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public int? PolicyVersion { get; set; }
    public string? PolicyHash { get; set; }
    public string? GrantId { get; set; }
    public InternalDecisionDisposition Disposition { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Answer { get; set; }
    public DateTime CreatedAt { get; set; }
}
