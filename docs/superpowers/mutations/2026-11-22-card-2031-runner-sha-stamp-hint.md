# CARD-2031 unbound Mutation candidates

Code and landing owner `3a33b568`. See the
[checkpoint plan](../plans/2026-11-22-card-2031-runner-sha-stamp-hint.md).
All variants are pending for method-scoped SourceLanding Mutation.

| ID | Deliberate defect | Detecting method | Expected red |
|---|---|---|---|
| PC-1 / suppress hint | Remove the advisory output line. | `/*/*/RunnerRestartScriptCompatibilityTests/Sha_stamp_hint_is_advisory` | The unknown, null and short builds miss the hint. |
| PC-1 / warn on every stamp | Ignore the SHA format on a reported build. | `/*/*/RunnerRestartScriptCompatibilityTests/Sha_stamp_hint_is_advisory` | The full-SHA cases get an unexpected hint. |
| PC-1 / warn after hard refresh | Remove the `-Hard` guard. | `/*/*/RunnerRestartScriptCompatibilityTests/Sha_stamp_hint_is_advisory` | The hard-refresh case gets an unexpected hint. |
| PC-2 / runbook, bootstrap | Remove the sentence requiring the initial supervisor refresh from one doc at a time. | `/*/*/DockerStackDocumentationTests/First_desktop_runner_restart_after_sha_activation_requires_hard` | The corresponding document's command requirement is absent. |
