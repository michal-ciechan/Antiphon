# rc/2026-11-11 cut

Outcome: cut `rc/2026-11-11` from master at `3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e`. Candidate id `rc-2026-11-11-3364197bf1cb`. Plain push, not a force. No tag, publish, or promote. `rc/2026-11-10` was not moved.

## Remote proof

Immediately before the push, `git ls-remote` showed master at `3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e` and no `refs/heads/rc/2026-11-11`. After `git push origin 3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e:refs/heads/rc/2026-11-11` (exit 0, new branch):

- `refs/heads/rc/2026-11-11` = `3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e`
- `refs/heads/master` = `3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e`
- `refs/heads/rc/2026-11-10` = `74a8004f405bc54666fea33889839f0ff4d62ec7` (unchanged)

## Name

`rc/2026-11-11` matches the long-lived pattern. `Test-ReleaseGateCandidateRef` accepts it (`Kind=long-lived`, candidate id `rc-2026-11-11-3364197bf1cb`). `rc-qualification-status` only checks the candidate-id charset. No script refuses this date. `release-cut.ps1` and `release-candidate.ps1` journal an existing ref and were not used to create the branch (`rc-ref-missing` if the ref is absent). The plain push is the create.

## Prechecks at the pin

- A1: `refs/heads/rc/2026-11-11` was empty before the push.
- A2: `git cherry <pin> <rc>` printed no `+` lines for `rc/2026-11-10` (`74a8004f`), `rc/2026-11-09` (`0e7aef0379`), and `rc/2026-11-08` (`d164e93a`). Each tip is an ancestor of the pin. Re-checked after the smokes, still empty.
- A3: stored `policyHash` = recomputed `c6da42db3549444e55fcf35ead72f674861dd24f4809bc924780bebcbd07f39d`. `PinnedInputDigest` literal = printed digest `2e519236f898920407b63636797731c00aa73e22f01727d2f92872126cbc56f3`.
- A4: `scripts/audit-land-verification-graph.ps1 -Check` exit 0. Digest `E0565240FF824353AA5AA6B90D579DE5C7AEE799C6A73D5990F946C1D780BC2D`, count 71. `-Update` was not run. SDK on the host is `10.0.403`.
- A5: `LinuxOnlyWindowsExclusionPolicy` 6 passed, 0 failed. Linux-only exclusion rows 93 (antiphon 63, session-runner 21, agents-pty 5, pty-host 4). `C1686_LinuxOnlyCensus` 62 passed, 0 failed.
- A6: `scripts/test-rc-coverage-admission.ps1` 8 passed, 0 failed.

## Build

One `Antiphon.sln` build, `UseAppHost=false`, native output, no `-m:1`. Start `2026-11-08T07:38:30Z`, end `2026-11-08T07:42:28Z`, elapsed `00:03:55.81`, 0 errors, 2069 warnings (existing NU1902 / CS8620 / TUnit0015 / AD0001). Lease `1e37f0b4-f549-4c3d-ae9d-92a3d587ba57`. No NETSDK1047.

## Smoke

All `--no-build`, `UseAppHost=false`, one build-slot lease each. Green rows were not rerun.

| Row | Result |
|---|---|
| Guards (policy, witnesses, slow-test, mechanical PC, platform, layering, snapshots, rc watcher, failed-first, land-graph refresh, orchestrator bundle, C0824 role/legacy/ref-only) | 540 total, 530 passed, 10 skipped, 0 failed, 40m 25s. The 10 skips are Windows-only. |
| Migration classes | 72 total, 70 passed, 2 hook timeouts (below), 8m 28s |
| Land, retention, Hangfire, land-notification, runner-read poll | 358 passed, 0 failed, 5m 51s |
| `/*/*/*/C467*` on Antiphon.Tests | 124 passed, 0 failed, 3m 53s |
| `C1149_C1150_Statement_budgets*` | 14 passed, 0 failed, 34s |
| Checkpoint cold-build, terminal receipt, receipt safety, C1217, manifest, import, TRX | 77 passed, 0 failed, 3m 08s |
| Five `RemoteScriptContractTests` deploy methods | 5 passed, 0 failed, 1m 24s |
| `C599A_ExecutionLedger*` once | 6 passed, 0 failed, 4m 21s. Each under 300s: Joins 54.7s, Chunks 54.2s, Markers 41.9s, ExecutionLedger 39.8s, Receipt 33.2s, Counts 32.3s |
| SessionRunner witnesses, HostStatsSampler, C0824 slot classes | 120 passed, 0 failed, 1m 19s. CP-4 witness methods present: `Policy_rows_require_pinned_attribute`, `Pinned_attribute_contract_matches_native_platform`, `Markers_do_not_expand_policy_exclusions`, `Registry_matches_compiled_metadata` |
| PtyHost witnesses | 4 passed, 0 failed, 709ms |
| Agents.Pty witnesses | 4 passed, 0 failed, 1.2s |
| E2E `ContractSnapshotTests` | 5 passed, 0 failed, 2m 04s |

One C467 command used a bare `|` between two full paths. On TUnit 1.44 that form runs far wider than the named methods (`docs/testing-and-build.md`, combined class filters). That process was stopped and is not evidence. The replacement filter `/*/*/*/C467*` is the row above.

### Timing-only, not a stop

`C1514HostLoadMigrationTests.PrepareHistoricalBaseline` exceeded its 60s BeforeTest budget on two parameter rows. Test-body time was 0ms. Both stacks were inside `MigrationTestDatabase.CreateAsync` while `DropClonedDatabaseAsync` waited on Npgsql:

- `UpgradeAndRefusedDowngradePreserveDurableRows(20261107104114_HostLoadRetentionCatchup, 20261107040142_HostLoadSnapshotMembership)`
- `UpgradeAndRefusedDowngradePreserveDurableRows(20261107034430_HostLoadTelemetryS1, 20261106032116_AddRefOnlyParkWorkspaceSnapshot)`

The other 70 migration rows in that run passed, including the non-database C1514 assertions. This is the same kind of timing overrun the brief allows for `C599A_ExecutionLedger`. Those two rows were not re-run. The execution-ledger row itself finished under 300s on this host.

## Since rc/2026-11-10 (`74a8004f`)

51 commits. Path counts: 109 test files, 8 docs, 8 scripts, 29 other (server, session-runner, checkpoint tool, land-graph audit, AGENTS.md, orchestrator bundle and skills).

Product-affecting commits include:

- CARD-1745 Linux server SHA restart gate (`2b0468947b`, `b4c226c2e4`)
- CARD-1749 land sibling deadline from GitSettings (`c4592abe2f`)
- CARD-1766 sequence-poll route (`2cd6d23b0e`, `abad143eff`)
- CARD-1767 skip the dead owner read (`80cb252b9e`)
- CARD-1768 Hangfire lease held until the host stops (`d116a618f1`, plus `HangfireConfiguration.cs` in `51f90a0d58`)
- CARD-1769 land-notification parking and rescan (`7f31d58017`, `593ba300b7`)
- CARD-1772 done-receipt gate in checkpoint `WaitCommand` (`83ce597ee6`)
- CARD-1753 land-graph refresh without asking (`cfb9c91b7c` through `7ddd79885b`, manifest `d0a3ee89e4`)
- Source-generated runner read JSON (`91fac7eb2d`)
- Unreadable release-evidence refusal and single-attribute path walk (`fbf9418c1f`, `df019b1604`)
- Residue worker kept until `FinishedAt` is stored (`640e560542`)

Tip `3364197bf1` only adds the workspace-slots reader to extracted deploy harnesses (tests). Docs: CARD-1744 cutover plan (`b1e129b448`), Review-column close-out (`6ccf8896e6`), CARD-1765 budgets (`cc3a2b2fe1`), evidence-path positive controls (`70aec174ed`).

## Migrations hosts apply at startup

Since `rc/2026-11-10`: no new files under `server/Migrations`.

Since `rc/2026-11-08` (`d164e93a`), three expand-only Ups. Each Down throws `NotSupportedException` (no downgrade):

- `20261107034430_HostLoadTelemetryS1` — add columns and create the host-load tables and indexes
- `20261107040142_HostLoadSnapshotMembership` — add `CreatedTransactionId`
- `20261107104114_HostLoadRetentionCatchup` — create `HostLoadRetentionStates`

Those three are already on `rc/2026-11-10`.

## Not done here

No tag, no `publish-release.ps1`, no `promote-release.ps1`, no land, no push to `rc/2026-11-10` or any older rc line, no `land-graph -Update`. Windows failed-first qualification and host activation are the next decisions, not this cut.
