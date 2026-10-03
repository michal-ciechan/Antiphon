# CARD-1011 Windows Grok qualification ledger

Status: **WQ-1 dropped by the operator; WQ-2 and amended WQ-3 MET. No activation is claimed.**
New Code/landing owner: `d422c5a9-e1b4-404d-9d28-438613671c15`, continuing
`b657a1e2-767d-4bcb-b25a-1cf2ed925be3` and `698c0e44-127d-4a7a-9584-7031570573e5`.
The [step-3 evidence note](2026-10-03-card-1011-code-d422c5a9.md) reconciles the
caller-supplied Windows evidence with the two tasks' retrieved full reports.
The [frozen plan](../superpowers/plans/2026-10-03-card-1011-windows-grok-routing-plan.md)
owns the exact procedure, filters, budgets and gate ordering.

Accepted canary: task `d6e4138e-fe11-4d7e-8ebf-b6a80b4fe597`, session
`26d8a18c-03f1-4563-b060-7ce6a1da7c8e`, source
`5f214b0c1daef4d6d7fbbbfbebd14deb83636639`. The plan accepted its complete
task-tagged UserPrompt at sequence 9, report at sequence 29 and server Stopped
state. Its transcript model was `grok-4.7-build`, dispatch model `grok-4.7`.
Reported Windows CLI: 1.0.41, build `4220f3b224a6`. These inherited facts do not
identify the actual backend, visible trust transition or runner release ownership.

| Row | Status | Evidence and remaining limits |
|---|---|---|
| WQ-1 inbox real Review | Dropped by operator; not passed | Legacy inbox backend is being deprecated in CARD-1022. No real inbox launch commissioned. Existing fake inbox regressions remain. |
| WQ-2 modern real Review | MET, task `02e5b9b7-9301-4324-b3e9-50fdbd929dc1` | Grok 1.0.46 (`2765805b9442` [stable]), ModernConPty 1.24.260710001, 120x30/ASCII > at row 26; Ready, UserPrompts 1 and 10, final report and clean pty-host exit. Required Review pin selected Grok/High; pins unchanged. |
| WQ-3 fresh-worktree Ready and one turn | MET at harness `189042dae13c605ed70d8fe9d6f94923ac1ae747`, Debug `d7e561a7-fa8b-4a0e-a74a-fee0fdb842d8` | Fresh detached worktree; trust not observed; startup inputs []; Ready after 6.6 s; one exact UserPrompt seq 1, nonce reply seq 3, TurnEnd seq 4, releaseConfirmed=True. Fresh one-result TRX, clean source and verified build provenance. |
| Backend restoration | No global change to restore in the accepted modern-only run | WQ-3 used a per-instance modern override; its report confirms no backend/settings/pin/default writes. Owned child/worktree/26 output directories removed, and provider auth/config/trust metadata unchanged. Caller owns any separate canonical configuration window. |
| Final/Full implementation Review | Pending | Independent ordinary Review binding clean committed source and verified build receipts |
| WQ-4 live Debug | Pending, post-activation | Effective pin with no kind/level/bypass, routing audit, source/bundle stamp, whole task receipt, useful report and release |

Each new row records full task/session/store/runner/source identities, CLI/build,
actual backend, terminal geometry, host-log path/hash, Ready observations and
complete received body/sequence, report and release owner. Preserve only this
task's bounded evidence. TrustBeforeFirstInput is an observation, true or false.
Missing Ready, complete turn, backend/version evidence or confirmed release fails
qualification. Never run login or copy/read/edit auth or trust stores.

Windows S0 has a separate commission: isolated stack preferred; canonical backend
experiments require the caller's configuration window and restoration. The probe
uses a local per-instance modern override, no global backend mutation. Its exact
Explicit filter is
`/*/*/RunnerGrokAdapterReadyTestsPty/C1011_real_fresh_worktree_ready_and_one_turn*`, minimum 1,
with `ANTIPHON_HEADED_TESTS=1`; ten minutes maximum. Ordinary CP-3 excludes it.

The success receipt is finalized only after confirmed child exit; failure observations
are retained. Capture complete `grok --version` output immediately before launch
and exact actual `backendLine` before prompt assertions, plus source/harness SHA,
runner/pty-host build identity, modern binary/package provenance and any differing
session-banner version. One bounded paid turn only; no automatic relaunch.

The supplied WQ-2/3 evidence permits the held prompt edit. Independent Final/Full
Review must bind the completed implementation before land; the live Debug pin
remains the caller's post-land activation work.
Activation: confirmed land -> canonical restart/version -> fresh pin/default/
runner reads -> approved Debug pin write/readback -> bundle stamp/idle refresh ->
WQ-4 -> acceptance -> SourceLanding Mutation. All PC-1..69 and their variants remain pending.
