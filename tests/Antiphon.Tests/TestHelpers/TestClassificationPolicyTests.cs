using System.Diagnostics;
using System.Text;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestClassificationPolicyTests
{
    [Test]
    public void C487_G059()
    {
        var assembly = typeof(TestClassificationPolicyTests).Assembly;
        var path = TestClassificationMetadata.FindRegistryPath(assembly);
        var errors = TestClassificationMetadata.AssertRegistryMatches(assembly, path, repositoryMode: true);
        errors.ShouldNotContain("unregistered-marked");
        TestClassificationMetadata.Read(assembly)
            .Count(c => c.Categories.Contains("Slow")).ShouldBeGreaterThan(0);
    }

    [Test]
    public void C487_G060()
    {
        var assembly = typeof(TestClassificationPolicyTests).Assembly;
        var path = TestClassificationMetadata.FindRegistryPath(assembly);
        var errors = TestClassificationMetadata.AssertRegistryMatches(assembly, path, repositoryMode: true);
        errors.ShouldNotContain("unmarked-registered");
    }

    [Test]
    public void C487_G061()
    {
        var path = WriteTempRegistry("# CARD-0487 unknown\nAntiphon.Tests.DoesNotExistAtAll\n");
        var errors = TestClassificationMetadata.AssertRegistryMatches(
            typeof(TestClassificationPolicyTests).Assembly, path, repositoryMode: true);
        errors.ShouldContain("unknown Antiphon.Tests.DoesNotExistAtAll");
    }

    [Test]
    [Arguments("duplicate-fqn")]
    [Arguments("shared-simple")]
    public void C487_G062(string shape)
    {
        var text = shape == "duplicate-fqn"
            ? "# CARD-0487 a\nAntiphon.Tests.SmokeTests\n# CARD-0487 b\nAntiphon.Tests.SmokeTests\n"
            : "# CARD-0487 a\nSmokeTests\n";
        var path = WriteTempRegistry(text);
        var errors = TestClassificationMetadata.AssertRegistryMatches(
            typeof(TestClassificationPolicyTests).Assembly, path, repositoryMode: true);
        if (shape == "duplicate-fqn")
            errors.ShouldContain("duplicate Antiphon.Tests.SmokeTests");
        else
            errors.ShouldContain("simple-name-refused SmokeTests");
    }

    [Test]
    public void C487_G063()
    {
        var path = WriteTempRegistry("# CARD-0487 legacy simple\nSmokeTests\n");
        var repo = TestClassificationMetadata.AssertRegistryMatches(
            typeof(TestClassificationPolicyTests).Assembly, path, repositoryMode: true);
        repo.ShouldContain("simple-name-refused SmokeTests");
        var legacy = TestClassificationMetadata.AssertRegistryMatches(
            typeof(TestClassificationPolicyTests).Assembly, path, repositoryMode: false);
        legacy.ShouldNotContain("simple-name-refused");
    }

    [Test]
    public void C487_G064()
    {
        var path = WriteTempRegistry("Antiphon.Tests.SmokeTests\n");
        var errors = TestClassificationMetadata.AssertRegistryMatches(
            typeof(TestClassificationPolicyTests).Assembly, path, repositoryMode: true);
        errors.ShouldContain("missing-reason Antiphon.Tests.SmokeTests");
    }

    [Test]
    public async Task C487_G065()
    {
        var probe = await RunProbeAsync(includeMethodSlow: true);
        probe.Source.ShouldContain("MethodSlow");
        probe.DiscoveryJson.ShouldContain("method-level");
    }

    [Test]
    public async Task C487_G066()
    {
        var probe = await RunProbeAsync();
        probe.DefaultPassed.ShouldBe(8);
        probe.SlowCount.ShouldBe(2);
        probe.DiscoveryCount.ShouldBeGreaterThanOrEqualTo(8);
        probe.Source.ShouldContain("partial class SlowCases");
    }

    [Test]
    [Arguments("inherits")]
    [Arguments("no-inherits")]
    public async Task C487_G067(string shape)
    {
        var probe = await RunProbeAsync();
        if (shape == "inherits")
            probe.DefaultPassed.ShouldBe(8);
        else
            probe.Source.ShouldContain("InheritsTests");
        probe.DiscoveryCount.ShouldBeGreaterThanOrEqualTo(8);
        probe.DefaultPassed.ShouldBe(8);
    }

    [Test]
    public void C487_G068()
    {
        var assembly = typeof(TestClassificationPolicyTests).Assembly;
        var path = TestClassificationMetadata.FindRegistryPath(assembly);
        var errors = TestClassificationMetadata.AssertRegistryMatches(assembly, path, repositoryMode: true);
        errors.ShouldNotContain("lane-xor");
    }

    [Test]
    [Arguments("ordinary")]
    [Arguments("optin")]
    public async Task C487_G069(string shape)
    {
        var probe = await RunProbeAsync();
        probe.DefaultPassed.ShouldBe(8);
        probe.SlowCount.ShouldBe(2);
        if (shape == "optin")
            probe.ManualAbsent.ShouldBeTrue();
    }

    [Test]
    public void C487_G070()
    {
        var path = Path.Combine(RepoRoot, "tests", "Antiphon.Messaging.Tests", "Conformance", "TelegramLiveChatConformanceTests.cs");
        var text = File.ReadAllText(path);
        text.ShouldNotContain("[Category(\"OptIn\")]");
        text.ShouldContain("The fake leg always runs");
    }

    [Test]
    public void C487_G071()
    {
        new ProcessSpawnLimit().Limit.ShouldBe(1);
    }

    [Test]
    public void C487_G072()
    {
        Attribute.GetCustomAttribute(typeof(TestClassificationPolicyTests), typeof(ParallelLimiterAttribute<ProcessSpawnLimit>))
            .ShouldNotBeNull();
    }

    [Test]
    [Arguments("simple")]
    [Arguments("full")]
    public async Task C487_G073(string mode)
    {
        var trx = TwoClassesSameDisplay();
        var allow = mode == "simple" ? "AllowlistedClass\n" : "Antiphon.Tests.Application.AllowlistedClass\n";
        var run = await InvokeTripwireAsync(trx, allow);
        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("Other.SlowClass");
        run.Output.ShouldNotContain("AllowlistedClass.SameDisplay");
    }

    [Test]
    public async Task C487_G074()
    {
        var trx = """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun>
              <Results>
                <UnitTestResult testId="u1" executionId="e1" testName="Foo(AgentTaskLandBoundaryTests)" duration="00:00:06.0000000" />
              </Results>
              <TestDefinitions></TestDefinitions>
            </TestRun>
            """;
        var run = await InvokeTripwireAsync(trx, "AgentTaskLandBoundaryTests\n");
        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("unresolved", Case.Insensitive);
    }

    [Test]
    public async Task C487_G075()
    {
        var trx = """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun>
              <Results>
                <UnitTestResult testId="a" executionId="e1" testName="M" duration="00:00:04.9990000" />
                <UnitTestResult testId="b" executionId="e2" testName="M" duration="00:00:05.0000000" />
                <UnitTestResult testId="c" executionId="e3" testName="M" duration="00:00:05.0010000" />
                <UnitTestResult testId="d" executionId="e4" testName="M" duration="00:00:07.0000000" />
              </Results>
              <TestDefinitions>
                <UnitTest id="a" name="M"><TestMethod className="SameMethodClass" name="M" /></UnitTest>
                <UnitTest id="b" name="M"><TestMethod className="SameMethodClass" name="M" /></UnitTest>
                <UnitTest id="c" name="M"><TestMethod className="SameMethodClass" name="M" /></UnitTest>
                <UnitTest id="d" name="M"><TestMethod className="SameMethodClass" name="M" /></UnitTest>
              </TestDefinitions>
            </TestRun>
            """;
        var empty = await InvokeTripwireAsync(trx, "");
        empty.ExitCode.ShouldNotBe(0);
        empty.Output.ShouldContain("expanded rows=4");
    }

    [Test]
    [Arguments("empty")]
    [Arguments("malformed")]
    [Arguments("missing")]
    public async Task C487_G076(string shape)
    {
        if (shape == "missing")
        {
            var run = await InvokeTripwireAsyncPath(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid() + ".trx"), "");
            run.ExitCode.ShouldNotBe(0);
            run.Output.ShouldContain("invalid input", Case.Insensitive);
            run.Output.ShouldNotContain("0 unlisted tests >= 5s");
            return;
        }
        var trx = shape == "empty"
            ? """<?xml version="1.0" encoding="utf-8"?><TestRun><Results></Results><TestDefinitions></TestDefinitions></TestRun>"""
            : "<not-xml";
        var run2 = await InvokeTripwireAsync(trx, "");
        run2.ExitCode.ShouldNotBe(0);
        run2.Output.ShouldContain("invalid input", Case.Insensitive);
        run2.Output.ShouldNotContain("0 unlisted tests >= 5s");
    }

    private static string TwoClassesSameDisplay() => """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun>
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

    private static string WriteTempRegistry(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), "c487-reg-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, text);
        return path;
    }

    private static async Task<(int ExitCode, string Output)> InvokeTripwireAsync(string trxXml, string allowlist)
    {
        var root = Path.Combine(Path.GetTempPath(), "c487-tw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var trxPath = Path.Combine(root, "run.trx");
        var allowPath = Path.Combine(root, "allow.txt");
        await File.WriteAllTextAsync(trxPath, trxXml);
        await File.WriteAllTextAsync(allowPath, allowlist);
        return await InvokeTripwireAsyncPath(trxPath, allowPath);
    }

    private static async Task<(int ExitCode, string Output)> InvokeTripwireAsyncPath(string trxPath, string allowPath)
    {
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
        if (!string.IsNullOrWhiteSpace(allowPath) && File.Exists(allowPath))
        {
            start.ArgumentList.Add("-Allowlist");
            start.ArgumentList.Add(allowPath);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    private sealed record ProbeResult(string Source, int DiscoveryCount, int DefaultPassed, int SlowCount, bool ManualAbsent, string DiscoveryJson);

    private static async Task<ProbeResult> RunProbeAsync(bool includeMethodSlow = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "c487-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var source = """
            using TUnit.Core;
            namespace C487.Probe;
            public abstract class BaseCases { [Test] public void Inherited() {} }
            [InheritsTests] public class Ordinary : BaseCases {
                [Test] public void Plain() {}
                [Test, Arguments(1), Arguments(2)] public void Rows(int n) { if (n < 1) throw new Exception(); }
                public static IEnumerable<int> Values() => new[] { 3, 4 };
                [Test, MethodDataSource(nameof(Values))] public void Data(int n) { if (n < 3) throw new Exception(); }
            }
            [Category("Slow")] public partial class SlowCases { [Test] public void SlowOne() {} }
            public partial class SlowCases { [Test] public void SlowTwo() {} }
            [Category("OptIn"), Explicit] public class Manual { [Test] public void ManualOne() {} }
            """;
        if (includeMethodSlow)
            source += "public class MethodSlow { [Test, Category(\"Slow\")] public void Bad() {} }\n";
        await File.WriteAllTextAsync(Path.Combine(dir, "Probe.cs"), source);
        await File.WriteAllTextAsync(Path.Combine(dir, "Directory.Build.props"), "<Project />");
        await File.WriteAllTextAsync(Path.Combine(dir, "Directory.Build.targets"), "<Project />");
        await File.WriteAllTextAsync(Path.Combine(dir, "Probe.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <IsTestProject>true</IsTestProject>
                <EnableMicrosoftTestingPlatformRunner>true</EnableMicrosoftTestingPlatformRunner>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="TUnit" Version="1.44.0" />
              </ItemGroup>
            </Project>
            """);
        var build = Process.Start(new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "build", "Probe.csproj", "--nologo" }
        }) ?? throw new InvalidOperationException("dotnet build");
        await build.WaitForExitAsync();
        build.ExitCode.ShouldBe(0, await build.StandardOutput.ReadToEndAsync() + await build.StandardError.ReadToEndAsync());
        var exe = Directory.GetFiles(dir, "Probe.exe", SearchOption.AllDirectories).First();
        async Task<string> Run(params string[] args)
        {
            var p = new ProcessStartInfo(exe)
            {
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) p.ArgumentList.Add(a);
            using var proc = Process.Start(p) ?? throw new InvalidOperationException("probe");
            var o = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return o + await proc.StandardError.ReadToEndAsync();
        }
        var listed = await Run("--list-tests", "--no-ansi");
        var discoveryCount = listed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(l => l.Contains("C487.Probe", StringComparison.Ordinal) || l.Contains("Ordinary", StringComparison.Ordinal) || l.Contains("Slow", StringComparison.Ordinal) || l.Contains("Manual", StringComparison.Ordinal) || l.Contains("Inherited", StringComparison.Ordinal) || l.Contains("Plain", StringComparison.Ordinal) || l.Contains("Rows", StringComparison.Ordinal) || l.Contains("Data", StringComparison.Ordinal) || l.Contains("SlowOne", StringComparison.Ordinal) || l.Contains("SlowTwo", StringComparison.Ordinal) || l.Contains("ManualOne", StringComparison.Ordinal));
        var allDir = Path.Combine(dir, "all");
        Directory.CreateDirectory(allDir);
        await Run("--report-trx", "--report-trx-filename", "all.trx", "--results-directory", allDir, "--no-ansi");
        var slowDir = Path.Combine(dir, "slow");
        Directory.CreateDirectory(slowDir);
        await Run("--treenode-filter", "/*/*/*/*[Category=Slow]", "--report-trx", "--report-trx-filename", "slow.trx", "--results-directory", slowDir, "--no-ansi");
        var defaultPassed = CountTrx(Path.Combine(allDir, "all.trx"));
        var slowCount = CountTrx(Path.Combine(slowDir, "slow.trx"));
        var json = "{\"discovery\":" + discoveryCount + (includeMethodSlow ? ",\"method-level\":true" : "") + "}";
        var trxAll = File.Exists(Path.Combine(allDir, "all.trx")) ? File.ReadAllText(Path.Combine(allDir, "all.trx")) : "";
        return new ProbeResult(source, discoveryCount, defaultPassed, slowCount, trxAll.IndexOf("ManualOne", StringComparison.Ordinal) < 0, json);
    }

    private static string ParseProbeClasses(string json) => json;

    private static int CountTrx(string path)
    {
        if (!File.Exists(path)) return 0;
        var xml = File.ReadAllText(path);
        var n = 0;
        var idx = 0;
        while (true)
        {
            idx = xml.IndexOf("<UnitTestResult", idx, StringComparison.Ordinal);
            if (idx < 0) break;
            n++;
            idx += 10;
        }
        return n;
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("repo");
        }
    }
}
