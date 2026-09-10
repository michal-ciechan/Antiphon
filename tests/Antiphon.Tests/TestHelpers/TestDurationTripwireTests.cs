using System.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>CARD-0475 S6: the duration tripwire joins TRX identity by testId, not display name.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestDurationTripwireTests
{
    [Test]
    [Arguments("namespace", "simple")]
    [Arguments("namespace", "full")]
    [Arguments("namespace", "none")]
    [Arguments("no-namespace", "simple")]
    [Arguments("no-namespace", "full")]
    [Arguments("no-namespace", "none")]
    public async Task C475_ClassIdentityJoin(string namespacing, string allow)
    {
        var namespaced = namespacing == "namespace";
        var trx = TwoClassesSameDisplay(namespaced);
        var allowlist = allow switch
        {
            "simple" => "AllowlistedClass\n",
            "full" => "AnTiPhOn.TeStS.ApPlIcAtIoN.AlLoWlIsTeDcLaSs\n",
            _ => "UnrelatedClass\n",
        };
        var run = await InvokeAsync(trx, allowlist);
        if (allow == "none")
        {
            run.ExitCode.ShouldNotBe(0);
            run.Output.ShouldContain("unlisted tests >= 5s");
            CountHits(run.Output).ShouldBe(2);
        }
        else
        {
            run.ExitCode.ShouldNotBe(0);
            CountHits(run.Output).ShouldBe(1, run.Output);
            run.Output.ShouldContain("Other.SlowClass");
            run.Output.ShouldNotContain("AllowlistedClass.SameDisplay");
        }
    }

    [Test]
    public async Task C475_ArgumentsCannotWhitelistAClass()
    {
        var trx = """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun>
              <Results>
                <UnitTestResult testId="u1" executionId="e1" testName="Foo(AgentTaskLandBoundaryTests)" duration="00:00:06.0000000" />
                <UnitTestResult testId="u2" executionId="e2" testName="Bar" duration="00:00:06.0000000" />
                <UnitTestResult testId="u3" executionId="e3" testName="Baz" duration="00:00:06.0000000" />
              </Results>
              <TestDefinitions>
                <UnitTest id="u1" name="Foo"><TestMethod className="UnlistedSlow" name="Foo" /></UnitTest>
                <UnitTest id="u2" name="Bar"><TestMethod className="AgentTaskLandBoundaryTestsExtra" name="Bar" /></UnitTest>
                <UnitTest id="u3" name="Baz"><TestMethod className="PreAgentTaskLandBoundaryTests" name="Baz" /></UnitTest>
              </TestDefinitions>
            </TestRun>
            """;
        var run = await InvokeAsync(trx, "AgentTaskLandBoundaryTests\n");
        run.ExitCode.ShouldNotBe(0);
        CountHits(run.Output).ShouldBe(3, run.Output);
        run.Output.ShouldContain("UnlistedSlow");
        run.Output.ShouldContain("AgentTaskLandBoundaryTestsExtra");
        run.Output.ShouldContain("PreAgentTaskLandBoundaryTests");
    }

    [Test]
    [Arguments("missing-definition")]
    [Arguments("missing-method")]
    [Arguments("missing-class")]
    public async Task C475_UnresolvedIdentityCannotBeExempted(string shape)
    {
        var definition = shape switch
        {
            "missing-definition" => "",
            "missing-method" => """<UnitTest id="u1" name="Foo"></UnitTest>""",
            _ => """<UnitTest id="u1" name="Foo"><TestMethod name="Foo" /></UnitTest>""",
        };
        var trx = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun>
              <Results>
                <UnitTestResult testId="u1" executionId="e1" testName="Foo(AgentTaskLandBoundaryTests)" duration="00:00:06.0000000" />
              </Results>
              <TestDefinitions>
                {definition}
              </TestDefinitions>
            </TestRun>
            """;
        var run = await InvokeAsync(trx, "AgentTaskLandBoundaryTests\n");
        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("unresolved", Case.Insensitive, run.Output);
    }

    [Test]
    public async Task C475_ExpandedRowsAndThresholdAreExact()
    {
        var trx = ThresholdTrx();
        var empty = await InvokeAsync(trx, "");
        empty.ExitCode.ShouldNotBe(0);
        CountHits(empty.Output).ShouldBe(3, empty.Output);
        empty.Output.ShouldContain("expanded rows=4");
        empty.Output.ShouldContain("body-seconds=22.000");
        empty.Output.ShouldNotContain("elapsed");

        var allowed = await InvokeAsync(trx, "SameMethodClass\n");
        allowed.ExitCode.ShouldBe(0, allowed.Output);
        allowed.Output.ShouldContain("0 unlisted tests >= 5s");
    }

    [Test]
    [Arguments("malformed-xml")]
    [Arguments("invalid-duration")]
    [Arguments("missing-duration")]
    [Arguments("negative-duration")]
    public async Task C475_InvalidInputIsNotGreen(string shape)
    {
        var trx = shape switch
        {
            "malformed-xml" => "<not-xml",
            "invalid-duration" => ResultTrx("00:zz:01"),
            "missing-duration" => """
                <?xml version="1.0" encoding="utf-8"?>
                <TestRun>
                  <Results>
                    <UnitTestResult testId="u1" executionId="e1" testName="Foo" />
                  </Results>
                  <TestDefinitions>
                    <UnitTest id="u1" name="Foo"><TestMethod className="C" name="Foo" /></UnitTest>
                  </TestDefinitions>
                </TestRun>
                """,
            _ => ResultTrx("-00:00:06.0000000"),
        };
        var run = await InvokeAsync(trx, "");
        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("invalid input", Case.Insensitive, run.Output);
        run.Output.ShouldNotContain("0 unlisted tests >= 5s");
    }

    [Test]
    public async Task C475_SimpleAndFullNamesAreExact()
    {
        var trx = """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun>
              <Results>
                <UnitTestResult testId="u1" executionId="e1" testName="Foo" duration="00:00:06.0000000" />
              </Results>
              <TestDefinitions>
                <UnitTest id="u1" name="Foo"><TestMethod className="Antiphon.Tests.Application.ExactClass" name="Foo" /></UnitTest>
              </TestDefinitions>
            </TestRun>
            """;
        (await InvokeAsync(trx, "ExactClass\n")).ExitCode.ShouldBe(0);
        (await InvokeAsync(trx, "antiphon.tests.application.exactclass\n")).ExitCode.ShouldBe(0);
        (await InvokeAsync(trx, "Exact\n")).ExitCode.ShouldNotBe(0);
        (await InvokeAsync(trx, "Class\n")).ExitCode.ShouldNotBe(0);
        (await InvokeAsync(trx, "Foo\n")).ExitCode.ShouldNotBe(0);
    }

    private static string TwoClassesSameDisplay(bool namespaced)
    {
        var xmlns = namespaced ? " xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"" : "";
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun{xmlns}>
              <Results>
                <UnitTestResult testId="id-a" executionId="exec-z" testName="SameDisplay(1)" duration="00:00:06.0000000" />
                <UnitTestResult testId="id-b" executionId="exec-a" testName="SameDisplay(1)" duration="00:00:06.0000000" />
              </Results>
              <TestDefinitions>
                <UnitTest id="id-b" name="SameDisplay(1)"><TestMethod className="Other.SlowClass" name="SameDisplay" /></UnitTest>
                <UnitTest id="id-a" name="SameDisplay(1)"><TestMethod className="Antiphon.Tests.Application.AllowlistedClass" name="SameDisplay" /></UnitTest>
              </TestDefinitions>
            </TestRun>
            """;
    }

    private static string ThresholdTrx() => """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun>
          <Results>
            <UnitTestResult testId="a" executionId="e1" testName="SameDisplay" duration="00:00:04.9990000" />
            <UnitTestResult testId="b" executionId="e2" testName="SameDisplay" duration="00:00:05.0000000" />
            <UnitTestResult testId="c" executionId="e3" testName="SameDisplay" duration="00:00:05.0010000" />
            <UnitTestResult testId="d" executionId="e4" testName="SameDisplay" duration="00:00:07.0000000" />
          </Results>
          <TestDefinitions>
            <UnitTest id="a" name="M"><TestMethod className="SameMethodClass" name="M" /></UnitTest>
            <UnitTest id="b" name="M"><TestMethod className="SameMethodClass" name="M" /></UnitTest>
            <UnitTest id="c" name="M"><TestMethod className="SameMethodClass" name="M" /></UnitTest>
            <UnitTest id="d" name="M"><TestMethod className="SameMethodClass" name="M" /></UnitTest>
          </TestDefinitions>
        </TestRun>
        """;

    private static string ResultTrx(string duration) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun>
          <Results>
            <UnitTestResult testId="u1" executionId="e1" testName="Foo" duration="{duration}" />
          </Results>
          <TestDefinitions>
            <UnitTest id="u1" name="Foo"><TestMethod className="C" name="Foo" /></UnitTest>
          </TestDefinitions>
        </TestRun>
        """;

    private static int CountHits(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            const string prefix = "SLOW-TEST TRIPWIRE: ";
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rest = trimmed[prefix.Length..];
            var space = rest.IndexOf(' ');
            if (space <= 0) continue;
            if (int.TryParse(rest[..space], out var n) && rest.Contains("unlisted tests", StringComparison.Ordinal))
                return n;
        }
        return 0;
    }

    private static async Task<(int ExitCode, string Output)> InvokeAsync(string trxXml, string allowlist)
    {
        var root = Path.Combine(Path.GetTempPath(), "c475-tripwire-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var trxPath = Path.Combine(root, "run.trx");
        var allowPath = Path.Combine(root, "allow.txt");
        await File.WriteAllTextAsync(trxPath, trxXml);
        await File.WriteAllTextAsync(allowPath, allowlist);
        var script = Path.Combine(RepoRoot, "scripts", "test-duration-tripwire.ps1");
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("-Trx");
        start.ArgumentList.Add(trxPath);
        start.ArgumentList.Add("-Allowlist");
        start.ArgumentList.Add(allowPath);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        var output = await stdout + await stderr;
        try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        return (process.ExitCode, output);
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            return dir?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate repo root (Antiphon.sln).");
        }
    }
}
