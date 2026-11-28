Append verification to the landed plan. Preserve the fix design.
Every guard that protects a safety-critical assertion gets a PC-n positive control.
Read changed tests + nearest fixture. Require recipe/variant/case/oracle/reachability/classification. Unbound payloads: binding required after Code.

## Verification design
### Inspection
- <bodies read> | <boundaries -> V/R IDs or exclusion>
### Delivery inventory
Each new/changed async delivery: producer, destination, persistence boundary, recovery, observable receipt by durable identity. Require real-queue producer-to-recipient tests: busy, already eligible, crash/enqueue-failure. Request/queue insert/event/Sent/ack are insufficient. Session input requires matching complete UserPrompt transcript. Declare substitutes. Reject missing recipient evidence. Add a named positive control per safety-critical delivery/recovery guard.
### Proves it works now
- V-1: <behaviour> | <layer> | <test/command> | <expected>
### Guards the regression
- R-1: <regression> | <test and decisive assertion>
### Guard inventory
- G-1: <plan ref + safety-critical guard/invariant> | PC-1
List every safety-critical guard incl. untested. Map distinct PC-n 1:1. Justify zero.
### Positive controls
- PC-1: break <G-1> by <compiling defect>; expect <exact method> red at <assertion>.
  Mutation runs break/red/restore/green after land; Code runs V/R; Review judges pre-land.
### Out of scope
- <exclusion and reason>
### Existing tests affected
Name classes (CARD-1782) or none for migrations, public DTOs/routes, operation labels, remote script functions and statement budgets.
### Checkpoints
| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
One isolated build + exact filter per row; union=ordinary scope. Min=--min-executed. EstimatedMinutes target 2x p95. total=max(30,2*sum+10) else totalTimeoutOverrideReason. Unit shards share a build; per-row TRX. Schema: docs/testing-and-build.md.
### Cost
- ordinary V/R floor (Code)=sum EstimatedMinutes; PC floor (Mutation); filters/minutes. Total=build/setup+V/R+PC red/restore/green (estimated/measured). Quantify savings or justify zero.
- Apply docs/test-performance-standards.md#r10-cheap-seams. Count setup/code process starts per test. Record seam/budget gaps.

Handoff: guards=N, mapped=N, missing=0, duplicate PC maps=0; all PCs run; numeric Cost.
next: code if complete; plan for seam/budget gaps; decide for human choices. Commit/push plan.
