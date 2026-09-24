using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0665 D-2/D-9: a pattern must be worktree-relative with <c>/</c> separators. A parent
/// segment, a leading <c>/</c>, a drive letter, a backslash or an empty entry fails on start.
/// </summary>
public sealed class WorktreeCleanupSettingsValidator : IValidateOptions<WorktreeCleanupSettings>
{
    public ValidateOptionsResult Validate(string? name, WorktreeCleanupSettings options)
    {
        var failures = new List<string>();
        Patterns(nameof(WorktreeCleanupSettings.DisposableIgnored), options.EffectiveDisposableIgnored, failures);
        Patterns(nameof(WorktreeCleanupSettings.RetainedIgnored), options.EffectiveRetainedIgnored, failures);
        Patterns(nameof(WorktreeCleanupSettings.ProtectedIgnored), options.EffectiveProtectedIgnored, failures);
        if (options.MaxRetainedEvidenceBytes <= 0)
            failures.Add("WorktreeCleanup:MaxRetainedEvidenceBytes must be positive.");
        if (options.MaxRetainedEvidenceFiles <= 0)
            failures.Add("WorktreeCleanup:MaxRetainedEvidenceFiles must be positive.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Patterns(string key, IReadOnlyList<string> patterns, List<string> failures)
    {
        foreach (var pattern in patterns)
        {
            var problem = string.IsNullOrWhiteSpace(pattern) ? "must not be empty"
                : pattern.Contains('\\') ? "must use '/' separators, not a backslash"
                : pattern.StartsWith('/') ? "must be worktree-relative, not rooted"
                : pattern.Length >= 2 && char.IsAsciiLetter(pattern[0]) && pattern[1] == ':' ? "must not name a drive"
                : pattern.Split('/').Any(segment => segment == "..") ? "must not contain a '..' segment"
                : null;
            if (problem is not null)
                failures.Add($"WorktreeCleanup:{key} pattern '{pattern}' {problem}.");
        }
    }
}
