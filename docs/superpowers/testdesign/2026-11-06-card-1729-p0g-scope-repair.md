# CARD-1729 P0-G round 2 scope repair

Task a3555767; original landing owner 742b9c5c-9e25-4fac-910e-799803e461c9.
Base 89bbb05617900d963a99feeabc5177ccefba56bc. Review d5fd03f4 B-3.
Extends the frozen Phase 0 TestDesign L5/L6 bodies; all nine method names stay.

Invariant: a value declaration suppresses a bare expression name only when its
lexical scope encloses the use. Type positions and qualified references retain
the prior B-2 protection. Enclosing generic type parameters also suppress type
references (A-6); declarations in other members/types/blocks do not.

L5 adds static-call-shadow-elsewhere-fires, property-of-other-class-fires,
enum-member-elsewhere-fires, nameof-shadow-elsewhere-fires,
not-pattern-shadow-elsewhere-fires, or-pattern-shadow-elsewhere-fires,
switch-pattern-shadow-elsewhere-fires, positional-parameter-of-other-record-fires,
local-of-other-member-fires, local-of-sibling-block-fires,
pattern-of-other-member-fires, lambda-parameter-elsewhere-fires and
type-parameter-elsewhere-fires. Each requires exactly the foreign Svc row and
strict failure. L6 adds 14 findings=0:same-scope variants for fields, properties,
parameters, locals, local functions, lambda parameters, pattern/switch variables,
primary parameters, enum members, foreach/catch variables and generic type/method
parameters. Each requires no finding/ambiguity and strict success.

Commit witnesses before fixing. Red CP-1 must fail L5 (B-3) and L6 (A-6) by
assertion against the unchanged base engine. Fix then rerun CP-1 and the full
named classification and mechanical classes. Inspect every fresh TRX method and
class count. Real inventory must differ by exactly the ModelDisabledException
ModelAlias row; add that one row under P0-A1, expect 0/224/0/at most 3.
Parity is added methods only: no methods added or removed, no discovery run.

V-1/R-1 bind CP-1; V-2 synthetic strict behavior is exercised, real strict
activation remains P0-G2. V-3..V-12 and R-2..R-8 belong to other slices.
The explicit repair brief excludes whole Unit (CARD-1720); the prior CP-93
timeout stays historical and uncredited. No delivery/landing/lease/persistence
integration sweep. Full affected integration class SlowTestRegistryMergeTests
is in CP-91.

Mutation: PC-1..PC-12, PC-36/37 and all prior variants remain pending;
PC-11/12 require P0-G2. PC-38: restore file-wide declaration lookup, exact L5
method must fail the scope variants. PC-39: skip every declared name even in type
positions, exact L5 method must fail Color Color variants. PC-40: remove the
generic-parameter suppression, exact L6 method must fail both generic variants.
SourceLanding Mutation owns deliberate mutants, red/restore/green, missing
controls; none discharged by Code. PC-13..PC-35 stay with their slice owners.

## Performance budget

One native incremental compile first (explicit brief prerequisite; default
outputs, UseAppHost=false), plus native checkpoint-tool compilation as an
unlisted prerequisite. Each build/test driver takes its own slot. TEMP=/tmp.
Guard/classification/mechanical historical counts 9/50/11. Prior Review group
wall 20m18s, build 434s, row waits 90/225/60s, build wait 195s. Cold build
p95 817s (frozen design n=11); build cap 20 minutes, row cap 2 minutes,
slot cap 12 minutes, run execution 38 minutes (derived from estimates).
No measured row p95; caps are provisional CARD-1729 bounded by these red/green
samples. Repository fixture is shared per class and records census/read/analysis;
synthetic inputs are memory-only. Other phase spans remain unknown when absent.
At most three rows per run; no unchanged green repetition. No full assembly run.
Restart none; no production OS branch or rc startup change.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | repair | tests/Antiphon.Tests -> bin-a3555767-guard/ | layering-guard | /*/Antiphon.Tests.Infrastructure/C1729LayeringGuardTests/* | V-1, R-1, L1..L9 plus scope variants | 9 methods; red then green; inventory printed | 9 | 10 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-1 |
| CP-91 | repair | CP-1 | classification | /*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)/* | full affected classes | 50 cases, no failures/skips | 50 | 2 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-91 |
| CP-92 | repair | CP-1 | mechanical-contract | /*/*/(MechanicalPcContract*)/* | mechanical contract | 11 cases, no failures/skips | 11 | 2 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-92 |
