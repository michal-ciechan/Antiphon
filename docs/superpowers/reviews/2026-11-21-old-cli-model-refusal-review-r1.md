# Old-CLI GPT-6.2 launch refusal, review round 1 (Final)

Verdict: one blocker. The cold-launch refusal is safe on the observed fleet, and the witness is pinned to the 0.169.1 floor. `tests/derived-impact-index.json` at the tip does not match the generator.

Reviewed source `bfac226447480de49587f7eeac54cef003aa65f5` on `origin/feat/card-task-02bed7b0`. Range `50fca3a071851dcc8d4545fa6597e41f99e7b0f2..bfac226447480de49587f7eeac54cef003aa65f5`. Debug owner `02bed7b0-7da8-4208-8fc8-aad0d64e151a` (Succeeded, branch `feat/card-task-02bed7b0`, title `Debug: rc run 3528e6b1 group-45 C959 old-CLI model selection`). Author is Codex. This review is Grok. Review task `dc1c14f7-3dc7-42d0-866e-daee3e1ee412`.

The range is 11 files, +166/−14. Commits: `a2c4eafe5e5bc5d35ecc71738920ad59b7214dc4` red witness, `da09ae742a0531934cd939f6330830c4eefcf376` the refusal, `bfac226447480de49587f7eeac54cef003aa65f5` resolved-executable matching. No migration. Positive controls stay pending. Mutation is waived (operator 2026-11-12). This review did not pause for that waiver.

The rc failure itself needs no product change. `CodexCliObservationTests.C959_Old_version_with_floor_never_refuses` passes at this tip (it is inside CP-2). The branch adds a real cold-launch gate on top of that already-green fixture repair.

## Behavior

`CodexCliVersionProbe.RequireSupportedModel` runs from `SessionRunnerRuntime.StartAsync` before the workspace gate, launch lock, custody, and session registration, and from `PhoneHomeCommandDispatcher.LaunchAsync` before the launch-generation record and `RecordLaunch`. A null probe returns without throwing. The interface default is a no-op. Continue and input do not call it. A warm pool reuse does not relaunch. A resume that starts a process goes through Launch or StartAsync and is checked.

The refusal requires every one of these: Codex transcript format (ordinal, ignore case); the last `--model`, `-m`, or `--model=` before `--` is exactly `gpt-6.2-sol` or `gpt-6.2-astra` (ordinal, case-sensitive); the snapshot is fresh (`checkedAt <= now` and age at most `MaxAgeMinutes`, default 15); the error is null, `stderr_output`, or `output_truncated`; the version parses and `CompareTo(floor) < 0`; a fingerprint is present and `MatchesDefaultLauncher` is true. Anything else returns. Unknown, stale, clock skew, a malformed version, any other probe error, a fingerprint or env-hook mismatch, a bare Herdr command, and the caught IO and resolution exceptions fail open.

`CodexCliVersion.Gpt62CatalogFloor` is `Parse("0.169.1")`, the same value `ModelLevelAliases` uses for both GPT-6.2 slugs. `CompareTo` orders major, minor, then patch as integers, then SemVer prerelease. Build metadata has no weight. `0.170.0` is greater than `0.169.1`. Equal `0.169.1` does not refuse. A prerelease of `0.169.1` is below the release and would refuse. `0.170.0-alpha.1` stays above `0.169.1` because the core triple wins. The documented floors for explicit `gpt-6-astra` (0.153.4) and `gpt-6.1-sol` (0.159.1) are not enforced here. Grok and Claude transcripts are untouched. A non-exact slug, including a different case of a 6.2 slug, is untouched.

`MatchesDefaultLauncher` runs only after the version is already below the floor. Null or whitespace exe, a null env, a nonempty `NODE_OPTIONS`, `NODE_PATH`, `LD_PRELOAD`, `LD_LIBRARY_PATH`, or `DOTNET_STARTUP_HOOKS` on the request or the runner process, a bare Herdr name that is not a Windows `*.cmd`, a fingerprint mismatch, or a resolver refusal all fail open. Paths compare ordinal-ignore-case on Windows and ordinal otherwise. `Resolve` still requires a native header, so a wrapper stays `launcher_unverified` and does not refuse.

Live `GET /api/session-runners` during this review: desktop (windows) `codexCliVersion=0.170.0`, checked 2026-11-21 21:41:21, error `stderr_output`, `codexCliVersionStale=false`, available and dispatch-eligible. server2 (linux) `0.170.0`, checked 2026-11-21 21:39:37, error `stderr_output`, stale false, available and dispatch-eligible. server2-temp has no version and is not available. `stderr_output` is an allowed advisory, and `0.170.0 >= 0.169.1` returns before fingerprint matching, so this check would not refuse desktop or server2. An empty observation does not refuse server2-temp.

The HTTP mapper already turns every `CodexLaunchException` into 409 with `type` equal to the exception code. Phone-home catches `codex_cli_model_unsupported` and returns that code at 409. `ConflictException` carries the human detail as its message. `LaunchInteractiveAsync` marks the session Failed with that sentence and releases session consumers. `AgentTaskLiveness.ClassifyFailure` keeps `FailureCode` null when `FailureReason` is nonempty, so this is not `AuthenticationRequired` and not `StoppedBeforeFirstPrompt`. There is no new `AgentTaskFailureCode`. The sentence names the model, the floor, and the observed version. The direct StartAsync path used by the witness creates no session, Herdr request, or generation record when it throws.

## Blocker

Where: `tests/derived-impact-index.json`, absent from the three-commit range.

Failure: `scripts/regenerate-derived-impact-index.ps1` printed `derived-impact-index changed`, then `-NoBuild` printed `derived-impact-index unchanged`. The dirty diff is 14 insertions and 9 deletions. Capped `first` for `SessionRunnerRuntime.DecisionPaths.cs` and `SessionRunnerRuntime.TempSweep.cs` replaces `CodexLaunchRefusalTests` with `TranscriptSinceReadTests`. Capped `first` for `SessionRunnerRuntime.cs` gains `CodexLaunchRefusalTests` and drops `GrokRulesRunnerRefusalTests`. The class is added on the Codex CLI version, probe, settings, and launch-problem edges. `windowsOnly` stayed 31 and `linuxOnly` stayed 1.

Why: the new witness links `CodexLaunchRefusalTests` to the production probe and the catalog floor. The committed index was not regenerated, so impact selection at this SHA does not match the generator. Those capped lists are what a later change uses to choose classes.

Fix: regenerate on this tip and commit `tests/derived-impact-index.json`. Do not hand-edit it. This review restored the file and did not commit it. Land-graph `-Check` was refused while the file was dirty (`source differs from HEAD`) and passed after the restore.

## Witness and mutants

`Verified_old_cli_refuses_gpt_6_2_before_launch` calls production `SessionRunnerRuntime.StartAsync` with the production probe. The fixture copies a native executable and the `version-old` seam prints `codex-cli 0.156.1`. The test requires code `codex_cli_model_unsupported`, message containing `0.169.1`, an empty session list, no Herdr request, and exactly one process start (the probe).

Scratch mutants, each its own build-slot lease, method filter `/*/*/CodexLaunchRefusalTests/Verified_old_cli_refuses_gpt_6_2_before_launch`, `--timeout 570s`, `UseAppHost=false`. Both restored. `git diff` on `CodexCliVersion.cs` was empty afterward.

- Floor `0.171.0` (load 9.41, lease `e41fa284-c61d-415a-9247-65c82924b8ee`): 1 failed, 0 passed. The launch still threw, and the message was `requires Codex CLI 0.171.0 or newer`, so `ShouldContain("0.169.1")` failed. Exit 2.
- Floor `0.150.0` (load 8.14, lease `3658fa4c-c6ed-47dd-92f4-a17dcf9baee7`): 1 failed, 0 passed. `StartAsync` did not throw `CodexLaunchException`, because 0.156.1 is not below 0.150.0. Exit 2.

## Checkpoint

One serial checkpoint-tool run at the subject SHA. Plan `/tmp/old-cli-review-r1-plan.md` (not committed). `TEMP=/tmp`, `TMPDIR=/tmp`, `ANTIPHON_LINUX_COMPILER_POLICY=0`. 1-minute load at start was 13.38. The tool took its own slot. Cold outputs `bin-r1-runner/` and `bin-r1-tests/`, deleted after the run. No `--max-wait` cap. Receipt `.antiphon/checkpoints/20261121-211519-05ed`.

```
CHECKPOINT CP-1 commit=bfac226447480de49587f7eeac54cef003aa65f5 build=ok filter=/*/*/CodexLaunchRefusalTests/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/20261121-211519-05ed/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=bfac226447480de49587f7eeac54cef003aa65f5 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=bfac226447480de49587f7eeac54cef003aa65f5 build=ok filter=/*/*/CodexCliObservationTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/20261121-211519-05ed/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=bfac226447480de49587f7eeac54cef003aa65f5 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=bfac226447480de49587f7eeac54cef003aa65f5 build=reused filter=/*/*/DelegateBranchDeliveryTests/(C1213_OldRunnerReceivesLegacy*)|(C1213_QueuedBranchesReachRecipient*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/20261121-211519-05ed/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=bfac226447480de49587f7eeac54cef003aa65f5 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=bfac226447480de49587f7eeac54cef003aa65f5 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/* executed=83 passed=83 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/20261121-211519-05ed/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=bfac226447480de49587f7eeac54cef003aa65f5 sourceState=clean buildSource=verified
```

CP-2 wall 326.738s (tests 304s). CP-3 wall 71.691s. CP-4 wall 104.234s. Slow classes inside green rows: `CodexCliObservationTests` 304s (8 tests), `TestClassificationPolicyTests` 81s (45 tests). Run `20261121-211519-05ed`, source clean, buildSource verified, unlisted commands none, 2 builds, max-concurrent-builds 1, 4 green, 0 red, verdict GREEN, exit 0. Host Debian GNU/Linux 12, 24 cores. Wall 24m28s. `C1213_QueuedBranchRecovery` was not in the filter.

The eight-class guard union executed 83, above the planned floor of 80. CP-2's derived row timeout was warned at 60 minutes and was not capped. The row finished in about five and a half minutes.

## Other checks

`scripts/antiphon evidence-diff --base 50fca3a071851dcc8d4545fa6597e41f99e7b0f2 --head bfac226447480de49587f7eeac54cef003aa65f5`: commits=3, entries=0, violations=0, exit 0. Lease `e464da0a-9ffa-4870-b05c-d1b46c5ce9c9`.

Downgrade selector over added C# lines (`MigrateAsync`, `IMigrator`, `MigrationTestDatabase`, `.Down(`): 0. No migration path in the range.

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` at the subject tip. `origin/master` is `faf7ff1b1206710886263dd3f32fdf9cd23d71a9`. Wrote tree `62e743f31bd0f36e8a493fe4c3bee1692a45fcc0`, exit 0. No merge was made.

Pins, lease `888a2d55-2d25-4231-a108-e7dc9b27874a`: `NightlyPolicy.Read` policyHash `a4b5c2852d4a5c4504c9ddc10f4041ab332b5fdbd65b7a2c313fa6d77d726083`. `scripts/test-live-lane-run.ps1 -PrintInputDigest` equals the sealed `PinnedInputDigest` `aff88704a0be7c2b989b6149b08976853da2fdf094491fd5baef6a57deae858d`. The range does not change nightly inputs.

`scripts/audit-land-verification-graph.ps1 -Check` on the restored clean tree: rebuilt the missing audit tool, digest `A9BB27C595EA171463F0ED13725E198AA64385B1744B9E0BA2D211829C65A7CF`, count 80, `approved`, exit 0. Lease `25535d34-34ae-4401-83cc-86d0d3772679`. The outside-solution and not-built lines in that output are the audit's own report; the phase ended `approved`.

## Advisories

These do not hold the land once the index is committed.

`docs/ops-http.md` still says no observation refuses create, retry, dispatch, warm reuse, or launch. `docs/cli-upgrade.md` step 4 still says `MinimumCodexCliVersion` is not enforced at launch. The model section of `docs/agent-kinds.md`, the ladder comment, and the cli-upgrade troubleshooting row do describe the cold-launch floor. The older 409 list in `docs/agent-kinds.md` and `docs/ai-agent-tui-configuration.md` does not name `codex_cli_model_unsupported`. `Refusal_maps_to_409_with_its_code` still enumerates only the three older codes; the mapper is generic, and the witness asserts the new code on the exception.

Phone-home checks, then records the generation and the workspace launch, then `StartAsync` checks again. Both calls are synchronous, but a concurrent probe refresh can flip the snapshot between them. A pass-then-refuse would consume the generation and record a launch with no session. Current desktop and server2 observations are 0.170.0, so that flip would not refuse them.

The slug compare is case-sensitive, so a differently cased 6.2 slug fail-opens. The server floor helper is ignore-case.

Windows was not executed. The new method uses `Path.IsPathFullyQualified`, slash normalization, ordinal-ignore-case path compare on Windows, and the existing `Resolve` path. It adds no Linux-only process API. A Linux pass is not a Windows pass.

## Review evidence

```
subjectTaskId: 02bed7b0-7da8-4208-8fc8-aad0d64e151a
reviewedSourceSha: bfac226447480de49587f7eeac54cef003aa65f5
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: 50fca3a071851dcc8d4545fa6597e41f99e7b0f2..bfac226447480de49587f7eeac54cef003aa65f5
```
