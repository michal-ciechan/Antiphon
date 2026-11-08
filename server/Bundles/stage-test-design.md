Design verification for the landed plan. Append structure; do not rewrite the fix design.
Every guard that protects a safety-critical assertion gets a PC-n positive control.
Read changed tests and nearest fixture. Require recipe/variant/case/oracle/reachability/classification. Unbound payloads: binding required after Code.

## Verification design
### Inspection
- <bodies read> | <boundaries -> V/R IDs or exclusion>
### Delivery inventory
For each new/changed async delivery path enumerate producer, destination, persistence boundary, recovery and observable receipt by durable identity. Require a producer-to-recipient test via the real queue: busy, already eligible, crash/enqueue-failure. A request, queue insert, event, Sent flag or ack never proves delivery. Session input needs the matching complete UserPrompt transcript. Declare substitutes; reject designs stopping before recipient evidence. Each safety-critical delivery/recovery guard needs a named positive control.
### Proves it works now
- V-1: <behaviour> | <layer> | <test/command> | <expected>
### Guards the regression
- R-1: <regression> | <test and decisive assertion>
### Guard inventory
- G-1: <plan ref + safety-critical guard/invariant> | PC-1
List all safety-critical guards, incl. untested. Map 1:1 to distinct PC-n. Justify none.
### Positive controls
- PC-1: break <G-1> by <compiling defect>; expect <exact method> red at <assertion>.
  Mutation runs break/red/restore/green after land; Code runs V/R; Review judges before land.
### Out of scope
- <exclusion and reason>
### Existing tests affected
For migrations, public DTOs/routes, operation labels, remote script functions and statement budgets. Name classes (CARD-1782) or none.
### Checkpoints
| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
One isolated build + exact filter per row; union=whole ordinary scope. Min=-MinExecuted. EstimatedMinutes target 2x p95. total=max(30,2*sum+10) else totalTimeoutOverrideReason. Unit shards share a build; per-row TRX. Schema: docs/testing-and-build.md.
### Cost
- Ordinary V/R floor (Code)=sum EstimatedMinutes; PC floor (Mutation). Name filters/minutes. Total=build/setup+V/R+PC red/restore/green; label estimated/measured; quantify savings or justify zero.

Before handoff: guards=N, mapped=N, missing=0, duplicate PC maps=0; all PCs run; numeric Cost.
next: code when complete; plan for unverifiable seams; decide for human choices. Commit and push plan.
