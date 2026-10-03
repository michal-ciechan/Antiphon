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

## Verification design

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

### Checkpoints

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
