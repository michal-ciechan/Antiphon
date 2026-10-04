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

#### Historical checkpoint proposal (superseded by the freeze below)

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

#### Historical cost proposal (superseded by the freeze below)

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

## Verification design

Frozen 2026-10-04 by TestDesign `d493e467`. The dispatch brief records the
orchestrator's acceptance of D-1/D-2/D-3; the decision request above is historical.
This appendix supersedes the proposed verification counts, manifest and costs,
without changing the fix design. The two historical proposal headings were
demoted because `PlanTableImporter.ExtractSection` reads the **first** exact
`### Checkpoints` heading. This is the only executable checkpoint table.

Inspection base: `bda1fe51071c3104d1b940294c6f46913e37288f`, incorporating
CARD-1011 `8bb0cea045f5a89359b0ba55712417f02e31a42a`. Read-only census also used
master `e0c74f38edb5351a565cc4a637d8d0341664be84`; all eleven selected existing
test files are byte-identical between those refs. CARD-1012/1013/1018/1024 and
later lands therefore do not change this selection. No build, test, mutation,
provider invocation, runtime restart or live hold/pin write ran in TestDesign.

### Inspection

- `WindowsGrokRoutingPolicyTests`: all three methods, `VerifyAsync`, `Hold`,
  `MatrixDirectory`; `RoutingPinCandidateCreateTests`: all 17 bodies and seeds;
  `TaskPlatformPlacementTests`: all 15 bodies, matrix directory and client |
  healthy/one-held/exhausted x Review/Debug x Windows/Linux -> R-1, V-1/V-4/V-5.
- `DefaultRunnerCreateTests`: create, fallback, explicit and excluded-shape bodies;
  complete `DefaultRunnerKit` including `Service`, `ReadAsync`, standing-session
  seed and fake directory/client; `DefaultRunnerPinTests`: conflict/exhaustion,
  kind pin, existing agent and SourceLanding policy bodies, constructor sites,
  SourceLanding graph/runner fixtures | Any defaults, explicit intent, custody
  and retained-process boundaries -> R-2, V-2/V-7/V-10/V-11.
- `ComplexityCreateTests.All_held_returns_200_Blocked_with_a_Blocked_event_and_parent_note`,
  `RefuseIfExhausted_is_409_routing_exhausted_with_the_walk_extension`, empty-cell
  and role-cell create bodies; `RoutingPinCandidateDispatchTests` all-held and
  hold-cleared bodies; `ComplexityDispatcherTests` all-held and hold-cleared
  bodies | pin/chain and empty chain, refusal versus durable row -> R-2/R-3, V-3/V-6.
- `ComplexityAttentionTests`: five bodies; `DefaultRunnerRerouteTests` blocked
  recovery/idempotence, parent delivery/restart/atomic-save bodies,
  `BlockNoteFault`, `TaskService`, `CreateDispatcher`; `TaskPlatformDispatchTests`
  reconnect, unavailable, reused-process bodies and dispatcher/directory/sink
  fixtures | recovery, grouped attention, platform revalidation -> R-3/R-4, V-3/V-6/V-9.
- `BridgeQueueHarness.CreateAsync`, attached-session branch, transcript insertion,
  adapter registration; `QueuedReceiptAssertions` complete body;
  `FakeAgentProtocolAdapter.OnSubmitted`/`SendInputAsync`; `TestDbFixture` clone
  and context lifecycle; `DelegationTestServices` registrations |
  nearest fixtures for the new file, real queue and simulated recipient -> V-8/V-9.
- `SessionMessageQueueInterruptedAttemptTests` verdict-less Sent/late-confirm,
  empty-composer and Enter-only recovery bodies; queue attempt save and shutdown
  catches | distinguish actual crash cuts from orderly cancellation -> V-9.
- `CardThreadServiceTests.Scenario.ThreadAsync` and seed shape;
  `AgentTaskPipelineStatusTests.CreateService`; production Attention grouping,
  pipeline `ToBlocked`, card-thread matching and card-transition dispatch-time
  predicate | fresh-read visible trail and unchanged card column -> V-3.
- Production `AgentTaskService.CreateAsync` routing/placement/save/note branches,
  `EnqueueBlockedParentNoteAsync`, constrained selection and explicit validation;
  complete `DefaultRunnerRoutingPolicy`; dispatcher resume and queued selection;
  queue stranded discovery, busy gate, interrupted-run recovery/late confirmation |
  all new-path guards below. Existing shape/admission guards outside the changed
  policy stay in R-1/R-2; no new concurrency, workspace or version policy is asserted.
- `PlanTableImporter`, `ManifestValidator`, `Program.Import`, checkpoint schema
  in `docs/testing-and-build.md`; project, lifecycle, orchestration, HTTP and
  session-runtime owners | import rules, execution floors, delivery verdict and
  activation. No tests are added to `Antiphon.Tests.Checkpoints`; its independent
  literal census remains **377** in `scripts/lib/checkpoint-usage.ps1`.

**Complete oracle replacement census.** A search of the current master tests
for the refusal code, exhaustion reason and exhausted local/remote claims finds
exactly these two executable methods pinning the obsolete placement behavior:

| Existing method/arguments | Remove | Replacement in S1 |
|---|---|---|
| `WindowsGrokRoutingPolicyTests.C1011_exhausted_pair_blocks(Review, Linux)` and `(Debug, Linux)` through `VerifyAsync` | `remote-exhaustion-refusal`, 409/code/message; unchanged before/after task/event ID sets (`remote-refusal-no-task`, `remote-refusal-no-event`) | Successful create, Blocked DTO and fresh persisted row on Linux `server2`; exactly one additional task ID, exactly one Created and one Blocked event for that ID. Keep platform, role, pin immutability, skipped reasons and no-chosen assertions. Both Windows arguments keep their existing Blocked oracle and gain the same durable-event checks. |
| `DefaultRunnerPinTests.Required_pin_conflict_is_unchanged`, exhausted Any portion | null RunnerId, `selected=local reason=routing_exhausted`, zero readiness resolves; stale class/inline comments | RunnerId=`server2`, `runner source=default requested=unset default=server2 selected=server2 reason=eligible`, exactly one readiness resolve at that point and no provider probe. Preserve all three preceding pin-conflict/no-insert checks and subsequent explicit Codex case. |

`RoutingPinCandidateCreateTests` has **no** exhausted Linux refusal to replace.
Its `Required_list_all_held_returns_200_Blocked_naming_the_pin` retains Blocked;
the method's historical `200` name is not an HTTP assertion. Its explicit opt-in
and single-candidate `model_disabled` methods keep exact 409 assertions.
`TaskPlatformPlacementTests` likewise has no exhaustion-specific refusal.
`C772_Platform_refusals_keep_precedence`, `Explicit_mismatch_has_no_side_effects`,
`Unknown_platform_refuses_specific_only` and
`Shapes_and_codex_worker_admission_are_preserved` retain their independent
refusals. `ComplexityCreateTests.RefuseIfExhausted_is_409_routing_exhausted_with_the_walk_extension`
is also unchanged. CARD-1011 G-58/G-59 and PC-58/59 are superseded historical
oracles, not evidence for this fix; its Required-list boundary remains intact.

**Setup to implement, not assumed present.** Add only
`tests/Antiphon.Tests/Application/RoutingExhaustedPlacementTests.cs` (all new
methods below). Keep fixture extensions local unless `DefaultRunnerKit` needs
optional injected event bus/registry/interceptors; preserve existing defaults.
Use one migrated isolated **database** per argument (the historical fixture name
is `IsolatedTestSchema`). All services and restarted harnesses use that connection
string. Dispose the writer and its transaction; open a fresh Npgsql connection
and fresh `AppDbContext`, with no tracked entities or ambient transaction, for
each durable/projection verdict. The fixture currently lacks reversed topology,
create-time commit cuts, provider-specific probe recorders, and a combined
create/dispatcher/parent-receipt harness; these are test-only setup work in S1.

Seed a same-board card, project and Backlog column, explicit Card binding and
`CARD-1021`-prefixed title; compare exact task IDs, never a shared fleet count.
Seed Human Required pairs or a Human complexity chain, and manual open-ended
holds on every candidate. Keep an available unlisted candidate as a decoy.
Seed runner defaults through `RunnerDefaultSettingsService`, controlled descriptor
platforms/features/eligibility and a recording directory; never infer OS from
InboxConhost. Set an explicit title to exclude asynchronous title diagnosis.
Create via the real `AgentTaskService`, not a hand-inserted Blocked row.
Enable `GrokCredentialProbeEnabled` and `CodexAuthProbeEnabled` explicitly for
V-7; return provider-attributed signed-out answers from the recording client.
Record the probe count even when the injected PC produces a typed authentication
refusal. Include a second valid parent session as V-8's wrong-destination decoy.
For V-3/V-6, an EF save observer records each task transition before downstream
dispatch can obscure an erroneous transient Queued state; assert these snapshots
as well as the final fresh-connection state.

The dispatcher graph registers logging/clock then `AddDelegationWorktreeGraph`,
real routing/availability services, all candidate definitions, recording launch,
input and worktree boundaries, and a controlled runner directory. Keep all
downstream prerequisites permissive while asserting held behavior, so an
unrelated missing definition/auth/backend cannot accidentally enforce no-spawn.
For recovery only, make the selected runner unavailable **after successful
creation**, then clear the second approved hold; this permits observing Queued
without real launch. Restore neither the host nor the requirement in fixture code.
Use the existing assembly process limiter on any test/helper that spawns git or
an owned process. Never boot production Program or connect to production 17204.

### Delivery inventory

The placement repair makes an existing asynchronous path reachable for remote
exhaustion; it therefore requires new producer-to-recipient evidence.

| Path and durable identity | Producer -> destination | Persistence boundary | Recovery and decisive receipt |
|---|---|---|---|
| Create-time Blocked parent note: task ID -> queue `SourceTaskId`, queue ID, parent session ID, reason `ContentDigest` | `CreateAsync` -> `EnqueueBlockedParentNoteAsync` -> real `SessionMessageQueueService` -> existing orchestrator session | Task, Created/Blocked events and Pending note share the create SaveChanges/transaction. Note is staged before that save; publish/wakeup is subsequent. | Busy parent waits for TurnEnd; already-idle parent and lost-wakeup restart use `FlushStrandedQueuesAsync`. Interrupted Sent attempt uses stored baseline/attempt evidence and late-confirm before retyping. Receipt is a matching complete UserPrompt in the **same parent session**, after the pre-delivery sequence, containing the task marker and full note body. V-8/V-9; G-8..G-10, G-42..G-47. |
| Held task -> approved-list recovery: same task ID and Rerouted event | real dispatcher resume -> durable Queued row -> existing dispatch path | Resume saves kind/level, cleared failure and one Rerouted event; host/platform stay frozen. | V-6 reads the same row after restart/ticks; R-4 covers existing platform fences. This is a state transition, not a recipient delivery claim. No child input may occur while exhausted. |

V-8 drives both busy and already-eligible recipients for both list sources. For
busy, prove no new UserPrompt and no submitted body while the parent is Working,
then append its genuine fixture TurnEnd and drive `OnTurnEndAsync`. For idle,
use the stranded sweep with an AlwaysOn fixture parent: do not manufacture a
future parent turn to get a green result. After receipt, repeat the sweep and
dispatcher tick; require one source-linked note and one matching complete prompt.

V-9 exercises **five cuts x pin/chain**, each through real creation and the real
queue. Faults must report `Fired=true`; reopen every reader independently:

1. `before-save`: throw in `SavingChangesAsync` when the new Blocked event is
   tracked. No task, events or note may commit. Dispose the failed scope, retry
   creation in a fresh scope, then require its recipient receipt.
2. `enqueue-fails`: throw at the parent's queue-sequence query, as the read
   `BlockNoteFault` does. Same all-or-nothing assertion and fresh create retry.
3. `after-commit-before-wake`: throw from the injected event bus at
   `AgentTaskChanged`, **after** create commits. Read the committed task/note by
   their recorded IDs; do not resubmit POST. Rebuild the queue/runtime attached to
   the same session/agent, then sweep to the complete receipt. An EF
   `SavedChanges` exception inside a still-open gate transaction is not this cut.
4. `attempt-before-input`: use `ISaveChangesInterceptor.SavedChangesAsync` to
   throw immediately after the ordinary note's attempt save (Sent, attempts=1,
   verdict=null), before entry into the delivery try/catch. Await the failed
   flush and dispose its graph. An adapter exception or cancellation is the wrong
   seam: the production catch deliberately reverts it to Pending. Assert no
   recipient prompt yet and the persisted Sent attempt, then attach a fresh
   graph and drive interrupted recovery/stranded sweep to one receipt.
5. `submit-before-verdict`: retain the adapter-produced complete UserPrompt,
   interrupt the queue before its Delivered-verdict save using a scoped EF
   interceptor, and await shutdown. Recreate the graph and recover the persisted
   attempt. It must late-confirm without another submitted body or another
   matching UserPrompt. Age/clock controls use the queue's documented interrupted
   window; no long wall-clock sleeps or production timeout changes.

For cuts 1/2 the failed request has no durable task ID; identity attaches only
to the new successful request. This design does **not** invent POST idempotency
or replay an ambiguously committed create. Absence of a parent session is an
intentional no-recipient boundary; V-1/V-2/V-4/V-5 cover manual creation/refusal.
Missing/dead parents and multi-producer sequence concurrency are unchanged queue
contracts, excluded from this producer-path qualification.

**Declared substitutes.** `BridgeQueueHarness` uses the real queue/runtime and
database with `FakeAgentProtocolAdapter`. Its `OnSubmitted` records only bytes
actually submitted by the adapter as UserPrompt; never insert the expected note
directly to satisfy an assertion. Match with `PromptSubmissionMatch.IsCompleteIn`
and normalized whole-body equality, not a marker substring, Sent, ack or event.
This proves producer/queue/recipient protocol integration and restart recovery;
it does not prove a native Claude/Grok terminal, phone-home transport or human
reading. Recording launch/worktree/client seams prove no calls at these server
boundaries, not native process teardown. Those transports are unchanged and
remain outside this card. Review rejects any replacement ending at queue insert.

### Proves it works now

All methods in this table are in `RoutingExhaustedPlacementTests` and run through
CP-3. Argument counts are separate TUnit executions, not loops/assertions. New
assertion labels named below are required labels in Code for decisive PC failures.

| ID | Exact method | Results and behavior | Layer and expected verdict |
|---|---|---|---|
| V-1 | `C1021_exhausted_automatic_placement_is_platform_symmetric` | 8 = local Windows/Linux x required Windows/Linux x pin/chain; eligible opposite remote and opposite-OS preferred default | Real create + fresh DB: `durable-blocked`, correct runner/observed OS/required platform/source/revision/audit, one extra task, one Created/Blocked event, unchanged Human list, all skipped and zero chosen. |
| V-2 | `C1021_any_exhaustion_honors_kind_then_global_default` | 3 = kind remote overriding global desktop, global remote with no kind override, no preference/local | Real runtime defaults + create: `any-runner`, `selection-source`, `defaults-revision`; compare against fixed fixture expectations, not a second call to the production policy. Null RunnerId means desktop, never unassigned. |
| V-3 | `C1021_bound_exhaustion_is_visible_and_remains_blocked` | 2 = pin/chain; card and parent bound; three real dispatcher ticks while every candidate held | Fresh DB/projections: exact task/events/note; `routing-pin-id` or `complexity`, `exhaustion-prefix`, `card-binding`, `routing-attention`, `pipeline-blocked`, `thread-blocked`; `no-session`, `no-launch`, no worktree/mirror/input, null DispatchedAt and unchanged card state. |
| V-4 | `C1021_refuse_if_exhausted_stays_opt_in` | 4 = pin/chain x Windows/Linux | Real create: with flag exact 409 `routing_exhausted` plus ordered skipped walk; unchanged task/event/queue ID sets (`opt-in-refusal`). Fresh otherwise-identical request without flag is durable Blocked. |
| V-5 | `C1021_real_placement_refusals_remain` | 8 = explicit mismatch, explicit unknown OS, Shared automatic requiring the remote OS, all known hosts wrong OS, preferred matching host ineligible, fallback matching host ineligible, preferred matching host missing feature, fallback matching host missing feature | Real create: mismatch/unknown exact corresponding `RunnerPlatformProblems` code; remaining six exact 409 Unavailable; `placement-refusal` and unchanged task/event/queue ID sets. Make all other descriptors wrong-OS so each individual fence is decisive. |
| V-6 | `C1021_returning_capacity_keeps_host_and_platform` | 2 = pin/chain; new remote Blocked task, approved second candidate recovers, unlisted third candidate already available, selected host temporarily unavailable | Real dispatcher + fresh DB: first tick resumes once; same `recovery-task-id`, `recovery-host`, `recovery-platform`, approved kind/alias, cleared reason; later two ticks resume zero times and exactly one Rerouted event. No task/session replacement. |
| V-7 | `C1021_exhausted_create_skips_provider_probe_and_launch` | 3 = held head Grok/Codex/Claude, each with a walked pair and eligible remote default | Real create with signed-out recording provider clients: zero provider-auth calls and launch/prep/input calls; successful durable Blocked. Signed-out answers allow detecting unwanted probes at `provider-probe-count`, without relying on a fixture exception. |
| V-8 | `C1021_blocked_parent_note_reaches_busy_or_idle_recipient` | 4 = pin/chain x busy/already-idle parent | Real create -> real queue -> adapter submission -> complete matching parent UserPrompt, as Delivery inventory; `busy-no-prompt`, `complete-parent-receipt`, `single-parent-receipt`. |
| V-9 | `C1021_blocked_parent_note_recovers_at_each_handoff` | 10 = pin/chain x five named cuts above | Real create/queue restart: `cut-fired`, `atomic-before-commit` or committed complete task/event/note set; matching recipient `recovered-parent-receipt`, same queue ID after committed cuts; `no-replayed-prompt` for submit-before-verdict. |
| V-10 | `C1021_explicit_exhausted_host_is_preserved` | 2 = explicit local Windows / explicit remote Linux, both with opposing runtime default | Real create: Blocked, explicit source, requested host preserved, no default substitution or provider choice. |
| V-11 | `C1021_other_shape_exclusions_stay_independent` | 8 = ExistingProcess; non-Worktree; unsupported kind; Codex SourceLanding; non-Claude orchestrator; specialist; SourceLanding wrong role; SourceLanding non-Worker | Pure policy with otherwise admissible isolated shape: exact retained exclusion reason (`shape-exclusion`). Exercise each independently, including Claude orchestrator/Mutation for the last row, so earlier kind/role fences cannot mask it. |

New total: **11 methods / 54 results**. The proposal's 24 became 54: +14 delivery
results, +8 independent retained-shape rows, +4 admission-boundary rows, +2
provider-head cases and +2 explicit-host successes. No unchanged test is renamed
or removed to make the census fit. Durable Blocked is the completion criterion:
**the task status must be Blocked, not Done/Succeeded**, and the card is not closed.

### Guards the regression

- R-1: 44 results = `WindowsGrokRoutingPolicyTests` 12 (3 methods x 4),
  `RoutingPinCandidateCreateTests` 17 and `TaskPlatformPlacementTests` 15.
  Decisive oracles: exactly the two replacements above, approved ordered pair,
  untouched pin, independent exact refusal/no-insert assertions and platform match.
- R-2: 27 results = `DefaultRunnerCreateTests` 7, `DefaultRunnerPinTests` 6,
  `ComplexityCreateTests` 14. Preserve explicit intent, ordinary default audit,
  excluded shapes, SourceLanding selected-host custody, independent provider/pin
  refusals and existing empty-chain/Blocked-note behavior. Their internal loops
  contribute one execution per method.
- R-3: 42 results = `RoutingPinCandidateDispatchTests` 12,
  `ComplexityDispatcherTests` 9, `ComplexityAttentionTests` 5,
  `DefaultRunnerRerouteTests` 16. Existing recovery tests assert one requeue,
  frozen host, approved candidate, grouping and complete parent receipts. Their
  legacy names mentioning incompatible Codex do not override their current bodies:
  Codex workers are admitted. Do not restore CARD-0796's removed policy.
- R-4: `TaskPlatformDispatchTests` 11 single-result methods. In particular
  `Reconnect_platform_change_blocks_launch` asserts Blocked/mismatch and zero
  launch frames; `Reused_process_mismatch_sends_no_input` asserts no input,
  Blocked and idle warm agent. Retry/reroute and missing-feature cases preserve
  the required platform. These are controlled phone-home peers, not native OS
  qualification or proof of a real provider reading a brief.

### Guard inventory

The inventory covers the fix's safety assertions and the retained policy guards
it could accidentally erase. Each independently removable policy predicate has
its own control, even where one method checks several. Broader unchanged
admission and transport internals selected incidentally by R-1..R-4 are regression
context, not a new qualification claim (see Out of scope).

| Guard | Plan reference and safety-critical invariant | Control |
|---|---|---|
| G-1 | D-1: exhaustion alone cannot exclude an otherwise eligible remote shape | PC-1 |
| G-2 | D-1: exhausted successful create persists Blocked | PC-2 |
| G-3 | D-4: pin-governed task retains governing RoutingPinId | PC-3 |
| G-4 | D-4: chain-governed task retains Complexity | PC-4 |
| G-5 | D-4: failure reason retains routing-exhausted prefix | PC-5 |
| G-6 | D-4: Created audit event persists | PC-6 |
| G-7 | D-4: Blocked event persists once | PC-7 |
| G-8 | D-4: parent note is staged with the Blocked task | PC-8 |
| G-9 | D-4: parent note preserves SourceTaskId identity | PC-9 |
| G-10 | D-4: parent note targets the caller's session | PC-10 |
| G-11 | D-4: task preserves resolved card binding | PC-11 |
| G-12 | D-4: routing attention projects the durable task/card evidence | PC-12 |
| G-13 | D-4: pipeline identifies the task as routing Blocked | PC-13 |
| G-14 | D-4: cited card thread includes the Blocked task | PC-14 |
| G-15 | D-5: held resume path must not enqueue a launch before finding a candidate | PC-15 |
| G-16 | D-1: exhausted create skips Grok authentication probe | PC-16 |
| G-17 | D-1: exhausted create skips Codex authentication probe | PC-17 |
| G-18 | D-3: chain RefuseIfExhausted remains a pre-insert refusal | PC-18 |
| G-19 | D-3: pin RefuseIfExhausted remains a pre-insert refusal | PC-19 |
| G-20 | D-2: Any kind default precedes global default | PC-20 |
| G-21 | D-2: Any global default is honored when no kind override applies | PC-21 |
| G-22 | D-3: automatic/explicit known OS must match hard requirement | PC-22 |
| G-23 | D-3: explicit unknown OS remains Unknown, not accepted | PC-23 |
| G-24 | D-3: preferred host must be dispatch eligible | PC-24 |
| G-25 | D-3: fallback host must be dispatch eligible | PC-25 |
| G-26 | D-3: preferred remote must advertise required-platform feature | PC-26 |
| G-27 | D-3: fallback remote must advertise required-platform feature | PC-27 |
| G-28 | D-3: existing-process automatic exclusion is retained | PC-28 |
| G-29 | D-3: non-Worktree automatic exclusion is retained | PC-29 |
| G-30 | D-3: unsupported worker-kind exclusion is retained | PC-30 |
| G-31 | D-3: Codex SourceLanding exclusion is retained | PC-31 |
| G-32 | D-3: non-Claude orchestrator exclusion is retained | PC-32 |
| G-33 | D-3: specialist automatic exclusion is retained | PC-33 |
| G-34 | D-3: wrong SourceLanding role exclusion is retained | PC-34 |
| G-35 | D-3: wrong SourceLanding task-kind exclusion is retained | PC-35 |
| G-36 | D-3: explicit host intent is preserved | PC-36 |
| G-37 | D-3: Required pin conflict still refuses before persistence | PC-37 |
| G-38 | D-3: single-candidate hold remains model_disabled | PC-38 |
| G-39 | D-5: no chosen approved candidate means no resume | PC-39 |
| G-40 | D-5: recovery preserves frozen host | PC-40 |
| G-41 | D-5: recovery preserves frozen required platform | PC-41 |
| G-42 | D-4: failure before create save commits nothing | PC-42 |
| G-43 | D-4: note enqueue failure cannot leave a committed task/event orphan | PC-43 |
| G-44 | D-4: committed pending note survives lost wakeup/restart | PC-44 |
| G-45 | D-4: busy parent receives no input until eligible | PC-45 |
| G-46 | D-4: interrupted attempt before input remains recoverable | PC-46 |
| G-47 | D-4: complete receipt is late-confirmed before any replay | PC-47 |
| G-48 | D-1/D-3: the Human pin is never rewritten by placement | PC-48 |
| G-49 | D-2: stored metadata head cannot become a falsely chosen routing outcome | PC-49 |
| G-50 | D-5: Blocked creation assigns no child session or dispatch time | PC-50 |
| G-51 | D-5: recovery is one transition, not a repeated requeue | PC-51 |
| G-52 | D-5: pre-claim platform mismatch blocks launch | PC-52 |
| G-53 | D-5: reused-process platform revalidation blocks input independently | PC-53 |

### Positive controls

Mutation runs **after land**, break/red/restore/green; Code runs ordinary V/R;
Review judges this design and the implemented oracles before land. No controls
ran here. Every row below describes a compiling production defect and the exact
test method/decisive assertion; no missing service, thrown fixture placeholder,
compiler error, timeout in unrelated setup or zero selection is a valid red.
Use `/*/*/ClassName/ExactTestMethod` for every individual red and restored-green
run. A parameterized method selects all its arguments; the named argument must
fail at the stated assertion. Do not widen to a class/suite.

In this table `N.` expands to the exact class `RoutingExhaustedPlacementTests.`.
For ordinary field-corruption controls, apply the assignment immediately before
the existing final create save, conditional on `routingExhausted`, leaving all
other state/foreign keys valid. These deliberate corruptions are independent;
they do not require replacing the whole method. Guard/PC numeric suffixes map 1:1.

| PC | Break the corresponding guard by this compiling defect | Exact method expected red and decisive assertion |
|---|---|---|
| PC-1 | In create's `DefaultRunnerShape`, set `ExistingProcess` to existing expression OR `routingExhausted` (reintroduces the old local-only exclusion without restoring a removed member) | `N.C1021_exhausted_automatic_placement_is_platform_symmetric`: remote argument fails successful create/`durable-blocked` with old Unavailable refusal. |
| PC-2 | Change the exhausted branch's `task.Status` assignment from Blocked to Queued | `N.C1021_bound_exhaustion_is_visible_and_remains_blocked`: `durable-blocked` expected Blocked. |
| PC-3 | Set `task.RoutingPinId = null` | `N.C1021_bound_exhaustion_is_visible_and_remains_blocked`: pin argument `routing-pin-id` equals seeded ID. |
| PC-4 | Set `task.Complexity = null` | `N.C1021_bound_exhaustion_is_visible_and_remains_blocked`: chain argument `complexity` equals requested complexity. |
| PC-5 | Set `task.FailureReason = "all candidates held"` | `N.C1021_bound_exhaustion_is_visible_and_remains_blocked`: `exhaustion-prefix`. |
| PC-6 | Remove/detach only the new Created event from the create ChangeTracker before save | `N.C1021_exhausted_automatic_placement_is_platform_symmetric`: exactly one persisted Created event for returned ID. |
| PC-7 | Omit only `AddEvent(...Blocked...)` in create | `N.C1021_bound_exhaustion_is_visible_and_remains_blocked`: exactly one persisted Blocked event. |
| PC-8 | Omit only create's `EnqueueBlockedParentNoteAsync` call | `N.C1021_blocked_parent_note_reaches_busy_or_idle_recipient`: source-linked note count equals one, then complete receipt obligation. |
| PC-9 | In `EnqueueBlockedParentNoteAsync`, set `SourceTaskId = null` | `N.C1021_blocked_parent_note_reaches_busy_or_idle_recipient`: queued `SourceTaskId` equals returned task ID. |
| PC-10 | Change the note's `AgentSessionId` to a second existing fixture session selected from `_db.AgentSessions` excluding `parentSession` (seed that decoy in V-8) | `N.C1021_blocked_parent_note_reaches_busy_or_idle_recipient`: queue destination equals original caller session and original caller receipt exists. |
| PC-11 | Set `task.CardId = null` | `N.C1021_bound_exhaustion_is_visible_and_remains_blocked`: `card-binding` equals the resolved same-board card. |
| PC-12 | Omit `items.AddRange(await BuildRoutingExhaustedItemsAsync(...))` in AttentionService | `N.C1021_bound_exhaustion_is_visible_and_remains_blocked`: `routing-attention` contains this task, Error, card and board evidence. |
| PC-13 | Replace `ToBlocked`'s `RoutingExhausted` expression with `false` | `N.C1021_bound_exhaustion_is_visible_and_remains_blocked`: `pipeline-blocked` routing flag true. |
| PC-14 | In `CardThreadService.LoadTasksAsync`, exclude `AgentTaskStatus.Blocked` from candidate query | `N.C1021_bound_exhaustion_is_visible_and_remains_blocked`: `thread-blocked` contains returned task ID with Blocked status. |
| PC-15 | At the top of the resume foreach, before the chosen-candidate guard, call `_taskLaunchSink!.Enqueue(task.Id, Guid.Empty, UtcNow(), new AgentLaunchSpec("claude", task.AgentKind, "claude", [], new Dictionary<string,string>(), task.WorkingDirectory, 120, 30))` | `N.C1021_bound_exhaustion_is_visible_and_remains_blocked`: `no-launch` recording sink count zero. This intentional early launch is intercepted; no provider process is started. |
| PC-16 | Add a call to `RefuseUnauthenticatedGrokAsync` with the existing create arguments in the exhausted arm, leaving Codex suppression intact | `N.C1021_exhausted_create_skips_provider_probe_and_launch`: Grok argument successful Blocked/`provider-probe-count` zero; catch a typed auth refusal only to assert the observed count, not to pass. |
| PC-17 | Independently call `RefuseUnauthenticatedRunnerCodexAsync` in the exhausted arm | `N.C1021_exhausted_create_skips_provider_probe_and_launch`: Codex argument `provider-probe-count` zero. |
| PC-18 | Change only the complexity arm's condition to `request.RefuseIfExhausted && routingWalk.Outcomes.Count < 0` | `N.C1021_refuse_if_exhausted_stays_opt_in`: chain argument `opt-in-refusal` exact exception/status/code. |
| PC-19 | Make the same change only in the pin-composed arm | `N.C1021_refuse_if_exhausted_stays_opt_in`: pin argument `opt-in-refusal`. |
| PC-20 | Disable the `_snapshot.KindDefaults.TryGetValue` branch in policy `Decide` | `N.C1021_any_exhaustion_honors_kind_then_global_default`: kind argument `any-runner` is remote and `selection-source` is KindDefault. |
| PC-21 | In `ApplySnapshot`, always set `_defaultRunnerId = null`, retaining `_snapshot` | `N.C1021_any_exhaustion_honors_kind_then_global_default`: global-remote argument `any-runner` is server2. |
| PC-22 | Make `AgentTaskService.PlatformMatches` return true | `N.C1021_exhausted_automatic_placement_is_platform_symmetric`: opposite preferred OS cannot win; stored observed platform equals requirement. |
| PC-23 | In `RefuseExplicitPlatform`, return when normalized observed OS is null | `N.C1021_real_placement_refusals_remain`: explicit-unknown argument exact Unknown `placement-refusal`. |
| PC-24 | Remove only the remote `DispatchEligible` check in `TryPlatformPreferenceAsync` | `N.C1021_real_placement_refusals_remain`: preferred-ineligible argument exact Unavailable refusal. |
| PC-25 | Remove only remote `DispatchEligible` in `SelectPlatformCandidateAsync` | `N.C1021_real_placement_refusals_remain`: fallback-ineligible argument exact Unavailable refusal. |
| PC-26 | Remove only remote feature check in `TryPlatformPreferenceAsync` | `N.C1021_real_placement_refusals_remain`: preferred-missing-feature exact Unavailable refusal. |
| PC-27 | Remove only remote feature check in `SelectPlatformCandidateAsync` | `N.C1021_real_placement_refusals_remain`: fallback-missing-feature exact Unavailable refusal. |
| PC-28 | Delete `shape.ExistingProcess` exclusion in `ExclusionFor` | `N.C1021_other_shape_exclusions_stay_independent`: existing-process `shape-exclusion` equals existing_process. |
| PC-29 | Delete Workspace exclusion | `N.C1021_other_shape_exclusions_stay_independent`: non-Worktree `shape-exclusion` equals workspace_not_worktree. |
| PC-30 | Delete `!IsWorkerAdmittedKind(shape.Kind)` exclusion | `N.C1021_other_shape_exclusions_stay_independent`: unsupported-kind `shape-exclusion` equals kind_not_supported. |
| PC-31 | Delete Codex-and-SourceLanding exclusion | `N.C1021_other_shape_exclusions_stay_independent`: Codex SourceLanding `shape-exclusion` equals kind_not_supported. |
| PC-32 | Delete non-Claude orchestrator exclusion | `N.C1021_other_shape_exclusions_stay_independent`: Grok orchestrator `shape-exclusion` equals kind_not_supported. |
| PC-33 | Delete specialist-role exclusion | `N.C1021_other_shape_exclusions_stay_independent`: specialist `shape-exclusion` equals kind_not_supported. |
| PC-34 | Remove only the role-disjunct in SourceLanding exclusion | `N.C1021_other_shape_exclusions_stay_independent`: wrong-role `shape-exclusion` equals source_landing_not_supported. |
| PC-35 | Remove only the task-kind disjunct in SourceLanding exclusion | `N.C1021_other_shape_exclusions_stay_independent`: non-Worker `shape-exclusion` equals source_landing_not_supported. |
| PC-36 | Before save overwrite explicit exhausted `task.RunnerId` with the opposing default (valid descriptor already seeded) | `N.C1021_explicit_exhausted_host_is_preserved`: stored requested host preserved. |
| PC-37 | In create, pass `IgnoreRoutingPin=true` to pin resolution for all requests, leaving request kind intact | `DefaultRunnerPinTests.Required_pin_conflict_is_unchanged`: first explicit-kind mismatch must throw RoutingPinConflictException before any new row. |
| PC-38 | In create's non-walked single-candidate availability catch, suppress the ModelDisabledException rather than rethrowing it | `RoutingPinCandidateCreateTests.Required_single_candidate_held_is_still_409_model_disabled_with_coda`: exact model_disabled exception required. |
| PC-39 | Replace resume's no-chosen `continue` with `task.Status = AgentTaskStatus.Queued; await _db.SaveChangesAsync(ct); continue;` | `N.C1021_bound_exhaustion_is_visible_and_remains_blocked`: fresh post-tick `durable-blocked` stays Blocked, even if downstream dispatch blocks it again (read/assert at the save interceptor boundary as well). |
| PC-40 | In successful resume set `task.RunnerId = null` immediately before its save | `N.C1021_returning_capacity_keeps_host_and_platform`: `recovery-host` remains server2. Read at resume commit before later dispatch. |
| PC-41 | Independently set `task.RequiredPlatform = RequiredPlatform.Any` in successful resume | `N.C1021_returning_capacity_keeps_host_and_platform`: `recovery-platform` remains Linux. |
| PC-42 | Insert an extra `await _db.SaveChangesAsync(ct)` after staging Created and before Blocked/note staging, in the no-open-gate fixture branch | `N.C1021_blocked_parent_note_recovers_at_each_handoff`: before-save argument `atomic-before-commit` finds zero new task/events/notes; mutant leaves an orphan. |
| PC-43 | Independently save inside `EnqueueBlockedParentNoteAsync` before querying queue sequence | `N.C1021_blocked_parent_note_recovers_at_each_handoff`: enqueue-fails argument `atomic-before-commit` finds zero new task/events/notes. |
| PC-44 | In `FlushStrandedQueuesAsync`, filter out this producer's `QueuedMessageOrigin.Delegation` in initial scoped-session discovery | `N.C1021_blocked_parent_note_recovers_at_each_handoff`: after-commit-before-wake argument `recovered-parent-receipt` must exist after the sweep. |
| PC-45 | Replace the `!await ReadWorkingAsync(...)` condition in the stranded-sweep loop with `true` | `N.C1021_blocked_parent_note_reaches_busy_or_idle_recipient`: busy argument `busy-no-prompt` and zero submitted bodies before TurnEnd. Invoke the sweep while busy, not only after. |
| PC-46 | Make `LoadInterruptedSentRunAsync` return an empty list | `N.C1021_blocked_parent_note_recovers_at_each_handoff`: attempt-before-input argument `recovered-parent-receipt` after restart; ensure fixture preserves Sent/unverdict-ed attempt at the cut. |
| PC-47 | Replace `LateConfirmAttemptedMessagesAsync` body with `await Task.CompletedTask; return LateConfirmCounts.Empty;` | `N.C1021_blocked_parent_note_recovers_at_each_handoff`: submit-before-verdict argument `no-replayed-prompt` equals one after the subsequent sweep. This bypasses the shared late-confirm guard for both interrupted-Sent and Pending recovery. |
| PC-48 | In exhausted create use `_db.RoutingPins.Where(p => p.Id == routingPinId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Reason, "mutated"), ct)` and await it before save; do not rely on modifying an untracked decision snapshot | `WindowsGrokRoutingPolicyTests.C1011_exhausted_pair_blocks`: fresh unchanged-pin `Reason.ShouldBe(reason)` fails. |
| PC-49 | In `ComplexityRoutingService.Walk.ToDto`, report the first skipped outcome as `chosen` when `Chosen` is null | `WindowsGrokRoutingPolicyTests.C1011_exhausted_pair_blocks`: candidates contain no chosen outcome and all have the exact held reason. |
| PC-50 | In exhausted create assign `task.AgentSessionId = caller.SessionId` (fixture parent exists) | `N.C1021_bound_exhaustion_is_visible_and_remains_blocked`: `no-session` requires null child session; no FK/setup error. |
| PC-51 | In successful resume, add a second Rerouted event with a fresh event ID but the same task ID/type/detail before the existing save | `N.C1021_returning_capacity_keeps_host_and_platform`: exact single Rerouted event assertion fails (two events); later ticks must still resume zero times. |
| PC-52 | In `ClassifyRequiredPlatformAsync`, replace the known-mismatch return with `(false, same detail)` | `TaskPlatformDispatchTests.Reconnect_platform_change_blocks_launch`: persisted Status must be Blocked, not merely held Queued. |
| PC-53 | In dispatch reuse branch skip only its second `ClassifyRequiredPlatformAsync(claimed, ct)` fence; retain pre-claim check | `TaskPlatformDispatchTests.Reused_process_mismatch_sends_no_input`: `sink.Inputs.ShouldBeEmpty`, Blocked, warm agent Idle. |

Controls modifying the same production file are serial. No batching savings are
assumed. PC-51 deliberately duplicates transition evidence rather than changing
only the query and being masked by its independent failure-prefix filter.
Mutation must restore every source byte and obtain green for that exact method.
If a control instead hits unrelated infrastructure or survives, return the
specific missing seam/oracle to Plan/Code; never broaden the filter or weaken a
test to claim red. All planned controls are executable with the stated fixture
seams; execution and measured timing remain Mutation's evidence.

### Out of scope

- No blanket guarantee for unavailable/invalid placement, concurrency refusal,
  forbidden workspace, SourceLanding custody or single-candidate hold. Their
  independent admission still precedes insertion; unchanged R-1/R-2 cover them.
  No new version matcher, pin mutation, provider fallback outside the list,
  runner migration, card transition or cleanup authority is authorized.
- Native Windows/Unix PTY, provider readiness/auth implementation, live Grok,
  real broker delivery, browser E2E, cleanup teardown and new HTTP wire schema
  are unchanged. No Windows-only checkpoint is required: these test bodies use
  supplied Windows/Linux descriptors and fake peers/adapters on either host.
  They do not call native PTY or a provider binary. The fixture's unused `cmd.exe`
  definition is not Windows execution evidence. If Code introduces native/path
  sensitivity, return to TestDesign for a **separate Windows Debug row at the
  exact final SHA**, not a skip or a reused earlier Windows receipt.
- No queue concurrency/idempotent-POST redesign, stale parent rebinding,
  transcript parser qualification, or lost historical-refusal backfill. Receipt
  tests use a live existing parent and the real queue with the declared adapter
  substitute; no end-user delivery claim exceeds that evidence.
- No `Antiphon.Tests.Checkpoints` additions; do not update 377 or run its namespace
  census just because this document imports a manifest. Full assembly and client
  suites are excluded under D-7; selected adjacent regressions are the closed scope.

**Caller/API impact and activation.** `AgentTaskEndpoints` POST returns
`Results.Created`, therefore **201 Created** with `status=Blocked`, task ID,
warning, routing and placement fields. It is not 200 and not a successful launch.
The busy night orchestrator receives a durable ID instead of the exhaustion-only
409 and should track that task/attention, refrain from inventing a provider, and
avoid resubmitting the same work. The parent note waits for its current turn.

Literal-code caller census: **zero** special matchers for
`409 runner_platform_unavailable` in `scripts/delegate.ps1` or `client/src`.
`Invoke-Antiphon` handles all HTTP errors generically and exits 1; its create
success branch already recognizes Blocked plus the routing-exhausted warning
and prints `BLOCKED`. `client/src/api/agentTasks.ts:useCreateAgentTask` uses
generic `apiPost`/mutation invalidation; `DelegateModal` shows a generic green
"queued" notification followed by the warning on success, and a generic error
on failure. That pre-existing wording can now appear for this Linux case; this
server-policy slice authorizes no UI rewrite. Operational docs have no executable
code matcher. Update the exhausted-local-only paragraph in
`docs/orchestration-loop.md`, document Blocked/opt-in semantics in `docs/ops-http.md`,
and append the CARD-1011 supersession as S2 requires. Historical investigation
`docs/investigations/2026-10-03-card-1011-code-continuation-698c0e44.md` records the
old literal code and stays historical. The API reference's opt-in 409 remains true.

After ordinary Code/Review and land, server activation is mandatory under the
earlier Activation section: canonical checkout advanced to the landed SHA,
canonical restart owner (never this worktree), exact `/api/version` SHA and health
checked. No runner upgrade or schema migration. Land alone does not activate it.

**Refreshed collisions and sequence.** CARD-0959 fix branch
`0def408517a4541238f54becf28d375cc313cf23` removed its proposed signed-out Codex
pre-claim refusal; compared with this base its dispatcher delta is only two blank
lines. That is also the current master dispatcher delta. Do not resurrect the
earlier `a8ec92f9` design; no semantic interaction remains with this placement fix.
Existing Grok/Codex create suppression and recovered-task platform checks remain.
CARD-1017 Code branch `bf1a8a14ffb223348627fd7ef4eb5910a087dd5f` owns Done cleanup/
retirement and its helpers; its latest report still records CP-9 zero-selection,
not established green. At this tip CardService and AgentTaskService have no diff
against this base, but shared lifecycle/DI ownership remains a sequencing risk:
serialize overlapping service/helper edits, preserve its inventory hooks, never
close the seeded card or use cleanup to demonstrate Blocked visibility.
CARD-1022's modern-PTY migration is now a landed plan/freeze, not authority to
change these descriptors; CARD-1023 observation-first compatibility and CARD-1010
store-replacement admission remain separate. Refresh those owner tips and stage/
host occupancy before Code. Start Code from this pushed freeze, integrate current
master through the normal landing workflow (no rebase of this FF-only task
branch), finish S1/S2 together, then run this manifest at the final committed SHA.
Any source collision resolution invalidates earlier receipts and needs the
affected row again at the resolved SHA. Keep Mutation post-land.

### Checkpoints

All five rows are Any-lane, Debug configuration, one isolated build and one exact
filter each. Source-declared existing count is **124**; new planned count is
**54**; ordinary closed scope is **178** executions. No native Windows row is
silently included. Use these exact filters, not the superseded 148 proposal.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1021-policy/` | any-policy | `/*/*/(WindowsGrokRoutingPolicyTests*)\|(RoutingPinCandidateCreateTests*)\|(TaskPlatformPlacementTests*)/*` | R-1 | all 44 listed results, 0 failed/skipped | 44 | 9 |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1021-create/` | any-create | `/*/*/(DefaultRunnerCreateTests*)\|(DefaultRunnerPinTests*)\|(ComplexityCreateTests*)/*` | R-2 | all 27 listed results, 0 failed/skipped | 27 | 9 |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-c1021-exhaustion/` | any-exhaustion | `/*/*/RoutingExhaustedPlacementTests/*` | V-1..V-11 | all 54 argument-expanded results, 0 failed/skipped | 54 | 15 |
| CP-4 | S1-S2 | `tests/Antiphon.Tests -> bin-c1021-recovery/` | any-recovery | `/*/*/(RoutingPinCandidateDispatchTests*)\|(ComplexityDispatcherTests*)\|(ComplexityAttentionTests*)\|(DefaultRunnerRerouteTests*)/*` | R-3 | all 42 listed results, 0 failed/skipped | 42 | 12 |
| CP-5 | S1-S2 | `tests/Antiphon.Tests -> bin-c1021-platform/` | any-platform | `/*/*/TaskPlatformDispatchTests/*` | R-4 | all 11 listed results, 0 failed/skipped | 11 | 8 |

Importer contract: exact nine headers, escaped OR pipes, positive integer minutes,
integer TUnit floors, `S1-S2` expands to S1 and S2, five unique groups/build IDs,
forward-slash `bin-.../` outputs, one first exact heading. `Expect` prose is not an
assertion parser: the importer extracts class tokens from Filter, so Code must
also reconcile the TRX argument-expanded roster against the V table. New missing
classes at TestDesign time are planned work, not evidence of a zero-test pass.

Run `Antiphon.Checkpoints run --plan
docs/superpowers/plans/2026-10-04-card-1021-routing-exhaustion-placement-plan.md
--after S1-S2 --expected-source-sha (git rev-parse HEAD)` from PowerShell using the verified tool;
continue `wait` until terminal, never stop at 75. Each row owns its build slot;
bootstrap builds use `scripts/build-slot.ps1`. Source stays frozen for the whole
selection. Preserve unedited CHECKPOINT lines and SHA-validated receipts in the
stored report, generated outputs ignored. Review runs
`scripts/check-evidence-diff.ps1` over the full candidate range. Clean only owned
alternate outputs after all owned children finish. Inherited red is established
at the exact base method, not by an extra assembly run or weakened assertion.

### Cost

All costs are **estimated**, not measured execution. Ordinary Code V/R floor =
**53 minutes**: CP-1 exact policy filter 9 + CP-2 create filter 9 + CP-3 new-class
filter 15 + CP-4 recovery filter 12 + CP-5 platform filter 8. This includes five
isolated builds (estimate 4 minutes each = 20) and 33 minutes of row tests/setup.
Separate tool/bootstrap/setup allowance = **4 minutes**; no allowance is counted
twice. Independent ordinary Review adds the same **53 minutes**.

Mutation PC floor = **636 minutes**: 53 distinct controls x **12 minutes** each
(5-minute isolated red build+exact method, 2-minute apply/restore/evidence work,
5-minute isolated restored-green build+the same exact method). Every method/filter
is fixed in the PC table; V-9's ten-argument filter receives the same conservative
per-phase five minutes. Queue timing uses bounded fixture clocks, not long sleeps.
No broad-class baseline/PC runs or hidden repetitions are budgeted. If actual
build/queue timings exceed this floor, report measured cost and a revised budget,
never remove receipt assertions.

Total setup + Code V/R + all PC red/restore/green = **693 minutes** (4+53+636).
Including independent Review = **746 minutes**. Verification is intentionally
separated: Code's handoff estimate is 57 minutes plus authoring, not 693. This
freeze adds 30 targeted executions and 5 ordinary minutes versus the 148/48-minute
proposal to close delivery and retained-guard gaps. Versus rebuilding separately
for each of the 11 new V methods and four regression groups (15 builds), the five
rows save an estimated **40 build minutes** at 4 minutes/build. PC batching savings
are **zero**: most controls share AgentTaskService, policy, dispatcher or queue
source and must stay independent/method-scoped. Slot contention/repairs are extra.

Pre-handoff audit: bodies read as listed; **guards=53, mapped=53, missing=0,
duplicate PC maps=0**. Every PC has a compiling mutation recipe, an exact method,
and an assertion/argument capable of failing; no unowned production seam is
needed. The Code stage implements the specified test-only recorders/interceptors
and 54-result roster before claiming verification. Import validation evidence is
recorded immediately below; TestDesign does not claim executable tests are green.

Read-only import evidence, 2026-10-04: executed the existing
`/work/worktrees/task-b491b9c4/tools/Antiphon.Checkpoints/bin-c1031-tool/Antiphon.Checkpoints.dll`
with `import --plan` pointing to this document and `--out .antiphon/c1021-freeze-import.yaml`.
It returned exit 0, `imported 5 rows`. The artifact remains ignored. The executed
DLL SHA-256 is `832f4ebdbc2a563969047075d24cc73b0a4e0cf0d3c814861299d10ee8ea1660`;
the importer's source and validator at that checkout's
`12806639cd068155e4a5eaf85048301295c2aa82` match this base. This is importer
execution evidence, not a build-provenance certificate for Code's tests.
Loaded that same assembly without compiling and called the real `ImportFile`
and `ManifestValidator.Validate` for both platform modes:

```text
IMPORT windows=False rows=5 builds=5 min=178 minutes=53 warnings=0 validation=ok
IMPORT windows=True rows=5 builds=5 min=178 minutes=53 warnings=0 validation=ok
guards=53 mapped=53 missing=0 duplicate-PC-maps=0 PC-recipes=53 exact-checkpoint-headings=1
```

The argument-expanded source census and `git diff --check` also passed. No build
or test run was used to obtain these results. The freeze is ready for Code; no
additional product decision or unverifiable production seam remains.
