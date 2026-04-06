namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// Represents the diff for a single file in a branch comparison.
/// </summary>
public record BranchDiffFileDto(
    string Filename,
    int Additions,
    int Deletions,
    string Patch);

/// <summary>
/// Branch diff result returned from GET /api/workflows/{id}/branch-diff.
/// </summary>
public record BranchDiffDto(
    string BaseBranch,
    string HeadBranch,
    IReadOnlyList<BranchDiffFileDto> Files,
    int? PrNumber,
    string? PrUrl,
    string? PrTitle,
    string? PrState);
