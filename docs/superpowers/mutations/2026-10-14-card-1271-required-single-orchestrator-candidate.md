# CARD-1271 unbound Mutation candidate

Every control is pending. Code ran no deliberate mutations. Ordinary Review and the
caller's landing of Code task `4fc0b3a4-4824-4feb-8a8a-d790c64d9581` come before
SourceLanding Mutation.

Plan: `docs/superpowers/plans/2026-10-14-card-1271-required-single-orchestrator.md`.
Project: `tests/Antiphon.Tests` (Integration, isolated schemas). Restart: server.

Bind these candidates to landed source. Mutation owns the deliberate mutants, the
red/restore/green evidence and missing-control discovery. Every cycle uses one
`/*/*/Class/ExactMethod` filter through the host build gate. PC-1 and PC-3..PC-6 edit
the same create arm in `AgentTaskService.CreateAsync`, so restore between cycles. Build
errors, fixture errors and zero tests are not red proof. Never widen a timeout or loosen
an assertion.

| PC | Deliberate defect | Class/ExactMethod | Expected red | Status |
|---|---|---|---|---|
| PC-1 | Delete the `AdmitsKind` refusal block in the Required single-candidate arm | `RequiredSingleCandidateRefusalTests/An_orchestrator_with_complexity_over_one_codex_row_is_409_routing_exhausted` | no RoutingExhaustedException; task created on Codex | Pending |
| PC-2 | Drop the Orchestrator clause from `ComplexityRoutingService.AdmitsKind` | `RoutingPinCandidateCreateTests/Orchestrator_skips_non_Claude_pin_candidates` | Grok chosen instead of ClaudeCode | Pending |
| PC-3 | Make the arm refuse every Orchestrator whatever its kind | `RequiredSingleCandidateRefusalTests/An_orchestrator_with_complexity_over_one_held_claude_row_is_409_model_disabled` | RoutingExhaustedException thrown instead of ModelDisabledException | Pending |
| PC-4 | Swallow `ModelDisabledException` in the arm | `RequiredSingleCandidateRefusalTests/A_forbid_only_required_pin_over_one_held_default_row_is_409_model_disabled` | no exception; task created | Pending |
| PC-5 | Skip `EnforceForbiddenAliases` in the arm | `RequiredSingleCandidateRefusalTests/A_forbid_only_required_pin_that_forbids_the_one_default_row_is_409_routing_pin_forbidden` | no exception; task created | Pending |
| PC-6 | Skip the quota gate `EnforceAsync` in the arm | `RequiredSingleCandidateRefusalTests/A_forbid_only_required_pin_over_one_quota_low_default_row_is_409_subscription_quota_low` | no exception; task created | Pending |

PC-2 also turns PC-1's method red; the narrowest witness is the existing pin-walk method.
PC-3's witness is the held ClaudeCode Orchestrator (D-1 repair, Review f28f0f79). Do not bind
it to `An_orchestrator_with_complexity_over_one_claude_row_is_created_on_it`: that control takes
the walk's chosen branch, never the arm, and stays green under PC-3. PC-4 also turns the held
Orchestrator method red. Code task `7551b8d4` ran PC-3 once in a scratch copy to show the new
method can go red; that demonstration does not discharge PC-3.
