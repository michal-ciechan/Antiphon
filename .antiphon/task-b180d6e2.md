# CARD-1490 Code task b180d6e2 report

Plan + launch-path trace: docs/superpowers/plans/2026-10-22-held-model-explicit-dispatch-plan.md

- Red witness (unlisted build-slot run, class filter `/*/*/HeldModelEscalationTests/*`) at
  8d407e7230ab79023ae2423b274511a8d85dcd36: total 4, failed 3. Both held-escalation cases red
  as designed (escalation succeeded onto the held fable tier); the non-held case red from a test
  seed bug (role Debug escalates straight to Frontier by role policy), corrected to Custom in the
  fix commit. Slot granted waited=0s held=485s.
- Fix 520d2cd41d4d04e09842a038711e1f80272da8aa: EscalateAsync RequireAsync before any mutation or
  kill (409 model_disabled); AutoEscalateStalledAsync skips a held target.
- Create with explicit -Kind/-Level on a held alias was already 409 model_disabled (no change).
- merge-tree vs origin/master 711409c41: clean. rc/2026-10-20 ceb6e3d6b: test then fix commits
  cherry-pick clean in sequence (plan doc stays master-only).
- Checkpoint CP-1..CP-4 results: see the final Code report message (run on the commit carrying
  this file).
- Not done (named in plan): next-chain-candidate in the refusal; Reply quota-resume re-reading the
  current hold; ordinary witness for the auto-clock skip (PC-2). Unit lane not run (brief: minimal).
