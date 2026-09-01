using System.Reflection;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Create-time starting points (CARD-0032, CARD-0255). A preset fills the agent row on create;
/// the resulting fields stay visible and editable. Nothing re-applies a preset on PATCH.
/// The catalog returns these so scripts and the UI agree.
/// </summary>
public static class AgentPresets
{
    public const string Orchestrator = "orchestrator";
    public const string Worker = "worker";

    /// <summary>
    /// Full Feature Pipeline. Same value as <c>DatabaseSeeder.BmadFullTemplateId</c>; duplicated so
    /// Application does not reference the seeder type.
    /// </summary>
    public static readonly Guid FullFeaturePipelineTemplateId = new("b0000000-0000-0000-0000-000000000001");

    private const string OrchestratorResource =
        "Antiphon.Server.Bundles.Presets.orchestrator-prompt.md";

    public static IReadOnlyList<AgentPresetDto> All { get; } =
    [
        new(
            Orchestrator,
            "Standing orchestrator",
            "Watches the board, delegates every change.",
            AlwaysOn: true,
            ModelLevel: AgentModelLevel.High,
            ReplyStyle: AgentReplyStyle.Normal,
            BundleKeys: [InstructionBundles.Orchestrator, InstructionBundles.BoardApi],
            SystemPromptTemplate: LoadOrchestratorTemplate(),
            NamePattern: "{project} Orchestrator",
            RemoteControlEnabled: true,
            DefaultWorkflowTemplateId: FullFeaturePipelineTemplateId),
        new(
            Worker,
            "Worker",
            "A worker you hand cards or tasks to.",
            AlwaysOn: false,
            ModelLevel: AgentModelLevel.High,
            ReplyStyle: AgentReplyStyle.Normal,
            BundleKeys: [],
            SystemPromptTemplate: null,
            NamePattern: "{project} Worker"),
    ];

    public static AgentPresetDto? Find(string? key) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolves create-time fields. An explicit request value always wins, including an empty
    /// <paramref name="bundleKeys"/> (detach-all, not unset). Unknown <paramref name="presetKey"/>
    /// is 422 <c>preset</c>.
    /// </summary>
    public static AppliedAgentPreset Apply(
        string? presetKey,
        AgentModelLevel? modelLevel,
        AgentReplyStyle? replyStyle,
        bool? alwaysOn,
        bool? remoteControlEnabled,
        IReadOnlyList<string>? bundleKeys,
        string? systemPromptAppend,
        Guid? defaultWorkflowTemplateId,
        string project,
        string board,
        string? repoUrl,
        string directory)
    {
        var preset = Find(presetKey);
        if (!string.IsNullOrWhiteSpace(presetKey) && preset is null)
            throw new ValidationException("preset", $"Unknown agent preset '{presetKey}'.");

        string? prompt;
        if (systemPromptAppend is not null)
            prompt = systemPromptAppend;
        else if (preset?.SystemPromptTemplate is { } template)
            prompt = RenderTemplate(template, project, board, repoUrl, directory);
        else
            prompt = null;

        return new AppliedAgentPreset(
            ModelLevel: modelLevel ?? preset?.ModelLevel ?? AgentModelLevel.High,
            ReplyStyle: replyStyle ?? preset?.ReplyStyle ?? AgentReplyStyle.Normal,
            AlwaysOn: alwaysOn ?? preset?.AlwaysOn ?? false,
            RemoteControlEnabled: remoteControlEnabled ?? preset?.RemoteControlEnabled ?? false,
            BundleKeys: bundleKeys ?? preset?.BundleKeys,
            SystemPromptAppend: prompt,
            DefaultWorkflowTemplateId: defaultWorkflowTemplateId ?? preset?.DefaultWorkflowTemplateId);
    }

    public static string RenderTemplate(
        string? template, string project, string board, string? repoUrl, string directory)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;
        return template
            .Replace("{project}", project, StringComparison.Ordinal)
            .Replace("{board}", board, StringComparison.Ordinal)
            .Replace("{repoUrl}", string.IsNullOrWhiteSpace(repoUrl) ? "(none)" : repoUrl, StringComparison.Ordinal)
            .Replace("{directory}", directory, StringComparison.Ordinal);
    }

    public static string RenderName(string pattern, string project) =>
        pattern.Replace("{project}", project, StringComparison.Ordinal);

    internal static string LoadOrchestratorTemplate()
    {
        var assembly = typeof(AgentPresets).Assembly;
        using var stream = assembly.GetManifestResourceStream(OrchestratorResource)
            ?? throw new InvalidOperationException(
                $"Embedded preset '{OrchestratorResource}' is missing. "
                + "Add server/Bundles/Presets/orchestrator-prompt.md as an EmbeddedResource.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().ReplaceLineEndings("\n").Trim();
    }
}

/// <summary>Concrete create fields after a preset (or the hard defaults) has been applied.</summary>
public sealed record AppliedAgentPreset(
    AgentModelLevel ModelLevel,
    AgentReplyStyle ReplyStyle,
    bool AlwaysOn,
    bool RemoteControlEnabled,
    IReadOnlyList<string>? BundleKeys,
    string? SystemPromptAppend,
    Guid? DefaultWorkflowTemplateId);
