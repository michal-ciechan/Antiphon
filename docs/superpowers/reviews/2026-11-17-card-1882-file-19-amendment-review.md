# CARD-1882 file-19 amendment review, round 1

CLEAN. No blocker. Advisories do not hold the land. Positive controls stay pending. Mutation stays waived (operator 2026-11-12).

Reviewed source `338e00cb1b56b8075b8b6d1cc91e17b3e738a4d6` on `origin/feat/card-task-31f0501d`, plan owner `31f0501d-d521-4eca-8178-f9769cdf44d5` (Claude Opus, commit trailer). The range is `870a93d21f2b18c85a8b0905ab689b28e3099813..338e00cb1b56b8075b8b6d1cc91e17b3e738a4d6` (one commit). `origin/master` is `870a93d21f2b18c85a8b0905ab689b28e3099813`. The diff is only `docs/superpowers/plans/2026-11-17-card-1882-file-19-amendment.md`. Reviewer is Grok. CARD-1882 was read at revision 15.

This round is the commissioned Final scope for the docs amendment: the claims below, a read-only checkpoint import, the current envelope class, the named five-class guard batch, evidence-diff, and merge-tree. The new witnesses are not in the tree, so CP-1..CP-26 were not executed. PCs were not run.

## Master state

The digest description matches `scripts/lib/release-impact.ps1:35-54`. The array at `:40` is only `scripts/impacted-classes.ps1` and `scripts/lib/release-impact.ps1`. `Invoke-ReleaseImpactGit` throws `release-impact Git input could not be verified` at `:23` on any non-zero Git exit. `scripts/impacted-classes.ps1:130-137` catches that and calls `Refuse-ReleaseImpact`. Lines `:98-111` write `ok=false`, `complete=false`, `incompleteReasons=['invalid-release-request']`, `fallback=Full`, an `error` prefixed `release-impact refused: `, and exit 2. That payload has no `algorithmDigest`.

The 18-file roster is planned, not implemented. `docs/superpowers/plans/2026-11-16-card-1882-algorithm-digest-plan.md:171-188` is those 18 paths in the amendment's order. The TestDesign repeats them at `:163-181`. `scripts/lib/release-impact-scripts.ps1` is absent from HEAD (`git cat-file -e` exit 128). No `.cs`, `.ps1`, or `.json` name in the tree is that module. Plan `:15` and TestDesign `:33` require a re-plan before adding it.

`ReleaseImpactEnvelopeTests` has eight `[Test]` methods. `AlgorithmSourceDriftIsRefused` (`:121-156`) has four `[Arguments]`. The other seven methods are single results. The leased run at this SHA executed 11, failed 0, skipped 0.

## Arithmetic

No floor term is wrong. Recomputed from the landed plan, the TestDesign, and this table:

| Term | Value | Source |
|---|---:|---|
| Drift today, planned | 36 | 18 × 2. Plan `:167`, TestDesign `:86` |
| Envelope today, planned | 43 | 36 + 7 retained methods. Plan `:269` |
| Overall today, planned | 180 | 43 + 1 + 78 + 57 + 1. Plan `:271`, TestDesign `:398-403` |
| File-19 drift only | 38 / 45 / 182 | 36+2, 43+2, 180+2. Sequencing review `f76736b8d` |
| Drift after this amendment | 38 | 19 × 2. The absence result is not a drift result |
| Envelope | 46 | 38 + 1 absence + 7. The 7 are CP-16's six methods plus `AlgorithmDigestBindsCommittedBlobs` |
| Overall | 183 | 46 + 1 + 78 + 57 + 1 |
| S1 minimum | 29 | CP-1..CP-14 = 28, CP-15 = 1 |
| S1c minimum | 11 | CP-20..CP-23 = 8, CP-25 = 2, CP-26 = 1 |
| S1b minimum | 143 | 6 + 1 + 78 + 57 + 1, unchanged |
| Union | 183 | 29 + 11 + 143 |

The sequencing review's 38/45/182 become 38/46/183 only because of `AlgorithmSourceAbsenceIsRefused_19_ScriptClosure`. Drift stays 38. Guards stay 27 in the TestDesign (`G-1`..`G-27`) and become 29 with G-28/PC-28 and G-29/PC-29. The PC floor `29 × (7+1+7+1) + 2 = 466` uses the TestDesign's per-control formula (`:541-545`); the `+2` is that file's bootstrap, not a new result. Commissioned Code time `2 + 33 + 5 + 50 = 90` is the old 74 plus 16. The 78 and 57 remain carried floors, not a new census.

Checkpoint cells: CP-1..CP-19 and CP-24 match the TestDesign table byte for byte. CP-20..CP-23 change After and Build only, and CP-20's estimate changes from 1 to 7. CP-25 and CP-26 are new. Group sums are S1 21 minutes (15 rows), S1c 12 (6 rows), S1b 17 (5 rows). Each is at or under 25. Estimates are provisional (n=0), labelled CARD-1882/CARD-1404, with the importer floor `max(15, 3 × estimate)` recorded as the tooling exception rather than a measured 2× p95 (`docs/test-performance-standards.md` R5).

## Fail-closed

Nothing in the amendment makes path 19 optional. Section 3.1 adds one literal and forbids `Test-Path`, an optional entry, and a try/catch around the loop. D-A5 and section 4 stop the Code stage on a failed preflight: file 19 must be tracked at HEAD and present on disk; CARD-1836 S5c, CARD-1820 S1R5, and CARD-1883 S3 must be ancestors; any CR stops the stage; the 18 TestDesign anchors are re-checked and file 19's anchor is bound by the D-A2 rule or the stage blocks. A missing anchor is a re-amendment, not an edit of file 19.

`AlgorithmSourceAbsenceIsRefused_19_ScriptClosure` can go red if a later edit skips a missing path. The recipe deletes the file from the fixture HEAD and from disk, then requires `SelectCopiedAlgorithmAsync` to exit 2. That helper returns the exit code (`ReleaseImpactEnvelopeTests.cs:329`). Before the production edit the array does not contain path 19, so the selector still exits 0 and `Exit.ShouldBe(2)` fails. After the edit, a missing path fails `rev-parse` or `hash-object` and the selector refuses at `:23`. A `Test-Path` continue, or a catch that continues on `release-impact Git input could not be verified`, also leaves exit 0, so the same assertion fails. The 38 drift results copy the file first, so they do not see this absence. PC-29's narrow catch rethrows `algorithm source drift`, which leaves CP-25 green and turns only CP-26 red.

## Import

Read-only import with the already-built checkpoint tool, no new build:

`dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-11-17-card-1882-file-19-amendment.md --out .antiphon/c1882-file19-review-import.yaml`

Exit 0. Stdout was `imported 26 rows` and no warning line. The ignored YAML has 26 checkpoints, 26 filters, and 26 build references. Three builds: `bin-c1882-s1/`, `bin-c1882-s1b/`, `bin-c1882-s1c/`, all `tests/Antiphon.Tests`. After counts are S1 15, S1c 6, S1b 5. Derived timeouts are CP-1 21, CP-20 21, CP-16 27, and 15 for the other 23 rows. `totalMinutes: 90` is the serialized default. No row is a stub: each names one filter, one build, and a positive Min.

## Collisions

Stage 3 edits only `scripts/lib/release-impact.ps1` (the `:40` array), `tests/Antiphon.Tests/Scripts/ReleaseImpactEnvelopeTests.cs`, and `tests/derived-impact-index.json` by the owner driver, build first then a second unchanged run (section 5.1, same commands as Plan `:202-207`). It does not edit file 19 or the other deciding files.

The shared-file table matches the sequencing plan's six pairs at `03b7faa9791e3668e5ff89f18838c84f20ae3492:44-48`. Every pair shares the generated index, and D-A5 keeps one writer at a time in the order S1R5, then 1883 S1..S3, then 1882 S1, then S1R6. S1R5 creates the module and must not edit the selector or the driver source. 1883 S2 edits roster file 7, which stage 3 hashes and whose anchor the preflight re-checks. S1R6 extends stage 3's array and edits the envelope fixtures, `scripts/impacted-classes.ps1`, and the driver source. The index is never hand-merged.

## Checks at the reviewed SHA

| Check | Result |
|---|---|
| `git merge-tree --write-tree origin/master HEAD` | tree `e5c13409ac853c4dc1c87f84725a539a510ba27f`, exit 0 |
| `scripts/check-evidence-diff.ps1` base..HEAD | commits=1, entries=0, violations=0 |
| `scripts/impacted-classes.ps1 -Range` | kinds, classes, files, filters, and windowsFilters empty. Migration row did not match. Downgrade count 0 |
| `scripts/check-os-witnesses.ps1` | OS-WITNESS complete. Only the plan markdown changed. Restart not required |
| Checkpoint import | 26 rows, 3 builds, 0 warnings |
| `ReleaseImpactEnvelopeTests`, lease `64719354-8163-4d40-8464-009904878bcd` | total 11, failed 0, skipped 0, 56s 259ms, exit 0. Slot held 383s. The slot noted that `dotnet run`'s implicit build does not receive `-maxcpucount` |
| Five-class guard batch, lease `4306130c-76b4-4172-b453-284f0e9e03c5`, `--no-build`, load 8.77 | `TestClassificationPolicyTests`, `TestClassificationGuardTests`, `SlowTestRegistryMergeTests`, `SlowTestTripwireTests`, `NativeLinuxPolicyWitnessTests`: total 53, failed 0, skipped 0, 54s 238ms, exit 0. Slot held 58s |

`reviewedSourceClean` stays false. The class runs are build-slot leases, not checkpoint-tool run receipts, and the plan rows were imported rather than executed. No session waits for input. Parking and CARD-1083 do not apply. No OS-selected production path changed.

## Advisories

These do not force Code or another Review.

- Plan `:209` says not to edit the other 16 deciding files. D-A2's phrase "S1 never edits a deciding file" is broader than that line: S1 does edit roster file 2. The instruction not to edit file 19 to invent an anchor is still explicit.
- The sentence "SEQ's S1R6 CP-108 floor of 11" names the wrong stage. CP-108 is the S1R5 row, and its floor of 11 is the class before stage 3. After stage 3 the class is 46, so S1R6's own TestDesign re-census is the real obligation. The count 46 is right.
- Section 5.1 matches the landed CARD-1882 plan: one script invocation that builds, then one `-NoBuild` fixed point. The sequencing plan's chain-wide block is a separate `dotnet build` lease plus two `-NoBuild` runs. Both reach the same fixed point. The script builds with `-p:UseAppHost=false` and native output when `-NoBuild` is absent.
- "must not touch" and "—" on the driver source mean those stages do not edit it. S1R5 and 1883 S3 still run it to regenerate the index, which the index row already says. `ReleaseImpactProjectTests.cs` is an 1883 S1 / S1R6 surface absent from the table. Stage 3 does not edit it.
- D-A4's "one more 7-minute build" is the new cold build. The table delta is +8 minutes on each of red and green: CP-20's old 1 minute is absorbed into the new 7, and CP-25 plus CP-26 add 2. The minute totals 21, 12, 17, 33, 50, and 90 are the figures that add.
- `Fixture.GitAsync` asserts exit 0 (`ReleaseImpactEnvelopeTests.cs:309`). The expected-failing `git cat-file -e` in the absence recipe needs a non-asserting git call. The red assertion itself uses `SelectCopiedAlgorithmAsync`, which returns the exit code.
- A BOM is recorded and kept. Any CR blocks. Newlines stay LF.
