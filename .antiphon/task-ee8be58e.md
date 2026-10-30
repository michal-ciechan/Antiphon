# S1 failed-first qualification ordering — ee8be58e

S1 is implemented and verified: CP-1/CP-2 pass with qualified clean-source receipts; next is ordinary Review. No later slice was implemented.

## Source and delivery

- Assigned branch `feat/card-task-ee8be58e`, base `e4c9547e10e3da59fd20684b49c029cdc6063fa8`.
- Final product source: `2f000adead99aa622ad220742a8cbf8fdfac97b6`. A later report-only commit does not relabel these test receipts.
- Both reviewed plan copies were copied first and committed independently. Both SHA-256 values are `effeb52d03efe82f34675a96c877009cb15b6978ffabf110f27d3d71b88c82e6`, matching reviewed plan tip `0df39b19eeb03283b4bf4364e71cd42f3e1646e7`.
- Read reviews `1254ac0d` and `20f68d70`; the round-3 choices govern this implementation.

## Implemented interface

Files are under `/work/worktrees/task-ee8be58e/`:

- `scripts/export-nightly-prior-failures.ps1`: explicit closed-run export; prints the original SHA-256, supports `-PassThru`, and emits a separately labelled error on refusal. New history is written atomically, outside the source run, without overwriting existing output.
- `scripts/lib/nightly-failure-history.ps1`: versioned schema, exact-byte evidence hashes, old-run provenance validation, path/link refusals, pinned MTP reducer with final per-UID state, discovery joins, conservative bound whole-chunk fallback, and current class/UID/chunk resolution. No failure message bodies enter history.
- `scripts/test-nightly-failed-first.ps1` and `tests/Antiphon.Tests/Scripts/RcFailureHistoryTests.cs`: two exact, unparameterized portable TUnit methods; 10 and 26 named inner assertions. Production planner replay retains 19,012 nodes / 150 chunks plus separately generated supplemental fixtures. This is synthetic evidence, not a fresh reading of the desktop's historical artifacts.
- `tests/test-execution-policy.json`: one argument-consumer harness census row; policy hash and catalog pin regenerated through existing producers.
- `tests/test-selection-policy.json`: new paths conservatively map to Full through P-AUTHORITY; catalog hash regenerated and validated.

Full resolution returns loud structured unresolved entries and keeps every resolvable class, including a class without an old chunk number. Partial refuses any historical/current unresolved member and empty input. Matching uses ordinal suite/bare-class identity, current required UIDs and every intersecting existing chunk. It does not reprice, repack or execute a chunk. Old SHA/policy provenance is validated against old evidence, never against the new candidate.

Neither the qualification loop nor coverage, admission, watchdog, ledger, publication or heartbeat implementations changed. No build definition file changed. The two policy files changed only as described above. No `-Update` ran. Scripts remain ASCII and use PowerShell 5.1-compatible constructs. The shared ScriptHarness's temporary cleanup is retained; this harness copies regular synthetic evidence and phase records into ignored `.antiphon/failfirst/<source>/portable/` before returning.

## Verification history — keep the failed outcomes

1. Slot-gated checkpoint-tool bootstrap: 346-second admission wait; 38-second lease, 0 errors / 5 warnings. Imported reviewed plan: 16 rows, seven build declarations. Only S1 CP-1/CP-2 were selected.
2. `20261030-120131-5a45`, source `8f416ff3fbff677bcf7ffbf1418c95fb85144f42`: build passed (MSBuild 11m01.54s, 0 errors / 1,415 warnings in existing files). CP-1 timed out at its unchanged 120-second harness bound after exposing missing union assertions: **not accepted as assertion RED**. CP-2 was genuine assertion RED: 1 outer failure, 25/26 named refusal assertions failed. Total wall 1,584.992s; admission-only 645.188s; execution 939.805s.
3. Baseline lookup indexed by ordinal class once per suite, preserving terminal-only extraction and unresolved-dropping baseline behavior. Corrected fixtures use an actual profile exclusion; recovered InProgress->Passed belongs to a separate complete chunk; removing a plan in one case no longer contaminates later digest probes.
4. One mistyped expected-SHA invocation refused before execution. Runs `20261030-122858-8d4f` and `20261030-123009-ac92` refused the previous source's output stamp (zero tests). The exact owned bin directory was then removed; receipts were retained.
5. `20261030-123034-a266`, source `bd964b8d9978a4de6ef55dc4634365672a94b7f8`: build deadline exceeded at the reviewed 12-minute bound, during staged native-helper work; zero tests. This is not behavioral RED or an inherited-red claim. No timeout was widened.
6. Explicitly unlisted, slot-gated interpreter diagnostic of the exact CP-1 harness at `bd964b8d9978a4de6ef55dc4634365672a94b7f8`: 105-second wait, 58-second lease. Completed **4 passed / 6 failed / 10 named rows**, exit 1. It preserves the expected Error/hang/fallback/immutable-output assertion RED through the real planner. This is not a qualified outer checkpoint receipt.
7. Explicitly unlisted, slot-gated interpreter diagnostics at `ea2e1e22330d7248270cf0e7db801aa3e0d15b88`: 45-second wait, 80-second lease; CP-1 10/10 named assertions and CP-2 26/26, both exit 0. Measured inner wall times 29.620s and 40.158s. These supported source debugging after the build deadline; they do not replace formal CP receipts. Each interpreter had a 130-second outer process bound plus 10-second kill grace; the ordinary ScriptHarness 120-second body / 10-second cleanup and 180-second outer test limits were unchanged.
8. `20261030-125301-8a18` at `ea8e866b055e81981093ab2a230fc73acfe78786` was deliberately stopped before tests after identifying the need to persist phase evidence beyond shared temporary-directory cleanup. Its observer returned 6 after executor stop and no terminal report; no tests are credited. The absolute-path stop command confirmed `stopped`; no owned build remained. The initial relative-path stop was an ownership-missing refusal. Shadow-copy cleanup raced between stop and observer (observer reported Removed); this does not qualify that run.

The final formal run and its unedited CHECKPOINT lines follow below. Historical logs, JSON receipts, TRX and synthetic input archives remain ignored. No generated payload is force-added to Git.

## Checks, placement and limits

- Real land graph: slot-gated `audit-land-verification-graph.ps1 -Check -ToolPath <owned-copy>/Antiphon.LandGraphAudit.dll`, at `ea8e866b055e81981093ab2a230fc73acfe78786`: exit 0, count 68, digest `7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99`, pass2 approved. Wait 210s / held 29s. Evaluator source is unchanged across the task; it was staged by the preceding isolated build. The later harness-only evidence-retention edit changes no definitions.
- Whitespace checks pass. Full-history evidence guard at `ea8e866b055e81981093ab2a230fc73acfe78786`: seven commits, zero evidence entries, zero violations. Final report-tip guard is reported at settlement.
- Fetched master `698bf1b73e8abf449d5569ea74ac8c82e80f92a1`, rc `e4c9547e10e3da59fd20684b49c029cdc6063fa8`. rc merge-tree is clean (tree `f59a8f911ab7d7f8b42e7a737299a50a1add6957` at ea8). Master merge-tree has 12 inherited conflict paths plus this task's `tests/test-execution-policy.json` conflict. Whole-branch master landing is not clean; use the separately reviewed master adaptation. No merge/rebase/target update was performed.
- Reviewed M1/M2 commit `3dd236bb3bb4d843a686256c168ddc84318f1b50` is not an rc ancestor. S2 onward remain unstarted and gated on reviewed integration.
- All runs are portable script/planner witnesses. No native qualification, desktop file access, live run d47848cc operation, shared native lock change, service restart, tag, publication or external message occurred.
- Phase samples remain provisional. No p95 is asserted from these few, differing-source samples. DB/AppHost/Docker/readiness/migration/snapshot/HTTP phases are not used by these cases. Missing individual-call spans are unknown; aggregate fixture/plan/body/archive/CLI/teardown values do not claim complete attribution. Cold test-build staging is near or beyond the 12-minute envelope; retain that performance finding for CARD-1514/CARD-1404/Debug rather than loosening it here.

## Owed work outside S1

CP-3 through CP-14 belong to later ordered slices. CP-15/16 remain **OWED**, never skipped or credited here: a separate S5-W task must request `-Platform Windows` with no host pin after the portable slices. On that commissioned source, bootstrap the isolated checkpoint tool under a build slot, then use:

```powershell
$plan='docs/superpowers/plans/2026-10-30-failed-first-qualification-ordering-plan.md'
$sha=(git rev-parse HEAD) # the commissioned S5-W source, not this S1-only tip
dotnet tools/Antiphon.Checkpoints/bin-failfirst-tool/Antiphon.Checkpoints.dll run --plan $plan --rows CP-15,CP-16 --expected-source-sha $sha --row-timeout 12m --total-timeout 28m --hard-wall-timeout 30m --serial
# If 75: same DLL wait --run <absolute returned run path> --max-wait 1m until terminal.
```

Exact Windows filters are `/*/Antiphon.Tests.Scripts/RcFailureHistoryNativeTests/WindowsHopAndLock` and `/*/Antiphon.Tests.Scripts/RcFailureHistoryNativeTests/WindowsJournalAndScope`. Private supervisor forwarding, Windows commissioning, operational Full qualification and the separate master port remain owed under the reviewed plan. No history has been exported from the inaccessible real desktop run.

## Final qualified S1 checkpoint run

Source `2f000adead99aa622ad220742a8cbf8fdfac97b6`, run `20261030-130534-327d`; receipt validation returned `CHECKPOINT SOURCE VALID source=2f000adead99aa622ad220742a8cbf8fdfac97b6 rows=2`.

Both exact methods executed once: **2 passed / 0 failed / 0 skipped**. CP-1 has 10 named assertions; CP-2 has 26. The isolated build passed with 0 errors / 1,415 warnings; MSBuild elapsed 7m43.22s. The build span in the receipt is 512.5492302s.

Unedited final source/provenance/count/timing lines:

```text
source: 2f000adead99aa622ad220742a8cbf8fdfac97b6 state=clean buildSource=verified
CHECKPOINT CP-1 commit=2f000adead99aa622ad220742a8cbf8fdfac97b6 build=ok filter=/*/Antiphon.Tests.Scripts/RcFailureHistoryTests/EvidenceUnion executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ee8be58e/.antiphon/checkpoints/20261030-130534-327d/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=2f000adead99aa622ad220742a8cbf8fdfac97b6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=43.833s wall=44.400s
PHASES CP-1 slotWait=15s build=512.5492302s startup=4.5236614s testsWall=36.6748438s teardown=0.8965526s hostWall=42.1040515s
CHECKPOINT CP-2 commit=2f000adead99aa622ad220742a8cbf8fdfac97b6 build=reused filter=/*/Antiphon.Tests.Scripts/RcFailureHistoryTests/RefusesIncompleteEvidence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ee8be58e/.antiphon/checkpoints/20261030-130534-327d/rows/CP-2/run.trx slot=granted waited=60s dirty=0 source=2f000adead99aa622ad220742a8cbf8fdfac97b6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=60s executed=68.707s wall=129.012s
PHASES CP-2 slotWait=60s build=0s startup=6.8412228s testsWall=59.7358268s teardown=0.4850026s hostWall=67.0620526s
BUDGET wall=834.829s execution=759.827s admissionOnly=75.002s
verdict: GREEN exit=0
```

Wall time was 834.9684211s; execution 759.8267546s; admission-only 75.0024654s. These are distinct from summed nested phase spans. No required S1 row remains owed.

### Retained final case evidence

- `.antiphon/failfirst/2f000adead99aa622ad220742a8cbf8fdfac97b6/portable/EvidenceUnion-4aad862c485c45c3ac036f8706542e45`: 10 passing named assertions; 16 regular files copied and SHA-256 verified. Inner wall 32.846s, fixture 6.545s, planner 20.716s, archive 0.058s. Host load snapshot `24.25 34.65 41.85 7/3661 1971`.
- `.antiphon/failfirst/2f000adead99aa622ad220742a8cbf8fdfac97b6/portable/RefusesIncompleteEvidence-4a7c983aa8a742798396435727393b12`: 26 passing named assertions; 22 regular files copied and SHA-256 verified. Inner wall 53.166s, fixture 11.636s, planner 26.207s, archive 0.035s. Host load snapshot `40.25 37.53 42.00 6/3822 3309`.

Both archives retain input listing digest `e203deda0430c2a411d4a3fe4f1cd9d23af499469f9d4ab4cc94456356a95216`, class-cost digest `507caee1c73ef5271c285000dc0c53b06aba51c491c339d0b21f1cc0038b288c`, 19,012 nodes / 150 chunks, assertion sequence, measured refusal/JSON-write spans and inventory. Fixture links are recorded and not copied. Shared temporary results were cleaned by ScriptHarness; these archives remain.

### Essential historical CHECKPOINT lines

Earlier failures are preserved, not replaced by the green run:

```text
CHECKPOINT CP-1 commit=8f416ff3fbff677bcf7ffbf1418c95fb85144f42 build=ok filter=/*/Antiphon.Tests.Scripts/RcFailureHistoryTests/EvidenceUnion executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ee8be58e/.antiphon/checkpoints/20261030-120131-5a45/rows/CP-1/run.trx slot=granted waited=405s dirty=0 source=8f416ff3fbff677bcf7ffbf1418c95fb85144f42 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=405s executed=127.021s wall=532.284s
CHECKPOINT CP-2 commit=8f416ff3fbff677bcf7ffbf1418c95fb85144f42 build=reused filter=/*/Antiphon.Tests.Scripts/RcFailureHistoryTests/RefusesIncompleteEvidence executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ee8be58e/.antiphon/checkpoints/20261030-120131-5a45/rows/CP-2/run.trx slot=granted waited=60s dirty=0 source=8f416ff3fbff677bcf7ffbf1418c95fb85144f42 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=60s executed=32.692s wall=92.987s
BUDGET wall=1584.992s execution=939.805s admissionOnly=645.188s
verdict: RED exit=1
CHECKPOINT CP-1 commit=bd964b8d9978a4de6ef55dc4634365672a94b7f8 build=failed filter=/*/Antiphon.Tests.Scripts/RcFailureHistoryTests/EvidenceUnion executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s slot-reason=refused: pre-existing-build-source dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-1 waited=0s executed=0.000s wall=0.000s
CHECKPOINT CP-2 commit=bd964b8d9978a4de6ef55dc4634365672a94b7f8 build=failed filter=/*/Antiphon.Tests.Scripts/RcFailureHistoryTests/RefusesIncompleteEvidence executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s slot-reason=refused: pre-existing-build-source dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-2 waited=0s executed=0.000s wall=0.000s
BUDGET wall=9.128s execution=9.128s admissionOnly=0.000s
verdict: RED exit=2
CHECKPOINT CP-1 commit=bd964b8d9978a4de6ef55dc4634365672a94b7f8 build=failed filter=/*/Antiphon.Tests.Scripts/RcFailureHistoryTests/EvidenceUnion executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s slot-reason=refused: pre-existing-build-source dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-1 waited=0s executed=0.000s wall=0.000s
CHECKPOINT CP-2 commit=bd964b8d9978a4de6ef55dc4634365672a94b7f8 build=failed filter=/*/Antiphon.Tests.Scripts/RcFailureHistoryTests/RefusesIncompleteEvidence executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s slot-reason=refused: pre-existing-build-source dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-2 waited=0s executed=0.000s wall=0.000s
BUDGET wall=10.140s execution=10.140s admissionOnly=0.000s
verdict: RED exit=2
CHECKPOINT CP-1 commit=bd964b8d9978a4de6ef55dc4634365672a94b7f8 build=failed filter=/*/Antiphon.Tests.Scripts/RcFailureHistoryTests/EvidenceUnion executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-1 waited=0s executed=0.000s wall=0.000s reason=build-deadline-exceeded
CHECKPOINT CP-2 commit=bd964b8d9978a4de6ef55dc4634365672a94b7f8 build=failed filter=/*/Antiphon.Tests.Scripts/RcFailureHistoryTests/RefusesIncompleteEvidence executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-2 waited=0s executed=0.000s wall=0.000s reason=build-deadline-exceeded
BUDGET wall=760.362s execution=760.362s admissionOnly=0.000s
verdict: RED exit=5 reason=build-deadline-exceeded
```

## Commits and settlement

All product slices were committed and pushed independently:

```text
1645f4678b04d10d757d0aa889d8ea18e6618148 docs: carry reviewed failed-first qualification ordering plan
1ea3fabb44ed08288a715f1807b0f57550daa88e test: witness retained failure union against terminal-only baseline
a71489f6eabf8e1e3625365a3a94abfb7fad0b38 test: witness fail-closed history resolution and provenance refusals
8f416ff3fbff677bcf7ffbf1418c95fb85144f42 test: bind synthetic diagnostic records to pinned MTP grammar
bd964b8d9978a4de6ef55dc4634365672a94b7f8 perf: index baseline failure-history resolution by ordinal class identity
ea2e1e22330d7248270cf0e7db801aa3e0d15b88 feat: export bound prior failures and refuse incomplete partial resolution
ea8e866b055e81981093ab2a230fc73acfe78786 fix: refuse hard-linked evidence and retain coverage-incomplete fallback
2f000adead99aa622ad220742a8cbf8fdfac97b6 test: retain failure-history phase and fixture evidence before harness cleanup
```

The final report is a separate Markdown-only commit after the tested product tip. It does not claim that the checkpoint tool tested that later report commit. The caller should retain the exact tested product SHA above when comparing receipts. Project alternate output directories `src/Antiphon.HostCleanup/bin-failfirst-tool/`, `tools/Antiphon.Checkpoints/bin-failfirst-tool/`, and `tests/Antiphon.Tests/bin-failfirst-s1/` are absent. Checkpoint build-log directories named `builds/bin-failfirst-s1/` are evidence and remain. All owned runs were joined; the stopped-run shadow copy is absent.

Standing authority covered the work; no additional approval was needed. No tests or assertions were removed or relaxed; no timeout was widened. No full/native qualification or operational failed-first run is claimed.

--- next stage ---
next: review
handoff: Review S1 against rc, including the exact CP-1/CP-2 receipts at 2f000adead99aa622ad220742a8cbf8fdfac97b6 and the report-only delta. S2 remains gated on reviewed M1/M2 integration.
artifact: /work/worktrees/task-ee8be58e/.antiphon/task-ee8be58e.md
