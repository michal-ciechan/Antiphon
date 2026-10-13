# CARD-1209 stale Unit assertions

Test-only repair on base ab0ca29525f7d63f485bad4615fa79259068e3bb. Landing owner: Code task 3df28f8c. Change only ChannelOutboundContractTests and GrokRunnerImageContractTests; no bundle, Dockerfile, product behavior, timeout or retry changes.

The channel assertion now requires the retained Markdown-default and channel-conversion guidance after CARD-0822 removed an adjective. The Grok assertion captures runtime-base ARG GROK_VERSION and GROK_SHA256, independently pins both, requires the versioned artifact URL, strict checksum command and exact success status, then install and pinned version probe in order. Existing docs, fixtures and verifier expectations remain; digest agreement with the two digest-bearing owner docs is also checked.

Inherited-red output and first-bad/parent evidence are preserved in docs/superpowers/reports/2026-10-13-card-1209-debug-evidence.md. At e19186dc8e156216a2875c8c87237b94b7a857f8 each exact method executed once and failed: channel expected "configured channel step"; Grok installed.Success was False. CARD-0989's review missed GrokRunnerImageContractTests because it ran only its own classes.

## Verification design

V-1: ChannelOutboundContractTests.Server_and_gateway_keep_source_and_transport_boundaries requires both retained guidance phrases and preserves all dependency, settings and no-Prefer-PDF checks.

V-2: GrokRunnerImageContractTests.Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers requires the independently qualified version/digest and checksum-before-install-before-probe chain, plus documentation/fixture/verifier agreement.

R-1: full ChannelOutboundContractTests and GrokRunnerImageContractTests, including all parameterized version acceptance/rejection cases.

R-2: CARD-0989's Runner_pins_grok_cli_by_version_and_digest and all Runner_grok_sha methods; also full DockerStackContractTests and GrokSha256PinTests to satisfy named full-class scope.

R-3: removed from this task's selection by the explicit operator refinement f2800e8f-820b-4179-b378-9dde2ace9ffa. Do not run the whole Unit lane again. The prior CP-6 attempt at 935bb3d6e08272693736aafd7dc2fa107a8a38fe timed out at its unchanged 15-minute limit before this refinement arrived; no final TRX or counts were available. That history is retained, not relabeled as a pass or as never run. Other Linux Unit reds are tracked under CARD-1210/CARD-1209 per the caller. This exclusion amends only this task's requested selection, not the standing Final verification policy.

No assembly-wide integration run: source/transport guidance and qualified installer invariants are the bounded impact; there are no changed unbounded runtime classes. Expected narrowed qualification cost: isolated build 4 minutes plus full affected classes 2 minutes. Unit does not prove delivery, landing, leases or persistence; none of those implementations changed.

V-3 manual: inspect fresh TRX identities and nonzero counts for every row; validate clean source receipts with verified build provenance; run evidence-diff over full base..HEAD, fetch origin/master and require merge-tree exit 0; confirm pushed task ref equals HEAD; remove only created outputs. Restart: none.

### Positive controls (all pending SourceLanding Mutation)

Code does not perform deliberate mutants under the stage ownership rule. The requested scratch-copy red demonstrations are specified here for method-scoped Mutation, not claimed complete. Each variant requires assertion red, restoration and green against the unchanged exact method; build/fixture failure and zero results do not qualify.

| PC | Exact detecting filter | Scratch-copy mutation | Expected red |
|---|---|---|---|
| PC-1a | `/*/*/ChannelOutboundContractTests/Server_and_gateway_keep_source_and_transport_boundaries` | Remove retained Markdown-default phrase from copied bundle | Markdown-default assertion |
| PC-1b | `/*/*/ChannelOutboundContractTests/Server_and_gateway_keep_source_and_transport_boundaries` | Remove retained channel-conversion phrase from copied bundle | Channel-conversion assertion |
| PC-2a | `/*/*/GrokRunnerImageContractTests/Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers` | Change copied Dockerfile GROK_VERSION to 1.0.42 | Qualified version assertion |
| PC-2b | `/*/*/GrokRunnerImageContractTests/Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers` | Change one digest character in copied Dockerfile | Reviewed digest assertion |
| PC-2c | `/*/*/GrokRunnerImageContractTests/Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers` | Remove strict checksum command from copied Dockerfile | Checksum assertion |
| PC-2d | `/*/*/GrokRunnerImageContractTests/Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers` | Move checksum after install in copied Dockerfile | Ordering assertion |
| PC-2e | `/*/*/GrokRunnerImageContractTests/Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers` | Change documented digest in copied docker-stack.md | Documentation agreement assertion |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1209-code/` | channel-method | `/*/*/ChannelOutboundContractTests/Server_and_gateway_keep_source_and_transport_boundaries` | V-1 | exactly 1 passed | 1 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=4` |
| CP-2 | S1 | CP-1 | grok-method | `/*/*/GrokRunnerImageContractTests/Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers` | V-2 | exactly 1 passed | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=4` |
| CP-3 | S1 | CP-1 | repaired-classes | `/*/*/(ChannelOutboundContractTests*)\|(GrokRunnerImageContractTests*)/*` | R-1 | all 28 passed | 28 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=4` |
| CP-4 | S1 | CP-1 | checksum-guards | `/*/*/(DockerStackContractTests*)\|(GrokSha256PinTests*)/(Runner_pins_grok*)\|(Runner_grok_sha*)` | R-2 | all 12 passed | 12 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=4` |
| CP-5 | S1 | CP-1 | checksum-classes | `/*/*/(DockerStackContractTests*)\|(GrokSha256PinTests*)/*` | R-2 | all full classes passed | 120 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=4` |
| CP-7 | S2 | `tests/Antiphon.Tests -> bin-c1209-refined/` | refined-repaired-classes | `/*/*/(ChannelOutboundContractTests*)\|(GrokRunnerImageContractTests*)/*` | V-1, V-2, R-1 | all 28 passed, including both repaired exact methods | 28 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=4` |
| CP-8 | S2 | CP-7 | refined-checksum-classes | `/*/*/(DockerStackContractTests*)\|(GrokSha256PinTests*)/*` | R-2 | all 124 passed, including all 12 checksum guards | 124 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=4` |

S1 already qualified CP-1..CP-5 at 935bb3d6e08272693736aafd7dc2fa107a8a38fe: 1, 1, 28, 12 and 124 passed, respectively, with zero failures/skips and validated clean source/build provenance. S2 commits this operator-requested manifest amendment, then qualifies its new HEAD using only CP-7/CP-8 (`--after S2`): one isolated build, UseAppHost=false and trailing forward slash. This is required source/profile qualification after the explicit refinement, not an optional repeat of unchanged green proof; no extra exact-method reruns are added. The removed CP-6 is not selected or rerun.

An unlisted build-slot-gated bin-c1209-refined-tool/ infrastructure bootstrap is needed because the prior bootstrap output was already cleaned; it changes no source. Run tool with run --plan this path --after S2 --expected-source-sha committed HEAD --serial --max-wait 55s, then wait until exit is not 75. No further repeats after green. Raw evidence stays ignored; final receipts belong in the stored task report without changing the tested source SHA.

--- next stage ---
next: review
handoff: Review the two test-only repairs and operator-refined ordinary evidence. CP-7/CP-8 qualify every full affected class; whole Unit is explicitly excluded by the refinement and its prior timeout remains disclosed. Every PC variant remains pending SourceLanding Mutation after caller lands Code task 3df28f8c.
artifact: docs/superpowers/plans/2026-10-13-card-1209-stale-unit-assertions.md
