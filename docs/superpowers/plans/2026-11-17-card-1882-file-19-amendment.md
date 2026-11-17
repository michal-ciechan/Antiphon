# CARD-1882 amendment 2: file 19, `scripts/lib/release-impact-scripts.ps1`

Plan and TestDesign amendment, documents only. No production or test source changes, and no test run. The only tool run is a read-only checkpoint import into ignored `.antiphon/`.

Source read: full `origin/master` `870a93d21f2b18c85a8b0905ab689b28e3099813`, fetched for this dispatch. The task branch `feat/card-task-31f0501d` starts at that SHA.

Inputs:

- CARD-1882 amended plan `docs/superpowers/plans/2026-11-16-card-1882-algorithm-digest-plan.md` (on master, landed as `bc338db9b`) and its TestDesign `docs/superpowers/plans/2026-11-16-card-1882-testdesign.md` (landed as `14f3b5528`).
- CARD-1882 reviews `docs/superpowers/reviews/2026-11-16-card-1882-plan-testdesign-review.md` (round 1, two blockers) and `...-review-r2.md` (round 2, clean, landed as `61259342c`).
- CARD-1820 sequencing plan `docs/superpowers/plans/2026-11-16-card-1820-s1r5-s1r6-sequencing-plan.md` on `origin/feat/card-task-dc5b6c22` at `03b7faa9791e3668e5ff89f18838c84f20ae3492`. It is not on master.
- Its clean round-1 review `docs/superpowers/reviews/2026-11-16-card-1820-s1r5-s1r6-sequencing-plan-review.md` on `origin/feat/card-task-4f507352` at `f76736b8de0b89af3d9ccf8f12b48f85015b4058`. It is not on master.
- CARD-1883 plan `docs/superpowers/plans/2026-11-16-card-1883-reflection-witness-plan.md`, on master.

Citations are master `870a93d21` line numbers. "Plan" means the CARD-1882 amended plan above, "TD" means its TestDesign, and "SEQ" means the CARD-1820 sequencing plan.

This file supersedes TD's `### Checkpoints` table. From now on the CARD-1882 S1 Code stage runs this file's table, `--plan docs/superpowers/plans/2026-11-17-card-1882-file-19-amendment.md`, not the TD's. Every other Plan and TD rule stays authoritative unless this file names a change. This file is separate from the Plan and TD on purpose: it leaves their reviewed bytes unchanged, and it is the whole reviewable delta.

## 1. What exists today

### The algorithm digest

`Get-ReleaseImpactAlgorithmIdentity` in `scripts/lib/release-impact.ps1:35-54`:

1. It resolves `HEAD^{commit}` of the selector's own source root to a 40-hex `Sha`. `New-ReleaseImpactEnvelope` passes `-SourceRoot (Split-Path -Parent $PSScriptRoot)`, so this is the checkout that holds the running selector (`scripts/impacted-classes.ps1:132-133`), not the analysed repository.
2. For each path in the literal array at `:40`, which today is only `scripts/impacted-classes.ps1` and `scripts/lib/release-impact.ps1`, in that order:
   - `git rev-parse --verify <sha>:<path>` gives the committed blob (`:41`).
   - `git hash-object --no-filters -- <path>` gives the raw on-disk blob (`:43`).
   - If they differ, it throws `release-impact algorithm source drift` (`:44`).
   - Otherwise it appends `path + NUL + blob + LF` to the inventory (`:45`).
3. The digest is the lowercase hex of SHA-256 over the UTF-8 bytes of the concatenated inventory (`:47-50`), and it is published as `algorithmDigest` (`:74`).

Every Git call goes through `Invoke-ReleaseImpactGit` (`:6-32`). A non-zero Git exit throws `release-impact Git input could not be verified` (`:23`). The selector catches any throw from the envelope path and calls `Refuse-ReleaseImpact` (`scripts/impacted-classes.ps1:130-137`). That writes `ok=false`, `complete=false`, `incompleteReasons=['invalid-release-request']`, `fallback='Full'`, and an `error` with the `release-impact refused: ` prefix, exits 2, and never writes `algorithmDigest` (`:98-111`).

How a roster change reaches the digest: adding a path to the `:40` array adds one `path NUL blob LF` record at that position. That changes the digest for every source tree, even when no file bytes changed. The array order is the record order, and nothing sorts it. Appending file 19 therefore changes every published `algorithmDigest` once. That fails closed: a changed algorithm identity between baseline and candidate refuses carry credit (Plan D-1, `Plan:24`; Plan `:37`). No test or script pins a literal envelope digest value. The only literal `algorithmDigest` values are the synthetic `c`/`d` admission facts in `scripts/test-rc-admission.ps1:38,85`, which are not envelope output.

The admission list is a different thing. `Get-RcAdmissionAlgorithmCoverage` (`scripts/lib/rc-admission.ps1:9-34`) is a static required list that always returns `complete=$false` and `release-graph-incomplete`. Plan D-1 and D-6 (`Plan:24,63`) reject treating it as this closure, and this amendment does not touch it.

### The 18-file roster

The 18-file roster is planned, not implemented. On master, `:40` lists only files 1 and 2. CARD-1882 S1 will grow the array to the 18 ordered paths in `Plan:171-188`, which TD repeats at `TD:162-181`. Positions 1-14 are the original plan's files and 15-18 are Amendment 1's MSBuild files (`Plan:158-165`):

1. `scripts/impacted-classes.ps1`
2. `scripts/lib/release-impact.ps1`
3. `scripts/lib/SelectorCensus.cs`
4. `tests/Antiphon.Tests/Scripts/ReleaseImpactGraphBuilder.cs`
5. `tests/Antiphon.Tests/Scripts/DerivedImpactIndexBuilder.cs`
6. `tests/Shared/PlatformTaxonomy.cs`
7. `tests/Antiphon.Tests/Scripts/ReleaseImpactProjectGraph.cs`
8. `tests/Antiphon.Tests/Scripts/ReleaseImpactRuntimeInputs.cs`
9. `scripts/regenerate-derived-impact-index/Program.cs`
10. `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj`
11. `tools/Antiphon.LandGraphAudit/Program.cs`
12. `tools/Antiphon.LandGraphAudit/ReleaseInputCommand.cs`
13. `tools/Antiphon.LandGraphAudit/ReleaseInputEvaluation.cs`
14. `tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj`
15. `Directory.Build.props`
16. `Directory.Build.targets`
17. `tests/Shared/CheckpointRepeat.targets`
18. `tools/Antiphon.LandGraphAudit.Core/Antiphon.LandGraphAudit.Core.csproj`

### Tests that assert the roster

On master `870a93d21`, in `tests/Antiphon.Tests/Scripts/ReleaseImpactEnvelopeTests.cs`:

| Test | Line | What it asserts about the roster |
|---|---|---|
| `AlgorithmSourceDriftIsRefused(path, normalized)` | `:121-156` | Four `[Arguments]` (`:122-125`): files 1 and 2, each with a multi-byte appended comment (`false`) and a CRLF rewrite (`true`). Drift exits 2 with `algorithm source drift` and no `algorithmDigest` (`:149-155`). |
| `AlgorithmDigestBindsCommittedBlobs` | `:158-186` | An independent two-path literal oracle (`:165`) equals the emitted digest, and a committed edit changes it. |
| `Fixture.CreateAsync` | `:238-271` | Copies the same two paths (`:244`) as text with CRLF folded to LF (`:247-248`). The fixture `.gitattributes` is `*.ps1 text eol=lf` (`:250`). |

That is 11 results today: 7 single tests plus 4 drift arguments. SEQ's review (`f76736b8d`) reports the same count.

After CARD-1882 S1 as planned, the roster is asserted by:

- 18 wrappers `AlgorithmSourceDriftIsRefused_NN_*`, two bool results each, behind one private helper (`TD:80-87`).
- `AlgorithmDigestBindsCommittedBlobs`, with an 18-literal-path oracle (`TD:191-193`).
- The drift recipe's step 6 oracle, also 18 literal paths (`TD:133-138`).
- A raw-byte fixture copy of all 18 files (`TD:89-98`).

`DerivedImpactIndexTests.committed_index_matches_the_builder` guards the regenerated index (`TD:202-207`). It does not assert the roster itself.

### The floors 36/43/180

The review's "38/45/182" are the post-file-19 values of these. None of them counts results that execute today.

| Number | What it counts | Source | Arithmetic |
|---|---|---|---|
| 36 | Drift results after S1: 18 files × 2 variants (B, W) | `Plan:167`, `Plan:268`, `TD:86` | 18 × 2 = 36 |
| 43 | `ReleaseImpactEnvelopeTests` results after S1: 36 drift + 7 single methods | `Plan:269`, `TD:86`, `TD:334` | 36 + 7 = 43. The seven are the six fixture/envelope methods of CP-16 plus `AlgorithmDigestBindsCommittedBlobs` (`:15-63`, `:158`, `:188`). |
| 180 | Minimum executed results across the whole S1 checkpoint union | `Plan:271`, `TD:398-403` | 43 envelope + 1 admission case (CP-17) + 78 (CP-18's nine guard classes) + 57 (CP-19 `ProcessSpawnLimitTests`) + 1 (CP-24 index) = 180 |

These are verified against the documents above. The 78 and 57 are reviewed-manifest floors carried from the CARD-1820 sibling (`TD:401-402`), not counted here. TD also pins a drift-run minimum of 37 (36 + CP-15) and a guard-run minimum of 143 (6 + 1 + 78 + 57 + 1) at `TD:402-403`.

## 2. Ground truth

| Assumption (review, SEQ or brief) | What the tree says on `870a93d21` |
|---|---|
| `scripts/lib/release-impact-scripts.ps1` is absent from master. | True. `ls` fails, and no `.cs`, `.ps1` or `.json` file names it. |
| CARD-1882's roster does not bind it. | True. The 18 paths are `Plan:171-188`. Plan `:15` and TD `:33` require a re-plan to add it, never a silent addition. |
| An absent roster file makes the digest fail closed. | True by code. `rev-parse --verify <sha>:<path>` exits non-zero for a path missing from HEAD, and `hash-object --no-filters -- <path>` exits non-zero for a path missing from disk. Either one throws `release-impact Git input could not be verified` (`release-impact.ps1:23`), and the selector refuses with exit 2 and no digest (`impacted-classes.ps1:98-111,137`). No test pins this today; see D-A3. |
| The floors become 38/45/182. | True for the file-19 drift wrapper alone (§3.3). This amendment also adds one absence witness, so the envelope floor is 46 and the overall floor is 183. Both are at least the review's numbers. |
| The 25-minute drift-run estimate absorbs one more row. | False. The TD's drift run is already exactly 25 (`TD:468-474`), and the Plan's cap is "no single checkpoint run estimated above 25" (`Plan:286`). SEQ D-3 says to split into bounded After groups instead of squeezing the build time. See D-A4. |
| File 19 is LF with no BOM, and has a unique uppercase comment anchor. | UNOBSERVED: the file does not exist. In the production `.gitattributes`, `*.ps1 text eol=lf` (`.gitattributes:88`) makes a committed `.ps1` LF in the index and the worktree. That is an expectation, not an observation of file 19. The anchor is bound by the rule in D-A2, checked first by Code. |
| A new tracked-path literal in the envelope test changes the index. | True. `DerivedImpactIndexBuilder.CollectStringTargets` adds an edge for any literal of 8 or more characters that equals a tracked path (`tests/Antiphon.Tests/Scripts/DerivedImpactIndexBuilder.cs:304-313`). The file-19 literal is 38 characters, and it is tracked only after S1R5 lands. |

## Decisions

D-A1. **File 19 is `scripts/lib/release-impact-scripts.ps1`, appended at roster position 19.** It is a deciding file under Plan D-2's first rule (`Plan:28`): its code resolves script closures, which decide which test classes a release change selects. Before S1R6 wires it in, no production command executes it, only its tests. Roster file 8 (`Plan:150`) is the precedent: bound before execution, because binding is not execution (Plan D-4, `Plan:59`).

Rejected alternatives:

- Inserting it next to file 2. That would renumber 3-18 and every CP, anchor and wrapper.
- Sorting the array, which Plan `:169` forbids.
- Deferring it to S1R6. That stage depends on 1882 S1's identity array (SEQ D-1/D-3), and SEQ D-3 requires the amendment before stage 3.

D-A2. **Same recipe as files 1-18: raw-byte B and W variants, raw fixture copy, independent oracle.** The roster entry is the literal string `scripts/lib/release-impact-scripts.ps1`, lowercase and forward slashes, appended as the 19th element of the `:40` array. Hashing is unchanged: committed blob from `rev-parse <sha>:scripts/lib/release-impact-scripts.ps1`, raw disk blob from `hash-object --no-filters`, and the record `scripts/lib/release-impact-scripts.ps1\0<blob>\n` after file 18's record.

The B anchor is UNOBSERVED. Code binds it with this rule before any edit:

- Take the first line of the file that matches `^\s*#\s+[A-Z]`.
- The anchor is the text from that uppercase letter to the end of the line, without the trailing whitespace.
- The anchor must occur exactly once in the file. B lowercases its first letter, which changes exactly one byte.

If no line qualifies, or the first qualifying anchor is not unique, Code stops and reports blocked for a re-amendment. It must not edit file 19 to create an anchor, because S1 never edits a deciding file (`Plan:209`).

Code records the bound anchor, its byte offset and the file's blob ID in its report.

Rejected: a multi-byte appended comment. TD's one-byte rule (`TD:111-116`) applies to every file.

D-A3. **An absent file 19 fails closed, and one new witness pins it.** With file 19 in the array, an absent file always refuses:

- missing from the HEAD tree: `rev-parse` fails;
- present only untracked on disk: `rev-parse` fails;
- committed but deleted on disk: `hash-object` fails.

In each case the result is the throw at `:23`, then exit 2, `ok=false`, `complete=false`, `fallback=Full`, and no `algorithmDigest`.

No existing or planned witness would catch a regression that skips missing roster paths. The drift wrappers always copy the file, so a "skip if absent" edit (`Test-Path`, a try/catch `continue`, or a `--` check) survives all 38 drift results.

Add one test, `AlgorithmSourceAbsenceIsRefused_19_ScriptClosure`, a single `[Test]` with no arguments:

1. Create a fresh fixture. Run the clean selector: exit 0 with a 64-hex digest.
2. Run the bounded `git rm -q -- scripts/lib/release-impact-scripts.ps1`, then the existing `CommitAsync`. Assert that `git cat-file -e HEAD:scripts/lib/release-impact-scripts.ps1` fails and the file is gone from disk. This is the exact pre-S1R5 shape.
3. Run `SelectCopiedAlgorithmAsync` and require:
   - `Exit.ShouldBe(2)`;
   - `AssertRefused`;
   - an error containing `Git input could not be verified`;
   - `TryGetProperty("algorithmDigest", out _)` false.

Before the S1 production edit, the array lacks path 19, so the clean selector exits 0 and the test is red at `Exit.ShouldBe(2)`.

Rejected alternatives:

- Prose only. The brief requires that absence must not pass silently, and only a test makes that true for later edits.
- Witnesses for all 19 paths. The mechanism is shared, and one path suffices to pin it.
- A two-shape `[Arguments]` version. Removing the file from both HEAD and disk already defeats every skip-if-missing variant.

D-A4. **Split the drift run into two After groups. Do not stretch the 25-minute estimate.** The drift rows are:

- `S1`: CP-1..CP-15.
- `S1c` (new): CP-20..CP-23, plus new CP-25 and CP-26. It has its own isolated build, `bin-c1882-s1c/`. CP-20 becomes the row that owns that build.

The guard run, `S1b`, is unchanged. Each group's estimate stays at or below 25 minutes: S1 is 21, S1c is 12, S1b is 17. CP numbers 1-24 are not renumbered. CP-20..CP-23 change only their After and Build cells, and their filters, Covers, Min and estimates are unchanged except CP-20's estimate (see §5).

Rejected alternatives:

- Moving CP-15 to S1b. CP-15 belongs in the red-first run, and S1b carries CP-24, which must stay out of it (`TD:409`). It would also leave S1 at exactly 25 with the build already counted as tight.
- OR-grouping drift rows. TD `:464-466` keeps one row per file for receipt identity.
- A 26-minute estimate. That would break the Plan `:286` cap.

The cost is one more 7-minute build in each of the red-first run and the green run.

D-A5. **Stage 3 runs only after stages 1 and 2 have landed.** The CARD-1882 S1 Code stage is dispatched only after CARD-1836 S5c, CARD-1820 S1R5 and CARD-1883 S1..S3 are all published on master, in SEQ D-1's order. Its first action is the preflight in §4. A failed preflight means report blocked and stop. Do not add path 19 conditionally, create or stub the file, or copy a pre-S1R5 placeholder.

Rejected: dispatching stage 3 alongside stage 2. Both write the index, and 1883 rewrites roster file 7, whose bytes and anchor stage 3 copies (SEQ collision 1883/1882).

D-A6. **The mutation waiver stands.** The operator waiver of 2026-11-12 still applies (Plan D-7). PC-28 and PC-29 are designed and not executed. They do not discharge M7.

## 3. The amendment

### 3.1 Roster entry and production edit

`scripts/lib/release-impact.ps1`, inside `Get-ReleaseImpactAlgorithmIdentity` only: the foreach array (`:40`) becomes the 18 Plan paths in order, then `'scripts/lib/release-impact-scripts.ps1'`. Nothing else in the module changes. No `Test-Path`, no optional entry, and no try/catch around the loop.

### 3.2 Test edits

All test edits are in `tests/Antiphon.Tests/Scripts/ReleaseImpactEnvelopeTests.cs`:

- **Fixture copy set.** Copy all 19 paths, file 19 included, with `File.ReadAllBytes` and `File.WriteAllBytes` (TD `:94-98`). The five-line fixture `.gitattributes` from TD `:91-93` already covers `*.ps1`, so it does not change.
- **Oracle.** One private static 19-element literal path array in the test class, independent of production. Both the `AlgorithmDigestBindsCommittedBlobs` oracle and drift recipe step 6 use it.
- **New drift wrapper.** `AlgorithmSourceDriftIsRefused_19_ScriptClosure` with `[Arguments(false)]` and `[Arguments(true)]`, delegating to the shared helper with the D-A2 anchor.
- **New absence test.** `AlgorithmSourceAbsenceIsRefused_19_ScriptClosure`, per D-A3.
- Unchanged: the 18 TD wrappers, the six CP-16 methods, `[Category("Unit")]` and `ProcessSpawnLimit`.

Name check: neither new method name starts with any operand in CP-16's OR filter or any CP-18 class name, so no suffix collision. Both new rows use single exact method filters.

### 3.3 Floors and counts

| Count | TD (18 files) | After amendment 2 | Arithmetic |
|---|---:|---:|---|
| Deciding files | 18 | 19 | 18 + 1 |
| Drift results | 36 | **38** | 19 × 2 |
| Absence results | 0 | 1 | D-A3 |
| `ReleaseImpactEnvelopeTests` results | 43 | **46** | 38 + 1 + 7. The file-19 wrapper alone gives the review's 45. |
| Overall minimum executed results | 180 | **183** | 46 + 1 + 78 + 57 + 1. The file-19 wrapper alone gives the review's 182. |
| Checkpoints | 24 | 26 | + CP-25, CP-26 |
| Run minimum, S1 | 37 (drift run) | 29 | CP-1..CP-14 = 28, + CP-15 = 1 |
| Run minimum, S1c | n/a | 11 | CP-20..CP-23 = 8, + CP-25 = 2, + CP-26 = 1 |
| Run minimum, S1b | 143 | 143 | unchanged |
| Guards and distinct controls | 27 | 29 | + G-28/PC-28, G-29/PC-29 |

Check: 29 + 11 + 143 = 183.

### 3.4 Verification, guard and control rows to add to the TD

These are the TD sections they amend:

- **V-24, "Proves it works now":** `scripts/lib/release-impact-scripts.ps1`, the script-closure module, added by CARD-1820 S1R5. The B anchor is bound by the D-A2 rule. Filter `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_19_ScriptClosure`. Before S1, B and W are red at exit 0 instead of 2. After S1, both are green. Reach: the production identity's Git reads only; the module is not executed by the envelope.
- **V-25:** `AlgorithmSourceAbsenceIsRefused_19_ScriptClosure`. Before S1 it is red at exit 0 instead of 2. After S1 it is green.
- **V-15 (amended):** the oracle becomes 19 literal paths. TD `:136-137` changes "all eighteen" to "all nineteen".
- **R-1 (amended):** "omission of any of nineteen deciding paths", over CP-1..CP-14, CP-20..CP-23 and CP-25.
- **R-6 (new):** a roster path missing from HEAD or disk is skipped instead of refused. Row CP-26.
- **G-28:** roster position 19's disk bytes must match its committed blob before identity publication. Control PC-28.
- **G-29:** a missing roster path refuses identity publication with exit 2 and no digest. Control PC-29.
- **PC-28:** omit only path 19 from the production array. Run exact method `AlgorithmSourceDriftIsRefused_19_ScriptClosure`. Both results go red at `refused.Exit.ShouldBe(2)` with actual exit 0. Restore, and both go green. 
- **PC-29:** keep path 19 in the array. Inside the per-path loop, wrap the body as `try { ... } catch { if ($_.Exception.Message -ceq 'release-impact Git input could not be verified') { continue }; throw }`, which still parses. Run exact method `AlgorithmSourceAbsenceIsRefused_19_ScriptClosure`. It goes red at `Exit.ShouldBe(2)` with actual exit 0. Restore, and it goes green.

PC-29 is distinct from PC-28. Under PC-29 a drift still throws `algorithm source drift`, which the catch rethrows, so `AlgorithmSourceDriftIsRefused_19_ScriptClosure` (CP-25) stays green and only CP-26 goes red. Under PC-28 both CP-25 and CP-26 go red.

### 3.5 Red-first and green execution

Red-first changes the TD `:405-433` substitute in these ways:

- There are now two red manifests. Import this table into ignored YAML twice: once keeping only the `After=S1` rows with output `bin-c1882-s1-red/`, and once keeping only the `After=S1c` rows with output `bin-c1882-s1c-red/`. Run each at the committed red SHA.
- Expected red, S1: CP-3..CP-14 (24 results) at the exit assertion, and CP-1, CP-2 and CP-15 (5 results) on the 19-path oracle.
- Expected red, S1c: CP-20..CP-23 and CP-25 (10 results) at the exit assertion, and CP-26 (1 result) at the exit assertion.
- CP-24 is still not in red-first. A clean-setup failure is not red evidence.

Green is three `run` invocations at the final SHA, after the index JSON commit, in this order:

1. `--after S1`
2. `--after S1c`
3. `--after S1b`

Each takes `--expected-source-sha <full-committed-code-sha>` and waits through exit 75 until terminal. Never pass `--after all`.

## 4. Code preflight (stage 3, before any edit)

Run this on the task branch base, read-only. Record each result in the report.

1. `git fetch origin master`. Record the full SHA, and confirm the task base is that SHA or a descendant of it.
2. Check that file 19 exists:
   - `git cat-file -e HEAD:scripts/lib/release-impact-scripts.ps1` exits 0;
   - `git ls-files --error-unmatch -- scripts/lib/release-impact-scripts.ps1` exits 0;
   - the file exists on disk.
3. Check that the predecessors are published. The caller's brief gives the landed master SHAs for CARD-1836 S5c, CARD-1820 S1R5 and CARD-1883 S3. For each, `git merge-base --is-ancestor <sha> HEAD` must succeed. If no SHA is supplied, find each landing by commit subject and card id in `git log origin/master`, not by task-branch SHA, because land rebases. Record what you found.
4. Check that no CARD-1820 S1R6 Code task holds `scripts/lib/release-impact.ps1`, `scripts/impacted-classes.ps1`, `ReleaseImpactEnvelopeTests.cs` or the index. Ask the caller if that is not stated in the brief.
5. Check file 19's bytes:
   - `git ls-files --eol -- scripts/lib/release-impact-scripts.ps1` shows `i/lf w/lf`;
   - the file has zero CR bytes;
   - whether it starts with a BOM is recorded. A BOM is allowed and preserved by the raw copy.
   If the file has any CR, stop: the W variant's LF-to-CRLF rewrite assumes LF.
6. Bind the D-A2 anchor for file 19. Re-check that each of the 18 TD anchors (`TD:164-181`) is still unique, inside a comment, and starts with an uppercase ASCII letter on this base. Stage 2 rewrote file 7 (`ReleaseImpactProjectGraph.cs`), so re-check its anchor `Conservative source inventory` in particular. Any miss means blocked and a re-amendment; never edit a deciding file.
7. `git merge-tree --write-tree origin/master HEAD` reports no conflicts.

If step 2 fails, file 19 has not landed. Stage 3 must not proceed. If it did proceed, it would not pass silently anyway:

- the real-repo `-ReleaseEvidence` path would always refuse, by D-A3;
- every envelope fixture would throw `FileNotFoundException` while copying from `RepoRoot()` (`ReleaseImpactEnvelopeTests.cs:244-248`, and the raw replacement), which is a fixture error and never green;
- the file-19 literal would add no index edge, because the path is untracked, so CP-24 would bind a wrong index.

These are reasons to stop, not acceptable states.

## 5. Files touched by stage 3, and collisions

The CARD-1882 S1 Code stage edits only these files:

| File | Edit |
|---|---|
| `scripts/lib/release-impact.ps1` | The `:40` path array grows to 19 entries (§3.1). |
| `tests/Antiphon.Tests/Scripts/ReleaseImpactEnvelopeTests.cs` | 19 wrappers, the absence test, the raw-byte fixture, the 19-path oracle, and the five-line fixture `.gitattributes`. |
| `tests/derived-impact-index.json` | Owner regeneration only, in its own commit (§5.1). |
| Optional Markdown reports | Ignored `.antiphon/` evidence. Never a deciding file. |

It reads and hashes, but never edits, the 19 deciding files. Of those, 18 are hashed in place, and `release-impact.ps1` is both edited and hashed. It does not edit `scripts/lib/rc-admission.ps1` (D-6), the qualification executor, `scripts/lib/release-impact-scripts.ps1`, or this plan, the Plan or the TD. Doc amendments go through a separate Plan task.

Collisions with the other three selector stages, in SEQ D-1 order (S1R5, then 1883 S1..S3, then 1882 S1, then S1R6):

| Shared file | 1820 S1R5 (stage 1) | 1883 S1..S3 (stage 2) | 1882 S1 (stage 3) | 1820 S1R6 (stage 4) |
|---|---|---|---|---|
| `tests/derived-impact-index.json` | regenerates | S3 regenerates | regenerates | regenerates |
| `scripts/lib/release-impact-scripts.ps1` | creates | — | hashes, roster 19, never edits | consumes and wires in, and may edit; it is then a drift target |
| `scripts/lib/release-impact.ps1` | must not touch | — | edits the array, roster 2 | edits for composition, and extends rather than replaces stage 3's array |
| `tests/Antiphon.Tests/Scripts/ReleaseImpactEnvelopeTests.cs` | — | — | edits | edits fixtures and the copy set. CP-108's class floor becomes 46, so re-census. |
| `tests/Antiphon.Tests/Scripts/ReleaseImpactProjectGraph.cs` | must not touch | S2 edits | hashes, roster 7, and re-checks the anchor | may touch adapters only through its TestDesign |
| `scripts/impacted-classes.ps1` | must not touch | — | hashes, roster 1 | edits |
| `scripts/regenerate-derived-impact-index/{Program.cs,Antiphon.DerivedImpactIndex.csproj}` | must not touch | — | hashes, roster 9-10, and builds and runs it to regenerate | owns |
| `tests/Antiphon.Tests/Scripts/ReleaseImpactScriptTests.cs` and `scripts/fixtures/selector-v2/**` | creates | — | — | extends |

"—" means the stage neither edits nor reads the file for this purpose. Sources: SEQ stage table (`SEQ:34-36`) and collision table (`SEQ:44-48`), the 1883 plan's D-5, S2 and S3 (`docs/superpowers/plans/2026-11-16-card-1883-reflection-witness-plan.md:34,42,44`), and Plan `:13-18`. Exactly one stage holds the index at a time. It is never hand-merged.

### 5.1 Index regeneration, owned by stage 3

Run this after the last edit to `ReleaseImpactEnvelopeTests.cs`, on the integrated base, with no test in flight. It is the same as Plan `:202-207`. Build the driver first, then run it twice:

1. `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1882-index -- pwsh -NoProfile -File scripts/regenerate-derived-impact-index.ps1`. Without `-NoBuild`, the script first builds `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj` with `-p:UseAppHost=false` and native output (`scripts/regenerate-derived-impact-index.ps1:22-29`), then runs it (`:31`). It must report that the bytes changed: S1 adds 17 new tracked-path literals, and file 19's literal adds a new edge `scripts/lib/release-impact-scripts.ps1` to `ReleaseImpactEnvelopeTests`.
2. `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1882-index-2 -- pwsh -NoProfile -File scripts/regenerate-derived-impact-index.ps1 -NoBuild`. It must report that the bytes are unchanged. If they changed, stop and report.
3. Commit only the JSON, then push. If the base moves before land, regenerate again in a new commit.

## Verification design

This is the checkpoint supplement. It supersedes the TD `### Checkpoints` table. The V, R, G and PC rows are the TD's, plus §3.4. The lane is Linux, on the server2 default from `GET /api/runner-defaults`, with no `-Runner` and no `-Platform`. Every row is serial. `Min` counts executed results. Each row needs a fresh TRX, and zero failed or skipped. There is no `totalTimeoutMinutes` line.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1882-s1/` | selector-entry | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_01_SelectorEntry` | V-1, R-1, R-2 | AlgorithmSourceDriftIsRefused_01_SelectorEntry; B/W, zero failed/skipped | 2 | 7 | true |
| CP-2 | S1 | CP-1 | release-module | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_02_ReleaseModule` | V-2, R-1, R-2 | AlgorithmSourceDriftIsRefused_02_ReleaseModule; B/W, zero failed/skipped | 2 | 1 | true |
| CP-3 | S1 | CP-1 | selector-census | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_03_SelectorCensus` | V-3, R-1, R-2 | AlgorithmSourceDriftIsRefused_03_SelectorCensus; B/W, zero failed/skipped | 2 | 1 | true |
| CP-4 | S1 | CP-1 | graph-builder | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_04_GraphBuilder` | V-4, R-1, R-2 | AlgorithmSourceDriftIsRefused_04_GraphBuilder; B/W, zero failed/skipped | 2 | 1 | true |
| CP-5 | S1 | CP-1 | index-builder | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_05_IndexBuilder` | V-5, R-1, R-2 | AlgorithmSourceDriftIsRefused_05_IndexBuilder; B/W, zero failed/skipped | 2 | 1 | true |
| CP-6 | S1 | CP-1 | platform-taxonomy | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_06_PlatformTaxonomy` | V-6, R-1, R-2 | AlgorithmSourceDriftIsRefused_06_PlatformTaxonomy; B/W, zero failed/skipped | 2 | 1 | true |
| CP-7 | S1 | CP-1 | project-graph | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_07_ProjectGraph` | V-7, R-1, R-2 | AlgorithmSourceDriftIsRefused_07_ProjectGraph; B/W, zero failed/skipped | 2 | 1 | true |
| CP-8 | S1 | CP-1 | runtime-inputs | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_08_RuntimeInputs` | V-8, R-1, R-2 | AlgorithmSourceDriftIsRefused_08_RuntimeInputs; B/W, zero failed/skipped | 2 | 1 | true |
| CP-9 | S1 | CP-1 | index-driver | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_09_IndexDriver` | V-9, R-1, R-2 | AlgorithmSourceDriftIsRefused_09_IndexDriver; B/W, zero failed/skipped | 2 | 1 | true |
| CP-10 | S1 | CP-1 | index-project | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_10_IndexProject` | V-10, R-1, R-2 | AlgorithmSourceDriftIsRefused_10_IndexProject; B/W, zero failed/skipped | 2 | 1 | true |
| CP-11 | S1 | CP-1 | evaluator-driver | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_11_EvaluatorDriver` | V-11, R-1, R-2 | AlgorithmSourceDriftIsRefused_11_EvaluatorDriver; B/W, zero failed/skipped | 2 | 1 | true |
| CP-12 | S1 | CP-1 | evaluator-command | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_12_EvaluatorCommand` | V-12, R-1, R-2 | AlgorithmSourceDriftIsRefused_12_EvaluatorCommand; B/W, zero failed/skipped | 2 | 1 | true |
| CP-13 | S1 | CP-1 | evaluator | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_13_Evaluator` | V-13, R-1, R-2 | AlgorithmSourceDriftIsRefused_13_Evaluator; B/W, zero failed/skipped | 2 | 1 | true |
| CP-14 | S1 | CP-1 | evaluator-project | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_14_EvaluatorProject` | V-14, R-1, R-2 | AlgorithmSourceDriftIsRefused_14_EvaluatorProject; B/W, zero failed/skipped | 2 | 1 | true |
| CP-15 | S1 | CP-1 | ordered-identity | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmDigestBindsCommittedBlobs` | V-15, R-2 | AlgorithmDigestBindsCommittedBlobs; zero failed/skipped | 1 | 1 | true |
| CP-16 | S1b | `tests/Antiphon.Tests -> bin-c1882-s1b/` | envelope-fixture | `/*/*/ReleaseImpactEnvelopeTests/(Fixture_repositories_live_under_system_temp_outside_the_checkout*)\|(MissingIndexIsIncomplete*)\|(StaleInputIsIncomplete*)\|(CandidateMismatchIsIncomplete*)\|(OrdinaryModeRemainsAdvisory*)\|(FixtureCleansUpAfterFailure*)` | V-16, R-2, R-4 | all six listed methods, zero failed/skipped | 6 | 9 | true |
| CP-17 | S1b | CP-16 | admission-freeze | `/*/*/RcHistoricalAdmissionTests/CollisionAndAlgorithmCoverageForceFull` | V-17, R-3 | CollisionAndAlgorithmCoverageForceFull; zero failed/skipped | 1 | 1 | true |
| CP-18 | S1b | CP-16 | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-18, R-4 | all nine listed classes, zero failed/skipped | 78 | 3 | true |
| CP-19 | S1b | CP-16 | spawn-limiter | `/*/*/ProcessSpawnLimitTests/*` | V-18, R-4 | ProcessSpawnLimitTests; zero failed/skipped | 57 | 1 | true |
| CP-20 | S1c | `tests/Antiphon.Tests -> bin-c1882-s1c/` | build-props | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_15_BuildProps` | V-19, R-1, R-2 | AlgorithmSourceDriftIsRefused_15_BuildProps; B/W, zero failed/skipped | 2 | 7 | true |
| CP-21 | S1c | CP-20 | build-targets | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_16_BuildTargets` | V-20, R-1, R-2 | AlgorithmSourceDriftIsRefused_16_BuildTargets; B/W, zero failed/skipped | 2 | 1 | true |
| CP-22 | S1c | CP-20 | repeat-targets | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_17_CheckpointRepeatTargets` | V-21, R-1, R-2 | AlgorithmSourceDriftIsRefused_17_CheckpointRepeatTargets; B/W, zero failed/skipped | 2 | 1 | true |
| CP-23 | S1c | CP-20 | evaluator-core-project | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_18_EvaluatorCoreProject` | V-22, R-1, R-2 | AlgorithmSourceDriftIsRefused_18_EvaluatorCoreProject; B/W, zero failed/skipped | 2 | 1 | true |
| CP-24 | S1b | CP-16 | index-fresh | `/*/*/DerivedImpactIndexTests/committed_index_matches_the_builder` | V-23, R-5 | committed_index_matches_the_builder; zero failed/skipped | 1 | 3 | true |
| CP-25 | S1c | CP-20 | script-closure | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_19_ScriptClosure` | V-24, R-1, R-2 | AlgorithmSourceDriftIsRefused_19_ScriptClosure; B/W, zero failed/skipped | 2 | 1 | true |
| CP-26 | S1c | CP-20 | script-closure-absent | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceAbsenceIsRefused_19_ScriptClosure` | V-25, R-6 | AlgorithmSourceAbsenceIsRefused_19_ScriptClosure; zero failed/skipped | 1 | 1 | true |

Changes from the TD table:

- CP-20..CP-23 move from After `S1`/Build `CP-1` to After `S1c`, with CP-20 owning `bin-c1882-s1c/`. CP-20's estimate goes from 1 to 7 because it now carries the cold build, as CP-1 does.
- CP-25 and CP-26 are new.
- All other cells are byte-identical to `TD:373-396`.

Union minimum: 29 (S1) + 11 (S1c) + 143 (S1b) = **183**.

Import check, read-only. The command was `dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-11-17-card-1882-file-19-amendment.md --out .antiphon/c1882-file19-import.yaml`, run on `870a93d21` with the already-built tool and no build. It exited 0 with `imported 26 rows` and printed zero warnings.

- Three builds: `bin-c1882-s1/`, `bin-c1882-s1b/` and `bin-c1882-s1c/`, all `tests/Antiphon.Tests`.
- After groups:
  - S1: CP-1..CP-15, min 29, estimate 21.
  - S1c: CP-20..CP-23, CP-25 and CP-26, min 11, estimate 12.
  - S1b: CP-16..CP-19 and CP-24, min 143, estimate 17.
- Every row uses its own group's build.
- Derived row timeouts: CP-1 and CP-20 get 21 minutes, CP-16 27, and the other 23 rows 15.
- The serialized `totalMinutes: 90` is the unused manifest default, since no explicit total is set.

## Performance budget

The rules are those of `docs/test-performance-standards.md` and Plan `:274-297`. Every estimate is still provisional, with no measured p95 (n=0), owned by CARD-1882/CARD-1404.

- **Durations per run.**
  - S1 is 21 minutes: CP-1 is 7 for the build plus its two results, and CP-2..CP-15 are 14.
  - S1c is 12 minutes: CP-20 is 7, and CP-21..CP-23, CP-25 and CP-26 are 5.
  - S1b is 17 minutes, unchanged.
  - Every run is at or below 25 minutes, with none near the 30-minute Debug threshold.
- **Phases.** The `C1820 phase=pwsh|git|cleanup` lines are kept. The absence test adds one bounded `git rm` and one commit under the existing 60-second Git ceiling, with no new phase.
- **Shared setup.**
  - One isolated build is shared per After group: 15 rows for S1, 6 for S1c and 5 for S1b. That avoids (14 + 5 + 4) × 7 = **161** estimated build minutes.
  - Each result still gets its own fresh scratch repository, because the bytes, index and commits are the subject (`TD:507-509`).
- **Timeouts.**
  - The importer derives `max(15, 3 × estimate)`: CP-1 and CP-20 get 21 minutes, CP-16 27, and every other row 15. This is the recorded tooling-floor exception, not a 2× p95 budget.
  - Derived run allowances: S1 `max(30, 2×21+10)` = 52, S1c `max(30, 2×12+10)` = 34, S1b 44.
  - The desired per-row caps are those of `TD:502-505`, plus 2 minutes each for CP-25 and CP-26.
  - Child bounds are unchanged: Git 60 s, pwsh 120 s, drain 10 s, teardown 30 s. No timeout is widened.
- **Cost.**
  - Bootstrap: 2 minutes.
  - Red-first: S1 21 + S1c 12 = 33 minutes.
  - Regeneration: 3 + 2 = 5 minutes.
  - Green: 21 + 12 + 17 = 50 minutes.
  - Commissioned Code total: **90 minutes**, up from 74.
  - PC floor, if Mutation is commissioned: 29 controls × (7 + 1 + 7 + 1) + 2 = **466 minutes**. The execution waiver schedules 0 minutes of it.
- **Measurement.** Collect timings from the first red and green runs, per the TD measurement plan (`TD:489-497`). If any run crosses 30 minutes, split it again and commission Debug.

No session that waits for input is added or changed. Parking and CARD-1083 do not apply.

## Existing tests affected

- Changed class: `ReleaseImpactEnvelopeTests` goes from 11 results today to 46 after S1. SEQ's S1R6 CP-108 floor of 11 is stale once stage 3 lands, so S1R6 must re-census to 46.
- Adjacent class, unchanged: `RcHistoricalAdmissionTests.CollisionAndAlgorithmCoverageForceFull`.
- Committed derived output: `tests/derived-impact-index.json`, guarded by `DerivedImpactIndexTests.committed_index_matches_the_builder`.
- Migrations, public DTOs and routes, operation labels, remote script functions, and statement budgets: none.
- CARD-1782 impacted-classes table (`tests/impacted-classes.json`): `scripts/lib/release-impact.ps1` and `scripts/lib/release-impact-scripts.ps1` have no row on `870a93d21`. The test edit matches `tests/Antiphon.Tests/**/*.cs`, which is row `test-lane-category`, class `TestLaneCategoryGuardTests`, and is covered by CP-18. No row is added.

## Card text to append to CARD-1882

> Amendment 2 (Plan 31f0501d, 2026-11-17, master `870a93d21f2b18c85a8b0905ab689b28e3099813`): `docs/superpowers/plans/2026-11-17-card-1882-file-19-amendment.md` on `feat/card-task-31f0501d` adds `scripts/lib/release-impact-scripts.ps1` (CARD-1820 S1R5) as ordered deciding file 19. It adds raw-byte B/W wrapper `AlgorithmSourceDriftIsRefused_19_ScriptClosure` and a fail-closed absence witness `AlgorithmSourceAbsenceIsRefused_19_ScriptClosure`, and grows the independent oracle to 19 literal paths. Floors are now 38 drift, 46 envelope and 183 overall (the review's 38/45/182 plus one absence result). There are 26 checkpoints: CP-25 and CP-26 are appended, and CP-1..24 are not renumbered. The drift run splits into After S1 (21 min) and a new S1c (12 min, own build `bin-c1882-s1c/`). Code runs this file's `### Checkpoints` table, which supersedes the TestDesign's. Stage 3 dispatch waits for S5c, 1820 S1R5 and 1883 S1..S3 to be published, and starts with the §4 preflight (file 19 tracked at HEAD, LF, anchor bound). It stops blocked if the preflight fails. It needs plan-review before Code.
