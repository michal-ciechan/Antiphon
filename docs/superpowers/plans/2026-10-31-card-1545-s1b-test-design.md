# CARD-1545 S1b admission and outcomes: test design

Scope: S1b-admission and S1b-outcomes only, against
`f971a87c673d34083a1df395405410419b22102c`. This companion appends verification
detail to the [landed fix plan](2026-10-31-card-1545-rc-scoped-lanes-live-claude-lane-plan.md);
it does not amend that plan or commission S1a/S1c. The first document slice records
inspection and scope; the completed verification inventory follows in the next slice.

Read both `.antiphon/task-a62610b7.md` and `.antiphon/task-917262f2.md`.
The latter's section (4) explicitly permits these two portable slices despite its
S1c defects. D-3/D-4/D-5/D-6/D-9/D-11 remain PROVISIONAL. Fixtures use opaque profile,
executable and account identities; they choose no credential host, model, reuse
policy, issuer or activation policy. All future results from these slices are
diagnostic, with no Full or release credit.

## Verification design

### Inspection

- Landed plan: Execution/credentials/lock/outcomes, typed setup table, implementation
  slices, performance budget and CP-2/CP-16; both review reports in full for the
  affected defects/advisories. Boundaries: wrapper admission and classifier only.
- `tests/Antiphon.Tests/Scripts/ReleaseGateRunTests.cs` and
  `scripts/lib/release-gate.ps1` native-lock acquisition/release bodies: real scratch
  lock ownership, refusal and continuation identity are the closest admission fixture.
- `tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs` and
  `tests/Antiphon.Tests/Scripts/ScriptHarness.cs`: offline slot/command seams,
  assembly process limiter, exact named PASS inventory and exit assertions are the
  nearest pattern for the two new test classes. A zero assertion count is not success.
- `docs/testing-and-build.md` checkpoint runner/manifest, build-slot, mutation,
  asynchronous-delivery sections; `docs/test-performance-standards.md`; orchestration
  D-2 advisory policy. No test execution is authorized by this documentation task.

### Delivery inventory

These slices add no message queue, recipient session input or asynchronous user
notification. They orchestrate a local owned checkpoint child and classify its
terminal evidence. The post-run transcript input is a synthetic classifier fixture,
not delivery evidence. Real queue busy/eligible recipient and crash/enqueue recovery
tests belong to a changed delivery path; none is changed here. S1c must independently
prove its complete matching UserPrompt and bridge/turn oracles. This document cannot
credit those paths from a request, ack, event, queue insert or child exit.

The local wrapper-to-child handoff and terminal observation boundaries will be
enumerated below, including durable run/source/row identity and refusal on incomplete
evidence. Portable substitutes do not prove Windows custody, paid caller wiring,
provider availability or actual prompt receipt.
