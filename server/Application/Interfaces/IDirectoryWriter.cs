namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Creates directories on demand (used when an agent is created with the
/// "create working directory" flag set, and by
/// <c>POST /api/agents/{id}/ensure-directory</c>). Idempotent — creating an
/// existing directory is a no-op.
/// </summary>
public interface IDirectoryWriter
{
    void CreateDirectory(string path);
}
