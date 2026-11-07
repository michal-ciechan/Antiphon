# Merge batch 111 (CARD-1734) — landed

Fast-forward of this branch onto master. Base and pre-push `origin/master`: `edb2c20156afa4da503b1b29fb83f1f953987bd5`. Push loop count: 0 (master had not moved; no re-pick). rc/* was not pushed.

Tests verified `d5d98f9dc6756a605e83e9bd5c5f1d667008f080`. This file is the documentation commit after that tip.

## Land graph

Operator approval 2026-11-07 allowed one further `-Update` to re-record the manifest digest and the `CopyOwnedPtyHostAppHostForHerdr` pin. `-Update` was not run again after that approval's single use.

| When | Result |
|---|---|
| Base `-Check` | approved, digest `D5B302A94FD3B0C28012C6764A2027B8B8DDE1D04506B3A641DD671EFB6F8C03`, count 69 |
| Post-pick `-Check` before the pin edit | pre-check only: manifest does not match the selected graph, digest `206F953DC44801E217D75850575B2A3837E2AED28B6FAAB941ABA2F12803221E`, count 69 |
| Pin commit `b8fe648a40a649faab48df7c41a037fc2e36f5d6` | `LandVerificationBuildGraph.pins.txt` only, 1 insertion, 1 deletion |
| `-Update` once | exit 0, approved, same digest, count 69. SDK line `10.0.204` -> `10.0.403` |
| Digest commit `d5d98f9dc6756a605e83e9bd5c5f1d667008f080` | `evaluation.txt` and `manifest.txt` only, 4 insertions, 4 deletions |
| `-Check` after that commit | exit 0, approved, digest `206F953DC44801E217D75850575B2A3837E2AED28B6FAAB941ABA2F12803221E`, count 69 |

The digest change is the SessionRunner.Tests csproj (the only definition file changed since the base). Count stayed 69. Two commits were required: the audit script will not run `-Update` while `pins.txt` differs from HEAD, and it does not write `pins.txt` itself.

| Record | Old | New |
|---|---|---|
| Target pin `CopyOwnedPtyHostAppHostForHerdr` | `73CF3A6F94F61454FA48E44EC824B4E52322B1E66AB95BD35C7FFC92ED1DB0C3` | `697025987CCFF9ABE0F9F40C71D3BB1B159DC2BB2183FA6D85753E09319939A7` |
| Manifest digest | `D5B302A94FD3B0C28012C6764A2027B8B8DDE1D04506B3A641DD671EFB6F8C03` | `206F953DC44801E217D75850575B2A3837E2AED28B6FAAB941ABA2F12803221E` |
| Pins blob | `97151190360cc5515e70ae743c2858e96151382e` | `7a2586e58bd4768648c6f670f6f2c4432f68936a` |
| Evaluation SDK | `10.0.204` | `10.0.403` |

## Policy

Recomputed on the picked tree before the pin refresh. Stored values already matched. No extra policy commit.

| Field | Base master | Picked tree (stored = recomputed) |
|---|---|---|
| policyHash | `82b4f28a4197c7aba6c28678e3113afa2b343ccbd84c905e0e7ea790b87c1d1a` | `f7b7f7534ca39270a26e8b7da7b6a171690940fb64d7c6b45f9b0462d99b44e1` |
| PinnedInputDigest | `1b87b54e8b22bbee3ea3c86ca248a925628ea1b0af52589d2e027fa3d37f130c` | `84003c9c0020549a4ad33e1cf2b480500bb17023fef9952e7794a0287ce57edc` |

## Build and tests

Commit under test: `d5d98f9dc6756a605e83e9bd5c5f1d667008f080`. `UseAppHost=false`. `TEMP=/tmp`. Native output. One lease per command.

Solution build `dotnet build Antiphon.sln -m:1` through `scripts/build-slot.ps1` (lease `b14f420f-65ea-47ec-8f7b-25b69fcd81e0`, the explicit `-m:1` kept the slot's granted 6 off the command line): 0 errors, 1965 warnings, 4m 12s.

`tests/Antiphon.Tests` one union filter, `--no-build`: 215 total, 215 passed, 0 failed, 0 skipped, 14m 04s (lease `a3493e12-fdf0-4a00-b05d-a8736589c0be`).

| Group | Count |
|---|---:|
| CP-1 `CheckpointColdBuildTests` | 3 |
| CP-2 eight plan classes | 66 |
| CP-3 guards, policy, mechanical contract, platform guidance | 75 |
| CP-5 `CheckpointTerminalReceiptTests` | 10 |
| CARD-1217 S1 (`C1217ConditionalSkipContractTests` 8, `CheckpointManifestTests` 7, `CheckpointImportTests` 34, `TrxReportTests` 7, `CheckpointRepeatConfigurationTests` 5) | 61 |

CP-2 breakdown: `C1307CliCancellationTests` 5, `WaitCommandTests` 12, `CheckpointToolCopyCleanupTests` 16, `CheckpointStatePublicationTests` 2, `CheckpointAppTests` 3, `ReportWriterTests` 6, `CheckpointSourceExecutionTests` 16, `RunCheckpointSourceScriptTests` 6.

CP-3 breakdown: `TestClassificationPolicyTests` 39, `TestClassificationGuardTests` 1, `SlowTestRegistryMergeTests` 5, `SlowTestTripwireTests` 2, `NativeLinuxPolicyWitnessTests` 3, `C1729LayeringGuardTests` 9, `MechanicalPcContractTests` 11, `TaskPlatformGuidanceTests` 5.

C1729 inventory from the passing repository test: `VIOLATIONS 0 LISTED 168 STALE 0 AMBIGUOUS 3`.

`tests/Antiphon.SessionRunner.Tests` CP-4 (`NativeLinuxPolicyWitnessTests`, `TestClassificationGuardTests`), `--no-build`: 4 total, 4 passed, 0 failed, 0 skipped (lease `0a5ef507-f154-4c88-8d98-e6f9472c72ff`, waited 30s).

## Evidence

`scripts/check-evidence-diff.ps1` at head `f8e5c9a41336f0814128317c40a45cf9915bd653`: commits=14 entries=0 violations=0. A later re-check exits 2 `shallow_or_unknown_history`: the shared partial clone gained `.git/shallow` boundary `12f86542fce96f1fcd2b0a17778bd811ce5e8ee1` (not an ancestor of this branch) at 12:58 UTC. Commits after the clean check are the stop report (allowed `.antiphon` markdown), the pin line, and the manifest/evaluation pair.

`git merge-tree --write-tree origin/master HEAD` at the pre-report tip wrote `c893b23d97cacc712e70d2682bf930282edffa5e` with exit 0.

## Commits

| Land SHA | Source SHA | Patch-id | Subject |
|---|---|---|---|
| `3fb7eaf2cdd6da36670904dcab6e93af7066f2c8` | `6a1ce89504feec47fc2eaf025a80bb878b5ed4a8` | `a057cbaf2ca9c7ddddf085b25d2bf99e3979f9d1` | test: witness cold helper build custody and terminal receipts |
| `7a114887cacd7f539ef4d16f9c2a2fa660e5f770` | `a42bbc0c5e00dea5b0ce21ac11504b49b34a6490` | `65a0119574a9d5c308293727925470e940dd3d69` | fix: confine the native test helper child build |
| `e6bf55322fd2baf07d02c50f13327d4603f5d9f8` | `42c43279866e655a8a592a751392475aa7e0aac3` | `d8545297cd914d4322b9348355b7946413a0425c` | test: expose terminal stop and executor receipt gaps |
| `cf9f32c559889452f9563f8a09bb3355a725429f` | `e57eb1086e02cf2ad4b1bc4e0dc77e915805f61f` | `d0b61f026ca48e6b0e73acd77d753387731c33fb` | test: cover partial results and provisional receipt custody |
| `ceda26b31ef344bc07efd590fa9bdb59bae0f23f` | `28641ae2811a53bcf0ce3798490735640f8b9282` | `c09bfd64a147cafbe24fc5c1df4064a6c29684c8` | fix: seal truthful terminal receipts before stop waiters return |
| `731fc6cafbc680113b005e8443026f9e53410647` | `36c327afc884b6292d6c83ab3f25f42a9741fc14` | `16a436ecfa07382b2d0aab27b36fec37b8ac4fc9` | docs: record repair invariant, red evidence and bounded selection |
| `62fe9ce0fa18255a6654a5fc935f10e68d943333` | `a517216da7deec60ee66d951b597fe25019ab62b` | `c2512a051c11962d25d48529cd5424a4ec09b6fa` | fix: retain terminal receipts when source observation fails |
| `13712657f943337c6ee9d87b272a9f0f3a265b31` | `69977d2e0d5898267c6bc5365fb7245b5f0d47bf` | `51677679ff4737ec04b863cf72ded09162621b38` | fix: reject forged green failure reasons before row admission |
| `d619d4f3a45b905a60e913706a96307071467a7e` | `e3c2fa55774b51fd89bd0a1fe0382b4717fe656a` | `a32b77cbc2a85fc51e06127b166aff35f7b830ce` | test: require the exact forged build-failure refusal |
| `e6ad99c617e646bae5cfaf239df8676261b20cf4` | `1b6bf236c93fc941b44511fd1cb59abced7d2875` | `73acc2a17cbdb9cf57db44738d6cfffd2e998dd4` | test: isolate generated cold-build fixture outputs |
| `953fee67e56fdeedde8145dbb91ff0793a31b969` | `a2ffbcee0b743711139076d551734a1fb144cda4` | `d868453db90e302b5341fdfc1656c9ab93473e76` | test: guard source history and repair native fixture admission |
| `080be2357f3215a02a5595ecf692409fcbc5f002` | `2aa4c57be3cdb51d931b708e4faf82cfa7795902` | `cb79b3910c6a004613e9b52df0013b580121accf` | fix: preserve adverse source history in terminal recovery |
| `5a680731b159b0f02175ee3afd174fc969187441` | `602c37029f3b818b87b2f3e7d4becbeebad21409` | `14d75abb2cfdca94ae08a8da363b0699488c021f` | docs: name the retained terminal-publication lock |
| `f8e5c9a41336f0814128317c40a45cf9915bd653` | `68aca958a611f59c928aa9c7982004e732fac058` | `8d2363833095e44152d2f6562825ad53352a6870` | fix: inline PtyHost artifact switches on the Exec |
| `ed607da5cda697326976417fe64127b67d251052` | — | — | docs: record the first-attempt stop (superseded by the pin approval) |
| `b8fe648a40a649faab48df7c41a037fc2e36f5d6` | — | — | chore: refresh the CopyOwnedPtyHostAppHostForHerdr land-graph pin |
| `d5d98f9dc6756a605e83e9bd5c5f1d667008f080` | — | — | chore: land graph digest for the csproj edits since edb2c20156afa4da503b1b29fb83f1f953987bd5 |

## Owed

Whole Unit for CARD-1720/1728, and the SourceLanding Mutation positive controls, remain owed. This batch did not run them.

--- next stage ---
next: done
