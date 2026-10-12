# CARD-0985: guard-the-guard state-machine fixtures

Task/landing owner: Code `8da91605`, branch `feat/card-task-8da91605`.
Base: `0973be4121b89249f092a6a5f4e9542797b86a2c`. Final, bounded Pty-only slice;
the brief explicitly excludes the broad Unit lane. No production or runtime changes.

## Decision and files

The environment census requires every owner of a ConPTY-directory mutation to have
unkeyed `[NotInParallel]` on its method or class. The self-test protects the census
from silently losing IL edges. Its existing lambda/helper assertions remain intact.
Current HEAD contains CARD-0978's guard but no state-machine self-test fixtures;
the file history ends at `5e7c725743a96f3ca6ac52f1c16f075fe4d14051`.

Add compiled, never-executed async-method, async-iterator and iterator fixtures in
`tests/Antiphon.Agents.Pty.Tests/ConPtyEnvironmentIsolationGuardTests.cs`. Each shape
has an unserialized mutator, a keyed control, a method-serialized control and a
class-serialized control. Every mutation sits after `await` or `yield`; the outer
method lacks the literal/setter pair. The compiler puts it in a generated state
machine's `MoveNext`, which a root-only reflection/IL scan misses. Use separate
fixture trees so the existing lambda/helper test and its assertions stay unchanged.

Each new test asserts a handwritten complete owner list and violation list, with
the same scheduling outcomes as the existing synchronous/helper fixtures. No
fixture runs or changes the actual process environment. Extend exact fixture-tree
exclusion for the real census. First commit and run the new rows against the
pre-slice scanner. Async/iterator traversal already exists, so those controls may
pass; do not invent a baseline failure. Async iterators currently have an unhandled
`AsyncIteratorStateMachineAttribute`; repair that test-only traversal if red confirms
it. A recognized state-machine attribute without a discoverable `MoveNext` throws
instead of letting an unknown shape make the guard green.

Deliberate mutants belong to the subsequent Mutation stage under the standing Code
contract. Its scratch red/restore/green controls below remain pending; ordinary
pre-repair failure reproduction is Code work, not PC discharge.

## Verification design

S1: commit this note. S2: commit fixtures/tests, run CP-1 test-first against the
unchanged scanner; commit a confirmed scanner repair, rerun the same CP-1.
No full-assembly or integration run: this invariant is bounded to this one Unit
class and compiled IL fixtures; no delivery, landing, lease or persistence changes.

| ID | Method / acceptance | Case -> mutation -> expected red |
|---|---|---|
| V-1 | `Guard_classifies_async_method_state_machines_by_the_owning_method` | mutation after await; skip state-machine traversal -> exact owner list loses all four roots |
| V-2 | `Guard_classifies_async_iterator_state_machines_by_the_owning_method` | mutation after await/yield; omit async-iterator attribute -> exact owner list loses all four roots |
| V-3 | `Guard_classifies_iterator_state_machines_by_the_owning_method` | mutation after yield; skip iterator attribute -> exact owner list loses all four roots |
| V-4 | `Guard_refuses_state_machines_without_MoveNext` | recognized attribute with missing body; silently ignore missing MoveNext -> required exception absent |
| R-1 | `ConPty_environment_mutators_are_unkeyed_not_in_parallel` and `Guard_detects_lambdas_closures_and_parameter_helpers_by_the_owning_method` | unchanged census floor and lambda/helper owner assertions |
| R-2 | Windows qualification disclosure | native Windows execution not required by this portable IL-only slice; disclose that it was not run |
| R-3 | manual source/publication checks | fixtures never invoked; old assertions unchanged; full-range evidence policy; merge-tree against fetched master; remote SHA equals HEAD; tracked file count; clean tree |

### Positive controls (pending Mutation)

Each PC is method-scoped to its named V method above. Mutation owns deliberate
scratch mutations, assertion-red validation, exact restoration and rebuilt green.

| PC / variant | Test | Guard defect |
|---|---|---|
| PC-1 / async | V-1 | stop pushing generated MoveNext bodies |
| PC-2 / async-iterator | V-2 | omit AsyncIteratorStateMachineAttribute traversal |
| PC-3 / iterator | V-3 | omit IteratorStateMachineAttribute traversal |
| PC-4 / missing-body | V-4 | silently continue when a recognized state machine lacks MoveNext |
| PC-5 / method-control | V-1 | reject unkeyed method serialization |
| PC-6 / class-control | V-2 | reject unkeyed class serialization |
| PC-7 / keyed-control | V-3 | accept keyed serialization |

### Cost

Ordinary checkpoint floor: 3 minutes including one isolated project build per
committed source group. Authoring, baseline-red inspection and rerun: 30–45 minutes.
Repeated green proof is unnecessary. Run serially, never beside Antiphon.Tests.
The checkpoint launcher/tool bootstrap itself is gated with build-slot.ps1;
this is infrastructure outside the one Pty project build, not an extra test run.
Linux checkpoint builds set UseAppHost=false and use a trailing-forward-slash
alternate output. Binding uses ANTIPHON_TASK_TOKEN when present without printing it.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S2 | `tests/Antiphon.Agents.Pty.Tests -> bin-c985-guard/` | c985-guard | `/*/*/ConPtyEnvironmentIsolationGuardTests/*` | V-1, V-2, V-3, V-4, R-1 | all 6 methods, 0 failed/skipped | 6 | 3 | true |
