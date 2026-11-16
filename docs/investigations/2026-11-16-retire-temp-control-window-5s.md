# Retire-temp: one-repository control fence and proposed 11-second policy

Debug task `2b159393`, 2026-11-16. Diagnosis and isolated timing experiments;
no production script, timeout, release branch, host or volume changed.

## Current finding and operator recommendation

The final host evidence identifies exactly **one repository, `repos/antiphon`**,
and one origin row per admitted/final inventory. Repository parallelism cannot
help this incident. The earlier three-repository prototype below is historical
and is not a model of this volume. The fresh evidence also supports ordinary
loaded-path variation rather than a hidden five-second success wait.

**Recommendation for the operator:** approve option (d), a provisional increase
of only the host's final binding-to-publication/effect fence from 5,000 to
11,000 ms. The native upper-tail observation is 5,070 ms; twice that is 10,140
ms, rounded up to 11 seconds. Keep the original host clock origin and all
proof, approval, nonce, custody and per-effect checks. Keep the desktop's
5,000 ms aggregate control/Git deadlines, the host's 10-second response read,
and the 900-second proof lifetime. This is the smallest measured-path proposal;
it explicitly adds at most six seconds to the permitted withdrawal race, rather
than disguising that change by moving the stopwatch. The one-repository witness
is red at 5,000 ms, green with the exact proposed host-only candidate, and red
again for an observation older than 11 seconds. The sample is small and the
budget provisional: track its qualification and timing collection under
CARD-1955, then review the empirical p95 after 30 comparable natural gate
observations. Operator choice is next; no more host data is needed to finish
this diagnosis and proposal.

No production implementation has been applied or pushed. The read-only brief
authorizes the diagnosis/prototype; its explicit design-decision stop applies
to accepting this larger safety window. Code and ordinary Review must follow
an operator choice. This report does not grant production green or native
Windows verification credit.

## Native measurements, now at the real shape

Every inventory has 51 lines naming only `repos/antiphon`. Each admitted/final
origin list is 246 bytes, containing one row. The separately measured 510 KB
advertisement takes approximately 1.3..2.0 s for one `ls-remote`. The rest of the
final fence includes inventory helper completion, approval/binding validation,
scheduling and both directions of the established SSH transport, proof/bundle
validation, and host persistence/checks. The desktop PowerShell process and
outer SSH connection already exist; there is no fresh per-control startup of
either. Git and its transport/credential children are newly started per query.

All following wall times are UTC, supplied by the orchestrator from read-only
host evidence:

| Operation / gate | Final inventory output mtime | Control record mtime | File delta s | Actual guard elapsed s |
| --- | --- | --- | ---: | --- |
| `c100811ab...` failed preview | 15:18:15.81 | 15:18:20.36 | 4.55 | 5.070, refused |
| `c1008c1bd...` successful preview | 15:26:48.38 | 15:26:52.26 | 3.88 | Not recorded; passed 5 s guard |
| Same operation, general Apply | 15:34:02.05 | 15:34:06.29 | 4.24 | Not recorded; passed 5 s guard |
| Same operation, runner-state control | 15:36:34.94 | 15:36:39.35 | 4.41 | 5.050, refused |

The recorded elapsed includes 0.520 / 0.640 s beyond the two failed runs'
file deltas. These are **four attempts**, not six independent samples: 4.41 and
5.050 describe the same attempt, as do 4.55 and 5.070. File deltas omit time
between emission of the binding clock and the inventory output's final mtime,
as well as time after the control file's data mtime through fsync, proof checks
and the guard sample. The evidence does not apportion the offset entirely to
post-control validation. Do not add the offset again to the measured 5.070 s.

Using the observed 0.52..0.64 s offset only as an estimate would put the passing
file deltas around 4.40..4.52 s and 4.76..4.88 s; their exact guard durations are
unknown, and their successful guards only establish that they fit 5 s. The
nearest-rank empirical p95 of four attempts would be the 5.070 s maximum, but
four observations, two with incomplete timing, are not a reliable population
p95. The honest R5 justification is a **provisional small-sample upper-tail
budget**, 2 x 5.070 s rounded up to 11 s. It is not a claim of a mature p95.

Kernel boot is `ca135ff7-0d75-4067-a313-c623f0727498`, namespace `global`.
Actual final binding stamps include `20563878650` ms (failed first preview),
`20564391170` ms (passing second preview), and `20564977670` ms (failed
runner-state control). Earlier stamps `20563547360` and `20563773500` confirm
the same millisecond clock domain. For example, the latter-to-final first-op
delta is 105,150 ms, consistent with about 105.1 s of wall-time separation.
This is not a units error.

First-op render finished at 15:17:57.60; the final inventory output was written
at 15:18:15.81. Second-op rendering was at 15:26:29..30, with final inventory
output at 15:26:48.38. These roughly 18 s gaps are host inventory setup/discovery
before the binding observation, with any post-stamp helper tail counted inside
the fence. They explain the gap without a hidden desktop startup or fixed wait.

## Assessment of the alternatives

- **(a) Parallel repositories:** inapplicable here. There is one query; the
  earlier synthetic speedup cannot be credited to this incident.
- **(b) Pre-warm, then reverify:** warming executable/credential/DNS/transport
  caches may help latency, but the graph renewal already contacts this origin
  before final control and no measured gain is available. A genuine in-window
  re-verification still needs a fresh origin query. Reusing an advertisement
  observed before the stamp while starting the age clock later violates the
  freshness property. It is safe only if its real observation age is carried
  conservatively and checked, in which case the earlier time still consumes
  the allowed age. There is no cheap bound origin-generation check in this
  protocol. Prevalidating bundle bytes similarly needs an immutable/sealed
  handle through use; dropping the hash alone would weaken custody.
- **(c) Desktop observation clock:** beginning a desktop query is a conservative
  point for that query's subsequent server observation; completion is later
  than the server snapshot and is not conservative by itself. Neither point
  can be subtracted from Linux uptime without a proven clock mapping and
  uncertainty/drift bounds. The existing boot/namespace binding proves that
  the Linux helper shares its host's clock; it says nothing about Windows.
  More fundamentally, resetting at the desktop query would drop the earlier
  host repository/origin and approval observations. Keeping a separate
  five-second binding-age guard preserves that guarantee but still rejects
  these runs. A safe redesign could use a new challenge/ack protocol with host
  timestamps bracketing the desktop observation and independently bounded host
  bindings. That is a larger protocol/policy decision, not a one-line clock
  repair. This is why the earlier analysis rejected (c).
- **(d) Host fence 11 seconds:** recommended as the minimal concrete policy
  decision. It accommodates the observed approximately 5.1 s complete path
  while keeping every observation and final effect under the same conservative
  host clock. The local 5 s control/Git caps remain useful and were not the
  observed refusal. Increasing them or the 10 s read timeout is unnecessary
  for this measured one-query path. This host-only proposal supersedes the
  earlier speculative suggestion to increase all three budgets together.

## What 11 seconds permits, and the concrete risk

The 900 s lifetime permits a proof/preview artifact to remain eligible for
15 minutes subject to all other checks. It does not permit a 15-minute-old
advertisement to authorize deletion: each Ready/Apply/volume effect still
renews and performs the final live gate. The proposed 11 s limit bounds the
age of the earliest final host binding observation through the actual
publication/deletion check. An origin query occurs later in that interval,
so its observation age is conservatively bounded by the same 11 s. Retaining
the desktop 5 s cap is an additional stricter local execution limit.

A concrete race is: the final query verifies that origin ref R advertises a
commit containing retained local commit C; R is then deleted or force-pushed,
and no other accepted origin ref reaches C; the host deletes the temp work
volume before it can observe that withdrawal. The earlier publication proof
was true at observation time but is no longer durable reachability authority.
Remote object retention after withdrawal is not guaranteed indefinitely; loss
of the temp copy could eventually make C unrecoverable if no independent copy
survives. The desktop proof bundle must not be assumed to be a permanent backup.
Thus the potential impact can be loss of recoverable work, despite the volume
being TEMP and C having been published and verified a moment earlier.

The five-second policy already allows that race. Eleven seconds increases its
worst permitted exposure by six seconds; it does not create atomic origin
retention. Normal additive pushes preserve old ancestry and are not this race.
Deletion/force-push that removes the final retaining ref is the relevant event.
No event-rate evidence supports a numerical probability. Idle temp state and
local writer locks reduce local work-changing races, but do not stop an external
origin withdrawal or a deliberate actor racing either window. The operator
must explicitly accept that tradeoff. Stronger protection would require an
origin-side retained/leased ref or another durably retained copy, beyond this
timing repair. Keep approval, object coverage, ref/OID comparisons, locks,
target identity and per-effect renewal unchanged.

## One-repository red/green/stale witness

The final witness uses the actual host control gate, real boot/namespace clock
binding and protected persistence, with a local sleeping protocol peer and
isolated precondition/renewal seams. Its purpose is runner-state, matching the
322-byte control. It performs **one** simulated 1.5 s origin query, 3.0 s of
existing-process scheduling/transport overhead, and 0.6 s of host work after
response persistence: nominally 5.1 s, plus small fixture/real guard overhead.
This overhead models measured aggregate cost, not three seconds of new pwsh
startup. It does not claim native per-stage attribution that was not measured.

The red run sources unchanged production. Green and stale runs source the
private host script with exactly the proposed production diff below; they
retain `read -t 10`, not the older prototype's 15-second read. No production
file is modified. The negative adds 11.25 s **after** the observation/reply,
so the origin observation is genuinely older than 11 s when refused.

| Case | Host fence ms | Guard sample / successful return ms | Result |
| --- | ---: | ---: | --- |
| One query + measured-shape overhead | 5,000 | 5,170 refusal sample | Expected red, exit 1; no admission |
| Same workload, exact candidate | 11,000 | 5,170 successful return | Green, exit 0 |
| Same query, then observation aged by 11,250 ms | 11,000 | 15,820 refusal sample | Expected stale refusal, exit 1; no admission |

**Three of three final expectations matched; zero unexpected outcomes.** Run
`one-repository-run-1794844403457`, approximately 28 s, build-slot lease
`6a1ac878-324f-40e1-8375-a6f619ae873b`. Source commit was
`22024984b2e9b70a2408a4a5c40dec9456db6362`; production scripts are unchanged
from the original base. Source SHA256 is
`a3dcbbef2dc0f9d21842c7775f920dffd1f67d83568da996d69d59241b461082`;
candidate SHA256 is
`cfbfbdf829c667efb3901dd390f2facb6049193604c5fa536dffb151607cb185`;
one-repository witness SHA256 is
`f93e5fbb5350da74955d4c13eb1a0439846e2001d9393e7d02a3e000119b7333`.
The unchanged text template SHA256 is
`614ca797ac594ebde3a7156ba8174c7f4727e9fdaff05af05ed2e08b44012d94`.

Artifacts are ignored under
`/work/worktrees/ws-01/.antiphon/control-window-2b159393/`:
`one-repository-witness.js`, `one-repository-receipt.json`, the named run's
per-case scripts/stdout/stderr/diagnostics, `candidate-11s/`, and
`proposed-host-11s.patch`. The earlier one-repository intermediate run used a
private comparator override and 15 s read; the final run above tests the exact
host-only candidate instead. Earlier multi-repository and process/coordinator
results below remain historical evidence; they are not substituted for this
three-case one-repository result.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-one-repository-exact-candidate-2b159393 -SlotWaitMinutes 2 -- /usr/local/bin/node .antiphon/control-window-2b159393/one-repository-witness.js
```

## Exact proposed production/policy diff

This is a reviewable proposal, not an applied implementation. `git apply --check --unidiff-zero`
succeeded against the assigned source, and `bash -n` passed for the private
candidate host script. Patch SHA256:
`e8534bd619a787324d9c133be4a3519ad02167f688e832637464198970069e72`.
The desktop process/control limits and host response-read timeout are unchanged.

```diff
diff --git a/scripts/c1008-retirement.sh b/scripts/c1008-retirement.sh
index 637d1d543..e711af75a 100644
--- a/scripts/c1008-retirement.sh
+++ b/scripts/c1008-retirement.sh
@@ -509 +509 @@ c1609_publication_gate() {
-            printf 'C1609_PUBLICATION_REFUSAL stage=exchange-required elapsed_ms=unstarted limit_ms=5000\n' >&2
+            printf 'C1609_PUBLICATION_REFUSAL stage=exchange-required elapsed_ms=unstarted limit_ms=11000\n' >&2
@@ -573 +573 @@ c1609_publication_diagnostic() {
-      '{schema:1,phaseNonce:$phase,stage:$stage,elapsedMs:$elapsed,limitMs:5000}')" || return 1
+      '{schema:1,phaseNonce:$phase,stage:$stage,elapsedMs:$elapsed,limitMs:11000}')" || return 1
@@ -578,2 +578,3 @@ c1609_publication_diagnostic() {
-# Final host advertisement/response, local custody and the effect share a five
-# second window. A later remote ref move is intrinsically unobservable; the next
+# Final host advertisement/response, local custody and the effect share an
+# eleven-second window (provisional: 2x 5070 ms, rounded up; CARD-1955).
+# A later remote ref move is intrinsically unobservable; the next
@@ -587,2 +588,2 @@ c1609_effect_publication_fresh() {
-    if [ "$elapsed" -lt 0 ] || [ "$elapsed" -gt 5000 ]; then
-        printf 'C1609_EFFECT_WINDOW stage=%s elapsed_ms=%s limit_ms=5000\n' "$stage" "$elapsed" >&2
+    if [ "$elapsed" -lt 0 ] || [ "$elapsed" -gt 11000 ]; then
+        printf 'C1609_EFFECT_WINDOW stage=%s elapsed_ms=%s limit_ms=11000\n' "$stage" "$elapsed" >&2
diff --git a/docs/docker-stack.md b/docs/docker-stack.md
index 44d821a02..d87d6c9d9 100644
--- a/docs/docker-stack.md
+++ b/docs/docker-stack.md
@@ -488,2 +488,2 @@ advertisement after staging closes the transfer/census interval. The final
-helper observation phase, control request, custody writes and effect have a
-five-second monotonic bound. The inventory start record must match the host's
+helper observation phase, control request, custody writes and effect have an
+eleven-second monotonic bound. The inventory start record must match the host's
@@ -493,0 +494,6 @@ is used; present but unreadable interfaces still refuse.
+The 11-second fence is provisional (CARD-1955): the native sample reached
+5,070 ms; twice that upper-tail observation rounds up to 11 seconds. Retain
+the desktop 5-second control/Git limits, 10-second response read and 900-second
+proof lifetime. The sample and six-second increase in withdrawal-race exposure
+are documented in [the investigation](investigations/2026-11-16-retire-temp-control-window-5s.md);
+collect the follow-up timings there before treating this as a measured p95 budget.
```

## Implementation verification after the operator decision

An approved Code task must add the one-repository expected-success and stale
observation witnesses to the maintained test harness and run the brief's closed
production profile. The private timing prototype is not that regression run.
The bound-sensitive maintained tests also need an explicit policy update:
`Late_ready_fence_refusal_invalidates_apply_admission` and
`Failed_refused_persist_refuses_active_lifecycle_apply` must delay at least 12 s
instead of 6 s; diagnostic assertions become `elapsed > 11000`, `limit == 11000`.
`Final_origin_binding_refresh_is_inside_effect_window` must likewise age the
binding beyond 11 s. Preserve the large-rendering test's ability to detect a
renderer moved back inside the fence: its intentional pre-fence delay and
minimum elapsed assertion must also exceed the new fence (12 s / 12,000 ms).
These are deliberate threshold-policy changes, not a waiver of stale authority.
Measure test costs before changing execution timeouts; do not globally replace
all six-second sleeps or all 5,000 values.

Required implementation runs remain receipt five shards, ApplyGate, HostProof,
timing labels, impacted selector and the specified guard union, through the
build-slot gate with `--timeout 570s` and the original policy/input/exclusion
pins. Add the exact binding-age method above because the changed global fence
directly affects its acceptance invariant. Ordinary Review and native activation
evidence follow. No native preview/apply, production regression suite or release
activation was run in this Debug task.

Record at least the next three approved native preview/apply gate measurements
and flag an unexpected overrun; accrue 30 comparable natural gate samples under
CARD-1955 before treating a p95 as mature. Include control, Ready/effect tails,
host/load and stage timings; do not create extra destructive runs for statistics.
The measurement follow-up does not block this operator-ready proposal.

**Next: decide.** Approve the provisional host-only 11 s policy and commission
Code/Review, or retain 5 s and commission the larger clock/protocol redesign.
No further host evidence is requested to finish this task.

## Historical diagnosis and hypotheses

The sections below preserve earlier reasoning, tests, missing-data requests and
costs. Any earlier multi-repository recommendation, request to wait for host
data, or proposal to increase desktop/read deadlines is superseded by the
current one-repository recommendation above.

### Original provenance and scope

Assigned base: `202970c671c05e52e1d727dfbc41b890d6759fde`, branch
`feat/card-task-2b159393`. The four requested production files have no diff
between that base and the caller's tooling commit
`db584731991a7efcae75bfa1665139bac05b5170`.

The MAX_PATH repair described as Merge 249 in the brief is not in this base;
the inspected `Assert-C1609LiveOriginProof` still uses the proof directory as
Git's working directory. This task does not duplicate or undo that repair.
The caller's short-export run is evidence that its live control completed;
this task did not inspect the desktop or run another real preview.

Native timestamps, the 5,070 ms diagnostic, desktop load and the standalone
`ls-remote` timings below are supplied by the caller. Local fixture measurements
are separately identified. No native per-repository timing was available.

## What the gate actually times

Owners: [docker-stack.md](../docker-stack.md), final v2 publication observation
policy; [c1008-retirement.sh](../../scripts/c1008-retirement.sh), functions
`c1609_publication_gate`, `c1609_recheck_origin_bindings`,
`c1609_accept_binding_clock`, `c1609_effect_publication_fresh`;
[c1008-content-audit.sh](../../scripts/c1008-content-audit.sh),
`c1609_emit_binding_clock`;
[c590-remote.sh](../../scripts/c590-remote.sh), inventory branch of
`c1008_git_program`, `c1008_audit_readonly` and `c1008_preview_finish`.

1. Before the final fence, the host extracts the proved-object set, validates
   the staged proof-set digest, rechecks bindings, renews the host proof, and
   rechecks task/container/reference facts. Renewal can take minutes; it is
   outside the five-second fence. Ready preparation also snapshots targets,
   renders all audit rows and flushes the staged files and directory beforehand.
2. The host assigns a preliminary `C1609_CONTROL_STARTED`, then launches the
   final offline inventory helper. Image/volume checks, Docker startup, script
   preparation, repository discovery and Git-layout classification precede the
   helper's binding clock and are excluded from the final measured interval.
3. After discovery, **before reading the first repository's origin URL**, the
   helper emits `C1609_BINDING_CLOCK boot=... namespace=... started=...`.
   The host validates boot, time namespace, and the invocation's start/end
   bracket, then replaces `C1609_CONTROL_STARTED` with that `started` value.
   Its clock is `/proc/uptime`, with roughly 10 ms resolution on this host.
4. Inside the fence, the helper reads each common repository's origin URL and
   emits its private-origin row. Temp/v2 is offline: `c1008_ls_remote_status`
   returns the private-origin classification without doing network Git.
   Helper completion, watchdog/helper release, lifecycle persistence and
   output sorting happen before the host receives the complete inventory.
5. The host extracts/sorts/deduplicates those rows, compares them with the
   admitted inventory, rereads the approved-origin registry, compares approvals,
   validates the clock, then emits `C1609_CONTROL_REQUEST` on existing SSH stdout.
6. The existing desktop coordinator receives it through asynchronous stream
   reads and a 10 ms polling loop. It validates nonce/proof/object bindings and
   starts its **own separate** control stopwatch. It reads approved-origin JSON
   and parses all private-origin rows. Its repository loop is serial.
7. For each row, `Assert-C1609LiveOriginProof` verifies approved origin, proof
   headers, operation/repository identity, bundle SHA-256, expected advertisement
   SHA/count and advertisement syntax. It starts one `git ls-remote` for heads,
   tags and canonical salvage refs, parses/sorts the response, and requires every
   previously proved ref/OID to remain present. Added refs are permitted; a
   withdrawn or changed expected ref refuses.
8. Before each row the coordinator computes the lesser of its remaining 5,000
   ms control allowance and its remaining shared 600,000 ms network allowance.
   The live checker accepts only budgets in 1..5,000. Proof hashing/parsing also
   consumes the coordinator stopwatch, though the Git timeout receives the
   remaining value calculated before entering that checker. A final desktop
   elapsed check catches an overall overrun before emitting the response.
9. `Invoke-C1609BoundedProcess` includes process start, IO, Git's transport and
   credential-helper children, output parsing/draining and process exit within
   its stopwatch/deadline. It polls at 5 ms. It is not just remote-server time.
10. The desktop writes/flushed its response to existing SSH stdin. The host's
    `read -t 10` starts after it emits the request; that ten-second IO timeout is
    separate from the stricter five-second freshness check. After binding the
    response, the host durably persists `control-<nonce>.json`, including file
    and directory fsync, runs the 900-second proof-header/custody check, and
    checks elapsed time from the helper's earlier clock.
11. On success, Ready's fixed remaining writes/renames/directory flush happen,
    then the **same** clock is checked at `ready-published`. Apply repeats the
    gate and checks that clock at the subsequent deletion effect. This failed
    operation never reached Ready publication.

The desktop outer PowerShell startup and original SSH handshake happen once,
before initial proof exchange. Their control-time contribution is zero new
startups. Scheduling delay of those existing processes and both SSH transits
still consume host freshness time. Git and any per-Git credential/transport
process startups consume it as well.

In symbols, the host control sample includes:

```text
remaining helper URL observations + helper completion/release
+ inventory/approval comparison + request transit + desktop dispatch
+ desktop inventory parsing + SUM(per-repository validation, Git, comparison)
+ reply transit + host read/validation + durable control write + proof checks
```

It excludes discovery and rendering before the helper binding stamp. The
`control` diagnostic's stage label does not narrow that interval to transport.

## Repository count and measured cost

The number checked is `Read-C1008PrivateOriginLines(...).Count`: one row per
enumerated common Git repository, not one per linked worktree and not one per
distinct origin URL. Two repository rows sharing an origin still cause two
serial live checks. Repository discovery deduplicates common directories.

**Operation count is unknown.** Its inventory is absent from this mirror.
An asynchronous request was made for scrubbed repository rows/count, unique
origin count, the final clock line and precise output/directory timestamps.
`repos/outer` in an earlier diagnosis cannot establish the count: that exact
path is also the constant in `VolumeRetirementPublicationFixture`.

| Supplied measurement | What it establishes |
| --- | --- |
| Initial exchange 58,772 ms; renewal 68,260 ms | Whole proof exchange time, outside final fence |
| Full-ref `ls-remote`: 1,433 / 1,809 / 1,301 ms; 6,635 lines, 510,880 bytes | Three standalone Git calls, not actual control-row spans |
| Two exact refs: 1,964 / 1,722 / 1,420 ms | Fewer refs did not improve those samples; no justification to narrow proof coverage |
| Host final control 5,070 ms | Whole final binding interval exceeded policy by 70 ms |
| Desktop `failure-line=0`, response accepted and control record persisted | Coordinator completed without its exception path; its local check fit its own allowance |
| Earlier N3 control 1,240 ms, Ready 1,360 ms | Different instant-coordinator fixture; not native desktop p95 |

No exact SSH, desktop-dispatch, proof-hashing, per-row Git, helper-tail or fsync
cost can be recovered from these measurements. The desktop may have used almost
five seconds or substantially less; the 70 ms excess does not prove that SSH
alone cost 70 ms. With three 1.3..2.0 s Git calls, the serial network component
alone could use roughly 3.9..6.0 s, but three repositories is a hypothesis.

### The gap after rendering

Supplied UTC times on 2026-11-16:

| Event | Time |
| --- | --- |
| Last rendered proof file data mtime | 15:17:57.60 |
| Inventory directory mtime | approximately 15:18:15.80 |
| Durable control record data mtime | 15:18:20.36 |
| Diagnostic data mtime | 15:18:20.51 |

The control record follows the response, while the elapsed sample follows that
record and precedes writing the diagnostic. Assuming stable host wall time and
the supplied ordering, subtracting 5.070 s brackets the helper binding stamp at
approximately **15:18:15.29..15.44**. This is an inference, not a replacement for
the actual monotonic clock line. About **17.7..17.8 s** after the last rendered
payload mtime therefore preceded the final stamp; approximately five seconds
followed it before refusal.

That excluded interval can contain final render fsync, final inventory setup,
Docker startup and repository discovery/classification. A payload mtime is not
evidence that its fsync and directory fsync finished. The code explains why this
interval exists and is excluded; the supplied timestamps cannot apportion it
between flush, Docker and discovery. The inventory directory mtime can reflect
creation of the post-helper `live-origins-*.txt`; it is neither the control-send
timestamp nor the binding stamp. There is no coded twenty-second sleep in this
gate and no evidence that a new desktop PowerShell startup occupied this gap.

## Freshness guarantee and the four candidates

The policy bounds both repository-to-origin binding observations and approved
origin advertisement observations before publication/effect. It limits TOCTOU
exposure; it cannot detect a remote change atomically after the last observation.
The 900-second proof lifetime is a separate condition and grants no exemption.

| Candidate | Assessment |
| --- | --- |
| (a) Bounded parallel live checks | Preserves the original guarantee if all checks succeed under one shared deadline, every worker is joined/cancelled before return, and the host clock stays unchanged. Prefer existing-process runspaces or explicitly owned asynchronous Git workers, capped at three initially. Keep per-repository proof/approval validation and fail closed if any row fails. One row gets no speedup. Actual operation count/cost and Windows worker behavior need evidence before claiming this fix. |
| (b) Pre-warm before control, verify cheaply inside | Fetching the graph and staging it already happen before control. A cached advertisement cannot cheaply prove current remote refs: there is no bound origin-generation/conditional-validation service here. A new live query remains necessary. Prevalidating bundle bytes could help only with explicit immutable handle/seal/identity protection through use; dropping the current hash without that would weaken custody. It is not a small proven change. |
| (c) Start at desktop check end | Reject as a minimal fix. Windows Stopwatch ticks cannot be compared to Linux uptime. `C1609_BINDING_CLOCK` binds a Linux helper to its host's boot and time namespace, not to Windows. Even perfect clock synchronization would not restore the discarded earlier binding observations or earliest repository advertisement. Git completion is also later than the server's actual ref observation. A new protocol needs conservative bounds for earliest observations, transit and clock uncertainty, plus an independent original binding-age fence. |
| (d) Increase documented bound | Smallest serial-path implementation, but an explicit safety-policy change. A provisional 11,000 ms candidate is rounded above 2 x 5,070 = 10,140 ms. One loaded native failure and one unlike fixture are not measured p95 and do not prove that 11 s covers future Ready/effect tails. Operator approval and a bounded measurement plan are required. |

If (d) is chosen, change the host effect comparator and diagnostic limit, the
desktop aggregate control limit, and the live check's allowed/default network
limit together. Raise the host control response read from 10 to 15 seconds so
it exceeds the new aggregate budget; retain the final freshness check. Do not
change the 600 s network budget, 900 s proof lifetime, clock-domain validation,
nonce/digest/approval/object checks or per-effect repetition. Update the owner
policy and behavioral tests. Inventory all matching guards/assertions rather
than globally replacing every `5000` in the scripts.

Collect at least three instrumented previews at representative busy load before
calling a candidate operationally adequate, retaining maximum and unknowns.
Treat that sample as provisional. Accumulate 30 comparable control/Ready/effect
samples across subsequent approved retirements before deriving a p95; do not
manufacture extra destructive runs for statistics. Give any longer provisional
budget an explicit tracking card and its measurement follow-up, per R5 in
[test-performance-standards.md](../test-performance-standards.md). Test timeout
guidance does not by itself authorize changing a live TOCTOU safety bound.

## Isolated red, green and stale-observation witnesses

Local source: `202970c671c05e52e1d727dfbc41b890d6759fde`. Linux x86_64,
kernel `4.15.0-213-generic`, runner mirror; contemporaneous load average
`4.76 / 5.59 / 5.43`. These are controlled delay experiments, not native latency
samples or p95. The fixture is Node/Bash with jq and the normal build-slot gate.

It sources the actual production retirement script and uses the actual control
request/response validation, protected durable persistence, helper clock-domain
acceptance, effect comparator and refusal diagnostic. It supplies isolated
inventory/approval/renewal preconditions and a no-op Ready renderer; no actual
origin Git, desktop coordinator, Docker, Ready receipt writer or volume is used.
An `admitted` fixture marker stands for successful return from the control gate.
The parallel candidate changes only the simulated coordinator's scheduling.
The 11-second candidate privately overrides the comparator/diagnostic and read
timeout in a generated fixture script. Production files remain byte-identical.

Each fake repository check sleeps 1.5 s; a 1 s dispatch delay precedes handling.
For stale-observation negatives, the host delays **after the last observation,
reply and control-record persistence**, before its freshness decision. Thus an
observation is actually older than the bound, rather than merely delaying work
before a new fresh observation. No stale case creates the admitted marker.

Final run: `run-1794843121185`, build-slot lease
`4b08de08-b9e1-4add-a104-6bf076796ab4`, approximately 40 s including all five cases.

| Case | Bound ms | Refusal sample / successful host return ms | Outcome |
| --- | ---: | ---: | --- |
| Three serial checks, 1 s dispatch | 5,000 | 5,560 refusal sample | Expected red; exit 1, no admission |
| Same checks concurrent, cap 3 | 5,000 | 2,560 successful return | Green timing prototype; exit 0 |
| Concurrent checks, then 5,250 ms host delay | 5,000 | 7,800 refusal sample | Stale origin observation refused; exit 1 |
| Serial checks, private 11 s candidate | 11,000 | 5,560 successful return | Green budget prototype; exit 0 |
| Serial checks, then 11,250 ms host delay | 11,000 | 16,810 refusal sample | Observation older than candidate bound refused; exit 1 |

Five expectations passed: two successful admissions and three expected refusals;
zero unexpected outcomes in the final run. Red-case elapsed above is the recorded
guard sample, excluding subsequent diagnostic-write time. Passing an 11-second
prototype does not preserve the five-second guarantee or constitute production
green verification. The parallel prototype demonstrates the benefit under the
stated synthetic workload; it does not test production runspaces or native Git.

Essential evidence provenance:

```text
source c1008-retirement.sh SHA256 a3dcbbef2dc0f9d21842c7775f920dffd1f67d83568da996d69d59241b461082
witness.js SHA256 51b222f975b9828b5e974c24eb4237c2e5cc6ff3db952ecd1f0bbb7c37609479
witness.py template SHA256 614ca797ac594ebde3a7156ba8174c7f4727e9fdaff05af05ed2e08b44012d94
```

Ignored artifacts remain under
`/work/worktrees/ws-01/.antiphon/control-window-2b159393/`: `witness.js`, its
`witness.py` text template, `receipt.json`, generated host scripts, per-case
stdout/stderr, clock records and refusal diagnostics. Python is not a runtime
dependency of the successful witness: Node reads the shell template as text.
Rerun from the assigned mirror:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-control-window-2b159393 -SlotWaitMinutes 2 -- /usr/local/bin/node .antiphon/control-window-2b159393/witness.js
```

Earlier setup outcomes are not product reds: the first driver could not launch
because Python was absent; the first Node setup hit an unset fixture census;
the first extended-bound negative encountered the unchanged 10 s read rather
than the comparator. The fixture was corrected. An intermediate five-case run
tested dispatch-delay refusal; the final negatives instead age the completed
observation. An intermediate host-delay wrapper also matched a diagnostic path
and added extra post-refusal sleep; the final wrapper matches only the control
record basename. Historical generated attempts remain ignored and retained.

## Exact missing evidence and next action

Before another native preview, retrieve existing operation evidence through the
orchestrator, without exposing URLs, credentials or raw Git output:

- For `c100811abfece53d749ac9c5924ed55ec98f5`, return repository paths/row count
  and distinct-origin count from `inventory-a694631c67fba9f767a251b7a294ca07.out`
  or its admitted `private-origins.txt`. Check against the final
  `live-inventory-*.out` / `live-origins-*.txt`.
- Return the final `C1609_BINDING_CLOCK` line and nanosecond `stat` times of the
  inventory output, final live output, live-origin rows, render directory,
  control record and diagnostic. Directory mtimes alone do not identify send.

For a separately reviewed instrumentation slice, collect in-memory fixed-schema
timings, keyed by phase/control nonce and repository ordinal; cap emitted rows
(for example 64) and include a total count plus truncation indicator. Never cap
the actual checks, and never log origin URLs, ref lines, prompts or raw stderr:

- Host monotonic points: inventory invocation begin; accepted helper binding
  stamp; inventory/approval comparison done; request send; response read;
  control persist done; proof freshness done; Ready/effect done. The helper clock
  line already supplies the domain and stamp. Separate render payload finish
  from render file/directory flush completion and helper discovery/startup.
- Desktop local elapsed points: line handled; inventory parsed; each row's
  proof/header/hash validation, Git process start-through-drain, advertisement
  comparison; aggregate before response write; response write/flush complete.
  Include repository and distinct-origin counts. Record no fresh pwsh-start
  phase for this path; that would describe a different operation.
- Write desktop `coordinator-controls.jsonl` after response flush, outside its
  timed work. Write host breakdown after the final effect check or on refusal.
  Keep the existing refusal record intact. Do not add stdout/stdin protocol
  fields casually or put diagnostic file fsync inside the very interval being
  measured. Measure instrumentation overhead and label residual transit/queue
  time unknown; cross-host local stopwatches cannot separate one-way latency.

After that slice, the orchestrator may run the existing read-only retire-temp
preview once using reviewed tooling plus the landed short-scratch repair. This
Debug task did not run that command and did not contact the real host.

## Verification boundary

Only an investigation document is tracked. The executable diagnostic artifacts
are ignored. `bash -n` passed for `c1008-retirement.sh`, `c590-remote.sh` and
`c1008-content-audit.sh` (three files). No production implementation is claimed,
so the brief's conditional
production regression profile was not run: receipt five shards, ApplyGate,
HostProof, timing labels, impact selector and the guard union remain required
for an implementation. All future dotnet test runs must use `--timeout 570s`
through the build-slot gate and the brief's policy/input/exclusion pins. This
proposal grants no Review/Full, native Windows or landing credit.

Next: decide. Supply operation inventory/stage evidence; retain the existing
five-second policy and commission a measured parallel implementation, or
explicitly accept a documented provisional larger freshness policy. A production
script change then needs the requested red/green and regression profile followed
by ordinary Review.

## Refinement: a passing preview and a later 5,050 ms Apply refusal

The refinement arrived after the first report. It added a passing preview and
a second near-limit failure, so the investigation tested the specific hypothesis
that a process/pipe wait consumes the entire five-second allowance and returns
success. No production limit or script was changed.

New caller-supplied evidence:

- Preview exited 0 and published Ready for
  `c1008c1bdd626ffbf40bea419318bb2575d1a`, with a control record at 15:26:52 UTC.
- A subsequent Apply refused with `stage=control elapsedMs=5050 limitMs=5000`.
  Its last control record at 15:36:39.35 UTC is 322 bytes, compared with 294-byte
  passing records. The receipt is consumed, and no volume was deleted.
- The relevant host directory is
  `/home/mc/antiphon-server2/retirement/c1008c1bdd626ffbf40bea419318bb2575d1a`.

### The 28-byte difference identifies a different gate

With the current schema, fixed-width nonces/digests and canonical compact JSON
plus newline, record lengths are:

| `purpose` | Bytes |
| --- | ---: |
| `ready` | 294 |
| `apply` | 294 |
| `audit` | 294 |
| `antiphon-runner-temp_runner-state` | 322 |
| `antiphon-runner-temp_dind-data` | 319 |
| `antiphon-runner-temp_runner-tmp` | 320 |
| `antiphon-runner-temp_work` | 314 |

The 322-byte record is therefore exactly consistent with the **first per-volume
runner-state gate**, rather than a larger timeout/error payload. Confirm its
`purpose` in the actual JSON before treating this inference as native proof.
The additional 28 bytes are fully explained by the longer purpose string.

For a fresh Apply, production orders these steps:

```text
publication_gate apply
require_ready_preview -> consumed=true, state=Applying
APPLY_ACTIVE=1
first existing volume: publication_gate antiphon-runner-temp_runner-state
recheck_target -> target_intent -> effect_fresh -> docker volume rm
```

This ordering explains both a consumed receipt and zero deletions when the
runner-state gate refuses. It also implies a successful general Apply gate if
the supplied run was a fresh Apply on this code path. A 294-byte record alone
cannot distinguish `ready`, `apply` and `audit`; request all JSON contents,
not only lengths and mtimes. Do not reuse consumed Ready authority merely
because no delete happened.

The desktop handler uses the same live-check body for all these purposes;
`purpose` changes binding/response data, not its timeout, repository loop or
Git execution method. Host `ready` additionally prepares the rendered receipt
before the binding stamp. Each per-volume gate independently renews proofs and
reobserves bindings, so comparison must include proof/object digests, phase,
inventory count and bundle sizes for that specific gate. A passing preview
does not supply timing credit for a later per-volume gate.

### No fixed five-second success wait found

The inspected production path has the following behavior:

- `WaitForExit(remaining)` returns when the process exits; it throws `deadline`
  if the bounded wait reports false. There is no deliberate sleep for
  `NetworkBudgetMs`.
- While streams remain open, the process wrapper polls at 5 ms and throws at
  `ElapsedMilliseconds >= TimeoutMs`. After observing the process exited,
  still-open streams trigger `drain` after more than 2,000 ms. A descendant
  holding a pipe can therefore cause a refusal, but that branch does not
  return a successful proof at five seconds.
- A failed/nonzero Git result becomes a host-ls-remote exception. The outer
  coordinator does not construct the control response on this exception path.
- Even if the inner process wrapper overshoots its last poll or exit check,
  the coordinator checks its aggregate elapsed time again before constructing
  `C1609_CONTROL_RESPONSE`; more than 5,000 ms throws `control-deadline`.
- Response `WriteLineAsync().Wait(10000)` is a separate bounded wait, after
  the last desktop elapsed check. A completed write returns early. Transit,
  write/flush and host work after that check can still age the host fence.
- Searches found no reachable `Sleep(5000)` / `Wait(5000)` or unconditional
  `sleep 5` in this gate. Five-second sleeps elsewhere in `c590-remote.sh` are
  deployment/readiness loops, not this retirement control path. The v1 Git
  `--kill-after=5s` grace is not a normal five-second wait; temp inventory is
  offline and bypasses that network branch.

The existence of a persisted, nonce/digest-bound control record establishes
that the host accepted a control response before failing its own fence. It is
inconsistent with a desktop deadline/drain exception **before that response**
on this source path. The failing Apply desktop `remote-output.txt` is still
needed; a later desktop exception would be a separate event.

### Direct production-function probes

Source was committed `f23d3cc8d56fff4759094183902a6b0cd2c7261d`; its production
scripts remain identical to the original base. Both probes ran under real
build-slot leases, in this Linux mirror, with only local inherited children.
All children were awaited/released. No real SSH connection, origin, Windows
desktop, Docker or volume was used. These timings do not exclude a Windows-only
runtime/transport problem; native stage telemetry remains required.

**Actual `Invoke-C1609BoundedProcess`: seven expectations matched.** The child
is an owned Node fixture, and the actual PowerShell process wrapper is used
without replacing its wait or stream code.

| Case, with 5,000 ms allowance | Measured ms | Result |
| --- | ---: | --- |
| Immediate successful child, first call | 256 | Exit 0 |
| Immediate successful child, warm call | 90 | Exit 0 |
| Child closes both streams, remains alive 150 ms | 217 | Exit 0; `WaitForExit` returned early |
| Child keeps streams open, exits after 700 ms | 792 | Exit 0 |
| Child keeps streams open beyond deadline | 5,087 | `deadline` exception, no result object, child released |
| Streams closed, process remains alive beyond deadline | 5,044 | `deadline` exception, no result object, child released |
| Immediate nonzero child | 82 | Exit 7, which the Git wrapper would reject |

The two actual expiry cases are themselves almost exactly limit plus tens of
milliseconds, but both are **errors**, not successful control replies. Fast
success does not wait out the budget.

**Actual `Invoke-C1609RetirementCoordinator`: five expectations matched.** Its
function and `Get-C1609ProofSetDigest` are loaded from the production AST. An
owned local `ssh` executable shim connects it to a fixture protocol peer. Only
network proof export and the live-origin check are supplied as fixture seams;
the real stream loop, budget calculation, final deadline check, asynchronous
response write, flush and transport receipt remain in use.

| Purpose and fixture check | Host-peer control ms | Total coordinator ms | Outcome |
| --- | ---: | ---: | --- |
| `ready`, instant check | 146.46 | 616 | Response, exit 0, failure-line 0 |
| runner-state, instant check | 13.42 | 94 | Response, exit 0, failure-line 0 |
| `ready`, 700 ms check | 714.78 | 825 | Response, exit 0, failure-line 0 |
| runner-state, 700 ms check | 743.28 | 831 | Response, exit 0, failure-line 0 |
| runner-state, 5,200 ms check | No response | 5,373 | Exit 2, nonzero exception line |

The first call includes warm-up cost. Purpose-specific fixed five-second waiting
was not observed. The over-budget check intentionally ignores its supplied
budget, testing the coordinator's independent final deadline guard. That guard
refused it; it did not send a success response at budget expiry.

Probe receipts and source provenance, all ignored under
`/work/worktrees/ws-01/.antiphon/control-window-2b159393/`:

```text
waits-1794843701404/receipt.json
  lease 79ffca11-fec0-4483-8d32-fa07b5c03c53; approximately 14 s
  c1609-host-proof.ps1 SHA256 58fd576b66ef19bcc441b55f14d25166134191ba0e859d13224b8107b0433ce6
  process-waits.ps1 SHA256 c794fb9f111903e31b19eeb94f9567f985f46568fbf0ff55f9a7d230e5494c90
coordinator-1794843784952/receipt.json
  lease 70a1a5a0-a56c-46df-9c4b-120813eb59ec; approximately 10 s
  c590-real.ps1 SHA256 d8e3f58cd3ac62cec1b70b75735cda824ac5f03a42eb4c3ed2f0c86fe48bcdbb
  coordinator-waits.ps1 SHA256 29f1630434aad90c034c7c31f8a10cf294a558f858fe7345b125a1b9f8cb0cb2
```

Rerun either script through `scripts/build-slot.ps1`, targeting the full path
to `/usr/local/bin/pwsh -NoProfile -File` and the named probe. These are portable
diagnostic witnesses, not replacements for the conditional production profile.
No unexpected outcomes occurred in these twelve refinement cases.

### Why two misses close to the limit do not establish a fixed wait

There are two deadlines with different start/end points. Write `D` for desktop
control work before the response write, and `H` for the rest of the host binding
interval. A valid desktop response requires approximately `D <= 5000`, while
host success requires `D + H <= 5000`.

Conditioning on **a response that was valid locally but stale at the host**
selects the narrow band `5000-H < D <= 5000`. For example, if `H` were 100 ms,
such host failures would naturally cluster just above 5,000 ms even without
any fixed wait. This is an explanation of selection bias, not a measurement
that native `H` is 100 ms. The supplied 5,050 and 5,070 ms samples alone cannot
distinguish that case from a near-deadline real external call.

Both budgets being 5,000 ms does not reserve room for host work. Carrying the
host's remaining budget to the desktop could improve deadline allocation and
diagnosis, but cannot make more than five seconds of required work fit without
reducing work. Do not move the host clock, accept stale Ready authority or raise
the policy merely to make this symptom disappear.

### Pending evidence and updated recommendation

An asynchronous request was sent for the new operation's existing files. The
reply had not arrived when this refinement report was prepared. Required next
input, scrubbed and read-only:

1. All `control-*.json` contents with exact mtimes, to identify passing `ready`,
   passing general `apply` and failing runner-state controls by purpose/phase,
   proof digest and object digest. Also return `publication-diagnostics.jsonl`.
2. `lifecycle.json` fields `active`, `complete`, `readyPublished`, phase and
   child join state, plus `receipt.json` fields `state`, `consumed`, intents and
   effects. Exclude attestations, URLs and credentials.
3. For the corresponding admitted and final live inventories, return repository
   paths/row counts, distinct origin hashes, actual `C1609_BINDING_CLOCK` lines
   and precise output/directory timestamps. Include proof/bundle sizes per row.
4. Failing Apply desktop `remote-output.txt` and
   `coordinator-exchanges.jsonl`; correlate exchange nonces to the controls.
   Then add the previously specified per-stage timing if existing evidence
   cannot locate the cost. Do not send raw ref advertisements or Git stderr.

The new evidence narrows the likely failing call site and provides a concrete
way to compare preview with per-volume Apply. It does **not** yet identify a
fixed wait or prove that serial repository checks dominate. Keep 5,000 ms and
defer both parallel implementation selection and any larger-policy decision
until the native breakdown is available. The 11-second model above remains
an explicit fallback proposal, not an accepted or newly justified change.

Refinement handoff remains `next: decide`: supply the named evidence; choose a
measured implementation after attribution. No real preview/apply or native
desktop workload was run by this task.
