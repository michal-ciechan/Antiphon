# CARD-1543 unbound Mutation candidate

Original Code/landing owner: dbf0540f-0274-4d61-85e4-e2c679c61895.
Every control and variant is pending. Mutation owns method-scoped deliberate
red/restore/green and missing-control discovery.

- PC-1: reintroduce redundant metadata lookup, separately in canceled-body,
  late-confirm, held-back and pointer checks; exact SQL budget must fail.
- PC-2: bypass fresh body write fencing; visible/absent/Unknown dialog and changed
  generation/revision variants must refuse without input.
- PC-3: bypass fresh Enter fencing after body; the same dialog/generation/revision
  race variants must retain the attempt without submitting Enter.
- PC-4: corrupt known Codex kind on pending/canceled/capped/expectation paths;
  pointer recovery must reject stacking and preserve complete receipt authority.

Bind after ordinary Review and landing. No control execution is claimed.
Restart server; runner none.
