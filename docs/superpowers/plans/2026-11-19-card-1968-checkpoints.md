# CARD-1968 compiler policy heap cap and CPU spread

Code task `ee721a69`. Base `42d7af1f33cfd0236f3bbcdcea57c60bf948e98d`. The behavior commit is `05eaab8175a7428efd36033031f729931064fbe4`. This file is the closed list and the re-measure note. It does not enable the policy.

`DOTNET_GCHeapHardLimit` is hexadecimal. The old literal `100000000` is 4 GiB (`4294967296`). The stock `Antiphon.Tests` compiler peaked at 5.8 GB RSS, and policy servers died while busy at 4.40–4.60 GB RSS. The emitted value is `0x200000000` (8 GiB, `8589934592`), the card's floor and the CARD-1903 figure. The same literal is in the cache identity and the `LINUX COMPILER` diagnostic, so a 4 GiB launcher is not reused.

`SelectCpus` no longer takes the first N allowed CPUs. The window is at least two CPUs when the allowed set has two or more, and its origin is `(grant * width) mod count`, clamped so the window stays inside the sorted allowed list. On `0-23`, grant 1 is `2,3` and grant 6 is `12,13,14,15,16,17`. A grant that covers the whole set still returns every allowed CPU. Defaults stay off: `docker-compose.server2-runner.yml` uses `:-0`, and `scripts/c590-remote.sh` keeps `RUNNER_LINUX_COMPILER_POLICY_DEFAULT=0`.

## Verification design

V-1: `CompilerEnvironment` emits a value that parses as hex (`Convert.ToUInt64` after an optional `0x`) to at least 6 GiB. At `98368fa8dafaa66c7617884deff365ffe118ff09` it parsed to exactly 4 GiB. After the fix it is `0x200000000`.
V-2: On allowed set `0-23`, grant 1 and grant 6 do not both start at CPU 0, and neither mask is a single CPU. At the red commit both started at 0 (`0` and `0,1,2,3,4,5`). After the fix they are `2,3` and `12,13,14,15,16,17`.
R-1: `LinuxSharedCompilerPolicyTests` all pass, including the previous sparse, overflow, and driver-environment assertions.
R-2: `TestClassificationPolicyTests` once.
R-3: `DerivedImpactIndexTests` alone. The index did not change.
R-4: The rest of the standing guard union, then `Repository_graph_satisfies_the_committed_allowlist` printed for the inventory line.
R-5: `scripts/test-linux-shared-compiler.ps1 -Case Adapter` (fake dotnet, policy on only inside that fixture).
R-6: `scripts/audit-land-verification-graph.ps1 -Check`, nightly pin recompute, `scripts/check-evidence-diff.ps1` over the task base..HEAD, and `git merge-tree --write-tree origin/master HEAD`.

No PC rows. Mutation is waived (operator 2026-11-12). The re-measure below is manual and stays outside this table.

## Re-measure after rollout

Do not run this during the Code task, and do not flip the standing default. After the fix is on a runner image, the orchestrator repeats the matched alternating protocol from `docs/investigations/2026-11-17-compiler-policy-matched-ab.md`:

- Four cold pairs and four warm pairs. Order A,B / B,A / A,B / B,A, where A is `ANTIPHON_LINUX_COMPILER_POLICY=1` and B is `0`, set on that command only.
- Command: `ANTIPHON_LINUX_COMPILER_POLICY=<1|0> pwsh -NoProfile -File scripts/build-slot.ps1 -Label c-policy-<id> -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false` plus `--no-incremental` for cold. No `-m`. Native output. One build at a time. Record load1/5/15 at start and end, slot wait, and host load during the build.
- Warm: touch one `tests/Antiphon.Tests` source file, then an incremental build. Confirm both sides rewrite the same obj assemblies.
- Sample `VBCSCompiler` RSS, last-interval CPU, and `Cpus_allowed_list`. Acceptance is that no policy server disappears while its last interval is still busy, and that grant 1 and grant 6 are not both pinned to the lowest CPUs. Log the server exit reason (`RoslynCommandLineLogFile` on that command only) if a server still exits.
- Leave Compose `:-0`, `RUNNER_LINUX_COMPILER_POLICY_DEFAULT=0`, and the saved stack.env at `0`. CARD-1950 stays blocked until this re-measure is accepted. A per-build server is not part of this pass.

## Performance budget

CP-1's EstimatedMinutes 15 is the cold-build allowance. The class itself is a few seconds once built. Read `/proc/loadavg` and start only below 16. `ANTIPHON_LINUX_COMPILER_POLICY=0` on every driver in this table. The Code pass used native output through `scripts/build-slot.ps1`, one lease per command, `--timeout 570s` on each `dotnet run`. It did not use the checkpoint tool, so there is no checkpoint receipt. A later receipt run uses the `bin-c1968/` output below and removes that directory by that exact path.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---:|---|---|
| CP-1 | fix | tests/Antiphon.Tests -> bin-c1968/ | compiler-policy | `/*/*/LinuxSharedCompilerPolicyTests/*` | V-1, V-2, R-1 | all listed, 0 failed | 10 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | fix | CP-1 | classification | `/*/*/TestClassificationPolicyTests/*` | R-2 | all listed, 0 failed | 42 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | fix | CP-1 | derived-impact | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | R-3 | all listed, 0 failed | 27 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | fix | CP-1 | guards | `/*/*/(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)\|(WindowsUnitPlatformAdmissionInvariantTests*)/*` | R-4 | 45 passed, 1 Linux skip `C1210_Windows_linux_only_outcomes_are_exact`, 0 failed | 45 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | fix | n/a | adapter | `pwsh -NoProfile -File scripts/test-linux-shared-compiler.ps1 -Case Adapter` | R-5 | assertions=36 failed=0; diagnostic heap=0x200000000 | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | fix | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-6 | exit 0, approved | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

### Cost

Ordinary floor is 40 minutes (sum of EstimatedMinutes). The Code pass wall was one cold `tests/Antiphon.Tests` build (292 s, red tree) plus one incremental rebuild (164 s) and the rows above. Pins were recomputed and not rewritten.
