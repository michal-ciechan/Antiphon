# CARD-1065 S5: regression red; protocol prerequisite decision

Original Code / landing owner: `d180d288-f8be-441d-bd2a-9cfcd1c5489f`.
Branch: `feat/card-task-d180d288`; worktree: `/work/worktrees/task-d180d288`.
Base: `a1d014fc0ede59f21ddb573fb369e6abce85a700`.
Tested source: `9d72aea30c1f0b2ac8ccab1af2076fd2eaff436e`.

S5 is **not implemented or ready for Review**. The three initial regression witnesses
compile and fail at the intended assertions. They do not yet implement the complete
V-12/V-13/V-14 scenario matrix. No production file changed. Parking stays default-off.

## Required prerequisite scope

The [plan](../superpowers/plans/2026-10-05-card-1065-blocked-task-parking-plan.md)
assigns S5 server composition and CP-5 only. Inspection of the landed S3/S4 contracts
found prerequisites outside that server-only footprint:

1. **Remote repository identity has no producer reachable by the server.**
   `TaskParkPublicationService.PrepareAsync` requires `remoteRepositoryIdentity` and
   returns `park_repository_unknown` without it. `PhoneHomeWorkspaceMirrorResponse`
   returns only `Path`; `WorkspaceParkCommand` accepts only Prepare/Verify, each already
   requiring the identity. The server must not derive it from the desktop common
   directory or guess a runner path. Add a guarded read/capture contract, bind it to the
   runner/store/generation and owned checkout, and persist it before publication.
2. **The final runner verifier represents only an owned remote Worktree publication.**
   `SessionRunnerRuntime` constructs its park verifier only from enabled phone-home
   repository policy. `RunnerWorkspaceParkService.ValidateLexicalTarget` requires the
   task-named directory and owned task ref; Verify requires RemoteSha=SourceSha.
   `TaskParkPublicationService` can produce local Shared and ReadOnly/NoSourceChanges
   evidence, but the final release wire has only `WorkspaceParkReceipt` with a required
   remote SHA. Add capability-gated local/source-mode verification under the existing
   input/generation gate. Do not omit publication from the command or convert a
   NoSourceChanges proof into a fictitious published receipt.
3. **Nonreport handoff composition needs an explicit durable source.** The S4 loader
   requires ReportDigest, derived from task.Result; nonreport blocks may have no Result.
   S5 must bind existing authorized transcript/checkpoint evidence without inventing a
   report or CompletedAt. This server work can stay with S5 after the wire scope is resolved.

The S4 evidence already flags the first gap and the separate release-action mismatch.
S5 must also use the persisted publication action ID at reservation/recovery, rather
than the coordinator's fresh Guid, and preserve source checks during ambiguous-release
reconciliation. These observations do not establish implementation or passing evidence.

**Caller decision:** commission a separate prerequisite Code repair (recommended), or
expand this task to the affected runner contracts/runtime and their CP-2/CP-3/CP-4
qualification before CP-5. The current brief explicitly says CP-5 only. A textual
scope answer was requested; none had arrived when this artifact was written.

## Executed evidence

CP-5: 3 executed, 0 passed, 3 failed, 0 skipped. Fresh TRX contains the exact three
BlockedTaskParkReleaseTests methods:

- V-12 / G-95: direct coordinator sent 1 conditional release command with no publication
  proof (expected 0).
- V-13 / G-106: the nonreport Blocked path registered 0 episodes (expected 1).
- V-14 / G-110: Shared Blocked release wrote PoolIdleSince (expected null).

```text
CHECKPOINT CP-5 commit=9d72aea30c1f0b2ac8ccab1af2076fd2eaff436e build=ok filter=/*/*/BlockedTaskParkReleaseTests/C1065_* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-d180d288/.antiphon/checkpoints/20261006-014723-04dd/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=9d72aea30c1f0b2ac8ccab1af2076fd2eaff436e sourceState=clean buildSource=verified
```

The SHA-bound report confirms clean unchanged source and verified build provenance.
Receipt validation exits 2, `CHECKPOINT SOURCE INVALID reason=row_failed`; this is red
evidence, not a clean qualification certificate. No retry, assertion relaxation,
timeout increase or deliberate mutant. Repair rounds used: 0 of 2.

Full evidence and remaining obligations: `.antiphon/task-d180d288.md` (committed
individual Markdown report). Generated evidence stays ignored under
`.antiphon/checkpoints/20261006-014723-04dd/`.

Restart: **none**. Caller/orchestrator owns future activation after complete qualification.
Next is the prerequisite scope decision, then Code; no Review or landing claim.
