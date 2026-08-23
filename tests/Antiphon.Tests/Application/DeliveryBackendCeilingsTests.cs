using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0161 B1 — DeliveryBackend axis and herdr ceiling knobs.</summary>
[Category("Unit")]
public class DeliveryBackendCeilingsTests
{
    [Test]
    public void Herdr_ceilings_carry_the_configured_numbers_and_are_a_paste_path()
    {
        var settings = new DelegationSettings();
        var ceilings = settings.HerdrCeilings("test");

        ceilings.Backend.ShouldBe(DeliveryBackend.HerdrPane);
        ceilings.BriefInlineMaxBytes.ShouldBe(43_200);
        ceilings.ReplyInlineMaxChars.ShouldBe(14_400);
        ceilings.SingleWriteMaxBytes.ShouldBe(86_400);
        ceilings.IsPastePath.ShouldBeTrue();
    }

    [Test]
    public void CeilingsFor_PtyBackend_maps_onto_DeliveryBackend_value_for_value()
    {
        var settings = new DelegationSettings();

        var inbox = settings.CeilingsFor(PtyBackend.InboxConhost, "t");
        inbox.Backend.ShouldBe(DeliveryBackend.InboxConhost);
        inbox.BriefInlineMaxBytes.ShouldBe(900);
        inbox.SingleWriteMaxBytes.ShouldBe(1_024);
        inbox.IsPastePath.ShouldBeFalse();

        var modern = settings.CeilingsFor(PtyBackend.ModernConPty, "t");
        modern.Backend.ShouldBe(DeliveryBackend.ModernConPty);
        modern.BriefInlineMaxBytes.ShouldBe(43_200);
        modern.IsPastePath.ShouldBeTrue();
    }

    [Test]
    public void ForAgentKind_still_zeroes_brief_ceiling_for_non_Claude_on_a_herdr_record()
    {
        var herdr = new DelegationSettings().HerdrCeilings("t");
        var forGrok = herdr.ForAgentKind(AgentKind.Grok);

        forGrok.BriefInlineMaxBytes.ShouldBe(0);
        forGrok.SingleWriteMaxBytes.ShouldBe(herdr.SingleWriteMaxBytes);
        forGrok.Backend.ShouldBe(DeliveryBackend.HerdrPane);
    }

    [Test]
    public void Modern_IsPastePath_stays_true_after_the_retype()
    {
        var modern = new DelegationSettings().CeilingsFor(PtyBackend.ModernConPty, "t");
        modern.IsPastePath.ShouldBeTrue();
        modern.Backend.ShouldBe(DeliveryBackend.ModernConPty);
    }
}
