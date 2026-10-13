# CARD-1032 S7 Code Final selection

Original Code / landing owner: f2b2ece7-37bf-4c83-9b74-cfb9e78c1eae.
Base: 63b2e70479c846cacd01af6b20190e845f8258e4.
Implementation remains uncomposed: no Program registration, sweep caller, public
route, Execute setting change, or live workspace deletion is assigned.
S8 CP-15/16 and recovery cut methods remain outside this slice.

The higher-priority Final profile requires the complete Unit lane and affected
integration classes in addition to the caller's CP-13/14. These additional rows
are declared here before execution; their presence does not commission S8 code.
No full assembly or namespace run is needed. Estimated additional cost is one
runner build plus 8 minutes tests, excluding slot wait. CP-13/14 retain the
frozen test-design filters and counts. Build outputs remain isolated, UseAppHost
is false on Linux, and each driver takes its own host slot.

Coverage: V-1 exact task/attempt/store endpoints and generation; V-2 complete
bound command/receipt and unsupported peer; V-3 exact inspected H, actual rebase
publication and external artifacts; V-6 commit before real phone-home send and
independent outcome read; R-3 mirror admission/receipt compatibility. R-1/R-2
adjacent runner and local cleanup classes preserve the preceding slice's behavior.
V-4/V-5 are adjacent runner regression coverage, not a new native qualification.

C1032_AdmissionBoundRebaseProof supplies a real Git landing and real recipient
positive, plus unsigned/tampered certificate negatives. The producer receives
only endpoint/run IDs. It reads the binding and server publication and signs
only its internally assembled complete command with configured phone-home
credentials. No key is carried in the wire payload. Three-argument transport
and one-argument inspector constructors retain their inert rebase default.
C1032_AdmissionArtifactSurvival uses safe regular-file reads through the existing
ChannelReplyAttachmentReader. Remote task-report inventory is held because the
inspection contract supplies no remote report bytes. Directory deliverable
pointers are conservatively held by this regular-file proof; they are not copied.

Deletion paths touched: server discovery/admission, signer and artifact proof
have no filesystem deletion. Transport calls RemoveAsync, whose pre-effect
binding/source/proof/inventory checks precede TryRetireAsync (shared gate and
journal-before-effect). Its S5 resume path validates recorded root/file identities
before rename, exact admin deletion and exact aside deletion. Rebased proof now
also requires the configured certificate key; fresh destination containment of
P and exact inspected H remain required. No branch deletion or repository prune
is added. Test disposal calls the existing fixture-owner-checked no-follow root
cleanup; explicit report deletion affects only a fixture's external report.

Ordinary baseline 27bc01d862690c443e6fd557a46f89e82cf367ab: CP-13 0/7 and
CP-14 0/7, all failing `S7 must discover independently bound mirror endpoints`.
Source was clean and both builds were verified. This is pre-slice defect evidence,
not a deliberate mutant. Code's stage contract assigns deliberate mutants to
Mutation, so caller-requested scratch family mutants are not run in Code.

Pending Mutation: all plan PCs and named variants remain pending, including
PC-60's moved rebase positive, PC-73..78/80..81, PC-271..283, PC-293,
PC-380..406 (each receipt field), PC-506..508, and PC-581.
New binding candidates: PC-588 omit certificate verification (CardId,
DoneRevisionId, TargetId, EndpointId, OperationId, ReportDigest,
ArtifactProofDigest, DisposalPolicyDigest and unsigned-map variants) under
`/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionBoundRebaseProof*`;
PC-589 admit missing receipt fields (each of the 27 fields) under the exact
seven CP-14 methods; PC-590 omit safe regular-file external artifact reads under
`/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionArtifactSurvival*`.
These are pending binding candidates, not red/restore/green evidence.
Unbound Mutation candidate reports belong in docs/superpowers/mutations/.

Windows Final Review remains pending on these exact existing methods:
`/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateFileIdentityRefuses*`,
`/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateFileContentRefuses*`,
`/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateRootIdentityRefuses*`.
Linux execution does not qualify Windows handle/share/reparse semantics.
Restart: none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-13 | S7 | `tests/Antiphon.Tests -> bin-c1032-admission/` | mirror-admission | `/*/*/CardWorktreeMirrorCleanupTests/(C1017_MirrorBehindPushedTip)\|(C1017_ReopenBetweenEndpoints)\|(C1017_MultipleMirrorsRemainDistinct)\|(C1032_Admission*)` | V-1, V-3, V-6, R-3 | all 7 listed including C1032_AdmissionBoundRebaseProof; 0 failed/skipped | 7 | 6 | true |
| CP-14 | S7 | `tests/Antiphon.Tests -> bin-c1032-receipt/` | mirror-receipt | `/*/*/CardWorktreeMirrorCleanupTests/(C1017_ReceiptMustMatchOperation)\|(C1017_ReceiptMustMatchEndpoint)\|(C1017_ReceiptMustMatchStore)\|(C1017_ReceiptMustMatchTaskAttempt)\|(C1017_AckIsNotRemoval)\|(C1017_UnsupportedNeverFallsBack)\|(C1032_Receipt*)` | V-2, V-6, R-3 | all 7 listed; 0 failed/skipped | 7 | 6 | true |
| CP-F1 | S7 | CP-14 | unit | `/*/*/*/*[Category=Unit]` | Final Unit | whole Unit lane; no new failures | 1 | 3 | true |
| CP-F2 | S7 | CP-14 | local-adjacent | `/*/*/(CardWorktreeCleanupRecoveryTests*)\|(CompletedCardWorktreeRemovalTests*)\|(CardWorktreeMirrorCleanupTests*)/*` | V-1..6, R-1..3 | all three complete classes; 0 failed/skipped | 1 | 4 | true |
| CP-F3 | S7 | `tests/Antiphon.SessionRunner.Tests -> bin-f2b2-runner-final/` | runner-adjacent | `/*/*/(CompletedCardWorkspaceRemovalTests*)\|(CompletedCardWorkspaceContractTests*)\|(RunnerWorkspaceServiceTests*)\|(LegacyWorkspaceRemovalGateTests*)/*` | V-2..5, R-1, R-2 | all four complete classes; no new failures | 1 | 4 | true |
