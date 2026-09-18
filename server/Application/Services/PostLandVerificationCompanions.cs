using System.Globalization;
using System.Text;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0552 D-2. The one writer of the CARD-0478 post-land verification companion card.
/// </summary>
/// <remarks>
/// It depends on <see cref="AppDbContext"/> and a clock only, so the land terminal can call it
/// from inside its own transaction under the owner's row lock without dragging the card-file
/// lease or an early <c>CardChanged</c> publish into a locked write. The caller owns
/// <c>SaveChanges</c>, the transaction and the publish.
/// </remarks>
public sealed class PostLandVerificationCompanions
{
    public const string Label = "post-land-verification";
    public const string KeyPrefix = "post-land-verification:";

    /// <summary>The land-side editor name on every revision this writer appends.</summary>
    public const string Editor = "land";

    private const int HandoffClip = 400;

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<PostLandVerificationCompanions> _logger;

    public PostLandVerificationCompanions(
        AppDbContext db, TimeProvider clock, ILogger<PostLandVerificationCompanions> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    /// <param name="CardId">The companion card this operation is now linked to.</param>
    /// <param name="Created">A new card was made by this call.</param>
    /// <param name="Linked">This call set <c>op.VerificationCardId</c>.</param>
    public sealed record Result(Guid CardId, string Identifier, bool Created, bool Linked);

    /// <summary>The facts a new companion's description is born with.</summary>
    public sealed record CompanionContext(
        Guid CompanionCardId,
        Guid? ReviewTaskId,
        string? ReviewHandoff,
        string? CodeHandoff,
        Guid? ProjectId,
        string? PlanPath,
        string? SupersedesIdentifier = null,
        Guid? SupersedesCardId = null);

    public static string StableKey(Guid ownerTaskId) => KeyPrefix + ownerTaskId.ToString("D");

    public static string Title(string originalIdentifier) => $"Post-land verification: {originalIdentifier}";

    /// <summary>
    /// Create-or-link the companion for a CONFIRMED publication. Requires an open transaction and
    /// the owner's row lock; the land terminal holds both.
    /// </summary>
    public async Task<Result> EnsureAsync(AgentTask owner, AgentTaskLanding op, DateTime now, CancellationToken ct)
    {
        if (owner.CardId is not Guid originalId)
            throw new InvalidOperationException("post_land_companion_requires_card");

        // Cleanup retries, LandingCleanup terminals and repeated endpoint calls land here and
        // write nothing at all.
        if (op.VerificationCardId is Guid alreadyLinked)
        {
            var identifier = await _db.Cards.AsNoTracking()
                .Where(c => c.Id == alreadyLinked)
                .Select(c => c.Identifier)
                .SingleAsync(ct);
            return new Result(alreadyLinked, identifier, false, false);
        }

        var original = await _db.Cards.SingleAsync(c => c.Id == originalId, ct);

        // Step 1: a companion another confirmed operation of the SAME owner already linked.
        var linkedIds = await _db.AgentTaskLandings.AsNoTracking()
            .Where(o => o.TaskId == owner.Id && o.VerificationCardId != null)
            .Select(o => o.VerificationCardId!.Value)
            .Distinct()
            .ToListAsync(ct);
        var linkedCards = linkedIds.Count == 0
            ? []
            : await _db.Cards.Where(c => linkedIds.Contains(c.Id)).ToListAsync(ct);

        var companion = Newest(linkedCards.Where(IsOpen));
        Card? superseded = companion is null ? Newest(linkedCards) : null;

        // Step 2: a companion a caller created under the pre-CARD-0552 recipe, discovered by the
        // stable key, on the ORIGINAL's board only.
        var key = StableKey(owner.Id);
        if (companion is null)
        {
            var keyed = await _db.Cards
                .Where(c => c.BoardId == original.BoardId
                    && c.ArchivedAt == null
                    && c.Status != CardStatus.Done
                    && c.Status != CardStatus.Canceled
                    && c.Description.Contains(key))
                .ToListAsync(ct);
            companion = Newest(keyed.Where(IsOpen));
            if (companion is not null) superseded = null;
        }

        var created = false;
        if (companion is null)
        {
            companion = await CreateAsync(original, owner, op, superseded, ct);
            created = true;
        }
        else
        {
            // Preserve existing: a caller's notes are appended to, never replaced.
            CardRevisionLog.AppendContentEdit(companion, ConfirmationReason(op), Editor, now);
            companion.Description = Clip(companion.Description + OperationFacts(op));
            companion.UpdatedAt = now;
            companion.ConcurrencyToken = Guid.NewGuid();
        }

        op.VerificationCardId = companion.Id;

        if (created)
            CardRevisionLog.AppendContentEdit(companion, ConfirmationReason(op), Editor, now);

        // The reverse link on the original: one appended line and nothing else. Status,
        // CompletedAt and TerminalReason are never written here (D-6).
        CardRevisionLog.AppendContentEdit(original, "Post-land verification companion", Editor, now);
        original.Description = Clip(original.Description
            + $"\nPost-land verification: {companion.Identifier} ({companion.Id:D})");
        original.UpdatedAt = now;
        original.ConcurrencyToken = Guid.NewGuid();

        _logger.LogInformation(
            "Confirmed publication {Operation} recorded post-land verification companion {Identifier} ({Card})",
            op.Id, companion.Identifier, companion.Id);

        return new Result(companion.Id, companion.Identifier, created, true);
    }

    private static string ConfirmationReason(AgentTaskLanding op) => $"Confirmed publication {op.Id:N}";

    private static bool IsOpen(Card card) =>
        card.ArchivedAt is null && card.Status is not (CardStatus.Done or CardStatus.Canceled);

    private static Card? Newest(IEnumerable<Card> cards) => cards
        .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).FirstOrDefault();

    private async Task<Card> CreateAsync(
        Card original, AgentTask owner, AgentTaskLanding op, Card? superseded, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var columns = await _db.BoardColumns.AsNoTracking()
            .Where(c => c.BoardId == original.BoardId)
            .OrderBy(c => c.ColumnOrder)
            .ToListAsync(ct);
        var column = columns.FirstOrDefault(c => c.CardStatus == CardStatus.Backlog)
            ?? columns.FirstOrDefault()
            ?? throw new InvalidOperationException("post_land_companion_board_has_no_column");

        var allocator = await CardIdentifierAllocator.ForBoardAsync(_db, original.BoardId, ct);
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = original.BoardId,
            BoardColumnId = column.Id,
            Identifier = allocator.Next(),
            Title = Title(original.Identifier),
            Description = string.Empty,
            PrivateNotes = string.Empty,
            Importance = CardImportance.Normal,
            ImportanceProvenance = CardImportanceProvenance.Auto,
            Urgency = CardUrgency.Normal,
            LabelsJson = BoardService.SerializeLabels([Label]),
            Status = column.CardStatus,
            CreatedAt = now,
            UpdatedAt = now,
        };
        card.Description = Describe(original, owner, op, await ContextAsync(original, owner, card.Id, superseded, ct));
        _db.Cards.Add(card);
        return card;
    }

    private async Task<CompanionContext> ContextAsync(
        Card original, AgentTask owner, Guid companionCardId, Card? superseded, CancellationToken ct)
    {
        var review = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.CardId == original.Id
                && t.Role == AgentTaskRole.Review
                && t.Status == AgentTaskStatus.Succeeded
                && t.NextStage == PipelineHandoffKind.Land)
            .OrderByDescending(t => t.CompletedAt).ThenByDescending(t => t.Id)
            .Select(t => new { t.Id, t.NextHandoff })
            .FirstOrDefaultAsync(ct);

        var plans = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.CardId == original.Id
                && t.Role == AgentTaskRole.Plan
                && t.Status == AgentTaskStatus.Succeeded
                && t.DeliverablePath != null)
            .OrderByDescending(t => t.CompletedAt).ThenByDescending(t => t.Id)
            .Select(t => t.DeliverablePath)
            .ToListAsync(ct);
        var plan = plans.FirstOrDefault(AgentTaskPipelineStatusService.IsVerifiedPlanDeliverable);

        return new CompanionContext(
            companionCardId,
            review?.Id,
            review?.NextHandoff,
            owner.NextHandoff,
            owner.ProjectId,
            plan,
            superseded?.Identifier,
            superseded?.Id);
    }

    /// <summary>
    /// The description a NEW companion is born with: ASCII, one fact per line, capped at
    /// <see cref="CardService.MaxDescriptionLength"/>.
    /// </summary>
    public static string Describe(Card original, AgentTask owner, AgentTaskLanding op, CompanionContext context)
    {
        var text = new StringBuilder();
        text.Append(StableKey(owner.Id)).Append('\n');
        if (context.SupersedesIdentifier is { } supersedes && context.SupersedesCardId is Guid supersededId)
            text.Append("Supersedes: ").Append(supersedes).Append(" (").Append(supersededId.ToString("D")).Append(")\n");
        text.Append('\n');
        text.Append("Original card: ").Append(original.Identifier).Append(" (").Append(original.Id.ToString("D")).Append(")\n");
        text.Append("Landing owner (Code task): ").Append(owner.Id.ToString("D")).Append('\n');
        text.Append("Review task: ").Append(context.ReviewTaskId is Guid r ? r.ToString("D") : "none recorded").Append('\n');
        text.Append("Commissioning project: ").Append(context.ProjectId is Guid p ? p.ToString("D") : "null").Append('\n');
        text.Append("Plan: ").Append(Field(context.PlanPath) ?? "not recorded").Append('\n');
        text.Append("Reviewed C: ").Append(op.OriginalSourceSha).Append('\n');
        text.Append(OperationLines(op));
        text.Append("Code handoff: ").Append(Field(context.CodeHandoff) ?? "none recorded").Append('\n');
        text.Append("Review handoff: ").Append(Field(context.ReviewHandoff) ?? "none recorded").Append('\n');
        text.Append('\n');
        text.Append("Pending PC inventory: every PC-n and named variant in the plan's Verification design; none\n");
        text.Append("executed at L. Record the executed counts, evidence root and restoration verdict here at close.\n");
        text.Append("Dispatch (explicit, WIP 1): delegate.ps1 -Role Mutation -Card ")
            .Append(context.CompanionCardId.ToString("D"))
            .Append(" -Worktree -SourceLanding ")
            .Append(op.Id.ToString("D"));
        return Clip(Ascii(text.ToString()));
    }

    /// <summary>The block appended to an EXISTING companion when a further operation links to it.</summary>
    private static string OperationFacts(AgentTaskLanding op) => "\n\n" + OperationLines(op).TrimEnd('\n');

    private static string OperationLines(AgentTaskLanding op) =>
        $"O: {op.Id:D}  L={op.VerifiedSourceSha}  R={op.ObservedRemoteTargetSha}\n"
        + $"Publication: {op.Publication} confirmed at "
        + $"{Stored(op.RemoteConfirmedAt)?.ToString("O", CultureInfo.InvariantCulture)}; cleanup={op.Cleanup}\n";

    /// <summary>
    /// The timestamp as the DATABASE holds it. Postgres stores microseconds, so the in-memory
    /// value this writer sees inside the land transaction carries up to 9 sub-microsecond ticks
    /// the row will not keep. Printing the untruncated value would put a timestamp on the card
    /// that appears nowhere in the database and matches nothing a later reader compares it to.
    /// </summary>
    private static DateTime? Stored(DateTime? value) => value is null
        ? null
        : new DateTime(value.Value.Ticks - value.Value.Ticks % 10, value.Value.Kind);

    /// <summary>
    /// Handoff and plan text arrives from reports; it is clipped here so no caller-supplied
    /// field can push the description past the card cap.
    /// </summary>
    private static string? Field(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var single = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return single.Length <= HandoffClip ? single : single[..HandoffClip];
    }

    private static string Clip(string value) =>
        value.Length <= CardService.MaxDescriptionLength ? value : value[..CardService.MaxDescriptionLength];

    /// <summary>
    /// Card descriptions in this repository are ASCII (the bundles, the card files and every
    /// contract test assume it). Accented letters fold to their base letter; dashes and quotes
    /// fold to their ASCII shapes; anything left above 127 is dropped rather than emitted.
    /// </summary>
    internal static string Ascii(string value)
    {
        if (value.All(c => c < 128)) return value;
        var folded = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            folded.Append(ch switch
            {
                '—' or '–' or '−' => "-",
                '‘' or '’' or '‛' => "'",
                '“' or '”' => "\"",
                '…' => "...",
                ' ' => " ",
                _ => ch.ToString(),
            });
        }

        var decomposed = folded.ToString().Normalize(NormalizationForm.FormD);
        var ascii = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (ch < 128) ascii.Append(ch);
        }

        return ascii.ToString();
    }
}
