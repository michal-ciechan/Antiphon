# Model-wait clock honors Codex token_count

The 20-minute model-wait clock now ages from the later of the last transcript timestamp and the last Codex `token_count`. Both signals going quiet still fails the task at 20 minutes, and the session is not killed.

Branch `feat/card-task-db402fb1`, parent `aa6dfd24a51db7c02e8722a05a21817bf4df407a`.

## Commits

- `5111812a1bf48db78718b46a4d0616ac36470cf2` red witnesses and unused `AgentSessions.CodexTokenCountAt`
- `b63ceff83f28f9c298030f44ed0cc35b564fbf9d` clock, normalizer, tailer snapshot, pull/sync stamp, both transcript maps
- `9f964c4394bf2ab7a987e57146a8051a6846e6ba` fixture newline so the tailer emits the heartbeat row

## Persistence

Migration `20261028170715_AddAgentSessionCodexTokenCount` adds nullable `timestamp with time zone` `AgentSessions.CodexTokenCountAt`. No existing field can carry it. `LastSeenAt` is process liveness for every kind and would move Claude and Grok. `LastPolledResultAt` is the parent result poll. Pipeline `LastActivityAt` is computed from the transcript and feeds another deadline. Null means no Codex heartbeat, so Claude and Grok stay on the transcript clock. The column is not a transcript row and not delivery evidence. A missed pull does not clear a stored value. The stamp is monotonic `ExecuteUpdate` on transcript pull and sync, at PostgreSQL microsecond precision. `token_count` is not a `TranscriptKind`.

## Clock

The heartbeat changes only `ModelWait` phase elapsed, after the boot substitution, still capped at this task's age. Boot (8), local execution (90), and the role ceiling (240) are unchanged, as is the 20-minute default. `DescribeLast` still names the transcript kind and its age. The session is not killed.

Phone-home `RunnerContractMapper.MapTranscript` and `SessionRunnerHttpClient.GetTranscriptAsync` both copy the optional field. An omitted field deserializes null.

## Settlement follow-up

Not implemented. `OnTurnEndLockedAsync` and `OnTranscriptActionAsync` load only `Dispatched` or `Working`, so a later done report on a Failed row does not settle. The incident already had a successful retry (`62fb542c`) while that Codex session was still running. Settling the Failed row would risk a double settlement.

## Tests

Red, before the clock read the column (lease `4f5a8296`, 74s):

- normalizer `token_count_keeps_its_timestamp_and_emits_no_transcript_part`: 1 failed (`LastTokenCountAt` null)
- tailer heartbeat: 1 failed (snapshot null)
- mapper: 1 failed (null vs `2026-10-28T16:03:58.8320000Z`)
- policy: 7 run, 1 failed (hold still ModelWait at 21m), 6 passed
- overdue: 4 run, 1 failed (status Failed), 3 passed

Green on `b63ceff83` (lease `48074527`, builds 0 errors, pre-existing warnings only):

- `RunnerContractMapperTranscriptEventTests` 7 passed, 0 failed
- `TaskDeadlinePolicyTests` 39 passed, 0 failed
- `AgentTaskOverdueDeadlineTests` 35 passed, 0 failed
- normalizer + tailer: 38 run, 36 passed, 2 failed

The heartbeat fixture had no closing newline, so the tailer never emitted the row. `9f964c439` adds it. Rerun of that method: 1 passed, 0 failed (lease `1324ceb6`).

`A_rollout_held_open_by_the_writer_is_still_read` failed because `File.ReadAllLines` on a `FileShare.Read` handle did not throw. That assertion runs before the tailer starts and expects the Windows sharing `IOException`. This change does not touch that path.

No native Windows tests. No whole Unit run.

## Land

`git merge-tree --write-tree origin/rc/2026-10-27 HEAD` is clean. rc tip is still `aa6dfd24a51db7c02e8722a05a21817bf4df407a`. Tree `2ba87278679328d8f378bb4baaeb13bc6ecc9390`.

`git merge-tree --write-tree origin/master HEAD` conflicts and was not merged. Keep both CARD-1574 readiness and `CodexTokenCountAt` in:

- `server/Application/Dtos/SessionRunnerDtos.cs`
- `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs`
- `RunnerContractMapper.MapTranscript`
- `SessionRunnerHttpClient.GetTranscriptAsync`

Inherited add/add, not edited on this branch: `.antiphon/task-af2b78c8.md` and `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`.

`scripts/check-evidence-diff.ps1` exit 0, 3 commits, 0 violations, over `aa6dfd24..9f964c439`.

Isolated `bin-db402fb1/` outputs were removed.
