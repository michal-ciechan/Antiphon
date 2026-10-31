# Stopped-run seed ordering

Post-green witness repair: initial run 20261031-100310-d7e1 at
be134adf2c33b328a43fa15c4349dc061d9e0f76 passed 8/8 in 9m22s, one isolated build.
An extra public-wrapper acknowledgement mutant survived: the fixture read ParamBlock
from FunctionDefinitionAst rather than Body, creating an unbound fake that inherited
wrapper variables. Require the actual parameter block and use Body.ParamBlock.
No production code changes. Re-run the closed eight-row selection from one fresh
bin-f6-seed-green2/ build so final receipts share the repaired harness source.
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
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-f6-seed-green2/` | seed-readonly | `/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/BuilderReadOnly` | source bytes/mtimes and completion | exact method | 1 | 8 | true | TEMP=/tmp |
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
