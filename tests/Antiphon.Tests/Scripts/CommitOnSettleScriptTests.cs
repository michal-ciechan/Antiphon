using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class CommitOnSettleScriptTests
{
    [Test]
    public async Task NoCommit_posts_commitOnSettle_Never()
    {
        using var server = new DelegateCreateStubApi();
        var result = await DelegateScriptRunner.RunAsync(server.BaseUrl, "-Role", "Docs", "-Goal", "x", "-NoCommit");
        result.ExitCode.ShouldBe(0, result.Output);
        server.LastBody!.RootElement.GetProperty("commitOnSettle").GetString().ShouldBe("Never");
    }

    [Test]
    public async Task Omitted_NoCommit_sends_no_commitOnSettle()
    {
        using var server = new DelegateCreateStubApi();
        var result = await DelegateScriptRunner.RunAsync(server.BaseUrl, "-Role", "Docs", "-Goal", "x");
        result.ExitCode.ShouldBe(0, result.Output);
        server.LastBody!.RootElement.TryGetProperty("commitOnSettle", out _).ShouldBeFalse();
    }

    [Test]
    public async Task Delegate_has_no_Always_or_Agent_switch()
    {
        using var server = new DelegateCreateStubApi();
        foreach (var flag in new[] { "-CommitAlways", "-CommitAgent" })
            (await DelegateScriptRunner.RunAsync(server.BaseUrl, "-Role", "Docs", "-Goal", "x", flag)).ExitCode.ShouldNotBe(0);
        server.RequestCount.ShouldBe(0);
    }
}
