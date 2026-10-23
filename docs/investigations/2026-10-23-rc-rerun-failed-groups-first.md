# Windows RC: rerun previously failing selections first

2026-10-23, Investigate task `88614cc1`. **Yes, as a separate diagnostic preflight;
the shipped qualification cannot reorder or resume selected groups. Then run the
entire Windows RC profile on the new candidate.** This is an operating question,
not a newly confirmed product bug. No implementation change is required here.

Evidence boundary: repository at `cbe0eda6a750dd934c1f3ed0bac4d1caee1f28bc`, plus
read-only Git objects for RC `5a126f6fc5ae4e16d803b628716b8f0485a37174`.
No qualification, build, or test was run; no desktop, release/verification files,
or PID 209072 was accessed. Commands below are for a separately commissioned
Windows execution. Historical results come from the stored
[Windows diagnosis](2026-10-23-rc-5a126f6fc-qualification-reds-debug.md), not a fresh
observation of that still-running qualification.

## 1. Selection and order supported today

Neither `nightly-run.ps1:86` nor `nightly-tests.ps1:27` exposes `-Groups`,
failed-first, or rerun-failed. `-Suites` selects **suites**, not numbered launches;
its input order is retained (`scripts/lib/nightly-policy.ps1:251`), and the executor
iterates that order (`scripts/lib/nightly-tests-impl.ps1:924`). A subset missing
required suites explicitly makes coverage incomplete (`:789`).

Within a suite the generated launch order is authoritative: namespace sort,
largest-first cost packing with a 900-second target and 30 operands, then oversized
method chunks, then exact exclusion leaves. It is deterministic for its inputs,
not a permanently fixed numbered roster (`scripts/lib/nightly-tests-impl.ps1:123`,
`:212`, `:377`, `:410`, `:1015`). No supported caller flag moves groups 3/13/14/15
ahead of the rest, and no restart-from-group flag exists in these entrypoints.

## 2. What earns gate credit

| Field | Implemented behavior |
|---|---|
| `requestedMode` | `Full`, `Shadow`, `ChangeAware`; default `Full` (`scripts/nightly-run.ps1:95`). |
| `effectiveMode` | Always `Full`. Both other requests still execute Full; `ChangeAware` adds `change-aware-unqualified` (`scripts/lib/release-selection.ps1:213`). |
| Selection `coverageScope` | Always `Full`; edited/narrow scope is rejected across hops (`scripts/lib/release-selection.ps1:350`). |
| Execution `coverageScope` | `Full`, or `PlatformDiagnostic` for Linux RC, which forces incomplete coverage (`scripts/lib/nightly-tests-impl.ps1:1226`). |
| Selector proposal `mode` | Lowercase `full` or `narrow`, with `executionEvidence=false`, `activation=unqualified`; this is intent, not a credited selective execution (`scripts/lib/test-selection.ps1:249`). |

There is no admitted `Selective` or `Changed` gate scope. Even a partial Windows
suite run can say scope `Full`; that label alone proves nothing because its
`coverageComplete` is false. `Resolve-ReleaseSelectionMembers` returns the whole
roster or throws `selected-execution-unqualified`
(`scripts/lib/release-selection.ps1:371`). The observed `requestedMode=Full`,
`fallbackReasons=[baseline-absent]` described in the brief is consistent with
`:72`, `:208`, `:222`; the desktop journal was not independently read here.

Credit requires real true booleans for coverage/tests/report, exit 0, run ID,
policy hash, Full scope when present, candidate ID, and matching full
SHA/expectedSha (`scripts/lib/release-gate.ps1:201`, `:219`). The Windows profile
requires all nine suites (`tests/test-execution-policy.json:27`); required skips,
missing/duplicate UIDs, stale evidence, or absent TRX block it
(`docs/release-gates.md:272`). Every required launch must pass on the **same
candidate SHA, policy, and run**. Publication checks the ledger's full selection,
run identity, each chunk's run/SHA, and its TRX digest
(`scripts/lib/release-authority.ps1:649`, `:797`). There is no receipt merger for
partial attempts. A diagnostic preflight followed by an independent full run
respects those rules; preflight results contribute zero gate coverage.

Interim is a separate card-review mechanism, shipped disabled; it does not relax
the RC gate (`docs/release-gates.md:579`, `:618`). The independent nightly watchdog
neither stops RC qualifications nor clears their locks (`docs/nightly-watchdog.md:123`).

## 3. Candidate identity and baselines

For `rc/2026-10-20`, identity is `rc-2026-10-20-<sha12>`; all comparisons still bind
the full SHA (`scripts/lib/release-gate.ps1:143`). A new tip is a new candidate,
including a tip containing only picked fixes. Moving the remote during a run
refuses it; fixes require testing the new tip from scratch
(`docs/release-gates.md:415`, `:435`). A rare sha12 collision is refused as a
repinned existing journal, not permission to reuse evidence
(`scripts/release-candidate.ps1:182`).

`BaselineReceiptPath` is **not** an old `last-run.json`, green group TRX, or even a
standalone complete-green file. The reader accepts an archived
`antiphon-full-release-baseline-v1` from a fully published RC release. It validates
same repository/platform, Full profile/scope, source commit, authority, full
ledger, publication, delivery, every required UID and TRX digest; the baseline
must be an ancestor. Compatibility compares policy, catalog, selector, runtime,
profile and platform hashes (`scripts/lib/release-selection.ps1:46`, `:67`, `:101`,
`:118`, `:138`, `:162`, `:176`, `:201`). Automatic search is under
`<ReleaseRoot>\baselines`. Neither failed Windows run qualifies: the stored diagnosis
records both incomplete/false (`docs/investigations/2026-10-23-rc-5a126f6fc-qualification-reds-debug.md:35`).

"Unchanged" in the proposal comes from Git's committed B..C path/content diff
(`--name-status -z --no-renames`), old/new catalog path rules, explicit dependency
edges, set/prerequisite closure, and candidate source membership. Unknown paths or
authority/build changes force a full proposal. It is not per-group result reuse
or a comparison of old build outputs (`scripts/lib/test-selection.ps1:169`, `:190`,
`:203`, `:234`). Hashes also bind the baseline's artifacts and selector contract.
Even a valid compatible baseline only informs the proposal **today**: execution
remains Full. `qualificationDigest` hashes supplied JSON but explicitly does not
activate its claim; train digest binds opaque bytes only; timing/intent/train
paths are recorded, not authority to skip launches
(`scripts/lib/release-selection.ps1:214`, `:228`, `:247`).

## 4. Cost and execution constraints

The smallest established target is the **16 distinct failing methods in five
classes**, covering the 19 historical failures plus any sibling argument rows of
those methods. This avoids rerunning every passing class that happened to share
a numbered group. Use one project build and reuse it for the diagnostic methods;
the later gate performs its normal full build/profile again. No dedicated
failed-first preflight command was found in the entrypoints above.

Use the same pinned Windows checkout, .NET/TUnit dependencies, Docker availability,
safe child environment, and TRX/diagnostic switches as qualification
(`scripts/lib/nightly-tests-impl.ps1:910`, `:1000`, `:1020`, `:550`;
`scripts/lib/nightly-policy.ps1:286`). A new owned clone is necessary if the existing
checkout is still in use; do not modify a running tree. Desktop admission defaults
to **6144 MB available memory**, two slots, four CPUs; the live broker governs
admission. Exit 4 means not run, never bypass the gate
(`docs/testing-and-build.md:980`, `:984`, `:1005`).

Direct test commands acquire neither nightly lock automatically. Hold both a
private diagnostic `run.lock` and the **real shared**
`C:\Antiphon\verification\native-run.lock`, in that order, across preparation/build/
tests. Do not substitute a private coordination directory. Native suites must not
overlap, even when two build slots are available
(`scripts/lib/nightly-run-impl.ps1:288`, `:301`;
`scripts/lib/release-gate.ps1:385`). A stale shared lock is intentionally not reclaimed.

## 5. Group membership and its evidence limit

These are the **confirmed failure-bearing classes**, not a claim to an exhaustive
passing-class roster. Source: the stored Windows TRX/diagnostic failure table at
`docs/investigations/2026-10-23-rc-5a126f6fc-qualification-reds-debug.md:49` and its
artifact hashes at `:263`.

| Windows group at 5a126f6fc | Namespace / class | Historical failures |
|---|---|---:|
| 3 | `Antiphon.Tests.Agents.DirectSessionRunnerClientDisposalTests` | 1 |
| 13 | `Antiphon.Tests.Application.TurnEndGapRestartDeliveryTests` | 4 |
| 13 | `Antiphon.Tests.Application.VerificationRoundDeliveryTests` | 1 |
| 14 | `Antiphon.Tests.Application.AgentTaskLandCleanupSafetyTests` | 1 |
| 15 | `Antiphon.Tests.Application.LandNonCodeSkipTests` | 12 |

`tests/test-execution-policy.json` owns required suites/exclusions/chunks; runtime
discovery feeds `Get-NightlyProfileLaunchPlan`; `nightly-class-costs.json` supplies
costs. `tests/test-selection-policy.json` and `select-test-rosters.ps1` describe
change-impact sets, not stable numbered launch groups. See
`scripts/lib/nightly-tests-impl.ps1:24`, `:144`, `:421`, `:942`, `:994`.

A read-only in-memory reconstruction using the checked-in Linux listing fixture
(`scripts/fixtures/nightly/c1410-antiphon-listing.txt:1`) and current Windows
exclusions produced groups 3/13/14/15 with 77/305/573/283 nodes, and put
`LandNonCodeSkipTests` in group 19. These disagree with the Windows receipt's
76/333/150 completed nodes in groups 3/13/14. Thus that fixture cannot establish
the actual Windows groups. It was data transformation only, not test execution.
The previous Windows run also put the C1205 cases in group 12 and cleanup case
in group 13 (`docs/investigations/2026-10-23-rc-5a126f6fc-qualification-reds-debug.md:57`, `:62`).

**Remaining uncertainty:** exhaustive membership of the four Windows groups is
not preserved in the accessible report. Resolve it by reading that exact run's
TRX `TestDefinitions`, launch filters/summary and profile discovery (group 15 has
no final TRX at the report cutoff), on Windows under a separate read-only task.
Do not replace this missing evidence with the fixture or infer the new tip's
group numbers. The exact-method preflight below does not depend on that gap.

## Recommended runbook (future Windows execution only)

1. **Let the current run finish, or commission its explicit manual stop.** Inspect
   it on its execution host, filling `$oldStateRoot` from task c11e7803's launch
   record, never guessing the directory:

   ```powershell
   pwsh -NoProfile -File scripts/rc-qualification-status.ps1 `
     -CandidateId rc-2026-10-20-5a126f6fc5ae `
     -RunId 6bd38cbb9604482e96a4854957cead05 -StateRoot $oldStateRoot `
     -CoordinationRoot C:\Antiphon\verification -ReleaseRoot C:\Antiphon\releases
   ```

   Follow `docs/release-gates.md:154` exactly if stopping: preserve state/lock
   bytes and private inventory; prove PID/start/ancestor/descendant ownership back
   to the launch supervisor; reobserve its generation immediately before
   `taskkill /PID $verifiedSupervisorPid /T /F`; census every observed generation
   and candidate/run descendant for at most 30 seconds. PID 209072 alone is not
   authority. Unknown/reused identity or surviving children means retain locks.
   Under an exclusive hold on native starts, move only the unchanged proven-stale
   shared lock to a fresh same-volume evidence path with
   `Move-Item -LiteralPath $nativeLockPath -Destination $uniqueEvidencePath`
   (no Force). Verify preserved bytes and original absence. Never delete both
   locks as a pair or rewrite the failed result. These are manual procedures,
   not an automatic supervisor-stop API.

2. **Pin the new candidate after the fixes reach the RC line.** Have the launch
   owner prepare a clean, marked, independently owned Windows qualification clone
   at the final full SHA, preserving the failed run. No daemon may hold its bin
   tree. In a fresh dedicated PowerShell 7 shell, define `$pin` from the approved
   new tip, `$repo` as that prepared clone, and `$diag` as a fresh external
   diagnostic directory outside candidate gate logs. Keep the remote tip stable.

   ```powershell
   Set-Location $repo
   if ($pin -cnotmatch '^[0-9a-f]{40}$') { throw 'Full SHA required' }
   if ((git rev-parse HEAD).Trim() -cne $pin) { throw 'Wrong checkout' }
   $remote = @(git ls-remote origin refs/heads/rc/2026-10-20)
   if ($LASTEXITCODE -ne 0 -or $remote.Count -ne 1 -or
       ($remote[0] -split '\s+')[0] -cne $pin) { throw 'Remote moved' }
   if (git status --porcelain --untracked-files=no) { throw 'Dirty source' }
   if (Test-Path -LiteralPath $diag) { throw 'Fresh diagnostic directory required' }
   ```

3. **Diagnostic preflight: zero gate credit.** Acquire the real shared lock before
   the build, use the policy's safe environment, then run the exact methods in
   former-group order. Retain all TRX and diagnostic output separately. These
   commands use existing helpers, not a new qualification mode.

   ```powershell
   . ./scripts/lib/nightly-common.ps1
   . ./scripts/lib/nightly-policy.ps1
   . ./scripts/lib/release-gate.ps1
   $preflightId = [guid]::NewGuid().ToString('N')
   $diagState = Join-Path $diag 'state'
   $localLock = Enter-NightlyExclusiveLock -StateRoot $diagState -RunId $preflightId
   if (!$localLock.Ok) { throw $localLock.Reason }
   $shared = Enter-ReleaseGateNativeLock -CoordinationRoot C:\Antiphon\verification `
     -RunId $preflightId -Lane diagnostic
   if (!$shared.Ok) {
     Exit-NightlyExclusiveLock -StateRoot $diagState -RunId $preflightId
     throw $shared.Reason
   }
   $policy = Read-NightlyExecutionPolicy -RepoRoot $repo
   $safe = Get-NightlySafeChildEnvironment -PolicyObject $policy.Object -SuiteId antiphon
   foreach ($key in $safe.Keys) {
     [Environment]::SetEnvironmentVariable($key, [string]$safe[$key], 'Process')
   }
   $env:C487_INVOCATION = $preflightId
   $env:C487_RUN_DIR = $diag
   pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-diagnostic-build -- `
     dotnet build tests/Antiphon.Tests -c Debug --nologo
   if ($LASTEXITCODE -ne 0) { throw 'Diagnostic build failed or did not run' }
   ```

   Use this exact selection list. Method filters include all argument rows, so
   the count may exceed the 19 historical failed rows. No retries are implied.

   ```powershell
   $targets = @(
     '3|Agents|DirectSessionRunnerClientDisposalTests|Dispose_awaits_exit_of_its_running_host_and_descendants'
     '13|Application|TurnEndGapRestartDeliveryTests|C1205_Watchdog_delivers_while_event_stream_remains_healthy'
     '13|Application|TurnEndGapRestartDeliveryTests|C1205_Busy_tail_keeps_completion_obligations_pending'
     '13|Application|TurnEndGapRestartDeliveryTests|C1205_Restart_delivers_five_gap_held_completions_once'
     '13|Application|VerificationRoundDeliveryTests|C544_BriefHandoffRecovery'
     '14|Application|AgentTaskLandCleanupSafetyTests|C448_V18_CleanupRetryPreservesChangedWork'
     '15|Application|LandNonCodeSkipTests|C1380_TargetContainedCodeIsExcludedFromTheActualRebasedDiff'
     '15|Application|LandNonCodeSkipTests|C1385_UnrelatedScriptChangeStillSkips'
     '15|Application|LandNonCodeSkipTests|C1380_RegularDocumentationChangesSkip'
     '15|Application|LandNonCodeSkipTests|C1380_DocsOnlyRebasedLandRecordsNonCodeSkip'
     '15|Application|LandNonCodeSkipTests|C1390_UpdateRefusesDocsMarkdownInput'
     '15|Application|LandNonCodeSkipTests|C1400_UpdateRefusesCaseVariantDocsInputs'
     '15|Application|LandNonCodeSkipTests|C1400_UpdateRefusesImportedPropsDocsInclude'
     '15|Application|LandNonCodeSkipTests|C1400_UpdateRefusesPercentEncodedDocsInclude'
     '15|Application|LandNonCodeSkipTests|C1400_UpdateRefusesAdditionalFilesDocsInclude'
     '15|Application|LandNonCodeSkipTests|C1400_RepairAnchoredImportRefusesDocsInclude'
   )
   foreach ($target in $targets) {
     $g, $ns, $type, $method = $target.Split('|')
     $name = "diagnostic-antiphon-former-group-$g-$type-$method"
     $filter = "/*/Antiphon.Tests.$ns/$type/$method"
     pwsh -NoProfile -File scripts/build-slot.ps1 -Label $name -- `
       dotnet run --no-build --project tests/Antiphon.Tests -c Debug -- `
       --treenode-filter $filter --no-progress --no-ansi --report-trx `
       --report-trx-filename "$name.trx" --results-directory $diag `
       --diagnostic --diagnostic-output-directory (Join-Path $diag "$name-execution")
     if ($LASTEXITCODE -ne 0) { throw "Diagnostic red/incomplete: $name" }
   }
   ```

   This uses the same test program and native TRX/diagnostic flags as qualification,
   through the required `dotnet run`/slot front door. Confirm each TRX contains
   the intended method and nonzero execution, no failures or required skips;
   process exit alone is insufficient. After normal completion and verified child
   exit, release only this shell's locks:

   ```powershell
   Exit-ReleaseGateNativeLock -CoordinationRoot C:\Antiphon\verification -RunId $preflightId
   Exit-NightlyExclusiveLock -StateRoot $diagState -RunId $preflightId
   ```

   On red, slot timeout, interruption, or unknown child cleanup, stop advancement;
   preserve evidence and locks until the launch owner establishes child exit.
   A second shell/qualification must not race this diagnostic. The build uses
   the owned clone's ordinary bin layout, as qualification does; it must not be
   pointed at a shared daemon checkout. No alternate bin directories are created
   by this recipe. Close the dedicated environment shell before the gate.

4. **Gate run: fresh full Windows qualification, all groups again.** Recheck full
   HEAD/remote SHA and cleanliness in the prepared owned clone. Journal the new
   candidate, verify its journal SHA equals `$pin`, then run with no `-Suites`,
   no `-NoReport`, no seams and no copied prior selection files. Populate a fresh
   run ID and private candidate state directory; retain launch PID/start/paths.

   ```powershell
   Set-Location $repo
   $candidateId = 'rc-2026-10-20-' + $pin.Substring(0, 12)
   pwsh -NoProfile -File scripts/release-candidate.ps1 -Ref rc/2026-10-20 `
     -CheckoutRoot $repo -ReleaseRoot C:\Antiphon\releases
   if ($LASTEXITCODE -ne 0) { throw 'Candidate journaling refused' }
   $candidateRoot = "C:\Antiphon\releases\candidates\$candidateId"
   $journal = Get-Content (Join-Path $candidateRoot 'candidate.json') -Raw | ConvertFrom-Json
   if ($journal.sha -cne $pin) { throw 'Candidate moved' }
   $gateRunId = [guid]::NewGuid().ToString('N')
   $gateState = Join-Path $candidateRoot "state-$gateRunId"
   pwsh -NoProfile -File scripts/build-slot.ps1 -Label "rc-$candidateId-full" `
     -SlotWaitMinutes 90 -- pwsh -NoProfile -File scripts/nightly-run.ps1 `
     -NoSync -Profile rc -Trigger rc -SelectionMode Full -Ref rc/2026-10-20 `
     -ExpectedSha $pin -CandidateId $candidateId -RunId $gateRunId `
     -CheckoutRoot $repo -StateRoot $gateState -LogRoot (Join-Path $candidateRoot 'logs') `
     -ReleaseRoot C:\Antiphon\releases -CoordinationRoot C:\Antiphon\verification
   ```

   `-NoSync` is valid only for the separately prepared, cleaned, marked checkout
   just verified at `$pin` (`docs/release-gates.md:423`). Keep the foreground
   owner alive, or separately commission the established supervisor with those
   exact arguments; the historical supervisor/launcher was task-local, not a
   checked-in restart command (`docs/investigations/2026-10-22-windows-qualification-0714a9a89-start.md:27`).
   Inspect using the status command with this new identity. Only the complete
   full run's validated report/coverage can advance its candidate green; actual
   publication still needs the pinned authority/ledger/report gate. Do not use
   `release-cut.ps1` for a diagnostic: it publishes on green unless `-SkipPublish`
   is specified (`scripts/release-cut.ps1:160`, `:171`).

Not done, noted: no group-reordering feature, test repair, qualification, stop,
release publication, or deployment was designed or performed.

--- next stage ---
next: land
handoff: Land this documentation-only operating answer. Separately commission Windows exact-method diagnostic preflight after the fixes reach a new pinned RC tip, then a fresh full qualification; prior passing groups earn no reusable credit. Exhaustive old-group membership requires the original Windows discovery/launch evidence.
artifact: docs/investigations/2026-10-23-rc-rerun-failed-groups-first.md
