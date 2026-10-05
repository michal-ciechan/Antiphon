using System.Text;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

public static class PushCredentialPolicyFile
{
    public static string Render(RunnerRepositoryPolicy policy)
    {
        if (!RepositoryCloneSource.TryNormalize(policy.PrimaryCloneSource, out var primary))
            throw new InvalidOperationException("PhoneHome:RunnerCloneSource must name a repository.");
        // Preserve the ordinal prefixes used by RepositoryCloneSource.IsAdmitted.
        var lines = new[] { primary }.Concat(policy.AllowedCloneSources).ToArray();
        if (lines.Any(line => line.Contains('\n') || line.Contains('\r')))
            throw new InvalidOperationException("Push credential policy entries must be single lines.");
        return string.Join('\n', lines) + "\n";
    }

    public static void Write(string path, RunnerRepositoryPolicy policy)
    {
        var content = Render(policy);
        var destination = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
