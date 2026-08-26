using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// One piece of work, assembled from the four places it is actually recorded: the card row, the
/// plan files in git, the delegated tasks, and the commits.
/// </summary>
/// <remarks>
/// <b>There is no foreign key holding this together, and that is the point.</b>
/// <c>AgentTask</c> deliberately does not reference <c>Card</c>. What exists instead — everywhere,
/// already — is the identifier as a citation: task titles say "CARD-0067 - …", commit messages say
/// <c>fix(channels): CARD-0067 - …</c>, spec filenames say <c>…-card-0035-…</c>. This projection
/// makes that convention pay rent rather than adding a column and a backfill to restate it.
///
/// <para>A text correlation can be wrong, so the response says how each piece was found
/// (<see cref="CardThreadTaskDto.MatchedOn"/>, <see cref="CardThreadPlanDto.Subject"/>) instead of
/// presenting the join as if a key had guaranteed it.</para>
/// </remarks>
/// <param name="RepoRoot">The checkout the plans and commits were read from, or null.</param>
/// <param name="ReposConsulted">
/// Whether a repository could be asked at all: a checkout resolved AND git answered in it. False
/// means <see cref="Commits"/> is ABSENT rather than empty — the <c>runnerConsulted</c> distinction
/// from CARD-0035 slice 1, and the reason this endpoint cannot report "nothing was committed" about
/// a card whose worktree has been deleted. <see cref="Tasks"/> is unaffected; it comes from the
/// database and is always complete.
/// </param>
public sealed record CardThreadDto(
    CardDto Card,
    string Identifier,
    string? RepoRoot,
    bool ReposConsulted,
    DateTime GeneratedAt,
    IReadOnlyList<CardThreadPlanDto> Plans,
    IReadOnlyList<CardThreadTaskDto> Tasks,
    IReadOnlyList<CardThreadCommitDto> Commits);

/// <param name="Subject">
/// True when the plan is ABOUT this card (its filename, title or <c>Card(s):</c> header says so),
/// false when it merely cites it under "Relates to" or "Supersedes". Both are on the thread, ranked
/// — dropping the citations would hide real context, and promoting them would put five neighbouring
/// plans on every card.
/// </param>
public sealed record CardThreadPlanDto(PlanSummaryDto Plan, bool Subject);

/// <param name="MatchedOn">
/// Which field carried the citation — <c>title</c>, <c>goal</c>, or <c>both</c>. On the row because
/// a title match is a much stronger claim than a goal match: a goal often names OTHER cards as
/// context, and an operator seeing a task they did not expect should be able to tell which it was.
/// </param>
/// <param name="LatestCheck">
/// The most recent check-in reading for this task, or null if it has never been checked.
/// </param>
/// <param name="SubtreeCostUsd">
/// Rolled-up spend for this task and everything under it, computed the same way the board and the
/// attention view compute it — two answers to "what has this cost" would eventually differ.
/// </param>
public sealed record CardThreadTaskDto(
    Guid Id,
    string Title,
    AgentTaskStatus Status,
    AgentTaskKind Kind,
    /// <summary>
    /// Which agent program ran (or will run) it - ClaudeCode unless the caller chose Grok. On the
    /// row because <see cref="ModelLevel"/> alone does not name a model: the same Frontier rung is
    /// <c>fable</c> on Claude and <c>grok-4.6</c> on Grok (CARD-0084 S4), and this thread is a
    /// surface an operator reads to decide what a card cost and what to escalate.
    /// </summary>
    AgentKind AgentKind,
    AgentModelLevel ModelLevel,
    string? AgentName,
    Guid? AgentSessionId,
    DateTime CreatedAt,
    DateTime? DispatchedAt,
    DateTime? CompletedAt,
    DateTime? NextCheckAt,
    int CheckCount,
    decimal CostUsd,
    decimal SubtreeCostUsd,
    string MatchedOn,
    CardThreadCheckDto? LatestCheck,
    string? Result,
    string? ResultFilePath,
    string? DeliverablePath,
    string? DeliverableRef,
    string? FailureReason);

/// <param name="FromInterpreter">
/// True when the text is the check interpreter's READING of the bundle (CARD-0035 slice 5), false
/// when it is the tail of the deterministic digest — the degrade mode for checks that ran before
/// that landed, or whose interpreter was busy. The client says which it is showing; a reading and a
/// digest tail are not the same kind of claim.
/// </param>
public sealed record CardThreadCheckDto(string Text, bool FromInterpreter, DateTime At);

public sealed record CardThreadCommitDto(
    string Sha,
    string ShortSha,
    string Author,
    DateTime Date,
    string Subject);
