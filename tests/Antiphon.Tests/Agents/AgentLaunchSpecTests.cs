using Shouldly;
using TUnit.Core;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class AgentLaunchSpecTests
{
    [Test]
    public void Record_equality_compares_by_value()
    {
        var args = new[] { "a", "b" };
        var env = new Dictionary<string, string> { ["X"] = "1" };

        var a = new AgentLaunchSpec("n", AgentKind.Raw, "exe", args, env, "cwd", 80, 24);
        var b = new AgentLaunchSpec("n", AgentKind.Raw, "exe", args, env, "cwd", 80, 24);

        a.ShouldBe(b);
    }

    [Test]
    public void AgentLaunchOptions_default_values_are_120_by_30()
    {
        var opts = new AgentLaunchOptions();
        opts.Cols.ShouldBe(120);
        opts.Rows.ShouldBe(30);
        opts.Cwd.ShouldBeNull();
        opts.ExtraArgs.ShouldBeNull();
        opts.ExtraEnv.ShouldBeNull();
    }
}
