# Fastest sufficient Windows requalification of the final rc tip

Date: 2026-10-27. Plan task: `84714b96`. Related owners: CARD-1514
(slow tests and timing), CARD-1404 (cost packing), CARD-1585 (lost suite plans),
CARD-1563 (timeout measurement), CARD-1313/1545 (future selective release credit).

**Finish native diagnosis and the reviewed rc top-up, fix incomplete coverage and
the timing-plan retention defect, then run one fresh Full Windows qualification
at the frozen final SHA. Failed-class reruns are useful preflight; they cannot
replace any part of that qualification. Reserve approximately 24 hours for the
final attempt until the existing run's timing sidecars support a tighter estimate.**

This is a documentation-only deliverable. No test, build, discovery, desktop
operation, task dispatch, card mutation, release, or activation was performed.
TestDesign remains a separate stage. The checkpoint proposal below is its input,
not a claim that the implementation or Windows witnesses have run.

The dispatch supplied two different requested document locations. This single
report uses the mandatory Plan-stage location under `docs/superpowers/plans/`;
there are no duplicate reports or source changes. CARD-1514's title and full
description were read before using its ownership reference; this does not bind
the unscoped task to a different card.

## Evidence and source identity

- Assigned branch: `feat/card-task-84714b96`; unchanged assigned base
  `718bd29f4a1504c64c491d607d31403fcb87dae2`. Fetched rc tip at inspection:
  `origin/rc/2026-10-27` = `d1d090f766451797a3bd9b30c9d2966f2f8c034c`.
  The branch was not switched, rebased, reset, or merged. The inspected release
  scripts, selection/coverage/timing implementation, policy and class-cost file
  are identical between this base and that fetched rc tip. Recheck at final cut.
- Failed Windows candidate: `105792f7d22f297746cdb338976f52e424b76fa7`,
  `rc/2026-10-25`; full run ID `8f055f87452d4c76998604df0b522db5`.
  Started `2026-10-26T09:46:22.8061354Z`, ended
  `2026-10-27T04:43:23.1108664Z`; `68220.305` seconds / `18.9501` hours.
- Whole-run digest: branch `feat/card-task-7e719c57`, inspected commit
  `c7ad9e901c6be169fb5a0f43be8e210211e9081b`,
  `docs/investigations/2026-10-27-rc-8f055f87-final-run-digest.md`.
  Its raw-summary SHA-256 is
  `bf4aeeaeb45333df4a58af84bb730e9eae17ae1a601e85aa12cf7c5001e5b668`.
  Read with `git show` from that ref; the report need not be landed to inspect it.
- Earlier digest: branch `feat/card-task-d5c669aa`, inspected commit
  `9d7bd87b67c786b28f50920c68cfd0611ab630a5`,
  `docs/investigations/2026-10-26-rc-8f055f87-windows-failure-digest.md`.
  Its 74 failures through group 64 are historical partial evidence.
- Owners: [release gates](../../release-gates.md),
  [testing/build](../../testing-and-build.md#nightly),
  [performance standards](../../test-performance-standards.md),
  [rc repair loop](../../orchestration-loop.md#rc-qualification-run-group-repair-loop-card-1514),
  [HTTP operations](../../ops-http.md), and [project conventions](../../project-context.md).
- `GET /api/runner-defaults` and `GET /api/session-runners` returned HTTP 200 at
  approximately 05:15 UTC. Defaults revision 2; the eligible Windows lane had
  capacity 2, occupied 2. An eligible Linux lane was available; another configured
  Linux lane was stale/draining. These observations are not reservations. Reread
  placement and effective project limits before commissioning. Omit `-Runner`;
  request `-Platform Windows` only for native work and leave portable work unpinned.
  No execution command below embeds a fleet host or chooses a private coordination
  root to evade another qualification's lock.

## Ground truth

| Brief assumption or proposed shortcut | Inspected code/evidence | Planning consequence |
|---|---|---|
| About 15 classes remain | Final digest: 140 failures in 65 classes; 33 classes/64 failures lack a complete matching repair at its capture. Even addressed failures await native closure | Reconcile the complete failure catalog, not the earlier estimate |
| Fix failures and the run becomes complete | There are also 538 skipped/unexecuted results and 179 required UIDs without any terminal result | Account for skips, unstarted cases and failed cases separately |
| Nine RC suites | `tests/test-execution-policy.json`, schema 4, requires ten: antiphon, session-runner, pty-host, agents-pty, messaging, client, scripts, e2e, markdown-pdf, observability | The policy at the final SHA owns the roster; older prose/comments are stale |
| `Full/Selective` are selectable release modes | Entry scripts accept `Full`, `Shadow`, `ChangeAware`. `New-ReleaseSelectionAttemptCore` always writes `effectiveMode=Full`, `coverageScope=Full`; `Resolve-ReleaseSelectionMembers` rejects selected execution | `Selective` is invalid; Shadow/ChangeAware currently save no release execution |
| Failed run 8f055f87 is a reusable baseline | `Read-ReleaseSelectionBaseline` requires an archived, published Full Windows RC with validated authority, ledger, artifact digests, ancestor SHA and compatible contract | A failed/incomplete summary, Linux green or checkpoint baseline is insufficient |
| `INHERITED` failures may pass the release gate | `BaselineComparer.Classify` reports INHERITED/INTRODUCED/NEW for checkpoint diagnosis. Release credit requires real true booleans for tests, coverage and report, exit 0, policy/run identity and matching pinned SHA | Classification is diagnostic, never a red waiver |
| A resumed attempt skips completed chunks | Immutable selection documents permit identical binding reuse; `Attempt` labels a new run. The executor still loops over all selected suites/chunks; no completed-chunk resume/import exists | A failed/interrupted qualification restarts Full with a fresh RunId; preserve the old attempt |
| Moving rc can retain qualification credit | Candidate identity includes the SHA prefix; checkout/remote SHA mismatches refuse. Publication rechecks the remote ref | Freeze the rc line during final qualification; any repair produces a new candidate |
| Two machines can each qualify half | Native execution is serial, `nativeActiveMax > 1` invalidates coverage; there is no cross-host or cross-run receipt merger. Linux RC is PlatformDiagnostic/incomplete | Parallelize diagnosis/repair across independent hosts; final credit comes from one Windows attempt |
| The E2E watchdog limits the whole suite to two hours | 7,200,000 ms applies to each generated launch. This run executed 74 E2E launches without a suite watchdog kill | Do not extend it, multiply it into an ETA, or undo splitting blindly |
| `summary.json.suites` contains suite elapsed times | Rows contain id/chunk/log/exit/timedOut/result/census; only the summary has total duration. Native child wall times live in `timing/groups-executed.json` | Exact per-suite hours are unavailable in the supplied digests; extract sidecar timings before promising savings |
| Timing retention is ready | `Write-TestPhasePlannedManifest` replaces one `groups-planned.json` for every suite; executed chunks are retained. CARD-1585 is still Backlog | Repair retention and suite/assembly joins before the next Full run |
| Complete-green automatically means publishable | Publisher additionally validates frozen policy/script hashes, execution plan, full ledger, teardown and correlated release-card readback. Production does not call `New-ReleaseGateAuthority`/`New-ReleaseGateExecutionPlan`; only harnesses do | A passing qualification is achievable separately. Publication remains held on the CARD-1313 S6 producer prerequisite |

## Decisions

These are the defaults under which this plan is complete; they do not change
release policy or grant publication authority.

| ID | Decision and reason | Rejected alternative |
|---|---|---|
| D-1 | One final Full Windows attempt after repairs; targeted native reruns first catch cheap failures | Failed-classes plus selective/baseline credit, which the gate cannot consume |
| D-2 | Use the full final digest and exact native task receipts; require a native closure or legitimate reviewed disposition for every gap | Treating Linux no-repro, a pushed fix, or an inherited failure label as Windows green |
| D-3 | Keep current coverage, native serialization and watchdogs; correct measured prices and oversized method groups | Parallel native processes on one host, separate locks, blanket exclusions or larger timeouts |
| D-4 | Fix CARD-1585 and underpriced groups before cutting; preserve individual suite source/assembly/policy/cost bindings | Append groups under the last suite's identity or manufacture missing phase values |
| D-5 | Reuse builds and setup where already supported; prioritize prices and known defects. Broader E2E fixture sharing needs a measured, separately reviewed repair | A new fixture architecture or scoped-gate feature as an unmeasured emergency shortcut |
| D-6 | Reserve a 24-hour stable maintenance window; use the current 18.95-hour failed run only as a planning anchor | Promising a 6- or 10-hour green run without per-suite timings or comparable p95 |
| D-7 | Run qualification without publication; keep tags, publish and promote human-only | Calling `release-cut.ps1` without `-SkipPublish` or assuming a green summary authorizes release |
| D-8 | Do not wait for a new selective-release implementation to qualify Windows. If the operator also needs a publishable release from this very run, land/review the existing Full authority producer prerequisite before freezing | Handwritten authority/ledger files or retrofitting new script hashes into tested evidence |

## Where the 18.95 hours went, and what is still unknown

The digest proves **333 suite rows, 331 native launches, nativeActiveMax=1**, four
successful build rows, and two one-hour Antiphon launch timeouts. It does not
include per-suite elapsed sums. A numerical allocation of all 18.95 hours from
these digests would be invented. The retained executed sidecar is the first
source to read; TRX `Times` is a separately labeled host-test interval fallback,
not end-to-end launch wall time.

| Suite | Launches/rows | Observed pass / fail / skip | Time evidence and next action |
|---|---:|---|---|
| antiphon | 179 | 19104 / 118 / 508 | Groups 28 and 65 each ran about 3600 s; combined roughly 2 h, 10.6% of total. Remaining group/split wall times unknown here; reprice from retained timings |
| session-runner | 6 | 1688 / 12 / 5 | No chunk watchdog; elapsed sum requires sidecar |
| pty-host | 50 | 116 / 0 / 8 | Many small launches; inspect prefix/namespace/exclusion constraints and startup overhead before coalescing |
| agents-pty | 9 | 647 / 1 / 17 | Run separately from Antiphon.Tests/FakeClaude consumers; elapsed sum unknown |
| messaging | 1 | 250 / 0 / 0 | Preserve disposable-broker setup; elapsed unknown |
| client | 1 | 1841 / 0 / 0 | JSON, not TRX; npm build/lint and test phase are separate; no native sidecar wall row |
| scripts | 1 | 591 / 0 / 0 | 13 harness logs; assertion counts are not TUnit tests; elapsed unknown |
| e2e | 74 | 161 / 9 / 0 | All emitted TRX; 170 required UIDs, seven excluded. No chunk reached its 2 h watchdog; launch/body/setup split unknown |
| markdown-pdf | 11 | 16 / 0 / 0 | Small launches; preserve required roster; elapsed unknown |
| observability | 1 | 12 / 0 / 0 | Required tenth suite; elapsed unknown |

Native totals are 21,994 passed / 140 failed / 538 skipped. Antiphon requires
19,909 expanded cases: 18,230 TRX results + 1,500 recovered diagnostic terminal
results + 179 without terminal evidence. The official missing-union reason lists
1,679 UIDs because missing TRX prevents the gate from consuming the 1,500 diagnostic
results. Diagnostic recovery does not grant coverage. A TIMEOUT row can be labeled
FAIL after missing-TRX evaluation; `outcome=TESTS` does not mean no watchdog fired.

Current packing uses 900 s target groups, at most 30 class operands, 2400 s
non-E2E lone-class/launch ceiling, and 1500 s E2E class-split threshold / 1800 s
launch ceiling. Unpriced ordinary nodes cost 0.5 s; Slow costs 1.5 s or 4 s in
Land/Delivery, with a 2x heavy-family factor. Those heuristics can badly underprice
DB/process-heavy classes. Prices divide class seconds by the recorded node count;
they are not per-method p95.

CARD-1584 deliberately priced landing E2E at 600 s/case (52 nodes = 31,200 s),
dispatch delivery at 600 s/case, retirement at 500 s/case, browser/app cases at
300 s/case, local guards at 10 s/case. Most are conservative provisional bounds.
The current required E2E projection totals 61,060 modeled seconds, about 16.96 h,
not an observed E2E duration. The 74-launch repair preserved all required cases
and avoided the former missing-TRX failure; it did not prove faster execution.

Priority order for savings:

1. **Groups 28/65:** replace heuristic prices with Windows evidence, split large
   classes/matrices into bounded groups, and prove fresh required UID partition.
   The one-hour caps were reached while tests were still progressing; the last
   named test is not proven hung. Splitting prevents lost evidence; it is not
   automatically a two-hour saving because those bodies still need execution.
2. **E2E:** use full-run per-class/method and phase observations to replace the
   pessimistic prices, then coalesce only where the existing planner permits it.
   Each removed host launch saves measured startup/discovery/teardown overhead;
   it does not eliminate per-test `new AntiphonAppFixture` or `LandDeliveryFixture`.
   `SharedApp` already shares one host per test process, but Board tests and native
   landing cases create fresh fixtures. Preserve deliberate crash/restart tests.
3. **50 PtyHost and 11 PDF launches:** price actual wall overhead and inspect exact
   filters. Prefix collisions/method exclusions can require separate invocations.
   Do not merge by class name alone or infer savings from case count.
4. **Long fixtures:** CARD-1514/1562 can share Docker, migrations and seed once per
   process/session, using template DBs, transactions or isolated schemas per case.
   Only commission a narrowly evidenced repair that beats its implementation and
   requalification cost; do not lower `ProcessSpawnLimit` protections to gain speed.

Two-machine option: portable investigation/planner tests on an eligible non-Windows
lane while one Windows lane serially runs native diagnosis. A second properly
provisioned Windows host could shorten diagnosis with independent owned resources;
no second eligible Windows host was observed. Two partial qualification runs cannot
be combined. Two complete Full runs merely duplicate work; one must independently
pass everything. CPU/GC/native limits already inside a test process remain intact.

## Execution order and concrete commands

All commands below are instructions for subsequently commissioned owners, not
commands executed by this Plan. Paths are absolute variables from that owner's
launch record: `$coordinatorRoot` (approved final-rc scripts), `$releaseRoot`,
`$checkoutRoot` (an owned independent release clone), `$coordinationRoot` (the
existing shared native lock root), and `$evidenceRoot` (outside the disposable
clone). No canonical checkout, task worktree or unmarked clone may impersonate the
release clone. Stop on a failed command; never retry unleased.

### 1. Consume native reports and close the actual roster

Existing tasks own their scopes; do not start competing native work:

| Task | Assigned scope | Server observation during planning |
|---|---|---|
| `32365cdf-a322-4747-954a-e570f1b08266` | 17 Antiphon terminal/delivery classes, FakeClaude, four SessionRunner classes | Dispatched; recorded base 718bd29f, so require actual tested SHA in its receipt |
| `fb80be16-f320-453e-b9e4-b3e46722b548` | 34 checkpoint/script/release/git classes | Queued; no native result yet |
| `595bc226-9292-457e-97b4-5ff0f20bca77` | Six E2E classes including native landing/dispatch | Queued; no native result yet |

Their combined briefs name 62 distinct failed classes from the 65-class catalog.
Add ownership for `LandingGitTests`, `RepositoryChildRecoveryTests`, and
`WorktreeManagerGitIntegrationTests`, which are absent from those briefs. Also
qualify the six incomplete classes: `ApiErrorRecoveryServiceTests` (40 missing),
`ChannelBridgeTests` (14), `ChannelInboundRecoveryTests` (41),
`CodexStartupDeliveryTests` (62), `LandEvidenceWarningDeliveryTests` (7), and
`WorktreeManagerGitIntegrationTests` (15). Do not double-dispatch the last class.

For each F001-F140 retain original Windows failure, repair commit, native tested
SHA, exact method/argument identity, counts and evidence. Separate product,
fixture/platform, environment and unexplained intermittent failures. Repeated
passes do not prove that an intermittent failure cannot recur. Use the existing
native tasks' explicit retry policy only for their suspected flakes. Read each
test's actual SDK selection (`global.json` permits latestMinor), build SDK and
audit provenance; do not infer SDK mismatch from an installed version alone.

Each of the 538 skip rows needs either successful execution or a legitimate,
reviewed, platform/method-specific policy disposition with reason and owner.
Keep required Windows native obligations required. Policy changes require a new
hash/candidate; no blanket "skip to green" or environment-variable opt-in for live
provider canaries. `antiphon/all-split-18`'s 132 skipped / zero executed is incomplete.

Native owners build once and run a single affected class/method at a time through
their approved checkpoint manifest. The already commissioned native rerun briefs
authorize a direct compiled fallback for the checkpoint reader's 64 MiB limit:

```powershell
# In the native owner's clean, pinned worktree, not in the retained release clone.
$project = 'Antiphon.Tests'
$class = 'BaselineComparerTests' # replace with the one assigned class
pwsh -NoProfile -File scripts/build-slot.ps1 -Label wq-native-build -- `
  dotnet build "tests/$project/$project.csproj" --property:OutputPath=bin-wq-native/
if ($LASTEXITCODE -ne 0) { throw "build exit $LASTEXITCODE" }
$exe = Join-Path $PWD "tests/$project/bin-wq-native/net9.0/$project.exe"
$resultDir = Join-Path $evidenceRoot ($class + '-' + [guid]::NewGuid().ToString('N'))
pwsh -NoProfile -File scripts/build-slot.ps1 -Label wq-native-class -- `
  $exe --treenode-filter "/*/*/$class/*" --results-directory $resultDir `
  --report-trx --report-trx-filename native.trx
if ($LASTEXITCODE -ne 0) { throw "native row exit $LASTEXITCODE; retain evidence" }
```

The executable is already compiled: do not pass dotnet's `--no-build` to the test
binary. Equivalent `dotnet run --no-build --property:OutputPath=bin-wq-native/`
must use the same project/output. No build per class. Checkpoint import failure is
not permission for every future task to bypass checkpoints; carry the explicit
fallback reason, lease, source/build provenance, timeout and counts in its brief.
The outer build-slot wrapper does not supply an execution deadline: run this
fallback only inside that native owner's bounded, process-owning supervisor, with
the measured row limit supplied by its verification design. Do not substitute the
task's conversational time budget for a process timeout.
Delete only the owner's `bin-wq-native/` outputs after all children have exited.

### 2. Extract existing timing without another test run

Have the evidence owner run this read-only aggregation on the execution host or
on an already authorized evidence copy. `$oldRunDirectory` must be the exact
8f055f87 directory named by its launch record, never the newest directory. This
does not call the summarizer, which would rewrite derived files in the old run.

```powershell
$s = Get-Content -LiteralPath (Join-Path $oldRunDirectory 'summary.json') -Raw | ConvertFrom-Json
if ($s.runId -ne '8f055f87452d4c76998604df0b522db5' -or
    $s.sha -ne '105792f7d22f297746cdb338976f52e424b76fa7') { throw 'wrong historical run' }
$p = Join-Path $oldRunDirectory 'timing/groups-executed.json'
$chunks = @()
if (Test-Path -LiteralPath $p) {
  $chunks = @((Get-Content -LiteralPath $p -Raw | ConvertFrom-Json).chunks)
}
$rows = foreach ($r in $s.suites) {
  $c = @($chunks | Where-Object { $_.suite -eq $r.id -and $_.chunk -eq $r.chunk -and $_.runId -eq $s.runId })
  if ($c.Count -gt 1) { throw "duplicate timing $($r.id)/$($r.chunk)" }
  $seconds = $null
  if ($c.Count -eq 1 -and $null -ne $c[0].hostWallSeconds) { $seconds = [double]$c[0].hostWallSeconds }
  [pscustomobject]@{ suite=$r.id; chunk=$r.chunk; wallSeconds=$seconds; timedOut=$r.timedOut }
}
$rows | Group-Object suite | ForEach-Object {
  $known = @($_.Group | Where-Object { $null -ne $_.wallSeconds })
  [pscustomobject]@{ suite=$_.Name; rows=$_.Count; measured=$known.Count;
    unknown=$_.Count-$known.Count; knownSeconds=($known | Measure-Object wallSeconds -Sum).Sum }
} | Format-Table -AutoSize
$rows | Sort-Object wallSeconds -Descending | Select-Object -First 25 | Format-Table -AutoSize
```

Retain individual chunk rows, run/source identity and a digest of each input.
Partition Antiphon/E2E by group, split and exact-method rows; derive per-suite sum,
median, p95 and maximum **launch wall** separately from summed TRX test durations.
This distribution across different launches is descriptive, not the comparable
same-selection p95 needed to set a row timeout. Report absent timings as unknown.
Subtract the known serial launch-wall sum from
68,220.305 s and label the residual build/preflight/discovery/analysis/reporting or
unknown only where timestamp evidence supports it. Never subtract overlapping
phase spans as though they were serial work. Sample size one is not p95.

Do not blindly run `refresh-nightly-class-costs.ps1` over every historical TRX:
it sums all outcomes/repetitions, labels rows `trx`, drops row-specific source text,
and cannot account for classes without TRX. Use a candidate output under the
evidence root, inspect complete same-host/SHA case sets, preserve provisional rows
for partial/skip-only classes, then review the intentional JSON diff. The optional
generation command is below. Do not lower a healthy-case allowance using the
short duration of a failed setup/assertion; keep its conservative bound until
complete comparable execution supports the change.

```powershell
# $completeComparableTrx is an explicit audited list, not a directory wildcard.
& scripts/refresh-nightly-class-costs.ps1 -TrxPath $completeComparableTrx `
  -OutputPath (Join-Path $evidenceRoot 'proposed-class-costs.json') `
  -Source 'Windows 8f055f87452d4c76998604df0b522db5 at 105792f7; audited complete classes only'
```

### 3. Repair, top up, and freeze

Run S1/S2 below alongside the existing diagnosis on portable lanes. Land reviewed
bug/test fixes and their prerequisites into the rc line through the existing
integration owner, including the queued top-up 8. A pushed branch is not proof of
rc inclusion. Record the exact selected commit list and new remote rc SHA, then
rerun only changed native witnesses whose earlier receipts predate those fixes.
No exact top-up cherry-pick command is supplied because the brief supplies no
authoritative ordered top-up commit manifest; do not invent it from branch names.

Require the full F/skip/missing matrix to be closed, bounded group projections,
multi-suite timing retention, and the Windows timing witness/overhead check before
the Full run. The still-expensive Full run has a CARD-1514 Debug obligation and
timing evidence even if every launch is below 30 minutes. Continue portable repair
work while Windows owns native verification; stop integrating into the frozen rc.
If publication from this attempt is required, satisfy D-8 before this freeze.

### 4. Establish a reboot-free execution window

Do this after the current native owners finish and before the final cut. The
maintenance owner checks pending restart state and managed update deadlines,
completes any already-required reboot, and verifies the OS/toolchain/Docker/slot
broker after boot. Record boot identity, OS/PowerShell/SDK/Git/browser versions,
available memory, native-lock observation and exact source SHA; a healthy service
alone does not prove it loaded the intended code.

Use Windows Settings' supported temporary update pause through the planned window
and record its restoration time. Active hours alone are insufficient: managed
deadline/grace expiry can force a restart irrespective of active hours. See
[Microsoft's update policy documentation](https://learn.microsoft.com/en-us/windows/deployment/update/update-policies)
and [temporary update pause](https://support.microsoft.com/en-us/windows/deployment/updates-lifecycle/pause-updates-in-windows).
Verify effective managed policy on this host; a UI setting is not proof against
an enforced deadline. Arrange power/sleep protection and an execution owner that
survives loss of the interactive caller, using the established qualification
supervisor. Detaching alone cannot survive an OS reboot. Do not disable update
services/tasks, change registry policy, or cancel another owner's restart.

If the maintenance owner cannot establish the window, defer Full; use the time
for repairs and measurement. A reboot/interruption keeps the old attempt red or
incomplete. Prove all owned children gone and use the release owner's stale-lock
recovery procedure before a new attempt; no automatic partial resume or lock deletion.

### 5. Journal and run the final pinned candidate, without publishing

In a foreground owning supervisor, from `$coordinatorRoot` at approved final-rc
scripts. Keep its launch record outside the disposable checkout, including the
supervisor PID/start, roots, candidate, full SHA and RunId. The diagnostic Plan
does not launch this command or select a host. Ensure the existing native tasks
and any scheduled native lane have released their ownership first.

```powershell
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'native Windows required' }
$rcRef = 'rc/2026-10-27'
git fetch origin $rcRef
if ($LASTEXITCODE -ne 0) { throw 'fetch failed' }
$pin = (git rev-parse "origin/$rcRef").Trim()
if ($LASTEXITCODE -ne 0 -or $pin -cnotmatch '^[0-9a-f]{40}$') { throw 'invalid pin' }
$cut = & scripts/release-candidate.ps1 -Ref $rcRef -ReleaseRoot $releaseRoot `
  -CheckoutRoot $checkoutRoot -PassThru
if ($cut.ExitCode -ne 0 -or $cut.Sha -cne $pin) { throw 'cut refused or rc moved before cut' }
$candidateId = $cut.CandidateId
$candidateRoot = Join-Path (Join-Path $releaseRoot 'candidates') $candidateId
$stateRoot = Join-Path $candidateRoot 'state'
$runId = [guid]::NewGuid().ToString('N')
# Persist the above identities/roots in the supervisor launch record before starting.
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-final-full -- `
  pwsh -NoProfile -File scripts/nightly-run.ps1 -Profile rc -Trigger rc `
  -Ref $rcRef -ExpectedSha $pin -CandidateId $candidateId -RunId $runId -Attempt 1 `
  -SelectionMode Full -CheckoutRoot $checkoutRoot -StateRoot $stateRoot `
  -LogRoot (Join-Path $candidateRoot 'logs') -ReleaseRoot $releaseRoot `
  -CoordinationRoot $coordinationRoot
if ($LASTEXITCODE -ne 0) { throw "qualification exit $LASTEXITCODE; retain the attempt" }
```

There is one slot-owning outer wrapper; the inspected nightly driver does not
take nested build slots. Require actual grant/continued ownership/release evidence;
slot exit 4 is not run, not a reason to bypass admission. No `-NoReport`, seam,
partial `-Suites`, reduced selection, `-AllowSharedTree` or fabricated marker.
The owned release clone builds into its own `bin/`; alternate outputs are for
native task worktrees whose daemon binaries must remain untouched.

The shorter supported alternative is the following **instead of**, not after,
the preceding cut/run pair; freeze/read back its returned candidate SHA:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-final-full -- `
  pwsh -NoProfile -File scripts/release-cut.ps1 -Ref rc/2026-10-27 `
  -SelectionMode Full -RunId $runId -Attempt 1 -ReleaseRoot $releaseRoot `
  -CoordinationRoot $coordinationRoot -SkipPublish
```

`-SkipPublish` is essential: without it release-cut calls the publisher on green.
Candidate journaling can be idempotent; that is not partial test reuse. An
identically bound self-reexec uses ContinueRunId internally to inherit live lock
ownership. Operators must not use ContinueRunId to revive a dead run. For another
attempt at the same SHA use a new RunId and increment Attempt; after a source
change journal a new candidate with Attempt 1. Retain old attempt documents.

### 6. Observe and accept

From the execution host, with variables from this specific launch:

```powershell
pwsh -NoProfile -File scripts/rc-qualification-status.ps1 `
  -CandidateId $candidateId -RunId $runId -StateRoot $stateRoot `
  -CoordinationRoot $coordinationRoot -ReleaseRoot $releaseRoot
```

Observe every five minutes and at group boundaries. `lastActivityAt` is a
transition timestamp, not a heartbeat or ETA. Record the current group/selection,
terminal UID progress, launch elapsed/budget, memory, slot wait/held time and
remaining measured work. Retain planned/executed manifests and per-test/phase
sidecars throughout; do not rely on a summary generated only after 19 hours.
An unknown owner or old timestamp alone is not stop authority. A failed group
gets Debug with its full evidence; repairs remain outside the frozen checkout.

Acceptance requires the recorded pinned SHA/ref/candidate/run/policy, all ten
required suites, exact required UID union, zero required failures/skips/missing
TRX, successful owned teardown, fresh client/script roster evidence, actual
boolean `coverageComplete=true`, `testsPassed=true`, `reportDelivered=true`, exit
0, and the candidate's `complete-green.json`. Read back the remote rc tip again.
No Linux credit, copied passes, repaired historical summary, or inherited-red
waiver. Count legitimate exclusions separately. Retain the historical failed run.

For publication, additionally require the real producer's frozen authority and
full execution ledger and a successful read-only publisher `-WhatIf` validation.
The current missing producer must be repaired through CARD-1313 S6 before claiming
that readiness. Then present the exact SHA, reservation and validated evidence to
the human owner for publish/tag/promote. No publication command without `-WhatIf`
is commissioned here. Qualification, publication and host activation are distinct.

## Implementation slices for separate commissioning

| Slice | Files and work | Tests/evidence; exit condition |
|---|---|---|
| S0: timing and closure census | Existing digest/native report artifacts; read `summary.json`, TRX, `timing/groups-executed.json`, phase shards and discovery without editing them | Per-suite wall split, top groups, exact skip/missing mapping, native closure matrix, sample/host/load provenance; no new test run just to obtain already-retained times |
| S1: cost correction, CARD-1404/1514 | `scripts/lib/nightly-class-costs.json`; `scripts/test-nightly-tests.ps1` and `tests/Antiphon.Tests/Scripts/C1514LandingSplitBudgetTests.cs`; retained compact input fixtures under the existing test-fixture conventions | Commit failing independent group-28/65 planner witnesses first; correct complete-class prices/splitting and E2E prices only with evidence. Assert every required expanded UID once, excluded zero, canonical filters, ceilings, unchanged watchdog/verdict. Keep partial-price provenance |
| S2: multi-suite timing, CARD-1585 | `scripts/lib/test-phase-evidence.ps1`, its calls in `scripts/lib/nightly-tests-impl.ps1`, `scripts/summarize-test-phases.ps1` only if needed; `tests/Antiphon.Tests/TestHelpers/TestPhaseSupervisorTests.cs`, `TestPhaseSummaryTests.cs`; update `docs/testing-and-build.md` | Retain immutable per-suite plans plus a run index or a versioned run-wide manifest with suite-specific bindings; use run/attempt/suite/chunk identities in joins. Add red multi-suite overwrite and join witnesses; preserve legacy single-suite reading, unknown values, failure isolation and gate immutability. Windows/PS5.1 witness and <2% paired instrumentation overhead owed |
| S3: rc integration and native closures | Only the reviewed top-up/repair files named in each native diagnosis; policy dispositions only where semantically justified | Separate implementation briefs supply exact class/method checkpoints and positive witnesses. Product fixes get ordinary review. Exact integrated SHA passes affected native rows; current incomplete classes and skip census accounted |
| S4: final qualification | No further source edits; commands above, approved owned clone and external evidence | One complete Windows Full attempt; release credit and timing quality checked independently; operator retains tag/publication/promotion decisions |

S1/S2 can share an assigned implementation task/build if their file scopes do not
collide with other owners. Do not fold S3's unknown product repairs or the
CARD-1313 authority feature into an unbounded implementation task. A broad shared
fixture rewrite is deferred unless S0 establishes a specific worthwhile repair.

## Performance budget

Applies [testing/build performance standards](../../testing-and-build.md#performance-standards).
This Plan itself runs no tests/builds. Published timings above come from one
failed Windows run; its load, per-phase measurements and comparable sample p95
are unavailable in the digest. They are **not measured p95**. The native rerun
tasks each have a 120-minute commissioning budget, not a promise that their
entire assigned roster will fit. Report remaining rows honestly.

| Phase/selection | Planning allowance | Evidence, timeout policy and sharing |
|---|---|---|
| Read-only S0 extraction | 15-30 min owner effort | Reuse retained files; no infrastructure setup or native tests |
| S1/S2 portable verification | Build 3-10 min once; planner/supervisor selections 1-5 min each | Provisional CARD-1514/1404 estimates. Existing planner sample: 45.955 s for four methods with one historical failure; exact repaired full-listing method 28.114 s. Not a native-performance receipt |
| S2 Windows witness | Build 5-15 min once; selected tests 2-5 min provisionally | No p95 yet. Collect paired disabled/enabled recorder timings; overhead below 2%; retain any missing measurement as open |
| Native diagnosis + affected post-top-up rows | Up to three existing 120-min task envelopes serially on one Windows host; repairs/integration additional and unknown | Do not duplicate their runs. Any class/selection over 30 min gets Debug under CARD-1514. Fresh native SHA/counts required |
| Final build/client/browser prerequisites | Provisional 10-30 min, inside final-run envelope | npm install/build/lint, solution build, browser install once in owned clone. Existing npm caps 10 min each; dotnet build cap 20 min. Slot wait separate, default acquisition bound 45 min |
| Docker/readiness/migration/seed/clone/host startup/body/teardown | Exact shares unknown until S0; record each rather than assign fictional zeros | Share expensive setup once per process/session. Isolate cases through templates/schemas/snapshots; explicitly measure required fresh crash/start witnesses |
| Final Full attempt, including prerequisites/report | Working forecast 19-24 h after readiness; reserve 24 h stable window, not a test timeout | 18.9501 h is one failed run. Pricing fixes may add launches; formerly skipped/missing cases add work. A tighter ETA requires S0 plus repaired-roster measurements |
| Report/teardown/acceptance | Provisional 5-15 min, inside final envelope | Joined child cleanup, report delivery, preserved artifacts; unknown remainder stays explicit |

The forecast excludes unresolved fix development, queue delay, maintenance/reboot,
and additional full attempts. Earliest calendar completion is therefore native
triage/repair readiness plus roughly one day; **no guaranteed same-day green** is
supported. A repaired price reduces risk, not necessarily runtime. Calculate a new
ETA as the sum of comparable launch-wall predictions plus measured build/preflight
and reporting overhead; show known/unknown coverage and confidence, not a sum of
test-duration spans masquerading as wall time.

Before implementation execution, TestDesign must set explicit row/operation
deadlines near **2x measured p95** using comparable Windows/source/load samples,
or label a bounded provisional exception against CARD-1514/1404/1563. Collect
from already-commissioned native runs and the next required qualification rather
than repeatedly run the entire suite just to estimate a percentile. For small
samples report N, max and provisional bound. The current importer uses a 15-min
floor/3x estimate; do not present that default as compliant p95 policy. Pin row and
execution/wall allowances in the imported manifest after measurement.

This plan changes no production/test watchdog. Existing 60-min Antiphon, 30-min
SessionRunner and 120-min E2E launch caps are legacy, documented exceptions;
the new packing must keep ordinary launches below 30 min (prefer 15 min), and
the E2E projection at or below 1800 s. Any measured launch or total over 30 min
has a Debug owner, even if it remains legitimately long. Bounds must separately
cover database readiness, HTTP, process exit/drain, filesystem/git calls and
teardown; a whole-test deadline is not sufficient phase attribution.

Record source SHA, OS/SDK, host/load, run/attempt/suite/chunk/test identity,
assembly/policy/cost digests, direct project graph/downstream categories, TRX
durations, planned/executed membership, phase spans, external-call spans and
remainder under the run's ignored `timing/` evidence. Slot/admission waits remain
separate from execution. Pure pricing/planning regressions use no real DB or
browser; their existing process probes remain limited and owned. Native E2E uses
the isolated random runner and disposable DB/broker, never production endpoints.

## TestDesign handoff and proposed verification scope

This brief did not fold TestDesign into Plan. TestDesign must finalize measured
timeouts, exact new method names/counts and fixtures, including historical
group-28/65 membership, before Code uses this closed list. No full Unit or full
assembly run is requested for S1/S2. The final RC roster is S4's separate gate.

- V-1: independent measured-cost witnesses fail on the old underpriced group-28/65
  projection; repaired projection retains each required expanded UID exactly once,
  no exclusions or suffix collisions, and unchanged watchdogs.
- V-2: two different suites using the same chunk name survive plan writing with
  their own source/assembly/policy/cost bindings; final-suite writing loses nothing.
- V-3: summaries join by run/attempt/suite/chunk/test; legacy single-suite input,
  missing/torn/ambiguous data and unknown timing remain honest. Sidecar failures
  do not turn red TRX green or alter release credit.
- R-1: existing four `C1514LandingSplitBudgetTests` cases preserve E2E/group10/
  group71/full-listing coverage and watchdog ceilings. Update measured arithmetic
  explicitly; never delete a failing census assertion.
- R-2: `ReleaseGateRunTests.C599_CreditVerdicts` retains Full/real-boolean/SHA
  semantics. No baseline waiver or selected execution is introduced.
- W-1: actual Windows file visibility, native custody and PS5.1 compatibility,
  multi-suite retention, and paired recorder overhead below 2%.

### Checkpoints

Lane is named in `Group`: portable rows do not pin a platform/host; Windows rows
require `-Platform Windows` at commissioning. Build output reuse is only within
one `After` group and one platform. Proposed new single-result methods are
`C1585_MultiSuitePlans` (supervisor) and `C1585_MultiSuiteJoin` (summary);
`C8f055_TimedOutGroupBudget` has two argument results (28 and 65). TestDesign
must confirm these expectations against its committed roster. Linux excludes the
existing Windows-only summary witness explicitly instead of accepting a skip.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-wq-portable/` | portable-supervisor | `/*/*/TestPhaseSupervisorTests/*` | V-2 | all 3 proposed results, 0 failed/skipped | 3 | 10 | true |
| CP-2 | S1-S2 | CP-1 | portable-summary | `/*/*/TestPhaseSummaryTests/(C1514T_MergeAndQuality*)\|(C1585_MultiSuiteJoin*)` | V-3 | 6 existing argument results plus 1 new, 0 failed/skipped | 7 | 3 | true |
| CP-3 | S1-S2 | CP-1 | portable-costs | `/*/*/C1514LandingSplitBudgetTests/*` | V-1, R-1 | 4 existing plus 2 new argument results, 0 failed/skipped | 6 | 3 | true |
| CP-4 | S1-S2 | CP-1 | portable-credit | `/*/*/ReleaseGateRunTests/C599_CreditVerdicts` | R-2 | exact method, 0 failed/skipped | 1 | 1 | true |
| CP-5 | S1-S2 | `tests/Antiphon.Tests -> bin-wq-windows/` | windows-timing | `/*/Antiphon.Tests.TestHelpers/(TestPhaseSupervisorTests*)\|(TestPhaseSummaryTests*)/*` | V-2, V-3, W-1 | all 11 proposed expanded results, 0 failed/skipped; retain overhead evidence | 11 | 15 | true |

Provisional costs include build where present. Rerun only the failed/touched row
after a repair. Commit the failing witness before its fix; preserve that red and
the repaired row separately. TestDesign must bind the overhead probe to W-1's
actual fixture; a generic passing timing test is not proof of the 2% bound.

Suffix audit at the inspected source found no sibling class with prefixes
`TestPhaseSupervisorTests`, `TestPhaseSummaryTests`, `C1514LandingSplitBudgetTests`
or `ReleaseGateRunTests`. The existing summary method prefix is unique. Recheck
new methods and the final discovery roster before execution. Every OR operand is
parenthesized and ends in `*`; table pipes are escaped. Single exact methods stay
exact. Positive result count and intended executed names in fresh TRX are mandatory.

Run each committed slice group through the checkpoint tool after TestDesign binds
the platform rows and explicit budgets, for example:

```powershell
# Bootstrap tool build, if needed, must itself use scripts/build-slot.ps1.
# Once a qualified tool image exists, run it directly; checkpoint rows own slots.
dotnet tools/Antiphon.Checkpoints/bin-wq-tool/net9.0/Antiphon.Checkpoints.dll run `
  --plan docs/superpowers/plans/2026-10-27-card-1514-windows-requalification-plan.md `
  --rows CP-1,CP-2,CP-3,CP-4
# On exit 75, continue wait for the printed run ID until final exit, retaining all receipts.
dotnet tools/Antiphon.Checkpoints/bin-wq-tool/net9.0/Antiphon.Checkpoints.dll wait `
  --run $checkpointRunId --max-wait 570s
# Use only CP-5 on the Windows owner. Never end the owner while its executor is running.
```

Tool bootstrap and alternate-output cleanup are separate accounted operations;
remove all task-owned `bin-wq-*` outputs only after runs finish. A 64 MiB reader
refusal stays a tooling failure requiring a separately commissioned remedy or
explicit fallback, never a passing checkpoint.

## Operator decisions and stop points

1. Choose and confirm the final rc integration manifest and a stable maintenance
   window. Human maintenance policy may be required for update controls; no current
   desktop setting was read or changed by this task.
2. Decide whether this cycle needs only the requested passing coverage-complete
   Windows qualification, or a publishable release from the same run. The latter
   requires the separately reviewed Full authority producer before the final SHA
   is frozen; an operator decision cannot waive missing authority.
3. After green and valid authority/readback, humans own tags, publication and
   promotion. The old CARD-1350 incomplete/inherited-red acceptance does not meet
   this task's requirement and confers no publication credit.

Routine timing extraction, narrow repairs, native preflight and safe cost fixes
need no additional go-ahead beyond their commissioned scope. This plan introduces
no session that waits for user input; no parking/release deadline is changed.

## Plan validation

Read-only API/Git/source inspection established the table and commands. No test
or build was run, and the old Windows artifacts were not accessed directly.
Static document, relative-link, PowerShell-syntax, filter/roster and Git whitespace
checks are recorded with the plan's completion report. Exact per-suite elapsed
times and native repair closure remain explicit execution prerequisites, not
claimed measurements or successful results.

--- next stage ---
next: test-design
handoff: Bind S1/S2 to retained Windows timing and exact group-28/65 rosters; finalize narrow cost/manifest regressions, Windows overhead witnesses and measured deadlines. Coordinate existing native owners and close skip/missing coverage before one pinned Full run; preserve the separate publication-authority hold.
artifact: docs/superpowers/plans/2026-10-27-card-1514-windows-requalification-plan.md
