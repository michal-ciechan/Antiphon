# Merge batch 111 (CARD-1734) — stopped

Master was not pushed. origin/master remains edb2c20156afa4da503b1b29fb83f1f953987bd5.
Prepared task tip before this report: f8e5c9a41336f0814128317c40a45cf9915bd653.
Push loop count: 0.

## Stop

Post-pick `scripts/audit-land-verification-graph.ps1 -Check` failed only with `manifest does not match the selected graph` (selected digest 206F953DC44801E217D75850575B2A3837E2AED28B6FAAB941ABA2F12803221E, count 69). That is the one failure the operator's digest-refresh approval covers, so `-Update` was run once.

`-Update` then refused before writing any record:

```
digest=206F953DC44801E217D75850575B2A3837E2AED28B6FAAB941ABA2F12803221E count=69
refused: tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj Target CopyOwnedPtyHostAppHostForHerdr (changed pin)
```

`git diff` after that refusal was empty. Manifest, evaluation, and pins files are unchanged. The stored pin is `73CF3A6F94F61454FA48E44EC824B4E52322B1E66AB95BD35C7FFC92ED1DB0C3` in `server/Application/Services/LandVerificationBuildGraph.pins.txt`. The inlined Exec in commit f8e5c9a41336f0814128317c40a45cf9915bd653 (source 68aca958a611f59c928aa9c7982004e732fac058) changes that target's XML body, which is what the pin hashes. The operator approved one manifest digest refresh, not a pin change. `-Update` was not retried.

## Land graph

| When | Result |
|---|---|
| Base edb2c20156afa4da503b1b29fb83f1f953987bd5 `-Check` | approved, digest D5B302A94FD3B0C28012C6764A2027B8B8DDE1D04506B3A641DD671EFB6F8C03, count 69 |
| After picks and the green solution build `-Check` | pre-check only: manifest mismatch, selected digest 206F953DC44801E217D75850575B2A3837E2AED28B6FAAB941ABA2F12803221E, count 69 |
| `-Update` once | refused, changed pin, no files written |

`-Check` stops at the pre-check when the manifest digest mismatches, so it does not reach the pin comparison. The pin refusal is visible only to the evaluation inside `-Update`.

## Policy

Recomputed on the picked tree. Stored values already matched. No extra policy commit.

| Field | Base master | Picked tree (stored = recomputed) |
|---|---|---|
| policyHash | 82b4f28a4197c7aba6c28678e3113afa2b343ccbd84c905e0e7ea790b87c1d1a | f7b7f7534ca39270a26e8b7da7b6a171690940fb64d7c6b45f9b0462d99b44e1 |
| PinnedInputDigest | 1b87b54e8b22bbee3ea3c86ca248a925628ea1b0af52589d2e027fa3d37f130c | 84003c9c0020549a4ad33e1cf2b480500bb17023fef9952e7794a0287ce57edc |

## Proofs

`scripts/check-evidence-diff.ps1` base edb2c20156afa4da503b1b29fb83f1f953987bd5 head f8e5c9a41336f0814128317c40a45cf9915bd653: commits=14 entries=0 violations=0.
All 14 patch-ids match the source commits. No `.antiphon/` paths in that range. Cherry-picks were clean. The tree differs from origin/feat/card-task-a9a521c5 only by the two master commits since merge-base c7216a06f55fba7dc255702c99217248e1b4a20e (land-fixture test files).

## Build

Slot build of `Antiphon.sln` with `UseAppHost=false` at maxcpucount 6 failed: NETSDK1047 (`src/Antiphon.PtyHost/obj/project.assets.json` has no `net9.0/linux-x64` target) and MSB3073 from `CopyOwnedPtyHostAppHostForHerdr`. E2E and SessionRunner.Tests ran that child PtyHost build at the same time against one assets file. 3 errors.

Rebuild with the same properties and `-m:1` (slot still leased; the named `-m` is left in place): Build succeeded, 0 errors, 7 warnings (NU1902 OpenTelemetry on Antiphon.Tests, MSB3277 EntityFrameworkCore.Relational on Antiphon.DockerStack.Fixture).

## Commits

| Land SHA | Source SHA | Patch-id | Subject |
|---|---|---|---|
| 3fb7eaf2cdd6da36670904dcab6e93af7066f2c8 | 6a1ce89504feec47fc2eaf025a80bb878b5ed4a8 | a057cbaf2ca9c7ddddf085b25d2bf99e3979f9d1 | test: witness cold helper build custody and terminal receipts |
| 7a114887cacd7f539ef4d16f9c2a2fa660e5f770 | a42bbc0c5e00dea5b0ce21ac11504b49b34a6490 | 65a0119574a9d5c308293727925470e940dd3d69 | fix: confine the native test helper child build |
| e6bf55322fd2baf07d02c50f13327d4603f5d9f8 | 42c43279866e655a8a592a751392475aa7e0aac3 | d8545297cd914d4322b9348355b7946413a0425c | test: expose terminal stop and executor receipt gaps |
| cf9f32c559889452f9563f8a09bb3355a725429f | e57eb1086e02cf2ad4b1bc4e0dc77e915805f61f | d0b61f026ca48e6b0e73acd77d753387731c33fb | test: cover partial results and provisional receipt custody |
| ceda26b31ef344bc07efd590fa9bdb59bae0f23f | 28641ae2811a53bcf0ce3798490735640f8b9282 | c09bfd64a147cafbe24fc5c1df4064a6c29684c8 | fix: seal truthful terminal receipts before stop waiters return |
| 731fc6cafbc680113b005e8443026f9e53410647 | 36c327afc884b6292d6c83ab3f25f42a9741fc14 | 16a436ecfa07382b2d0aab27b36fec37b8ac4fc9 | docs: record repair invariant, red evidence and bounded selection |
| 62fe9ce0fa18255a6654a5fc935f10e68d943333 | a517216da7deec60ee66d951b597fe25019ab62b | c2512a051c11962d25d48529cd5424a4ec09b6fa | fix: retain terminal receipts when source observation fails |
| 13712657f943337c6ee9d87b272a9f0f3a265b31 | 69977d2e0d5898267c6bc5365fb7245b5f0d47bf | 51677679ff4737ec04b863cf72ded09162621b38 | fix: reject forged green failure reasons before row admission |
| d619d4f3a45b905a60e913706a96307071467a7e | e3c2fa55774b51fd89bd0a1fe0382b4717fe656a | a32b77cbc2a85fc51e06127b166aff35f7b830ce | test: require the exact forged build-failure refusal |
| e6ad99c617e646bae5cfaf239df8676261b20cf4 | 1b6bf236c93fc941b44511fd1cb59abced7d2875 | 73acc2a17cbdb9cf57db44738d6cfffd2e998dd4 | test: isolate generated cold-build fixture outputs |
| 953fee67e56fdeedde8145dbb91ff0793a31b969 | a2ffbcee0b743711139076d551734a1fb144cda4 | d868453db90e302b5341fdfc1656c9ab93473e76 | test: guard source history and repair native fixture admission |
| 080be2357f3215a02a5595ecf692409fcbc5f002 | 2aa4c57be3cdb51d931b708e4faf82cfa7795902 | cb79b3910c6a004613e9b52df0013b580121accf | fix: preserve adverse source history in terminal recovery |
| 5a680731b159b0f02175ee3afd174fc969187441 | 602c37029f3b818b87b2f3e7d4becbeebad21409 | 14d75abb2cfdca94ae08a8da363b0699488c021f | docs: name the retained terminal-publication lock |
| f8e5c9a41336f0814128317c40a45cf9915bd653 | 68aca958a611f59c928aa9c7982004e732fac058 | 8d2363833095e44152d2f6562825ad53352a6870 | fix: inline PtyHost artifact switches on the Exec |

## Not run

The batched final-tree tests were not run. Still owed: whole Unit CARD-1720/1728, and the SourceLanding Mutation positive controls. rc/* was not pushed.

## Needed

Approve a pin refresh for `CopyOwnedPtyHostAppHostForHerdr` (or send that target body back to Code) before another manifest re-record. The digest refresh alone cannot land this tree.
