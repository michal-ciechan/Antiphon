using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0307 S2: <c>test-client &lt;filter&gt;</c> must scope the Vitest run to the named file. The PowerShell wrapper once
/// called <c>npx vitest run @args</c>; on Windows <c>npx</c> is the installer's <c>npx.ps1</c>, which rebuilt argv from the
/// SOURCE TEXT of the calling line, so the literal <c>@args</c> reached Node and every scoped run silently became the whole
/// 5-7 minute suite. The command (Antiphon.Nightly.ClientTests, which replaced the body of scripts/test-client.ps1 in unit 32
/// of the 2026-11-22 plan) starts <c>node client/node_modules/vitest/vitest.mjs run</c> itself, each argument one argv entry.
/// These cases pin that boundary in process, on a recording runner: the filter, a flag and a name with spaces reach the
/// Vitest launch unchanged and in order, npx and npm are never the program, and the gate switches are the gates' (the small
/// node tsconfig and the one test file), not Vitest's. The real-Vitest run of the former test (one test file under the
/// wrapper, ~46 s) is gone: no test runs node or npm.
/// </summary>
[Category("Unit")]
public sealed class TestClientFilterTests
{
    [Test]
    public void Filter_argument_reaches_vitest_and_runs_exactly_one_file()
    {
        var world = new ClientChecksTests.World().Installed();
        world.Write(world.Client("src", "features", "attention", "attentionVisuals.test.ts"));

        world.Test("--typecheck-project", "client/tsconfig.node.json", "--lint-path", "client/src/features/attention/attentionVisuals.test.ts",
            "attentionVisuals.test", "-t", "keeps the glow when collapsed").ShouldBe(0);

        var launches = world.Tools.Launches;
        launches.Count.ShouldBe(3, "the typecheck gate, the lint gate and Vitest, in that order");
        launches.ShouldAllBe(launch => launch.FileName == "node", "never npx or npm: their PowerShell shims rebuild argv from source text");
        launches[0].Arguments.ToArray().ShouldBe([world.Tsc, "--pretty", "false", "--noEmit", "-p", "client/tsconfig.node.json"]);
        launches[1].Arguments.ToArray().ShouldBe([world.Eslint, "attentionVisuals.test.ts", "--max-warnings", "0"]);
        launches[2].Arguments.ToArray().ShouldBe([world.Vitest, "run", "attentionVisuals.test", "-t", "keeps the glow when collapsed"]);
        launches[2].WorkingDirectory.ShouldBe(world.Client());
        launches[2].Arguments.ShouldNotContain("--typecheck-project");
        launches[2].Arguments.ShouldNotContain("--lint-path");
    }
}
