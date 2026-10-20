using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>The OS releases this lock on exit; the mutation lease still fences unfinished children.</summary>
public sealed class LandExecutionLease(ILandingGit git) : ILandExecutionLease
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(string repository, CancellationToken ct)
    {
        var common = await git.CommonDirectoryAsync(repository, ct);
        var path = Path.Combine(common, "antiphon", "land-execution.lock");
        LandVerificationArtifacts.RequireNoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ct.ThrowIfCancellationRequested();
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException) { return null; }
    }
}
