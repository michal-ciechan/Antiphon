using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public sealed class HerdrDisposalRedactorTests
{
    private const string Mask = "[redacted]";

    [Test]
    [Arguments("xC:secret-home")][Arguments("XC:secret-home")][Arguments("xc:secret-home")]
    [Arguments("0C:secret-home")][Arguments("1C:secret-home")][Arguments("2C:secret-home")]
    [Arguments("3C:secret-home")][Arguments("4C:secret-home")][Arguments("5C:secret-home")]
    [Arguments("6C:secret-home")][Arguments("7C:secret-home")][Arguments("8C:secret-home")]
    [Arguments("9C:secret-home")][Arguments("1c:secret-home")]
    [Arguments("repoC:secret")][Arguments("workdirC:secret")][Arguments("123C:secret")]
    [Arguments("xC::x")]
    [Arguments("agent:claude")][Arguments("Review:notes")][Arguments("dir:name")]
    public void Glued_drive_followed_by_a_non_space_non_digit_is_masked(string value)
    {
        var projected = Project(value);
        projected.WorkspaceLabel.ShouldBe(Mask);
        projected.TabLabel.ShouldBe(Mask);
        projected.PaneLabel.ShouldBe(Mask);
        projected.BackendVersion.ShouldBe(Mask);
        projected.Claims[0].Source.ShouldBe(Mask);
        projected.Claims[0].Origin.ShouldBe(Mask);
        projected.Claims[0].AgentKind.ShouldBe(Mask);
        projected.PaneId.ShouldBe("w1:p2");
        projected.Eligible.ShouldBeTrue();
    }

    [Test]
    public void Letter_or_digit_run_glued_to_a_drive_prefix_is_masked()
    {
        foreach (var value in new[]
        {
            new string('x', 100_000) + "C:secret",
            new string('1', 100_000) + "C:secret"
        })
        {
            var projected = Project(value);
            projected.WorkspaceLabel.ShouldBe(Mask);
            projected.TabLabel.ShouldBe(Mask);
            projected.PaneLabel.ShouldBe(Mask);
            projected.BackendVersion.ShouldBe(Mask);
            projected.Claims[0].Source.ShouldBe(Mask);
            projected.Claims[0].Origin.ShouldBe(Mask);
            projected.Claims[0].AgentKind.ShouldBe(Mask);
        }
    }

    [Test]
    [Arguments("Review: notes")][Arguments("fix: retry")][Arguments("build:1")]
    [Arguments("CARD-0866 12:30")]
    [Arguments("xC: secret")][Arguments("1C: secret")]
    [Arguments("xC:1")][Arguments("1C:9")]
    [Arguments("xC:")][Arguments("1C:")]
    [Arguments("Å:notes")][Arguments("\u0421:secret")][Arguments("\u0391:secret")][Arguments("\uFF23:secret")]
    public void Glued_or_non_ascii_colon_text_stays_visible(string value)
    {
        var projected = Project(value);
        projected.WorkspaceLabel.ShouldBe(value);
        projected.TabLabel.ShouldBe(value);
        projected.PaneLabel.ShouldBe(value);
        projected.BackendVersion.ShouldBe(value);
        projected.Claims[0].Source.ShouldBe(value);
        projected.Claims[0].Origin.ShouldBe(value);
        projected.Claims[0].AgentKind.ShouldBe(value);
        projected.PaneId.ShouldBe("w1:p2");
        projected.Eligible.ShouldBeTrue();
    }

    private static HerdrPaneDisposalPreview Project(string value)
    {
        var session = Guid.Parse("09190000-0000-4000-8000-000000000919");
        var preview = new HerdrPaneDisposalPreview(
            Guid.Parse("09190000-0000-4000-8000-000000000001"),
            DateTimeOffset.UnixEpoch,
            "w1:p2",
            session,
            null,
            "w1",
            "w1:t2",
            "term",
            value,
            value,
            value,
            value,
            20,
            4242,
            null,
            [new HerdrPaneDisposalClaim(session, value, value, false, value)],
            false,
            true,
            true,
            true,
            []);
        return HerdrDisposalRedactor.Project(preview);
    }
}
