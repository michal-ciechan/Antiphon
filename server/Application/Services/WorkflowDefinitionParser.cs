using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.ValueObjects;
using YamlDotNet.RepresentationModel;

namespace Antiphon.Server.Application.Services;

public static class WorkflowDefinitionParser
{
    private const int DefaultHookTimeoutSeconds = 30;

    public static WorkflowDefinition ParseYamlDefinition(string yaml)
    {
        var yamlStream = new YamlStream();
        using var reader = new StringReader(yaml);
        yamlStream.Load(reader);

        if (yamlStream.Documents.Count == 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["yaml"] = ["YAML document is empty."]
            });

        var root = yamlStream.Documents[0].RootNode;
        if (root is not YamlMappingNode rootMapping)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["yaml"] = ["YAML root must be a mapping."]
            });

        var name = GetScalarValue(rootMapping, "name") ?? "Unnamed Workflow";
        var description = GetScalarValue(rootMapping, "description") ?? string.Empty;
        var selectableStagesStr = GetScalarValue(rootMapping, "selectableStages");
        var selectableStages = string.Equals(selectableStagesStr, "true", StringComparison.OrdinalIgnoreCase);
        var hooks = ParseHooks(rootMapping);

        var stagesKey = new YamlScalarNode("stages");
        if (!rootMapping.Children.ContainsKey(stagesKey) ||
            rootMapping.Children[stagesKey] is not YamlSequenceNode stagesSequence)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["yaml"] = ["YAML must contain a 'stages' array."]
            });
        }

        var stages = new List<StageDefinition>();
        foreach (var child in stagesSequence.Children)
        {
            if (child is not YamlMappingNode stageMapping)
                continue;

            var stageName = GetScalarValue(stageMapping, "name")
                ?? throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["yaml"] = ["Each stage must have a 'name' field."]
                });

            var executorType = GetScalarValue(stageMapping, "executorType")
                ?? throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["yaml"] = [$"Stage '{stageName}' must have an 'executorType' field."]
                });

            var modelName = GetScalarValue(stageMapping, "modelName");
            var gateRequiredStr = GetScalarValue(stageMapping, "gateRequired");
            var gateRequired = string.Equals(gateRequiredStr, "true", StringComparison.OrdinalIgnoreCase);
            var systemPrompt = GetScalarValue(stageMapping, "systemPrompt");

            stages.Add(new StageDefinition(stageName, executorType, modelName, gateRequired, systemPrompt));
        }

        if (stages.Count == 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["yaml"] = ["'stages' array must contain at least one stage."]
            });
        }

        return new WorkflowDefinition(name, description, stages, selectableStages, hooks);
    }

    private static string? GetScalarValue(YamlMappingNode mapping, string key)
    {
        var yamlKey = new YamlScalarNode(key);
        if (mapping.Children.TryGetValue(yamlKey, out var node) && node is YamlScalarNode scalar)
            return scalar.Value;

        return null;
    }

    private static WorkflowHooks ParseHooks(YamlMappingNode rootMapping)
    {
        var hooksKey = new YamlScalarNode("hooks");
        if (!rootMapping.Children.TryGetValue(hooksKey, out var hooksNode))
            return WorkflowHooks.Empty;

        if (hooksNode is not YamlMappingNode hooksMapping)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["yaml"] = ["'hooks' must be a mapping."]
            });
        }

        var allowedHookNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "after_create",
            "before_run",
            "after_run",
            "before_remove"
        };

        foreach (var key in hooksMapping.Children.Keys.OfType<YamlScalarNode>())
        {
            if (key.Value is not null && !allowedHookNames.Contains(key.Value))
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["yaml"] = [$"Unknown hook '{key.Value}'."]
                });
            }
        }

        return new WorkflowHooks(
            ParseHook(hooksMapping, "after_create"),
            ParseHook(hooksMapping, "before_run"),
            ParseHook(hooksMapping, "after_run"),
            ParseHook(hooksMapping, "before_remove"));
    }

    private static WorkspaceHookDefinition? ParseHook(YamlMappingNode hooksMapping, string hookName)
    {
        var hookKey = new YamlScalarNode(hookName);
        if (!hooksMapping.Children.TryGetValue(hookKey, out var hookNode))
            return null;

        if (hookNode is YamlScalarNode scalar)
            return CreateHookDefinition(hookName, scalar.Value, null);

        if (hookNode is YamlMappingNode hookMapping)
        {
            var command = GetScalarValue(hookMapping, "command");
            var timeoutSeconds = GetOptionalHookScalar(hookMapping, hookName, "timeout_seconds")
                ?? GetOptionalHookScalar(hookMapping, hookName, "timeoutSeconds");

            return CreateHookDefinition(hookName, command, timeoutSeconds);
        }

        throw new ValidationException(new Dictionary<string, string[]>
        {
            ["yaml"] = [$"Hook '{hookName}' must be a command string or mapping."]
        });
    }

    private static string? GetOptionalHookScalar(YamlMappingNode hookMapping, string hookName, string fieldName)
    {
        var key = new YamlScalarNode(fieldName);
        if (!hookMapping.Children.TryGetValue(key, out var node))
            return null;

        if (node is YamlScalarNode scalar)
            return scalar.Value;

        throw new ValidationException(new Dictionary<string, string[]>
        {
            ["yaml"] = [$"Hook '{hookName}' {fieldName} must be a scalar value."]
        });
    }

    private static WorkspaceHookDefinition CreateHookDefinition(
        string hookName,
        string? command,
        string? timeoutSeconds)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["yaml"] = [$"Hook '{hookName}' must define a non-empty command."]
            });
        }

        var timeout = DefaultHookTimeoutSeconds;
        if (!string.IsNullOrWhiteSpace(timeoutSeconds)
            && (!int.TryParse(timeoutSeconds, out timeout) || timeout <= 0))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["yaml"] = [$"Hook '{hookName}' timeout_seconds must be a positive integer."]
            });
        }

        return new WorkspaceHookDefinition(command, TimeSpan.FromSeconds(timeout));
    }
}
