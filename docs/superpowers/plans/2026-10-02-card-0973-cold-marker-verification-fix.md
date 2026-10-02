# CARD-0973 cold marker after seed-image cleanup

Live evidence supplied in `.antiphon/inbox/EVIDENCE-0973.md` confirms the exact
refusal at c590-remote.sh:2030-2031: redeploy-parent deletes the seed-time image,
then the cold validator's image inspect returns 1. All marker format predicates
pass. Cold admission is a format/provenance contract across deploy SHAs; the
historical image need not remain installed. Full-marker maintenance is unchanged.

S1 commits a real-marker fixture, Docker boundary fakes that remove the seed image
between parent deployment and verification, reader-specific contract cases, and
rolling T-20. Red must name `pruned-seed-image-accepted`, `available-main-helper`,
or `full-context-refused-c849_seed`, plus rolling
`T-20 pruned cold seed image verifies before admission`; the latter must expose
CacheSeedMarkerInvalid. No syntax/build/zero-execution red qualifies.

S2 removes the cold readiness dependency on historical-image existence, permits
helper fallback to the identified current standing runner when the marker image
is gone, and makes cold retired verification record a locally available helper
image. Missing/malformed/foreign marker/volume refusals stay. Full recovery/hash,
saved-donor, Reset and Prune gates stay unchanged. No server code or live deploy.

The six allow-cold readers each get a separately reported TUnit argument case:
ordinary Seed, parent deployment, temp deployment, retirement, runner verification,
and retired verification. Each executes the actual marker-reading slice with real
prepare/volume/readiness code; verification and retired verification execute their
whole host-case body. Five helper contexts each exercise absence of both the
seed image and the requested SHA tag, identified-main fallback, malformed image,
foreign main and missing images. One maintenance case covers saved/full gates
and unchanged full-marker payload validation. Prior cold/full tests remain selected.

The rolling fake uses the real captured eight-field cold marker and executes
parent/verify reader slices; successful fake parent deployment removes the seed
image from the fake image catalogue. SavedDonor transport keeps its existing
source-selection seam; full-marker admission is tested separately. Final roster:
20 groups, 59 driver invocations, 206 assertions (prior 19/55/197 plus T-20 1/4/9).
New marker fixture executions require Linux bash and jq (WSL on Windows).

## Verification design

Run direct run-checkpoint.ps1 as required by the brief; it owns the build slot.
All filters are literal, with trailing class-prefix stars. No Unit-lane selection.
The affected rolling plan rows are CP-18, CP-20 and CP-21; backend/runtime rows
and live CP-22..24 are outside this script fix and live operations are prohibited.
The full rolling contract row includes DockerStackContractTests as requested.
Final rows share one isolated committed build. jq 1.7.1 is checksum-verified at
`/tmp/card0973-KPglJm/bin/jq`; prepend its directory to PATH.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-973-red | S1 | `tests/Antiphon.Tests -> bin-c973-red/` | pruned-image-red | `/*/*/RemoteScriptContractTests*/C973_*` | six ready readers, five helper readers, full context | 12 executed; named assertion reds; 0 skips | 12 | 8 |
| CP-973-rolling-red | S1 | n/a | rolling-red | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c973-rolling-red -- pwsh -NoProfile -File scripts/test-deploy-server2.ps1` | T-20 real cold marker after parent cleanup | exit 1; named T-20 assertion, CacheSeedMarkerInvalid | n/a | 2 |
| CP-973-caches | S1-S2 | `tests/Antiphon.Tests -> bin-c973-final/` | cache-contracts | `/*/*/RemoteScriptContractTests*/(C849_*)\|(C912_*)\|(C973_*)` | existing C849/C912 and new reader matrix | 39 executed, 0 failed/skipped | 39 | 8 |
| CP-18 | S1-S2 | CP-973-caches | rolling-contracts | `/*/*/(DockerStackContractTests*)\|(DindRunnerContractTests*)\|(RemoteScriptContractTests*)\|(RunnerDrainScriptTests*)\|(DockerStackDocumentationTests*)/*` | affected rolling plan CP-18, DockerStackContractTests | all selected, 0 failed/skipped | 173 | 5 |
| CP-20 | S1-S2 | n/a | rolling-harness | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c973-rolling -- pwsh -NoProfile -File scripts/test-deploy-server2.ps1` | affected rolling plan CP-20, T-1..T-20 | 20 groups/59 invocations/206 assertions, 0 failures | n/a | 6 |
| CP-21 | S1-S2 | CP-973-caches | rolling-docs | `/*/*/DockerStackDocumentationTests*/*` | affected rolling plan CP-21 | all selected, 0 failed/skipped | 10 | 2 |

Set `C804_ORPHAN_SWEEP_ROOT=c973-disabled` and `TUNIT_MAX_PARALLEL_TESTS=1` for
all TUnit rows; pass the clean committed SHA as ExpectedSourceSha. Keep source
unchanged through each row group. Final throwaway rebase uses
`git -c rebase.autoStash=false -c rebase.updateRefs=false rebase origin/master`
in a detached scratch worktree only. Assigned branch remains fast-forward-only.
