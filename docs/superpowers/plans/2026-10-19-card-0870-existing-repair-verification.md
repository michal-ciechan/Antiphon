# CARD-0870 existing repair verification

Code task / landing owner: `34eae1e5-0e77-4009-8ddc-f55d928ac126`.
Assigned branch: `feat/card-task-34eae1e5`.
Base: `a0f3c4f3e728655933da1782981a88fb2b7b3a56`.

The brief's implementation already exists at the assigned base:

- `d75783b811e18453cf2e7bf8841a4b135d898896` registers the session-state store,
  replaces the retired kill/retry assertions with detection, and updates C1162.
- `cb67130bbe8523f970ea10af8dcfe37d28edff15` registers the runtime-action
  coordinator and deletes its rows before deleting transcript rows in cleanup.

No production requeue repair is commissioned. The explicit-retry slug collision
remains outside this harness repair, as the investigation and witness comment state.
Do not undo landed fixes to manufacture a red-first run. The historical red and
Claude-isolation result are in
`docs/investigations/2026-10-17-card-0870-investigation.md`; they are not fresh
execution evidence for this task's SHA. Deliberate mutants belong to Mutation.

The server confirms original Code owner `2a687183-07bc-44e9-af70-6c806311ec8d`
and Review `ef093d32-5bd4-4c4e-ad70-dc5f09f084b9`. Landing operation
`ba47b99e-e0c9-4050-99ce-b3d10999d0bc` published the repair to master at
`cb67130bbe8523f970ea10af8dcfe37d28edff15` on 2026-10-18; cleanup was refused
with `evidence_retention_unavailable`. This task must not claim that publication
as its own. The prior Review reported a separate inherited settlement-worker
defect, CARD-1334, affecting other members of the same harness class.

The first CP-1 execution at `2fcbccfe873911c5a2d6d8d34ef940a442d42637`
built successfully but matched zero tests (exit 3), so it proves no witness.
The CP-1 filter below corrects the selector to the documented method-segment OR
with trailing wildcards. Require all three intended TRX identities, not just a
successful process exit. Historical evidence remains unchanged.

## Verification design

S1 records the already-present implementation and the bounded verification selection.
S2 records the results and any remaining gate. There are no new test methods.

| ID | Obligation | Test selection |
|---|---|---|
| V-1 | Real Windows ConPTY rules ACK and pointer delivery reach the detection sweep; two sweeps retain the same session, Dispatched, attempt 1, null failure, no kill/retry, and one warning. | `GrokDelegateEndToEndTests.a_provider_that_never_answers_boot_is_detected_until_explicit_retry` |
| R-1 | Claude launch, inline brief and pricing remain functional in the shared harness. | `GrokDelegateEndToEndTests.a_ClaudeCode_worker_still_launches_claude_types_its_brief_inline_and_prices_on_the_claude_ladder` |
| R-2 | C1162 names the detection witness and rejects both retired names. | `BootStallDocumentationTests.C1162_Grok_conpty_witness_name_states_detection_until_explicit_retry` |
| R-3 | Final Unit lane, including the full affected `BootStallDocumentationTests` class. | `/*/*/*/*[Category=Unit]` |
| R-4 | Final full affected integration class. | `/*/*/GrokDelegateEndToEndTests/*` |

The brief simultaneously requires one narrow green run/no suites and a Final
whole Unit/full affected class profile. CP-1 is the common authorized selection;
CP-2/CP-3 remain required for Final and are not passed or waived by CP-1. Scope
clarification was requested before execution. No full assembly is proposed.
Each executed row has an explicit five-minute cap; no test deadline is widened.
If a row cannot finish within that cap, retain the incomplete gate.

Required manual work: inspect the inherited commits, the fresh TRX roster and
source/build receipts, run the evidence diff guard over base..HEAD, and check
merge-tree against freshly fetched origin/master. No activation is required.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c0870-34eae1e5/` | narrow-existing-repair | `/*/*/*/(a_provider_that_never_answers_boot_is_detected_until_explicit_retry*)\|(a_ClaudeCode_worker_still_launches_claude_types_its_brief_inline_and_prices_on_the_claude_ladder*)\|(C1162_Grok_conpty_witness_name_states_detection_until_explicit_retry*)` | V-1, R-1, R-2 | all 3 methods, 0 failed/skipped | 3 | 5 | true |
| CP-2 | S1 | CP-1 | final-unit | `/*/*/*/*[Category=Unit]` | R-3 | all discovered Unit cases, 0 failed/skipped | 1 | 5 | true |
| CP-3 | S1 | CP-1 | final-grok-class | `/*/*/GrokDelegateEndToEndTests/*` | R-4 | all 6 methods, 0 failed/skipped | 6 | 5 | true |

### Cost

Ordinary Final estimate: 15 minutes plus tool bootstrap and slot waits. CP-1's
current bounded estimate is five minutes including its build. The latter two
rows are an obligation, not a claim they can finish inside the requested cap.
The checkpoint tool bootstrap is an explicitly explained support build outside
the test table; it runs under the host build slot into its own alternate output.

### Mutation handoff

No new PCs or variants are introduced by this verification-only slice. All
inherited controls and missing-control discovery remain pending for method-scoped
SourceLanding Mutation. Candidate:
`docs/superpowers/mutations/2026-10-19-card-0870-existing-repair-candidate.md`.
