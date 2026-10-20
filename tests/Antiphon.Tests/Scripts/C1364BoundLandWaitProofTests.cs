using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class C1364BoundLandWaitProofTests
{
    private const string ProjectId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1";
    private const string LandId = "22222222-2222-2222-2222-222222222222";
    private const string Operation = "c100800000000000000000000000000000001";
    private static string LandPath => RollingVolumeRecycleScriptTests.C1087ListPath(ProjectId, "land");

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1364_Wrapper_redeploy_reports_waited()
    {
        foreach (var mode in new[] { "ordinary", "resume", "preview" })
        foreach (var clears in new[] { false, true })
            await WrapperVector("V-1/" + mode, "redeploy-old", mode, clears, earlierCensuses: 0,
                nextCase: "deploy-parent");
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1364_Wrapper_cleanup_reports_waited()
    {
        foreach (var entry in new[] { "drain-temp", "retire-temp", "preview" })
        foreach (var clears in new[] { false, true })
            await WrapperVector("V-2/" + entry, entry == "drain-temp" ? entry : "retire-temp",
                entry == "preview" ? "preview" : "ordinary", clears, earlierCensuses: 0,
                nextCase: "retire-temp-containers");
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1364_Wrapper_retire_volumes_reports_waited()
    {
        foreach (var mode in new[] { "ordinary", "resume", "preview" })
        foreach (var clears in new[] { false, true })
            await WrapperVector("V-3/" + mode, "retire-temp", mode, clears,
                earlierCensuses: mode == "resume" ? 0 : 1, nextCase: "retire-temp-runner");
    }

    private static JsonObject Land(string runner) => new()
    {
        ["id"] = LandId, ["status"] = "Succeeded", ["runnerId"] = runner,
        ["projectId"] = ProjectId, ["scopeSource"] = "Task",
        ["landRequestedAt"] = "2020-01-01T00:00:00Z", ["landStartedAt"] = null
    };

    private static async Task WrapperVector(string label, string phase, string mode, bool clears,
        int earlierCensuses, string nextCase)
    {
        using var fixture = new C1008WrapperFixture();
        var runner = phase == "redeploy-old" ? "server2" : "server2-temp";
        if (runner == "server2")
        {
            fixture.MainDrained();
            fixture.State["allowClear"] = true;
        }
        var empty = fixture.State["tasks"]!;
        var pending = empty.DeepClone();
        pending["items"]!.AsArray().Add(Land(runner));
        var sequence = new JsonArray();
        for (var i = 0; i < earlierCensuses * 2; i++) sequence.Add(empty.DeepClone());
        sequence.Add(pending);
        if (clears) sequence.Add(empty.DeepClone());
        fixture.State["taskQueries"] = new JsonObject { [LandPath] = sequence };
        var args = new List<string>();
        if (mode == "resume") args.AddRange(["-ResumeRecycle", Operation]);
        if (mode == "preview") args.Add("-DryRun");
        if (clears) fixture.Environment["C727_TEST_LAND_WAIT_MS"] = "60000";
        else args.AddRange(["-LandWaitMinutes", "0"]);
        var observer = Path.Combine(fixture.Root, "observe-case.ps1");
        var observedReceipt = Path.Combine(fixture.Root, "selected-census.json");
        File.WriteAllText(observer, $$"""
            param([string]$Case, [string]$Manifest)
            $ErrorActionPreference = 'Stop'
            if ($Case -eq '{{nextCase}}') {
                $request = Get-Content -Raw -LiteralPath $Manifest | ConvertFrom-Json
                Copy-Item -LiteralPath (Join-Path $request.evidenceRoot 'census-{{phase}}-{{runner}}.json') -Destination '{{observedReceipt}}'
            }
            & '{{DelegateScriptRunner.RepoRoot}}/scripts/fixtures/c727-fake-verify.ps1' -Case $Case -Manifest $Manifest
            exit $LASTEXITCODE
            """);
        fixture.Environment["C727_TEST_VERIFY_STUB"] = observer;
        var run = await fixture.Run(phase, args.ToArray());
        label += clears ? "/clears" : "/expires";
        run.Exit.ShouldBe(clears ? 0 : 2, label + ": " + run.Output);
        var cases = run.Trace.Where(t => t["kind"]?.GetValue<string>() == "case")
            .Select(t => t["name"]!.GetValue<string>()).ToArray();
        cases.Contains(nextCase).ShouldBe(clears, label + ": next host effect");
        if (earlierCensuses != 0)
            cases.ShouldContain("retire-temp-containers", label + ": earlier cleanup really completed");
        if (!clears)
            cases.ShouldBe(earlierCensuses == 0 ? [] : new[] { "retire-temp-containers" }, label);
        var reads = run.Trace.Where(t => t["path"]?.GetValue<string>() == LandPath).ToArray();
        // V-2 retire-temp clearing can continue through the later volume census as well.
        var laterCensuses = clears && nextCase == "retire-temp-containers" && phase == "retire-temp" ? 1 : 0;
        reads.Length.ShouldBe(earlierCensuses * 2 + (clears ? 3 : 1) + laterCensuses * 2, label);
        var waitLines = run.Output.Split('\n').Select(x => x.TrimEnd('\r'))
            .Where(x => x.StartsWith("RECYCLE_LAND_WAIT ", StringComparison.Ordinal)).ToArray();
        waitLines.ShouldBe(clears ? new[] { $"RECYCLE_LAND_WAIT runner={runner} id={LandId} polls=0" } : [], label);
        var root = run.Trace.First(t => t["kind"]?.GetValue<string>() == "prerequisite")["evidenceRoot"]!.GetValue<string>();
        var receipt = JsonNode.Parse(File.ReadAllText(clears ? observedReceipt :
            Path.Combine(root, $"census-{phase}-{runner}.json")))!;
        receipt["landWait"]!["polls"]!.GetValue<int>().ShouldBe(clears ? 1 : 0, label);
        receipt["landWait"]!["outcome"]!.GetValue<string>().ShouldBe(clears ? "cleared" : "expired", label);
        if (!clears)
        {
            var diagnostic = run.Output.Split('\n').Select(x => x.TrimEnd('\r'))
                .Single(x => x.StartsWith("Rolling deploy stopped: ", StringComparison.Ordinal))[24..];
            AssertWaitDiagnostic(diagnostic, runner, 0, label);
        }
        Console.WriteLine("C1364_VECTOR " + label + " passed");
    }

    private static void AssertWaitDiagnostic(string diagnostic, string runner, int polls, string label)
    {
        var match = Regex.Match(diagnostic,
            @"\ARecycleLandInFlight (?<id>[0-9a-f-]{36}) runner=(?<runner>[a-z0-9-]+) waited=(?<polls>[0-9]+)polls\z");
        match.Success.ShouldBeTrue(label + ": complete, single waited suffix: " + diagnostic);
        match.Groups["id"].Value.ShouldBe(LandId, label);
        match.Groups["runner"].Value.ShouldBe(runner, label);
        int.Parse(match.Groups["polls"].Value).ShouldBe(polls, label + ": diagnostic waited count");
    }
}
