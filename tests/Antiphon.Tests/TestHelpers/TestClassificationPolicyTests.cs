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
    [Arguments("blank")]
    public void C1383_Detached_reason_is_rejected(string shape)
    {
        var text = "# CARD-1383 first\n" + (shape switch
        {
            "blank" => "\n" + C1383Smoke + "\n",
            "overwritten" => "# CARD-1383 second\n" + C1383Smoke + "\n",
            _ => ""
        });
        var errors = C1383Errors(text);
        errors.ShouldContain("orphan-reason line 1");
        if (shape == "blank")
            errors.ShouldContain("missing-adjacent-reason line 3 " + C1383Smoke);
    }

    [Test]
    [Arguments("bare-row")]
    [Arguments("second-row")]
    public void C1383_Row_without_adjacent_reason_is_rejected(string shape)
    {
        var prefix = shape == "bare-row" ? "" : "# CARD-1383 first\n" + C1383Other + "\n";
        C1383Errors(prefix + C1383Smoke + "\n")
            .ShouldContain($"missing-adjacent-reason line {(shape == "bare-row" ? 1 : 3)} {C1383Smoke}");
    }

    [Test]
    [Arguments("between-pairs")]
    [Arguments("interrupts-first-pair")]
    public void C1383_Header_after_records_is_rejected(string shape)
    {
        var interrupted = shape == "interrupts-first-pair";
        var text = interrupted
            ? "# CARD-1383 reason\n## misplaced header\n" + C1383Smoke + "\n"
            : "# CARD-1383 reason\n" + C1383Smoke + "\n## misplaced header\n";
        var errors = C1383Errors(text);
        errors.ShouldContain($"header-after-entry line {(interrupted ? 2 : 3)}");
        if (interrupted)
        {
            errors.ShouldContain("orphan-reason line 1");
            errors.ShouldContain("missing-adjacent-reason line 3 " + C1383Smoke);
        }
    }

    [Test]
    [Arguments("\n", true)]
    [Arguments("\n", false)]
    [Arguments("\r\n", true)]
    [Arguments("\r\n", false)]
    public void C1383_Valid_reason_pairs_are_accepted(string newline, bool finalNewline)
    {
        var entries = C1383Entries();
        var text = "## Registry instructions\n## More instructions\n\n"
            + string.Join("\n\n", entries.Select(e => $"# {e.Reason}\n{e.Name}"));
        text = (text + (finalNewline ? "\n" : "")).Replace("\n", newline);
        C1383WithRegistry(text, path =>
        {
            TestClassificationMetadata.AssertRegistryMatches(GetType().Assembly, path, true).ShouldBe("");
            var actual = TestClassificationMetadata.ReadRegistry(path);
            actual.Select(e => (e.Name, e.Reason)).ShouldBe(entries.Select(e => (e.Name, e.Reason)));
        });
    }

    [Test]
    [Arguments("non-repository")]
    [Arguments("other-assembly")]
    public void C1383_Layout_opt_in_is_scoped(string mode)
    {
        if (mode == "non-repository")
        {
            var text = C1383RegistryText(C1383Entries());
            text = text.Insert(text.IndexOf('\n') + 1, "\n") + "# CARD-1383 orphan\n";
            C1383WithRegistry(text, path =>
                TestClassificationMetadata.AssertRegistryMatches(GetType().Assembly, path, false).ShouldBe(""));
        }
        else
        {
            var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                new System.Reflection.AssemblyName("C1383.Other"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
            C1383WithRegistry("# CARD-1383 orphan\n", path =>
                TestClassificationMetadata.AssertRegistryMatches(assembly, path, true).ShouldBe(""));
        }
    }

    [Test]
    public void C1383_ReadRegistry_retains_legacy_layout_behavior()
    {
        var text = $"# CARD-1383 first\n\n{C1383Smoke}\n# CARD-1383 overwritten\n# CARD-1383 surviving\n{C1383Other}\n# CARD-1383 orphan\n";
        C1383WithRegistry(text, path => TestClassificationMetadata.ReadRegistry(path).ShouldBe(
            new[]
            {
                new TestClassificationMetadata.RegistryEntry(C1383Smoke, "CARD-1383 first", 3),
                new TestClassificationMetadata.RegistryEntry(C1383Other, "CARD-1383 surviving", 6)
            }));
    }

    internal const string C1383Smoke = "Antiphon.Tests.SmokeTests";
    internal const string C1383Other = "Antiphon.Tests.Application.WorktreeDefaultAdmissionTests";

    internal static IReadOnlyList<TestClassificationMetadata.RegistryEntry> C1383Entries()
    {
        var assembly = typeof(TestClassificationPolicyTests).Assembly;
        var entries = TestClassificationMetadata.ReadRegistry(TestClassificationMetadata.FindRegistryPath(assembly));
        var slow = TestClassificationMetadata.Read(assembly).Where(c => c.Categories.Contains("Slow"))
            .Select(c => c.FullName).ToArray();
        slow.ShouldContain(C1383Smoke);
        slow.ShouldContain(C1383Other);
        entries.Select(e => e.Name).ShouldContain(C1383Smoke);
        entries.Select(e => e.Name).ShouldContain(C1383Other);
        return entries;
    }

    internal static string C1383RegistryText(IEnumerable<TestClassificationMetadata.RegistryEntry> entries) =>
        string.Concat(entries.Select(e => $"# {e.Reason}\n{e.Name}\n"));

    internal static string C1383Errors(string text)
    {
        var errors = "";
        C1383WithRegistry(text, path => errors = TestClassificationMetadata.AssertRegistryMatches(
            typeof(TestClassificationPolicyTests).Assembly, path, true));
        return errors;
    }

    private static void C1383WithRegistry(string text, Action<string> check)
    {
        var path = WriteTempRegistry(text);
        try { check(path); }
        finally { File.Delete(path); }
    }

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
    public void Missing_slow_class_is_named()
    {
        const string dropped = "Antiphon.Tests.Application.WorktreeDefaultAdmissionTests";
        var assembly = typeof(TestClassificationPolicyTests).Assembly;
        var real = TestClassificationMetadata.FindRegistryPath(assembly);
        var kept = File.ReadAllLines(real)
            .Where(line => !string.Equals(line.Trim(), dropped, StringComparison.Ordinal))
            .ToArray();
        kept.ShouldNotContain(dropped);
        var path = WriteTempRegistry(string.Join("\n", kept) + "\n");
        var errors = TestClassificationMetadata.AssertRegistryMatches(assembly, path, repositoryMode: true);
        errors.ShouldContain("missing classes: ");
        errors.ShouldContain(dropped);
        errors.ShouldContain("unregistered-marked " + dropped);
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
        var dll = Directory.GetFiles(probe.Dir, "Probe.dll", SearchOption.AllDirectories)
            .FirstOrDefault(p => p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            ?? Directory.GetFiles(probe.Dir, "Probe.dll", SearchOption.AllDirectories).First();
        var asm = System.Reflection.Assembly.LoadFrom(dll);
        var registry = WriteTempRegistry("# CARD-0487 probe SlowCases\nC487.Probe.SlowCases\n");
        var errors = TestClassificationMetadata.AssertRegistryMatches(asm, registry, repositoryMode: true);
        errors.ShouldContain("method-level-slow");
        errors.ShouldContain("C487.Probe.MethodSlow");
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
        var telegram = Path.Combine(RepoRoot, "tests", "Antiphon.Messaging.Tests", "Conformance", "TelegramLiveChatConformanceTests.cs");
        var telegramText = File.ReadAllText(telegram);
        telegramText.ShouldNotContain("[Category(\"OptIn\")]");
        telegramText.ShouldContain("The fake leg always runs");

        var fake = Path.Combine(RepoRoot, "tests", "Antiphon.Agents.Pty.Tests", "ClaudeSubmitContractTests.cs");
        var fakeText = File.ReadAllText(fake);
        fakeText.ShouldNotContain("[Category(\"OptIn\")]");
        fakeText.ShouldNotContain("[Category(\"Headed\")]");
        fakeText.ShouldContain("Arguments(\"fakeclaude\")");
        fakeText.ShouldNotContain("Arguments(\"claude\")");

        var live = Path.Combine(RepoRoot, "tests", "Antiphon.Agents.Pty.Tests", "ClaudeSubmitContractLiveTests.cs");
        var liveText = File.ReadAllText(live);
        liveText.ShouldContain("[Category(\"OptIn\")]");
        liveText.ShouldContain("[Category(\"Headed\")]");
        liveText.ShouldContain("Arguments(\"claude\")");
        liveText.ShouldNotContain("Arguments(\"fakeclaude\")");
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

    private sealed record ProbeResult(string Dir, string Source, int DiscoveryCount, int DefaultPassed, int SlowCount, bool ManualAbsent, string DiscoveryJson);

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
        // CARD-0681: the probe's apphost has no extension off Windows.
        var exe = Directory.GetFiles(dir, OperatingSystem.IsWindows() ? "Probe.exe" : "Probe", SearchOption.AllDirectories).First();
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
        return new ProbeResult(dir, source, discoveryCount, defaultPassed, slowCount, trxAll.IndexOf("ManualOne", StringComparison.Ordinal) < 0, json);
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
