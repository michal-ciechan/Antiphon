# Review d6b0addf: CARD-1656 record-release hardening (round 1 of 3, full)

Verdict: CLEAN. No defects. Reviewed subject tip 71b100052e2ea19b8565b27f3d26c4294d12928c, base 907edaebbd301af95629c31095f1b530803baea2.

## Runs

All runs were on server2 (Linux), one build-slot lease each, native output, `-p:UseAppHost=false`, `TEMP=/tmp`.

- **Tip build:** clean source at 71b100052. Slot wait 360 s, build 2m58s, 0 errors.
- **Tip run:** filter `/*/*/(C1656RecordReleaseHardeningTests*)|(PhoneHomeRollingRunnerTests*)/*`.
  - Total 37, failed 0, skipped 0, duration 1m29s.
  - That is the C1656 class (3) plus the whole partial class PhoneHomeRollingRunnerTests (33 tests across the Rolling, Drain and Retire files). It includes `C1649_RecordReleaseStampsOnlyTheStuckAttemptOfATaskThatIsNotLive` and `C0824_DrainSlotUnknownIsBoundedAndAnOperatorRecordsTheRelease`.
- **Red and mutant build:** one env-gated build (`REVIEW_D6B0_MUTANT`), with the source restored by `git checkout --` right after the build. Each mode was run on the brief's 5-test selection: `/*/*/(C1656RecordReleaseHardeningTests*)|(PhoneHomeRollingRunnerTests*)/(C1656*)|(C1649*)|(C0824_DrainSlotUnknown*)`.

| Mode | Change | Result |
|---|---|---|
| RED | Base production behaviour: tracked stamp with no conditional update, `char.IsControl` only, Ordinal token compare. This is the red commit 30afe1309 plus the compile fix fe16011f6. | 5 total, 3 failed: all three C1656 witnesses. C1649 and C0824 green. |
| M1 | `!HoldingLease.Contains(t.Status)` removed from the conditional update | 5 total, 1 failed: `C1656_ReleaseIsNeverStampedOnATaskThatGoesLiveWhileItIsRecorded` only |
| M2 | Control characters only (rune check replaced by `char.IsControl`) | 5 total, 1 failed: `C1656_RequesterRefusesFormatAndSeparatorCharacters` only |
| M3 | Case-sensitive token compare (Ordinal) | 5 total, 1 failed: `C1656_RequesterRefusesTheOperatorTokenInAnyCase` only |

- **check-evidence-diff:** `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 907edaebb.. -HeadRef 71b100052`. commits=4, violations=0, exit 0.
- **merge-tree onto origin/master** (907edaebb, the branch base): clean, exit 0.
- **merge-tree onto origin/rc/2026-11-01** (10a826b9d):
  - The whole-branch merge-tree conflicts on unrelated master history (`client/src/api/agentTasks.ts`).
  - A per-commit cherry-pick simulation also conflicts. rc has no `WorkspaceSlotReleaseService.cs` (modify/delete), and CARD-0824 and CARD-1649 are not on rc.
  - So CARD-1656 is master-only until those are ported. This is a fact, not a defect.

## Verification points

1. **A-1 atomicity.**
   - The stamp is one `ExecuteUpdateAsync`, i.e. a single `UPDATE "AgentTasks" SET ... WHERE id/attempt/runner/slot/COALESCE(gen,0)/released IS NULL/NOT status = ANY(holding)`.
   - Under Postgres Read Committed, a concurrent committed status change is re-evaluated against the WHERE clause (EvalPlanQual). A task that went live therefore matches 0 rows.
   - The audit insert goes through `SaveChangesAsync` inside the same explicit transaction, which is begun only when none is ambient and committed after the save. On a throw, dispose rolls it back.
   - No retrying execution strategy is configured, so a user-initiated transaction is allowed.
   - The tracked `task` entity is no longer modified, so `SaveChanges` writes only the event.
   - The AgentTask `ConcurrencyToken` is not bumped. The pre-change tracked stamp did not bump it either, so this is not a regression.
   - The previous CARD-1649 behaviour is unchanged: C1649 and C0824 are green.
2. **Error codes and callers.**
   - `workspace_slot_release_changed` is documented in docs/ops-http.md.
   - No contract snapshot, route catalogue, client file or script references the route or any of its codes. A grep over client/, scripts/ and tests/ finds only the server, the docs and the two test files, so nothing else needs an update.
3. **Charset and token.**
   - The charset check is per Rune via `Rune.GetUnicodeCategory`, so astral characters are covered (the U+E0041 tag is tested).
   - It refuses Cc, Cf (U+200B-U+200F, U+202A-U+202E and U+2066-U+2069 are all Cf), Zl and Zp. Combining marks (Mn) are allowed.
   - The token is 64 lowercase hex characters and must match the header exactly (`OperatorTokenFile.Matches`).
   - `OrdinalIgnoreCase` uses simple invariant upper-casing. No non-ASCII character upper-cases to 0-9 or A-F, so case folding cannot bypass the token check.
   - The 400 message is static and echoes neither the requester nor the token. The log line is written only after success.

## Advisories (non-blocking)

- **A-1:** no witness exercises the `workspace_slot_release_changed` branch (a non-live change such as an attempt bump to Queued after the checks). No witness covers the stamp/audit transaction coupling either: removing the transaction would survive the class.
- **A-2:** if the task row is deleted between the update and the re-read, `SingleAsync` throws, giving a 500 instead of a 409 or 404. This is improbable.
- **A-3:** "at most 64 characters" in ops-http counts UTF-16 code units, so an astral character counts as 2. This is a documentation nit.
- **Mirror state:** this mirror's native `tests/Antiphon.Tests` bin holds the env-gated mutant build. It is inert without the `REVIEW_D6B0_MUTANT` variable, but rebuild before any `--no-build` run here.

## Scope

- The brief narrowed the selection to the class, the named tests and the three mutants, under operator minimal-testing.
- The Final profile's whole Unit lane was **not** run. Its full affected integration classes did run: C1656 plus all of PhoneHomeRollingRunnerTests.
- `ordinaryScopeCompleted` is therefore Interim.
- No checkpoint-tool receipts were produced (no plan table for CARD-1656), so `reviewedSourceClean` is false.
- PCs stay pending for SourceLanding Mutation.
