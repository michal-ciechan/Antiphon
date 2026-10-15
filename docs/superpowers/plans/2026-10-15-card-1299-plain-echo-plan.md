# CARD-1299 plain echo precedence

Code owner/landing owner: 095a7f1a-4260-43dc-b1dd-f0505eb03695.
Base: 067b47e2dc903247f1a7c8aa539032cc4266e4ad.
Branch: feat/card-task-095a7f1a. Round: Final, with the explicit CARD-1299 brief's
bounded verification override: no whole Unit lane (CARD-1287), no full assembly.
This work targets master and the next release cut, not release/2026-10-15.

## Implementation

S1 commits the reviewer's exact synthetic witness as a detecting test before the
production fix. Prompt Run abc?; raw C# string
`"Run\u001b[1;5Habc?\r\n> Run abc?\r\nDone."` must produce Done. and false for
IsAskingQuestion, directly and through RunnerCodexAdapter's transcript-completion
raw fallback. RawSnapshot must remain unchanged and TurnCompleted true.

S2 changes only CodexResponseAnalyzer.ExtractResponse in
src/Antiphon.Agents.Pty/CodexDetectors.cs: prefer a plain-stripped echo match; only
search the cursor-separated copy when plain misses. Preserve the separated copy
when neither matches, retaining answer word boundaries. Grok/OpenCode default
behavior and the existing echo matching algorithm remain unchanged.

## Verification design

- V-1: CodexResponseAnalyzerTests.Plain_echo_takes_precedence_over_an_earlier_cursor_separated_match
  fails on the original production implementation and passes after S2. Its shared
  helper asserts exact answer, question verdict, adapter result and raw diagnostics.
- R-1: complete CARD-0907 CP-3 parser classes; now 74 results including V-1.
- R-2: complete CARD-0907 CP-6 consumer classes, 21 results, including persisted
  subscription-monitor behavior and runner completion.
- R-3: complete CARD-0907 CP-7 caller classes, 20 results; Codex opt-in and
  Grok/OpenCode compatibility.
- R-4: source-reader/classification guards, full classes. Grep found no literal
  CodexDetectors.cs reader; TestLaneCategoryGuardTests and DelegationHarnessCensusTests
  enumerate/read every Antiphon.Tests C# file, including the changed test.
  TestClassificationGuardTests additionally checks compiled metadata. The two
  source censuses have documented inherited failures in CARD-1287; if reproduced,
  verify those exact failures at base, retain the red outcomes, and do not repair
  unrelated guards in this task.

No live CLI/manual acceptance is required for this synthetic parser regression.
Inspect fresh TRX identities and nonzero counts for every named class and V-1.
Validate clean final receipts against actual committed source. No repeat-proof
runs or deliberate mutants are commissioned. PC-1 (revert precedence to
separated-first) remains pending for method-scoped SourceLanding Mutation using
V-1; ordinary red-first evidence does not discharge it. Parent CARD-0907 PCs and
its outstanding Windows/capture obligations remain owned by that card.

### Checkpoints

One isolated test-project build per slice; all final filters reuse CP-3's build.
CP-1 is the intentionally red S1 row. CP-3 reruns that method within its full
class after the fix. CP-8 is the extra source-reader row required by the brief.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1299-red/` | precedence-red | `/*/*/CodexResponseAnalyzerTests/Plain_echo_takes_precedence_over_an_earlier_cursor_separated_match` | V-1 red-first | 1 expected assertion failure before S2 | 1 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1;C804_ORPHAN_SWEEP_ROOT=c1299-disabled` |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1299-final/` | parser | `/*/*/(CodexResponseAnalyzerTests*)\|(SubscriptionUsageParserTests*)\|(UsageLimitWallParserTests*)\|(ProviderQuotaRefusalTests*)/*` | V-1,R-1 | 74 executed, 0 failed/skipped | 74 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1;C804_ORPHAN_SWEEP_ROOT=c1299-disabled` |
| CP-6 | S2 | CP-3 | consumers | `/*/*/(RunnerCodexAdapterTurnCompleteTests*)\|(SubscriptionUsageMonitorTests*)/*` | R-2 | 21 executed, 0 failed/skipped | 21 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1;C804_ORPHAN_SWEEP_ROOT=c1299-disabled` |
| CP-7 | S2 | CP-3 | callers | `/*/*/(RunnerGrokAdapterTurnCompleteTests*)\|(OpenCodeAdapterTests*)\|(CodexAdapterRawResponseTests*)/*` | R-3 | 20 executed, 0 failed/skipped | 20 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1;C804_ORPHAN_SWEEP_ROOT=c1299-disabled` |
| CP-8 | S2 | CP-3 | source-readers | `/*/*/(TestLaneCategoryGuardTests*)\|(DelegationHarnessCensusTests*)\|(TestClassificationGuardTests*)/*` | R-4 | 9 executed; preserve and baseline any inherited red | 9 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1;C804_ORPHAN_SWEEP_ROOT=c1299-disabled` |

### Cost

Ordinary floor: 18 minutes plus authoring, tool bootstrap, queue and any exact
base-failure confirmation, within the 45-minute brief. No unbounded shared-impact
classes or full assembly runs. One leased checkpoint-tool bootstrap is necessary
if no local launcher exists. Run bound with expected-source-sha set to committed
HEAD; only a missing-token refusal permits the brief's foreground unbound fallback
using env -u ANTIPHON_TASK_ID. Wait until terminal, never leave an executor behind.

Restart after landing/activation: server. No runner restart is needed.
