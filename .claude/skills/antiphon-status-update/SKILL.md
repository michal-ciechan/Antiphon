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

1. Read `GET /api/orchestrator-instructions` for current directions and seat target.
   Run `pwsh -NoProfile -File scripts/status-snapshot.ps1 -ProjectId <id>` (add
   `-Json` for machine output). Omit ProjectId only when the pipeline identifies
   one project; ambiguity requires an explicit id. API URL and authentication use
   `ANTIPHON_API` and the task-token header, as card.ps1/delegate.ps1 do.
   The script is read-only, reads remote refs without fetching, and marks cached
   history. Exit 2 means partial facts: preserve every `UNVERIFIED`/`errors` entry.
2. Check `GET /api/agent-tasks/pipeline?projectId=<id>` for stages in flight,
   queued, blocked, effective limits, hosts and remaining seats. Check
   `GET /api/session-runners/<runner>/slots` for occupied seats and orphan ghosts.
   Runner seats/hosts are fleet facts even on a project-scoped pipeline.
3. Run `pwsh -NoProfile -File scripts/delegate.ps1 -Status <8hex>` for every
   relevant task, including finished/failed tasks known from the previous update.
   Read state, session activity, and parsed `next=`/`next:` and `handoff:` lines.
   Pull transcript progress before declaring healthy or stalled. Use
   `scripts/card.ps1 get <CARD-id> -Board <board>` for actual card decisions;
   read history when a decision or earlier statement needs correction.
4. Run `git fetch origin master`, `git log <previous-update-sha>..origin/master
   --oneline`, `git rev-parse origin/master`, and
   `git ls-remote --heads origin 'rc/*'`. If the previous-update SHA is unknown,
   say the landing interval is unverified. Attribute each landing from its receipt;
   a changed tip alone does not identify who landed it.
5. For live Windows rc qualification, obtain the owner's exact candidate id, cut
   SHA, run id and paths. Follow
   [antiphon-rc-qualification](../antiphon-rc-qualification/SKILL.md): run logs are
   `C:\Antiphon\releases\candidates\rc-<date>-<sha12>\logs\<run>`, state is
   `C:\Antiphon\releases\state-<id>\last-run.json`, and heartbeat is
   `C:\Antiphon\releases\task-<id>\supervisor-heartbeat.json`.
   Add `-RcRunLogDir <logs> -RcStateDir <state> -RcHeartbeatPath <heartbeat>` to
   the snapshot on the host that can read them. Compare TRX file count with
   `dispatch-schedule.json` entries; inspect `active-chunk.json` at the recorded
   timing root, last-run fields, supervisor heartbeat (`observedAtUtc` or `utc`),
   elapsed time and the failure watcher's output. File count is progress, not a
   pass count. Check identities before combining records; shared last-run may
   belong to a later run (use the task's `own-last-run.json` when appropriate).
   If Windows paths cannot be read, obtain the owner's observation or mark the
   rc facts unverified. Never infer completion from a watcher or a healthy signal.

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
   failures and each fix's position (landed on master / branch / Debug running);
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
