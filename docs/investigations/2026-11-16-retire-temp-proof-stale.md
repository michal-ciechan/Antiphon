# Retire-temp publication refusal, 2026-11-16

The refusal came from **the final five-second publication fence immediately
after `c1609_write_ready_receipt`**, at `scripts/c590-remote.sh:9467`. The inner
publication gate had succeeded, including final origin binding, live control,
proof custody and its first fence check. The Ready writer then persisted the
receipt and rendered the audit evidence before the second fence check refused.
This is a host-side refusal reported by the desktop, not a desktop timestamp or
nonce-binding failure. The 15-minute proof lifetime did not expire.

The orchestrator's supplied read-only host evidence closes the gap recorded in
the initial investigation commit `8ca032243508635cfad284191a5f12690a0ec8b1`.
No further host access is needed for this attribution. The exact elapsed value
was not retained; the coordinator discards the fence's stderr diagnostic.
Use a new explicit `-DryRun` preview and require its successful exit before
applying. Do not consume the refused attempt's residual Ready receipt.

## Attempt and evidence identity

- Requested rollout source: `a71c45ab4d5703d480f4eee4c4e30adab45bc4f9`,
  `rc/2026-11-22`.
- Reported attempt interval from the commission: 2026-11-16
  `00:03:39Z` through `00:10:45Z` (426 seconds).
- Desktop evidence root, positively identified by its manifest:
  `C:\src\Antiphon\.antiphon\rolling-server2\c72708e6bad9b7dc\`.
- Retirement operation: `c100884ecc9a17ca049e4862f423ba2433321`.
- Project: `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`.
- Host phase nonce: `36b15bbe91d8828f0f0a70c751359d5c`.
- The `retire-temp-runner.manifest.json` explicitly has `recycle.dryRun=true`.
  Both `c590-result.json` and `retire-temp-runner/c590-result.json` record
  `accepted=false`, `exit=2`, and
  `RecycleGitAuditUnknown reason=proof-stale`.
- `remote-output.txt` contains exactly `transport-exit=2` and `failure-line=0`.
  This is a normal remote refusal with no caught coordinator exception, rather
  than a demonstrated desktop proof-writer/transport exception.
- The container-cleanup receipt records no candidates or removals. Temp was
  retired/offline, with zero sessions and queued tasks. Main was accepting and
  reported the requested source. These are the refused attempt's observations,
  not a fresh assertion about the fleet now.

Read-only API retrieval used the documented directory-browse and agent file
content routes, with an existing agent whose workspace is `C:\src\Antiphon`:
`GET /api/filesystem/browse?path=.../` and
`GET /api/agents/392d5d22-1b34-4dbd-a3e4-f0534fab3c91/files/content?path=...&rev=work`.
No session input, agent dispatch, desktop execution, or live Windows test-run
file access was involved. The 325 returned rolling directories were checked for
a matching retirement manifest; exactly one matched this source.

Local uncommitted evidence copies and the read helper are outside the repository
under `/work/evidence/d8f79eb5/`. The original desktop receipt root above is the
durable reference for the caller. The retrieved text responses were not
truncated. Generated receipts and source snapshots are not committed.

## Host evidence supplied on reopening

The caller retrieved the following through trusted SSH from
`/home/mc/antiphon-server2/retirement/c100884ecc9a17ca049e4862f423ba2433321/`.
These are caller-supplied host observations, not additional SSH reads by this
delegate. All times below are UTC on 2026-11-16.

| Record | Supplied observation |
|---|---|
| `control-9116f0bb96d552df90a07ea87fcd97dc.json` | Exists, mtime 00:10:43.552; purpose `ready`, phase nonce `36b15bbe91d8828f0f0a70c751359d5c`, proof digest `1da31aca7a4172410a8dc1bf393ad3277f33fedebcf95a72b6f9bee5ee3f5b4d`, object digest `6f4da91373db299044c55cd8b9af2608f44544d4e62858d29fa3255e7a595a6b`. |
| `receipt.json` | Exists, 2,681 bytes, mtime 00:10:43.712; schema 2, policy 1, scripted, state `Ready`, `consumed=false`, `readyAt=2026-11-16T00:10:43Z`, preview nonce `5f734249b4841cbe3ebb3e42fd5b4cf4`. |
| `preview.json`, `preview-5f734249b4841cbe3ebb3e42fd5b4cf4.json` | Exist, mtimes about 00:10:43.7. |
| `audit.txt` | 1,131,573 bytes, mtime 00:10:43.864; size matches the retrieved complete desktop audit. |
| `findings.jsonl` | mtime 00:10:43.868. |
| `proof.md` | mtime 00:10:44.580, rendered state `Ready`. |
| `lifecycle.json` | mtime 00:10:44.732; `active=false`, `complete=true`, `readyPublished=true`; all four helper children joined. Archived lifecycle mtime 00:10:44.672. |
| Renewed exchange record | mtime 00:10:05.347. |
| Initial exchange record | mtime 00:06:05.826. |

The receipt's local-content inventory digest is
`7d23f00e83ce2b6b0b74d003e0e5f4e560789de4e950fa838d9b56dc919ce1a6`.
Its daemon is `5bd91f05-d2a2-4db8-bc1e-39f7288854d7`; the lifecycle owner is
PID 22460, start ticks 2050905193. The origin-request inventory digest discussed
below is a different digest with a different purpose; equality is not required.

Two live-inventory outputs contain binding clocks with boot
`ca135ff7-0d75-4067-a313-c623f0727498`, namespace `global`, and starts
`20509326980` and `20509422190`. Units are **milliseconds since boot**, derived
from `/proc/uptime` by `c1609_monotonic_ms`/`c1609_emit_binding_clock`. Their
difference is 95,210 ms = 95.210 seconds. The first is the binding refresh before
renewal; the later one supplies the effect-gate start after renewal, census and
Ready-input preparation. The 95 seconds between observations is not charged to
the final five-second fence. `global` is explicitly accepted when both time
namespace interfaces are absent; it is not an invalid namespace label.

## Which proof and which clocks

This is an operation-owned **origin publication proof**, not a task's settlement
receipt, a workspace-slot lease, a retained-evidence receipt, or a timestamp on
the canary report. The producer observes approved origin refs and constructs a
credential-free Git graph bundle.

For the renewed proof, the host directory selected by the response is:

```text
/home/mc/antiphon-c590/c1008-proof-c100884ecc9a17ca049e4862f423ba2433321-1794787745-cc90382dfd2f4d9fb9c8c38aad024995/
```

Its manifest filename is:

```text
515456d534447d77bc21a445225f4416bbac94372fe3d0c13faa469d44571bb7
```

The companion graph is that filename plus `.bundle`. The filename hashes the
repository path `repos/antiphon` and exact origin URL
`https://github.com/michal-ciechan/Antiphon.git`. The copied desktop version is
under `exchange-44ccee82baa9592c3a164c66a8743d5f/` in the attempt root.

| Observation | Initial proof | Renewed proof |
|---|---|---|
| Exchange nonce | `82dd8bccfcfbd6f9190ea1871b4cabf7` | `44ccee82baa9592c3a164c66a8743d5f` |
| Operation/exchange start epoch | `1794787497` | `1794787745` |
| Header `created` | `1794787497` = 00:04:57Z | `1794787746` = 00:09:06Z |
| Header `completed` | `1794787558` = 00:05:58Z | `1794787798` = 00:09:58Z |
| Producer duration from header | 61 seconds | 52 seconds |
| Age at reported refusal, 00:10:45Z | 287 seconds | 47 seconds |
| Coordinator exchange duration | 67,605 ms | 58,584 ms |
| Advertised ref count | 6,613 | 6,613 |

Both manifests have bundle digest
`ca1dcb3931782b7e6b62652aa3acf4db5c88572dba7c49a36bb229c2c0c759b9`
and advertisement digest
`a70e082f892eb20fdc9df1151ce92d9d3284362eab55f454b8f978adc551fd90`.
The inventory digest is
`e69ab6bef0bf15683147d7bb6aa996b972d51fe8505ef329db180bc65862d80f`.
The renewed proof-set digest recorded by the coordinator is
`1da31aca7a4172410a8dc1bf393ad3277f33fedebcf95a72b6f9bee5ee3f5b4d`.

The relevant checks are separate:

1. **Offline scanner proof header** —
   `scripts/c1008-content-audit.sh:5`, `c1609_apply_host_proof`, requires
   `created >= C1008_OPERATION_STARTED`, `completed >= created`, producer
   duration at most 600 seconds, and host UTC minus `completed` between -60 and
   +900 seconds. Version, operation, repository, origin, phase nonce, inventory,
   graph hash, advertisement hash and coverage must also match. The start value
   in this path is the current proof exchange's exported start, not an old task
   timestamp or the canary's start.
2. **Host proof freshness and custody** —
   `scripts/c1008-retirement.sh:261`, `c1609_publication_fresh`, checks the
   manifest/bundle files and every ancestor, operation/nonce/inventory headers,
   and the same -60/+900-second completion-age window. Files must belong to the
   applying host uid (`mc` in production), have one hard link and no group/world
   write bits; ancestors must satisfy the protected custody rules. It can fail
   for custody or binding with perfectly recent timestamps.
3. **Live publication gate** — `c1609_publication_gate` at line 502 refreshes
   local origin bindings and the trusted-origin registry, renews through the
   desktop, refreshes status/census, and performs a final live control exchange.
   `c1609_request_host_proof` records the response binding in
   `/home/mc/antiphon-server2/retirement/<operation>/exchange-<nonce>.json`.
   A failed subcheck anywhere in this gate becomes the same outer diagnosis.
4. **Final five-second fence** — `c1609_effect_publication_fresh` at line 559
   compares host monotonic time with `C1609_CONTROL_STARTED`, requiring elapsed
   time in `[0,5000]` ms. `c1609_accept_binding_clock` takes that start from the
   final helper's `C1609_BINDING_CLOCK` record, validates the current kernel boot
   and time namespace, and brackets it within this invocation. The clock is
   emitted after repository discovery and before fresh origin URL reads. The
   remaining binding/approval work, live advertisement/control response,
   custody writes, and Ready publication must fit the fence. An overrun prints
   `C1609_EFFECT_WINDOW elapsed_ms=<n> limit_ms=5000` on stderr.
5. **Ready receipt lifetime** — separately, a successfully issued schema-2
   `receipt.json`/`preview.json` has a 900-second admission lifetime. This is not
   the manifest `completed` clock. A refused preview is not a usable Ready
   capability, even if a host receipt happens to exist after a late refusal.

`scripts/c590-real.ps1` owns the credential-capable desktop coordinator.
`Get-C1008RetireProofExports` invokes `Write-C1008HostOriginProof` v2, which calls
`Write-C1609HostOriginProof` in `scripts/c1609-host-proof.ps1`. The host requests
an initial exchange, then a renewal **at every publication gate**, even for an
unchanged object set within 900 seconds. The final
`Assert-C1609LiveOriginProof` contacts origin again after staging. Apply obtains
fresh proof as part of its own gate. There is no need or sanction to hand-edit
the proof timestamps or copy an old proof into a new operation.

## Attribution: what the receipts establish

**Not a 15-minute expiration.** The header arithmetic above rules that out for
both supplied manifests at the reported refusal time, absent an unrecorded large
host clock discontinuity. Renewal was recorded only 47 seconds before refusal.
Producer duration was also far below 600 seconds. The seven-minute total run
does not measure the five-second final fence.

**Not an observed unpublished-canary refusal.** Both snapshots advertise:

```text
98c5006a139f454a9e04a93b711071c7a04c75a0 refs/heads/feat/card-task-8ec395a1
```

The initial and renewed advertisement digests are identical. The provisional
content audit enumerated two repositories/entries (`repositories=2 partial=2`)
and emitted 5,191 `C1609_PROVED_OBJECT` rows. The renewed
`published-objects.txt` is 519,094 bytes; its independently recomputed SHA-256,
`6f4da91373db299044c55cd8b9af2608f44544d4e62858d29fa3255e7a595a6b`,
matches the coordinator's required-object digest. Reaching the recorded renewal
establishes that the parent had accepted the provisional scan and metadata
stage; a scanner refusal would have exited earlier with an `audit check=` detail.

The audit does contain preliminary `C1609_FINDING` rows, including mirror dirty,
FETCH_HEAD and reflog observations. Preserve them. They are not themselves the
terminal verdict: the same output contains publication proofs and
`published-fetch-head`, and the parent proceeded to renewal. In particular,
`proof-stale` is not permission to delete the paths mentioned by those rows.

The `dirty` row explicitly names **`repos/antiphon`**, the mirror checkout,
not `worktrees/task-8ec395a1`, the canary worktree. Its null `objectId` and
`commitId` are deliberate arguments in `c1609_scan_git`
(`scripts/c1008-content-audit.sh:370`): the preliminary enumerator emits this
row for any nonempty ordinary Git porcelain status, without identifying a
specific file or applying the full content classifier. Null is not an unknown
canary commit or a missing publication proof.

The authoritative `consider_dirty` classifier in `scripts/c590-remote.sh:7198`
subsequently checks the worktree/index. For example, the empty no-index deploy
seed can show HEAD deletions in ordinary status and still pass when its worktree
contains nothing outside `.git` and it has no stash. Nested repositories are
classified independently rather than refused as their parent's untracked
directory. These explain why a preliminary dirty row and an accepted scan can
coexist by design. Genuine modified/staged/protected untracked content still
refuses `RecycleWorktreeDirty`. The precise porcelain entries behind this
mirror row were not retained, so do not assert that one particular exception
was exercised. No further porcelain evidence is needed to attribute this fence
failure, and nothing here authorizes ignoring a future dirty refusal.

The retrieved audit contains **11 JSON finding rows**: one dirty, one FETCH_HEAD,
seven master-side reflog rows, and two canary-side reflog rows. The reopening
message called them ten, but its listed categories also sum to eleven.

**No demonstrated slot-ledger or retained-evidence cause.** The supplied
standing-runner canaries (`80c30683`, `22ee1e10`) ran on main, whereas this
operation audited temp's private volumes. The main ledger currently identifies
this Debug task in `ws-01`, prior task `80c30683`, and a free `ws-02` last used by
`22ee1e10`. That current main observation cannot stand in for a historic temp
volume inventory. No inspected refusal names `/work/workspace-slots/ledger.json`
or `/work/retained/`. No deletion or repair of either location is justified.

**Exact failed subcheck: post-Ready effect-window check.** The current
operation/nonce's freshly written receipt, control and rendering records close
the ambiguity left by the desktop evidence. The dry-run branch executes:

```text
c1609_publication_gate ready       -> failure label proof-stale (line 9465)
c1609_write_ready_receipt          -> failure label RecycleReceiptUnavailable
c1609_effect_publication_fresh     -> failure label proof-stale (line 9467)
print successful Ready completion
```

The new receipt could not have been written by this path if the first call had
refused. A failure of the writer, including its final directory flush, would
have produced `RecycleReceiptUnavailable`, which is not the recorded diagnosis.
Therefore the failing call is the **third call, line 9467**, after a successful
Ready write. The inner gate had already accepted the binding clock, current
origin approval, live-origin control response, proof headers/custody, and its
own first five-second check. The host's durable control file proves the response
binding. The desktop merely relayed the resulting host refusal at 00:10:45.

The failing function checks the existing monotonic start and elapsed window;
it does not recompare the nonces, reopen proof custody, or run another control
exchange. With the accepted clock domain/start, the fence aged across the Ready
writer. The exact measured elapsed value is unavailable because the coordinator
discards stderr, including `C1609_EFFECT_WINDOW`. Do not invent a numeric overrun
or derive it by subtracting UTC file mtimes from monotonic uptime.

**Receipt mtime is not its publication time.** The writer assigns `readyAt` and
writes three temporary Ready payloads before flushing and renaming them. It
then copies the full audit, loops through every audit line to render findings,
writes `proof.md`, and only afterward renames the authoritative `receipt.json`
and syncs its directory. Renaming preserves the payload's earlier mtime. Thus
the 00:10:43.712 receipt mtime and `readyAt=00:10:43Z` do not prove publication
completed at 00:10:43.712. On this code path canonical receipt installation
follows the 00:10:44.580 proof rendering. This explains how the host can retain
Ready bytes while the overall preview refuses immediately afterward.

**Not structurally caused by seven minutes of scanning.** The timer starts at
the last binding observation, after repository discovery, full content scanning,
renewal and census. The copied census durations of 7,280 / 7,420 / 7,400 ms are
outside that final observation boundary. The current implementation does,
however, place work proportional to the audit's size/line count inside the
remaining five-second budget: this audit is 1,131,573 bytes and 10,417 lines.
The supplied audit/findings/proof mtimes show evidence rendering continuing
after the successful control record. That avoidable work, receipt durability
and the already-consumed control time are the performance repair target.

A fresh preview starts a new late-bound clock and may pass with more execution
headroom; there is no evidence-based guarantee that rerunning the same workload
will fit. This one attempt proves a final-window failure, not deterministic
failure of every long audit. Treat repeated refusal as a Code repair requirement,
not a reason for unbounded retries or a larger deadline. The CARD-1901 plan's
earlier 5,360/7,040 ms positive-path failures are historical supporting context,
not measurements of this attempt.

**The two nonce domains are intentional.**
`C1609_PHASE_NONCE=36b15bbe91d8828f0f0a70c751359d5c` identifies this host
invocation, its inventory, lifecycle and control/exchange records.
`c1609_prepare_ready_receipt` generates a new random nonce
`5f734249b4841cbe3ebb3e42fd5b4cf4` for the immutable preview receipt and stores it
as the receipt's `phaseNonce`, matching `preview-<nonce>.json`.
`c1609_require_ready_preview` reads that receipt nonce to select and compare
the immutable preview. It does not require equality to the invocation nonce.
The publication manifests also have their own proof-exchange nonce, already
validated separately. None of these intended distinctions caused this refusal.

The four relevant desktop script texts match this task's source after newline
normalization; the coordinator and retirement module also match the requested
rollout Git object's bytes. The operation-owned host `tooling.sha256` was not
retrieved, so this is not a claim that live staged-tool hashes were independently
verified.

## Sanctioned recovery and exact commands for the caller

Owner policy: `docs/docker-stack.md`, gate 9 and the volume-recycling publication
section, plus `docs/orchestration-loop.md`, operational autonomy. For an Unknown
refusal other than `registry-missing`, take one fresh preview, then apply only a
successful Ready preview, otherwise send the continuing refusal to Debug. This
Debug commission expressly prohibits executing either step, and neither was run.
No new approval is required for the caller's already-authorized normal recovery.

**A new preview is required; do not apply the old residual Ready.** The owner
policy requires a successfully completed preview, not just Ready bytes on disk.
This preview returned exit 2 after its final freshness check, so its receipt
does not authorize the documented next step. Additionally its `readyAt` is
00:10:43Z and the nominal 900-second admission lifetime reaches 00:25:43Z;
that lifetime has expired by completion of this follow-up.

There is a useful implementation distinction: while such a residual receipt is
still within 900 seconds, `New-C1609RetirementReadPacket` checks Ready state,
matching immutable preview, lifecycle completion/join, custody, daemon and age.
It does not check the enclosing preview's exit/result. Therefore it can appear
technically consumable even after this specific late refusal. That is not
sanctioned recovery. Apply would still perform its own fresh content/publication
gates; this observation is not proof of deletion without those gates. Preserve
the refused evidence and include invalidation of residual Ready in the repair.

If the one fresh recovery attempt has not already been spent, use the sequence
below from the canonical desktop checkout. It captures the operation only from
a new preview with exit 0. The script's successful full-preview path has
`auditPending=false`; a pending container-only preview yields no Ready operation
and this sequence stops. If the one retry has already been used, commission the
repair described below, activate the reviewed script bundle through the existing
owner procedure, and then use this sequence for its new preview.

A plain `-Phase retire-temp` is not the explicit preview-only interface: the
wrapper previews, then follows the apply path subject to its matching token.
Use **`-DryRun`**. A fresh preview may succeed, but success inside the final fence
cannot be promised from these records. Repeated refusal requires repair rather
than repeated retries. Neither command in this sequence was executed here.

```powershell
Set-Location C:\src\Antiphon
& pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha a71c45ab4d5703d480f4eee4c4e30adab45bc4f9 -Phase retire-temp -DryRun |
    Tee-Object -Variable previewLines
$previewExit = $LASTEXITCODE
if ($previewExit -ne 0) {
    throw "Preview refused (exit $previewExit); retain volumes and route the continuing failure to repair."
}
$readyOperations = @($previewLines | ForEach-Object {
    if ([string]$_ -cmatch '^C1609_RETIREMENT state=Ready operation=(c1008[0-9a-f]{32})$') {
        $Matches[1]
    }
} | Sort-Object -Unique)
if ($readyOperations.Count -ne 1) {
    throw 'No unique completed Ready preview; retain volumes.'
}
$operation = $readyOperations[0]
try {
    $env:ANTIPHON_VOLUME_RETIREMENT_APPLY = "enabled-$operation"
    & pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha a71c45ab4d5703d480f4eee4c4e30adab45bc4f9 -Phase retire-temp -RetirementProof $operation
    if ($LASTEXITCODE -ne 0) {
        throw "Apply refused or failed (exit $LASTEXITCODE); retain its receipt for the owner recovery procedure."
    }
} finally {
    Remove-Item Env:\ANTIPHON_VOLUME_RETIREMENT_APPLY -ErrorAction SilentlyContinue
}
```

Run the apply immediately after the successful preview, within its 900-second
admission lifetime. The old `c100884ecc9a17ca049e4862f423ba2433321` operation is
not the value to substitute. Do not use Resume or Close to manufacture success.
Waiting does not refresh
publication; a new preview/coordinator exchange does. There is no documented
standalone proof-refresh command that bypasses these gates.

The standing exact-path synthetic-fixture removal permission is **irrelevant**
to this diagnosis. It authorizes a separate, proven fixture case after retention;
it cannot fix publication custody, an origin binding, a control exchange or an
expired monotonic fence. No current evidence selects a fixture for that remedy.

## Proposed follow-up card description; no code changed

**Title:** Move retirement evidence rendering outside the final publication
window and invalidate refused Ready receipts.

On rollout attempt `c72708e6bad9b7dc`, retirement operation
`c100884ecc9a17ca049e4862f423ba2433321`, the inner publication gate completed
with a valid 47-second-old renewed proof and a durable live control record.
`c1609_write_ready_receipt` then rendered a 1,131,573-byte / 10,417-line audit
inside the remaining five-second window. The following check at
`c590-remote.sh:9467` refused `proof-stale`, leaving state Ready, unconsumed,
and `readyPublished=true` although the enclosing preview failed.

Prepare/stage the full audit/findings/proof rendering before the final binding
clock, leaving only required bounded publication work after live control. Do
not omit evidence, reset the clock after observations, widen the five-second
bound, or reuse the old live-origin result. Keep payload/directory durability,
all custody/header/nonce checks, fresh origin approval, fresh advertisement,
900-second receipt admission and per-effect gates. If a late final check
refuses after Ready installation, invalidate the mutable receipt for admission
while preserving the immutable preview and refusal evidence. Persist a bounded
scrubbed stage/elapsed diagnostic, since the coordinator currently discards it.

A separate Code task should add a red-first focused witness for rendering work
consuming the remaining fence budget after successful control, then verify the
same workload completes inside the unchanged fence after staging is moved.
Also require late-failure residue to refuse Apply admission and retain existing
file/directory fsync-refusal, live-origin withdrawal, clock-binding and dirty-work
guards. No production or test code was changed in this Debug task.

## Validation and completion

No build, test suite, retirement invocation, token-bearing retirement command,
server2 mutation, volume mutation, deletion, cleanup, or production restart ran.
Evidence checks independently recalculated timestamps, the required-object
digest, manifest identity, desktop script equality, the 95,210 ms clock delta,
and eleven preliminary finding rows. The recommended PowerShell sequence passed
a parser-only check with zero syntax errors; it was not executed. The initial
direct SSH attempt refused
with exit 255, `Host key verification failed`; no trust check was bypassed.
This follow-up used the caller's supplied host records without further host
access. The original blocked attribution is now resolved to the post-Ready
fence; the elapsed value remains unrecorded, not a requirement for another
host read. Repair and live recovery remain separate caller work.
