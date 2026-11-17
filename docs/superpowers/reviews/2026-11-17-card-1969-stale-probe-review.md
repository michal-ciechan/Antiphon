# CARD-1969 slice 1 Review: stale-Working probe stamp and bounded tail

- Review task: 3e32f1e6 (Claude Opus; author Grok, Code 277baf57)
- Round: 1, Final, ordinary scope Full
- Subject: `origin/feat/card-task-277baf57`, tip `c5a5367c94f955785f3c047f840c5c4685383a85`
- Range: `9920f248aff1fa59352c58d272dc7aafbe302f3a..c5a5367c94f955785f3c047f840c5c4685383a85`
  (`git cherry origin/master c5a5367c9`: the CARD-1888 commits are on master; the four `card-1969` commits
  are this branch's own work.)
- Plan: `docs/superpowers/plans/2026-11-17-card-1969-stale-working-probe-plan.md` (master)
- Master at review: `7d67cf93183b1643344c99039f8219b7d93ab2e2`

## Verdict: CLEAN

No blockers and no regressions. One advisory, filed as CARD-1978. It does not hold the land.

## 1. Equivalence (delivery evidence)

`RecoveryUnchanged` (`AgentSessionRuntime.cs:1172`) is used for the second read (`:1102`) and at the commit
fence (`:1607`). If either side is `Full`, it compares the whole array, as before. If both sides are partial,
it compares the stamp `(Count, MaxSequence, FirstRowId, TopRowId)` and the 64-row tail by value.

- **One snapshot:** `TailSql` is a single statement. Under READ COMMITTED, the aggregate subquery, both
  seeks and the `LATERAL` tail all read one snapshot. `(AgentSessionId, Sequence)` is unique
  (`IX_TranscriptEntries_AgentSessionId_Sequence`), so `ORDER BY Sequence` has no ties. The first row, the
  top row and the tail boundary are deterministic.
- **Frontier:** if the tail holds an activity row, the session's last activity row is in the tail. If the
  session has 64 rows or fewer, the tail is the whole session. Otherwise the whole read runs (W-7).
  `before.Tail.LastOrDefault(RecoveryActivity)` is therefore the same row the whole-array read selected.
- **Mixed shapes:** the second read and the fence pass `before.Full`/`expectedRecovery.Full`, so a Full
  observation is compared with a Full one. A partial request can fall back to Full only when the newest
  64 rows have no activity and the count is over 64. The arrays then differ in length, so the result is
  `Unknown`/`frontier_changed`. That direction is fail-safe, and the next probe starts Full.
- **Writer walk:**
  - The batch append (`:1580`), its per-row retry (`:1703`) and the stub insert (`:1739`) store at
    `max(e.Sequence, prelude max + 1)`. Each net insert changes `Count`. One above the max also changes
    `MaxSequence`/`TopRowId`.
  - A 23505 collision is a no-op.
  - `DataRetentionService.cs:372` deletes the whole session, which changes `Count`. Recreated rows get new
    `Id`s, which change `FirstRowId`/`TopRowId`.
  - The CARD-1888 prefix memo only reads.
  - No production `ExecuteUpdate`/`UPDATE`/partial `DELETE` on `TranscriptEntries` exists. Grep over
    `server/` and `src/`: none outside the whole-session delete. Deleting a session cascades, and the
    observation then becomes `null` and `Unknown`.
  - Every production change is detected. The only unobserved change is the plan's accepted hole F-1: an
    in-place edit or same-Sequence replacement below the tail. `docs/session-runtime-invariants.md:353`
    records it, and no production writer makes such a change.
- **Wrong RecoveredIdle:** a newly delivered `UserPrompt` is an append, so `Count` changes and the probe
  returns `Unknown`, not idle. The idle decision still needs the runner snapshot to be not-Working,
  `IsWorkingAsync` false after the commit, and the publication lease not Working. All three are unchanged.
  The invariants note says the probe makes no `UserPrompt` presence claim below the tail.
  Transcript-confirmed `UserPrompt` remains the delivery verdict.
- **W-7 and b2f1a869:** `IsActivity` excludes only `TurnEnd`, `TurnTitle`, `SessionRestartBoundary`,
  `Queue*`, `CompactBoundary` and command/continuation prompts. A Working orchestrator's newest 64 rows hold
  assistant and tool rows, so the fallback does not apply there. The fallback needs 64 or more consecutive
  queue or annotation rows at the top of the session.
- **Diagnostics:** `AddedCount` is now `count(Sequence > beforeMax)` instead of an `Id` set difference.
  `PostStoredSequence` is `MaxSequence`. Both feed only the result log line (plan §1). Appends land above
  the max, so they are equivalent.

## 2. The Unknown loop

On a partial observation, no path reads all rows. The probe makes two observations (three on the idle path,
with the fence) plus the stamp-only `AddedSql`. Each is one indexed aggregate over the session's
`(AgentSessionId, Sequence)` tuples, two seeks, and at most 64 full rows. W-1 bounds the materialised rows
at ≤128 on CheckedWorking, and W-2 at ≤193 on idle, for sessions of 300 and 1,200 events.

The 8.4-9.2 s whole read is replaced by an O(index tuples) count. The plan (§10) estimates that count at
10-50 ms (UNOBSERVED). That leaves the runner pull (`GetTranscriptAsync`) as the remaining term inside the
10 s budget. Plan §10 keeps the pull out of scope and names M-6b/M-7 as the post-deploy confirmation.
Expected outcome for b2f1a869: `CheckedWorking`/`runner_working`, with the next probe due in 120 s.

## 3. Witnesses

The roster has 14 cases: W-1 ×4, W-2 ×2, W-3 ×2, W-4 ×1, W-5 ×2, W-6 ×2, W-7 ×1. The plan table's 12/66
undercounts W-6's second argument. The plan's `Min` values are floors, so nothing is invalid; the actual
counts are recorded here.

- **Red at S2:** rerun independently at `5bce70af169726aef02cbc7ee78b7558a06cbfee`, run
  `20261117-170738-21e9`, row CP-2, filter `/*/*/WorkingTranscriptRecoveryTests/C1969_*`. 14 executed,
  9 passed, 5 failed on assertions, not on build or fixture errors:
  - `C1969_middle_row_deleted_between_reads_is_observed` (True, False) and
    `C1969_tail_row_changed_in_place_is_observed` (True, False): `result.Outcome`.
  - `C1969_middle_row_deleted_at_the_fence_is_rejected`: committed `TurnEnd` count.

  This confirms that S2 alone is unsafe and that W-3..W-5 can go red.
- **Red at S1b:** the CP-1 claim (14 run, 8 passed, 6 failed; W-1 600/600/2400/2400, W-2 1201/4801 against
  128/193) was not rerun. It is a bound proof that sits before the fix. Section 1 independently verifies the
  bound mechanism, and W-1/W-2 assert positive row counts (`> 0`). No stub.
- **Counter:** it counts SELECT commands whose text has `"TranscriptEntries"` and `"ApiErrorTimeZoneId"` and
  no `INSERT`.
  - EF `SaveChanges` sends INSERT … RETURNING batches without a SELECT of the whole-row projection.
  - The old whole-row LINQ read, the new `TailSql` and the fence read are separate SELECT commands, so they
    are counted.
  - The stamp-only `AddedSql` has no whole-row columns and materialises one row.
  - Excluding INSERT text hides no recovery read. Rows are counted per `Read()`.
- **Green at the tip:** rerun at `c5a5367c9`, run `20261117-170714-80ff` (one cold build, 5m02s):

| Row | Filter | Executed | Passed | Failed | Skipped | Wall |
|---|---|---:|---:|---:|---:|---:|
| CP-3 | `/*/*/WorkingTranscriptRecoveryTests/*` | 68 | 68 | 0 | 0 | 79 s |
| CP-4 | `/*/*/(AgentSessionRuntimeTests*)\|(StandingResumeReceiptReadinessTests*)/*` | 60 | 60 | 0 | 0 | 80 s |
| CP-5 | `/*/*/StaleWorkingTranscriptWatchdogTests/*` | 53 | 53 | 0 | 0 | 112 s |
| CP-G | `/*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)/*` | 53 | 53 | 0 | 0 | 59 s |

  That run's verdict is RED for a reviewer-side reason only: I set CP-G's `Min` to 100 against a 53-test
  selection. Every test passed. The clean receipt for CP-3..CP-5 is run `20261117-172111-a620`, listed in
  the Receipts section. Code's own CP-3..CP-5 receipt `20261117-160223-0a36` also validates:
  `CHECKPOINT SOURCE VALID source=c5a5367c9… rows=3`.

## 4. Verification claims and the two refused receipts

`scripts/validate-checkpoint-receipt.ps1 -Evidence <report.json> -ExpectedSourceSha c5a5367c9…`:

| Receipt | Validator | Result | Skipped identity |
|---|---|---|---|
| `20261117-161522-bad5` (guard union, CP-1 114+1) | branch (pre-CARD-1958) | `row_failed`, exit 2 | `WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact` |
| `20261117-161522-bad5` | master `7d67cf931` (CARD-1958) | `skip_identity_unlisted`, exit 2 | same |
| `20261117-164114-e7b3` (derived closure, CP-1 101+1) | branch | `row_failed`, exit 2 | `RcBridgeProbePlatformTests.Windows_probe_reports_a_measured_count` |
| `20261117-164114-e7b3` | master | `skip_identity_unlisted`, exit 2 | same |

The refusals have these causes:

- **Branch validator:** the subject's base `9920f248a` predates CARD-1958 (`16f5b5a79`/`4eb084b79` are on
  master, not in the branch). Its validator refuses any `skipped > 0` row (`row_failed`, line 95).
- **Master validator:** it reads the roster at the receipt's own source SHA. At `c5a5367c9`,
  `WindowsUnitSkipRoster.WindowsOnly` lacks the C1210 entry that CARD-1958 added on master, so the skip is
  `skip_identity_unlisted`.

  After the land rebase onto master, that skip is rostered.
- **RcBridge:** `RcBridgeProbePlatformTests.Windows_probe_reports_a_measured_count` is on no roster, at
  master or at the tip. It skips through a raw `SkipTestException`, so it stays refused even after rebase.
  This is a test-roster defect, not a validator defect and not a CARD-1969 defect. **Advisory A-1:
  CARD-1978.** CARD-1967 already covers the checkpoint tool's own `validate`, which has no skip allowance.
- **Not a blocker:** neither refused receipt covers a CARD-1969 row. They are the Code task's extra guard
  and closure runs, and every test in them passed. The guard batch the brief names runs 53/53 green with
  0 skipped at the tip (CP-G above).
- **Owed classes:** the 127 derived impacted classes (capped) for `AgentSessionRuntime.cs` stay owed to the
  nightly backstop and the rc qualification run. That is consistent with the Final verification profile,
  under which per-task Final does not own the whole Unit lane, and with the operator's minimal-testing and
  waived-mutation rules. PCs stay pending for the method-scoped SourceLanding Mutation.

## 5. Land rule

- Commit `5bce70af1` says "S2 incomplete; S2 and S3 land together".
- Commit `c5a5367c9` says "(S2 and S3 land together)".
- The code comment at `:1170` repeats "S2 and S3 land together".

The CARD-1888 chain is already on master. The Merge must land `470659ad3`, `5f054a4ee`, `5bce70af1` and
`c5a5367c9` in one push and must never publish a tip at `5bce70af1`. The S2-only red rerun above shows that
tip is unsafe.

`git merge-tree --write-tree origin/master(7d67cf931) c5a5367c9` exits 0 with tree `b768a0155`, so there
are no conflicts. Merge 262 (C1192/CARD-1961/CARD-1883) had not reached `origin/master` at review time. The
lander re-runs merge-tree after it lands.

## 6. Scope

- **Changed files:** `docs/session-runtime-invariants.md`, `server/Application/Services/AgentSessionRuntime.cs`,
  `server/Infrastructure/Data/TranscriptRecoveryRead.cs` (new) and
  `tests/Antiphon.Tests/Application/WorkingTranscriptRecoveryTests.cs`.
- **Excluded:** no migration, no index (it reuses the unique `(AgentSessionId, Sequence)` index), no setting
  and no `*.json`.
- **Pins:** unchanged. The diff touches none of the pinned inputs: policyHash `c2e39398…`,
  PinnedInputDigest `7cb7b55c…` (`scripts/test-live-lane-run.ps1`: nightly libs, policy json, c487 diag),
  and LegacyExclusionDigest `6d6ff646…`.
- **`check-evidence-diff.ps1`** over `9920f248a..c5a5367c9`: commits=4, entries=0, violations=0, exit 0.

## Advisories

- **A-1 (CARD-1978):** the RcBridge Windows-only skip is unrostered, so Linux receipts that select it refuse.
- **A-2 (recorded, no card):** the plan's CP-1/CP-2/CP-3 counts (12/12/66) undercount W-6's second argument.
  The actual counts are 14/14/68.

## Receipts

- Tip, CP-3..CP-5 (clean): run `20261117-172111-a620` (68/60/53 green, 0 skipped, wall 8m43s): `CHECKPOINT SOURCE VALID source=c5a5367c9… rows=3`. Run `20261117-171645-8d35` refused `source_changed` (this review file was created during its build).
- Tip, all rows: `20261117-170714-80ff` (RED only on the reviewer's CP-G `Min`; all tests passed).
- S2 red: `20261117-170738-21e9`, in the detached scratch worktree at `5bce70af1`.
