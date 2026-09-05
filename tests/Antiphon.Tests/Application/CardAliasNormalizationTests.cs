using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0350 S2: alias validation and read projection. No database — the service
/// rejects at this helper, and <see cref="BoardService.ToCardDto"/> surfaces the stored value.
/// </summary>
[Category("Unit")]
public class CardAliasNormalizationTests
{
    [Test]
    public void Blank_input_normalizes_to_null()
    {
        CardService.TryNormalizeAlias(null, out var missing, out var missingError).ShouldBeTrue();
        missing.ShouldBeNull();
        missingError.ShouldBeNull();

        CardService.TryNormalizeAlias("   ", out var blank, out var blankError).ShouldBeTrue();
        blank.ShouldBeNull();
        blankError.ShouldBeNull();
    }

    [Test]
    public void Trims_and_collapses_internal_whitespace()
    {
        CardService.TryNormalizeAlias("  Status   Stuck  ", out var normalized, out var error)
            .ShouldBeTrue();
        normalized.ShouldBe("Status Stuck");
        error.ShouldBeNull();
    }

    [Test]
    public void Rejects_a_newline()
    {
        CardService.TryNormalizeAlias("Status\nStuck", out var normalized, out var error)
            .ShouldBeFalse();
        normalized.ShouldBeNull();
        error.ShouldBe("Alias must be a single line.");
    }

    [Test]
    public void Rejects_a_sixth_word()
    {
        CardService.TryNormalizeAlias("one two three four five six", out var normalized, out var error)
            .ShouldBeFalse();
        normalized.ShouldBeNull();
        error.ShouldBe("Alias must be at most 5 words; got 6.");
    }

    [Test]
    public void Accepts_five_words_at_the_character_cap()
    {
        // 12+1+12+1+12+1+12+1+12 = 64
        var value = string.Join(' ', Enumerable.Repeat("abcdefghijkl", 5));
        value.Length.ShouldBe(CardService.MaxAliasLength);
        CardService.TryNormalizeAlias(value, out var normalized, out var error).ShouldBeTrue();
        normalized.ShouldBe(value);
        error.ShouldBeNull();
    }

    [Test]
    public void Rejects_an_over_length_value_after_collapse()
    {
        var value = new string('a', CardService.MaxAliasLength + 1);
        CardService.TryNormalizeAlias(value, out var normalized, out var error).ShouldBeFalse();
        normalized.ShouldBeNull();
        error.ShouldContain("64");
        error.ShouldContain("65");
    }

    [Test]
    public void Board_projection_exposes_the_stored_alias()
    {
        var card = new Card
        {
            Id = Guid.NewGuid(),
            Identifier = "CARD-0350",
            Title = "bounded check headers and optional card aliases",
            Alias = "Check header alias",
            Description = "body",
            Status = CardStatus.Backlog,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        BoardService.ToCardDto(card).Alias.ShouldBe("Check header alias");
        card.Alias = null;
        BoardService.ToCardDto(card).Alias.ShouldBeNull();
    }
}
