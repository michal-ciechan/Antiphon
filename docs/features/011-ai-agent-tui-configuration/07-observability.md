# AI Agent TUI Configuration — Observability

## 1. Signals To Track

| What to track | Prometheus metric | Labels | Where it is used | Owner |
|---|---|---|---|---|
| Profile inventory and readiness | `antiphon_agent_tui_profiles` | `runner_type`, `enabled`, `validation_state`, `auth_mode` | Configuration health dashboard and release smoke | Antiphon maintainers |
| Protecting-key readiness | `antiphon_agent_tui_secret_protection_ready` | `protector_type` | Startup health, configuration banner, release smoke | Installation operator |
| Secret mutations | `antiphon_agent_tui_secret_operations_total` | `operation`, `outcome` | Security audit trend; never labels profile, environment name, or value | Antiphon maintainers |
| Model discovery runs | `antiphon_agent_tui_discovery_runs_total` | `runner_type`, `outcome`, `cache_result` | Discovery reliability and stale-cache diagnosis | Antiphon maintainers |
| Model discovery duration | `antiphon_agent_tui_discovery_duration_seconds` | `runner_type`, `outcome` | NFR-7 and timeout tuning | Antiphon maintainers |
| Cached catalogue age | `antiphon_agent_tui_model_cache_age_seconds` | `runner_type`, `availability` | Staleness dashboard and UI corroboration | Antiphon maintainers |
| Profile validation stages | `antiphon_agent_tui_validation_stages_total` | `runner_type`, `stage`, `outcome` | Setup troubleshooting and deployment gate | Antiphon maintainers |
| Profile validation duration | `antiphon_agent_tui_validation_duration_seconds` | `runner_type`, `outcome` | NFR-7 and stuck-child diagnosis | Antiphon maintainers |
| Launch resolution | `antiphon_agent_tui_launches_total` | `runner_type`, `outcome`, `model_mode`, `activity_mode` | Runner adoption, failures, exact/default model proof, degraded-mode use | Antiphon maintainers |
| Launch resolution duration | `antiphon_agent_tui_launch_resolution_duration_seconds` | `runner_type`, `outcome` | Startup and incremental runtime targets | Antiphon maintainers |
| File-definition import | `antiphon_agent_tui_imports_total` | `outcome`, `change_kind` | Migration and rollback evidence | Antiphon maintainers |
| Revision conflicts | `antiphon_agent_tui_revision_conflicts_total` | `operation` | UI concurrency diagnosis | Antiphon maintainers |

Metrics use only bounded labels. Profile names, profile IDs, revision IDs, model identifiers, executable paths, arguments, environment-variable names, provider output, and secret values remain in sanitized correlated logs when needed, never metric labels.

## 2. Human-Actionable Alerts

| Human-actionable event | Trigger / window | Metric / PromQL | PagerDuty route | Owner | Runbook | Expected action |
|---|---|---|---|---|---|---|
| Managed-secret protection unavailable while enabled profiles require it | Readiness is `0` for 5 minutes after startup or changes from `1` to `0` | `min_over_time(antiphon_agent_tui_secret_protection_ready[5m]) == 0` joined with enabled managed profiles | None for local installs; production route must be named before enabling paging | Installation operator | AI Agent TUI key-ring recovery | Restore the configured protector/key ring or disable affected profiles; verify decryptability without exposing values. |
| Profile import cannot establish a usable installation default | Import fails and no enabled default profile exists for 5 minutes | Increase in failed `antiphon_agent_tui_imports_total` plus zero ready default | None for local installs; production route must be named before enabling paging | Installation operator | AI Agent TUI migration rollback | Restore the prior file definition or repair the imported default, then rerun idempotent import and smoke. |

Discovery failures, stale catalogues, individual validation failures, revision conflicts, unsupported capabilities, and degraded PTY mode are not PagerDuty events. They remain visible in the UI, dashboard, metrics, and sanitized logs because an operator can choose another profile/model or retry during normal administration.

## 3. Logging

- `Error`: protecting-key loss with affected enabled profiles, unrecoverable import/default failure, or a confidentiality invariant violation; include owner action and correlation ID.
- `Warning`: discovery/validation timeout, cached-catalogue fallback, unsupported/degraded capability, failed child cleanup that is subsequently recovered, or rejected stale revision.
- `Information`: profile/revision lifecycle, secret set/replace/clear metadata, discovery/validation start and result, migration summary, and launch selection using profile ID, revision, runner type, and exact/default mode.
- Never log secret plaintext/ciphertext, submitted secret bodies, child environment blocks, full arguments, raw authentication output, or browser secret state. Model identifiers and paths are sanitized and bounded before diagnostic logging.

## 4. Dashboards And Ownership

- Dashboard: **AI Agent TUI Configuration Health** — key readiness, enabled profiles by runner/auth mode, validation state, discovery outcomes/age, launch outcomes/activity mode, migration, and revision conflicts.
- Deployment evidence: the **AI Agent TUI Profile Smoke** captures the dashboard snapshot plus sanitized validation, launch, and response results.
- Service owner: Antiphon maintainers. Installation-specific key custody, credential rotation, and local wrapper ownership: installation operator.
- Runbook ownership is transferred explicitly if a production PagerDuty route is later introduced; no page is enabled without a named route and responder action.

## 5. Open Questions

None for the specification baseline.
