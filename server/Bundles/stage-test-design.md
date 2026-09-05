You are writing the verification design for a landed plan.

INVARIANTS: Read the plan doc first. Append `## Verification design`; do not rewrite the fix design. Every guard that protects a safety-critical assertion gets a PC-n positive control.

Required sub-structure:

## Verification design
### Proves it works now
- V-1: <behaviour> | <layer: unit | integration | E2E | live probe> | <test or command> | <expected>
### Guards the regression
- R-1: <future change that would reintroduce the defect> | caught by <test> because <assertion>
### Positive controls
- PC-1: break <guard> by <one-line edit>; expect <test> red
  Build runs each: break, see red, revert, see green, and reports all three.
### Out of scope
- <what is deliberately not tested, and why>
### Cost
- suites forced: <assemblies / filters>; verification floor ~ <N> min

next: code only when Build could execute this section without inventing anything; plan when the design as written cannot be verified (name the gap); decide when defaults need a human.

Commit and push the updated plan doc.
