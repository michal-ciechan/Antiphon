using System.Globalization;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public sealed class AgentDraftService
{
    private const int MaxDetailsLength = 1200;

    private readonly IAgentDraftGenerator _generator;
    private readonly ILogger<AgentDraftService> _logger;

    public AgentDraftService(IAgentDraftGenerator generator, ILogger<AgentDraftService> logger)
    {
        _generator = generator;
        _logger = logger;
    }

    public async Task<DraftAgentResponse> DraftAsync(DraftAgentRequest request, CancellationToken ct)
    {
        var description = request.Description?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(description))
            throw new ValidationException(nameof(request.Description), "Describe the agent you want to add.");

        AgentDraftSuggestion? generated = null;
        try
        {
            generated = await _generator.GenerateDraftAsync(description, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent draft generation failed; using local fallback.");
        }

        var fallback = CreateFallback(description);
        var name = Coalesce(generated?.Name, fallback.Name ?? "New Agent");
        var workingDirectory = Coalesce(generated?.WorkingDirectory, fallback.WorkingDirectory ?? Environment.CurrentDirectory);
        var details = Coalesce(generated?.Details, fallback.Details ?? description);
        var assignmentPolicy = generated?.AssignmentPolicy ?? fallback.AssignmentPolicy ?? AgentAssignmentPolicy.AutoPick;

        return new DraftAgentResponse(name, workingDirectory, details, assignmentPolicy, generated is not null);
    }

    private static AgentDraftSuggestion CreateFallback(string description)
    {
        return new AgentDraftSuggestion(
            InferName(description),
            InferWorkingDirectory(description),
            Truncate(description),
            AgentAssignmentPolicy.AutoPick);
    }

    private static string InferName(string description)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "and", "agent", "for", "in", "of", "on", "that", "the", "to", "with"
        };

        var words = Regex.Matches(description, @"[A-Za-z0-9][A-Za-z0-9+#.-]*")
            .Select(match => match.Value)
            .Where(word => !stopWords.Contains(word))
            .Where(word => !LooksLikePath(word))
            .Take(3)
            .Select(ToTitleCase)
            .ToList();

        return words.Count == 0 ? "New Agent" : $"{string.Join(" ", words)} Agent";
    }

    private static string InferWorkingDirectory(string description)
    {
        var windowsPath = Regex.Match(description, @"(?:[A-Za-z]:[\\/]|\\\\)[^\s,;]+");
        if (windowsPath.Success)
            return TrimPathPunctuation(windowsPath.Value);

        var unixPath = Regex.Match(description, @"(?<!\w)/(?:[^\s,;]+/?)+");
        if (unixPath.Success)
            return TrimPathPunctuation(unixPath.Value);

        var current = Environment.CurrentDirectory;
        var directory = new DirectoryInfo(current);
        return directory.Name.Equals("server", StringComparison.OrdinalIgnoreCase) && directory.Parent is not null
            ? directory.Parent.FullName
            : current;
    }

    private static string Coalesce(string? value, string fallback)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

    private static string ToTitleCase(string value)
    {
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
    }

    private static bool LooksLikePath(string value)
    {
        return value.Contains(':', StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal) ||
            value.Contains('/', StringComparison.Ordinal);
    }

    private static string TrimPathPunctuation(string value)
    {
        return value.Trim().TrimEnd('.', ',', ';', ':', ')', ']');
    }

    private static string Truncate(string value)
    {
        if (value.Length <= MaxDetailsLength)
            return value;

        return $"{value[..MaxDetailsLength].TrimEnd()}...";
    }
}
