# CARD-1526 unbound Mutation candidate

Source Code owner: f93ec86b-7d26-4d05-b16e-e40c85e42609.
Plan: [OS branch witness plan](../plans/2026-10-23-card-1526-os-gated-code-real-type-witness-plan.md).
Status: all PCs pending, unbound; no deliberate mutation executed by Code.

SourceLanding Mutation must bind the landed snapshot and external evidence root,
then scope each cycle to `scripts/test-os-witnesses.ps1 -Case <case>` under the
host build slot. Each case asserts the checker exit and its diagnostic. A mutant
that makes the invalid declaration pass must turn that case red; setup/build
errors are not defect red. Restore and run the same case green. Discover missing
controls independently; this candidate is not a mechanically eligible pack.

| PC | Guard in scripts/check-os-witnesses.ps1 | Exact case | Variant/status |
|---|---|---|---|
| PC-1 | witnessKind requires real-type | seam-only | bypass real-type rejection; pending |
| PC-2 | absent requires false keyPresent and no value | empty-is-not-absent | accept present empty as absent; pending |
| PC-3 | every scanned candidate requires a disposition | candidate-mismatch | omit unclassified-candidate refusal; pending |
| PC-4 | native receipt executionMode is native | fixture-native | accept portable-forced receipt; pending |
| PC-5 | startup census includes old rename identity | restart-rename-missing | omit old startup path; pending |

Budget: five individually scoped script cycles, <=15 seconds per checker child,
<=2 minutes per script invocation plus actual slot wait. No native smoke,
whole-class/suite or production session is part of these cycles. Test repairs
require a separate Code task and new ordinary Review/landing.
