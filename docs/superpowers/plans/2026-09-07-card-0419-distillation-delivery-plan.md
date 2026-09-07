# CARD-0419: durable full reports and applied completion delivery

Plan, 2026-09-07. Ready for separate TestDesign; no implementation, live-model
dispatch, runtime setting change or Apply acceptance run is included in this stage.

Basis: the [landed investigation](../../investigations/2026-09-07-card-0419-distillation-delivery.md)
at `0df28561`, checked against planning checkout `3d0a3dd2`. The live CARD-0419,
CARD-0392 and CARD-0330 were read through `scripts/card.ps1 get`. CARD-0419 currently
has `bug,delegation` labels and no complexity label; treat this as medium for stage
selection because it crosses settlement, filesystem custody and queue replacement.

## Outcome and scope

Complete the existing distiller's file-backed delivery contract. A qualifying
report gets a durable, gitignored canonical copy before completion-note admission.
An eligible successful Apply replaces the existing parent note with its original
header, cheap-model summary, required deliverable metadata and an absolute path to
that copy. The raw task result remains authoritative.

The feature remains optional: rejection, deadline expiry and unavailable work keep
the original raw/excerpt note. This explicitly qualifies CARD-0419's current
unconditional "ONLY a short summary" acceptance. A model cannot be required both
to preserve all load-bearing information and always fit a short budget. The human
reviewer must accept this qualification before closure; hiding a rejected report
or silently loosening its gates is not the default.

CARD-0392 owns interrupted distillation intent and ledger reconciliation. It is a
release dependency, not another implementation slice hidden in this card. Code
and a scoped happy-path canary can proceed independently; broad Apply rollout and
CARD-0419 closure wait for its recovery evidence under D-6.

## Ground truth

| Assumption or requirement | Current evidence | Planned consequence |
|---|---|---|
| The distillation may only appear in a task drawer. | `AgentTaskReplyService.DeliverToParentAsync` queues the actual parent completion. `SessionMessageQueueService.TryApplyDistillationAsync` replaces that exact row's `Body`; normal queue delivery types it. | Reuse this path. Do not create a second summarizer, reply lane or completion note. |
| Turning on Apply is the whole fix. | Investigation inherits the session's Shadow finding. `OutputDistillation.PointerLine` always gives the task API/status command, even when `ResultFilePath` exists. | Add usable file pointers; no mode change in Plan or Code. |
| A report around 1,000 tokens already has a file. | `ResolveSpillFileAsync` only persists above the transport ceiling: 3,000 conservative or 14,400 modern characters. | Persist useful-to-distill reports independently of transport spill. A 5,500-character modern-profile report is the primary regression case. |
| All size thresholds mean "long." | Input eligibility is inclusive 1,200–20,000 .NET string characters; the output gate is 1,500 characters / 0.6 ratio. The v2 prompt requests 1,200 characters / under half. Transport also has UTF-8 byte ceilings. | Separate input usefulness, model work cap, output quality and transport safety; D-1 fixes the meaning and proposed default. |
| `.antiphon/task-<shortid>.md` is durable enough. | The current path is relative to `task.WorkingDirectory`; linked worktree removal can erase it. An existing file is accepted without checking that it equals `task.Result`. | Canonical report storage outside transient worktrees, identified by full task ID and exact-content hash. |
| Keeping the header preserves every delivery artifact. | `BuildCompletionNote` puts generated `--- deliverable ---` / attach markers after the body. They need not occur in the raw input seen by the distiller. | Recompose this deterministic block from settled task metadata as well as retaining `NoteHeader`; do not ask the model to invent it. |
| CARD-0432 S0–S2 fixed the restart defect. | `OutputDistillationQueue` is still an in-memory channel; its worker has no boot scan; ledger insertion is in request cleanup and can be lost. The completion worker recovers pending raw delivery only. | Explicit CARD-0392 dependency, coordinated with CARD-0432 S3/S4. |
| Existing Apply tests prove operator acceptance. | Unit/DB tests and a fake-adapter race test exist. No real Apply parent-transcript run is established. | Require the live evidence described below, after a scoped human-approved test decision. |

Source entry points: `server/Application/Services/AgentTaskReplyService.cs`
(`ResultFilePath` assignment, `DeliverToParentAsync`, `ResolveSpillFileAsync`),
`DelegationReportFormatter.cs` (`BuildCompletionNote`, `FitReport`),
`OutputDistillationService.cs` (`ShouldRequest`, `RequestCoreAsync`),
`OutputDistillation.cs` (`PointerLine`), `SessionMessageQueueService.cs`
(`TryApplyDistillationAsync`, final composed-body spill), and
`server/Application/Settings/DelegationSettings.cs`.

## Decisions

### D-1. Separate four size policies; use an explicit character approximation

Propose changing only the shipped `DistillMinChars` default from **1,200 to 4,000**.
This is a rough 1,000-token reading preference using four characters per token as
an illustration, not an exact tokenizer or a promise across languages, code and
Unicode. Keep the existing setting name and count .NET UTF-16 `string.Length`
before prompt scrubbing, consistently in the persistence and distillation paths.
Existing explicit overrides continue to win; do not rewrite live settings or
historical ledger rows. Record the effective minimum in acceptance evidence and
segment Shadow comparisons at this default change rather than mixing cohorts.

| Policy | Proposed boundary | Meaning |
|---|---|---|
| Useful to summarize | `raw.Length >= DistillMinChars`, default 4,000 | Worth optional model work and a canonical report copy for the target completion path. |
| Maximum model input | `raw.Length <= DistillMaxRawChars`, unchanged 20,000 inclusive | Work/cost cap. Larger reports still get a file, but skip the model. |
| Allowed summary | Existing gate: at most 1,500 chars and 0.6 raw ratio, plus all current content/anchor gates | Applies to summary text. Header, pointer and deterministic deliverable block are additional. The v2 prompt's stricter request remains unchanged. |
| Safe terminal delivery | Existing profile reply ceilings (3,000 / 14,400 chars), and final composed UTF-8 byte guard | Determines raw excerpting and last-resort queue spill, never model eligibility. |

Use the configured minimum for artifact eligibility even when it is overridden
back to 1,200. At exactly 4,000 and 20,000 characters the model remains eligible;
20,001 skips it but retains the full file. A 3,500-character conservative report
can need transport spill while being too short for a model turn. A 5,500-character
modern report needs the new file without needing transport spill. These are
intentional combinations, not settings conflicts.

Reject a single shared threshold: it would couple terminal safety to reading
preferences. Reject adding a tokenizer dependency or raising the model input cap
in this card. Reject describing the existing 1,200-character minimum as 1,000
tokens. Retaining 1,200 everywhere was considered; 4,000 better expresses the
operator's stated preference and avoids optional turns on already modest reports.

### D-2. Persist the authoritative report independently of a model attempt

Target all settled, non-specialist, `ReplyTo=Session` Succeeded/Failed reports at
or above the configured usefulness minimum. Also retain the existing report-spill
trigger for any report above its transport ceiling, including other reply/status
paths. Do not make persistence depend on Apply, model availability, successful
admission, an eventual gate pass, or `DistillMaxRawChars`. Shadow and disabled mode
still preserve the canonical report when this target predicate is true; their
completion bodies keep the existing raw/excerpt policy and disabled stays no-spend.

Introduce an application filesystem seam `IAgentReportStore` with its implementation
in Infrastructure. Return a typed success/path or failure reason; do not add a
second report entity. Keep `AgentTask.ResultFilePath` as the persisted pointer and
update its XML contract. Existing DTOs already carry it; no schema migration is
required by this design.

Default location for a supported Git workspace:

```text
<canonical-main-checkout>\.antiphon\reports\<full-task-guid>\<raw-utf8-sha256>.md
```

Resolve the repository identity with the existing Git workspace service, starting
from persisted `RepoPath` and falling back to the task working directory.
`GetWorkspaceInfoAsync` already maps ordinary linked worktrees through the common
Git directory to the main checkout. Do not treat its documented bare/odd-layout
fallback to the local toplevel as proof of durability. Verify that the chosen root
is the primary persistent checkout, outside `WorktreePath`, before publishing it.
Never choose the parent agent's unrelated repository or the first AllowedRoots
entry just because either is accessible.

For non-Git or unsupported layouts, support an optional absolute
`Delegation:ReportStorageRoot` naming a persistent operator-owned report directory.
It must be outside transient worktrees and temporary directories. If it lies in a
Git checkout, enforce the same ignore/tracked-file check. Without a usable root,
take the explicit file-unavailable fallback. No new AllowedRoots entry or access
grant is implied by this setting. Windows configuration examples use backslashes.

Before any report bytes are written in a repository, ensure the destination is
ignored and not already tracked. Antiphon's existing `.antiphon/` rule satisfies
this; for another repository, a narrow `/.antiphon/reports/` entry in its local
Git exclude file is permissible. Do not commit report contents, edit that repo's
tracked `.gitignore` automatically, or silently use a tracked destination. Keep
all path components generated from validated roots, a GUID and a digest, never
from report prose.

Store **exactly the settled `task.Result`**, UTF-8 without a wrapper or rewritten
line endings; verify decoded content against the raw result. The filename hash is
SHA-256 of those exact UTF-8 bytes. It is distinct from `DelegationNoteDigest`,
which normalizes line endings/trailing whitespace and must remain unchanged for
queue identity and poll suppression. Write a unique
temporary sibling, flush/close, then atomically publish the final name. Retries
reuse a file only after content verification. A stale or partial file is repaired
atomically or rejected, never trusted solely because it exists. Do not overwrite
the delegate-authored legacy `.antiphon/task-<shortid>.md`. Historical settled rows
and already-sent notes are not scanned or rewritten by this card.

The canonical file archives the final report, not every artifact transitively
referenced by it. A short final response pointing at a larger delegate-authored
file remains a short report; do not read arbitrary referenced files into the
distiller. Preserve its path anchors and the existing deliverable bundle contract.
For legacy/non-target spill cases that intentionally reuse a delegate-authored
detail file, preserve that behavior and its tests rather than relabeling that
different content as the canonical raw-result copy.

Run root resolution and file publication within a small cooperative I/O budget
(initially two seconds total, linked to settlement cancellation), before saving
`ResultFilePath` and enqueueing the parent note. Do no model work here and leave no
unobserved I/O against a disposed scope. File failure must not abort an otherwise
valid settlement: retain `Result`, persist a null canonical pointer and continue
with the existing API recovery route. Record task ID, phase, duration and reason,
not report contents. Validate the existing 1,000-character column limit before
saving a path. Do not add file I/O inside the queue's delivery/DB locks or reset
CARD-0432's admitted request deadline to compensate for storage delay.

"Durable" means survives service restart and delegate worktree cleanup while the
main checkout/configured report root is retained. It is not a backup against disk
loss or deletion of that root. No TTL or automatic artifact deletion in this card;
worktree cleanup must not remove this directory. The retained task row remains
the recovery source if the file later disappears. A future retention policy must
coordinate report-file and task-record lifetimes; generic scratch cleanup must
exclude `reports/` while those references are retained.

### D-3. Apply uses the file pointer without changing delivery ownership

Prefer a verified usable `ResultFilePath`:

```text
[task <id> done] <the existing header, including next/deliverable/warnings>

<gate-approved cheap-model summary>

Full report: <absolute canonical report path>
<existing deterministic deliverable block, when present>
```

Use literal readable path text, including paths containing spaces/Unicode. This
is a report reference, never an `[[attach:]]` command for the raw report. Keep the
task API/status command as the alternative when no usable file is available; do
not append it instead of the file on the normal successful path.

Put pointer selection/formatting in shared completion formatting code used by
Apply and raw excerpt delivery. The application service validates file usability
outside the queue lock within the remaining budget. Under the existing lock and
row locks, recheck source result digest and selected path identity; if the path
changed or is unusable, compose the API fallback. No disk write or repair occurs
under that lock. A file removed after validation is still recoverable through the
task API; filesystem existence and database commit cannot be one transaction.

Reuse `TryApplyDistillationAsync` and all current claim checks: captured Apply
mode, exact source/note/raw digest, Delegation origin, Pending status, zero delivery
attempts, no matching full-report poll, and strictly before the original deadline.
Replace body and clear hold in the same guarded update. Keep `NoteHeader`,
`ContentDigest`, source identity, persisted stage/handoff and raw `Result` intact.

Reuse the existing deliverable formatter and `DeliverableBundleService` metadata
to reconstruct the generated attachment block. Keep required `next:`/`handoff:`
and raw path/count/commit anchors under the current gates (including CARD-0431's
landed corrections). Do not edit the output-distiller bundle, prompt append,
contract version, gate thresholds or live specialist session in this work.

The summary limit excludes deterministic metadata; unusually large headers or
attachment lists may still trigger the final UTF-8 composed-message spill guard.
Preserve that guard and the LF/bracketed-paste/separate-Enter delivery contract.
Do not truncate metadata or declare a whole-note 1,500-character guarantee.

### D-4. Explicit fallback matrix

| Condition | Parent delivery | File/model behavior |
|---|---|---|
| Successful, timely eligible Apply | Original header + accepted summary + usable file path + deliverable block | Canonical raw copy already exists; normal positive acceptance requires this path to work. |
| File storage/unavailability alone | Accepted summary + API/status recovery pointer if Apply otherwise wins | Raw remains on the task. This is degraded file delivery, not a passing file-pointer acceptance run. |
| Shadow or disabled | Existing raw body below transport ceiling, otherwise explicit head/tail excerpt | Target reports still persist; Shadow only records summaries; disabled invokes no model. |
| Below usefulness minimum | Raw, or transport excerpt if the independent ceiling requires it | No model turn; preserve SkippedShort accounting when enabled. |
| Above 20,000 input cap | Raw or explicit excerpt according to transport policy, with file pointer where usable | Persist full report; SkippedLong, no model turn or recursive chunking. |
| Empty/failed/unavailable/held/busy specialist, queue refusal, gate rejection | Original raw/excerpt note and existing deterministic metadata | Keep canonical file. Release hold through existing bounded cleanup, or allow finite hold expiry. Never relabel an excerpt as a model summary. |
| Expired request, late output, delivery already attempted, note sent/canceled or identity mismatch | Existing queue-owned body; no replacement or second completion | Preserve deadline/outcome semantics; never kill an active specialist merely because optional work expired. |
| Matching full-report poll | Existing polled-note suppression/shrink behavior | Do not reintroduce either raw or summary; preserve poll-vs-Apply serialization. |
| Restart loses distillation request | Persisted raw/excerpt note becomes eligible when its original hold expires | The file survives, but intent/ledger may be missing until CARD-0392 lands. No restart-safety claim from this card alone. |

Direct filesystem reads do **not** stamp `LastPolledResultHash` or `FullReadAt`.
Keep API parent polls as the existing tracked read action, with explicit feedback
for quality assessment. Document `FullReadAt` as API-read evidence, not total full
report readership after adding file pointers. Do not add file watchers, telemetry
beacons or automatic reads just to preserve that metric.

### D-5. Human rollout is separate from code landing

Ship and test deterministic code with the default mode still Shadow. Preserve
CARD-0330's [orchestration-loop section 10](../../orchestration-loop.md#10-distiller-prompt-review-card-0330)
governance: human review/merge, a week of ledger review, and an explicit human
Apply decision. Existing zero/missing rows cannot establish a successful Shadow
cohort; interpret them with CARD-0392's known gap and record effective settings.

After code lands, the operator may authorize an explicitly scoped Apply canary
on an isolated real stack with its own database, random runner port, scratch
specialist identity and dedicated transcript-observable parent session. Reuse the
established isolated-runner/provider setup; do not connect a test host to production
17204 or a live messaging broker. No production mode flip is needed for this
canary. A production Apply trial, if chosen instead, requires a separately scoped
human rollout decision and approved restart procedure; it is not implicitly
authorized by this plan.

The canary must use the real configured cheap specialist and real delegate
settlement/parent delivery. No fake model, prefilled `DistilledResult`, manual
queue-body replacement, or verification-disabled adapter satisfies live acceptance.
Known model holds/authentication refusals are reported, not bypassed or silently
rerouted. Preserve the 45-second deadline and gates even if the test rejects or
times out. Such a run is evidence of fallback, not a successful Apply run.

Rollback after an approved trial is the operator restoring Shadow (or disabling
distillation for a no-model fallback), verified in effective settings; code rollback
uses the reviewed revert. Preserve existing canonical files and the raw task rows.
Already admitted requests retain their captured mode/deadline: changing the global
setting is not cancellation of in-flight Apply work. Let that bounded cohort settle
and verify pending-note disposition before declaring the trial stopped.

### D-6. CARD-0392 is an explicit release dependency

Keep CARD-0392 open. Its owner must design durable request/ledger identity, early
intent insertion, bounded boot reconciliation and a recorded duplicate-digest
outcome. Coordinate ledger identity/migrations with CARD-0432 S4's eventual-cost
projection; CARD-0432 S3's runtime action recovery is not a replacement for this
work. Do not add a competing scan/channel/ledger scheme under CARD-0419.

Required dependency handoff:

- Preserve the original captured mode, request deadline, source ID, raw digest and
  queued-note ID. Reconciliation must not restart a 45-second Apply budget.
- A sent/attempted/polled/expired note can never be resent or retrospectively
  replaced. Reconcile interrupted accounting or a still-valid original request,
  with bounded work; any ledger-only catch-up model spend needs an explicit policy.
- Crash windows before drain, during the specialist run and before final ledger
  save must leave a durable disposition after recovery, without duplicate model
  runs/cost counting or duplicate parent completions.
- Recovered work consumes CARD-0419's persisted file pointer; do not recreate
  report storage in transient worktrees or infer a file exists from ledger state.

TestDesign/Code may proceed before CARD-0392. General Apply rollout and CARD-0419
closure require its reviewed restart tests/evidence and an accepted fallback
contract. If the operator wants a narrower early release with known restart loss,
that is an explicit decision recorded on the card, not a claim that this dependency
was fixed by CARD-0432 S0–S2. This Plan stage changes no card state or description;
the caller should carry this dependency and acceptance qualification into the next
card revision/rollout decision.

## Implementation slices

### S1. Canonical report store and threshold contract

Files: `server/Application/Settings/DelegationSettings.cs`, new
`server/Application/Interfaces/IAgentReportStore.cs`, new
`server/Infrastructure/Files/AgentReportStore.cs`, composition in
`server/Program.cs`, `server/Application/Services/AgentTaskReplyService.cs`, and
`server/Domain/Entities/AgentTask.cs` documentation. Reuse Git workspace I/O;
make only a narrow addition to that service if durable-root validation requires it.

Implement D-1/D-2, including effective-minimum independence from mode, max-input
and transport ceiling, canonical exact content, ignore-before-write, atomic
publication, idempotence, path limit and recoverable I/O failure. Preserve the
legacy delegate-authored spill semantics outside the new target path. Update
test DI through `DelegationTestServices` where appropriate, not copied graphs.

Tests: new `AgentReportStoreTests`; extend `AgentTaskReplyIntegrationTests`,
`OutputDistillationProducerTests` and settings/skip cases in
`OutputDistillationTests`. Root/worktree tests use disposable Git repositories,
never the live checkout as a removal target.

### S2. File-first replacement and metadata preservation

Files: `OutputDistillation.cs`, `OutputDistillationService.cs`,
`SessionMessageQueueService.cs`, `DelegationReportFormatter.cs` and the existing
deliverable metadata formatter. Keep file validation out of queue/DB locks and
make successful Apply and raw excerpt formatting agree about usable pointers.

Tests: extend `OutputDistillationTests`, `OutputDistillationApplyRaceTests`,
`DelegationUnitTests` and `OutputDistillationProducerTests`; preserve relevant
`PolledCompletionNoteShrinkTests`, `SessionMessageQueueSpillTests`,
`OutputDistillationDeadlineTests`, `OutputDistillationCleanupTests`,
`DistillationEndpointTests` and `OutputDistillationGateTests` coverage. Use actual
queue replacement and settlement integration, not only a PointerLine unit test.

### S3. Operational contract, evidence and approved live acceptance

Update the living owner `docs/orchestration-loop.md` section 10 with the four
threshold meanings, durable file location/lifetime, fallbacks, file-read metric
limitation and rollout dependency. Update `.gitignore`'s scratch comment if needed
to distinguish retained reports from disposable briefs; keep report files ignored.
Do not change the distiller prompt or weekly schedule.

TestDesign supplies executable canary setup/cleanup instructions using the existing
isolated stack and the evidence checklist below. Code supplies deterministic
verification first. After landing and the human's scoped test approval, the live
run writes a tracked evidence report at
`docs/investigations/<date>-card-0419-apply-acceptance.md`; raw reports, transcripts
and fixture outputs remain under durable gitignored storage. The evidence report
contains identities, hashes, measurements and sanitized snippets, not whole raw
reports or unrelated conversation contents. Failure to run or pass the live test
is explicitly "live acceptance pending/failed," never Done by code review alone.

## Acceptance requirements for TestDesign

TestDesign is a separate stage. It must add the executable `## Verification design`
with V/R/PC IDs, exact class/method filters, fixture ownership and red/green controls.
The following requirements constrain that design; they are not an executed test
record or permission to fold the stage into Code.

1. **Boundary matrix:** below/equal/above 4,000 and 20,000, both transport profiles,
   an explicit legacy 1,200 override, Unicode character-vs-byte distinctions,
   Shadow/Apply/disabled, ineligible specialist/blocked cases, and the 3,500-char
   conservative/5,500-char modern contrasting cases. Keep terminal byte limits
   unchanged. Verify skip outcomes and no model attempt in out-of-range cases.
2. **Storage integrity/lifetime:** real temporary repo and linked worktree, source
   working directory below a repo root, full-ID collision resistance, spaces and
   Unicode paths, exact UTF-8 round trip including reports that share a normalized
   note digest but differ in line endings/trailing whitespace, ignore-before-write, tracked destination
   refusal, retries/partial files, configured non-Git root and absent/unsupported
   root. Remove only the fixture's worktree, reconstruct services, then read the
   same canonical file and compare it to the settled raw result. Exercise storage
   failure and cancellation without losing settlement or parking a raw note.
3. **Actual parent completion:** through settlement producer and real queue method,
   assert accepted summary + `ResultFilePath`, no raw payload, unchanged header/raw
   digest/result, stage and generated attach markers retained, and no new note.
   Test file absence/path change separately; API fallback is expected there.
4. **Fallback/races:** deterministic rejected/empty/failed/held/busy/refused/expired
   outcomes, original absolute deadline including equality, late completion,
   SendNow-first vs Apply-first, matching full-report poll and composed-body spill.
   Assert the delivered raw/excerpt or suppressed body, not just a ledger label.
   Preserve CARD-0432's finite hold, owned flush and standing-process ownership.
5. **Positive controls:** reverting to transport-only persistence must fail the
   5,500-character modern case; restoring API-only PointerLine must fail actual
   applied-body assertions; writing under the transient cwd must fail the
   post-cleanup read; dropping the generated deliverable block must fail its
   marker assertion. Existing claim/deadline/poll regression tests must remain
   sensitive to bypassing the corresponding guard. Build errors or zero tests are
   not acceptable red evidence.

Run scoped TUnit classes through `dotnet run`, never `dotnet test`, with a fixed
trailing-slash isolated output such as:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c419/ -- --treenode-filter "/*/*/OutputDistillationProducerTests/*" --report-trx --report-trx-filename card-0419-producer.trx
```

The next stage must name the other exact filters after writing its cases. Verify
nonzero executed method manifests and pass/fail counts from fresh TRX files.
Follow [testing-and-build.md](../../testing-and-build.md): shared test-Postgres
assertions scoped to fixture IDs, correct fake clocks, a process-spawn limiter for
new Git/process tests, and sequential test assemblies. No full-suite or client
build is required solely for these backend/doc changes; if the canary uses the
browser E2E fixture, rebuild `client/dist` per that fixture's guard.

### Mandatory real Apply evidence

The successful run must record all of the following in one correlated evidence
record. A populated `DistilledResult`, `Applied` row, healthy server or terminal
screen alone is insufficient.

- Code commit actually loaded by the test server, server/runner identities and
  ports, effective mode **Apply**, effective thresholds/deadline, real cheap
  specialist kind/model/bundle stamp, scoped approval and test time window.
- A real non-specialist delegate's final report between 4,000 and 14,400 characters
  (target about 5,500) delivered to a real transcript-observable parent on the
  modern profile. Use benign report contents with distinct middle-body text and
  a small number of meaningful anchors, so the existing gates can legitimately
  accept it. The delegate does not pre-spill the report; this proves the missing
  below-transport-ceiling storage path. Include a stage handoff/deliverable case.
- Source task ID, parent session ID, queued-message ID, raw digest/character and
  UTF-8 byte counts, persisted file path and file hash, Distill task ID, ledger
  identity/outcome, mode, request/deadline/decision times and observed cost. A
  rejected/late run cannot be presented as the successful sample.
- Pull the parent transcript via
  `GET /api/sessions/{parentId}/transcript?since=<baseline>` using the documented
  server front door. Capture the confirming **UserPrompt**, its sequence and
  timestamp. It must contain the accepted summary, preserved header/metadata and
  the complete canonical path, and omit the distinctive raw middle-body text and
  full raw payload. Require the queue's transcript-confirmed delivery verdict,
  not a screen-only degraded verdict.
- Only **after** this confirmation, have the parent read the file through its
  actual allowed filesystem access and return a length/hash acknowledgement;
  compare with the saved raw task result. Do not use `delegate.ps1 -Status` or a
  parent-authenticated task GET before delivery: that can stamp the full-report
  read marker and suppress the very note under test. Collect pre-delivery
  identities through fixture-owned telemetry/read-only DB access that does not
  invoke the parent poll behavior.
- Demonstrate a separate real long-report fallback (for example >20,000 characters,
  which skips the model) with parent UserPrompt evidence and a readable canonical
  file. Rejection/expiry and race fallbacks also require the deterministic coverage
  above; do not change global gates, holds or timeout settings to force them.
- Confirm the canonical file survives removal of the canary's disposable delegate
  worktree and service restart/reconstruction, while ordinary completion delivery
  remains once-only. This proves artifact persistence; CARD-0392 separately proves
  interrupted distillation recovery. Clean up only test-owned sessions/worktrees
  through established ownership paths; retain the evidence files.

When a run fails, record the exact gate/outcome or missing evidence and rerun only
after a concrete correction or an available approved window. Do not keep spending
on identical failures or widen the budget until one happens to pass.

## Completion and handoff

Plan completion: this artifact is committed and pushed. Next: **TestDesign**.
No build/test or live acceptance result is claimed by this planning document.

Implementation completion requires S1/S2 and deterministic verification; landing
those changes does not close CARD-0419. Closure requires S3's real successful Apply
evidence, verified fallbacks/file lifetime, the human's acceptance of D-4 and rollout
decision under CARD-0330, and CARD-0392's release dependency resolved or explicitly
accepted as a narrower release on the card. Keep those pending items visible in
the Code/Review/Deploy reports rather than converting them into passing checkboxes.
