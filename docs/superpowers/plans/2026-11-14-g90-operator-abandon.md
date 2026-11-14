# g90 round 2: explicit operator abandonment

Code/landing owner: ac7f437c-c7df-4e32-ba07-a57dce8c6eac. Base:
f701b2ba967d503427d7064fe4fffa17b8c1738d. Review 3afa3cfa D-1.
All nine original round-1 commits are retained by cherry-pick -x on current master.

Dedicated POST /api/sessions/{id}/messages/{messageId}/abandon is the smallest
safe addition: DELETE retains ordinary Pending cancellation, and abandonment needs
an operator credential and mandatory reason. Reuse Canceled. Persist an Info
AuditRecord using the existing ToolInvocation kind, keyed by message id with an exact
operator-abandon:<message-id> summary, session, actor, reason and timestamp, atomically
with the status change. Duplicate requests return the original cancellation time
and do not overwrite the audit. No timer, Enter, resend or retype.

GET /api/sessions/{id}/messages includes unresolved charged Sent rows alongside
Pending rows, with status, deliveryUnconfirmed, attempt count and last attempt.
The switch refusal names this actual route, blocking ids and the exact operator
POST (Pending rows still use DELETE). Reuse ParkedMessage attention projected
from durable unresolved rows, including stopped/failed sessions and old attempts;
offer OpenAgent and AbandonMessage, never SendNow for Sent rows.

Late matching UserPrompt after abandonment is retained in the ordinary transcript
and ignored by queue reconciliation. Canceled remains terminal with a null verdict.
The shared queue semaphore serializes abandon with late confirmation and switch;
the audit/status commit is transactional. Preserve generation/baseline/attempt data.

## Verification design

V-1: real HTTP queue/abandon, composerless Running/Stopped/Failed variants.
V-2: source switch refusal -> visible ids -> abandon -> switch succeeds; other
session and confirmed rows retain round-1 behavior, plus zero-attempt control.
V-3: operator credential/reason, confirmed/Pending refusal, ordinary cancellation,
idempotence, immutable actor/time/reason audit, wrong-session isolation.
V-4: late prompt after abandon stays terminal, no input; both serialized race orders.
V-5: durable bounded attention with OpenAgent and abandon, gone after resolution.
R-1: all brief-named delivery/standing/control classes, preserving CARD-1924 flush.
R-2: policy/mechanical/platform/layering/category/index/selector guard union.
R-3: impacted-classes rows whose globs match full base..HEAD, plus changed classes.
R-4: evidence-diff full range, merge-tree, OS declaration, fresh TRX nonzero class
and method census, client targeted tests/typecheck; inspect unchanged LF/paste/Enter,
no automatic abandonment or timeout/assertion relaxation. No migration/model change.

PC-1..PC-10 and all variants in the parent CARD-1892 mutation candidate remain
pending SourceLanding Mutation. Additional PC-11 variants: hide unresolved Sent;
drop operator check; allow Pending/confirmed; omit/duplicate audit; reconcile
Canceled; remove shared lock; remove session predicate; offer SendNow on Sent;
expire unresolved attention. Mutation owns deliberate mutants and discovery.

## Performance budget

Build the derived-index driver first (done, native output), regenerate twice with
second unchanged, commit index separately. Bootstrap checkpoint driver separately.
These are unlisted prerequisite builds/runs, each leased individually. Checkpoint
rows run serially with a ten-minute row cap, no full Unit/assembly run. Red API row
is expected to fail assertions before implementation; repeat it green after fix.
One cold build per committed red/green group; later green rows share that build.
Previous Review measured named rows below ten minutes; cold builds historically
336/528 seconds. Estimates are provisional, not p95; retain TRX/phase timings and
loadavg with slot admission separate. CARD-1906 tracks oversubscribed host costs;
CARD-1404/1514 own missing p95 and phase gaps. No new test timeout is introduced.
Postgres/HTTP host is shared per test session, unique row/session identities isolate
new tests; the HTTP host uses a cloned shared test database and a refusing runner.
Unknown fixture/external phases stay unknown. No repetitions required after green.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | red | tests/Antiphon.Tests -> bin-ac7f-red/ | abandon-red | `/*/*/SessionQueueAbandonTests/*` | V-1,V-3,V-4 | assertion failures before fix | 13 | 8 |
