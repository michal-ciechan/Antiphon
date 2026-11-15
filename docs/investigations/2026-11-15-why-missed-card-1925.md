# Why CARD-1925 missed production clock regressions

The provider witness in EfLambdaClockGuardTests constructs ClockProbeContext queries.
It documents Npgsql translation but never invokes the changed dispatcher, queue, claim
or conversion runner. Its source-string companions do not establish SQL semantics.
A production change could silently switch clock authority while that witness stayed green.
CARD-1925 was identified by Review ec0488ff at reviewed tip
1f44a31a694edab5a1f3ce160b9a0505ef3adf43 and retained as advisory D4.

EfProductionClockQueryTests now executes those production service methods on the standard
TestDbFixture clone, intercepts their commands and counts compilation. The injected clock
changes seven minutes between calls. Assertions require both exact clock parameter values
and stable SQL with one compilation; the conversion fence instead requires PostgreSQL now()
and no DateTime parameter. Dispatcher SQL must retain gen_random_uuid() per canceled row.
Private operations are invoked through reflection to avoid adding production test seams.
Fence/claim misses execute real SQL without starting sessions or creating conversion work.

The existing synthetic witness remains useful as a provider translation example; it is
not credited as production coverage. The new tests cover four specific query sites, not
all 18 audited hoists. Adjacent cache and source guards retain their separate roles.
Deliberate source controls PC-1 through PC-5 remain pending for Mutation; this Code report
cannot claim mutation red/restore/green. No production query was changed by this task.

Verification design and the closed checkpoint list:
[plan](../superpowers/plans/2026-11-15-card-1925-production-clock-witness-plan.md).
