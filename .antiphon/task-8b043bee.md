# CARD-1677 S3 Review (round 1 of 3, full) - task 8b043bee

Outcome: S3 is clean. I found no defect. CP-10 is 41/41 and CP-11 is green, both re-run through the checkpoint tool at 19062e44923759ec3ee55075e5835acbe52f13f0 with valid receipts. PC-116 to PC-120 and the three extra mutants each went red on their named witness. The whole Unit lane was **not** run, so the scope is Interim.

- Subject: origin/feat/card-task-0f317c85, tip 19062e44923759ec3ee55075e5835acbe52f13f0. The code tip is 461e4081a9d991d1f30a1039adacdd316e871ad2; the tip adds only the report.
- Landing owner: the Code task 0f317c85.
- Review branch feat/card-task-8b043bee. It holds this report only.

## Checkpoints (fresh, one run per row)

| Run | Row | Result |
|---|---|---|
| 20261103-064927-f92d | CP-10 | GREEN executed=41 passed=41 failed=0 skipped=0, slot=granted waited=0s, sourceState=clean buildSource=verified, `validate` VALID. Phases: build 244 s, startup 55 s, tests 6.8 s. Bin deleted by the tool |
| 20261103-065638-49ca | CP-11 | GREEN exit=0, slot=granted waited=0s: typecheck PASS, lint PASS, attentionVisuals.test.ts 32/32 (console.log), `validate` VALID |

```
CHECKPOINT CP-10 commit=19062e44923759ec3ee55075e5835acbe52f13f0 build=ok filter=/*/*/(C1677DiskAlarmTests*)|(AttentionKindWireTests*)/* executed=41 passed=41 failed=0 skipped=0 trx=/work/worktrees/task-8b043bee/.antiphon/checkpoints/20261103-064927-f92d/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=19062e44923759ec3ee55075e5835acbe52f13f0 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=19062e44923759ec3ee55075e5835acbe52f13f0 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 attentionVisuals.test.ts executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=19062e44923759ec3ee55075e5835acbe52f13f0 sourceState=clean buildSource=notApplicable
```

The checkpoint tool itself was built by `dotnet run --project tools/Antiphon.Checkpoints -- start`, the documented entry point.

## Mutants (informational; every PC stays pending for SourceLanding Mutation)

**Method.** I used a detached scratch worktree at 19062e44 with every mutant gated by the environment variable `C1677_RV_MUTANT`. It had one gated build (`build-slot.ps1 -Label c1677-rv-mutants`, waited 15 s, 3m15s). Each mutant then ran under its own leased `dotnet run --no-build -p:UseAppHost=false`. Both files were restored with `git checkout -- <path>` and the worktree was removed with `git worktree remove`, without `--force`.

**Control.** With the gate off, the whole class passed 36/36.

| Mutant | Break | Witness | Result |
|---|---|---|---|
| PC-116 | the first disk counts instead of the configured path | Only_the_configured_volume_counts | 1/1 red `other-volume-no-row` |
| PC-117 | freshness check removed | Stale_sample_raises_nothing | 1/1 red `stale-16-min-no-row` |
| PC-118 | `>=` becomes `>` | Work_at_85_percent_raises_one_warning | 1/1 red `boundary-85-one-row` |
| PC-119 | threshold `UsedPercent - 1` | Work_below_85_percent_raises_nothing | 1/1 red `below-85-no-row` |
| PC-120 | alarm every runner in the cache (path /work) | Unlisted_runner_raises_nothing | 1/1 red `unlisted-runners-no-row` |
| extra D-11 | desktop skip removed | Unlisted_runner_raises_nothing | 1/1 red `desktop-listed-still-no-row` |
| extra overflow | `checked` long compare instead of Int128 | Missing_or_unusable_reading_raises_nothing | 1/9 red (huge-total), OverflowException out of `GetAsync`. A throw does reach the test, so the witness can see one |
| extra retention | Execute read from the Enabled key | Retention_mode_is_read_from_configuration | 2/6 red (true,null / true,false: DryRun expected) |

## Verification points

**1. The boundary compare and throw safety.**

- The compare is `Int128 100*(Total-Free) >= UsedPercent*Total`, guarded by `Total > 0` and `0 <= Free <= Total`.
- 85.0 percent alarms and 84.9 does not. That is exact, and PC-118 and PC-119 kill both off-by-ones.
- None of these raises an alarm or throws: total 0, negative free (used > total), free > total, a huge total, an absent cache, entry, `Current` or volume, a failure-only reading, or a stale sample. The `usedPercent` display division runs only after the guard.
- The one remaining path that could throw is a JSON `null` element inside `Disks`: `entry.Path` would hit a null reference. The runner probe never emits one (advisory A-4).

**2. De-duplication and recovery.**

- The row is computed on every read with `ConditionKey workspace-disk-low:<runner>`, so there is one row per runner. It clears when the reading recovers or goes stale.
- I judge stale = no alarm to be correct. A runner that stops reporting would otherwise keep showing a frozen "full" row. The silent runner is surfaced instead by RunnerUnavailable (RunnerAlarmState episodes) and by the host-stats state.
- Freshness is measured from when the sample was first observed (pre-existing `Record` semantics). A runner that repeats one old sample therefore goes stale, and the test checks this.

**3. The desktop is never alarmed.**

- The code default is an empty map, and appsettings.json lists only server2.
- The validator refuses the desktop id (case-insensitive), a blank id, and a blank or relative path.
- The builder also skips the desktop. The D-11 mutant shows this skip is load-bearing for a listed desktop.

**4. Configuration.**

- Dictionary binding does merge keys: configuration can add or overwrite a key but never remove one. The empty-in-code choice is therefore right.
- The same limit applies to the appsettings.json `server2` key. An overlay cannot drop it. Setting it to `""` makes the validator refuse startup, so the only per-runner off switch is to repoint the path, or the global `Enabled=false` (A-1).
- **Key names match S1.** S1 (origin/feat/card-task-1296ccbd, dc33c3e42) defines `RunnerWorktreeRetentionSettings.SectionName = "RunnerWorktreeRetention"` with `bool Enabled` and `bool Execute`. There is no `DryRun` key, so there is no mismatch.
- S1's appsettings.json still ships `Enabled=false, Execute=false`. The operator's on-by-default / Execute decision is not on the S1 branch yet; that belongs to S1, not S3.
- Until S1 lands there is no `RunnerWorktreeRetention` section, so the evidence line says `retention=Off`. That is accurate, because no retention job exists without S1. After S1 it reads the same keys.
- **A-2.** The mode ignores `RunnerWorktreeRetention:Runners`. With Enabled=true but server2 not in that list, the line would claim `DryRun` or `Execute` for a runner that retention never contacts. This is cosmetic.

**5. Client.**

- `ATTENTION_VISUALS.WorkspaceDiskLow` is a warning in the review bucket, labelled "report only". The `UNKNOWN_ATTENTION_VISUAL` fallback in `visualOf` is untouched.
- `targetOf` returns null, the same as for RunnerUnavailable.
- No contract snapshot carries attention kinds: no attention fixture exists in `client/src/test/fixtures/contract/`, and `ContractSnapshotTests` has no attention reference. ContractSnapshotTests was therefore not run.
- No server consumer of the feed switches on kinds exhaustively: DecisionCardNotifier, BlockedTaskNotifier and AwayDigestProjection all filter on their own kind.

**6. Windows and build.** No csproj, pins or land-graph change. The code is platform-neutral. DI resolves the three optional trailing constructor parameters, because `HostStatsCache` and `IConfiguration` are registered singletons and there is a single constructor.

**7. Merges and evidence.**

- check-evidence-diff over 7b920d84a..19062e449: commits=5 entries=1 violations=0, exit 0.
- `merge-tree` onto origin/master 59a37c1675862a7ccc8bc7bc36769e2ac50bc0b8: clean.
- Onto S1 (origin/feat/card-task-1296ccbd): clean, tree a358cd5d. Program.cs and appsettings.json do not collide.
- Onto rc/2026-11-01 (10a826b9d): a whole-branch merge-tree conflicts in `client/src/api/agentTasks.ts`, which is master drift and not S3. The two S3 commits e34c59fcf and 461e4081a cherry-pick clean when chained with `merge-tree --merge-base`. The build on rc was not run.
- rc has none of the CARD-1663/1666/1671 commits; master has 7, 2 and 2. rc does have `CeilingHold=64`, so 65 is free there too.

## Advisories (no Review rerun needed)

- **A-1.** No per-runner opt-out: an overlay cannot remove the appsettings `server2` key, and a blank value fails validation at startup. The levers are `Enabled=false` or repointing the path.
- **A-2.** The retention mode in the evidence line ignores `RunnerWorktreeRetention:Runners`.
- **A-3.** `SinceUtc` is the latest sample's first-seen time. It moves roughly every 5 s, so the row's age always reads "just now" rather than the time the disk crossed 85. RunnerUnavailable uses the onset. The `keyOf` identity is the conditionKey, so the row does not remount.
- **A-4.** A `null` element in `Current.Disks` would throw a null reference out of the feed. Only a malformed runner response can produce one. `entry is not null &&` would close it.
- **Plan wording.** §3.6 says "the evidence of a current row notes `stale`", but the implementation simply drops stale rows. Dropping is the safer reading.

## Not run

- **The whole Unit lane (Final profile).** Not run. It is about 3 h on server2 against a 60-minute round budget, and the brief closed the list at CP-10 and CP-11 plus the mutants. **ordinaryScopeCompleted: Interim.**
- ContractSnapshotTests: not applicable (no attention snapshot).
- The rc build of the cherry-picked S3 commits.
