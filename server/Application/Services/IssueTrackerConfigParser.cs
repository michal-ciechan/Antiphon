using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Antiphon.Server.Application.Services;

public static class IssueTrackerConfigParser
{
    public static bool TryParse(Board board, out IssueTrackerConfig? config, out string? error)
    {
        config = null;
        error = null;

        if (board.TrackerKind == TrackerKind.Internal)
            return false;

        var activeDefinition = board.WorkflowDefinitions
            .Where(d => d.IsActive)
            .OrderByDescending(d => d.Version)
            .FirstOrDefault();
        if (activeDefinition is null || string.IsNullOrWhiteSpace(activeDefinition.Content))
        {
            error = "Board has no active workflow definition containing tracker configuration.";
            return false;
        }

        try
        {
            var yaml = WorkflowDefinitionLoader.TryParseContent(
                activeDefinition.Content,
                out var parsed,
                out _)
                    && parsed is not null
                ? parsed.FrontMatter
                : activeDefinition.Content;

            var yamlStream = new YamlStream();
            using var reader = new StringReader(yaml);
            yamlStream.Load(reader);
            if (yamlStream.Documents.Count == 0
                || yamlStream.Documents[0].RootNode is not YamlMappingNode root)
            {
                error = "Workflow tracker configuration must be a YAML mapping.";
                return false;
            }

            var trackerNode = GetMapping(root, "tracker");
            if (trackerNode is null)
            {
                error = "Workflow definition does not contain a tracker block.";
                return false;
            }

            var kindRaw = GetScalar(trackerNode, "kind");
            if (!TryParseKind(kindRaw, out var parsedKind))
            {
                // Absent kind falls back to the board's derived index (kept in lockstep by the
                // loader after CARD-0166 S1). An explicit unparseable kind is a config error.
                if (!string.IsNullOrWhiteSpace(kindRaw))
                {
                    error = $"Tracker kind '{kindRaw}' is not a recognised tracker kind.";
                    return false;
                }

                parsedKind = board.TrackerKind;
            }

            var kind = parsedKind;
            if (kind != board.TrackerKind)
            {
                error = $"Tracker kind '{kind}' does not match board tracker kind '{board.TrackerKind}'.";
                return false;
            }

            var options = trackerNode.Children
                .Where(kvp => kvp.Key is YamlScalarNode && kvp.Value is YamlScalarNode)
                .ToDictionary(
                    kvp => ((YamlScalarNode)kvp.Key).Value ?? string.Empty,
                    kvp => ((YamlScalarNode)kvp.Value).Value ?? string.Empty,
                    StringComparer.Ordinal);

            var owner = GetScalar(trackerNode, "owner");
            var repo = GetScalar(trackerNode, "repo");
            var repository = GetScalar(trackerNode, "repository")
                ?? (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo)
                    ? null
                    : $"{owner}/{repo}");

            if (options.TryGetValue(TrackerLandingColumn.OptionKey, out var importColumn)
                && !TrackerLandingColumn.IsValidImportColumn(importColumn))
            {
                error = $"Tracker import_column '{importColumn}' is not a recognised value (backlog or active).";
                return false;
            }

            config = new IssueTrackerConfig(
                kind,
                GetScalar(trackerNode, "base_url")
                    ?? GetScalar(trackerNode, "endpoint")
                    ?? DefaultBaseUrl(kind),
                GetScalar(trackerNode, "project")
                    ?? GetScalar(trackerNode, "project_key"),
                repository,
                ParseStringList(trackerNode, "active_states")
                    ?? DefaultActiveStates(kind),
                GetScalar(trackerNode, "api_key_env")
                    ?? GetScalar(trackerNode, "token_env"),
                GetScalar(trackerNode, "jql"),
                options,
                TokenKeyName: GetScalar(trackerNode, "token_key")
                    ?? GetScalar(trackerNode, "token_key_name"));
            return true;
        }
        catch (YamlException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Shared alias table for <c>tracker.kind</c>. Returns false for null/blank/unrecognised —
    /// callers decide whether blank means Internal (loader) or "use board kind" (TryParse).
    /// </summary>
    public static bool TryParseKind(string? value, out TrackerKind kind)
    {
        kind = TrackerKind.Internal;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (Enum.TryParse<TrackerKind>(normalized, ignoreCase: true, out var parsed)
            && parsed != TrackerKind.Internal)
        {
            kind = parsed;
            return true;
        }

        if (normalized.Equals("github", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("githubissue", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("githubissues", StringComparison.OrdinalIgnoreCase))
        {
            kind = TrackerKind.GitHubIssues;
            return true;
        }

        // Explicit "internal" is a recognised kind value (deactivation via kind, not only block removal).
        if (normalized.Equals("internal", StringComparison.OrdinalIgnoreCase))
        {
            kind = TrackerKind.Internal;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolve the board's derived TrackerKind from workflow front matter.
    /// Returns false with <paramref name="error"/> when kind is present but unparseable.
    /// </summary>
    public static bool TryResolveBoardTrackerKind(
        string frontMatter,
        out TrackerKind kind,
        out string? error)
    {
        kind = TrackerKind.Internal;
        error = null;

        try
        {
            var yamlStream = new YamlStream();
            using var reader = new StringReader(frontMatter);
            yamlStream.Load(reader);
            if (yamlStream.Documents.Count == 0
                || yamlStream.Documents[0].RootNode is not YamlMappingNode root)
            {
                return true;
            }

            var trackerNode = GetMapping(root, "tracker");
            if (trackerNode is null)
                return true;

            var kindRaw = GetScalar(trackerNode, "kind");
            if (string.IsNullOrWhiteSpace(kindRaw))
                return true;

            if (!TryParseKind(kindRaw, out kind))
            {
                error = $"Tracker kind '{kindRaw}' is not a recognised tracker kind.";
                return false;
            }

            var importColumn = GetScalar(trackerNode, TrackerLandingColumn.OptionKey);
            if (!TrackerLandingColumn.IsValidImportColumn(importColumn))
            {
                error = $"Tracker import_column '{importColumn}' is not a recognised value (backlog or active).";
                return false;
            }

            return true;
        }
        catch (YamlException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static IReadOnlyList<string>? ParseStringList(YamlMappingNode mapping, string key)
    {
        var nodeKey = new YamlScalarNode(key);
        if (!mapping.Children.TryGetValue(nodeKey, out var node))
            return null;

        if (node is YamlSequenceNode sequence)
        {
            return sequence.Children
                .OfType<YamlScalarNode>()
                .Select(s => s.Value?.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToList();
        }

        if (node is YamlScalarNode scalar && !string.IsNullOrWhiteSpace(scalar.Value))
        {
            return scalar.Value
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        return null;
    }

    private static YamlMappingNode? GetMapping(YamlMappingNode mapping, string key)
    {
        var nodeKey = new YamlScalarNode(key);
        return mapping.Children.TryGetValue(nodeKey, out var node) && node is YamlMappingNode child
            ? child
            : null;
    }

    private static string? GetScalar(YamlMappingNode mapping, string key)
    {
        var nodeKey = new YamlScalarNode(key);
        return mapping.Children.TryGetValue(nodeKey, out var node)
            && node is YamlScalarNode scalar
            && !string.IsNullOrWhiteSpace(scalar.Value)
                ? scalar.Value.Trim()
                : null;
    }

    private static string DefaultBaseUrl(TrackerKind kind) =>
        kind switch
        {
            TrackerKind.Linear => "https://api.linear.app/graphql",
            TrackerKind.GitHubIssues => "https://api.github.com",
            TrackerKind.Jira => string.Empty,
            _ => string.Empty
        };

    private static IReadOnlyList<string> DefaultActiveStates(TrackerKind kind) =>
        kind switch
        {
            TrackerKind.GitHubIssues => ["open"],
            TrackerKind.Jira => ["To Do", "In Progress"],
            _ => ["Todo", "In Progress"]
        };
}
