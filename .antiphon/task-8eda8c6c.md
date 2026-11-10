# Merge batch 166

Landed on origin/master `a8d830d12f0258a8f133364e58b8de4eb25d77bd`, fast-forward from `f125755c1648533c473ce46311c307788b4a9b65`. First push accepted. No rc/* push. rc/2026-11-15 stayed `ac6d00022e940ee3b90584c7d313567b4491c498`. No subject excluded. No migration.

## Commits

Cherry-pick -x, patch-ids matched. Evidence `f125755c1648533c473ce46311c307788b4a9b65..acaa9d9b1ef3ab32c5f7776913aa964bdbe70550` was commits=11 entries=0 violations=0. Repository was not shallow.

| Landed | Source | Patch-id | Subject |
|---|---|---|---|
| d4499af2df1563e31259b3f5ec97aedf42b80f0c | 04ef5a8726f9f16642cc2fb809456a27de117d2e | 6dbaa3a7f115d9b92e3f37a7c4ef53d7f55c29af | CARD-1833 red: slash autocomplete |
| 1622c8cab8e5819c1a738de560d265d371747c99 | 51142c464b0dc6c4cd7dcf669506e0b76b663fc4 | 8f7ec47fb61b287a900ee40268c4615117f50c01 | CARD-1833 fix: read /remote-control |
| bc5923992e1b668fb40da0a6cd38b2f3429ebf7b | 2ed7cd10654b70df37818f4bd7345fc8715cbb01 | 93b4403ccfa9374e7e894b63ccc74ed49a921d8a | CARD-1833 red: palette not Clear |
| bfbcadc2fad86d9a4bf5fc1247bd1438ed4f164c | 585b6c1956e60a8b1f27de6eabbc96d8622fed96 | ade45a5d604caa42f730708f1f2c7d1b9f89bfbb | CARD-1833 fix: highlight must match draft |
| cc7738b3c5e0d164c1521d0ce3cdc57ccae068ec | 054d684b0803fcddc6ecac1675b6e3b64c509290 | 6c2d1a38d0634519356ee9c574feb0bf6e70bff3 | CARD-1832 plan |
| 387397c60c885a048cbf68e44d39ea40f4e44192 | b343feb9cb510b53a7dc136ae3f4a794e01d7234 | 1dffda049ba159b01fe2f8bb5b1af525d8550a01 | CARD-1832 S1 red |
| 50c2a29b94c3871702a4e73ed1c579378f262f4e | efc45212d68375323e3621be58205296396b5f01 | c5c479ebfb7e9c40d55e9917c589be1ecccc60f1 | CARD-1832 S1 attributes |
| 053987a721db96af2209c85a04e65266e9e50038 | 47e72266c348c2a8592b0fa62aeff7e8abb84d73 | de11647e82d58fe5d75187bc1b0a5758721efaea | CARD-1832 S1 PropertyAttribute |
| a6d52c42cb7978f9bb729f329928e4a85940757c | 078d07bdd104ae3a1850e216152e82bfc7074a3e | 23677d80319ab1b1b519e8da2e86e1077068dfa9 | CARD-1832 S1 exclude-filter doc |
| 449f72b1e4369ebe79f09814ebe9086a7ee7b889 | bf0aaaad253f907e38d89c7d8413fbebe7ccb433 | 029b01a8f3dfb1d448bf1c4be4826b1e72c1c85b | CARD-1832 S2 red |
| acaa9d9b1ef3ab32c5f7776913aa964bdbe70550 | 22e52129be63a576ab4f7ff702767f160ab8e02c | d747124dadddf1fda5ec23b315954cad202fd178 | CARD-1832 S2 derive |
| a8d830d12f0258a8f133364e58b8de4eb25d77bd | (this batch) | n/a | land-graph refresh |

Sources: origin/feat/card-task-c0753d6e `585b6c1956e60a8b1f27de6eabbc96d8622fed96` (includes the ad16ed45 pair) and origin/feat/card-task-9b04bed7 `22e52129be63a576ab4f7ff702767f160ab8e02c`. Every `git cherry origin/master` `+` commit was a product or plan commit. None were `.antiphon` or report-only.

## Pins

Unchanged. policyHash `568aae96ca07d5fb5cb5c54e62a5a4a02b1159ae6fd287de18ca9e8f2c0f2a52`. PinnedInputDigest `08fb1f1eb83e4e8ef3a17a8ea98246745c79e318d4c4cdd48b7f79cea1ce5c4a`. Recomputed with Read-NightlyExecutionPolicy / Get-NightlyPolicyHash and `scripts/test-live-lane-run.ps1 -PrintInputDigest` immediately before the push. Master had not moved. No re-pin commit.

## Land graph

`-Check` refused before refresh: selected digest `0BA6BDADD57C034ABB40B935E8B05243EF224BB2235D0208179F233D167E0D7E` count 77, manifest `F799258CDF1E75D23CD2BA5FF28EE218B52D4A961F2BA4B2BBCF97D0D4DAC141` count 77. Cause is the CARD-1832 Compile include in `tests/Antiphon.Tests/Antiphon.Tests.csproj`. `scripts/land-graph-land-refresh.ps1 -Base f125755c1648533c473ce46311c307788b4a9b65` refreshed and re-checked to exit 0.

- old digest F799258CDF1E75D23CD2BA5FF28EE218B52D4A961F2BA4B2BBCF97D0D4DAC141 count 77
- new digest 0BA6BDADD57C034ABB40B935E8B05243EF224BB2235D0208179F233D167E0D7E count 77
- pins blob unchanged `7a2586e58bd4768648c6f670f6f2c4432f68936a`
- changed records: `tests/Antiphon.Tests/Antiphon.Tests.csproj`
- commit touches only `LandVerificationBuildGraph.manifest.txt` and `LandVerificationBuildGraph.evaluation.txt`

## Checks

Antiphon.sln `-m:1` `UseAppHost=false` through build-slot: 0 errors, 2212 warnings.

| Row | Result |
|---|---|
| Agents.Pty ClaudeDialogObservationTests / ClaudeScreenTests / RemoteControl* | 15 passed, 0 failed, 0 skipped |
| SessionRunner RemoteControlConditionalInputTests / RemoteControl* | 4 passed, 0 failed, 0 skipped |
| Policy guards own process | 52 passed, 0 failed, 0 skipped |
| CP-1 include `[Platform=LinuxOnly]` | 2 passed, 0 failed, 0 skipped |
| CP-2 exclude unannotated sibling | 1 passed, 0 failed, 0 skipped |
| CP-3 PlatformTaxonomyDerivationTests | 7 passed, 0 failed, 0 skipped |
| Antiphon.Tests batch (remote, taxonomy, guards except policy) | 188 passed, 3 failed, 0 skipped, total 191 |
| C1729 inventory | VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3 |
| CP-merge-subset-tests (selector filter, includes Card1277 and TestLaneCategory) | 50 passed, 0 failed |
| CP-merge-subset-tests-land-1 | 34 passed, 0 failed |
| CP-merge-subset-tests-land-2 | 104 passed, 0 failed |
| LandGraphAuditProjectInstanceTests | 1 passed, 0 failed |
| A5 pair | 2 passed, 0 failed |
| ContractSnapshotTests | 5 passed, 0 failed |
| import documented merge rows | 5 rows, 0 warnings |
| import CARD-1832 plan | 11 rows, 0 warnings |

The 3 batch failures are `LinuxOnly_is_a_property_not_a_skip`, `Class_shorthand_is_selected_with_the_linux_only_property`, and `Unannotated_sibling_is_excluded_from_the_linux_only_selection`. Each asserts `ANTIPHON_PLATFORM_FILTER` and the two lanes require opposite values, so they cannot pass in one process. CP-1 passed the two include methods and CP-2 passed the exclude method. Not flakes. Not rerun.

Selector `migrations=false`. Windows owed, not counted as passed: `tests/Antiphon.Tests /*/*/(Card1277PtyHostFixtureTests*)/*`.

## Owed

CARD-1832 S3–S9 (CP-4 onward) were not in this batch. SourceLanding mutation positive controls were not run. The Unit lane stays on nightly and the Windows rc qualification. Native Windows still owes the Card1277 pty-host fixture row. No rc cut.
