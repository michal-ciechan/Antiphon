# CARD-1287 Q2 unbound Mutation candidate

Original Code/landing owner: `7a7b92ba-6819-4460-9776-a07787de775d`.
Plan: `docs/superpowers/plans/2026-10-15-card-1287-q2-windows-custody.md`.
This is not bound to a SourceLanding operation or snapshot.

The repair changes host path spelling in the virtual fixture and its custody and
receipt test callers. It changes no production guard, assertion expectation,
deadline, method roster or variant. The existing Windows tests fail before the
repair and must pass with their original assertions after it.

Every inherited PC-1..413 and every variant remains pending. In particular, the
custody bindings PC-261..312 and PC-403..411 remain as listed in
[the S1a0 candidate](2026-10-15-card-0987-s1a0-unbound.md), and receipt bindings
PC-313..341 and PC-412/413 remain as listed in
[the S1a1 candidate](2026-10-15-card-0987-s1a1-candidate.md).
The [frozen test design](../plans/2026-10-15-card-0987-test-design.md)
owns all variants. None ran here; ordinary red/green is not mutation evidence.

After ordinary Review and confirmed landing of the original Code task, commission
SourceLanding Mutation with exact method filters, red/restore/green and independent
missing-control discovery. Include host-path spelling in that discovery; do not
add a self-comparison test or silently drop a control that now reaches its guard
on Windows. Generic creation custody remains distinct from sealed tool authority.
