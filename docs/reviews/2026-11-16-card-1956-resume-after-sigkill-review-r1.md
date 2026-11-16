# CARD-1956 Review round 1: SIGKILL resume of a temp Apply

Reviewed source task: 55d5be7a-57c6-49a0-a188-edb7a33d8248 (Debug, Succeeded, platform Any).
Reviewed SHA: 185d2ab436a821c7c6561b7c7e7c0bd073c951c1.
Range: 48a4615dcb4d67033886a3e31629dc7351ed0852..185d2ab436a821c7c6561b7c7e7c0bd073c951c1.
reviewedSourceClean: false. No plan checkpoint manifest; these runs are leased slot runs, not checkpoint-tool receipts.
ordinaryScopeCompleted: Interim. Every required row at the reviewed SHA passed. The scratch-merge guard union did not run.

Behavior at the reviewed SHA is clean. Land is not authorized. `git merge-tree --write-tree origin/master HEAD` against current origin/master `87a37c7c314e573d507dbf1a1e4df042343e3ae3` exits 1. The brief's master `18eabe4ce48a46adea27aa9cbb410415eae9ac91` is stale; a merge-tree against that older tip was clean (tree `61386fa64a83ea19d5ff979205329fe237774664`).

## Publication conflict

Where: `tests/Antiphon.Tests/Scripts/VolumeRetirementReceiptTests.cs` and `tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs`, merge of 185d2ab with origin/master 87a37c7c.

Failure: one content conflict in each file. `scripts/c1008-retirement.sh`, `scripts/fixtures/c1609-retirement-real-cases.mjs`, and `docs/docker-stack.md` auto-merge. `c1609_require_resume_phase` and `c1609_phase_begin` are byte-identical to the reviewed tip after that auto-merge. The merged shell carries master's `C1609_EFFECT_WINDOW_MS=11000`. The reviewed tip still has the hardcoded 5000 ms fence (`scripts/c1008-retirement.sh` lines 509, 552, 588) and does not assign `C1609_EFFECT_WINDOW_MS`.

Why: both this card and the landed 11 s fence (master commits 787766988, 29ae09469, 153c72efa, 6935a2218) appended methods and NativeSourceOnly roster rows at the same end. In the receipt file the shared context line after the conflict is the closing brace of `AssertResumeRefused` on this side and of `PreviewAgedControl` on master's side. Keeping both texts without duplicating that brace does not compile. The roster conflict is the seven resume rows versus master's two fence rows (`One_repository_measured_control_fits_the_host_fence`, `Control_observation_older_than_the_host_fence_refuses`), inserted after `Failed_refused_persist_refuses_active_lifecycle_apply`.

Fix: rebase onto 87a37c7c and keep both appended sets. Duplicate the shared closing brace so both `AssertResumeRefused` and `PreviewAgedControl` close. Do not drop either set. Do not change resume admission, and do not edit `C1609_EFFECT_WINDOW_MS` (leave master's 11000). This review did not resolve the merge.

The required scratch-merge step (regenerate the derived-impact index twice, then the eleven-class guard union) was not run. A conflicted tree is not a valid merged build. Index regeneration was not started.

## Safety at 185d2ab

1. The round-1 hole stays closed. `c1609_phase_begin` calls `c1609_require_resume_phase` whenever the lifecycle is active or `C1008_RESUME=1` (`scripts/c1008-retirement.sh` 1079-1080). That function returns 1 unless resume is set, dry-run is off, and the canonical receipt is schema 2, policyVersion 1, consumed, owner null, state Applying|PartialRefused|Refused|Completed, same operation and project, daemon match, and `readyPublished`. Schema 2 must be phase Apply. Schema 1 needs the bound admission record. `Failed_refused_persist_refuses_active_lifecycle_apply` does not pass resume and passed. `Resume_refuses_an_unconsumed_ready_receipt` passes resume and passed. An unconsumed Ready receipt is not admitted.

2. Resume cannot delete on a stale or foreign state.
   - Liveness (`c1609_phase_begin` 1083-1088): a missing `/proc/$pid` is dead and may continue. A present `/proc` must show numeric start ticks (stat field 20 after comm) that differ from the recorded ticks. Equal ticks refuse. Non-numeric ticks refuse. A recycled pid is therefore not treated as alive. The live-owner witness passed (`Resume_refuses_a_live_apply_owner`, real test-process pid and start ticks).
   - Same operation: `c1609_lifecycle_valid` requires `.operationId==$op` (line 985) before the resume predicate. The receipt jq in `c1609_require_resume_phase` and `c1609_prepare_resume` repeats that bind. `c1609_prepare_resume` requires `.targets` sorted equal to `C1008_TARGETS`. The delete loop iterates only `C1008_TARGETS` (`scripts/c590-remote.sh` 9651), the fixed temp four, not a foreign receipt list. `Resume_refuses_a_different_operation_lifecycle` passed.
   - Before any resume `docker volume rm` (9665), the unchanged `c1008_recycle` path still runs: operation and context format, dry/resume exclusivity, apply-gate token, protected-target refusal (host runner-state, nuget/npm caches, `antiphon_pgdata`), lock, `c1609_phase_begin`, journal match, status proof, tooling image, bind generation, `c1609_prepare_resume` (immutable preview bind, per-target `c1609_target_snapshot`, daemon id, retiredAt), journal-root checks, fingerprint match to the journal original, private identity, cache and topology match, references, save, exchange, audit, inventory digest when the work volume still exists, container stop/rm, post-removal audit when containers were removed, status/references/disk, credential scan, `c1609_publication_gate apply`, then per name `c1609_publication_gate`, `c1609_recheck_target`, `c1609_target_intent`, and `c1609_effect_publication_fresh`. Resume skips only the early credential scan plus `c1609_require_ready_preview` admit/consume (9556-9567 and 9641-9642), because the receipt is already consumed. Those skips are not a path around the later gates. Already-absent pending intents are recorded `removed-observed` inside `c1609_prepare_resume` and are not passed to `docker volume rm`. A present volume must still match the original fingerprint.
   - `C1008_RESUME=1` with nothing consumed refuses. No lifecycle, or a receipt present and the lifecycle moved aside, is `lifecycle-missing` and does not recreate `lifecycle.json` (`c1609_phase_begin` 1095-1097). `Missing_lifecycle_has_a_distinct_refusal` passed. An unconsumed Ready fails `c1609_require_resume_phase`.

3. Nothing in this diff weakens the reviewed tree. Six files, no PowerShell, no `c590-remote.sh`, no migration. No assertion was deleted. The fixture nonce pin became a 32-hex match plus a stability pin because production now mints the nonce. The 5000 ms fence, the 900 s windows, and `read -r -t 10` are unchanged on this tip. This diff contains no 5000/11000 constant line. The fence change lives on master, not in this range.

4. The witnesses are real. `KillApplyingShell` calls `fixture.Run("retire-temp-runner")`, which is `c1008_recycle`, and overrides `c1609_target_result` to `kill -KILL "$$"` after the production `docker volume rm` call. The witness requires exit 137. N3's shim kills `C1609_CUT_PID` (the bash `$$`) after a real `docker volume rm` when the fault is `kill-after-rm`. Both passed. The four negative witnesses passed and leave receipt bytes, lifecycle bytes, and the removed set unchanged, so a resume branch that admitted them would rewrite `lifecycle.json` or delete and go red. `require_resume_phase` does not re-check lifecycle operationId; `c1609_lifecycle_valid` does, and removing that check would let `phase_begin` rewrite the file.

5. `bash -n` passed for `scripts/c1008-retirement.sh` and `scripts/c590-remote.sh`. `node --check` passed for the real-case fixture. No PowerShell file is in the diff. The new docker-stack paragraph is ASCII. Pre-existing non-ASCII elsewhere in that file is outside the hunk.

## Verification at 185d2ab

Build of `tests/Antiphon.Tests` through `scripts/build-slot.ps1`, `UseAppHost=false`, native output, `TEMP=/tmp`: 0 errors, 6m 19s, load1 14.00 at start. Rows used `--no-build --timeout 570s`. Load was read before each row and the row waited while load1 was at or above 16.

| Row | Passed | Failed | Duration |
|---|---:|---:|---|
| focused (Failed_refused + seven new witnesses) | 8 | 0 | 4m 03s |
| N3 Exact_deletion_and_crash_resume_preserve_other_volumes | 1 | 0 | 5m 13s |
| N3refusals Replacement_and_unattributed_absence_refuse_resume | 1 | 0 | 2m 56s |
| receipts-1 | 4 | 0 | 53s |
| receipts-2 | 2 | 0 | 1m 39s |
| receipts-3 | 1 | 0 | 2m 30s |
| receipts-4 | 1 | 0 | 3m 15s |
| receipts-5 | 3 | 0 | 2m 19s |
| receipts-6 | 1 | 0 | 4m 37s |
| receipts-7 | 2 | 0 | 2m 49s |
| receipts-8 | 3 | 0 | 1m 58s |
| VolumeRetirementApplyGateTests | 13 | 0 | 5m 18s |
| VolumeRetirementHostProofTests | 15 | 0 | 1m 01s |
| TestTimingOperationLabelTests | 2 | 0 | 5s |
| ImpactedClassesSelectorTests | 50 | 0 | 3m 43s |
| DerivedImpactIndexTests | 27 | 0 | 2m 26s |
| five-class guard batch at the SHA | 53 | 0 | 51s |

`check-evidence-diff.ps1` base..HEAD: commits=4 entries=0 violations=0. Pins unchanged by this diff and still present: policyHash `c2e3939821a8fef911600c44be767b951c8212bcb62824cb746e48c5aaba6909`, PinnedInputDigest `2f48120a65280582b586f9369e2eaf6b1bdc464a2362c8e49d434bd852479f60`, LegacyExclusionDigest `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`. Impacted-classes kinds were native-linux-gate-roster, remote-scripts, spawn-limit-rosters, and test-lane-category. The migration/downgrade kind did not match. Downgrade selector count: 0. No `### Checkpoints` table exists.

Code's historical test-project builds were 10m 36s and 10m 55s under host overload. This review's build was 6m 19s. Those earlier costs stand.

## Advisories

These do not change the resume verdict. They also do not make the missing scratch-merge union optional.

- The unconsumed-Ready witness uses a Preview lifecycle, so it does not isolate `consumed==true` from the state allow-list. The preview-lifecycle witness covers phase Apply separately.
- There is no direct witness that a recycled pid (proc exists, ticks differ) is allowed. The tick comparison is the code path above.
- Several refusal witnesses assert exit 2 and byte stability, not the reason string. The missing-lifecycle witness does assert `reason=lifecycle-missing`.
- Mutation checks stay waived and owed, including CARD-1955 A-3 (same-shell branches of `c1609_require_ready_preview`).
- `scripts/check-os-witnesses.ps1` was not run: this Debug task has no plan and no OS-witness declaration. The behavior is the production shell, witnessed by the native retire-temp and N3 runs. Source task platform is Any. Runner defaults were read and no runner is pinned.
- Remote-script classes selected by `scripts/*.sh` outside the commissioned retirement and guard list were not executed. ProcessSpawnLimitTests was not folded into the guard count.
- Linux does not discharge the Windows-only roster audit.
- A consumed Refused receipt with zero intents still deletes the remaining same-operation targets. That is the existing `Journal_intents_and_partial_resume_are_exact` contract (receipts-6 passed), not a new hole.

## Review evidence

subjectTaskId: 55d5be7a-57c6-49a0-a188-edb7a33d8248
reviewedSourceSha: 185d2ab436a821c7c6561b7c7e7c0bd073c951c1
reviewedSourceClean: false
ordinaryScopeCompleted: Interim
reviewRound: 1
reviewedRange: 48a4615dcb4d67033886a3e31629dc7351ed0852..185d2ab436a821c7c6561b7c7e7c0bd073c951c1
