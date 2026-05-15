namespace Antiphon.Server.Domain.Entities;

public class RetrySchedule
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public DateTime? NextRetryAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }

    public Card Card { get; set; } = null!;
}
