using System.Diagnostics;
using System.Text;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
[ClassDataSource<ClassificationProbeSession>(Shared = SharedType.PerClass)]
public sealed class TestClassificationPolicyTests
{
    private readonly ClassificationProbeSession _probes;

    public TestClassificationPolicyTests(ClassificationProbeSession probes) => _probes = probes;

    [Test]
    [Arguments("blank")]
    [Arguments("overwritten")]
    [Arguments("eof")]
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
        var errors = "";
        C1383WithRegistry(string.Join("\n", kept) + "\n",
            path => errors = TestClassificationMetadata.AssertRegistryMatches(assembly, path, repositoryMode: true));
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
        var errors = C1383Errors("# CARD-0487 unknown\nAntiphon.Tests.DoesNotExistAtAll\n");
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
        var errors = C1383Errors(text);
        if (shape == "duplicate-fqn")
        {
            errors.ShouldContain("duplicate Antiphon.Tests.SmokeTests");
            var upper = C1383Smoke.ToUpperInvariant();
            C1383Errors($"# CARD-0487 first\n{C1383Smoke}\n# CARD-0487 second\n{upper}\n")
                .ShouldContain("duplicate " + upper);
        }
        else
            errors.ShouldContain("simple-name-refused SmokeTests");
    }

    [Test]
    public void C487_G063()
    {
        C1383WithRegistry("# CARD-0487 legacy simple\nSmokeTests\n", path =>
        {
            var repo = TestClassificationMetadata.AssertRegistryMatches(
                typeof(TestClassificationPolicyTests).Assembly, path, repositoryMode: true);
            repo.ShouldContain("simple-name-refused SmokeTests");
            var legacy = TestClassificationMetadata.AssertRegistryMatches(
                typeof(TestClassificationPolicyTests).Assembly, path, repositoryMode: false);
            legacy.ShouldNotContain("simple-name-refused");
        });
    }

    [Test]
    public void C487_G064()
    {
        foreach (var prefix in new[] { "", "# \n", "# unqualified reason\n" })
            C1383Errors(prefix + C1383Smoke + "\n").ShouldContain("missing-reason " + C1383Smoke);
    }

    [Test]
    public async Task C487_G065()
    {
        var errors = "";
        // The probe directory is deleted when RunProbeAsync returns, so read the build inside it.
        var probe = await RunProbeAsync(includeMethodSlow: true, inspect: dir =>
        {
            var dll = Directory.GetFiles(dir, "Probe.dll", SearchOption.AllDirectories)
                .FirstOrDefault(p => p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                ?? Directory.GetFiles(dir, "Probe.dll", SearchOption.AllDirectories).First();
            var asm = System.Reflection.Assembly.LoadFrom(dll);
            C1383WithRegistry("# CARD-0487 probe SlowCases\nC487.Probe.SlowCases\n",
                registry => errors = TestClassificationMetadata.AssertRegistryMatches(asm, registry, repositoryMode: true));
        });
        probe.Source.ShouldContain("MethodSlow");
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
        // Each probe build is ~48 MB; one left per run filled server2's /tmp (120 GB, 2026-11-08).
        Directory.Exists(probe.Dir).ShouldBeFalse();
    }

    [Test]
    public async Task C1864_ordinary_probe_is_shared_after_its_directory_is_removed()
    {
        var watch = Stopwatch.StartNew();
        var first = await RunProbeAsync();
        var second = await RunProbeAsync();
        Console.WriteLine($"C1864 two ordinary probe requests seconds={watch.Elapsed.TotalSeconds:F3}");
        Directory.Exists(first.Dir).ShouldBeFalse();
        Directory.Exists(second.Dir).ShouldBeFalse();
        second.ShouldBeSameAs(first, "the immutable discovery/run result should be built once per test session");
        first.DefaultPassed.ShouldBe(8);
        first.SlowCount.ShouldBe(2);
        first.ManualAbsent.ShouldBeTrue();
    }

    [Test]
    public async Task C487_probe_temp_directory_is_removed_after_the_body()
    {
        string? seen = null;
        var result = await WithTempDirectoryAsync("c487-probe-", async dir =>
        {
            seen = dir;
            var output = Directory.CreateDirectory(Path.Combine(dir, "bin", "Debug"));
            var readOnly = Path.Combine(output.FullName, "Probe.dll");
            await File.WriteAllTextAsync(readOnly, "probe");
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            return 7;
        });
        result.ShouldBe(7);
        seen.ShouldNotBeNull();
        Directory.Exists(seen).ShouldBeFalse();
    }

    [Test]
    public async Task C487_probe_temp_directory_is_removed_when_the_body_throws()
    {
        string? seen = null;
        await Should.ThrowAsync<InvalidOperationException>(() => WithTempDirectoryAsync<int>("c487-probe-", async dir =>
        {
            seen = dir;
            await File.WriteAllTextAsync(Path.Combine(dir, "Probe.cs"), "probe");
            throw new InvalidOperationException("probe build failed");
        }));
        seen.ShouldNotBeNull();
        Directory.Exists(seen).ShouldBeFalse();
    }

    [Test]
    public async Task C487_probe_stale_sibling_is_removed_at_start()
    {
        var owned = ProbeOwnedRoot();
        var stale = Path.Combine(owned, "c487-probe-" + Guid.NewGuid().ToString("N"));
        var fresh = Path.Combine(owned, "c487-probe-" + Guid.NewGuid().ToString("N"));
        var nearMiss = Path.Combine(owned, "c487-probe-" + new string('G', 32));
        Directory.CreateDirectory(stale);
        Directory.CreateDirectory(fresh);
        Directory.CreateDirectory(nearMiss);
        await File.WriteAllTextAsync(Path.Combine(stale, "marker.txt"), "stale\n");
        await File.WriteAllTextAsync(Path.Combine(fresh, "marker.txt"), "fresh\n");
        await File.WriteAllTextAsync(Path.Combine(nearMiss, "marker.txt"), "near\n");
        var old = DateTime.UtcNow.AddHours(-25);
        Directory.SetLastWriteTimeUtc(stale, old);
        Directory.SetLastWriteTimeUtc(nearMiss, old);
        Directory.SetLastWriteTimeUtc(fresh, DateTime.UtcNow);

        var foreignParent = OwnedTempRoot.CreateOwned();
        var foreign = Path.Combine(foreignParent, "c487-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(foreign);
        await File.WriteAllTextAsync(Path.Combine(foreign, "marker.txt"), "foreign\n");
        Directory.SetLastWriteTimeUtc(foreign, DateTime.UtcNow.AddHours(-48));
        try
        {
            await WithTempDirectoryAsync("c487-probe-", dir =>
            {
                SameDirectory(Path.GetDirectoryName(dir)!, owned).ShouldBeTrue();
                Path.GetFileName(dir).StartsWith("c487-probe-", StringComparison.Ordinal).ShouldBeTrue();
                Directory.Exists(stale).ShouldBeFalse();
                Directory.Exists(fresh).ShouldBeTrue();
                Directory.Exists(nearMiss).ShouldBeTrue();
                Directory.Exists(foreign).ShouldBeTrue();
                File.ReadAllText(Path.Combine(fresh, "marker.txt")).Replace("\r\n", "\n").ShouldBe("fresh\n");
                File.ReadAllText(Path.Combine(nearMiss, "marker.txt")).Replace("\r\n", "\n").ShouldBe("near\n");
                File.ReadAllText(Path.Combine(foreign, "marker.txt")).Replace("\r\n", "\n").ShouldBe("foreign\n");
                return Task.FromResult(0);
            });
        }
        finally
        {
            if (Directory.Exists(stale))
                StartRefGit.DeleteDirectory(stale);
            if (Directory.Exists(fresh))
                StartRefGit.DeleteDirectory(fresh);
            if (Directory.Exists(nearMiss))
                StartRefGit.DeleteDirectory(nearMiss);
            OwnedTempRoot.Release(foreignParent);
        }
    }

    // The stable root must stay out of OwnedTempRoot. A process-exit release that tracks it
    // deletes every sibling, including another process's live probe.
    [Test]
    public async Task C487_probe_release_leaves_fresh_foreign_siblings()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("C487_RELEASE_WITNESS_CHILD"), "1", StringComparison.Ordinal))
        {
            ReleaseFreshForeignSiblings();
            return;
        }

        var scratch = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "c487-release-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var child = await RunReleaseWitnessChildAsync(scratch);
            child.ExitCode.ShouldBe(0, child.Output);
            AssertFreshForeignSiblings(scratch);
        }
        finally
        {
            if (Directory.Exists(scratch))
                StartRefGit.DeleteDirectory(scratch);
        }
    }

    [Test]
    public void C487_probe_stale_sweep_skips_a_probe_named_reparse_point()
    {
        var root = Path.Combine(Path.GetTempPath(), "c487-reparse-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(Path.GetTempPath(), "c487-reparse-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(target);
        var marker = Path.Combine(target, "keep.txt");
        File.WriteAllText(marker, "keep\n");
        var link = Path.Combine(root, "c487-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateSymbolicLink(link, target);
        try
        {
            File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint).ShouldBeTrue();
            Directory.SetLastWriteTimeUtc(link, DateTime.UtcNow.AddHours(-25));
            SweepStaleProbeDirectories(root, DateTimeOffset.UtcNow);
            Directory.Exists(Path.GetFullPath(link)).ShouldBeTrue("probe-named reparse point must survive the stale sweep");
            File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint).ShouldBeTrue();
            File.ReadAllText(Path.GetFullPath(marker)).Replace("\r\n", "\n").ShouldBe("keep\n");
        }
        finally
        {
            if (Directory.Exists(root))
                StartRefGit.DeleteDirectory(root);
            if (Directory.Exists(target))
                StartRefGit.DeleteDirectory(target);
        }
    }

    private static void ReleaseFreshForeignSiblings()
    {
        var scratch = Environment.GetEnvironmentVariable("TMPDIR");
        if (string.IsNullOrEmpty(scratch))
            throw new InvalidOperationException("release witness child requires TMPDIR");
        SameDirectory(Path.GetTempPath(), scratch).ShouldBeTrue(
            "GetTempPath must be the scratch TMPDIR before release");
        var root = ProbeOwnedRoot();
        SameDirectory(Path.GetDirectoryName(root)!, scratch).ShouldBeTrue();
        var fresh = Path.Combine(root, "c487-probe-" + Guid.NewGuid().ToString("N"));
        var nearMiss = Path.Combine(root, "c487-probe-" + new string('A', 32));
        var unrelated = Path.Combine(root, "unrelated");
        Directory.CreateDirectory(fresh);
        Directory.CreateDirectory(nearMiss);
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(fresh, "live.txt"), "fresh\n");
        File.WriteAllText(Path.Combine(nearMiss, "live.txt"), "near\n");
        File.WriteAllText(Path.Combine(unrelated, "o.txt"), "other\n");
        OwnedTempRoot.ReleaseAll();
        AssertFreshForeignSiblings(scratch);
    }

    private static void AssertFreshForeignSiblings(string scratch)
    {
        var root = Path.GetFullPath(Path.Combine(scratch, OwnedTempRoot.OwnedPrefix + ProbeOwnedLeaf));
        Directory.Exists(root).ShouldBeTrue("owned root must survive release");
        var fresh = Directory.EnumerateDirectories(root)
            .Select(NormalizeDirectory)
            .Where(path => IsExactProbeDirectoryName(Path.GetFileName(path)))
            .ToArray();
        fresh.Length.ShouldBe(1, "a fresh c487-probe sibling must survive release");
        var nearMiss = Path.GetFullPath(Path.Combine(root, "c487-probe-" + new string('A', 32)));
        var unrelated = Path.GetFullPath(Path.Combine(root, "unrelated"));
        Directory.Exists(nearMiss).ShouldBeTrue("uppercase near-miss must survive release");
        Directory.Exists(unrelated).ShouldBeTrue("unrelated sibling must survive release");
        File.ReadAllText(Path.Combine(fresh[0], "live.txt")).Replace("\r\n", "\n").ShouldBe("fresh\n");
        File.ReadAllText(Path.Combine(nearMiss, "live.txt")).Replace("\r\n", "\n").ShouldBe("near\n");
        File.ReadAllText(Path.Combine(unrelated, "o.txt")).Replace("\r\n", "\n").ShouldBe("other\n");
    }

    private static async Task<(int ExitCode, string Output)> RunReleaseWitnessChildAsync(string scratch)
    {
        var assembly = typeof(TestClassificationPolicyTests).Assembly.Location;
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = scratch,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--treenode-filter");
        start.ArgumentList.Add("/*/*/TestClassificationPolicyTests/C487_probe_release_leaves_fresh_foreign_siblings");
        start.ArgumentList.Add("--no-ansi");
        start.Environment["TMPDIR"] = scratch;
        start.Environment["TMP"] = scratch;
        start.Environment["TEMP"] = scratch;
        start.Environment["C487_RELEASE_WITNESS_CHILD"] = "1";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet exec did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            throw new TimeoutException("c487 release witness child exceeded 5 minutes");
        }

        return (process.ExitCode, await stdout + await stderr);
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

        var live = Path.Combine(RepoRoot, "tests", "Antiphon.LiveClaude.Tests", "ClaudeSubmitContractLiveTests.cs");
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
        Directory.Exists(empty.Root).ShouldBeFalse();
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

    private static Task<(int ExitCode, string Output, string Root)> InvokeTripwireAsync(string trxXml, string allowlist)
        => WithTempDirectoryAsync("c487-tw-", async root =>
        {
            var trxPath = Path.Combine(root, "run.trx");
            var allowPath = Path.Combine(root, "allow.txt");
            await File.WriteAllTextAsync(trxPath, trxXml);
            await File.WriteAllTextAsync(allowPath, allowlist);
            var run = await InvokeTripwireAsyncPath(trxPath, allowPath);
            return (run.ExitCode, run.Output, root);
        });

    // 32 hex digits so this directory matches the registered antiphon-owned family.
    // The name is stable so a later process can see probe directories a killed run left.
    // The root itself is not tracked: ReleaseAll would otherwise delete every sibling.
    internal const string ProbeOwnedLeaf = "c4870000000000000000000000000000";

    internal static string ProbeOwnedRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), OwnedTempRoot.OwnedPrefix + ProbeOwnedLeaf);
        Directory.CreateDirectory(root);
        return NormalizeDirectory(root);
    }

    private static readonly TimeSpan StaleProbeAge = TimeSpan.FromHours(24);

    private static bool SameDirectory(string left, string right) =>
        string.Equals(NormalizeDirectory(left), NormalizeDirectory(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsExactProbeDirectoryName(string name)
    {
        const string prefix = "c487-probe-";
        if (name.Length != prefix.Length + 32 || !name.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        for (var i = prefix.Length; i < name.Length; i++)
        {
            if (name[i] is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                return false;
        }

        return true;
    }

    private static bool IsProbeReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// Deletes direct children of <paramref name="ownedRoot"/> named <c>c487-probe-</c> plus 32
    /// lowercase hex digits whose last write is older than 24 hours. A probe-named symlink or
    /// other reparse point is left in place. The parent must be the temp directory. Nothing
    /// outside that parent is enumerated.
    /// </summary>
    internal static int SweepStaleProbeDirectories(string ownedRoot, DateTimeOffset utcNow)
    {
        var root = NormalizeDirectory(ownedRoot);
        var temp = NormalizeDirectory(Path.GetTempPath());
        var parent = Path.GetDirectoryName(root);
        if (parent is null || !SameDirectory(parent, temp) || SameDirectory(root, temp) || !Directory.Exists(root))
        {
            Console.WriteLine("c487-probe stale sweep removed=0");
            return 0;
        }

        var removed = 0;
        foreach (var entry in Directory.EnumerateDirectories(root))
        {
            var full = NormalizeDirectory(entry);
            var entryParent = Path.GetDirectoryName(full);
            if (entryParent is null || !SameDirectory(entryParent, root))
                continue;
            if (!IsExactProbeDirectoryName(Path.GetFileName(full)))
                continue;
            if (IsProbeReparsePoint(full))
                continue;
            var written = new DateTimeOffset(Directory.GetLastWriteTimeUtc(full), TimeSpan.Zero);
            if (utcNow - written <= StaleProbeAge)
                continue;
            ReleaseTempDirectory(full);
            if (!Directory.Exists(full))
                removed++;
        }

        Console.WriteLine($"c487-probe stale sweep removed={removed}");
        return removed;
    }

    // Every temp directory this class makes is its own; the body's result is all that survives it.
    private static async Task<T> WithTempDirectoryAsync<T>(string prefix, Func<string, Task<T>> body)
    {
        string dir;
        if (string.Equals(prefix, "c487-probe-", StringComparison.Ordinal))
        {
            var owned = ProbeOwnedRoot();
            SweepStaleProbeDirectories(owned, DateTimeOffset.UtcNow);
            dir = Path.Combine(owned, prefix + Guid.NewGuid().ToString("N"));
        }
        else
        {
            dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        }

        Directory.CreateDirectory(dir);
        dir = NormalizeDirectory(dir);
        OwnedTempRoot.Track(dir);
        try { return await body(dir); }
        // Best-effort: a failed delete (a Probe.dll still loaded on Windows) never fails the test.
        finally { ReleaseTempDirectory(dir); }
    }

    private static void ReleaseTempDirectory(string path)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (!Directory.Exists(path))
            {
                OwnedTempRoot.Release(path);
                return;
            }

            ClearReadOnlyAttributes(path);
            StartRefGit.DeleteDirectory(path);
            if (!Directory.Exists(path))
            {
                OwnedTempRoot.Release(path);
                return;
            }

            if (attempt < 3)
                Thread.Sleep(TimeSpan.FromMilliseconds(50 * attempt));
        }
    }

    private static void ClearReadOnlyAttributes(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (attributes.HasFlag(FileAttributes.ReparsePoint))
            return;
        if (attributes.HasFlag(FileAttributes.ReadOnly))
        {
            try { File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(path).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var entry in entries)
        {
            FileAttributes entryAttributes;
            try
            {
                entryAttributes = File.GetAttributes(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (entryAttributes.HasFlag(FileAttributes.ReparsePoint))
                continue;
            if (entryAttributes.HasFlag(FileAttributes.ReadOnly))
            {
                try { File.SetAttributes(entry, entryAttributes & ~FileAttributes.ReadOnly); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            if (entryAttributes.HasFlag(FileAttributes.Directory))
                ClearReadOnlyAttributes(entry);
        }
    }

    // The tripwire in process (Antiphon.Nightly.DurationTripwire, the test-duration-tripwire command; unit 33 deleted the
    // script). Without a readable allowlist it reads the checkout's, as the command does.
    private static Task<(int ExitCode, string Output)> InvokeTripwireAsyncPath(string trxPath, string allowPath)
    {
        var output = new StringWriter();
        var allowlist = !string.IsNullOrWhiteSpace(allowPath) && File.Exists(allowPath) ? allowPath : Antiphon.Nightly.DurationTripwireOptions.DefaultAllowlist(RepoRoot);
        var exit = Antiphon.Nightly.DurationTripwire.Run(new Antiphon.Nightly.DurationTripwireOptions { Trx = trxPath, Allowlist = allowlist },
            Antiphon.Nightly.NightlyHost.Production(output, output), new System.IO.Abstractions.FileSystem());
        return Task.FromResult((exit, output.ToString()));
    }
    internal sealed record ProbeResult(string Dir, string Source, int DiscoveryCount, int DefaultPassed, int SlowCount, bool ManualAbsent, string DiscoveryJson);

    private Task<ProbeResult> RunProbeAsync(bool includeMethodSlow = false, Action<string>? inspect = null)
        => !includeMethodSlow && inspect is null
            ? _probes.OrdinaryAsync()
            : WithTempDirectoryAsync("c487-probe-", dir => BuildAndRunProbeAsync(dir, includeMethodSlow, inspect));

    internal static async Task<ProbeResult> BuildOrdinaryProbeAsync()
    {
        var watch = Stopwatch.StartNew();
        // Only the immutable result survives the existing owned-directory cleanup.
        var result = await WithTempDirectoryAsync("c487-probe-", dir => BuildAndRunProbeAsync(dir, false, null));
        Console.WriteLine($"C1864 ordinary probe setup seconds={watch.Elapsed.TotalSeconds:F3}");
        return result;
    }

    private static async Task<ProbeResult> BuildAndRunProbeAsync(string dir, bool includeMethodSlow, Action<string>? inspect)
    {
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
        inspect?.Invoke(dir);
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

// TUnit owns this lazy for one class in one process. A failed setup remains failed;
// variants that inspect a live assembly still take the fresh path above.
public sealed class ClassificationProbeSession
{
    private readonly Lazy<Task<TestClassificationPolicyTests.ProbeResult>> _ordinary =
        new(TestClassificationPolicyTests.BuildOrdinaryProbeAsync);

    internal Task<TestClassificationPolicyTests.ProbeResult> OrdinaryAsync() => _ordinary.Value;
}
