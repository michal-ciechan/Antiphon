# CARD-1310 streaming custody: terminal verification

Incomplete: the last authorized CP-3 attempt reached terminal RED before any test
executed. Post-build confinement reported four TAMPERED paths. Both validators
rejected the terminal receipt with cleanup_refused; the tool did not throw a
JsonException on this terminal receipt. No retry or guard relaxation followed.
Next stage: Code, to diagnose this confinement refusal and complete M-1 under a
new authorization. The unchanged-source CP-3 normal-attempt budget is exhausted.

Original Code / landing owner: a5c09b23-f3a1-4956-a4bd-221d9bbf7628.
Code task: e9f573b2-52b2-48cf-bcc0-ef3867f27ac1.
Branch: feat/card-task-e9f573b2.
Worktree: C:\Antiphon\worktrees\card-task-e9f573b2.
Tested implementation SHA: 1fb704bd4614ab019553fda205a63261c0606e98.
This evidence-only commit follows that tested implementation; no implementation,
test selection, assertion, timeout or custody rule changed after that SHA.
Plan: docs/superpowers/plans/2026-10-16-card-1310-coverage-mapping.md.
Mutation candidate: docs/superpowers/mutations/2026-10-16-card-1310-candidate.md.

## Implementation and initial inventory

The Windows lower bound uses destination creation time; write/change upper bounds
remain enforced and Linux retains its change-time lower bound. Output adoption
and confinement hashes now use a verified handle, a fixed 64 KiB streaming buffer
and a documented 1 GiB hard ceiling. The receipt reader retains its independent
64 MiB cap. Identity, containment, owner/private ACL, links/hardlinks, tracked-file
checks and the three-second deadline remain enforced.

Before the streaming source changes, a read-only production-path probe inspected
all 2,673 retained E2E entries (2,376 files). Exactly two refusals were found:
.playwright/node/win32_x64/node.exe and
fakegrok/.playwright/node/win32_x64/node.exe, each 93,580,104 bytes, both size-cap
refusals. Time-window, owner/ACL, links/hardlinks, tracked files and other refusal
counts were all zero. The unpersisted old exit instant was bracketed by build-log
completion and build-stamp creation; both bounds agreed for every entry.
Audit: .antiphon/c1310-r3/audit-before-streaming.json.

Red-first native 65 MiB adoption and confinement-ledger tests failed on their
intended adoption/content assertions before the streaming fix. Both now pass,
including bounded allocation and final-byte rewrite detection. The opened-handle
size 1 GiB+1 negative passes and asserts no reading, adoption or deletion. Its
small backing bytes ensure a missing ceiling would fail the adoption oracle,
rather than pass through an incidental timeout. The deliberate ceiling mutant
remains pending SourceLanding Mutation.

## Ordinary verification

Run 20261016-100304-d10c at the tested implementation SHA:

| CP | Executed | Passed | Failed | Skipped | Outcome |
|---|---:|---:|---:|---:|---|
| CP-1 | 21 | 21 | 0 | 0 | V-1 through V-7 passed, including all seven unrelated-entry and six mapping-authority variants |
| CP-2 | 69 | 38 | 31 | 12 | R-1 remains inherited red; exact match to Review 85782542's original-base 31-name list, zero differences; ran once |
| CP-5 | 35 | 21 | 14 | 6 | V-8 passed; R-3 failures exactly match the pre-streaming-fix run after excluding the repaired new ledger test |
| CP-4 | 0 | - | - | - | R-2 NOT RUN by explicit user instruction, CARD-1287 |

Fresh TRX class counts were checked: mapping virtual 15/native 6; owned-output
10/output-cleanup 43/authority 16/custody 12; confinement-ledger 17/handle 16/receipt
8. CP-5 covers the additional affected hash consumer and shared handle loop. No
whole assembly or whole Unit lane was run. The 31 CP-2 failures remain Windows
fixture teardown/platform assumptions, path separators and missing fixture output
owner authority. The 14 CP-5 failures remain fixture teardown/dirty-tracked
assumptions reproduced before the streaming repair. No full green regression claim.

## Historical receipt attempts preserved

- 20261016-093151-aa91: before streaming, CP-3 12/12 passed but the first large
  node.exe was refused; both validators rejected cleanup_refused. The original
  creation-bound evidence note preserves the stop and exact refusal.
- 20261016-101526-eaa7: after streaming, CP-3 12/12 passed and all fresh E2E output
  was removed. The imported manifest also named the unused retained CP-1 output,
  so terminal receipt cleanup refused still-present. Both validators rejected it.
  Recorded pre-cleanup wall: 215.8529838 seconds; complete executor wall including
  custody cleanup: 721.8964983 seconds. This receipt is not qualified.
- 20261016-102931-945a: CP-3-only manifest, 12/12 passed; identity-checked stop before
  the initial continuation budget expired. No extension had arrived. The executor
  exited, its tool copy was removed, and phase is stopped. PowerShell accepted its
  incomplete intermediate receipt; the tool refused it. Read-only invocation of
  the production parser established the exact exception:
  System.Text.Json.JsonException: receipt-shape: outputPaths required for outputNames
  Receipt: .antiphon/checkpoints/20261016-102931-945a/report.json.
  This stopped receipt is not qualified. No receipt contents were printed by the
  exception probe. The validator disagreement is retained, not relabeled green.

The caller subsequently extended the budget by 30 minutes and explicitly authorized
one final CP-3-only attempt at a fresh output ID. No further CP-3 retry is allowed.
This is the third normal attempt at the unchanged implementation SHA, within the
three-attempt limit. The prior stopped and refused outcomes remain part of cost
and verification history.

## Mutation and custody

PC-1, PC-2, PC-3, PC-4, PC-5, PC-6, PC-7, PC-8 and PC-9, including every variant in
the candidate, remain pending method-scoped SourceLanding Mutation. Code ran no
deliberate mutant. Initial ordinary Review remains required.

All checkpoint rows use slot=granted waited=0s. Necessary unlisted bootstrap builds
used build-slot.ps1: original red/fixed bootstrap held 6s/39s; streaming bootstrap
held 9s, each successful with four existing warnings. The read-only initial audit
used slot=granted waited=0s held=13s. No timeout was widened. Runner defaults and
session runners were read; no placement setting was embedded or changed.

Automatic approval review rejected native PowerShell removal of nine completed
task-owned output roots with "blocked by policy", despite resolving each exact
path inside this worktree and rejecting reparse roots. No deletion workaround or
foreign-output deletion occurred. Retained outputs and the auxiliary detached red
proof checkout are listed in .antiphon/task-e9f573b2-streaming.md and
.antiphon/c1310-r3/retained-own-output-paths.txt. The two streaming bootstrap outputs
and the interrupted CP-3 output are also retained. Normal validated cleanup removed
the completed checkpoint tool shadow copies.

No production restart, landing or deployment was performed. Restart: none.
The full continuation audit, exact inherited failure names, all source commit SHAs
and generated evidence paths are in .antiphon/task-e9f573b2-streaming.md. The
historical committed note is docs/superpowers/evidence/2026-10-16-card-1310-creation-bound-code.md.


## Last authorized CP-3 attempt: terminal refusal

Run: 20261016-104253-3680.
Manifest: .antiphon/c1310-r3/stream-terminal.yaml (CP-3 and its sole E2E build only).
Fresh output ID: bin-c1310-stream-terminal-e2e.
Expected/actual implementation HEAD: 1fb704bd4614ab019553fda205a63261c0606e98.
Terminal phase: done; executor 32932 exited and its tool shadow was removed.
Compiler: zero errors, 36 warnings; compiler wall 00:01:24.80.
Post-build confinement refused: tampered. No tests launched and no TRX was produced.
The row is not passed. Source start/end remained clean with the same fingerprint,
but buildSource is unknown because confinement verification failed.

Every flagged path:

- _bmad/tea/testarch/knowledge/overview.md
- _bmad/tea/testarch/knowledge/pact-consumer-di.md
- tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/captures.json
- tests/Antiphon.E2E/bin-c1310-r3-final-e2e/.playwright/node/win32_x64/node.exe

Both validator commands returned exit 2 with the exact output:

CHECKPOINT SOURCE INVALID reason=cleanup_refused

Terminal receipt (no receipt contents were printed by the validator capture):
C:\Antiphon\worktrees\card-task-e9f573b2\.antiphon\checkpoints\20261016-104253-3680\report.json
Validator captures: .antiphon/c1310-r3/terminal-powershell-validator.txt and
.antiphon/c1310-r3/terminal-tool-validator.txt.

A read-only follow-up used the production ConfinedFileReader.Hash path, the same
1 GiB ceiling and unchanged three-second deadline. All four paths were readable;
elapsed times were 247ms, 2ms, 1ms and 1240ms respectively. The three tracked files
still match HEAD and their write times predate this run. The large retained file
is still 93,580,104 bytes. This probe ran through build-slot.ps1, label
c1310-terminal-refusal-probe: slot=granted waited=0s held=3s. It was a diagnostic
read, not another CP-3 run, build, test or mutation.
Probe: .antiphon/c1310-r3/terminal-refusal-probe.json.

Attribution is unresolved. The confinement stamp helper maps read exceptions to
unreadable, and the refusal log does not preserve the old/new stamp or underlying
read exception. A transient read failure is a possible explanation, not an
established cause. No content change, deadline problem or code regression is
claimed as proven. No file was restored, no deadline was widened, and no further
CP-3 attempt was made. Required M-1 acceptance remains incomplete.

## Exact checkpoint evidence

.antiphon/checkpoints/20261016-100304-d10c/report.md

source: 1fb704bd4614ab019553fda205a63261c0606e98 state=clean buildSource=verified
CHECKPOINT CP-1 commit=1fb704bd4614ab019553fda205a63261c0606e98 build=ok filter=/*/*/(CheckpointCoverageMappingTests*)|(CheckpointCoverageMappingNativeTests*)/* executed=21 passed=21 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-e9f573b2\.antiphon\checkpoints\20261016-100304-d10c\rows\CP-1\run.trx slot=granted waited=0s dirty=0 source=1fb704bd4614ab019553fda205a63261c0606e98 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=264.7185387s startup=4.1107857s testsWall=3.0502929s teardown=0.3301153s hostWall=7.4912638s
CHECKPOINT CP-2 commit=1fb704bd4614ab019553fda205a63261c0606e98 build=reused filter=/*/*/(OutputCleanupTests*)|(CheckpointOwnedOutputTests*)|(HostCleanupCheckpointCustodyTests*)|(HostCleanupCheckpointAuthorityTests*)/* executed=69 passed=38 failed=31 skipped=12 trx=C:\Antiphon\worktrees\card-task-e9f573b2\.antiphon\checkpoints\20261016-100304-d10c\rows\CP-2\run.trx slot=granted waited=0s dirty=0 source=1fb704bd4614ab019553fda205a63261c0606e98 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=0s startup=4.9841327s testsWall=58.6614563s teardown=0.3979418s hostWall=64.0441391s
CHECKPOINT CP-5 commit=1fb704bd4614ab019553fda205a63261c0606e98 build=reused filter=/*/*/(CheckpointConfinementLedgerTests*)|(HostCleanupCheckpointReceiptTests*)|(PlanCoverageHandleTests*)/* executed=35 passed=21 failed=14 skipped=6 trx=C:\Antiphon\worktrees\card-task-e9f573b2\.antiphon\checkpoints\20261016-100304-d10c\rows\CP-5\run.trx slot=granted waited=0s dirty=0 source=1fb704bd4614ab019553fda205a63261c0606e98 sourceState=clean buildSource=verified
PHASES CP-5 slotWait=0s build=0s startup=5.0052797s testsWall=163.2207443s teardown=0.4407808s hostWall=168.6684078s
wall: 9m35s  sequential-equivalent: 4m17s  builds: 2  max-concurrent-builds: 1  rows: 1 green 2 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1310-stream-final/
verdict: RED exit=1

.antiphon/checkpoints/20261016-104253-3680/report.md

source: 1fb704bd4614ab019553fda205a63261c0606e98 state=clean buildSource=unknown
CHECKPOINT CP-3 commit=1fb704bd4614ab019553fda205a63261c0606e98 build=failed filter=/*/*/OutputDistillationCanaryGuardTests/Configuration_refuses_unowned_resources_before_start executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s slot-reason=refused: tampered dirty=unknown source=unknown sourceState=unknown buildSource=unknown
wall: 9m31s  sequential-equivalent: 0m00s  builds: 1  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1310-stream-terminal-e2e/
verdict: RED exit=2

## Final retained output inventory

Automatic approval also rejected removal of the four remaining/new task-owned
bootstrap/interrupted/terminal output roots with blocked by policy. No workaround
was attempted. All 13 retained task-owned output roots follow; these are not
foreign outputs. The auxiliary detached checkout also remains registered for its
red-first evidence.

- .antiphon/c1310-r3/red-source/tests/Antiphon.Tests/bin-c1310-stream-red
- src/Antiphon.HostCleanup/bin-c1310-r3-tool
- src/Antiphon.HostCleanup/bin-c1310-r3-tool-red
- src/Antiphon.HostCleanup/bin-c1310-stream-tool
- tests/Antiphon.E2E/bin-c1310-r3-final-e2e
- tests/Antiphon.E2E/bin-c1310-stream-receipt-e2e
- tests/Antiphon.E2E/bin-c1310-stream-terminal-e2e
- tests/Antiphon.Tests/bin-c1310-r3-red
- tests/Antiphon.Tests/bin-c1310-stream-final
- tests/Antiphon.Tests/bin-c1310-stream-red
- tools/Antiphon.Checkpoints/bin-c1310-r3-tool
- tools/Antiphon.Checkpoints/bin-c1310-r3-tool-red
- tools/Antiphon.Checkpoints/bin-c1310-stream-tool
