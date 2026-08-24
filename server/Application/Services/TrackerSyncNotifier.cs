using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0171: announces what a bidirectional tracker sync changed, to the chat channel the board's
/// <c>tracker.notify_channel</c> names.
///
/// Deliberately NOT the alert path (<see cref="ChannelAlertRouter"/>): alert sinks are selected by
/// severity alone, so making a family group chat hear about card moves would also make it hear
/// every stalled task and quota warning, in the ops digest voice. This is a targeted send via
/// <see cref="ChatChannelService.SendAsync"/> instead.
///
/// Runs AFTER the sync has committed, so nothing here may throw at the caller: every failure
/// becomes a <see cref="TrackerSyncNotificationResult"/> with a reason, logged at Warning.
/// </summary>
public sealed class TrackerSyncNotifier
{
    /// <summary>The <c>tracker:</c> block key naming the channel — a GUID or an exact title.</summary>
    public const string NotifyChannelOptionKey = "notify_channel";

    private readonly AppDbContext _db;
    private readonly ChatChannelService _channels;
    private readonly ILogger<TrackerSyncNotifier> _logger;

    public TrackerSyncNotifier(
        AppDbContext db,
        ChatChannelService channels,
        ILogger<TrackerSyncNotifier> logger)
    {
        _db = db;
        _channels = channels;
        _logger = logger;
    }

    /// <summary>
    /// One message per resolved channel per run: boards that changed are grouped by their resolved
    /// channel, so two boards pointing at the same chat produce one message with two blocks.
    /// Boards with no changes produce no entry at all.
    /// </summary>
    public async Task<IReadOnlyList<TrackerSyncNotificationResult>> NotifyAsync(
        TrackerSyncRunResult run, CancellationToken ct)
    {
        var changed = run.Boards.Where(b => b.Changes.Count > 0).ToList();
        if (changed.Count == 0)
            return [];

        var results = new List<TrackerSyncNotificationResult>();
        // Insertion-ordered so a multi-board run's messages go out in board order.
        var groups = new List<ChannelGroup>();

        foreach (var boardResult in changed)
        {
            var board = await _db.Boards
                .Include(b => b.WorkflowDefinitions)
                .FirstOrDefaultAsync(b => b.Id == boardResult.BoardId, ct);
            IssueTrackerConfig? config = null;
            if (board is not null)
                IssueTrackerConfigParser.TryParse(board, out config, out _);

            var target = config?.Options is { } options
                         && options.TryGetValue(NotifyChannelOptionKey, out var raw)
                         && !string.IsNullOrWhiteSpace(raw)
                ? raw.Trim()
                : null;

            if (target is null)
            {
                // The per-board config is the consent; the trigger flag alone is not enough.
                results.Add(new TrackerSyncNotificationResult(
                    boardResult.BoardId, Sent: false, ChannelId: null, Reason: "notify_channel_unset"));
                continue;
            }

            var (channel, reason) = await ResolveChannelAsync(target, ct);
            if (channel is null)
            {
                _logger.LogWarning(
                    "Tracker sync notification for board {BoardId} not sent: {Reason} (notify_channel '{Target}')",
                    boardResult.BoardId, reason, target);
                results.Add(new TrackerSyncNotificationResult(
                    boardResult.BoardId, Sent: false, ChannelId: null, Reason: reason));
                continue;
            }

            var existing = groups.FirstOrDefault(g => g.Channel.Id == channel.Id);
            if (existing is null)
                groups.Add(new ChannelGroup(channel, [(boardResult, config)]));
            else
                existing.Boards.Add((boardResult, config));
        }

        foreach (var (channel, boards) in groups)
        {
            var text = TrackerSyncSummaryFormatter.Format(boards);
            if (text is null)
                continue; // unreachable: every board in a group has changes.

            string? failure = null;
            try
            {
                await _channels.SendAsync(channel.Id, text, ct);
            }
            catch (ConflictException ex) when (ex.Code == "channel_disabled")
            {
                failure = "channel_disabled";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failure = "send_failed";
                _logger.LogWarning(
                    ex,
                    "Tracker sync notification to channel {ChannelId} failed ({Length} chars)",
                    channel.Id, text.Length);
            }

            if (failure == "channel_disabled")
            {
                _logger.LogWarning(
                    "Tracker sync notification to channel {ChannelId} not sent: channel is disabled",
                    channel.Id);
            }

            foreach (var (board, _) in boards)
            {
                results.Add(new TrackerSyncNotificationResult(
                    board.BoardId,
                    Sent: failure is null,
                    ChannelId: channel.Id,
                    Reason: failure));
            }
        }

        return results;
    }

    private sealed record ChannelGroup(
        ChatChannel Channel,
        List<(TrackerSyncBoardResult Board, IssueTrackerConfig? Config)> Boards);

    /// <summary>
    /// A GUID (recommended — titles are editable) or an exact, case-insensitive title that is
    /// unique in the catalog. Titles are matched in memory: the catalog is tiny and ordinal
    /// ignore-case is not what the database collation would give us.
    /// </summary>
    private async Task<(ChatChannel? Channel, string? Reason)> ResolveChannelAsync(
        string target, CancellationToken ct)
    {
        if (Guid.TryParse(target, out var id))
        {
            var byId = await _db.ChatChannels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
            return byId is null ? (null, "channel_not_found") : (byId, null);
        }

        var all = await _db.ChatChannels.AsNoTracking().ToListAsync(ct);
        var matches = all
            .Where(c => string.Equals(c.Title, target, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            0 => (null, "channel_not_found"),
            1 => (matches[0], null),
            _ => (null, "channel_ambiguous")
        };
    }
}
