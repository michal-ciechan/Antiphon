# CARD-0167 S6: docs and operator-owned canaries

Code task / landing owner `db635bb0`, branch `feat/card-task-db635bb0`, base
`5b44186fc19c6a8612e7b17d1647bdd2fa89679f`. Master only. Extends the
[landed plan](2026-10-22-card-0167-provider-endpoint-plan.md), incorporating the S3
repair's launch-time pool stamp and Endpoint-mode replacement semantics.

The parent plan's project PATCH instruction is corrected to the shipped PUT route. Project and
agent update examples name endpoint members, and require preserving the other required fields.

The explicit S6 brief limits this round to minimal verification: no Unit lane or whole suite.
CP-7 below is unchanged from the parent plan (the S6 prose's reference to CP-6 was a typo;
CP-6 is S5's client row). CP-9 adds the requested portable sentinel variant, with both cases
of its full affected integration class. It reuses CP-7's build. The real Codex class compiles
but is excluded from checkpoint selection: it remains Explicit/Windows/opt-in, and a skip
cannot satisfy a checkpoint. No production code changes.

The new sentinel class drives real AgentControl Start -> persisted project map/key lookup ->
capturing adapter, then clear -> stop/fresh Start. ClaudeCode and Codex each check the scoped
sentinel key, routing env/argv and absence on the next launch. It does not launch a CLI, read
an auth file/home, call a provider or claim delivery. The real Codex canary instead uses the
production runner adapter and a private local stub, guarded before process creation against
missing endpoint projection. Its key and home are synthetic; its dual-hit result remains
operator-owned. The live Claude proxy/key procedure is also operator-owned. Both exact
procedures are in [agent-kinds.md §7](../../agent-kinds.md#7-pointing-a-cli-at-a-different-api-endpoint).

Red witness: committed `32d9b64395f7377bbdaac9e0f9912af421613a0b`, all three docs tests failed
on missing setting row, production-setting guidance and projection table. This is the parent
plan D-14's unlisted build-slot run, slot granted, waited=0s, held=273s, executed=3 failed=3
skipped=0. Fresh TRX: `.antiphon/c0167-s6-red/_0c18bb7f591e_2026-10-22_21_03_45.9251813.trx`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-7 | S6 | `tests/Antiphon.Tests -> bin-c0167f/` | docs-pins | `/*/*/(ProviderEndpointDocumentationTests*)\|(FakeLlmApiSelfTests*)/(C0167_*)\|(ForClaude_sets*)\|(ForGrok_requires*)\|(ForCodex_emits*)` | docs pins; rewritten Explicit canary compiles | 6 passed, 0 failed, 0 skipped | 6 | 10 |
| CP-9 | S6 | CP-7 | sentinel-agent-launch | `/*/*/ProviderEndpointSentinelCanaryTests/*` | scoped sentinel key through agent launch; set/clear for ClaudeCode and Codex | 2 passed, 0 failed, 0 skipped | 2 | 2 |

### Cost and pending acceptance

One isolated assembly build, two bounded filters: estimated 12 minutes plus the D-14 red run.
No full assembly selection or unbounded shared impact. The checkpoint tool bootstrap may reuse
the tool compiled by the red build; it is orchestration, and its build/test children take slots.
The final source/report commit must precede this run; pass that HEAD as expected-source-sha.
Validate report.json and inspect fresh TRX for every method/argument case and nonzero counts.

No V-n or R-n IDs belong to S6 in the parent plan. Earlier S3 R1 repair evidence is inherited,
not rerun or claimed here. Required S6 ordinary rows are CP-7/CP-9. Unit lane and earlier-slice
checkpoints are deferred by the explicit brief. Operator acceptance 1 (real Codex stub) and 2
(live Claude proxy set/clear and transcript) remain pending until the operator executes them;
sentinel green is not a substitute. There are no PC-n rows in the original plan. All deliberate
mutants, red/restore/green cycles and missing-control discovery remain pending for SourceLanding
Mutation, including this zero-PC plan. Next: ordinary Review, then caller lands original Code
task and commissions SourceLanding Mutation. Restart: none for this docs/test slice.
