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
        observed.GetProperty("blankImage").GetString().ShouldBe("unknown");
        observed.GetProperty("missedImage").GetString().ShouldBe("unknown", "missed pid was left as a live image");
        observed.GetProperty("missedOnlyImage").GetString().ShouldBe("unknown", "pid that exited before capture was dropped");
        observed.GetProperty("missedContainsLive").GetBoolean().ShouldBeTrue();
        observed.GetProperty("missedContainsBlank").GetBoolean().ShouldBeFalse("blank image is not a missed pid");
        observed.GetProperty("jobEmpty").GetBoolean().ShouldBeTrue();
        observed.GetProperty("drainMilliseconds").GetDouble().ShouldBe(7);
        observed.GetProperty("executionMilliseconds").GetDouble().ShouldBe(20);
        observed.GetProperty("launchMilliseconds").GetDouble().ShouldBe(10);
        observed.GetProperty("cleanupMilliseconds").GetDouble().ShouldBe(4);
        observed.GetProperty("childrenExited").GetBoolean().ShouldBeTrue();
        observed.GetProperty("outputDrained").GetBoolean().ShouldBeTrue();
        observed.GetProperty("remainingProcessCount").GetInt32().ShouldBe(0);
        observed.GetProperty("rootExitedAtUtc").GetString().ShouldBe("2026-10-29T00:00:02.0000000Z");
        observed.GetProperty("rootStartedAtUtc").GetString().ShouldBe("2026-10-29T00:00:00.0000000Z");
        observed.GetProperty("runId").GetString().ShouldBe("run-1");
        observed.GetProperty("attempt").GetString().ShouldBe("1");
        observed.GetProperty("attempt2Preserved").GetBoolean().ShouldBeTrue("rerun overwrote the first owner observation");
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
        observed.GetProperty("antiphonAssembly").GetString().ShouldBe("Antiphon.Tests");
        observed.GetProperty("e2eAssembly").GetString().ShouldBe("Antiphon.E2E");
        observed.GetProperty("antiphonSource").GetString().ShouldBe("src", "index dropped source digest");
        observed.GetProperty("e2eSource").GetString().ShouldBe("src", "index dropped source digest");
        observed.GetProperty("stale").GetBoolean().ShouldBeFalse("stale-input-not-joined");
        observed.GetProperty("antiphonJoined").GetBoolean().ShouldBeTrue("cross-suite plan was stale");
        observed.GetProperty("e2eJoined").GetBoolean().ShouldBeTrue("cross-suite plan was stale");
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
        observed.GetProperty("loaderWrapped").GetBoolean().ShouldBeTrue("loader fault escaped");
        observed.GetProperty("productionUnwired").GetBoolean().ShouldBeTrue("production still loads the host-stats probe");
    }

    [Test]
    [Timeout(120_000)]
    public async Task ProductionCallSitesPersistDiscoveryAndSetup(CancellationToken cancellationToken)
    {
        var root = FreshRoot();
        var observed = await Run(CallSiteScript, cancellationToken, root);
        observed.GetProperty("exitCode").GetInt32().ShouldBe(0, observed.GetProperty("text").GetString());
        observed.GetProperty("discoveryNull").GetBoolean().ShouldBeTrue("call site copied the suite constant");
        observed.GetProperty("setupNpmCi").GetBoolean().ShouldBeTrue("npm-ci setup phase was not written");
        observed.GetProperty("setupNpmBuild").GetBoolean().ShouldBeTrue("npm-build setup phase was not written");
        observed.GetProperty("setupLint").GetBoolean().ShouldBeTrue("lint setup phase was not written");
        observed.GetProperty("setupDotnet").GetBoolean().ShouldBeTrue("dotnet-build setup phase was not written");
        observed.GetProperty("planningPresent").GetBoolean().ShouldBeTrue("planning-<suite> was not written");
        observed.GetProperty("planningSeconds").GetDouble().ShouldBeGreaterThan(0, "planning-<suite> seconds were zero");
        observed.GetProperty("supplementalIsMarker").GetBoolean().ShouldBeTrue("supplemental discovery was not the marker");
    }

    [Test]
    [Timeout(60_000)]
    public async Task PhaseRootAbsentEmptyAndSet(CancellationToken cancellationToken)
    {
        var root = FreshRoot();
        var observed = await Run(PhaseRootScript, cancellationToken, root);
        observed.GetProperty("exitCode").GetInt32().ShouldBe(0, observed.GetProperty("text").GetString());
        observed.GetProperty("absentPresent").GetBoolean().ShouldBeFalse("absent phase root was copied");
        observed.GetProperty("emptyPresent").GetBoolean().ShouldBeFalse("empty phase root was copied");
        observed.GetProperty("setCopied").GetBoolean().ShouldBeTrue("set phase root was not copied");
    }

    [Test]
    [Timeout(60_000)]
    public async Task DiscoveryIsNotCopiedFromSuite(CancellationToken cancellationToken)
    {
        var root = FreshRoot();
        var observed = await Run(DiscoveryScript, cancellationToken, root);
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
        observed.GetProperty("perChunkBuildSum").GetDouble().ShouldBe(6, "build was summed once per chunk");
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
        observed.GetProperty("summaryAggregationBudgetMilliseconds").GetInt32().ShouldBe(5000, "aggregate re-read budget");
        observed.GetProperty("summaryAggregationMilliseconds").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
        observed.GetProperty("summaryAggregationMilliseconds").GetInt32().ShouldBeLessThanOrEqualTo(5000);
    }

    [Test]
    [Timeout(90_000)]
    public async Task SummaryReReadSharesOneBudget(CancellationToken cancellationToken)
    {
        var root = FreshRoot();
        var observed = await Run(BudgetScript, cancellationToken, root);
        observed.GetProperty("exitCode").GetInt32().ShouldBe(0, observed.GetProperty("text").GetString());
        observed.GetProperty("firstLate").GetBoolean().ShouldBeTrue("first over-budget shard was not re-read");
        observed.GetProperty("secondLate").GetBoolean().ShouldBeFalse("second shard was re-read after the shared budget was exhausted");
        observed.GetProperty("summaryAggregationBudgetMilliseconds").GetInt32().ShouldBe(5000, "aggregate re-read budget");
        var spent = observed.GetProperty("summaryAggregationMilliseconds").GetInt32();
        spent.ShouldBeGreaterThan(0, "aggregate re-read recorded no time");
        spent.ShouldBeLessThanOrEqualTo(5000, "aggregate re-read exceeded the shared budget");
    }

    [Test]
    [Timeout(60_000)]
    public async Task NativeOwnerExitWitnessIsUnconditional(CancellationToken cancellationToken)
    {
        var root = FreshRoot();
        var observed = await Run(OwnerWitnessShapeScript, cancellationToken, root);
        observed.GetProperty("exitCode").GetInt32().ShouldBe(0, observed.GetProperty("text").GetString());
        observed.GetProperty("declared").GetBoolean().ShouldBeTrue("Invoke-RcWindowsOwnerExitWitness was not declared");
        observed.GetProperty("assertsImages").GetBoolean().ShouldBeTrue("DescendantImages was not asserted");
        observed.GetProperty("assertsRootExit").GetBoolean().ShouldBeTrue("RootExitedAtUtc was not asserted");
        observed.GetProperty("assertsMissed").GetBoolean().ShouldBeTrue("MissedDescendantPids was not asserted");
        observed.GetProperty("caseCalls").GetBoolean().ShouldBeTrue("C1039_DescendantExit does not call the witness");
        observed.GetProperty("caseUnconditional").GetBoolean().ShouldBeTrue("owner witness is still behind the packaged PowerShell condition");
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
        $problems = New-Object System.Collections.Generic.List[string]
        $persisted = $false
        $image = ''
        $blankImage = ''
        $missedImage = ''
        $missedOnlyImage = ''
        $missedContainsLive = $false
        $missedContainsBlank = $false
        $jobEmpty = $false
        $drain = 0.0
        $execution = 0.0
        $launch = 0.0
        $cleanup = 0.0
        $childrenExited = $false
        $outputDrained = $false
        $remaining = -1
        $exited = ''
        $rootStarted = ''
        $runId = ''
        $attempt = ''
        $attempt2Preserved = $false
        try {
            $lib = Join-Path $repo 'scripts/lib'
            . (Join-Path $lib 'nightly-common.ps1')
            . (Join-Path $lib 'test-phase-evidence.ps1')
            . (Join-Path $lib 'nightly-tests-impl.ps1')
            if (-not (Get-Command Publish-NightlyChunkObservation -ErrorAction SilentlyContinue)) {
                throw 'production Publish-NightlyChunkObservation is missing'
            }
            function New-OwnerRun([string]$retainedImage) {
                return [pscustomobject]@{
                    ExitCode = 0
                    RootStartedAtUtc = '2026-10-29T00:00:00.0000000Z'
                    RootExitedAtUtc = '2026-10-29T00:00:02.0000000Z'
                    JobEmptyObserved = $true
                    RemainingProcessCount = 0
                    LaunchMilliseconds = 10
                    ExecutionMilliseconds = 20
                    CleanupMilliseconds = 4
                    DrainMilliseconds = 7
                    ChildrenExited = $true
                    OutputDrained = $true
                    MissedDescendantPids = @(45, 46)
                    DescendantIdentities = @(
                        '43:2026-10-29T00:00:00.1000000Z',
                        '44:2026-10-29T00:00:00.2000000Z',
                        '45:2026-10-29T00:00:00.3000000Z'
                    )
                    DescendantImages = @($retainedImage, '', 'LiveName')
                    DescendantExitedAtUtc = @('2026-10-29T00:00:01.5000000Z', '', '')
                }
            }
            [void](Publish-NightlyChunkObservation -Run (New-OwnerRun 'OwnedChild.exe') -LogRoot $work -TrxPath (Join-Path $work 'missing.trx') -Suite 'antiphon' -Chunk 'all-group-1' -RunId 'run-1' -Attempt '1' -HostWallSeconds 1 -CoverageComplete $true -TestsPassed $true)
            [void](Publish-NightlyChunkObservation -Run (New-OwnerRun 'Other.exe') -LogRoot $work -TrxPath (Join-Path $work 'missing.trx') -Suite 'antiphon' -Chunk 'all-group-1' -RunId 'run-1' -Attempt '2' -HostWallSeconds 1 -CoverageComplete $true -TestsPassed $true)
            $timing = Join-Path $work 'timing'
            $firstPath = Join-Path $timing 'owner-antiphon-all-group-1-run-1-1.json'
            $secondPath = Join-Path $timing 'owner-antiphon-all-group-1-run-1-2.json'
            if (-not (Test-Path -LiteralPath $firstPath)) { throw 'owner sidecar missing' }
            $raw = [IO.File]::ReadAllText($firstPath)
            $doc = $raw | ConvertFrom-Json
            $persisted = $true
            $exitedMatch = [regex]::Match($raw, '"rootExitedAtUtc"\s*:\s*"([^"]*)"')
            if ($exitedMatch.Success) { $exited = $exitedMatch.Groups[1].Value }
            $startedMatch = [regex]::Match($raw, '"rootStartedAtUtc"\s*:\s*"([^"]*)"')
            if ($startedMatch.Success) { $rootStarted = $startedMatch.Groups[1].Value }
            function Read-Text([object]$Obj, [string]$Name) {
                if ($null -eq $Obj -or $Obj.PSObject.Properties.Name -notcontains $Name) { return '' }
                return [string]$Obj.$Name
            }
            $runId = Read-Text $doc 'runId'
            $attempt = Read-Text $doc 'attempt'
            if ($doc.PSObject.Properties.Name -contains 'jobEmpty') { $jobEmpty = [bool]$doc.jobEmpty }
            if ($doc.PSObject.Properties.Name -contains 'drainMilliseconds') { $drain = [double]$doc.drainMilliseconds }
            if ($doc.PSObject.Properties.Name -contains 'executionMilliseconds') { $execution = [double]$doc.executionMilliseconds }
            if ($doc.PSObject.Properties.Name -contains 'launchMilliseconds') { $launch = [double]$doc.launchMilliseconds }
            if ($doc.PSObject.Properties.Name -contains 'cleanupMilliseconds') { $cleanup = [double]$doc.cleanupMilliseconds }
            if ($doc.PSObject.Properties.Name -contains 'childrenExited') { $childrenExited = [bool]$doc.childrenExited }
            if ($doc.PSObject.Properties.Name -contains 'outputDrained') { $outputDrained = [bool]$doc.outputDrained }
            if ($doc.PSObject.Properties.Name -contains 'remainingProcessCount') { $remaining = [int]$doc.remainingProcessCount }
            $missedIds = @()
            if ($doc.PSObject.Properties.Name -contains 'missedDescendantPids') { $missedIds = @($doc.missedDescendantPids) }
            foreach ($id in $missedIds) {
                if ([int]$id -eq 45) { $missedContainsLive = $true }
                if ([int]$id -eq 44) { $missedContainsBlank = $true }
                if ([int]$id -eq 46) { $missedOnlySeen = $true }
            }
            if ($doc.PSObject.Properties.Name -contains 'descendants') {
                foreach ($row in @($doc.descendants)) {
                    $rowPid = 0
                    if ($row.PSObject.Properties.Name -contains 'pid') { $rowPid = [int]$row.pid }
                    $rowImage = ''
                    if ($row.PSObject.Properties.Name -contains 'image') { $rowImage = [string]$row.image }
                    if ($rowPid -eq 43) { $image = $rowImage }
                    if ($rowPid -eq 44) { $blankImage = $rowImage }
                    if ($rowPid -eq 45) { $missedImage = $rowImage }
                    if ($rowPid -eq 46) { $missedOnlyImage = $rowImage }
                }
            }
            if ((Test-Path -LiteralPath $secondPath)) {
                $second = [IO.File]::ReadAllText($secondPath) | ConvertFrom-Json
                $secondImage = ''
                if ($second.PSObject.Properties.Name -contains 'descendants') {
                    foreach ($row in @($second.descendants)) {
                        if ([int]$row.pid -eq 43) { $secondImage = [string]$row.image }
                    }
                }
                if ($image -eq 'OwnedChild.exe' -and $secondImage -eq 'Other.exe') { $attempt2Preserved = $true }
            }
        } catch {
            $problems.Add([string]$_.Exception.Message)
        }
        if (-not $persisted) { $problems.Add('owner-not-persisted') }
        if ($image -ne 'OwnedChild.exe') { $problems.Add('retained-image-missing') }
        if ($blankImage -ne 'unknown') { $problems.Add('blank-image-not-unknown') }
        if ($missedImage -ne 'unknown') { $problems.Add('missed-live-image-not-unknown') }
        if ($missedOnlyImage -ne 'unknown') { $problems.Add('exited-before-capture-dropped') }
        if (-not $missedContainsLive) { $problems.Add('missed-list-dropped-live') }
        if ($missedContainsBlank) { $problems.Add('blank-image-marked-missed') }
        if ($rootStarted -ne '2026-10-29T00:00:00.0000000Z') { $problems.Add('root-start-missing') }
        if ($runId -ne 'run-1' -or $attempt -ne '1') { $problems.Add('run-attempt-missing') }
        if (-not $attempt2Preserved) { $problems.Add('rerun-overwrote-owner') }
        $exit = 0
        $text = ''
        if ($problems.Count -gt 0) { $exit = 1; $text = ($problems -join '; ') }
        $out = [ordered]@{
            exitCode = [int]$exit
            text = [string]$text
            persisted = [bool]$persisted
            image = [string]$image
            blankImage = [string]$blankImage
            missedImage = [string]$missedImage
            missedOnlyImage = [string]$missedOnlyImage
            missedContainsLive = [bool]$missedContainsLive
            missedContainsBlank = [bool]$missedContainsBlank
            jobEmpty = [bool]$jobEmpty
            drainMilliseconds = [double]$drain
            executionMilliseconds = [double]$execution
            launchMilliseconds = [double]$launch
            cleanupMilliseconds = [double]$cleanup
            childrenExited = [bool]$childrenExited
            outputDrained = [bool]$outputDrained
            remainingProcessCount = [int]$remaining
            rootExitedAtUtc = [string]$exited
            rootStartedAtUtc = [string]$rootStarted
            runId = [string]$runId
            attempt = [string]$attempt
            attempt2Preserved = [bool]$attempt2Preserved
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
            ProfileExclusions = @()
        }
        [void](Write-TestPhasePlannedManifest @common -Suite 'antiphon' -AssemblyId 'Antiphon.Tests' -Groups @(New-RcGroup $antiphonClass) -DiscoveryNodes @(New-RcNode 'uid-antiphon' $antiphonClass))
        [void](Write-TestPhasePlannedManifest @common -Suite 'e2e' -AssemblyId 'Antiphon.E2E' -Groups @(New-RcGroup $e2eClass) -DiscoveryNodes @(New-RcNode 'uid-e2e' $e2eClass))
        function Write-RcCase([string]$suite, [string]$id) {
            $timing = Join-Path $work 'timing'
            $assembly = 'Antiphon.Tests'
            if ($suite -eq 'e2e') { $assembly = 'Antiphon.E2E' }
            $header = '{"recordKind":"header","version":1,"pid":1,"nonce":"n1","frequencyHz":10000000,"epochUtc":"2026-10-29T00:00:00.0000000Z","runId":"run-1","groupId":"all-group-1","attempt":"1","suite":"' + $suite + '","assembly":"' + $assembly + '"}'
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
        $antiphonAssembly = ''
        $e2eAssembly = ''
        $antiphonSource = ''
        $e2eSource = ''
        $indexPath = Join-Path $timing 'groups-planned-index.json'
        if (Test-Path -LiteralPath $indexPath) {
            $index = [IO.File]::ReadAllText($indexPath) | ConvertFrom-Json
            if ($index.PSObject.Properties.Name -contains 'suites') {
                $indexCount = @($index.suites).Count
                foreach ($row in @($index.suites)) {
                    $suiteName = [string]$row.suite
                    $asm = ''
                    $source = ''
                    if ($row.PSObject.Properties.Name -contains 'assembly') { $asm = [string]$row.assembly }
                    if ($row.PSObject.Properties.Name -contains 'sourceDigest') { $source = [string]$row.sourceDigest }
                    if ($suiteName -eq 'antiphon') { $antiphonAssembly = $asm; $antiphonSource = $source }
                    if ($suiteName -eq 'e2e') { $e2eAssembly = $asm; $e2eSource = $source }
                }
            }
        }
        $rows = New-Object System.Collections.Generic.List[object]
        $antiphonJoined = $false
        $e2eJoined = $false
        $stale = $false
        $summaryPath = Join-Path $timing 'summary.json'
        if (Test-Path -LiteralPath $summaryPath) {
            $summary = [IO.File]::ReadAllText($summaryPath) | ConvertFrom-Json
            if ($summary.PSObject.Properties.Name -contains 'quality') {
                foreach ($flag in @($summary.quality)) {
                    if ([string]$flag -eq 'stale-input-not-joined') { $stale = $true }
                }
            }
            foreach ($test in @($summary.tests)) {
                $suite = ''
                if ($test.PSObject.Properties.Name -contains 'suite') { $suite = [string]$test.suite }
                $joined = $false
                if ($test.PSObject.Properties.Name -contains 'joined') { $joined = [bool]$test.joined }
                $rows.Add([ordered]@{ testId = [string]$test.testId; suite = $suite })
                if ([string]$test.testId -eq 'antiphon-case' -and $joined -and $suite -eq 'antiphon') { $antiphonJoined = $true }
                if ([string]$test.testId -eq 'e2e-case' -and $joined -and $suite -eq 'e2e') { $e2eJoined = $true }
            }
        }
        $problems = New-Object System.Collections.Generic.List[string]
        if (-not $antiphonJoined -or $stale) { $problems.Add('cross-suite-stale-not-joined') }
        if (-not $e2eJoined) { $problems.Add('e2e-not-joined') }
        if ($antiphonSource -ne 'src' -or $e2eSource -ne 'src') { $problems.Add('index-digest-missing') }
        if ($antiphonAssembly -ne 'Antiphon.Tests' -or $e2eAssembly -ne 'Antiphon.E2E') { $problems.Add('index-assembly-mismatch') }
        $exit = 0
        $text = ''
        if ($problems.Count -gt 0) { $exit = 1; $text = ($problems -join '; ') }
        $out = [ordered]@{
            exitCode = [int]$exit
            text = [string]$text
            indexSuites = [int]$indexCount
            antiphonUids = @(Read-Uids 'groups-planned-antiphon.json')
            e2eUids = @(Read-Uids 'groups-planned-e2e.json')
            antiphonAssembly = [string]$antiphonAssembly
            e2eAssembly = [string]$e2eAssembly
            antiphonSource = [string]$antiphonSource
            e2eSource = [string]$e2eSource
            stale = [bool]$stale
            antiphonJoined = [bool]$antiphonJoined
            e2eJoined = [bool]$e2eJoined
            rows = @($rows.ToArray())
        }
        [IO.File]::WriteAllText((Join-Path $work 'observation.json'), ($out | ConvertTo-Json -Compress -Depth 6))
        """;

    private const string HostScript = """
        $ErrorActionPreference = 'Stop'
        $repo = $args[0]
        $work = $args[1]
        $lib = Join-Path $repo 'scripts/lib'
        $path = Join-Path $lib 'qualification-host-stats.ps1'
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
        $loaderWrapped = $false
        $productionUnwired = $false
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
                $script:RcMatrixCalled = $false
                $result = Test-QualificationHostRuntime -Edition $edition -FrameworkDescription $framework -Loader { $script:RcMatrixCalled = $true; return 'loaded' }
                return [pscustomobject]@{ decision = [string]$result.decision; loaderCalled = [bool]$script:RcMatrixCalled; admission = [bool]$result.measurementAdmission; diagnostic = [bool]$result.diagnosticComplete }
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
            try {
                $blown = New-QualificationHostStatsProvider -AssemblyPath (Join-Path $work 'probe.dll') -Edition 'Core' -FrameworkDescription '.NET 9.0.8' -Loader { throw 'loader-fault' }
                if ([string]$blown.loadReason -eq 'loader-failed' -and -not [bool]$blown.measurementAdmission) { $loaderWrapped = $true }
            } catch { $loaderWrapped = $false }
            . (Join-Path $lib 'nightly-common.ps1')
            . (Join-Path $lib 'test-phase-evidence.ps1')
            . (Join-Path $lib 'nightly-tests-impl.ps1')
            $implText = [IO.File]::ReadAllText((Join-Path $lib 'nightly-tests-impl.ps1'))
            $productionUnwired = -not (Get-Command Start-NightlyHostSampling -ErrorAction SilentlyContinue)
            if ($implText.Contains('qualification-host-stats.ps1')) { $productionUnwired = $false }
            if ($implText.Contains('Antiphon.SessionRunner.dll')) { $productionUnwired = $false }
            if ($implText.Contains('Add-Type')) { $productionUnwired = $false }
        }
        $problems = New-Object System.Collections.Generic.List[string]
        if (-not $loaderWrapped) { $problems.Add('loader-fault-escaped') }
        if (-not $productionUnwired) { $problems.Add('production-still-wired') }
        $exit = 0
        $text = ''
        if ($problems.Count -gt 0) { $exit = 1; $text = ($problems -join '; ') }
        $out = [ordered]@{
            exitCode = [int]$exit
            text = [string]$text
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
            loaderWrapped = [bool]$loaderWrapped
            productionUnwired = [bool]$productionUnwired
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
        $problems = New-Object System.Collections.Generic.List[string]
        if (Get-Command Resolve-NightlyProductionChunkDiscovery -ErrorAction SilentlyContinue) {
            $missing = Resolve-NightlyProductionChunkDiscovery -SuiteDiscoverySeconds 21.377 -ChunkMarkerSeconds $null
            $marked = Resolve-NightlyProductionChunkDiscovery -SuiteDiscoverySeconds 21.377 -ChunkMarkerSeconds 1.5
            $extra = Resolve-NightlyProductionChunkDiscovery -SuiteDiscoverySeconds 21.377 -ChunkMarkerSeconds 1.25 -SupplementalLaunch
            $copied = [bool]$missing.copiedFromSuite
            $missingChunk = $missing.chunkDiscoverySeconds
            $markedChunk = $marked.chunkDiscoverySeconds
            $supplemental = $extra.chunkDiscoverySeconds
            $suite = $missing.suiteDiscoverySeconds
        } else {
            $problems.Add('production-discovery-writer-missing')
        }
        if ($copied) { $problems.Add('discovery-copied-from-suite') }
        if ($null -ne $missingChunk) { $problems.Add('ordinary-chunk-copied-suite-constant') }
        $exit = 0
        $text = ''
        if ($problems.Count -gt 0) { $exit = 1; $text = ($problems -join '; ') }
        $out = [ordered]@{
            exitCode = [int]$exit
            text = [string]$text
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
        . (Join-Path $lib 'nightly-tests-impl.ps1')
        $exit = 0
        $text = ''
        $problems = New-Object System.Collections.Generic.List[string]
        $countedOnce = $false
        $setup = -1.0
        $sum = -1.0
        $overlapped = $true
        try {
            if (-not (Get-Command Write-NightlyPrerequisitePhases -ErrorAction SilentlyContinue)) {
                $problems.Add('prerequisite-writer-missing')
            } else {
                Write-NightlyPrerequisitePhases -LogRoot $work -NpmCiStarted '2026-10-29T00:00:00.0000000Z' -NpmCiSeconds 1 -NpmBuildStarted '2026-10-29T00:00:01.0000000Z' -NpmBuildSeconds 6 -LintStarted '2026-10-29T00:00:07.0000000Z' -LintSeconds 2 -DotnetStarted '2026-10-29T00:00:09.0000000Z' -DotnetSeconds 0
            }
            Update-QualificationChunkTiming -LogRoot $work -TimingRoot (Join-Path $work 'timing') -Suite 'antiphon' -Chunk 'a' -LastPhase 'run' -BuildSeconds 6 -RunId 'run-1' -Attempt '1'
            Update-QualificationChunkTiming -LogRoot $work -TimingRoot (Join-Path $work 'timing') -Suite 'antiphon' -Chunk 'b' -LastPhase 'run' -BuildSeconds 6 -RunId 'run-1' -Attempt '1'
            $acct = Get-QualificationSetupAccounting -LogRoot $work
            $countedOnce = [bool]$acct.countedOnce
            $setup = [double]$acct.setupSeconds
            $sum = [double]$acct.perChunkBuildSum
            $overlapped = [bool]$acct.overlapped
            if (-not $countedOnce) { $problems.Add('counted-more-than-once') }
            if ([double]$setup -ne 9) { $problems.Add(('setup-seconds=' + [string]$setup)) }
            if ([double]$sum -ne 6) { $problems.Add(('per-chunk-build-sum=' + [string]$sum)) }
            if ($overlapped) { $problems.Add('phases-overlapped') }
        } catch {
            $problems.Add([string]$_.Exception.Message)
        }
        if ($problems.Count -gt 0) { $exit = 1; $text = ($problems -join '; ') }
        $out = [ordered]@{
            exitCode = [int]$exit
            text = [string]$text
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
        $budget = -1
        $spent = -1
        if ($summary.PSObject.Properties.Name -contains 'summaryAggregationBudgetMilliseconds' -and $null -ne $summary.summaryAggregationBudgetMilliseconds) {
            $budget = [int]$summary.summaryAggregationBudgetMilliseconds
        }
        if ($summary.PSObject.Properties.Name -contains 'summaryAggregationMilliseconds' -and $null -ne $summary.summaryAggregationMilliseconds) {
            $spent = [int]$summary.summaryAggregationMilliseconds
        }
        $problems = New-Object System.Collections.Generic.List[string]
        if ($budget -ne 5000) { $problems.Add(('summary-budget=' + [string]$budget)) }
        if ($spent -lt 0 -or $spent -gt $budget) { $problems.Add(('summary-spent=' + [string]$spent)) }
        if (-not $early) { $problems.Add('early-missing') }
        if (-not $late) { $problems.Add('late rows dropped') }
        if (-not $torn) { $problems.Add('torn-missing') }
        if ([bool]$summary.complete) { $problems.Add('torn-marked-complete') }
        if ([int]$shared.Count -ne 2) { $problems.Add(('shared-count=' + [string]$shared.Count)) }
        $suiteNames = @($shared.ToArray() | Sort-Object)
        if (($suiteNames -join ',') -ne 'antiphon,e2e') { $problems.Add(('shared-suites=' + ($suiteNames -join ','))) }
        if ($orphanOutcome -ne 'unknown') { $problems.Add(('orphan-outcome=' + $orphanOutcome)) }
        if (-not $orphanIncomplete) { $problems.Add('orphan-not-incomplete') }
        if ($orphanKnown) { $problems.Add('orphan-counts-known') }
        if ($duplicateJoined) { $problems.Add('duplicate-joined') }
        $exit = 0
        $text = ''
        if ($problems.Count -gt 0) { $exit = 1; $text = ($problems -join '; ') }
        $out = [ordered]@{
            exitCode = [int]$exit
            text = [string]$text
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
            summaryAggregationBudgetMilliseconds = [int]$budget
            summaryAggregationMilliseconds = [int]$spent
        }
        [IO.File]::WriteAllText((Join-Path $work 'observation.json'), ($out | ConvertTo-Json -Compress -Depth 5))
        """;

    private const string BudgetScript = """
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
            return '{"recordKind":"complete","testId":"' + $id + '","className":"C","method":"M","args":[],"startOffsetMs":0,"endOffsetMs":1,"outcome":"' + $outcome + '","phases":[],"processes":[],"exclusive":{"db":1,"host":0,"process":0,"fs":0,"network":0,"fixture":0},"overlapMs":0,"remainderMs":0,"childUnionMs":0,"droppedPhaseCount":0,"shared":false,"incomplete":false}'
        }
        function Write-OverBudgetShard([string]$name, [string]$suite, [string]$earlyId, [string]$lateId) {
            $sb = New-Object System.Text.StringBuilder
            [void]$sb.Append(($header.Replace('SUITE', $suite) + "`n"))
            [void]$sb.Append((Complete-Line $earlyId 'Passed') + "`n")
            $pad = Complete-Line 'pad-case' 'Passed'
            while ($sb.Length -lt 270000) { [void]$sb.Append($pad + "`n") }
            [void]$sb.Append((Complete-Line $lateId 'Passed') + "`n")
            [IO.File]::WriteAllText((Join-Path $timing $name), $sb.ToString())
        }
        Write-OverBudgetShard 'cases-a-budget.jsonl' 'antiphon' 'early-a' 'late-a'
        Write-OverBudgetShard 'cases-b-budget.jsonl' 'e2e' 'early-b' 'late-b'
        $script:TestPhaseSummaryReReadWatch = New-Object psobject
        $script:TestPhaseSummaryReReadWatch | Add-Member NoteProperty ElapsedMilliseconds 0
        $script:TestPhaseSummaryReReadWatch | Add-Member NoteProperty IsRunning $false
        $script:TestPhaseSummaryReReadWatch | Add-Member ScriptMethod Start { $this.IsRunning = $true }
        $script:TestPhaseSummaryReReadWatch | Add-Member ScriptMethod Stop { $this.IsRunning = $false }
        $script:TestPhaseSummaryReReadWatch | Add-Member ScriptMethod OnFullRead { $this.ElapsedMilliseconds = 5000 }
        [IO.File]::WriteAllText((Join-Path $timing 'groups-planned.json'), '{"version":1,"runId":"run-1","attempt":"1","suite":"antiphon","assembly":"Antiphon.Tests","groups":[]}')
        [IO.File]::WriteAllText((Join-Path $timing 'groups-planned-e2e.json'), '{"version":1,"runId":"run-1","attempt":"1","suite":"e2e","assembly":"Antiphon.E2E","groups":[]}')
        Invoke-TestPhaseRunSummary -RunDirectory $work -Top 20
        $summary = [IO.File]::ReadAllText((Join-Path $timing 'summary.json')) | ConvertFrom-Json
        $firstLate = $false
        $secondLate = $false
        foreach ($test in @($summary.tests)) {
            $id = [string]$test.testId
            if ($id -eq 'late-a') { $firstLate = $true }
            if ($id -eq 'late-b') { $secondLate = $true }
        }
        $budget = -1
        $spent = -1
        if ($summary.PSObject.Properties.Name -contains 'summaryAggregationBudgetMilliseconds' -and $null -ne $summary.summaryAggregationBudgetMilliseconds) {
            $budget = [int]$summary.summaryAggregationBudgetMilliseconds
        }
        if ($summary.PSObject.Properties.Name -contains 'summaryAggregationMilliseconds' -and $null -ne $summary.summaryAggregationMilliseconds) {
            $spent = [int]$summary.summaryAggregationMilliseconds
        }
        $problems = New-Object System.Collections.Generic.List[string]
        if (-not $firstLate) { $problems.Add('first-shard-not-reread') }
        if ($secondLate) { $problems.Add('second-shard-reread') }
        if ($spent -le 0) { $problems.Add(('summary-spent-zero=' + [string]$spent)) }
        if ($budget -ne 5000) { $problems.Add(('summary-budget=' + [string]$budget)) }
        if ($spent -gt 5000) { $problems.Add(('summary-spent-over=' + [string]$spent)) }
        $exit = 0
        $text = ''
        if ($problems.Count -gt 0) { $exit = 1; $text = ($problems -join '; ') }
        $out = [ordered]@{
            exitCode = [int]$exit
            text = [string]$text
            firstLate = [bool]$firstLate
            secondLate = [bool]$secondLate
            summaryAggregationBudgetMilliseconds = [int]$budget
            summaryAggregationMilliseconds = [int]$spent
        }
        [IO.File]::WriteAllText((Join-Path $work 'observation.json'), ($out | ConvertTo-Json -Compress -Depth 5))
        """;

    private const string CallSiteScript = """
        $ErrorActionPreference = 'Stop'
        $repo = $args[0]
        $work = $args[1]
        $bin = Join-Path $work 'bin'
        New-Item -ItemType Directory -Path $bin -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $bin 'npm'), "#!/bin/sh`nexit 0`n")
        $env:PATH = $bin + [IO.Path]::PathSeparator + $env:PATH
        $lib = Join-Path $repo 'scripts/lib'
        . (Join-Path $lib 'nightly-common.ps1')
        . (Join-Path $lib 'nightly-policy.ps1')
        . (Join-Path $lib 'nightly-coverage.ps1')
        . (Join-Path $lib 'release-gate.ps1')
        . (Join-Path $lib 'release-selection.ps1')
        . (Join-Path $lib 'nightly-tests-impl.ps1')
        $log = Join-Path $work 'run'
        $timing = Join-Path $work 'timing'
        New-Item -ItemType Directory -Path $log -Force | Out-Null
        New-Item -ItemType Directory -Path $timing -Force | Out-Null
        $seamPath = Join-Path $work 'seams.ps1'
        $seam = @'
        $NightlySeams = @{
            DockerInfo = { return @{ exitCode = 0; version = 'test'; detail = 'seam' } }
            DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
            StartProcess = {
                param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
                return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = '' }
            }
        }
        '@
        [IO.File]::WriteAllText($seamPath, $seam)
        $planningSeamPath = Join-Path $work 'planning-seams.ps1'
        $planningSeam = @'
        $NightlySeams = @{
            DockerInfo = { return @{ exitCode = 0; version = 'test'; detail = 'seam' } }
            DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
            HostPlatform = { return 'linux' }
            StartProcess = {
                param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
                $argv = @($ArgumentList)
                if ($argv -contains '--list-tests') {
                    Start-Sleep -Milliseconds 250
                    return @{ ExitCode = 1; TimedOut = $true; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = '' }
                }
                return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = '' }
            }
        }
        '@
        [IO.File]::WriteAllText($planningSeamPath, $planningSeam)
        $supplementalSeamPath = Join-Path $work 'supplemental-seams.ps1'
        $supplementalSeam = @'
        $NightlySeams = @{
            DockerInfo = { return @{ exitCode = 0; version = 'test'; detail = 'seam' } }
            DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
            HostPlatform = { return 'linux' }
            StartProcess = {
                param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
                $argv = @($ArgumentList)
                if ($argv -contains '--list-tests') {
                    Start-Sleep -Milliseconds 400
                    $diagIndex = [array]::IndexOf($argv, '--diagnostic-output-directory')
                    if ($diagIndex -lt 0 -or ($diagIndex + 1) -ge $argv.Count) { throw 'seam-missing-diagnostic-directory' }
                    $diagDir = [string]$argv[$diagIndex + 1]
                    New-Item -ItemType Directory -Path $diagDir -Force | Out-Null
                    $diag = '{"format":"antiphon-tunit-discovery-v1","tunitVersion":"1.44.0","mtpVersion":"2.2.2","assemblyHash":"seam-timing-hash","nodes":[{"uid":"uid-seam","assembly":"Antiphon.Tests","namespace":"Antiphon.Tests.Scripts","type":"SeamTimingProbeTests","method":"Probe","signature":"","state":"Discovered","excluded":false,"categories":[]}]}'
                    [IO.File]::WriteAllText((Join-Path $diagDir 'log.diag'), $diag)
                    return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = '' }
                }
                if ($argv -contains '--report-trx') {
                    $nameIndex = [array]::IndexOf($argv, '--report-trx-filename')
                    $dirIndex = [array]::IndexOf($argv, '--results-directory')
                    if ($nameIndex -lt 0 -or $dirIndex -lt 0) { throw 'seam-missing-trx' }
                    $trxPath = Join-Path ([string]$argv[$dirIndex + 1]) ([string]$argv[$nameIndex + 1])
                    $trxDir = Split-Path -Parent $trxPath
                    if ($trxDir -and -not (Test-Path -LiteralPath $trxDir)) { New-Item -ItemType Directory -Path $trxDir -Force | Out-Null }
                    $trx = '<?xml version="1.0" encoding="utf-8"?><TestRun><TestDefinitions><UnitTest id="uid-seam" name="Antiphon.Tests.Scripts.SeamTimingProbeTests.Probe"><TestMethod className="Antiphon.Tests.Scripts.SeamTimingProbeTests" name="Probe" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="uid-seam" testName="Antiphon.Tests.Scripts.SeamTimingProbeTests.Probe" outcome="Passed" /></Results></TestRun>'
                    [IO.File]::WriteAllText($trxPath, $trx)
                    return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = '' }
                }
                return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = '' }
            }
        }
        '@
        [IO.File]::WriteAllText($supplementalSeamPath, $supplementalSeam)
        $planningLog = Join-Path $log 'planning'
        $supplementalLog = Join-Path $log 'supplemental'
        New-Item -ItemType Directory -Path $planningLog -Force | Out-Null
        New-Item -ItemType Directory -Path $supplementalLog -Force | Out-Null
        $problems = New-Object System.Collections.Generic.List[string]
        $discoveryNull = $false
        $setupNpmCi = $false
        $setupNpmBuild = $false
        $setupLint = $false
        $setupDotnet = $false
        $planningPresent = $false
        $planningSeconds = 0.0
        $supplementalIsMarker = $false
        try {
            $null = Invoke-AntiphonNightlyTests -RepoRoot $repo -LogRoot $log -TimingRoot $timing -Suites @('scripts') -Profile 'nightly' -Sha '9f00008facfc48e75555c08df59887019b3f8dfe' -RunId 'call-site' -Trigger 'manual' -SeamsPath $seamPath -AllowSharedTree
        } catch {
            $problems.Add('scripts-threw')
        }
        try {
            $null = Invoke-AntiphonNightlyTests -RepoRoot $repo -LogRoot $planningLog -TimingRoot $timing -Suites @('antiphon') -Profile 'rc' -Sha '9f00008facfc48e75555c08df59887019b3f8dfe' -RunId 'planning-site' -Trigger 'manual' -SeamsPath $planningSeamPath -AllowSharedTree
        } catch {
            $problems.Add('planning-threw')
        }
        try {
            $null = Invoke-AntiphonNightlyTests -RepoRoot $repo -LogRoot $supplementalLog -TimingRoot $timing -Suites @('antiphon') -Profile 'nightly' -Sha '9f00008facfc48e75555c08df59887019b3f8dfe' -RunId 'supplemental-site' -Trigger 'manual' -SeamsPath $supplementalSeamPath -AllowSharedTree
        } catch {
            $problems.Add('supplemental-threw')
        }
        $setupPath = Join-Path $timing 'setup-phases.json'
        if (Test-Path -LiteralPath $setupPath) {
            $setup = [IO.File]::ReadAllText($setupPath) | ConvertFrom-Json
            $names = @()
            if ($null -ne $setup.phases) {
                foreach ($row in @($setup.phases)) {
                    $phaseName = [string]$row.name
                    $names += $phaseName
                    if ($phaseName -eq 'planning-antiphon') {
                        $planningPresent = $true
                        if ($null -ne $row.seconds) { $planningSeconds = [double]$row.seconds }
                    }
                }
            }
            $setupNpmCi = $names -contains 'npm-ci'
            $setupNpmBuild = $names -contains 'npm-build'
            $setupLint = $names -contains 'lint'
            $setupDotnet = $names -contains 'dotnet-build'
        }
        $supplementalPath = Join-Path (Join-Path $log 'supplemental') 'antiphon-all.timing.json'
        if (Test-Path -LiteralPath $supplementalPath) {
            $supplementalRaw = [IO.File]::ReadAllText($supplementalPath)
            $supplementalMatch = [regex]::Match($supplementalRaw, '"discoverySeconds":([^,}]+)')
            $supplementalToken = ''
            if ($supplementalMatch.Success) { $supplementalToken = $supplementalMatch.Groups[1].Value.Trim() }
            $supplementalValue = 0.0
            $supplementalParsed = $false
            if ($supplementalToken -ne '' -and $supplementalToken -ne 'null') {
                $supplementalParsed = [double]::TryParse($supplementalToken, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$supplementalValue)
            }
            if ($supplementalParsed -and $supplementalValue -gt 0.2) { $supplementalIsMarker = $true }
        }
        $timingPath = Join-Path $log 'scripts-all.timing.json'
        $token = ''
        if (Test-Path -LiteralPath $timingPath) {
            $raw = [IO.File]::ReadAllText($timingPath)
            $match = [regex]::Match($raw, '"discoverySeconds":([^,}]+)')
            if ($match.Success) { $token = $match.Groups[1].Value.Trim() }
        }
        $discoveryNull = ($token -eq 'null')
        if (-not $discoveryNull) { $problems.Add('discovery-not-null') }
        if (-not $setupNpmCi) { $problems.Add('missing-npm-ci') }
        if (-not $setupNpmBuild) { $problems.Add('missing-npm-build') }
        if (-not $setupLint) { $problems.Add('missing-lint') }
        if (-not $setupDotnet) { $problems.Add('missing-dotnet-build') }
        if (-not $planningPresent) { $problems.Add('planning-suite-missing') }
        if ($planningSeconds -le 0) { $problems.Add('planning-seconds-zero') }
        if (-not $supplementalIsMarker) { $problems.Add('supplemental-not-marker') }
        $exit = 0
        $text = ''
        if ($problems.Count -gt 0) { $exit = 1; $text = ($problems -join '; ') }
        $out = [ordered]@{
            exitCode = [int]$exit
            text = [string]$text
            discoveryNull = [bool]$discoveryNull
            setupNpmCi = [bool]$setupNpmCi
            setupNpmBuild = [bool]$setupNpmBuild
            setupLint = [bool]$setupLint
            setupDotnet = [bool]$setupDotnet
            planningPresent = [bool]$planningPresent
            planningSeconds = [double]$planningSeconds
            supplementalIsMarker = [bool]$supplementalIsMarker
        }
        [IO.File]::WriteAllText((Join-Path $work 'observation.json'), ($out | ConvertTo-Json -Compress -Depth 5))
        """;

    private const string PhaseRootScript = """
        $ErrorActionPreference = 'Stop'
        $repo = $args[0]
        $work = $args[1]
        $lib = Join-Path $repo 'scripts/lib'
        . (Join-Path $lib 'nightly-common.ps1')
        . (Join-Path $lib 'test-phase-evidence.ps1')
        $saved = [Environment]::GetEnvironmentVariable('ANTIPHON_TEST_PHASE_ROOT')
        $problems = New-Object System.Collections.Generic.List[string]
        $absentPresent = $true
        $emptyPresent = $true
        $setCopied = $false
        try {
            [Environment]::SetEnvironmentVariable('ANTIPHON_TEST_PHASE_ROOT', $null)
            $absent = @{}
            $absent['ANTIPHON_TEST_PHASE_ROOT'] = 'stale'
            $null = Enable-TestPhaseExecutionEnvironment -Environment $absent -LogRoot $work -RunId 'run-1' -GroupId 'g' -Attempt '1' -Suite 'antiphon' -Profile 'local' -TimingRoot ''
            $absentPresent = $absent.ContainsKey('ANTIPHON_TEST_PHASE_ROOT')
            [Environment]::SetEnvironmentVariable('ANTIPHON_TEST_PHASE_ROOT', '')
            $empty = @{}
            $empty['ANTIPHON_TEST_PHASE_ROOT'] = 'stale'
            $null = Enable-TestPhaseExecutionEnvironment -Environment $empty -LogRoot $work -RunId 'run-1' -GroupId 'g' -Attempt '1' -Suite 'antiphon' -Profile 'local' -TimingRoot '   '
            $emptyPresent = $empty.ContainsKey('ANTIPHON_TEST_PHASE_ROOT')
            $setRoot = Join-Path $work 'phase-root'
            [Environment]::SetEnvironmentVariable('ANTIPHON_TEST_PHASE_ROOT', $setRoot)
            $set = @{}
            $null = Enable-TestPhaseExecutionEnvironment -Environment $set -LogRoot $work -RunId 'run-1' -GroupId 'g' -Attempt '1' -Suite 'antiphon' -Profile 'local' -TimingRoot ''
            $setValue = ''
            if ($set.ContainsKey('ANTIPHON_TEST_PHASE_ROOT')) { $setValue = [string]$set['ANTIPHON_TEST_PHASE_ROOT'] }
            $setCopied = ($setValue -eq $setRoot) -and ([string]$set['ANTIPHON_TEST_PHASE_RUN_ID'] -eq 'run-1')
        } catch {
            $problems.Add('phase-root-threw')
        } finally {
            [Environment]::SetEnvironmentVariable('ANTIPHON_TEST_PHASE_ROOT', $saved)
        }
        if ($absentPresent) { $problems.Add('absent-key-copied') }
        if ($emptyPresent) { $problems.Add('empty-key-copied') }
        if (-not $setCopied) { $problems.Add('set-value-not-copied') }
        $exit = 0
        $text = ''
        if ($problems.Count -gt 0) { $exit = 1; $text = ($problems -join '; ') }
        $out = [ordered]@{
            exitCode = [int]$exit
            text = [string]$text
            absentPresent = [bool]$absentPresent
            emptyPresent = [bool]$emptyPresent
            setCopied = [bool]$setCopied
        }
        [IO.File]::WriteAllText((Join-Path $work 'observation.json'), ($out | ConvertTo-Json -Compress -Depth 5))
        """;

    private const string OwnerWitnessShapeScript = """
        $ErrorActionPreference = 'Stop'
        $repo = $args[0]
        $work = $args[1]
        $problems = New-Object System.Collections.Generic.List[string]
        $declared = $false
        $assertsImages = $false
        $assertsRootExit = $false
        $assertsMissed = $false
        $caseCalls = $false
        $caseUnconditional = $false
        function Get-BraceBody([string]$Text, [int]$OpenIndex) {
            $depth = 0
            for ($j = $OpenIndex; $j -lt $Text.Length; $j++) {
                $ch = $Text[$j]
                if ($ch -eq '{') { $depth++ }
                elseif ($ch -eq '}') {
                    $depth--
                    if ($depth -eq 0) { return $Text.Substring($OpenIndex, ($j - $OpenIndex) + 1) }
                }
            }
            return $null
        }
        function Strip-LineComments([string]$Text) {
            $lines = $Text -split "`n"
            $kept = New-Object System.Collections.Generic.List[string]
            foreach ($line in $lines) {
                $mark = $line.IndexOf('#')
                if ($mark -ge 0) { $line = $line.Substring(0, $mark) }
                $kept.Add($line)
            }
            return ($kept -join "`n")
        }
        try {
            $witnessPath = Join-Path $repo 'scripts/test-rc-speed-os-witness.ps1'
            $nativePath = Join-Path $repo 'scripts/test-nightly-native.ps1'
            $witness = [IO.File]::ReadAllText($witnessPath)
            $native = [IO.File]::ReadAllText($nativePath)
            $fn = [regex]::Match($witness, '(?m)^function\s+Invoke-RcWindowsOwnerExitWitness\s*\{')
            if (-not $fn.Success) { $problems.Add('function-missing') }
            else {
                $body = Get-BraceBody $witness ($fn.Index + $fn.Length - 1)
                if ([string]::IsNullOrWhiteSpace($body)) { $problems.Add('function-body-missing') }
                else {
                    $code = Strip-LineComments $body
                    $declared = $code.Contains('[Antiphon.Nightly.NativeProcessOwner]::Start')
                    $assertsImages = $code.Contains('DescendantImages')
                    $assertsRootExit = $code.Contains('RootExitedAtUtc')
                    $assertsMissed = $code.Contains('MissedDescendantPids')
                    if (-not $declared) { $problems.Add('start-missing') }
                    if (-not $assertsImages) { $problems.Add('images-missing') }
                    if (-not $assertsRootExit) { $problems.Add('root-exit-missing') }
                    if (-not $assertsMissed) { $problems.Add('missed-pids-missing') }
                }
            }
            $caseAt = $native.IndexOf("'C1039_DescendantExit'")
            if ($caseAt -lt 0) { $problems.Add('case-missing') }
            else {
                $nextAt = $native.IndexOf("'C1039_", $caseAt + 1)
                if ($nextAt -lt 0) { $nextAt = $native.Length }
                $section = $native.Substring($caseAt, $nextAt - $caseAt)
                $caseCalls = $section.Contains('Invoke-RcWindowsOwnerExitWitness')
                $caseUnconditional = $caseCalls -and (-not $section.Contains('$packaged.Count'))
                if (-not $caseCalls) { $problems.Add('case-does-not-call-witness') }
                if ($section.Contains('$packaged.Count')) { $problems.Add('case-still-conditional') }
            }
        } catch {
            $problems.Add('shape-threw')
        }
        $exit = 0
        $text = ''
        if ($problems.Count -gt 0) { $exit = 1; $text = ($problems -join '; ') }
        $out = [ordered]@{
            exitCode = [int]$exit
            text = [string]$text
            declared = [bool]$declared
            assertsImages = [bool]$assertsImages
            assertsRootExit = [bool]$assertsRootExit
            assertsMissed = [bool]$assertsMissed
            caseCalls = [bool]$caseCalls
            caseUnconditional = [bool]$caseUnconditional
        }
        [IO.File]::WriteAllText((Join-Path $work 'observation.json'), ($out | ConvertTo-Json -Compress -Depth 5))
        """;
}
