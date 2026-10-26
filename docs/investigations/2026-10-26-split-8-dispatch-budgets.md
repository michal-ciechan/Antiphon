# Split-8 dispatch recovery statement budgets

Task `71a63ee1`, Linux/server2, 2026-10-26. Base and fetched master:
`9a3e6c7a53d2e98fd8582dd7d0c0ef1cb4be87a9`. The Windows qualification
`db2c2088`, candidate `a13e544e2baea214376944129a66ec4b525fafce`, was untouched.

## Diagnosis

The full `/*/*/DelegationDispatchRecoveryBoundaryTests/*` selection at the base
executed 381 cases: 378 passed, three failed, zero skipped. All failures were the
existing exact statement assertions:

| Action | Expected | Actual | Delta |
|---|---:|---:|---:|
| running-recovery-missing-brief | 63 | 65 | +2 |
| running-recovery-existing-brief | 67 | 70 | +3 |
| watchdog-first-recovery | 63 | 65 | +2 |

`git log -S CanWriteResumeInputAsync` identifies master commit
`b0f7c20f73adb1516cb216ba8f83294b3e971eab` (CARD-1574); the rc equivalent is
`3e4cbf5fb1636526c9f7f75fffd48cdb87324f41`. Both add session-owner/generation
reads for resume receipt fencing and baseline capture. The relevant queue and
budget sources are identical on fetched master and rc; this is not OS-specific.

Missing-brief ensure delivers directly through `DeliverNextLockedAsync`; its two
new reads are the receipt fence before automatic delivery and the launch-owner
read in `CaptureTranscriptBaselineAsync`. Existing-brief ensure leaves delivery
to `FlushIfIdleAsync`, which adds its own earlier receipt fence (+3 total).
Watchdog missing-brief recovery follows the direct ensure path (+2).
Each added command reads `AgentSessions` by session ID, including `StandingAgentId`
and `StartedAt`. No keyed-note query executes for these non-standing fixtures.
The complete retained SQL places baseline's new read immediately after the
transcript MAX: ordinal 42 for missing/watchdog, 44 for existing brief.

These checks intentionally enforce CARD-1574's receipt obligation before automatic
input, before working-state admission, and when deciding whether an empty
transcript baseline is observable. They run across distinct asynchronous
boundaries. Removing/caching them would alter that acceptance contract. This
repair therefore changes only the exact budgets by the observed deltas, with
CARD-1574 rationale; it does not remove product guards or widen timeouts.

CARD-1522 was read with all six history revisions and its retained Code report.
It is related historical budget maintenance, not the same source regression:
its seven repaired counts passed 14/14 at
`22e324fe61864daa7370b0f2f6fbed57f8ca2b15`. CARD-1543 subsequently reduced
the three recovery budgets; CARD-1574 added the present work afterwards.

All six `C1153_Only_new_cold_dispatch_prepares_evidence` variants passed on Linux,
including `remote-old-runner`. The rc's registration fixture hard-codes `linux`
unless an explicit platform is supplied, while the real adapter reports Windows
on Windows. The product's platform-consistency guard returns 409
`runner_platform_conflict` (CARD-0710, original master commit
`99d153a4a666f954c57c5321eeab9372de86cd64`). Master already fixes the fixture in
`6befcc97c22e12e58ac4ae538010dec8b517d597`, using
`platform ?? capabilities?.Platform` while preserving explicit mismatch tests.
That fixture correction is absent from rc. The Windows diagnosis is a source
comparison, not a claimed native reproduction; rerun the exact failed case there
after integrating the existing fixture repair.

## Evidence and validation

Raw evidence: `/work/test-evidence/task-71a63ee1/`.
Initial isolated output: `bin-71a63ee1/`, build 226.67 seconds, zero errors;
build slot granted with zero wait, held 227 seconds. The base class run held its
separate granted slot for 94 seconds (zero wait), test-host duration 90.077
seconds. TRX retains individual durations and argument identities; opt-in phase
records are in `red-phases/`. Uninstrumented phase attribution remains unknown.
Full SQL artifacts are in `red/`; console is `red.log`, TRX `red/run.trx`.

The initial build and class reproduction were explicitly commissioned. A second,
incremental build in the same isolated output is necessary to compile the changed
argument constants; no additional build root or full class repetition is planned.
Final validation selects only
`/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets`
(14 arguments), using `dotnet run --no-build` through `scripts/build-slot.ps1`.
No Unit lane, native test lane, tags, release, deployment or runner changes.
Final budget verification is pending at this evidence commit.
