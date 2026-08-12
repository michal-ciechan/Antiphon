using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class AgentTuiProfileRevision
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public int RevisionNumber { get; set; }
    public string Executable { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "[]";
    public string DiscoveryArgumentsJson { get; set; } = "[]";
    public string VersionArgumentsJson { get; set; } = "[]";
    public string? WorkingDirectory { get; set; }
    public AgentTuiAuthenticationMode AuthenticationMode { get; set; }
    public string NonSecretEnvironmentJson { get; set; } = "{}";
    public string SecretEnvironmentNamesJson { get; set; } = "[]";
    public string? ModelArgumentName { get; set; }
    public string Guidance { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public AgentTuiProfile Profile { get; set; } = null!;
    public ICollection<AgentSession> Sessions { get; set; } = new List<AgentSession>();
}
