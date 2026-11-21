# Land-path advisories review, round 1 (Final)

Subject: origin/feat/card-task-cf792bfc at `e277c379f9a7bdc8d9327a0ff8f0c050adc47ac2` (Code task `cf792bfc-06b0-42aa-b298-cdf70042b767`). Range `adce1900111e85c0e437e2caf7c2f98b8c9a203f..e277c379f9a7bdc8d9327a0ff8f0c050adc47ac2`. Reviewer task 76b77db6. Verdict: **CLEAN**. No blocker and no regression. Advisories below do not hold the land. No Debug task.

`reviewedSourceClean` is false: these rows are leased `build-slot` runs in native output (`UseAppHost=false`, `TEMP=/tmp`, compiler policy 0). The plan's `bin-cf792-*` outputs are outside this brief, so there is no checkpoint-tool receipt. PCs PC-1a, PC-1b, PC-2a, and PC-2b stay pending (mutation waived 2026-11-12).

## The ten docs-skip failures

They are these ten cases in `LandNonCodeSkipTests`, and this branch does not edit them or the land-graph manifest:

- `C1380_RegularDocumentationChangesSkip` for add, edit, delete, rename, README, and empty
- `C1380_DocsOnlyRebasedLandRecordsNonCodeSkip`
- `C1380_TargetContainedCodeIsExcludedFromTheActualRebasedDiff`
- `C1385_RegistryMatchesNarrowGraph`
- `C1385_UnrelatedScriptChangeStillSkips`

At the reviewed tip the class is 122 total, 112 passed, 10 failed. Each of the nine land cases reports `h.Verifier.Calls` 1 instead of 0, with `publication=Landed` and `policy=unrecognized-build-graph`. The registry case expects embedded fingerprint `78497A2C9CC6C908D77259B0181A87AA4CA1B25F923AE358C16DD2DB2D1FA181` and the live audit of that tree returns `819858AE650809E79A56F06FEC131EE85FA720D2A05DCF53293403A766F7B8DD` (80 definitions on both sides). The same ten failed at the assigned base. The new `C_FT2_*` cases passed 10/10, including the exact `trust=target-manifest` receipt.

The tip still carries the pre-refresh manifest. Master admitted the probe satellite resource language in `72259dca94c1f934dad20ceeb2f396d8ca04d7cc` and refreshed the graph in `84d49df2883d3fb2dacbdeb2756ea183d9701403`. The brief's name `0a7dbc69` is not a git object. Those two commits are on master and are absent from this tip. A docs-only land on the stale tip still publishes: `unrecognized-build-graph` runs the verifier instead of taking the skip, which is why lands kept succeeding while the tests expected zero verifier calls.

Current `origin/master` moved during this review to `72565797a7702f4af64f43f51d854ea08071b1d1` (`chore: refresh land-graph records for the landing batch`). Its manifest is `544627DF4CFD73157DA134EE730CA45C3DEC7A5DFAFFF977986D045295D00A33`, count 80. `audit-land-verification-graph.ps1 -Check` on that tip exited 0 (`approved`). The same ten cases, run earlier on `ce0eb1c5fbccdf8b2918643a319b88386435b761`, passed 10/10 while that tip's digest was `819858AE...`. Current master does not carry ten red land tests. Do not commission a Debug task.

The land tree is merge-tree `9ec683ed8434eea8a7a821abebbd1c9a389e8d85` (snapshot `95b458e857c1293b4d7eb3381d10a0e6d046a333`, parents the reviewed tip and `72565797`). On that tree the same ten cases passed 10/10, `-Check` exited 0 with digest `544627DF...` count 80, and `regenerate-derived-impact-index.ps1 -NoBuild` printed `derived-impact-index unchanged`. An earlier merge against `ce0eb1c5` (tree `13272e9f98669a9b5700fc031a68b1aa0f573dbb`) had the same 10/10 and an unchanged regen at digest `819858AE...`. Rebase onto current master keeps the skip path.

## Safety of `index-regen-unavailable`

`DerivedImpactIndexRegenerator.RegenerateAsync` probes `dotnet --version`, then `pwsh -Version`, under a 10-second linked cancellation that kills the process tree. `RegenWhy` keeps only an ASCII letter, digit, or hyphen token of length 1–40. A missing tool returns `ToolchainUnavailable` with `dotnet-sdk`, `pwsh`, or `toolchain-check-timeout`. `RefuseRegeneration` then sets phase `Refused`, returns result reason `rebase_conflict` with the index path, and puts `index-regen-unavailable:<why>` in `Detail` and, for the unavailable case only, in `LastReason`. Content failures keep `LastReason` `rebase_conflict` and use `index-regen-failed:<why>`. The land service blocks the task, creates the merge helper, and appends that detail to the conflict event and the rebase stage outcome. It does not publish and does not move the candidate ref. The next land resets the disposable worktree in `PrepareLandWorktreeAsync`.

The probe starts `dotnet` and `pwsh` by bare file name, `UseShellExecute` false, working directory the land worktree, after `BuildDriverEnvironment.Apply` with `ANTIPHON_LINUX_COMPILER_POLICY=0`. It does not run the candidate's regenerate script or a worktree-local binary. `dotnet --version` can read `global.json`; it does not MSBuild the candidate. When both tools succeed, the existing slot-gated driver build and `regenerate-derived-impact-index.ps1 -NoBuild` still run, and the probe time is subtracted from the 180-second budget. The image is unchanged: the runtime stage stays `mcr.microsoft.com/dotnet/aspnet:9.0` with Git and curl, and the SDK stays in the publish stage. A runtime-container probe this review ran earlier exits 145 from `dotnet --version` (`No .NET SDKs were found`) and has no `pwsh` on `PATH`, so a container land stops at `index-regen-unavailable:dotnet-sdk`.

`LandGeneratedArtifactTests` at the tip: 5 total, 5 passed, including `Missing_selected_sdk_names_toolchain_in_conflict_and_status` (phase `Refused`, publication not `Landed`, reason `index-regen-unavailable:dotnet-sdk`, `NeedsResolution`, remote unchanged).

## Skip receipt

A target-manifest match records `graph=<target digest>` and `trust=target-manifest`. An embedded match records `graph=<candidate digest>` and `trust=embedded-manifest`. `HasVerification` keys off `VerificationSkipReason` (`non-code` or `docs-skip-target-manifest`) at schema 3. `BuildSlotNote` reads only the `; build-slot=` marker. `scripts/lib/post-land-early-warning.ps1`, `scripts/lib/release-impact.ps1`, and `scripts/check-evidence-diff.ps1` do not parse `trust=` or `graph=`. On current master the post-land early warning is `PostLandEarlyWarning.cs`; its `Skipped` helper filters paths, and the merge tree has no receipt parser of `trust=`. Old receipts without `trust=` stay valid. None of the ten failures is a receipt-parser failure.

## Docs

`docs/orchestration-loop.md` heading `### Landing toolchain recovery and FT-2 rollback (CARD-2022, CARD-2036)` is in the land runbook, immediately after the publication-recovery paragraphs (line 2114 on the tip and on the current merge). It names the three unavailable reasons, the Merge recovery on an SDK and PowerShell runner, and the schema-3 phases `Verified`, `PushStarted`, `PublicationConfirmed`, and `CleanupStarted`. Schema 3 goes `Verified` to `PushStarted`. `docs-skip-target-manifest` requires schema 3, so a pre-FT-2 server refuses that skip during publication or cleanup with `landing_transition_refused`. The current master's four-line edit to this file is the fast-track evidence-diff command rename. The rollback section is intact. Merge-tree against `72565797` exited 0.

## Evidence at the reviewed SHA

Native `dotnet build tests/Antiphon.Tests -p:UseAppHost=false`: exit 0, 0 errors. Every later Antiphon.Tests row used `--no-build` with the same property.

| Row | Result |
|---|---|
| CP-3 `LandGeneratedArtifactTests/*` | 5 total, 5 passed |
| `C_FT2_*` | 10 total, 10 passed |
| CP-4 `LandNonCodeSkipTests/*` | 122 total, 112 passed, 10 failed (the inherited set above) |
| CP-5 `DelegationWorktreeTests/*` | 58 total, 58 passed |
| CP-6 `AgentTaskLandingStateTests/*` | 83 total, 83 passed |
| CP-7 nine-class guard union | 84 total, 84 passed (floor 81) |
| CP-8 `DerivedImpactIndexTests/*` | 27 total, 27 passed |
| CP-9 `C1677TempFamilyRegistryTests/*` | SessionRunner build exit 0; 3 total, 3 passed |
| CP-10 land-graph `-Check` on the current merge | exit 0, digest `544627DF...`, count 80, `approved` |
| CP-11 pins at the tip | `policyHash=11897a91e82799521012ed5f457d8500549dba2fb50694ecca61e08b8fc57f2a` equals the live-lane digest and the pinned digest, exit 0. This branch does not touch the pin files. Current master re-pinned after the tip; merge-tree is clean, so the land keeps master's pins. |
| CP-12 evidence-diff `adce19001..e277c379` | commits=6, entries=0, violations=0, exit 0 |
| CP-13 merge-tree vs `72565797` | exit 0, tree `9ec683ed8434eea8a7a821abebbd1c9a389e8d85` |
| CP-14 index driver | exit 0 at the tip and on the current merge |
| regen on the current merge | `derived-impact-index unchanged`, exit 0; `LandGeneratedArtifactTests` remains a reader of `LandingEvidenceDto.cs` |
| OS-branch witnesses `adce19001..e277c379` | exit 0; `DerivedImpactIndexRegen` disposition portable |
| downgrade selector | `impacted-classes.ps1 -MergeSubset` reported `migrations=false` (0 migration files in the diff). Kinds were `temp-family-registry` and `test-lane-category`, covered by CP-9 and the CP-7 union. The broader high-radius notification classes were outside this closed checkpoint list. |

Historical CP-1 and CP-2 were the red TDD witnesses. They are green at this tip (the missing-SDK case is inside the 5/5; the receipt case is inside the 10/10 `C_FT2_*`).

## Advisories (card them; they do not hold the land)

- The end-to-end witness covers `index-regen-unavailable:dotnet-sdk`. `pwsh` and `toolchain-check-timeout` share `ToolAvailableAsync` and the same refusal shape, and they have no separate test.
- No production reader parses `trust=`. The state machine uses `VerificationSkipReason`. A future receipt splitter that assumes a fixed field count would need to accept both the old and the new command text.
- The rollback list names the schema-3 phases that can carry `docs-skip-target-manifest`. Schema-2 `TargetAdvanceStarted` and `LocalTargetAdvanced` stay on the older transition table, and that skip is refused there because it requires schema 3.
