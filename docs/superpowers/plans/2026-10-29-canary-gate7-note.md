# Gate-7 canary: CARD-1589
CARD-1589 concerns cache Fixture F-9 rejecting an absent offline temp runner in a non-retiring deployment hold.
## Decisions
D-1: Keep this canary documentation-only; reject rollout or implementation changes because the brief authorizes one short note.
D-2: Verify the existing fix before proposing another patch; current source already admits the failed-deployment hold with status and host-absence proof.
## Ground truth
| Card assumption | Current checkout |
|---|---|
| Inventory requires retirement for absent temp | `scripts/c590-remote.sh`, `case_runner_cache_inventory`, accepts retired or non-retiring hold with offline zero-work status and empty whole-project census. |
| Resume contract conflicts with preflight | `docs/docker-stack.md` now documents the hold and PC-31 refusal controls; runtime acceptance remains unverified here. |
## Slice and next concrete step
S-1: Only this note changes. Next, verify `scripts/fixtures/c1589-cache-inventory.sh` via its documented `controls` command and inspect PC-31 evidence before any host preflight; retain live/busy/unknown refusals and retirement safeguards.
Review lane: documentation inspection only; no executable checkpoint or test-design dispatch is needed for this canary note.
## Performance budget
No tests/builds run or added; setup, execution phases, measured p95 and approximately 2x timeouts are inapplicable. This canary has a 10-minute total budget; subsequent verification needs its own measured budget under `docs/testing-and-build.md#performance-standards`.
