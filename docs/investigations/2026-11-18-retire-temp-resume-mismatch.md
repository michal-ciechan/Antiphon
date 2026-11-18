# retire-temp Resume of c100884f ends RecycleResumeMismatch (2026-11-18)

Task 36d4e397 (Investigate, read-only). Rollout rc/2026-11-27 `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`,
gate 9, operation `c100884f1e02a03b541118d329379ce16e480`.
Code read at origin/master **`e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`**, which is also
the main checkout `C:\src\Antiphon` HEAD and the rollout SHA. Nothing was run on server2 except
read-only `ls`, `jq`, `sha256sum`, `awk|sort`, `docker ps/volume ls/volume inspect`, `df`.

## Verdict

**Confirmed.** Resume fails at the temp inventory-digest comparison
`scripts/c590-remote.sh:9573-9580`. The check is deterministic: no Resume of this operation
can pass while `antiphon-runner-temp_work` exists. The digest filter
(`scripts/c1008-retirement.sh:118-123`) keeps `disposable-fixture` rows. Those rows come from
the three volumes that this operation already removed, so the fresh apply audit can never
reproduce the preview's digest. This is a code defect (CARD text below). The documents are
also incomplete: they name Resume, and Close, as the recovery for this state, and neither
can work here.

## A. Which precondition mismatched

Evidence: receipt folder `C:\src\Antiphon\.antiphon\rolling-server2\c727618127f406cb`
(`c590-result.json` = `{"accepted":false,"diagnosis":"RecycleResumeMismatch","exit":2}`;
manifest `recycle.resume=true`, `operationId=c100884f…`, `sourceSha=e229fb4d3…`).

The checks before the failing one passed:

| Check | Where | Result |
|---|---|---|
| Journal identity: schema 1, op, project, context, dryRun false, composeDigest `8a8154db…` | c590-remote.sh:9401-9406 | pass. The journal at 06:13:12 still has the same fields; `phase=removing`, `outcome=partial`. |
| `c1609_require_resume_phase`: receipt schema 2, consumed, owner null, state `PartialRefused`, daemon `5bd91f05…` equals lifecycle daemon, `readyPublished=true`, lifecycle schema 2 `phase=Apply` | c1008-retirement.sh:1024-1050 | pass. The run went on to start a new Apply lifecycle `ae5584e9…` (`lifecycle.json` 06:13:13, children joined). |
| `c1609_prepare_resume`: receipt equals preview `cdc82bfb…` apart from ledger fields; intents and effects are consistent (3 removed) | c1008-retirement.sh:836+, called at c590-remote.sh:9420 | pass. Execution reached the audit. |
| Per-target original vs current facts; work outcome `pending`; preserved; owned containers | c590-remote.sh:9506-9551 | pass. The work volume CreatedAt `2026-11-18T01:30:21Z` and its labels are unchanged; owned=0. |
| **Temp inventory digest of `apply-audit.out` vs `receipt.json.inventoryDigest`** | **c590-remote.sh:9577-9579** | **FAIL** |

The failing comparison, reproduced from the stored evidence with the same filter
(`LC_ALL=C awk '/^(entry repo=|tip=|inventory-local-|repositories=|disposable-fixture|published-fetch-head)/' | LC_ALL=C sort | sha256sum`):

| Input | Filtered lines | Digest |
|---|---|---|
| Preview audit (`server2:…/retirement/c100884f…/audit.txt`, 05:32) | 5395 | `9d75563577688ce8b1141fd90eba6819595578af6aecb68170d01dd71eb9c849` = `receipt.json.inventoryDigest` = `preview.json.inventoryDigest` |
| Resume apply audit (`retire-temp-runner/apply-audit.out`, 06:13) | 5387 | `de72d8f0ea1e86f986c4f221f713e1c440ac817b5ac92f483d9f8444a6f2ff70` |

The 8 missing lines are all `disposable-fixture` rows from the three volumes this operation
already removed (`effects`: runner-state, dind-data, runner-tmp = removed):

```
disposable-fixture classifier=bash-edit-diff-v1 path=/tmp/claude-1654/bash-edit-diff/…  (runner-tmp; c1008-content-audit.sh:482)
disposable-fixture classifier=claude-session-key-v1 path=claude/sessions/1507….key size=111 (runner-state; c1008-volume-metadata.sh:42)
disposable-fixture classifier=claude-session-key-v1 path=claude/sessions/7585….key size=111 (runner-state)
disposable-fixture classifier=dind-containers-empty-v1 path=containers                     (dind-data; c1008-volume-metadata.sh:87)
disposable-fixture classifier=dind-engine-id-v1 path=engine-id size=36                     (dind-data)
disposable-fixture classifier=dind-volumes-v1 path=volumes type=d                          (dind-data; :100)
disposable-fixture classifier=dind-volumes-v1 path=volumes/backingFsBlockDev type=b        (dind-data)
disposable-fixture classifier=dind-volumes-v1 path=volumes/metadata.db type=f              (dind-data)
```

None of the work-volume Git rows changed: every `entry repo=`, `tip=`, `inventory-local-*`,
`repositories=2 partial=2` and `published-fetch-head e229fb4d3…` row is identical in both
files. The retained Git inventory is the same as at preview time. What changed is only the
rows of the volumes that this operation itself deleted.

The comment at c590-remote.sh:9573-9575 states the intent: "Removed metadata-only roles change
their present/absent rows. All remaining roles were freshly scanned; retained Git inventory
must still equal the immutable preview". The filter does not follow that intent, because
`disposable-fixture` rows carry no role, and no rows are dropped for removed roles. The filter
(commit 8536d74cf) and the Resume comparison (commit bec905c77) both landed 2026-10-30.

The other fields the brief asked about:
- **sha**: the journal `sourceSha` and the receipt both use `e229fb4d3…`, and the run passed `-Sha e229fb4d3…`. They match.
- **phase**: journal `removing`, receipt `PartialRefused`, lifecycle `Apply`. All are accepted.
- **plan hash / composeDigest**: `8a8154db…` matches.
- **volume list**: the four targets match; work `pending` with the original incarnation.
- **diagnosis string**: the earlier `RecycleGitAuditUnknown reason=proof-stale` (attempt 2, CARD-1989) is history, not an input. Resume does not compare it. The journal `diagnosis` is now overwritten with `RecycleResumeMismatch`.
- **Exchange**: the new exchange `a20aa080…` recorded `inventoryDigest=e69ab6be…` and an empty `objectsDigest` (`e3b0c442…`, `published-objects.txt` 0 bytes). This was not the refusal point. It is consistent with the same three removed volumes no longer contributing.

The refusal happened before any effect. `c1609-census.jsonl` has only the two pre-audit
census gates; the deletion loop's `c1008_status_proof` (c590-remote.sh:9628, 9653) never ran.
The work volume is untouched, and receipt effects/intents still number 3.

## B. Is the operation still resumable?

**No, by no command line on current code.**
- The digest check runs whenever `antiphon-runner-temp_work` is present (c590-remote.sh:9577),
  and the work volume is the only remaining target. The removed rows cannot return.
- A different `-Sha` changes nothing. Temp journals already accept any sha (`… or .project=="antiphon-runner-temp"`,
  c590-remote.sh:9405), and the digest is SHA-independent. Other flags do not reach this branch. The
  token must stay `enabled-c100884f…`. A dry-run cannot resume.
- Do not edit the receipt or journal (docker-stack.md:756).

**Close is also impossible now.** `c1609_close_host` requires every target absent and refuses
`TempVolumesRetained antiphon-runner-temp_work` (c1008-retirement.sh:1286-1289, refusal at :1288).

**Recovery without a code change: a fresh operation, then Close the old one.** This matches the
gate-9 rule "Any other refusal … gets one fresh preview and apply" (docker-stack.md:130).

1. Fresh preview (new operation id):
   ```powershell
   pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4 -Phase retire-temp -DryRun
   ```
   Read `C1609_RETIREMENT state=Ready operation=<new-op>`.
2. Within 900 s:
   ```powershell
   $env:ANTIPHON_VOLUME_RETIREMENT_APPLY = 'enabled-<new-op>'
   pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4 -Phase retire-temp -RetirementProof <new-op>
   ```
3. When all four names are absent, close the stranded operation (it needs the operator credential via
   `Get-RunnerOperatorToken`; a delegate also needs `-AuthorityFile`):
   ```powershell
   pwsh -NoProfile -File scripts/retire-runner-volumes.ps1 -Close -OperationId c100884f1e02a03b541118d329379ce16e480 -ExpectedJournalDigest <sha256 of server2:/home/mc/antiphon-server2/retirement/c100884f1e02a03b541118d329379ce16e480/receipt.json, re-read just before; at 06:21Z it was a006437f65f3f756716f7ae229615bd79949eb29bb810bb7d8b10ca03e7365fa> -ReasonFile <file>
   ```
   Close records `removed` for the three effects and `unattributed-absence` for work. It does not remove anything.

**Coverage of the fresh operation.** `C1008_TARGETS` is still all four names. The three
absent volumes get `facts=null`. `c1008_private_identity` accepts null (c590-remote.sh:8612),
and the deletion loop records `alreadyAbsent` for a null original (c590-remote.sh:9675). So the
preview lists all four targets, and only `antiphon-runner-temp_work` is deleted. Its preview
audit will lack the eight metadata fixture rows, so preview and apply digests agree.

Preconditions for the fresh operation (all observed true at 06:21Z unless noted):
- Registry `retirement-origins.json` is present.
- The temp placeholder is retired: liveZero `retiredAt 04:46:10Z`, sessions 0.
- No temp containers; nothing references the work volume.
- The work findings (dirty, three reflog and one fetch-head at `e229fb4d3…`) are the same set the 05:32 preview accepted as Ready.
- **Unverified:** I found no guard that refuses a new temp preview while another operation's receipt is PartialRefused. That sibling scan exists only in deploy-temp `c1609_temp_retirement_clearable` (c590-remote.sh:9894-9950) and in Close (c1008-retirement.sh:1279-1284). If the fresh preview refuses because of c100884f, stop and Debug.
- **Risk carried over:** attempt 2 ended PartialRefused on the CARD-1989 control deadline while the work volume (the last and largest target, 2.5 GB, 18,827 entries) was being processed. A fresh apply can hit the same deadline. If it does, the new operation is left in the same Resume dead end.

## C. Is the document or the code wrong?

**The code is wrong.** The documents are also wrong or incomplete.
- Code: `c1609_inventory_digest` (c1008-retirement.sh:118-123) hashes `disposable-fixture`
  rows of every role. Temp Resume (c590-remote.sh:9573-9580) compares that digest against
  the full-preview digest after metadata volumes were removed. This contradicts the in-line
  comment. As a result, any Resume after at least one removal of a non-empty runner-state,
  dind-data or runner-tmp volume always refuses `RecycleResumeMismatch`. Work is always last
  (docker-stack.md:369), so every realistic PartialRefused is affected.
- Docs: docker-stack.md:139-142 ("Recovery is Resume of that operation, or … -Close") and
  :373-375 / :601-609 / :746-753 present Resume, or Close, as the recovery. They do not say:
  - Close needs all four volumes absent.
  - Resume with a retained target is currently impossible after metadata removals.
  - The working path is fresh preview+apply, then Close of the stranded operation.

Defect card text (to file; a search for "inventory digest" and "RecycleResumeMismatch" found no duplicate):

> **Title:** Temp retire Resume always refuses RecycleResumeMismatch after metadata volumes were removed (inventory digest includes removed roles' disposable-fixture rows)
>
> **Facts:** rollout rc/2026-11-27 e229fb4d3, op c100884f1e02a03b541118d329379ce16e480. After a PartialRefused that left 3/4 volumes removed, `-ResumeRecycle` refused at c590-remote.sh:9577-9579. Preview digest 9d755635… (5395 rows) vs apply digest de72d8f0… (5387 rows). The only difference is 8 `disposable-fixture` rows from the removed runner-state, dind-data and runner-tmp volumes. All Git rows are identical. The filter in c1008-retirement.sh:118-123 (8536d74cf) keeps `disposable-fixture` for every role. This contradicts the comment at c590-remote.sh:9573-9575 (bec905c77).
>
> **Impact:** a temp PartialRefused with a retained work volume cannot be resumed. Close refuses `TempVolumesRetained`, and the next deploy-temp refuses `TempRetirementUnresolved`. The only way out is a fresh operation plus Close.
>
> **Accept:**
> - A Resume whose only difference is the absence of rows from roles with recorded removed or removed-observed effects passes. Any changed or new retained row still refuses.
> - There is a red-first test that removes a metadata volume and then resumes.
> - docker-stack.md recovery text names the fresh-operation-then-Close path for PartialRefused with retained targets.

## D. Risk to the standing runner

None was observed (06:21Z).
- `docker ps -a --filter label=com.docker.compose.project=antiphon-runner-temp`: empty.
- `docker ps -a --filter volume=antiphon-runner-temp_work`: empty.
- Standing runner: `antiphon-runner-session-runner-1` is Up 2 hours (healthy) on image tag
  `e229fb4d3fb8`. `antiphon-runner-build-slots-1` is Up 7 weeks (healthy).
- Preserved caches and `antiphon-runner_runner-state` are recorded `preserved` and were not targets.
- Disk: 250 G free (64%).
- The only remaining temp volume is `antiphon-runner-temp_work` (CreatedAt 2026-11-18T01:30:21Z, compose labels temp/work).

## E. What else blocks the next rollout

The next `deploy-temp` gate `c1609_temp_retirement_clearable` (c590-remote.sh:9894-9950) will refuse on two counts:
1. `TempVolumesRetained antiphon-runner-temp_work` (the volume is present).
2. `TempRetirementUnresolved`. c100884f is `PartialRefused` with 3 effects/3 intents and no closure. It is the only unresolved
   record among the 28 retirement directories: 5 Completed, 2 Ready with 0 effects, 1 Refused with 0 effects, and the rest receipt-less previews.

Both clear only by removing the volume and then Closing c100884f. Nothing else blocking was
found: no close-pending, no live lifecycle owner, and lifecycle `ae5584e9…` is inactive and complete.

## Not done, noted

- Fix idea: in temp Resume, compare digests over rows of retained roles only, or tag
  `disposable-fixture` rows with their volume and drop rows of targets with recorded effects.
- Not verified: whether the fresh preview tolerates the sibling PartialRefused receipt (see B).
