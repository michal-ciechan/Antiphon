# CARD-1724: E2E artifact paths

Base: `d465bd4e363a3fd3ba334f174a5576852ee7e227`. Original Code/landing owner:
`d933f966-15f7-4f30-bac2-1b5c6b21eed9`. Test-only; restart: none.

S1 adds a Roslyn source guard while leaving the defective E2E sources intact.
S2 redirects repository-docs writers to ignored E2E TestOutput and verifies them.
No documentation screenshot update mode is needed; deliberate documentation refresh
remains separate from ordinary test execution.

## Verification design

V-1: the full new E2EArtifactPathGuardTests class must flag AgentE2ETests at S1
(base E2E source), then pass at S2. Synthetic scanner inputs exercise ScreenshotAsync,
File.WriteAllTextAsync and Directory.CreateDirectory, aliases, comments, reads and
ignored artifact destinations. The scanner is a bounded syntax/dataflow guard,
not a complete interprocedural C# security analyzer.

R-1: full TestClassificationPolicyTests, TestClassificationGuardTests,
SlowTestRegistryMergeTests and SlowTestTripwireTests classes.
V-2: compile Antiphon.E2E, without executing browsers/canaries.
R-2: whole Unit lane, required by the stage contract. This is broader than the
card brief's focused selection; it protects classification/source-scanner invariants.
No full assembly is selected. Integration execution is bounded to R-1; the changed
browser/canary classes are compile-only by explicit brief instruction.
V-3: manually audit tests/**/*.cs, client tests/E2E and scripts for tracked-path
writers; preserve inventory and dispositions in the Code evidence report.
V-4: evidence-diff over full base..HEAD and conflict-free merge-tree against fetched master.

PC-1 (pending SourceLanding Mutation): method-scoped
E2EArtifactPathGuardTests.E2E_writers_do_not_target_repository_docs; restore the
Agent screenshot's FindRepoRoot()/docs/screenshots destination, require assertion
red naming AgentE2ETests, restore and require green. Mutation owns this cycle and
additional missing-control discovery; Code's ordinary S1 regression is separate.
PC-1 variants also restore one Board screenshot writer and the canary's
FindRepositoryRoot()/docs/investigations writer, independently, using that same
exact guard method. All three variants remain pending for Mutation.

## Performance budget

Provisional estimates (unmeasured on this loaded host): scanner/build 6 minutes;
focused guards/build 10 minutes; E2E compile 5 minutes. Unit lane 40 minutes, from
the testing guide's recorded over-30-minute lane. Admission is separate (up to
45 minutes each); no repeats requested. Container/host/body/teardown phases of
existing guards and Unit remain unchanged and are retained in TRX/run logs.
No new external calls, test timeouts, browser startup or shared fixture setup.
The broad Unit run's known performance debt needs a separate Debug commission;
it is not a reason to widen existing assertions/timeouts or claim partial Full.
Use explicit 45-minute row and 60-minute execution allowances, TEMP=/tmp,
UseAppHost=false. Bootstrap tool compilation is an explained extra gated build.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1724-red/` | scanner-red | `/*/*/E2EArtifactPathGuardTests*/*` | V-1 | 5 executed; repository guard red naming AgentE2ETests, 4 scanner cases green | 5 | 6 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1724-green/` | scanner-and-policy | `/*/*/(E2EArtifactPathGuardTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)/*` | V-1, R-1 | all listed, 0 failed | 6 | 10 |
| CP-3 | S2 | n/a | e2e-compile | `dotnet build tests/Antiphon.E2E -p:UseAppHost=false --nologo` | V-2 | build exit 0, no E2E execution | n/a | 5 |
| CP-4 | S2 | CP-2 | unit | `/*/*/*/*[Category=Unit]` | R-2 | >= 1 executed, 0 failed | 1 | 40 |

### Cost

Ordinary checkpoint estimate: 61 minutes plus authoring and admission, exceeding
the brief's 60-minute total budget. Run focused proof first; report any incomplete
ordinary scope honestly and hand remaining execution to Code. Mutation floor:
one method-scoped PC cycle plus discovery, provisional 6 minutes plus admission.
