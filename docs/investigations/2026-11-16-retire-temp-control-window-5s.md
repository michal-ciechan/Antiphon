# Retire-temp: the 5,070 ms final window

Debug task `2b159393`, 2026-11-16. Diagnosis and isolated timing experiments;
no production script, timeout, release branch, host or volume changed.

## Finding and decision

The reported 5,070 ms is **binding-observation-to-control-check time**, not a
measurement of just the SSH control exchange. The gate correctly refused its
5,000 ms policy. The coordinator already uses a standing PowerShell process and
an established SSH connection. Starting either anew is not this control path's
cost. Each repository does start a Git process, and proof validation includes
rehashing its bundle before that Git call.

The minimal guarantee-preserving candidate is bounded concurrency in the existing
desktop coordinator, retaining the host's original clock and all validation. A
three-repository timing prototype demonstrates its benefit, but the operation's
actual repository count and stage costs are missing. It would be speculative to
claim this fixes the native failure: a one-repository operation gains nothing.
The persistent-process worker ownership, shared deadline and Windows behavior
also require an implementation and behavioral tests, not a blind conversion of
the loop to background jobs.

**Recommended decision:** retain 5,000 ms, obtain the bounded breakdown below,
then commission bounded parallelism if multiple serial checks dominate. If the
measured critical path cannot fit, the simple fallback is an explicit,
provisionally 11,000 ms freshness policy. That is option (d), requiring an
operator decision: it increases acceptable observation age and cannot honestly
be described as preserving the existing five-second guarantee. No such increase
has been applied. Option (c), moving the clock to the desktop check's end, is not
a safe fix for the existing policy.

The brief authorizes read-only diagnosis and this proposal; that work proceeded
without another approval request. Production implementation and native preview
remain pending the missing evidence and the freshness-policy decision.

## Provenance and scope

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
