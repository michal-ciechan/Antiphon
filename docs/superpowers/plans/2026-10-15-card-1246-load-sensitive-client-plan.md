# CARD-1246 — load-sensitive agents/hosts/board client tests

**Date:** 2026-10-15 · **Stage:** Code · **Branch:** `feat/card-task-e10fbce9`
**Task base:** `78e4001788e99ebc04f0590a84db18442f6324c6`

Causes, change and measurements: `docs/superpowers/evidence/2026-10-15-card-1246-code.md`.

## Verification design

| ID | Test | Guards |
|---|---|---|
| V-1 | The six named files below, at idle | Settled/pasted tests pass with synchronous assertions; no timeout or skip added. |
| V-2 | Same files under the scratch CPU stressor (evidence file) | Measured before/after, disclosed; not a pass/fail gate. |

### Checkpoints

One run through the checkpoint tool (`run --plan`, `--row-timeout 40m`, `--total-timeout 150m`,
expected source SHA = committed HEAD). The tool bootstrap build is the one unlisted, slot-gated
build. The client row builds nothing.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---:|---|
| CP-1 | S2 | n/a | client-affected | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentHerdrPlacement.test.tsx src/features/agents/AgentsPage.test.tsx src/features/hosts/hostStatsHubReceipt.test.ts src/features/agents/AgentAttachHerdr.test.tsx src/features/agents/AgentCreateModal.test.tsx src/features/board/SessionTabs.test.tsx` | V-1 | 6 files; `Tests 46 passed`, 0 failed/skipped; `CLIENT TESTS EXIT CODE: 0` | n/a | 4 | true |

Census (source `it(` lines, no `skip`/`only`/`todo`): `AgentHerdrPlacement` 6, `AgentsPage` 25 plus one `it.each` over 2 cases = 27,
`hostStatsHubReceipt` 1, `AgentAttachHerdr` 4, `AgentCreateModal` 3, `SessionTabs` 5.
