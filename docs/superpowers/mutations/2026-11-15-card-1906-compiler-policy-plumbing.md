# CARD-1906 unbound compiler-policy plumbing candidate controls

Original Code owner: `28eecdf5-2364-4811-934c-f9fbdc10ef55`.
No source snapshot is bound. Deliberate mutants are owned only by caller-commissioned
SourceLanding Mutation. Operator waived Mutation checks for this Code dispatch on
2026-11-12; ordinary assertion reds do not discharge the controls below.
All IDs and variants remain **pending**. No red/restore/green mutant cycle ran.

| ID / variant | Candidate deliberate defect | Method-scoped witness |
|---|---|---|
| PC-1 / every refusal shape | accept one malformed saved assignment | RunnerLinuxCompilerPolicyTests/Saved_invalid_setting_refuses_before_side_effects (precisely bind each parameterized variant) |
| PC-2 / standing | remove standing wrapper policy export | RunnerLinuxCompilerPolicyTests/Saved_values_render_and_survive_each_named_phase_independently |
| PC-3 / temp | remove temp wrapper policy export | RunnerLinuxCompilerPolicyTests/Saved_values_render_and_survive_each_named_phase_independently |
| PC-4 / standing | drop standing env rewrite line | RunnerLinuxCompilerPolicyTests/Saved_values_render_and_survive_each_named_phase_independently |
| PC-4 / temp | drop temp env rewrite line | RunnerLinuxCompilerPolicyTests/Saved_values_render_and_survive_each_named_phase_independently |
| PC-5 / saved default | change missing saved input to 1 | RunnerLinuxCompilerPolicyTests/Saved_values_render_and_survive_each_named_phase_independently |
| PC-5 / Compose absent and empty | change Compose default to 1 | RunnerLinuxCompilerPolicyTests/Saved_values_render_and_survive_each_named_phase_independently |
| PC-6 / inherited 0 and 1 | remove the policy key at the session or wrapper boundary | RunnerLinuxCompilerPolicyTests/Rendered_policy_inherits_through_session_and_build_wrapper |

Mutation must discover missing controls, bind real committed source, retain per-PC
provenance, and use exact method treenode filters. Never weaken assertions, widen
timeouts or claim fixture/build errors or zero tests as a deliberate red.

## Explicit pending variants

- PC-1/empty: pending.
- PC-1/space-only: pending.
- PC-1/tab-only: pending.
- PC-1/leading-space-key: pending.
- PC-1/leading-tab-key: pending.
- PC-1/space-before-equals: pending.
- PC-1/tab-before-equals: pending.
- PC-1/space-after-equals: pending.
- PC-1/tab-after-equals: pending.
- PC-1/trailing-space: pending.
- PC-1/trailing-tab: pending.
- PC-1/crlf: pending.
- PC-1/boolean-true: pending.
- PC-1/boolean-false: pending.
- PC-1/numeric-two: pending.
- PC-1/leading-zero: pending.
- PC-1/lowercase-key: pending.
- PC-1/mixed-case-key: pending.
- PC-1/export-prefix: pending.
- PC-1/quoted-value: pending.
- PC-1/inline-comment: pending.
- PC-1/comment-without-space: pending.
- PC-1/indented-duplicate: pending.
- PC-1/tabbed-duplicate: pending.
- PC-1/spaced-duplicate: pending.
- PC-1/comment-looking-duplicate: pending.
- PC-1/case-duplicate: pending.
- PC-1/same-value-duplicate: pending.
- PC-1/later-duplicate: pending.
- PC-1/invalid-value: pending.
- PC-1/duplicate: pending.
- PC-1/shell-value: pending.

PC-2/standing, PC-3/temp, PC-4/standing, PC-4/temp, PC-5/saved-default, PC-5/Compose-absent, PC-5/Compose-empty, PC-6/session-0, PC-6/session-1, PC-6/wrapper-0 and PC-6/wrapper-1 are all pending.

PC-7/native-admission is pending: remove only the new native class admission row, re-seal policy metadata as necessary, and bind `NativeLinuxPolicyWitnessTests/Markers_do_not_expand_policy_exclusions` to its specific reverse-coverage assertion. Source/pin edits and restoration belong only to commissioned Mutation. The introduced ordinary guard red is preserved in Code evidence, not credited as a mutant cycle.
