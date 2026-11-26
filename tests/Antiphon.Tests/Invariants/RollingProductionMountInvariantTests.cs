using System.Diagnostics;
using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;
using Antiphon.Tests.Scripts;
using static Antiphon.Tests.Scripts.RollingProductionMountTests;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class RollingProductionMountInvariantTests
{
    [Test]
    [ParallelLimiter<ScriptModelSpawnLimit>]
    public void Shipped_current_compose_includes_the_token_mount_absent_from_the_previous_model()
    {
        var root = OwnedTempRoot.CreateOwned();
        try
        {
            var models = new C1008ComposeSeed().CloneInto(root);
            foreach (var project in new[] { "antiphon-runner", "antiphon-runner-temp" })
            {
                var current = models[project]!["services"]!["session-runner"]!["volumes"]!.AsArray();
                var previous = models[project + "@" + C1008HostFixture.PreviousSha12]!["services"]!["session-runner"]!["volumes"]!.AsArray();
                current.ShouldContain(IsTokenMount, project);
                previous.ShouldNotContain(IsTokenMount, project);
            }
        }
        finally
        {
            OwnedTempRoot.Release(root);
        }
    }

    private static bool IsTokenMount(JsonNode? mount) =>
        mount?["target"]?.GetValue<string>() == "/run/antiphon/github-token";

    [Test]
    public void Shipped_v218_compose_has_eleven_bind_entries()
    {
        var accept = C1008HostFixture.LoadComposeV218(DelegateScriptRunner.RepoRoot);
        SessionVolumeBinds(accept).ShouldBe(11);
    }
}
