# CARD-1618 S-B1 Linux lane

S-B1 is done on Linux. The hostile-autocrlf regression went red at the immutable commit, then the same assertion went green after fixture isolation. Native Windows Verify is still owed and must not move this branch.

## Slice

Shared fixture Git isolation for shape (S), tracked baseline (T), and output confinement (O). Direct git, seed `dotnet`, fixture-built pwsh, and the real checkpoint driver receive an owned empty global and system config, `GIT_CONFIG_NOSYSTEM=1`, `GIT_NO_REPLACE_OBJECTS=1`, `GIT_ATTR_NOSYSTEM=1`, and a run-owned NuGet restore file. Parent environment is not changed. Production `HardenGit` is unchanged. `GIT_ATTR_NOSYSTEM` is a fixture restriction.

`git init` pins local `core.autocrlf=false`. CP-51 still uses the unisolated `CheckpointFixtureProcess.Git` overload and must observe the dirty ` M payload.txt` differential. Public `WaitAsync`, `RunScriptAsync(ProcessStartInfo)`, and `RunConfinedScriptAsync(ProcessStartInfo)` stay thin so CP-39 can pass a caller start info. `BuildUseAppHostShim` is unwired. O's five `RequireLinux` gates stay for S-B2. Shape's ten gates stay.

Census: the eight T names left `Get-NamespaceCensus` `windowsSkips` and `$expectedWindows` in `scripts/test-rc-platform.ps1`. Twenty-one Windows entries remain (six unrelated, ten S, five O). The 35-identity `$expected` roster, `$gaps`, `linuxSkips`, and `ReleaseGatePlatformTests` still name those eight methods. No definition file changed. `tests/test-execution-policy.json` did not change. Land graph pins did not change.

## Commits

Parent S-A report: `823c5526425d17efe0258e3e96afe40ed9c15e7d`.

| SHA | Role |
|---|---|
| `9fbcb239237517b15ae442a4fb6124942b1014ec` | Immutable red. Do not amend. |
| `ad97bd3cbc0321ae89b54a32b565e5a6c616cf89` | Isolation helper, S/T/O wiring, census. |
| `19d031a3aca96f581f8fd8530c638cecb61bc7da` | CP-38 compares driver stdout without the line ending `ProcessDriver.Capture` appends. |

Windows green receipts must use `19d031a3aca96f581f8fd8530c638cecb61bc7da`, not a later report-only commit. Do not rewind `feat/card-task-b47c4dfa`.

## Linux checkpoints

CP-37 run `20261030-083535-e430` at `9fbcb239237517b15ae442a4fb6124942b1014ec`. Source clean, buildSource=verified. Verdict RED exit=1.

`CHECKPOINT CP-37 commit=9fbcb239237517b15ae442a4fb6124942b1014ec build=ok filter=/*/*/CheckpointFixtureGitEnvironmentTests/fixture_commit_matches_hardened_status_under_hostile_config executed=1 passed=0 failed=1 skipped=0 ... dirty=0 source=9fbcb239237517b15ae442a4fb6124942b1014ec sourceState=clean buildSource=verified`

Named failure additional info: `dirty-baseline payload.txt` then ` M payload.txt`. PHASES build=333.8058773s testsWall=0.2939895s.

Green run `20261030-090144-f07c` at `ad97bd3cbc0321ae89b54a32b565e5a6c616cf89`. Source clean, buildSource=verified. Shared build `bin-c1199b1/` about 325s. Verdict of that invocation was RED exit=1 because CP-38 failed; every other selected row was green.

| CP | executed | passed | failed | skipped | commit |
|---|---:|---:|---:|---:|---|
| CP-13 | 1 | 1 | 0 | 0 | `ad97bd3cbc0321ae89b54a32b565e5a6c616cf89` |
| CP-14 | 1 | 1 | 0 | 0 | `ad97bd3cbc0321ae89b54a32b565e5a6c616cf89` |
| CP-15 | 1 | 1 | 0 | 0 | `ad97bd3cbc0321ae89b54a32b565e5a6c616cf89` |
| CP-16 | 1 | 1 | 0 | 0 | `ad97bd3cbc0321ae89b54a32b565e5a6c616cf89` |
| CP-17 | 1 | 1 | 0 | 0 | `ad97bd3cbc0321ae89b54a32b565e5a6c616cf89` |
| CP-18 | 1 | 1 | 0 | 0 | `ad97bd3cbc0321ae89b54a32b565e5a6c616cf89` |
| CP-19 | 1 | 1 | 0 | 0 | `ad97bd3cbc0321ae89b54a32b565e5a6c616cf89` |
| CP-20 | 1 | 1 | 0 | 0 | `ad97bd3cbc0321ae89b54a32b565e5a6c616cf89` |
| CP-38 | 2 | 1 | 1 | 0 | `ad97bd3cbc0321ae89b54a32b565e5a6c616cf89` |
| CP-39 | 4 | 4 | 0 | 0 | `ad97bd3cbc0321ae89b54a32b565e5a6c616cf89` |
| CP-47 | 6 | 6 | 0 | 0 | `ad97bd3cbc0321ae89b54a32b565e5a6c616cf89` |
| CP-48 | 1 | 1 | 0 | 0 | `ad97bd3cbc0321ae89b54a32b565e5a6c616cf89` |
| CP-50 | 1 | 1 | 0 | 0 | `ad97bd3cbc0321ae89b54a32b565e5a6c616cf89` |
| CP-51 | 1 | 1 | 0 | 0 | `ad97bd3cbc0321ae89b54a32b565e5a6c616cf89` |

CP-38's failing method was `real_driver_keeps_confinement_and_cancellation`. `shown.Stdout` was the owned global config path plus the newline `Capture` appends with `AppendLine`. The path matched. The fix trims only `\r` and `\n` before the ordinal comparison. No other assertion changed.

CP-38 rerun `20261030-091947-3076` at `19d031a3aca96f581f8fd8530c638cecb61bc7da`. Source clean, buildSource=verified. `executed=2 passed=2 failed=0 skipped=0`. Verdict GREEN exit=0. Build 731.5133711s (slot was busy). The code delta after `ad97bd3cbc0321ae89b54a32b565e5a6c616cf89` is that one comparison, so the other rows were not re-executed.

CP-50 is the same regression method as CP-37, now green: payload.txt absent from hardened status. CP-51 passed by observing sentinel `c1199-hostile` and ` M payload.txt`.

## Land check

`scripts/audit-land-verification-graph.ps1 -Check` exit 0. `digest=7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99 count=68`, one `approved` line. `-Update` was not run.

`scripts/check-evidence-diff.ps1 -BaseRef 823c5526425d17efe0258e3e96afe40ed9c15e7d -HeadRef HEAD` exit 2, `EVIDENCE error reason=shallow_or_unknown_history`. This server2 mirror is shallow.

## Owed native Windows Verify

Dispatch `-Platform Windows`. Use a separate checkout. Do not reset `feat/card-task-b47c4dfa`.

Red, at `9fbcb239237517b15ae442a4fb6124942b1014ec` only:

`dotnet tools/Antiphon.Checkpoints/bin-c1199-plan-tool/Antiphon.Checkpoints.dll run .antiphon/c1199-checkpoints.yaml --rows CP-37 --serial`

Expect executed=1, passed=0, failed=1, skipped=0, additional info `dirty-baseline payload.txt` and status ` M payload.txt`.

Green, at `19d031a3aca96f581f8fd8530c638cecb61bc7da` (not this report commit). Windows omits Linux-only CP-39:

`dotnet tools/Antiphon.Checkpoints/bin-c1199-plan-tool/Antiphon.Checkpoints.dll run .antiphon/c1199-checkpoints.yaml --rows CP-13,CP-14,CP-15,CP-16,CP-17,CP-18,CP-19,CP-20,CP-38,CP-47,CP-48,CP-50,CP-51 --serial`

Expect CP-13..CP-20, CP-48, CP-50, CP-51 each 1 passed; CP-38 2 passed; CP-47 6 passed; zero failed and zero skipped. Delete `bin-c1199b1/` by literal path before that run if a prior commit left it (`pre-existing-build-source`).

## Handoff

Linux S-B1 rows are green. Windows CP-37 at the red SHA and the Windows green rows above are owed. S-B2 was not started.
