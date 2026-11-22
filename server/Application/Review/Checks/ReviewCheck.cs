using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Review.Checks;

/// <summary>A <c># @&lt;audience&gt;: &lt;name&gt;</c> section of a check file (design 9).</summary>
public sealed record ReviewCheckSection(string Audience, string Name, string Body);

/// <summary>What makes a check a candidate before any model call (design 4.4 step 1). Empty: always a candidate.</summary>
public sealed record ReviewCheckTriggers(
    IReadOnlyList<string> Globs,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Symbols)
{
    public static readonly ReviewCheckTriggers None = new([], [], [], []);

    public bool IsEmpty => Globs.Count == 0 && Languages.Count == 0 && Tags.Count == 0 && Symbols.Count == 0;
}

/// <summary>
/// One check (design 9): front matter plus audience sections. <see cref="PromptHash"/> covers the
/// <c>@model</c> sections only, so editing rationale or revision notes changes nothing a model saw.
/// </summary>
public sealed record ReviewCheck(
    string Id,
    string Title,
    string Category,
    ReviewSeverity Severity,
    IReadOnlyList<string> Flags,
    string Status,
    bool RequiresHuman,
    ReviewCheckTriggers Triggers,
    IReadOnlyList<string> RelatedFiles,
    IReadOnlyList<ReviewCheckSection> Sections,
    string PromptHash,
    string Source)
{
    public const string ModelAudience = "model";
    public const string HumansAudience = "humans";

    public ReviewCheckSection? Section(string audience, string name) =>
        Sections.FirstOrDefault(s => s.Audience == audience && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<ReviewCheckSection> ModelSections => Sections.Where(s => s.Audience == ModelAudience);

    /// <summary>The <c>@model: Selector</c> text, the instructions of the Jev <c>applies</c> question; the title when absent.</summary>
    public string Selector => Section(ModelAudience, "Selector")?.Body is { Length: > 0 } body ? body : Title;

    /// <summary>The first <c>@humans</c> section: shown next to a finding from this check.</summary>
    public string? HumansWhy => Sections.FirstOrDefault(s => s.Audience == HumansAudience)?.Body;

    public bool IsActive => string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A parsed check file, or why it is not one.</summary>
public sealed record ReviewCheckParseResult(ReviewCheck? Check, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
