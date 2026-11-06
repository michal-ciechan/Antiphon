# C1466 EchoWorld first-output scheduling repair

Original Code/landing owner: `69ade330-9ef8-47da-8414-72d5790a61ea`.
Base: `1527c29fef2737bf3eaa49f2293d82d85402265a`. Test-side only.
Debug: `origin/feat/card-task-1f10d17d`, report commit `2ce364964`.

## Implementation

S1 adds a compiling deterministic red witness, asynchronous-output gate and an
inert default-off synchronous option. Extract EchoWorld's existing adapter creation
so the witness drives the same factory as StartCancelledAsync. S2 implements the
option with the same Emit then signal sequence, and enables it in that factory only.
Keep the one-second FirstDeltaTimeoutMs, empty-output behavior, callbacks, prompt
failure/recording and every existing state assertion. No product, policy or d69fe301
repair changes. No changed OS-selected production behavior; OS witness is N/A.

## Verification design

V-1: withhold the asynchronous worker with an incomplete task. The actual EchoWorld
factory must emit the diagnostic text and complete first-output before SendPrompt
returns. A zero-duration observation discriminates synchronous completion without
waiting for scheduling. S1 must fail this assertion; S2 must pass it.
V-2: default adapter remains asynchronous: no output/signal while gated, real output
and signal after release. V-3: empty prompt output plus noise never completes the
first-output signal. V-4: callbacks, failures and prompt history retain their order.
R-1: full AgentSessionCancelledTurnTests, including all EchoWorld state assertions.
R-2: classification policy/guard, slow registry merge/tripwire and mechanical
instruction contract. R-3: whole Unit lane, covering its bounded ordinary invariants;
it does not establish delivery, landing, leases or persistence acceptance.
R-4: C1382 methods repeat proof at S2. The brief requests 20 repetitions; the stage
contract caps unchanged proof at 3 normal plus 2 loaded, with extra repeats allowed
only for a Review-demonstrated flake. No such Review exists, so execute 3 normal
repetitions and preserve this limit explicitly rather than claiming 20.

Consumer search: `rg -l 'FakeAgentProtocolAdapter|EchoWorld' tests/Antiphon.Tests
--glob '*.cs'` finds 138 files. The default path is unchanged; the opt-in closure is
only AgentSessionCancelledTurnTests.EchoWorld. V-2 guards the shared default path.
No full-assembly run is needed. Guards are explicitly named in CP-2 even though
some also belong to Unit. Inspect fresh TRX methods and class counts for each row.
Required manual checks: unchanged deadline/state assertions; opt-in consumers;
policy unchanged; evidence diff over base..HEAD; conflict-free merge-tree.

### Cost

Native incremental compile first (brief requirement): provision 6 minutes.
Checkpoint bootstrap: 2 minutes. Ordinary checkpoint floor 29 minutes; authoring
and admission extra. Existing Debug build 325 seconds, fixture method 28 seconds;
Unit historical namespace assembly baseline 25.5 minutes is only a provisional
ceiling, not a measured Unit p95. Row watchdogs are provisional diagnostic bounds,
never changes to test assertions/deadlines. Total charged budget 60 minutes.

The S1 manifest at `649d74b7a89921a78cf10277b4cca2e1b803b0c3` produced the
intended V-1 assertion red (1 failed). The final manifest below reruns CP-1 at S2
and shares that fresh build with CP-2/CP-3. CP-4 has Repeat=3 and therefore needs
its own output and stamp, rather than reusing a Repeat=1 build.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Repeat |
|---|---|---|---|---|---|---|---:|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1466-green/` | deterministic-red | `/*/*/AgentSessionCancelledTurnTests*/C1466_EchoWorld_output_precedes_withheld_async_work*` | V-1 | 1 intended assertion failure before S2; green after S2 | 1 | 6 | 1 |
| CP-2 | S2 | CP-1 | fixture-guards | `/*/*/(AgentSessionCancelledTurnTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(MechanicalPcContractTests*)/*` | V-1, V-2, V-3, V-4, R-1, R-2 | all listed classes, 0 failed/skipped | 23 | 7 | 1 |
| CP-3 | S2 | CP-2 | unit | `/*/*/*/*[Category=Unit]` | R-3 | >= 1 executed, 0 failed | 1 | 12 | 1 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1466-repeat/` | c1382-proof | `/*/*/AgentSessionCancelledTurnTests*/C1382_*` | R-4 | all C1382 methods, 3 normal repetitions via --repeat 3, 0 failed | 13 | 4 | 3 |
