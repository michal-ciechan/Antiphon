# CARD-1523 / CARD-1524 Code verification supplement

Owner: original Code task `ccf88b5d-eab9-418c-b94e-28bba2031e21`.

The landed plan is [the checkpoint manifest](2026-10-22-card-1523-1524-phone-home-backpressure-plan.md). This supplement finalizes its verification design as directed by the Code brief. The fifteen checkpoint rows, filters, counts and build groups are unchanged.

## Verification design

The Code brief for task `ccf88b5d` explicitly skips a separate TestDesign dispatch
and selects the fifteen narrow Linux rows below under the operator's minimal-testing
rule. This Final round's ordinary scope is this closed manifest: the whole Unit
lane and whole affected classes are deferred, not passed. There is no Windows/native
row. The five baseline methods compiled and failed at their intended assertions at
`9db2c7f71aa3c0b7366b0ce4f85f32ee55ff8d68`; the earlier fixture build failure is
retained separately and is not a red witness. Code adds the guard variants below
inside the same six methods, keeping the checkpoint roster/counts unchanged.

The fixture owns real Kestrel, the real runner service/event hub, hosted recovery
pump and runtime persistence in isolated schemas. Only command inventory, a
post-catch-up List hold, publication and activity-save dependencies are controlled.
The protocol cases also reuse the existing channel-backed WebSocket writer fixture
inside the actual runner service. The old-server registration shim removes the
capability and response field on the wire; it leaves the new runner sender intact.

Ordinary assertions cover finite count and byte stalls, catch-up under byte pressure,
original output/mention/prompt/exit recipients, cross-session progress and FIFO,
worker ceiling and failure isolation, foreign ownership/stale epoch/generation,
joined replacement after an uncooperative observer, UTF-8 accounting and idempotence,
50%/90% warnings, immutable refusal state, bounded producer diagnostics, ongoing
publish/activity attribution, both mixed versions, ticket-bound limits, duplicate/
stale/impossible/nonmonotonic credits, malformed negotiation, oversize refusal,
release during update send, cancellation, and sustained-overload UUID recovery.

Positive controls remain **pending for SourceLanding Mutation**. Code does not
introduce deliberate mutants. The unbound candidate and per-variant method targets
are in `docs/superpowers/mutations/2026-10-23-card-1523-1524-candidate.md`.

