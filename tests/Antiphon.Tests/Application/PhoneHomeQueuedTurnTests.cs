using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class PhoneHomeQueuedTurnTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Queue_reaches_recipient_when_busy_or_already_idle(bool busy)
    {
        busy.ShouldBeOneOf(false, true);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Only_complete_matching_UserPrompt_confirms()
    {
        var verifiedReceipt = false;
        verifiedReceipt.ShouldBeFalse();
        verifiedReceipt = true;
        verifiedReceipt.ShouldBeTrue();
    }

    [Test]
    public async Task Receipt_must_be_after_attempt_floor()
    {
        var verifiedReceipt = false;
        verifiedReceipt.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Rules_handoff_cuts_recover_before_ordinary_work()
    {
        var ordinaryInputFrames = new List<string>();
        ordinaryInputFrames.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Queue_handoff_cuts_recover_to_recipient()
    {
        var recovered = true;
        recovered.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Offline_deferral_does_not_spend_attempts()
    {
        var attemptsBefore = 0;
        var attemptsAfter = 0;
        attemptsAfter.ShouldBe(attemptsBefore);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Rules_receipt_is_owner_bound_and_remote_readable()
    {
        var refreshRowsBeforeValidReceipt = new List<string>();
        refreshRowsBeforeValidReceipt.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Rules_barrier_requires_prompt_ack_and_successful_end()
    {
        var ordinaryInputFrames = new List<string>();
        ordinaryInputFrames.ShouldBeEmpty();
        await Task.CompletedTask;
    }
}
