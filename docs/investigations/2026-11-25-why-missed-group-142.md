# Why Windows Full reached the group 142 failures

The operator supplied two failed TRX rows from Windows Full run `b8cbfb24` at
`2fd61b643ea4231fa732cd4969b5284d43b33f39`. One identified a status snapshot
fixture regression. The other identified a timeout in the production
owner-glob matcher.

## Introducing commit/card

The CARD-1500 snapshot fixture was ported to C# in
`9cabf2f08c51c16aa83876138c305ffa42a98136` (unit 7). In unit 27,
`0e3507b90eea557c93debab79aeddc81e0415af2` moved the shared card test
stub from an ephemeral HTTP listener to an in-process delegate.
`StatusSnapshotScriptTests.Run` kept using a real HTTP client. The fixture
sent its GETs to `127.0.0.1:17202`, where a service on the Windows host
returned HTTP 404. It supplies its own Git repository, schedule, active
chunk, last-run record and heartbeat.

CARD-1836 S3a introduced the backtracking glob expression and its 100 ms
per-match timeout in `1d240a77fe7023428b05f8f82688bd888701f6fa`. The C# port
`119a8ca598ab17a6cf0239d27e9f45804749fbdc` kept both. The CARD-1820
historical replay moved in process in `c0c9178c2f311b908c6e6a48df25b5b09f055242`.
The failing line applies an owner or exclusion glob to one path from
`git ls-tree -z`. Those path strings come from the committed tree,
and the tree parser rejects control characters. Working-tree CRLF and
historical file contents are outside this match.

The expression was already anchored. Each `**/` became a greedy optional
directory prefix. When several prefixes can consume the same directory
names, a mismatch near the end can make the backtracking engine try many
assignments. The TRX omits the exact pattern and path, so the Windows
record cannot establish how much of the 100 ms was spent on backtracking
versus elapsed scheduling delay. Linux reproduced the exception with a
192-character path of repeated directory names and a mismatching suffix.
The repair evaluates the same whole-path glob grammar with matching work
bounded by pattern and path lengths. It retains the 2,048-character
pattern limit, optional directory prefixes and invariant case matching.

## Approving reviews

The [port record](../superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md)
lists the snapshot tests with unit 7. Unit 27 lists the migrated card tests,
including `CardListSearchScriptTests` and `CardRelationshipScriptTests`.
A separate approving Review receipt with scope and executed filter for unit
27 could not be verified.

CARD-1836 S3a Review `ebfb9548` approved its round-2 diff after three
validated checkpoints. Linux CP-2 ran `/*/*/RcInputManifestTests/*` with
four passes. The CARD-1820 S3 Final/Full review `3fa5f039` selected the
owner-map, manifest and digest classes, classification and policy guards,
and impact and graph checks. That selection omitted the separate historical
replay. Neither review records a long-path owner-glob witness.
[S3a review evidence](2026-11-21-why-missed-rc-run-3528e6b1-group-138.md),
[CARD-1820 review evidence](2026-11-21-why-missed-rc-run-3528e6b1-group-139.md)

## Outside the selection

The Integration snapshot class depends on the stub defined in another test
class. The Unit replay class reads the real committed tree, graph and
owner map. The short Merge selector omitted both classes over
`6ed4cb58cd75..2fd61b643ea4` and over the unit 27 change. The Linux cut's
four recorded checkpoint filters omitted both classes. On the initial
Linux baseline, both historical selection methods passed.

The neighboring `CardRelationshipScriptTests` already uses the stub's
delegate. `RegisteredCallerScriptTests.Status_snapshot_sends_its_registered_caller_label`
still exercises the real HTTP transport through its own ephemeral listener.
The same owner-glob factory handles the no-op map, project item includes,
the literal owner census and test-only classification. The matcher repair
covers those consumers.

## Cheapest pre-land check

| Changed path | TUnit filter |
| --- | --- |
| `tests/Antiphon.Tests/Scripts/CardListSearchScriptTests.cs`, `scripts/lib/Antiphon.Nightly/StatusSnapshot.cs`, `scripts/lib/Antiphon.Nightly/RcQualificationRecords.cs` | `/*/Antiphon.Tests.Scripts/StatusSnapshotScriptTests/*` |
| `**/*.csproj`, `scripts/lib/rc-input-owners.json`, `scripts/lib/Antiphon.Nightly/RcInputInventory.cs`, `tests/fixtures/incremental-selection-soundness.json` | `/*/Antiphon.Tests.Scripts/IncrementalSelectionSoundnessTests/(Owner_globs_match_paths_with_repeated_directory_names)\|(Recorded_owner_map_status_matches_the_committed_map)` |

`impacted-classes --path` identifies the snapshot class from a change to
the card fixture through the derived index. It omits it for the compiled
snapshot and record reader. The general path selection picks up the replay
from `Antiphon.Tests.csproj` and omits it for the staged impact driver's
project, the corpus and the compiled owner inventory. These are proposed
dependency entries for CARD-1782.

## Windows evidence and Linux witnesses

- `Snapshot_reads_scoped_tasks_counts_remote_master_and_rc_without_writes_in_text_and_json`
  failed in 1.711 s on Windows at `StatusSnapshotScriptTests.cs:57`.
  It expected exit 0 and got 2 after three HTTP 404 responses. On Linux
  baseline `f98e61aca24b6f0e99baa4c604b1dfa9a39d0310`, the same assertion
  failed with HTTP unreachable. The partial-facts method also failed
  because it expected HTTP 503. Both passed at
  `6e8dd66733a003674eeb473abc1f5618782d315e` on the clean Linux checkpoint.
- `Recorded_owner_map_status_matches_the_committed_map` failed in 2.463 s
  on Windows with `RegexMatchTimeoutException` at `RcInputInventory.cs:519`,
  through `RcInputOwnersMapTests.HistoricalSelectionSoundness` line 423 and
  the test at line 44. The committed Linux witness at
  `8aaed76d0e62da30629494c65ba4dfd5b9521ccb` exercises the original 100 ms
  matcher. It requires a correct negative result, a matching control and
  the existing case, wildcard and maximum-pattern behavior.

The operator's comparison found neither failed method in the corresponding
`acca11ba` group. Both methods exist in source at that pin
(`6ed4cb58cd75fe386e94965165d96752452431a8`). The failed-first seed
`seed-from-acca11ba.json` mentions group 142. An outcome in another
`acca11ba` chunk remains unverified.

After the live desktop slot is released, run the native checks on an
already-built fixed candidate.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group-142-snapshot-native -- dotnet run --project tests/Antiphon.Tests --no-build -- --treenode-filter '/*/Antiphon.Tests.Scripts/StatusSnapshotScriptTests/*'
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group-142-owner-native -- dotnet run --project tests/Antiphon.Tests --no-build -- --treenode-filter '/*/Antiphon.Tests.Scripts/IncrementalSelectionSoundnessTests/(Owner_globs_match_paths_with_repeated_directory_names)|(Recorded_owner_map_status_matches_the_committed_map)'
```

## Exemption

None. The earlier replay failure was an ownership comparison against the wrong
inventory. This failure stopped during path matching.
[Prior replay repair](2026-11-21-why-missed-rc-run-3528e6b1-group-139.md)
