# CARD-0418 round 9: partial ordinary matrix continuation

This continues the [round-8 ledger](2026-09-28-card-0418-round-8-evidence.md). It is not Final verification or a landing verdict.

## Changes

- The concurrent-admission integration row now sends the identical source window through two independent DbContexts at once and asserts one intent. A second prompt window, a trailing send kind and a second destination use the same source task ID but retain four distinct intent keys. The two channels each keep their own `MaxPending=1` budget.
- Complete-source publication stamps now require the actual inline source bytes, or every required ZIP member, to match the manifest length and SHA-256. The verifier bounds total expanded bytes to 64 MiB and refuses missing, duplicate, malformed or changed source content. The unit row adds changed-inline and changed-ZIP controls that the prior path-only predicate would have accepted.

## Verification status

- The required checkpoint tool was invoked for CP-5 on committed slice `249a79a62`, but exited 7 before a row launched: `CHECKPOINT owner owner-unverified`. This task process has `ANTIPHON_TASK_ID` and `ANTIPHON_API`, but no `ANTIPHON_TASK_TOKEN`; the tool's owner guard requires that token. **No CP-5 tests ran.** The same owner guard prevents the required full CP-1–CP-13 Final sweep until the task transport is repaired. Do not substitute an unbound run for owner-checked checkpoint evidence.
- A compile-only build was run through `scripts/build-slot.ps1` because the checkpoint tool could not start. Its first Linux attempt failed at the documented FakeClaude apphost/directory collision. The corrected build with `--property:UseAppHost=false` and an isolated output path exited 0 with 0 errors and 482 warnings. This is syntax/build evidence only, not a checkpoint or test verdict.

## Remaining scope

The round-8 open V-1–V-19, V-21–V-23 and R-1–R-14 groups remain open except for the individual V-9 identity and V-16 stamp assertions above, which are implemented but unexecuted. Full X/Y/Z settlement/routing, runtime release, pump lease takeover, policy and dispatch races, crash-state oracles, control caller and monitor matrices, browser child cleanup, full broker fallback, and R-14 evidence accounting still need named native evidence. F-5/V-24 remains with CARD-0784; do not duplicate it. V-25 and PC-1–PC-30 remain later gates. No land or shared-stack restart was performed.

Once task-token injection is restored, run the checkpoint tool against the current committed slice (at least CP-4 and CP-5 for these edits), finish the ordinary matrix, then run all CP-1–CP-13 on one committed tip. The test assertions added here require an actual red/green execution before they can be credited.
