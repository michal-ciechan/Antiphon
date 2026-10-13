# CARD-1032 S7 report-loss repair Mutation candidates

Code / landing owner: 60025819-39c5-49e6-8951-817cdeeff5fc.
Implementation tested at 78fcc1e2f346fb797ee042fdb97b5338a23af5de.
These are unbound candidates, not deliberate-mutant execution evidence.
SourceLanding Mutation is commissioned by the caller after Review and land.
Bind compile-safe, method-scoped patches on that landed source. Never widen
timeouts or loosen assertions; a build/fixture error or zero tests is not red.

| PC | Guard / candidate | Exact detecting filter | Pending named variants |
|---|---|---|---|
| PC-591 | Shared producer/runner task-report recognition | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportRecognitionCaseTable` | lowercase, uppercase, mixed case, case-varied directory/extension, normalized separator/./, nonhex and Unicode identifiers, terminal LF; other extensions, directory, ADS-like suffix, trailing dot/space, wrong length/brief/nesting/escape and Unicode lookalikes use the literal 24-row table; Windows filename normalization remains native pending |
| PC-592 | Fresh recipient report hold in S5 no-follow inventory | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportAddedAfterProducerInspection` | lower/upper/mixed report added at the explicit post-intent gate; exact bytes/registration must remain, no imported Complete receipt |
| PC-592 | Recipient report hold with otherwise admitted artifacts | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalEligiblePhysical` | lower/upper/mixed unpreserved report; exact bytes and no set-aside/receipt; discover separate missing controls for prepare, rename, unregister, delete and replay rather than masking a bypass with a later guard |
| PC-593 | Eligible cleanup when task report is absent | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportAbsentAllowsCleanup` | real physical directory/registration removal and imported matching receipt; generic admitted TRX evidence remains present before removal |

PC-582 keeps missing report/artifact/policy/pointer authority controls under
C1032_RemovalEligiblePhysical. Its eligible positive now uses complete.trx;
all physical-removal/receipt/ref/sentinel assertions are retained. Generic
artifact authority cannot prove remote task-report bytes without a per-report
manifest, so the old positive expecting their deletion was invalid for this repair.

PC-588..590 and every earlier plan PC/variant remain pending. Ordinary baseline
red at c82b985895892533d517521e8efc138a603e0a8c and Final green do not discharge
any control. Mutation owns deliberate red/restore/green and missing-control
discovery. Native Windows qualification and S8 obligations remain separate.

## S7 F4 negative-witness candidates

Code task 55328335 (landing owner 60025819) built and executed the scratch mutants VP-08/09/10/13/15 on its Code branch; each turned
its witness red on the first vector reached and was reverted without being
committed. That is Code evidence, not formal post-land SourceLanding Mutation
qualification; it discharges no PC.

| PC | Guard / candidate | Exact detecting filter | Pending named variants |
|---|---|---|---|
| PC-595 | Landing MAC covers Binding.TaskId | `/*/*/CompletedCardWorkspaceContractTests/C1032_LandingSignatureCoversTaskId` | other TaskId, empty TaskId |
| PC-596 | Publication scoped to owning task | `/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessLandingOwnership` | foreign task; missing-control discovery for foreign store path, repository, earlier generation |
| PC-597 | Complete landing evidence required | `/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessLandingCompleteness` | 13 vectors across 11 fields |
| PC-598 | Replay checks RequestDigest | `/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessPersistedReplayTamper` | report digest, policy digest, binding worktree path, expected HEAD |
| PC-599 | Unsupported removal reply fails closed | `/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessUnsupportedRemovalAfterInspection` | capability-withdrawal unsupported (client-preflight result, no recipient removal reply), intercepted unsupported; discovery for unknown and no-reason refusals |
