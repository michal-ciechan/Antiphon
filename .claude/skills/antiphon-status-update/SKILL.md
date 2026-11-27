---
name: antiphon-status-update
description: Compose an evidence-based operator status update. Use when the operator or another orchestrator asks for status, how the rc is going, or what is in flight.
---

# Antiphon status update

Run from the repository root. General operating rules and dispatch mechanics live in
[orchestration](../../../docs/orchestration-loop.md),
[antiphon-orchestrator](../antiphon-orchestrator/SKILL.md) and
[antiphon-delegate](../antiphon-delegate/SKILL.md).

## Gather directly

Run `scripts/antiphon status --project-id <id>` (`scripts\antiphon.cmd` on Windows)
once. Add `--json` for structured output. The call reads operating instructions,
project and fleet tasks, the land queue, merges, runner seats, remote refs, and rc
progress, failures and ETA. Project scope requires an explicit id when the pipeline
cannot identify one project. Treat runner seats and host limits as fleet facts.
Use the live instructions for the seat target. The command uses `ANTIPHON_API`
and the task-token header and reads Git refs without fetching. Exit 2 means
partial facts. Keep every `UNVERIFIED` or JSON `errors` entry.

On the desktop, omitted rc paths are discovered under `C:\Antiphon\releases`.
Supply the owner's exact `--rc-run-log-dir`, `--rc-state-dir`, and
`--rc-heartbeat-path` when discovery is ambiguous. Use `--rc-release-root` for a
copied release layout and `--rc-previous-run-log-dir` to select a specific comparable
baseline. Verify candidate, pin and run identity when reconciling with the owner.
The shared last-run record can belong to a later run. The task's matching
`own-last-run.json` is the fallback. TRX file count measures progress. Read the
failures and fix positions separately, and keep unknown work and build/startup
overhead with the sequential ETA. A missing previous run leaves the ETA
unverified. If the host cannot read the Windows paths, obtain the owner's
observation or preserve the rc caveat. Require a completion record before
declaring the run complete. Follow [antiphon-rc-qualification](../antiphon-rc-qualification/SKILL.md).

The call is bounded: API reads run concurrently, each with its own deadline (the
attention feed a shorter one), inside one overall budget; a read that misses it is an
`UNVERIFIED` line, never a stall. Unknown remaining chunks make the ETA a lower
bound (`lowerBound`, an `UNVERIFIED` line naming them). The previous run is the
newest earlier attempt with terminal evidence for each entry in its
`dispatch-schedule.json`. Native chunks use complete TRXs. The `client/all` and
`scripts/all` chunks use terminal timing sidecars. The estimate prices native TRX
durations and treats any unpriced unfinished chunks as unknown work.

`--json` always carries every key below, `null` when the fact is unknown:
- top level: `observedAtUtc`, `projectId`, `runner`, `counts`, `pipeline`,
  `orchestratorInstructions`, `master` (`tip`, `cachedOriginMasterLog`,
  `historyIsCached`), `rcRefs`, `rc`, `taskScope`, `tasks`, `mergeInFlight`,
  `fleetHosts`, `fleetConcurrencyScopes`, `runnerDefaults`, `landQueue`,
  `runnerCatalogue`, `runnerSlots`, `cardsAtRisk`, `errors`;
- `rc`: `candidateId`, `pin`, `runId`, `phase`, `elapsedSeconds`, `lastRun`,
  `runLogDir`, `stateDir`, `activeChunk`, `heartbeat`, `heartbeatAgeSeconds`,
  `trxCount`, `finishedChunks`, `scheduledEntries`, `failures`, `eta`;
- `rc.eta`: `status`, `previousRunLogDir`, `ratio`, `matchedFinished`,
  `knownRemaining`, `unknownRemaining`, `unknownRemainingChunks`,
  `finishedWithoutBaseline`, `baselineRemainingSeconds`,
  `projectedRemainingSeconds`, `projectedFinishUtc`, `lowerBound`,
  `p95FinishUtc`, `confidence`,
  `observedStartupSeconds`, `startupOverheadNote`, `biggestRemaining`.

Use a task's parsed next-stage and handoff fields. A task absent from the
snapshot can still matter to an earlier update. Consult its status if needed.
Confirm transcript progress before calling a task healthy or stalled. Use
the card record and its history for actual decisions and corrections. Attribute
landings from their receipts and mark the interval unverified when the previous
update SHA or the receipts are unavailable. Cached master history cannot
establish the landing interval.

## Classify what needs to happen

- Running and healthy: name the task and transcript progress that supports it.
- Finished, awaiting the orchestrator: use its next-stage block to name the exact
  next dispatch and handoff. Never derive the stage from the report body. A missing
  block is unknown; retrieve it rather than guessing. Queued work remains queued.
- Blocked with an operator-level question: state the question, options and your
  recommendation. Separate capacity deferrals and routine dispatch work from decisions.
- Failed: a Debug task is owed; name the existing Debug task or say it is owed.
  If a Failed task has a pushed tip and a done report with only unfinished whole-Unit
  verification, identify the bookkeeping mismatch and the verification still owed.
  Never call the unfinished verification passed.
- Stalled: no transcript progress for 30+ minutes. State the last progress time
  and uncertainty; the absence of a watcher event alone cannot establish a stall.

## Phone output

Lead with one outcome bullet. Use bullets with one fact each, ids/counts/SHAs instead
of adjectives, no paragraphs; a compact task table is allowed. Keep about 40 lines
unless asked for detail. Retain every caveat, risk, correction and uncertainty even
if that needs more lines. Use these sections in this order:

1. **BLOCKED OR WRONG**: failures with counts, stalls, and corrections to earlier reports.
2. **DECISIONS WAITING ON THE OPERATOR**: each question, options, recommendation.
3. **MASTER**: tip SHA; one line per verified landing since the last update.
4. **rc**: line, candidate id, cut SHA, run id, TRX/scheduled entries, elapsed time;
   ETA with unknown work and overhead; failures and each fix's position (landed
   on master / branch / Debug running);
   explicitly state fixes the pinned run does not yet contain.
5. **IN FLIGHT**: occupied seats against the standing target of 7 on server2,
   overridden by the live operating instructions; distinguish project tasks from
   fleet seats. Per task: role, short id, card, one-line purpose, state.
6. **NEXT**: the next concrete dispatches the orchestrator will make.

For an empty section say none verified; for unavailable facts say exactly what
could not be checked. A skipped step is skipped; a failing test is failing, with
its count. Quote an operator instruction only with its date. Never paste secrets.
Preparing status does not itself authorize dispatch, repair, release or restart.

## Common misreadings

- A watcher window expiring is not an event; check the underlying facts.
- Merge cap 1 and Code cap 5 409s are deferrals, not reasons to override a live cap.
- A master tip that moved may be the operator's pushes; it is not evidence of your landing.
- A Failed delegate with a pushed tip and done report needs its actual remaining
  obligation and bookkeeping mismatch stated, not an invented implementation failure.
