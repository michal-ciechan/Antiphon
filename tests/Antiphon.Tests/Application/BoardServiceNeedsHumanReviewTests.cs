using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0327 decision 7: needsHumanReview is derived at read time. Each clause that
/// clears it is pinned here so a later rewrite cannot drop one.
/// </summary>
[Category("Unit")]
public class BoardServiceNeedsHumanReviewTests
{
    [Test]
    public void Import_from_non_operator_still_Auto_in_Backlog_needs_review()
    {
        var card = ReviewCandidate();
        BoardService.NeedsHumanReview(card).ShouldBeTrue();
        BoardService.ToCardDto(card).ExternalIssue!.NeedsHumanReview.ShouldBeTrue();
        BoardService.ToCardDto(card).ExternalIssue!.Author.ShouldBe("bob");
        BoardService.ToCardDto(card).ExternalIssue!.AuthorIsOperator.ShouldBe(false);
        BoardService.ToCardDto(card).ImportanceProvenance.ShouldBe(CardImportanceProvenance.Auto);
    }

    [Test]
    public void Rated_Human_clears_needs_review()
    {
        var card = ReviewCandidate();
        card.ImportanceProvenance = CardImportanceProvenance.Human;
        BoardService.NeedsHumanReview(card).ShouldBeFalse();
        BoardService.ToCardDto(card).ExternalIssue!.NeedsHumanReview.ShouldBeFalse();
    }

    [Test]
    public void Moved_to_InProgress_clears_needs_review()
    {
        var card = ReviewCandidate();
        card.Status = CardStatus.InProgress;
        BoardService.NeedsHumanReview(card).ShouldBeFalse();
    }

    [Test]
    public void Archived_clears_needs_review()
    {
        var card = ReviewCandidate();
        card.ArchivedAt = DateTime.UtcNow;
        BoardService.NeedsHumanReview(card).ShouldBeFalse();
    }

    [Test]
    public void Operator_author_does_not_need_review()
    {
        var card = ReviewCandidate();
        card.ExternalIssueRef!.AuthorIsOperator = true;
        BoardService.NeedsHumanReview(card).ShouldBeFalse();
    }

    [Test]
    public void Unjudged_author_does_not_need_review()
    {
        var card = ReviewCandidate();
        card.ExternalIssueRef!.AuthorIsOperator = null;
        BoardService.NeedsHumanReview(card).ShouldBeFalse();
    }

    [Test]
    public void Export_origin_does_not_need_review()
    {
        var card = ReviewCandidate();
        card.ExternalIssueRef!.Origin = ExternalIssueOrigin.AntiphonExport;
        BoardService.NeedsHumanReview(card).ShouldBeFalse();
    }

    private static Card ReviewCandidate()
    {
        var card = new Card
        {
            Id = Guid.NewGuid(),
            Identifier = "CARD-0325",
            Title = "From GitHub",
            Status = CardStatus.Backlog,
            Importance = CardImportance.Normal,
            ImportanceProvenance = CardImportanceProvenance.Auto,
            CreatedAt = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc)
        };
        card.ExternalIssueRef = new ExternalIssueRef
        {
            TrackerKind = TrackerKind.GitHubIssues,
            ExternalKey = "#30",
            Url = "https://github.test/acme/app/issues/30",
            Origin = ExternalIssueOrigin.ExternalImport,
            Author = "bob",
            AuthorIsOperator = false,
            Card = card
        };
        return card;
    }
}
