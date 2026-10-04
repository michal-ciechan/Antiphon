# CARD-0884 instruction bundle argv headroom repair

Code owner: fcc274e3-c53a-46f0-b262-e752349292a1 (original landing owner).
Task base: fd0eef7157136446653e583d916366e38e2acb47.
Round: Final. The stage contract requires the whole Unit lane despite the urgent
brief's narrower request. No delivery, landing, lease or persistence implementation
changes; no integration classes are affected. The affected classes below are Unit.

## Implementation

S1 compresses five existing orchestrator paragraphs while retaining all rules,
restores the pre-growth 14,310-character size ceiling, updates the exact raw-byte
pin, and strengthens the existing argv-budget test to reserve 500 characters.
No production budget, timeout or assertion is loosened.

## Verification design

V-1: whole InstructionBundleTests class, including all non-specialist role/kind
compositions with board API, explanatory style, CRLF Telegram append and six other argv
entries. The real budget guard must accept them at budget minus 500. The original
30,001-character estimate fails this guard at both 30,000 and 29,500; the repaired
worst case is 29,295. ChannelPreamble uses AppendLine, so the CRLF append exercises
the larger Windows estimate on Linux too. The unmodified baseline test passes on
Linux at its old 30,000 guard; the Windows estimate is 30,001. LF and simulated
CRLF bundle size checks retain the old ceiling.

V-2: whole CheckpointRepeatDocumentationTests class (one method), including the
repeat_budget_reaches_code_briefs_without_bundle_growth raw SHA-256 pin.

R-1: whole Unit lane to catch other textual policy and composition pins. This is
the Final stage contract's required scope; no full assembly run is authorized or
needed. No unbounded class impact is inferred. Search tests/server for orchestrator
v7ac72f39 and old SHA prefixes 15f9660f/a33560cb; the only exact byte pin is V-2.

V-3 manual: compare every changed paragraph against the base and retain delegation,
provider incident handling, next-stage/report handling, continued pipeline action,
task-state observation and StartRef isolation/authority semantics. Record counts
for each paragraph and the normalized bundle tag. Run full base..HEAD evidence diff.

### Positive controls (pending SourceLanding Mutation)

PC-1: restore the original verbose orchestrator bytes while retaining the strengthened
test; exact method InstructionBundleTests.the_worst_case_composition_measured_sits_far_under_the_budget
must refuse the 29,500 estimate. Mutation owns intended red/restore/fresh green.
PC-2: change one orchestrator byte; exact method
CheckpointRepeatDocumentationTests.repeat_budget_reaches_code_briefs_without_bundle_growth
must fail orchestrator-approved-bytes. Mutation owns this cycle and missing-control discovery.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-fcc274e3/` | instruction-bundles | `/*/*/InstructionBundleTests/*` | V-1 | all listed, 0 failed | 1 | 4 |
| CP-2 | S1 | CP-1 | repeat-byte-pin | `/*/*/CheckpointRepeatDocumentationTests/*` | V-2 | all listed, 0 failed | 1 | 1 |
| CP-3 | S1 | CP-1 | unit-final | `/*/*/*/*[Category=Unit]` | R-1 | >= 1 executed, 0 failed | 1 | 6 |

### Cost

Ordinary checkpoint floor: 11 minutes; authoring/manual acceptance: 10 minutes.
PC floor: 4 minutes plus discovery, owned by post-land Mutation. Run once; green
requires no repetitions. Failure-driven reruns retain exact selection and budget.
Restart: server, owned by caller after ordinary Review and confirmed landing.
