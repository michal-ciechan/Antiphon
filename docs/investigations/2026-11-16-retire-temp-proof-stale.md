# Retire-temp publication refusal, 2026-11-16

The refused attempt reached the **final publication gate after a successful
initial proof exchange, provisional content scan, and proof renewal**. The
15-minute proof-file lifetime did not expire during this seven-minute attempt.
The final gate also has a separate five-second fence. Its generic `proof-stale`
diagnosis does not identify the failed predicate, and the coordinator discards
the output that could distinguish those predicates. A five-second overrun is a
credible explanation, not a demonstrated root cause.

The investigation is incomplete on that last distinction: the desktop receipts
were retrieved, but direct host SSH failed host-key verification. The exact
host-side reads needed to finish are below. Do not delete fixtures, edit a proof,
relax a clock bound, or treat this refusal as evidence of unpublished work.

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

**No demonstrated slot-ledger or retained-evidence cause.** The supplied
standing-runner canaries (`80c30683`, `22ee1e10`) ran on main, whereas this
operation audited temp's private volumes. The main ledger currently identifies
this Debug task in `ws-01`, prior task `80c30683`, and a free `ws-02` last used by
`22ee1e10`. That current main observation cannot stand in for a historic temp
volume inventory. No inspected refusal names `/work/workspace-slots/ledger.json`
or `/work/retained/`. No deletion or repair of either location is justified.

**Final live origin movement is not demonstrated.** A ref can move after a
renewal. If the desktop final live-origin assertion had thrown, the coordinator
would catch it and record a nonzero `failure-line`. Here it records zero. This
rules out a caught final assertion failure in the inspected coordinator; it
does not establish that the final control request was reached or succeeded.

**Confirmed diagnostic defect; unresolved underlying subcheck.** In the dry-run
branch, `scripts/c590-remote.sh:9465` maps every failure of
`c1609_publication_gate ready` to bare `reason=proof-stale`; line 9467 applies
the same label to the post-Ready five-second check. The coordinator reads stdout
and stderr but retains neither arbitrary stdout lines nor stderr text; its
`remote-output.txt` is only transport exit/failure-line metadata. Successful
control requests are not appended to `coordinator-exchanges.jsonl`, either.
Consequently the five-second diagnostic can be emitted and discarded, and the
desktop copy does not disambiguate the two call sites.

The three copied host census attempts took 7,280 / 7,420 / 7,400 ms. Those are
outside the final observation boundary and do not prove a five-second overrun.
The CARD-1901 plan already records earlier positive-path failures at 5,360 and
7,040 ms, making this a concrete performance candidate, not proof about this
attempt. Classify the observed diagnostics as a script observability defect;
leave tight-window versus other final-gate failure, and transient versus
repeatable behavior, unresolved until the host evidence below is read.

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

First retain the existing receipt root. From a host with its existing trusted
server2 SSH configuration, read these **without changing the host**:

```powershell
ssh -o BatchMode=yes mc@server2 'ls -la /home/mc/antiphon-server2/retirement/c100884ecc9a17ca049e4862f423ba2433321'
ssh -o BatchMode=yes mc@server2 'cat /home/mc/antiphon-server2/retirement/c100884ecc9a17ca049e4862f423ba2433321/lifecycle.json'
ssh -o BatchMode=yes mc@server2 'cat /home/mc/antiphon-server2/retirement/c100884ecc9a17ca049e4862f423ba2433321/control-*.json'
ssh -o BatchMode=yes mc@server2 'cat /home/mc/antiphon-server2/retirement/c100884ecc9a17ca049e4862f423ba2433321/receipt.json'
ssh -o BatchMode=yes mc@server2 'rg -n "^C1609_BINDING_CLOCK|^audit check=" /home/mc/antiphon-server2/retirement/c100884ecc9a17ca049e4862f423ba2433321/inventory-36b15bbe91d8828f0f0a70c751359d5c/live-inventory-*.out'
```

Missing files are observations to preserve. Do not create them to make these
commands succeed. A control record distinguishes a completed final response
from an earlier host failure; receipt/lifecycle state distinguishes failure
before Ready from a post-publication check. The binding record further narrows
clock-domain/bracketing problems. These records may still lack a captured exact
elapsed value, because the coordinator loses stderr.

If the one fresh recovery attempt has not already been spent, the normal command
sequence from the canonical desktop checkout is:

```powershell
Set-Location C:\src\Antiphon
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha a71c45ab4d5703d480f4eee4c4e30adab45bc4f9 -Phase retire-temp -DryRun
```

Stop this sequence on refusal. A plain `-Phase retire-temp` is not the explicit
preview-only interface: the wrapper previews, then follows the apply path;
the matching token gates that path. Use `-DryRun` for an unambiguous preview.
Only after exit 0, `auditPending=false`, and its fresh
`C1609_RETIREMENT state=Ready operation=<operation-id>`, apply that exact operation
at once, within its 900-second receipt lifetime:

```powershell
$operation = '<operation-id from the successful new Ready preview>'
try {
    $env:ANTIPHON_VOLUME_RETIREMENT_APPLY = "enabled-$operation"
    pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha a71c45ab4d5703d480f4eee4c4e30adab45bc4f9 -Phase retire-temp -RetirementProof $operation
} finally {
    Remove-Item Env:\ANTIPHON_VOLUME_RETIREMENT_APPLY -ErrorAction SilentlyContinue
}
```

The previously refused operation is not the value to substitute. Do not use
Resume or Close to manufacture a successful preview. Waiting does not refresh
publication; a new preview/coordinator exchange does. There is no documented
standalone proof-refresh command that bypasses these gates.

The standing exact-path synthetic-fixture removal permission is **irrelevant**
to this diagnosis. It authorizes a separate, proven fixture case after retention;
it cannot fix publication custody, an origin binding, a control exchange or an
expired monotonic fence. No current evidence selects a fixture for that remedy.

## Proposed follow-up card description; no code changed

**Title:** Preserve retirement publication subgate refusal and timing evidence.

On rollout attempt `c72708e6bad9b7dc`, retirement operation
`c100884ecc9a17ca049e4862f423ba2433321`, a 47-second-old renewed proof received
bare `RecycleGitAuditUnknown reason=proof-stale`. The final publication gate
maps custody, binding, digest, renewal, Ready preparation, clock-record, control
and five-second failures to one label. The desktop coordinator drops detailed
host output, including the existing effect-window diagnostic, and records no
successful control exchange.

Persist a bounded, scrubbed typed failure stage and the relevant clock/elapsed
facts in operation evidence; distinguish failure inside the Ready gate from the
post-Ready check, and retain the final control outcome. Keep the five-second and
900-second limits, custody/binding predicates, live-origin requirement, and
deletion admission unchanged. Add focused witnesses showing fresh proofs with
custody, binding and effect-window failures remain refused and produce distinct
evidence with no destructive effect. Measure any subsequent normal-path timing
repair separately; do not widen a bound or add a retry based on this diagnosis.

## Validation and outstanding requirement

No build, test suite, retirement invocation, token-bearing retirement command,
server2 mutation, volume mutation, deletion, cleanup, or production restart ran.
Evidence checks independently recalculated timestamps, the required-object
digest, manifest identity, and desktop script equality. Direct SSH refused with
exit 255, `Host key verification failed`; no trust check was bypassed.

To finish exact root-cause attribution, the caller must supply the host records
listed above or commission their read from a host that already has trusted SSH
access. The limitation is missing evidence/access, not missing authorization for
the standing rollout recovery. The diagnostic defect and the recovery sequence
are established; a specific five-second overrun or other final predicate is not.
