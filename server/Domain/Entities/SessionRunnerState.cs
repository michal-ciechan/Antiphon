namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// CARD-0727 D-6. Durable drain and retire book for one configured runner id. The directory
/// mirrors a copy for its synchronous gates; a desktop restart reloads this row.
/// </summary>
public sealed class SessionRunnerState
{
    public string RunnerId { get; set; } = "";
    public bool Draining { get; set; }
    public DateTimeOffset? DrainedAt { get; set; }
    public string? DrainReason { get; set; }
    public string? RedirectTo { get; set; }
    public bool RetireWhenIdle { get; set; }
    public DateTimeOffset? IdleObservedAt { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
    public string? RetireReason { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByTaskId { get; set; }
}
