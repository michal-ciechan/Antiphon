# CARD-1878 CARD-1871 advisory repairs

Code task `d7c12587`. Base `321f98ec990a575f77e442ba3b1554f0fde33521`
(current origin/master at dispatch). Parent CARD-1871 is Done. This card
implements the landed advisories only. AD-5, the native Windows PowerShell 5.1
run, stays owed and is not a passed row. No selector or impacted-class edit.
Mutation checks stay waived by the operator (2026-11-12) and are not claimed
passed. Positive controls stay pending for SourceLanding Mutation.

## Decisions

- A-1: a lone surrogate or other encoding failure aborts the call with a
  `boundary-chunk:` refusal. It does not become an unassigned dirty chunk.
  S4a and S5b consume that refusal; they do not receive a partial chunk.
- A-3: ordinary ids stay `b-<boundary>-` plus 16 hex. Collision ids stay
  `b-unassigned-` plus 64 hex (77 characters). One parser accepts both and
  rejects every other length.

## Verification design

- V-1878-1 / AR2-1: a scriptblock comparer passed to the static Sort fails the
  structural witness. The real sort still passes `[StringComparer]::Ordinal`.
- V-1878-2 / AR2-2: a quadratic `for` plus a decoy static Sort fails the
  witness, and so does a renamed comparator. The real sort has one key-building
  `for` and no comparator call.
- V-1878-3 / AR2-3: the witness no longer arms a comparator counter only when
  `Compare-RcBoundaryIdentity` exists. That function is absent, so the old
  counter was dead.
- V-1878-4 / A-1: both public construction paths refuse a lone surrogate with a
  message that starts with `boundary-chunk:`. The call still throws.
- V-1878-5 / A-2: `-CaseKeys $null` with one identity throws
  `boundary-chunk: case key count` before any null index.
- V-1878-6 / A-3: the parser accepts a real 77-character collision id, a
  16-hex core id, a 16-hex unassigned id and a hyphenated boundary id. It
  rejects a 64-hex core id, a mid-length unassigned id and an unknown boundary.
  S4a `Complete-NightlyBoundaryLaunchGroups` uses that parser.
- R-1878-1: the touched harness cases and `RcBoundaryChunkIdTests`.
- R-1878-2: the brief's guard union, evidence-diff and merge-tree.
- The 5 second sort bound and the harness budgets stay as they are.

Each witness is false on the base library or the base harness check. A note
that is already true there is not the witness.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | advisories | n/a | sort-shape | `pwsh -NoProfile -File scripts/test-rc-boundary.ps1 -Case OrdinalSortAndSingleDigest` | V-1878-1 V-1878-2 V-1878-3 | one contract, exit 0 after repair | n/a | 1 |
| CP-2 | advisories | n/a | surrogates | `pwsh -NoProfile -File scripts/test-rc-boundary.ps1 -Case LoneSurrogatesFailClosed` | V-1878-4 | one contract, exit 0 after repair | n/a | 1 |
| CP-3 | advisories | n/a | case-count | `pwsh -NoProfile -File scripts/test-rc-boundary.ps1 -Case ExplicitCaseKeyCount` | V-1878-5 | one contract, exit 0 after repair | n/a | 1 |
| CP-4 | advisories | n/a | collision-id | `pwsh -NoProfile -File scripts/test-rc-boundary.ps1 -Case DuplicateMembershipsAndUnassignedCollisions` | V-1878-6 | one contract, exit 0 after repair | n/a | 1 |
| CP-5 | advisories | n/a | s4a-collision | `pwsh -NoProfile -File scripts/test-rc-boundary-pack.ps1 -Case ShortIdCollisionUsesS2Policy` | V-1878-6 | one contract, exit 0 after repair | n/a | 3 |
| CP-6 | advisories | `tests/Antiphon.Tests` default output | boundary-build | `dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj -p:UseAppHost=false` | R-1878-1 | build exit 0 | n/a | 8 |
| CP-7 | advisories | CP-6 | boundary-class | `/*/*/RcBoundaryChunkIdTests/*` | R-1878-1 | every method, 0 failed | 11 | 4 |
| CP-8 | advisories | CP-6 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | R-1878-2 | each class nonzero, 0 failed | 80 | 15 |

Rows run through `scripts/build-slot.ps1` into the worktree default output.
The checkpoint tool's `bin-*/` output is not used: this brief forbids a custom
build output while the slot pool is under repair. AD-5 is not a row. No row is
expected over 30 minutes. No timeout is widened. `--timeout 570s` is passed to
every `dotnet run`. Restart target: none.
