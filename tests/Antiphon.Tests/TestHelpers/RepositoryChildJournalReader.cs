using System.Text.Json;
using Antiphon.Server.Infrastructure.Git;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Reads a live child journal the way the server's fence readers do. File.ReadAllText shares
/// only Read, which on Windows denies the owner's atomic replace while the git child is alive.</summary>
internal static class RepositoryChildJournalReader
{
    public static async Task<RepositoryChildJournal.ChildRecord?> ReadAsync(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return await JsonSerializer.DeserializeAsync<RepositoryChildJournal.ChildRecord>(stream, cancellationToken: ct);
    }
}
