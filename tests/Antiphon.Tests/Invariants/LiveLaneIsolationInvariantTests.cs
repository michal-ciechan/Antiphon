using System.Reflection;
using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport.LiveClaude;
using Microsoft.Extensions.Logging.Abstractions;
using Antiphon.Tests.Agents;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;
using TUnit.Core;
using Antiphon.Tests.Scripts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using static Antiphon.Tests.Scripts.LiveLaneIsolationTests;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
[Timeout(30_000)]
[NotInParallel]
public sealed class LiveLaneIsolationInvariantTests
{
    [Test]
    public void Canary_callers_match_the_declared_profile() =>
        LiveClaudeProfileAudit.C680Callers(LiveLaneIsolationTests.Build, ClaudeDialogWatchdogCanaryTests.ProjectProfile);

    [Test]
    public void Shipped_claude_definition_projects_the_owned_production_profile()
    {
        var root = OwnedTempRoot.CreateOwned();
        try
        {
            const string model = "named-A";
            var context = new LiveClaudePortableContext(root, new string('a', 40), "fixture");
            var launch = Production(context, model);
            launch.Cwd.ShouldBe(Path.Combine(root, model, "workspace"));
            launch.Env["CLAUDE_CONFIG_DIR"].ShouldBe(Path.Combine(root, model, "profile"));
            launch.Args.ShouldContain("--dangerously-skip-permissions");
            launch.Args.ShouldContain("--session-id");
            launch.Args.ShouldContain(model);
        }
        finally
        {
            OwnedTempRoot.Release(root);
        }
    }
}
