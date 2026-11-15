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
