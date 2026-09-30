# CARD-0796: qualify desktop Codex, then remove the task refusal

Plan date: 2026-09-30. Source inspected: `e833925894b5e3d6ffea4d51ee9671b73473a218`.
Plan task: `dcb476c9-fab5-493b-8d31-7443e034db8e`.

The CARD-0777 fix is already in this checkout. This plan does not run a diagnostic,
authorize a launch, or remove the refusal. The next action is an operator decision
on S2. S3 is gated on the accepted S2 evidence, not merely permission to attempt S2.

## Ground truth

| Card premise | Observed source / record | Consequence |
|---|---|---|
| Step 1 must land | CARD-0777 suppression, classification, active Escape fallback and timeout capture are present; history includes `9dc296014`, `716dbe4bd`, `fe6522cab`, `16da56da5`. | Do not reimplement the fix. Landing is not proof of desktop activation. |
| Desktop launch failed before input | [CARD-0777 investigation](../../investigations/2026-09-28-card-0777-codex-desktop-update-modal.md) reproduced the update picker under ModernConPty. It explicitly records no qualifying Antiphon desktop launch. | Require a seeded pending update in the home actually used by the diagnostic. The old observation that 0.158.0 was current is not a current version check. |
| One refusal must be removed | `DefaultRunnerRoutingPolicy` has `ReasonCodexDesktopUnqualified`, `CodexDesktopRefusal`, `IsDesktopCodex`, desktop arms in **both** host predicates, and the desktop blocked-reason arm. `AgentTaskService` checks create and explicit reroute. Its wall reroute and the dispatcher's queued rewalk / blocked recovery use the compatibility predicate. | Removing only create, or deleting the whole dispatcher fence, is wrong. Preserve remote admission checks. |
| A diagnostic override exists | No desktop-unqualified bypass parameter exists in `delegate.ps1`, `CreateAgentTaskRequest`, or the refusal. Cardless named-agent start uses `AgentControlService` -> `AgentSessionLaunchComposer` -> `AgentSessionService` -> `RunnerCodexAdapter`, without task placement. | D-2 proposes one explicitly sanctioned named-agent diagnostic exception through existing APIs. Do not invent `-IgnoreCodexDesktopUnqualified`, turn off readiness, or remove the production guard to obtain proof. |
| Three dispatch tests are stale | The three methods named by CARD-0796 are in `CodexDelegateDispatchTests`; CARD-0783 also names `PinnedProfileLaunchSpecTests.T8_model_rule_one_flag_from_ModelId_or_the_profile_kind_tier_alias`. CARD-0843 names `PinnedAgentKindTests.T1` / `T2`. Both cards were read in full. | Six known stale test methods, not three. Their accepted desktop behavior becomes correct again after S3. Keep their original intent. |
| Only stale acceptance tests need attention | Refusal assertions also exist in `TaskPlatformPlacementTests`, `TaskPlatformDispatchTests`, `DefaultRunnerCreateTests`, and `DefaultRunnerRerouteTests`, including transactional parent-note cases. | Convert acceptance coverage and preserve delivery / transaction coverage with still-valid refusal fixtures. |
| A runner default implies desktop execution | Read-only `/api/runner-defaults` at 09:03 UTC returned revision 2, global `server2`, no kind overrides. `/api/session-runners` reported Windows desktop and Linux server2 eligible, server2-temp draining. | These are observations, not pinned fleet policy. S2 explicitly uses the desktop named-agent path. Ordinary Code checkpoints need no fixed host; re-read the catalogue at dispatch. |

Owners: [agent kinds](../../agent-kinds.md), [TUI configuration](../../ai-agent-tui-configuration.md),
[credentials](../../agent-credentials.md), [HTTP operations](../../ops-http.md),
[logs](../../logs.md), [session invariants](../../session-runtime-invariants.md),
[ModernConPty](../../adr/0002-modern-conpty-backend.md), [testing](../../testing-and-build.md),
[orchestration](../../orchestration-loop.md), and [restart](../../apphost-runbook.md).

## Decisions

- **D-1 — Evidence before removal.** Retain the desktop task refusal until one sanctioned
  cold desktop session proves the pending-update case, positive readiness within the unchanged
  60,000 ms gate, `SessionStatus.Running`, and a complete matching native `UserPrompt`.
  A passing health check, screen redraw, queue insertion, or synthetic test is insufficient.
- **D-2 — Operator-owned diagnostic exception.** Use one fresh, cardless, non-AlwaysOn
  named Codex agent on the existing desktop stack, with `SessionBackend=PtyHost`, an
  approved standard Codex profile, and an isolated authenticated `CODEX_HOME`. This is an
  operational exception to the task ban, not a new production bypass setting. The operator
  must sanction this exact exception; absent that decision neither Plan nor Code runs it.
  It exercises the shipped named launch composition, shared adapter, real runner, and real
  delivery queue. Offline tests cover the delegate composition and task fences separately.
- **D-3 — Bounded spend and state.** One new session, one short diagnostic prompt, Low tier
  (`gpt-5.6-luna`, `model_reasoning_effort=low` at this source), no real card work, channel,
  remote control, delegation, quota override, CLI update, or automatic second attempt.
  The operator approves use of the installed CLI's normal
  `--dangerously-bypass-approvals-and-sandbox` launch template for this scratch session.
- **D-4 — Pending update and home isolation.** Use
  `%LOCALAPPDATA%\Antiphon\codex-test-home`, exclusively reserved for this run, and save/restore
  its exact `version.json` bytes (or original absence). Never seed the user's normal home,
  copy `auth.json`, dump the environment, or hand-delete a Codex rollout. If the isolated
  home lacks login, the operator signs in there through `codex login` first; no account
  fallback is inferred. The nonce workspace is separate from the home and source checkout.
- **D-5 — Preserve the remaining boundaries.** Desktop admission becomes true in both host
  predicates. Remote worker/explicit admission, required-platform checks, holds, quota,
  pinned-kind consistency, remote control, specialist, Orchestrator and SourceLanding rules
  remain in force. No global or per-kind runner-default changes, deadline extension, or
  eager retry/requeue migration belongs to this card.
- **D-6 — Evidence scope is explicit.** A seeded launch with update checks disabled qualifies
  the normal modal-prevention path. It does not by itself demonstrate the live Escape
  fallback fired. The two measured modal fixtures retain that coverage. A live run with
  update suppression disabled would be a second, separately sanctioned experiment and is
  not required or authorized by this plan.

Rejected alternatives: editing the real Codex home contaminates user state; temporarily
removing the task guard opens ordinary dispatch before qualification; a standalone `codex`
or raw runner launch misses Antiphon's readiness and delivery path; Linux success does not
qualify Windows; changing stale tests to expect refusal would preserve the defect this card
is meant to remove. Supplying a remote runner to pinned standing-agent tests changes their
subject and conflicts with retained-process placement.

## Slices

### S1 — Already landed; verify activation as an S2 precondition

No implementation slice. On the desktop, the operator records canonical checkout HEAD,
the loaded `/api/version` SHA/capabilities, runner identity, installed Codex version and
the selected profile revision. The loaded source must contain the full CARD-0777 fix,
including the measured-picker-only Escape correction. A dirty tree or old loaded SHA is
not qualification provenance. If activation is needed, use the canonical main checkout
and `pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha <full-sha>`
under the restart runbook; no worktree override. Re-read `/api/version`, including
`land-v2`, immediately after restart. Health alone is not an activation verdict.

### S2 — Operator-sanctioned desktop diagnostic and evidence commit

**Operator approval must name** CARD-0796, the exact stack SHA, standard Codex profile ID
and revision, model/tier, exclusive isolated home, one cold local PtyHost session, one
diagnostic turn and its subscription/API spend, pending-update file replacement/restoration,
and permission to stop that exact diagnostic agent on success or failure. Record approver,
UTC time and the approval reference in the evidence. This approval does not permit retries,
ordinary task launches, global setting changes, or refusal removal without a passing receipt.

The following commands are for **PowerShell 7 on the desktop, after that sanction**.
They are an operator procedure, not an automated Code checkpoint. Runtime IDs and the SHA
are deliberately operator inputs; the API fields and CLI flags below are the existing ones.
Use the approved standard profile with `Kind=Codex`, `ModelArgumentName=--model`, a recognized
Codex npm launcher, normal `--no-alt-screen` / bypass template, and no wrapper/provider redirect.
If none exists, stop for profile preparation and revised approval, rather than manufacturing
an unreviewed profile. Inspect only sanitized profile metadata, never secret values.

First set `$c796ProfileId` and `$c796ApprovedRevision` (profile/revision GUIDs), `$c796ExpectedSha` (full loaded source SHA),
`$c796ApprovalRef` (recorded approval), and `$c796InstalledVersion` (the version of the
**profile's resolved executable**, not an unrelated PATH installation). Refresh the card
via `pwsh -NoProfile -File scripts/card.ps1 get CARD-0796 -Json`; the API read below obtains
its board without hard-coding the deployment's board GUID.

```powershell
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'S2 requires the Windows desktop operator shell.' }
if (-not $c796ApprovalRef -or $c796ExpectedSha -notmatch '^[0-9a-f]{40}$') {
    throw 'Recorded operator sanction and full expected SHA are required.'
}
$c796Api = 'http://localhost:17202'
$c796Headers = @{}
if ($env:ANTIPHON_TASK_TOKEN) {
    $c796Headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN
}
$c796Version = Invoke-RestMethod "$c796Api/api/version" -Headers $c796Headers
if ($c796Version.version -ne $c796ExpectedSha -or 'land-v2' -notin $c796Version.capabilities) {
    throw 'Loaded server identity does not match the approved fixed stack.'
}
$c796Profile = Invoke-RestMethod "$c796Api/api/agent-tui/profiles/$c796ProfileId" -Headers $c796Headers
if ($c796Profile.kind -ne 'Codex' -or -not $c796Profile.isEnabled -or
    $c796Profile.revisionId -ne $c796ApprovedRevision -or
    $c796Profile.revisionDetails.modelArgumentName -ne '--model') {
    throw 'Approved standard Codex profile is unavailable.'
}
$c796Card = Invoke-RestMethod "$c796Api/api/cards/CARD-0796" -Headers $c796Headers
$c796Run = [guid]::NewGuid().ToString('N')
$c796Evidence = Join-Path $env:LOCALAPPDATA "Antiphon\c796-evidence\$c796Run"
$c796Cwd = Join-Path $c796Evidence 'workspace'
$c796Home = Join-Path $env:LOCALAPPDATA 'Antiphon\codex-test-home'
if (-not (Test-Path (Join-Path $c796Home 'auth.json'))) {
    throw 'Operator must log into the isolated home first; do not copy credentials.'
}
New-Item -ItemType Directory -Path $c796Cwd -ErrorAction Stop | Out-Null
$c796VersionFile = Join-Path $c796Home 'version.json'
$c796HadVersion = Test-Path $c796VersionFile
$c796PriorVersion = if ($c796HadVersion) { [IO.File]::ReadAllBytes($c796VersionFile) } else { $null }
if ($c796HadVersion) {
    [IO.File]::WriteAllBytes((Join-Path $c796Evidence 'version.before.json'), $c796PriorVersion)
}
# The sentinel must be NEWER than the installed version; never reuse an old release assumption.
$c796PendingVersion = '999.0.0'
if ([version]$c796InstalledVersion -ge [version]$c796PendingVersion) {
    throw 'Choose and approve a newer pending-version sentinel.'
}
$c796Seed = @{
    latest_version = $c796PendingVersion
    last_checked_at = [DateTime]::UtcNow.ToString('o')
    dismissed_version = $null
} | ConvertTo-Json
[IO.File]::WriteAllText($c796VersionFile, $c796Seed, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath $c796VersionFile -Destination (Join-Path $c796Evidence 'version.seed.json')
@{ approval = $c796ApprovalRef; sourceSha = $c796ExpectedSha; run = $c796Run
   profileId = $c796ProfileId; revisionId = $c796ApprovedRevision
   hadVersion = $c796HadVersion; versionFile = $c796VersionFile
} | ConvertTo-Json | Set-Content (Join-Path $c796Evidence 'operation.json')
```

Before seeding, confirm no live CLI or other test is using this isolated home. The current
`last_checked_at` prevents an immediate release refresh from erasing the sentinel. If the
installed version has a prerelease suffix, compare parsed semantic versions deliberately;
do not drop the newer-than-installed assertion. Preserve the approval/profile revision and
seed hash in the local evidence inventory. No output or archive may include credentials.

Create and start exactly this one diagnostic agent. Creation with `runnerId=null` selects
the desktop for a **named agent**; task defaults do not relocate it. `assignmentPolicy=Paused`
and an empty queue prevent picking up real work. Do not attach CARD-0796 as its current card.

```powershell
$c796Agent = Invoke-RestMethod "$c796Api/api/agents" -Method Post -Headers $c796Headers `
    -ContentType 'application/json' -Body (@{
        name = "c796-diag-$c796Run"
        workingDirectory = $c796Cwd
        details = 'CARD-0796 operator-approved startup diagnostic only.'
        boardId = $c796Card.boardId
        assignmentPolicy = 'Paused'
        tuiProfileId = $c796ProfileId
        modelLevel = 'Low'
        modelId = 'gpt-5.6-luna'
        sessionBackend = 'PtyHost'
        runnerId = $null
        alwaysOn = $false
        remoteControlEnabled = $false
        autoCompactEnabled = $false
        bundleKeys = @()
        systemPromptAppend = 'Diagnostic only. Do not use tools, edit files, or delegate. Answer only the diagnostic marker.'
    } | ConvertTo-Json -Depth 6)
if ($c796Agent.kind -ne 'Codex' -or $c796Agent.currentCardId -or $c796Agent.queue.Count) {
    throw 'Unexpected diagnostic agent identity or work queue; do not start.'
}
$c796Agent.id | Set-Content (Join-Path $c796Evidence 'agent-id.txt')
$c796StartUtc = [DateTime]::UtcNow
$c796Started = Invoke-RestMethod "$c796Api/api/agents/$($c796Agent.id)/start" `
    -Method Post -Headers $c796Headers -ContentType 'application/json' -Body (@{
        fresh = $true
        remoteControl = $false
        ignoreSubscriptionQuota = $false
        ignoreModelDisabled = $false
        launchEnvOverride = @{ CODEX_HOME = $c796Home }
    } | ConvertTo-Json -Depth 4)
$c796Session = [guid]$c796Started.persistentSessionId
$c796Session | Set-Content (Join-Path $c796Evidence 'session-id.txt')
```

This Start request is the **exact launch command**; it deliberately supplies no prompt.
`/api/version` calls its SHA field `version`, not `sha` (see `VersionDtos.cs`).
Recheck the profile revision before Start and abort if it changed since approval; require
the session's recorded `tuiProfileRevisionId` to match in the final evidence as well.
The expected effective native argv, after the runner resolves the npm shim, is:

```text
node.exe <resolved-installed-codex.js> --no-alt-screen --dangerously-bypass-approvals-and-sandbox --model gpt-5.6-luna -c model_reasoning_effort=low -c disable_paste_burst=true -c check_for_update_on_startup=false -c developer_instructions=<composed-diagnostic-instructions>
```

The `.cmd` -> node -> native resolution is the shipped runner path; do not invoke that
illustrative native line separately. Record the actual executable, CLI version, profile
revision, selected flags, ModernConPty backend (not a fallback), and isolated-home binding.
The update flag must come from shipped `AgentSessionLaunchComposer`; adding it manually in
the profile would conceal a broken composition. There is no `--session-id`, `--name`,
`--append-system-prompt`, `/usage`, raw input, or `SendNow` here.

Poll `GET /api/agents/{id}` at most once a second for `liveSession.status=Running`, recording
the session ID and timestamps. **Agent status alone is not sufficient**: Start can mark
the agent Running while its session is still Starting. Keep the adapter's actual
`codex-startup ready reason=positive-settle elapsedMs=...` line, with elapsed time at most
60,000 ms and the unchanged 1,000 ms settle setting. Bound the outer start observation to
90 seconds to include launch/API overhead; this does not add time to the readiness gate.
If no Running session or no positive ready evidence exists, collect failure evidence and
stop this diagnostic under the sanction. Do not retry, shorten settle, or lengthen the gate.

Before sending anything, save the positive empty-composer snapshot from
`GET http://localhost:17204/sessions/{id}/snapshot` and the server buffer. It must show
a loaded model, supported empty composer, model/effort/cwd footer, and no modal/MCP blocker.
Save the transcript baseline, then enqueue the one LF-separated nonce prompt:

```powershell
$c796Baseline = Invoke-RestMethod "$c796Api/api/sessions/$c796Session/transcript?since=0" -Headers $c796Headers
$c796Prompt = "[card-0796-diagnostic:$c796Run]`nReply exactly C796_OK_$c796Run. Do not use tools."
[IO.File]::WriteAllText((Join-Path $c796Evidence 'prompt.txt'), $c796Prompt, [Text.UTF8Encoding]::new($false))
$c796Queued = Invoke-RestMethod "$c796Api/api/sessions/$c796Session/messages" `
    -Method Post -Headers $c796Headers -ContentType 'application/json' `
    -Body (@{ body = $c796Prompt; mode = 'WhenIdle' } | ConvertTo-Json)
# Poll these reads for at most 60 seconds after enqueue; never re-POST on uncertain delivery.
Invoke-RestMethod "$c796Api/api/sessions/$c796Session/transcript?since=$($c796Baseline.lastSequence)" `
    -Headers $c796Headers | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $c796Evidence 'transcript.after.json')
Invoke-RestMethod "$c796Api/api/sessions/$c796Session/messages" -Headers $c796Headers `
    | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $c796Evidence 'queue.json')
```

The queue owns LF normalization, bracketed paste and the separate Enter. Require a **new
`UserPrompt` after the baseline** containing the complete submitted body, not merely its
nonce or a `QueuedUserPrompt` / assistant echo. Compare to the immutable actual queue body
(including any server-owned framing) using the complete-match rule, retaining both bodies.
Record the queue ID, attempt floor, verdict and native conversation ID. Capture the same
session's native rollout metadata and `thread/start` evidence from the isolated home's
session-specific Codex log/trace, joined to the runner session, cwd and timestamps. Missing
native correlation or missing full input is inconclusive, even if a reply looks correct.
Keep the `C796_OK_<nonce>` response / TurnEnd if produced; do not start a second turn to
repair an incomplete attempt. Prompt receipt and readiness are separate verdicts.

**Evidence artifact:** commit `docs/investigations/2026-09-30-card-0796-desktop-codex-qualification.md`
(use the actual date if later). Record approval, source/loaded SHAs, runner/CLI/profile/backend,
unchanged readiness settings, seed contents/hash and newer-version comparison, isolated home
identity, run/agent/session/native-thread/queue IDs, timing, ready screen, complete input
comparison, final verdict and rollback. Store raw sanitized evidence outside the checkout
under the per-run evidence directory with an exact file/hash inventory. Publish selected
diagnostic-only excerpts sufficient for remote review; do not rely on an inaccessible path
alone or copy whole provider stores. On failure keep the startup capture path (default
`%TEMP%\antiphon-codex-startup`), escaped raw tail and session-scoped desktop/runner/pty-host
logs. Do not write frames or home contents into ordinary application logs.

**Rollback, including failed attempts:** capture evidence first; stop only the recorded
diagnostic agent through the server, confirm its exact session is Exited/absent at the
runner and terminal at the server, then restore version bytes (or absence). This stop is
part of the explicitly sanctioned bounded experiment, not an automatic response to any
other stalled session. If stop/ownership is uncertain, retain the home reservation and
report cleanup pending; a HTTP acknowledgement alone does not prove process exit.

```powershell
Invoke-RestMethod "$c796Api/api/agents/$($c796Agent.id)/stop" -Method Post -Headers $c796Headers | Out-Null
# After verifying terminal state for $c796Session at BOTH server and runner:
if ($c796HadVersion) {
    [IO.File]::WriteAllBytes($c796VersionFile, $c796PriorVersion)
} else {
    Remove-Item -LiteralPath $c796VersionFile -ErrorAction SilentlyContinue
}
```

Run rollback on any exception after the seed, including create/start refusal. If the shell
is lost, recover the agent by the recorded unique name/ID and version bytes from the local
backup before proceeding; never blindly repeat Start or create another agent. Preserve the
stopped diagnostic agent/session for review. No profile/default was edited; the home
override was launch-only. Optional later native-thread removal uses `codex delete --force
<native-uuid>` with this isolated `CODEX_HOME`, never manual rollout deletion. Evidence stays.
Restore no file while a process can still write it. Verify the restored hash/absence.

### S3 — Remove the refusal and repair its complete test footprint (one atomic Code slice)

**Entry gate:** accepted, committed S2 evidence naming the qualified source/stack and all
required receipts. A Code brief must link that evidence commit and the operator sanction.
If absent, report the gate without launching a probe or editing production admission.

1. `server/Application/Services/DefaultRunnerRoutingPolicy.cs`: delete the obsolete reason,
   refusal string and desktop-only predicate if no callers remain. Make the canonical
   desktop arm of `IsHostKindCompatible` and `IsHostKindAdmitted` admit kinds as before
   CARD-0772; keep their distinct remote predicates. Remove only the desktop branch of
   `RunnerKindBlockedReason`; retain `runner_kind_unsupported` and its recovery prefix.
2. `server/Application/Services/AgentTaskService.cs`: remove the final desktop create guard
   and explicit-reroute desktop exception. Keep platform precedence and generic host-kind
   checks in reroute/wall paths. `AgentTaskDispatcher.cs`: retain the pre-claim remote fence
   and both later host-kind checks; update the CARD-0772 comment to the remaining contract.
   Do not revive historical Failed/Blocked tasks by database rewrite. Existing list-governed
   routing recovery continues normally; an old ungoverned refusal remains an explicit reroute
   decision. Historical reason text may remain in stored events and records.
3. `TaskPlatformPlacementTests`: rename the first four C772 refusal tests to
   `C796_Explicit_desktop_codex_is_admitted`, `C796_Defaulted_desktop_codex_is_admitted`,
   `C796_Windows_fallback_selects_desktop_without_linux_reroute`, and
   `C796_Resolved_codex_kind_is_admitted`. Preserve every existing matrix dimension and
   change the oracle to a persisted Queued Codex task on canonical null desktop, correct
   platform/source/default audit and exactly one creation per request. Preserve retained
   standing-session identity. Update `Any_keeps_default_and_local_fallback` and the Shared
   Codex branch of `DefaultRunnerCreateTests.Explicit_local_is_persisted_as_null` likewise.
4. `TaskPlatformDispatchTests`: replace the legacy block test with
   `C796_Legacy_desktop_codex_dispatches` across null/local/desktop/case/whitespace aliases,
   Any/Windows and every tier. Use the existing accepting `StableDesktopDirectory` and
   launch/queue sink, not `HoldingDirectory` (which intentionally throws below the old fence).
   Give loop cases isolated schemas or sufficient budget/scope so a fixture hold cannot
   masquerade as admission. Assert persisted Dispatched/session/agent kind, desktop binding,
   expected Windows/Any launch constraint, one task-associated brief and no remote preparation.
   Replace the existing-session block test with
   `C796_Desktop_codex_existing_session_receives_one_brief`: reuse the exact idle Codex
   session, one marked queued brief, no fresh launch, no stop, and no `/compact` input.
5. `DefaultRunnerRerouteTests`: replace its first four C772 refusal cases with
   `C796_Explicit_desktop_codex_reroute_keeps_host`, `C796_Queued_walk_selects_desktop_codex`,
   `C796_Blocked_resume_selects_desktop_codex`, and `C796_Usage_wall_selects_desktop_codex`.
   Assert actual selected Codex, unchanged host/platform/requirement, expected explicit
   versus governed pin semantics, one Rerouted event and no obsolete Blocked note. The
   wall case releases only the old walled session through `RecordingSessionStopper`.
   Test an ordered Claude -> Codex -> Grok list with Claude held: Codex wins, never Grok.
   Use accepting desktop dispatch fixtures, or a separately asserted downstream hold after
   the successful reroute, rather than allowing a fixture exception to be the result.
6. Preserve the five C772 parent-note/idempotence methods one-for-one with valid oracles:
   the transaction, receipt and restart cases become C796 **remote legacy OpenCode**
   pre-claim `runner_kind_unsupported` cases; the repeated-desktop-block case becomes
   `C796_Repeated_desktop_codex_resume_is_idempotent` (one resume/reroute, no duplicate
   brief or refusal note across fresh ticks); the usage-wall-note case uses a real
   exhausted candidate list, retaining both enqueue-failure and crash-after-save cuts.
   Parameterize the receipt helper's expected reason; never keep an assertion of the
   removed refusal. Preserve busy/idle parent variants and complete recipient UserPrompt,
   immutable queue identity, restart recovery, atomic none/all saves and `fault.Fired`.
   Existing generic remote refusal tests remain. Do not delete transactional tests to get green.
7. Repair/strengthen the **six** stale methods without retargeting them to a remote runner
   or weakening their acceptance assertions: `CodexDelegateDispatchTests` cold session/pool,
   warm unrelated-work reuse and dispatch model; `PinnedAgentKindTests.T1` / `T2` inference
   and mismatch-versus-agreement; `PinnedProfileLaunchSpecTests.T8` exact model versus tier
   plus the real dispatch event. Add explicit canonical desktop identity where useful.
   A method restored by production removal may need only stronger assertions/comments.
8. In both the Codex delegate launch-spec test and
   `CardSpawnModelArgumentTests.Codex_assigned_card_spawn_carries_the_reasoning_effort_override`,
   assert the **literal** `check_for_update_on_startup=false` appears exactly once immediately
   after `-c`, and there is no conflicting true override. Comparing only to
   `CodexLaunchArgs.DisableUpdateCheck` is a self-consistency check that misses a bad constant.
9. Update `docs/agent-kinds.md` to link the accepted qualification and describe restored task
   admission. Append a dated outcome/link to the CARD-0777 investigation; preserve its original
   historical refusal statement. Never edit generated `docs/cards/` exports. Commit/push the
   implementation and test changes together, then run the closed checkpoint list below.

### S4 — Review, land, activate and report the related-card disposition

Ordinary Review examines S2 provenance and CP receipts before land; synthetic green cannot
replace S2. Follow ordinary landing and the canonical activation check; report the new loaded
SHA. No second live launch is implied. The caller can close CARD-0783 and CARD-0843 with the
six-method green evidence after S3 lands, using normal card revision/reason-file operations.
This Plan task does not close them. Commission the designed Mutation battery after confirmed
land under the normal SourceLanding custody rules. If S3 needs rollback, use a new revert of
the S3 admission change and its changed tests, preserving CARD-0777, then review/land/activate
normally; never reset published history. S2 failure needs no production rollback: the refusal
was never removed.

## Verification design

No build, tests, native launch or live state mutation was executed in the Plan stage. Counts
below are source-derived planned TUnit executions, not claimed test results. Internal loops
are not extra executions. Keep one-for-one test replacements so the named-class totals hold;
if implementation adds cases, revise the roster/counts before execution and explain the delta.

### Ordinary invariants and roster

| ID | Oracle / classes | Planned executed count |
|---|---|---:|
| V-1 | Desktop create/default/platform/retained-kind acceptance, platform refusals and placement audit: `TaskPlatformPlacementTests` + `DefaultRunnerCreateTests` | 15 + 7 = 22 |
| V-2 | Cold/warm desktop dispatch and platform revalidation, no remote fallback: `TaskPlatformDispatchTests` | 10 |
| V-3 | Explicit, queued, recovery and wall transitions; real remaining refusals; parent queue/transaction/restart receipts: `DefaultRunnerRerouteTests` | 16 |
| V-4 | All six known failures plus model/argv/kind/pool invariants: `CodexDelegateDispatchTests` + `PinnedAgentKindTests` + `PinnedProfileLaunchSpecTests` | 21 + 4 + 4 = 29 |
| R-1 | Remote Codex admission and still-rejected shapes, named/card launch composition and literal update flag: `CodexPhoneHomeCreateTests` + `CardSpawnModelArgumentTests` | 6 + 15 = 21 |
| R-2 | Adapter readiness, one Escape, capture and no logging of frame contents: `RunnerCodexAdapterReadyTests` | 12 |
| R-3 | Positive settle, both modal fixtures, no repeated Escape, unknown/blocked/deadline frames: `CodexReadyWaitTests` + `CodexStartupReadinessTests` + `CodexReadyTrackerTests` | 19 + 27 + 4 = 50 |
| R-4 | Server Unit lane: cross-cutting policy/contract/classification checks required by the repository Code recipe | all discovered Unit cases; floor 1, exact total from fresh TRX |
| LIVE-1 | Operator S2 ready/Running/native-thread/full-UserPrompt/rollback receipt on Windows | one session / one queued prompt; not TUnit |

The seven bounded class groups total **160 executions**; the Unit lane also contains some
of those tests and is reported separately, never summed as unique coverage. R-4's dynamic
assembly-wide count is intentionally not fabricated from source-method counts. Zero failed
and zero skipped are required for every named group; Unit failures/skips must be individually
dispositioned, never silently credited as green or deferred to the unrelated stale-test cards.
Database fixtures use the established isolated test schema/Testcontainers support. No test
host may contact the production runner, provider, broker or user's Codex home.

### Positive controls (Mutation after land; do not execute against the live stack)

Each PC uses an isolated source mutation, exact method filter, baseline green -> expected
assertion red -> restored green, fresh output/TRX and restored tracked/index bytes. Compile
failure, zero discovery, unrelated fixture failure or generic timeout is not the red oracle.
All filters have the prefix `/*/*/`; suffixes below are the complete class/method portion.
Use the unmodified `scripts/run-checkpoint.ps1` under the Mutation evidence/custody contract.

| PC | Reintroduced production defect | Exact filter suffix and required red |
|---|---|---|
| PC-1 | Restore the desktop create refusal in `AgentTaskService.CreateAsync` with a literal local ConflictException; leave dispatcher fixed. | `TaskPlatformPlacementTests/C796_Explicit_desktop_codex_is_admitted` — valid create now throws instead of persisting the required Queued Codex row. |
| PC-2 | Restore only the desktop rejection in `IsHostKindAdmitted`, using an inline canonical-desktop/Codex check (no deleted symbols). | `TaskPlatformDispatchTests/C796_Legacy_desktop_codex_dispatches` — expected Dispatched/session/brief becomes Blocked. This catches the refusal silently remaining after create is fixed. |
| PC-3 | Restore only desktop rejection in `IsHostKindCompatible`. | `DefaultRunnerRerouteTests/C796_Explicit_desktop_codex_reroute_keeps_host` — the explicit transition refuses instead of selecting Codex. |
| PC-4 | In `TryRewalkQueuedChainAsync`, make a desktop Codex candidate take the existing block branch. | `DefaultRunnerRerouteTests/C796_Queued_walk_selects_desktop_codex` — selected-kind / Rerouted-event assertions fail. |
| PC-5 | In `ResumeRoutingBlockedAsync`, make that candidate take the block branch. | `DefaultRunnerRerouteTests/C796_Blocked_resume_selects_desktop_codex` — resume count and persisted Codex/Rerouted assertions fail. |
| PC-6 | In `RerouteOnWallAsync`, make that candidate take the incompatible-host branch. | `DefaultRunnerRerouteTests/C796_Usage_wall_selects_desktop_codex` — Rerouted verdict and persisted Codex assertions fail, while old-session release remains attributable. |
| PC-7 | Make the remote arm of `IsHostKindAdmitted` return true. | `DefaultRunnerRerouteTests/Queued_rewalk_blocks_incompatible_host_before_prep` — its legacy remote OpenCode row no longer has the required Blocked/no-session outcome. |
| PC-8 | Change `CodexLaunchArgs.DisableUpdateCheck` to the literal true value. | `CodexDelegateDispatchTests/a_codex_delegate_launches_the_codex_definition_with_a_slug_an_effort_and_developer_instructions` — literal false flag assertion fails. |
| PC-9 | Remove only the update flag pair from `AgentSessionLaunchComposer`. | `CardSpawnModelArgumentTests/Codex_assigned_card_spawn_carries_the_reasoning_effort_override` — named/card composition flag assertion fails. |
| PC-10 | Suppress the measured-picker Escape write in `CodexReadyWait`; keep fixture snapshots progressing. | `CodexReadyWaitTests/Update_modal_mid_wait_is_skipped_once_then_requires_fresh_readiness*` — expected single Escape assertion fails on both argument rows. |
| PC-11 | Make warm reuse enqueue `/compact` for unrelated Codex work. | `CodexDelegateDispatchTests/a_codex_pool_reuse_of_unrelated_work_types_no_compact` — extra slash input / queue-count assertion fails. |

The two independent admission controls PC-1/PC-2 are mandatory: leaving either refusal in
production must make ordinary acceptance tests red. Retained rejection assertions alone,
string absence searches, or a policy-helper self-comparison cannot establish removal.
PC-3 through PC-6 prevent admission working only for fresh tasks. PC-7 prevents removing
the generic fence along with the obsolete desktop condition.

### Execution

Commit/push S3 before its one checkpoint run. Reuse a built checkpoint tool if available;
otherwise its launcher build is the single explicitly listed tooling prerequisite (reason:
CARD-0723 mandatory driver), leased through `scripts/build-slot.ps1`. Run:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c796-checkpoint-launch -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-30-card-0796-qualify-desktop-codex-plan.md --after S3
```

After exit 75, use the emitted run ID with `dotnet run --no-build --project
tools/Antiphon.Checkpoints -- wait <run-id> --max-wait 50s` until terminal; do not abandon
a running executor. The row driver owns each build/test slot. Exit 4 is reported as slot
timeout, never retried with `-NoSlot`. One shared server build and one Pty build; reused
outputs share `After=S3`. Rows are serial, including builds, so server and Pty assemblies
never overlap. No full assembly, E2E, real headed tests or unlisted diagnostic runs.

### Cost

Ordinary checkpoint budget: **45 minutes** plus S3 authoring (~90 minutes). Operator S2:
~20 minutes including preflight/evidence/cleanup, excluding any separate login or activation.
Review ~30 minutes; Mutation ~90 minutes for eleven method-scoped controls and restoration.
These are estimates, not permission to omit a row. Report each CP with commit, build status,
exact filter, executed/passed/failed/skipped, fresh TRX and rerun count. Update estimates if
the real lane differs; do not convert minutes or internal assertions into execution counts.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S3 | `tests/Antiphon.Tests -> bin-c796-server/` | create-placement | `/*/Antiphon.Tests.Application/(TaskPlatformPlacementTests*)\|(DefaultRunnerCreateTests*)/*` | V-1 | all 22 listed executions, 0 failed/skipped | 22 | 8 | true |
| CP-2 | S3 | CP-1 | desktop-dispatch | `/*/*/TaskPlatformDispatchTests/*` | V-2 | all 10 listed executions, 0 failed/skipped | 10 | 5 | true |
| CP-3 | S3 | CP-1 | reroute-receipts | `/*/*/DefaultRunnerRerouteTests/*` | V-3 | all 16 listed executions, 0 failed/skipped | 16 | 8 | true |
| CP-4 | S3 | CP-1 | stale-codex-regressions | `/*/Antiphon.Tests.Application/(CodexDelegateDispatchTests*)\|(PinnedAgentKindTests*)\|(PinnedProfileLaunchSpecTests*)/*` | V-4 | all 29 listed executions including all six known failures, 0 failed/skipped | 29 | 5 | true |
| CP-5 | S3 | CP-1 | remote-and-composition | `/*/Antiphon.Tests.Application/(CodexPhoneHomeCreateTests*)\|(CardSpawnModelArgumentTests*)/*` | R-1 | all 21 listed executions, 0 failed/skipped | 21 | 5 | true |
| CP-6 | S3 | CP-1 | adapter-readiness | `/*/*/RunnerCodexAdapterReadyTests/*` | R-2 | all 12 listed executions, 0 failed/skipped | 12 | 2 | true |
| CP-7 | S3 | CP-1 | server-unit | `/*/*/*/*[Category=Unit]` | R-4 | all discovered Unit executions, >= 1 executed, 0 failed; report every skip | 1 | 7 | true |
| CP-8 | S3 | `tests/Antiphon.Agents.Pty.Tests -> bin-c796-pty/` | modal-and-ready | `/*/Antiphon.Agents.Pty.Tests/(CodexReadyWaitTests*)\|(CodexStartupReadinessTests*)\|(CodexReadyTrackerTests*)/*` | R-3 | all 50 listed executions, 0 failed/skipped | 50 | 5 | true |
