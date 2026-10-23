# CARD-1526 unbound Mutation candidate

Source Code owner: f93ec86b-7d26-4d05-b16e-e40c85e42609.
Repair / landing owner: 15cca245-0f8c-4e01-bf7a-2450326b6113.
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
| PC-6 | pinned test content validates type, factory, test declaration and setup | test-seam-only; test-always-assigned; test-comment-only; test-case-missing; test-comment-strings | drop test-content validation; five individually scoped variants pending |
| PC-7 | pinned test path must exist | missing-path | drop the test-file read, as exercised by Review e9c9a232; pending |
| PC-2b | optional inputs include an absent state | absent-case-required | drop absent-input requirement, as exercised by Review e9c9a232; pending |
| PC-6b | comments cannot supply test declarations or executable fragments | test-line-comment-strings; test-hash-comment-strings | retain comments; two individually scoped variants pending |
| PC-6c | exact factory and method declarations | test-factory-missing; test-name-call-only; test-name-prefix | bypass fragment/declaration checks; three individually scoped variants pending |
| PC-6d | read test contents from the declared Git object | pinned-test-ignores-worktree; pinned-test-rejects-worktree-repair | read worktree test instead; two individually scoped variants pending |
| PC-8 | reject unterminated blocks with their kind before matching | test-unterminated-csharp-block; test-unterminated-powershell-block; test-unterminated-block-after-code | drop unterminated-block refusal; three individually scoped variants pending |
| PC-9 | PowerShell block depth survives an inner closer | test-nested-powershell-block; test-nested-powershell-inner-close-only | drop nesting; two individually scoped variants pending |
| PC-10 | optional absent value is read only after ContainsKey | strict-valid-absent | drop strict-mode guard; pending |

Budget: twenty-five individually scoped script cycles, <=15 seconds per checker child,
<=2 minutes per script invocation plus actual slot wait. No native smoke,
whole-class/suite or production session is part of these cycles. Test repairs
require a separate Code task and new ordinary Review/landing.

Repair 2 source: task 663fcd39-a18d-4831-a116-dc324df29b15, re-cut from
master while retaining the original Code/landing owners above. PC-8/9/10 are
the three requested repair controls, reserved for Mutation by the Code stage
contract. Review 6690773f demonstrated the surviving comment bypass. Its finding
justifies new regression cases, not extra unchanged green repetitions.
