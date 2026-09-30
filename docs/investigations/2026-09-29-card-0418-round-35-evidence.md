# CARD-0418 round 35: recovery and remaining ordinary verification

This continues [round 34](2026-09-29-card-0418-round-34-evidence.md) from
`c87a5641a7756d2c84958be096593cb95a990b5e`. Round 34 established the
durable source/input/output hashes, task and correlation ownership, publication
stamps, post-restart attention and actual broker acceptance evidence for V-15.
It left the dispatched-worker transcript/process reconciliation at C-3 and
normal worker settlement across death at C-4 open. This round records any new
receipts below. The inherited CP-8 `PinnedAgentKindTests.T1/T2` failures for
`codex_desktop_unqualified` remain outside this card's changes.

## V-15 C-3/C-4

The dispatched C-3 row now keeps the killed dispatcher's task/session identity,
records a marked transcript closing turn, and settles that existing task through
`AgentTaskReplyService`. The worker writes its output manifest under the actual
request's output directory. A second process dies at the C-4 pre-claim boundary
after normal settlement and before pump observation. A fresh probe then validates
the output and must publish the converted attachment. The test checks the
retained task id/session, transcript marker, output hash and accepted payload.

An initial CP-6 run on `5a94ecc99720c712703ef661b65ca970ff6d3300` executed
29 / passed 28 / failed 1 at `.antiphon/checkpoints/r35-cp6-a/`. The C-4
barrier was initially after the pump's claim; the killed process left a five-minute
lease on a two-minute conversion deadline, so immediate recovery stayed
`Converting`. This did not validate the normal worker output. The barrier moved
to just before claim; that cut still follows committed transcript settlement and
precedes pump observation. Post-claim death with a short configured conversion
deadline is a separate lease/deadline behavior and is not claimed by this case.
The next CP-6 run on `424f06ef3d766e65ea3c72d0b0c4708b3d533339` also
executed 29 / passed 28 / failed 1 at `.antiphon/checkpoints/r35-cp6-b/`:
the test's zero-offset recovery configuration selected the probe's deliberate
refusing producer, so it reached `PublishUncertain`. The recovery offset is now
one second, which selects the durable evidence producer without expiring the
conversion deadline. The next CP-6 rerun on `7a72c77ca24d5f59ab3b2574b763be40a791f484`
executed 29 / passed 28 / failed 1 at `.antiphon/checkpoints/r35-cp6-c/`:
the change hit the earlier task-creation call, not the post-C-4 recovery call.
Both call sites are now corrected.

The final CP-6 rerun on `103dc8e266a171f91db51163398b90b03acd659d`
executed 29 / passed 29 / failed 0 at
`.antiphon/checkpoints/r35-cp6-d/CP-6-20260929-232045-3221/run.trx`.
This proves the C-3 persisted dispatched owner can settle from a marked
transcript after the dispatch process dies, and the C-4 settled output survives
a second process death before the pump claims it. It does **not** yet prove
reconciliation of a submitted/running converter process after abrupt server
death: the crash probe still uses a refusing external launch sink. V-15 therefore
remains open at the whole-ID level. The existing F-5 owned-host-restart test
`Four_sources_convert_only_for_the_selected_conversation` exercises a real
FakeGrok converter process across a graceful host restart, but that is not an
abrupt process-death C-3 receipt.

## Checkpoint receipts

The Final checkpoint selection ran on committed code at
`28db8f37ca426fc8504a44081e2cfcd0c5a160cb`. Each .NET row used
`scripts/run-checkpoint.ps1` with its plan filter, minimum and named classes;
CP-12/13 used `scripts/build-slot.ps1`. The row logs and TRX files are under
`.antiphon/checkpoints/r35-final-*` in this worktree (ignored build evidence).

| Row | Executed / passed / failed / skipped | Verdict |
|---|---:|---|
| CP-1 Unit | 3491 / 3491 / 0 / 33 | Pass; `TUNIT_MAX_PARALLEL_TESTS=2` |
| CP-2 source settlement | 373 / 373 / 0 / 0 | Pass |
| CP-3 policy/schema | 32 / 32 / 0 / 0 | Pass |
| CP-4 file boundary | 64 / 64 / 0 / 0 | Pass |
| CP-5 purpose/deadline | 62 / 62 / 0 / 0 | Pass |
| CP-6 crash/transport | 29 / 29 / 0 / 0 | Pass |
| CP-7 routing/attention | 320 / 320 / 0 / 0 | Pass; `TUNIT_MAX_PARALLEL_TESTS=2` |
| CP-8 existing deadlines | 71 / 69 / 2 / 0 | Inherited `PinnedAgentKindTests.T1/T2` `codex_desktop_unqualified`; no introduced failure |
| CP-9 renderer | 19 / 19 / 0 / 0 | Pass |
| CP-10 real browser | 1 / 1 / 0 / 0 | Pass; `ANTIPHON_HEADED_TESTS=1` |
| CP-11 gateway wire | 125 / 125 / 0 / 0 | Pass; `ANTIPHON_BROKER_TESTS=1` |
| CP-12 channels client | 26 / 26 / 0 / 0 | Pass, two files |
| CP-13 client bundle | — | Pass, production Vite bundle |

CP-8's own TRX is
`.antiphon/checkpoints/r35-final-CP-8/CP-8-20260930-002525-1f4a/run.trx`;
the two exceptions both come from `AgentTaskService.CreateAsync`'s pre-existing
desktop Codex qualification guard. PC-1–PC-30 remain reserved for method-scoped
SourceLanding Mutation; ordinary and nightly green do not discharge them.

## V-16 publication outcomes

The plan-named `Only_acceptance_stamps_complete_actual_payload` has four
producer outcomes under a real isolated database: blocked before acceptance,
two definite queue refusals then acceptance, three definite refusals, and an
ambiguous exception. It hashes the actual serialized payload and checks that
delivery, correlation, source and channel stamps agree only after acceptance;
inbound routing fields stay frozen. Final CP-5 executed 62 / passed 62.
The first CP-5 build on `51bd3ba3a` did not reach tests: two references to
`MessagingJson` were ambiguous between the Messaging and Client namespaces.
Both now select the actual `Antiphon.Messaging.MessagingJson.Options` wire contract.
CP-5 rerun on `d642deb417560f051b030df7313edfc418426664` executed 57 /
passed 57 / failed 0 at
`.antiphon/checkpoints/r35-cp5-b/CP-5-20260929-233336-c2a2/run.trx`.
V-16 remains open for source completeness and multi-target outcomes.

`Source_publication_requires_all_four_members_or_a_complete_zip` now adds four
actual publication variants (one/four Markdown attachments and incomplete/complete
zip), comparing the accepted serialized payload to the frozen original and
checking the source delivery stamp after publication. Final CP-5 executed
62 / passed 62.
The first CP-5 run on `68535c0630ed04f77032d63297d742ab8c7630f4`
executed 61 / passed 60 / failed 1 at `.antiphon/checkpoints/r35-cp5-c/`:
the complete-zip fixture used a name outside `IsSafeStoredSourceName`'s
`*-sources.zip` contract, so it could not be counted as a source file. The
fixture now uses `fixture-sources.zip` consistently.
CP-5 rerun on `1dffdd6fa737d0fb88c9ce0fce9b944e512f55d6` executed 61 /
passed 61 / failed 0 at
`.antiphon/checkpoints/r35-cp5-d/CP-5-20260929-234312-5c50/run.trx`.

`Accepted_target_is_not_retried_when_a_second_target_fails` now adds a
two-channel send from one source task: A accepts once, B returns three definite
pre-acceptance queue refusals, and a repeated pump tick must not replay A.
It compares each delivery's state, attempt count, correlation stamp and the
source task stamp. Final CP-5 executed 62 / passed 62.
The first CP-5 build on `bca86d551` did not run tests: the two-target fixture
declared `ChannelOutboundSnapshot` without importing its Application.Interfaces
namespace. That import is now explicit.
CP-5 rerun on `09d76205b` executed 62 / passed 61 / failed 1 at
`.antiphon/checkpoints/r35-cp5-f/`: the two-target fixture reused the
globally unique `SourceKey`. It now uses a distinct intent key for each
destination while retaining the same source task id and source bytes.
CP-5 rerun on `e04679c6c8f676303d64368e11935a87202f2c03` executed 62 /
passed 62 / failed 0 at
`.antiphon/checkpoints/r35-cp5-g/CP-5-20260929-235507-98ed/run.trx`.

V-16's ordinary publication outcome matrix is now present: the blocked producer,
two/three definite refusals and ambiguous send, exact accepted wire bytes,
per-target attempts, four-source completeness and zip completeness all have
fresh DB and payload assertions. The round-34 C-8 commit-failure cut checks the
atomic stamp boundary. The earlier F-5 composed test demonstrates conversion
output separately from source completeness. Final CP receipts are below.

## Remaining ordinary work after this slice

V-15 still needs abrupt death with an already submitted/running converter
process, followed by normal runner/transcript reconciliation of that same owner.
V-17 needs the old pending-correlation TTL/late-confirm and bounded-deadline
interaction plus full Held/Failed/PublishUncertain/Published attention rows.
V-18 needs the in-flight policy-change boundary matrix. V-23 needs the exact
serialized budget edges and full real-broker key cases. R-2–R-6, R-9, R-11 and
R-13–R-14 remain open at their whole-ID level until those cases are satisfied.
