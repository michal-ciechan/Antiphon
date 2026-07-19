using System.Security.Cryptography;
using System.Text;

namespace Antiphon.PtyHost.Client;

/// <summary>
/// Content-addressed shadow copies of the pty-host build output. Hosts must never run from
/// <c>bin/</c>: a running exe locks its file (breaking the next build) and a rebuild would
/// version-skew live hosts. Version dirs are named <c>&lt;yyyyMMdd-HHmmss&gt;-&lt;sha8&gt;</c> -
/// the UTC stamp makes them sort oldest-first for cleanup, the 8-hex content hash makes reuse
/// and dedup trivial: identical build output maps onto the existing dir instead of a new copy.
/// </summary>
public sealed class ShadowCopyStore(string binRoot)
{
    /// <summary>Excluded from hashing and copying (test/report noise, never runtime inputs).</summary>
    private static readonly string[] ExcludedDirNames = ["TestResults"];

    public string BinRoot => binRoot;

    /// <summary>
    /// Ensures a shadow copy of <paramref name="sourceDir"/> exists and returns the directory.
    /// Reuses an existing dir with the same content hash; otherwise copies under a fresh
    /// timestamped name. Copy is staged (<c>.copying</c> suffix) then renamed so a concurrent
    /// launcher never sees a half-copied dir.
    /// </summary>
    public string EnsureCurrent(string sourceDir)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Shadow-copy source not found: {sourceDir}");

        var sha8 = ComputeContentSha8(sourceDir);
        Directory.CreateDirectory(binRoot);

        var existing = Directory.EnumerateDirectories(binRoot, $"*-{sha8}")
            .FirstOrDefault(d => !d.EndsWith(".copying", StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing;

        var dirName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{sha8}";
        var finalDir = Path.Combine(binRoot, dirName);
        var stagingDir = finalDir + ".copying";

        try
        {
            CopyRecursive(sourceDir, stagingDir);
            Directory.Move(stagingDir, finalDir);
        }
        catch (IOException) when (Directory.Exists(finalDir))
        {
            // Concurrent launcher won the rename race; theirs is identical by construction.
            TryDeleteDirectory(stagingDir);
        }
        catch
        {
            TryDeleteDirectory(stagingDir);
            throw;
        }

        return finalDir;
    }

    /// <summary>
    /// Deletes version dirs not in <paramref name="referencedDirs"/>, oldest first (the date
    /// prefix sorts them). Dirs whose files are locked by a live host simply survive the pass.
    /// </summary>
    public int CleanupUnreferenced(IReadOnlySet<string> referencedDirs)
    {
        if (!Directory.Exists(binRoot))
            return 0;

        var deleted = 0;
        foreach (var dir in Directory.EnumerateDirectories(binRoot).OrderBy(d => d, StringComparer.Ordinal))
        {
            if (referencedDirs.Contains(dir))
                continue;
            if (TryDeleteDirectory(dir))
                deleted++;
        }

        return deleted;
    }

    /// <summary>SHA-256 over relative paths + contents in stable order, first 8 hex chars.</summary>
    public static string ComputeContentSha8(string sourceDir)
    {
        using var sha = SHA256.Create();
        foreach (var file in EnumerateContentFiles(sourceDir)
                     .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var pathBytes = Encoding.UTF8.GetBytes(file.RelativePath.ToLowerInvariant());
            sha.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);
            var content = File.ReadAllBytes(file.FullPath);
            sha.TransformBlock(content, 0, content.Length, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexStringLower(sha.Hash!)[..8];
    }

    private static IEnumerable<(string FullPath, string RelativePath)> EnumerateContentFiles(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            var topSegment = relative.Split(Path.DirectorySeparatorChar, 2)[0];
            if (ExcludedDirNames.Contains(topSegment, StringComparer.OrdinalIgnoreCase))
                continue;
            yield return (file, relative);
        }
    }

    private static void CopyRecursive(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var (fullPath, relativePath) in EnumerateContentFiles(sourceDir))
        {
            var target = Path.Combine(targetDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(fullPath, target);
        }
    }

    private static bool TryDeleteDirectory(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
