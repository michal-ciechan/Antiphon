using System.Text.RegularExpressions;

namespace Antiphon.HostCleanup;

public static class CleanupPath
{
    public static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');

    public static bool IsAbsolute(string path)
    {
        var value = Normalize(path);
        return value.StartsWith("/", StringComparison.Ordinal) ||
               (value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && value[2] == '/');
    }

    public static bool IsSafe(string path)
    {
        var value = Normalize(path);
        if (!IsAbsolute(value) || value.Contains('\0') || value.IndexOfAny(['*', '?', '[', ']']) >= 0) return false;
        if (value.StartsWith("//", StringComparison.Ordinal)) return false;
        var parts = value.Split('/');
        return !parts.Skip(1).Any(part => part is "" or "." or "..");
    }

    public static bool IsSameOrChild(string path, string ancestor)
    {
        var candidate = Normalize(path);
        var parent = Normalize(ancestor);
        var comparison = parent.Length >= 2 && parent[1] == ':'
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(candidate, parent, comparison) ||
               candidate.StartsWith(parent + '/', comparison);
    }

    public static string Leaf(string path) => Normalize(path).Split('/').Last();
}

public sealed class CleanupFamilyRegistry
{
    private static readonly Regex CheckpointName = new("^c723-[0-9a-f]{32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ProbeName = new("^(?:c(?:527|487|590|408|490)-[a-zA-Z0-9_-]+|c[0-9]+-probe-[a-zA-Z0-9_-]+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LandVerifyName = new("^antiphon-land-verify-[a-zA-Z0-9_-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IReadOnlyList<CleanupRoot> roots;
    private readonly IReadOnlyList<string> extraDenied;

    public CleanupFamilyRegistry(IReadOnlyList<CleanupRoot> roots, IReadOnlyList<string>? extraDenied = null)
    {
        if (roots.Count == 0) throw new ArgumentException("At least one explicit root is required.", nameof(roots));
        foreach (var root in roots)
        {
            if (!CleanupPath.IsSafe(root.Path) || string.IsNullOrWhiteSpace(root.StorageId))
                throw new ArgumentException("Each cleanup root needs an absolute safe path and storage identity.", nameof(roots));
        }
        for (var i = 0; i < roots.Count; i++)
        for (var j = i + 1; j < roots.Count; j++)
            if (CleanupPath.IsSameOrChild(roots[i].Path, roots[j].Path) ||
                CleanupPath.IsSameOrChild(roots[j].Path, roots[i].Path))
                throw new ArgumentException("Cleanup roots must not overlap.", nameof(roots));
        this.roots = roots;
        this.extraDenied = extraDenied ?? [];
    }

    public CleanupRoot? FindRoot(string candidatePath)
    {
        if (!CleanupPath.IsSafe(candidatePath)) return null;
        return roots.SingleOrDefault(root => CleanupPath.IsSameOrChild(candidatePath, root.Path));
    }

    public bool IsDenied(string path)
    {
        if (!CleanupPath.IsSafe(path)) return true;
        var value = CleanupPath.Normalize(path);
        foreach (var denied in extraDenied)
            if (CleanupPath.IsSameOrChild(value, denied) || CleanupPath.IsSameOrChild(denied, value))
                return true;

        // The hard list cannot be removed by configuration. Protect both parents and children.
        var hardPaths = new[] { "/tmp/antiphon-pty-hosts", "/var/lib/docker", "/var/lib/postgresql" };
        foreach (var denied in hardPaths)
            if (CleanupPath.IsSameOrChild(value, denied) || CleanupPath.IsSameOrChild(denied, value))
                return true;

        var components = value.Split('/');
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".claude", ".codex", ".grok", "node_modules",
            "antiphon-pty-hosts", "build-slots", "runner-state", "hangfire",
            "postgres", "docker", "reports", "deliverables"
        };
        return components.Any(forbidden.Contains);
    }

    public bool IsFamilyShape(CleanupRoot root, string candidatePath)
    {
        var value = CleanupPath.Normalize(candidatePath);
        var parent = value[..Math.Max(0, value.LastIndexOf('/'))];
        var name = CleanupPath.Leaf(value);
        switch (root.Family)
        {
            case CleanupFamily.Checkpoint:
                return parent == CleanupPath.Normalize(root.Path) && CheckpointName.IsMatch(name);
            case CleanupFamily.Probe:
                return parent == CleanupPath.Normalize(root.Path) && ProbeName.IsMatch(name);
            case CleanupFamily.LandVerify:
                return parent == CleanupPath.Normalize(root.Path) && LandVerifyName.IsMatch(name);
            case CleanupFamily.BuildOutput:
                return name == "obj" || name.StartsWith("bin-", StringComparison.Ordinal);
            case CleanupFamily.TaskScratch:
            case CleanupFamily.WorkScratch:
            case CleanupFamily.PrivateCache:
            case CleanupFamily.TestSandbox:
                return parent != value && value != CleanupPath.Normalize(root.Path);
            default:
                return false;
        }
    }
}
