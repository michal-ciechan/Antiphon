# CARD-1609: content-derived volume retirement

Status: early draft, investigation and verification design in progress. Plan task
`dcbf1d18`; assigned base `f8bddb6efda7b0e8f50f3b45744087e52188bc66`.

## Problem and evidence

Retired volumes must be removable using current audit tooling when their content
is proven safe. CARD-1609, CARD-1607 and CARD-1594 were read from the Antiphon
board on 2026-10-29. Current retirement couples content audit to cleanup receipt,
Compose generation and image identity. The manual 2026-10-29 prototype and the
CARD-1607 branch are being examined before this draft is finalized.

## Ground truth

| Card assumption | Code at assigned base |
|---|---|
| Historical image/receipt binding blocks safe cleanup | `c1008_recycle`, `c994_lookup_image` and `c1008_bind_generation` bind cleanup context before audit. |
| Publication is only branch tips | Existing `c1008_git_program` also protects reflogs, pseudorefs, linked admin directories and non-commit objects; retain those protections. |
| Preview authorizes apply | Current preview grants no apply authority; introduce an explicit current proof receipt. |
| CARD-1607 host proof is available at base | `scripts/c1008-host-proof.ps1` is absent; read cited branch without merging it. |

## Decisions

- D-1: retain content safety, exact volume incarnation, current operation and live
  retirement guards. Rejected: treating build provenance as deletion authority.
- D-2: use current trusted tooling, resolved once per operation. Historical SHA
  and creating-image digest become provenance. Preserve current-tool identity for
  diagnostics and policy-version compatibility.
- D-3: manual operator attestation cannot waive unpublished/unknown/dirty findings.
  Rejected: force-delete or missing-receipt bypass of content inspection.

## Performance budget

No builds or tests run in this Plan task. The implementation budget will distinguish
build/slot wait, inventory, publication proof, content checks, teardown and deletion.
Existing production audit measurements (5225 s for 800 repositories; 1404 s for
OLD-volume replay at load 17, docs/docker-stack.md) are not test p95 estimates.
Final verification selections and provisional measurement obligations follow in
the completed plan under docs/testing-and-build.md#performance-standards.

## Verification design

Pending final witness roster and checkpoint manifest. Required behavior includes
successful retirement without a historical receipt, refusal for unpublished or
unknown content, read-only audit isolation, receipt freshness/incarnation binding,
operator attestation, exact-name deletion and interrupted-operation recovery.

## Review handoff

This early draft is not implementation approval. Final artifact requires an
independent design review using a different company's model as requested in the
brief; subsequent TestDesign must settle executable verification details.
