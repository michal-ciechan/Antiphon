# CARD-1021: preserve routing-exhausted tasks across automatic runner placement

Date: 2026-10-04. Plan task: `fa74e03b-d6d3-4a04-961f-87c0242e383a`.
Inspected checkout and observed remote master:
`8bb0cea045f5a89359b0ba55712417f02e31a42a`.
Branch: `feat/card-task-fa74e03b`. Plan only; no production or test source changes.

## Outcome and stage boundary

The defect is present. Its exact refusal is **409 `runner_platform_unavailable`**
in `AgentTaskService.SelectConstrainedAutomaticAsync`, before task construction
and insertion. It is not the opt-in `routing_exhausted` exception. Exhaustion
currently excludes automatic remote placement, leaving only the local descriptor
to satisfy a specific platform. With a Windows local descriptor, Windows persists
Blocked and Linux refuses, even when an eligible Linux runner exists.

Recommend removing routing exhaustion as a host-shape exclusion. An otherwise
admissible request with a suitable runner should resolve its host normally and
persist through the existing Blocked path, including its routing audit, parent
note and attention projection. This also changes exhausted `Any` requests from
local-only to normal default placement. Host selection is not provider selection:
no candidate becomes available merely because a host was selected.

This is the decision-bearing Plan requested by the card. **D-1 through D-7 are
stated defaults for caller acceptance; next is `decide`, then `test-design`.**
TestDesign was not folded into this dispatch. The verification requirements and
checkpoint proposal below are not a frozen Verification design or permission to
skip TestDesign. No build, test, paid dispatch, setting change or restart ran here.

Read the full live CARD-1021 and its history with `scripts/card.ps1`: revision 0,
no history. Read CARD-0090, CARD-1023 and the named collision cards. Owners read:
`docs/project-context.md`, `docs/ops-http.md`, `docs/antiphon-api.md`, the relevant
orchestration and card-lifecycle contracts, checkpoint/build guidance, and the
canonical AppHost restart runbook. CARD-1011's landed plan and exhaustion amendment
are historical evidence, not a new test run by this Plan task.

## Ground truth

All source line numbers below are at the inspected SHA above. Follow symbols
after another card lands; do not apply edits using stale line offsets.

| Card assumption | What the code or existing test does | Consequence |
|---|---|---|
| The default exhausted walk itself throws 409. | `server/Application/Services/AgentTaskService.cs:955` and `:1006` throw only with `RefuseIfExhausted`. Default arms at `:965` and `:1016` set `routingExhausted` and retain the first candidate's kind/tier as a stored snapshot. | Preserve the opt-in exception; repair the later placement obstruction. |
| Remote placement refuses with `routing_exhausted`. | `DefaultRunnerRoutingPolicy.cs:239` returns the exclusion reason `routing_exhausted`; `AgentTaskService.cs:1734` rejects remote preferences for any exclusion, then `:1697` restricts the fallback to local. The throw at `:1708` uses `RunnerPlatformProblems.Unavailable`. | Actual exception: `ConflictException`, HTTP 409, code `runner_platform_unavailable`, message `No eligible runner can run a Linux task.` in the current matrix. The policy reason and HTTP code are different. |
| Linux behavior depends on the OS running the test/server. | `tests/Antiphon.Tests/Application/WindowsGrokRoutingPolicyTests.cs:182` defines a directory whose local descriptor is always Windows and remote descriptor always Linux (`:188-195`). No host-OS test selects the refusal branch. | This is local-versus-remote topology plus a required-platform constraint. Add reversed descriptors so a future Linux-local/Windows-remote installation is covered too. |
| Every exhausted request using a remote default refuses. | With `RequiredPlatform.Any`, `AgentTaskService.cs:1290` does not call constrained selection; the policy's local fallback is persisted Blocked. Explicit remote intent returns from `DefaultRunnerRoutingPolicy.Decide` at `:209` before automatic exclusions, subject to its other validation. | Narrow the premise: the demonstrated refusal requires automatic placement and a specific platform the eligible local descriptor cannot satisfy. Any and explicit-remote are separate regression cases. |
| No task/event trail is saved for the demonstrated Linux refusal. | Placement runs at `AgentTaskService.cs:1283`; entity construction is `:1300`, Blocked assignment `:1408`, Add(task) `:1508`, Created/Blocked events `:1509/:1535`, and SaveChanges `:1565`. `WindowsGrokRoutingPolicyTests.cs:80-93` compares fresh-context task and event IDs before/after the refusal. | The refusal precedes every new task/event insert. This is not a task that the dispatcher later loses. |
| Only CARD-1011's Linux assertions need updating. | `WindowsGrokRoutingPolicyTests.C1011_exhausted_pair_blocks` has four role/platform results (`:37-43`). Its Linux arm asserts 409 and unchanged task/event sets; Windows asserts Blocked (`:122-131`). `DefaultRunnerPinTests.Required_pin_conflict_is_unchanged`, `:147-159`, additionally pins exhausted Any to local, its audit reason, and zero readiness calls. | Update both contradictory oracles in the same implementation slice. Preserve the pin-conflict portion of the older method. |
| RoutingPinCandidateCreateTests and TaskPlatformPlacementTests also contain the same Linux exhaustion branch. | At this SHA, `RoutingPinCandidateCreateTests.cs:55` pins default Required-list Blocked, `:83` pins explicit `RefuseIfExhausted` 409, and `:109` pins single-candidate `model_disabled`. `TaskPlatformPlacementTests.cs:260`, `:420`, and `:457` pin platform/shape refusals; there is no exhausted-Linux method there. | Run the complete three-class CARD-1011 CP-2 selection together. Do not mechanically replace every 409 in the named classes. The exact Linux exhaustion branch is in WindowsGrokRoutingPolicyTests. |
| The routing engine needs an OS-aware alternate candidate. | `ComplexityRoutingService.cs:273-309` walks the composed list, keeps skipped reasons and leaves `Chosen` null when exhausted. `ExhaustedSentence` at `:143-173` preserves the governing pin/chain and every skip reason. | Keep the walker, Required-pin boundary and provider ordering unchanged. Selecting an unapproved provider would violate CARD-0090. |
| Attention needs a new durable table or incident. | `AttentionService.cs:199-206` projects durable list-governed Blocked tasks with the exhaustion prefix; `:396-440` groups by list source, and `:506-520` emits RoutingExhausted/Error with task/card/board evidence. | Reach the existing durable row and projection. No new attention kind, alert sink, schema or migration. A grouped attention item is not necessarily one row per task. |
| Card trail means immediately moving the card. | `AgentTaskService.cs:1319` binds CardId and `:1524` includes the card in Created detail. Pipeline Blocked DTO at `AgentTaskPipelineStatusService.cs:493-506` retains a card ref and exhaustion flag. `CardWorkTransitionService.cs:181-189` requires a non-null DispatchedAt, which creation-time Blocked lacks. | Prove a linked task, event trail and card-linked attention. Do not invent a dispatch timestamp or force a column change. |
| A bound CardId alone guarantees a task appears in the card's text-correlated thread. | `CardThreadService.cs:155-177` matches card citations in title/goal, not CardId. `:188-199` returns matched tasks with their status. | For the thread witness use the ordinary CARD-prefixed title and explicit card binding. For an uncited explicit binding, prove card-linked attention/pipeline instead; fixing all thread correlation is a different scope. |
| A dispatch preview is authoritative placement admission. | The form's advisory hint and explicit-platform conflict check are `client/src/features/delegations/DelegateModal.tsx:89-115`; it sends automatic placement to the server. Pipeline status is a projection of existing rows (`AgentTaskPipelineStatusService.cs:493`). The server's worktree-base preview is later, at `AgentTaskService.cs:1485`, and is returned at `:1608`. | None can cause or preserve the demonstrated pre-insert refusal. No separate authoritative routing/placement preview endpoint was found in the documented API/source. Keep preview claims advisory; do not add a second placement resolver. |
| Blocked must await a new human action even after an approved candidate recovers. | CARD-0090's explicit operator amendment wants auto-resume within the approved list. `AgentTaskDispatcher.cs:1578-1670` re-walks, checks host-kind compatibility/cascade limit, and requeues the same row without changing RunnerId. | Preserve recovery onto the frozen host and required platform. Do not broaden the list, auto-change the host, or describe Blocked as permanent. |

### Exact failure chain and platform matrix

1. Both aliases held: the walker returns no chosen candidate. The default create
   branch sets `routingExhausted=true`, retaining the head only as task metadata.
2. `AgentTaskService.cs:1241-1249` passes that flag into `DefaultRunnerShape`.
   `DefaultRunnerRoutingPolicy.cs:227-240` converts it into a local-only decision.
3. A specific requirement invokes `SelectConstrainedAutomaticAsync`. Remote
   preferences cannot pass `ExclusionFor`. Its exclusion arm considers only the
   local descriptor's eligibility, platform and required-platform capability.
4. Windows local + Linux required throws at `:1708`; Windows required returns
   local at `:1704` and reaches Blocked persistence. General remote candidate
   enumeration at `:1713/:1759` is never reached in the refused case.

| Request, with an otherwise valid Worktree Worker and both candidates unavailable | Current behavior with eligible Windows local and Linux remote | Proposed behavior |
|---|---|---|
| Automatic + Windows required | Created Blocked on local | Created Blocked on the normal eligible Windows selection |
| Automatic + Linux required | 409 `runner_platform_unavailable`; no new task/events | Created Blocked on the normal eligible Linux selection |
| Automatic + Any + remote default | Created Blocked on local with `reason=routing_exhausted` | Created Blocked on the resolved default; normal host-selection audit |
| Explicit matching remote | Can persist Blocked after ordinary explicit-host checks | Same contract; explicit host stays frozen |
| `RefuseIfExhausted=true`, walked list | 409 `routing_exhausted`; no new task | Same opt-in contract on both requested platforms |
| Required single candidate is held | Existing `model_disabled` refusal | Same; this is not the walked-list default exhaustion branch |
| No eligible required-platform host, incompatible explicit host, or unsupported remote shape | Existing placement/validation refusal | Same; independent admission failures are not converted by this slice |

The final row bounds this recommendation: it fixes loss of a task caused solely
by treating routing exhaustion as a remote-shape defect. It does not promise a
durable task for every invalid/unplaceable request. If the caller wants a trail
even when **no** host can be selected, choose a separate deferred-placement or
refusal-ledger design; a null RunnerId currently means local, not unassigned.

Read-only platform observations at approximately 01:59 UTC on 2026-10-04:
`GET /api/runner-defaults` returned revision 2, a global Linux-lane preference and
no kind overrides. `GET /api/session-runners` returned eligible Windows and Linux
lanes plus an unavailable/draining temporary entry. These are dated observations,
not a fleet recipe. Re-read both routes before dispatch. Omit `-Runner` unless
pinning one host; omit `-Platform` for these portable tests, use it only for an OS
requirement, and use `-Platform Any` only to remove an inherited requirement.

## Decisions

- **D-1 — Default: durable Blocked after ordinary placement.** Remove only the
  routing-exhausted exclusion from automatic host selection. Keep the create
  variable that sets task status and suppresses provider-auth probes. Reason:
  known provider holds explain Blocked, not an assertion that a matching host
  cannot accept a durable task. This restores CARD-0090 and the allow-by-default
  principle without choosing another provider. Reject making 409 the common
  default: it would regress working Windows/Any callers and require a new
  persistent refusal/attention mechanism to satisfy the visible-trail requirement.
- **D-2 — Default: Any follows the same placement policy.** Remove the obsolete
  `DefaultRunnerShape.RoutingExhausted` input and `ReasonRoutingExhausted` policy
  constant once the exclusion has no consumers; update constructor callers.
  Retain normal kind-default/global-default/fallback precedence and its frozen
  audit fields. Reject a Linux-only exception or clearing the flag only in the
  constrained selector: those preserve the same incorrect local-only behavior
  for Any and can strand recovery on the wrong host. The snapshot head remains
  metadata; the returned routing DTO must still contain zero chosen candidates.
- **D-3 — Default: keep independent admission rules.** Preserve explicit host
  intent, existing-process affinity, Worktree/kind/custody constraints, required
  platform evidence, host availability, concurrency and workspace admission.
  Preserve `RefuseIfExhausted`, single-candidate holds and explicit pin conflicts.
  Reject catch-all ConflictException handling or inserting a task before these
  validations. CARD-1023's unknown compatibility observations are not a reason
  to introduce another refusal here; this slice changes no version policy and
  does not relax hard OS requirements. No deferred/unassigned host state is added.
- **D-4 — Default: reuse existing visibility.** Persist the existing Created and
  Blocked events, governing list IDs, failure prefix, CardId and parent queue note
  in the normal save/transaction. Attention remains a grouped read projection.
  Test card-linked evidence through real projection services; for the thread,
  retain the customary card citation in the task title. Reject a new card column,
  synchronous move to NeedsDecision, fabricated dispatch time or duplicate alert.
  Parent queue persistence is not proof of transcript delivery; this card adds
  no new notification channel or receipt claim.
- **D-5 — Default: preserve recovery, with no dispatcher rewrite.** While all
  approved candidates remain held, repeated ticks must leave the row Blocked and
  launch/prep nothing. On recovery, only the approved list can requeue it, with
  host/requirement unchanged and the existing pre-launch platform/auth fences.
  Reject auto-host migration, caller-selected emergency providers or a blanket
  disablement of CARD-0090 auto-resume.
- **D-6 — Default: one production slice plus its evidence/documentation.** The
  production change is confined to `DefaultRunnerRoutingPolicy.cs` and the shape
  constructor in `AgentTaskService.cs`; existing create persistence owns the rest.
  No DB, DTO wire, runner binary, UI, terminal or deployment-script change is
  expected. If tests expose a separate defect, preserve the evidence and return
  a scoped finding instead of expanding this implementation into it.
- **D-7 — Default: regression-only ordinary review, separate TestDesign.** Freeze
  the affected contracts below, including the opposite topology and complete
  visible trail. Do not commission the whole assembly, Grok live qualification,
  Windows inbox testing, or unrelated deployment suites for this server-policy
  change. Review judges introduced regressions against the accepted contract;
  inherited red is verified at its exact base method and reported separately.

## Implementation slices

### S1 — placement fix and contradictory test oracles, one committed slice

Files:

- `server/Application/Services/DefaultRunnerRoutingPolicy.cs`: remove the
  exhaustion-only exclusion, now-unused shape member and reason constant. Keep
  every other exclusion and explicit-intent branch. Do not turn unavailable
  runners into eligible ones.
- `server/Application/Services/AgentTaskService.cs`: update the shape constructor.
  Preserve both default exhausted branches, `RefuseIfExhausted` precedence,
  `repeatOf` precedence, provider-probe suppression, status/event/note persistence,
  governing IDs and frozen placement fields. Do not add a catch-and-insert path.
- `tests/Antiphon.Tests/Application/WindowsGrokRoutingPolicyTests.cs`: update
  `C1011_exhausted_pair_blocks`/`VerifyAsync` so the two Linux cases use the same
  durable Blocked assertions as Windows. Retain all four arguments, healthy and
  one-held cases, unchanged Human pin assertion, zero chosen outcomes, exact
  skip reasons and platform checks. Replace the old no-task assertion with
  fresh-context evidence for exactly the newly created task and its events.
- `tests/Antiphon.Tests/Application/DefaultRunnerPinTests.cs`: update the exhausted
  Any portion of `Required_pin_conflict_is_unchanged` to assert the eligible remote
  default and normal audit. Keep all existing explicit-kind/pin-conflict checks.
  Update `DefaultRunnerShape` constructions after removal of the obsolete member.
- New `tests/Antiphon.Tests/Application/RoutingExhaustedPlacementTests.cs`: the
  24-result proposal below, using real service entry points and isolated schemas.
  Extend `DefaultRunnerKit` in `DefaultRunnerCreateTests.cs` only for explicit
  fixture seams needed for these tests; never change its defaults to hide red.
  Dispatcher fixtures use `DelegationTestServices`, and any process-spawning
  fixture keeps the assembly-local limiter.

Keep `RoutingPinCandidateCreateTests.cs` and `TaskPlatformPlacementTests.cs` in
the same verification selection as the changed CARD-1011 matrix. Their existing
opt-in and independent refusal assertions remain correct. Any changed assertion
in them must be justified by a specific D-n, not by replacing 409 with success.

If a red-first witness commit is used, its message must say the expected red;
commit/push it before running the selected method. Commit/push the implementation
and changed oracles before the final checkpoint run. Do not edit during a run.

### S2 — contract documentation and handoff

- `docs/orchestration-loop.md`: replace the Windows Review/Debug paragraph's
  exhausted-local-only caveat (current `:606-609`) with normal placement followed
  by durable Blocked, bounded by D-3. Keep opt-in 409 and approved-list recovery.
- `docs/ops-http.md`: document returned Blocked state/ID, normal frozen placement,
  grouped routing attention and explicit `RefuseIfExhausted` semantics together.
  Do not promise a card move or a new preview route.
- `docs/superpowers/plans/2026-10-03-card-1011-windows-grok-routing-plan.md`:
  append a dated supersession linking CARD-1021's accepted verification. Historical
  G-58/G-59 and PC-58/59 proved the old behavior and must remain labeled historical;
  do not rewrite their receipts as proof of the new contract. Their reversal and
  the retained G-23 Required-list fence belong to CARD-1021's new control mapping.
- This plan: TestDesign appends the frozen `## Verification design`, reconciles
  the final test roster and imports the checkpoint manifest. Code later records
  the exact implementation SHA and unedited checkpoint lines in its stored report.

Commit/push S2. Run the ordinary checkpoint selection once at committed S1-S2,
then independent Review and normal land of the implementation owner. A later
documentation-only commit needs explicit source binding, not an invented rerun.

## Verification requirements for TestDesign

No tests were executed for this Plan. The existing CARD-1011 CP-2 source roster
is 12 matrix + 17 Required-pin create + 15 placement = **44 results**. All three
classes run on Any: their Windows/Linux descriptors are controlled inputs, not
native Windows qualification. TestDesign must preserve that distinction.

Proposed new class roster (method names are intended identities for the freeze):

| ID | Proposed method in RoutingExhaustedPlacementTests | Separately parameterized results | Required oracle |
|---|---|---:|---|
| V-1 | `C1021_exhausted_automatic_placement_is_platform_symmetric` | 8: local Windows/Linux x required Windows/Linux x Required pin/complexity chain | Fresh service CreateAsync returns Blocked. Correct descriptor/host, requirement/source/defaults revision/audit; all candidates skipped, none chosen. New task and Created/Blocked events survive a fresh DbContext. Reverse topology must reach the remote Windows host. |
| V-2 | `C1021_any_exhaustion_honors_kind_then_global_default` | 3: kind override, global remote, no preference/local fallback | Exhausted Any obeys the existing normal precedence, same host fields/audit as an otherwise identical placeable request; no false chosen-provider audit. |
| V-3 | `C1021_bound_exhaustion_is_visible_and_remains_blocked` | 2: pin/chain | Use explicit same-board card binding and a CARD-prefixed title, plus an existing parent session. Read real task detail/events, grouped RoutingExhausted/Error attention with card/board/task evidence, pipeline Blocked/card/exhaustion projection, and card thread. Exactly one parent queue note and one Blocked transition. Repeated ticks while held create no session/worktree/mirror/input; card column and DispatchedAt stay truthful. |
| V-4 | `C1021_refuse_if_exhausted_stays_opt_in` | 4: pin/chain x Windows/Linux required | With the flag, exact 409 `routing_exhausted` and the walk payload, no new task/events/parent message. Without it the same otherwise-valid request reaches durable Blocked. |
| V-5 | `C1021_real_placement_refusals_remain` | 4: explicit mismatch, explicit unknown platform, unsupported remote shape, no eligible required-platform host | Exact existing typed status/code and unchanged task/event sets; do not accept an arbitrary exception. Fixtures must separately establish the asserted placement problem. |
| V-6 | `C1021_returning_capacity_keeps_host_and_platform` | 2: pin/chain | Starting with a newly created remote Blocked row, clear one test-owned hold and drive the real recovery path. Same task/host/required platform, next approved alias only, no repeated requeue. Capture queue/recovery before a paid/real launch; existing dispatch-platform guards remain selected separately. |
| V-7 | `C1021_exhausted_create_skips_provider_probe_and_launch` | 1 | Recording/throwing fake provider probes and launch/preparation seams remain untouched during exhausted creation despite selecting a remote host; distinguish allowed descriptor/readiness observation from forbidden authentication/launch activity. |

Totals: **7 proposed methods / 24 results**, not 24 assertions or loop iterations.
TestDesign owns the exact arrangement and must adjust both census and matrix if
it splits a method. Avoid duplicating production placement in the fixture's oracle.
Seed pins, holds, defaults and descriptors only in test stores. No live pin or
hold writes are authorized by this plan.

Required existing regressions:

- R-1: all 44 CARD-1011 CP-2 results, with the exhausted Linux expectations updated;
  single-candidate and opt-in 409s still exact, Required/Preferred/card precedence
  intact and healthy/fallback choices preserve OS.
- R-2: `DefaultRunnerCreateTests` (7), `DefaultRunnerPinTests` (6),
  `ComplexityCreateTests` (14): **27** source-declared results. Protect explicit
  host failures, provider refusal, disabled defaults, existing-agent affinity,
  SourceLanding and transactional Blocked/note persistence.
- R-3: `RoutingPinCandidateDispatchTests` (12), `ComplexityDispatcherTests` (9),
  `ComplexityAttentionTests` (5), `DefaultRunnerRerouteTests` (16): **42** results.
  Protect governing-list recovery, attention grouping, incompatible-host fences,
  and existing parent-note behavior. No new delivery channel is introduced.
- R-4: `TaskPlatformDispatchTests` (11): platform changes block launch, unknown or
  unavailable descriptors hold, retry/reroute keep the frozen host and requirement,
  and reused-process mismatches send no input. All eleven are source-declared
  single-result methods at this baseline; internal matrix loops are not extra cases.

TestDesign must supply method-scoped negative controls. At minimum: restore the
old exhaustion exclusion; drop Blocked assignment; omit either governing list ID
or failure prefix; omit the Blocked event/note; bypass the required-platform match;
ignore explicit RefuseIfExhausted; skip kind-default precedence; and change the
stored host during recovery. Each control must name its decisive assertion and
compile; no whole-class mutation, fixture/build failure or zero-test red counts.
G-58/G-59's old expected refusal/no-insert controls cannot be reused unchanged.
An assertion of a pending queue row establishes persistence only, never delivery.

### Checkpoints

**Proposal for the separate TestDesign freeze.** All rows use the **Any lane**;
Group names make that explicit without an unsupported Lane column. Every row has
one isolated build and one exact filter. Expected source-derived total is
**44 + 27 + 24 + 42 + 11 = 148**; CP-3's 24 are planned, not existing or executed.
TestDesign must reconcile roster names/counts against the accepted implementation
design and validate import before Code. Do not run this table as frozen Code scope
until its Verification design exists and the caller has accepted D-1/D-2/D-3.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1021-policy/` | any-policy | `/*/*/(WindowsGrokRoutingPolicyTests*)\|(RoutingPinCandidateCreateTests*)\|(TaskPlatformPlacementTests*)/*` | R-1 | All 44 named-class results; 0 failed/skipped | 44 | 9 |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1021-create/` | any-create | `/*/*/(DefaultRunnerCreateTests*)\|(DefaultRunnerPinTests*)\|(ComplexityCreateTests*)/*` | R-2 | All 27 named-class results; 0 failed/skipped | 27 | 9 |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-c1021-exhaustion/` | any-exhaustion | `/*/*/RoutingExhaustedPlacementTests/*` | V-1..V-7 | All 24 proposed results after freeze; 0 failed/skipped | 24 | 10 |
| CP-4 | S1-S2 | `tests/Antiphon.Tests -> bin-c1021-recovery/` | any-recovery | `/*/*/(RoutingPinCandidateDispatchTests*)\|(ComplexityDispatcherTests*)\|(ComplexityAttentionTests*)\|(DefaultRunnerRerouteTests*)/*` | R-3 | All 42 named-class results; 0 failed/skipped | 42 | 12 |
| CP-5 | S1-S2 | `tests/Antiphon.Tests -> bin-c1021-platform/` | any-platform | `/*/*/TaskPlatformDispatchTests/*` | R-4 | All 11 named-class results; 0 failed/skipped | 11 | 8 |

Run one checkpoint-tool selection per committed slice group through
`tools/Antiphon.Checkpoints run --plan <this-plan> --after S1-S2`. Bootstrap any
tool build through `scripts/build-slot.ps1` with its own forward-slash alternate
OutputPath; use a verified prebuilt tool when available. The tool's row drivers
take their own slots. Continue `wait` to a terminal exit, never settle at 75.
Exit 4 is slot timeout, not permission to run unleased. Preserve each CP's SHA,
counts, source/build provenance, TRX path and slot outcome in the report. Keep
source frozen while running. Delete only owned `bin-c1021-*` outputs after owned
children exit; TRX/JSON/log payloads stay ignored.

This scoped profile replaces a whole-assembly run for this card. An extra driver
requires a stated reason. A failing pre-existing test is rerun by exact method at
the source base before classification; no broader base suite and no loosened
assertion/timeout. Review applies the operator's regression-only verdict rule.

### Cost

Prospective ordinary Code verification: **48 minutes** (9+9+10+12+8), including
one build per row, excluding authoring and slot waits. Independent ordinary Review
adds the same estimated 48 minutes. Tool bootstrap/import allowance: 4 minutes if
needed. These are estimates, not measured execution results. No native Windows,
live Grok, image rollout or paid provider acceptance is required by this change.
The separate TestDesign stage must price its final method-scoped controls; no
mutation total is fabricated here.

## Collision and sequencing notes

Re-read effective stage/host occupancy and refresh relevant task tips before Code;
these observations are not authority to dispatch into a collision. This Plan reads
other branches without merging/rebasing this assigned fast-forward-only branch.

| Concurrent work / source inspected | Overlap and handling |
|---|---|
| CARD-0959 fix task `ef329fb0`; refreshed branch tip `a8ec92f92673baef965afe0ae35ad5f869eec381` | Its actual dispatcher delta adds `RefuseUnauthenticatedRunnerCodexAsync` before the claim near baseline `AgentTaskDispatcher.cs:4397`. The latest commit adjusts warm-claim test capacity and does not establish green. Keep this fix intact. CARD-1021 changes no dispatcher source and keeps exhausted-create probe suppression. Recovery still traverses existing/new authentication gates. Rebase/land resolution must not reintroduce version admission that 0959's inert re-freeze removed. |
| CARD-1017 Code task `c37846cc`; observed tip `d9186457a03e70bef21e218336ab8c74a9b71199` | Treat AgentTaskService/card lifecycle as a collision even though the inspected tip has no AgentTaskService diff versus this baseline: active work owns Done cleanup inventory, CardRevisionLog, AppDbContext and retirement services. Preserve new inventory/transaction hooks as it advances. This slice adds no card move, deletion authority or migration, and must not piggyback cleanup on Blocked creation. Serialize any overlapping service edits. |
| CARD-1011 landed tests/plan | The obsolete Linux refusal and DefaultRunnerPin local-only oracles change with production. Keep 44-result CP-2 regression context and append the supersession of G-58/G-59 and PC-58/59. Do not rerun its native/paid qualification for this routing fix. |
| CARD-1020 plan `d87d195be`, TestDesign `d4203e109` | Owns DirectSessionRunnerClient teardown, test-owned PtyHosts and native process-exit receipts. Reuse its landed helper if a fixture needs one; do not edit that helper or run the Grok native class here. Keep any downstream limiter/teardown requirements. |
| CARD-0980 plan `6236cd04a`, TestDesign `17485f615` | Owns RemoteScriptContractTests LinuxShell/WSL path translation and Windows proof. Not a dependency for this Any server-policy suite; no helper/script edits or inherited Windows WSL failures imported into this verdict. |
| CARD-0983 plan `facd67183`, TestDesign `15a2601b8` | Owns required-jq rolling-harness receipts and related script tests. No deployment or jq harness is in this manifest. Preserve its stricter receipt policy when later performing an actual rollout. |
| CARD-1022 plan `13d7100febc3f4b595299eb982bf7fc5e44c88af` | Plans staged ModernConPty migration and truthful UnixPty observations. Tests here should use only the required-platform feature/descriptor facts, not interpret InboxConhost as the host OS. No inbox qualification or runner capabilities edit. |
| CARD-1023 plan `02d54f6ae2297d072693b495a972e5e5bda0954d` | Observation-first catalogue; later admission requires evidence-backed known-broken combinations. No version floor, failed-probe refusal or compatibility matcher is added by CARD-1021. Preserve normal provider/host gates and keep that product decision separate. |
| CARD-1010 plan `2395858ecc0a6ab2e8acc3b2deb2cb6298ed1307` | Observable store-replacement admission first; later explicit state/cache maintenance. This plan changes no runner lifecycle, lease or recycle behavior and requires only server activation. |
| Checkpoint census | `scripts/lib/checkpoint-usage.ps1:114` is literal **377** for `Antiphon.Tests.Checkpoints`. New tests are Application tests; leave 377 and its independent census unchanged. No checkpoint-tool repair belongs here. |

The related plans above were read from their recorded Git objects because most
are not yet in this checkout's tree. Their proposals are not landed behavior.
Later stages must refresh them and the relevant task status rather than copying
an obsolete branch implementation into this card.

## Activation and acceptance

After accepted decisions, TestDesign, Code, independent regression Review and
confirmed publication, **restart the server**. No runner-binary or database
migration is required by the recommended implementation.

The orchestrator/activation owner operates from the canonical checkout under
`docs/apphost-runbook.md` and `docs/orchestration-loop.md` operational autonomy:

1. Wait for lands to finish, inspect restart/launch locks and checkout ownership,
   record current HEAD and `/api/version`, then advance the canonical checkout to
   the published implementation. A worktree push alone does not advance it.
2. From an independent owner shell, run
   `pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha <full-landed-sha>`.
   Respect the runbook's Job Object/current-checkout caveat. Never `-AllowWorktree`
   or a second bare AppHost launcher. An exit-3 refusal is investigated, not bypassed.
3. Verify `/health` and `/api/version` exact expected SHA, then re-read runner
   defaults/catalogue. Health alone is not activation evidence.
4. Associate the accepted checkpoint receipts with the published source. If a
   real exhausted request occurs naturally, record task ID, Blocked event,
   governing list and observed platform plus attention/card evidence. Do not
   induce fleet holds, alter Human pins or spend on a live provider canary solely
   to demonstrate a pre-launch server-policy change. A separately sanctioned
   isolated acceptance request must carry the same evidence and remain non-launching.

If post-activation behavior contradicts the contract, retain the task/refusal
evidence and reopen the defect. Do not hide it with `-Runner local`, a platform
reset or an unapproved provider. A rollback is a reviewed revert plus canonical
server activation; it must not erase already-persisted Blocked tasks or rewrite
their history. Historical requests refused before insertion cannot be backfilled
reliably from this change.

## Caller decision and handoff

Accept D-1/D-2's normal-placement + durable-Blocked contract, including the Any
change, and D-3's boundary around genuinely invalid/unplaceable requests. Then
commission TestDesign to freeze the 24-result addition, 124 existing regressions,
method-scoped controls and five-row manifest. If the desired guarantee includes
every no-host refusal or a forced card revision/column change, revise this Plan
explicitly before Code; do not silently expand the small placement fix.
