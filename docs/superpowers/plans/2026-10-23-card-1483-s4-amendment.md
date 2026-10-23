# CARD-1483 S4 verification amendment

Owner/landing task: 6e80fc7e-008b-4c02-af99-8b6ff3ef8dc1. Clean master base:
eb6101b1a0eb8b7542cd631f8064c6ae0671733c. Scope is D-6 of the original plan:
record evaluation filesystem probes and independently compare a canary evaluation.
No script integration (S5), pin regeneration, or target allowance is added.

The complete affected LandGraphEvaluationTests class has 30 executions: 20 inherited,
five V-11 probe variants, four V-12 direct/escaped/condition reads and V-13 real tree.
Every V-1..V-13 and inherited V-1a/V-1b/V-3a runs. No ordinary R-n rows exist.
Unit and whole suites are deferred explicitly by the brief (CARD-1466 known base reds).
V-14..V-16 and CARD-1498 remain S5. CARD-1504 evaluation-time docs Conditions are S4;
its pinned target cases remain protected by inherited S3 tests.

Unlisted runs: slot-gated checkpoint-tool bootstrap and the same CP-4 selection on
committed red tests before implementation, required to prove the defect witnesses.
The final CP-4 runs after the implementation/report commit, with exact expected SHA.
Generate/check the real manifest with the audit script, never by hand.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c1483-s4/` | c1483-s4 | `/*/*/LandGraphEvaluationTests/(C1483_*)` | V-1..V-13, V-1a V-1b V-3a | exactly 30 executed, 0 failed/skipped | 30 | 18 |

### Cost

One isolated build and full affected class per committed group, approximately 18 minutes.
No full-assembly run, repeat-proof or additional class is needed. Every deliberate
positive control remains pending for method-scoped SourceLanding Mutation.

### D-4 / D-6 compatibility established by the first implementation run

MSBuild's glob matcher calls DirectoryExists even for the required safe JSON glob.
Retain the S2 allowance by delegating only directory/existence/enumeration traversal
with a verified Microsoft.Build FileMatcher frame to the unchanged D-4 glob policy.
Condition function frames forbid this exception; unknown provenance refuses. No
path-based whitelist is added. V-11 now combines the safe JSON glob with each unsafe
Exists probe, so the glob cannot authorize an explicit probe on the same directory.
The existing V-5 matrix still refuses Markdown, wildcard extensions and ignored excludes.

The canary is a sibling of the source repository: MSBuild enumerates ancestor
.globalconfig candidates, and a /tmp copy of a /work checkout changes those candidates
without any documentation dependency. No item/property category is excluded from the
comparison. The sibling preserves the same ancestors; lack of write access fails closed.
Established item/import/glob refusals take diagnostic precedence over probe refusals;
probe checks remain decisive whenever those prior checks would approve.
