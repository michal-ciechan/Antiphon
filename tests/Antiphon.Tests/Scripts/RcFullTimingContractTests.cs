using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1514 S1. Portable contracts for the rc timing instrumentation.
// Native Windows owner proof is CP-2 and is not discharged here.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcFullTimingContractTests
{
    [Test]
    [Timeout(60_000)]
    public async Task OwnerObservationIsPersisted(CancellationToken cancellationToken)
    {
        var root = FreshRoot();
        var observed = await Run(OwnerScript, cancellationToken, root);
        observed.GetProperty("exitCode").GetInt32().ShouldBe(0, observed.GetProperty("text").GetString());
        observed.GetProperty("persisted").GetBoolean().ShouldBeTrue("root exit and descendant image were not persisted");
        observed.GetProperty("image").GetString().ShouldBe("OwnedChild.exe");
        observed.GetProperty("missedImage").GetString().ShouldBe("unknown");
        observed.GetProperty("jobEmpty").GetBoolean().ShouldBeTrue();
        observed.GetProperty("drainMilliseconds").GetDouble().ShouldBe(7);
        observed.GetProperty("executionMilliseconds").GetDouble().ShouldBe(20);
        observed.GetProperty("rootExitedAtUtc").GetString().ShouldBe("2026-10-29T00:00:02.0000000Z");
    }

    [Test]
    [Timeout(60_000)]
    public async Task PlansRetainEverySuite(CancellationToken cancellationToken)
    {
        var root = FreshRoot();
        var observed = await Run(PlansScript, cancellationToken, root);
        observed.GetProperty("exitCode").GetInt32().ShouldBe(0, observed.GetProperty("text").GetString());
        observed.GetProperty("indexSuites").GetInt32().ShouldBe(2, "old singleton overwrite");
        Uids(observed, "antiphonUids").ShouldBe(new[] { "uid-antiphon" });
        Uids(observed, "e2eUids").ShouldBe(new[] { "uid-e2e" });
        SuiteOf(observed, "antiphon-case").ShouldBe("antiphon");
        SuiteOf(observed, "e2e-case").ShouldBe("e2e");
    }

    [Test]
    [Timeout(60_000)]
    public async Task HostSamplesPreserveUnknownAndStop(CancellationToken cancellationToken)
    {
        var root = FreshRoot();
        var observed = await Run(HostScript, cancellationToken, root);
        observed.GetProperty("exitCode").GetInt32().ShouldBe(0, observed.GetProperty("text").GetString());
        observed.GetProperty("sampleCount").GetInt32().ShouldBe(2);
        observed.GetProperty("firstCpu").ValueKind.ShouldBe(JsonValueKind.Null);
        observed.GetProperty("secondCpu").GetDouble().ShouldBe(5);
        observed.GetProperty("unavailableMemory").ValueKind.ShouldBe(JsonValueKind.Null);
        observed.GetProperty("readsAfterStop").GetInt32().ShouldBe(0);
        Matrix(observed, "desktop").ShouldBe("incompatible-runtime");
        Matrix(observed, "net8").ShouldBe("incompatible-runtime");
        Matrix(observed, "net9").ShouldBe("compatible");
        observed.GetProperty("desktopLoaderCalled").GetBoolean().ShouldBeFalse();
        observed.GetProperty("net8LoaderCalled").GetBoolean().ShouldBeFalse();
        observed.GetProperty("desktopAdmission").GetBoolean().ShouldBeFalse();
        observed.GetProperty("net8Admission").GetBoolean().ShouldBeFalse();
        observed.GetProperty("desktopDiagnosticComplete").GetBoolean().ShouldBeTrue();
    }

    [Test]
    [Timeout(60_000)]
    public async Task DiscoveryIsNotCopiedFromSuite(CancellationToken cancellationToken)
    {
        var root = FreshRoot();
        var observed = await Run(DiscoveryScript, cancellationToken, root);
        var impl = await File.ReadAllTextAsync(
            Path.Combine(C1514Support.RepoRoot(), "scripts", "lib", "nightly-tests-impl.ps1"), cancellationToken);
        impl.Contains("$chunkDiscoverySeconds = $suiteDiscoverySeconds", StringComparison.Ordinal)
            .ShouldBeFalse("baseline copied constant");
        observed.GetProperty("exitCode").GetInt32().ShouldBe(0, observed.GetProperty("text").GetString());
        observed.GetProperty("copiedFromSuite").GetBoolean().ShouldBeFalse("baseline copied constant");
        observed.GetProperty("missingChunk").ValueKind.ShouldBe(JsonValueKind.Null);
        observed.GetProperty("markedChunk").GetDouble().ShouldBe(1.5);
        observed.GetProperty("supplementalChunk").GetDouble().ShouldBe(22.627);
        observed.GetProperty("suiteDiscoverySeconds").GetDouble().ShouldBe(21.377);
    }

    [Test]
    [Timeout(60_000)]
    public async Task SetupPhasesAreNonoverlapping(CancellationToken cancellationToken)
    {
        var root = FreshRoot();
        var observed = await Run(SetupScript, cancellationToken, root);
        observed.GetProperty("exitCode").GetInt32().ShouldBe(0, observed.GetProperty("text").GetString());
        observed.GetProperty("countedOnce").GetBoolean().ShouldBeTrue("planning and build were summed once per chunk");
        observed.GetProperty("setupSeconds").GetDouble().ShouldBe(9);
        observed.GetProperty("perChunkBuildSum").GetDouble().ShouldBe(12);
        observed.GetProperty("overlapped").GetBoolean().ShouldBeFalse();
    }

    [Test]
    [Timeout(90_000)]
    public async Task CountsAndJoinsPreserveEvidence(CancellationToken cancellationToken)
    {
        var root = FreshRoot();
        var observed = await Run(CountsScript, cancellationToken, root);
        observed.GetProperty("exitCode").GetInt32().ShouldBe(0, observed.GetProperty("text").GetString());
        observed.GetProperty("earlyPresent").GetBoolean().ShouldBeTrue("completed rows retained");
        observed.GetProperty("latePresent").GetBoolean().ShouldBeTrue("completed rows past the inline bound were dropped");
        observed.GetProperty("torn").GetBoolean().ShouldBeTrue("torn input incomplete");
        observed.GetProperty("complete").GetBoolean().ShouldBeFalse("torn input incomplete");
        observed.GetProperty("sharedCount").GetInt32().ShouldBe(2, "same chunk id across suites collapsed");
        Suites(observed, "sharedSuites").OrderBy(item => item, StringComparer.Ordinal).ShouldBe(new[] { "antiphon", "e2e" });
        observed.GetProperty("orphanOutcome").GetString().ShouldBe("unknown", "missing is unknown");
        observed.GetProperty("orphanIncomplete").GetBoolean().ShouldBeTrue("no false pass");
        observed.GetProperty("orphanCountsKnown").GetBoolean().ShouldBeFalse("no false zero");
        observed.GetProperty("duplicateJoined").GetBoolean().ShouldBeFalse("duplicate native markers");
    }

    private static string[] Uids(JsonElement observed, string name)
    {
        var values = new List<string>();
        foreach (var item in observed.GetProperty(name).EnumerateArray())
            values.Add(item.GetString() ?? "");
        return values.ToArray();
    }

    private static string SuiteOf(JsonElement observed, string testId)
    {
        foreach (var row in observed.GetProperty("rows").EnumerateArray())
        {
            if (row.GetProperty("testId").GetString() == testId)
                return row.GetProperty("suite").GetString() ?? "";
        }

        throw new InvalidOperationException("missing summary row " + testId);
    }

    private static string Matrix(JsonElement observed, string name) =>
        observed.GetProperty("matrix").GetProperty(name).GetString() ?? "";

    private static List<string> Suites(JsonElement observed, string name)
    {
        var values = new List<string>();
        foreach (var item in observed.GetProperty(name).EnumerateArray())
            values.Add(item.GetString() ?? "");
        return values;
    }

    private static string FreshRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "rcspeed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<JsonElement> Run(string body, CancellationToken cancellationToken, string root)
    {
        var script = Path.Combine(Path.GetTempPath(), "rcspeed-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(script, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        var start = new ProcessStartInfo("pwsh");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(C1514Support.RepoRoot());
        start.ArgumentList.Add(root);
        var ran = await C1514Support.RunProcess(start, cancellationToken);
        var resultPath = Path.Combine(root, "observation.json");
        if (!File.Exists(resultPath))
            throw new InvalidOperationException("observation missing\n" + ran.Text);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath, cancellationToken));
        var copy = doc.RootElement.Clone();
        return copy;
    }

    private const string OwnerScript = """
        $ErrorActionPreference = 'Stop'
        $repo = $args[0]
        $work = $args[1]
        $lib = Join-Path $repo 'scripts/lib'
        . (Join-Path $lib 'nightly-common.ps1')
        . (Join-Path $lib 'test-phase-evidence.ps1')
        $descendants = @(
            [pscustomobject]@{ pid = 43; image = 'OwnedChild.exe'; startedAtUtc = '2026-10-29T00:00:00.1000000Z'; exitedAtUtc = '2026-10-29T00:00:01.5000000Z' }
            [pscustomobject]@{ pid = 44; image = ''; startedAtUtc = ''; exitedAtUtc = '' }
        )
        $verdict = [pscustomobject]@{
            ExitCode = 0
            coverageComplete = $true
            testsPassed = $true
            rootStartedAtUtc = '2026-10-29T00:00:00.0000000Z'
            rootExitedAtUtc = '2026-10-29T00:00:02.0000000Z'
            jobEmpty = $true
            remainingProcessCount = 0
            launchMilliseconds = 10
            executionMilliseconds = 20
            cleanupMilliseconds = 4
            drainMilliseconds = 7
            childrenExited = $true
            outputDrained = $true
            missedDescendantPids = @(44)
            descendants = $descendants
        }
        [void](Complete-TestPhaseLaunch -Verdict $verdict -LogRoot $work -TrxPath (Join-Path $work 'missing.trx') -Suite 'antiphon' -Chunk 'all-group-1' -RunId 'run-1' -Attempt '1' -HostWallSeconds 1)
        $found = $false
        $image = ''
        $missedImage = ''
        $jobEmpty = $false
        $drain = 0.0
        $execution = 0.0
        $exited = ''
        $timing = Join-Path $work 'timing'
        if (Test-Path -LiteralPath $timing) {
            foreach ($file in @(Get-ChildItem -LiteralPath $timing -File -Filter '*.json')) {
                $text = [IO.File]::ReadAllText($file.FullName)
                if ($text.IndexOf('rootExitedAtUtc') -lt 0) { continue }
                $found = $true
                $doc = $text | ConvertFrom-Json
                if ($doc.PSObject.Properties.Name -contains 'rootExitedAtUtc') { $exited = [string]$doc.rootExitedAtUtc }
                if ($doc.PSObject.Properties.Name -contains 'jobEmpty') { $jobEmpty = [bool]$doc.jobEmpty }
                if ($doc.PSObject.Properties.Name -contains 'drainMilliseconds') { $drain = [double]$doc.drainMilliseconds }
                if ($doc.PSObject.Properties.Name -contains 'executionMilliseconds') { $execution = [double]$doc.executionMilliseconds }
                if ($doc.PSObject.Properties.Name -contains 'descendants') {
                    foreach ($row in @($doc.descendants)) {
                        $pid = 0
                        if ($row.PSObject.Properties.Name -contains 'pid') { $pid = [int]$row.pid }
                        $rowImage = ''
                        if ($row.PSObject.Properties.Name -contains 'image') { $rowImage = [string]$row.image }
                        if ($pid -eq 43) { $image = $rowImage }
                        if ($pid -eq 44) { $missedImage = $rowImage }
                    }
                }
            }
        }
        $out = [ordered]@{
            exitCode = 0
            text = ''
            persisted = [bool]$found
            image = [string]$image
            missedImage = [string]$missedImage
            jobEmpty = [bool]$jobEmpty
            drainMilliseconds = [double]$drain
            executionMilliseconds = [double]$execution
            rootExitedAtUtc = [string]$exited
        }
        [IO.File]::WriteAllText((Join-Path $work 'observation.json'), ($out | ConvertTo-Json -Compress -Depth 6))
        """;

    private const string PlansScript = """
        $ErrorActionPreference = 'Stop'
        $repo = $args[0]
        $work = $args[1]
        $lib = Join-Path $repo 'scripts/lib'
        . (Join-Path $lib 'nightly-common.ps1')
        . (Join-Path $lib 'nightly-policy.ps1')
        . (Join-Path $lib 'nightly-coverage.ps1')
        . (Join-Path $lib 'test-phase-evidence.ps1')
        function New-RcNode([string]$uid, [string]$className) {
            $type = $className.Split('.')[-1]
            return [pscustomobject]@{
                ClassName = $className
                Type = $type
                Method = 'One'
                Uid = $uid
                State = 'NotStarted'
                Categories = @()
                Excluded = $false
            }
        }
        function New-RcGroup([string]$className) {
            $type = $className.Split('.')[-1]
            return [pscustomobject]@{ id = 'all-group-1'; classes = @($type); seconds = 3; exactFilter = '' }
        }
        $antiphonClass = 'Antiphon.Tests.Scripts.RcAntiphonSampleTests'
        $e2eClass = 'Antiphon.Tests.E2E.RcE2ESampleTests'
        $common = @{
            LogRoot = $work; RunId = 'run-1'; Attempt = '1'
            SourceDigest = 'src'; AssemblyDigest = 'asm'; PolicyDigest = 'pol'; CostFileDigest = 'cost'
            AssemblyId = 'Antiphon.Tests'; ProfileExclusions = @()
        }
        [void](Write-TestPhasePlannedManifest @common -Suite 'antiphon' -Groups @(New-RcGroup $antiphonClass) -DiscoveryNodes @(New-RcNode 'uid-antiphon' $antiphonClass))
        [void](Write-TestPhasePlannedManifest @common -Suite 'e2e' -Groups @(New-RcGroup $e2eClass) -DiscoveryNodes @(New-RcNode 'uid-e2e' $e2eClass))
        function Write-RcCase([string]$suite, [string]$id) {
            $timing = Join-Path $work 'timing'
            $header = '{"recordKind":"header","version":1,"pid":1,"nonce":"n1","frequencyHz":10000000,"epochUtc":"2026-10-29T00:00:00.0000000Z","runId":"run-1","groupId":"all-group-1","attempt":"1","suite":"' + $suite + '","assembly":"Antiphon.Tests"}'
            $complete = '{"recordKind":"complete","testId":"' + $id + '","className":"C","method":"M","args":[],"startOffsetMs":0,"endOffsetMs":1,"outcome":"Passed","phases":[],"processes":[],"exclusive":{"db":1,"host":0,"process":0,"fs":0,"network":0,"fixture":0},"overlapMs":0,"remainderMs":0,"childUnionMs":0,"droppedPhaseCount":0,"shared":false,"incomplete":false}'
            [IO.File]::WriteAllText((Join-Path $timing ('cases-' + $suite + '-n1.jsonl')), ($header + "`n" + $complete + "`n"))
            $trxPath = Join-Path $work ($suite + '.trx')
            $trx = '<?xml version="1.0" encoding="utf-8"?><TestRun><Results><UnitTestResult testId="' + $id + '" testName="' + $id + '" outcome="Passed"><Output><StdOut>C1514_NATIVE body ' + $id + '</StdOut></Output></UnitTestResult></Results></TestRun>'
            [IO.File]::WriteAllText($trxPath, $trx)
            $verdict = [pscustomobject]@{ ExitCode = 0; coverageComplete = $true; testsPassed = $true }
            [void](Complete-TestPhaseLaunch -Verdict $verdict -LogRoot $work -TrxPath $trxPath -Suite $suite -Chunk 'all-group-1' -RunId 'run-1' -Attempt '1' -HostWallSeconds 1)
        }
        Write-RcCase 'antiphon' 'antiphon-case'
        Write-RcCase 'e2e' 'e2e-case'
        Invoke-TestPhaseRunSummary -RunDirectory $work -Top 20
        $timing = Join-Path $work 'timing'
        function Read-Uids([string]$name) {
            $path = Join-Path $timing $name
            if (-not (Test-Path -LiteralPath $path)) { return @() }
            $doc = [IO.File]::ReadAllText($path) | ConvertFrom-Json
            $ids = New-Object System.Collections.Generic.List[string]
            foreach ($group in @($doc.groups)) {
                foreach ($id in @($group.plannedTestIds)) { $ids.Add([string]$id) }
            }
            return @($ids.ToArray())
        }
        $indexCount = 0
        $indexPath = Join-Path $timing 'groups-planned-index.json'
        if (Test-Path -LiteralPath $indexPath) {
            $index = [IO.File]::ReadAllText($indexPath) | ConvertFrom-Json
            if ($index.PSObject.Properties.Name -contains 'suites') { $indexCount = @($index.suites).Count }
        }
        $rows = New-Object System.Collections.Generic.List[object]
        $summaryPath = Join-Path $timing 'summary.json'
        if (Test-Path -LiteralPath $summaryPath) {
            $summary = [IO.File]::ReadAllText($summaryPath) | ConvertFrom-Json
            foreach ($test in @($summary.tests)) {
                $suite = ''
                if ($test.PSObject.Properties.Name -contains 'suite') { $suite = [string]$test.suite }
                $rows.Add([ordered]@{ testId = [string]$test.testId; suite = $suite })
            }
        }
        $out = [ordered]@{
            exitCode = 0
            text = ''
            indexSuites = [int]$indexCount
            antiphonUids = @(Read-Uids 'groups-planned-antiphon.json')
            e2eUids = @(Read-Uids 'groups-planned-e2e.json')
            rows = @($rows.ToArray())
        }
        [IO.File]::WriteAllText((Join-Path $work 'observation.json'), ($out | ConvertTo-Json -Compress -Depth 6))
        """;

    private const string HostScript = """
        $ErrorActionPreference = 'Stop'
        $repo = $args[0]
        $work = $args[1]
        $path = Join-Path $repo 'scripts/lib/qualification-host-stats.ps1'
        $sampleCount = 0
        $firstCpu = $null
        $secondCpu = $null
        $unavailable = 0
        $readsAfter = 99
        $matrix = [ordered]@{ desktop = 'missing'; net8 = 'missing'; net9 = 'missing' }
        $desktopLoader = $true
        $net8Loader = $true
        $desktopAdmission = $true
        $net8Admission = $true
        $desktopDiagnostic = $false
        if (Test-Path -LiteralPath $path) {
            . $path
            $script:RcHostStopped = $false
            $script:RcHostReads = 0
            $script:RcHostReadsAfter = 0
            $probe = {
                if ($script:RcHostStopped) { $script:RcHostReadsAfter++; return $null }
                $script:RcHostReads++
                if ($script:RcHostReads -eq 1) {
                    return [pscustomobject]@{ at = '2026-10-29T00:00:00.0000000Z'; cpuPercent = $null; cores = 4; memoryAvailableBytes = 10; memoryCommittedBytes = $null; diskQueue = $null }
                }
                return [pscustomobject]@{ at = '2026-10-29T00:00:30.0000000Z'; cpuPercent = 5; cores = 4; memoryAvailableBytes = $null; memoryCommittedBytes = $null; diskQueue = $null }
            }
            $sampled = Invoke-QualificationHostSampler -Probe $probe -SampleCount 2 -OwnerCompleted { $script:RcHostStopped = $true }
            $sampleCount = @($sampled.samples).Count
            if ($sampleCount -ge 1) { $firstCpu = $sampled.samples[0].cpuPercent }
            if ($sampleCount -ge 2) {
                $secondCpu = $sampled.samples[1].cpuPercent
                $unavailable = $sampled.samples[1].memoryAvailableBytes
            }
            $readsAfter = [int]$script:RcHostReadsAfter
            function Invoke-RcMatrix([string]$edition, [string]$framework) {
                $called = $false
                $result = Test-QualificationHostRuntime -Edition $edition -FrameworkDescription $framework -Loader { $called = $true; return 'loaded' }
                return [pscustomobject]@{ decision = [string]$result.decision; loaderCalled = [bool]$called; admission = [bool]$result.measurementAdmission; diagnostic = [bool]$result.diagnosticComplete }
            }
            $desktop = Invoke-RcMatrix 'Desktop' '.NET Framework 4.8.1'
            $net8 = Invoke-RcMatrix 'Core' '.NET 8.0.11'
            $net9 = Invoke-RcMatrix 'Core' '.NET 9.0.8'
            $matrix.desktop = $desktop.decision
            $matrix.net8 = $net8.decision
            $matrix.net9 = $net9.decision
            $desktopLoader = $desktop.loaderCalled
            $net8Loader = $net8.loaderCalled
            $desktopAdmission = $desktop.admission
            $net8Admission = $net8.admission
            $desktopDiagnostic = $desktop.diagnostic
        }
        $out = [ordered]@{
            exitCode = 0
            text = ''
            sampleCount = [int]$sampleCount
            firstCpu = $firstCpu
            secondCpu = $secondCpu
            unavailableMemory = $unavailable
            readsAfterStop = [int]$readsAfter
            matrix = $matrix
            desktopLoaderCalled = [bool]$desktopLoader
            net8LoaderCalled = [bool]$net8Loader
            desktopAdmission = [bool]$desktopAdmission
            net8Admission = [bool]$net8Admission
            desktopDiagnosticComplete = [bool]$desktopDiagnostic
        }
        [IO.File]::WriteAllText((Join-Path $work 'observation.json'), ($out | ConvertTo-Json -Compress -Depth 6))
        """;

    private const string DiscoveryScript = """
        $ErrorActionPreference = 'Stop'
        $repo = $args[0]
        $work = $args[1]
        $lib = Join-Path $repo 'scripts/lib'
        . (Join-Path $lib 'nightly-common.ps1')
        . (Join-Path $lib 'test-phase-evidence.ps1')
        . (Join-Path $lib 'nightly-tests-impl.ps1')
        $copied = $true
        $missingChunk = 21.377
        $markedChunk = 21.377
        $supplemental = 21.377
        $suite = 21.377
        if (Get-Command Resolve-NightlyChunkDiscoverySeconds -ErrorAction SilentlyContinue) {
            $missing = Resolve-NightlyChunkDiscoverySeconds -SuiteDiscoverySeconds 21.377 -ChunkMarkerSeconds $null
            $marked = Resolve-NightlyChunkDiscoverySeconds -SuiteDiscoverySeconds 21.377 -ChunkMarkerSeconds 1.5
            $extra = Resolve-NightlyChunkDiscoverySeconds -SuiteDiscoverySeconds 21.377 -ChunkMarkerSeconds 1.25 -SupplementalLaunch $true
            $copied = [bool]$missing.copiedFromSuite
            $missingChunk = $missing.chunkDiscoverySeconds
            $markedChunk = $marked.chunkDiscoverySeconds
            $supplemental = $extra.chunkDiscoverySeconds
            $suite = $missing.suiteDiscoverySeconds
        }
        $out = [ordered]@{
            exitCode = 0
            text = ''
            copiedFromSuite = [bool]$copied
            missingChunk = $missingChunk
            markedChunk = $markedChunk
            supplementalChunk = $supplemental
            suiteDiscoverySeconds = $suite
        }
        [IO.File]::WriteAllText((Join-Path $work 'observation.json'), ($out | ConvertTo-Json -Compress -Depth 5))
        """;

    private const string SetupScript = """
        $ErrorActionPreference = 'Stop'
        $repo = $args[0]
        $work = $args[1]
        $lib = Join-Path $repo 'scripts/lib'
        . (Join-Path $lib 'nightly-common.ps1')
        . (Join-Path $lib 'test-phase-evidence.ps1')
        $countedOnce = $false
        $setup = 12.0
        $sum = 12.0
        $overlapped = $true
        if (Get-Command Write-QualificationRunPhase -ErrorAction SilentlyContinue) {
            Write-QualificationRunPhase -LogRoot $work -Name 'planning' -StartedAt '2026-10-29T00:00:00.0000000Z' -EndedAt '2026-10-29T00:00:03.0000000Z' -Seconds 3
            Write-QualificationRunPhase -LogRoot $work -Name 'build' -StartedAt '2026-10-29T00:00:03.0000000Z' -EndedAt '2026-10-29T00:00:09.0000000Z' -Seconds 6
            Update-QualificationChunkTiming -LogRoot $work -TimingRoot (Join-Path $work 'timing') -Suite 'antiphon' -Chunk 'a' -LastPhase 'run' -BuildSeconds 6 -RunId 'run-1' -Attempt '1'
            Update-QualificationChunkTiming -LogRoot $work -TimingRoot (Join-Path $work 'timing') -Suite 'antiphon' -Chunk 'b' -LastPhase 'run' -BuildSeconds 6 -RunId 'run-1' -Attempt '1'
            $acct = Get-QualificationSetupAccounting -LogRoot $work
            $countedOnce = [bool]$acct.countedOnce
            $setup = [double]$acct.setupSeconds
            $sum = [double]$acct.perChunkBuildSum
            $overlapped = [bool]$acct.overlapped
        }
        $out = [ordered]@{
            exitCode = 0
            text = ''
            countedOnce = [bool]$countedOnce
            setupSeconds = [double]$setup
            perChunkBuildSum = [double]$sum
            overlapped = [bool]$overlapped
        }
        [IO.File]::WriteAllText((Join-Path $work 'observation.json'), ($out | ConvertTo-Json -Compress -Depth 4))
        """;

    private const string CountsScript = """
        $ErrorActionPreference = 'Stop'
        $repo = $args[0]
        $work = $args[1]
        $lib = Join-Path $repo 'scripts/lib'
        . (Join-Path $lib 'nightly-common.ps1')
        . (Join-Path $lib 'test-phase-evidence.ps1')
        $timing = Join-Path $work 'timing'
        New-Item -ItemType Directory -Path $timing -Force | Out-Null
        $header = '{"recordKind":"header","version":1,"pid":1,"nonce":"n1","frequencyHz":10000000,"epochUtc":"2026-10-29T00:00:00.0000000Z","runId":"run-1","groupId":"all-group-1","attempt":"1","suite":"SUITE","assembly":"Antiphon.Tests"}'
        function Complete-Line([string]$id, [string]$outcome) {
            return '{"recordKind":"complete","testId":"' + $id + '","className":"C","method":"M","args":[],"startOffsetMs":0,"endOffsetMs":1,"outcome":"' + $outcome + '","phases":[],"processes":[],"exclusive":{"db":4,"host":0,"process":0,"fs":0,"network":0,"fixture":0},"overlapMs":0,"remainderMs":0,"childUnionMs":0,"droppedPhaseCount":0,"shared":false,"incomplete":false}'
        }
        $sb = New-Object System.Text.StringBuilder
        [void]$sb.Append(($header.Replace('SUITE', 'antiphon') + "`n"))
        [void]$sb.Append((Complete-Line 'early-case' 'Passed') + "`n")
        [void]$sb.Append((Complete-Line 'shared-case' 'Passed') + "`n")
        $pad = Complete-Line 'pad-case' 'Passed'
        while ($sb.Length -lt 270000) { [void]$sb.Append($pad + "`n") }
        [void]$sb.Append((Complete-Line 'late-case' 'Passed') + "`n")
        [void]$sb.Append('{"recordKind":"comp')
        [IO.File]::WriteAllText((Join-Path $timing 'cases-antiphon-n1.jsonl'), $sb.ToString())
        $e2e = $header.Replace('SUITE', 'e2e') + "`n" + (Complete-Line 'shared-case' 'Failed') + "`n"
        [IO.File]::WriteAllText((Join-Path $timing 'cases-e2e-n1.jsonl'), $e2e)
        function Write-Trx([string]$name, [string]$body) {
            $xml = '<?xml version="1.0" encoding="utf-8"?><TestRun><Results>' + $body + '</Results></TestRun>'
            [IO.File]::WriteAllText((Join-Path $work $name), $xml)
        }
        function Row([string]$id, [string]$outcome, [string]$stdout) {
            return '<UnitTestResult testId="' + $id + '" testName="' + $id + '" outcome="' + $outcome + '"><Output><StdOut>' + $stdout + '</StdOut></Output></UnitTestResult>'
        }
        Write-Trx 'antiphon.trx' ((Row 'early-guid' 'Passed' 'C1514_NATIVE body early-case') + (Row 'shared-guid' 'Passed' 'C1514_NATIVE body shared-case') + (Row 'late-guid' 'Passed' 'C1514_NATIVE body late-case') + (Row 'dup-a' 'Passed' 'C1514_NATIVE body shared-case') + (Row 'dup-b' 'Failed' 'C1514_NATIVE one shared-case' + "`n" + 'C1514_NATIVE two shared-case'))
        Write-Trx 'e2e.trx' (Row 'e2e-guid' 'Failed' 'C1514_NATIVE body shared-case')
        Write-Trx 'orphan.trx' (Row 'orphan-guid' 'Passed' 'no-phase')
        $antiphonVerdict = [pscustomobject]@{ ExitCode = 0; coverageComplete = $true; testsPassed = $true }
        $e2eVerdict = [pscustomobject]@{ ExitCode = 1; coverageComplete = $true; testsPassed = $false }
        $orphanVerdict = [pscustomobject]@{ ExitCode = 0; coverageComplete = $true; testsPassed = $true }
        [void](Write-TestPhaseExecutedManifest -Verdict $antiphonVerdict -LogRoot $work -TrxPath (Join-Path $work 'antiphon.trx') -Suite 'antiphon' -Chunk 'all-group-1' -RunId 'run-1' -Attempt '1' -HostWallSeconds 1)
        [void](Write-TestPhaseExecutedManifest -Verdict $e2eVerdict -LogRoot $work -TrxPath (Join-Path $work 'e2e.trx') -Suite 'e2e' -Chunk 'all-group-1' -RunId 'run-1' -Attempt '1' -HostWallSeconds 1)
        [void](Write-TestPhaseExecutedManifest -Verdict $orphanVerdict -LogRoot $work -TrxPath (Join-Path $work 'orphan.trx') -Suite 'scripts' -Chunk 'all-group-1' -RunId 'run-1' -Attempt '1' -HostWallSeconds 1)
        [IO.File]::WriteAllText((Join-Path $timing 'groups-planned.json'), '{"version":1,"runId":"run-1","attempt":"1","suite":"antiphon","assembly":"Antiphon.Tests","groups":[]}')
        Invoke-TestPhaseRunSummary -RunDirectory $work -Top 20
        $summary = [IO.File]::ReadAllText((Join-Path $timing 'summary.json')) | ConvertFrom-Json
        $early = $false
        $late = $false
        $shared = New-Object System.Collections.Generic.List[string]
        $orphanOutcome = ''
        $orphanIncomplete = $false
        $orphanKnown = $true
        $duplicateJoined = $false
        foreach ($test in @($summary.tests)) {
            $id = [string]$test.testId
            $suite = ''
            if ($test.PSObject.Properties.Name -contains 'suite') { $suite = [string]$test.suite }
            if ($id -eq 'early-case') { $early = $true }
            if ($id -eq 'late-case') { $late = $true }
            if ($id -eq 'shared-case') { $shared.Add($suite) }
            if ($id -eq 'orphan-guid') {
                $orphanOutcome = [string]$test.outcome
                $orphanIncomplete = [bool]$test.incomplete
                if ($test.PSObject.Properties.Name -contains 'countsKnown') { $orphanKnown = [bool]$test.countsKnown }
            }
            if ($id -eq 'shared-case' -and [string]$test.outcome -eq 'Passed' -and -not [bool]$test.incomplete) { $duplicateJoined = $true }
        }
        $quality = @($summary.quality | ForEach-Object { [string]$_ })
        $torn = ($quality -contains 'torn-tail') -or ([int]$summary.tornLines -gt 0)
        $out = [ordered]@{
            exitCode = 0
            text = ''
            earlyPresent = [bool]$early
            latePresent = [bool]$late
            torn = [bool]$torn
            complete = [bool]$summary.complete
            sharedCount = [int]$shared.Count
            sharedSuites = @($shared.ToArray())
            orphanOutcome = [string]$orphanOutcome
            orphanIncomplete = [bool]$orphanIncomplete
            orphanCountsKnown = [bool]$orphanKnown
            duplicateJoined = [bool]$duplicateJoined
        }
        [IO.File]::WriteAllText((Join-Path $work 'observation.json'), ($out | ConvertTo-Json -Compress -Depth 5))
        """;
}
