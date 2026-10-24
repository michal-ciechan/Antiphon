# CARD-1575 Code verification contract

Original Code/landing owner: `8ac0836f-185e-4867-ba36-9126cc761a4f`.
Branch: `feat/card-task-8ac0836f`; base: `a640c8614c6358839c125326049c9f08e174bc1a`.
Round: Final. Restart: none. Test-only repair; no production or timeout changes.
The brief explicitly commissions direct Code without a separate Plan stage.

Investigation source: commit `75e47d0ad64057f40b609933c3db74cff2135073`,
`docs/investigations/2026-10-24-card-1575-herdr-claude-parity-tests-expect-automated-input-despite-unknown-custody.md`.
CARD-0680 in `docs/session-runtime-invariants.md` owns the Unknown custody refusal.

## Coverage and acceptance

All affected cases belong to `HerdrAlwaysOnChannelParityTests` (three partials).
Only its main and StandingRecovery partials change.

| ID | Required observation |
| --- | --- |
| V-1 | Original named Claude row: exact Unknown refusal, Failed agent/session, incomplete launch, zero automated writes/UserPrompt receipts; actual labelled placement captured before failed-start cleanup. |
| V-2 | Claude launch-definition row: same refusal plus existing backend/kind, definition, pane/tab rename and slug proofs. Grok/Codex retain running/adopt/empty-pane exit assertions. |
| V-3 | Both Claude/Herdr history rows: fresh/fresh/resume identity, exactly one resume argument, current policy environment, unchanged seeded native history, transferred pending channel body with zero delivery attempts and no reply. |
| R-1 | Entire affected class, including supported Grok and PtyHost history/receipt/reply proofs, missing-history/hold rows, and a Grok named-placement control retaining all successful repeated-resume/specialist-tab assertions. |
| R-2 | Whole Antiphon.Tests Unit lane required by the Code stage contract. |
| N-1 | Native Windows rerun: full `/*/*/HerdrAlwaysOnChannelParityTests/*`; Linux's six existing skips remain skipped, never credited as passes. Caller owns native scheduling. |

No new skip, timeout widening, retry or guard bypass is authorized.
The successful-launch idempotence check applies to supported completed launches;
a refused Claude launch is explicitly Failed and cannot supply that premise.
Seeded fake native history proves preservation only, never prompt acceptance.
No manual product acceptance is requested for this test-only change.

## Performance budget

Historical Linux source `8cf9943ca1968bd101eac8ce00b377d0f0fa469c` ran group 35a
in 107.054 seconds (62 cases across four classes); cold build 458.34 seconds.
This task's base isolated build measured 194.61 seconds, 1,197 existing warnings,
zero errors. Its named-row witness is 0 passed / 1 failed, exact Unknown refusal,
at the committed base, with build-slot wait 30 seconds and lease held 73 seconds.
Evidence: `/work/test-evidence/task-8ac0836f/base/`, `base-test.log`,
`base-provenance.json`. The first build invocation failed before compilation
because an in-process PowerShell call split a switch; the corrected gated build
is retained separately as `base-build-retry.log`.

CP-1 provisionally budgets 6 minutes including build; CP-2 3 minutes, based on
the documented historical 70.29-second Unit sample (1992 cases, older source).
These are bounded collection estimates, not current p95 measurements. No test
timeout changes. Checkpoint's inherited 15-minute row/build floor is retained;
CARD-1404 owns measured-budget tooling and CARD-1514 missing instrumentation.
Shared database initialization remains assembly-owned; fixture temp/native homes
remain isolated. Retain TRX per-case times, phase sidecars and checkpoint build,
admission and execution timings. Uninstrumented external calls/phases are unknown.
No assembly-wide run is selected. A run over 30 minutes requires Debug.

The initial gated baseline build and exact-method red run are deliberate
pre-change evidence, additional to the closed post-change checkpoint selection.
Use the .NET checkpoint tool built by that baseline: its streaming output digest
supports the large DLL; the PowerShell script's 64 MiB byte reader is avoided.
The tool takes build slots itself, runs compiled output with `--no-build`, and
receives committed HEAD via `--expected-source-sha`. Run sequentially.

## Pending SourceLanding Mutation inventory

All controls remain pending: PC-1 exact Unknown refusal/no backend writes (named
Claude, launch-definition Claude, both history bool variants); PC-2 no attempted
queue delivery/receipt/reply (both history bool variants); PC-3 real named-tab and
specialist preservation across repeated resume (Grok); PC-4 identity/argv/current
environment/history preservation (both Claude/Herdr bool variants and supported
Grok/PtyHost controls). Mutation owns deliberate mutants and missing-control
discovery; this Code task executes no mutant cycles.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1575-final/` | herdr-parity | `/*/*/HerdrAlwaysOnChannelParityTests/*` | V-1, V-2, V-3, R-1 | >= 19 executed, 0 failed; six existing Linux skips retained | 19 | 6 |
| CP-2 | S1 | CP-1 | unit | `/*/*/*/*[Category=Unit]` | R-2 | >= 1992 executed, 0 failed; native skips reported | 1992 | 3 |
