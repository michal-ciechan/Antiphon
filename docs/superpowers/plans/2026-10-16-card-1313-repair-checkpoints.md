# CARD-1313 S1-S3 repair selection

Code task `ff740e37`, original Code and landing owner `209c77e6-2f0d-4da5-910a-c931d34aa655`.
Base `585303a77a4cf758d83be104bed7ad1e95082559`; target master; restart none.

The repair brief explicitly requires narrow rows and excludes the whole Unit lane
(CARD-1287). This selection replaces the generic Final profile for this repair only;
it does not claim S4-S7 or whole-plan qualification. The original plan and earlier
results remain unchanged. Every PC-1..81 and variant remains pending for Mutation.

Use the checkpoint tool's foreground `row` verb for each row below, passing committed
HEAD as `--expected-source-sha`, `--msbuild-property UseAppHost=false` to both producing
and reused rows, and a process-local PATH beginning with the installed regular
PowerShell directory. The explicit property addresses the inherited Windows
checkpoint build/launch mismatch without changing checkpoint-runner source.
Use a new output directory for each producing commit. Reused rows pass `--no-build`.
The `row` verb avoids the full plan's fractional-estimate importer refusal. CP-23's
estimate below is rounded to one minute; no deadline or assertion is widened.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-R3 | repairs | `tests/Antiphon.Tests -> bin-c1313-repair-green/` | absent-discovery | `/*/*/ReleaseGateAuthorityContractTests/C599B_MissingDiscovery` | R-1 | exactly 1 outer execution, 3 named assertions, 0 failed/skipped | 1 | 6 | true |
| CP-R1 | repairs | `CP-R3` | publication-control | `/*/*/ReleaseGatePublicationTests/C599_PublicationGate` | R-1 | exactly 1 outer execution, 17 named assertions, 0 failed/skipped | 1 | 2 | true |
| CP-R2 | repairs | `CP-R3` | pinned-schema | `/*/*/ReleaseGatePublicationAuthorityTests/C599A_PinnedPolicySchema` | R-1 | exactly 1 outer execution, 6 named assertions, 0 failed/skipped | 1 | 2 | true |
| CP-R4 | repairs | `CP-R3` | pinned-eight | `/*/*/ReleaseGateAuthorityContractTests/C599B_PinnedPolicy` | R-1 | exactly 1 outer execution, 10 named assertions, 0 failed/skipped | 1 | 2 | true |
| CP-R5 | repairs | `CP-R3` | pinned-eight-matrix | `/*/*/ReleaseGatePublicationAuthorityTests/C599A_PinnedPolicy` | R-1 | exactly 1 outer execution, 4 named assertions, 0 failed/skipped | 1 | 2 | true |
| CP-19 | repairs | `CP-R3` | windows-git-paths | `/*/*/TestSelectionTests/C1313_PathIdentity` | V-2 | exactly 3 outer executions, 0 failed/skipped | 3 | 2 | true |
| CP-22 | repairs | `tests/Antiphon.Agents.Pty.Tests -> bin-c1313-repair-fake/` | fake-delivery | `/*/*/ClaudeVerifiedDeliveryTests/Fake_evidence_appears_then_submit_completes_a_turn` | V-1 | exactly 1 outer execution, 0 failed/skipped | 1 | 6 | true |
| CP-23 | repairs | `CP-22` | delivery-metadata | `/*/*/ClaudeVerifiedDeliveryTests/C1313_DeliveryDiscovery` | V-1 | exactly 1 outer execution, 0 failed/skipped | 1 | 1 | true |

CP-R4/5 cover the explicit schema-2 eight-suite fixtures changed by the repair.
CP-R3 asserts actual production plan and persisted discovery output for missing,
empty and null input; it must fail on the phantom-node defect. Its red-first script
case is run directly under the host build-slot gate before fixing production, to
avoid rebuilding the main assembly just for the new thin TUnit adapter. This is a
named non-TRX characterization, separate from the final outer execution count.
CP-R1/2 and CP-19 are also reproduced on the unchanged base before repair.

Discover each producing assembly once under the build-slot gate and native owned
process boundary; retain the exact arguments, hash and expanded roster. The direct
row driver has no pre-execution discovery hook, so discovery follows its producing
row and precedes all consumers. This ordering limitation is explicit. Main and PTY
tests run sequentially. No live provider, full assembly, runner restart or AppHost
restart is included. Only the runner's build-slot broker is used.

### Cost

Ordinary row estimates total 23 minutes including builds, plus tool bootstrap,
red characterization and authoring. Slot waits are reported separately. No unchanged
green selection is repeated. Exact fresh TRX and source receipts must be checked.

### Deferred work

V-3/V-4 and the unchanged S1-S3 portable CP-1..8 retain the original evidence; this
repair does not relabel them as rerun. V-5..10, R-2/3 and CP-9..18/20/21/24 remain
outside S1-S3 and pending. All PC-1..81 variants remain pending and unbound in
`docs/superpowers/mutations/2026-10-16-card-1313-s1-s3-candidate.md`.
