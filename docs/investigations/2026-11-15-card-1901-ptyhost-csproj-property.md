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

## Verification

Repair and verification results will be appended after execution. No land-graph
refresh or `-Update` invocation is authorized by this brief.
