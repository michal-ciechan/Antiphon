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
One isolated build + exact filter per row; union=whole ordinary scope. Min=--min-executed. EstimatedMinutes target 2x p95. total=max(30,2*sum+10) else totalTimeoutOverrideReason. Unit shards share a build; per-row TRX. Schema: docs/testing-and-build.md.
### Cost
- Ordinary V/R floor (Code)=sum EstimatedMinutes; PC floor (Mutation). Name filters/minutes. Total=build/setup+V/R+PC red/restore/green; label estimated/measured; quantify savings or justify zero.
- Process starts (docs/test-performance-standards.md R10): child processes (git, dotnet, CLIs) usually dominate a test's time, so estimate them per test, setup and code under test alike, and keep them proportional to what the test proves. Build fixture state once or in process (library, cloned template, shared world), not by replaying CLI scripts per test. Where code asks one question through several commands, prefer an intent-level interface (e.g. IGitRepository) that answers the fastest correct way, in process (libgit2) by default with the CLI as fallback. Most tests use that interface: a fake where the behaviour under test is ours, the in-process implementation where real repository state matters; build on libgit2 so tests stay fast. Assert the question or outcome, not exact command text. A deadline in the code under test must not make the test pass or fail by host speed: fix the clock or make the work fit; never raise the product budget for a test. Real CLI processes belong only in tests whose subject is the CLI behaviour, in their own test project and slice: one shared contract (same methods, same expectations, shared assertions) runs once in a project against the in-process implementation and once in a separate project against the real CLI. A missing seam or a deadline that cannot fit is a fix-design gap: record it and route next: plan; do not redesign the fix here.

Before handoff: guards=N, mapped=N, missing=0, duplicate PC maps=0; all PCs run; numeric Cost.
next: code when complete; plan for unverifiable seams; decide for human choices. Commit and push plan.
