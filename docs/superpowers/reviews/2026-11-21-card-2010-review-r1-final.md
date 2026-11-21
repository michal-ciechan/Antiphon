# CARD-2010 review round 1, Final completion (CP-10 and CP-11)

Review task: c1840169. Subject: Code task c8aa09fc (`c8aa09fc-bbb6-4e6f-b179-608bd401b1e9`), branch
`feat/card-task-c8aa09fc`, tip `5e8d13105048f2467514ba0a6688c52ecb7ba04e` (Grok author). The first pass was
review task e72b1802, `docs/superpowers/reviews/2026-11-21-card-2010-review-r1.md` on
`feat/card-task-e72b1802` at `6946e9a15dc4472881ba8f1f5b805312a9705485`. That pass was CLEAN but Interim,
because CP-10 and CP-11 were deferred. Plan:
`docs/superpowers/plans/2026-11-20-card-2010-s6c-port-advisories-checkpoints.md`.

## Verdict

CLEAN. CP-10 and CP-11 are green at the reviewed tip. The round-1 CLEAN verdict now covers the Final
scope: ordinaryScopeCompleted is Full. No blockers and no regressions. The subject branch did not
change between the two passes. Its tip is still `5e8d1310`, so the touched classes and the guard
batch were not rerun. They are green in run `20261121-024249-1ef0` from the first pass.

## Checkpoint runs

| CP | Run | Result |
|---|---|---|
| CP-10 SessionRunner ProcessSpawnLimitTests | `20261121-030606-6a3e` (ws-01) | executed=2 passed=2 failed=0 skipped=0 (Min 2). commit=5e8d1310, sourceState=clean, buildSource=verified, slot=granted |
| CP-11 Agents.Pty ProcessSpawnLimitTests, first attempt | `20261121-030606-6a3e` (ws-01) | Not a test result. The row stopped with `owner-unverified` (exit 7) after the 3-minute owner-uncertainty budget. During that time `GET $ANTIPHON_API/api/agent-tasks/<id>` returned HTTP 502 on every poll (03:08:18 to 03:11:18 UTC). The test host had printed no output, and no TRX was written. The API answered 200 again at about 03:12 UTC. |
| CP-11 retry | `20261121-031258-7bee` (detached scratch worktree at the same SHA) | executed=2 passed=2 failed=0 skipped=0 (Min 2). commit=5e8d1310, sourceState=clean, buildSource=verified, slot=granted. The tool deleted its own output |

Both runs: `unlisted: none`. Server2 1-minute load was 3.3 and 3.0 at the two starts, below the cap of 14.

Why the retry ran in a scratch worktree: the first run kept `tests/Antiphon.Agents.Pty.Tests/bin-c2010-s6c-pty/`
as red evidence. The tool cannot delete it (`clean` says to remove it manually), and a rerun in ws-01
would refuse with `pre-existing-output`. The brief forbids recursive deletion. The retry therefore ran
in a fresh detached worktree at `5e8d13105048f2467514ba0a6688c52ecb7ba04e`, using the checkpoint tool host
already built in ws-01.

Builds outside the tool, each under its own build-slot lease:
- one `dotnet build tools/Antiphon.Checkpoints` for the tool host;
- one `check-evidence-diff.ps1`.

## Other checks

- `check-evidence-diff.ps1 -BaseRef 328eafa3daa7052d47587df66513e951dd0208cb -HeadRef 5e8d1310`:
  commits=6 entries=0 violations=0.
- Master has moved from `328eafa3` to `5a57eb90bba1f1f749ed200525eb1e06c9638b96` since the first pass.
  `git merge-tree --write-tree origin/master 5e8d1310` gives exit 0, tree
  `1d92de4c867126a2b5f2a15b0e9c541bd4a759bc`, no conflicts.
  - The only path both sides changed is the generated `tests/derived-impact-index.json`.
  - Both sides raised the same counters by 1. One example is the `Antiphon.sln` allowlist row, 342 to
    343 on each side. Because the edits are identical, the merged index keeps 343, one class short.
  - This is the expected I-5 case (docs/orchestration-loop.md). The landing wave regenerates the
    index. DerivedImpactIndexTests is red between waves, which is inherited, not a blocker.

## Advisories (do not hold the land)

- AD-1: after the land, the index row counts in the merged tree are one short wherever both sides
  added a class. The I-5 wave regeneration covers this.
- AD-2: an HTTP 502 from the task API for 3 minutes is enough to fail a checkpoint row as
  `owner-unverified`. That is the designed budget. It is recorded here only so this red is not
  mistaken for a test failure.
