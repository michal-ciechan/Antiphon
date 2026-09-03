# CARD-0339 — compact, correlated check-interpreter readings

**Date:** 2026-09-03

**Plan task:** 33c53ffb

**Card:** CARD-0339 — “Check-interpreter's successful digest should be a single correlating statement, not a narrative paragraph”

**Scope:** successful check readings and their deterministic envelope. The raw fallback remains CARD-0089 territory; interpreter availability returns to CARD-0079.

## Decision

Make a successful interpreter reading one compact, evidence-based physical line and make the delivery code own all correlation and liveness fields. Do not add a second distiller or a self-editing prompt loop.

Target shape:

    [check 639b197e #3] … elapsed (expected …) · session Running · working · last activity 0m ago

    On track — fixed a stale test pin; now verifying the larger channel test suites (no action needed).

The exact punctuation of the existing header, including capture timestamp, title, fallback marker, and superseded banner, remains compatible with its current contracts. The ownership boundary changes: line one is harness-measured data; line two is the interpreter's one-statement judgement.

## Ground truth and design choices

### Change all three conflicting instructions

The standing system prompt is the check-interpreter bundle, reconciled to SystemPromptAppend by CheckInterpreterProvisioner. It currently says “3-5 lines” twice. The fresh interpretation-task brief repeats that instruction in both CheckInterpretation.OutputFormatReminder and DelegationReportFormatter.CheckReportingContract. Editing only the bundle leaves the task brief instructing the same model to produce a narrative.

The latest three successful results demonstrate the problem even without physical line breaks: 894, 734, and 687 characters. Each repeats elapsed/expected, session state, transcript activity, and an argument that the overrun is reasonable.

Replace the formatting directions in all three sources with the same contract:

- Return exactly one physical line, at most 240 characters: no bullets, line breaks, evidence list, recap, or explanation that an overrun is fine.
- Start with On track, Needs attention, Unclear, or Settled at capture. These are the caller-facing mappings of the current judgement: DOING and PRODUCED become On track; LOOKS STUCK becomes Needs attention; AMBIGUOUS becomes Unclear; SETTLED becomes Settled at capture. No code parses these labels.
- Follow it with one current, evidence-backed clause and an optional short action cue. The target is: On track — fixed a stale test pin; now verifying larger channel suites (no action needed).
- Do not repeat task/check identity, capture, elapsed/expected, session status, working state, last activity, transcript counts, or a chronology of intermediate actions. The harness already supplies them.
- Preserve the existing safety language: do not call the checked task complete, done, or successful; do not infer authorship from shared-checkout Git data; use no tools; read only the supplied bundle; complete the interpretation with its own done report token and never blocked.

This is a deliberate contract change, not a post-hoc semantic truncator. Cutting a verbose answer in BuildNote can discard the only stated blocker and make the stored evidence disagree with the delivered note. Repeated, precise instruction plus a small output budget addresses the behaviour without hiding it.

### Most of the header is harness-generated; last activity is not

AgentTaskCheckService.BuildNote already emits the check short id and number, title, capture time, elapsed versus expected, session status, working/idle state, and the INTERPRETER DOWN or SUPERSEDED markers. The interpreter body replaces only the digest beneath that header.

Last activity is the exception. DelegateCheckProbe.CheckSessionFacts already carries SinceLastEntry and RenderDigest supplies it to the model as transcript entries and last age, but BuildNote does not add it to the header. That is why the current model prose repeats it.

Add the field to the BuildNote session branch:

- With a captured entry, append a formatted SinceLastEntry age, for example last activity 0m ago.
- With a live session but no entry, show an explicit unknown or never value, using the repository's existing display vocabulary.
- Keep the no-session branch, capture timestamp, title, INTERPRETER DOWN, and SUPERSEDED behaviour unchanged.

The field is gathered at the same capture instant as the rest of the header. No new probe, schema, endpoint, client work, queue change, or fallback-digest redesign is necessary.

### CARD-0330 is not this card's mechanism

CARD-0330 correctly chooses a separate output-distiller, a ledger, anchor-retention gates, and human-merged weekly prompt proposals for finished delegate reports. Its plan deliberately excludes check notes from that first consumer. A check reading is a time-sensitive interpretation of live work with a different safety vocabulary and no feedback ledger. CARD-0339 should ship one reviewed v4 contract and observe it; it must not add automatic prompt mutation or a second specialist seat.

### Availability is separately confirmed: reopen CARD-0079

The JSONL estimate was directionally correct but not its measurement. A live outcome audit from 2026-09-02T00:00:00Z through 2026-09-03T08:21Z found 76 Check-role interpretation tasks:

| Outcome | Count | Share |
|---|---:|---:|
| Succeeded with a reading | 23 | 30.3% |
| Canceled before dispatch; fallback delivered | 53 | 69.7% |

All 53 canceled rows had no DispatchedAt, zero cost, and the failure reason “The check that asked for it stopped waiting.” Every one also had an AgentTaskEvent Held entry with the exact detail “haiku is held; dispatch paused for that model.” The failures run to 2026-09-03 04:29Z; success resumes at 04:45Z. The current model-availability endpoint has no active hold, so the outage is over but the rate was not a rare edge case.

This differs from CARD-0079's completed session-migration and delivery kill-restart loop. Reopen CARD-0079 with a revision reason that names this separate cause: a held Low/haiku alias kept every new interpretation undispatched until its 60-second waiting window expired. Do not fold diagnosis or a provider policy into CARD-0339. The reopened card should establish why the pinned specialist obeys a model hold, decide whether an explicitly approved specialist exception is warranted, and improve visibility. It must not silently reroute to another provider; the model-hold contract requires an explicit operator choice.

## S1 — move all correlation data into the deterministic envelope

**Files**

- server/Application/Services/AgentTaskCheckService.cs
- tests/Antiphon.Tests/Application/AgentTaskCheckInterpreterTests.cs

1. Extend BuildNote's existing facts.Session branch with a last-activity bit derived from SinceLastEntry. Reuse FormatAge or add a narrowly named private formatter only if the nullable wording needs it. Do not read the transcript again or change capture timing.
2. Keep the assembly order: optional superseded banner, header, blank line, then successful reading or degraded fallback. The new field therefore applies uniformly to successful and fallback notes.
3. Extend the successful-reading integration test to assert that the header, rather than the model body, includes id/number, elapsed/expected, session state/working, and last activity. Assert the next non-empty line is exactly the supplied one-line reading.
4. Cover the no-session or no-entry rendering branch in the same focused class so the compact header cannot show a fabricated age. Preserve the existing fallback, superseded, and marker coverage.

## S2 — replace the narrative instructions with the v4 contract

**Files**

- server/Bundles/check-interpreter.md
- server/Application/Services/CheckInterpretation.cs
- server/Application/Services/DelegationReportFormatter.cs
- server/Application/Services/AgentTaskCheckService.cs
- tests/Antiphon.Tests/Application/CheckInterpreterProvisionerTests.cs
- tests/Antiphon.Tests/Application/AgentTaskCheckInterpreterTests.cs
- tests/Antiphon.Tests/Application/InstructionBundleTests.cs

1. Change the bundle heading and CheckInterpretation.ContractVersion from v3 to v4 together. Replace both 3-5-line directions with the exact one-line, 240-character contract and target On-track example. Explicitly name the header fields which the model must not repeat.
2. Change OutputFormatReminder to the same compact direction. It rides after every captured fact bundle and is the instruction that survives compaction, so it cannot contradict the system prompt.
3. Rewrite CheckReportingContract to request the same one-line reading before its mandatory, separate report-token line. Preserve the special Check done/failed/never-blocked settlement semantics; only the reading becomes one line.
4. Update comments that call the stored reading “3-5 lines,” so the historical target is not reintroduced.
5. Add focused assertions that the bundle, the output reminder, and the Check reporting contract contain the one-line constraint and do not contain 3-5 lines. Retain the existing non-completion, task-owned Git, no-tools, and closing-token spot pins. The embedded-bundle content hash changes automatically.

## S3 — test, deploy, and separate availability follow-up

1. Run the focused TUnit classes sequentially, not dotnet test:

    dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0339/ -- --treenode-filter "/*/*/AgentTaskCheckInterpreterTests/*"
    dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0339/ -- --treenode-filter "/*/*/CheckInterpreterProvisionerTests/*"
    dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0339/ -- --treenode-filter "/*/*/InstructionBundleTests/*"

   Use the forward-slash output path and remove only the scoped bin-card0339 output after its result is recorded. No client or E2E suite is implicated.

2. Deploy normally. Ensure a fresh controlled launch of the standing specialist through the agent API or UI after deployment. EnsureAsync reconciles the persisted prompt, but the running session correctly retains launch-time instructions until it is restarted; do not type a system prompt into a live terminal.
3. Inspect one ordinary scheduled check and its interpretation result. Verify v4's header carries last activity, the successful body is one line at or below 240 characters, no structured fields are repeated, and the raw digest remains in the checked task's AgentTaskEvent.
4. Reopen CARD-0079 in a separate board action using the measured evidence above. Its next Plan/Debug pass owns the model-hold diagnosis and any separately authorized reliability repair.

## Invariants and non-goals

- One line applies to the caller-facing successful reading, not the separate protocol line that settles the Check task.
- Do not truncate a completed reading in production code. The prompt is the quality control; the stored result remains faithful evidence for attention and later review.
- Keep raw fallback content unchanged except for the shared deterministic last-activity header addition. CARD-0089 remains its owner.
- Keep INTERPRETER DOWN and its incident path. The data shows why the marker remains necessary, but the held-haiku cause belongs to reopened CARD-0079.
- Do not auto-fallback to another model/provider. A recovered current hold state is not permission to make a future hold invisible.
