namespace Antiphon.Server.Application.Settings;

// CARD-1886. Application connections disable JIT unless this is set false.
// A process recycle picks up the change; no schema migration is involved.
public sealed class PostgresSettings
{
    public const string SectionName = "Postgres";

    public bool DisableJit { get; set; } = true;
}
