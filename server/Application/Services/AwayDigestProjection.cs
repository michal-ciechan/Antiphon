using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>Read-only server projection of work that changed in an away window.</summary>
public sealed class AwayDigestProjection
{
    private readonly AppDbContext _db;
    private readonly AttentionService _attention;
    private readonly SubscriptionUsageReader _subscriptions;
    private readonly DelegationSettings _delegation;

    public AwayDigestProjection(
        AppDbContext db,
        AttentionService attention,
        SubscriptionUsageReader subscriptions,
        IOptions<DelegationSettings> delegation)
    {
        _db = db;
        _attention = attention;
        _subscriptions = subscriptions;
        _delegation = delegation.Value;
    }

    public async Task<AwayDigestDto> ComputeAsync(DateTime sinceUtc, DateTime untilUtc, CancellationToken ct)
    {
        sinceUtc = Utc(sinceUtc);
        untilUtc = Utc(untilUtc);
        var attention = await _attention.GetAsync(ct);
        var blocked = attention.Items.Where(i => i.Kind == AttentionKind.BlockedQuestion && i.TaskId is not null)
            .Select(i => new AwayDigestTaskDto(i.TaskId!.Value, Short(i.TaskId.Value), i.Title,
                FirstSentence(i.Evidence), i.SinceUtc, i.SubtreeCostUsd ?? 0m, i.SinceUtc > sinceUtc))
            .OrderByDescending(i => i.IsNew).ThenBy(i => i.At).ToList();
        // Decisions are intentionally not windowed: a card is still waiting until it is moved out,
        // and an away digest that lets an old question disappear recreates the original problem.
        var decisions = attention.Items
            .Where(i => i.Kind == AttentionKind.CardNeedsDecision && i.CardId is not null)
            .Select(i => Decision(i, sinceUtc, untilUtc))
            .OrderByDescending(i => i.IsNew).ThenBy(i => i.At).ToList();

        var roots = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.ParentTaskId == null)
            .Where(AgentTaskRoles.NotSpecialist)
            .ToListAsync(ct);
        var rootIds = roots.Select(t => t.Id).ToList();
        var family = rootIds.Count == 0 ? [] : await _db.AgentTasks.AsNoTracking()
            .Where(t => rootIds.Contains(t.RootTaskId)).ToListAsync(ct);
        var costs = AgentTaskCostWalk.Calculate(roots, family);
        var settled = roots.Where(t => t.CompletedAt is not null && t.CompletedAt > sinceUtc && t.CompletedAt <= untilUtc).ToList();
        var failed = settled.Where(t => t.Status == AgentTaskStatus.Failed)
            .OrderByDescending(t => t.CompletedAt)
            .Select(t => Task(t, FirstLine(t.FailureReason), costs.GetValueOrDefault(t.Id))).ToList();
        var finished = settled.Where(t => t.Status is AgentTaskStatus.Succeeded or AgentTaskStatus.Canceled)
            .OrderByDescending(t => t.CompletedAt)
            .Select(t => Task(t, FirstSentence(t.Result), costs.GetValueOrDefault(t.Id))).ToList();

        var cards = await _db.Cards.AsNoTracking()
            .Where(c => c.CompletedAt != null && c.CompletedAt > sinceUtc && c.CompletedAt <= untilUtc)
            .Select(c => new AwayDigestCardDto(c.Identifier, c.Title, c.CompletedAt!.Value, null, false)).ToListAsync(ct);
        finished.AddRange(cards.Select(c => new AwayDigestTaskDto(Guid.Empty, c.Identifier, c.Title, "done", c.At, 0m)));

        var reviewMoves = await _db.CardRevisions.AsNoTracking()
            .Where(r => r.Kind == CardRevisionKind.Move && r.ToStatus == CardStatus.Review && r.CreatedAt > sinceUtc && r.CreatedAt <= untilUtc)
            .Include(r => r.Card).ToListAsync(ct);
        var review = reviewMoves.Where(r => r.Card.Status == CardStatus.Review)
            .Select(r => new AwayDigestCardDto(r.Card.Identifier, r.Card.Title, r.CreatedAt)).ToList();

        var runningRoots = roots.Where(t => t.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working).ToList();
        var longest = runningRoots.OrderBy(t => t.DispatchedAt ?? t.CreatedAt).FirstOrDefault();
        var running = new AwayDigestRunningDto(runningRoots.Count, longest?.Title, longest?.DispatchedAt,
            longest is null ? null : costs.GetValueOrDefault(longest.Id));
        var settledCost = settled.Sum(t => costs.GetValueOrDefault(t.Id));
        var spend = new AwayDigestSpendDto(settledCost,
            settled.Count == 0 ? 0m : settled.Max(t => costs.GetValueOrDefault(t.Id)),
            runningRoots.Count(t => costs.GetValueOrDefault(t.Id) >= _delegation.MaxCostUsdPerRoot / 2));
        var subscription = (await _subscriptions.GetLatestAsync(ct))
            .Select(s => new AwayDigestSubscriptionDto(s.Provider, s.RemainingPercent, s.ResetsAt)).ToList();

        return new AwayDigestDto(sinceUtc, untilUtc, false, blocked, failed, finished, review, decisions, running, spend, subscription);
    }

    private static AwayDigestTaskDto Task(AgentTask t, string detail, decimal cost) =>
        new(t.Id, Short(t.Id), t.Title, detail, t.CompletedAt, cost);
    private static AwayDigestCardDto Decision(AttentionItemDto item, DateTime sinceUtc, DateTime fallbackAt)
    {
        var title = item.Title.Split(" — ", 2);
        return new AwayDigestCardDto(
            title[0],
            title.Length > 1 ? title[1] : item.Title,
            item.SinceUtc ?? fallbackAt,
            FirstSentence(item.Evidence),
            item.SinceUtc > sinceUtc);
    }
    private static string Short(Guid id) => id.ToString("N")[..8];
    internal static string FirstSentence(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "No report provided.";
        var flat = value.ReplaceLineEndings(" ").Trim();
        var end = flat.IndexOfAny(['.', '!', '?']);
        return end >= 0 ? flat[..(end + 1)] : flat;
    }
    private static string FirstLine(string? value) => string.IsNullOrWhiteSpace(value)
        ? "No failure reason provided." : value.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "No failure reason provided.";
    private static DateTime Utc(DateTime date) => date.Kind == DateTimeKind.Utc ? date : date.ToUniversalTime();
}
