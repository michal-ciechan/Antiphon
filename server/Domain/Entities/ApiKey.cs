namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// A named, encrypted value an agent's launch environment can reference by placeholder
/// (<c>{{key:NAME}}</c>) instead of carrying the secret itself (CARD-0106 S1).
///
/// <para>Global-vs-project is a nullable FK rather than two tables: nothing anywhere treats the two
/// scopes differently except precedence at lookup time (project wins), and one table makes "list
/// every key that could resolve for this agent" a single query.</para>
///
/// <para>Distinct from <see cref="AgentTuiSecret"/>, which coexists deliberately (plan §7): a
/// profile secret authenticates the RUNNER PROGRAM under a profile and its name IS the environment
/// variable name; an API key is a named value agents reference by placeholder under whatever
/// variable name each one needs.</para>
/// </summary>
public class ApiKey
{
    public Guid Id { get; set; }

    /// <summary>
    /// Referenced by <c>{{key:NAME}}</c>. <c>[A-Za-z0-9_.-]+</c>, max 128 characters, compared
    /// Ordinal — the same case-sensitive handling environment names get in <c>AgentRegistry.Resolve</c>.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Null = global. Cascade-deleted with the project.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>
    /// DataProtection payload. Protected under a purpose chain keyed on <see cref="Id"/>, never on
    /// <see cref="Name"/>, so renaming a key does not orphan its ciphertext.
    /// </summary>
    public string Ciphertext { get; set; } = string.Empty;

    /// <summary>Same field <see cref="AgentTuiSecret"/> carries; the protector's contract version.</summary>
    public string ProtectionVersion { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Project? Project { get; set; }
}
