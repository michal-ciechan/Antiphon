# CARD-0262: A user preference stored in KB does not reach the agent's own instructions

Date: 2026-10-04. Investigator task: `15ae6c85-18cb-4740-a38b-ea6828c8d79a`.
Board: Antiphon (`8988ca03-7414-47ad-b0b6-51556c701703`).
Card row: `9bbf792b-23a8-4a13-b1fa-08ff46eb6c45`, InProgress, revision count 33.
Source examined: `bb18064ba647e0ddb03cae4da437ab60ed447d98`.

## Outcome

**Confirmed by reconstruction from current source and stored task evidence: preference propagation remains incomplete.** The foreign KB has no automatic input into Antiphon's instruction composition. Antiphon now has a per-agent pin store/API, but its production reconciler is an S1 no-op. Capturing a pin saves text and pending projection intent; it does not project a file, compose the pin into a launch, or queue a reread. Keep CARD-0262 open and hand this evidence to Plan.

This is a general standing-instruction gap, not a PDF-specific conversion or attachment defect. The original foreign KB row and originating provider transcript were not available for direct inspection in this investigation; confirmation applies to the propagation mechanism, with the incident provenance and limits below.

## Evidence identity and running state

Read-only requests used the inherited `ANTIPHON_API` and task-token header without printing credentials. `GET /api/version` returned:

```json
{"version":"bb18064ba647e0ddb03cae4da437ab60ed447d98","informationalVersion":"1.0.0+bb18064ba647e0ddb03cae4da437ab60ed447d98","capabilities":["land-v2","operator-shutdown-v1"]}
```

Thus the served application identifies the same source SHA examined here. A newer local implementation hidden behind an older server is not the explanation for this observation.

`scripts/card.ps1 get CARD-0262 -Board Antiphon` returned the card identity above, its full problem statement, and the operator's September 8 hold release. That release also requires an agent-unique file even in shared workspaces. These are recorded requirements, not evidence that file delivery was implemented.

## Storage-to-context trace

| Boundary | Current evidence | What reaches the next boundary |
|---|---|---|
| Foreign KB -> Antiphon | Historical incident identifies KB row `f5792203-d578-4f80-9d44-953cbd675fc0`. The earlier investigation locates the KB in the PredictionMarkets project's own store: [historical plan](../superpowers/plans/2026-08-31-card-0262-kb-preference-to-agent-instructions-plan.md), lines 9 and 32–43. Current `server/` and `client/src/` searches for `KnowledgeBase`, `KbEntry`, and that GUID found no connector. | No automatic import was found. The historical absence of a pin store is outdated; the absence of a KB connector remains consistent with current source. |
| Explicit capture -> pin DB rows | `server/Api/Endpoints/AgentPinnedInstructionEndpoints.cs:33` exposes capture. `server/Application/Services/AgentPinnedInstructionService.cs:57` accepts instruction text and source identifiers as request data; `:170` creates a pin; `:187` rotates state and records reconciliation intent; `:192` saves and commits. | Saved text/provenance, desired revision/hash, and projection metadata. Source identifiers are stored strings, not a foreign KB reader. |
| Committed rows -> projection/notification | `AgentPinnedInstructionService.cs:547` marks reconciliation Pending; `:605` creates a Pending projection row. `:195` publishes an event and invokes the reconciler. `server/Program.cs:522` registers `NoOpAgentPinnedInstructionReconciler`; its `:27` method only appends `(AgentId, Revision)` to an in-memory list and returns a completed task. | No filesystem write or session queue call. A target path in a DB row is not an existing file. |
| Pin DB -> effective launch instructions | `server/Application/Services/AgentSessionLaunchComposer.cs:52` loads bundle attachments. `:98` passes only attached keys, reply style, and `SystemPromptAppend` to the composer. `server/Application/Services/InstructionBundleComposer.cs:71` has those three inputs; `:91` renders bundles and `:99` appends the contract. | No pin text or pin snapshot is included. Claude argv, Codex developer instructions and Grok rules transport at `AgentSessionLaunchComposer.cs:113` carry this same pin-free composition. |
| Pin DB -> generated workspace floor | `server/Application/Services/AgentWorkspaceProvisioner.cs:142` calls Render with agent/directory/channels; `:191` exposes only those inputs. `:152` preserves unmarked files as LeftAlone. | No pin snapshot or generated pin import. Restarting the agent does not bridge the missing input. |
| Pin change/compaction -> live context | `AgentPinnedInstructionService.cs:655` publishes `AgentPinnedInstructionsChanged` without instruction text. No production consumer of that event was found. `server/Application/Services/CompactionRecoveryService.cs:86` selects the generic recovery body from `SystemPromptAppend`; `:111` queues it. | The existing recovery note has no pin snapshot or agent-specific pin-file pointer. |

Paths abbreviated to `AgentPinnedInstructionService.cs` in the table are under `server/Application/Services/`.

Repository-wide reference checks at the examined SHA found one reconciler implementation and its single production DI registration, no production caller of `RecordProjectionWriteAsync` (only its definition and a test call), and no assignments to `PinLaunchRevision`, `PinLaunchHash`, or `PinRefreshKey`. The schema fields alone therefore do not establish runtime integration.

## Reconstruction from stored evidence

The scoped task listing was read through `GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703`. Individual evidence below comes from `GET /api/agent-tasks/{full-task-id}`, using its stored `result`, `summary`, and `landing` fields.

| Stored row | Material evidence |
|---|---|
| Investigate `2ad377d7-7c4c-499a-a49d-e05a81b244dd`, Succeeded, completed `2026-09-07T18:21:52.619543Z` | Recorded that the KB belongs to an external project and that investigation/design had landed without propagation. This is historical evidence; its statement that no pin entity exists no longer describes the current checkout. |
| Code `604d1c73-c55b-4c96-8c70-029babce5df7`, Succeeded, completed `2026-09-13T12:27:54.203883Z` | Its result states: “File I/O and queue delivery are not in this slice.” It also explicitly excludes S2 composition/launch and identifies the reconciler as a no-op. Reports 27/27 scoped checks for S1, not end-to-end propagation. |
| Review `2a5e61bd-0ce0-442c-b758-b722974747b8`, Succeeded, completed `2026-09-13T13:05:06.832548Z` | Reviewed S1 at `300940435826a9885d0bde4210ffb39356aef8c8`; stored result accounts for 23/23 service/endpoint integrations plus 4/4 path/hash checks. Scope is explicitly S1. |
| Code owner's `landing` receipt | `publication=Landed`, `sourceSha=300940435826a9885d0bde4210ffb39356aef8c8`, `verifiedSha=remoteSha=8ccdb1c93d6dc288b05738ee0f7faded35548b4a`, `remoteConfirmedAt=2026-09-13T15:25:35.016278Z`. Cleanup separately says Refused, reason `ignored_content_preserved`. |
| Disposition `0e348410-be54-43fc-9782-8c9b207c5865`, Succeeded, completed `2026-09-14T01:19:32.23098Z` | Correctly reports containment of `8ccdb1c9` and evidence preservation, but concludes “CARD-0262 is done once you run that Ops step.” That conclusion overstates the landed S1 scope; cleanup cannot supply the absent runtime code. |

The landed `8ccdb1c93d6dc288b05738ee0f7faded35548b4a` is an ancestor of the examined HEAD (`git merge-base --is-ancestor` exited 0). Current source still contains the same no-op and pin-free launch seam, so this is incomplete implementation rather than an unlanded S1 branch.

The existing persisted-capture test supplies a concrete reconstruction:

1. `tests/Antiphon.Tests/Application/AgentPinnedInstructionServiceTests.cs:32` captures text with namespace `kb`, key `row-1`, reference `KB f5792203`.
2. At `:54` and `:64` it expects reconciliation and projection to remain Pending after successful capture; `:65` expects one no-op reconciler call.
3. A fresh DB context at `:67` reads the saved instruction and revision 1. The stored S1 Code/Review results report this suite passing.
4. Production wires the same no-op at `server/Program.cs:522`. Its method performs no I/O, while the launch composer independently omits pins. Therefore successful storage and successful S1 tests coexist with absent instruction delivery by construction.

This reconstruction uses stored execution reports plus the current test/source assertions. No test was newly executed, and raw historic TRX copies were not revalidated in this task.

## Original incident and separate attachment issue

Live `card.ps1 get CARD-0250 -Board Antiphon` returned row `b74e9df0-1cca-4579-bb88-171b84d4f851`, Done, revision count 6. Its stored description records the August 30, 15:57 Slack preference “always give me pdf” under KB row `f5792203-d578-4f80-9d44-953cbd675fc0`, followed by a written PDF and the human asking “Where is the new pdf” at 16:32. Its closure explicitly defers KB propagation to CARD-0262 while recording the attachment follow-up fix as shipped.

Those are card-stored incident observations, not a freshly retrieved transcript or foreign DB row. They explain provenance; the current source and S1 records establish the missing propagation mechanism independently. Attachment delivery and durable arbitrary preferences are separate boundaries.

## Remaining uncertainties and their resolution

- **Original KB bytes and owning agent identity:** the external PredictionMarkets DB and `D:\src\project\predictionMarkets` workspace are not available from this runner mirror. Retrieve that exact row, its update timestamp, and the originating agent/session metadata from the owning host to verify the original storage event and whether a source writer ever invoked capture.
- **Original workspace shape:** no direct evidence establishes whether the incident agent used ClaudeBot memory files or an unmarked project `CLAUDE.md`. Inspect only that agent's recorded cwd and instruction files on its owning host; do not infer it from the current Antiphon orchestrator.
- **Live pin rows:** a read using the inherited task token against `/api/agents/a392cbc4-0fc0-4603-b4d0-5198d1929718/pinned-instructions` returned HTTP 403, `code=pin_caller_mismatch`, “Pinned instructions require a live named-agent session token.” No headerless fallback was attempted. An authorized named-agent/operator read is needed to establish actual current pin rows; their existence is unnecessary to prove the code gap.
- **Historic transcript availability:** the S1 task's `summary.agentSessionId` is `a56f2219-bedf-4bfa-8eaf-e239c0cd96da`; `/api/sessions/{id}/transcript?since=0` returned `entries=[]`, `lastSequence=0`. This establishes no retrievable normalized transcript, not that the task received no prompt. Retention is documented in `docs/logs.md:76`. The original August incident transcript was not retrieved.
- **End-to-end adoption:** no current agent behavior canary was run. Saved/queued/file-projected state and transcript-confirmed reread must be distinguished from actual model compliance in later acceptance evidence.

These limits prevent claiming a fresh reproduction of the original Slack interaction. They do not leave competing explanations for the demonstrated absent instruction path at the served SHA.

## Disposition and verification

Next stage: **Plan**. Root cause is confirmed; this is not already fixed and does belong in Antiphon's instruction delivery path. Reconcile the existing September 8 plan with the landed S1 boundary and the operator's recorded requirements. Existing plan rows describe S2–S7 as later work, and its final acceptance section warns against reporting those slices verified from the document (`docs/superpowers/plans/2026-09-08-card-0262-kb-preference-to-agent-instructions-plan.md:640`, `:727`). No new design is proposed here.

No source, card, settings, sessions, KB rows, or live pin state were changed. No builds/tests were run. Verification comprised read-only HTTP evidence, source references, ancestry, and documentation diff checks. Stored test counts above are historical scope evidence only.

Useful read-only reruns:

```sh
pwsh -NoProfile -File scripts/card.ps1 get CARD-0262 -Board Antiphon
pwsh -NoProfile -File scripts/delegate.ps1 -Status 604d1c73-c55b-4c96-8c70-029babce5df7
rg -n 'IAgentPinnedInstructionReconciler|RecordProjectionWriteAsync' server --glob '!**/Migrations/**'
rg -n 'PinLaunchRevision\s*=|PinLaunchHash\s*=|PinRefreshKey\s*=' server --glob '!**/Migrations/**'
```

## Not done, noted

Fix idea only: complete the existing plan's capture-to-launch, agent-unique file, and durable reread integration beyond S1; no fix was designed or implemented in this investigation.

--- next stage ---
next: plan
handoff: CARD-0262 remains open: S1 store/API landed, but production uses a no-op reconciler and launch composition omits pins. Reconcile the existing September 8 plan with this confirmed boundary and operator requirements; original external KB/workspace evidence remains unavailable here.
artifact: docs/investigations/2026-10-04-card-0262-a-user-preference-stored-in-kb-does-not-reach-the-agents-own-instructions.md
