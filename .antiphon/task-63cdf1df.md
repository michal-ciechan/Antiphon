# Merge 63cdf1df: STEP 0 landed; rc/2026-11-13 NOT cut (three inherited master reds)

Outcome: the four fixes are on master. Master went `d2ff7e8d4..251dee88e11ee48da66c34aa636f57c4b3e3eb41` as a fast-forward, a plain push that succeeded on the first try. I did not cut `rc/2026-11-13`. A3 fails, and the cut smoke has two more deterministic reds. All three come from the operator's master commits and are not timing. No tag, publish, promote or `-Update`. `rc/2026-11-12` was not touched.

## STEP 0 (landed)

Every pick was `cherry-pick -x` onto `d2ff7e8d4` and applied cleanly. Each picked file is byte-identical to its source branch tip, and master had not touched any of those files since `cad776695`.

| master | from | what |
|---|---|---|
| 4b05e0e5c | 79f8db3b0 | C1121 canceled-row retarget (Debug 94b17435) |
| 946637eac | f1a220891 | its why-missed note |
| 07dd28d56 | 574371536 | OutputCleanupTests stamp-back fix (Debug 8559e75b) |
| 45ea1d4cc | 0d95225a0 | its note |
| fe77d3a81 | 6ed72cf53 | profile-1543 Windows read-out (docs) |
| 2b8d083d0, 3b0ae86a0, a821de28c, bbf43863d, 251dee88e | fd585fe29, d1185e6ac, 8df732e4a, 3b831a19e, 6ecfdcc14 | CARD-1799 (report commits f0d53b51d and 5e1861103 skipped) |

STEP 0 checks, all at 251dee88e:

- AgentTaskLandReceiptWatermarkTests: 36/36. AgentTaskLandReceiptWatermarkSafetyTests: 18/18. Both include `dispatch-base-canceled-row`.
- ChannelBridgeTests 44 and ChannelContractsTests 14: green.
- Messaging Telegram, ContractSchema and WireContract: 78/78.
- OutputCleanupTests went red 6 times in the batch. Each was `read-stalled ... no progress within 3s` at load average ~30. The class alone was 45/45, plus 4 Windows skips, in 21 s. That makes the batch reds timing-only.
- A4 `-Check`: exit 0. No PublicAPI or property refusal. No refresh was needed.

## Cut prechecks at 251dee88e

- Name: `Test-ReleaseGateCandidateRef rc/2026-11-13` gives Ok, `long-lived`, would-be id `rc-2026-11-13-251dee88e11e`. No script refuses the name, and the naming scripts are unchanged since 5fef2c23b.
- A1: `refs/heads/rc/2026-11-13` is empty, both before and at the end.
- A2: rc/2026-11-12 5fef2c23b, -11 3364197bf, -10 74a8004f4, -09 0e7aef037 and -08 d164e93ae all show 0 `+` lines, and each is an ancestor of the pin.
- **A3: FAIL.**
  - policyHash: stored = computed = `7e54681b56c7925bef082d528cde3285ec579a13d51f34625c8c3aad1ee2dbb0`. OK.
  - `PinnedInputDigest`: stored `b507aab8fd1fb40e7458e89b58293d4b5ab099178a1d5faac756805eab5cb4e1`, printed `c5cf05b595d2e0fd6a8e2d1f0fd63b86970b0826d128e61f8849cfaccb9be8ee`. **Mismatch.**
  - The operator's nightly commits 93a64b5bb..77e0ca3db changed the protected inputs (`nightly-common/policy/coverage/core.ps1` and `test-execution-policy.json`) without re-pinning.
  - The witness `LiveLaneRunnerTests.WrapperOnlyPaidManifest` is red with `full-inputs` and `crlf-inputs`.
- A4: exit 0, `approved`, digest `F0AC784A390FF2CCBCC9317BA6B3AA03D42387A7558F326F2BC3CA5B1B95692D`, count 75. The operator re-recorded it at d2ff7e8d4, so it is not the 9F368441.../71 the brief expected.
- A5: LinuxOnlyWindowsExclusionPolicy 6/6. C1686_LinuxOnlyCensus 62/62.
- A6: 8/8.

## Build

One `Antiphon.sln` build, `UseAppHost=false`, native output, started 01:38:00Z. It took 4m52s and held the lease 293 s. 0 errors, 2102 warnings. No NETSDK1047, and no `-m:1`. `tests/Antiphon.IO.WriteBehind.Tests` is not a solution member.

## Smoke (one lease per row)

| Row | Result |
|---|---|
| Policy guard batch (own process) | **43/52, 9 red** (red 2) |
| nightly: NightlyCore, ReleaseSelectionCli, ReleaseSelectionHopSeed, LiveLaneRunner | **7/9, 2 red** (A3 witness, and red 3) |
| C1313*/C1336* methods | 34/35. `C1313_BaselineAuthority` was red in the batch (`baseline-full-bound`, 7m51s) but alone the script case passed 14/14. Load-sensitive, not a stop |
| Checkpoint: ColdBuild, TerminalReceipt, ReceiptSafety, C1217 contract, Manifest, Import, TrxReport, XmlInputSecurity, SourceExecution, AdmissionContract | 105/105, 6m00s |
| Land: C1677 Retention/DiskAlarm, C0824 slot server, LandingRemoval, LandBoundary x2, ProjectReadiness, TranscriptSinceClient, Hangfire x2, C1514TaskLoadWindow, Card1761 | 498/501, 2 skipped, 1 red: C1514 KnownBoundariesWithMissingInteriorAreIncomplete (Npgsql cancel). Alone it passed 1/1. Timing-only |
| Migrations: CardFilePrivacyMigration, TranscriptHotPathMigration, C1514HostLoadMigration, C0824RefOnlySnapshot, AppDbContextModel, AdditiveMigrationRuleWitness, Card1773TranscriptPromptStampMigration | 37/37 |
| C467* | 124/124 |
| Guards 2a: RcQualificationWatcherScript, RcFailedFirst x2 | 9/9, 19m07s |
| Guards 2b: MechanicalPc, TaskPlatformGuidance, C1729Layering, C0824DesktopLegacyRelease, LandGraphLandRefresh, C0824 RoleGate, TestSelection, ImpactedClasses, Server2Production, DockerStack, MergeHighRadius, ServerRepositoryMirror, ServerGitProcess, ServerImagePushCredential, GithubCredentialHelper, PwshPrivateCache | 481/489, 8 skipped (Windows), 0 red |
| C599A_ExecutionLedger* (once) | 6/6, 4m01s total, so each method is under 300 s |
| LandGraphEvaluationTests | 155/155 |
| E2E ContractSnapshot + AntiphonAppFixtureHostPath | 7/7 |
| SessionRunner: TranscriptSinceRead, PtyConditionalRead, NativeLinuxPolicyWitness, RunnerCapabilities | 28/28 |
| PtyHost / Agents.Pty witnesses | 3/3, 3/3 |
| WriteBehindFileSystemTests (linked into Antiphon.Tests) | 8/8 |

## The three master reds (all inherited, all deterministic)

None of the inputs below differs between d2ff7e8d4 and 251dee88e.

1. **A3 live-lane pin is stale.** Fix: set `$script:PinnedInputDigest` in `scripts/test-live-lane-run.ps1` to the printed value of the tree it lands on. At 251dee88e that is `c5cf05b5...`; at the current master ccd390978 it is `b499df304358cc5a1ac64adaa6a482fa9dc29a76bd3d9f6bcc65ba9a54c47faf`.
2. **lane-xor on `Antiphon.IO.WriteBehind.Tests.WriteBehindFileSystemTests`.** 6a371bc38 links the class into `Antiphon.Tests.csproj`, and the class has no Unit/Integration `[Category]`. This produces 9 guard reds: Registry_matches_compiled_metadata, C487_G068, C1383_Rebase x2, C1383_Valid_reason_pairs x4 and C1383_Layout(non-repository). Fix: add the category (and any registry row it needs). I found no card.
3. **`NightlyCoreTests.CoreMatchesFallback` `projectWalk` is red on Linux by construction.** a930059d8 marks `hidden/` with `FileAttributes.Hidden`. Linux ignores that attribute (I probed it: Attributes stay `Directory`), so both walks keep `hidden/Skip.csproj`. Fix: use a dot-prefixed directory on non-Windows, or gate that case to Windows.

Master moved again after my push, to `ccd390978` (7 operator perf commits to scripts, selection and nightly; 12f7c346a..ccd390978). That tip still has all three reds: the pin is unchanged and the test and category files are unchanged.

## Migrations

- Since rc/2026-11-12: exactly `20261108121426_Card1773TranscriptPromptStampIndex`. It is additive, a retryable CONCURRENTLY index on (AgentSessionId, Kind, Sequence), and sorts after `20261107104114_HostLoadRetentionCatchup`. Down drops it concurrently with IF EXISTS, together with the `_invalid` leftover.
- The already-applied `20260925081051_AddTranscriptHotPathIndexes.cs` was also edited, but only to change its helper: it is now `internal` and the filter is optional. Existing calls behave the same, and hosts do not re-apply that migration.
- Since rc/2026-11-08: the three host-load migrations, `20261107034430_HostLoadTelemetryS1`, `20261107040142_HostLoadSnapshotMembership` and `20261107104114_HostLoadRetentionCatchup`. All are expand-only. Plus CARD-1773.

## Changes since 5fef2c23b (90 commits to 251dee88e)

PRODUCT-affecting:

- Operator (Mike Ciechan, 11-07..11-09):
  - New `src/Antiphon.IO.WriteBehind` (6a371bc38, f9113c774).
  - `Antiphon.Nightly` project and nightly C#/perf work: 93a64b5bb, 25581a30e, a930059d8, eb7f9c68c, 5428f0511, cdb34950e, 68e276a9b, 51d387914, bd71d268b, 3cc2a4c7e, 7e34da97f, 9e1ec564b, a1459528c, b99b3ead4.
  - Checkpoint process-runner cache (887ca9f48).
  - `Antiphon.Scripts` CLI select-release (77e0ca3db).
  - sln folder (9eb8dd5d0) and land-graph re-record (d2ff7e8d4).
- CARD-1799 Telegram over-cap note and auto-reply. This changes the messaging wire contract and PublicAPI.
- CARD-1773: park prune, canceled terminal, pull dedupe, kind index and migration (ae5b545d9, 1ea3ce114, e0684c974).
- CARD-1764 gap-free since-cursor (87000d366).
- CARD-1794 retention budget and disk floor (aa600ddec).
- CARD-1787 land git identity allow-list and env scrub (cef76fe40).
- CARD-1788 host HEAD compare and /work chown scope (d95e5f0e1).
- CARD-1776 start-site land probe (6d3f02560).
- CARD-1790 private pwsh cache per test host and runner (e79c0e03b).
- Land-graph evaluator membership and reference fixes (f5cc4facf, 9de6cc1eb, c557ed31b, 99d828a43), with manifest refreshes (eaabe1af5, ce1639b17, ae6a3a71a).
- CARD-1783 merge high-radius subset on merge and rc cut (8b5d10264).

Tests: C1121 retarget, OutputCleanup stamp-back, CARD-1795 E2E fixture residue, CARD-1776 Windows mirror and credential roots, CARD-1773/1794/1787/1788/1764 witnesses, f05ef5a54.

Docs: why-missed notes, run a6410818 read-outs (groups 118, 131 and profile 1543), CARD-1790 plan, and nightly timing investigations.

## Proofs and cleanup

- `git ls-remote origin refs/heads/master` = `ccd3909780fe3e7a177effac627a6e25be42f0d7` (operator, after my `251dee88e`).
- `refs/heads/rc/2026-11-13` is absent.
- `rc/2026-11-12` = `5fef2c23b89414b8818aaf40db9ad738bce9524c`, unchanged.
- The build worktree, the tip worktree and the land-graph canary are all deleted. /work has 193G free.

--- next stage ---
next: decide
handoff: Commission one Debug/Code task for the three master reds: (1) re-pin PinnedInputDigest on the tree it lands on, (2) give WriteBehindFileSystemTests a Unit category, (3) make NightlyCoreTests projectWalk's hidden case portable (dot-dir on Linux or Windows-gated). Then re-run this cut from the new master, cutting rc/2026-11-13. Only the build and cheap checks need repeating (A1-A6, guard batch, nightly row, C1313/C1336) if the delta is those repairs plus the operator's script perf commits. After the cut: the Windows failed-first run commission. Host activation is a separate decision.
artifact: .antiphon/task-63cdf1df.md
