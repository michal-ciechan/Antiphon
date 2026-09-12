using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DelegateScriptRepairSourceTests
{
    [Test]
    public async Task C499_V01_RepairSourcePostsTheFullOwnerGuid()
    {
        var owner = Guid.NewGuid();
        using var server = new DelegateCreateStubApi();
        var run = await DelegateScriptRunner.RunAsync(server.BaseUrl,
            "-Role", "Code", "-Worktree", "-RepairSource", owner.ToString("D"), "-Goal", "repair the owner branch");
        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull().RootElement;
        body.GetProperty("repairSourceTaskId").GetString().ShouldBe(owner.ToString("D"));
        body.TryGetProperty("mergeTargetRef", out _).ShouldBeFalse();
        body.GetProperty("workspace").GetString().ShouldBe("Worktree");
    }

    [Test]
    public async Task C499_V01b_ServerRefusalPassesThrough()
    {
        var owner = Guid.NewGuid();
        using var server = new DelegateCreateStubApi("""{"code":"repair_source_mode","title":"One or more validation errors occurred."}""", 422);
        var run = await DelegateScriptRunner.RunAsync(server.BaseUrl,
            "-Role", "Code", "-Worktree", "-RepairSource", owner.ToString("D"), "-Goal", "repair");
        run.ExitCode.ShouldBe(1);
        run.Output.ShouldContain("repair_source_mode");
    }
}
