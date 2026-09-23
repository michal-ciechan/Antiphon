Design verification for the landed plan. Append this structure; do not rewrite the fix design.
Every safety-critical guard gets a PC-n positive control.
Read touched tests/fixtures/helpers before naming cases, the nearest fixture for new files. Record missing setup; cover boundary combinations or justify exclusion.

## Verification design
### Inspection
- <test/fixture bodies read> | <boundaries -> V/R IDs or exclusion>
### Delivery inventory
For each new/changed async outcome-delivery path enumerate producer, destination, persistence boundary, recovery and observable receipt, joined by the durable identity. Include a producer-to-recipient test through the real queue: busy recipient, one already eligible, crash/enqueue-failure recovery at each handoff. A request, queue insert, event, Sent flag or transport ack never proves delivery. Session input needs the matching complete UserPrompt transcript. Declare substitutes and what each cannot prove. Reject a design that stops before recipient evidence. Every safety-critical delivery/recovery guard needs a named positive control.
### Proves it works now
- V-1: <behaviour> | <layer> | <test/command> | <expected>
### Guards the regression
- R-1: <regression> | <test and decisive assertion>
### Guard inventory
- G-1: <plan ref + safety-critical guard/invariant> | PC-1
Inventory every safety-critical guard, incl. untested; split independently bypassable guards. Map each 1:1 to a distinct PC-n. Justify none.
### Positive controls
- PC-1: break <G-1> by <compiling defect>; expect <exact method> red at <assertion>.
  Mutation runs break/red/restore/green after land; Code runs V/R; Review judges before land.
### Out of scope
- <exclusion and reason>
### Checkpoints
| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
One isolated build + one exact filter per row; union = whole ordinary scope. Min=-MinExecuted (TUnit executions; n/a for non-TUnit). EstimatedMinutes=time. Schema: docs/testing-and-build.md.
### Cost
- Separate floors: ordinary V/R (Code) = sum of CP EstimatedMinutes; PC (Mutation). Name filters and minutes. Total = setup/build + V/R + each PC red/restore/green; label estimated/measured; quantify savings or justify zero.

Before handoff: bodies read; guards=N, mapped=N, missing=0, duplicate PC mappings=0; all PCs executable; numeric Cost. No placeholders/TBD.
next: code only when complete; plan for an unverifiable seam; decide for a human choice. Commit and push the plan doc.
