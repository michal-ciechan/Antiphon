namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// Which of the two conventions a plan file follows. One list, one reader — the distinction is a
/// label on the row, not a second endpoint (the mobile-thread spec §9.4: the reader is identical
/// either way, so splitting them would buy a taxonomy and cost a surface).
/// </summary>
public enum PlanKind
{
    /// <summary>A dated file in <c>docs/superpowers/specs/</c>.</summary>
    Spec = 0,

    /// <summary>A <c>proposal.md</c> under a <c>docs/features/&lt;name&gt;/</c> folder.</summary>
    Proposal = 1,
}

/// <summary>
/// One plan file, as much of it as could be read. Every field except the path pair is BEST-EFFORT:
/// the 23 specs that exist today follow no enforced header format, and a projection that dropped
/// what it could not fully parse would be a projection over the subset of plans written after it
/// shipped — which is the opposite of the point.
/// </summary>
/// <param name="RelativePath">
/// Root-relative, forward slashes. This is the <c>?file=</c> key the content route takes, and the
/// only path form that ever leaves the server — an absolute one would invite a caller to construct
/// the next request by editing it.
/// </param>
/// <param name="Title">The first <c>#</c> heading, or the filename humanised when there isn't one.</param>
/// <param name="Date">
/// The filename's <c>YYYY-MM-DD</c> prefix, else a date read out of a <c>Date:</c> header field.
/// Null on a proposal, which carries neither.
/// </param>
/// <param name="Status">
/// The <c>Status:</c> header line, verbatim and unclassified. Deliberately NOT parsed into an enum:
/// the live corpus says "Planned", "planned, not implemented", "Proposed", "**Implemented**
/// (2026-07-19, slices 1-6 ...)" and "User decisions recorded; ready for implementation", and a
/// five-value enum over that would be a lie with a type on it.
/// </param>
/// <param name="Cards">
/// The identifiers this plan is ABOUT — read from the filename, the title and a <c>Card(s):</c>
/// header field. This is the correlation key <c>CardThreadService</c> matches on.
/// </param>
/// <param name="MentionedCards">
/// Every other <c>CARD-nnnn</c> in the file's first 200 lines: the "Relates to" and "Supersedes"
/// citations. Kept apart from <see cref="Cards"/> because most specs cite four or five neighbours,
/// and folding those in would put every plan on every thread.
/// </param>
public sealed record PlanSummaryDto(
    string RelativePath,
    string FileName,
    PlanKind Kind,
    string Title,
    DateOnly? Date,
    string? Status,
    IReadOnlyList<string> Cards,
    IReadOnlyList<string> MentionedCards,
    long SizeBytes,
    DateTime ModifiedAt);

/// <param name="Root">
/// The repo root the catalog was taken over, or null when none resolved.
/// </param>
/// <param name="RootResolved">
/// Whether a plans root was found at all. False means the list is ABSENT, not empty — the same
/// distinction <see cref="AttentionDto.RunnerConsulted"/> draws, and for the same reason: "this
/// card has no plans" and "nobody could find the repo" are different answers and a client that
/// collapses them shows a confident empty state over a broken lookup.
/// </param>
public sealed record PlanCatalogDto(
    string? Root,
    bool RootResolved,
    DateTime GeneratedAt,
    IReadOnlyList<PlanSummaryDto> Plans);

/// <summary>A plan's raw markdown, with the summary that was parsed from it.</summary>
public sealed record PlanContentDto(PlanSummaryDto Plan, string Content);
