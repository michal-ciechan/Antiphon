using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public class PhoneHomeSchedulePreviewTests
{
    [Test]
    public async Task Before_first_List_preview_remains_queueable_without_writes()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        var before = await h.DurableStateAsync();
        var preview = await h.PreviewAsync();
        preview.Target.AgentLive.ShouldBe(true, "an active bound session is unknown until the first authoritative List");
        preview.Effect.ShouldContain("WhenIdle");
        preview.Spend.ShouldBe("none");
        preview.WillStartSession.ShouldBeFalse();
        preview.Warnings.ShouldBeEmpty();
        (await h.DurableStateAsync()).ShouldBe(before);
        h.Host.Local.Calls.ShouldNotContain("start");
    }
}
