namespace Antiphon.SessionRunner;

/// <summary>One boot id shared by registration and an accepted retire reply.</summary>
public sealed class PhoneHomeProcessIdentity
{
    public Guid BootId { get; } = Guid.NewGuid();
}
