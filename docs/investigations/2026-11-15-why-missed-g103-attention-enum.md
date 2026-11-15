# g103: AttentionAction append broke an obsolete maximum assertion

## Reproduction and attribution

Windows Full run `b7812833`, chunk `antiphon-all-group-103`, reported
`UnmarkedWaitingContractTests.attention_action_continue_is_appended_after_clear_hold`
at rc source `64e3d78c487d51570f708492ab67b5db6d0de9a9`. The supplied failure
took 73 ms; it was an assertion failure, not a timeout or load diagnosis.

Debug task `f21bf3ba` reproduced the exact method alone on clean current master
`4ce55f36794f3e2fa904d56ba980f4182d89ab3a`: 1 executed, 1 failed, 0 skipped,
exit 2. The failure is `Enum.GetValues<AttentionAction>().Max(v => (int)v)`:
expected 11, actual 12. The gated native-output build passed in 316 seconds;
the test runner reported 5.994 seconds, with a 9-second build-slot hold.
Initial `/proc/loadavg` was `10.12 14.13 18.13`; a later build sample was
`33.07 20.29 19.87`. Load does not explain the deterministic ordinal mismatch.

A binary search over the first-parent sources from previous green-run source
`b559bfba2e9a420495122d27171352fe9b7e1ba4` to current master evaluated the
unchanged maximum assertion against the declared enum values. It found:

- Last good: `dc653af2c276cfc56dbd6cfc92ccebed14287db7`, maximum 11.
- First bad: `cdc98e27e552c305baa036eb384643d152a7ad36`, maximum 12.

This is a source bisection, distinct from the runtime endpoint reproductions.
The culprit adds `AbandonMessage = 12` without changing the test. The entire
`DelegationUnitTests.cs` file is unchanged between the old green source and
current master. The exact method also ran on a clean detached checkout of
`b559bfba2e9a420495122d27171352fe9b7e1ba4`: 1 executed, 1 passed, 0 failed,
0 skipped, exit 0. Its gated build passed in 209 seconds; the runner reported
8.522 seconds, with an 11-second slot hold. This independently reproduces both
ends of the supplied green-to-red interval. Raw logs and fresh TRX are ignored under
`.antiphon/f21bf3ba-evidence/` in the task worktree.

## Intended contract

`git log -S` traces the method and its maximum assertion to CARD-0294 S1,
commit `332098cafc65ba61f17cda934c6b7c472ced5455`. Its commit description
explicitly corrects the stale plan snapshot: the shipped last action was
`ClearHold = 10`, so `Continue` appended as 11, rather than the plan's 4.
The DTO documents “Appended after shipped 10; do not renumber.” Neither this
requirement nor the plan forbids future actions after Continue.

CARD-1892's [operator-abandon plan](../superpowers/plans/2026-11-14-g90-operator-abandon.md)
requires the new `AbandonMessage` action. Its implementation correctly appends 12.
`client/src/api/attention.ts` includes the string union member `AbandonMessage`
after `Continue`; `server/Program.cs` configures `JsonStringEnumConverter` with
integer values refused. There is no AttentionAction entity property or EF model/
migration mapping under `server/Domain`, `server/Infrastructure/Data`, or
`server/Migrations`: these action lists are computed response DTOs. Nevertheless,
the shipped ordinals must remain stable; renumbering either action would violate
the append contract for no product benefit.

The repair in `9e2dd96a3f5d7299f5556bf49ca42193b1c96758` retains the explicit
ClearHold/Continue assertions, protects every shipped action name/value from
Reply=0 through AbandonMessage=12, and allows later names only above 12.
Renamed/deleted members, changed ordinals, and new aliases into the prefix fail.
The neighboring AttentionKind contract uses the same pattern. No product source
or client mapping changes.

## Why ordinary verification missed it

The Merge 232 operator-abandon plan's CP-12 selected `AttentionServiceTests`,
`AttentionFeedNoiseTests`, `StandingBootAttentionTests`, and
`StandingContinuityAttentionTests`. Its queue rows selected queue/switch/rules
consumers. None selected `UnmarkedWaitingContractTests`, whose class name lives
inside `Application/DelegationUnitTests.cs` rather than an Attention-prefixed file.

The existing `contracts` impacted-classes row selected `ContractSnapshotTests`
for DTO edits, and `input-delivery-uncertainty` selected attention and queue
behavior, but neither selected this enum contract class. A single row mapping
`server/Application/Dtos/AttentionDtos.cs` to `UnmarkedWaitingContractTests`
would have caught the failure before the Full run. The same repair commit adds
the exact `attention-action-contract` mapping to `tests/impacted-classes.json`.
Running `impacted-classes.ps1 -Path server/Application/Dtos/AttentionDtos.cs -Json`
now names that kind and includes `UnmarkedWaitingContractTests` in its executable
`tests/Antiphon.Tests` filter. The selector scripts are unchanged.

## Verification scope and performance

Debug uses gated direct builds and serial TUnit rows with fresh TRX, preserving
native default build output and `UseAppHost=false` for build and no-build runs.
The closed scope is: exact-method master red; exact-method historical green;
whole `UnmarkedWaitingContractTests`; `Attention*` classes; the brief's eleven-class
guard union; and `ImpactedClassesSelectorTests` for the one-row mapping change.
Historical and index-driver builds are explicit diagnostic prerequisites, not
additional product coverage. No assembly-wide or Unit-lane run is needed.
Each execution row is bounded to less than ten minutes; no product/test timeout
is widened. Existing host contention is tracked by CARD-1906; unavailable phase
timings remain unknown. Mutation checks are waived by the operator's brief.

Nightly pins at the initial master are unchanged by this scope:

- policyHash: `b610138753e25545cda6f208abe19ebd507cdc77afc288c9888b778ad1ff6b9d`.
- PinnedInputDigest: `26d90df028d4e2358ed43b02aa7ef4ad688224c5470aba371d2fd566ff4a663e`.
- LegacyExclusionDigest: `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`.

The policy hash was recomputed through `Read-NightlyExecutionPolicy`; the live-lane
digest was recomputed with `test-live-lane-run.ps1 -PrintInputDigest`. Both match.
Their protected inputs are outside this repair. The derived impact index will be
regenerated separately after building its driver, with a second unchanged run.
No live host, qualification run, runner setting, or release branch is touched.
Final class/attention/guard/selector counts, index idempotence, source SHAs and
merge/evidence audits belong to the stored Debug report, so this investigation
can remain frozen before final source qualification.
