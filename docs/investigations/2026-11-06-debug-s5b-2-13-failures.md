# S5b-2: thirteen inherited failures and current-master integration

Task `32c41b68`, Debug, 2026-11-06, Linux runner mirror. **All thirteen failures are
category (b), inherited fixture/load timing failures; all thirteen pass on master.**
No category (a) product defect or category (c) separate policy/product red was found
among them. Next: Merge the current master repairs while preserving S5b-2's extensions.
No product code or tests changed. This is diagnostic evidence, not Final acceptance or land approval.

## Source and scope

The assigned source is `0c42013b281b928d6381e3d35651a73b8baed8c3`, containing tested
source `0461d9d4892ced384ce41ecf0271f90ff6e0aece` plus its report-only successor.
Current master was fetched and pinned at `a27ce632fe7775f2370f498c2f59bd4f771f3c5c`.
The post-run fetch advanced it to `10c3a8dd043326ac3abfefdb113b2fdd3e65a3c9`.
Those six additional commits change documentation, unrelated test bodies and the
FailedFirst script fixture; none changes the selected nine methods, their application
fixtures, production source or project/build inputs. The green run remains attributed
to **a27ce632**, not relabelled as a 10c3a8dd execution. No unchanged green selection
was repeated solely for the new label. Merge feasibility below uses the newer tip.
The task branch remains `feat/card-task-32c41b68`; master was inspected/tested in an
owned detached checkout, never checked out on the task branch.

The prior continuation's complete affected-class result remains **1,372 passed,
13 failed, one skipped**, across 55 classes, with all 924 declared methods found.
Its FULL-REDIRECT row was 78 passed/13 failed; its separate assigned-base
`03fc4879178ed93e0262a7a60d99bc736547f7b1` run was 66 passed/25 failed.
All thirteen candidate failures matched that base; the twelve additional base-only
failures must not be reassigned to the candidate. These historical reds are retained.

This Debug selected exactly the nine methods containing the thirteen failing
parameter cases. TUnit's method selection also includes C672's six other parameter
cases: **19 cases total, thirteen target identities and six siblings**. All nine
methods and all thirteen target identities exist on master. One native-default-output
compile and one separately leased test invocation were used per source. No whole
class, assembly, Unit lane, checkpoint battery, deliberate mutant, or repeated
green selection was run. The invariant is the same remote-preparation/lease/release
behavior on the branch and master, with unchanged test assertions and time limits.

## Exact failures and classification

All methods below are in namespace `Antiphon.Tests.Application`. `R` means
`RemoteWorkspacePreparerTests`; `S` means `C0824WorkspaceSlotServerTests`.
The original error strings are:

- **M10:** `TimeoutException: Expected 1 WorkspaceMirror request(s); saw 0.`
  The ten-second `Rig.WaitForRequestsAsync` poll expires; the test calls TickAsync
  without awaiting it before starting that poll.
- **T15:** `TimeoutException: The operation has timed out.` at the prepared arm's
  `rig.TickAsync().WaitAsync(TickBound)`; TickBound is fifteen seconds.
- **T35:** `TimeoutException: Test '<method>' timed out after 00:00:35`, with the
  exact method name from the row substituted. The eleven errors include TUnit's
  `Task Status: WaitingForActivation` and timeout-handler stack, which is explicitly
  **not the blocked test's stack**. They do not establish a particular blocked line.

| # | Class | Exact method | Parameter row | Historical first error | Fresh branch | Master | Class |
|---|---|---|---|---|---|---|---|
| 1 | R | `Cancel_of_a_queued_runner_task_returns_while_its_mirror_is_pending_and_a_late_success_is_still_recorded` | none | M10 | failed M10, 36.091s | passed, 13.042s | (b) |
| 2 | R | `C672_prepared_task_launches_while_a_land_holds_the_lease` | `prepared` | T15 | passed, 37.512s | passed, 13.186s | (b) |
| 3 | S | `C0824_ReleaseRoundTripRealRunner` | `direct` | T35 | failed T35, 36.044s | passed, 0.908s | (b) |
| 4 | S | `C0824_ReleaseRoundTripRealRunner` | `reply-lost` | T35 | failed T35, 36.037s | passed, 13.576s | (b) |
| 5 | S | `C0824_FlagOffMirrorRequestUnchanged` | `flag-off` | T35 | failed T35, 36.038s | passed, 12.678s | (b) |
| 6 | S | `C0824_FlagOffMirrorRequestUnchanged` | `runner-lacks-WorkspaceSlotV1` | T35 | failed T35, 36.039s | passed, 12.644s | (b) |
| 7 | S | `C0824_ColdWorkspacePersistsAndLeaseCarriesCold` | none | T35 | failed T35, 36.039s | passed, 13.302s | (b) |
| 8 | S | `C0824_LeaseRecoveryAtEachHandoff` | `reply-lost` | T35 | failed T35, 36.045s | passed, 0.950s | (b) |
| 9 | S | `C0824_LeaseRecoveryAtEachHandoff` | `runner-crash-after-leasing` | T35 | failed T35, 36.037s | passed, 1.826s | (b) |
| 10 | S | `C0824_SlotUnavailableHoldsWithoutFallback` | `overflow_cap` | T35 | failed T35, 36.039s | passed, 13.362s | (b) |
| 11 | S | `C0824_SlotUnavailableHoldsWithoutFallback` | `disk_budget` | T35 | failed T35, 36.037s | passed, 1.319s | (b) |
| 12 | S | `C0824_LeaseRoundTripRealRunner` | none | T35 | failed T35, 36.037s | passed, 0.832s | (b) |
| 13 | S | `C0824_BusyRunnerLeaseSerialisedNotDuplicated` | none | T35 | failed T35, 36.037s | passed, 0.910s | (b) |

Fresh branch result: **7 passed, 12 failed, zero skipped**, exit 2; all six C672
sibling cases passed. The prepared arm's 37.512s is total case duration, not the
duration of its fifteen-second TickAsync wait. It includes fixture and teardown work.
Master result: **19 passed, zero failed/skipped**, exit 0, including all thirteen
target identities and all six siblings. Fresh TRX identities were joined to the
historical failure names one-to-one; `comparison.json` retains that audit.

### Causal evidence and limits

The branch starts the selected rigs concurrently without the current-master
workspace-rig admission lanes and without master’s pre-body EF runtime warm-up.
Its trace contains overlapping `dispatcher.tick` spans of approximately 14.3–17.5s.
This explains why a ten-second request poll can expire while its unawaited tick is
still working, and why a fifteen-second tick budget is vulnerable. The branch's
eleven slot failures share a roughly 36.04s total interval at their 35s outer bound.
Those timeout stacks alone cannot distinguish setup, clone admission, query compilation,
body, or disposal. No timeout has been raised and no assertion weakened here.
Master's same-case total durations range from 0.832s to 13.576s; its longest
selected case stays below the original 35s slot-test bound. The host load differed,
so this comparison does not isolate the contribution of each individual repair.
It establishes the category and the practical integration path, not a new p95.

Relevant repairs already present on master:

- `77b4e1d77aaa44bb56bbaaa8edda57afcc1c9896`: initializes the actual EF provider/model
  in TestDbFixture's selected-store assembly hook before timed bodies, and adds
  strict process limiters and bounded scratch Git execution.
- `735e80526bd535fbdbd6b1f491e5075ca66ab52e`, then
  `3b7d80c66c28e8cae7a5c1b762d066dee6f3c07b`: admits scripted rigs at measured clone
  width and binds that lane **at method scope**; actual runner transports use only
  the strict assembly-local `ProcessSpawnLimit` lane. A class lane can mask the
  stricter method lane and must not be restored.
- `e4103bf1650840496a79bd854dde5bcf94b5aea1`: calibrates scratch Git's single
  start/exit/drain deadline to one second from its recorded command sample.

The owner evidence is
`docs/superpowers/plans/2026-11-05-d69fe301-runtime-warmup-repair.md` on master.
It records the original 77-case cached-path timeout incident, the pre-body warm-up,
clone-queue measurements and the final lane correction. These are the fixture/load
family named by the brief; they are not evidence of RefOnly redirect rejection.
The separate 10s-to-60s custody-class budget work (`527029d2e` and its linked repair)
concerns SessionRunner custody tests, not these eleven 35s server-slot cases.

No policy-entry, assertion, malformed protocol, or missing-method failure appears
among the thirteen historical errors. Category (c), a separate inherited policy or
product assertion already repaired by master, must not be inferred from the branch's
age. **Category (a): none among these thirteen. Category (b): all thirteen.
Category (c): none among these thirteen.** The C672 prepared arm additionally
demonstrates nondeterminism at the unchanged branch source.

The smallest action is to integrate the existing master warm-up/admission/deadline
repairs, preserving the method-scoped lane distinction, and verify the combined
candidate once. No S5b-2 product fix, timeout increase or frozen-design change is
justified by these failures. The unfinished C4 and other frozen variants listed
below still require Code after integration; their absence is substantive acceptance
debt, and their unexecuted outcomes cannot be inferred from the thirteen legacy cases.

## Current-master merge feasibility

`git merge-tree --write-tree origin/master 0c42013b281b928d6381e3d35651a73b8baed8c3`
against initial master a27ce632 returned exit **1**, tree
`e9b384b092259bf4d1405035d302dd8fa5a1ab07`; against refreshed master
`10c3a8dd043326ac3abfefdb113b2fdd3e65a3c9` it returned exit **1**, tree
`1c046470a58ee78e3f4c813b90eb680bdc2f462c`, with the same two conflicts:

| Conflicted file | Cause | Required resolution |
|---|---|---|
| `docs/superpowers/testdesign/2026-11-05-card-0824-s5b-2-testdesign.md` | add/add: master has the TestDesign added by `0dbd8295a034f8d88f5b98d1e29f14ac82c1890a`; the task lineage independently carries it plus continuation amendments | Preserve the frozen controls/checkpoints and S5b-2's later caller-closure/DecisionPathInspect amendments. Resolve the document as a whole, not by dropping the task version. |
| `tests/Antiphon.Tests/Application/C0824SlotTransportWorld.cs` | content: master replaces direct Process.Start/WaitForExit/drain code with the custodied, cancellation-aware CheckpointProcessRunner path (`77b4e1d77`, calibrated by `e4103bf16`); S5b-2 retains/reworks the old helper while adding redirect-world capabilities | Keep master’s `GitBudget`/`RunGit`/runner/deadline/timing path and S5b-2's `Workspace`, `RealDesktopGit`, real canonical/common-directory support, peer runner/store setup and runner-settings seam. Do not replace the master helper with the old 30s child wait. |

**The brief's two named landing-tip commits are not the direct origins of those
conflicts.** `fed5899951daa3c66be3dee31355e6261286dc01` changes only
`tests/Antiphon.Tests/Layering/allowlist.txt`, adding the three S4a layering debts.
`3737148ee1e43e3695174b7a8e9befeb6d69fb41` changes only that same allowlist,
reconciling Phase 0 wave 0a moves. The allowlist merges automatically here, as do
the namespace-preserving Phase 0 file moves. Preserve the reconciled inventory and
S4a switch/cleanup protections; do not restore the old inventory wholesale.

Integration is feasible as a separately commissioned Merge with these two bounded
resolutions. It is **not currently conflict-free or landable**. No merged candidate
was built or tested, and successful method tests on each input cannot certify the
combined tree. This Debug performed no merge, rebase, target push, land or activation.

## Exact remaining Final-acceptance work

The thirteen failures and the unfinished frozen variants are distinct debts.
Historical CP-1..CP-5 successes retain their original source SHAs; passing a method
or a full legacy class does not cover variants that its body never exercises.
The following list is taken from the frozen TestDesign and continuation-4 report;
this Debug neither relaxes nor expands it.

| ID | Still owed |
|---|---|
| V-1 / S1..S7 | Ancestry I/O failures; CAS I/O and post-read unknown/wrong-S; complete independent initial/under-lease endpoint, pin and binding-tuple corruptions; mirror negatives and the real queued phone-home P recipe; actual lease/reservation and child-lifetime/cancellation variants. |
| V-2 / D1..D3 | Unexercised durable-debt crash/recovery cuts and corruption controls, including independent attempt/source/save cuts; prove exact recorded S and episode at both claim and save, preserving existing gate-off recovery. |
| V-4 / B1..B4 | Frozen artifact/path/type/budget/source-unavailable variants not reached in the authored methods; exact immutable source and independent negative cases. The legacy bundle class's 23 passes do not substitute for these. |
| V-5 / C1 | Missing-mirror recreation and complete Q delivery handoffs, through accepted complete UserPrompt at bound remote cwd/S/ref. |
| V-5 / C2 | Actual P mutation queue; before/after publication intent and receipt commits; recovery claimed/synced/saved cuts; independent attempt/source/save variants. |
| V-5 / C3 | Released-seat answer path, full Q handoffs and independent destination/generation/body/floor/receipt corruption controls. Live idle/busy/restart/gate-off cases already passed. |
| V-5 / C4 | Actual queued/deferred old-seat release, network loss, pre-enqueue failure, recipient-release/before-lost-reply, enqueue/save/preparation cuts, and independent early/locked refusal controls. The six authored custody/recovery cases passed; this is the unimplemented remainder, not the thirteen legacy timeouts. |
| V-6 / U1 / R-6 | Actual dispatched and follow-up child; complete unusable/changed binding variants; retain frozen S, explicit remote source and Worker/Shared/ReadOnly/local-tuple refusals. Off defaults and absence of a new producer remain required. |
| V-6 / U2 | Actual RefOnly AgentSessionService and AgentControlService caller witnesses; complete missing/wrong runner/store/session/path binding cases. DecisionPathInspect's remote grant tests do not cover these other callers. |
| V-6 / U3 | Advisory ownership, same-slot/generation terminal ambiguity, RefOnly ZombieCensus and inherited-follow-up display witnesses. Home and same-ref ownership successes do not cover them. |
| V-7 / N1 / A-7 | `C0824RefOnlyBoundaryNativeTests.Windows_case_alias_cannot_own_the_same_ref_twice`, including binding-owner and legacy-Checkout-owner controls, on an authorized **non-desktop Windows runner**. Linux syntax/path strings do not discharge this. |
| V-8 / W1 | Complete Q handoffs and independent negative recipient receipts, then actual-provider manual acceptance. Queue insert/Sent/ack alone is never the receipt. |
| Whole Unit | Execute the full required Unit scope using CARD-1720's bounded/sharded, evidence-producing procedure after integration; this branch has not discharged it. Master’s budget/shard repairs do not themselves supply this S5b-2 execution. |
| Other native/manual | Execute the skipped `AgentSessionServiceIntegrationTests.AgentSessionService_start_to_first_text_delta_with_real_worktree_and_raw_pty` on actual Windows; native Windows remote-path containment and the required real-provider/input/receipt acceptance. No desktop work is authorized. |

V-3's authored read/progress/report cases, V-7 A1..A3 and portable cleanup, and R-1..R-5
have their recorded ordinary successes; no fresh failure was discovered in those
areas here. R-7 classification was historically 47/47, with unchanged branch policy;
integrating master’s policy/census must retain its matching inputs. Do not recreate
the obsolete blanket claim that all 55 affected classes are NOT RUN: they were run.

After the remaining ordinary implementation/verification is complete, an initial
Claude **Final/Full round-1 Review** is still required. Every **PC-1..PC-159**, all
listed variants and missing-control discovery remain pending method-scoped
**SourceLanding Mutation after land**; ordinary negative fixtures are not PC credit.
The current incomplete ordinary gate cannot be promoted to Clean/Full by D-2 wording.

The future at-least-20-task/24h server2 canary, role widening, new RefOnly producer,
S5b-3 lifecycle qualification, deployment and activation are separately commissioned
rollout work. They were not run here and are not silently added to this S5b-2 Debug.
Feature admission remains OFF; no further approval was needed for this authorized
read-only diagnosis and task-branch report publication. Restart required: none.

## Run evidence and reproduction

Evidence root: `/work/worktrees/task-32c41b68/.antiphon/32c41b68/` (ignored).
Original raw failures and durations are retained as `historical-failures.json`;
original TRX remains in task-5528528e's checkpoint evidence. Fresh TRX, complete
rosters, build/test logs and trace JSONL stay in this task's evidence root.

| Source | Build | Test cases pass/fail/skip | Admission/execution |
|---|---|---|---|
| branch `0c42013b281b928d6381e3d35651a73b8baed8c3` | exit 0, 1,757 warnings, zero errors; native compile 427.03s | 7/12/0, exit 2 | build waited 0s/held 428s; tests waited 60s/held 57s; test host 53.582s |
| master `a27ce632fe7775f2370f498c2f59bd4f771f3c5c` | exit 0, 1,769 warnings, zero errors; native compile 449.53s | 19/0/0, exit 0 | build waited 361s/held 450s; tests waited 0s/held 39s; test host 34.999s |

Host: Debian 12 linux-x64, SDK 10.0.403, test runtime .NET 9.0.22; granted build
maxcpucount 6. Sampled host load was 51.74/37.09/29.86 and later
35.74/35.90/30.79 (1/5/15-minute averages). These are shared-host observations,
not isolated benchmark or per-method p95 measurements. Per-case timing is in TRX;
available call spans are in trace JSONL. Missing DB/container/migration/clone,
host/body/teardown and syscall breakdowns remain **unknown**, never zero. Enabling
the phase-root environment did not yield phase files for either selection.
Build and slot waits are separated above, and overlapping span totals are not wall time.
The selected-closure diff from a27ce632 to refreshed master 10c3a8dd is retained as
`master-selected-closure.diff` and is zero bytes. The refreshed merge-tree transcript
is `merge-tree.txt`. These are static checks, not execution credit at the newer SHA.

All four owned build/test drivers and their slot holders were joined. Cleanup used
literal absolute paths: the task's native bin/obj and in-output test results were
deleted, and the clean, owned detached master checkout/registration was removed.
A fresh server/src/tests/tools/scripts scan found no bin, obj or TestResults
directories. Diagnostic TRX/log/trace evidence remains ignored under the evidence
root. No other worktree or process was stopped or cleaned. `git diff --check`
passed; the only deliverable staged for the single commit is this Markdown report.

Use the same command from each clean source checkout. Native output and
`UseAppHost=false` must match between build and run; each invocation takes its own lease:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label s5b2-debug-build -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label s5b2-debug-methods -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/*/(Cancel_of_a_queued_runner_task_returns_while_its_mirror_is_pending_and_a_late_success_is_still_recorded)|(C672_prepared_task_launches_while_a_land_holds_the_lease)|(C0824_ReleaseRoundTripRealRunner)|(C0824_FlagOffMirrorRequestUnchanged)|(C0824_ColdWorkspacePersistsAndLeaseCarriesCold)|(C0824_LeaseRecoveryAtEachHandoff)|(C0824_SlotUnavailableHoldsWithoutFallback)|(C0824_LeaseRoundTripRealRunner)|(C0824_BusyRunnerLeaseSerialisedNotDuplicated)' --report-trx --report-trx-filename run.trx --results-directory .antiphon/s5b2-debug-results
```

These are direct diagnostic runs, not checkpoint certificates. Do not rerun an
unchanged green selection merely to attach a new label. Validate the resolved
Merge candidate's affected selection once, and retain these input-source results.

## Handoff

No product repair is required for the thirteen failures. Commission Merge against
current master with the two concrete resolutions above, preserving its runtime
warm-up, method admission lanes, bounded scratch-Git custody, S4a safeguards and
Phase 0 inventories. Then continue the exact frozen ordinary variants and native/
Unit/manual work above; only after ordinary completion commission Final/Full Review.
This report alone neither completes ordinary acceptance nor authorizes land.

--- next stage ---
next: merge
handoff: Integrate master; resolve TestDesign add/add by retaining frozen rows and continuation amendments; resolve C0824SlotTransportWorld by retaining master GitBudget/RunGit custody and S5b-2 real/peer-runner/store/settings extensions. No category (a) fix among the thirteen; all pass on master. Then Code owes the enumerated frozen V/R variants, Unit and native/manual qualification. Keep feature OFF; ordinary acceptance remains incomplete.
artifact: docs/investigations/2026-11-06-debug-s5b-2-13-failures.md
