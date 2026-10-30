# C1105 and load flakes

C1105 is a **confirmed test-fixture defect**. A separate fresh preview failure is a **confirmed environment prerequisite failure**. Investigation only: no production or test repair, definition-file change, graph approval regeneration or deadline change was made.

Full file:line citations, retained transcripts, commands, failed attempts and uncertainties are in [the detailed CARD-1105 investigation](2026-10-30-card-1105-c1105-red-on-master-and-the-load-flakes-classify-and-fix.md).

| Test | Cause class | Evidence | Fix | Owner | Risk / next evidence |
|---|---|---|---|---|---|
| C1105 resume preserves proof | TEST DEFECT, confirmed | Extracted fixture omits `c1008_forward_host_proof`; same failure on named master and rc; previous parent passes. Audit family 164/165, only resume red | None; next Plan | Resume fixture / CARD-1607 integration | Existing proof remains intact; successful resume arm is untested until the witness is repaired |
| C1364 retire-volumes waited | Original failure unresolved | Historical exit 139; fresh exact method 1/1 and full class 6/6 | None | Script harness / PowerShell runtime | Obtain first failing child stderr and crash evidence; do not call exit 139 a timeout |
| C1008 legacy failing-shell | Original failure unresolved | Fresh four-mode method 4/4; full Rolling class 77/77 in two bounded groups | None | Rolling harness | Preserve failed roster assertion, child exit and duration; 400-second per-mode deadline is distinct from the shell harness |
| C1008 dry-run receipt | Fresh ENVIRONMENT failure confirmed; original broken pipe unresolved | Child could not resolve `chroot`, exit 127 in 0.105 s; unchanged test passes with child-only `/usr/sbin:/sbin` PATH qualification | No source change | Runner child prerequisite / preview fixture | New failure differs from original broken pipe; retrieve its producer/consumer trace |
| C1385 graph registry | Reported copy safety refusal reconstructed; underlying delay unresolved | Clean runs pass; 9/9 graph safety guards pass. Natural-load replay sampled 13.89–17.96, below the reported ~21 | None; keep **5 s Linux copy bound** | LandGraphAudit | Copy timeout must refuse approval; retain contemporaneous phase/IO/scheduling timing |

One leased build: 0 errors. Failed and interrupted setup attempts remain in the detailed ledger. All 34 owned alternate-output directories were removed after all runs ended. The branch has four inherited rc/master merge conflicts, reproduced at the assigned base; no merge or rebase was attempted.

--- next stage ---
next: plan
handoff: Use the confirmed C1105 fixture mechanism for Plan; retain the original load incidents as evidence-gathering follow-ups and preserve the graph-copy safety bound.
artifact: docs/investigations/2026-10-30-card-1105-c1105-red-on-master-and-the-load-flakes-classify-and-fix.md
