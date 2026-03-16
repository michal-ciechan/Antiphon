namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Represents a system user. Seeded with a default admin for MVP.
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
}
