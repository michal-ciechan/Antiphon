# CARD-1004 Linux Grok composer marker

The dispatch brief is the implementation authority. Preserve the CARD-0778 Windows
fixtures/tests; accept only ASCII `>` and U+276F as the empty composer interior.
The qualification remains 120x30 with borders at columns 2 and 117. CARD-0861 owns
other sizes. No runner image, runtime delivery or trust/sign-in predicate changes.

S1 commits the two decoded Linux frames and positive tests against the unchanged
classifier. S2 commits the exact two-marker predicate, negative/settle tests, adapter
coverage, opt-in FakeGrok marker and platform qualification docs. All normal runs
use the committed source with `-ExpectedSourceSha`. CP-2/CP-3 are the brief's scratch
mutation diagnostics over committed S2: each has one stable dirty source file,
omits strict `-ExpectedSourceSha`, uses an isolated build, must fail at an outcome
assertion, and is restored byte-for-byte with an empty Git diff before proceeding.
They do not discharge any method-scoped SourceLanding Mutation PC.

## Verification design

V-1: Both Linux captures classify Ready, with 30 rows, maximum rendered width 118,
U+276F at row 25/column 4, borders 2/117 and pinned UTF-8 screen digests
(`GrokLinuxStartupReadinessTests.Captured_linux_dashboard_is_ready`).
V-2: Tracker settlement requires two positive observations and a whole second;
reject ghost/typed text, U+2192, U+203A, `x`, status/spinner, wrong hints, 29 rows,
121 columns, sign-in and trust (`GrokLinuxStartupReadinessTests`).
V-3: Runner adapter settles both original Linux captures without startup input
(`RunnerGrokAdapterReadyTests`). FakeGrok's default ASCII marker and opt-in U+276F
both traverse the native PTY, TerminalScreen and adapter, then produce exactly one
complete UserPrompt for a single-line nonce (`RunnerGrokAdapterReadyTestsPty`).
The .NET fake's Unix console can convert CR to LF and coalesce multi-line paste
with Enter even after a shell raw-mode preamble. Use its existing Linux
`ANTIPHON_FAKE_LF_ENTER` opt-in for the nonce. This does not qualify real Grok
multi-line paste/Enter behavior; that remains the real-provider canary's job.
R-1: Unchanged Windows fixture and readiness regression tests, capture privacy,
sign-in/trust predicates and adapter factory (`GrokStartupReadinessTests`,
`GrokStartupCaptureStoreTests`, `GrokSignInPromptDetectorTests`,
`GrokTrustPromptDetectorTests`, `GrokAdapterTests`). Linux execution of Windows
fixture replay does not confirm Windows ConPTY; that is a separate Windows task.
R-2: One whole Unit lane at the final SHA; report known Windows-only and missing-jq
skips separately from failures. No real provider/auth/model call is authorized here.

Manual rollout acceptance belongs to the orchestrator: after Final Review/land,
activate the server through the canonical AppHost restart, verify `/api/version`
against source HEAD, then run the unpinned Grok canary on server2-temp. Require a
complete matching UserPrompt transcript receipt. Linux sign-in/trust UI variants,
real CLI Enter/paste semantics, updater/version drift and other terminal sizes
remain real-provider qualification risks. The two captures qualify screen shape,
not a completed real Linux model turn.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1-red | S1 | `tests/Antiphon.Tests -> bin-c1004-red/` | linux-red | `/*/*/GrokLinuxStartupReadinessTests*/*` | V-1 | 2 failed with ComposerUnavailable before fix | 2 | 8 |
| CP-2-remove | S2 scratch | `tests/Antiphon.Tests -> bin-c1004-remove/` | remove-marker | `/*/*/GrokLinuxStartupReadinessTests*/*` | V-1, V-2 | new positives fail without U+276F acceptance | 21 | 8 |
| CP-3-any | S2 scratch | `tests/Antiphon.Tests -> bin-c1004-any/` | arbitrary-marker | `/*/*/GrokLinuxStartupReadinessTests*/*` | V-2 | uncaptured single-character marker negatives fail | 21 | 8 |
| CP-4-final | S2 | `tests/Antiphon.Tests -> bin-c1004-final/` | affected | `/*/*/(GrokStartupReadinessTests*)\|(RunnerGrokAdapterReadyTests*)\|(GrokStartupCaptureStoreTests*)\|(GrokSignInPromptDetectorTests*)\|(GrokTrustPromptDetectorTests*)\|(GrokAdapterTests*)\|(GrokLinuxStartupReadinessTests*)/*` | V-1, V-2, V-3, R-1 | every named full class, 0 failed/skipped | 50 | 8 |
| CP-5-unit | S2 | `tests/Antiphon.Tests -> bin-c1004-unit/` | unit | `/*/*/*/*[Category=Unit]` | R-2 | whole Unit lane, 0 failed; report platform/tool skips | 2000 | 8 |
