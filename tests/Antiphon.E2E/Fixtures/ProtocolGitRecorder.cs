using System.Text.Json;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.E2E.Fixtures;

/// <summary>
/// CARD-1704 S1 (T-D-11): writes one committed <c>protocol-git-&lt;guid&gt;.json</c> per git call the land's
/// <c>LandingGit</c> completes (written to <c>.tmp</c>, then moved), so the C467 receipt wait keeps renewing its
/// quiet window. The base fields plus <c>backend</c> and <c>kind</c>. A write failure never fails git.
/// </summary>
internal sealed class ProtocolGitRecorder(string root) : IGitOperationObserver
{
    public void Completed(GitOperation operation)
    {
        var path = Path.Combine(root, $"protocol-git-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new
            {
                repository = operation.Repository, arguments = operation.Arguments, operation.ExitCode,
                at = DateTime.UtcNow, pid = Environment.ProcessId, backend = operation.Backend,
                kind = operation.Kind switch
                {
                    GitOperationKind.Stdin => "stdin",
                    GitOperationKind.InProcess => "inproc",
                    _ => "process",
                },
            }));
            File.Move(path + ".tmp", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
