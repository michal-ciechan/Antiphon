# CARD-1500 qualification status: unbound verification supplement

## Verification design`, binds control locations/assertions and replaces this
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

## Verification design

Code task 05f0443d accepts D-1 as directed by its caller: the automated stop script
remains separate work. The caller explicitly skipped TestDesign and requested the
existing exact rows, fixtures only, no desktop or whole-suite runs. That narrower
selection overrides the generic Final Unit profile for this dispatch. Production
producer/lock/publisher code is unchanged; there is no unbounded shared impact.

`RcQualificationStatusTests` delegates to the C1500 cases in
`scripts/fixtures/c1500-status.ps1`, loaded by the existing release-gate harness.
V-1/R-1 asserts 25 fixture outcomes: exact identity, typed booleans/nulls, early SHA,
failed state, absent/reused/unknown owner observations, foreign native owner,
missing owner start/journal/state, each journal identity mismatch, malformed,
oversized, changed, unreadable and non-file inputs, redacted probe exceptions and
unsafe selection. V-2/R-2 calls the real entrypoint and compares recursive names,
byte hashes and modification times, including dead-owner locks and a master sentinel;
action-command interceptors record any attempted process/network/delete command.
V-3/R-3 holds a private native lock with Write/Read sharing while each actual
PowerShell host invokes the real entrypoint and asserts coherent live identity.
The fixture has no passing platform skip; unavailable Windows means not run.

Tests were committed before implementation. The baseline has no status reader;
a missing-file failure is not a behavioral red. No deliberate production mutation
is performed by Code. All controls below remain **pending SourceLanding Mutation**:

| PC / variants | Guard to alter | Exact method and expected assertion |
|---|---|---|
| PC-1a/b/c/d/e | state run/candidate/profile/SHA comparison; boolean typing | `C1500_StatusIdentityAndObservation`: foreign-state-run/candidate, foreign-profile/sha, string-boolean |
| PC-2a/b/c | journal candidate/ref/SHA comparison | same method: journal-candidateId/ref/sha |
| PC-3a/b/c | process start validation, probe-error handling, foreign-lock relationship | same method: missing-owner-start, probe-error-redacted, foreign-native-lock |
| PC-4a/b/c/d/e | read cap, changed-stamp check, malformed/unreadable/non-file handling | same method: bounded-read, changed-record, malformed-state, unreadable-record, non-file-refused |
| PC-5 | candidate path admission | same method: invalid-selection |
| PC-6a/b | introduce private stale-lock write; forbidden process action | `C1500_StatusDoesNotMutate`: input-bytes-and-times-preserved; no-action-commands |
| PC-7 | change FileShare.ReadWrite to Read | `C1500_WindowsSharedReadAndPs51`: powershell/pwsh-shared-read, no parser/startup red credit |

These candidates are unbound until Mutation reviews the source and discovers any
missing controls; fixture-injected absent/reused observations exercise projection,
not native PID reuse. Manual review covers each destructive boundary, exclusive
start hold, same-volume non-overwriting move, retained bytes, 30-second stop census
and release-cut publication distinction. No live recovery is an acceptance test.

Placement re-read in Code: default server2/Linux; the sole eligible Windows runner
is desktop. This dispatch expressly forbids desktop execution. CP-3 and V-3/R-3
therefore remain pending for a separately authorized Windows fixture task; they are
not passed or silently removed. No whole Unit/class fallback is authorized.


Closed checkpoint manifest: [2026-10-22-card-1500-qualification-ops-plan.md](../plans/2026-10-22-card-1500-qualification-ops-plan.md).
