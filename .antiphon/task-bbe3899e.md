# task-bbe3899e: CARD-1689 distinct retire-temp exchange diagnoses and registry docs

**Outcome:** done. D-2 is implemented red-first. D-1 is documented with a red-first docs witness. Every brief-named class is green on Linux/server2. No helper script was added (see Deviations).

- Branch: `feat/card-task-bbe3899e`
- Worktree: runner mirror `/work/worktrees/task-bbe3899e`
- Base: `f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0`
- Landing owner: Code task bbe3899e
- Plan: none. There is no CARD-1689 plan and no `### Checkpoints` table.

## Commits (all pushed)

| SHA | Slice |
|---|---|
| `cba46a6fe88794d29d62dc34e8d05e51dec0f5c4` | test: red exchange witnesses (3 methods) and 3 skip-roster rows. It did not compile because a `using` was missing. |
| `facf02d19dd0cf78d5fb62e3bdde2ae9cf99090a` | test: adds the missing `using Antiphon.Tests.Application`. Red verified here: 3/3 failed. |
| `3b58c26d53bcb7c76ee99f7d7b5880b740176b58` | fix: `scripts/c1008-retirement.sh`. Green: 3/3. |
| `26a5582dc6c7fcf57795677ae2dd750a0495847f` | test: red docs witness. Its first red was for the wrong reason: this class's `Read` folds whitespace, so the row split could never match. |
| `9b1208e76d65ed896ac79303c8da627146c70934` | test: reads the row and the script unfolded. Red verified for the right reason (gate 9 lacked the prerequisite). |
| `340cf8dc9a9a43316386926937923c97fb483e64` | docs: `docs/docker-stack.md` gate 9 row plus a new section. DockerStackDocumentationTests 17/17. |

## D-2: diagnoses (`c1609_prepare_exchange`)

I kept the repo's naming style, a `RecycleGitAuditUnknown reason=<x>` suffix, rather than new codes. This keeps every consumer that keys on the first token unchanged. Each cause now names itself and the host file that holds its detail. The coordinator console shows only the `DIAGNOSIS=` line.

| Cause | Diagnosis |
|---|---|
| Inventory helper exit other than 0/3/4 | `RecycleGitAuditUnknown <first audit check= line> reason=inventory detail=<retirement/<op>/inventory-<nonce>.out>` |
| Registry absent | `... reason=registry-missing detail=/home/mc/antiphon-server2/retirement-origins.json` |
| Custody, canonical bytes or shape | `reason=registry-invalid detail=<registry>` |
| No entry for `C1008_PROJECT_ID` | `reason=registry-project detail=<registry>` |
| Origin not listed | `reason=unapproved-origin detail=<inventory-<nonce>/private-origins.txt>` |
| Desktop proof exchange failed | `reason=proof-exchange stage=<request\|response\|binding\|freshness\|digest\|record>` |

For the exchange, `c1609_request_host_proof` records the last stage it reached in `C1609_PROOF_STAGE`. No check logic changed.

Unchanged:
- Inventory exits 3 and 4 still give bare `RecycleUnpublishedWork` and `RecycleWorktreeDirty`.
- `case_retire_temp_inventory`'s `reason=unapproved-origin` is the same.
- Both `c590-remote.sh` call-site fallbacks (`|| c1008_refuse RecycleGitAuditUnknown`) are untouched. A bare diagnosis now means only a mkdir, persist or sync failure.
- `c590-remote.sh`, `c590-real.ps1` and `deploy-server2.ps1` were not edited.

Before the fix I grepped every consumer of `RecycleGitAuditUnknown`. The desktop has only exact or first-token matches on other paths, and no allowlist sees these strings.

## D-1: docs

- The gate 9 row names the prerequisite and links `#temp-retirement-trusted-origins-card-1689`.
- The new section gives the schema exactly as `c1609_export_trusted_origins` and `c1609_read_canonical` and `c1609_custody` accept it:
  - owner `mc`, one link, no group/world write, at most 1 MiB, no symlinked path;
  - canonical `jq -cS` bytes; keys exactly `projects` and `schema`, with `schema` 1;
  - lowercase project-id keys, each with only `origins`, a unique non-empty string list;
  - an entry for the project;
  - exact-string origins, so with and without `.git` are different strings.
- It also gives:
  - how to find the project id (`check-census`, the `RECYCLE_PROJECT id=` line, or the `-ProjectId` override);
  - the one-time `umask 077; jq -ncS ...` command;
  - how to rewrite the file;
  - a diagnosis table, including where each stage's desktop detail lives (`remote-output.txt`, `coordinator-exchanges.jsonl`, `exchange-*`).
- Nothing was written on server2, and the registry was not created.

## Tests

- **New exchange witnesses (Linux-only):** three methods in `VolumeRetirementContentTests`. They source the real `c1008-retirement.sh` and stub only `c1008_audit_readonly` and `c1008_refuse`.
  - `Exchange_inventory_refusal_names_its_check_and_detail_file`, with 3/4 controls
  - `Exchange_registry_refusals_name_the_registry_cause`: 5 vectors and a distinctness check
  - `Exchange_proof_failure_names_its_stage`: silent and mismatched replies, plus the no-origin control
- **New docs witness:** `DockerStackDocumentationTests.Temp_retirement_trusted_origins_registry_is_documented`. It checks that every documented reason and stage exists in the script.
- **NativeLinux bookkeeping:** the new methods sit in an existing class-level `[NativeLinuxOnly]` class. I added only 3 `WindowsUnitSkipRoster.NativeSourceOnly` rows.
  - No `tests/test-execution-policy.json` row was added.
  - The C1323 `test-rc-platform.ps1` roster is class-level and unchanged.
  - The live-lane `PinnedInputDigest` was not re-recorded, because none of its five census inputs changed.

### Runs

All runs were on server2/Linux at the fix-plus-docs content. The tested source was HEAD `340cf8dc9`, except the 3/3 green which ran at `3b58c26d5`. Each run took one build-slot lease and wrote a TRX that I checked per class.

| Run | Result | slot waited / held |
|---|---|---|
| Red, exchange (at `facf02d19`) | 3 failed / 3 | 0 s |
| Green, exchange | 3/3 | 0 s |
| Red, docs (at `9b1208e76`, old docs) | 1 failed / 1 | n/a |
| DockerStackDocumentationTests | 17/17 | n/a |
| VolumeRetirement Content 25, Receipt 11, ApplyGate 13, HostProof 9, Wrapper 6 | 64/64 | 15 s / 2168 s |
| WindowsUnitPlatformAdmissionTests `C1210_*` | 5 passed, 1 skipped | 136 s |
| RollingVolumeRecycleScriptTests 77, RetiredTempContainerHostTests 22, ReleaseGatePlatformTests 5 | 104/104 | 75 s / 3083 s |
| RemoteScriptContractTests | 511/511 | 165 s / 3778 s |

- The C1210 skip is `C1210_Windows_linux_only_outcomes_are_exact`, which is Windows-placement-only by design.
- The Rolling and Remote groups ran concurrently.
- The builds were three direct Antiphon.Tests builds, needed for the red/green cycles and the test edits. All were `UseAppHost=false` with native output.
- No checkpoint tool run was made, because there is no plan table. All of the above are brief-named direct runs.

### Other checks

- `check-evidence-diff.ps1 -BaseRef f32aed42e -HeadRef HEAD`: commits=6, violations=0.
- `git merge-tree --write-tree origin/master HEAD`: clean. `origin/master` is still `f32aed42e`.

## Deviations and not run

- **Whole Unit lane not run.** The profile says Final, whole Unit lane, but the brief's 90-minute budget and its explicit class list set the scope. The lane is about 3 h on server2. This is deferred to Final review or nightly and is not claimed passed.
- **The "C1010" class could not be identified.** The `RecycleGitAudit` grep gave the C1008/C1609 classes above, plus `VolumeRetirementHostProofTests` and `DockerStackDocumentationTests`, and all were run.
- **No installer helper.** The registry's origins are the exact `C1008_PRIVATE_ORIGIN` strings found in the temp volume, not the repo's configured remotes. A remote-derived helper would be wrong for the no-`.git` fixture origins and would need host-side custody parity. So I documented the change only, as the brief allowed.
- **Remaining gap for Mutation:** the stages `freshness`, `digest` and `record` are only doc-pinned to their assignments. No behaviour test drives them, because they need a real proof directory.

## PCs pending for SourceLanding Mutation (none discharged)

| PC | Mutant | Expected red |
|---|---|---|
| PC-1 | Inventory `*)` branch back to bare `RecycleGitAuditUnknown` | `Exchange_inventory_refusal_names_its_check_and_detail_file` |
| PC-2 | Drop `detail=$out`, or the audit-line extraction | same |
| PC-3 | 3/4 branches gain the detail suffix | same (controls) |
| PC-4 | Remove the registry-missing pre-check (falls through to invalid) | `Exchange_registry_refusals_name_the_registry_cause` |
| PC-5 | Fold `registry-project` back into the shape check | same |
| PC-6 | `unapproved-origin` detail points at the registry | same |
| PC-7 | Export-failure branch back to `return 1` | same |
| PC-8 | Exchange failure back to `return 1` | `Exchange_proof_failure_names_its_stage` |
| PC-9 | Remove `C1609_PROOF_STAGE=binding` | same (mismatched reply reads `stage=response`) |
| PC-10 | Rename a reason or stage in the script only | `Temp_retirement_trusted_origins_registry_is_documented` |

Restart: none.
