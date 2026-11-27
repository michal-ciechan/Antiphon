using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-0780. Drive the verifier's ConvertFrom-Json reading and the live bridge without contacting server2. Unit 66 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the verify-docker-stack command in process (C590Bridge on
// C590BridgeWorld's fake ssh and scp), where this ran scripts/verify-docker-stack.ps1 in pwsh with function fakes.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RetireTempRunnerTimestampTests
{
    [Test]
    [Arguments("2026-09-27T18:47:03.0367129Z", "2026-09-27T18:47:03.0367129Z")]
    [Arguments("2026-09-27T18:47:03.0367129+00:00", "2026-09-27T18:47:03.0367129Z")]
    [Arguments("2026-09-27T19:47:03.0367129+01:00", "2026-09-27T18:47:03.0367129Z")]
    public void Bridge_exports_a_parsed_manifest_timestamp_as_iso_8601_utc(string input, string expected)
    {
        using var world = new C590BridgeWorld();
        var run = world.RunCase("retire-temp-runner", Manifest(input));
        run.Exit.ShouldBe(0, run.Output + run.Error);
        run.Output.ShouldContain("DIAGNOSIS=c780-fake-remote");
        run.Error.ShouldNotContain("tempRetiredAt rejected");
        using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(world.Checkout, ".antiphon", "evidence", "c590-result.json")));
        result.RootElement.GetProperty("accepted").GetBoolean().ShouldBeTrue();
        world.Remotes().Last().ShouldContain($"export C590_TEMP_RETIRED_AT='{expected}'");
    }

    [Test]
    public void Bridge_still_rejects_a_non_timestamp_before_any_remote_call()
    {
        using var world = new C590BridgeWorld();
        var run = world.RunCase("retire-temp-runner", Manifest("2026'; touch /tmp/c780; '"));
        run.Exit.ShouldNotBe(0, run.Output);
        run.Error.ShouldContain("tempRetiredAt rejected");
        world.Calls.ShouldBeEmpty("the bridge must reject before calling ssh or scp");
    }

    private static JsonObject Manifest(string timestamp) => new()
    {
        ["sourceSha"] = new string('a', 40),
        ["runId"] = "c780test",
        ["c604Branch"] = "master",
        ["tempRetiredAt"] = timestamp,
    };
}
