using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.TestSupport;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointTempUsageTests : CheckpointTestBase
{
    [Test]
    public async Task inspection_is_read_only()
    {
        var sandbox = TempDir();
        var candidate = Path.Combine(sandbox, "c723-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(candidate);
        var sentinel = Path.Combine(candidate, "sentinel.bin");
        await File.WriteAllBytesAsync(sentinel, RandomNumberGenerator.GetBytes(4096));
        var before = SHA256.HashData(await File.ReadAllBytesAsync(sentinel));
        var script = Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "inspect-checkpoint-temp.ps1");
        using var process = Process.Start(new ProcessStartInfo("pwsh")
        {
            ArgumentList = { "-NoProfile", "-File", script, "-TempRoot", sandbox },
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        RegisterCheckpointChild(process);
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        process.ExitCode.ShouldBe(0, error);
        using var report = JsonDocument.Parse(output);
        report.RootElement.GetProperty("roots").GetArrayLength().ShouldBe(1);
        report.RootElement.GetProperty("roots")[0].GetProperty("reason").GetString().ShouldBe("legacy-unmarked");
        SHA256.HashData(await File.ReadAllBytesAsync(sentinel)).ShouldBe(before);
        Directory.Exists(candidate).ShouldBeTrue();
    }

    [Test]
    public async Task incomplete_trx_never_accepts_residue()
    {
        var dir = TempDir();
        var roster = UsageLibrary.WriteRoster(dir, "N.C.one", "N.C.two");
        var events = UsageLibrary.WriteEvents(dir, complete: true);
        var torn = UsageLibrary.WriteEvents(dir, complete: false);
        var partial = Path.Combine(dir, "partial.trx");
        CheckpointFixtures.WriteResults(partial, ("N.C.one", "Passed"));
        var whole = Path.Combine(dir, "whole.trx");
        CheckpointFixtures.WriteResults(whole, ("N.C.one", "Passed"), ("N.C.two", "Passed"));
        using var result = await UsageLibrary.RunAsync(RegisterCheckpointChild, dir, $$"""
            @{
              partial = Test-UsageEvidence -Phase Full -RosterPath {{UsageLibrary.Quote(roster)}} -TrxPath {{UsageLibrary.Quote(partial)}} -EventsPath {{UsageLibrary.Quote(events)}}
              whole = Test-UsageEvidence -Phase Full -RosterPath {{UsageLibrary.Quote(roster)}} -TrxPath {{UsageLibrary.Quote(whole)}} -EventsPath {{UsageLibrary.Quote(events)}}
              torn = Test-UsageEvidence -Phase Full -RosterPath {{UsageLibrary.Quote(roster)}} -TrxPath {{UsageLibrary.Quote(whole)}} -EventsPath {{UsageLibrary.Quote(torn)}}
              missing = Test-UsageEvidence -Phase Full -RosterPath {{UsageLibrary.Quote(roster)}} -TrxPath {{UsageLibrary.Quote(Path.Combine(dir, "absent.trx"))}} -EventsPath {{UsageLibrary.Quote(events)}}
            } | ConvertTo-Json -Depth 8
            """);
        var root = result.RootElement;
        var partialErrors = UsageLibrary.Errors(root, "partial");
        partialErrors.ShouldContain("roster/TRX count mismatch selected=2 terminal=1");
        partialErrors.ShouldContain("selected class/method multiset differs from TRX");
        // The complete inputs clear both gates, so the partial TRX alone caused them.
        var wholeErrors = UsageLibrary.Errors(root, "whole");
        wholeErrors.ShouldNotContain(error => error.StartsWith("roster/TRX count mismatch", StringComparison.Ordinal));
        wholeErrors.ShouldNotContain("selected class/method multiset differs from TRX");
        wholeErrors.ShouldNotContain("event tail incomplete");
        wholeErrors.ShouldNotContain("created/deleted root IDs do not reconcile");
        UsageLibrary.Errors(root, "torn").ShouldContain("event tail incomplete");
        UsageLibrary.Errors(root, "torn").ShouldContain("created/deleted root IDs do not reconcile");
        UsageLibrary.Errors(root, "missing").ShouldContain("TRX missing");
    }

    [Test]
    public async Task namespace_census_uses_the_native_execution_roster()
    {
        var dir = TempDir();
        // The real compiled checkpoint cases, and this OS's declared skips resolved by the library.
        var names = CompiledCheckpointNames();
        var namesFile = Path.Combine(dir, "names.json");
        await File.WriteAllTextAsync(namesFile, JsonSerializer.Serialize(names));
        using var declared = await UsageLibrary.RunAsync(RegisterCheckpointChild, dir, $$"""
            $census = Get-NamespaceCensus
            ConvertTo-Json -InputObject @(Get-Content -LiteralPath {{UsageLibrary.Quote(namesFile)}} -Raw | ConvertFrom-Json |
                Where-Object { Test-DeclaredOsSkip $_ $census })
            """);
        var skips = declared.RootElement.EnumerateArray().Select(name => name.GetString()!).ToHashSet(StringComparer.Ordinal);
        skips.ShouldNotBeEmpty();
        var events = UsageLibrary.WriteEvents(dir, complete: true);
        var roster = UsageLibrary.WriteRoster(dir, names);
        var native = Trx(dir, names, name => skips.Contains(name) ? "NotExecuted" : "Passed");
        var skipRan = Trx(dir, names, _ => "Passed");
        var extraSkip = names.First(name => !skips.Contains(name));
        var unexplained = Trx(dir, names, name => skips.Contains(name) || name == extraSkip ? "NotExecuted" : "Passed");
        var shortNames = names.Where(name => name != extraSkip).ToArray();
        var shortRoster = UsageLibrary.WriteRoster(dir, shortNames);
        var shortTrx = Trx(dir, shortNames, name => skips.Contains(name) ? "NotExecuted" : "Passed");
        string Evidence(string rosterPath, string trx) =>
            $"Test-UsageEvidence -Phase Namespace -RosterPath {UsageLibrary.Quote(rosterPath)} -TrxPath {UsageLibrary.Quote(trx)} -EventsPath {UsageLibrary.Quote(events)}";
        using var result = await UsageLibrary.RunAsync(RegisterCheckpointChild, dir, $$"""
            @{
              native = {{Evidence(roster, native)}}
              skipRan = {{Evidence(roster, skipRan)}}
              unexplained = {{Evidence(roster, unexplained)}}
              short = {{Evidence(shortRoster, shortTrx)}}
            } | ConvertTo-Json -Depth 8
            """);
        var evidence = result.RootElement.GetProperty("native");
        UsageLibrary.Errors(result.RootElement, "native").ShouldBeEmpty();
        evidence.GetProperty("selected").GetInt32().ShouldBe(names.Length);
        evidence.GetProperty("executed").GetInt32().ShouldBe(names.Length - skips.Count);
        evidence.GetProperty("skipped").GetInt32().ShouldBe(skips.Count);
        UsageLibrary.Errors(result.RootElement, "skipRan").ShouldContain(error => error.StartsWith("namespace skips differ", StringComparison.Ordinal));
        UsageLibrary.Errors(result.RootElement, "unexplained").ShouldContain(error => error.StartsWith("namespace skips differ", StringComparison.Ordinal));
        UsageLibrary.Errors(result.RootElement, "short").ShouldContain(error => error.StartsWith("namespace census selected=", StringComparison.Ordinal));
    }

    [Test]
    public async Task namespace_census_matches_compiled_checkpoint_cases()
    {
        var dir = TempDir();
        var names = CompiledCheckpointNames();
        var namesFile = Path.Combine(dir, "names.json");
        await File.WriteAllTextAsync(namesFile, JsonSerializer.Serialize(names));
        using var result = await UsageLibrary.RunAsync(RegisterCheckpointChild, dir, $$"""
            $census = Get-NamespaceCensus
            $names = @(Get-Content -LiteralPath {{UsageLibrary.Quote(namesFile)}} -Raw | ConvertFrom-Json)
            $unmatched = @(@($census.windowsSkips) + @($census.linuxSkips) | Where-Object {
                $pattern = $_; @($names | Where-Object { $_ -clike $pattern }).Count -eq 0 })
            @{ selected = $census.selected; unmatched = ($unmatched -join ',') } | ConvertTo-Json -Depth 4
            """);
        // Adding or removing a checkpoint case must move Get-NamespaceCensus with it.
        result.RootElement.GetProperty("selected").GetInt32().ShouldBe(names.Length,
            "scripts/lib/checkpoint-usage.ps1 Get-NamespaceCensus is stale against the compiled checkpoint cases");
        result.RootElement.GetProperty("unmatched").GetString().ShouldBeEmpty("every declared OS skip must name a compiled checkpoint case");
    }

    [Test]
    public void full_roster_excludes_explicit_cases()
    {
        var assembly = typeof(CheckpointTempUsageTests).Assembly;
        var explicitClasses = assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && type.IsDefined(typeof(ExplicitAttribute), inherit: true))
            .Where(type => TestClassificationMetadata.GetTestMethods(type).Count > 0)
            .ToArray();
        explicitClasses.ShouldNotBeEmpty("this assembly carries [Explicit] cases the Full roster must drop");
        var cases = explicitClasses
            .SelectMany(type => TestClassificationMetadata.GetTestMethods(type).Select(method => (type, method.Name)))
            .Select((test, i) => new CheckpointRoster.Case("explicit-" + i, test.type, test.Name, test.Name))
            .Append(new CheckpointRoster.Case("ordinary", typeof(CheckpointTempUsageTests),
                nameof(full_roster_excludes_explicit_cases), nameof(full_roster_excludes_explicit_cases)))
            .ToArray();
        cases.Length.ShouldBeGreaterThan(1);
        var target = Path.Combine(TempDir(), "roster.json");
        CheckpointRoster.Write(target, cases);
        using var roster = JsonDocument.Parse(File.ReadAllText(target));
        roster.RootElement.EnumerateArray().Select(test => test.GetProperty("id").GetString()).ShouldBe(["ordinary"]);
        var compiled = CheckpointRoster.CompiledCases(assembly);
        compiled.ShouldContain((typeof(CheckpointTempUsageTests).FullName!, nameof(full_roster_excludes_explicit_cases)));
        compiled.ShouldNotContain(test => explicitClasses.Any(type => type.FullName == test.ClassName));
    }

    private static string[] CompiledCheckpointNames() =>
        CheckpointRoster.CompiledCases(typeof(CheckpointTempUsageTests).Assembly, "Antiphon.Tests.Checkpoints")
            .Select(test => test.ClassName + "." + test.Method).ToArray();

    private static string Trx(string dir, IEnumerable<string> names, Func<string, string> outcome)
    {
        var path = Path.Combine(dir, "namespace-" + Guid.NewGuid().ToString("N") + ".trx");
        CheckpointFixtures.WriteResults(path, names.Select(name => (name, outcome(name))).ToArray());
        return path;
    }

    [Test]
    public async Task allocated_bytes_include_written_payload()
    {
        var work = TempDir();
        var dir = Path.Combine(work, "sampled");
        Directory.CreateDirectory(Path.Combine(dir, "nested"));
        await File.WriteAllBytesAsync(Path.Combine(dir, "nested", "payload.bin"), RandomNumberGenerator.GetBytes(128 * 1024));
        using var result = await UsageLibrary.RunAsync(RegisterCheckpointChild, work, $$"""
            $bytes = Get-Allocated {{UsageLibrary.Quote(dir)}}
            $absent = Get-Allocated {{UsageLibrary.Quote(Path.Combine(dir, "absent"))}}
            $script:duCalls = 0
            function du {
                $script:duCalls++
                if ($script:duCalls -eq 1) {
                    $global:LASTEXITCODE = 1
                    "du: cannot access '/gone': No such file or directory"
                } else {
                    $global:LASTEXITCODE = 0
                    "8192`t$($args[-1])"
                }
            }
            $retried = Get-Allocated {{UsageLibrary.Quote(dir)}}
            function du { $global:LASTEXITCODE = 1; 'du: Permission denied' }
            $denied = $false
            try { [void](Get-Allocated {{UsageLibrary.Quote(dir)}}) }
            catch { $denied = $_.Exception.Message -like 'Allocated-byte sample failed*' }
            @{ bytes = $bytes; absent = $absent; retried = $retried; calls = $script:duCalls; denied = $denied } | ConvertTo-Json
            """);
        var allocated = result.RootElement.GetProperty("bytes").GetInt64();
        allocated.ShouldBeGreaterThanOrEqualTo(128 * 1024);
        allocated.ShouldBeLessThan(192 * 1024);
        result.RootElement.GetProperty("absent").GetInt64().ShouldBe(0);
        if (OperatingSystem.IsLinux())
        {
            result.RootElement.GetProperty("retried").GetInt64().ShouldBe(8192);
            result.RootElement.GetProperty("calls").GetInt32().ShouldBe(2);
            result.RootElement.GetProperty("denied").GetBoolean().ShouldBeTrue();
        }
    }

    [Test]
    public async Task repeated_pass_manifest_is_exact()
    {
        var dir = TempDir();
        var events = UsageLibrary.WriteEvents(dir, complete: true);
        var firstRoster = UsageLibrary.WriteRoster(dir, "C.a", "C.a", "C.b");
        var firstTrx = Path.Combine(dir, "first.trx");
        CheckpointFixtures.WriteResults(firstTrx, ("C.a", "Passed"), ("C.a", "Passed"), ("C.b", "Passed"));
        var reorderedRoster = UsageLibrary.WriteRoster(dir, "C.b", "C.a", "C.a");
        var reorderedTrx = Path.Combine(dir, "reordered.trx");
        CheckpointFixtures.WriteResults(reorderedTrx, ("C.b", "Passed"), ("C.a", "Passed"), ("C.a", "Passed"));
        var shortRoster = UsageLibrary.WriteRoster(dir, "C.a", "C.b");
        var shortTrx = Path.Combine(dir, "short.trx");
        CheckpointFixtures.WriteResults(shortTrx, ("C.a", "Passed"), ("C.b", "Passed"));
        var skewedTrx = Path.Combine(dir, "skewed.trx");
        CheckpointFixtures.WriteResults(skewedTrx, ("C.a", "Passed"), ("C.b", "Passed"), ("C.b", "Passed"));
        var prior = Path.Combine(dir, "pass1-report.json");
        using var result = await UsageLibrary.RunAsync(RegisterCheckpointChild, dir, $$"""
            $first = Test-UsageEvidence -Phase Full -RosterPath {{UsageLibrary.Quote(firstRoster)}} -TrxPath {{UsageLibrary.Quote(firstTrx)}} -EventsPath {{UsageLibrary.Quote(events)}}
            [IO.File]::WriteAllText({{UsageLibrary.Quote(prior)}}, (@{ rosterHash = $first.rosterHash; peakAllocatedBytes = 100; finalAllocatedBytes = 0; createdRoots = 1 } | ConvertTo-Json))
            $reordered = Test-UsageEvidence -Phase Full -RosterPath {{UsageLibrary.Quote(reorderedRoster)}} -TrxPath {{UsageLibrary.Quote(reorderedTrx)}} -EventsPath {{UsageLibrary.Quote(events)}}
            $short = Test-UsageEvidence -Phase Full -RosterPath {{UsageLibrary.Quote(shortRoster)}} -TrxPath {{UsageLibrary.Quote(shortTrx)}} -EventsPath {{UsageLibrary.Quote(events)}}
            @{
              first = $first
              skewed = Test-UsageEvidence -Phase Full -RosterPath {{UsageLibrary.Quote(firstRoster)}} -TrxPath {{UsageLibrary.Quote(skewedTrx)}} -EventsPath {{UsageLibrary.Quote(events)}}
              samePass = Compare-UsagePass -PriorPath {{UsageLibrary.Quote(prior)}} -RosterHash $reordered.rosterHash -PeakBytes 150 -FinalBytes 0 -CreatedRoots 1
              shortPass = Compare-UsagePass -PriorPath {{UsageLibrary.Quote(prior)}} -RosterHash $short.rosterHash -PeakBytes 150 -FinalBytes 0 -CreatedRoots 1
              noPrior = Compare-UsagePass -PriorPath {{UsageLibrary.Quote(Path.Combine(dir, "absent.json"))}} -RosterHash $first.rosterHash -PeakBytes 0 -FinalBytes 0 -CreatedRoots 1
            } | ConvertTo-Json -Depth 8
            """);
        var root = result.RootElement;
        UsageLibrary.Errors(root, "first").ShouldNotContain("selected class/method multiset differs from TRX");
        UsageLibrary.Errors(root, "skewed").ShouldContain("selected class/method multiset differs from TRX");
        UsageLibrary.Errors(root, "samePass").ShouldBeEmpty();
        root.GetProperty("samePass").GetProperty("delta").GetProperty("peakAllocatedBytes").GetInt64().ShouldBe(50);
        UsageLibrary.Errors(root, "shortPass").ShouldBe(["pass rosters differ"]);
        UsageLibrary.Errors(root, "noPrior").ShouldBe(["pass-1 accepted report missing"]);
    }

    [Test]
    public async Task assembly_and_allocation_invoke_bounded_sweep()
    {
        // The assembly hook alone: the selected child test allocates nothing.
        var assemblySandbox = TempDir();
        var assemblyOrphan = PlantDeadRoot(assemblySandbox);
        var (assemblyExit, assemblyOutput) = await RunLifecycleHost(assemblySandbox, "AssemblyHookHostTests", "no_allocation",
            disableAssemblySweep: false);
        assemblyExit.ShouldBe(0, assemblyOutput);
        Directory.Exists(assemblyOrphan).ShouldBeFalse(assemblyOutput);

        // The allocation sweep alone: the assembly hook is disabled and the child allocates one root.
        var allocationSandbox = TempDir();
        var allocationOrphan = PlantDeadRoot(allocationSandbox);
        var (allocationExit, allocationOutput) = await RunLifecycleHost(allocationSandbox, "LifecycleHostTests", "passing",
            disableAssemblySweep: true);
        allocationExit.ShouldBe(0, allocationOutput);
        Directory.Exists(allocationOrphan).ShouldBeFalse(allocationOutput);
    }

    private static string PlantDeadRoot(string sandbox)
    {
        var candidate = Path.Combine(sandbox, "c723-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(candidate);
        File.WriteAllBytes(Path.Combine(candidate, "payload.bin"), RandomNumberGenerator.GetBytes(4096));
        var owner = new ProcessIdentityProbe().Current();
        TestRootGuard.Write(candidate, new CheckpointRootMarker
        {
            RootId = Path.GetFileName(candidate)[5..], RootPath = candidate,
            AttemptId = Guid.NewGuid().ToString("N"), AssemblyInvocationId = "usage-unit",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Owner = owner with { Pid = int.MaxValue, StartUtcTicks = 1 },
        });
        new CheckpointTempRootSweep(sandbox).Register(candidate);
        return candidate;
    }

    private async Task<(int Exit, string Output)> RunLifecycleHost(string sandbox, string className, string method,
        bool disableAssemblySweep)
    {
        var hostDll = Path.Combine(AppContext.BaseDirectory, "checkpoint-lifecycle-host",
            "Antiphon.Checkpoints.LifecycleHost.dll");
        File.Exists(hostDll).ShouldBeTrue("lifecycle host must be staged by the parent build");
        var psi = new ProcessStartInfo("dotnet")
        {
            ArgumentList = { hostDll, "--treenode-filter", $"/*/*/{className}/{method}" },
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["TMPDIR"] = sandbox;
        psi.Environment["TEMP"] = sandbox;
        psi.Environment["TMP"] = sandbox;
        psi.Environment["C804_LIFECYCLE_ROOTS"] = Path.Combine(sandbox, "roots.txt");
        psi.Environment.Remove("C804_ROSTER_FILE");
        psi.Environment.Remove("C804_ROOT_EVENTS");
        // The hook skips itself whenever this is set; point it at a path that does not exist.
        if (disableAssemblySweep)
            psi.Environment["C804_ORPHAN_SWEEP_ROOT"] = Path.Combine(sandbox, "no-orphan-sweep");
        else
            psi.Environment.Remove("C804_ORPHAN_SWEEP_ROOT");
        using var process = Process.Start(psi)!;
        RegisterCheckpointChild(process);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
        return (process.ExitCode, await stdout + "\n" + await stderr);
    }
}

/// <summary>
/// Runs scripts/lib/checkpoint-usage.ps1 (the functions verify-checkpoint-temp-usage.ps1 uses)
/// in a fresh pwsh against supplied inputs, returning the JSON the driver body prints.
/// </summary>
internal static class UsageLibrary
{
    public static string LibraryPath => Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "lib", "checkpoint-usage.ps1");

    public static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    public static async Task<(int Exit, string Output, string Error)> InvokeAsync(Action<Process> register, string dir, string body)
    {
        var file = Path.Combine(dir, "c804-usage-driver-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(file, "$ErrorActionPreference = 'Stop'\n. " + Quote(LibraryPath) + "\n" + body + "\n");
        try
        {
            using var process = Process.Start(new ProcessStartInfo("pwsh")
            {
                ArgumentList = { "-NoProfile", "-NonInteractive", "-File", file },
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            register(process);
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            return (process.ExitCode, await output, await error);
        }
        finally
        {
            File.Delete(file);
        }
    }

    public static async Task<JsonDocument> RunAsync(Action<Process> register, string dir, string body)
    {
        var (exit, output, error) = await InvokeAsync(register, dir, body);
        exit.ShouldBe(0, error);
        return JsonDocument.Parse(output);
    }

    public static string[] Errors(JsonElement root, string name)
    {
        var errors = root.GetProperty(name).GetProperty("errors");
        // A one-element PowerShell array serializes as the element itself.
        return errors.ValueKind switch
        {
            JsonValueKind.Array => errors.EnumerateArray().Select(error => error.GetString()!).ToArray(),
            JsonValueKind.String => [errors.GetString()!],
            _ => [],
        };
    }

    public static string WriteRoster(string dir, params string[] names)
    {
        var path = Path.Combine(dir, "roster-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(names.Select((name, i) =>
        {
            var split = name.LastIndexOf('.');
            return new { id = "id-" + i, className = name[..split], method = name[(split + 1)..], displayName = name };
        })));
        return path;
    }

    public static string WriteEvents(string dir, bool complete)
    {
        var path = Path.Combine(dir, "events-" + Guid.NewGuid().ToString("N") + ".jsonl");
        var root = Path.Combine(dir, "c723-" + Guid.NewGuid().ToString("N"));
        var create = JsonSerializer.Serialize(new { kind = "root-create", path = root, bytes = 0 });
        var delete = JsonSerializer.Serialize(new { kind = "root-delete", path = root, bytes = 0 });
        // A torn stream stops after the create, without the final newline.
        File.WriteAllText(path, complete ? create + "\n" + delete + "\n" : create);
        return path;
    }
}
