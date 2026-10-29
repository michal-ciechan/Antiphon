# CARD-1589 gate-2 canary plan

## Decisions
D-1: Limit execution to reading CARD-1589 and publishing its short canary note; this proves delegate Git publication without changing rollout state. Reject deployment or fixture execution because the brief authorizes documentation only.
D-2: Treat the card's failure as historical: the current predicate already admits a non-retiring hold. Reject another speculative fix; the next concrete step is Review of the existing fix and its exact-source receipts.
D-3: Use live runner defaults/catalogue for placement; both GETs were read successfully. No runner or OS pin is needed in this documentation plan.

## Ground truth
| Card assumption | Checked-out behavior |
| --- | --- |
| Inventory requires retirement for an absent temp. | `scripts/c590-remote.sh`, `case_runner_cache_inventory`, now accepts retired or non-retiring holds with offline, non-accepting, zero-work status. |
| Absence must be proven rather than inferred. | The inventory also requires an empty all-service census, including stopped containers. |
| Original failure needs resolution. | A code predicate now exists; its passing runtime verification is not established by this canary. |

## Slices
S-1: Write `docs/superpowers/plans/2026-10-29-canary-gate2-note.md` (at most 15 lines) and this required Plan-stage artifact; inspect the diff, commit on the assigned branch, push, and confirm the remote SHA. No source or tests change.

### Checkpoints
No build/test checkpoints: documentation-only lane. Check note line count, exact changed-path roster, whitespace, branch identity, and published SHA. No class filters or suffix-collision roster applies.

## Performance budget
No tests are added, changed, or run: this canary verifies documentation publication only. Expected total work is below the brief's 10-minute cap; no test/row/batch duration or measured p95 exists. Setup, container readiness, data preparation, host startup, migrations, database isolation and teardown are not invoked; shared expensive setup is unnecessary. Build and slot admission are not invoked. HTTP/CLI observations remain in the task transcript, with operation durations in command results; no per-test timing artifact exists. No test timeout is set; future fixture verification must record phase durations and derive timeouts near 2x measured p95 under docs/testing-and-build.md#performance-standards, rather than treating this canary's deadline as measured execution evidence.

## Verification design
Folded documentation verification only: inspect both Markdown diffs, ensure the requested note has at most 15 lines, run `git diff --check`, and compare the full committed SHA with `git ls-remote` for the assigned branch. Review checks the stated predicate against `scripts/c590-remote.sh` and does not credit this note as a Fixture/Inventory pass. No product verification or production acceptance is claimed; next stage is Review as explicitly commissioned.
