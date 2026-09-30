# CARD-0719: usage limits are quota refusals

Plan baseline: `bc838ab35ac354e8ee163266fdbf79c57625c417`. Card read in full with
`pwsh -NoProfile -File scripts/card.ps1 get CARD-0719` on 2026-09-30.
This artifact includes verification design; its next implementation stage is Code.

## Outcome and scope

Recognize provider quota refusals before scheduling transient recovery, write a timed
automatic model-availability hold, and put affected delegated tasks in `Blocked` with
the reset instant and an attention item. Preserve their conversation and worktree so
the orchestrator can explicitly choose the next provider. Do not send a retry prompt
to a quota-blocked delegate, including after a reset or operator hold clear.

The three incident replays are mandatory. Claude session/model limits and Grok's
already-supported exhausted-credit/spending-limit forms receive the same delegated
task outcome. This is not a subscription-monitor rewrite, a new fallback-routing
policy, or permission to launch live providers in tests.

## Ground truth

| Card assumption / question | Observed code or transcript | Consequence |
|---|---|---|
| Codex was treated as transient | Every captured quota TurnEnd has `IsApiError=true`, `ApiErrorClass=usage_limit_exceeded`, null HTTP status. `ApiErrorClassifier.Classify` recognizes `rate_limit` but not `usage_limit_exceeded`; it returns **Unknown**, not Transient. `ApiErrorRetrySchedule` gives Unknown the transient ladder and `FireOneAsync` selects `TransientPrompt` for everything except Wall. | Add the exact structural class. A text-only fix would miss the strongest evidence. |
| The compact wrapper may hide the refusal | Session `061581e5`, sequences 35/37/39/41/43/45, retains both `usage_limit_exceeded` and the complete `Error running remote compact task: ...` diagnostic. | The wrapper did not hide this incident. Preserve it as raw evidence; support the same quota grammar after this specific prefix. |
| The Codex parser dropped the error | `CodexTranscriptNormalizer.ReadError` preserves the string `codex_error_info` fallback and non-JSON message, capped at 600 characters. `FromTaskComplete` emits an error TurnEnd. The captured texts fit the cap. | Preserve the normalizer's error boundary and bounded diagnostic. Do not scan ordinary assistant messages or make compaction metadata end a turn. |
| A Wall automatically supplies the required hold | `UsageLimitWallParser` recognizes `reset(s) [at] <clock> [(zone)]`, not `try again at Sep 26th, 2026 11:16 AM`. Unparsed resets use the timed model-cap fallback. | Add a dated reset grammar and retain the reset's timezone provenance. |
| All three messages have the same printed reset | The two Linux-session tails say **11:16 AM**; the Windows-session tail says **12:16 PM**, all on Sep 26, 2026. Neither prints a timezone. | The card paraphrase loses a material difference. Never apply the server's local timezone to all runners. |
| `/api/subscription-usage` should have stopped this | `SubscriptionUsageEndpoints` projects stored parsed samples; `SubscriptionUsageReader` is read-only. `SubscriptionQuotaGate` checks fresh samples at launch, passing absent/stale samples. `SubscriptionUsageParser` reads percentage panels, not error TurnEnds. | The error path must work with no samples and with monitoring disabled. Do not fabricate percentage samples from a refusal. |
| Correcting classification makes tasks Blocked | `AgentTaskReplyService.HandleApiErrorTurnAsync` defers unresolved recoveries as Working; another arm retains Working for a capacity wait. A list-governed Wall can reroute before either arm. | Add an explicit delegated-quota Blocked path before defer, capacity retention, and wall reroute. |
| Source is named Auto | The persisted/public enum is `ModelAvailabilitySource.AutoDetected`; the other value is `Manual`. The auto writer refuses `*` and `<synthetic>`. Manual rows retain source and deadline when automatic evidence arrives. | Acceptance's “Auto” means **AutoDetected**, not a new enum spelling. Test with no manual hold, then test manual precedence separately. |
| Re-adopting existing retries fixes them | `EnsureAdoptedAsync` returns an existing row after `TryRepairEmptyWallAsync`, which only repairs Wall rows. | Existing Unknown quota recoveries need a guarded reclassification path; adding a switch arm only fixes new rows. |
| Unknown's attempt cap stops the incident loop | Recoveries are keyed by session plus TurnEnd sequence. Each retry produces a new quota TurnEnd, and adoption replaces the old recovery with a fresh attempt counter. | A per-row Unknown cap cannot substitute for recognizing quota refusals. |
| Text is resolved before classification | `BuildNewRowAsync` classifies before loading/prefering the API-error AssistantText sibling. | Resolve one evidence bundle first, then classify and parse it. This matters to the Claude/text fallback path. |

Owners read: `project-context.md`, relevant checkpoint/build-slot sections of
`testing-and-build.md`, `ops-http.md`, `logs.md`, `agent-card-lifecycle.md`,
`orchestration-loop.md` (stage contract), and the quota/capacity sections of
`session-runtime-invariants.md`. Code must also read the relevant runtime/PTY owners
before changing transcript carriage.

### Captured incident evidence

Source: authenticated, read-only `GET /api/sessions/{full-id}/transcript?since=0` on
2026-09-30. The accompanying
[`fixtures/card-0719-transcript-tails.json`](fixtures/card-0719-transcript-tails.json)
contains only the error and retry entries selected from those responses. Text,
timestamps, sequence, UUID, API-error fields and turn-boundary metadata are preserved;
unrelated work content is omitted. These are normalized server records, **not raw
provider JSONL**. Any test JSONL reconstructed from them must be labelled synthetic.

| Task / card | Session | Retrieved entries / last sequence | First quota | Last quota | Compact evidence |
|---|---|---:|---|---|---|
| `7ebf8aa2-bc3a-421e-a314-ce38b00de365` / CARD-0589 | `faddd3e4-c9a7-444f-b011-38412388e705` | 208 / 208 | seq 192, 2026-09-25 16:34:07.146Z | seq 208, 16:44:33.098Z | none |
| `fb3b2b1c-bfff-4c0d-a375-1524c0cd8a4f` / CARD-0716 | `5af622ce-9fd4-452e-a310-9fb3ce94215f` | 15 / 15 | seq 7, 2026-09-25 16:38:29.095Z | seq 15, 16:44:38.132Z | none |
| `42c6b627-1173-4f42-ae8a-074fee7d5c76` / CARD-0688 | `061581e5-b0f6-4691-898f-5f2780ece69e` | 45 / 45 | seq 31, 2026-09-25 16:33:45.700Z | seq 45, 16:44:35.610Z | first at seq 35, 16:37:19.216Z |

All three tasks are now Canceled. The plan author did not alter them or write a live
hold. Acceptance replays isolated copies, not these historical database rows.

The exact Linux diagnostic uses a curly apostrophe:

> You’ve hit your usage limit. Visit https://chatgpt.com/codex/settings/usage to purchase more credits or try again at Sep 26th, 2026 11:16 AM.

Windows uses `12:16 PM`; its compact form prepends `Error running remote compact task: `.
The preserved retry entries include the task marker and “Your previous turn was
killed by a transient API error.” Their presence proves historical misdelivery;
tests must distinguish those input records from **newly enqueued output**.

## Decisions

### D-1. Structural quota evidence wins; text fallback is narrow

Map `usage_limit_exceeded` to Wall without requiring HTTP 429 or successfully parsed
text. Preserve existing `rate_limit`, 402, 429 and capacity-403 behavior. Add a pure,
bounded quota-text predicate for error stubs: Codex's straight/curly-apostrophe usage
limit, Claude's existing session/named-model limit forms, and Grok's measured capacity
vocabulary. Accept the known compact prefix, not arbitrary prose containing “limit”.

Apply that predicate only to a genuine API-error TurnEnd or its matching API-error
AssistantText sibling. Text fallback may upgrade unknown/other or a generic
server/transport wrapper carrying the exact quota diagnostic to Wall; explicit
authentication/model failures retain NeedsHuman. Plain 403 permission denial stays
NeedsHuman; real 5xx/transport failures stay Transient. Quoted quota text in a normal
assistant response, tool result, task brief or report must not trigger a hold.

Use the same resolved evidence/classification in recovery and task settlement.
Reject broad substring searches across transcript text and separate classifiers with
different precedence. No new `ApiErrorClassification` value is necessary.

### D-2. Dated reset parsing uses evidence time and the provider host's zone

Extend `UsageLimitWallParser` with the exact `try again at <English month> <day with
optional st/nd/rd/th>, <four-digit year> <h:mm AM/PM>` grammar, invariant culture and
strict calendar/time validation. Preserve Claude's existing clock/zone grammar,
including its DST rules. An explicit date is never rolled to tomorrow/next year
because a delayed sweep happens after it. Keep raw text and distinguish missing
reset, malformed reset and missing timezone evidence.

Add optional `ApiErrorTimeZoneId` at the end of `TranscriptPart`,
`RunnerTranscriptEvent` and corresponding server DTOs, persist it on
`TranscriptEntry`, and carry it through HTTP and phone-home ingestion, sidecars,
catch-up and the transcript API. Capture the provider process's effective timezone
at launch/tailer construction (honoring its launch `TZ` override if present); do not
recompute it on the receiving server. Give the normalizer/tailer an injectable zone
for tests. Codex errors with local dated reset text carry that zone; raw diagnostics
remain byte-for-byte unchanged within the existing bound. Old payloads deserialize
null; no lockstep deployment requirement is introduced.

Precedence is an explicit zone/offset in the diagnostic, then this captured zone.
Legacy messages with neither remain quota refusals but use the existing finite
fallback hold, explicitly reporting `reset timezone unavailable` and retaining the
printed reset. Never silently assume server-local or UTC. A known local runner may
supply historical zone evidence only when it was actually captured; current fleet
OS or runner name is not a timezone database.

For the replay oracle, explicitly supply UTC to the two Linux fixtures and
Europe/London to the Windows fixture: all three then resolve to
**2026-09-26T11:16:00Z**. This is a stated test-context assumption supported by the
one-hour difference, not a claim that the normalized historical records contain
timezone metadata. Also replay each without zone evidence and assert the honest
fallback. Test the Windows zone-ID equivalent and a receiver in a different zone.

Persist the parsed UTC reset separately on `ApiErrorRecovery.ResetAtUtc`; use the
existing `reset + 2 minutes` resume padding for `ModelAvailabilityHold.DisabledUntil`.
Thus the incident oracle expects `DisabledUntil=2026-09-26T11:18:00Z`, and the task
reason must name **both** reset and effective hold-until. Bump the wall parse version.
Absent/malformed resets use evidence time plus the configured model-cap fallback
(currently six hours); label it estimated, never a parsed reset. Retain date, UTC and
raw local wording in the reason/evidence rather than only `11:16`.

### D-3. Reuse model availability and its custody rules

Use `ModelAvailability.UpsertAutoDetectedAsync` for the session's resolved canonical
model alias, never `<synthetic>`, guessed aliases or an auto `*`. Existing reads then
refuse/hold new work on that `(kind, alias)`. Other providers remain available.
This card retains the documented per-model scope: it does not prove every model or
every account on a provider shares the same quota. Do not broaden it to a fleet-wide
or provider-wide auto hold.

Keep Manual source/deadline authoritative, including indefinite manual holds and
kind-wide manual holds. Automatic detection may refresh allowed evidence but must
not demote Manual or alter its deadline. Record the task's parsed reset even if a
manual hold lasts longer. Do not recreate operator-cleared holds from old evidence.
Unknown aliases still block the affected task and surface the evidence, with a clear
`model alias unresolved; hold not written` reason rather than inventing a key.

### D-4. Delegated Wall tasks become Blocked and await an explicit decision

For a current attributable API-error Wall on a Dispatched/Working task, atomically
persist `Status=Blocked`, a new appended failure code `SubscriptionQuotaExceeded`,
the quota/reset reason and one Blocked event. Keep `CompletedAt` null, preserve
Result/report evidence, worktree, session binding and ownership; the error is not a
successful report. Publish the task change and use the existing nonterminal caller
notification path. Repeated delivery or sweep of the same boundary is idempotent.

Place this branch after the existing later-UserPrompt protection and before
complexity wall reroute, retry defer and retained-capacity arms. This deliberately
changes the delegated-Wall contract documented by CARD-0412: quota blocking is
visible, and the orchestrator chooses a replacement provider explicitly. It applies
to the Claude/Grok equivalents as well as Codex. Do not kill/release a Blocked
conversation, silently requeue it, or move its card to a new column.

For these tasks resolve the API recovery as `QuotaBlocked`, set NextAttemptAt null,
and do not register a LiveSession capacity-resume wait. Supersede any prior wait
owned by this exact task/session through the existing capacity cancellation
primitive and clear retained-capacity flags. Audit compatibility reconciliation so
it cannot recreate a wait for this failure code. Hold expiry clears availability;
it does not answer or resume a Blocked task. Explicit requeue/cancel uses existing
ownership-safe operations. Reject direct Reply/Continue into an active quota hold
with its reason; after hold expiry an explicit answer may use the ordinary
Blocked-to-Working watermark path.

Taskless/standing-session Wall recovery keeps its paced reset-time behavior. Genuine
transient task failures keep their existing retry behavior. Reject changing all
Working capacity waits to Blocked indiscriminately or disabling global recovery.

### D-5. Repair existing Unknown rows before they can fire again

Resolve sibling text before classification. Add an idempotent, parse-versioned
repair for a latest still-relevant Unknown/Transient recovery whose persisted error
evidence now proves Wall. Repair even an UnknownExhausted row while its affected
task remains open. Do not repair canceled/finished tasks, superseded turns, already
cleared holds, changed hold lineage or unrelated errors. Preserve historical
AttemptCount/LastEnqueuedAt and the original evidence timestamp.

The sweep, `EnsureAdoptedAsync` and fire path must converge on the same adoption
logic. Current recovery adoption has transaction/unique-key protection, but
`FireOneAsync` has no shared enqueue-decision lock; do not assume the insert dedup
serializes delivery. Use a conditional durable recovery claim plus a final quota
check under `SessionMessageQueueService`'s existing per-session delivery lock, so a
previously due Unknown row cannot race out one last TransientPrompt after blocking.
Do not recursively acquire that queue lock from an already-locked flush/callback.
There is no recovery-to-queue-row foreign key today:
cancel only still-pending supervision retry messages positively attributable by
session, exact configured retry body plus task marker, and the recovery enqueue
timestamp/episode. Never remove arbitrary human prompts or already-sent history.
If a queue row cannot be safely attributed, retain it as evidence and prevent its
automatic delivery while this task's quota block is active using the queue gate.

No bulk rewrite of old canceled incident sessions and no live replay/backfill job
are part of deployment. The captured incidents are tests, not operator commands.

### D-6. Attention is a durable projection, not a new alert sink

Reuse the existing blocked-task attention surface (currently also used for
cost-ceiling and merge blocks). Identify the quota condition by the failure code,
not a substring of FailureReason. Give it a quota-specific headline, provider/model,
reset/hold-until evidence, task/session links and a stable condition key. Do not
describe it as waiting for an answer, advertise Continue during the hold, or create
duplicate attention/incident rows on each sweep. Existing `BlockedContextDto`
answerability flags can suppress Reply/Continue without inventing a question.

No new attention enum, page or client flow is required. An attention row disappears
when the task leaves this blocked condition, not merely because a timer elapsed.

### D-7. Platform, rollout and limits

On 2026-09-30 `/api/runner-defaults` returned revision 2; `/api/session-runners`
reported available Windows and Linux runners, with one Linux runner draining. This
is discovery evidence, not a dispatch pin. All ordinary checkpoints below are
cross-platform, no PTY/provider launch; use the current eligible default and omit
`-Runner`/`-Platform` unless the caller has a separate host requirement.

Use additive nullable columns and EF CLI-generated migration(s) only. Deploy server
support before expecting new runner timezone carriage; a lagging runner still gets
quota classification/blocking with an explicitly estimated hold. Rollback may leave
the additive columns in place; do not delete holds or blocked task evidence. Follow
the main-checkout activation runbook and verify `/api/version` on a deployment;
this Plan task does not restart or deploy anything.

## Slices

### S1 — preserve provider timezone evidence

Change `src/Antiphon.SessionRunner/TranscriptNormalizer.cs` (TranscriptPart),
`CodexTranscriptNormalizer.cs`, `CodexTranscriptTailer.cs`,
`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs`, and the other
tailer projection calls as necessary to preserve the additive member.
Carry it through `server/Application/Dtos/{SessionRunnerDtos,SessionTranscriptDto}.cs`,
`server/Infrastructure/Agents/SessionRunner/{RunnerContractMapper,SessionRunnerHttpClient}.cs`,
`server/Application/Services/{AgentSessionRuntime,AgentSessionService,CapacityEvidence}.cs`
and `server/Domain/Entities/TranscriptEntry.cs`. Include sidecar serialization and
old-payload tests, not just direct normalizer assertions. Add the recovery reset
column needed by S2 in the same CLI-generated migration if convenient.

Tests: new `CodexQuotaEvidenceTests` (8 cases), existing
`CodexTranscriptNormalizerTests` (16), new `QuotaEvidenceWireTests` (4). Pin genuine
error versus ordinary assistant text and stable re-tail identities. Synthetic raw
Codex task_complete fixtures are separate from the captured normalized records.

### S2 — classify, parse and adopt quota refusals

Change `server/Application/Services/{ApiErrorClassifier,UsageLimitWallParser,ApiErrorRecoveryService,CapacityEvidence}.cs`,
`server/Domain/Entities/ApiErrorRecovery.cs` and its EF mapping/migration. Reuse
`ApiErrorStubText` for sibling preference. Add the narrow evidence predicate in the
classifier/parser area, not a dependency from Domain to infrastructure.

Implement D-1/D-2/D-3 and the guarded repair in D-5. Keep taskless resumes and real
transient errors intact. Do not edit SubscriptionUsageEndpoints/Reader/Monitor to
make error handling depend on samples. New `ProviderQuotaRefusalTests` has 18
independent executions; keep the existing classifier/parser regression roster.

### S3 — block delegates, stop automatic retry delivery, expose attention

Change `server/Application/Services/{AgentTaskReplyService,ApiErrorRecoveryService,CapacityRecoveryService,AttentionService,BlockedContextBuilder}.cs`,
the appropriate answer/requeue guard in `AgentTaskService`, the existing session
queue delivery gate, and append the task failure code in
`server/Domain/Enums/AgentTaskEnums.cs`. Preserve later-prompt and settlement locks.
Do not use the terminal release primitive for this transition.

Add `ProviderQuotaRefusalAcceptanceTests` (18 executions listed below), using the
real normalizer/ingestion/recovery/settlement/attention boundaries with a fake runner
and isolated Postgres schema. Update the existing reply API-error tests whose
Working/retained/failure assertions are intentionally replaced by Blocked. Tests
about transient deferral must seed a genuine transient stub, not a quota wall.
Keep their assertions about result integrity, ownership, stale prompts and errors.
Update `ComplexityWallRerouteTests`' automatic wall-reroute expectations to the
explicit quota block, retaining direct explicit-requeue coverage; include
`CapacityRecoveryCompatibilityTests` to guard against reconstructing a canceled wait.

### S4 — reconcile documentation and rollout evidence

Update `docs/session-runtime-invariants.md` quota and CARD-0412 bullets to state the
delegated-versus-taskless distinction; update `docs/agent-card-lifecycle.md` blocked
quota semantics and the relevant `docs/ops-http.md` availability/task inspection
notes. Document AutoDetected spelling, parsed reset versus padded deadline, manual
precedence, old-runner fallback and explicit re-dispatch. Do not edit generated
`docs/cards/` files. Record actual checkpoint counts and any justified manifest
change in the Code report.

## Verification design

No builds or tests ran during Plan. Counts below are planned execution counts, not
passing results. Existing counts were derived from source `[Test]`/`[Arguments]`
rosters at the baseline; TRX executed names/counts are the Code verdict. New tests
must not hide the stated independent executions inside a single assertion loop.

### Coverage and exact new roster

| ID | Test class / executions | Required observable result |
|---|---|---|
| V-1 | `CodexQuotaEvidenceTests`, 8 | Separate cases: literal refusal; compact refusal; JSON-wrapped diagnostic; UTC capture; London capture; supplied Windows zone ID; ordinary AssistantText exclusion; deterministic re-tail/600-character bound. Verify class/text/TurnEnd/zone, not only helper output. |
| V-2 | `ProviderQuotaRefusalTests`, 18 | Eight classifier cases: structural class with absent text; straight apostrophe; curly apostrophe; compact prefix; Claude session wording; Claude named-model wording; Grok exhausted credits; Grok spending limit. Ten reset cases: Linux date; Windows date; ordinal variants; noon; midnight; explicit year across year-end; invalid calendar/time; missing zone; DST gap/fold policy; delayed replay preserves original date. Multi-vector assertions within one listed case still count once. |
| V-3 | `QuotaEvidenceWireTests`, 4 | HTTP event mapping; phone-home mapping; DB/API roundtrip; legacy payload/sidecar missing optional member. Changing or dropping a field must fail end-to-end assertions. |
| V-4 | `ProviderQuotaRefusalAcceptanceTests`, cases 1–3 | One independently reported test per incident session. Replay in original order with evidence-time clock and explicit fixture zones, creating an attributable task prompt/context in the harness. For every current refusal: AutoDetected hold on actual model, reset 11:16Z/hold-until 11:18Z, Blocked with both instants, null CompletedAt, no quota text stored as result, attention present, zero new transient prompts. Include the real compact rows in the Windows case. |
| V-5 | Same class, cases 4–6 | Claude session limit, Claude model cap, Grok credit wall through production normalization/adoption/settlement. Dated/clock reset is used when present; otherwise bounded fallback and honest reason. All Blocked with attention, no retry. No invented Grok reset format. |
| V-6 | Same class, cases 7–9 | Duplicate/restarted adoption produces one effective block/hold/event; manual timed hold remains Manual with unchanged deadline; manual indefinite hold remains indefinite. Scope every DB assertion to the created task/session/hold. |
| V-7 | Same class, cases 10–12 | Later UserPrompt prevents stale blocking; existing Unknown/UnknownExhausted latest quota reclassifies before firing; explicit operator clear does not resurrect old hold. |
| V-8 | Same class, cases 13–15 | Real transient still schedules the configured transient prompt; no subscription sample/monitor disabled still blocks quota and the actual create/start/dispatcher door honors its hold; racing sweep/turn-end/fire produces no new retry and one block. |
| V-9 | Same class, cases 16–18 | Missing alias or timezone blocks honestly without fabricated parsed reset/alias; pending owned retry suppressed while unrelated human message is preserved; hold clear/expiry keeps task Blocked and no capacity re-registration/auto-reroute, then explicit requeue/cancel removes attention and preserves ownership rules. |
| R-1 | Existing `ApiErrorClassifierTests` 16, `UsageLimitWallParserTests` 39 | 5xx/transport/unknown/auth/403 negative cases and Claude hour-only, DST, parse-failure, model-alias behavior. Add negative prose/wrapper assertions where needed without silently changing the roster floor. |
| R-2 | Existing `ApiErrorRecoveryServiceTests` 40 | Taskless resumes, evidence timestamps, sibling text, duplicate adoption, lineage, delayed correction, clear/manual precedence. Where a fixture has an actual delegated task, update only the intentionally changed quota outcome. |
| R-3 | Existing `ModelAvailabilityManualTests` 7 and `ModelAvailabilityDispatcherTests` 3 | Existing manual override, per-model versus kind-wide OR, expiry and actual dispatch gate. V-8 additionally proves the incident's Codex hold is wired into that path. |
| R-4 | Existing `SubscriptionUsageHttpTests` 6 | Read-only endpoint, no polling/enabling side effects, unchanged camel-case/null/privacy response. |
| R-5 | Existing `AgentTaskReplyIntegrationTests` 154 | Full touched class, including stale boundary, transcript attribution, caller delivery, Blocked ownership, API-error result integrity and intentionally updated quota expectations. No namespace-wide selection. |
| R-6 | Existing `ComplexityWallRerouteTests` 11, `CapacityRecoveryCompatibilityTests` 4 | Chain selection does not bypass the new quota block; explicit re-dispatch still works; compatibility reconciliation does not revive a quota-blocked task's automatic resume. |

Replay details: first consume the original prefix up to each refusal and run the
production observer before feeding any next recorded historical retry prompt. This
prevents later prompts from incorrectly superseding the evidence under test. Those
historical retries remain fixture inputs; count only newly created queue records as
output. Also replay the last refusal of each captured tail from a seeded marked
prompt, which tests the current-boundary path without historical retry input.
Run the clock at the original error time, advance past transient retry rungs and
past reset, then restart the services over the same isolated rows. Never move the
old date to today to make the assertion pass. A separate delayed-adoption assertion
expects the original historical deadline and immediate expiry, not a fresh long hold.

Test factory safety: use the established ProductionRunnerGuard/fake runner or
BridgeQueueHarness; never launch real Program against the production runner. Use
isolated schemas and serial integration rows for sweeps/hold mutations. Use
FakeTimeProvider instead of sleeps. Any new process-spawning harness must carry the
assembly-local ProcessSpawnLimit; this plan requires no provider process or PTY.

### Positive controls

These are intentional regression mutations for Mutation, not additional ordinary
Code runs. Each must fail an outcome assertion; build failure, discovery failure,
or zero executions is not a positive control. Record changed production line,
selected test, executed/failed counts and restoration SHA. Run through a build slot.

| PC | Change that must go red | Exact target |
|---|---|---|
| PC-1 | Remove only the `usage_limit_exceeded` arm while supplying no text | `ProviderQuotaRefusalTests.Structural_quota_without_text_is_wall` |
| PC-2 | Restore the old reset-lead-only grammar | `ProviderQuotaRefusalTests.Codex_dated_reset_uses_utc_evidence_zone` |
| PC-3 | Drop ApiErrorTimeZoneId at one production transport/persistence mapping | `QuotaEvidenceWireTests.Phone_home_zone_survives_ingestion_and_storage` |
| PC-4 | Substitute receiver-local/UTC for Windows provider-zone evidence | `ProviderQuotaRefusalAcceptanceTests.Replay_061581e5_blocks_at_shared_reset` |
| PC-5 | Route Wall back through Working defer/retained wait | `ProviderQuotaRefusalAcceptanceTests.Replay_faddd3e4_blocks_without_retry` |
| PC-6 | Remove the repair/fire quota guard on an already adopted Unknown | `ProviderQuotaRefusalAcceptanceTests.Existing_unknown_quota_is_reclassified_before_fire` |
| PC-7 | Permit an auto update to overwrite a Manual hold deadline/source | `ProviderQuotaRefusalAcceptanceTests.Manual_timed_hold_outranks_quota` |
| PC-8 | Exclude the persisted quota-blocked task from attention projection | `ProviderQuotaRefusalAcceptanceTests.Replay_5af622ce_has_quota_attention` |

TestDesign/Code should use these exact method names for the named cases above so
method-scoped Mutation can execute them without rediscovery. For the remaining
cases use descriptive names and preserve the execution totals. An existing
test named `Codex_TurnEnd_text_without_AssistantText_still_parses_session_limit`
currently seeds Claude/Fable-shaped text/class; it is not incident acceptance.

### Checkpoints

Each row owns one isolated build and one exact TUnit filter. All rows are ordinary
required scope. `Serial=true` prevents checkpoint rows from sharing sweep/hold state
or overlapping another build; test-level sweep isolation still belongs to the
fixtures/NotInParallel. Five builds are intentional: rows close different slice
groups or isolate the large reply regression class. Expected total is **344**
executions (24 + 73 + 22 + 71 + 154), subject only to explicitly reported source-roster
changes, with zero failed/skipped. A floor is not permission to omit a named case.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c719-runner/` | quota-runner | `/*/*/(CodexTranscriptNormalizerTests*)\|(CodexQuotaEvidenceTests*)/*` | V-1 | Both classes; 16 existing + 8 new = 24, 0 failed/skipped | 24 | 6 | true |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c719-parsing/` | quota-parsing | `/*/*/(ApiErrorClassifierTests*)\|(UsageLimitWallParserTests*)\|(ProviderQuotaRefusalTests*)/*` | V-2, R-1 | Three classes; 16 + 39 + 18 = 73, 0 failed/skipped | 73 | 8 | true |
| CP-3 | S1-S3 | `tests/Antiphon.Tests -> bin-c719-acceptance/` | quota-acceptance | `/*/*/(QuotaEvidenceWireTests*)\|(ProviderQuotaRefusalAcceptanceTests*)/*` | V-3–V-9 | Both classes; 4 + 18 = 22, including each of the three incident tests, 0 failed/skipped | 22 | 12 | true |
| CP-4 | all | `tests/Antiphon.Tests -> bin-c719-recovery/` | quota-recovery-regression | `/*/*/(ApiErrorRecoveryServiceTests*)\|(ModelAvailabilityManualTests*)\|(ModelAvailabilityDispatcherTests*)\|(SubscriptionUsageHttpTests*)\|(ComplexityWallRerouteTests*)\|(CapacityRecoveryCompatibilityTests*)/*` | R-2–R-4, R-6 | Six classes; 40 + 7 + 3 + 6 + 11 + 4 = 71, 0 failed/skipped | 71 | 14 | true |
| CP-5 | all | `tests/Antiphon.Tests -> bin-c719-reply/` | quota-reply-regression | `/*/*/AgentTaskReplyIntegrationTests/*` | R-5 | All 137 methods / 154 argument-expanded executions, 0 failed/skipped | 154 | 15 | true |

Run one checkpoint-tool invocation per committed slice group, for example:

```powershell
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-30-card-0719-usage-limit-quota-refusal-plan.md --rows CP-1
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-30-card-0719-usage-limit-quota-refusal-plan.md --rows CP-2
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-30-card-0719-usage-limit-quota-refusal-plan.md --rows CP-3
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-30-card-0719-usage-limit-quota-refusal-plan.md --rows CP-4,CP-5
```

Use the repository build-slot wrapper for a launcher build if required; checkpoint
row drivers acquire their own slots. Continue `wait` while exit is 75; exit 4 is a
slot timeout to report, never permission to run unleased. Report each CP's commit,
build result, filter, actual executed/passed/failed/skipped counts, TRX and reruns.
Any extra test/build requires a stated reason (migration CLI build included).
Do not run `dotnet test`, a namespace-wide suite, live transcript injection or a
provider quota-exhaustion experiment. No client/dist or headed E2E work is required.

### Cost

Ordinary verification floor: **55 minutes** (6 + 8 + 12 + 14 + 15), plus current
host-slot waits. Allow roughly 90–150 minutes for implementation/migration/docs and
fixture authoring; reserve Mutation separately. Counts are not time estimates.

## Acceptance and handoff

Code is complete when every checkpoint passes and the three captured incidents
each prove a parsed AutoDetected hold, Blocked task carrying reset and hold-until,
zero newly delivered transient-retry prompts and a quota attention item. The Windows
compact variant must be part of the real incident replay, not only a synthetic
regex unit test. Manual precedence, unknown timezone behavior, genuine transient
recovery and explicit provider re-dispatch remain covered.

No operator decision is needed to start under D-1–D-7. The UTC/London fixture zones
are explicit test inputs, not a fleet configuration change. Do not claim that old
zone-less records alone prove a UTC instant, that the compact wrapper caused the
miss, or that merely changing Unknown to Wall satisfies this card.
