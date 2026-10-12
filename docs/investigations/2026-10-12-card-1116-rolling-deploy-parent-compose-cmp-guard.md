# CARD-1116: rolling deploy-parent Compose comparison

2026-10-12. Investigate task `2000a0d1-996f-44dc-be0c-4408146185ed`.

Confirmed original mechanism, already repaired on master
`a91ba68a72185d57d39e87b218e459c5df2451de`: the pre-repair guard ran after
reclamation and bypassed the active journal's refusal helper. Landed repair
`c88411f0f6ad7da341475fb94c0aafd4e5ba350e` now compares before the lock/removal
(`scripts/c590-remote.sh:5947`) and records a late mismatch through
`c1008_refuse` (`:6171`). Landed tests are
`RemoteScriptContractTests.cs:2112` and `:2145`.

No remaining receipt/wrapper defect was demonstrated. The fresh apply's one-shot
reclamation summary can retain outcome=reclaimed (`scripts/c590-remote.sh:6124`),
while the saved final receipt has the actual mismatch diagnosis and partial or
refused outcome (`:3905`). The separate result returns exit 2; the wrapper stops
on that result (`scripts/deploy-server2.ps1:893`). Resume paths differ in whether
they already printed the reclamation summary.

The [full investigation](../superpowers/plans/2026-10-12-card-1116-investigation.md)
contains every acceptance mapping, master ancestry, DB-backed task/card rows,
historical transcript references, unedited checkpoint provenance, and the late
fresh/resume path reconstruction. It is the caller-requested deliverable.

Recommend closure as already implemented, disclosing these limits: historical
compose-specific red-first assertion evidence was not retained in the sources
examined; no current-master tests or live rollout observation were performed;
post-land Mutation was **NOT run**.

## Not done, noted

No fix designed or implemented; a second terminal summary would require a separate contract decision.

--- next stage ---
next: none
handoff: Already repaired through CARD-1105; caller owns closure with the historical red-first evidence limit and post-land Mutation NOT run stated explicitly.
artifact: docs/superpowers/plans/2026-10-12-card-1116-investigation.md
