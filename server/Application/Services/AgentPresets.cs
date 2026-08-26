using System.Reflection;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Server-side starting points for setup (CARD-0032). A preset is what the UI shows and the user
/// can edit, not a hidden default. The catalog returns these so scripts and the UI agree.
/// </summary>
public static class AgentPresets
{
    public const string Orchestrator = "orchestrator";
    public const string Worker = "worker";

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
            ReplyStyle: AgentReplyStyle.Caveman,
            BundleKeys: [InstructionBundles.Orchestrator, InstructionBundles.BoardApi],
            SystemPromptTemplate: LoadOrchestratorTemplate(),
            NamePattern: "{project} Orchestrator"),
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
