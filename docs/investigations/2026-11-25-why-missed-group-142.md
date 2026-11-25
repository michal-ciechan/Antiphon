# Why Windows Full reached the group 142 failures

The watcher reported one failure each in `StatusSnapshotScriptTests` and
`IncrementalSelectionSoundnessTests` in Windows Full run `b8cbfb24` at
`2fd61b643ea4231fa732cd4969b5284d43b33f39`. Linux reproduced a status snapshot fixture
regression. Both incremental selection methods passed on Linux, so the Windows
failure in that class remains unexplained.

## Introducing commit or card

The status snapshot fixture came from the CARD-1500 status snapshot and its C# port
in `9cabf2f08c51c16aa83876138c305ffa42a98136` (unit 7). The regression arrived in
`0e3507b90eea557c93debab79aeddc81e0415af2` (unit 27), which moved the card test
fixture's HTTP stub from an ephemeral listener to an in-process delegate.
`StatusSnapshotScriptTests.Run` kept constructing a production HTTP transport.
Its fixture inherited `http://127.0.0.1:17202` as the address and issued real GETs
there. The listener was gone, so whether the test saw a connection failure or an
unrelated HTTP response depended on the host. The test supplies its own scratch
repository, dispatch schedule, active chunk, last-run record and heartbeat.

The incremental selection fixture is CARD-1820 S2, introduced in
`d63fce0464007c06a358f06957ce2632101566b8` and corrected in
`61eec38f922d86d59c59e75f69d7a9fd44c9f493`. Its committed owner-inventory replay
was ported in `c0c9178c2f311b908c6e6a48df25b5b09f055242`. The two methods, the
historical corpus and the inventory implementation have identical source at the
two rc pins. The inventory itself also reads the committed graph and owner map.
The independent M1 replay enumerates `.csproj` files in the working tree. The
introducing change for the reported Windows failure is unknown.

## Approving reviews

The [port record](../superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md)
lists the snapshot tests with unit 7. Unit 27 lists the migrated card tests,
including `CardListSearchScriptTests` and `CardRelationshipScriptTests`, and
omits the snapshot tests. A separate approving Review receipt with scope and
executed filter for unit 27 could not be verified.

The documented CARD-1820 S3 Final/Full review `3fa5f039` selected the owner-map,
manifest and digest classes, the classification and policy guards, and impact
and graph checks. The separate S2 replay was outside that selection. This older
review explains the previous owner-replay coverage gap. The scope and filter
that approved the as-yet unidentified Windows regression are unknown.
[Prior group 139 investigation](2026-11-21-why-missed-rc-run-3528e6b1-group-139.md)

## Outside the selection

The Integration snapshot class depends on a test fixture in another class.
The Unit replay class reads the repository tree, graph and owner map. The
checked-in short Merge selector omitted both classes when evaluated over
`6ed4cb58cd75..2fd61b643ea4`. It also omitted both over the single unit 27 commit.
The Linux cut's four recorded checkpoint filters omitted both classes. The
snapshot regression therefore reached Windows despite being reproducible on Linux.
The omitted replay is a verified coverage gap, and the evidence needed to
explain its Windows failure is still outstanding.

The neighboring `CardRelationshipScriptTests` uses the same stub through
`CardListSearchScriptTests.RunAsync`, which calls `stub.Transport`. The snapshot
was the only other consumer of this fixture that constructed a network client.
`RegisteredCallerScriptTests.Status_snapshot_sends_its_registered_caller_label`
still exercises the production HTTP transport against its own ephemeral listener.
For the replay, `ImpactedClassesCensusTests.Full_census_matches_the_canonical_regex_within_budget`
also walks the live source tree. That shared exposure does not establish a common
failure cause.

## Cheapest pre-land check

| Changed path | TUnit class filter |
| --- | --- |
| `tests/Antiphon.Tests/Scripts/CardListSearchScriptTests.cs`, `scripts/lib/Antiphon.Nightly/StatusSnapshot.cs`, `scripts/lib/Antiphon.Nightly/RcQualificationRecords.cs` | `/*/Antiphon.Tests.Scripts/StatusSnapshotScriptTests/*` |
| `**/*.csproj`, `scripts/lib/rc-input-owners.json`, `scripts/lib/Antiphon.Nightly/RcInputInventory.cs`, `tests/fixtures/incremental-selection-soundness.json` | `/*/Antiphon.Tests.Scripts/IncrementalSelectionSoundnessTests/*` |

`impacted-classes --path` on the card fixture already identifies the snapshot
class through the derived index. It does not select that class for the compiled
snapshot or record reader. The general path selection picks up the replay for
`tests/Antiphon.Tests/Antiphon.Tests.csproj`, and omits it for the staged impact
driver's project, the historical corpus and the compiled owner inventory.
The table records the proposed dependencies for CARD-1782. The replay's native
failure still needs an exact method and stack before choosing a narrower check.

## Reproduction and remaining Windows evidence

Linux baseline `f98e61aca24b6f0e99baa4c604b1dfa9a39d0310` used a leased native-output
build and a leased TUnit run restricted to the two named classes. Four tests ran,
two passed and two failed, with zero skips. Port 17202 had no listener.

- `Snapshot_reads_scoped_tasks_counts_remote_master_and_rc_without_writes_in_text_and_json`
  expected `text.ExitCode` 0 and got 2, with pipeline, slots and instructions
  unavailable (`HTTP unreachable`). The first test frame was
  `StatusSnapshotScriptTests.cs:57`.
- `Snapshot_reports_partial_facts_as_unverified_and_exits_2` expected a
  `ServiceUnavailable` slots observation, but the HTTP client could not reach
  the fixture transport. The first test frame was `StatusSnapshotScriptTests.cs:105`.
- `Recorded_owner_map_status_matches_the_committed_map` and
  `Project_closure_misses_match_the_recorded_known_unsound_set` both passed.

The status snapshot fixture now supplies its recording transport. The desktop
TRX, tests log and execution records were unavailable from the Linux mirror.
Strict SSH host-key verification could not authenticate the desktop. The previous
run `acca11ba` at `6ed4cb58cd75fe386e94965165d96752452431a8` predates the snapshot
fixture regression. Its method outcomes and the group 142 entry in
`seed-from-acca11ba.json` remain unverified. Match previous TRX rows by method,
since chunk membership can change between pins. The Windows failure records are
needed to match the Linux snapshot signature and classify the incremental failure.

After the live desktop slot is released, the snapshot fixture's native check on
an already-built fixed candidate is:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group-142-snapshot-native -- dotnet run --project tests/Antiphon.Tests --no-build -- --treenode-filter '/*/Antiphon.Tests.Scripts/StatusSnapshotScriptTests/*'
```

## Exemption

None for the snapshot transport regression. The one earlier replay failure had
a confirmed owner-map assertion and a repair already present at this pin. The
current Windows replay does not yet have enough evidence to establish recurrence.
[Prior replay repair](2026-11-21-why-missed-rc-run-3528e6b1-group-139.md)
