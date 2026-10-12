# CARD-1116 investigation: deploy-parent Compose comparison

2026-10-12. Investigate task `2000a0d1-996f-44dc-be0c-4408146185ed`.

## Outcome

The original defect is confirmed by reconstruction of the pre-repair code and is
already repaired on master. CARD-1105 landed the requested early comparison, the
retained late comparison through `c1008_refuse`, and both named regression
controls. Recommend closing CARD-1116 as implemented through CARD-1105, with the
qualification limits below stated in the close reason. No additional production
defect is demonstrated by the late-summary observation.

The implementation criteria are satisfied. The historical requirement to observe
the mismatch control red before the repair is **not independently established**:
the retained evidence examined does not contain that assertion failure. This is
an evidence limit, not proof that the run never occurred. Post-land Mutation was
**NOT run**; no pending positive control is discharged by this investigation.

## Scope and source identity

Read-only source investigation; only these findings and their investigation index
are written. No production/test edits, builds, test execution, mutation, SSH,
rollout, drain changes, or desktop checkout access.

- Assigned branch: `feat/card-task-2000a0d1`.
- Starting HEAD: `97329dc10e5a21cade083f0bd01abb56f4916413`.
- Fetched and advertised master: `a91ba68a72185d57d39e87b218e459c5df2451de`.
- At 07:21 UTC, `git ls-remote origin refs/heads/master` returned that same SHA.
- `git diff --quiet origin/master --` the remote shell, desktop deploy wrapper,
  live bridge, result writer, two cited test files and Docker owner document
  returned 0. Thus the working-copy file:line references below describe master.
- `git merge-base --is-ancestor` returned 0 for both production repair
  `c88411f0f6ad7da341475fb94c0aafd4e5ba350e` and test addition
  `a0ca0b49908f99badd082cbe13b6be3ad2005170` against master. Blame attributes the
  early guard and late refusal replacement to the former and the requested
  assertions/mismatch method to the latter.

`ANTIPHON_TASK_TOKEN` was present and used as `X-Antiphon-Task-Token` for reads;
its value was never printed or persisted. Authenticated task detail confirmed
the assigned task's bound card, role and checkout. Selected returned DB-backed
rows (the detail envelope's `summary`, not its top-level properties):

| Task id | Role | Status observed | Card id | Worktree |
|---|---|---|---|---|
| `2000a0d1-996f-44dc-be0c-4408146185ed` | Investigate | Dispatched | `6b809709-73cc-453a-bb5d-47917f472f1d` | `C:\Antiphon\worktrees\card-task-2000a0d1` |
| `a2122818-032d-4c45-9a95-efc8caa6ca79` | Code | Succeeded | `5f07b7ce-0d6e-48e5-a7b8-3d77cfd62cf2` | `C:\Antiphon\worktrees\card-task-a2122818` |

Read routes: `GET /api/agent-tasks/<id>` and the explicitly Antiphon-board-scoped
`GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703`.

## Card acceptance reconciliation

Authoritative card read:
`pwsh -NoProfile -File scripts/card.ps1 get CARD-1116 -Board Antiphon`.
The returned row was Review, revisions=2, id
`6b809709-73cc-453a-bb5d-47917f472f1d`, board
`8988ca03-7414-47ad-b0b6-51556c701703`, title:
“Rolling deploy-parent: run the target-compose cmp guard before the recycle
destroys anything, and refuse through c1008_refuse (CARD-1105 D-4 follow-up)”.
The description's Fix paragraph and red-first clause are the acceptance text;
the card has no separate acceptance list in the returned description.

| Acceptance sentence or condition | Master evidence | Finding |
|---|---|---|
| Add the same comparison immediately after target materialization. | `scripts/c590-remote.sh:5944` calls `c1008_compose_model`; `:5947` compares the materialized target with `SERVER2_COMPOSE`; `:5948` refuses `RecycleComposeMismatch`. Materialization is `:4113`, with Git-object extraction at `:3963`. | Satisfied. |
| Host project and apply only; dry run stays read-only. | Host/apply predicate at `scripts/c590-remote.sh:5946`; preview intercept at `:6871` precedes checkout/boot setup; preview exits at `:5974` before lock `:5976`. | Satisfied. The compare does not apply to temp or preview. Evidence materialization is permitted; this is not a zero-filesystem-writes claim. |
| Refuse before `c1008_lock`, `docker stop`, container/volume removal. | Guard `scripts/c590-remote.sh:5947` precedes journal initialization `:5952`, normal lock `:5976`, stop `:6072`, rm `:6084`, volume rm `:6107`. The resume-only lock in `c1008_bind_generation` at `:5917` is called after the guard at `:5969`. | Satisfied, including the resume preflight boundary. Checkout and boot setup already precede recycle at `:6129`/`:6130`; “host untouched” means no stop/removal, not no checkout setup. |
| Keep the pre-up comparison and change it to `c1008_refuse`. | Active late guard `scripts/c590-remote.sh:6169`, compare `:6170`, refusal `:6171`, followed by `up` at `:6174`. | Satisfied. A mismatch exits before that up call. |
| Active journal and copied receipt preserve the actual mismatch diagnosis and partial outcome. | `scripts/c590-remote.sh:3905` sets diagnosis and derives partial/refused from removed-volume count; save `:3907`; journal and receipt save at `:3881`/`:3882`. | Satisfied on successful saves. Partial is conditional on actual removals; no removals yields refused. Save failure explicitly reports `RecycleReceiptUnavailable`. |
| `C1008HostFixture` mismatch control exits 2, reports mismatch, removes nothing, issues no stop/rm, creates no journal. | `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:2145` is `C1105_Compose_cmp_refuses_before_removal`; source seam `:2151`; assertions `:2157` through `:2161`. Trace counter `:2496` also counts volume rm. | Requested runtime test exists with all requested assertions; retained historical CP-10 below reports green. |
| Extend `C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle`. | Method at `RemoteScriptContractTests.cs:2112`; early-before-lock and stop assertions `:2120`/`:2121`; retained late compare `:2118`; refusal-call assertion `:2123`; preview assertions `:2124`. | Satisfied; historical CP-3 below reports green. |
| Mismatch control red first against the then-current shell. | Test commit precedes repair and its commit message claims red. Repair report `.antiphon/task-a2122818.md:18` names a different red test, the remove-boundary receipt control. Transcript evidence below is a prediction, not an executed assertion failure. | Unverified historical acceptance evidence. Do not call the commit message or a predicted failure a retained red run. |
| Order after CARD-1105 S3, or fold into it. | Both repair/test commits are master ancestors; `.antiphon/task-a2122818.md:22` explicitly says CARD-1116 is implemented in the slice; the CARD-1105 plan's CP-10 at `docs/superpowers/plans/2026-10-06-card-1105-compose-mount-generation-plan.md:457` carries these controls. | Folded change already landed; no remaining source dependency. |

## Confirmed original mechanism

Reconstruction from `git show c88411f0^:scripts/c590-remote.sh`: the only
byte comparison was the active deploy tail at historical lines 5018-5019,
immediately before up. It used `write_result false RecycleComposeMismatch 2`.
The recycle call had already run. That correctly refused deployment, but could
discover a mismatched checkout only after stopping/removing the old generation;
the direct result call also bypassed journal diagnosis/outcome update.

The production repair adds the comparison to recycle before destruction and
replaces that late result call with the established refusal path. This is a
confirmed mechanism reconstructed from stored Git objects, not a new runtime
reproduction. No repair was designed or implemented by this task.

## Late refusal: reconstructed output and receipt behavior

Conditions for this path: early comparison matches, a later change makes the
files unequal, all intervening gates succeed, and the late guard is reached.
Earlier `c1008_record_recreated` calls can rematerialize the target through
`scripts/c590-remote.sh:5788`; simply editing the materialized file at an earlier
point does not prove that the final comparison will still see that change.

`c1008_summary` is intentionally one-shot (`scripts/c590-remote.sh:3886`),
and its format at `:3896` contains no diagnosis field. The state flag is not
reset elsewhere in the script. Successful normal host reclamation sets outcome
reclaimed at `:6123` and prints at `:6124`. Deploy then records recreating /
verificationPending at `:5791` before the late comparison.

| Invocation reaching late mismatch | Journal and `CASE_DIR/recycle.json`, when save succeeds | `C1008_RECYCLE` stdout | Result |
|---|---|---|---|
| Fresh apply after normal reclamation | diagnosis=RecycleComposeMismatch; phase=recreating; outcome=partial if any private volume was removed, otherwise refused | One earlier line with outcome=reclaimed; no new failure summary because the flag is already 1 | accepted=false, diagnosis=RecycleComposeMismatch, exit=2; `DIAGNOSIS=RecycleComposeMismatch`; no late up |
| Resume returning from recreating/verified branch (`:5986` through `:5999`) | Same diagnosis/partial-or-refused rule and updated copied receipt | No reclamation summary ran in this invocation, so refusal prints one line with partial/refused | Same failure result and no late up |
| Resume continuing unfinished removal through normal recycle tail | Same final refusal rule | Reclamation prints first, so same one-shot behavior as fresh apply | Same failure result and no late up |
| Early mismatch, fresh or resume | No new journal; resume's old journal is unchanged because ACTIVE has not been set | No recycle summary | exit=2 and mismatch result, before lock/stop/removal |

These are conditional code-path conclusions, not runtime measurements. The
recreating/verified resume branch sets ACTIVE at `:5998` and returns without
the normal summary; normal activation is `:6052`. `c1008_refuse` saves before
attempting its summary, so suppression cannot suppress the receipt update.
The shell result writer at `scripts/c590-remote.sh:106` writes accepted/diagnosis/
exit JSON and prints the separate diagnosis at `:110` before exiting.

The bridge copies the whole case directory even after host refusal
(`scripts/c590-real.ps1:505` through `:507`), reads the copied result at `:515`,
and forwards accepted/diagnosis/exit at `:516`. Copy failure for a recycle is an
explicit `RecycleReceiptUnavailable` at `:509`. The desktop result writer
`scripts/c590-command.ps1:59` prints the diagnosis and exits with that code.
`deploy-server2.ps1:892` invokes the verifier, and `:893`/`:901` throws on nonzero;
it never parses `C1008_RECYCLE` to admit success. Its exception suffix specializes
only TempContainerStillRunning (`:898`), so the mismatch exception itself is
generic HostCaseFailed, while stdout and the copied result retain the cause.
No claim that the wrapper exception gains the mismatch name is justified.

The established owner contract (`docs/docker-stack.md:319`) describes the summary
as reclamation counts/disk measurements; it does not require a second terminal
deployment summary. The one-line policy is also pinned by
`C1008_Recycle_receipt_records_disk_and_partial_failure` at
`RemoteScriptContractTests.cs:1717`. That test is not late-mismatch coverage.

Therefore the fresh stdout line is a record of completed reclamation, not the
final deployment verdict. Read in isolation it lacks the later outcome; read
with the mandatory result and receipt, the cause and failure are preserved.
There is **no demonstrated remaining receipt/wrapper bug** against this contract.
The card's statement that no recycle summary was printed is not literally true
for normal completed reclamation even before the repair; it meant no new refusal
summary. Switching refusal helpers does not change that global summary policy.

## Retained evidence and qualification limits

Historical repair task detail Result confirms CARD-1116 was implemented and left
open; its published source was `6e57b73701673c8072765b46151a5b5375951e3a`, report
commit `001d9e037f3832e4a63b3baf7a63c6c0f91bb636`. Its DB-backed progress evidence
reported synchronized publication of that report commit. These pre-landing IDs
must not be confused with the master repair/test IDs above.

Authenticated historical transcript read:
`GET /api/sessions/effe446f-a379-4f3b-babd-7ff5d4db7d7d/transcript?since=0`,
419 entries, lastSequence=419. The original task detail identifies that session.
Sequence 87, Thinking, 2026-10-06 21:00:40 UTC, says the compose test “will be red
before the ordering fix and green after.” Sequence 112, Thinking, 21:02:52 UTC,
describes drafting the mismatched-source injection. Neither is an assertion
failure. Returned kinds were 265 ToolCall, 127 Thinking, 19 AssistantText,
3 UserPrompt, 3 TurnEnd and 2 CompactBoundary; there were no tool-output entries.
The final AssistantText at sequence 410 (22:58:31 UTC) reports the known redeploy
fixture deadline failures; it does not retain the compose mismatch red assertion.

The examined report set contains green checkpoint records for the mismatch test
(including `.antiphon/task-f9e7ea8a.md:35`, `.antiphon/task-68308978.md:136`, and
`.antiphon/task-d8d32528.md:124`), but no retained compose-specific red assertion.
The original repair worktree and the cited CP-10 raw TRX path were absent locally.
Thus these are retained reports of previous runs, not independently inspected raw
receipts or current-master green results. Essential unedited provenance from
`.antiphon/task-68308978.md:130` and `:136`:

```text
CHECKPOINT CP-3 commit=db249df5b35274c11cf421d10f1aa29083822378 build=reused filter=/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)|(Deploy_parent_creates_the_github_token_directory_without_reading_it*)|(Deploy_parent_creates_the_codex_home_directory_without_reading_it*)|(Deploy_parent_seeds_or_verifies_runner_checkout*)|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner*)|(Deploy_parent_seeds_a_fresh_runner_checkout_before_starting_the_runner*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-68308978/.antiphon/checkpoints/20261008-045930-46d1/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=db249df5b35274c11cf421d10f1aa29083822378 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=db249df5b35274c11cf421d10f1aa29083822378 build=reused filter=/*/*/RemoteScriptContractTests/(C1105_Compose_cmp_refuses_before_removal*)|(C1105_Resume_identity_refusals*)|(C1105_Resume_state_init_image_follows_the_journal*)|(C1105_Resume_foreign_leftover_is_not_an_image_mismatch*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-68308978/.antiphon/checkpoints/20261008-045930-46d1/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=db249df5b35274c11cf421d10f1aa29083822378 sourceState=clean buildSource=verified
```

The existing fixture normally exits successfully at build_server2_images
(`RollingVolumeRecycleScriptTests.cs:1000`), before reaching the late guard.
The mismatch test therefore proves the early boundary; the ordering test pins
the late refusal call text. Neither proves a runtime late-mismatch receipt.
`C1105DeployTail` at `RemoteScriptContractTests.cs:2555` lets other tests reach
the deploy tail, but no late byte-mismatch test was found in these controls.

Remaining uncertainties:

1. The original mismatch red-first run lacks a retained observed assertion.
   An original failure log/TRX tied to the pre-repair shell would resolve that
   historical question. A newly commissioned mutation pass is distinct evidence,
   not retroactive proof of a past run; Mutation remains NOT run.
2. Late mismatch behavior is established by the code path, not a new offline
   execution, and no current-master green test run is claimed. If the caller
   requires executable qualification beyond the existing contract and historical
   reports, that is separately commissioned verification, not evidence of missing
   guard code.
3. The actual deployed script/version and real receipt-copy behavior during a
   live mismatch were not inspected. This conclusion is about landed master.

## Closure disposition

Caller can close as already implemented with the retained passing reports and
the explicitly unverified historical red-first requirement. If strict closure
requires that old failed assertion, retrieve it or commission qualification;
do not commission a duplicate Code change. The late stdout observation does not
justify new TestDesign for a demonstrated production defect.

## Not done, noted

No fix designed or implemented; a requirement for a second terminal summary would be a separate contract decision, not an established CARD-1116 defect.

--- next stage ---
next: none
handoff: CARD-1116 is already implemented on master through CARD-1105. Caller owns closure; disclose missing historical red-first assertion evidence and that post-land Mutation was NOT run. No remaining receipt/wrapper defect was demonstrated.
artifact: docs/superpowers/plans/2026-10-12-card-1116-investigation.md
