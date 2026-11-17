# CARD-1984 plan review, round 1

Final plan review of `docs/superpowers/plans/2026-11-17-card-1984-spawn-lane-plan.md`. Blockers only. Reviewer is Grok. Author is Claude Opus, plan task `003fea68-661a-45bf-9064-929839589a8a`, tip `420ceb9af4ef807a2e6bf0223b4b02aac6d46a57`.

Reviewed range `62cfb330883cda0959d4f87f8f456933f35ca1cc..420ceb9af4ef807a2e6bf0223b4b02aac6d46a57` (2 commits, the plan only). Fetched `origin/master` is `62cfb330883cda0959d4f87f8f456933f35ca1cc`. `git merge-tree --write-tree origin/master HEAD` exited 0, tree `466a11aaec27fd628ebbf103e84a730659276e0a`. `scripts/check-evidence-diff.ps1` reported commits=2, entries=0, violations=0. The subject changes no code and no tests, so the guard batch was not re-run. CP-1..CP-16 are the future Code rows; they were not executed. Mutation stays waived (operator 2026-11-12). Positive controls stay pending.

## Verdict

Two blockers. The census, the width formula, and the checkpoint table otherwise hold. O-1, O-2, O-3, O-4, and O-6 stand. O-5 does not.

## Blockers

### B-1. S5 restores the whole class onto a cap the plan's own floor misses

Where: plan §5 S5, §8 O-5, and the card's slice 3.

Failure: S5 changes `tests/impacted-classes.json` `remote-scripts` from five methods to `"classes": ["RemoteScriptContractTests"]` once CP-8 and the stress rows are under 15 minutes, and it tells the Merge process the class is no longer waived.

Why: Microsoft.Testing.Platform `--timeout` is a global test-execution timeout (`A global test execution timeout`, format `<value>[h|m|s]`), not a per-test cap. Merge and review briefs pass `--timeout 570s` on the whole `dotnet run`. 570 seconds is 9.5 minutes. The plan's own width-6 floor is `max(315s, 3600/6) ≈ 10–11 minutes`, already past 570s, before the strict-lane companions. The class `[Timeout(600_000)]` is per test and does not raise that session cap. The checkpoint tool does not pass `--timeout` (`BuildStep.RunArguments`), but `-MergeSubset` joins a non-land class into `CP-merge-subset-tests` (`scripts/impacted-classes.ps1` `New-FilterRows`). That row is EstimatedMinutes 5, so `RowTimeout.DeriveRowMinutes` is 15 minutes, and the fixed subset already occupies about 2 minutes of it. A class accepted at "under 15 minutes" can miss the joined row. This range's selector run was `migrations: false`, `kinds: []`.

Fix: do not put the whole class in `classes`. Shard the restored guards so each Merge `dotnet run` stays under 570s, and do not join those shards into `CP-merge-subset-tests`. A dedicated checkpoint row is admissible only when its derived deadline covers the measured wall and that row is not launched with `--timeout 570s`. "Under 15 minutes" is not the admission bar.

### B-2. W-5's allow rule cannot go green on the fixtures it scans

Where: plan §3 W-5.

Failure: the scanner fails a `/tmp/` or `/dev/shm/` literal that is not followed by `XXXX`, `$$`, a Guid or `mkdtemp` call, or the allowlist `/tmp/donor` and `/tmp/escape`. The safe idioms in the scanned tree put `mkdtempSync` before the literal, and several literals are in-container tokens.

Why: CP-1 includes W-5 and S1b expects it green. These current lines fail the written rule:

- `scripts/fixtures/c1008-fake-docker.sh:138` `fs.mkdtempSync('/tmp/c1008-pty-')`
- same file `:296` `fs.mkdtempSync('/dev/shm/c1008-preview-')`
- same file `:155` and `:556`, and `c1008-recycle-real-cases.mjs:515` and `:526`, the token `/tmp/antiphon-pty-hosts` (substituted or used inside the simulated root, not created as a host directory)
- `scripts/fixtures/c1105-git-audit.cjs:5` `fs.mkdtempSync('/tmp/c1105-audit-')` and `:892` `startsWith('/tmp/c1105-audit-')`
- `scripts/fixtures/c1105-compose-v218-config.json` `SessionRunner__PtyHostDir` `/tmp/antiphon-pty-hosts`
- `scripts/fixtures/c1192-workspace-contract.sh:306` the cleanup pattern `/tmp/c1192-parent.*` beside a correct `mktemp -d /tmp/c1192-parent.XXXXXX`

A stub that returns no findings is red for the right reason. The implemented rule, as written, stays red on code the plan does not change.

Fix: allow a literal that is the argument of `mkdtemp`, `mkdtempSync`, or `mktemp -d`, and allowlist the in-container `/tmp/antiphon-pty-hosts` token and the `c1192-parent.*` cleanup pattern. Keep the negative control on a fixed literal that is none of those.

## Census (re-derived)

No second shared resource that the SAFE rows would race.

- No `Environment.SetEnvironmentVariable` and no `Directory.SetCurrentDirectory` in the widened classes. Child `ProcessStartInfo.Environment` is per process. HostJq git is `git -C` the fixture root (`HostJqPrerequisiteScriptTests.cs:1280-1302`), after the scripts are copied there.
- Fake Docker state is `<fixture>/docker.json`. The mount storm runs inside `unshare --mount` (`c1008-fake-docker.sh:362-365`). It does not share the test process mount namespace. Container names live in that per-root state file.
- `C1008ComposeSeed` is one immutable `PerTestSession` object. `CloneInto` parses a stored string into the caller's root. A double factory would write two private temp roots.
- `C994ScriptProcess` always sets `WorkingDirectory` to the checkout (`C994ScriptProcess.cs:10`). The Rolling and RetiredTemp call sites use absolute fixture paths or `git -C` a fixture repo (`RollingVolumeRecycleScriptTests.cs:1456-1467`). They do not take the checkout `index.lock`. The plan's Gotcha sentence ("that writes there") matches that. D-5's first sentence, which keeps every checkout-cwd child strict, is wider than the slice table. Advisory below.
- Checkout writers the plan keeps at width 1 are the ones that write `.antiphon/` or run with the checkout as cwd and a production script: C727 wrapper and host-proof, the C849/C912/C913 front doors, and the retirement pwsh drivers. `RunCheckpointScriptTests` stays on `StrictClasses`.
- `VolumeRetirementDockerTests` has `[ClassDataSource<..., Shared = PerTestSession>]` and a checkout cwd. It is not in the widen set. Leave it there.
- Host limits on this runner: `nproc` 24, `pid_max` 32768, cgroup `pids.max` `max`, `ulimit -u` unlimited, `ulimit -n` 1048576. The profile's ~150 PIDs/s is one case's short-lived fork rate inside the fake-docker namespace (node about 35–67 ms), and the profile says the count is namespace-wide. Width 6 is on the order of tens to low hundreds of concurrent short-lived children, not a `pid_max` exhaustion. A leaked tree would be different; CP-11..CP-15 are the check, and one red stops the rollout.

## Width (D-2)

`RowRunner` sets `ANTIPHON_BUILD_SLOT_MAXCPUCOUNT` on command rows only (`RowRunner.cs:160`). A TUnit row inherits the executor environment and does not get that assignment (`BuildStep.RunArguments` has no such variable). `build-slot.ps1` exports the variable for a non-dotnet command, not for `dotnet`. The plan's UNOBSERVED case is the one the checkpoint rows hit: the variable is unset, and `Clamp(processorCount / 4, 2, 6)` applies. This host has 24 processors, so the width is 6. A 2-core host resolves to 2, which is the formula floor, and the rows are `serial: true`, so that host runs two chains and no sibling row. O-1 stands. The build-slot lease still grants `maxcpucount=6` to the build; the test phase then uses about six cores while other slots can use the rest. That is the lease the plan is spending, not a second grant.

## Witnesses

W-1, W-2, W-3, and W-4 can go red for the reasons in the table: a stub `Resolve` of 1 fails the width rows, width 1 makes the rendezvous time out, the families are still on the strict lane, and a walker stub that does not reject a method that reaches `Process.Start` fails the negative control. W-6 is an extension of an existing roster. W-5 can go red against its negative control and cannot go green against the current tree (B-2).

## Checkpoints

Read-only import at this tip, load 2.20, lease `58f4b6ce-9296-4342-8c7a-d953b5741f0f`, `dotnet run --no-build` of the already-built checkpoint tool: exit 0, 16 rows, no warning line. Output `.antiphon/c1984-plan-import.yaml` (gitignored). Every row `serial: true`. Output paths end with `/`. Estimate 15 derives 45 minutes (CP-1, CP-5, CP-7, CP-8, CP-11..CP-16). CP-3 estimate 6 derives 18 minutes. The other short rows derive 15. CP-8's 45-minute deadline does not kill an 11–15 minute class. Pairing the stress rows keeps one run near 22 minutes of expected test wall; the derived total for two estimate-15 rows is 70 minutes. The table is `### Checkpoints`, CP-digits only, one build id and one filter per row. The OR filters match the selector shape (`/*/*/(Class*)|(Class*)/*`), which this repo already runs. Min floors are below the plan's own arithmetic (CP-1 14 against 13 argument rows plus the probe pair, CP-5 75 against 80+12+1, CP-8 500 against 511–516). TestDesign is already assigned to pin them. That slack is not a blocker.

## Slices and collisions

`tests/derived-impact-index.json` stays regenerate-at-tip, never hand-merged, including against CARD-1820 and CARD-1893. S5's `remote-scripts` edit is B-1, not a textual overlap with the CARD-1820 selector sources: the plan correctly forbids editing `ImpactedClassesSelectorTests.cs` and `scripts/lib/release-impact-scripts.ps1`. The sentence to change in `docs/testing-and-build.md` is the Impacted classes line "Remote deploy checks and the statement-budget checks stay method-scoped." The waiver text the plan quotes is in Merge briefs and `docs/superpowers/plans/2026-11-17-card-1960-checkpoints.md`, not in `docs/orchestration-loop.md`.

## Operator decisions

| # | Review |
|---|---|
| O-1 | Confirm width 6 on this 24-core host. The checkpoint test host does not see `ANTIPHON_BUILD_SLOT_MAXCPUCOUNT`; the processor fallback is 6 here and 2 on a 2-core host. |
| O-2 | Confirm. Keep checkout writers and `RunCheckpointScriptTests` on the strict lane. |
| O-3 | Confirm the five stress runs once, in pairs, before any Merge restore. |
| O-4 | Confirm, last, and only for methods the W-4 walk accepts. |
| O-5 | Object. See B-1. Do not restore the whole class under a 15-minute bar. |
| O-6 | Confirm Windows width 1 until a WSL sample exists. |

## Advisory (does not hold the next stage)

Align D-5's first sentence with the Gotcha line "that writes there". `C994ScriptProcess` sets the checkout as cwd for `git -C` a fixture repo and for `chmod` of an absolute fixture path. A literal reading of "any checkout cwd stays strict" would leave the Rolling saving on the width-1 lane. The slice table is the rule to implement.
