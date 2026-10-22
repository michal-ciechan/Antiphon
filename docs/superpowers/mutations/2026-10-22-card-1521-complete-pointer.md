# CARD-1521 unbound Mutation candidate

Plan: `docs/superpowers/plans/2026-10-22-card-1521-complete-codex-pointer-plan.md`.
Original Code landing owner: `efe49438-7e31-4b87-8c99-39353812aa21`.
This is not a bound SourceLanding pack. Both PCs and all variants are pending.

- PC-1: substitute the former `ComposerDeliveryEvidence.IsVisible` result at the
  scoped whole-composer decision. Run only
  `/*/Antiphon.Tests.Application/SessionMessageQueueCodexPointerTests/C1521_Initial_consumption_is_complete`.
  The partial capture/prefix rows must fail their emitted-CR assertion.
- PC-2: bypass scoped recovery before the Pending head fall-through. Run only
  `/*/Antiphon.Tests.Application/SessionMessageQueueCodexPointerTests/C1521_Recovery_never_stacks_a_pointer`.
  Pending must fail on duplicated body writes.

Mutation owns deliberate red/restore/green, missing-control discovery and a precise
method-scoped bound filter compatible with the pinned runner. Budget eight minutes
per PC including builds, 16 minutes total. No Code green discharges either PC.
