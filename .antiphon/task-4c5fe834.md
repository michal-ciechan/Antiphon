# CARD-1777 Review, round 1 (Final): CLEAN

Subject: `origin/feat/card-task-999353af` tip `73ebc2c5d5b769e376c1fe1546eff5bd89cf2624`, Code by Grok.
Range reviewed: `3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e..73ebc2c5d5b769e376c1fe1546eff5bd89cf2624` (4 commits).
Verdict: no blockers and no regressions. Three advisories (A-1..A-3) below. None of them holds the land.

## Brief checks

| Check | Result |
|---|---|
| Unset or whitespace `ANTIPHON_API` exits 2 before any operator-token read or docker call, and names `docs/docker-stack.md` | Holds. `scripts/deploy-server2.ps1:50-54` sits before the token read at `:61`. The only earlier statements are parameter guards, a `git rev-parse` for `retire-temp`, and dot-sources whose top-level code only assigns strings and heredocs. The witness covers `deploy-temp` and `check-census`, with the variable removed and with `"   "`, and checks the exit code, the message, no `OperatorTokenMissing` and no docker or ssh marker. |
| A set value is used | Holds. `Set_api_value_is_used_for_the_census_read` sees exactly `<api>/api/projects?includeArchived=true` on a loopback listener. The output does not echo the origin or the token sentinel. |
| `check-census` still skips the operator token | Holds. `:61` is unchanged. `Check_census_does_not_read_the_operator_token` runs with no token file and gets no `OperatorTokenMissing`. |
| `-Phase all` unchanged | Holds. The ValidateSet, the default and the phase list at `:1547` are untouched. |
| `totalTimeoutMinutes` 50 to 52 | Justified. The four plan rows estimate 4+6+6+5 = 21 minutes, so the importer floor is 2*21+10 = 52. The importer enforces that floor: my own extract was refused with `checkpoint-total-below-derived`. This is a run budget, not a loosened assertion. |
| Policy digests | They recompute equal. `Get-NightlyPolicyHash` gives `8db98312b28b5b3c2e9ac8bbf1345b46660b5af51e08d52d20670e57f46b04d6`. `test-live-lane-run.ps1 -PrintInputDigest` gives `eb1717de2c6c17344b0661ff071fd972a0a5cbdf2e3f96e8f7addde29efddf2f`. |
| No secret in logs or docs | Holds. The tests use synthetic tokens only. The docs give names and paths only, and the script never prints the token value. |
| Witnesses are real | They are. 3 + 17 = 20 executed. At red commit `72dc9b4e61` the script is byte-identical to master, so `Unset_refuses...` must fail: the message is missing and the master path emits `OperatorTokenMissing`. Its assertions also go red if the token read or a docker or ssh call moves ahead of the check. |
| Fixture updates | The three harnesses that spawn the script (`test-deploy-server2.ps1` Run-C727, `HostJqFixture`, and the C849 wrapper in `RemoteScriptContractTests`) default `ANTIPHON_API` to `127.0.0.1:9` only when it is unset. `C1008WrapperFixture` already set the variable. The `VolumeRetirementWrapperTests` unbound case still fails on `ShaRequired` first. |
| `check-evidence-diff.ps1` | `commits=4 entries=0 violations=0`, exit 0 |
| `merge-tree` against master `3364197bf1` | Clean (tree `eff0511be0c0`) |

## Checkpoint evidence (server2, all at source `73ebc2c5d5`, state clean, buildSource verified)

| Row | Filter | Result |
|---|---|---|
| CP-3 (plan row) | `(DeployServer2ApiDefaultTests*)\|(LinuxServerRestartGateTests*)` | 20/20 |
| R-1 | `(HostJqPrerequisiteScriptTests*)\|(HostJqFixtureClosureTests*)\|(LiveLaneRunnerTests*)` (touched fixture and pinned digest) | 46/46 |
| R-2 | RemoteScriptContractTests `C849_Seed*`, `C946_*`, `C983_Required_jq_*` (touched C849 wrapper and harness runs) | 10/10 |
| G-1 | Guard batch `TestClassificationPolicy*\|TestClassificationGuard*\|SlowTestRegistryMerge*\|SlowTestTripwire*\|NativeLinuxPolicyWitnessTests*` | 50/50 |
| R-3 | RollingVolumeRecycleScriptTests `C1008_Legacy_rolling_and_jq_rosters_remain` (full patched roster, 4 modes) | 4/4 |

- Run `20261108-111505-5da2` (CP-3, R-1, R-2, G-1) is GREEN. `validate --expected-source-sha 73ebc2c5d5...` returns `CHECKPOINT SOURCE VALID rows=4`.
- Run `20261108-102305-3d53` (R-3) is GREEN and validates (`rows=1`).
- Run `20261108-095553-d366` had CP-3, R-1, R-2 and G-1 green. R-3 hit the row deadline because I estimated it at 4 minutes. It really takes about 16.5 minutes: 4 modes x up to 400 s each. That was my plan error, and the rerun above replaces it.
- Run `20261108-105005-17a1` had CP-3 and G-1 green, with R-1 at 12 failed and R-2 at 1 failed. Every failure fell between 11:11:28 and 11:12:21 host time. In each one a pwsh child aborted with exit 134 and `Stack overflow.`, including in `LiveLaneRunnerTests`, which does not run `deploy-server2.ps1`. The same SHA was green before and after that window (see A-3).

## Advisories (go to cards, never hold the land)

- A-1. `docs/docker-stack.md:926-928` still says in the present tense "Its default API is `http://localhost:17202` (`deploy-server2.ps1:44`)". The new paragraph at `:1061` calls this the previous default, but the older sentence now reads as stale. Fix: rewrite it in the past tense or link to `:1059`.
- A-2. The runbook command lines (`docs/docker-stack.md:32`, `:58-66`, the rollout table `:122-130`, `:538`, `:577`) do not mention `ANTIPHON_API`. The "What deploys" paragraph says the variable is required but gives no value for each stage. Before the cutover the desktop value is `http://localhost:17202`. After it, the value is the Linux server origin. An operator in a plain desktop shell now gets exit 2 at the next rollout. That failure is closed and the card requires it, but the doc should name the value.
- A-3. The infra flake on loaded server2: pwsh children aborted with exit 134 `Stack overflow.` in one 53-second window and affected unrelated classes. It is not caused by this change. Card it if it recurs.

## Merge note

CARD-1775 (`feat/card-task-c0a0ea0f`, Merge batch 126) reseals the same two digests. `git merge-tree HEAD origin/feat/card-task-c0a0ea0f` conflicts in `scripts/test-live-lane-run.ps1` (`PinnedInputDigest`) and in `tests/test-execution-policy.json` (`policyHash`, plus the adjacent NativeLinux row). The Merge has to keep both NativeLinux rows and then recompute both values together:
- `policyHash` with `Get-NightlyPolicyHash` (dot-source `nightly-common.ps1` and `nightly-policy.ps1`)
- `PinnedInputDigest` with `pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest`

This is not a blocker.

## Disclosures

- One unlisted build. I ran `dotnet run --project tools/Antiphon.Checkpoints -- --help` once outside a build slot to read the tool's usage, which compiled the checkpoint tool. Every test ran through the checkpoint tool, which takes its own slot.
- Wall time went well past the 40-minute budget. The cause was my R-3 underestimate, followed by the transient window, which forced two extra runs.
