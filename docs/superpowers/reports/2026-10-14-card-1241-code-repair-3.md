# CARD-1241 Code repair report 3 (task 7e815cf9, Final Review 9f1c8a2e D1/D2)

Continues `6cd297c5ec3eac2c53f489d82f8ab72600082233` (repair Code dd7920ee; landing owner 586e863b) on
`feat/card-task-7e815cf9`. Commits: `d0a124faed79ee3194a7127b2af6ba3794d10787` (strict mode, D2, docs, census),
`f158c5d07ab9977cf4c27591fcb3af5937c850f0` and `82f6cd8ccec130861c2e9f772093c1c9c25b33bf` (fixture fixes in the new
scheduler test). No master merge. Mutation not run.

## D1: opt-in strict admission; default limit stated

| Where | Change |
|---|---|
| `BuildDefaults.AdmissionHash` (manifest `build.admissionHash`) | `stat` (absent, the default) or `strict`; `ManifestValidator` refuses another value (field `admissionHash`). |
| `BuildConfinement.AdmitLedger(ledger, strict)` | `strict` hashes each ledger entry whatever its stat. Default rule unchanged. |
| `RunScheduler.BuildOneAsync` | Passes `build.admissionHash == "strict"`. |
| `docs/testing-and-build.md`, `AdmitLedger` and `LedgerStat` comments | The limit, as rows (behaviour, named test, what it asserts). |

Limit, default mode: a non-output file changed during the slot wait through a shared memory mapping, or by any writer
that does not update the ctime, keeps its stamp and is not hashed at admission; when the build then writes the old bytes
back, the after-release hash sees no difference. A difference still present after release is reported. Strict mode closes
it at the in-slot hash cost the stat rule avoids. Tests:
`shared_mapping_style_change_with_an_unchanged_stat_is_refused_at_admission_only_in_strict_mode` (stat provider returns
the captured stamp after A -> B: default 0 hashes and no refusal; strict hashes the ledger size and refuses `tampered`
with the exact path; after A is written back `VerifyLedger` is clean) and
`strict_admission_hash_in_the_manifest_refuses_the_build_before_launch` (scheduler, manifest round trip: default run
launches the build, state `ok`, file holds A; strict run does not launch, `refused: tampered`, TAMPERED line, B kept).
`admission_hash_other_than_stat_or_strict_is_rejected` covers the field.

Sentences removed or rewritten (`git diff 8995111253..HEAD`): repair-2 heading ("cannot show"); repair-2 Linux paragraph
(ctime "moves on content writes") now names the write-plus-utimes test and the shared-mapping limit; 586e863b report
trade-off now cites `non_output_file_changed_before_slot_release_is_refused`; repair-1 `AdmitLedger` row ("any
difference") narrowed to hashed entries and its quoted quantifiers paraphrased; test comments "a write during the slot
wait is now refused" and "proves nothing" qualified.

## D2: portable expectation

`entry_modified_just_before_the_ledger_is_hashed_when_the_slot_is_granted` now (1) injects a complete stamp with an old
ctime so the mtime alone puts notes.txt in the window (1 hash), and (2) captures with `LedgerStat.Portable` and asserts
the ledger keys equal the fixture files outside `.git/` and `App/obj/` (enumerated from disk) and that the hash count
equals their number. `HasIdentityAndChangeTime` mutated to true gives 1 hash in part (2), a red.

## Census

`Get-NamespaceCensus` selected 554 -> 557 (+3 new cases); guard green at `82f6cd8c`.

## Verification (server2, `scripts/build-slot.ps1`, `bin-c1241r3/`, `UseAppHost=false`)

| Run | SHA | Result |
|---|---|---|
| 8 classes | `d0a124fa` | 93/94; `strict_admission_hash_in_the_manifest_refuses_the_build_before_launch` red: the row's slot acquire rewrote B after the build (fixture) |
| Ledger + census | `f158c5d0` | 16/17; same test: default-run `build.log` absent (fake build writes none) |
| 8 classes (`.antiphon/c1241-r3-classes2/classes.trx`) | `82f6cd8c` | 94/94, waited=0s held=646s |
| RunSchedulerTests x5 (`.antiphon/c1241-r3-sched1..5`) | `82f6cd8c` | 15/15 each, waited 0 s |

| Class at `82f6cd8c` | Passed |
|---|---:|
| CheckpointConfinementLedgerTests | 16/16 |
| CheckpointTrackedBaselineTests | 13/13 |
| CheckpointOutputConfinementTests | 5/5 |
| CheckpointReceiptSafetyTests | 8/8 |
| CheckpointSourceExecutionTests | 7/7 |
| OutputCleanupTests | 29/29 |
| RunSchedulerTests | 15/15 |
| CheckpointNamespaceCensusUsageTests | 1/1 |

## Pending positive controls (Mutation)

| PC | Mutation | Expected red |
|---|---|---|
| PC-15 | `AdmitLedger` ignores `strict` | `shared_mapping_style_change_with_an_unchanged_stat_is_refused_at_admission_only_in_strict_mode`, `strict_admission_hash_in_the_manifest_refuses_the_build_before_launch` |
| PC-16 | `RunScheduler` passes `false` for strict | `strict_admission_hash_in_the_manifest_refuses_the_build_before_launch` |
| PC-17 | `ManifestValidator` accepts any `admissionHash` | `admission_hash_other_than_stat_or_strict_is_rejected` |
| PC-14 (revised target) | `HasIdentityAndChangeTime` returns true | adds `entry_modified_just_before_the_ledger_is_hashed_when_the_slot_is_granted` |
| PC-9 (revised target) | `AdmitLedger` drops `recorded.Modified < RacyFrom` | `entry_modified_just_before_the_ledger_is_hashed_when_the_slot_is_granted` (ctime now held old, so the mtime conjunct alone guards it) |
