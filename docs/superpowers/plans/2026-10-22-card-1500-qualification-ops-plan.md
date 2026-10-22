# CARD-1500: Windows rc qualification status and recovery procedures

Plan prepared on 2026-10-22 from `3f54023d6b715fe2653014dc82187d358c0193b6`.
The card was read in full through `scripts/card.ps1 get CARD-1500 -Board Antiphon`.
This is a plan under the scope default D-1, not an implementation or a qualification
receipt. The plan's Docs landing belongs to its caller. Verification design is a
separate stage; the dispatch did not fold TestDesign into Plan.

## Outcome and scope

Provide a repository-owned, read-only `scripts/rc-qualification-status.ps1` and
one documented manual stop/recovery procedure in `docs/release-gates.md`, linked
from the Nightly section of `docs/testing-and-build.md` and from
`docs/nightly-watchdog.md`. Operators can inspect the exact candidate/run, distinguish
process observations from recorded progress, stop a positively identified run, and
preserve a stale shared lock before clearing it under exclusive operational control.

There is a material scope difference to resolve before Code: the full card requests
`rc-qualification-stop.ps1` as well; the dispatch asks for the smaller status script
and documented procedures. This plan follows the latter under D-1. It does **not**
claim to discharge the card's automated-stop acceptance item. If that item remains
required now, revise this plan around an explicit supervisor ownership contract;
do not turn the manual PID checks below into an allegedly safe automatic killer.

No live qualification was inspected or changed in this Plan task. In particular,
the dispatch's supervisor PID 133964 and run
`d2777e21ef4c4a3e9b1fb315ff8f5602` are protected context, not targets or fixtures.
No command was executed on the desktop. Future verification uses only private
fixture roots and children created by that verification task.

## Ground truth

| Card assumption or operational expectation | What the inspected code/evidence does | Design consequence |
|---|---|---|
| A supported status command should describe a qualification. | `scripts/release-status.ps1` answers whether a running SHA is a published release, using `/api/version` and `publications.json`. | Add a separate qualification reader; do not change publication status semantics or call the server from it. |
| The supervisor can be discovered from the native lock. | `New-ReleaseGateLockOwner` in `scripts/lib/release-gate.ps1` records `pid`, `processStartedAt`, `runId`, `lane`, `continuationId`, `acquiredAt`. It has no candidate, supervisor identity, tree inventory or cancellation handle. | A lock owner's PID is not a supervisor PID or permission to stop its ancestors. |
| There is a repo supervisor/status pair to standardize. | The [start investigation](../../investigations/2026-10-22-windows-qualification-0714a9a89-start.md) records task-private `supervise-rc.ps1` and `status.ps1`; `rg --files scripts` has no rc supervisor. `scripts/lib/nightly-probe-supervisor.ps1` is separate probe infrastructure. The private scripts themselves are unavailable in this checkout. | Do not invent their schema or import them by analogy. This change does not add a scheduler or supervisor. |
| A stale native lock can be reclaimed like a per-run lock. | `Enter-ReleaseGateNativeLock` returns `stale-shared-lock` on unsuccessful acquisition when its PID probe is false; it never reclaims that file. `Exit-ReleaseGateNativeLock` removes only a lock this process owns. | Keep acquisition unchanged. Manual stale recovery is a distinct operation after stop/death verification. |
| A false liveness check proves death. | `Test-NightlyProcessAlive` catches every error and returns false; `Get-NightlyProcessStartUtc` catches every error and returns null. | The status reader needs alive / absent / reused / unknown observations. Permission/probe errors must remain unknown. Do not reuse those boolean helpers as death proof. |
| `last-run.json` is a heartbeat and identifies the active executor. | `Invoke-AntiphonNightlyRun` in `scripts/lib/nightly-run-impl.ps1` writes `Started`, fetch/tests and final state. `lastActivityAt` is set at transitions, not continuously during a suite; the record lacks a supervisor PID. | Report recorded phase/time separately from observed liveness. No age-based hang verdict, ETA, automatic stop or progress percentage. |
| Candidate state always lives at a derivable default path. | `nightly-run.ps1` accepts independent `StateRoot` and `LogRoot`; the incident uses `state-2a34ba8c`. Its parameter help explicitly says candidate derivation does not choose private roots. Direct diagnostic runs need not have a candidate journal. | Require the exact state root and run ID. Do not select the newest directory or treat a missing candidate journal as proof no run exists. |
| Reading a lock with ordinary file defaults works on Windows. | `Read-NightlyLockRecord` in `scripts/lib/nightly-common.ps1` uses `FileAccess.Read` plus `FileShare.ReadWrite`; its comments describe the Windows sharing violation with `FileShare.Read` against the live per-run writer. It also collapses malformed/missing/unreadable inputs to null. | Reuse the sharing rule, but retain distinct reader outcomes. A Linux-only test cannot establish native sharing or PowerShell 5.1 compatibility. |
| Stopping a run makes the stored verdict terminal and releases both locks. | Normal `finally` calls release both locks after owned children return. A hard tree kill can bypass that path and leave state/locks stale; `run.lock` has its own reclaim logic in `nightly-common.ps1`. | Preserve stale state as evidence. Do not rewrite it to success/cancelled or apply native-lock instructions blindly to `run.lock`. |
| A watchdog detects and stops this qualification. | `docs/nightly-watchdog.md` describes independent scheduled-nightly outage notification; it never reads Windows files or changes the worker. Suite watchdogs are separate, and missing TRX remains incomplete. | Cross-link the local procedure without changing watchdog deployment, notifications, health or suite deadlines. |

## Decisions

- **D-1 — Scope default requiring caller disposition:** implement the dispatch's
  status script plus manual documentation. Defer the card's automatic stop script.
  Reason: this is the smallest specified fix, and existing records cannot bind a
  supervisor tree to a candidate with the strength an automatic killer needs.
  Rejected alternatives: silently dropping the card item; copying an unavailable
  task-private supervisor; advertising `taskkill /T` with a PID from `native-run.lock`
  as an ownership-checked stop command. The caller can accept this scope and retain
  automated stop as a follow-up, or request a revised plan that includes it.
- **D-2 — Exact, explicit selection:** require `CandidateId`, `RunId`, `StateRoot`
  and `CoordinationRoot`; accept `ReleaseRoot` only to read an optional candidate
  journal. No fleet/host/root discovery and no implicit latest run. This supports
  the actual nonstandard state root and disposable fixtures. Reject searching
  `task-*`, guessing roots, or using a branch name as a run identity.
- **D-3 — Observation, not authority:** output recorded values, their source paths,
  a UTC observation time, identity mismatches and process observation reasons.
  Never issue gate credit, cleanup permission or a stop recommendation from age.
  Reject reimplementing release eligibility in a diagnostic script.
- **D-4 — No runtime producer changes:** leave the nightly runner, lock acquisition,
  process custody and release publisher unchanged. A new tiny reader library may
  hold pure parsing/projection and injectable process observation; do not broaden
  `nightly-common.ps1` globally just to expose this reader. Reject a new durable
  supervisor/heartbeat protocol for this slice.
- **D-5 — Recovery remains a deliberate local procedure:** only the operator who
  establishes the exact supervisor/run relationship and owns the maintenance window
  performs the stop and stale-file move. Unknown ownership or surviving children
  means retain the lock and escalate with evidence. A dead lock owner alone is
  insufficient while descendants may still execute.
- **D-6 — Compatibility and verification:** production scripts remain ASCII and
  Windows PowerShell 5.1 compatible. Portable fixtures cover projection and absence
  of mutation; a separately commissioned Windows fixture covers actual sharing and
  both PowerShell hosts. No live qualification is a test fixture.

## Status contract

Proposed invocation, using operator-provided values from the launch record:

```powershell
pwsh -NoProfile -File scripts/rc-qualification-status.ps1 `
    -CandidateId $candidateId -RunId $runId `
    -StateRoot $stateRoot -CoordinationRoot $coordinationRoot `
    -ReleaseRoot $releaseRoot
```

All required strings must be nonblank; candidate/run values are validated before
using them in paths. Use literal paths, reject linked/reparse input files and
ancestors, and read only the requested state's `last-run.json`, `run.lock`, shared
`native-run.lock`, and optional candidate journal. No recursion, directory creation,
log tailing, transcript/command-line dump, network request, Git operation, marker,
temporary file, process creation or filesystem mutation. Print a compact JSON
object to stdout; `-PassThru` returns that same object for in-process callers.
Any internal observation seam belongs in the library test API, not a public flag
that executes a caller-supplied script.

Read each small record once through a bounded read (1 MiB maximum), sharing with
the live writer. Return `missing`, `malformed`, `unreadable`, `unsupported-path` or
`ok` per input. A file replacement during reading is `changed`; do not combine it
with other records into a coherent snapshot. Retain field-level evidence even
when the overall observation is incomplete. Restrict exception output to stable
reason codes; arbitrary file bodies and exception messages are not the output.

The output includes:

- Requested candidate/run and supplied roots, `observedAtUtc`, and
  `observationComplete` (this describes the observation only).
- State identity: run/candidate, profile, ref, expected/full SHA where present;
  recorded phase, `startedAt`, `lastActivityAt`, nullable exit and coverage/test/report
  flags. Preserve real booleans and nulls; do not coerce strings to true or invent
  terminal state. An early blank `sha` is permitted when `expectedSha` is present.
- Both lock records' identity fields and separate `ownerObservation` values:
  `alive` only for matching PID/start; `absent` only for a definitive not-found;
  `reused` when the PID exists with a different start; `unknown` for missing start,
  invalid identity, access error or unsupported probing. An unrelated native lock
  remains visible as `other-run`; it cannot supply this run's liveness.
- Identity mismatch reasons for wrong run/candidate/profile/SHA. Optional journal
  absence is explicit and does not hide a direct diagnostic run. If present, its
  candidate/ref/SHA must match the state. Missing required state or indeterminate
  required reads yields `observationComplete=false`.

Exit 0 means a coherent observation was returned, including a recorded failed run;
exit 2 means invalid input or incomplete/contradictory observation. Neither is a
test or release verdict. `alive` describes the recorded owner only, never the whole
supervisor tree. Files are independent snapshots: expose a changing/mismatched
identity rather than claiming an atomic view. Process observation on a copied state
directory is meaningful only on its original execution host; the docs must say so.

## Documentation procedure to implement

`docs/release-gates.md` owns a new “Inspecting and stopping a qualification” section
beside “Serialising the two lanes”. It supplies the status invocation, field/exit
meanings and these numbered manual steps with an explicit abort condition at each
destructive boundary. Examples use variables populated from the operator's launch
record, never the live incident's PIDs, roots or task-private command.

1. **Identify and preserve.** Obtain the candidate, full SHA, run ID, exact state,
   coordination and checkout paths, and the supervisor PID/start/script from the
   launch evidence. Run the status reader on the execution host. Preserve the exact
   state and lock bytes and a restricted local process inventory under the operator's
   evidence root; don't publish raw command lines or credentials.
2. **Prove the tree.** Use `Get-CimInstance Win32_Process` and process start times to
   trace the recorded nightly lock owner through the wrapper/build-slot chain to
   the launch-record supervisor. Check candidate/run/checkout arguments at the
   nightly hop, process creation order and descendant membership. A script name,
   path substring, cached PID or heartbeat alone is insufficient. A reused PID,
   missing ancestry, inaccessible identity or unrelated child aborts this stop.
   Never kill all `pwsh`, `dotnet`, `conhost`, runners or processes under a shared root.
3. **Stop the verified supervisor tree.** Under the operator's exclusive control of
   this launch, reobserve PID/start immediately before an explicit
   `taskkill /PID <verified-supervisor-pid> /T /F`. This is manual emergency recovery,
   not an atomic ownership API; if the process generation changed, abort and collect
   a new inventory. Do not substitute the native lock owner's PID for the supervisor.
   Bound the post-stop observation to 30 seconds, checking every previously observed
   identity and any remaining candidate/run descendants. A timeout, unknown identity,
   surviving child or incomplete census is an incomplete stop: retain locks. Do not
   repeat a kill against a now-reused PID. The command's exit code alone proves nothing.
4. **Clear only a proven stale shared lock.** This may also be entered after an
   independently established owner exit. First establish that the recorded owner
   generation and the run's children are gone; retain unknown/reused identities for
   manual investigation. Serialize recovery with all other operators and hold new
   native starts through the existing launch owners for the short recovery window.
   If that cannot be established, do not clear. Re-read the exact `native-run.lock`
   identity and bytes against the preserved copy. A foreign run, changed file,
   malformed record or live/unknown owner aborts cleanup. Move only that exact file
   with `Move-Item -LiteralPath` to a unique, nonexisting evidence filename on the
   **same volume**, without `-Force`, recursion or a wildcard. Keep it as the receipt;
   verify its bytes and the original path's absence before releasing the start hold.
   The operational hold is required: an unchecked read/delete pair is not a
   compare-and-swap. Failure retains evidence and does not authorize a retry against
   a different lock. Do not change `Enter-ReleaseGateNativeLock` to auto-reclaim.
5. **Record the result, then restart only as separately intended.** Keep stop time,
   reason, identities, before/after observations and retained lock path. Do not edit
   `last-run.json`, green files, candidate journals or reports to manufacture a terminal
   result. `run.lock` follows its existing distinct reclaim rules; do not delete both
   lock files as a pair. A later qualification is a new attempt with its own identity.
   Do not invoke `release-cut.ps1` as a diagnostic restart: it publishes on green.

The Nightly section links to this procedure and explains shared versus per-run lock
ownership. The watchdog document links to it from “Windows side” and distinguishes
scheduled-nightly outage alerts, per-suite timeouts and this local rc inspection.
No new watchdog deadline or auto-stop policy is introduced.

## Slices

| Slice | Files and work | Tests / review evidence |
|---|---|---|
| S1 | Add `scripts/rc-qualification-status.ps1` and `scripts/lib/rc-qualification-status.ps1`; implement the explicit-input, bounded reader/projection and process observation contract. Add narrowly scoped C1500 fixture cases to existing `scripts/test-release-gate.ps1`, with private roots and injected observer outcomes. | New `tests/Antiphon.Tests/Scripts/RcQualificationStatusTests.cs`: `C1500_StatusIdentityAndObservation`, `C1500_StatusDoesNotMutate`. Use the existing `ScriptHarness` and `ParallelLimiter<ProcessSpawnLimit>` pattern. |
| S2 | Add the native fixture case to `scripts/test-release-gate.ps1` and `C1500_WindowsSharedReadAndPs51` to the same test class. It holds a private lock open with the real writer's access/share flags and invokes the real status entrypoint with Windows PowerShell 5.1 and pwsh. | Exact native row CP-3; join all fixture children and dispose handles. Unavailable Windows/5.1 is an outstanding row, never a passing skip. |
| S3 | Edit `docs/release-gates.md`, `docs/testing-and-build.md` Nightly, and `docs/nightly-watchdog.md` as above. Keep the procedure in one owner and use links elsewhere. | Inspect commands against actual parameters, link targets, ambiguity/death/census aborts, stale-lock evidence preservation and publication distinction. No prose-only snapshot tests. |

Commit failing behavioural tests before their implementation when a real pre-fix
failure can be exercised. A missing script, parse error or failed fixture is not the
required behavioural red. TestDesign must bind one decisive red control per behaviour
to the actual implemented branch, then one narrow restored green; do not run a broad
suite just to obtain a red. Commit and push each meaningful implementation slice.

## Verification outline for TestDesign

This is the closed proposed ordinary selection, not a claim that verification design
is complete. After D-1 is resolved, TestDesign appends the required
`## Verification design`, binds control locations/assertions and replaces this
outline's checkpoint section with the approved table (leave only one such section).

- **V-1 / R-1:** the requested run is projected accurately; table-driven sibling
  fixtures cover mismatched candidate/run/SHA, missing/malformed/changing records,
  foreign shared lock, absent/reused/inaccessible process, an early state with no
  `sha`, and a completed failure. The decisive red control changes a run-identity
  comparison to accept the foreign run; the exact V-1 method must reject it.
- **V-2 / R-2:** calling the real entrypoint changes no protected input tree, including
  dead-owner locks and master-state sentinels, and invokes no writer or process action.
  Compare inventory and byte hashes plus modification times before/after; the harness
  writes its evidence outside those inputs. The decisive red control adds a write to
  the private stale-lock fixture; the exact V-2 method must fail at preservation.
- **V-3 / R-3:** Windows PowerShell 5.1 and pwsh read the real, writer-held private
  lock without changing it and produce the same allowed snapshot. Check script bytes
  are ASCII. The decisive red control changes sharing to `FileShare.Read`, causing the
  expected coherent live observation assertion to fail under the native writer handle.
  A parser/startup failure is not that red. Native fixtures do not touch machine roots.

No async delivery path or session waiting for input is added. No delivery inventory,
seat-release change, full qualification, publisher test or watchdog deployment is owed
by this scope. Manual stop/recovery documentation receives substantive command/safety
review; fixture status tests do not prove an automatic tree-stop implementation.

Placement was checked through `GET /api/runner-defaults` and
`GET /api/session-runners` on 2026-10-22: an eligible default Linux lane and an eligible
Windows lane existed. These are placement observations, not reservations. Re-read both
routes before dispatch; omit `-Runner`, and omit `-Platform` for the portable work.
CP-3 requires `-Platform Windows` on any eligible Windows runner, with private fixtures.
The current Plan dispatch's desktop prohibition remains in force; it authorizes none
of that future execution. Group names below name the lane without adding an unsupported
checkpoint-table column.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c1500-status/` | portable-status-identity | `/*/*/RcQualificationStatusTests/C1500_StatusIdentityAndObservation` | V-1, R-1 | exactly 1 executed, 0 failed, 0 skipped | 1 | 8 |
| CP-2 | S1-S3 | CP-1 | portable-status-readonly | `/*/*/RcQualificationStatusTests/C1500_StatusDoesNotMutate` | V-2, R-2 | exactly 1 executed, 0 failed, 0 skipped | 1 | 1 |
| CP-3 | S1-S3 | `tests/Antiphon.Tests -> bin-c1500-status-windows/` | windows-status-ps51-sharing | `/*/*/RcQualificationStatusTests/C1500_WindowsSharedReadAndPs51` | V-3, R-3 | exactly 1 executed, 0 failed, 0 skipped; both PowerShell hosts observed | 1 | 10 |

Portable and Windows dispatches select only their named rows through the checkpoint
tool; no Windows row on Linux and no whole-class/assembly fallback. Use one committed
source per group, expected-source SHA and host build slots. Reuse CP-1 output only at
the same source/After group. Preserve each CHECKPOINT line and qualified receipts;
remove owned alternate output directories after completion. Slot timeout is not run.

The ordinary estimate is **19 minutes**, two isolated builds and three TUnit results.
For the requested sensitivity proof, reserve **one method-scoped red plus one restored
green per behaviour**, six control executions total; estimated additional **30 minutes**
including rebuilds (49 minutes total execution estimate, excluding queue time and
authoring). No controls were run in Plan. TestDesign must enumerate all independent
guards and account for any additional safety controls explicitly; the three examples
are a minimum, not a fabricated complete guard inventory. Code runs ordinary V/R;
the separately commissioned Mutation stage owns later source mutations. Existing
same-source green evidence is retained; no repeated green after an unchanged pass.

## Handoff and acceptance

The caller must first disposition D-1: accept the smaller dispatch scope and retain
the card's automatic stop item separately, or require a revised plan for that item.
The current card should not be closed as fully implemented on the strength of this
plan. Once the scope is accepted, the next execution stage is **TestDesign**, which
must complete the verification section before Code. This Plan needs only Markdown
and diff review; no build, desktop activity or qualification execution is warranted.

--- next stage ---
next: decide
handoff: Resolve D-1: accept the dispatch's status script plus manual stop/stale-lock procedure, retaining the card's automatic stop script as follow-up, or request a revised supervisor-ownership design. If accepted, dispatch TestDesign to complete the verification section and closed checkpoint controls before Code.
artifact: docs/superpowers/plans/2026-10-22-card-1500-qualification-ops-plan.md
