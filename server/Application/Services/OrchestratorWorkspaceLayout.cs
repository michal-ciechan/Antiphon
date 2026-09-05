using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0251 S1: positively identify an orchestrator's working directory as a dedicated
/// sibling workspace, the nested shape that leaks Claude instructions into every delegate,
/// today's checkout-as-cwd, or neither. Classify is pure over already-gathered facts —
/// the server must not shell out to a CLI on a readiness read.
/// </summary>
public static class OrchestratorWorkspaceLayout
{
    public const string MarkerFileName = "antiphon.workspace.json";
    public const int MarkerVersion = 1;

    private static readonly JsonSerializerOptions MarkerJson = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Regex CodexProjectHeader = new(
        @"^\[projects\.'([^']+)'\]\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex GrokFolderHeader = new(
        @"^\[folders\.'([^']+)'\]\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    public static OrchestratorWorkspaceState Classify(
        OrchestratorWorkspaceDirectoryFacts dir,
        OrchestratorWorkspaceCli cli,
        OrchestratorWorkspaceHomeState homeState)
    {
        if (IsDedicatedShape(dir))
        {
            if (CheckoutIsNestedInside(dir))
                return OrchestratorWorkspaceState.DedicatedNested;
            if (dir.DirectoryGitToplevel is null
                && homeState.PreconditionHolds(cli))
                return OrchestratorWorkspaceState.Dedicated;
            if (dir.DirectoryGitToplevel is null)
                return OrchestratorWorkspaceState.DedicatedUnapproved;
        }

        if (!string.IsNullOrEmpty(dir.DirectoryGitToplevel))
        {
            return dir.GitRootHasInstructionArtifacts
                ? OrchestratorWorkspaceState.CheckoutAsCwd
                : OrchestratorWorkspaceState.Unconfigured;
        }

        return OrchestratorWorkspaceState.Foreign;
    }

    public static bool TryParseMarker(string? json, out OrchestratorWorkspaceMarker marker)
    {
        marker = default!;
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            var dto = JsonSerializer.Deserialize<MarkerDto>(json, MarkerJson);
            if (dto is null || dto.Version != MarkerVersion || string.IsNullOrWhiteSpace(dto.Checkout))
                return false;
            marker = new OrchestratorWorkspaceMarker(
                dto.Version,
                dto.Checkout.Trim(),
                dto.Project,
                dto.Cli);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Claude's project key is the forward-slash form. A backslash key is written by
    /// some tools and silently misses — the import stays dropped.
    /// </summary>
    public static ClaudeExternalIncludesApproval ReadClaudeExternalIncludes(
        string? claudeJson, string directory)
    {
        if (string.IsNullOrWhiteSpace(claudeJson) || string.IsNullOrWhiteSpace(directory))
            return ClaudeExternalIncludesApproval.Absent;

        try
        {
            using var doc = JsonDocument.Parse(claudeJson);
            if (!doc.RootElement.TryGetProperty("projects", out var projects)
                || projects.ValueKind != JsonValueKind.Object)
                return ClaudeExternalIncludesApproval.Absent;

            var key = ForwardSlashKey(directory);
            if (!projects.TryGetProperty(key, out var project)
                || project.ValueKind != JsonValueKind.Object)
                return ClaudeExternalIncludesApproval.Absent;

            if (!project.TryGetProperty("hasClaudeMdExternalIncludesApproved", out var flag)
                || flag.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return ClaudeExternalIncludesApproval.Absent;

            if (flag.ValueKind == JsonValueKind.True)
                return ClaudeExternalIncludesApproval.Approved;
            if (flag.ValueKind == JsonValueKind.False)
                return ClaudeExternalIncludesApproval.Declined;
            if (flag.ValueKind == JsonValueKind.String
                && bool.TryParse(flag.GetString(), out var parsed))
            {
                return parsed
                    ? ClaudeExternalIncludesApproval.Approved
                    : ClaudeExternalIncludesApproval.Declined;
            }

            return ClaudeExternalIncludesApproval.Absent;
        }
        catch (JsonException)
        {
            return ClaudeExternalIncludesApproval.Absent;
        }
    }

    /// <summary>
    /// Codex project keys are lower-case backslash paths:
    /// <c>[projects.'c:\src\antiphon']</c>. Mixed case or forward slashes do not match.
    /// </summary>
    public static bool ReadCodexTrusted(string? configToml, string directory)
    {
        if (string.IsNullOrWhiteSpace(configToml) || string.IsNullOrWhiteSpace(directory))
            return false;

        var expected = LowerBackslashKey(directory);
        foreach (Match header in CodexProjectHeader.Matches(configToml))
        {
            if (!string.Equals(header.Groups[1].Value, expected, StringComparison.Ordinal))
                continue;
            var body = TableBody(configToml, header.Index + header.Length);
            if (HasAssignment(body, "trust_level", "trusted"))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Grok folder trust is an exact path in <c>~/.grok/trusted_folders.toml</c>. Either
    /// slash form matches; a parent folder does not cover a different path.
    /// </summary>
    public static bool ReadGrokTrusted(string? trustedFoldersToml, string directory)
    {
        if (string.IsNullOrWhiteSpace(trustedFoldersToml) || string.IsNullOrWhiteSpace(directory))
            return false;

        foreach (Match header in GrokFolderHeader.Matches(trustedFoldersToml))
        {
            if (!SameFilesystemPath(header.Groups[1].Value, directory))
                continue;
            var body = TableBody(trustedFoldersToml, header.Index + header.Length);
            if (HasAssignment(body, "trusted", "true"))
                return true;
        }

        return false;
    }

    public static string ForwardSlashKey(string directory)
    {
        var full = Path.GetFullPath(directory).TrimEnd('\\', '/');
        return full.Replace('\\', '/');
    }

    public static string LowerBackslashKey(string directory)
    {
        var full = Path.GetFullPath(directory).TrimEnd('/', '\\');
        return full.Replace('/', '\\').ToLowerInvariant();
    }

    /// <summary>
    /// Convention sibling: <c>C:\src\gym-stat-orchestrator</c> beside <c>C:\src\gym-stat</c>.
    /// The marker records the real link; this is only the default the plan/readiness name.
    /// </summary>
    public static string ProposedSiblingPath(string checkout)
    {
        var full = Path.GetFullPath(checkout).TrimEnd('\\', '/');
        var parent = Path.GetDirectoryName(full) ?? full;
        var name = Path.GetFileName(full);
        if (string.IsNullOrEmpty(name))
            return Path.Combine(full, "orchestrator");
        return Path.Combine(parent, name + "-orchestrator");
    }

    public static OrchestratorWorkspaceCli CliFromKind(AgentKind kind) => kind switch
    {
        AgentKind.Codex => OrchestratorWorkspaceCli.Codex,
        AgentKind.Grok => OrchestratorWorkspaceCli.Grok,
        _ => OrchestratorWorkspaceCli.Claude,
    };

    internal static bool ContextFileNamesCheckoutAgents(
        OrchestratorWorkspaceCli cli, string content, string directory, string checkout)
    {
        var target = Path.GetFullPath(Path.Combine(checkout, "AGENTS.md"));
        if (cli == OrchestratorWorkspaceCli.Claude)
            return HasResolvingAtImport(content, directory, target);
        return MentionsPath(content, directory, target);
    }

    internal static bool HasInstructionArtifacts(string root)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            return false;
        if (File.Exists(Path.Combine(root, "AGENTS.md"))) return true;
        if (File.Exists(Path.Combine(root, "CLAUDE.md"))) return true;
        if (File.Exists(Path.Combine(root, ".claude", "settings.json"))) return true;
        if (Directory.Exists(Path.Combine(root, ".codex"))) return true;
        if (Directory.Exists(Path.Combine(root, ".grok"))) return true;
        return false;
    }

    internal static string ContextFileName(OrchestratorWorkspaceCli cli) =>
        cli == OrchestratorWorkspaceCli.Claude ? "CLAUDE.md" : "AGENTS.md";

    internal static bool SameFilesystemPath(string a, string b)
    {
        try
        {
            var na = Path.GetFullPath(a).TrimEnd('\\', '/');
            var nb = Path.GetFullPath(b).TrimEnd('\\', '/');
            return string.Equals(na, nb,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    internal static bool IsWithin(string candidate, string root)
        => DelegationWorkspaceResolver.IsWithinRoot(candidate, root);

    private static bool IsDedicatedShape(OrchestratorWorkspaceDirectoryFacts dir)
    {
        if (dir.Marker is null || string.IsNullOrEmpty(dir.ResolvedCheckout) || !dir.CheckoutExists)
            return false;
        if (string.IsNullOrEmpty(dir.CheckoutGitToplevel)
            || !SameFilesystemPath(dir.CheckoutGitToplevel, dir.ResolvedCheckout))
            return false;
        return dir.ContextFileExists && dir.ContextFileNamesCheckoutAgents;
    }

    private static bool CheckoutIsNestedInside(OrchestratorWorkspaceDirectoryFacts dir)
    {
        if (string.IsNullOrEmpty(dir.ResolvedCheckout) || !dir.CheckoutExists)
            return false;
        if (SameFilesystemPath(dir.ResolvedCheckout, dir.Path))
            return false;
        return IsWithin(dir.ResolvedCheckout, dir.Path);
    }

    private static bool HasResolvingAtImport(string content, string directory, string target)
    {
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('@'))
                continue;
            var spec = trimmed[1..].Trim().Trim('"').Trim('\'');
            if (string.IsNullOrEmpty(spec))
                continue;
            if (TryResolve(directory, spec) is { } resolved && SameFilesystemPath(resolved, target))
                return true;
        }

        return false;
    }

    private static bool MentionsPath(string content, string directory, string target)
    {
        var relative = Path.GetRelativePath(directory, target);
        if (ContainsPathForm(content, relative) || ContainsPathForm(content, target))
            return true;
        return HasResolvingAtImport(content, directory, target);
    }

    private static bool ContainsPathForm(string content, string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        var forward = path.Replace('\\', '/');
        var back = path.Replace('/', '\\');
        return content.Contains(forward, StringComparison.OrdinalIgnoreCase)
            || content.Contains(back, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryResolve(string directory, string path)
    {
        try
        {
            var combined = Path.IsPathRooted(path) ? path : Path.Combine(directory, path);
            return Path.GetFullPath(combined);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string TableBody(string toml, int start)
    {
        var next = toml.IndexOf('\n', start);
        var cursor = next < 0 ? start : next + 1;
        while (cursor < toml.Length)
        {
            var eol = toml.IndexOf('\n', cursor);
            var line = eol < 0 ? toml[cursor..] : toml[cursor..eol];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('[') && !trimmed.StartsWith("[[", StringComparison.Ordinal))
                return toml[start..cursor];
            if (eol < 0)
                return toml[start..];
            cursor = eol + 1;
        }

        return toml[start..];
    }

    private static bool HasAssignment(string body, string key, string value)
    {
        using var reader = new StringReader(body);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            var eq = trimmed.IndexOf('=');
            if (eq <= 0)
                continue;
            var left = trimmed[..eq].Trim();
            var right = trimmed[(eq + 1)..].Trim().Trim('"').Trim('\'');
            if (string.Equals(left, key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(right, value, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private sealed class MarkerDto
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("checkout")]
        public string? Checkout { get; set; }

        [JsonPropertyName("project")]
        public Guid? Project { get; set; }

        [JsonPropertyName("cli")]
        public string? Cli { get; set; }
    }
}

public enum OrchestratorWorkspaceCli
{
    Claude = 0,
    Codex = 1,
    Grok = 2,
}

public enum OrchestratorWorkspaceState
{
    Dedicated = 0,
    DedicatedNested = 1,
    DedicatedUnapproved = 2,
    CheckoutAsCwd = 3,
    Unconfigured = 4,
    Foreign = 5,
}

/// <summary>
/// Claude's external-import flag is three-valued: missing, explicitly declined
/// (<c>false</c>), or approved. Both missing and declined classify as
/// <see cref="OrchestratorWorkspaceState.DedicatedUnapproved"/>.
/// </summary>
public enum ClaudeExternalIncludesApproval
{
    Absent = 0,
    Declined = 1,
    Approved = 2,
}

public sealed record OrchestratorWorkspaceMarker(
    int Version,
    string Checkout,
    Guid? Project,
    string? Cli);

public sealed record OrchestratorWorkspaceHomeState(
    ClaudeExternalIncludesApproval ClaudeExternalIncludes,
    bool CodexTrusted,
    bool GrokTrusted)
{
    public static OrchestratorWorkspaceHomeState None { get; } = new(
        ClaudeExternalIncludesApproval.Absent, CodexTrusted: false, GrokTrusted: false);

    public bool PreconditionHolds(OrchestratorWorkspaceCli cli) => cli switch
    {
        OrchestratorWorkspaceCli.Claude =>
            ClaudeExternalIncludes == ClaudeExternalIncludesApproval.Approved,
        OrchestratorWorkspaceCli.Codex => CodexTrusted,
        OrchestratorWorkspaceCli.Grok => GrokTrusted,
        _ => false,
    };
}

/// <summary>Filesystem facts for one candidate directory. No home-config, no I/O.</summary>
public sealed record OrchestratorWorkspaceDirectoryFacts(
    string Path,
    OrchestratorWorkspaceMarker? Marker,
    string? ResolvedCheckout,
    bool CheckoutExists,
    string? DirectoryGitToplevel,
    string? CheckoutGitToplevel,
    bool ContextFileExists,
    bool ContextFileNamesCheckoutAgents,
    bool GitRootHasInstructionArtifacts);

/// <summary>
/// I/O adapter for <see cref="OrchestratorWorkspaceLayout"/>. Gathers the small fact
/// record Classify consumes; never shells out to Claude/Codex/Grok.
/// </summary>
public sealed class OrchestratorWorkspaceFactGatherer
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(10);

    public async Task<OrchestratorWorkspaceState> ClassifyFromDiskAsync(
        string directory, OrchestratorWorkspaceCli cli, CancellationToken ct = default)
    {
        var facts = await GatherAsync(directory, cli, ct);
        var home = ReadHomeFromProfile(directory);
        return OrchestratorWorkspaceLayout.Classify(facts, cli, home);
    }

    public async Task<OrchestratorWorkspaceDirectoryFacts> GatherAsync(
        string directory, OrchestratorWorkspaceCli cli, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(directory);
        OrchestratorWorkspaceMarker? marker = null;
        var markerPath = Path.Combine(full, OrchestratorWorkspaceLayout.MarkerFileName);
        if (File.Exists(markerPath)
            && OrchestratorWorkspaceLayout.TryParseMarker(File.ReadAllText(markerPath), out var parsed))
            marker = parsed;

        string? resolvedCheckout = null;
        var checkoutExists = false;
        if (marker is not null)
        {
            resolvedCheckout = ResolveCheckout(full, marker.Checkout);
            checkoutExists = resolvedCheckout is not null && Directory.Exists(resolvedCheckout);
        }

        var directoryToplevel = await GetGitToplevelAsync(full, cancellationToken);
        var checkoutToplevel = checkoutExists
            ? await GetGitToplevelAsync(resolvedCheckout!, cancellationToken)
            : null;

        var contextName = OrchestratorWorkspaceLayout.ContextFileName(cli);
        var contextPath = Path.Combine(full, contextName);
        var contextExists = File.Exists(contextPath);
        var namesCheckout = false;
        if (contextExists && checkoutExists)
        {
            namesCheckout = OrchestratorWorkspaceLayout.ContextFileNamesCheckoutAgents(
                cli, File.ReadAllText(contextPath), full, resolvedCheckout!);
        }

        var artifactRoot = directoryToplevel ?? full;
        var artifacts = OrchestratorWorkspaceLayout.HasInstructionArtifacts(artifactRoot);

        return new OrchestratorWorkspaceDirectoryFacts(
            full,
            marker,
            resolvedCheckout,
            checkoutExists,
            directoryToplevel,
            checkoutToplevel,
            contextExists,
            namesCheckout,
            artifacts);
    }

    public OrchestratorWorkspaceHomeState ReadHome(
        string directory, string? claudeJson, string? codexToml, string? grokToml) =>
        new(
            OrchestratorWorkspaceLayout.ReadClaudeExternalIncludes(claudeJson, directory),
            OrchestratorWorkspaceLayout.ReadCodexTrusted(codexToml, directory),
            OrchestratorWorkspaceLayout.ReadGrokTrusted(grokToml, directory));

    public OrchestratorWorkspaceHomeState ReadHomeFromProfile(string directory)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return ReadHome(
            directory,
            TryReadFile(Path.Combine(home, ".claude.json")),
            TryReadFile(Path.Combine(home, ".codex", "config.toml")),
            TryReadFile(Path.Combine(home, ".grok", "trusted_folders.toml")));
    }

    /// <summary>
    /// CARD-0251 S4: when <paramref name="directory"/> is a dedicated sibling workspace,
    /// return the marker's checkout; otherwise return the directory itself. Used by
    /// <c>card.ps1</c> / <c>delegate.ps1</c> and <see cref="AgentTaskService.Caller"/>.
    /// </summary>
    public static string FollowMarkerOrSelf(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return directory;
        try
        {
            var full = Path.GetFullPath(directory);
            var markerPath = Path.Combine(full, OrchestratorWorkspaceLayout.MarkerFileName);
            if (!File.Exists(markerPath))
                return full;
            if (!OrchestratorWorkspaceLayout.TryParseMarker(File.ReadAllText(markerPath), out var marker))
                return full;
            var checkout = ResolveCheckout(full, marker.Checkout);
            return checkout is not null && Directory.Exists(checkout) ? checkout : full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                                   or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return directory;
        }
    }

    private static string? ResolveCheckout(string directory, string checkout)
    {
        try
        {
            var combined = Path.IsPathRooted(checkout) ? checkout : Path.Combine(directory, checkout);
            return Path.GetFullPath(combined);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? TryReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<string?> GetGitToplevelAsync(string directory, CancellationToken ct)
    {
        if (!Directory.Exists(directory))
            return null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("rev-parse");
            psi.ArgumentList.Add("--show-toplevel");
            using var process = Process.Start(psi);
            if (process is null)
                return null;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(GitTimeout);
            var stdout = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            var top = stdout.Trim();
            return process.ExitCode == 0 && top.Length > 0 ? Path.GetFullPath(top) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested)
                                   || ex is AggregateException)
        {
            return null;
        }
    }
}
