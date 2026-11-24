# rc/2026-12-05 cut commands after the C# ports

**Cut gate failed at `3e0cbab19324fb3ca2f50df1b74cb8b957fef06a`.** The command sequence below records the interfaces at that pin. Run it with a pin that has passed the cut gates. The newest remote line observed during this verification was `rc/2026-12-04` at `6ed4cb58cd75fe386e94965165d96752452431a8`. The next name was available as `rc/2026-12-05`.

## Server2 gate evidence

| Gate | Verdict | Evidence |
| --- | --- | --- |
| Land graph audit | **FAIL** | `land-graph-audit --check` exited 1 at the manifest precheck with `manifest does not match the selected graph`. The live digest was `2AE7A58366ED940EC46A5950CA053732FDC1580D61E07E1C047BFA5DC96A2375`, while the checked-in digest was `39B06B3A948BF44BF1BAF758201BAC0D00D37A6DDB1A361A4075B9CF6EECD71A`. Both count 80 definitions. See `.antiphon/rc-cut-81f39228/graph.log`. |
| Derived impact index | **FAIL** | `regenerate-derived-impact-index` exited 0 and reported `changed`. It modified `tests/derived-impact-index.json` (37 insertions, 19 deletions). The diff includes missing `DesktopPostgresPowerShellTests` edges. The captured diff is `.antiphon/rc-cut-81f39228/index.diff`. The test runs used the restored committed bytes. |
| Native startup probe on server2 | **PASS** | `dotnet --version` returned `10.0.404` with exit 0 in a granted slot. See `.antiphon/rc-cut-81f39228/startup.log`. |
| Guard union CP-1 | **FAIL** | Run `20261124-185121-334c` covered all six requested classes. 101 passed, 1 failed, 1 skipped. `DerivedImpactIndexTests.committed_index_matches_the_builder` failed the full-byte comparison. The skip was `WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact`, rostered for Windows. The terminal receipt was source-bound and verified by the checkpoint tool. The green-source validator rejected it with `report_failed` (exit 2). |
| Linux repair union CP-2 | **PASS** | Run `20261124-190153-f00d` covered all 19 selected classes for CARD-2058, CARD-2064, CARD-2065 and groups 128, 92, 102, 112 and 136. 200 passed, 0 failed, 0 skipped. The green-source validator returned exit 0 for the exact pin. |

These commands ran on server2 under granted slots. Both checkpoint selections target `Antiphon.Tests`, with independent cold builds so each gate has its own terminal receipt. Group 136 uses the Linux `ScriptHarnessHandleProbeTests` witnesses. The native case `ScriptHarnessWindowsOwnershipTests.Only_standard_handles_are_inherited` remains due in Windows Full.

The graph manifest was last recorded at `8732a7c6b`. The project definitions for `Antiphon.Nightly`, `Antiphon.Scripts` and `Antiphon.Tests` changed in the pinned tree after that record. [Checked-in graph manifest](../../server/Application/Services/LandVerificationBuildGraph.manifest.txt)

Rerun the command gates from the verified source checkout with the slot wrapper.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-cut-graph -- ./scripts/antiphon land-graph-audit --check
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-cut-derived-impact-index -- ./scripts/antiphon regenerate-derived-impact-index
git status --porcelain=v1
```

Checkpoint evidence is under `.antiphon/checkpoints/<run-id>/report.json` with the row's execution TRX. CP-1 report SHA-256 is `e312459ae629b2db63b62ce6dd3197c392a4dacb9e9393eeae888ebda0e2a087`. CP-2 report SHA-256 is `f0ae1ecb15bf5402cd3214d9b0f17b0616b02301958c12282fe61d1fd2979796`.

```text
CHECKPOINT CP-1 commit=3e0cbab19324fb3ca2f50df1b74cb8b957fef06a build=ok filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(DerivedImpactIndexTests*)|(C1729LayeringGuardTests*)|(WindowsUnitPlatformAdmissionTests*)|(NativeLinuxPolicyWitnessMutationTests*)/* executed=102 passed=101 failed=1 skipped=1 trx=/work/worktrees/ws-03-d4/.antiphon/checkpoints/20261124-185121-334c/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=3e0cbab19324fb3ca2f50df1b74cb8b957fef06a sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=3e0cbab19324fb3ca2f50df1b74cb8b957fef06a build=ok filter=/*/*/(OrchestratorWorkspaceWarningTests*)|(BootstrapCheckTests*)|(VerifyDevStackSmokeTests*)|(DesktopPostgresTests*)|(DesktopPostgresPowerShellTests*)|(MeasureSessionStateCacheScriptTests*)|(PtyHostReaperTests*)|(ZombieAgentReaperTests*)|(TestFixtureCleanupGuardTests*)|(ReviewMirrorCheckoutTests*)|(ReviewDelegationRunnerApiTests*)|(ProductionRunnerIsolationTests*)|(C0824WorkspaceSlotRoleGateTests*)|(ChannelOutboundProbeModelTests*)|(ProbeModelComparisonTests*)|(AppDbContextModelTests*)|(ReviewEngineMigrationTests*)|(CheckpointCallerBudgetTests*)|(ScriptHarnessHandleProbeTests*)/* executed=200 passed=200 failed=0 skipped=0 trx=/work/worktrees/ws-03-d4/.antiphon/checkpoints/20261124-190153-f00d/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=3e0cbab19324fb3ca2f50df1b74cb8b957fef06a sourceState=clean buildSource=verified
```

## 1. Push the new ref and prepare its checkout

Run from PowerShell 7 on the desktop, using an owned command checkout at the exact verified pin with push access to `origin`. The assigned desktop checkout was `C:\Antiphon\worktrees\card-task-81f39228`. Its presence and HEAD were unverified from server2.

```powershell
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
function Assert-Exit { param([int]$Code, [string]$Step)
    if ($Code -ne 0) { throw "$Step exited $Code" }
}

$toolsRoot = 'C:\Antiphon\worktrees\card-task-81f39228'
$pin = '3e0cbab19324fb3ca2f50df1b74cb8b957fef06a'
$rc = 'rc/2026-12-05'
$ref = "refs/heads/$rc"
$sha12 = $pin.Substring(0, 12)
$candidateId = "$($rc.Replace('/', '-'))-$sha12"
$releaseRoot = 'C:\Antiphon\releases'
$coordinationRoot = 'C:\Antiphon\verification'
$runId = [guid]::NewGuid().ToString('N').Substring(0, 8)
$checkout = "$releaseRoot\checkout-$runId"
$taskRoot = "$releaseRoot\task-$runId"
$stateRoot = "$releaseRoot\state-$runId"
$logRoot = "$releaseRoot\candidates\$candidateId\logs"
$launchRecord = "$taskRoot\launch-record.json"
$sourceRun = 'acca11ba'
$sourceCandidate = 'rc-2026-12-04-6ed4cb58cd75'
$sourceSha = '6ed4cb58cd75fe386e94965165d96752452431a8'

Set-Location -LiteralPath $toolsRoot
$toolsHead = git rev-parse HEAD
Assert-Exit $LASTEXITCODE 'read command checkout HEAD'
if ($toolsHead -cne $pin) { throw "Command checkout must be at $pin" }

pwsh -NoProfile -File scripts\build-slot.ps1 -Label rc-cut-native-startup -- dotnet --version
Assert-Exit $LASTEXITCODE 'desktop startup probe'

$existing = @(git ls-remote origin $ref)
Assert-Exit $LASTEXITCODE 'check release ref absence'
if ($existing.Count -ne 0) { throw "$ref already exists" }
git push origin "$($pin):$ref"
Assert-Exit $LASTEXITCODE 'push new release ref'
$published = @(git ls-remote origin $ref)
Assert-Exit $LASTEXITCODE 'read new release ref'
if ($published.Count -ne 1 -or ($published[0] -split '\s+')[0] -cne $pin) {
    throw "Remote $ref must point to $pin"
}

$candidateArgs = @('--ref', $rc, '--release-root', $releaseRoot, '--checkout-root', $checkout)
$journal = & .\scripts\antiphon.cmd release-candidate @candidateArgs
Assert-Exit $LASTEXITCODE 'journal and clone candidate'
$journalRecord = $journal[-1] | ConvertFrom-Json
if ($journalRecord.sha -cne $pin -or $journalRecord.candidateId -cne $candidateId) {
    throw 'Candidate journal identity mismatch'
}
if (-not (Test-Path -LiteralPath "$checkout\.antiphon-release-owned" -PathType Leaf)) {
    throw 'Release checkout ownership marker is missing'
}

$nightlyMarker = "$releaseRoot\checkout-$sourceRun\.antiphon-nightly-owned"
if ((Get-Content -LiteralPath $nightlyMarker -Raw | ConvertFrom-Json).kind -cne 'nightly-clone') {
    throw 'Source nightly marker kind mismatch'
}
Copy-Item -LiteralPath $nightlyMarker -Destination "$checkout\.antiphon-nightly-owned"
git -C $checkout config --local core.longpaths true
Assert-Exit $LASTEXITCODE 'enable Git long paths'
git -C $checkout checkout --detach $pin
Assert-Exit $LASTEXITCODE 'detach candidate checkout at pin'
```

At the pin, `scripts\release-candidate.ps1` has been replaced by `release-candidate`. With a fresh checkout, the earlier dry-run recipe would try to resolve origin in a directory the dry run has yet to create. The remote check above serves as the preview. For a long-lived rc ref, the command reads the published tip, creates an independent clone if needed, writes `.antiphon-release-owned` and journals the candidate. The explicit detach sets the clone's HEAD to the verified pin. `nightly-run.ps1 -NoSync` requires `.antiphon-nightly-owned` and `core.longpaths=true`. The acca11ba checkout path is documented. The marker's current presence and contents were unverified on server2. [Release command and flags](../../scripts/lib/Antiphon.Scripts/ReleaseCandidateCommand.cs) · [Clone and journal implementation](../../scripts/lib/Antiphon.Nightly/ReleaseCandidate.cs) · [Nightly ownership checks](../../scripts/lib/Antiphon.Nightly/NightlyRun.cs) · [Previous checkout and run paths](2026-11-24-group-136-native-evidence.md)

## 2. Export the stopped-run seed and start Windows Full

Continue in the same desktop PowerShell session after the operator has stopped acca11ba, verified its writers have exited, and confirmed the shared native lock is free. The retained log directory below comes from the native evidence note. The timing sibling is derived from the default run layout and its presence on the desktop is unverified. Inspect the retained summary or timing evidence if the launch used a different timing root.

```powershell
$sourceLogRoot = "$releaseRoot\candidates\$sourceCandidate\logs"
$sourceRunDirectory = "$sourceLogRoot\2026-11-24-0922-acca11ba"
$sourceTimingRoot = "$sourceLogRoot\timing-$sourceRun"
if (Test-Path -LiteralPath "$coordinationRoot\native-run.lock") {
    throw 'The shared native-run lock is present'
}
$null = New-Item -ItemType Directory -Path $taskRoot, $stateRoot, $logRoot

$seedPath = "$stateRoot\seed-from-$sourceRun.json"
Set-Location -LiteralPath $checkout
$seedArgs = @('--run-directory', $sourceRunDirectory, '--output-path', $seedPath,
    '--source-run-id', $sourceRun, '--source-candidate-id', $sourceCandidate,
    '--source-sha', $sourceSha, '--timing-root', $sourceTimingRoot)
$seedReceipt = & .\scripts\antiphon.cmd export-nightly-stopped-run-seed @seedArgs
Assert-Exit $LASTEXITCODE 'export acca11ba stopped-run seed'
$seedDigest = (Get-FileHash -LiteralPath $seedPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($seedReceipt[-1] -cne "SEED $seedPath SHA256 $seedDigest") {
    throw 'Seed export receipt and file digest disagree'
}
$seedReceipt

[ordered]@{
    checkoutRoot = $checkout
    expectedSha = $pin
    buildSlotLabel = "rc-qualification-$runId"
    nightlyArguments = @('-NoSync', '-Profile', 'rc', '-Trigger', 'rc',
        '-SelectionMode', 'Full', '-Ref', $rc, '-ExpectedSha', $pin, '-CandidateId', $candidateId,
        '-CheckoutRoot', $checkout, '-StateRoot', $stateRoot, '-LogRoot', $logRoot,
        '-ReleaseRoot', $releaseRoot, '-CoordinationRoot', $coordinationRoot, '-RunId', $runId,
        '-PriorFailuresPath', $seedPath, '-PriorFailuresDigest', $seedDigest,
        '-AcceptCrossCandidateSeed', '-SeedSourceCandidateId', $sourceCandidate)
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $launchRecord -Encoding utf8

& .\scripts\antiphon.cmd workspace-slots cold-reset --all-runners --reason "rc qualification $rc $sha12"
Assert-Exit $LASTEXITCODE 'request workspace slot cold reset'
& .\scripts\antiphon.cmd qualification-preflight --checkout-root $checkout
Assert-Exit $LASTEXITCODE 'qualification preflight'

$starter = "cmd.exe /c $checkout\scripts\antiphon.cmd qualification-start-supervisor --task-root $taskRoot --launch-record $launchRecord --log-root $logRoot --state-root $stateRoot --run-id $runId"
$started = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
    CommandLine = $starter
    CurrentDirectory = $checkout
}
if ($started.ReturnValue -ne 0) { throw "Starter creation failed with $($started.ReturnValue)" }

$startRecord = "$taskRoot\start-supervisor.json"
$deadline = [DateTime]::UtcNow.AddSeconds(90)
while (-not (Test-Path -LiteralPath $startRecord -PathType Leaf)) {
    if ([DateTime]::UtcNow -ge $deadline) { throw "No starter record at $startRecord" }
    Start-Sleep -Seconds 2
}
$start = Get-Content -LiteralPath $startRecord -Raw | ConvertFrom-Json
if ($start.runId -cne $runId -or $start.observation -cne 'alive') {
    throw "Starter record for $runId reports $($start.observation)"
}
```

The starter uses `pwsh` from PATH by default and accepts `--powershell-path` if needed. The Windows detached process's PATH for `dotnet` and `pwsh` remains unverified. After the new state root has `last-run.json`, run `scripts\antiphon.cmd qualification-observe --task-root $taskRoot --state-root $stateRoot` to check the new run's identity and state. The starter supplies the supervisor's launch child with the same `--run-id` that the record gives `nightly-run.ps1` as `-RunId`. [Preflight](../../scripts/lib/Antiphon.Scripts/QualificationPreflightCommand.cs) · [Starter flags](../../scripts/lib/Antiphon.Scripts/QualificationStartSupervisorCommand.cs) · [Launch and slot](../../scripts/lib/Antiphon.Nightly/QualificationLaunch.cs) · [Nightly parameter mapping](../../scripts/nightly-run.ps1) · [Cold reset options](../../scripts/lib/Antiphon.Nightly/WorkspaceSlots.cs)

Run `557cdb63` supplied acca11ba's intake seed. The new Full's seed comes from acca11ba's retained run and timing evidence and carries source candidate `rc-2026-12-04-6ed4cb58cd75`. Check the new run's log for `failed-first fallback:` and compare its `dispatch-schedule.json` `historyDigest` with `$seedDigest` before crediting seeded ordering. Intake can continue unseeded after a refusal. It checks the digest, envelope hash, source candidate and the 14-day age bound on both seed timestamps. The export and native launch were unverified on the desktop. [Seed CLI flags](../../scripts/lib/Antiphon.Nightly/NightlySeedCommands.cs) · [Exporter and timing custody](../../scripts/lib/Antiphon.Nightly/StoppedSeedFiles.cs) · [Seed intake](../../scripts/lib/Antiphon.Nightly/PriorFailureHistory.cs) · [Dispatch schedule](../../scripts/lib/Antiphon.Nightly/NightlyDispatch.cs)
