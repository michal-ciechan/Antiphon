namespace Antiphon.Server.Domain.Entities;

public class AgentTuiSecret
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Ciphertext { get; set; } = string.Empty;
    public string ProtectionVersion { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public AgentTuiProfile Profile { get; set; } = null!;
}
