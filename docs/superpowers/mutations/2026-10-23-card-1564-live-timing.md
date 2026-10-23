# CARD-1564 unbound SourceLanding Mutation candidate

Original Code/landing owner: `6e89ac58-d2e0-411e-a1f1-f9b27b3b2aa5`.
Plan: `docs/superpowers/plans/2026-10-23-card-1564-live-timing.md`.
All controls below are pending; ordinary red witnesses do not discharge Mutation.
Bind actual landed L after ordinary Review and publication. Use one exact method
per cycle, local inherited execution, intended assertion red (not fixture/build
failure), restore/fresh build/green. Retain evidence externally at assigned root.
Run missing-control discovery even if this inventory is replaced with zero PCs.

| PC | Proposed defect / variant | Guard class / exact method |
|---|---|---|
| PC-1 | suppress startup record / suppress Activity span | LiveTimingTests / V1_Startup_records_monotonic_duration_and_correlated_span |
| PC-2 | default incomplete scope to completed / publish twice | LiveTimingTests / V2_Incomplete_scope_is_failed_and_disposal_is_idempotent |
| PC-3 | suppress slow warning / warn below threshold / retain external history | LiveTimingTests / V3_External_calls_warn_only_over_threshold_and_do_not_fill_startup_history |
| PC-4 | remove sink containment / remove 64-record bound | LiveTimingTests / V4_Sink_failure_does_not_replace_application_outcome_and_snapshot_is_bounded |
| PC-5 | omit startup route / return a new recorder | StartupEndpointTests / V5_Diagnostics_returns_the_live_instance_without_payloads |
| PC-6 | add URI/header/body/exception text as a tag (separate variants) | StartupEndpointTests / V6_Http_span_omits_uri_headers_body_and_exception_text |
| PC-7 | omit async failure/cancellation / change scalar/nonquery/reader results | ExternalBoundaryTests / V7_Db_callbacks_preserve_results_and_measure_success_failure_cancellation |
| PC-8 | omit git scope / omit directory scope / add path or args tag | ExternalBoundaryTests / V8_Git_and_filesystem_boundaries_emit_safe_spans |
| PC-9 | omit seed / migration / host-start completion (separate variants) | LiveStartupIntegrationTests / V9_Real_server_reports_startup_phases_after_listeners_are_ready |
| PC-10 | omit finally record / mark failure completed / serialize action output | StartupScriptTests / V10_Powershell_preserves_values_and_failures_without_logging_payloads |

Provisional ordinary budget: small isolated build plus ten cases, no full assembly.
Mutation cost is separate: one fresh build and exact-method red/restore/green per
independent variant; first measure and retain actual costs before scheduling more.
Windows launcher native R-1 is a separate ordinary acceptance obligation, not a PC.
Restart target: server and session runner; Windows AppHost launcher changes also
require the native probe. Mutation never restarts or deploys from its snapshot.
