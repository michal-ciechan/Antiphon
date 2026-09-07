using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class CardFilePolicyTests
{
    [Test]
    public void Defaults_are_board_off_card_Inherit_notes_empty_repository_Unknown()
    {
        new Board().SyncCardFiles.ShouldBeFalse();
        new Board().CardFilesDirectorySlug.ShouldBeNull();
        new Board().CardFilesRepositoryPath.ShouldBeNull();
        new Card().CardFileVisibility.ShouldBe(CardFileVisibility.Inherit);
        new Card().PrivateNotes.ShouldBeEmpty();
        new Project().RepositoryVisibility.ShouldBe(RepositoryVisibility.Unknown);
        new CardRevision().PrivateNotes.ShouldBeNull();
        new CardRevision().CardFileVisibility.ShouldBeNull();
        new CardFileSyncSettings().Enabled.ShouldBeTrue();
        new CardFileSyncSettings().AutoCommit.ShouldBeFalse();
        new CardFileSyncSettings().IntervalSeconds.ShouldBe(60);
    }

    [Test]
    public void Board_repository_card_policy_matrix()
    {
        var permitted = 0;
        var count = 0;
        foreach (var board in new[] { false, true })
        foreach (var repo in Enum.GetValues<RepositoryVisibility>())
        foreach (var card in Enum.GetValues<CardFileVisibility>())
        {
            var expected = !board ? "board_not_opted_in" : repo == RepositoryVisibility.Unknown
                ? "repository_visibility_unknown" : card == CardFileVisibility.Private ? "card_private" : null;
            var reason = new CardFilePolicyService().GetReason(true, false, false, board, repo, card);
            reason.ShouldBe(expected, $"board={board}; repo={repo}; card={card}");
            if (reason is null) permitted++;
            count++;
        }
        count.ShouldBe(18);
        permitted.ShouldBe(4);
    }

    [Test]
    public void Warning_dedup_is_per_board_target_reason()
    {
        var gate = new CardTaskFileSyncGate();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        gate.NoteSkipReason(a, "C:\\synthetic", "board_not_opted_in").ShouldBeTrue();
        gate.NoteSkipReason(b, "C:\\synthetic", "board_not_opted_in").ShouldBeTrue();
        gate.NoteSkipReason(a, "C:\\synthetic", "board_not_opted_in").ShouldBeFalse();
        gate.NoteSkipReason(b, "C:\\synthetic", "board_not_opted_in").ShouldBeFalse();
        gate.NoteSkipReason(a, "C:\\synthetic", null).ShouldBeFalse();
        gate.NoteSkipReason(a, "C:\\synthetic", "board_not_opted_in").ShouldBeTrue();
        gate.NoteSkipReason(a, "C:\\synthetic-new", "board_not_opted_in").ShouldBeTrue();
    }

    [Test]
    public void Public_projection_has_no_entity_or_private_note_input()
    {
        CardFilePublicProjection.Select.ToString().ShouldNotContain("PrivateNotes");
        var projection = typeof(CardFilePublicCard);
        foreach (var property in projection.GetProperties())
        {
            property.Name.ShouldNotContain("PrivateNotes");
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            (type.IsEnum || type == typeof(string) || type == typeof(Guid) || type == typeof(DateTime)
                || type == typeof(int) || type == typeof(bool)).ShouldBeTrue(property.Name);
        }
        typeof(CardTaskFileRenderer).GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .SelectMany(m => m.GetParameters()).ShouldNotContain(p => p.ParameterType == typeof(Card));
        projection.GetProperty("Position").ShouldNotBeNull();
    }
}
