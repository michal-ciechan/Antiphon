# CARD-1011: Windows Review and Debug on the Grok route

Date: 2026-10-03. Plan task: `4c1708dc-6827-4519-a065-f54908b1ff61`.
Source inspected: `5f214b0c1daef4d6d7fbbbfbebd14deb83636639`.
Card: `3893294b-0b6a-4192-bc9a-d1f7712c65b2`, Antiphon board
`8988ca03-7414-47ad-b0b6-51556c701703`.

This is a plan-only artifact. No source, runtime pin, runner setting, or agent was
changed. TestDesign is a separate stage: the brief did not fold it into Plan.
The checkpoint proposal below is input to that stage, not a claim that verification
design or Windows acceptance has completed.

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
