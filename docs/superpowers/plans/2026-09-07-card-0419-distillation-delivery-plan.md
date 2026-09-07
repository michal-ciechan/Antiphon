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

## Verification design

TestDesign appended 2026-09-07 against `c00a3a8e`. D-1 through D-6 above remain
the fix design. This section specifies tests for Code to implement and execute;
none of the proposed tests or live probes has run in this TestDesign stage.
The next implementation stage is **Code**. The earlier Plan-stage handoff above
is historical, not an instruction to dispatch TestDesign again.

### Fixtures and execution boundaries

**F-1: report data and filesystem.** Add `AgentReportStoreTests` and
`OutputDistillationPolicyTests` under `tests/Antiphon.Tests/`, and shared helpers
under `TestHelpers/`. Filesystem tests use the real Infrastructure store, actual
files and a disposable Git repository with a committed seed file, plus a linked
worktree and a nested working directory. Use a unique root under the checkout's
gitignored `.antiphon/test-output/card-0419/<guid>/`; this allows a valid persistent
configured root without placing it under the OS temp directory. Every delete or
worktree removal must resolve within that fixture root and exclude the repository
being built. No test touches the user's main `.antiphon/reports/` or Git excludes.
Use the assembly-local `ProcessSpawnLimit` on Git/process-spawning classes.

Supply `ReportOfLength(n, taskId)` with a known first outcome, a distinctive
middle sentence made of words (not a hex/URL/path anchor), a final caveat and the
correct report token. Adjust padding so the **settled Result** has the requested
UTF-16 length; assert that precondition before asserting any threshold decision.
No leading/trailing padding that `Trim` could silently discard. Store-byte tests
call the store with explicit raw strings to exercise CRLF, LF, trailing spaces,
non-ASCII and surrogate pairs without the settlement extractor masking them.

Use an exact UTF-8 SHA-256 computed independently in the fixture for the filename
and content oracle. The normalized `DelegationNoteDigest` is a separate assertion.
For partial-publication tests, expose a per-store-instance internal barrier after
the temporary file is closed and before publish; null in production. It may pause
the real path, but must not replace the write/rename with a mock. Gate failures at
filesystem/root-resolution I/O seams; do not simulate an I/O failure by returning
a fabricated successful path. Permission-denied cases use an injected I/O refusal
rather than changing Windows ACLs on shared directories.

**F-2: settlement to parent delivery.** Add
`tests/Antiphon.Tests/Application/OutputDistillationDeliveryTests.cs`. Extend the
existing `AgentTaskSettlementRaceTests.BuildHarness` / `DelegationTestServices`
graph, real PostgreSQL and `AgentTaskReplyService.OnTurnEndAsync`. Register the
real report store, queue, output-distillation hosted worker and owned completion
flush worker. Seed a marked source turn, not a pre-settled result. The specialist
is the only scripted model boundary: observe its real Distill task creation and
settle that task with a chosen result using the existing distillation helper.
Do not seed a successful source `ResultFilePath` or replace the parent's body in
the test; that would bypass the defect.

Register a concrete `PtyDeliveryProfile` with the explicit backend override and
controlled runner capability response, following `PtyDeliveryCeilingsTests`.
Assert the effective reply ceiling is 3,000 or 14,400 before executing that row.
The modern integration row needs the redistributable available on the Windows
verification machine; silently falling back to inbox does not cover modern.
Pure policy tests also call `DelegationSettings.CeilingsFor` for both backends so
those boundaries are covered without native binaries. Do not emulate modern by
only increasing `ReplyInlineMaxChars` while leaving the actual queue on inbox.

Use `FakeAgentProtocolAdapter` with verification enabled. Seed the parent's
transcript baseline and an idle TurnEnd. Its submit callback inserts the **actual
submitted body**, never the expected body, as a UserPrompt through the established
bridge fixture. Assert `Delivered` with complete matching UserPrompt text. For a
final queue spill, read the actual inbox file and assert the complete inner
message there as well. The callback is a deterministic transport simulation;
only F-5 below is live acceptance. Capture the raw pending note before allowing
the specialist to finish, then assert the same note ID after Apply/fallback.

New delivery tests are `[Category("Integration")]`, global `[NotInParallel]`
because they drive workers/sweeps, and `[ParallelLimiter<ProcessSpawnLimit>]`.
All queries/assertions scope by fixture session/task/slug. No new hand-built git
DI graph. Retain old helpers' explicit 1,200 override for legacy tests that need
it; the new policy/producer tests must exercise the new default. Existing
`OutputDistillationProducerTests` uses 1,400-character samples: set an explicit
legacy minimum there or lengthen those samples when preserving busy/deadline
coverage. Do not accidentally turn a busy-path test into a SkippedShort test.

**F-3: time, claims and failures.** Extend `OutputDistillationApplyRaceTests`
with barriers at file validation, source-row lock and guarded body update.
Use `TaskCompletionSource` with asynchronous continuations and release every
barrier in `finally`; await workers and operations before disposing their scopes.
Use the existing poll-aware clock pump for the specialist, an offset-over-real
clock for queue polling, and controlled `FakeTimeProvider` advances for pure
deadline checks. Never advance fake time while ordinary DB I/O is pending just
to make a test finish (`OutputDistillationCleanupTests` pins this).

For ordinary PostgreSQL lock-expiry tests use system time and a fixture-owned
transaction/interceptor with a one-second test request deadline. For the separate
SQL-predicate control, invoke Apply directly with a `FakeTimeProvider` initialized
to current UTC and `AutoAdvanceAmount=TimeSpan.FromTicks(1)`. Do not run queue poll
loops in that cell. Pause immediately before executing the UPDATE, wait until
PostgreSQL time is past D, then release: application UTC and its timer budget are
still before D, so only the SQL predicate rejects the write. This distinguishes
SQL protection from an already-canceled request token. These are test requests,
not a change to shipped/live 45 seconds. A wall-clock watchdog detects broken
fixtures; its timeout is not the expected red assertion.

For the post-lock application check, use an offset-over-real clock: pause after
row reads and change its offset past D while the real timer/DB clock remain
before D. Assert **no UPDATE is attempted**, in addition to unchanged body. This
makes that check observable even if a later expiry check would roll back a write.

**F-4: evidence oracle and isolation controls.** Add
`tests/Antiphon.E2E/OutputDistillationCanaryGuardTests.cs` (non-explicit, no model
launch) and an evidence validator used by the real canary. Validate captured
identities/content/delivery evidence, not just one success boolean. Negative
fixtures remove or corrupt one item of an otherwise complete evidence record.
These prove the acceptance harness cannot certify known bad evidence; they do
not substitute for generating that evidence with the live system.

**F-5: genuinely live isolated stack.** Add
`tests/Antiphon.E2E/OutputDistillationApplyCanaryTests.cs` with the single method
`Real_apply_and_long_fallback_reach_parent_and_files_survive_cleanup`. Mark it
`[Explicit]`, `[Category("Headed")]`, `[Category("HeadedCanary")]`,
`[NotInParallel("Headed")]` and use a one-wide E2E-local `ProcessSpawnLimit`
(add `Fixtures/ProcessSpawnLimit.cs` if still absent). Require both
`ANTIPHON_HEADED_TESTS=1` and the new dedicated opt-in
`ANTIPHON_DISTILLER_APPLY_CANARY=1`. Without them ordinary suite runs skip without
launching a model. Once explicitly requested, failed preflight is a failed/pending
acceptance result, not a passing skipped canary.

Read a non-secret approval record from
`ANTIPHON_DISTILLER_CANARY_APPROVAL_FILE`, written by the rollout operator after
the scoped human decision. Required fields: `approvalReference` (card revision or
task/report reference), `approvedUtc`, `expiresUtc`, `expectedSourceRole` (`Review`),
`expectedDistillerKind` (`ClaudeCode`), `expectedDistillerModelAlias` (the approved
current Low alias), and `availabilityCheckedUtc` / `availabilityVerdict` (`allowed`).
Require current time inside the approved window and the availability check no
more than five minutes old before launching. This file records an existing
decision; the test never creates its own approval or treats the opt-in flags as
evidence of production rollout approval. Compare actual resolved model identity
to the record before the source task; if a hold/refusal emerges later, stop.

Extend `AntiphonAppFixture` with a narrow, internal canary-options seam, applied
after its ordinary configuration defaults and before host startup. The default
fixture continues to disable the distiller. Reuse its real Kestrel host,
PostgreSQL testcontainer and `IsolatedSessionRunner`; retain access to the same
container, runner and report root for a server-host-only restart. The new options
must not allow callers to replace the owned DB, runner address or manifest root.
This is test-harness work, not a new production Apply override API.

Current `DelegationSequencingE2ETests.TestSessionRunner` seeds sessions and manually
submits the dequeued note through `ClaudeHarness`. **Do not use that substitute**
for this canary. Use normal `AgentControlService` launches, real runner client,
hosted dispatch/settlement/distillation and `SessionMessageQueueService` delivery.
No `TakeNoteForAsync`, fake CLI/API, transcript injection, direct PTY work-body
typing, manual `DistilledResult` stamps or artificial Applied ledger rows.

### Proves it works now

All method names below not already present are the required new test names.
Parameterized rows are part of the named item, not optional examples. Run every
row and record its outcome; V-23 is explicitly a live probe.

| ID | Behavior and layer | Test/command | Required assertions |
|---|---|---|---|
| V-1 | Defaults and units; unit | `OutputDistillationPolicyTests.Defaults_separate_usefulness_from_transport` | Default minimum 4,000, input cap 20,000, mode Shadow, wait 45 seconds; output gates 1,500 / 0.6; conservative and modern reply ceilings 3,000 / 14,400 and their existing byte envelopes unchanged. |
| V-2 | Independent boundaries; integration | `OutputDistillationDeliveryTests.Report_boundaries` | Both profiles at lengths 2,999, 3,000, 3,001, 3,500, 3,999, 4,000, 4,001, 5,500, 14,399, 14,400, 14,401, 19,999, 20,000, 20,001. Check the F-6 truth table below, exact persisted raw length, file content, model-attempt count and actual note/body disposition. |
| V-3 | Persistence independent of mode/status; integration | `OutputDistillationDeliveryTests.Mode_and_terminal_status_preserve_report` | A 5,500-char modern report for Succeeded/Failed crossed with enabled-Shadow, enabled-Apply, disabled-Shadow, disabled-Apply. All get a canonical file. Enabled Apply alone replaces; enabled Shadow records without replacement; disabled produces no Distill task/ledger/hold. Failed status/header never becomes success. |
| V-4 | Explicit override and UTF-16 accounting; integration/unit | `OutputDistillationDeliveryTests.Legacy_minimum_and_unicode_boundaries`; `OutputDistillationPolicyTests.Unicode_length_is_not_utf8_bytes_or_tokens` | Minimum override 1,200 at 1,199/1,200/1,201 on modern. Non-ASCII/surrogate-pair samples exactly 3,999/4,000 UTF-16 units with larger UTF-8 size use character eligibility. No tokenizer or byte-based eligibility; final byte spill remains independent. |
| V-5 | Ineligible reports and legacy spill; integration/unit | `OutputDistillationDeliveryTests.Ineligible_and_legacy_reports_keep_their_contract`; `OutputDistillationPolicyTests.Nonterminal_tasks_are_ineligible` | Specialist Check/Distill/Diagnose, Blocked and ReplyTo None at 5,500 below modern ceiling: no new canonical-usefulness/model path. Queued/Dispatched/Working fail the model eligibility predicate. A non-target oversized report still uses legacy backstop and preserves a delegate-authored file; a short final pointer is never recursively expanded. ReplyTo has only None/Session; no invented Channel enum. |
| V-6 | Exact content identity; filesystem integration | `AgentReportStoreTests.Exact_content_and_full_ids_own_distinct_files` | CRLF/LF/trailing-space variants sharing a normalized note digest have distinct exact-byte hashes; non-ASCII round-trips without BOM/wrapper. Two GUIDs with the same first eight characters do not collide. Source report prose cannot supply destination path components. |
| V-7 | Root custody; filesystem integration | `AgentReportStoreTests.Root_selection_requires_a_durable_owned_location` | Main, nested main, linked worktree and nested worktree map to the same canonical main root; parent repo/first unrelated AllowedRoot cannot win. Non-Git with valid explicit root succeeds; missing root, relative root, temp override, source-worktree override and unsupported Git layout without override return unavailable. Supported explicit override rescues the unsupported layout. |
| V-8 | Never publish tracked report bytes; filesystem integration | `AgentReportStoreTests.Ignore_and_tracking_are_checked_before_writing` | Existing ignore; missing ignore with local exclude creation; tracked destination even with a matching ignore; inaccessible exclude; and an ignore negation. Verify `git check-ignore --no-index`, `git ls-files` and report write observations: either ignored/untracked before the first report byte, or no report bytes written. Tracked `.gitignore` is untouched. |
| V-9 | Atomic/idempotent publication; filesystem integration | `AgentReportStoreTests.Publication_exposes_only_a_complete_verified_report` | Pause before publish: final path absent or previous complete content, no published ResultFilePath. Release: exact file becomes visible. Identical retry, simultaneous same-content retry and truncated/corrupt preexisting destination repair/refusal never return bad bytes. Cancellation before publish leaves no usable partial canonical pointer. Legacy author file stays byte-identical. |
| V-10 | Lifetime beyond worktree/provider; integration | `OutputDistillationDeliveryTests.Canonical_file_survives_worktree_removal_and_provider_recreation` | Settle from an actual linked worktree; dispose provider; remove that fixture worktree through Git; rebuild provider against same DB/main root; reload ResultFilePath and read exact report. No re-settlement or test-side rewrite is allowed to restore the file. |
| V-11 | Storage failure does not lose settlement; integration | `OutputDistillationDeliveryTests.Storage_failures_leave_a_recoverable_settled_report` | Root lookup/write/rename/readback IOException, access denied, path >1,000 chars and cooperative I/O-budget expiry: raw Result and terminal state persist; no bad path; actual parent fallback uses API when a pointer is needed. No model work under storage/queue locks; log metadata, not report text. Host cancellation propagates; retry with a fresh scope settles once without a partial pointer. |
| V-12 | Real producer reaches Apply replacement; integration | `OutputDistillationDeliveryTests.Modern_report_below_transport_limit_applies_with_canonical_file` | Default 5,500-char report, no pre-spill. One source settlement, one canonical file, one request/Distill task, one original note ID. Accepted summary and exact path reach the simulated parent's complete UserPrompt; raw middle/payload absent. Result, NoteHeader, ContentDigest and source identity unchanged; hold cleared; Applied ledger corresponds to this request. |
| V-13 | Only usable pointers; integration | `OutputDistillationDeliveryTests.Unusable_file_uses_api_recovery`; `OutputDistillationApplyRaceTests.Path_changed_after_validation_cannot_publish_stale_pointer` | Null, deleted, unreadable and corrupted canonical files give API fallback. Pause after validation, change persisted ResultFilePath on an otherwise identical source, then Apply: stale path absent, API present. Raw/source/header remain unchanged. No repair or awaited file read inside the queue/row lock. |
| V-14 | Load-bearing metadata survives; integration | `OutputDistillationDeliveryTests.Apply_preserves_handoff_warnings_and_generated_deliverables` | Seed a stage report with exact next/handoff and artifact metadata, plus caller warning, git/scope header bits, two real attachable fixture files. At least one generated marker is absent from raw input. Applied body retains original header/warning, accepted next/handoff, deterministic markers exactly once and file pointer; it never marks the raw report as an attachment. Include Failed and empty-deliverable variants. |
| V-15 | Honest fallbacks through delivery; integration | `OutputDistillationDeliveryTests.Fallback_delivers_original_raw_or_marked_excerpt` | Both profiles, 5,500-char target, for missing-path, missing-next and missing-handoff rejection, oversized-summary rejection, empty, failed, unavailable, held, backlog-full, request-queue-full/closed/missing, expired-before-run and run-timeout. Raw/excerpt inner note remains unchanged apart from existing delivery/poll transformations; canonical file readable; outcome/reason correct; no hidden raw truncation, no second note, no active specialist kill. |
| V-16 | Application claim identity; integration | existing `OutputDistillationApplyRaceTests.Apply_eligibility_matrix`, extended with file-bearing source and `missing-source`/`missing-note` | Eligible row applies. Wrong note/source/digest/raw result/origin, Pending with attempts >0, Sent, Canceled, matching poll, captured Shadow and expiry all refuse replacement; inspect unrelated note as well as target. Exact source raw unchanged. |
| V-17 | Competing operations serialize; integration | existing `Apply_and_SendNow_preserve_one_complete_body`, `Apply_owns_the_delivery_session_lock_until_commit`; new `File_validation_does_not_own_delivery_lock`; new `Parent_poll_and_apply_have_one_ordered_winner` in `OutputDistillationApplyRaceTests` | Both SendNow orders give one complete delivered body. Block file validation: SendNow can claim/send without waiting for disk. For both poll orders use real `AgentTaskService.GetAsync(..., pollingSessionId: parent)` and separate scopes: poll-first forbids Apply; Apply-first may subsequently be shrunk by the existing poll policy before delivery. No stale summary restoration or duplicate send. |
| V-18 | Deadline remains absolute through new I/O; integration | `OutputDistillationApplyRaceTests.File_validation_consumes_original_deadline`, `Equality_and_postlock_expiry_never_apply`, `Database_clock_rejects_an_expired_body_update`; existing `Expired_lock_wait_never_applies`, `Expired_database_write_never_applies` | Consume request time in file validation and lock wait; equality is expired. Gate after source/note reads, expire application UTC, release: no UPDATE attempted. For the separate DB predicate row, hold the UPDATE past DB time D while application UTC and timer budget remain before D: zero changed rows/unchanged body. In all cases no fresh deadline or second completion; use F-3. |
| V-19 | Finite raw fallback after lost wakeup; integration | `OutputDistillationDeliveryTests.Provider_recreation_releases_expired_raw_note_once` | Persist note+file+finite hold, stop before optional drain, reconstruct provider/flush worker against same DB and advance past original D. Actual parent receives raw/excerpt once without a new TurnEnd or test-side SendNow. File survives. Do not assert missing ledger as a desired invariant or claim interrupted distillation intent recovered; that is CARD-0392. |
| V-20 | Last composed byte guard and metadata; integration | `OutputDistillationDeliveryTests.Composed_summary_and_metadata_obey_utf8_spill_guard` | Add large deterministic metadata and multi-byte text so complete composed bodies are at C-1/C/C+1 UTF-8 bytes, with C the actual caller-selected queue spill ceiling. At C+1 the typed pointer leads to the full composed body retaining summary, canonical report path and attachment block. At/below C inline behavior remains. Also cover a same-root batch crossing C; check each note/file association, no lost second completion. |
| V-21 | Read metrics and API compatibility; integration | `OutputDistillationDeliveryTests.File_read_does_not_claim_a_parent_api_poll`; existing `DistillationEndpointTests.full_read_at_is_set_only_by_a_parent_poll_after_SentAt` | Reading file alone changes neither LastPolledResultHash nor FullReadAt. A subsequent actual parent API/service poll returns authoritative raw Result and ResultFilePath and stamps the existing API metric. A different session does not stamp it. |
| V-22 | Canary cannot certify synthetic/unsafe success; unit in E2E | `OutputDistillationCanaryGuardTests.Configuration_refuses_unowned_resources_before_start`; `Evidence_rejects_missing_or_wrong_delivery_proof` | Invalid opt-in, absent/expired approval, stale/refused availability, model mismatch, non-owned DB/runner/manifest root, production runner 17204 or live broker configuration refuses before launch. Evidence negatives: wrong parent/task/note/digest/path, only screen/AssistantText, no or clipped UserPrompt, raw middle present, API-only pointer, rejected/late/Shadow ledger, wrong cheap model, missing parent read acknowledgement or failed teardown. No negative is accepted. |
| V-23 | Mandatory live acceptance; live probe | `OutputDistillationApplyCanaryTests.Real_apply_and_long_fallback_reach_parent_and_files_survive_cleanup` using the command below | Execute the full F-5 procedure below, including genuine real-model Apply success, genuine >20,000-char fallback, parent file reads, worktree cleanup and host restart. Skipped, unavailable, rejected or incomplete evidence leaves live acceptance pending/failed. |

**F-6: V-2 expected outcomes.** With enabled Apply and an otherwise valid source,
`4,000 <= L <= 20,000` permits one specialist attempt. `L < 4,000` records
SkippedShort and `L > 20,000` records SkippedLong, with no attempt. With no
delegate-authored file, a canonical target file is required at `L >= 4,000`;
transport backstop also requires a file whenever `L > T`, where T is the
effective reply ceiling. Thus a conservative 3,500-char report has a spill file
and no model attempt, whereas modern 5,500 has a canonical file and a model
attempt even though no raw excerpt would be required. Equality `L == T` stays
inline in `FitReport`; `L == 4,000` persists; `L == 20,000` remains model-eligible.
For below-minimum transport-only files, retain the legacy contract rather than
asserting the canonical-target filename. For skipped/rejected work, assert the
raw body at `L <= T`, explicit excerpt at `L > T`, then separately account for
the final composed queue byte guard. Do not equate a queue inbox file with the
canonical full-report file.

**F-7: fallback mechanics for V-15.** Produce a passing summary first as fixture
data, with 120–1,500 characters and all required anchors, and assert the current
gate passes it. For over-compression remove only one required path/next/handoff
anchor; for under-compression use 1,501 characters retaining anchors. Script empty
or failed specialist results separately. Availability, queue refusal and backlog
use their actual seams as in `OutputDistillationAdmissionTests` and ProducerTests.
Expiry-before-run creates no new model task; timeout-after-dispatch leaves the
standing owner intact and follows its existing outcome. Seed 5,500-char reports,
not the old 1,400-char samples, so every intended branch is reached. Capture body
at admission; after bounded cleanup/finite hold expiry, drive only the real flush
worker and compare the submitted inner body to that snapshot. Also read the
canonical file. The applied-summary fixture must never appear in fallback input.

### Real isolated Apply canary procedure (V-23)

Code implements this recipe in the named F-5 method; it is not a request to run
it during TestDesign or permission to enable production Apply. After code lands,
the human authorizes this scoped canary under D-5. The command requires no
credential argument and is independent of the operator's production mode.

1. Before starting a host, validate the opt-ins, Windows/Claude availability,
   modern ConPTY prerequisites, Docker availability and a fixture-owned persistent
   root. Use the approved wrapper-managed Claude authentication path from
   `docs/agent-credentials.md`; do not print/copy credential stores, introduce a
   stub model endpoint or silently select another provider. Any known hold/quota
   refusal must be carried into the isolated test's availability preflight rather
   than evaded by its empty DB. Record a non-secret availability verdict and the
   explicit approval reference, not account tokens. A fresh isolated DB is not
   proof that the operator's model has no hold.
2. Create `<checkout>\.antiphon\acceptance\card-0419\<run-guid>\` for retained
   evidence and its own Git `repo\` with a harmless seed commit. Put descendant
   worktrees under this run's `worktrees\`, outside that main repo. No remote is
   configured, so source delegates cannot push to the real repository. Include
   `docs/canary-evidence.md` and two benign source-report fixture inputs in the
   seed commit; the canonical `.antiphon/reports/` tree starts absent.
3. Start a new PostgreSQL testcontainer and the fixture's own runner with a new
   random loopback port and per-run manifest root. Start the real app on a random
   Kestrel port. Canary-only settings: distiller enabled, mode Apply, minimum
   4,000, max input 20,000, wait 45, existing output gates, unique specialist slug
   and cwd; CheckInterpreter and Diagnose disabled; GitHub and ChannelBridge
   disabled. Replace external messaging producer/consumer with refusing test
   adapters and set broker configuration to a non-live loopback endpoint so an
   unexpected send fails. Do not replace the real runner, distiller, gate, queue,
   transcript runtime or hosted completion workers. `UsePrebuiltFrontend=false`
   and `UseMockExecutor=false`; no UI browser/Playwright or client build is needed.
   The existing deliverable renderer may use its owned headless browser; keep its
   normal failure/source-copy fallback and include its cleanup in fixture teardown.
4. Before any agent starts, set this fixture's `Delegation.ApiBaseUrl` to its
   Kestrel URL and its allowed root to its own canary repo; this changes only
   the isolated host. Assert effective DB identity, runner URL/manifest root,
   mode/thresholds, registered real runner client and absence of external
   channels. Call `PtyDeliveryProfile.RefreshAsync` and require modern/14,400,
   confirmed by the owned runner's capabilities. Record server assembly location,
   informational version/MVID and file hash, runner binary hash/PID/start time
   and candidate Git commit. Checking source HEAD or health alone is insufficient.
5. Start one named real Claude parent through normal agent control with a unique
   slug and the canary main repo as cwd. Give it a short canary-only instruction
   to acknowledge incoming completions without quoting the report, polling task
   status, dispatching follow-ups or reading files until instructed. Submit the
   initial instruction through `/api/sessions/{id}/messages`; wait for an actual
   initial UserPrompt and completed idle turn. This establishes a bound transcript
   before the completion under test. Provision/start the actual cheap specialist
   with `OutputDistillerProvisioner.EnsureAsync`, then wait for its real ready/idle
   state before creating the source task; warming is outside any report deadline.
   Assert its resolved kind/Low-tier model alias and unchanged output-distiller
   bundle stamp, deny-tools contract and separate session identity.
6. Create one real `Worker`/`Review` stage task in a new worktree, using
   `AgentTaskService.CreateAsync` in fixture DI with
   `Caller(Task: null, SessionId: parentId, WorkingDirectory: canaryRepo)`.
   This is a fixture-local named-session caller, not a seeded fake parent or a
   production capability. Request ClaudeCode explicitly, use the normal Review
   tier, and assert the caller session made the created task `ReplyTo=Session`;
   ReplyTo is derived by the service, not a CreateAgentTaskRequest parameter.
   Let the normal dispatcher create/run the delegate.
   Its narrow brief says to read the benign fixture, emit its complete approximately
   5,500-character report as the final assistant response with the task's real
   report token, retain the next/handoff/artifact lines, and perform no git writes,
   additional dispatches or report pre-spill. The input fixture is not the output
   artifact: assert the legacy task-report file and canonical report tree did not
   exist for that task before settlement. Use outcome/caveat prose, a small anchor
   set, and `artifact: docs/canary-evidence.md`; the Review task exercises stage
   metadata without requiring a code change or an external push.
7. Observe task/queue/ledger rows through fresh **read-only fixture DB scopes**
   with AsNoTracking. Never call a parent-authenticated source-task GET or
   `delegate.ps1 -Status` before parent delivery. Let the real specialist return
   its own result and the real gates decide. Require the actual settled source
   length to be 4,000–14,400, actual Applied ledger with a linked real Distill task,
   one original source completion note and exact canonical file. If the source
   shortened the report, if a gate rejected it or if Apply missed D, fail with that
   reason; do not stamp results, alter prompts/gates or extend D to rescue the run.
8. Capture parent transcript baseline and pull
   `/api/sessions/{parentId}/transcript?since=<baseline>` from the fixture server.
   Locate a complete UserPrompt carrying this task's completion header, accepted
   summary, exact file path and generated deliverable block. Assert the distinctive
   raw middle sentence and full raw report are absent from this completion prompt,
   and exactly one matching completion prompt exists. Require queue verdict
   Delivered or LateConfirmed **with** that complete matching transcript record;
   neither enum alone is enough. Correlate note ID/source ID/raw digest and
   request/deadline/decision times. Compare the exact UTF-8 file hash with Result.
9. After that evidence is captured, queue a parent instruction to read the exact
   canonical file through its normal tools and return only its UTF-16 length and
   SHA-256. The instruction names the path but does **not** reveal the expected
   length/hash. It may use `[IO.File]::ReadAllText` and `Get-FileHash`; inspect the
   actual tool result plus the final acknowledgement. Compare with independently
   computed values. Check no parent API-poll stamp arose from this file read.
   Escape paths as data through the queue request, not shell interpolation.
10. Repeat sequentially for one real source report about 21,000 characters,
    using a separate fixture input and task. The source brief explicitly requests
    the full final response to exercise the transport backstop, overriding the
    normal advice to pre-spill. Require actual length >20,000, SkippedLong, no new
    Distill task for this source, a canonical full file, and a parent UserPrompt
    containing the marked raw excerpt and usable path. Validate head/tail and
    omitted-middle behavior independently. Have the parent read/acknowledge this
    file too. This live fallback cannot substitute for the successful Apply case.
11. Export evidence outside both source worktrees. After both tasks and parent
    reads finish, stop their owned delegate sessions through normal lifecycle
    services, establish that no process owns those cwds, then remove only those
    two canary worktrees through Git. Check canonical files still read from the
    main canary repo. Restart **only the test server host**, retaining its actual
    container, runner, files and database. Do not call fixture Dispose or restart
    the production AppHost. Reload both source rows and parent transcript through
    the new host, read the same files, and observe at least two completion-scan
    intervals: no second completion prompt or new Distill task may appear. All
    work was already settled before restart; interrupted intent is CARD-0392's test.
12. In `finally`, first save task/note/ledger and relevant transcript evidence,
    then stop the named test parent/specialist through agent control and use the
    existing isolated-runner snapshot/kill/census teardown for any remaining owned
    hosts. Verify PID/start-time identity before any process fallback. Dispose
    the fixture host/container/runner while retaining the acceptance directory.
    Unreachable cleanup or a leaked test process prevents a green canary; report
    ownership/evidence for recovery without touching foreign sessions.

The live evidence JSON must include schema version, approval reference, commit
and loaded binary identities, settings, root/host/runner ownership, source/parent/
note/Distill/ledger IDs, raw and summary lengths/bytes, raw normalized digest,
exact file hashes/paths, deadline/decision times, original header, expected
preserved anchors, confirming UserPrompt sequence/time/text, parent file-read
tool result/ack, before/after restart counts, cost observations and teardown
verdict. Keep full raw/transcript evidence gitignored; publish only the correlated
sanitized record in `docs/investigations/<date>-card-0419-apply-acceptance.md`.
Do not claim all full reads are observable or that file persistence fixes lost
distillation requests. Record CARD-0392 dependency status separately.

Approved live invocation after Code implements the named test:

```powershell
$env:ANTIPHON_HEADED_TESTS = '1'
$env:ANTIPHON_DISTILLER_APPLY_CANARY = '1'
$env:ANTIPHON_DISTILLER_CANARY_APPROVAL_FILE = 'C:\src\Antiphon\.antiphon\acceptance\card-0419\approval.json'
dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-c419-live/ -- --treenode-filter "/*/*/OutputDistillationApplyCanaryTests/Real_apply_and_long_fallback_reach_parent_and_files_survive_cleanup" --report-trx --report-trx-filename card-0419-live.trx
```

Use the actual approved record path for another checkout. Restore the prior
values of those three test-only environment variables afterward.
The fixture has an outer 20-minute observation budget including startup and two
source tasks, and reserved teardown time. It never changes the distiller's
45-second request budget. At most two source tasks and one ordinary distillation
attempt are intended per run; a failed success case stops before the second
source. Parent boot/ack/file-read turns also use the real model. No automatic
retry loop; a new run needs a concrete corrected cause or an available approved
window. Record actual usage/cost and all failed attempts, not only the green one.

### Guards the regression

| ID | Future regression | Caught by |
|---|---|---|
| R-1 | File creation drifts back to transport-only or Apply-only | V-2/V-3/V-12: modern 5,500 and Shadow/disabled file assertions fail. |
| R-2 | Cwd/short-ID/existence-only storage loses or substitutes reports | V-6 through V-10: independent exact bytes, collisions, publish barrier and post-removal read fail. |
| R-3 | An applied result exists only in the ledger/UI, uses API instead of file, or drops generated metadata | V-12/V-14/V-23 assert submitted/completely transcribed text and generated markers, not a task DTO projection. |
| R-4 | Failure hides the raw report or an expired result changes an attempted note | V-15 through V-19 plus existing OutputDistillationAdmission/Deadline/Cleanup/Dispatch tests preserve actual raw delivery, original deadline, cancel eligibility and one standing owner. |
| R-5 | Parent poll loses its serialization/suppression or filesystem reads fake the metric | V-17/V-21 plus all `PolledCompletionNoteShrinkTests` and `DistillationEndpointTests` check real read behavior and queued output. |
| R-6 | Formatter or batching changes drop text at the terminal boundary | V-20 plus `SessionMessageQueueSpillTests`, `PtyDeliveryCeilingsTests` and the selected delivery-verification tests below distinguish whole message, report file and inbox file. |
| R-7 | Gates or bundle are weakened to make acceptance green | All `OutputDistillationGateTests`, existing `InstructionBundleTests.the_output_distiller_contract_forwards_to_its_bundle_with_the_pinned_invariants` and `the_distill_reporting_contract_never_offers_blocked_and_keeps_the_handoff_anchor`, plus V-15's actual refusal. |
| R-8 | A live harness bypasses the production delivery path or touches the shared stack | V-22, F-5 configuration assertions and V-23's parent UserPrompt/file-read/census evidence refuse that run. |

Retain these existing raw-spill/formatting cases with explicit legacy/non-target
fixtures when their assertions depend on the old author-file path:
`AgentTaskReplyIntegrationTests.an_oversized_report_is_backstopped_to_a_file_by_the_server`,
`a_spill_file_the_delegate_wrote_itself_is_used_as_is`,
`a_five_kilobyte_report_spills_and_the_caller_gets_a_marked_excerpt`,
`the_completion_note_is_delivered_into_the_parents_session`;
`DelegationUnitTests.a_report_within_the_ceiling_is_forwarded_whole`,
`an_oversized_report_keeps_its_beginning_and_its_end`,
`an_excerpt_points_at_the_spill_file_when_the_delegate_wrote_one`,
`an_excerpt_falls_back_to_the_api_url_when_there_is_no_spill_file`, and
`a_degenerate_excerpt_budget_never_produces_more_text_than_it_was_given`.

Required transcript regressions in `SessionMessageQueueDeliveryVerificationTests`:
`A_swallowed_first_enter_is_re_pressed_and_the_delivery_confirms`,
`A_stale_record_alone_never_produces_delivered`,
`Screen_output_advancing_without_a_record_is_no_longer_delivered`,
`A_clipped_prefix_parks_as_truncated_not_sent`,
`A_complete_long_body_still_marks_sent`,
`Late_confirm_does_not_promote_a_truncated_body_to_sent`.
Run `DeliverableBundleServiceTests` when extracting/reusing its metadata formatter,
and `DelegationTestServicesTests`/`DelegationHarnessCensusTests` if their DI helper
changes. No broad namespace filter is needed.

### Positive controls

Run controls only in a disposable verification worktree containing the candidate
implementation commit, never by mutating shared `master` while the live stack can
build from it. Each row specifies a narrow one-line defect; where alternatives
are enumerated, each is a separate subcontrol. Report the exact diff/location,
named failing assertion, restored diff and green result. New-store method names
may follow implementation naming, but the predicate being broken and expected
observable failure below are fixed. Do not weaken assertions or fixtures to make
a mutation appear effective. Release barriers even after expected failure.

| ID | One-line defect to introduce | Expected red |
|---|---|---|
| PC-1 | In the new artifact-eligibility predicate, remove the usefulness arm so only `L > replyCeiling` persists. | V-12: 5,500-char modern canonical path/file absent. |
| PC-2 | Add `mode == Apply && enabled` to canonical persistence eligibility. | V-3 Shadow and disabled rows lose the file. |
| PC-3 | Change lower comparison `L >= min` to `L > min`; separately change maximum eligibility `L <= max` to `L < max` (a/b). | V-2 exact 4,000 loses eligibility/persistence; exact 20,000 incorrectly skips model work. |
| PC-4 | Compare UTF-8 byte count instead of `string.Length` at the minimum gate. | V-4 3,999-unit multi-byte report incorrectly creates model work. |
| PC-5 | Resolve the destination from `task.WorkingDirectory` instead of canonical main root. | V-10: saved path unreadable after fixture worktree removal. |
| PC-6 | Use `Short(task.Id)` instead of the full GUID as the task directory component. | V-6: two deliberately colliding short IDs share a destination. |
| PC-7 | Use `DelegationNoteDigest.Compute(raw)` for filename identity instead of exact UTF-8 SHA-256. | V-6: whitespace/newline variants have the same destination rather than distinct immutable copies. |
| PC-8 | Remove the tracked-destination refusal; separately force ignore/exclude failure to be treated as success (a/b). | V-8: tracked/nonignored fixture observes a report-byte write that must never happen. |
| PC-9 | Remove configured-root containment/persistence validation (apply separately to worktree, temp and relative-root guards). | V-7: each explicitly unsafe override returns/publishes a path instead of unavailable. |
| PC-10 | Return the existing destination immediately on `File.Exists`, bypassing content verification. | V-9: corrupt/truncated preexisting file is returned as a valid report. |
| PC-11 | Direct the temporary write to the final filename instead of the unique sibling. | V-9 pre-publish barrier: incomplete/unpublished final content is visible. |
| PC-12 | Pass `CancellationToken.None` to the report I/O seam instead of the shared write-budget token; separately omit the >1,000 path check (a/b). | V-11 token-observing storage gate fails cancellation/expiry propagation; path case fails to settle with a null pointer. Fixture watchdog exceptions do not qualify as red. |
| PC-13 | Restore API-only `OutputDistillation.PointerLine` for a valid canonical path. | V-12: actual applied parent body lacks the canonical file path. |
| PC-14 | Trust any nonempty ResultFilePath without usability validation; separately omit the path-identity recheck after validation (a/b). | V-13 missing/corrupt file advertises a bad path; path-change race publishes the stale verified path. |
| PC-15 | Omit the deterministic deliverable-block append from applied composition. | V-14: generated marker absent from raw input disappears from delivered output. |
| PC-16 | Set `source.Result = distilled` in the successful application branch before saving. | V-12/V-14 authoritative raw-result assertion fails. |
| PC-17 | Bypass `if (!gate.Passed)` in `OutputDistillationService`. | V-15 missing-anchor/oversized-summary rows submit the rejected summary instead of the saved raw/excerpt. |
| PC-18 | Omit source/note/digest/origin eligibility checks **one predicate per subcontrol**: SourceTaskId, ContentDigest, recomputed raw digest, Delegation origin. | V-16 corresponding wrong-source/digest/report/origin row is replaced. Wrong-note row must additionally leave the unrelated note untouched. |
| PC-19 | Remove Pending-status guard; separately remove zero-attempt guard; separately remove captured-Apply-mode guard (a/b/c). | V-16 Sent/Canceled, attempted, or Shadow rows are wrongly replaced. |
| PC-20 | Remove the matching full-report poll guard; separately remove the source-row `FOR UPDATE` while retaining the poll check (a/b). | V-17 poll-first row or V-16 polled row replaces an already-read report; row-lock subcontrol forces a poll commit between the application read and UPDATE and exposes a stale replacement. |
| PC-21 | Bypass the application-side per-session semaphore acquisition/release in the test mutation while preserving runnable cleanup. | V-17 lock-owner assertion observes delivery enter before Apply commits; report the exact narrow lock-scope edit if two lines are required. |
| PC-22 | Move the existing file-validation await inside the acquired delivery-lock scope. | V-17 file-validation barrier: competing SendNow cannot reach submission while only disk validation is held. |
| PC-23 | Recompute `DeadlineAt` after file validation; separately change the shared expiry comparison so equality is allowed (a/b). | V-18 file-validation/equality cases apply after original D. If multiple local checks enforce equality, mutate the shared expiry helper or all duplicated equality checks as one documented equivalent subcontrol. |
| PC-24 | Remove the final post-lock application deadline check; separately remove SQL `clock_timestamp() < deadline` from guarded update (a/b). | V-18 post-read gate case attempts an UPDATE after application expiry; the separate DB-clock case changes a row after DB expiry. F-3 keeps the other clock/token protections from masking the particular guard. |
| PC-25 | Omit hold release on rejection; separately make normal flush ignore `HoldUntil` expiry (a/b). | V-15 rejected row retains a hold after cleanup; V-19 no parent input by two controlled scan opportunities despite original D passing. Neither red may rely on a generic test timeout. |
| PC-26 | Bypass the composed-body byte spill call. | V-20 C+1/multi-byte/batched row types oversize body instead of a pointer to the intact message. |
| PC-27 | Skip the current complete-prompt check while keeping the identity/head check. | R-6 `A_clipped_prefix_parks_as_truncated_not_sent` wrongly marks the clipped prompt Delivered. |
| PC-28 | Make the canary configuration validator accept an unowned runner/DB; separately make its evidence validator accept missing UserPrompt or API-only pointer (a/b/c). | V-22 corresponding negative fixture is wrongly accepted. Run only the no-launch guard tests under these mutations, never the live canary. |
| PC-29 | Drop required next/handoff retention in the gate, one subcontrol for each. | V-15 handoff-rejection rows deliver the lossy summary; `OutputDistillationGateTests.dropping_next_or_handoff_from_a_present_block_is_over_compressed` fails. |

For guards with redundant checks, the control must actually reach the named
production boundary. A mutation that remains protected by another check is not
positive-control evidence: isolate that boundary with F-3's clocks/barriers and
record the minimal equivalent edit. PC-24b specifically separates the database
clock from the application clock. For PC-20b, gate the real source-row read and
force the poll commit between application read and UPDATE only on the mutation
arm; on the correct arm the poll must wait for the application transaction. The
control must expose a stale replacement, not merely a different legal ordering.
No production query behavior
is changed outside the disposable mutation worktree.

### Commands and evidence accounting

Implement proposed methods before running these commands. A plan naming a test
that has not been written is not an executed check. Use the same fixed `bin-c419/`
output for all deterministic tests, and a new run stamp for each green or control
run. Run classes sequentially; do not execute `Antiphon.Tests`, E2E or Pty test
assemblies concurrently. The following PowerShell runner uses class filters and
fresh TRX names, preserving each native exit code:

```powershell
$c419Run = Get-Date -Format 'yyyyMMdd-HHmmss'
$c419Classes = @(
    'OutputDistillationPolicyTests', 'AgentReportStoreTests',
    'OutputDistillationDeliveryTests', 'OutputDistillationApplyRaceTests',
    'OutputDistillationTests', 'OutputDistillationProducerTests',
    'OutputDistillationAdmissionTests', 'OutputDistillationDeadlineTests',
    'OutputDistillationCleanupTests', 'OutputDistillationDispatchTests',
    'OutputDistillationQueueTests', 'OutputDistillationGateTests',
    'PolledCompletionNoteShrinkTests', 'DistillationEndpointTests',
    'SessionMessageQueueSpillTests', 'PtyDeliveryCeilingsTests'
)
foreach ($c419Class in $c419Classes) {
    dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c419/ -- --treenode-filter "/*/*/$c419Class/*" --report-trx --report-trx-filename "$c419Run-$c419Class.trx"
    if ($LASTEXITCODE -ne 0) { throw "$c419Class failed: exit $LASTEXITCODE" }
}
dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-c419-live/ -- --treenode-filter "/*/*/OutputDistillationCanaryGuardTests/*" --report-trx --report-trx-filename "$c419Run-canary-guards.trx"
if ($LASTEXITCODE -ne 0) { throw "Canary guards failed: exit $LASTEXITCODE" }
```

Run the specifically named R-6/R-7 and legacy regression methods above with
`/*/*/<Class>/<method>` filters, plus the conditional deliverable/DI classes when
touched. For example:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c419/ -- --treenode-filter "/*/*/SessionMessageQueueDeliveryVerificationTests/A_clipped_prefix_parks_as_truncated_not_sent" --report-trx --report-trx-filename card-0419-completeness.trx
```

For each PC use its exact method filter with distinct `pc-N-red.trx` and
`pc-N-restored.trx` names; rebuild through `dotnet run` on both sides of the edit.
Archive the first candidate green result too. Check fresh TRX executed method
names and all argument rows, nonzero counters, expected assertion failures in
red, and zero failures after restoration. `--list-tests`, a fixture/setup/build
error, an empty selection or a missing canary is not verification. Do not run
`dotnet test` or treat the exit code of an output-trimming pipeline as the verdict.

Code reports one table containing V-1..V-23, R-1..R-8 and PC-1..PC-29 including
subcontrols: implemented test, executed row count, pass/fail/skip, exact failure
and evidence path. Keep distinct totals for deterministic checks, mutation reds,
restored greens and the one live canary. Before approved live execution, V-23 is
**pending mandatory live acceptance**, never pass/not-needed. Attach CARD-0392's
reviewed recovery evidence or mark the release dependency pending; V-19/V-23 do
not close that card's interrupted-request gap.

### Out of scope

- Production Apply changes, production restarts, real broker/channel sends,
  capability/credential changes and provider rerouting. F-5 owns separate runtime
  resources; normal settings and the weekly prompt-review schedule stay untouched.
- CARD-0392's durable request-first ledger, bounded boot reconciliation and
  duplicate outcome implementation. Its crash-window tests are a release
  dependency, not an assertion that missing ledger rows are acceptable forever.
- CARD-0432 S3 event-pump restructuring and S4 eventual-cost accounting. Existing
  deadline/claim tests guard compatibility; this card does not claim those slices.
- Retention/backup after deleting the main report root, hostile operating-system
  administrators or external removal between file validation and delivery. Tests
  prove the stated service-restart/worktree-cleanup lifetime and API fallback.
- Exact model-token counting, model summarization of referenced files, or guaranteed
  short summaries for gate-rejected/oversized inputs. These would change D-1/D-4.
- Broad UI/Playwright, all-provider/Herdr matrices and a full test-suite rerun.
  The new code is backend/file delivery; the live acceptance uses real Claude on
  modern ConPTY, with conservative limits exercised deterministically.

### Cost

Forced suites: the named `Antiphon.Tests` classes/methods, no-launch E2E guard
tests, and the explicit live E2E canary after approval. No model spend in
deterministic or mutation runs. No client build with F-5's frontend-off fixture.

Estimated verification floor after tests are implemented: approximately 15–25
minutes for scoped deterministic runs/builds, another 30–60 minutes for all
isolated red/restored controls, and 10–20 minutes for one approved live canary
including teardown. These are planning estimates, not measured durations. The
canary involves two real source tasks, one cheap distillation and the parent's
boot/ack/read turns; report actual usage rather than an invented dollar estimate.
Implementation time and waiting for human approval/model availability are
additional. Record an unavailable canary honestly instead of dropping this floor.
