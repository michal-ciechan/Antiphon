using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The arithmetic behind CARD-0205's clip. The interesting property is not "it shortens things" —
/// it is that the RESULT fits, including the marker, and that it is still a valid string.
/// </summary>
[Category("Unit")]
public class ColumnTextTests
{
    [Test]
    public void Text_that_fits_is_returned_unchanged()
    {
        ColumnText.Clip("short", 10).ShouldBe("short");
        ColumnText.Clip("exactly-10", 10).ShouldBe("exactly-10");
    }

    /// <summary>
    /// The ellipsis is part of the budget, not added on top of it. Clipping to exactly the column
    /// width and then appending a marker is how a "fix" for this bug reintroduces it by one char.
    /// </summary>
    [Test]
    public void Clipping_fits_the_marker_inside_the_budget()
    {
        var clipped = ColumnText.Clip(new string('x', 5_000), 4_000);

        clipped.Length.ShouldBe(4_000);
        clipped.ShouldEndWith("…");
        clipped[..3_999].ShouldBe(new string('x', 3_999));
    }

    /// <summary>
    /// Half a surrogate pair is not a valid string, and handing one to Npgsql trades an oversize
    /// failure for an encoding failure — the same bug wearing a different message. The cut backs
    /// off a character rather than splitting the pair, so the result comes in one under budget.
    /// </summary>
    [Test]
    public void A_surrogate_pair_is_never_split()
    {
        // "aa" + 4 astral chars (2 chars each): a cut at index 5 would land inside the third pair.
        var text = "aa" + string.Concat(Enumerable.Repeat("😀", 4));

        var clipped = ColumnText.Clip(text, 6);

        clipped.Length.ShouldBe(5, "one char is given up rather than emitting a lone surrogate");
        clipped.ShouldBe("aa😀…");
    }

    [Test]
    public void A_zero_budget_yields_nothing_rather_than_throwing()
    {
        ColumnText.Clip("anything", 0).ShouldBe(string.Empty);
        ColumnText.Clip("anything", 1).ShouldBe("…");
    }

    /// <summary>An absent detail is not the same as an empty one, and must survive as absent.</summary>
    [Test]
    public void Null_survives_as_null()
    {
        ColumnText.ClipOrNull(null, 10).ShouldBeNull();
        ColumnText.ClipOrNull("", 10).ShouldBe("");
    }
}
