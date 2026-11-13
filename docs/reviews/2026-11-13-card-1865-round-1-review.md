# CARD-1865 round 1 review: S2 boundary identity hardening

Review task `20d47839`. Round 1, Final profile, full scope. Subject:
`origin/feat/card-task-ff9f59c6` at `aa36b65553049476f0dcd60bfc061635edc4f2d1`
(Code `ff9f59c6`, Grok). There are 8 hardening commits over the S2 tip
`9cf86d295`. The reviewer is from a different company than the author.

**Verdict: CLEAN. No blockers.** The advisories below go to cards. They do not
hold the land.

## Landing path

`git merge-tree --write-tree origin/master aa36b6555` (master `ab7af12c6`)
conflicts. The four touched files and `tests/derived-impact-index.json` have
add/add or content conflicts, because master received S2 through cherry-picks
(batch 193). `git cherry origin/master aa36b6555` marks the 5 S2 commits `-`
(already applied) and the 8 hardening commits `+`.

In a scratch worktree at `origin/master`, cherry-picking the 8 `+` commits in
order (`5896159b3 62dac3195 4754b69d8 efb97fb30 28efd9a99 38f4a679e 1c8974df3
aa36b6555`) applies with 0 conflicts. After the picks,
`scripts/lib/rc-boundary.ps1`, `scripts/test-rc-boundary.ps1`,
`RcBoundaryChunkIdTests.cs` and the S2 verification plan are byte-identical to
the branch tip. `git diff origin/master HEAD` shows only those 4 files
(+272/-41). The hardening does not touch `tests/derived-impact-index.json`;
master's copy stays. **The Merge must pick only the 8 `+` commits.**

## Verification at aa36b6555

Checkpoint run `20261113-053226-7601`, the plan's closed table, all rows
`sourceState=clean buildSource=verified`:

| Row | Executed | Passed | Failed |
|---|---|---|---|
| CP-2 | 7 | 7 | 0 |
| CP-11 | 162 | 161 | 1 |
| CP-12 | 2 | 2 | 0 |
| CP-13 | 2 | 2 | 0 |

The one CP-11 failure is
`DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged`:
"The application to execute does not exist ...
`scripts/regenerate-derived-impact-index/bin/Debug/net9.0/Antiphon.DerivedImpactIndex.dll`".
That driver is outside `Antiphon.sln`, and a fresh mirror has not built it. This
is an environment precondition, already recorded in the plan's repair history.
It is not caused by this change. The driver was then built under a slot, and the
method was rerun in run `20261113-060528-6094` (see Receipts).

`scripts/check-evidence-diff.ps1` returned 0 violations over both ranges:
`9cf86d295..aa36b6555` (8 commits) and `cc48c7005..aa36b6555` (13 commits).

Red-first: each witness commit was run through the harness at its own SHA, and
each was red with the expected message:

- `5896159b3` CanonicalEncoding: json encoding survived
- `4754b69d8` CaseKey and TwoBoundaries: distinct case keys refused, conflicting rules assigned
- `28efd9a99` and `38f4a679e` ShortIdCollision: full digest dropped, resolver absent

## Receipts

Run `20261113-060652-474d` reran the CP-11 red method (CP-11b) after building
the driver: 1 executed, 1 passed, `sourceState=clean buildSource=verified`.
Validator: `CHECKPOINT SOURCE VALID source=aa36b65553049476f0dcd60bfc061635edc4f2d1 rows=1`.
Run `20261113-053226-7601` has verdict RED because of the environment red, so
`validate --rows CP-2,CP-12,CP-13` refuses `report_failed`. The CHECKPOINT lines
for those rows are fresh, at the reviewed SHA, and clean/verified, but no single
validated receipt covers the whole selection. reviewedSourceClean is therefore
false. A full green rerun after the driver prebuild would give that receipt
(about 20 minutes on loaded server2).

The mutant and red-first harness runs were `pwsh` only, with no dotnet. They ran
unleased after one leased tip control, and they are diagnostic.

## Hard checks

1. **Injectivity.** The canonical bytes are the length-prefixed (big-endian
   uint32) UTF-8 encodings of suite and boundary, then a uint32 count, then
   class, method and caseKey for each identity, in ordinal (class, method,
   caseKey) order. A key splits at its first `/`, and class cannot contain `/`,
   so the split is unique. Probes did not alias in any of these cases:
   delimiter shifts between class and method, between method and caseKey, NUL
   inside a name, splicing two identities into one, NFC versus NFD, 100k-char
   names, `< > & '`, and empty caseKey. The encoding is injective over
   well-formed Unicode. Exception: see AD-1.
2. **Collisions fail closed.** I recomputed independently in Node: the fixture
   case keys `10788987499786589518` and `17490688019260441962` both give
   `b-core-360a4ef28b10a96c`, with different full digests. The resolver rebuilds
   both chunks as `unassigned`, `collision=true`, `alwaysDirty=true`, with
   distinct ids. No input path gave one clean id for two different inputs.
   Already-unassigned colliders keep a shared id but stay dirty (AD-2).
3. **Ids changed against S2.** The old pin `b-core-793b6fdbcbb93ce8` appears
   nowhere on master plus the picks. No script dot-sources `rc-boundary.ps1`,
   and nothing calls `Get-RcBoundaryChunkId`, `New-RcBoundaryChunk`,
   `Resolve-RcBoundaryChunks` or `Get-RcClassBoundary`. The `rc-ledger.ps1`
   `chunkId` fields are input-driven. No ids are persisted, and the library is
   not wired into launch.
4. **Determinism.** The id inputs contain no paths. Comparisons are ordinal,
   `GetBytes` emits no BOM, and endianness is explicit. The pins
   `b-core-eca0b76b8b1ed195`, `b-core-f2e0eae2ff0cf3c2` and the collision prefix
   were recomputed byte-for-byte by a separate Node implementation. The Windows
   PowerShell 5.1 run remains owed (AD-5).
5. **Mutants.** Ten mutants were run in scratch copies through all 7 harness
   cases, and every one was killed. The tip control was 7/7 green.

| Mutant | Killed by |
|---|---|
| full digest truncated to 16 hex | ShortIdCollision |
| collision check skipped | ShortIdCollision |
| ConvertTo-Json encoder restored | StableId, CanonicalEncoding, ShortIdCollision |
| caseKey dropped from digest | StableId, CanonicalEncoding, ShortIdCollision |
| caseKey dropped from membership key | CaseKey |
| first-rule-wins restored | TwoBoundaries |
| collision rebuild keeps boundary | ShortIdCollision |
| length prefix dropped | three pinned cases |
| identity count dropped | three pinned cases |
| collision grouped by full digest | ShortIdCollision |

6. **Inert.** Only the 4 files changed. There is no launch, packing, nightly or
   `layers.txt` edit, and the `-SelectionMode Full` source is unchanged. The
   guard union is in CP-11.

## Advisories (cards, not land holds)

- **AD-1:** `[Text.Encoding]::UTF8` replaces lone surrogates with U+FFFD. So
  `A\uD800/M`, `A\uDC00/M` and `A�/M` share a digest. To fail closed on
  ill-formed text, use `New-Object Text.UTF8Encoding($false, $true)`. Real C#
  identifiers cannot contain lone surrogates. A future caseKey producer could.
- **AD-2:** a short-id collision between two chunks that are already
  `unassigned` rebuilds to the same `b-unassigned-…` id for different
  memberships. The chunks stay dirty and `collision=true`, and the rebuilt set
  is not re-checked. The resolver also accepts the same identity in two
  memberships and returns both chunks as clean. D-2 says duplicate cases must
  not disappear through dictionary overwrite. S4a must key collision rows by
  `fullDigest` and refuse cross-chunk duplicate identities.
- **AD-3:** in `Get-RcBoundaryChunkId -CaseKeys @('')` with more than one
  identity key, the count check is skipped. A one-element array holding an empty
  string is falsy in PowerShell. Use `$PSBoundParameters.ContainsKey('CaseKeys')`.
- **AD-4 (performance):** `Sort-RcBoundaryIdentities` is a PowerShell insertion
  sort, O(n²) in function calls. At load 25-33, `New-RcBoundaryChunk` took
  0.6 s / 2.3 s for 200 identities (random / reverse order) and 6.1 s / 12.0 s
  for 800. S2 used `[Array]::Sort`. Each chunk also computes the digest twice.
  This must be fixed before S4a wires packing, per the performance standards.
- **AD-5:** the Windows PowerShell 5.1 run of the reserved-character witness and
  the whole library is still owed. It includes parsing the `@{ class = … }`
  keyword key under 5.1.
- **AD-6:** the plan's CP-11 includes
  `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged`,
  which needs the out-of-sln driver built first. The table should state that
  prebuild as a row precondition.

Still owed from the S2 Review: the Windows native startup probe and rehearsal
before an rc pick. PC-V-5..V-8 and the new PCs stay pending; the operator waived
mutation on 2026-11-12.
