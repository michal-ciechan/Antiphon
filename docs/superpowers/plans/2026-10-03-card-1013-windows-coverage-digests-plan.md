# CARD-1013: deterministic inline-checklist digests on Windows

## Outcome and stage boundary

The CARD-0891 JSON mismatch is explained by a platform-dependent **derived
inline checklist**, not by a hash-pinned fixture checkout. `PlanCoverageReader`
normalizes the plan to LF, then reconstructs the inline JSON with
`StringBuilder.AppendLine`, reintroducing CRLF on Windows. Its hash changes, as
does the combined input hash. Substituting just those two fields in the retained
Linux JSON reproduces the exact Windows hash recorded on CARD-1013.

Plan a one-line reader correction, regression assertions in existing methods,
and seven exact LF attributes for a separate golden-fixture portability defect.
Retain native Windows symlink/non-regular-project qualification as explicit
remaining work. CARD-1005 and CARD-1006 are landed but do not supersede this work.

This dispatch is Plan only. No production, test, fixture or script edits; no
build, coverage invocation, TUnit execution, native probe or mutation. Verification
below is proposed for **TestDesign**, which was not folded into this dispatch.
Next: **test-design**.

## Ground truth

Inspected source: `2d3c582416c5610d72e53df3e790bc114d87ad82`, the assigned base
and the origin/master observed during this dispatch. Read CARD-1013's complete
description and its one history revision. The revision records Debug task
`2b6de129-5eff-4e00-b2a6-ecf1acc568ff`; there is no closing verdict.

| Card/brief assumption | What code and retained evidence establish | Consequence |
|---|---|---|
| Native Windows coverage may be broken generally | CARD-1013 records 20 passed, 0 failed, 7 skipped on Windows; valid CARD-0891 exits 0 with 72/72, CARD-0780 exits 1 with 11 findings. | Preserve those outcomes; no broad checkpoint runner rewrite. These are historical results, not this Plan's runs. |
| The unexplained JSON difference might be the CARD-1006 CRLF fixture problem | `CoverageCommand` selects and parses C# source; it does not execute fixture-loading test methods. Its CARD-0891 report contains eight C# source entries. All eight digests and the plan digest match Git blobs at `f4044e8f7409e5a8e460c8475360cff2d70d035f`. | Golden fixture bytes cannot explain this CLI result. Do not claim attributes alone fix it. |
| LF input plus normalized output ought to agree | `Coverage/PlanCoverageReader.cs:35` uses `inline.AppendLine(line)` after splitting LF-normalized plan text; `MergeChecklist` hashes that derived text. `PlanCoverageAnalyzer.cs:15` incorporates this digest into `InputsSha256`. | Canonicalize the derived inline representation to LF. |
| Raw source digests should ignore checkout differences | `PlanCoverageReport.Hash` hashes UTF-8 encoded decoded text without newline normalization. `ConfinedFileReader.Read` preserves line endings while decoding/BOM-detecting. | Keep plan/source/external-checklist digest semantics. Identical input bytes remain a prerequisite for comparing report bytes. |
| CARD-1005 might have resolved the mismatch | Owner `535715ec` has confirmed publication at `7af83b0c3270006cd98a25a69531f24e37240a4e`, contained in this base. It adds opt-in census; `AppendLine` remains. | Preserve census behavior and legacy opt-out; run its focused regression class. |
| CARD-1006's fix might cover these goldens | Owner `d0491157` has confirmed publication at `261500e2ff0566cc2b0c370de1cc411f715163a7`, contained in this base. Its attributes name only four Grok fixture/provenance paths. | Coverage fixture LF pins still need their own exact entries. Board column alone was not used as publication proof. |
| The remaining native checks already passed | Windows Debug could not create a symlink. A directory named `.csproj` was skipped by `EnumerateFiles`; it did not test a special file. Existing hardlink/FIFO tests are Linux-gated. | Do not count these skips or the directory probe as Windows acceptance. Preserve the separate CARD-1001 PC obligation. |

### Exact hash reconciliation, without executing coverage

Evidence owner: Code task `d579272f-c1b1-4372-9d90-372eefd83f0e`, retained
`.antiphon/c1001-evidence/final-c891.json`. Its full file SHA-256 is the Linux
reference below; `base-c891.json` and `changed-c891.json` are adjacent historical
comparison artifacts. The owner and Review task `a5c51cc2` reports name this
reference. Windows task `2b6de129` and CARD-1013 name the other reference.
The Linux artifact was read directly from its still-present task workspace;
the Windows raw artifact was not accessible here.

| Value | LF reconstruction (Linux) | CRLF reconstruction (Windows) |
|---|---|---|
| `planSha256`, unchanged | `405657db764a90a4737e8f2904d8552157d2ee714224f23359cfa3a5e78a4569` | same |
| `checklistSha256` | `de0b08d002cc1a7d179944b3e86b18a81eac27cc7fd8cec39590cd247b8d570a` | `3a20d3c481cdb590bb8e4f4c57099ac3b70071513e7b49ec3b8cab102333e2b4` |
| `inputsSha256` | `148f294bc6408b0899562494741daf389845b90f1ecf7d1a87f97db3e5d21a92` | `2dc77e8d71996997f6514efdbbb5907312d3dc31800990fd9691fe83f630be38` |
| Complete JSON SHA-256 | `7357550d7d41e3273950d8e596354bbbf99d143282758b61965898bcbacd1e0f` | `5bf5bee98fb88813a6d94f40d7326c8b38583357f32db5d8fa39846a875eb748` |

Read-only calculation performed: read the original LF JSON; read the plan and
each selected source using `git show f4044e8f7409e5a8e460c8475360cff2d70d035f:<path>`;
verify all nine input hashes; extract the inline fence body including its final
LF; hash its LF and CRLF forms. Recompute the combined digest using the production
formula `planHash + "\n" + checklistHash + "\n" + join("\n", path + "\0" + sourceHash)`.
Replace only the two 64-character digest values in the original JSON string,
without parsing/reserializing it. The resulting full hash equals the recorded
Windows hash exactly. No source files or evidence bytes were modified.

This also explains why CARD-0780 could agree after input/output normalization:
the problematic reconstruction is specific to an inline checklist. Its historical
comparison reference remains
`1317845a9f91bbdac02b49760f1fd312f083ae12c58afebdea435ca19b087ab1`.
Retain raw artifacts under their owners' evidence policy; these hashes and the
calculation do not require committing generated JSON or moving it into docs.

### Separate fixture line-ending evidence

Command inspected:
`git ls-files --eol tests/Antiphon.Tests/Checkpoints/Fixtures/PlanCoverage`.
All twelve files show `i/lf w/lf attr/text=auto` in this Linux checkout.
By contrast the four CARD-1006 paths show `i/lf w/lf attr/text eol=lf`.
This is not an observation of the unavailable Windows checkout.

`PlanCoverageGoldenTests.c866_legacy_reports_v1_v4_v5` hashes all seven blobs
listed in `Fixtures/PlanCoverage/provenance.json` through
`PlanCoverageFixture.Raw` and checks `coverage-raw-provenance`:

| Exact leaf under `tests/Antiphon.Tests/Checkpoints/Fixtures/PlanCoverage/` | Bytes | Current LF matches recorded pin | CRLF conversion matches pin |
|---|---:|---|---|
| `c866-plan.md.txt` | 69779 | yes | no |
| `c866-tests.cs.txt` | 24121 | yes | no |
| `c866-fixed-tests.cs.txt` | 35935 | yes | no |
| `c835-plan.md.txt` | 71301 | yes | no |
| `c835-state.cs.txt` | 17760 | yes | no |
| `c835-script.cs.txt` | 27004 | yes | no |
| `c835-approval.cs.txt` | 26442 | yes | no |

The last two columns were computed in memory from the existing files. This proves
the same *fixture-pin vulnerability* as CARD-1006; it is not a native Windows test
result, nor the cause of the two-field CLI discrepancy above.

## Decisions

- **D-1 — Canonicalize derived inline JSON only.** Replace
  `inline.AppendLine(line)` with `inline.Append(line).Append('\n')`. Input lines
  are already normalized. Preserve the trailing newline, blank lines, whitespace,
  original plan coordinates and strict checklist validation. Reject normalizing
  in `PlanCoverageReport.Hash`, which would change all source/external-input
  provenance. Reject JSON reserialization, which changes whitespace/hash semantics.
- **D-2 — Preserve the schema and raw input contract.** No schema-version change,
  new report field or CLI option. LF-host report bytes on identical inputs remain
  unchanged. The Windows inline-derived hash intentionally becomes the Linux hash.
  External checklist LF/CRLF differences and plan/source LF/CRLF differences still
  produce distinct input digests. Text rendering may retain native output newlines.
- **D-3 — Pin exactly seven raw goldens.** Add one `text eol=lf` entry in
  `.gitattributes` for each leaf in the table. Do not alter provenance pins or fixture
  contents. Reject global `*.txt`/`*.cs`/`*.md` normalization and fixture-loader
  `.Replace` workarounds, which obscure the raw-byte provenance contract. No blanket
  `git add --renormalize .`; a new Windows checkout must materialize the new rules.
- **D-4 — Extend existing tests.** Put inline digest vectors into
  `PlanCoverageParserTests.maps_checklist_without_erasing_legacy_requirements` and
  public report/digest vectors into
  `PlanCoverageCommandTests.renders_stable_text_json_and_exit_codes`. Reuse the
  existing seven-blob provenance assertion. No new Test methods, namespace census
  literal change, whole-Unit run or provider launches are needed for this narrow fix.
- **D-5 — Use OS lanes, not a host pin.** Runner-defaults and session-runners GETs
  were read: revision 2 chooses an eligible Linux default; an eligible Windows lane
  existed but was at capacity. Re-read before dispatch. Generic Plan/TestDesign
  omits `-Runner` and `-Platform`; Windows qualification uses `-Platform Windows`,
  Linux comparison uses `-Platform Linux`, with no `-Runner`. `-Platform Any` is only
  for deliberately clearing an inherited OS pin. Queue when capacity is unavailable.
- **D-6 — Keep remaining native acceptance honest.** Reuse the prior Windows
  hardlink-plan/source and oversize evidence unless source changes justify reruns.
  Commission one in-root symlink acceptance probe on an account already able to
  create it; do not change machine privilege settings as part of this fix. A native
  non-regular project probe must actually reach selected-file admission. A directory
  omitted by enumeration and the pure metadata decision table cannot prove it.
  TestDesign must freeze a safe bounded fixture or name this as a separate Debug
  investigation/qualification decision; do not expand the reader's production API
  merely to manufacture an acceptance result.

## Implementation footprint and slices

| Slice | Files and actions | Tests/evidence and boundary |
|---|---|---|
| S1 — Reader and fixture correction | `tools/Antiphon.Checkpoints/Coverage/PlanCoverageReader.cs`; `.gitattributes`; extend only the two existing methods in `tests/Antiphon.Tests/Checkpoints/PlanCoverageParserTests.cs` and `tests/Antiphon.Tests/Checkpoints/PlanCoverageCommandTests.cs`. Seven fixture contents and `provenance.json` remain unchanged. | Windows detects the original AppendLine defect; the existing golden method detects CRLF checkout of the seven pins. Commit and push the complete slice before execution. |
| S2 — Contract and qualification instructions | Update the static coverage subsection of `docs/testing-and-build.md` with derived-inline LF versus raw-input digest semantics. TestDesign may finalize this plan's exact assertions/commands and native fixture qualification before Code. | Preserve historical CARD-0891/CARD-1001/CARD-1005 plans and frozen goldens. Commit/push. Execute the closed S1-S2 rows at the committed source. |
| S3 — Debug acceptance record, no production edits | Retain task-owned ignored JSON/checkpoint receipts and a bounded Markdown report; update CARD-1013 through `card.ps1`, not `docs/cards/`. | Compare same-input Linux/Windows CLI output; record symlink and non-regular-project evidence or explicit pending reason. Report all exits/counts and exact source SHAs; do not discharge CARD-1001 PC-1..PC-5. |

## Plan-stage verification proposal (superseded by the freeze below)

This is the Plan-stage verification proposal. TestDesign must freeze literal
assertion targets, positive controls, the two OS run selections and native Debug
procedure before Code admission. No tests below were executed in Plan.

V-1: Extend `maps_checklist_without_erasing_legacy_requirements` with an explicitly
LF-authored multi-line inline JSON fence. Independently pinned UTF-8 digest for
`{\n  "version": 1,\n  "items": []\n}\n` is
`da6fa0fb6dc64a3faf1f81f830b8696ccf02b9c654135111429090fa240870f8`;
its CRLF counterpart is
`79f2f76348e92b2903f7a7e371d28a1e951cf465a1ec68a1f1ee2221a871a200`.
Assert the inline digest equals the LF literal on both OSes, including a CRLF
plan, and preserve valid obligations/coordinates. Assert external LF and CRLF
checklists retain their respective distinct literals. Include trailing blank-line
preservation so trimming or removing the final newline cannot pass. Expected
digests must not be calculated by the production hash/reconstruction helper.

V-2: Extend `renders_stable_text_json_and_exit_codes` using the same inert LF plan
and source bytes, once with an inline checklist and once with equivalent external
LF JSON. Assert public exit/result, checklist digest and the independently
constructed combined digest. Also change only actual plan/source/external bytes
to CRLF and assert their provenance changes; do not assert full report equality
between genuinely different inputs. The unchanged-production Windows invocation
must fail at the inline-digest assertion, not at setup or a missing fixture.

V-3: Existing `c866_legacy_reports_v1_v4_v5` must pass on a fresh Windows checkout
with `core.autocrlf=true`; independently inspect all seven `git ls-files --eol`
rows and recorded pins. An unchanged existing checkout is insufficient, because
new attributes need not rewrite already-materialized files. Linux must retain
all seven current blob hashes. Do not loosen `coverage-raw-provenance`.

R-1: Entire Parser, Golden and Census classes: 8 + 4 + 12 = **24** existing
single-result methods per lane. This covers strict checklist parsing, frozen
counts, provenance and CARD-1005 opt-in/opt-out behavior. No result/census delta.

R-2: Three Command methods: `renders_stable_text_json_and_exit_codes`,
`coverage_never_starts_driver_or_writes_run_state`, and
`frozen_checklist_preserves_all_72_obligations`. Require **3** results per lane,
stable exits/digests, no driver or run-state writes, and the frozen 72 bindings.

R-3: One leased tool bootstrap per OS, `bin-c1013-tool/`, explicitly additional to
the test-project rows below. With the resulting DLL, run these exact selections
against a task-owned LF archive of
`f4044e8f7409e5a8e460c8475360cff2d70d035f` on both OSes:

```text
dotnet tools/Antiphon.Checkpoints/bin-c1013-tool/net9.0/Antiphon.Checkpoints.dll coverage --repo-root .antiphon/c1013-inputs --plan docs/superpowers/plans/2026-10-01-card-0891-plan-to-test-coverage-check-plan.md --format json
dotnet tools/Antiphon.Checkpoints/bin-c1013-tool/net9.0/Antiphon.Checkpoints.dll coverage --repo-root .antiphon/c1013-inputs --plan docs/superpowers/plans/2026-09-30-card-0780-retire-temp-date-parse-plan.md --format json
```

Capture stdout bytes, stderr and exit separately using a byte-preserving child
process capture. Preserve raw outputs; compare a separate UTF-8/LF representation
with no added BOM/newline. Require exits **0 / 1**, CARD-0891 **72/72** and the
historical Linux hash above, CARD-0780 **11 findings** (2 MISSING_METHOD,
9 PROSE_UNMAPPED) and its historical hash above. Compare every JSON field and input
digest when a hash differs. Never erase digest fields, use current test sources
for one side, or rebaseline the expected hash. A residual difference is a finding
to diagnose, not permission for an unrelated compatibility repair.

R-4: Bounded Windows Debug probe for a symlink to an in-root regular selected C#
file: coverage exits 0; compare with the direct-file case using its canonical
reported path. Preserve the created link/target identity and exact invocation.
For a non-regular selected project, establish that the candidate is enumerated
and selected, then require exit 2 with no hang under a joined 15-second child
deadline. If no suitable safe fixture is established, report **not qualified**
and ask the caller to disposition the limitation; do not claim metadata-only
or Linux FIFO evidence is native Windows proof. This prerequisite is for
TestDesign/Debug, not an approval to run probes during Plan.

Positive controls proposed for the later Mutation commission: restore AppendLine
and run only the exact V-1 Windows method to its intended digest assertion; remove
one exact fixture LF rule in a fresh CRLF-materialized test checkout and run only
`PlanCoverageGoldenTests/c866_legacy_reports_v1_v4_v5` to its provenance assertion.
TestDesign freezes per-control restoration/evidence. An LF-only mutant run cannot
prove the Windows newline control. Keep these separate from the landed owner's
existing native-handle PCs.

### Proposed checkpoint rows (historical)

Closed ordinary **test** list, proposed for TestDesign. Lane is in Group. Windows
selects CP-1,CP-3; Linux selects CP-2,CP-4. Each lane has one isolated test build
and reuses it only within the same committed S1-S2 group. All rows serial; no
whole-assembly/provider/PTY suite. R-3 tool bootstraps and R-3/R-4 Debug probes are
the explicitly named additional acceptance work above, not hidden TUnit results.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1013-windows/` | windows-checklist-goldens | `/*/*/(PlanCoverageParserTests)\|(PlanCoverageGoldenTests)\|(PlanCoverageCensusTests)/*` | V-1, V-3, R-1 | all 24 results, 0 failed/skipped | 24 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1013-linux/` | linux-checklist-goldens | `/*/*/(PlanCoverageParserTests)\|(PlanCoverageGoldenTests)\|(PlanCoverageCensusTests)/*` | V-1, V-3, R-1 | all 24 results, 0 failed/skipped | 24 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S2 | CP-1 | windows-public-command | `/*/*/PlanCoverageCommandTests/(renders_stable_text_json_and_exit_codes)\|(coverage_never_starts_driver_or_writes_run_state)\|(frozen_checklist_preserves_all_72_obligations)` | V-2, R-2 | all 3 results, 0 failed/skipped | 3 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S2 | CP-2 | linux-public-command | `/*/*/PlanCoverageCommandTests/(renders_stable_text_json_and_exit_codes)\|(coverage_never_starts_driver_or_writes_run_state)\|(frozen_checklist_preserves_all_72_obligations)` | V-2, R-2 | all 3 results, 0 failed/skipped | 3 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

### Cost and execution discipline

Ordinary checkpoint floor: **12 minutes**, 6 per OS, **54** total TUnit results.
Budget a further 6 minutes for the two gated tool bootstraps and 10 minutes for
bounded comparison/native evidence, excluding unavailable-host/privilege waits.
These are estimates, not measurements. No repeat proof or whole Unit run is
commissioned. TestDesign may add an explicitly labeled, method-scoped baseline-red
row on unchanged production; it must account for that build and execution.

Use the checkpoint tool once per OS's committed slice selection, exact source SHA
and `--rows`; use `wait` until exit is not 75. Bootstrap through
`scripts/build-slot.ps1`, forward-slash alternate OutputPath, never a nested lease
around the executor. Exit 4 is not-run, never permission to bypass the gate.
Freeze source during execution, validate clean receipts, retain unedited
CHECKPOINT lines and counts in the stored report, and remove only owned alternate
outputs after all children exit. Code/Review run `scripts/check-evidence-diff.ps1`
over the full task range. Generated JSON/TRX/logs stay ignored.

## Activation and collision order

1. Publish this plan and commission TestDesign. Recheck runner eligibility,
   capacity, source footprint and the reader line before Code. Freeze the native
   Debug fixture/limitation and importer admission of both row selections.
2. Code commits/pushes S1 and S2 before builds; runs the two OS selections at the
   same source SHA and the declared R-3/R-4 acceptance work. Use fresh Windows
   fixture materialization. Any residual native qualification remains explicitly
   pending on CARD-1013 until caller disposition.
3. Separate ordinary Review verifies source, targeted receipts, raw comparisons
   and exact LF attributes. Land through the original Code owner. This tooling
   fix needs **no server, runner or AppHost restart**; consumers advance checkout
   and rebuild the checkpoint tool before expecting the corrected Windows hash.
4. Keep post-land mutation obligations separate, including CARD-1001 PC-1..PC-5
   and variants. A same-input JSON pass is no handle-safety mutation certificate.

Collision snapshot from board-scoped task reads on 2026-10-03:

| Concurrent/related work | Overlap and handling |
|---|---|
| CARD-0959 Code `bd02f8d9`, Dispatched | Runner Codex observation; no planned coverage-reader or fixture edits. Shared build slots only. Do not change runner/auth/configuration. |
| CARD-1011 continuation `b657a1e2`, amended Windows WQ-3 `d7e561a7`, both Succeeded at read | Grok Windows routing/qualification, not this CLI fix. Recheck next continuation and Windows occupancy; do not launch a provider or change its fixtures. |
| CARD-1018 Plan `245a0a09` Succeeded; TestDesign `f8613557` Dispatched | Hostless Grok sign-in wording/tests. No direct file overlap; coordinate any shared testing-owner doc edits. |
| CARD-1020 Plan `76611588` Succeeded | Test-owned PtyHost cleanup. Avoid its process helper/test files and PTY suite; no dependency for pure coverage parsing. |
| CARD-1017 Plan `402bbca2` Succeeded | Worktree cleanup/report preservation. Retained CARD-1001 raw evidence is task-owned and not guaranteed durable; record its identity before cleanup. Do not edit or delete another task's evidence. |
| CARD-1022 Plan `da6e8e47` Dispatched | Removes legacy inbox backend; possible shared `docs/testing-and-build.md` edits and Windows slot pressure. This fix is independent of ConPTY and needs no backend matrix. |
| Landed CARD-1005 / CARD-1006 | Preserve census behavior and the four existing Grok LF attributes. Recheck later coverage-reader edits before applying D-1. |

No scope-registry extension is warranted. TestDesign/Code should pause on an actual
same-file active Code collision, not on the broad fact that another task has tests.

## Plan validation

Read-only source/Git/API inspection and in-memory SHA calculations established
the exact Windows hash prediction, all eight selected source digests, the plan
digest, and all seven LF/CRLF fixture-pin comparisons. No runtime verification is
claimed. A document-only check confirmed six decisions, four eleven-cell rows,
54 result floors and a 12-minute checkpoint sum. The plan's table needs real
importer admission in TestDesign/Code;
document shape validation alone does not substitute for that admission.

--- next stage ---
next: test-design
handoff: Freeze the narrow AppendLine-to-LF fix, seven exact fixture attributes, existing-method digest assertions and four OS checkpoint rows. Exact historical Windows hash is explained by two derived digest fields. Resolve the bounded Windows symlink/non-regular-project qualification procedure before Code; preserve CARD-1001 PCs.
artifact: docs/superpowers/plans/2026-10-03-card-1013-windows-coverage-digests-plan.md

## Verification design

TestDesign freeze, CARD-1013, 2026-10-03, task `e116f6ae`. This section supersedes
only the proposed verification above; D-1 through D-6 and S1-S3 remain the fix
design. The earlier checkpoint heading was demoted because `ExtractSection`
selects the first exact `### Checkpoints`. There is one executable manifest.
This task changes this plan only and performs no build, TUnit, coverage, native
probe or mutation execution. Ordinary Code runs Linux; separate Windows Debug
tasks run the Windows selection at Code's exact final committed SHA C. Review
requires both OS receipts at C; pending Windows evidence is not a Final pass.

### Inspection

Paths below are relative to the repository. Test bodies were read before freezing
assertions, including helpers and the native fixture nearest the Debug probes.

- `tests/Antiphon.Tests/Checkpoints/PlanCoverageParserTests.cs`, all eight bodies,
  especially `maps_checklist_without_erasing_legacy_requirements` |
  inline/external, LF/CRLF, final LF, blank lines, indentation, coordinates,
  strict version/stale mapping and additive obligations -> V-1, R-1, PC-1..PC-7.
- `PlanCoverageCommandTests.cs` in that directory, all ten bodies, including
  `WriteFrozenWorld`, project-link setup and the FIFO child cleanup |
  public JSON, actual file bytes, exit 0/1/2, frozen 72 bindings, no driver/writes
  -> V-2, R-2, PC-5..PC-8; outside-root/FIFO ownership remains CARD-1001.
- `PlanCoverageGoldenTests.cs`, all four bodies; `PlanCoverageFixture.cs`, all
  helpers; `Fixtures/PlanCoverage/provenance.json`, all seven pins and projection
  ranges | raw loader versus projected analysis, seven independent LF checkout
  rules -> V-3, R-1, PC-9..PC-15. Frozen fixture contents are not edited.
- `PlanCoverageCensusTests.cs`, all twelve bodies and its `Make`, `Plan`,
  `Checklist`, `SetRoster`, `Run`, `Snapshot` helpers | inline/external opt-in,
  false/absence, rejected values/conflicts, exact roster versus count,
  qualified aliases, partial declarations, unsupported shapes and read-only
  CLI -> R-1. No census implementation change or new census PC is commissioned.
- `CheckpointTestScope.cs` including `CheckpointTestBase`, and
  `CheckpointTestSupport.cs` (`FakeDriver`, `CheckpointFixtures`) |
  task-owned temporary roots, cleanup and driver substitute -> V-1/V-2/R-2.
  `CheckpointTempUsageTests.cs`'s `CheckpointNamespaceCensusUsageTests` and
  `UsageLibrary`, plus `scripts/lib/checkpoint-usage.ps1` -> unchanged census.
- `PlanCoverageHandleTests.cs`, all seven bodies including argument expansions,
  `SelectedPath`, and joined FIFO child | bounded native read, Linux-only
  hardlink/FIFO gates and pure metadata substitute -> NQ-1/NQ-2 below;
  inherited CARD-1001 PC-1..PC-5 remain separate and pending.
- `tools/Antiphon.Checkpoints/Coverage/PlanCoverageReader.cs` (Read and
  MergeChecklist), `PlanCoverageReport.cs`, analyzer digest construction,
  `CoverageCommand.cs` and `ConfinedFileReader.cs` | normalization before
  reconstruction, raw input hashing, selected project enumeration/canonicalization,
  native no-follow opening and rejection -> V-1/V-2, NQ-1/NQ-2.
- `.gitattributes`, including the initial `* text=auto` and all four exact
  CARD-1006 rules | last matching attribute assignment wins -> V-3.
- `Manifest/PlanTableImporter.cs`, `AfterSelector.cs`, `CheckpointManifest.cs`,
  `ManifestValidator.cs`, Program's Import, relevant `CheckpointImportTests`
  bodies and `docs/testing-and-build.md`'s manifest/runner/build-slot/mutation
  contracts | first section, escaped pipes, build reuse, counts versus time,
  serial/environment and roster-token limitations -> CP-1..CP-4.

Missing setup is explicit: there is no native Windows execution in this Linux
worktree; Windows Debug needs a fresh checkout at C with `core.autocrlf=true`,
existing symlink permission, a managed debugger with source/PDB breakpoints and
process ownership/timeout control. Debug checks these before NQ-2. No machine
privilege setting, Developer Mode, provider login or PTY backend is changed.

Live footprint recheck at 2026-10-03 23:39 UTC used the board-scoped task listing
and each task's detail/goal. `bd02f8d9-52d9-4ba6-a67e-f6325fff27ee` (CARD-0959),
`d422c5a9-e1b4-404d-9d28-438613671c15` (CARD-1011), and
`13149836-be98-48fa-af5e-34b257df9bc5` (CARD-1018) were all Dispatched. Their
reported scopes were null, so scope metadata alone does not prove separation.
Their goals name Codex observation/provider docs, Grok routing/bundle/provider
docs, and hostless sign-in wording respectively; none names our reader, two test
files or seven attributes. Shared host build slots and Windows capacity remain
constraints. Code rechecks footprints before editing, and isolates S2's static
coverage subsection in its own small commit if a testing-owner doc collision
appears. Do not modify their files or runner settings.

The three owners' local branch diffs against origin/master and their working-tree
status were also checked for `tools/Antiphon.Checkpoints`,
`tests/Antiphon.Tests/Checkpoints`, `.gitattributes` and
`docs/testing-and-build.md`: all three produced no overlapping changed paths at
23:49 UTC. This is a point-in-time observation, not a reservation.

`origin/master` was independently checked with `git ls-remote` as
`2d3c582416c5610d72e53df3e790bc114d87ad82`; its census and this start ref's census
both say `selected = 377`. CARD-1005/CARD-1015 are already in the base. This card
adds **zero** `[Test]`, argument, repeat or data-source cases to
`Antiphon.Tests.Checkpoints`; it does **not** bump 377 or edit
`checkpoint-usage.ps1`. Reconcile any later owner's genuine count increment at
integration; never set a newer literal back to 377. No dependency on a pending
coverage/census implementation was found. Do not relabel CARD-1005's static
census as this card's runtime execution count.

### Delivery inventory

None: no new or changed asynchronous delivery path. Coverage synchronously reads
selected files and returns a report to its invoking process. No queue producer,
recipient session, persisted handoff, recovery worker or UserPrompt receipt is
changed. Queue/busy-recipient/crash/enqueue-failure cases are excluded for that
reason. `FakeDriver` in R-2 proves the command does not invoke the execution
pipeline; it cannot prove queue delivery. Debug stdout is evidence of this CLI's
result only, never session receipt. All native safety PCs inherited from
CARD-1001 retain their own ownership; no delivery/recovery guard is introduced.

### Proves it works now

- V-1: canonical derived inline text with raw input provenance | reader unit |
  extend only `PlanCoverageParserTests.maps_checklist_without_erasing_legacy_requirements`
  | retain every existing assertion, then add the literal vectors and matrix below.
- V-2: public JSON carries the canonical checklist hash and correct combined input
  hash | filesystem/CLI adapter |
  extend only `PlanCoverageCommandTests.renders_stable_text_json_and_exit_codes`
  | retain existing exit/order/content-change assertions; assert every row of the
  byte matrix below, with no production hash helper used for expectations.
- V-3: exactly seven golden blobs materialize as LF on both OSes | Git checkout
  plus existing golden method |
  `PlanCoverageGoldenTests.c866_legacy_reports_v1_v4_v5` and `git check-attr` /
  `git ls-files --eol` | all seven raw pins match, with unchanged fixture and
  provenance blobs. The Windows result uses a freshly materialized checkout.

For V-1, append the inline fence after `PlanCoverageFixture.Plan()`'s checkpoint
row, within its Verification design section; do not shift the existing row at
line 5. Use explicit `\n` strings, never `Environment.NewLine`, `AppendLine`, or
platform-dependent source raw-string line endings for the expected vectors.
The exact base JSON is `{\n  "version": 1,\n  "items": []\n}\n` (escaped here).
An empty checklist still leaves the fixture's `Demo.Check` and `target-label`
obligations intact. Pin their original coordinates `(5,10)` and `(5,33)`, names,
kind and ID after reading each inline plan, and assert `Invalid == false`.
Keep the existing nonempty additive/mapped checklist assertions separately.

| Inline payload | LF SHA-256 literal | Required assertion label |
|---|---|---|
| Base, including final LF | `da6fa0fb6dc64a3faf1f81f830b8696ccf02b9c654135111429090fa240870f8` | `c1013-inline-lf` |
| Base plus one blank line (two final LFs) | `3ba91a5d353e5eaab71a23740fd938f42e9cd1996fe809aded37f4b2951bcc33` | `c1013-inline-blank-line` |
| Base with four spaces before `"version"` instead of two | `85c491e24a155122596d00155b48f01c095a8b8968b71985736a69155256feb1` | `c1013-inline-whitespace` |

For **each** payload, read an LF plan and its CRLF counterpart and require the
same payload hash, including the final LF. Require the two plan hashes to equal
independent SHA256/UTF8 of their respective original strings and to differ
(`c1013-raw-plan`). For the base payload also read an external LF checklist and
external CRLF checklist with no inline fence. Pin LF to the base literal and CRLF
to `79f2f76348e92b2903f7a7e371d28a1e951cf465a1ec68a1f1ee2221a871a200`
(`c1013-external-lf`, `c1013-external-crlf`). Run these external variants with
both LF and CRLF plans. Include the existing strict invalid/version/stale cases
unchanged; Census R-1 already covers inline/external conflict and duplicate fences.

For V-2 use a new TempDir world **inside the existing method**. Write explicit
UTF-8 without BOM and LF to `plan.md`, `tests/Sample/Demo.cs`, and, for external
mode, `checklist.json`. Source is the valid single-class fixture with a real
`ShouldBe(1, "target-label")` call and a final `\n`; it is analyzed, never run.
Construct two plan strings, one ending with the base inline fence, one without a
fence using the equivalent external base checklist. For inline mode cover all
2 plan-ending x 2 source-ending combinations. For external mode cover all
2 plan-ending x 2 source-ending x 2 checklist-ending combinations: **12 reports**,
not 12 TUnit executions. Preserve the method's original exit-1 and exit-0 examples.

For every report assert exit 0, `invalid=false`, schema 1, clean summary, no
diagnostics, exactly the two original obligations matched, canonical source path
`tests/Sample/Demo.cs`, and the independently calculated raw `planSha256` and
source `sha256` (`c1013-command-raw-plan`, `c1013-command-raw-source`). Assert
`checklistSha256` against the pinned LF/CRLF literal (`c1013-command-checklist`).
Calculate the independent combined expectation using SHA256/UTF8 over
`expectedPlanHash + "\n" + expectedChecklistHash + "\n" +
"tests/Sample/Demo.cs\0" + expectedSourceHash`, using **local input strings and
literal checklist expectations**, never hashes obtained from the actual report.
Assert equality with `inputsSha256` (`c1013-command-inputs`). Repeat one identical
LF invocation and require identical complete JSON. Assert differing raw-byte
variants change their raw field and combined digest; inline versus external
plans have different raw plan bytes, so their complete reports must not be compared
for equality. The existing changed-source assertion still covers semantic changes.

V-3 adds these **seven exact lines**, after `* text=auto` and preferably beside
the CARD-1006 exact pins. Check no later rule overrides them. No new wildcard,
fixture-loader normalization, pin update or blanket renormalization is allowed:

```gitattributes
tests/Antiphon.Tests/Checkpoints/Fixtures/PlanCoverage/c866-plan.md.txt text eol=lf
tests/Antiphon.Tests/Checkpoints/Fixtures/PlanCoverage/c866-tests.cs.txt text eol=lf
tests/Antiphon.Tests/Checkpoints/Fixtures/PlanCoverage/c866-fixed-tests.cs.txt text eol=lf
tests/Antiphon.Tests/Checkpoints/Fixtures/PlanCoverage/c835-plan.md.txt text eol=lf
tests/Antiphon.Tests/Checkpoints/Fixtures/PlanCoverage/c835-state.cs.txt text eol=lf
tests/Antiphon.Tests/Checkpoints/Fixtures/PlanCoverage/c835-script.cs.txt text eol=lf
tests/Antiphon.Tests/Checkpoints/Fixtures/PlanCoverage/c835-approval.cs.txt text eol=lf
```

For each named path require `git check-attr text eol -- <path>` to return `text:
set`, `eol: lf`; `git ls-files --eol -- <path>` must show `i/lf w/lf` and
`attr/text eol=lf`. Compare actual UTF-8 bytes with each `provenance.json` pin;
record all seven path/hash pairs. Create the Windows task checkout with
`git -c core.autocrlf=true worktree add --detach <new-task-owned-path> C` before
any file materialization (C is Code's reported full SHA). If dispatch already
materialized files under unknown Git settings, use a fresh task-owned checkout;
changing the config of an old checkout alone proves nothing. Do not change the
shared repository's persistent configuration. Compare the seven fixture Git blobs
and provenance against the task base: all must be byte-identical.

### Guards the regression

- R-1: parser, raw goldens and CARD-1005 opt-in/opt-out stay compatible |
  all bodies of Parser (8), Golden (4), Census (12) | exactly **24 executed**,
  zero failed/skipped per OS; preserve the existing labels and census assertions.
- R-2: public API result stability, frozen method bindings and inert operation |
  the three exact Command methods in CP-3/CP-4 | exactly **3 executed** per OS;
  `coverage-public-cli`, `coverage-driver-zero`, `coverage-no-side-effects`,
  `coverage-frozen-count-clean` (72), and all V-2 labels remain decisive.

Separate acceptance obligations **AQ-1** and **NQ-1/NQ-2** follow. These are named,
priced Debug/CLI acceptance work outside the four ordinary TUnit rows, not extra
V/R test scope hidden from the checkpoint manifest. They finalize the proposed
R-3/R-4 acceptance procedures without changing the fix. Four CP rows' union is
all ordinary V-1..V-3/R-1..R-2 scope.

AQ-1: preserve the two historical full-report CLI results. On each OS build the
tool once through the host slot into `bin-c1013-tool/` (default Debug, retaining
PDB), then use that DLL for the two exact CLI selections already printed in the
Plan-stage proposal. Bootstrap command from the checkout root:

```text
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1013-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1013-tool/ --nologo
```

Read the actual OutputPath location: this repository's build may place the DLL
directly in `bin-c1013-tool/`, rather than its `net9.0/` subdirectory. Use the
produced DLL and record its SHA-256 and full path; never execute a stale default
bin. Export the **same** historical Git tree at
`f4044e8f7409e5a8e460c8475360cff2d70d035f` to task-owned
`.antiphon/c1013-inputs` on both OSes. Capture bytes without PowerShell text
redirection, retain stdout/stderr/exit separately, and compare only a separately
LF-normalized UTF-8 rendering (no inserted BOM/newline). Require CARD-0891 exit 0,
72/72 and hash `7357550d7d41e3273950d8e596354bbbf99d143282758b61965898bcbacd1e0f`;
CARD-0780 exit 1, 2 MISSING_METHOD plus 9 PROSE_UNMAPPED and hash
`1317845a9f91bbdac02b49760f1fd312f083ae12c58afebdea435ca19b087ab1`.
Retain raw evidence. Disagreeing fields are diagnosed, not removed or rebaselined.

Native Windows qualification uses this bounded scratch fixture, modeled on
`PlanCoverageFixture.WriteWorld`: `plan.md` is the explicit-LF `Plan()` string,
`tests/Sample/Demo.cs` is the valid default source, and `tests/Sample/Probe.csproj`
is `<Project />`. It is **not** a compiled project or provider session. All files
are within a task-owned root Q, with one NTFS link each. The public command is
`dotnet <C-built-tool.dll> coverage --repo-root Q --plan plan.md --format json`.
Own the process by PID plus start identity, capture both streams concurrently,
join exit and drain output. At a 15-second execution deadline kill only that
owned child tree, join it and report timeout as failure, never exit-2 success.

NQ-1: save the direct fixture's exit-0 JSON. Move `Demo.cs` to `Actual.cs`, create
`Demo.cs` as a **file** symlink to the in-root `Actual.cs`, record LinkTarget,
resolved full path and file attributes, and invoke the same command. Require
exit 0, no diagnostics, source path exactly `tests/Sample/Actual.cs` and its
original raw source hash. Remove only the symlink and invoke again with Actual.cs
as the sole direct source; the complete report must equal the symlink case.
The old direct Demo.cs report differs in canonical path and combined digest;
do not require its full equality. Failure to create the link is **not qualified**,
not a skip accepted as green. Restore a regular Demo.cs fixture before NQ-2.

NQ-2: deterministically exercise the native non-regular **selected project**
boundary on Windows using a managed debugger; no production seam is added.
A normal static symlink is resolved before opening, and an ordinary directory is
not enumerated as a project. Neither proves this boundary. Use the C-built Debug
DLL/PDB and the actual public command above under a debugger supporting source
breakpoints. Freeze the following stop/resume procedure:

1. Start with regular `Probe.csproj` and an in-root regular `Target.xml` containing
   `<Project />`. Set a breakpoint at `ConfinedFileReader.OpenWindows` on the
   `var handle = CreateFile(...)` statement. Continue past other selected files
   until local `path` is Q's exact `tests/Sample/Probe.csproj`. Record the call
   stack containing `CoverageCommand.ProjectSources`, its `read` delegate and
   `ConfinedFileReader.Read`. This proves enumeration and canonical selection
   have already occurred; a breakpoint before `Confined` is insufficient.
2. While the owned target is stopped, use a separate owned shell to move
   `Probe.csproj` to `Original.xml`, then call
   `[System.IO.File]::CreateSymbolicLink(<absolute Probe.csproj>, <absolute Target.xml>)`.
   Record that Probe is now a file reparse point, still in root, with LinkTarget
   Target.xml. Do not evaluate target-process methods or change its variables.
3. Disable the breakpoint, resume, start the 15-second execution deadline and
   await exit/output. Require exit **2**, `invalid=true`, summary `invalid`,
   diagnostic `INPUT_INVALID`, `testPath=tests/Sample/Probe.csproj`, and detail
   **`selected file is not regular`**. The unchanged `OPEN_REPARSE_POINT` open
   must expose the reparse attribute to `InspectWindows`/`Refusal`, rather than
   read Target.xml. An unreadable-path/escape error, debugger fault, skipped
   directory, missed breakpoint or timeout does not meet this criterion.
4. Remove only the created link, restore the original regular project, stop/join
   the debugger and all owned children, and run the command normally. Require
   exit 0 again. Record tested C, OS/filesystem, debugger/version, PDB binding,
   exact argv, call stack/local path, link metadata, all three exits and stdout.

Bound the entire breakpoint/setup phase to **5 minutes**, separate from the
15-second resumed child deadline, and join/clean up on every failure. This proves
a real Windows selected-project replacement reaches the opened-handle guard;
it is not a native FIFO test. It is a Debug acceptance probe, not a post-land PC
or an automated TUnit receipt. If the host lacks a managed debugger or symlink
permission, do not manufacture a metadata-only pass: report NQ-2 not qualified
and route that native acceptance seam to **Plan** for a bounded alternative.
Code may implement the independent digest fix; CARD-1013's native qualification
cannot be reported complete until these observations exist. The prior hardlink
plan/source and oversize-source observations are retained, with their historical
SHA, because this card changes no native reader behavior. Legacy inbox ConPTY,
ModernConPty, PTY providers and their version matrix are irrelevant to this CLI.

### Guard inventory

Safety-critical here means the changed or explicitly preserved digest/provenance
assertions. Independently bypassable newline, whitespace, raw-input and exact
attribute rules are separate guards. No newly changed delivery guard exists.
Unchanged handle/link/count/cap/confinement guards and their inherited controls
remain CARD-1001 PC-1..PC-5; NQ acceptance does not discharge them. Census guard
mutation remains CARD-1005. This freeze does not reassign those owners' inventories.

| Guard | Plan reference and invariant | Positive control |
|---|---|---|
| G-1 | D-1: inline line separator is LF on Windows as well as Linux | PC-1 |
| G-2 | D-1: final inline newline is retained | PC-2 |
| G-3 | D-1: blank inline lines are retained | PC-3 |
| G-4 | D-1: per-line whitespace is retained | PC-4 |
| G-5 | D-2: original plan line endings still affect raw plan provenance | PC-5 |
| G-6 | D-2: original source line endings still affect raw source provenance | PC-6 |
| G-7 | D-2: external checklist line endings remain raw | PC-7 |
| G-8 | D-2: combined input digest includes the checklist digest | PC-8 |
| G-9 | D-3: c866-plan.md.txt exact LF checkout rule | PC-9 |
| G-10 | D-3: c866-tests.cs.txt exact LF checkout rule | PC-10 |
| G-11 | D-3: c866-fixed-tests.cs.txt exact LF checkout rule | PC-11 |
| G-12 | D-3: c835-plan.md.txt exact LF checkout rule | PC-12 |
| G-13 | D-3: c835-state.cs.txt exact LF checkout rule | PC-13 |
| G-14 | D-3: c835-script.cs.txt exact LF checkout rule | PC-14 |
| G-15 | D-3: c835-approval.cs.txt exact LF checkout rule | PC-15 |

### Positive controls

All are **post-land SourceLanding Mutation** on the exact published L, with
baseline/compiling defect/intended red/restore/fresh-build green evidence. Code
runs ordinary V/R; Review judges this design and evidence before land. No PC is
claimed executed here. Use local inherited children and the external evidence
root; never commit/push a sourced snapshot. Each cycle executes one exact method,
`-MinExecuted 1`, and must yield one executed result, not a fixture/build error.
Do not batch the seven attribute mutants: the shared loop stops at the first bad
pin, so a batch would hide independent failures. PC-1 requires native Windows;
use Windows for all controls to reuse setup, serially.

Exact method filters (aliases below are explanatory, not filters to pass):

- P = `/*/*/PlanCoverageParserTests/maps_checklist_without_erasing_legacy_requirements`
- C = `/*/*/PlanCoverageCommandTests/renders_stable_text_json_and_exit_codes`
- F = `/*/*/PlanCoverageGoldenTests/c866_legacy_reports_v1_v4_v5`

| PC | Compiling defect / independent break | Exact method and decisive red assertion |
|---|---|---|
| PC-1 | Break G-1: replace fixed `inline.Append(line).Append('\n')` with `inline.AppendLine(line)` | P, `c1013-inline-lf`: expected da6fa0fb..., actual 79f2f763... on Windows |
| PC-2 | Break G-2: change `inlineJson = inline.ToString()` to `inlineJson = inline.ToString().TrimEnd('\n')` | P, `c1013-inline-lf` differs from the pinned final-LF digest |
| PC-3 | Break G-3: append only when `checklistFence && line.Length != 0` | P, `c1013-inline-blank-line` expects 3ba91a5d..., receives base digest |
| PC-4 | Break G-4: change the fixed append to `inline.Append(line.Trim()).Append('\n')` | P, first `c1013-inline-lf` digest mismatch (indented JSON remains valid) |
| PC-5 | Break G-5: only in Reader's report initialization hash `text.Replace("\r\n", "\n")` instead of `text` | P, `c1013-raw-plan` on the CRLF plan |
| PC-6 | Break G-6: only in Analyzer's source selection hash `s.Text.Replace("\r\n", "\n")` | C, `c1013-command-raw-source` on the CRLF source |
| PC-7 | Break G-7: set `var json = checklist?.Replace("\r\n", "\n") ?? inlineJson` | P, `c1013-external-crlf` expects 79f2f763..., receives da6fa0fb... |
| PC-8 | Break G-8: replace only the checklist contribution in Analyzer's InputsSha256 preimage with `""` | C, `c1013-command-inputs` differs from independent complete preimage hash |
| PC-9 | Break G-9: remove only c866-plan.md.txt's exact attribute; rematerialize that path as below | F, `coverage-raw-provenance` for c866-plan.md.txt |
| PC-10 | Break G-10: remove only c866-tests.cs.txt's exact attribute; rematerialize that path | F, `coverage-raw-provenance` for c866-tests.cs.txt |
| PC-11 | Break G-11: remove only c866-fixed-tests.cs.txt's exact attribute; rematerialize that path | F, `coverage-raw-provenance` for c866-fixed-tests.cs.txt |
| PC-12 | Break G-12: remove only c835-plan.md.txt's exact attribute; rematerialize that path | F, `coverage-raw-provenance` for c835-plan.md.txt |
| PC-13 | Break G-13: remove only c835-state.cs.txt's exact attribute; rematerialize that path | F, `coverage-raw-provenance` for c835-state.cs.txt |
| PC-14 | Break G-14: remove only c835-script.cs.txt's exact attribute; rematerialize that path | F, `coverage-raw-provenance` for c835-script.cs.txt |
| PC-15 | Break G-15: remove only c835-approval.cs.txt's exact attribute; rematerialize that path | F, `coverage-raw-provenance` for c835-approval.cs.txt |

For PC-9..PC-15 use the actual managed snapshot, not an external clone/executor.
Save the original `.gitattributes` and fixture bytes externally. With just the
one rule removed, force that exact indexed fixture through
`git -c core.autocrlf=true -c core.eol=crlf checkout-index --force -- <exact-path>`.
First verify `git check-attr` reports no LF eol rule, `git ls-files --eol` reports
`w/crlf`, its raw hash differs from its unchanged provenance pin, and the other
six pins still match. This configuration/data defect leaves C# compilation valid.
Run F, retain the failing actual/expected hash and identify the one fixture by
those pre-run hashes. Restore `.gitattributes` exactly, rematerialize that same
path with the restored LF rule, and require all seven original raw hashes before
fresh-build green. The loop's existing provenance assertion is not replaced.
A materialization that remains LF is an invalid control, not a survivor.

Mutation uses the copied unmodified `run-checkpoint.ps1` driver as specified in
`docs/testing-and-build.md`, with fresh `bin-c1013-pcN-<phase>/` outputs, one slot
per invocation, exact P/C/F literal, and external per-PC receipts. Keep source
frozen during each run, preserve expected assertion text/nonzero counts, await
all children and restore source/index plus fixture bytes before completion.
Zero tests, wrong assertion, noncompiling edit or Linux-only PC-1 is not red.

### Out of scope

- No hash implementation normalization, JSON schema change, new CLI flags,
  provenance repinning or fixture-content edits; these would invalidate the
  narrow D-1/D-2 contract and frozen byte comparisons.
- No new tests/files in the namespace, whole Unit/assembly run, provider launch,
  live broker, session delivery, PTY suite or backend matrix. Existing parser,
  golden, census and three Command bodies bound the ordinary regression surface.
- No all-encoding/BOM matrix or unsupported OS qualification: decoding policy,
  supported native platforms and reader limits are unchanged. LF/CRLF cross
  products exhaust the changed newline boundary; LF plus blank/whitespace vectors
  protect reconstruction without inventing new decoding behavior.
- No rerun of CARD-1001 or CARD-1005 mutation inventories; all inherited native
  controls/variants remain pending with those owners. NQ-2 is real native Debug
  evidence but is not a replacement SourceLanding mutation certificate.

### Checkpoints

Closed ordinary list. CP-2 then CP-4 are Code's Linux selection. CP-1 then CP-3
are a separate Windows Debug selection at C, in one fresh checkout/run so build
reuse is legitimate. A separate task per row must instead build its own output;
it cannot claim reuse of another task's unqualified binary. All rows are serial
and set the in-host test parallelism to 1. `Min` counts executed TUnit cases;
internal digest matrices and seven provenance comparisons are not extra cases.
No new test cases: floors are 24/24/3/3, **54 overall**.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1013-windows/` | windows-checklist-goldens | `/*/*/(PlanCoverageParserTests)\|(PlanCoverageGoldenTests)\|(PlanCoverageCensusTests)/*` | V-1, V-3, R-1 | exactly 24 executed, 0 failed/skipped | 24 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1013-linux/` | linux-checklist-goldens | `/*/*/(PlanCoverageParserTests)\|(PlanCoverageGoldenTests)\|(PlanCoverageCensusTests)/*` | V-1, V-3, R-1 | exactly 24 executed, 0 failed/skipped | 24 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S2 | CP-1 | windows-public-command | `/*/*/PlanCoverageCommandTests/(renders_stable_text_json_and_exit_codes)\|(coverage_never_starts_driver_or_writes_run_state)\|(frozen_checklist_preserves_all_72_obligations)` | V-2, R-2 | exactly 3 executed, 0 failed/skipped | 3 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S2 | CP-2 | linux-public-command | `/*/*/PlanCoverageCommandTests/(renders_stable_text_json_and_exit_codes)\|(coverage_never_starts_driver_or_writes_run_state)\|(frozen_checklist_preserves_all_72_obligations)` | V-2, R-2 | exactly 3 executed, 0 failed/skipped | 3 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

The importer unescapes literal pipes and expands S1-S2 to `[S1,S2]`; CP-3/4 share
the respective earlier build and identical After. It does **not** enforce Group
as an OS selector: never run all four rows on one host. Invoke the checkpoint
tool's `run --plan <this-plan> --rows CP-2,CP-4 --expected-source-sha C` on Linux,
and `--rows CP-1,CP-3` with the same other arguments on Windows. Use `wait` until
the final exit is not 75; own/join every executor. If a tool bootstrap is needed,
reuse the named AQ-1 bootstrap. Code must freeze S1-S2 and push before these runs.

Importer `RosterTokens` returns **empty** Expect for the exact non-star combined
class filter and only `PlanCoverageCommandTests` for the method union. Therefore
`Min` alone cannot certify the roster: preserve and inspect the emitted EXECUTED
list/TRX and require exactly the 8 Parser, 4 Golden, 12 Census bodies read above
and exactly the three named Command methods. Do not quietly add `*` and expand
the scope to get automatic Expect tokens. Validate structured source receipts at
C, with dirty=0, sourceState=clean and buildSource=verified, retaining unedited
CHECKPOINT lines; report skipped/failed/zero counts honestly. Code/Review run
`check-evidence-diff.ps1` over the full candidate range. Clean task-owned alternate
outputs with the documented guarded cleanup after owned processes are joined.

Read-only admission was performed with the existing tool at
`/work/worktrees/task-d422c5a9/tools/Antiphon.Checkpoints/bin-c1011c-tool/Antiphon.Checkpoints.dll`:
`dotnet <dll> import --repo-root /work/worktrees/task-e116f6ae --plan
 docs/superpowers/plans/2026-10-03-card-1013-windows-coverage-digests-plan.md --out
 /work/worktrees/task-e116f6ae/.antiphon/c1013-import.yaml`.
Its Program Import, PlanTableImporter and ManifestLoader source matched this
checkout byte-for-byte when inspected; binary build provenance is not a test
receipt. Import exited 0 with four rows, two builds, counts 24/24/3/3, serial true
and TUNIT_MAX_PARALLEL_TESTS=1. The generated YAML stays ignored. The frozen final
table was re-imported successfully. Direct calls to that assembly's real
`PlanTableImporter.ImportFile` with `isWindows=false` and `isWindows=true`, then
`ManifestValidator.Validate`, both succeeded: exit 0, zero warnings, four rows,
two builds, 24/24/3/3 floors, 12 summed minutes, four serial rows and correct
Windows/Windows and Linux/Linux build reuse. No code was compiled to make these
calls. This admission is not a build or test run and claims no production behavior
pass. A separate structural check found exactly one checkpoint section and 15
guard/control rows with 15 unique mappings.

### Cost

All figures are **estimated**, except this task's observed zero builds/tests.

- Ordinary V/R floor = CP-1 5 + CP-2 5 + CP-3 1 + CP-4 1 = **12 minutes**,
  including one test-project build per OS; **6 minutes Code/Linux** plus
  **6 minutes Windows Debug**. Filters are the two literal groups above.
- Additional setup/build = **10 minutes**: fresh LF/CRLF checkout and hash setup
  4, two AQ-1 gated tool bootstraps 3 each. Additional acceptance = **12 minutes**:
  paired AQ-1 comparisons 4; NQ-1 2; NQ-2 debugger stop/swap/resume/restore 6.
  Host queue and missing-permission waits are excluded and reported separately.
- Mutation floor = **180 minutes**, 15 independent PCs at **12 minutes each**:
  one exact-method baseline build/run 4, compiling defect plus red build/run 4,
  restoration plus fresh green build/run 4. PC-1..PC-5 and PC-7 use P,
  PC-6/PC-8 use C, PC-9..PC-15 use F. No whole-class mutation runs or amortized
  attribute mutants. Budget a further **5 minutes** for snapshot discovery,
  driver copy and final restoration evidence: Mutation dispatch floor **185**.
- Total verification budget = setup/build **10** + ordinary V/R **12** +
  additional acceptance **12** + Mutation cycles **180** + Mutation setup **5**
  = **219 minutes**. Code authoring, Review time and queue waits are additional.
  No extra ordinary pre-fix/red build is commissioned: there are no new methods;
  compiling-defect proof is the independent post-land stage.
- Savings: same-SHA build reuse saves **two** test-project builds across CP-3/4,
  estimated **8 minutes** versus building every row independently. Bounded
  ordinary filters avoid an otherwise unnecessary ~25.5-minute Antiphon.Tests
  assembly run. Mutation batching savings are **0** because controls share
  methods/files or provenance-loop failure order; independence requires isolation.

Before handoff: touched bodies/helpers read; **guards=15, mapped=15, missing=0,
duplicate PC maps=0**. Every PC has an exact method, valid defect and decisive
assertion; all 15 are executable after Code lands and Windows prerequisites are
present. Native setup unavailability must remain an explicit not-qualified
result. No build, test, native qualification or PC was run by TestDesign.

--- next stage ---
next: code
handoff: Implement D-1..D-4 and S2 from this frozen plan with no new test cases/census bump. Run Linux CP-2/CP-4; return final C for separate Windows CP-1/CP-3 and AQ/NQ Debug at that exact SHA. Preserve all 15 independent post-land PCs and inherited CARD-1001 obligations; unavailable native debugger/symlink setup remains not qualified.
artifact: docs/superpowers/plans/2026-10-03-card-1013-windows-coverage-digests-plan.md
