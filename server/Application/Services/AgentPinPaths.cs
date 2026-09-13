using Antiphon.Server.Application.Exceptions;

namespace Antiphon.Server.Application.Services;

/// <summary>Canonical per-agent pin projection paths (CARD-0262 D3). Never derived from names or pin text.</summary>
public static class AgentPinPaths
{
    public const int PathSchemaVersion = 1;
    public const int MarkerVersion = 1;
    public const string DefaultHost = "local";
    public const string FileName = "antiphon.md";

    public static string AgentIdHex(Guid agentId) => agentId.ToString("N");

    public static string RelativePath(Guid agentId) =>
        $@".antiphon\pins\{AgentIdHex(agentId)}\{FileName}";

    public static string CanonicalHost(string? host)
    {
        var value = string.IsNullOrWhiteSpace(host) ? DefaultHost : host.Trim();
        return value.ToLowerInvariant();
    }

    public static string CanonicalCwd(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
            throw new ValidationException("cwd", "A projection cwd is required.");

        var full = Path.GetFullPath(cwd.Trim()).Replace('/', '\\');
        if (full.Length > 3 && full.EndsWith('\\'))
            full = full.TrimEnd('\\');
        return full;
    }

    public static string AbsolutePath(string canonicalCwd, Guid agentId)
    {
        var relative = RelativePath(agentId);
        return Path.Combine(canonicalCwd, relative);
    }
}
