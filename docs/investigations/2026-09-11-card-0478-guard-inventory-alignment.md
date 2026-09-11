# CARD-0478 guard/PC inventory alignment

Date: 2026-09-11. Continuation of application custody Code on `feat/card-task-48888860`
from checkpoint `24431612`.

All **230 planned PCs remain pending** for post-land Mutation. This record maps ordinary
Code tests to the plan's G/PC rows and names independently bypassable safeguards that
already have plan IDs.

## Extra independently bypassable safeguard

`VerificationExecutionBinding.RunnerStoreId` is required by the host/runner contract and
persisted in B before runner I/O. It is not a new G-n: it is **G-209 / PC-209**
(`R.C478_G209_StoreIdentity`). Ordinary importer policy already rejects a crossed store
in `PostLandMutationReceiptPolicyTests`. Do not add G-231 for the same invariant.

A reserved B that never reached a host is **Unknown**, not NeverStarted. Empty history is
cleanup-eligible only when the irreversible seal records `NeverReserved`
(`C478_G224_EmptyHistory`). Those remain distinct.

## Named ordinary methods added this pass

Publication G-27..G-55 and V-02 live in `PostLandMutationPublicationTests`.
Admission V-01 and G-1..G-7, G-11, G-12, G-14..G-16, G-21..G-26 live in
`PostLandMutationAdmissionTests`. Workflow V-07/V-08/V-10/V-11 and G-124/G-126/G-129
live in `PostLandMutationWorkflowTests`. Delivery V-09a/b/c live in
`PostLandMutationDeliveryTests`. Worktree V-02/V-03/V-05 and G-56/G-74 live in
`PostLandMutationWorktreeTests`. Cleanup V-06 and G-88/G-101/G-228 live in
`PostLandMutationCleanupTests`. Custody V-13 and G-183/G-184/G-186/G-189/G-224 plus
typed execution detail live in `PostLandMutationCustodyTests`. Contract G-162/G-163/
G-166/G-167/G-178/G-230 and V-11 alias live in `PostLandMutationContractTests`.
Native V-14 and G-192/G-193/G-195 live in `PtyCustodyTests`; host intermediary spawn
and G-213 live in `HostCustodyTests`.

Continuation `27871064` added the remaining named ordinary methods through G-230
and the V-4 / V-9 crash / V-15 / V-16 scenario methods. Cleanup G-91..G-97 and
several G-108+ rows currently alias a sibling first-boundary refusal rather than
corrupting only that coordinate; Review should treat those as named-method
coverage, not independently falsifiable PC oracles. Loaded V-12 selector/bundle
probes after canonical deployment remain caller-owned. All **230 PCs remain
pending**.

## Unresolved-release ownership

Sourced `killSession:false` recovery keeps the pool delegate `Running`, assigned to the
terminal sourced task, with residue `verification_release_unresolved`. The janitor skips
that owner. After every attempt has an imported `Exited`/`NeverStarted` receipt, or a
fenced NeverReserved seal, the existing authorized stop path may retire it
(`verification_release_stopped`). Cleanup still never kills.
