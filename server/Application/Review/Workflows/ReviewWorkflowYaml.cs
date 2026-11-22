using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Antiphon.Server.Application.Review.Workflows;

/// <summary>
/// Loads <c>.antiphon/review/workflow.yaml</c> (design 3.3). Edges are strings in a small arrow
/// language, <c>a -> b -> c</c> and <c>a[out1|out2] -> b</c>, or mappings that add edge settings:
/// <c>{ edge: "a[out] -> b", maxIterations: 10, overflow: gate-id, description: ... }</c>.
/// Structural problems throw <see cref="WorkflowYamlException"/>; graph rules belong to
/// <see cref="ReviewWorkflowValidator"/>.
/// </summary>
public static class ReviewWorkflowYaml
{
    public const string DefaultResourceName = "Antiphon.Server.Review.pr-review.yaml";

    private static readonly Regex Endpoint = new(
        "^(?<node>[A-Za-z][A-Za-z0-9_-]*)(?:\\[(?<label>[^\\]]+)\\])?$", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> NodeKeys = new(StringComparer.Ordinal) { "kind", "run", "description" };

    /// <summary>The default <c>pr-review</c> workflow, embedded in the server assembly.</summary>
    public static WorkflowDefinition LoadDefault()
    {
        var assembly = typeof(ReviewWorkflowYaml).Assembly;
        using var stream = assembly.GetManifestResourceStream(DefaultResourceName)
            ?? throw new InvalidOperationException($"Embedded workflow {DefaultResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static WorkflowDefinition Parse(string yaml)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            throw new WorkflowYamlException($"workflow.yaml is not valid YAML: {ex.Message}");
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new WorkflowYamlException("workflow.yaml must be a mapping.");

        var name = Scalar(root, "workflow") ?? throw new WorkflowYamlException("workflow.yaml needs a 'workflow' name.");
        var version = int.TryParse(Scalar(root, "version"), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 1;

        var nodes = new List<WorkflowNode>();
        if (Child(root, "nodes") is YamlMappingNode nodeMap)
        {
            foreach (var (key, value) in nodeMap.Children)
            {
                var id = ((YamlScalarNode)key).Value ?? "";
                if (value is not YamlMappingNode body)
                    throw new WorkflowYamlException($"Node '{id}' must be a mapping.");
                var kindText = Scalar(body, "kind");
                if (!WorkflowDefinition.TryParseKind(kindText, out var kind))
                    throw new WorkflowYamlException($"Node '{id}' has unknown kind '{kindText}'.");
                var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var (paramKey, paramValue) in body.Children)
                {
                    var keyText = ((YamlScalarNode)paramKey).Value ?? "";
                    if (!NodeKeys.Contains(keyText))
                        parameters[keyText] = ToValue(paramValue);
                }

                nodes.Add(new WorkflowNode(id, kind, Scalar(body, "run"), Scalar(body, "description"), parameters));
            }
        }

        var sections = new List<WorkflowSection>();
        if (Child(root, "sections") is YamlMappingNode sectionMap)
        {
            foreach (var (key, value) in sectionMap.Children)
            {
                var id = ((YamlScalarNode)key).Value ?? "";
                if (value is not YamlMappingNode body)
                    throw new WorkflowYamlException($"Section '{id}' must be a mapping.");
                var members = Child(body, "nodes") is YamlSequenceNode seq
                    ? seq.Children.OfType<YamlScalarNode>().Select(s => s.Value ?? "").ToList()
                    : [];
                sections.Add(new WorkflowSection(id, Scalar(body, "title") ?? id, members,
                    string.Equals(Scalar(body, "collapsed"), "true", StringComparison.OrdinalIgnoreCase)));
            }
        }

        var edges = new List<WorkflowEdge>();
        if (Child(root, "edges") is YamlSequenceNode edgeList)
        {
            foreach (var item in edgeList.Children)
            {
                switch (item)
                {
                    case YamlScalarNode scalar:
                        edges.AddRange(ParseArrows(scalar.Value ?? "", null, null, null));
                        break;
                    case YamlMappingNode mapping:
                        var text = Scalar(mapping, "edge") ?? throw new WorkflowYamlException("An edge mapping needs 'edge'.");
                        int? max = null;
                        if (Scalar(mapping, "maxIterations") is { } maxText)
                        {
                            if (!int.TryParse(maxText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                                throw new WorkflowYamlException($"Edge '{text}' has a non-integer maxIterations '{maxText}'.");
                            max = parsed;
                        }

                        edges.AddRange(ParseArrows(text, max, Scalar(mapping, "overflow"), Scalar(mapping, "description")));
                        break;
                    default:
                        throw new WorkflowYamlException("An edge must be a string or a mapping.");
                }
            }
        }

        return new WorkflowDefinition(name, version, sections, nodes, edges);
    }

    /// <summary><c>a -> b[x] -> c</c>: a label belongs to the node it follows and names that node's outputs.</summary>
    private static IEnumerable<WorkflowEdge> ParseArrows(string text, int? maxIterations, string? overflow, string? description)
    {
        var parts = text.Split("->", StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Any(p => p.Length == 0))
            throw new WorkflowYamlException($"Edge '{text}' must be 'from -> to'.");
        var endpoints = parts.Select(p =>
        {
            var match = Endpoint.Match(p);
            if (!match.Success)
                throw new WorkflowYamlException($"Edge endpoint '{p}' in '{text}' must be 'node' or 'node[output]'.");
            return (Node: match.Groups["node"].Value, Label: match.Groups["label"].Success ? match.Groups["label"].Value.Trim() : null);
        }).ToList();
        if (parts.Length > 2 && maxIterations is not null)
            throw new WorkflowYamlException($"Edge '{text}': maxIterations needs a single 'from -> to' edge.");
        for (var i = 0; i < endpoints.Count - 1; i++)
        {
            if (endpoints[i + 1].Label is not null && i + 1 == endpoints.Count - 1)
                throw new WorkflowYamlException($"Edge '{text}' ends with an output label; labels go on the source node.");
            yield return new WorkflowEdge(endpoints[i].Node, endpoints[i + 1].Node, endpoints[i].Label, maxIterations, overflow, description);
        }
    }

    private static YamlNode? Child(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node : null;

    private static string? Scalar(YamlMappingNode mapping, string key) => (Child(mapping, key) as YamlScalarNode)?.Value;

    /// <summary>Scalars become int, bool or string; sequences lists; mappings dictionaries.</summary>
    private static object? ToValue(YamlNode node) => node switch
    {
        YamlScalarNode { Value: null } => null,
        YamlScalarNode scalar when scalar.Style is ScalarStyle.Plain
            && long.TryParse(scalar.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) => number,
        YamlScalarNode scalar when scalar.Style is ScalarStyle.Plain && scalar.Value is "true" or "false" => scalar.Value == "true",
        YamlScalarNode scalar => scalar.Value,
        YamlSequenceNode sequence => sequence.Children.Select(ToValue).ToList(),
        YamlMappingNode mapping => mapping.Children.ToDictionary(
            p => ((YamlScalarNode)p.Key).Value ?? "", p => ToValue(p.Value), StringComparer.Ordinal),
        _ => null,
    };
}

public sealed class WorkflowYamlException(string message) : Exception(message);
