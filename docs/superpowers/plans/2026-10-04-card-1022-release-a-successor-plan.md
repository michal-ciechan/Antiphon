# CARD-1022 release A: successor baseline and publication reconciliation

Date: 2026-10-04. Plan task: `de732954-4dc5-46b5-9056-70e281abb0fb`.

Release A is already implemented and published. The valid continuation is to
reconcile acceptance and activation of that publication, then commission a fresh
master-based correction only if an actual defect remains. Replaying the blocked
Code attempt would duplicate implementation, regress the checkpoint roster, and
still not establish eligibility to adopt its replacement into the old owner.

This is an additive successor plan. Preserve
[the current A/B/C plan and TestDesign freeze](2026-10-03-card-1022-modern-conpty-only-plan.md)
unchanged. Its release-A semantics, V-1–V-4, R-1–R-6 and PC-1–PC-73 remain the
behavior and coverage contract. Release B refusal, release C removal, and the
snapshot-fixture evidence-guard exception are separate work. This dispatch does
not fold in TestDesign, implement code, commission execution, or activate services.

## Pinned source and attribution

Fetched `origin/master` for this plan is **B =
`9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd`**. Refresh and pin a new full B at
future dispatch; do not use a moving ref as the recorded task base.

The assigned Plan branch starts at
`353e374fa5b8345cd57dbc8901bd5d2ce0903cc7`. Keep it fast-forward-only; it is
not the baseline for a successor Code task. The CARD-1022 implementation on this
branch precedes its landing rebase. Reading current master through Git objects
does not authorize resetting, rebasing, or merging this branch.

The current plan has the same blob in this checkout and B:
`315e96ae3139d814925c7ff4022d258e70ba5860`. Master's roster update is
`dc129d2bd1e08edc8c37104abc09c22dc08a0122`. Compared with the old owner plan,
it adds two portable prompt-reader results and one portable receipt result:
CP-3 **18 -> 20**, CP-7 **23 -> 24**, Windows **92 -> 95**, total **152 -> 155**.
It also documents the receipt fixture's required TurnEnd. Copying the old file
over this one is prohibited, even if an evidence-history probe accepts the copy.

Read-only task API observations on 2026-10-04 establish the ownership chain:

| Identity | Actual authority and outcome |
|---|---|
| Original attempt `ffc43849-8d59-468d-8b6a-2a00033acbda` | Brief named it Code and landing owner. Still Blocked; base `3e3436b329da772134c24c8ce4925063bf7d898a`, pushed tip `34410810c12c4bb5f01d6dd792ba0bd769b83abb`, no landing request/publication. It authored prerequisite integration and an admission report, not release-A implementation. |
| Implementation `e41a7005-3049-48f9-957a-162b4a4f9fbb` | Its subsequent caller brief explicitly names it Code and landing owner, starting clean at `a10bd1883e0803bcad3fe4513f3f1d1685c0b666`. The [Code report](../../investigations/2026-10-04-card-1022-code-e41a7005.md) records release A. No own publication is recorded. |
| Repair/publication owner `b7c17822-63b4-4626-b5bb-525cfccc0cea` | Its caller brief explicitly says NEW LANDING OWNER and starts at e41a7005's `4e865e05d288374c0d61d8599080a5c399db5f4e`. The [repair report](../../investigations/2026-10-04-card-1022-windows-test-repair-b7c17822.md) preserves that supersession. Its structured publication is Landed, phase Complete, cleanup Complete. |
| Review `71a5997b-d0d0-45a0-8699-84817a158fd5` | Evidence `d6ed9bd1-1784-44ec-b432-b488ffb1bb47` names subject b7c17822, reviewed SHA **C = `65a78f3b1f88c213bbb4ac595681212476447130`**, Final/Full, `reviewedSourceClean=true`. Its report states the commissioned rerun was CP-1/2/9. |
| Publication | Operation **O = `8d0f6e2a-2aec-4204-8ca4-17fdae2deacc`**; request `1a6acba8-ea5d-41b2-b718-d9c61347cc4c`; verified SHA **L = `f11d2715f340c7caa03343862cb8dd73cc27f66b`**; observed remote SHA **R = L**; destination `refs/heads/master`; remote confirmed at `2026-10-04T07:02:49.963726Z`. |

These are existing caller instructions and durable publication facts, not a new
ownership reassignment by this planner. Preserve ffc43849's failed admission,
e41a7005's implementation, and b7c17822's repair/publication attribution separately.
Never relabel this Plan, Investigate, Debug, or Review as the release-A Code owner.

## Ground truth

| Card/old continuation assumption | What current code or evidence does | Consequence |
|---|---|---|
| A successor must implement the default flip. | B contains `08a367e98`, `a8890fd32`, the later fixtures, and L. `PtyBackendPolicy.Resolve` defaults Windows to modern and bypasses Windows discovery for `UnixPty`. `git merge-base --is-ancestor L B` exits 0. | No remaining default-flip implementation delta was found. |
| The card's unset/unknown selector still means inbox. | `src/Antiphon.Agents.Pty/PtyBackend.cs` keeps enum values 0/1/2; explicit legacy aliases remain deprecated; missing-pair fallback remains observable; unknown Windows requests warn and try modern. | This is release A, not the release B refusal or C deletion. |
| Capability and launch composition still need the original S2. | `PtyBackendConfiguration.cs`, `Program.cs`, `SessionRunnerRuntime.cs` and `PhoneHomeRuntimeAdapter.cs` already share the runtime decision for host argv, custody, HTTP and phone-home projection. | Preserve the landed implementation and nullable DTO deprecation after the four CLI fields. |
| All CARD-1022 failures remain outstanding. | `C1022TypedInputTests.ReadPrompts` accepts string/text-block content; `SessionQueueReceiptPlumbingTests.SeedReceiptTurnAsync` appends TurnEnd for partial-current input before idle-only recovery. Both are already in L. | Keep the portable reproductions and native assertions; do not reapply the two repairs. |
| The old frozen 152-result roster is authoritative. | Current plan is 155; CP-7 explicitly selects `C1022_Partial_receipt_turn_parks_interrupted_attempt`; typed reader has two argument results. | Preserve the current plan, counts and exact selection. |
| The 290 guard records were generated by release-A Code. | [Investigation](../../investigations/2026-10-04-card-1022-evidence-guard.md) proves 264 distinct imported paths, with 26 observed again in the prerequisite merge. No release-A authorship of those payloads was demonstrated. | Preserve attribution and the fixed old task base; do not blame Code or delete tip files to hide historical violations. |
| A zero-violation re-cut can automatically land through ffc43849. | The nonempty re-cut guard result is valid, but old owner tip is not an ancestor of B (exit 1). `AgentTaskLandSourceResolver.cs:368-400` checks the source task's recorded base against owner lineage before checking patch containment. The resolver blob is identical at this checkout and B. | Guard success is not adoption authority. A fresh master source cannot be promised to pass `-Land ffc43849 -FromTask <successor>`. |
| Original owner must now be silently replaced to proceed. | Subsequent caller briefs already explicitly commissioned e41a7005 and then b7c17822; b7c17822 has a confirmed publication. | Use the actual publication for acceptance. Do not submit a second land under ffc43849 or retroactively rewrite its status/base. |
| Linux green establishes Windows behavior. | Linux ordinary reports cover CP-1/2/9; Windows Debug `4608a549-7ba1-4e67-a42d-2aff1f4d8286` reports CP-3 20/20 and CP-7 24/24 at C, with zero failures/skips. | Keep native evidence at its actual SHA; it is not an L or B receipt. |
| There is one fresh Windows 95/95 run at C or L. | Initial Debug `28a1714e-dbf0-44c4-9ef4-f617fd53ee69` ran 92 at `4e865e05`, 90 passed/2 failed. The later Debug reran only CP-3/7, 44/44 at C; CP-4/5/6/8 were carried as prior evidence. | Do not invent a six-row total or erase the two original failures. Investigate exact landed-SHA qualification and any caller acceptance of prior rows. |
| Publication proves activation. | O is confirmed, but no current loaded Windows runner/host inventory or activation receipt was obtained by this Plan. The Debug's historical live-runner observation was an older build without `PtyBackendDeprecated`. | Measure current loaded state directly before deciding whether a restart is still needed. |
| Every old Unit failure is now a release-A regression. | e41a7005 reports 4,030 passed / 15 inherited missing-jq failures / 53 skips, with the same 15 failing at its base. These are historical, SHA-bound results. | A new failure needs exact-method comparison at the new task's B, not a blanket inherited-red exemption. |
| Shared delivery behavior can be simplified now. | Unix still uses Porta; non-modern/phone-home/uncertain-Herdr delivery remains conservative, with spill and whole-body receipts. | Preserve 900/3,000/1,024 conservative limits and existing per-kind narrowing; no envelope increase or spill removal. |
| Current master is just L. | Subsequent CARD-1031 changes touch CLI version/probe contracts and tests; other routing/cleanup work also landed. The A plan and named A production/fixture files remain unchanged from L. | Start from B; preserve all subsequent work. Never restore a whole historical tree or whole DTO to reproduce A. |

## Decisions

**D-1 — Continue the published release, with no assumed code delta.** Default to
acceptance reconciliation at O/L. This avoids duplicating an already implemented
feature. Reject blindly re-running the original S1–S3 implementation brief and
reject treating the old Blocked task as proof the release is still unpublished.

**D-2 — A fresh master baseline belongs to a new task.** Any required corrective
Code is cut at fetched, full-SHA B through the task creation API, with its own
recorded base and branch. Reject changing ffc43849's BaseRef, merging prerequisite
history into it, rewriting a pushed branch, or using this investigation-derived
Plan branch as the new Code base. The evidence exception does not need to be
implemented before this continuation.

**D-3 — Preserve the current plan and implementation.** Add this document beside
the original. Do not copy the old owner's plan/admission tree onto B. Keep all
three extra portable results, the TurnEnd correction, modern C1011 parameter,
CARD-1020 owned-process cleanup and later capability/probe changes. Reject using
the investigation's two-file synthetic re-cut as a production patch.

**D-4 — Preserve ownership through receipts and explicit commissioning.** O/L
remain attributed to b7c17822 under the already recorded caller supersession;
ffc43849 remains the original blocked attempt. A new defect correction may have
its own explicitly commissioned Code owner and new publication, without becoming
the author/owner of the earlier release. Reject automatic reviewed adoption from
B into ffc43849: patch equivalence does not satisfy its recorded-base lineage.
If the caller instead insists that ffc43849 must receive a new publication,
return a concrete decision; this plan authorizes no lineage bypass or branch reset.

**D-5 — Separate publication, qualification, activation and Mutation.** Preserve
C/O/L/R and the review evidence ID. Obtain existing reports before repeating
tests, and select only genuinely missing qualification. No receipt at C becomes
an L receipt after rebase. Keep PC-1–PC-73 and variants pending unless separately
discharged at the exact published source. The original brief paused Mutation;
this plan does not resume it.

**D-6 — Placement follows live policy.** This Plan read `GET /api/runner-defaults`
and `GET /api/session-runners` on 2026-10-04: revision 2, no per-kind overrides,
eligible Windows and Linux lanes, and one unavailable/draining entry. These are
observations, not placement pins. Re-read at each dispatch; omit `-Runner` unless
pinning an operational host. Omit `-Platform` for portable work, use
`-Platform Windows` for native Windows qualification and `-Platform Linux` for
Unix transport; `-Platform Any` clears an inherited OS pin. Before commissioning
Code, also read pipeline occupancy and host budgets and defer real source overlap.

**D-7 — TestDesign remains a separate stage if verification changes.** The table
below preserves the current roster as a candidate selection, not a new freeze or
a demand to repeat nine green rows. First investigate the acceptance gap because
the premise that release A still needs a successor implementation is false.
Then TestDesign binds any missing rows/new defect to the chosen source, existing
V/R witnesses and exact-method controls. Reject new tests solely to make a
documentation-only successor look nonempty.

## Successor admission and landing route

1. Read the current card, the three Code task details, Review evidence and O's
   structured publication. Record C/O/L/R and distinguish publication from the
   notification's delivery state. A missing caller notification is not permission
   to land the implementation again. Read existing activation/Debug evidence and
   the post-land companion before creating more work.
2. For this plan's publication, use the normal caller-managed documentation
   landing process. The only new authored deliverable is this file; the inherited
   investigation is context. Landing owns any rebase. Do not dispatch Code from
   this branch or import its already published source commits into another task.
3. If an actual residual defect is found, create/link its correction card and
   explicitly commission Code from current B, for example
   `delegate.ps1 -Role Code -Card <correction-card> -Worktree -StartRef <B> -Goal $goal`.
   Read a file-backed brief into `$goal`; there is no `-GoalFile`. The brief names
   the historical release owner b7c17822 and the new task as owner of only the
   corrective delta. Use the same authorized project/repository. No
   `-RepairSource`, implicit prior-task branch, or `-FromTask` is needed.
4. Before source edits, inspect the task detail and dispatch event: requested
   start ref, `worktreeBaseSha`, initial HEAD and observed branch all match B;
   tracked source/index are clean. Reconcile B with this plan's pinned source.
   Carry only the newly accepted plan amendment if this document has not landed;
   keep the original plan unchanged. Record a meaningful delta, not an empty
   commit to manufacture work.
5. Commit/push each correction slice on its own branch. Code and independent
   Review run `scripts/check-evidence-diff.ps1 -BaseRef <recorded-B> -HeadRef <S>`
   over every introduced commit through the exact pushed S, including report
   commits. Do not substitute merge-base/tip diff/current master for recorded B.
   Exit 1 is a policy failure; exit 2 is unverifiable. Generated receipts stay
   ignored. The grandfathered legacy files on B remain untouched.
6. A correction's Clean Final/Full Review names **that corrective Code task** as
   `subjectTaskId`, its exact branch/SHA and verified clean receipts. Caller lands
   it with `delegate.ps1 -Land <corrective-code-id> -ExpectedSourceSha <S>
   -ReviewEvidenceId <new-evidence-id>`. Keep its new O2/L2/R2 distinct from O/L/R.
   A defect-free qualification continuation has no new implementation land.

This route uses ordinary new-task ownership for a forward correction and the
existing receipt for the shipped release. It does not claim that a clean re-cut
can be adopted by the old owner. The fallback of creating a repair at the old
owner tip would reintroduce the failed historical range and is rejected here.

## Slices and files

| Slice | Files/artifacts | Work and exit condition | Tests/evidence |
|---|---|---|---|
| S0 — this additive plan | This file only; existing plan and investigation are read-only inputs | Commit/push the successor topology, source reconciliation, ownership chain and preserved roster. | Diff/whitespace, roster equality/counts, link checks and full Plan-task evidence-history guard; no product build needed. |
| S1 — acceptance investigation | Stored report; optional authored `docs/investigations/<date>-card-1022-release-a-acceptance.md` | Bind existing Review/Debug/activation/companion evidence to C/O/L and identify exact missing facts. Re-check current master and loaded binaries; do not change production state merely to inspect it. | Inspect existing CP receipts and actual runner/host SHA/backend, package and complete UserPrompt; report unknowns explicitly. |
| S2 — separate TestDesign, only for a demonstrated gap | This successor plan's selection/verification amendment; preserve `2026-10-03-card-1022-modern-conpty-only-plan.md` | Name the exact source to qualify (L for publication qualification, a new S for a correction), bounded rows and platform. Add a Verification design section before a Code dispatch. | Existing V-1–V-4/R-1–R-6 witnesses; real importer for both OS settings; actual expanded roster; no Checkpoints-namespace tests added (current census 377). |
| S3 — conditional forward correction | Only demonstrated defect files: selection in `src/Antiphon.Agents.Pty/PtyBackend.cs`; composition in `src/Antiphon.SessionRunner/PtyBackendConfiguration.cs`, `Program.cs`, `SessionRunnerRuntime.cs`, `PhoneHomeRuntimeAdapter.cs`; wire in `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs`; or fixture repairs in the test files named below | No edit is pre-authorized merely because a file appears here. A defect report and frozen scope choose the subset. Keep A semantics and current-master additions. | Red first in affected `PtyBackendPolicyTests`, `C1022BackendCapabilitiesTests`, `C1022BackendLaunchTests`, `C1022TypedInputTests` or `SessionQueueReceiptPlumbingTests`; then the amended closed checkpoint selection. Confirm inherited red at B without weakening assertions/timeouts. |
| S4 — review/publication/activation closure | Stored independent review, existing/new landing receipt, card revision and companion | Reuse O for existing A; use O2 only for an actual correction. Caller verifies required activation and records explicit disposition of remaining B/C and Mutation work. | Final/Full source qualification, native host provenance, complete destination receipt, and loaded runner/server identity; no implied PC-clean claim. |

## Acceptance measurements and verification handoff

The next investigation should answer these bounded questions, using existing
evidence first. This is the reason for `next: investigate`, rather than dispatching
another default-flip Code task.

* Is there qualification at **L** after the landing rebase? Enumerate per row
  source SHA, executed/passed/failed/skipped, clean source and build provenance.
  CP-3/7 at C are known green; the other Windows rows have prior-SHA evidence.
  If final landed-SHA Windows Debug is still owed, freeze CP-3–8 at L as a distinct
  95-result run. Do not perform a Windows row on Linux and accept its skips.
* Is release A active now? Read the serving Windows runner's loaded build/backend/
  raw request/fallback/deprecation, inventory retained detached hosts, and inspect
  a fresh owned modern host's actual image/package/complete receipt if required
  by the acceptance brief. Compare binaries and source independently. Health and
  a checkout SHA alone are insufficient. This Plan did not perform that canary.
* Was the Windows-runner-first activation already completed and followed by the
  required server activation/SHA observation? If not, the caller uses canonical
  restart runbooks after qualification and old/unknown host inventory. Do not
  kill live sessions, use `-AllowWorktree`, or restart from this Plan checkout.
  Release A requires no Linux ConPTY rollout; Unix transport is unchanged.
* Locate the same-board companion by the published owner's stable key
  `post-land-verification:b7c17822-63b4-4626-b5bb-525cfccc0cea`; preserve pending
  PC-1–PC-73, all variants and discovery obligations. Mutation remains paused
  until the caller explicitly commissions it against O/L. Missing evidence
  cannot close that obligation as clean.

If no implementation defect exists, report the acceptance facts and remaining
operational obligations without a Code successor. If a real defect exists, return
its reproduction and exact source/files to separate TestDesign. Release B/C
design or the snapshot exception must not enter that correction by implication.

### Checkpoints

Preserved current-master release-A roster for TestDesign/acceptance selection.
The original design's S1–S3 already exist on B. `After=S2` below means the
successor selection/source has been frozen before any new execution. This is the
only importable Checkpoints table in this document. Group names identify lanes;
they do not enforce placement. Select rows explicitly.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S2 | `tests/Antiphon.Agents.Pty.Tests -> bin-c1022-policy/` | portable-policy | `/*/*/(PtyBackendPolicyTests*)\|(Da1StartupResponderTests*)/*` | V-1, R-1 | 8 policy + 22 parser results; 0 failed/skipped | 30 | 4 | true | `ANTIPHON_PTY_BACKEND=inbox` |
| CP-2 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1022-capabilities/` | portable-capabilities | `/*/*/(C1022BackendCapabilitiesTests*)\|(RunnerCapabilitiesTests*)/*` | V-2 | 5 coherence + 7 existing results; 0 failed/skipped | 12 | 5 | true | `ANTIPHON_PTY_BACKEND=inbox` |
| CP-3 | S2 | `tests/Antiphon.Agents.Pty.Tests -> bin-c1022-windows-native/` | windows-modern | `/*/*/(PtyBackendContractTests*)\|(ModernPtyDa1Tests*)\|(PtyBackendEnvGuardTests*)\|(ConPtyEnvironmentIsolationGuardTests*)\|(C1022TypedInputTests*)\|(PtyInputChunkingTests*)/*` | R-1, R-2, R-6 | 4 + 4 + 1 + 2 + 3 + 6 results; 0 failed/skipped | 20 | 9 | true | `ANTIPHON_PTY_BACKEND=inbox` |
| CP-4 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1022-windows-launch/` | windows-owned-hosts | `/*/*/(C1022BackendLaunchTests*)\|(PtyBackendEnvGuardTests*)/*` | V-3, R-2 | 3 owned launches + 1 guard; 0 failed/skipped | 4 | 7 | true | `ANTIPHON_PTY_BACKEND=inbox` |
| CP-5 | S2 | `tests/Antiphon.PtyHost.Tests -> bin-c1022-windows-pair/` | windows-shadow-pair | `/*/*/(ShadowCopyStoreTests*)\|(PtyBackendEnvGuardTests*)/(Shipped_conpty_binaries_survive_the_deps_json_closure_filter*)\|(The_suite_ignores_an_inherited_pty_backend*)` | R-2, R-3 | Both named methods; 0 failed/skipped | 2 | 4 | true | `ANTIPHON_PTY_BACKEND=inbox` |
| CP-6 | S2 | `tests/Antiphon.Tests -> bin-c1022-windows-delivery/` | windows-delivery | `/*/Antiphon.Tests.Application/(PtyDeliveryCeilingsTests*)\|(SessionDeliveryProfileTests*)\|(GrokDeliveryShapeTests*)\|(TypedBodySpillTests*)/*` | R-4 | 14 + 7 + 14 + 9 results; 0 failed/skipped | 44 | 8 | true | `ANTIPHON_PTY_BACKEND=inbox;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S2 | `tests/Antiphon.Tests -> bin-c1022-server-guard/` | windows-queue-and-grok | `/*/*/(PtyBackendEnvGuardTests*)\|(SessionQueueReceiptPlumbingTests*)\|(RunnerGrokAdapterReadyTestsPty*)/(The_suite_ignores_an_inherited_pty_backend*)\|(C475_*)\|(C1022_Incomplete_or_stale_receipts_do_not_confirm*)\|(C1022_Partial_receipt_turn_parks_interrupted_attempt*)\|(C1011_windows_backends_reach_ready_and_complete_prompt*)` | V-4, R-2, R-5 | 1 guard + 20 retained queue + 1 native negative + 1 portable negative + 1 modern C1011; 0 failed/skipped | 24 | 12 | true | `ANTIPHON_PTY_BACKEND=inbox;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S2 | `tests/Antiphon.E2E -> bin-c1022-e2e-guard/` | windows-e2e-guard | `/*/*/PtyBackendEnvGuardTests/*` | R-2 | 1 guard, no browser; 0 failed/skipped | 1 | 5 | true | `ANTIPHON_PTY_BACKEND=inbox` |
| CP-9 | S2 | `tests/Antiphon.Agents.Pty.Tests -> bin-c1022-linux-argv/` | linux-unix-transport | `/*/*/(UnixPtyArgvTests*)\|(PtyBackendEnvGuardTests*)/*` | R-2, R-3 | 17 Unix argument-expanded + 1 guard; 0 failed/skipped | 18 | 5 | true | `ANTIPHON_PTY_BACKEND=modern` |

No row changes test selection, count, build, environment or cost from the current
master freeze. Portable CP-1/2 plus Linux CP-9 total **60**; Windows CP-3–8 total
**95**. All nine total **155**. This table is not evidence that any row ran in
this Plan task, nor authority for a whole Unit/full-assembly rerun.

After the separate freeze, use the checkpoint tool's `run --plan <frozen-plan>
--rows <selected-CPs> --expected-source-sha <actual-SHA>` and await terminal exit
(continue `wait` after 75). Any necessary tool bootstrap/build driver uses
`scripts/build-slot.ps1`; rows acquire their own slots. One isolated build/filter
per row, forward-slash outputs, no source edits in flight, no unleased retry after
slot timeout. Preserve unedited CHECKPOINT lines and validate SHA-bound clean
receipts. No live provider, live inbox, paid turn or browser is part of A's rows.
Remove only task-owned alternate outputs after all processes exit. Code/Review
also retain the full recorded-base history guard result through the final push.

### Cost and limits

Preserved full ordinary floor: **59 minutes** (portable/Linux 14, Windows 45),
plus up to 4 minutes bootstrap, authoring and actual slot waits. A distinct full
Windows Debug is another **45 minutes** only when still owed. Existing PC floor
is **578 minutes**, paused and separately commissioned; do not include it in an
ordinary correction dispatch. The historical full ordinary + bootstrap + PC +
Windows Debug estimate remains **686 minutes**, not a new spend instruction.

S1 is a bounded evidence/loaded-state investigation, estimated 15–25 minutes
before any explicitly commissioned canary. S2 is a separate selection/design
amendment; S3's authoring and verification cost cannot be assigned until a defect
is identified. There is no justification for repeating the old implementation
or whole Unit suite merely to create a successor.

## Plan validation and stage disposition

This Plan inspected the full dispatched brief, investigation, old Code brief and
admission, current plan/freeze, landed source, explicit successor-owner briefs,
Review result and Windows rerun result, plus live defaults/inventory and the
structured publication. No production build, test, mutation, restart or deploy
was run. Static document checks and the Plan-task history guard are reported in
the task's final report at its exact pushed SHA.

The old admission problem is real; the assumption that release A still needs to
be implemented and landed is stale. Next: **Investigate** the named acceptance
measurements at O/L. Separate **TestDesign** follows only for a proven
qualification gap or corrective delta; there is no direct Code handoff from this
Plan. No unresolved design choice prevents this document from being delivered.

## Acceptance verification addendum (2026-10-04)

The demonstrated qualification gap is frozen in
[the acceptance TestDesign](2026-10-04-card-1022-release-a-acceptance-test-design.md).
For this continuation use that document's closed CP-3..8 selection at published L,
not the nine-row candidate table above. It preserves the 155-result contract and
accepted ordinary Review; qualification, activation/retained-host acceptance and
paused Mutation remain separate. The fix design and source attribution above
are unchanged.
