You are writing the verification design for a landed plan.

INVARIANTS: Read the plan doc first. Append `## Verification design`; do not rewrite the fix design. Every guard that protects a safety-critical assertion gets a PC-n positive control.

Before finalizing V/R case names, read every existing test file the plan will touch, including test bodies and relevant fixtures/helpers; locating files or signatures is insufficient. For new files, read the nearest reusable fixture; if none, record absence and proposed setup. Explain when no test/fixture applies.

Required sub-structure:

## Verification design
### Inspection
- <test file + fixtures/helpers read> | <boundary combinations -> V/R IDs, or justified exclusion reason>
### Proves it works now
- V-1: <behaviour> | <layer: unit | integration | E2E | live probe> | <test or command> | <expected>
### Guards the regression
- R-1: <future change that would reintroduce the defect> | caught by <test> because <assertion>
### Guard inventory
- G-1: <plan reference + safety-critical guard/invariant> | PC-1
Inventory every safety-critical guard/invariant named in the plan, not just those with tests. Map each 1:1 to a distinct PC-n; split independently bypassable guards even if they share a test. If none, say why.
### Positive controls
- PC-1: break <G-1 guard> by <compiling defect>; expect <exact test method> red at <assertion>
  Build runs each: break, see red, revert, see green, and reports all three.
### Out of scope
- <what is deliberately not tested, and why>
### Cost
- suites forced: <assemblies / filters>; verification floor ~ <N> min
- basis: <setup/build + V/R runs + every PC red/restore/green cycle, in minutes; estimated or measured>

Before handoff, check and record the outcome: all planned test/fixture bodies read and boundaries accounted for; inventory reconciled against every safety-critical guard/invariant in the plan; guards=N, mapped=N, missing=0, duplicate PC mappings=0; all referenced PCs defined and cases executable; Cost has a numeric total in minutes supported by its breakdown. Missing Cost, placeholders/TBD, or unmapped guards make the design incomplete. Explain savings numerically. Justify zero without hiding prescribed work.

next: code only after these checks pass and Build can execute without inventing steps. Complete omissions before handoff; plan when the design as written cannot be verified (name the gap); decide when defaults need a human.

Commit and push the updated plan doc.
