# CARD-1587: verify the existing fixture repair

Original Code and landing owner: cdf07a4b (cdf07a4b-57d3-42d9-8519-58c046e0eac8).
Task base: df1fc145181a3bedf444f9113f822f30d3b136f6. Branch: feat/card-task-cdf07a4b.

## Finding and implementation boundary

The card's historical red is at 9a3e6c7a53d2e98fd8582dd7d0c0ef1cb4be87a9.
The assigned base already contains 8495c0a2f53b062223294763f6f206f54682ad1c,
which renames the four Markdown specimens and the text hash file to `.fixture`
without changing their bytes, and updates ChannelOutboundComposedTransportTests.
It also contains 23b7fa999593fc1d330741f028bf9fa02284bce6, which updates both
browser consumers. The audit and transport assertions remain intact.

The requested initial native incremental build passed (264.83s, 45s admission).
The exact reported method C1483_S5_CheckRequiresEvaluationRecord then passed on
the clean task base: 1 passed, 0 failed, 0 skipped; body 78.562s, host 90.626s,
15s admission. These two unlisted gated drivers are the brief's required initial
compile and base reproduction, before establishing whether implementation is needed.
Evidence: .antiphon/c1587-native-build.log, .antiphon/c1587-base-method.log and
.antiphon/c1587-base-method/run.trx. No new repair or test is warranted by this result.

## Verification design

- V-1: the exact reported method executes on the assigned base, including the
  real repository audit; fresh TRX must contain its nonzero passing result.
- V-2: full LandGraphEvaluationTests retains document admission, evaluation and
  provenance refusal coverage; full ChannelOutboundComposedTransportTests retains
  the real broker/gateway/Slack fixture-byte and attachment-name transport assertions.
- V-3: whole Unit lane, including method-level Unit attributes. Its scope is the
  commissioned Final profile, not a full-assembly run. No full assembly is requested.
- R-1: complete policy/classification/native-Linux/registry/tripwire classes,
  MechanicalPcContractTests, TaskPlatformGuidanceTests and C1729LayeringGuardTests.
- R-2: manually compare all five renamed fixture Git blobs with their historical
  predecessors, and inspect all three consumers, preserving audit rules and wire names.
- R-3: full task base..HEAD evidence-diff guard, fresh origin/master merge-tree,
  clean source receipt validation and exact pushed ref confirmation.

The only changed files are verification documentation. No product behavior,
OS-selected helper, startup path, policy JSON, policyHash or PinnedInputDigest changes.
Restart: none. No manual browser acceptance is required for this no-op finding.
Unrelated baseline failures must be reported with their actual attribution; never
relax assertions, add retries or widen timeouts. Rerun only red cases, never green rows.

## Pending Mutation inventory

PC-1 / original-Markdown-extension: at the actual SourceLanding SHA, restore the
four `.md.fixture` paths to their original `.md` names in an owned snapshot. Run
only `/*/*/LandGraphEvaluationTests/C1483_S5_CheckRequiresEvaluationRecord`;
require a compiling intended failure at the real repository audit assertion with
the disallowed Markdown diagnostic, restore, rebuild and run that same method green.
Code does not execute this deliberate mutant. Mutation also owns missing-control
discovery, even though this task adds no implementation or tests.

## Performance budget

One native build already measured 264.83s on Linux/.NET SDK 10.0.403 under shared
host load; one method sample is 78.562s, not a p95. Checkpoint cold build provisionally
5-8 minutes; affected classes provisionally 8 minutes; guard row 3 minutes.
The complete Unit lane has no current measured p95 and is documented as exceeding
30 minutes (CARD-1573 and CARD-1514/CARD-1314 performance work). Use a fixed 45-minute
row deadline from the initial 15-minute estimate; this is a provisional importer
exception, not a measured 2x p95. Existing test deadlines remain unchanged.
Retain actual timings for caller Debug commissioning if a selection exceeds 30m;
this delegate cannot subdelegate. Do not repeat the expensive green selection.

The checkpoint rows run serially, sharing one isolated build and the existing
once-per-assembly database setup. Integration body and teardown use their existing
per-test isolation. TRX retains individual durations and membership; checkpoint
receipts retain build, execution and admission separately. Container/readiness,
migration, filesystem, CLI, HTTP and teardown attribution absent from those receipts
remains unknown, never zero. Total execution allowance is 62 minutes (2*26+10),
independent bounded slot admission is 45 minutes per acquisition. No deadline is
widened after observing a failure. A timeout is incomplete verification.

### Checkpoints

totalTimeoutMinutes: 62

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1587-final/ | full-unit | /*/*/*/*[Category=Unit] | V-3 | full Unit lane, 0 failed | 1 | 15 | true | TEMP=/tmp |
| CP-2 | S1 | CP-1 | affected-integrations | /*/*/(LandGraphEvaluationTests*)\|(ChannelOutboundComposedTransportTests*)/* | V-1, V-2 | both full classes, 0 failed | 2 | 8 | true | TEMP=/tmp |
| CP-3 | S1 | CP-1 | required-guards | /*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/* | R-1 | all eight full classes, 0 failed | 8 | 3 | true | TEMP=/tmp |
