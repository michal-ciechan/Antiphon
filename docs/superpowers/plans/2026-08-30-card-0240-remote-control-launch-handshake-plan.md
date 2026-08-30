# CARD-0240 — Bound a stuck `/remote-control` launch handshake and preserve work delivery

**Date:** 2026-08-30  
**Status:** planned (design only — no implementation in this change)  
**Card:** CARD-0240 (`1a17d12f-3039-486d-9b19-cf3bf1453541`)  
**Scope:** ClaudeCode remote-control bootstrap in `AgentSessionService`, its protocol-adapter read seam,
and focused launch tests. No change to normal delegated-task dispatch, which already passes
`remoteControlName: null`.

## Verdict

The normal `WaitForRemoteControlArmedAsync` loop does contain a nominal
`RemoteControlArmTimeoutMs` deadline, but that deadline does not bound work performed inside an
iteration. It calls synchronous `IAgentProtocolAdapter.SnapshotRawOutput()`; the production
runner adapter synchronously blocks on an HTTP snapshot with `CancellationToken.None`. Therefore
the loop cannot re-check its deadline, degrade, or release the real work prompt while that one
snapshot is outstanding. The live evidence proves the normal timeout/degrade path did not run
for the affected Claude sessions (zero `RcDegraded` incidents), but the missing transcripts mean
it cannot prove which pre-timeout await or snapshot call was blocked. Treat the synchronous
snapshot as the concrete unbounded boundary to remove, and impose one cancellation-aware total
budget over the entire best-effort RC setup so no other delivery/read operation can recreate the
same launch hostage.

When the bridge does not arm, record the existing Warning incident and return control to the real
launch. Do **not** issue `/rename` after that outcome: Claude only syncs a rename while the bridge
is armed, so it cannot achieve its purpose and it adds another command to a TUI that just failed
to leave `/rc connecting…`. The subsequent card/work prompt remains the existing decisive
verified-delivery operation: if the composer is healthy it is sent; if the stuck local command
has made it unusable, that *work-prompt* failure remains visible and fatal rather than silently
leaving a live but purposeless session.

## Evidence and limits of the incident record

| Observation | Evidence | Design consequence |
|---|---|---|
| The confirmed stuck population is ClaudeCode, not a cross-kind RC mechanism. | `gym-stat-numericoverflow` (`1fa9e45e-…`), `gym-stat-floorplanux` (`c45860a0-…`) and `gym-stat-privacypolicy` (`a8b93113-…`) are `AgentKind=ClaudeCode`; their retained runner buffers show `/rc connecting…`, the generated `zz<session-short-id>` pairing text, then no meaningful later output (last sequences 21, 19, 19). | Limit the behavioural fix to the shared Claude-capable RC path; do not add kind switches. `RemoteControlPolicy.Permits` remains the gate. |
| The timeout-and-degrade branch was not reached. | Each of those three session ids has zero `AgentIncidentKind.RcDegraded` rows. Their process was later stopped manually; no launch failure was recorded while they sat. | A timeout log/incident alone is not enough: the whole setup call must be bounded, including each I/O operation inside its polling loops. |
| The requested transcript check is inconclusive, not negative evidence. | `GET /api/sessions/{id}/transcript` returns `entries: []` for both named sessions. Runner logs say the Claude transcript claim was refused and the children exited without an identifiable transcript, although input was delivered. | Do not claim that `/remote-control`, the arm timeout, `/rename`, or the work brief completed from transcript evidence. Add outcome-level logs/tests so the next event is attributable even when a transcript cannot bind. |
| Grok and Codex were nearby failed launches, but not RC handshakes. | `gym-stat-accountgymforms` is Grok and its retained screen is the Grok welcome UI; `gym-stat-machinetypeeditor` is Codex and its screen is the Codex welcome/composer. Neither contains `/rc`; both have zero transcript entries. The catalog supports remote control for ClaudeCode only, and the direct-start policy rejects explicit RC on Grok/Codex. | They need separate diagnosis if still actionable. They are not evidence for widening this card to provider-specific RC behaviour. |
| The code's apparent 20-second timer is not an end-to-end deadline. | `AgentSessionService.WaitForRemoteControlArmedAsync` checks `DateTime.UtcNow` around `adapter.SnapshotRawOutput()`. `RunnerClaudeAdapter.SnapshotRawOutput()` calls `SnapshotTextAsync(CancellationToken.None).GetAwaiter().GetResult()`, which reaches the runner's `/snapshot` endpoint. `SessionRunnerHttpClient` has a normal request timeout (currently 100 seconds), but the arm loop supplies no remaining-time cancellation and cannot regain control until the call returns or faults. | Replace this synchronous read in the arm wait with a cancellable async read, using the remaining RC budget. Also cap the outer RC bootstrap, not just the marker poll. |

The screen buffers establish that Claude accepted `/remote-control` far enough to enter its own
connecting state. They do **not** identify whether the trapped await was the first-output check,
the first raw snapshot, or a later command delivery. This plan deliberately fixes the proven
deadline leak and adds stage/outcome logging; a live reproduction is still required if a bounded
implementation shows a different stage timing out.

## Current flow and fault line

`AgentSessionService.StartAsync` (card path) and `LaunchInteractiveProcessAsync` (named-agent
path) both call `SendRemoteControlCommandsAsync` before the actual work/launch message. For a
permitted, non-empty name the method currently does:

1. capture the raw-output baseline;
2. `SendBootPromptWithRetryAsync("/remote-control")`;
3. wait up to three seconds for any first output;
4. poll raw output for `"remote-control is active"` until `RemoteControlArmTimeoutMs`;
5. on no marker, write `RcDegraded` but still send `/rename <name>`;
6. return and let the actual prompt take the normal verified path.

`SendBootPromptWithRetryAsync` retries only `PromptDeliveryException` and its Claude adapter's
screen/output polls have their own configured limits. Those inner limits are not a total RC
launch budget, and other exceptions/cancellation are not caught by the present best-effort
handler. Most importantly, step 4's synchronous snapshot call is outside the deadline's control.
`RaiseRemoteControlDegradedAsync` is already best-effort and must remain so.

## Implementation plan

### 1. Give remote-control setup one explicit, cancellable deadline

**Files:** `server/Application/Settings/AgentSessionSettings.cs`, `server/Application/Services/AgentSessionService.cs`,
and the bound configuration/defaults tests that validate `AgentSessionSettings`.

- Add a clearly named total RC-bootstrap setting (for example `RemoteControlSetupTimeoutMs`) with
  an intentionally documented default. It must cover only the monitoring bootstrap — delivery of
  the card work prompt and launch note is not part of this budget. Keep
  `RemoteControlArmTimeoutMs` as the maximum time spent waiting for the armed marker after a
  successful RC submit.
- At the start of `SendRemoteControlCommandsAsync`, create a linked CTS from the caller token and
  apply the total deadline. Pass that local token through the RC submit, first-output wait,
  marker wait, and optional rename. Distinguish expiry of that local CTS from cancellation of the
  launch itself: external cancellation still propagates; only local RC-budget expiry becomes a
  best-effort degradation and a return to launch.
- Catch all expected RC setup failures needed to meet that contract, not merely
  `PromptDeliveryException`. Convert a local deadline expiry and an RC transport/read failure
  into the same existing `RcDegraded` Warning path, with a precise failure reason/message naming
  the failed stage. Preserve `RaiseRemoteControlDegradedAsync`'s own independent scope and
  never make an incident-write failure abort a healthy launch.
- Log a start, armed, and degraded/timeout outcome with the session id and stage. This is
  operational evidence only; do not manufacture a transcript record for a local slash command.

**Acceptance condition:** once the total timer expires, `SendRemoteControlCommandsAsync` returns
within the small cancellation/cleanup margin, has raised at most the deduplicated Warning, and
the caller proceeds to the real launch operation.

### 2. Remove the synchronous snapshot from the marker wait

**Files:** `server/Application/Interfaces/IAgentProtocolAdapter.cs`, all concrete adapter
implementations and test forwarding adapters found by the interface change, notably
`server/Infrastructure/Agents/SessionRunner/RunnerClaudeAdapter.cs`,
`server/Infrastructure/Agents/SessionRunner/RunnerTerminalSession.cs`, the in-process PTY
adapters, and `tests/Antiphon.Tests/Agents/FakeAgentProtocolAdapter.cs`.

- Add an asynchronous raw-output snapshot seam that accepts a `CancellationToken`, retaining the
existing synchronous snapshot only where a synchronous caller genuinely requires it. The new
method is the path `WaitForRemoteControlArmedAsync` uses.
- In the session-runner adapter, implement it by awaiting the terminal snapshot with the supplied
token; do not call `.GetAwaiter().GetResult()` and do not substitute `CancellationToken.None`.
In in-memory/in-process adapters it can return the already-available raw buffer, but must honour
the common interface contract.
- Change `WaitForRemoteControlArmedAsync` to take the local RC token, calculate the remaining arm
time before each read/delay, and stop as an unarmed result when the arm deadline expires. A
canceled read caused by the local RC deadline is an expected unarmed/setup-timeout result; an
external cancellation still escapes. Do not use `Task.Run`/`WhenAny` around the old synchronous
call: that abandons a live terminal operation and permits later prompts to race it.
- Audit the first-output wait and both boot-command calls under the outer CTS from step 1. Their
own delivery verification retains its more detailed retry semantics, but it cannot consume an
unbounded amount of launch time any more.

**Acceptance condition:** the marker wait has no synchronous/blocking terminal or HTTP operation
that can outlive its budget, and adapter contract changes compile across Runner, in-process, raw,
Codex, Grok, and test-wrapper implementations.

### 3. Make the unarmed branch safe for the work prompt

**File:** `server/Application/Services/AgentSessionService.cs`.

- Change the no-marker outcome from “degrade and send `/rename` anyway” to “degrade and return
from RC setup.” This only changes the unarmed/timeout path. Keep the existing order — RC first,
rename second — on the successfully armed path so claude.ai title synchronisation is unchanged.
- Do not try to clear, escape, or retype Claude's `/rc connecting…` screen without a measured
vendor contract. The current incident has no evidence that Escape/Ctrl+C safely restores the
composer, and an unverified recovery keystroke could interrupt or corrupt a real work prompt.
- Let the next real prompt use its existing verified delivery. If that prompt cannot be shown and
submitted, it remains a work-delivery failure (visible launch failure and teardown), not an RC
degradation misreported as a successful task. If it can be delivered, the session continues
unmonitored as CARD-0056 requires.

**Acceptance condition:** a failed monitor bootstrap cannot append `/rename` into a connecting
TUI, and it cannot silently convert an undeliverable actual task prompt into a healthy-running
session.

### 4. Replace the misleading test contract with launch-completion coverage

**Primary file:** `tests/Antiphon.Tests/Application/AgentSessionLaunchFailureTests.cs`.

- Replace/update `Remote_control_that_never_arms_records_an_incident_and_still_renames`; its
current expected `/rename` preserves the unsafe behaviour this card corrects. Use the existing
`LaunchFixture` and `FakeAgentProtocolAdapter` rather than a live CLI.
- Add a card-launch test whose fake accepts `/remote-control`, never emits
`remote-control is active`, and continues to accept the actual work body. Configure a compressed
RC budget/arm timeout. Assert, within a bounded test deadline, that:
  - the card start returns rather than hanging;
  - `RcDegraded` is a Warning with the expected timeout/unarmed reason;
  - `/rename` was never sent;
  - the actual work prompt was sent/submitted after RC setup; and
  - session/attempt outcome follows the fake's normal successful path.
- Add a stricter adapter-seam test in the same class or the closest adapter test file: make the
async raw snapshot wait until its supplied token is canceled. Assert the total RC budget releases
the launch, records the degradation, and reaches the actual work prompt. This is the regression
test for the exact deadline leak; a simple “no marker” fake alone only tests a responsive polling
loop.
- Keep the existing tests that prove a failed work prompt still fails the launch. Add/retain an
assertion that external caller cancellation is not swallowed as `RcDegraded`.
- Update any interface forwarding stubs in `AgentSessionServiceIntegrationTests` and
`OrchestratorServiceIntegrationTests`, then run the affected integration test class. No headed
or real-provider test is needed for this deterministic service/adapter seam.

## Verification

Run from the main checkout after implementation:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0240 -- --treenode-filter "/*/*/AgentSessionLaunchFailureTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0240 -- --treenode-filter "/*/*/AgentSessionServiceIntegrationTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0240 -- --treenode-filter "/*/*/OrchestratorServiceIntegrationTests/*"
```

Then launch one disposable **ClaudeCode** named agent with remote control enabled while observing
the session timeline and runner buffer. Verify either the armed marker and rename arrive in order,
or one `RcDegraded` Warning names the bounded stage and the real prompt is visibly delivered (or
fails visibly through the work-prompt path). Do not use Grok/Codex as a remote-control smoke test:
the policy should refuse an explicit request before a session is created.

## Non-goals and follow-up signal

- This card does not implement remote control for Grok, Codex, OpenCode, or Raw, and does not
  relax `RemoteControlPolicy`.
- It does not repair the three affected sessions' transcript binding failures; those prevented a
  definitive historical command timeline and remain independently incidented.
- It does not guess a safe Claude key sequence to cancel `/rc connecting…`. If the bounded fix
  still leaves a reproducible connecting screen after the work prompt is confirmed delivered,
  capture that fresh transcript/screen timeline and create a separate measured recovery card.
