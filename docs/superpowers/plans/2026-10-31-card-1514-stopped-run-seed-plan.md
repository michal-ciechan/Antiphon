# Stopped-run seed ordering

Post-green witness repair: initial run 20261031-100310-d7e1 at
be134adf2c33b328a43fa15c4349dc061d9e0f76 passed 8/8 in 9m22s, one isolated build.
An extra public-wrapper acknowledgement mutant survived: the fixture read ParamBlock
from FunctionDefinitionAst rather than Body, creating an unbound fake that inherited
wrapper variables. Require the actual parameter block and use Body.ParamBlock.
No production code changes. Re-run the closed eight-row selection from one fresh
bin-f6-seed-green4/ build so final receipts share the repaired harness source.
The extra cost is explicitly retained (initial actual 9m22s plus final provisional
15m, not a hidden retry). Two added direct guard diagnostics bind public-wrapper
forwarding and age rejection, with byte restoration and the same cases green.

Commissioned task f6aabaef extends the failed-first plan at e805b7848ae1cc1fe00c9d6f7ccb6e21b0ababc3.
Synthetic portable tests only. Never operate the live run or read desktop files.

S1 commits fixtures and fail-capable tests before implementation. Interpreter RED diagnostics
are additional listed work, with one narrow case per guard. S2 adds the read-only builder,
acknowledged source-candidate ingress and mapping to canonical new chunks. S3 verifies eight
exact methods from one isolated build, plus real land graph -Check, without -Update.
The existing CP-6 script remains untouched because another commissioned task edits it.

The seed is an ordering hint with source candidate/run/SHA, observation time, failed
class+method identities and completed/unfinished membership. It grants no execution credit.
Missing acknowledgement, wrong candidate acknowledgement, bad hash, stale data or wholly
unmappable history restores the complete original order. Resolved failures survive an
unresolved old class. Unknown timestamps retain canonical order; known timestamps sort oldest first.

Missing-plan-row refinement: a retained antiphon-all-group-105.trx has a real failed
identity but no matching historical group row. The builder must read it and preserve its
class identity; the current plan leaves that class unresolved while retaining the other
five mapped failures. BuilderReadOnly, FailedFirstMapping and TimeoutIsFailure are
committed RED before the production fix. Second checkpoint group 20261031-101758-8ae1
at da15833114c7dfdb31c89f6d826485cb5b95bf6f passed 8/8 in 7m25s (one build), closing
the wrapper-witness repair. That historical cost is retained. The final group uses a
third fresh output, bin-f6-seed-green4/, one build shared by all eight exact rows.

Whole-suite refinement: a legitimate plan row can have classes=[]; retain observed
class membership from its TRX. BuilderReadOnly was committed RED at 753e048d0 and
reproduced seed-trx-class-unplanned before the one-line production fallback. The
missing-plan-row RED group was 0/3; its repaired probe was 4/4. Final expanded
fixture probe was 8/8 in a 97s lease before this final isolated checkpoint build.
The two historical checkpoint groups remain 9m22s and 7m25s; final ordinary
estimate remains 15m excluding admission. No required timeout was widened.

Case-identity refinement: group 105 uses Synthetic.antiphon.red while a mapped
failure uses Synthetic.antiphon.Red. Preserve both identities and leave only the
lower-case class unresolved. Commit/run FailedFirstMapping RED before making
Group-Object case-sensitive. Run 20261031-103312-2cff at 3070e34816 was stopped
through the checkpoint tool during its build, before source edits; no test ran,
no receipt or green credit is claimed. It had acquired a slot after 15s admission;
its stopped state retains provisional budget data only. Exact-output process
census was empty after the stop. Final output is a fresh bin-f6-seed-green4/.

## Verification design

Seven guard mutants are separately run and restored against the exact named portable case:
failed partition -> FailedFirstMapping; acknowledgement -> AcknowledgementRequired;
hash -> HashMustVerify; source write -> BuilderReadOnly; chunk drop -> ScheduleConservesAllChunks;
Timeout as pass -> TimeoutIsFailure; coordinator PreviousRun forwarding -> ScheduleConservesAllChunks. These diagnostics are not post-land Mutation credit.
Additional fallback case binds age and no-map behavior. Permutation trials use 64 fixed random
seeds; the actual prepared-schedule boundary launches every chunk and observes new red work.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-f6-seed-green4/` | seed-readonly | `/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/BuilderReadOnly` | source bytes/mtimes and completion | exact method | 1 | 8 | true | TEMP=/tmp |
| CP-2 | S2 | `CP-1` | seed-order | `/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/FailedFirstMapping` | failed/new/passed mapping | exact method | 1 | 1 | true | TEMP=/tmp |
| CP-3 | S2 | `CP-1` | seed-ack | `/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/AcknowledgementRequired` | explicit source acknowledgement | exact method | 1 | 1 | true | TEMP=/tmp |
| CP-4 | S2 | `CP-1` | seed-hash | `/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/HashMustVerify` | tampered payload fallback | exact method | 1 | 1 | true | TEMP=/tmp |
| CP-5 | S2 | `CP-1` | seed-timeout | `/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/TimeoutIsFailure` | four failure outcomes | exact method | 1 | 1 | true | TEMP=/tmp |
| CP-6 | S2 | `CP-1` | seed-permutation | `/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/ScheduleConservesAllChunks` | permutation and execution | exact method | 1 | 1 | true | TEMP=/tmp |
| CP-7 | S2 | `CP-1` | seed-fallback | `/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/InvalidSeedFallsBack` | stale/unmapped original order | exact method | 1 | 1 | true | TEMP=/tmp |
| CP-8 | S2 | `CP-1` | seed-hops | `/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/PublicHopsForwardAcknowledgement` | public wrappers, sync and executor hop | exact method | 1 | 1 | true | TEMP=/tmp |

## Performance budget

One build (provisional 6-8 minutes from S3 build samples), eight serial tiny fixtures,
ordinary estimate 15 minutes excluding broker admission (up to 45 minutes per lease).
No setup duplication of a database, host or container: none is used. Each method owns one
small file fixture. Harness records body wall and unavailable attribution as unknown;
checkpoint records build, startup, tests, teardown and lease spans. Per-method 90s child /
120s outer bound provisional until first measurement (CARD-1514/CARD-1404); collect next
three ordinary receipts, no sample-only reruns. Row/build 12m, execution 24m, hard wall 65m.
No definition file change is intended. Bootstrap checkpoint tool is an explicitly listed
additional gated isolated build; copy audit binaries from the one checkpoint build before
its cleanup for the real graph check. Generated evidence remains ignored.
