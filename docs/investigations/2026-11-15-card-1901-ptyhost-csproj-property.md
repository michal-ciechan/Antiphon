# CARD-1901: PtyHost project property refusal

## Red witness

Task branch `feat/card-task-4c25994e` starts at the required chain tip
`7ac976b596b4c8251bf352f291f0f040a6c9547e`. No master merge or rebase was performed.
The current master was fetched for comparison, not incorporated into this chain.
Initial `/proc/loadavg`: `1.70 2.92 5.11 2/3063 20050`.

At that unmodified tip this exact command exited **1**:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1901-land-graph-red -- pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check
```

The refusal was:

```text
definition file sets a property the audit does not allow; -Update cannot approve it: tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj _PtyHostNativeArtifactsArgs
audit refused: land graph pre-check refused
```

The leased driver took 13 seconds, including an 11-second automatic evaluator
build. Raw evidence is ignored at `.antiphon/debug-4c25994e/land-red.log`.

## Cause and intended invariant

Commit `43f1aac1585bbaf8f6ca67670a7555e945342b7e` added
`_PtyHostNativeArtifactsArgs` to the Linux-only `CopyOwnedPtyHostAppHost` target.
It is empty for native-default builds. With `UseArtifactsOutput=true`, it appends
`-p:UseArtifactsOutput=true` and a quoted `ArtifactsPath` under
`$(_PtyHostNativeDir)artifacts/`, confining the child build's intermediates as well
as its output to the checkpoint. The child process avoids stale RID-less assets
cached by the outer MSBuild node.

`LandGraphPrecheck.AllowedProperties` admits `_PtyHostNativeDir`, but not the new
argument property. `AssertAllowedProperties` examines every PropertyGroup,
including groups inside targets. Item types and metadata are separately closed
allowlists. Task parameters themselves are not evaluated by that precheck;
target bodies have a separate exact-pin boundary during real evaluation.

The repair must preserve the child command, argument tokens, branch conditions,
copy operations, chmod, and Windows target exclusion, while defining no new
property or item name. Conditional Exec elements can express the two existing
argument variants directly.

## Merge handoff: duplicate roster pair

The combined tree with master needs the duplicate pair removed from
`WindowsUnitSkipRoster`: master `50d2dfdd1` and this chain's roster repair both
add the same two `VolumeRetirementContentTests Selector_fixture_*` entries.
Keep master's pair. Otherwise `C1210_Roster_closure_matches_source_gates` fails
with `c1210-duplicate-roster` (239 entries, 237 distinct). This task does not
alter that combined-tree roster.

## Rework and equivalence

Repair commit: `06cdf6876758eb3746a82b354d42dc75894f7b2d`.
The argument property is removed. Two mutually exclusive Exec elements carry the
original command: one for `UseArtifactsOutput != true`, and one for
`UseArtifactsOutput == true` with the two checkpoint arguments inlined. The
existing `_PtyHostNativeDir` is the only target-local property. The Linux target
condition, AfterTargets, absent Inputs/Outputs, both Copy operations and chmod
are unchanged. Windows still excludes the entire target.

A separately leased XML comparison against the required parent verified all five
values: empty, false, true, uppercase TRUE, and another value. Expanded commands
match after trimming the old empty argument's trailing whitespace. It also checks
the unchanged target attributes, copies, chmod and absence of new property names.
This is static equivalence evidence, not a native Windows execution receipt.

## Verification on the committed repair

All builds and runs were serial foreground local children through
`scripts/build-slot.ps1`, with granted leases and zero slot wait. Direct builds
used native default output with `UseAppHost=false`; every no-build run used the
same property. The longest leased row was 262 seconds. Longer drivers had a
570-second outer cap with bounded termination; none reached it. No timeout,
assertion or retry was loosened. Mutation checks remain operator-waived.

| Row | Result | Leased wall seconds |
|---|---|---:|
| Build `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj` first | 0 warnings, 0 errors | 5 |
| Derived-index driver, first regeneration | unchanged, exit 0 | 28 |
| Derived-index driver, second regeneration | unchanged, exit 0 | 28 |
| Static command equivalence | 5 variants match, exit 0 | 1 |
| Build `tests/Antiphon.PtyHost.Tests` on Linux | 3 warnings, 0 errors | 16 |
| `PtyHostPipeNameTests/*` | 1 passed, 0 failed, 0 skipped | 3 |
| Build `tests/Antiphon.Tests` | 2106 warnings, 0 errors | 175 |
| Required eleven-class guard union | 113 passed, 0 failed, 1 skipped | 219 |
| `ImpactedClassesSelectorTests*/*` | 42 passed, 0 failed, 0 skipped | 262 |
| Release-graph driver, exactly one invocation | exit 0 | 30 |

The test total is **157 results: 156 passed, zero failed, one skipped**. Fresh TRX
files confirm all eleven guard classes ran, including 27 DerivedImpactIndexTests
and seven WindowsUnitPlatformAdmissionTests results (six passed, one skipped).
The skip is `C1210_Windows_linux_only_outcomes_are_exact`, which requires Windows.
Layering retained `VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3`.

The PtyHost build executed the native child build successfully, copied both the
apphost and `libporta_pty.so`, and made the apphost executable. SHA-256 comparison
confirmed that each destination matches its child-output source. The true
checkpoint-argument branch is covered by the static command comparison; this
task's requested native-default build executes the other branch.

The release invocation was:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1901-release-graph -- dotnet run --project scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj --no-build -p:UseAppHost=false -- --release-graph /work/worktrees/task-4c25994e 06cdf6876758eb3746a82b354d42dc75894f7b2d /work/worktrees/task-4c25994e/.antiphon/selector-replay/card1901-4c25994e.json
```

It reported `incomplete/Full classes=2115 paths=4458` for that exact source SHA.
This is the driver's conservative Full artifact classification, not graph-audit
approval. The generated JSON was deleted using its literal absolute path and its
absence checked. No derived-index bytes changed, so no index commit was needed.

Raw logs, the comparison script, and fresh TRX are ignored under
`/work/worktrees/task-4c25994e/.antiphon/debug-4c25994e/`. Timing observations are
contended-host samples, not p95. A later load sample reached
`21.87 12.70 8.57 4/3335 14745`.

## Required graph gate remains incomplete

On repair commit `06cdf6876758eb3746a82b354d42dc75894f7b2d`, the same leased
audit command (label `card1901-land-graph-fixed`) exited **1** in one second:

```text
digest=7B94E1CAD5814AE261E36ED615977641EF728FE1A2B2ACF42DB85F42A88C0220 count=80
manifest does not match the selected graph
audit refused: land graph pre-check refused
```

The original property refusal is gone: the audit reaches fingerprint comparison.
The committed manifest still records
`0091BC0F4C3F0814B5349FB50429E3B725FFCBB8942A2ADC73F099B5CCD724F8`.
`LandGraphPrecheck.Execute` fingerprints the exact tracked definition blobs, so a
changed csproj necessarily changes that digest. Separately, `TargetPins.Check`
binds exact target XML; its existing pin also needs proper reapproval after this
target change. The full check stops at manifest drift before real evaluation.

The brief explicitly forbids both `-Update` and the sanctioned land refresh.
Neither was run, and no manifest, evaluation record, pins or audit rules were
edited. Therefore the requested full green check cannot be delivered within
that constraint. This is an incomplete gate, not a green/landable claim. A caller
decision is needed to authorize a separate proper graph-record refresh now that
the refused property is removed, or to hand the repair to Merge while retaining
that incomplete gate. Standing test-only repair authority was used without
another approval request for the repair itself.

## Other handoff checks

Fetched master `8ca2ca57fca282be239a6d4326499b8106eba514` retains all three
required pins:

- policyHash: `b610138753e25545cda6f208abe19ebd507cdc77afc288c9888b778ad1ff6b9d`
- PinnedInputDigest: `26d90df028d4e2358ed43b02aa7ef4ad688224c5470aba371d2fd566ff4a663e`
- LegacyExclusionDigest: `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`

This repair changes none of their inputs, so no re-pin was made.
`git diff --check` and evidence-history checking passed (zero generated evidence
entries, zero violations). The required-parent ancestry check passed.

The read-only `git merge-tree --write-tree origin/master HEAD` exited 1, retaining
the chain's four existing conflicts in `scripts/test-live-lane-run.ps1`,
`tests/Shared/TestTiming.cs`, `tests/derived-impact-index.json`, and
`tests/test-execution-policy.json`. No merge or conflict resolution was performed.
Merge must also keep master's roster pair as described above, and retain the
chain's prior Remote/native-Windows and publication-corpus verification debt
recorded in `2026-11-15-card-1901-fixture-leak-fix.md`. This task does not discharge
that older debt. Next stage: **merge**, with the graph gate explicitly incomplete.
