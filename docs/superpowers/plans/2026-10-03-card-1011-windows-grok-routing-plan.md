# CARD-1011: Windows Review and Debug on the Grok route

Date: 2026-10-03. Plan task: `4c1708dc-6827-4519-a065-f54908b1ff61`.
Source inspected: `5f214b0c1daef4d6d7fbbbfbebd14deb83636639`.
Card: `3893294b-0b6a-4192-bc9a-d1f7712c65b2`, Antiphon board
`8988ca03-7414-47ad-b0b6-51556c701703`.

This is a plan and TestDesign artifact. No production source, runtime pin, runner
setting, or agent was changed. The original checkpoint proposal is historical;
the appended Verification design is the frozen Code scope. Windows acceptance
and runtime activation remain evidence gates, not completed results.

## Outcome and scope

Make the orchestrator explicitly use the effective required Review/Debug routing
pin for Windows work, with Grok/High (`grok-4.7`) first and ClaudeCode/High (`opus`)
second. Preserve OS placement independently of model selection. A Windows task
uses `-Platform Windows`; ordinary work omits `-Runner` and `-Platform` and follows
the current defaults/inheritance. `-Platform Any` explicitly removes an inherited
platform constraint. No dispatch recipe below fixes a fleet host location.

The smallest implementation uses the existing pin machinery and changes guidance,
tests, qualification documentation, and one live Debug pin. It does not require a
model-alias, startup-classifier, database-schema, or runner-binary change.
**That implementation also changes Linux Debug to Grok**, because existing pins
have no OS dimension. D-1 was approved by the operator in the TestDesign brief:
role-wide Debug uses Grok/High then ClaudeCode/High on every platform, including
Linux. This approval does not waive Windows qualification or activation ordering.

## Ground truth

Read-only API observations were collected at approximately 14:29 UTC on 2026-10-03.
Re-read them at activation; these are evidence, not durable fleet settings.

| Card assumption | Observed source/runtime behavior | Consequence |
|---|---|---|
| A desktop Grok task has not succeeded. | `GET /api/agent-tasks/d6e4138e` reports a Succeeded Review/Worktree task, Grok/High, required platform Windows, observed platform windows, 14:23:47.711Z to 14:27:03.748Z. Its Created event names the Human Required Review pin without a bypass. | The supplied canary is real route/turn evidence, not just a fake or self-reported model name. |
| The canary proves every Windows acceptance row. | The task and normalized transcript prove prompt receipt, execution, final report, and server session Stopped at 14:27:06.998Z. They do not identify the actual console backend or prove that a trust dialog appeared and was answered. | Reuse its proven facts; obtain backend/trust evidence and the other host row before landing the policy change. |
| Review needs a Windows pin change. | Stage-wide Review pin `50328b61-de56-4508-a037-d707d652d583` is already Human/Required, `[Grok/High, ClaudeCode/High]`; no card-specific pins were returned. | Retain Review unless a fresh read finds drift. Never erase another card's Human exception. |
| Debug can be changed just for Windows by editing its pin. | Debug pin `909836ed-74dd-4016-933d-ffe2c729624f` is Human/Required, `[Codex/High, Grok/High]`. `RoutingPin.cs`, `RoutingPinDtos.cs`, `RoutingPinService.cs`, and `routing-pin.ps1` support card+role or role-wide grains, with no platform key. | Choose role-wide scope explicitly (D-1), or commission a different design for OS-specific routing. |
| ModelLevelAliases controls the Windows provider exception. | `ModelLevelAliases.ForGrok` already returns `grok-4.7` for every tier; `ForClaude(High)` returns `opus`. It has no role/OS selection. | Do not change aliases or RolePolicy to try to override a Required live pin. |
| The checked-in orchestrator prompt says Windows Debug must stay on Claude with `-IgnoreRoutingPin`. | Searches of `server/Bundles`, both Antiphon orchestration/delegation skills, `AGENTS.md`, and the routing owner docs found no such literal instruction at this SHA. | Add a clear positive routing rule. Inspect any live custom append/older composition before claiming the source of the historic override is removed. |
| The prompt has room for another paragraph. | LF-normalized .NET string length of `server/Bundles/orchestrator.md` is exactly 14,310. `InstructionBundleTests.orchestrator_bundle_points_to_operational_autonomy_without_growing` enforces that cap for LF and CRLF. | Shorten existing exposition to fund the addition; do not raise the cap or weaken its test. |
| Related stage bundles can name Grok. | `InstructionBundleTests.each_stage_bundle_is_ascii_and_under_the_size_cap` forbids provider names in stage bundles. `stage-review.md` is 2,476 characters; `TaskPlatformGuidanceTests` requires at most 2,480 after normalization/trim. Debug is a helper and has no `stage-debug.md`. | Keep provider policy on pins and in orchestrator guidance. Audit stages, but do not add provider names or a Debug stage. |
| A configured modern backend establishes actual modern execution. | The ConPTY ADR documents fallback to inbox when the modern payload is unavailable. Native test `RunnerGrokAdapterReadyTestsPty` explicitly requests modern on Windows and currently has two marker cases. | Record the actual backend per session and reject a modern qualification row that fell back. |
| Qualification is independent of terminal size. | Grok startup classification is qualified at 120x30. Windows marker is ASCII `>`; Linux also accepts U+276F. Existing trust unit tests use the older captured dialog. | Record CLI/build/backend/size per live row; other sizes remain unqualified. A fresh worktree alone does not establish trust-prompt coverage. |
| An ordered fallback handles every startup failure automatically. | Candidate-list tests cover admission/dispatch availability walking. The orchestrator bundle treats AuthenticationRequired and other terminal launch incidents as explicit recovery decisions. An explicit `-Kind` narrows the candidate list. | Preserve automatic availability fallback by normally omitting kind/level; document a separate, explicit startup-failure recovery. Do not promise new automatic retry behavior. |
| A fleet location should be added to the policy. | `GET /api/runner-defaults` revision 2 has a global Linux-runner preference and no per-kind entries. `GET /api/session-runners` reports an eligible Windows runner and eligible Linux runner; another Linux entry is unavailable/draining. | Use current defaults and observed platform, not a baked-in runner name. |

### Accepted canary evidence and limits

Full task ID: `d6e4138e-fe11-4d7e-8ebf-b6a80b4fe597`.
Session ID: `26d8a18c-03f1-4563-b060-7ce6a1da7c8e`.
Source: `5f214b0c1daef4d6d7fbbbfbebd14deb83636639`.

`GET /api/sessions/<session-id>/transcript?since=0` contains:

- Sequence 1: rules-file UserPrompt; sequence 7: matching rules acknowledgement.
- Sequence 9, 14:24:40.796Z: complete task-tagged UserPrompt containing the brief
  pointer and trailing task marker. Subsequent tool calls read the referenced brief.
- TurnEnd model metadata includes `grok-4.7-build`; dispatch metadata is `grok-4.7`.
- Sequence 29, 14:27:03.144Z: returned canary report, including the expected heading.

The task has `failureCode: null` and the session is Stopped. Backend identity,
startup Ready observation, visible trust transition, and runner-side release
ownership still need their specific evidence. Its `reviewEvidence` is null and
an event says `review_evidence_subject_invalid`: this was a no-change launch
canary, not valid ordinary Review approval for any implementation. Do not land
anything on its `next: land` suggestion or reuse its clean assertion for Code.

## Decisions

- **D-1 — approved, TestDesign task 29fba8af, 2026-10-03:** use the existing
  stage-wide Human Required Debug pin with `[Grok/High, ClaudeCode/High]`, matching
  Review on every OS. Reason: this is the minimal durable policy change and makes
  Windows obey exactly the same pin mechanism. The operator explicitly accepted
  the Linux Debug change (server2 Grok works since CARD-1004; desktop canary
  d6e4138e passed on grok-4.7). Rejected as implicit behavior:
  silently expanding a Windows-only request to every platform. If Linux Debug
  must remain Codex, return to Plan for platform-scoped pin semantics; do not hide
  that difference in `-IgnoreRoutingPin` or ad hoc per-dispatch overrides. Per-card
  pins are not an OS selector and would affect non-Windows tasks on the same card.
- **D-2 — preserve gate fidelity:** accept the supplied desktop canary's proven
  route/turn facts. Require both actual Windows console hosts and observed fresh-
  worktree trust handling before landing the policy implementation. Rejected:
  treating one successful task or fixture replay as every acceptance row passing.
  Existing attributable evidence can satisfy a row without another paid run.
- **D-3 — configuration, not a new routing engine:** retain the existing alias
  table, Required-pin precedence, card exception behavior, exhaustion refusal,
  and runner placement. Rejected: changing RolePolicy or aliases to bypass pins,
  seed migrations that overwrite Human settings, or automatic settings writes
  from a tracker tick.
- **D-4 — bounded prompt change:** add durable routing semantics and point to the
  living owner for provider policy/diagnosis; compress the existing model-tier
  explanation to recover space. Retain the kind-versus-tier rule and owner link.
  Keep the 14,310 cap, all safety instructions, and stage-provider neutrality.
  Rejected: increasing limits, truncating text, or deleting a pinned safety rule.
- **D-5 — fallback boundaries:** preserve automatic availability walking to the
  listed Opus candidate. On a Windows startup failure, retain the failed task and
  session evidence, stop repeated Grok attempts, and use explicit authorized
  ClaudeCode/High recovery on the same required platform. Do not change holds or
  login state to force the fallback. Rejected: unconditional Windows Claude
  bypasses, a retry storm, and claiming arbitrary launch failures auto-reroute.
- **D-6 — what “orchestrator prompt on Grok” means:** update the orchestrator's
  dispatch instructions; the orchestrator itself stays on its existing supported
  provider. `AgentKind.Grok` orchestrators remain unsupported. No new provider
  support for the standing orchestrator is part of this card.
- **D-7 — separate verification design:** after D-1 is settled, TestDesign adds the
  required Verification design, full guard/PC inventory, exact test identities,
  measured or source-derived floors, and executable Windows-canary procedure.
  Code must not treat this proposed checkpoint list as a completed TestDesign.

## Implementation slices

### S0 — close the Windows qualification gate

Owner: a commissioned Windows verification task, before policy implementation lands.
Create `docs/investigations/2026-10-03-card-1011-windows-grok-qualification.md` with
the canary identities above and the WQ rows below. Retrieve the known session's
startup/pty-host logs via `docs/logs.md`, rather than reading credential stores.
Commit and push the evidence as a separate meaningful slice.

Reuse the accepted run where its log evidence permits. Missing real inbox,
modern, or trust evidence requires a bounded Windows confirmation. TestDesign
must fix the exact harness/configuration before that task runs. Prefer a dedicated
isolated Windows stack for backend experiments. If using the canonical runner is
necessary, a caller-approved configuration window must cover backend selection,
restoration, and canonical restart; the current Plan grants no settings mutation.
Never switch the backend beneath active test runs or kill unrelated sessions.

Tests/owners: `RunnerGrokAdapterReadyTestsPty`,
`RunnerGrokAdapterTrustPromptTests`, `GrokStartupReadinessTests`,
`GrokTrustPromptDetectorTests`; `docs/adr/0002-modern-conpty-backend.md` and
`docs/session-runtime-invariants.md`. Fakes supplement the real gate.

### S1 — guidance and qualification documentation

Change these files in one reviewable commit:

- `server/Bundles/orchestrator.md`: state that platform requirements never justify
  bypassing the effective Review/Debug pin; ordinarily omit kind/level to retain
  its ordered fallback. Link the owner section for current Grok/Opus policy and
  startup-failure recovery. Recover space in the model-tier explanation and
  record the final LF-normalized length. Preserve the existing platform paragraph.
- `docs/orchestration-loop.md`: add one authoritative Windows Review/Debug policy
  section with the chosen D-1 scope, the pin commands below, availability versus
  startup-failure fallback, and the gate/activation procedure. Mark its role-tier
  table as fallback policy rather than effective live routing where needed.
- `.claude/skills/antiphon-orchestrator/SKILL.md` and
  `.claude/skills/antiphon-delegate/SKILL.md`: link that section and eliminate any
  instruction implying OS alone authorizes a pin bypass. Keep model selection
  and runner selection distinct; inspect current content again at Code time.
- `docs/agent-kinds.md` and `docs/ai-agent-tui-configuration.md`: add the Windows
  qualification matrix with actual CLI/build, actual backend, 120x30, ASCII `>`,
  trust status, task/session/SHA, receipt and release evidence. Preserve historical
  fixture-only evidence as such. Document failed-startup diagnostics below.
- `tests/Antiphon.Tests/Application/InstructionBundleTests.cs`: guard the new
  platform/pin instruction and its owner pointer through the composed bundle,
  retaining the existing LF/CRLF cap guard and all existing invariants.

Audit `AGENTS.md` and `server/Bundles/stage-*.md`. At the inspected SHA neither
contains the alleged override, so no gratuitous edits are required. Keep stage
bundles provider-neutral; the existing platform instructions already separate
placement. If a newer checkout contains the override, remove it and extend
`TaskPlatformGuidanceTests` without relaxing its existing assertions or cap.

Suggested compact bundle wording (final wording must pass the complete contracts):

> Windows Review/Debug follows the effective required routing pin. OS needs do not
> authorize -IgnoreRoutingPin. Normally omit -Kind/-Level to preserve its ordered
> fallback; see docs/orchestration-loop.md#windows-review-and-debug-routing for
> policy and explicit startup-failure recovery.

### S2 — prove policy composition and both Windows backends

Add `tests/Antiphon.Tests/Application/WindowsGrokRoutingPolicyTests.cs`, using the
existing isolated-schema `RoutingPinCandidateCreateTests` and
`TaskPlatformPlacementTests`/`DefaultRunnerKit` patterns. Seed the proposed
Human/Required pair locally; never write production pins from a test. Cover
Review and Debug crossed with Windows and Linux for three behaviors: healthy
Grok chosen, held Grok selects Opus, both candidates unavailable blocks. Assert
stored kind/level, `RoutingPinId`, required platform, selected runner's platform,
and the routing audit; a fallback must retain the OS constraint. This is twelve
separately parameterized results, not twelve loop iterations counted as tests.
If D-1 changes, revise this matrix before Code.

Extend `tests/Antiphon.Tests/Agents/RunnerGrokAdapterReadyTestsPty.cs` with a
Windows-only two-argument method for actual inbox and modern backend selection,
using its isolated `DirectSessionRunnerClient`, assembly-local
`ParallelLimiter<ProcessSpawnLimit>`, and 120x30 launch. Assert actual backend,
Ready, and one complete nonce UserPrompt. Preserve the existing two marker cases.
Do not infer the real CLI's trust behavior from FakeGrok. Do not modify production
startup tolerances, add retries, or widen timeouts to obtain green.

Commit/push S1-S2 before the checkpoint run. Any source fix after a red row is a
new commit before a rerun; compare inherited failures at the base exact method.

### S3 — ordinary Review, gated activation, and Windows Debug confirmation

Code runs ordinary checks and hands off to independent Review. The caller lands
the original Code owner only after S0 is complete and valid Final/Full Review
evidence binds the implementation SHA. Keep canary evidence separate from that
Review. Activation then follows the ordered procedure below. Commit/push the
final qualification/activation evidence in the commissioned documentation task.

## Windows-specific acceptance

| Row | Lane and action | Required evidence |
|---|---|---|
| WQ-1 | Windows real Review on inbox console host, Grok 1.0.41 build `4220f3b224a6`, 120x30. | Pin honored without `-IgnoreRoutingPin`; actual InboxConhost; Ready/ASCII `>`; grok-4.7 launch and transcript model; complete task UserPrompt; report; session release. |
| WQ-2 | Same real Review on modern ConPTY, 120x30. | Same oracle plus actual modern backend and binary provenance, with no inbox fallback. Reuse d6e4138e only after identifying its backend. |
| WQ-3 | Real Windows launch into a new, previously untrusted worktree. | Capture before trust, detector's single affirmative `y`, disappearance of trust prompt, settled Ready, complete UserPrompt, report, release. Record which backend; run alongside WQ-1 or WQ-2. A path that was pre-trusted is not this row. |
| WQ-4 | Post-activation Windows Debug using the effective pin with no kind, level, or routing bypass override. | Created audit names the intended pin, Grok/High and observed Windows; loaded source/bundle stamp; meaningful read-only Debug probe; complete UserPrompt; final report; released session. |

The real CLI gate is not a stub-proxy or FakeGrok test. Review canary requests may
explicitly use `-Role Review -Kind Grok -Platform Windows` as acceptance requires;
that narrows selection and is not a test of automatic Opus fallback. Normal
dispatch and WQ-4 omit `-Kind`/`-Level` to exercise the ordered list. Set goals via
files and include a unique nonce. Ten minutes per real canary is the starting
budget; report timeouts/infrastructure refusal as incomplete, not qualified.

Record `grok --version` and actual backend metadata for each new run. If installed
CLI or geometry differs, qualify and document that tuple instead of relabeling
old evidence. Unqualified sizes remain out of scope. The existing 120x30 fixture
matrix is not permission to broaden the startup classifier.

Diagnosis uses the server's failed-startup log `screenReason` and the named
`%TEMP%\antiphon-grok-startup\grok-startup-*.txt` (or configured
`Agents:GrokStartupCaptureDirectory`). The file uses `outcome` and
`lastScreenReason`; logs and file keys are not identical. Correlate session,
frame sequence/time, Ready observations, and actual host. Frames are diagnostics,
not delivery receipts. Sign-in frames suppress content; use `grok login` through
the authorized operator workflow, never expose or copy auth files.

## Checkpoint proposal for TestDesign

The closed list below names lanes without inventing an unsupported `Lane` table
column: CP-1/2 are **Any**, CP-3/4 are **Windows**. Execute them as separate lane
selections against the same committed S1-S2 source. Do not rerun the whole suite
on Windows. TestDesign must freeze the final roster and guard/PC mapping and
add `## Verification design` before Code is commissioned.

- V-1: composed policy, cap, and existing instruction contracts; Unit lane.
- V-2: both roles, both platforms, healthy/fallback/exhausted policy matrix and
  existing pin precedence regression class.
- V-3: native inbox/modern fake startup and complete prompt transport on Windows.
- V-4: Windows checkout cap with both LF and CRLF inputs.
- R-1: no OS-driven bypass; no provider names in stage bundles; no cap increase.
- R-2: fallback stays within the required list/platform; explicit overrides and
  card pin precedence retain existing semantics.
- R-3: Ready is followed by exact transcript receipt, not screen/queue inference.

### Historical checkpoint proposal (superseded by the freeze below)

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1011-any/` | any-unit | `/*/*/*/*[Category=Unit]` | V-1, R-1 | Full Unit roster; InstructionBundleTests and TaskPlatformGuidanceTests present; 0 failed | 42 | 15 |
| CP-2 | S1-S2 | CP-1 | any-routing | `/*/*/(WindowsGrokRoutingPolicyTests\|RoutingPinCandidateCreateTests)/*` | V-2, R-2 | New 12-case matrix plus all 17 existing create cases; 0 failed/skipped | 29 | 5 |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-c1011-win/` | windows-native | `/*/*/RunnerGrokAdapterReadyTestsPty/*` | V-3, R-3 | Existing 2 marker cases plus 2 new Windows backend cases; 0 failed/skipped | 4 | 7 |
| CP-4 | S1-S2 | CP-3 | windows-bundle-cap | `/*/*/InstructionBundleTests/orchestrator_bundle_points_to_operational_autonomy_without_growing` | V-4, R-1 | Both newline cases execute; normalized length <= 14310; 0 failed/skipped | 2 | 2 |

CP-1's 42 is only a conservative source-derived lower bound from the inspected
InstructionBundleTests method count, not the Unit roster size. TestDesign must
replace it with the justified ordinary execution floor and confirm required
class identities. The other counts are source-derived/planned, not measured
passes. Existing Unit skips need explicit eligibility/accounting; a required
Windows row skipped off-platform is not a pass.

Use the checkpoint tool's `run --plan <plan> --rows <lane rows>` and `wait` to a
terminal result, with the documented host build-slot gate on every build/test
driver, including tool bootstrap. Report each CP with source SHA, build binding,
counts, TRX, slot result and reruns. Slot timeout is not run. Keep source frozen
while drivers run and remove only task-owned alternate outputs after completion.
No build or test was run during this Plan task.

Estimated ordinary floor: 15 + 5 + 7 + 2 = **29 minutes**, excluding slot waits,
authoring, Review's independent rerun, and live qualification. Live qualification
budget: up to **30 minutes** for two host canaries (trust folded into one) and
one Debug confirmation, plus **15 minutes** setup/restoration/evidence. Existing
attributable evidence can avoid a redundant live run. TestDesign supplies the
separate PC floor and total; no pending PC is silently waived by these estimates.

No asynchronous delivery mechanism changes in the proposed implementation.
TestDesign must inventory the existing canary path (task creation -> queued rules
barrier/task prompt -> normalized UserPrompt -> report settlement -> release)
and distinguish reused contract evidence from new policy tests. Its guard review
must include pin precedence/exhaustion/platform constraints, the budget boundary,
and readiness/receipt assertions; identify any needed exact-method post-land PCs.

## Activation order and fallback

1. Record D-1's decision on the card. Complete S0's WQ-1/2/3 evidence, including
   restoration from any backend experiment. These gate the policy implementation
   landing, not this plan's publication. A remaining gap keeps activation pending.
2. Complete TestDesign, committed Code checkpoints, and independent Final/Full
   Review. Record the post-land verification companion and ordinary-review/source
   identities as required by `docs/orchestration-loop.md` before landing Code.
3. Confirm structured landing publication. From the canonical main checkout,
   follow `docs/apphost-runbook.md`: wait for active lands and lock ownership,
   advance to the published source, restart AppHost through the canonical script,
   verify `/health` and `GET /api/version` SHA. Never deploy from this worktree.
   Bundle changes are embedded server resources; no runner/image upgrade is
   expected for this card. Any backend configuration restoration is separate.
4. Serialize the routing activation window: before commissioning another
   Review/Debug task, read `/api/runner-defaults`, `/api/session-runners`, and both
   role pin sets again. Also read pipeline/host occupancy before dispatch. Save
   existing full pin rows for rollback and inspect card overrides. Do not overwrite
   unrelated Human exceptions or silently adopt changed policy.
5. Under the approved D-1 scope and completed Windows gate, set the Debug pin:

   ```powershell
   pwsh -NoProfile -File scripts/routing-pin.ps1 get -Role Debug -Json
   pwsh -NoProfile -File scripts/routing-pin.ps1 get -Role Review -Json
   pwsh -NoProfile -File scripts/routing-pin.ps1 set -Role Debug -Provenance Human -Strength Required -Candidates 'Grok/High,ClaudeCode/High' -Reason 'CARD-1011: qualified Windows Grok; approved role-wide Debug policy, Grok first and Opus fallback.'
   ```

   Review is already the target list. Only if an authorized fresh comparison
   requires restoration, set Review with the same provenance/strength/candidates
   and a truthful reason. Read both rows back and confirm alias, order, strength,
   scope, and provenance; a successful write response alone is insufficient.
   Queued tasks retain their recorded selection; do not pretend the pin retargets
   already-created work.
6. Confirm `/api/agents/bundles` exposes the new orchestrator bundle stamp. Refresh
   the affected standing orchestrator through the idle-gated policy-refresh
   mechanism, or start a fresh authorized session; inspect its composed stamp
   and `BundlesOutOfDate`. A running conversation can still hold old text.
   Audit the relevant custom append and effective instructions for the old Windows
   exception, without exporting unrelated private prompt content. A Notify receipt
   alone does not prove a new bundle was composed. Do not kill Working sessions.
7. Run WQ-4 on Windows through the intended pin. Record the Created routing audit,
   actual backend/model, complete task UserPrompt, useful Debug report, and
   terminal/released ownership. Attach the new pin rows, source/bundle identities,
   WQ evidence and canonical restoration record to the card. Only then is rollout
   accepted. Commission the planned SourceLanding PCs through the companion.

For a startup failure, collect its task/session/failure-code and bounded capture
first. Preserve the row instead of re-dispatching Grok repeatedly. An authorized
recovery can select the already-listed ClaudeCode/High candidate with
`-Role Review` or `-Role Debug`, `-Platform Windows`, `-Kind ClaudeCode -Level High`,
and no `-IgnoreRoutingPin`; explicit kind narrows to that approved fallback.
If the effective card pin disallows it, surface the conflict instead of bypassing.
If all candidates are unavailable, leave the task Blocked for the operator.
No new automatic startup-failure rerouting is promised by this plan.

If activation fails, pause new affected dispatches, restore only the pin state
changed by this activation from the saved rows after checking for newer edits,
and record the failure. Restore any temporary backend settings and verify the
actual backend after canonical restart. A source rollback is a reviewed revert
plus normal land/restart/policy refresh, never a reset or force-push. Keep the
historical accepted canary evidence and the new failure evidence separate.

## Handoff

The original Plan handoff is superseded by the TestDesign freeze below. D-1 is
approved for every platform. Code admission and the Windows qualification gates
are specified below; the existing Review Grok/Opus pin remains in force.

## Verification design

TestDesign freeze: task `29fba8af-3bb0-489b-a0cb-914e22a491c3`, 2026-10-03,
source `0514484597439bc46cd1d2117ef1ceb92b7c59cd`. This appendix supersedes the
historical checkpoint proposal and its V/R identifiers, not S0-S3 or D-2..D-6.
No repository build, test, live dispatch, pin write or backend change was run by
TestDesign. Counts below are source-derived or planned executions, never passes.

The exact S1 prompt edit replaces the entire paragraph beginning `Model-tier
names are` and ending `ModelLevelAliases.cs` with the following text. Preserve
the surrounding blank lines and final file newline; make no other bundle edit:

```text
Model-tier names are **not AgentKind values**. `-Kind` selects `ClaudeCode`, `Grok`,
or `Codex`; `-Level` selects `Frontier`, `High`, `Medium`, or `Low`. Never pass a
model-tier name as either flag. Codex resolves to full model IDs, not bare family
names. See [agent kinds and model levels](../../docs/agent-kinds.md#3-model-levels)
and `server/Application/Services/ModelLevelAliases.cs`.

Windows Review/Debug follows the effective required routing pin. OS needs do not
authorize -IgnoreRoutingPin. Normally omit -Kind/-Level to preserve its ordered
fallback; see docs/orchestration-loop.md#windows-review-and-debug-routing for
policy and explicit startup-failure recovery.
```

Read-only UTF-16 string arithmetic after CRLF-to-LF normalization: current
file **14,310**, replaced span **868**, replacement **674**, final **14,116**,
headroom **194**. This is the raw file length including final LF, not the
catalog's trimmed length. The existing 14,310 assertion and its LF/CRLF arguments
stay unchanged. Keep the `Model-tier names are` anchor: the standing-pipeline
documentation fixture uses it as a section boundary. No safety paragraph or
platform instruction is shortened. If Code's base changes this span or count,
reconcile the exact edit and census before building; do not raise the cap.

**Publication gate:** this plan may land now. The new prompt and live Debug pin
must not land/activate ahead of WQ-1, WQ-2 and WQ-3 evidence and restored backend
configuration. Tests and qualification evidence can be prepared first. Separate
Final/Full Review must then bind the implementation SHA. Activation remains:
confirmed land -> canonical source advance/restart and `/api/version` -> fresh
pin/default/runner reads -> approved Debug pin write and readback -> bundle stamp
and idle-gated standing-policy refresh -> WQ-4 -> acceptance. A successful
d6e4138e turn, queue status, or Notify acknowledgement cannot waive a missing gate.

### Inspection

| Bodies read | Boundaries -> coverage or exclusion |
|---|---|
| Entire `InstructionBundleTests`, both classes in `TaskPlatformGuidanceTests.cs`, and `StandingPipelinePolicyDocumentationTests`, including composition, raw-file reads and section extraction | V-1/R-1; LF/CRLF cap, composition, stage neutrality, platform inheritance, retained section anchor |
| Entire `RoutingPinCandidateCreateTests` and `TaskPlatformPlacementTests`; `DefaultRunnerKit` factory, Service, ReadAsync, directory/client and seed helpers in `DefaultRunnerCreateTests.cs` | V-2/R-2; ordered Required list, holds/exhaustion, explicit narrowing, card precedence, OS placement; neither fixture launches a recipient |
| Entire `RunnerGrokAdapterReadyTestsPty`, `RunnerGrokAdapterTrustPromptTests`, `GrokTrustPromptDetectorTests`, `GrokStartupReadinessTests`/`GrokStartupFixture`; `RunnerGrokAdapterReadyTests.AnimatedAdapterReadyAsync`, NewAdapter/Spec and scripted-screen client | V-3/R-3; native versus scripted Ready, two markers, old trust capture versus current real CLI; classifier expansion excluded |
| `DirectSessionRunnerClient` constructor/BuildRuntime, StartAsync, GetCapabilitiesAsync, snapshot/transcript/input/kill and DisposeAsync; `PtyBackendPolicy.Resolve`, `HostSession` constructor/backend log; `RunnerGrokAdapter.SendPromptAsync`/WaitForReadyAsync; `GrokReadyWait` trust/settle branches | V-3/R-3; per-instance backend override, static capability is not actual-host evidence; complete receipt after Ready |
| `SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery`, `C475_AlreadyIdleWhenIdleHasRecipientReceipt`, WaitUntilAsync, OffsetClock, entire PtyWorld/ForwardingClient/InsertFault; entire `SessionQueueTranscriptPump` | V-4/R-4; busy/eligible recipient, six durable/input cuts, recipient JSONL then DB, queue recreation and owned disposal |
| `SessionMessageQueueGrokPtyIntegrationTests.Multiline_delivery_is_transcript_confirmed_through_the_real_grok_tailer`, its launch/service setup, transcript pump and wait helpers | V-5/R-4; actual Grok tailer/normalizer with FakeGrok, LF removal, inbox transport; live provider qualification excluded from this substitute |
| `TestDbFixture` initialization/options/isolation API; queue enqueue commit/claim, DeliverNextLockedAsync admission, LateConfirmAttemptedMessagesAsync and RecoverDeliveryRunLockedAsync; RoutingCandidates.Compose/PinSlots, ComplexityRoutingService.WalkCandidatesAsync/FirstSkipReasonAsync, RoutingPinService.ResolveAsync, AgentTaskService task persistence and platform selection | V-2/V-4/R-2/R-4; fault and mutation locations, persistence-before-input, no unlisted fallback, no duplicate delivery |
| Current bundle; relevant orchestration/delegate skill guidance; project-context, orchestration stage/Review/activation owner, testing manifest/runner/Mutation/delivery rules, agent-kinds/TUI startup sections, session-runtime delivery/release owner, ConPTY ADR/backend provenance and logs owner | S0-S3, all V/R; no live configuration inferred from documentation |
| CARD-0959 Inspection/Delivery/admission/checkpoint freeze; CARD-1005 freeze from commit `b1ed3f4244419f59bb71c85f398f3740526f8f93` (not present in this checkout's working tree) | Source census versus executions, explicit missing setup, independent controls and post-land custody |

Missing setup, to implement in S1-S2: five single-result InstructionBundle methods
named below; new WindowsGrokRoutingPolicyTests with three four-argument methods;
one two-argument native FakeGrok method; and a separately opt-in, single-result
real trust probe in that same native file for S0. The latter is verification
scaffolding, not a production adapter change. Keep helpers test-local. The matrix
can reuse `DefaultRunnerKit.Service(..., withRouting: true)` and its fresh-context
reader. Its private placement MatrixDirectory must be reproduced locally, not
made a production dependency. `CreateIsolatedSchemaAsync` actually gives each
case a cloned database; do not interpret its historical name as shared-schema
isolation. Use System/offset-real time, never a frozen queue clock.

Native prerequisites are staged FakeGrok/FakeClaude apphosts, Windows ConPTY
payload and manifest/log access. The existing class-level process limiter stays;
mark the new paid method Explicit plus the existing real-CLI opt-in convention,
and never run it in ordinary automation. Ordinary missing binaries, Windows skips,
zero methods or absent database are incomplete evidence, not passing rows.

### Delivery inventory

**No async producer, destination, persistence or recovery implementation changes.**
The new routing tests stop at durable admission and make no delivery claim. The
following inherited path is the path S0/WQ-4 must actually observe:

| Producer -> destination | Persistence boundary and durable identity | Recovery and observable receipt |
|---|---|---|
| CreateAgentTask -> dispatcher/launch queue -> bound Grok session | AgentTask.Id, selected RoutingPinId, AgentSessionId/accepted generation; durable task/session rows precede volatile launch scheduling | Existing launch/reconciliation owns failed or stranded starts. Preserve the failure; any retry is explicit. WQ captures task/session identity and first complete task-tagged UserPrompt; Created/Queued/Started is insufficient. |
| Composed rules -> runner rules file -> queued initialization -> Grok | Rules revision/digest and rules-refresh key joined to session generation and queue message | Closed rules barrier holds task input until initialization acknowledgement. WQ records the complete initialization UserPrompt and its matching rules acknowledgement before the task UserPrompt. Do not mistake that acknowledgement for receipt of the task. |
| Dispatcher task body/pointer -> SessionMessageQueueService -> PTY -> provider transcript -> normalized transcript | ExecutionTaskId/task marker -> SessionQueuedMessage.Id, generation, baseline sequence and complete body; message/attempt persist before input; runner transcript then DB receipt | V-4 exercises six handoffs below through the real queue. V-5 uses the real Grok tailer; WQ checks the real CLI. On a spilled brief, compare the whole delivered pointer including both task markers, then require the matching tool read of the task-owned brief and correct report. Never claim the full file was typed inline. |
| Grok report -> task settlement -> session release | Same TaskId/SessionId, final report token and terminal task event; release ownership is separate state | WQ retains normalized final report and checks runner process/session ownership after settlement: confirmed exit, warm pool owner or standing owner. Task Succeeded alone is insufficient. No new settlement/release mechanism is commissioned here. |

V-4's real queue producer is `EnqueueAsync`, not a seeded queue row. The frozen
six argument values are `insert-fails`, `pending-before-flush`,
`attempt-before-write`, `body-before-enter`, `recipient-before-ingestion`, and
`receipt-before-verdict`. Respectively they cover failed enqueue with no writes,
committed Pending on a busy recipient followed by TurnEnd, committed attempt
before bytes, body before Enter, recipient receipt before DB ingestion, and
ingested receipt before verdict persistence. Recreate the queue as the fixture
does, recover using the same message Id, and compare the entire recipient JSONL
UserPrompt AND normalized DB UserPrompt. The separate already-idle method proves
inline eligible delivery. The last two cuts must recover without another body
or Enter; the body-before-Enter cut sends only Enter; the empty pre-write cut
charges exactly one additional attempt. These are explicit fault simulations,
not claims of machine power-loss testing.

Substitutes and limits: routing fixtures fake runner descriptors and cannot
establish a launch or actual OS. DirectSessionRunnerClient uses the real runner
runtime/PTY/tailer but its capabilities always advertise modern; only the
session's host log proves its chosen backend. FakeGrok proves transport and
normalization, not current CLI trust/auth/model behavior. V-4's FakeClaude and
test transcript pump prove shared queue recovery, not Grok ACP ingestion (V-5)
or dispatch/rules/settlement crash recovery. Those latter producers are unchanged;
their full recovery matrices are explicitly excluded, while WQ supplies real
end-to-end success evidence through them. Review must reject a report stopping
at request, queue insert, Sent, event, acknowledgement, screen redraw or tail
substring. This scoped reuse does not certify every pre-existing delivery guard.

### Proves it works now

- V-1: composed guidance and bounded prompt | Unit, CP-1/CP-4 |
  Add exactly `InstructionBundleTests.C1011_composed_windows_routing_contract`,
  `C1011_model_kind_and_tier_contract`, `C1011_owner_role_wide_policy_and_fallback`,
  `C1011_qualification_gates`, `C1011_activation_order`. Read the real composition
  for bundle assertions and the actual owner/skill files for documentation
  assertions, not copied constants alone. Use whitespace collapse for wrapped
  sentences; do not normalize away flags, order or negation. Keep the raw cap
  test unchanged. Expected labels/oracles below bind the controls.
- V-2: required policy on both OSes | DB integration, CP-2 |
  `WindowsGrokRoutingPolicyTests.C1011_healthy_head_is_grok`,
  `C1011_held_head_falls_back_to_opus`, `C1011_exhausted_pair_blocks`.
  Each has exactly four `[Arguments]`: Review/Windows, Review/Linux,
  Debug/Windows, Debug/Linux. Seed a Human Required role pin with exactly
  Grok/High then ClaudeCode/High. Create Worktree workers without explicit kind,
  level or runner. Set the runtime preference to the opposite platform so an
  ignored constraint cannot accidentally pass. Read the saved task and Created
  audit from a new DbContext. Assert selected kind/High, alias `grok-4.7` or
  `opus` in routing outcomes, exact pin Id, role, RequiredPlatform and
  ObservedPlatform, selected directory descriptor's platform, and candidate
  order/outcomes. Healthy chooses 1/2; a manual open-ended Grok alias hold chooses
  2/2 and records its skipped reason; both alias holds produce Blocked, the same
  pin Id, an exhausted explanation and no chosen candidate. No role-policy or
  Codex escape. Also assert the role pin is unchanged after each create.
- V-3: both native Windows hosts really deliver | Native, CP-3 |
  add `RunnerGrokAdapterReadyTestsPty.C1011_windows_backends_reach_ready_and_complete_prompt`
  with exactly `[Arguments("inbox")]` and `[Arguments("modern")]`. Use its
  existing isolated client, fake home, 120x30, ASCII `>`, bounded cancellation and
  owned disposal. Require the per-session host log to contain respectively
  `pty backend: InboxConhost (requested 'inbox')` or
  `pty backend: ModernConPty (requested 'modern')`; no modern fallback accepted.
  Assert Ready and current Ready classification, no pre-input UserPrompt, then
  send a unique single-line `C1011 HEAD <nonce> TAIL` and require exactly one
  UserPrompt equal to the complete body. Preserve the existing two marker cases.
- V-4: shared queue receipt and recovery | Native DB integration, CP-5 |
  exact existing `SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery`
  (six results) and `C475_AlreadyIdleWhenIdleHasRecipientReceipt` (one).
  The decisive assertions are the whole `[text]` recipient/file and DB lists,
  retained message Id/baseline, expected attempt count, and no duplicate writes.
- V-5: Grok multiline queue receipt | Native DB integration, CP-6 |
  exact existing `SessionMessageQueueGrokPtyIntegrationTests.Multiline_delivery_is_transcript_confirmed_through_the_real_grok_tailer`.
  Expected `prompt.Text == body.Replace("\n", "")` and sequence > 1 through the
  production Grok tailer; Sent alone cannot pass this method.

Frozen V-1 owner assertions: the section heading is exactly
`## Windows Review and Debug routing`. Require the literal policy clauses
`Review and Debug on every platform, including Linux`, `Human Required`,
`Grok/High then ClaudeCode/High`, and `grok-4.7`/`opus`. Both skills link
`orchestration-loop.md#windows-review-and-debug-routing`. The owner distinguishes
availability walking from startup incidents with the sentence
`Startup failure requires explicit authorized recovery; do not retry Grok automatically.`
It preserves `-Platform Windows`, `-Kind ClaudeCode -Level High`, and
`no -IgnoreRoutingPin` in that recovery recipe. It states each of the following
as separate, individually asserted clauses:

1. `Both actual InboxConhost and ModernConPty require real Review evidence.`
2. `A fresh worktree must visibly show trust, receive one y, clear trust, and reach Ready.`
3. `Delivery requires a matching complete UserPrompt transcript.`
4. `Settlement requires a final report and confirmed release ownership.`
5. `WQ-1, WQ-2 and WQ-3 gate prompt landing and Debug pin activation.`

The owner activation recipe has ordered anchors `confirmed land`,
`canonical restart and /api/version`, `pin write and readback`,
`bundle stamp and idle-gated refresh`, `WQ-4 complete receipt and release`.
`C1011_activation_order` asserts each anchor exists and increasing indices.
It also pins `Preserve unrelated Human card exceptions.` and
`Queued tasks retain their recorded selection.` These tests guard the published
instructions; independent Review and the activation owner enforce them against
real evidence. They do not turn prose or a fixture pass into rollout approval.

### Guards the regression

- R-1: prompt cannot buy space by deleting safety/platform contracts, raising
  limits or naming providers in stage bundles | CP-1/CP-4 run all current
  InstructionBundleTests, TaskPlatformGuidanceTests, RunnerDefaultGuidanceTests
  and StandingPipelinePolicyDocumentationTests. The existing cap remains <=14310
  for both newline forms; stage-review remains <=2480 after trim; all previously
  pinned instructions remain asserted. Code re-audits AGENTS and stage bundles.
- R-2: Required list, explicit narrowing, card override and platform semantics
  remain intact | CP-2 retains all 17 RoutingPinCandidateCreateTests and all 15
  TaskPlatformPlacementTests. Exact decisive methods for card/explicit/chain
  boundaries are named in PCs 28-31. Existing single-candidate, Preferred,
  `RefuseIfExhausted`, Any/unpin, unknown/mismatched platform and retained-session
  cases are regression context, not new routing behavior.
- R-3: trust is not Ready and neither a backend request nor Ready is receipt |
  CP-1 retains all four RunnerGrokAdapterTrustPromptTests; CP-3 asserts actual
  backend and full prompt. The old 1.0.13 trust fixture remains historical;
  WQ-3 must observe the installed CLI independently.
- R-4: no delivery claim before recipient evidence, no loss or duplicate across
  queue recovery | CP-5/CP-6 whole-body assertions described in V-4/V-5. Preserve
  LF/bracketed-paste/separate-Enter production behavior and all existing budgets.

### Guard inventory

This is the complete inventory of safety-critical guards newly added or relied
on for CARD-1011 acceptance, including manual evidence-policy guards. Broad
unchanged regression coverage is not a claim to re-mutate the entire product.
Each independently bypassable scoped guard has its own control; shared methods
are allowed, shared PC identifiers are not.

| Guard | Plan reference and invariant | Control |
|---|---|---|
| G-1 | S1: OS does not authorize routing-pin bypass | PC-1 |
| G-2 | S1: ordinary dispatch omits explicit Kind | PC-2 |
| G-3 | S1: ordinary dispatch omits explicit Level | PC-3 |
| G-4 | S1: composed instructions reach the policy owner | PC-4 |
| G-5 | D-4: raw normalized orchestrator file <=14310 | PC-5 |
| G-6 | D-4: kind names remain kinds, never model families | PC-6 |
| G-7 | D-4: Level remains the four tier names | PC-7 |
| G-8 | D-4: stage bundles remain provider-neutral | PC-8 |
| G-9 | D-1: both roles use the approved role-wide scope including Linux | PC-9 |
| G-10 | D-1/D-3: published candidate pair order | PC-10 |
| G-11 | D-5: startup failure requires explicit authorized recovery, not automatic retry | PC-11 |
| G-12 | D-2/WQ-1/2: both actual hosts required | PC-12 |
| G-13 | D-2/WQ-3: visible real trust transition required | PC-13 |
| G-14 | WQ: complete UserPrompt is the delivery verdict | PC-14 |
| G-15 | WQ: report plus release ownership required | PC-15 |
| G-16 | S0: WQ-1/2/3 before prompt land or pin activation | PC-16 |
| G-17 | S3: canonical activation/source check precedes pin change | PC-17 |
| G-18 | S3: unrelated Human card exceptions are preserved | PC-18 |
| G-19 | S3: bundle stamp and idle refresh precede rollout acceptance | PC-19 |
| G-20 | S3: WQ-4 receipt/release completes acceptance | PC-20 |
| G-21 | S2: first healthy listed candidate wins | PC-21 |
| G-22 | S2: held Grok is skipped for listed Opus | PC-22 |
| G-23 | S2/D-3: Required exhaustion cannot escape to role policy/another provider | PC-23 |
| G-24 | S2: durable RoutingPinId identifies the applied list | PC-24 |
| G-25 | S2: selected High tier is persisted | PC-25 |
| G-26 | S2: persisted required platform survives selection/fallback | PC-26 |
| G-27 | S2: actual selected descriptor matches required platform | PC-27 |
| G-28 | D-3: card pin precedes role-wide pin | PC-28 |
| G-29 | D-5: explicit listed candidate narrows to its own pair | PC-29 |
| G-30 | D-3/D-5: explicit unlisted candidate is refused | PC-30 |
| G-31 | D-3: Required list precedes a complexity chain | PC-31 |
| G-32 | WQ-1/V-3: inbox row observes actual InboxConhost | PC-32 |
| G-33 | WQ-2/V-3: modern row observes actual ModernConPty, not fallback | PC-33 |
| G-34 | S2/V-3: startup must actually become Ready | PC-34 |
| G-35 | S2/V-3: exact complete recipient prompt after Ready | PC-35 |
| G-36 | WQ-3/R-3: affirmative trust key is y | PC-36 |
| G-37 | WQ-3/R-3: trust is answered at most once | PC-37 |
| G-38 | WQ-3/R-3: uncleared trust never becomes Ready | PC-38 |
| G-39 | V-4: enqueue must commit before any recipient write | PC-39 |
| G-40 | V-4: busy WhenIdle recipient is held | PC-40 |
| G-41 | V-4: attempt and baseline commit before transport | PC-41 |
| G-42 | V-4: interrupted empty composer recovers without losing the row | PC-42 |
| G-43 | V-4: body already visible recovers with Enter only | PC-43 |
| G-44 | V-4: late complete receipt wins over retransmission | PC-44 |
| G-45 | V-4: already-idle WhenIdle recipient is delivered without a future TurnEnd | PC-45 |
| G-46 | R-3: healthy startup sends no trust key | PC-46 |
| G-47 | D-1/D-3: policy strength remains Required | PC-47 |
| G-48 | D-1/D-3: policy provenance remains Human | PC-48 |
| G-49 | D-3: Grok tier resolves to grok-4.7 | PC-49 |
| G-50 | D-3: ClaudeCode High resolves to opus | PC-50 |
| G-51 | S1: orchestrator skill points to the owner | PC-51 |
| G-52 | S1: delegate skill points to the owner | PC-52 |
| G-53 | D-5: explicit startup recovery retains Windows | PC-53 |
| G-54 | D-5: explicit startup recovery forbids IgnoreRoutingPin | PC-54 |
| G-55 | S3: pin edits do not retarget already queued tasks | PC-55 |
| G-56 | S3: successful pin write requires readback | PC-56 |
| G-57 | D-3: explicit Level alone narrows to the surviving pair | PC-57 |

### Positive controls

All controls below are executable compiling defects, not test assertion removal.
New test methods/labels are ordinary Code deliverables. For each PC, use
`/*/*/ClassName/ExactMethod*` with only that method; the trailing wildcard admits
its argument suffixes, never the whole class. Baseline -> mutate -> red -> exact
restoration -> green, with a fresh TRX and build for each phase. Builds/fixture
errors, zero tests, timeouts before the intended assertion and skips are not red.
Markdown mutations compile as embedded/read documentation. Code runs V/R;
Review judges this design before land; SourceLanding Mutation executes all PCs
after land with inherited local children and evidence outside the snapshot.

For PC-1..PC-20, labels below are required Shouldly messages on the indicated
assertions. Keep each mutation separate even when it uses the same method/file.

| PC | Break guard by this defect | Exact method and expected red assertion |
|---|---|---|
| PC-1 | In orchestrator.md replace `OS needs do not` with `OS needs do` | InstructionBundleTests.C1011_composed_windows_routing_contract; `os-pin` missing original prohibition |
| PC-2 | Replace `omit -Kind/-Level` with `omit -Level` | InstructionBundleTests.C1011_composed_windows_routing_contract; `omit-kind` |
| PC-3 | Replace `omit -Kind/-Level` with `omit -Kind` | InstructionBundleTests.C1011_composed_windows_routing_contract; `omit-level` |
| PC-4 | Change the bundle's `#windows-review-and-debug-routing` anchor to `#missing-routing` | InstructionBundleTests.C1011_composed_windows_routing_contract; `owner-pointer` |
| PC-5 | Append 195 ASCII x characters to the frozen 14116-character raw bundle | InstructionBundleTests.orchestrator_bundle_points_to_operational_autonomy_without_growing; both argument rows fail Length <=14310 at 14311 |
| PC-6 | In replacement paragraph change Kind's `ClaudeCode` to `Fable` | InstructionBundleTests.C1011_model_kind_and_tier_contract; `kind-values` |
| PC-7 | Change Level's `Frontier` to `Astra` | InstructionBundleTests.C1011_model_kind_and_tier_contract; `level-values` |
| PC-8 | In stage-plan.md replace the opening sentence's lowercase `plan` with `Grok` (no size growth) | InstructionBundleTests.each_stage_bundle_is_ascii_and_under_the_size_cap; stage-plan row fails ShouldNotContain("Grok") |
| PC-9 | In the new owner section replace `including Linux` with `excluding Linux` | InstructionBundleTests.C1011_owner_role_wide_policy_and_fallback; `all-platforms` |
| PC-10 | Reverse the published `Grok/High then ClaudeCode/High` order | InstructionBundleTests.C1011_owner_role_wide_policy_and_fallback; `required-pair` |
| PC-11 | Replace `Startup failure requires explicit authorized recovery; do not retry Grok automatically.` with `Retry Grok automatically.` | InstructionBundleTests.C1011_owner_role_wide_policy_and_fallback; `startup-recovery` |
| PC-12 | Delete owner clause 1 (both actual hosts) | InstructionBundleTests.C1011_qualification_gates; `both-hosts` |
| PC-13 | Delete owner clause 2 (visible trust) | InstructionBundleTests.C1011_qualification_gates; `fresh-trust` |
| PC-14 | Replace `matching complete UserPrompt transcript` with `Sent flag` | InstructionBundleTests.C1011_qualification_gates; `whole-receipt` |
| PC-15 | Delete `and confirmed release ownership` from clause 4 | InstructionBundleTests.C1011_qualification_gates; `release-owner` |
| PC-16 | Delete clause 5 (prelanding evidence gate) | InstructionBundleTests.C1011_qualification_gates; `preland-gate` |
| PC-17 | Move the complete `pin write and readback` step before `canonical restart and /api/version` | InstructionBundleTests.C1011_activation_order; `canonical-before-pin` index assertion |
| PC-18 | Delete `Preserve unrelated Human card exceptions.` | InstructionBundleTests.C1011_activation_order; `preserve-exceptions` |
| PC-19 | Delete the `bundle stamp and idle-gated refresh` step | InstructionBundleTests.C1011_activation_order; `refresh-before-canary` existence assertion |
| PC-20 | Delete the `WQ-4 complete receipt and release` step | InstructionBundleTests.C1011_activation_order; `debug-confirmation` existence assertion |
| PC-21 | ComplexityRoutingService.WalkCandidatesAsync iterates `candidates.Reverse()` | WindowsGrokRoutingPolicyTests.C1011_healthy_head_is_grok; persisted AgentKind equals Grok (all four rows) |
| PC-22 | In FirstSkipReasonAsync ignore a Grok hold only: change hold condition to `hold is not null && candidate.Kind != AgentKind.Grok` | WindowsGrokRoutingPolicyTests.C1011_held_head_falls_back_to_opus; selected kind equals ClaudeCode |
| PC-23 | RoutingCandidates.Compose Required arm appends `resolveAgainstRolePolicy(AgentKind.ClaudeCode, AgentModelLevel.Medium)` to pinSlots | WindowsGrokRoutingPolicyTests.C1011_exhausted_pair_blocks; Status equals Blocked (unlisted Sonnet is unheld) |
| PC-24 | AgentTaskService.CreateAsync initializer writes `RoutingPinId = null` | WindowsGrokRoutingPolicyTests.C1011_healthy_head_is_grok; persisted RoutingPinId equals seeded Id |
| PC-25 | That initializer writes `ModelLevel = AgentModelLevel.Medium` | WindowsGrokRoutingPolicyTests.C1011_healthy_head_is_grok; persisted ModelLevel equals High |
| PC-26 | That initializer writes `RequiredPlatform = RequiredPlatform.Any` | WindowsGrokRoutingPolicyTests.C1011_held_head_falls_back_to_opus; persisted RequiredPlatform equals argument |
| PC-27 | AgentTaskService.PlatformMatches returns `true` for any descriptor | WindowsGrokRoutingPolicyTests.C1011_held_head_falls_back_to_opus; ObservedPlatform equals required OS (opposite default makes this decisive) |
| PC-28 | RoutingPinService.ResolveAsync selects `stagePin ?? cardPin` | RoutingPinCandidateCreateTests.Card_list_beats_the_stage_list; kind equals ClaudeCode instead of role Grok |
| PC-29 | In ResolveAsync single-compatible overlay use `pinCandidates[0]` instead of `compatible[0]` | RoutingPinCandidateCreateTests.Explicit_kind_only_narrows_to_a_single_non_head_survivor_and_is_not_walked; ModelLevel.ShouldBe(High) fails (explicit kind still stays ClaudeCode) |
| PC-30 | Disable only the Required incompatible-kind/list throw branch in ResolveAsync with `false &&` | RoutingPinCandidateCreateTests.Explicit_kind_against_a_required_list_with_no_match_is_409_listing_candidates; Should.ThrowAsync RoutingPinConflictException |
| PC-31 | RoutingCandidates.Compose Required arm uses `chainList` when nonempty instead of pinSlots | RoutingPinCandidateCreateTests.Required_list_plus_complexity_bypasses_the_chain; chosen kind equals Grok |
| PC-32 | HostSession constructor uses `new PtyAgentRunner("modern")`, ignoring options | RunnerGrokAdapterReadyTestsPty.C1011_windows_backends_reach_ready_and_complete_prompt; inbox argument fails exact InboxConhost/requested-inbox log assertion |
| PC-33 | HostSession constructor uses `new PtyAgentRunner("inbox")`, ignoring options | RunnerGrokAdapterReadyTestsPty.C1011_windows_backends_reach_ready_and_complete_prompt; modern argument fails exact ModernConPty/requested-modern log assertion |
| PC-34 | RunnerGrokAdapter.WaitForReadyAsync awaits existing GrokReadyWait call then returns false | RunnerGrokAdapterReadyTestsPty.C1011_windows_backends_reach_ready_and_complete_prompt; returned Ready ShouldBeTrue |
| PC-35 | RunnerGrokAdapter.SendPromptAsync unverified branch sends `prompt[..^1]` | RunnerGrokAdapterReadyTestsPty.C1011_windows_backends_reach_ready_and_complete_prompt; prompts[0].Text.ShouldBe(body) fails on missing final character |
| PC-36 | GrokReadyWait writes literal `"n"` in its trust arm instead of AffirmativeKey | RunnerGrokAdapterTrustPromptTests.A_launch_into_an_untrusted_directory_answers_y_before_reporting_ready; ready.ShouldBeTrue fails |
| PC-37 | Remove `!trustWritten &&` from trust write condition, retaining original trustAt after first write so the deadline remains bounded | RunnerGrokAdapterTrustPromptTests.A_trust_dialog_that_does_not_clear_is_not_ready; Inputs.ShouldBe([AffirmativeKey]) fails with repeated y |
| PC-38 | In GrokReadyWait return true as soon as observation.Reason is Trust | RunnerGrokAdapterTrustPromptTests.A_trust_dialog_that_does_not_clear_is_not_ready; ready.ShouldBeFalse fails |
| PC-39 | EnqueueCoreAsync calls `await _runtime.SendInputAsync(sessionId, trimmed, ct)` immediately before the queue-insert SaveChanges | SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery; insert-fails row fails world.Forward.Writes.ShouldBeEmpty |
| PC-40 | SessionMessageQueueService.ReadWorkingAsync returns `Task.FromResult(false)` | SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery; pending-before-flush row fails pending.Status.ShouldBe(Pending) before TurnEnd |
| PC-41 | In DeliverNextLockedAsync omit the non-completion `else await db.SaveChangesAsync(ct)` immediately before delivery | SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery; attempt-before-write row fails claimed.Status.ShouldBe(Sent) while write is held |
| PC-42 | In RecoverDeliveryRunLockedAsync omit the loop reverting remaining Sent rows to Pending | SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery; attempt-before-write row fails recovered DeliveryAttempts.ShouldBe(2) |
| PC-43 | In EnterOnlyConfirmLockedAsync retype `body` with SendInputAsync immediately before its existing CR write | SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery; body-before-enter row fails exact post-cut writes.ShouldBe(new[] { "\r" }) because writes are body then CR |
| PC-44 | LateConfirmAttemptedMessagesAsync returns LateConfirmCounts.Empty unconditionally | SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery; recipient-before-ingestion and receipt-before-verdict rows fail recovered.DeliveryAttempts.ShouldBe(1) |
| PC-45 | Disable EnqueueCoreAsync's immediate delivery condition with `false && deliverIfIdle` | SessionQueueReceiptPlumbingTests.C475_AlreadyIdleWhenIdleHasRecipientReceipt; dto.Messages.ShouldBeEmpty fails immediately with a Pending row |
| PC-46 | In GrokReadyWait successful Ready arm write `"y"` immediately before returning true | RunnerGrokAdapterTrustPromptTests.A_healthy_launch_types_nothing; Inputs.ShouldBeEmpty fails |
| PC-47 | Change new owner section's `Human Required` to `Human Preferred` | InstructionBundleTests.C1011_owner_role_wide_policy_and_fallback; `required-strength` |
| PC-48 | Change new owner section's `Human Required` to `Auto Required` | InstructionBundleTests.C1011_owner_role_wide_policy_and_fallback; `human-provenance` |
| PC-49 | ModelLevelAliases.ForGrok returns `"grok-4.6"` | WindowsGrokRoutingPolicyTests.C1011_healthy_head_is_grok; chosen outcome Alias equals grok-4.7 |
| PC-50 | ModelLevelAliases.ForClaude High arm returns `"sonnet"` | WindowsGrokRoutingPolicyTests.C1011_held_head_falls_back_to_opus; chosen outcome Alias equals opus |
| PC-51 | Change only orchestrator skill's new policy anchor to `#missing-routing` | InstructionBundleTests.C1011_composed_windows_routing_contract; `orchestrator-skill-pointer` |
| PC-52 | Change only delegate skill's new policy anchor to `#missing-routing` | InstructionBundleTests.C1011_composed_windows_routing_contract; `delegate-skill-pointer` |
| PC-53 | Change new owner recovery recipe's `-Platform Windows` to `-Platform Any` | InstructionBundleTests.C1011_owner_role_wide_policy_and_fallback; `recovery-platform` |
| PC-54 | Delete `no -IgnoreRoutingPin` from that recovery recipe | InstructionBundleTests.C1011_owner_role_wide_policy_and_fallback; `recovery-no-bypass` |
| PC-55 | Delete `Queued tasks retain their recorded selection.` | InstructionBundleTests.C1011_activation_order; `queued-selection` |
| PC-56 | Change activation step `pin write and readback` to `pin write` | InstructionBundleTests.C1011_activation_order; `pin-readback` existence assertion |
| PC-57 | ResolveAsync single-compatible `resolvedKind` uses `pinCandidates[0].AgentKind` instead of `compatible[0].AgentKind` | RoutingPinCandidateCreateTests.Explicit_level_only_narrows_to_a_single_non_head_survivor_and_is_not_walked; created.AgentKind.ShouldBe(ClaudeCode) fails |

PC-23 uses a compiling list expression or `pinSlots.Concat(new[] { ... }).ToList()`;
PC-34 stores the awaited bool in a local before `return false`, not unreachable
code. PC-37 changes both the write guard and timestamp assignment deliberately
to break the single-write invariant without creating an unbounded fixture.
PC-43 targets the shared Enter-only writer, not just its interrupted-Sent caller:
the Pending retry arm also calls it and would mask removal of only the first
caller's HeadFragment check. Its retyped body is still contained in the resulting
prompt, so the exact write-list assertion must catch the extra write before the
final whole-receipt list. For PC-44 use `if (pending.Count >= 0) return
LateConfirmCounts.Empty;` ahead of the existing body to keep the async body
compilable without introducing unreachable-code warnings.
PC-46 calls the existing nullable write delegate only when non-null, with the
current cancellation token. Assertions for G-47/G-48 test the strength and
provenance words separately, so each mutation reaches its own named label.
G-53/G-54 assertions read only the recovery-recipe subsection, not another
matching flag elsewhere in the owner. G-55/G-56 have distinct assertions from
the exception-preservation and order checks. G-49/G-50 check the actual routing
DTO Alias, not a value recomputed by the mutated ModelLevelAliases helper.
For G-2/G-3, extract the tokens in the composed `Normally omit ... to preserve`
clause and assert Kind/Level separately with the two labels; one assertion for
the entire sentence would make PC-3 fail early at the wrong label. Activation
anchor existence assertions use their PC labels before checking their order.
Do not alter expected values, seed lists or test input to manufacture red.

### Out of scope

- OS-specific pin storage, aliases, RolePolicy, automatic startup retry,
  authentication changes, quotas and schema/runner upgrades remain excluded.
- MacOS routing/native qualification and alternate terminal geometry are excluded:
  the accepted scope uses role-wide pins, but this card qualifies Windows plus
  existing Linux admission behavior. No claim of MacOS CLI support is added.
- Full classifier/modal/malformed-frame mutation belongs to CARD-1006; this card
  does not change those files. No tolerance, retry or timeout relaxation is allowed.
- The full Unit category and full assembly are replaced by the explicit affected
  Unit profile below: the production delta is embedded guidance, with routing
  and native contracts selected separately. Unrelated Unit tests do not improve
  the prompt/placement/delivery evidence. This is an explicit narrow profile,
  not an assertion that 90 is the repository's full Unit inventory.
- Existing dispatch/rules/settlement/release crash matrices are unchanged and not
  requalified wholesale. Their successful real chain remains mandatory WQ evidence;
  the six queue handoff recovery cases are included, not waived by this exclusion.

### Windows qualification and Code admission

The desktop checkout is unreachable from this runner mirror. No Windows/backend,
fresh-trust or activation result is claimed by this freeze. There is no unresolved
product choice: D-1 is approved. The remaining work is implementation and evidence.

S0 starts by retrieving the exact d6e4138e session through the documented transcript
and pty-host log routes. Attribute only the row its actual backend satisfies.
Record `grok --version`, actual model/build, source SHA, runner/store/session and
task IDs, terminal geometry, host-log path/hash, Ready evidence, complete received
body/sequence, final report and release owner. Archive only this task's evidence.
Modern also records the loaded conpty.dll/OpenConsole paths and package/hash
provenance; configured modern without the actual host line is incomplete.

For missing WQ-1/2 use a commissioned Windows Review/Worktree canary through
`scripts/delegate.ps1 -Role Review -Platform Windows -Worktree -Goal $c1011Goal`.
Load `$c1011Goal` with `Get-Content -Raw -LiteralPath
'.antiphon/c1011-wq-review-goal.txt'`; the script has no GoalFile parameter.
Omit Kind/Level/IgnoreRoutingPin. The goal file requests a bounded, read-only
Review of this plan, carries a unique nonce and requires a report naming it.
Record the complete task pointer/body and subsequent file-read tool evidence.
Do not reuse that no-change canary as Final/Full implementation Review.

Backend experiment configuration is fixed as `SessionRunner:PtyBackend=inbox`
then `modern`, 120x30, PtyHost (not Herdr). Default to an already provisioned
isolated Windows stack with its own database, loopback ports, runner manifest/log
roots and supported provider session; its server uses the matching backend
ceiling. Seed/read back the approved Human Required Review pair in that isolated
database and record its own pin identity; never reuse the production pin GUID as
proof of an isolated stack's configuration. If unavailable, the caller must commission the canonical configuration
window described in S0: save only the relevant setting, stop new affected
dispatches, inspect lock ownership and occupancy, change that setting through
the runner's configured override, use canonical restart scripts, then verify
the actual newly launched host. Restore the saved setting, restart canonically
and prove the restored actual backend before ending the window. Never change
tracked defaults, restart from a worktree, switch beneath active test runs, or
kill unrelated sessions. This freeze grants no canonical settings mutation.

WQ-3 needs input observation absent from the existing adapter fake. Implement
`RunnerGrokAdapterReadyTestsPty.C1011_real_fresh_worktree_trust` as a Windows-only
Explicit test, enabled only by `ANTIPHON_HEADED_TESTS=1` and an explicit method
filter, with method-level `NotInParallel("Headed")` and the class's existing
process limiter. Use a new task-owned git worktree/cwd, the installed real grok executable
and its already authorized provider session; do not copy/read auth stores or run
login. Use DirectSessionRunnerClient with `ptyBackend: "modern"`, the real
RunnerGrokAdapter at normal settings, 120x30 and a test-local forwarding
ISessionRunnerClient that observes snapshot classification and exact startup
inputs before forwarding. No global environment/backend mutation. Normal launch
args are `--always-approve --no-alt-screen --model grok-4.7 --session-id <id>`;
the spec Cwd is the new worktree and spec.SessionId is that same GUID. This
probe is separate from WQ-1/2's live
pin/routing proof; combining it with a Review row is permitted only when that
row has equivalent actual key/snapshot evidence.

The observer records a Trust frame before the first write, exactly one literal
`y` before Ready, a non-trust settled Ready frame and actual modern host log.
After Ready send one unique single-line read-only nonce prompt and require the
complete normalized UserPrompt, a useful response/turn end, and confirmed exit
of this owned session in finally/disposal. Missing trust is a failed
qualification row, even if the worktree is new; it is not a skip or permission
to alter trust/auth files. Keep bounded observations limited to this probe and
suppress sign-in screen contents. Run at most ten minutes. Exact paid filter:
`/*/*/RunnerGrokAdapterReadyTestsPty/C1011_real_fresh_worktree_trust*`, MinExecuted 1,
through run-checkpoint.ps1 under an explicit S0 commission. It is excluded from
ordinary CP-3's filter. New probe scaffolding is committed before its paid run;
it may precede the gated prompt landing. Each live Review canary also has a
ten-minute budget. Refusal or deadline means incomplete, not another silent try.

WQ-4 follows the plan's activation order, on the live Debug role pin, with no
kind/level/bypass override. Preserve same-platform authorized Opus recovery,
card exception handling and blocked exhaustion exactly as the original plan.

Code start condition: dispatch from this pushed freeze after checking current
source collisions, especially CARD-1006's native ready-test file. Recount any
base drift; retain the exact prompt edit/cap and argument rosters. Implement
tests and evidence scaffolding without production detector/queue changes. The
caller commissions missing Windows S0 evidence, independently from Linux work.
Policy prompt landing and pin activation wait for complete WQ-1/2/3 and ordinary
Final/Full Review. No automatic fleet pin write is a Code test or tracker action.

### Checkpoints

This is the sole active checkpoint table. All rows follow committed S1-S2 tests
and guidance; CP-1/2 are Any, CP-3..6 Windows at the identical implementation SHA.
One exact filter per row, isolated builds in CP-1/3 and reuse only within the same
After group. The table plus separate S0/WQ acceptance is the closed scope; no
repository build/test was run by TestDesign.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1011-any/` | any-guidance-unit | `/*/*/(InstructionBundleTests*)\|(TaskPlatformGuidanceTests*)\|(RunnerDefaultGuidanceTests*)\|(StandingPipelinePolicyDocumentationTests*)\|(RunnerGrokAdapterTrustPromptTests*)/*` | V-1, R-1, R-3 | 90 expanded results; all named classes; 0 failed/skipped | 90 | 8 |
| CP-2 | S1-S2 | CP-1 | any-routing | `/*/*/(WindowsGrokRoutingPolicyTests*)\|(RoutingPinCandidateCreateTests*)\|(TaskPlatformPlacementTests*)/*` | V-2, R-2 | 12 new + 17 create + 15 placement; 0 failed/skipped | 44 | 6 |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-c1011-win/` | windows-native | `/*/*/RunnerGrokAdapterReadyTestsPty/(Fake_dashboard_marker_reaches_ready_and_complete_first_prompt*)\|(C1011_windows_backends_reach_ready_and_complete_prompt*)` | V-3, R-3 | 2 existing marker + 2 backend argument results; 0 failed/skipped | 4 | 7 |
| CP-4 | S1-S2 | CP-3 | windows-bundle-cap | `/*/*/InstructionBundleTests/orchestrator_bundle_points_to_operational_autonomy_without_growing*` | V-1, R-1 | LF and CRLF; <=14310; 0 failed/skipped | 2 | 1 |
| CP-5 | S1-S2 | CP-3 | windows-queue-recovery | `/*/*/SessionQueueReceiptPlumbingTests/(C475_QueueCommitAndTransportRecovery*)\|(C475_AlreadyIdleWhenIdleHasRecipientReceipt*)` | V-4, R-4 | 6 cut arguments + 1 already-idle; exact complete receipts; 0 failed/skipped | 7 | 8 |
| CP-6 | S1-S2 | CP-3 | windows-grok-tailer | `/*/*/SessionMessageQueueGrokPtyIntegrationTests/Multiline_delivery_is_transcript_confirmed_through_the_real_grok_tailer*` | V-5, R-4 | 1 real queue/tailer FakeGrok result; 0 failed/skipped | 1 | 4 |

Read-only census recipe: `rg -n '^    \[(Test|Arguments)'` on each listed file,
then assign Arguments to the following method (no loop/Shouldly assertion is
an execution). InstructionBundleTests has **42 methods / 62 results**: its
2/8/6/3/6 argument groups add 20 over method count. Five new single-result
methods make 67. TaskPlatformGuidanceTests contributes 5, the second class
RunnerDefaultGuidanceTests in that file contributes 4, standing-policy has
7 methods/10 results, trust adapter has 4: **67+5+4+10+4=90**.
Routing-create is 17 and placement is 15 single-result methods; new matrix is
3 methods x 4 arguments: **44**. Ordinary native filter is 4, cap is 2,
queue filter is 6+1, tailer is 1: **148 ordinary executions**, including the
intentional two cap executions repeated on Windows. The Explicit paid method
is not counted. These counts were reconciled against bodies and grep; they are
not measured discovery or passes. Recount after any source drift, and inspect
fresh TRX for every named identity/argument, not only the total.

No prebuilt importer was assumed or executed. Code's explicit bootstrap exception
is one gated tool build to `bin-c1011-tool/` (estimated four minutes), then run
that built Antiphon.Checkpoints DLL's `import --plan` on this artifact without
another build. Require six imported rows, exact filters and minima. Use its
`run --plan ... --rows CP-1,CP-2` and `run --plan ... --rows CP-3,CP-4,CP-5,CP-6`
for the two commissioned OS lanes and `wait` until a terminal result (not 75).
Tool bootstrap/build drivers use scripts/build-slot.ps1; checkpoint driver
builds take their own slot. Exit 4 means not run, never an unleased retry.
Serial native runs must not overlap Antiphon.Agents.Pty.Tests/FakeClaude in
another process. Freeze source during runs; retain CP receipts/TRX/source SHA
and remove only task-owned alternate outputs after all owned children exit.

### Cost

All times below are estimates, not measurements. The ordinary V/R floor (Code)
is **8+6+7+1+8+4 = 34 minutes** for CP-1..6, including their two isolated builds.
Tool bootstrap/import adds **4 minutes**, so ordinary execution/setup is **38**.
Authoring, independent Review's same 34-minute rerun, slot waits and evidence
investigation are additional and must be reported separately.

Mutation PC floor, using the exact method filters in the PC table, is:

| Controls | Baseline build/test each | Red build/test each | Restore + green build/test each | Edit/evidence each | Subtotal minutes |
|---|---:|---:|---:|---:|---:|
| PC-1..20 (guidance/owner/cap) | 2 | 2 | 2 | 1 | 140 |
| PC-21..31 (routing/placement) | 3 | 3 | 3 | 1 | 110 |
| PC-32..35 (native backend/receipt) | 3 | 3 | 3 | 1 | 40 |
| PC-36..38 (trust adapter) | 2 | 2 | 2 | 1 | 21 |
| PC-39..44 (six-cut queue method) | 4 | 4 | 4 | 1 | 78 |
| PC-45 (already-idle queue method) | 4 | 4 | 4 | 1 | 13 |
| PC-46 (healthy trust adapter) | 2 | 2 | 2 | 1 | 7 |
| PC-47/48/51/52/53/54/55/56 (independent owner/skill guards) | 2 | 2 | 2 | 1 | 56 |
| PC-49/50/57 (aliases and explicit level) | 3 | 3 | 3 | 1 | 30 |

Thus **PC floor = 495 minutes**, including all 57 baseline/red/restored-green
cycles, phase builds and edit/evidence time. Do not use class-wide mutation
filters to reduce this cost. Batch only genuinely independent different-file,
different-method controls, and report measured savings rather than assuming them.
Numeric automated total is **4 + 34 + 495 = 533 minutes**. S0/WQ live allowance
is four bounded runs x 10 = **40 minutes** (two Review hosts, separate observed
trust, post-activation Debug), plus **15 minutes** setup/restoration = **55**;
combined execution/qualification estimate **588 minutes**, excluding Review,
authoring and slot waits. Reusing attributable canary evidence saves 10 minutes
per whole live row avoided; no saving is booked before its missing evidence exists.

The affected Unit row is 8 versus the proposal's 15 minutes (estimated 7 saved).
Reusing outputs on four rows avoids four estimated three-minute rebuilds (12
saved versus rebuilding this same roster per row). Ordinary scope grows from
the proposal's 29 to 34 minutes because complete queue/recovery/Grok-tail evidence
is now explicit. No correctness or Windows gate is traded for those savings.

Read-only final artifact audit found one active Checkpoints heading, six rows
with nine cells each, minima 90/44/4/2/7/1 (148 total), estimates totaling 34,
and the exact 868-to-674-character prompt substitution yielding 14116. This
text audit is not a compiled importer or test execution; Code still imports it.

Before handoff audit: bodies above read; **guards=57, mapped=57, missing=0,
duplicate PC maps=0**; all controls specify a compiling defect, exact method and
decisive assertion. Newly named tests remain Code work, not existing passes.
Next is Code under the admission/evidence gates above; PCs remain pending for
independent post-land Mutation.
