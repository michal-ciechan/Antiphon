namespace Antiphon.Server.Domain.Entities;

public class TokenUsage
{
    public Guid Id { get; set; }
    public Guid RunAttemptId { get; set; }
    public long TokensIn { get; set; }
    public long TokensOut { get; set; }
    public decimal CostUsd { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public RunAttempt RunAttempt { get; set; } = null!;
}
