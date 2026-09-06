using System.Linq.Expressions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>Explicit immutable scalar whitelist; neither notes nor entity navigations can reach rendering.</summary>
internal sealed record CardFilePublicCard(
    Guid Id,
    string Identifier,
    string Title,
    string? Alias,
    string Description,
    CardStatus Status,
    CardImportance Importance,
    CardImportanceProvenance ImportanceProvenance,
    CardUrgency Urgency,
    DateTime? DueAt,
    int? Position,
    string LabelsJson,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime? ArchivedAt,
    string? ArchivedBy,
    string? ArchivedReason,
    string? TerminalReason,
    TrackerKind? ExternalTracker,
    string? ExternalKey,
    string? ExternalUrl,
    string? ExternalAuthor,
    bool NeedsHumanReview);

internal static class CardFilePublicProjection
{
    internal static Expression<Func<Card, CardFilePublicCard>> Select => c => new(
        c.Id,
        c.Identifier,
        c.Title,
        c.Alias,
        c.Description,
        c.Status,
        c.Importance,
        c.ImportanceProvenance,
        c.Urgency,
        c.DueAt,
        c.Position,
        c.LabelsJson,
        c.CreatedAt,
        c.StartedAt,
        c.CompletedAt,
        c.ArchivedAt,
        c.ArchivedBy,
        c.ArchivedReason,
        c.TerminalReason,
        c.ExternalIssueRef == null ? null : c.ExternalIssueRef.TrackerKind,
        c.ExternalIssueRef == null ? null : c.ExternalIssueRef.ExternalKey,
        c.ExternalIssueRef == null ? null : c.ExternalIssueRef.Url,
        c.ExternalIssueRef == null ? null : c.ExternalIssueRef.Author,
        c.ExternalIssueRef != null && c.ExternalIssueRef.Origin == ExternalIssueOrigin.ExternalImport
            && c.ExternalIssueRef.AuthorIsOperator == false && c.ImportanceProvenance == CardImportanceProvenance.Auto
            && c.Status == CardStatus.Backlog && c.ArchivedAt == null);
}
