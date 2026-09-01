using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0241 S4: conservative matcher. Until the headed canary writes the two chrome literals,
/// <see cref="GrokQuestionPopup.IsPresent"/> is always false (withhold-Esc is a no-op).
/// </summary>
[Category("Unit")]
public class GrokQuestionPopupTests
{
    [Test]
    public void Null_or_empty_is_absent()
    {
        GrokQuestionPopup.IsPresent(null).ShouldBeFalse();
        GrokQuestionPopup.IsPresent("").ShouldBeFalse();
    }

    [Test]
    public void Usage_overlay_is_not_the_question_popup() =>
        GrokQuestionPopup.IsPresent("Weekly limit (SuperGrok)\nc copy session ID  |  Esc close")
            .ShouldBeFalse();

    [Test]
    public void Plain_composer_is_not_the_question_popup() =>
        GrokQuestionPopup.IsPresent("> ").ShouldBeFalse();

    [Test]
    public void Unmeasured_literals_mean_IsPresent_is_always_false()
    {
        if (GrokQuestionPopup.HeadingLiteral.Length > 0
            && GrokQuestionPopup.FooterLiteral.Length > 0)
        {
            GrokQuestionPopup.IsPresent(
                    GrokQuestionPopup.HeadingLiteral + "\n" + GrokQuestionPopup.FooterLiteral)
                .ShouldBeTrue();
            return;
        }

        GrokQuestionPopup.IsPresent("Ask User\nProceed as planned (Recommended)\nEsc")
            .ShouldBeFalse("do not guess fragments from the JSONL question text");
    }
}
