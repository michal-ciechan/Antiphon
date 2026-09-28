namespace Antiphon.Server.Application.Dtos;

public sealed record HostLimit(
    string HostId, int? Configured, int? Declared, int? Effective, string Source,
    string? Reason = null, DateTime? UpdatedAt = null, int Revision = 0);
