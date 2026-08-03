using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CRUD + agent dispatch for inline review threads. Dispatching queues an enveloped prompt into
/// the agent's session (WhenIdle, so it never interrupts a turn) and registers a reply
/// correlation with <see cref="ReviewReplyDispatcher"/> — the agent's answering turn lands back
/// on the thread the same way channel replies land back in Telegram. Many threads can be
/// in-flight at once; each is matched by its own envelope.
/// </summary>
public sealed class ReviewThreadService
{
    private readonly AppDbContext _db;
    private readonly SessionMessageQueueService _queue;
    private readonly ReviewReplyDispatcher _replies;
    private readonly AgentFilesService _files;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReviewThreadService> _logger;

    public ReviewThreadService(
        AppDbContext db,
        SessionMessageQueueService queue,
        ReviewReplyDispatcher replies,
        AgentFilesService files,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<ReviewThreadService> logger)
    {
        _db = db;
        _queue = queue;
        _replies = replies;
        _files = files;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ReviewThreadDto>> GetThreadsAsync(Guid agentId, string? path, CancellationToken ct)
    {
        var query = _db.ReviewThreads.AsNoTracking()
            .Include(t => t.Comments)
            .Where(t => t.AgentId == agentId);
        if (!string.IsNullOrEmpty(path))
            query = query.Where(t => t.Path == path);
        var threads = await query.OrderByDescending(t => t.UpdatedAt).ToListAsync(ct);
        return threads.Select(ToDto).ToList();
    }

    public async Task<ReviewThreadDto?> CreateAsync(
        Guid agentId, CreateReviewThreadRequest request, CancellationToken ct)
    {
        var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null)
            return null;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var thread = new ReviewThread
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            Path = request.Path.Replace('\\', '/'),
            Line = request.Line,
            Snippet = request.Snippet,
            Status = ReviewThreadStatus.Open,
            CreatedAt = now,
            UpdatedAt = now,
            Comments =
            [
                new ReviewComment
                {
                    Id = Guid.NewGuid(),
                    Author = ReviewCommentAuthor.Human,
                    Body = request.Body,
                    CreatedAt = now,
                },
            ],
        };
        _db.ReviewThreads.Add(thread);
        await _db.SaveChangesAsync(ct);

        if (request.Dispatch)
            await DispatchAsync(thread.Id, ct);

        await PublishChangedAsync(thread.Id, ct);
        return await GetThreadAsync(thread.Id, ct);
    }

    public async Task<ReviewThreadDto?> AddCommentAsync(
        Guid threadId, AddReviewCommentRequest request, CancellationToken ct)
    {
        var thread = await _db.ReviewThreads.Include(t => t.Comments)
            .FirstOrDefaultAsync(t => t.Id == threadId, ct);
        if (thread is null)
            return null;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        thread.Comments.Add(new ReviewComment
        {
            Id = Guid.NewGuid(),
            ThreadId = threadId,
            Author = ReviewCommentAuthor.Human,
            Body = request.Body,
            CreatedAt = now,
        });
        thread.Status = ReviewThreadStatus.Open;
        thread.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        if (request.Dispatch)
            await DispatchAsync(threadId, ct);

        await PublishChangedAsync(threadId, ct);
        return await GetThreadAsync(threadId, ct);
    }

    public async Task<ReviewThreadDto?> ResolveAsync(Guid threadId, CancellationToken ct)
    {
        var thread = await _db.ReviewThreads.FirstOrDefaultAsync(t => t.Id == threadId, ct);
        if (thread is null)
            return null;
        thread.Status = ReviewThreadStatus.Resolved;
        thread.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
        await PublishChangedAsync(threadId, ct);
        return await GetThreadAsync(threadId, ct);
    }

    /// <summary>
    /// Queue the thread into the agent's session. The envelope's leading tag is the correlation
    /// probe; the body carries the anchor, the file's current diff (trimmed), and the thread so
    /// far, so the agent can answer without hunting for context.
    /// </summary>
    public async Task<bool> DispatchAsync(Guid threadId, CancellationToken ct)
    {
        var thread = await _db.ReviewThreads.Include(t => t.Comments)
            .FirstOrDefaultAsync(t => t.Id == threadId, ct);
        if (thread is null)
            return false;
        var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == thread.AgentId, ct);
        if (agent is null || !Guid.TryParse(agent.PersistentSessionId, out var sessionId))
            return false;
        var sessionRunning = await _db.AgentSessions
            .AnyAsync(s => s.Id == sessionId && s.Status == SessionStatus.Running, ct);
        if (!sessionRunning)
        {
            _logger.LogWarning("Review thread {ThreadId} not dispatched — agent session not running", threadId);
            return false;
        }

        var prompt = await BuildPromptAsync(thread, ct);
        _replies.Track(sessionId, new ReviewReplyDispatcher.PendingThreadReply(
            thread.Id, prompt, _timeProvider.GetUtcNow().UtcDateTime));

        await _queue.EnqueueAsync(
            sessionId, prompt, MessageSendMode.WhenIdle, ct,
            origin: QueuedMessageOrigin.Channel,
            conversationKey: $"review:{thread.Id}");

        thread.Status = ReviewThreadStatus.AwaitingAgent;
        thread.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
        await PublishChangedAsync(threadId, ct);
        return true;
    }

    private async Task<string> BuildPromptAsync(ReviewThread thread, CancellationToken ct)
    {
        var lines = new List<string>
        {
            $"{ReviewPromptFormat.EnvelopePrefix(thread.Id)} {thread.Path}:{thread.Line}",
        };
        if (!string.IsNullOrWhiteSpace(thread.Snippet))
            lines.Add($"> {thread.Snippet!.Trim()}");

        var diff = await _files.GetDiffAsync(thread.AgentId, thread.Path, since: null, ct);
        if (!string.IsNullOrWhiteSpace(diff))
        {
            var trimmed = diff.Length > 4000 ? diff[..4000] + "\n… (diff truncated)" : diff;
            lines.Add("Current diff vs HEAD:");
            lines.Add("```diff");
            lines.Add(trimmed.TrimEnd());
            lines.Add("```");
        }

        lines.Add("Thread:");
        foreach (var comment in thread.Comments.OrderBy(c => c.CreatedAt))
            lines.Add($"{comment.Author}: {comment.Body}");

        lines.Add(
            "Respond to this review comment. Your final text this turn is recorded as your reply on "
            + "this thread (and only this thread). If a code/file change is the right response, make "
            + "the change and summarize it. Reply NO_REPLY to acknowledge without commenting.");
        return string.Join("\n", lines);
    }

    private async Task<ReviewThreadDto?> GetThreadAsync(Guid threadId, CancellationToken ct)
    {
        var thread = await _db.ReviewThreads.AsNoTracking()
            .Include(t => t.Comments)
            .FirstOrDefaultAsync(t => t.Id == threadId, ct);
        return thread is null ? null : ToDto(thread);
    }

    private async Task PublishChangedAsync(Guid threadId, CancellationToken ct)
    {
        try
        {
            await _eventBus.PublishToAllAsync("ReviewThreadChanged", new { threadId }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to publish ReviewThreadChanged for {ThreadId}", threadId);
        }
    }

    internal static ReviewThreadDto ToDto(ReviewThread thread) => new(
        thread.Id,
        thread.AgentId,
        thread.Path,
        thread.Line,
        thread.Snippet,
        thread.Status.ToString(),
        thread.CreatedAt,
        thread.UpdatedAt,
        thread.Comments.OrderBy(c => c.CreatedAt)
            .Select(c => new ReviewCommentDto(c.Id, c.Author.ToString(), c.Body, c.CreatedAt))
            .ToList());
}

/// <summary>The frozen envelope grammar for review-thread prompts (correlation probe included).</summary>
public static class ReviewPromptFormat
{
    /// <summary>Leading tag of a dispatched thread prompt, e.g. <c>[Review #1a2b3c4d]</c>.</summary>
    public static string EnvelopePrefix(Guid threadId) => $"[Review #{threadId.ToString("N")[..8]}]";
}
