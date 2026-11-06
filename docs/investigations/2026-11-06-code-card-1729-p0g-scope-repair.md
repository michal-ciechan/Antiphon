# CARD-1729 P0-G round 2 scope repair evidence

B-3 is repaired; A-6 generic-parameter false findings are also repaired. Requested
ordinary rows pass 70/70 with separate validated clean receipts. Ready for Claude
round 3, blockers only, diff from 89bbb05617900d963a99feeabc5177ccefba56bc.

Task a3555767-d833-4bdd-a061-5f279d610ff2. Original Code / landing owner:
742b9c5c-9e25-4fac-910e-799803e461c9. Branch `feat/card-task-a3555767`;
worktree `/work/worktrees/task-a3555767`. Restart: none.

Base: 89bbb05617900d963a99feeabc5177ccefba56bc.
Red witness: 81af4919b72be356c9cfab0c7c9b5a507cd23e4b; engine byte-identical to base.
Fix and actual tested SHA: f3d69b6da7046c65cb93e8f28422bcaee1a62fa5.
Both commits were pushed before their long verification runs. This report is a
later documentation commit; it changes no tested source or assertions.

Plan/checkpoints: `docs/superpowers/testdesign/2026-11-06-card-1729-p0g-scope-repair.md`
at 81af4919b72be356c9cfab0c7c9b5a507cd23e4b, section `### Checkpoints`.
Unbound Mutation candidate:
`docs/superpowers/mutations/2026-11-06-card-1729-p0g-resolver-repair.md`.
Prior Review: d5fd03f4-b332-4b33-a1f4-83c9c63f0973, report at commit
444e859bd on `origin/feat/card-task-d5fd03f4`.

## Invariant and changes

A declaration suppresses a bare expression name only when its scope encloses
the use. Fields/properties/methods/events/enum members bind to their containing
type; parameters bind to their owner; locals/local functions/designations bind
to enclosing lexical scopes; foreach/catch variables have their enclosing
statement/clause. Generic type parameters suppress type references only inside
their owner, including type positions. Existing own-type, type-only-context and
qualified-reference behavior is preserved. Labels are not value declarations.

L5/L6 retain all previous assertions and method names. New L5 variants require
the exact foreign Svc row and strict failure. New L6 variants require no
unlisted/ambiguous findings and strict success. Assertions are byte-identical
between red and fix commits. There are no new test methods, categories, Slow
registry entries, policy/hash/census pins, production changes or timeout changes.

Allowlist diff: exactly **one addition, zero deletions**, at line **7**, under
`# slice P0-A1` (header line 3):

`server/Application/Exceptions/ModelDisabledException.cs -> ModelAlias @ server/Application/Services/ModelAlias.cs`

Real inventory changes 0/223/0/3 -> **VIOLATIONS 0 LISTED 224 STALE 0 AMBIGUOUS 3**.
No other real row changes. The existing three non-violation ambiguities remain.
Parallel P0-A1/P0-E/P0-F1 edits must merge this one row, not replace the allowlist.

## Ordinary verification and provenance

Red CP-1: 9 executed, 7 passed, 2 failed, zero skipped. L5 fails by assertion on
all twelve new out-of-scope value cases. L6 fails by assertion on both generic
parameter cases. These are not build, fixture or discovery failures. The
out-of-scope generic-parameter L5 variant already passed at base and protects
the scope of the new generic suppression.

Initial fix group: CP-1 9/9 and CP-92 11/11 green; CP-91 timed out at the original
two-minute cap after its banner, with no TRX or counts. Cause/last test phase is
unknown; it is not certified inherited and is not relabelled passed.
Only CP-91 was rerun for this red result, using the same filter/source/build
and a 120-second driver cap: **50/50 pass**, zero failures/skips.

The report validator refuses the mixed group's otherwise green CP-1/CP-92 rows
with `report_failed`, even with row selection. The D-2 / CARD-1570 receipt-gap
rule therefore required those exact missing receipt selections once. These
two additional runs address qualification, not flake proof. Each passed using
the same verified build and 120-second cap. No assertions or timeouts were
widened, and no further green repetition followed receipt validation.

The direct checkpoint-tool `row` verb has no deadline option; its default is
15 minutes. An ignored executable shim caps only the launched real dotnet
process at the original 120 seconds, with ten-second kill grace:
`exec /usr/bin/timeout --signal=TERM --kill-after=10s 120s /usr/bin/dotnet "$@"`.
The tool still acquires each row's slot and verifies its original build stamp.
UseAppHost=false, ArtifactsPath and UseArtifactsOutput match the producing build.
The shim is `.antiphon/a3555767-dotnet-120s.sh`; no shim rebuild occurred.

Qualified evidence at tested SHA f3d69b6da7046c65cb93e8f28422bcaee1a62fa5:

| CP | Fresh standalone report directory under `.antiphon/checkpoints/` | Actual class executions |
|---|---|---|
| CP-1 | CP-1-20261106-053306-61c3 | C1729LayeringGuardTests 9 |
| CP-91 | CP-91-20261106-052823-7c84 | TestClassificationPolicyTests 39; TestClassificationGuardTests 1; SlowTestRegistryMergeTests 5; SlowTestTripwireTests 2; NativeLinuxPolicyWitnessTests 3 |
| CP-92 | CP-92-20261106-053511-1a09 | MechanicalPcContractTests 11 |

Each standalone `report.json` passes checkpoint `validate` against the exact
tested SHA. All lines have dirty=0, sourceState=clean and buildSource=verified.
Every intended class/method and nonzero count was inspected in fresh TRX;
final rosters are `.antiphon/a3555767-CP-*-final-roster.json` (9/50/11).
Validation log: `.antiphon/a3555767-receipt-validation.log`.
Original red run: `20261106-050132-2b5b`; original mixed group:
`20261106-051343-a72b`. Both original reports remain unedited.
The original mixed report remains RED and is not a qualified receipt.

V-1 PASS; R-1 PASS (CP-1, L1..L9 and all scope variants).
V-2 synthetic strict behavior PASS; real strict activation remains P0-G2,
pending there and not claimed passed. V-3, V-4, V-5, V-6, V-7, V-8, V-9,
V-10, V-11, V-12 and R-2, R-3, R-4, R-5, R-6, R-7, R-8 belong to other slices.
Parity PASS: nine before, nine after, zero added/removed methods; the explicit
brief limits parity to added methods, so no discovery run was needed.

Whole Unit was explicitly excluded by this repair brief (CARD-1720); it was
not run. The prior task's CP-93 timeout remains historical and uncredited.
This report claims the requested scoped ordinary rows, not whole-Unit or
whole-Phase-0 completion. No delivery/landing/lease/persistence integration
sweep was run. The named full affected integration class is included in CP-91.

OS/startup inventory lint passed at the fix SHA: test-only portable changes,
zero changed production OS branches, zero startup paths, restart none. Both
required platform GETs were read: defaults server2; Linux available/eligible.
No desktop execution or fixed fleet location was used. Fresh fetch/merge-tree
against master 1038d07e66a54b630a1bc1d394923bc7049b73dd was conflict-free,
tree b3b085342b7310a123697524dfe5e992d676d77c (fix tip).
Full task base..HEAD evidence-history check is run again after committing this
report; its final result is in `.antiphon/a3555767-final-evidence-diff.log`.

## Mutation handoff: every control remains pending

PC-1, PC-2, PC-3, PC-4, PC-5, PC-6, PC-7, PC-8, PC-9, PC-10, PC-11, PC-12,
PC-36, PC-37, PC-38, PC-39 and PC-40: **all pending / unbound**.
PC-11/12 require strict P0-G2. Every original variant stays pending.
PC-13..PC-35 remain pending with their other Phase 0 slice owners.

New L5 variants (all pending Mutation): static-call-shadow-elsewhere-fires;
property-of-other-class-fires; enum-member-elsewhere-fires;
nameof-shadow-elsewhere-fires; not-pattern-shadow-elsewhere-fires;
or-pattern-shadow-elsewhere-fires; switch-pattern-shadow-elsewhere-fires;
positional-parameter-of-other-record-fires; local-of-other-member-fires;
local-of-sibling-block-fires; pattern-of-other-member-fires;
lambda-parameter-elsewhere-fires; type-parameter-elsewhere-fires.

New L6 variants (all pending Mutation; each has the findings=0: prefix):
same-scope-value-member; same-scope-property; same-scope-parameter;
same-scope-local; same-scope-local-function; same-scope-lambda-parameter;
same-scope-pattern-variable; same-scope-switch-variable;
same-scope-primary-parameter; same-scope-enum-member;
same-scope-foreach-variable; same-scope-catch-variable;
same-scope-generic-type-parameter; same-scope-generic-method-parameter.

PC-38 restores file-wide declaration presence; PC-39 skips every enclosing
declaration even in type positions. Both bind the exact L5 method:
`/*/*/C1729LayeringGuardTests/Synthetic_core_file_referencing_an_application_type_fails_in_strict_mode`.
PC-40 removes generic-parameter suppression; it binds exact L6:
`/*/*/C1729LayeringGuardTests/Allowed_edges_listed_rows_comments_and_strings_do_not_fire`.
SourceLanding Mutation owns deliberate mutants, red/restore/green and
missing-control discovery. Code ran no deliberate mutants and discharges no PC.
No more than three normal executions per unchanged proof selection occurred;
no loaded repetitions or exception budget were used.

## Costs, exceptions and cleanup

Unlisted prerequisites were explicitly requested native baseline compilation
and necessary native checkpoint-tool compilation. Both used native default
outputs and separate build slots, with TEMP=/tmp and UseAppHost=false.
Baseline compile: 174.32s MSBuild, slot granted waited=121s held=177s,
zero errors (1,728 existing warnings). This initial command used the wrapper's
default 45-minute admission cap and had no explicit outer wall timeout; it
completed normally. Tool compile: 8.11s MSBuild, slot granted waited=180s
held=10s; zero warnings/errors, twelve-minute admission and 25-minute outer cap.

Red group: wall 622.325s, execution 562.323s, admission-only 60.001s;
build 394.547s, build waited=15s; CP-1 waited=45s, driver 24.614s;
output cleanup 97.992s. Mixed group: wall 744.185s, execution 699.161s,
admission-only 45.024s; build 471.296s waited=0s; CP-1 driver 35.796s;
CP-91 timeout 122.180s; CP-92 driver 13.540s, waited=45s.
Standalone waits: CP-91 75s, CP-1 120s, CP-92 60s, all slot=granted.
Direct-row admission uses the tool default; no build was repeated.
CP-91 passing TRX covers 95.335s; its slowest case is C487_G065 at 12.503s.
Other missing fixture/external-call spans remain unknown, not zero.
Each test's membership and duration remain in its TRX/roster. No complete run
exceeded 30 minutes, no timeout widening occurred, and no full assembly ran.

The only additional test runs were the red CP-91 retry and the two explicitly
explained D-2 receipt-gap rows. This records the exception to the brief's
no-green-repeat preference; it does not hide/reclassify the mixed report.
No unleased build/test driver ran. All foreground commands were awaited.

Owned native bin/obj outputs and the retained checkpoint build output are
removed after receipt validation using exact literal absolute paths. Creation
inventory: `.antiphon/a3555767-output-inventory.log`; preserved build stamp and
assembly digest: `.antiphon/a3555767-checkpoint-build-source.json` and
`.antiphon/a3555767-build-hash.json`. Generated receipts, logs and TRX stay
ignored. No production restart, landing or deployment occurred.

## Unedited checkpoint lines

```text
CHECKPOINT CP-1 commit=81af4919b72be356c9cfab0c7c9b5a507cd23e4b build=ok filter=/*/Antiphon.Tests.Infrastructure/C1729LayeringGuardTests/* executed=9 passed=7 failed=2 skipped=0 trx=/work/worktrees/task-a3555767/.antiphon/checkpoints/20261106-050132-2b5b/rows/CP-1/run.trx slot=granted waited=45s dirty=0 source=81af4919b72be356c9cfab0c7c9b5a507cd23e4b sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=f3d69b6da7046c65cb93e8f28422bcaee1a62fa5 build=ok filter=/*/Antiphon.Tests.Infrastructure/C1729LayeringGuardTests/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-a3555767/.antiphon/checkpoints/20261106-051343-a72b/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=f3d69b6da7046c65cb93e8f28422bcaee1a62fa5 sourceState=clean buildSource=verified
CHECKPOINT CP-91 commit=f3d69b6da7046c65cb93e8f28422bcaee1a62fa5 build=reused filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=2m slot=granted waited=0s dirty=0 source=f3d69b6da7046c65cb93e8f28422bcaee1a62fa5 sourceState=clean buildSource=verified
CHECKPOINT CP-92 commit=f3d69b6da7046c65cb93e8f28422bcaee1a62fa5 build=reused filter=/*/*/(MechanicalPcContract*)/* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-a3555767/.antiphon/checkpoints/20261106-051343-a72b/rows/CP-92/run.trx slot=granted waited=45s dirty=0 source=f3d69b6da7046c65cb93e8f28422bcaee1a62fa5 sourceState=clean buildSource=verified
CHECKPOINT CP-91 commit=f3d69b6da7046c65cb93e8f28422bcaee1a62fa5 build=reused filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)/* executed=50 passed=50 failed=0 skipped=0 trx=/work/worktrees/task-a3555767/.antiphon/checkpoints/CP-91-20261106-052823-7c84/run.trx slot=granted waited=75s dirty=0 source=f3d69b6da7046c65cb93e8f28422bcaee1a62fa5 sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=f3d69b6da7046c65cb93e8f28422bcaee1a62fa5 build=reused filter=/*/Antiphon.Tests.Infrastructure/C1729LayeringGuardTests/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-a3555767/.antiphon/checkpoints/CP-1-20261106-053306-61c3/run.trx slot=granted waited=120s dirty=0 source=f3d69b6da7046c65cb93e8f28422bcaee1a62fa5 sourceState=clean buildSource=verified
CHECKPOINT CP-92 commit=f3d69b6da7046c65cb93e8f28422bcaee1a62fa5 build=reused filter=/*/*/(MechanicalPcContract*)/* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-a3555767/.antiphon/checkpoints/CP-92-20261106-053511-1a09/run.trx slot=granted waited=60s dirty=0 source=f3d69b6da7046c65cb93e8f28422bcaee1a62fa5 sourceState=clean buildSource=verified
```
