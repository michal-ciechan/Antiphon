using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
    /// <summary>
    /// Excluded from hashing and copying (never runtime inputs), matched at ANY path depth:
    /// test/report noise, alternate build outputs, and the runner's own workspace. The workspace
    /// exclusion is load-bearing — it holds these very shadow copies, so if it ever sits inside
    /// the source dir (e.g. the runner ran with cwd = its bin), a top-level-only check would copy
    /// every previous generation into the next one, nesting the tree deeper on each launch.
    /// </summary>
    private static readonly string[] ExcludedDirNames =
        ["TestResults", "workspace", "bin-verify", "bin-ptyhost", "pty-hosts"];

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
        // When the source dir carries more than the host (the runner's own bin, or a test bin with
        // hundreds of MB of unrelated assemblies), restrict to the host's dependency closure - the
        // host can only ever load what its deps.json names. Falls back to everything when absent.
        var closure = TryBuildHostClosure(root);

        foreach (var file in EnumerateFilesSkippingExcludedDirs(root))
        {
            var relative = Path.GetRelativePath(root, file);
            if (closure is not null && !closure.Contains(Path.GetFileName(file)))
                continue;
            yield return (file, relative);
        }
    }

    /// <summary>
    /// Depth-first file walk that PRUNES excluded directories instead of filtering afterwards —
    /// an infected tree can hold millions of entries, so it must never be entered at all.
    /// </summary>
    private static IEnumerable<string> EnumerateFilesSkippingExcludedDirs(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root))
            yield return file;

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            if (ExcludedDirNames.Contains(Path.GetFileName(dir), StringComparer.OrdinalIgnoreCase))
                continue;
            foreach (var file in EnumerateFilesSkippingExcludedDirs(dir))
                yield return file;
        }
    }

    /// <summary>
    /// File basenames the host needs at runtime, from <c>Antiphon.PtyHost.deps.json</c>: every
    /// managed/native asset name mentioned anywhere in the document, plus the host's own files.
    /// Returns null (no filtering) when the deps.json is not present.
    /// </summary>
    private static HashSet<string>? TryBuildHostClosure(string sourceDir)
    {
        var depsPath = Path.Combine(sourceDir, "Antiphon.PtyHost.deps.json");
        if (!File.Exists(depsPath))
            return null;

        var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Antiphon.PtyHost.exe",
            "Antiphon.PtyHost.dll",
            "Antiphon.PtyHost.pdb",
            "Antiphon.PtyHost.deps.json",
            "Antiphon.PtyHost.runtimeconfig.json",
            // The shipped pseudoconsole (CARD-0037). It is a Content item, not a package asset, so
            // it never appears in deps.json — without these two names the shadow copy would silently
            // drop them and every detached host would fall back to the inbox conhost, i.e. to the
            // clipping behaviour the flag exists to escape. Their relative path (conpty\win-x64\) is
            // preserved by the copy; only the basename is matched here. Named literally rather than
            // via Antiphon.Agents.Pty.ConPtyRedistributable so this assembly keeps its single
            // dependency — PtyBackendRedistributableTests asserts the two lists agree.
            "conpty.dll",
            "OpenConsole.exe",
        };

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(depsPath));
            CollectAssetNames(doc.RootElement, closure);
        }
        catch (JsonException)
        {
            return null; // corrupt deps.json - copy everything rather than guess
        }

        return closure;
    }

    private static void CollectAssetNames(JsonElement element, HashSet<string> closure)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AddIfAsset(property.Name, closure);
                    CollectAssetNames(property.Value, closure);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectAssetNames(item, closure);
                break;
            case JsonValueKind.String:
                AddIfAsset(element.GetString(), closure);
                break;
        }
    }

    private static void AddIfAsset(string? value, HashSet<string> closure)
    {
        if (value is null)
            return;
        if (value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
        {
            closure.Add(Path.GetFileName(value.Replace('/', Path.DirectorySeparatorChar)));
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
