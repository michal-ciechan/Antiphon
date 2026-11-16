# CARD-1955 Final Review, round 1

Reviewed source: Debug task `f7f2de80-4f22-4eae-a8b2-29dcf0f6152c`, branch
`feat/card-task-f7f2de80`, tip `f1251c5cc19b524ef81ddef30f96d9e3f52e209b`.
Range `52a90a3935f1f8ed4198200ce3a5e21ca8dab370..f1251c5cc19b524ef81ddef30f96d9e3f52e209b`.
This review document is a later commit on `feat/card-task-9e615360`. Land the
Debug owner at `f1251c5cc19b524ef81ddef30f96d9e3f52e209b`, not the review commit.
`reviewedSourceClean` is false: there is no checkpoint manifest, and these runs
were build-slot leases.

Verdict: one blocker. Do not land until host Apply refuses a Ready receipt whose
revocation persist failed.

## Blocker: failed Ready revocation is not fail-closed for host Apply

Where: `scripts/c1008-retirement.sh` `c1609_invalidate_ready_receipt`,
`c1609_phase_end`, `c1609_phase_begin`, and `c1609_require_ready_preview`;
host deletion is `scripts/c590-remote.sh` `c1008_recycle`.

Failure: After this invocation has installed `receipt.json` as `state=Ready`,
`consumed=false`, a late refusal calls `c1609_invalidate_ready_receipt`. That
sets `C1609_READY_INVALIDATION_FAILED=1` and then persists `state=Refused`,
`consumed=true`. `c1609_persist` renames onto the receipt only after the temp
file syncs, so a failed persist leaves the receipt Ready and unconsumed and
leaves the flag at 1. `c1609_phase_end` then returns before `active=false`.
`New-C1609RetirementReadPacket` requires `active==false`, so the public reader
refuses. Host Apply does not. `c1609_require_ready_preview` admits
`state==Ready` and `consumed==false` and does not read the lifecycle.
A later `c1008_recycle` for the same operation calls `c1609_phase_begin`, which
accepts a previous lifecycle whose `complete==true` once the owner pid is gone.
`complete==true` is written at phase start, so it does not mean the preview
finished. `phase_begin` then overwrites `lifecycle.json` and
`c1609_require_ready_preview admit` accepts the still-Ready receipt. With
`ANTIPHON_VOLUME_RETIREMENT_APPLY=enabled-$operation`, deletion can run.

The same-invocation desktop preview throws before Apply, and a later
`-RetirementProof` read uses the public reader, so those two desktop paths
refuse. Direct host Apply does not.

Clock-binding mismatch and origin withdrawal happen inside
`c1609_publication_gate` before `c1609_write_ready_receipt`, so they do not
install this invocation's Ready. The post-install paths are the
`ready-published` fence and the directory sync after the receipt rename
(`c1609_write_ready_receipt`'s final `sync`). Those are the refusals that hit
this hole when the Refused persist fails. The immutable `preview-$nonce.json`
is not rewritten. `Late_ready_fence_refusal_invalidates_apply_admission` covers
a successful revocation only.

Why: The revocation is durable only when the Refused persist succeeds. The only
durable signal of a failed revocation is `lifecycle.active` remaining true, and
the next host Apply ignores that signal.

Fix: In `c1609_phase_begin`, refuse when the previous lifecycle has
`active==true`, including when the owner pid is dead. A preview that did not
reach `phase_end` must not be applicable. A crash after a successful Ready
rename and before `phase_end` then also refuses, which is the safe side of the
post-write fence. Add a witness that fails the Refused persist and shows a
later host `c1609_require_ready_preview` returns non-zero, with the immutable
preview still intact. Do not weaken the 5s or 900s checks.

## Safety answers that are not blockers

Staleness. A proof, control exchange, custody record, or origin advertisement
older than its window does not publish a Ready receipt that Apply accepts.
`c1609_prepare_ready_receipt` snapshots volume fingerprints and copies the
already-written `preview-audit.out` into a private render directory, then
syncs those files, all before `C1609_CONTROL_STARTED`. That copy does not
rescan git. The binding clock is emitted in inventory mode after git discovery
and before origin URL reads (`c590-remote.sh`), and
`c1609_recheck_origin_bindings effect` overwrites the clock from that line.
Inside the 5000ms window the writer re-reads origin URLs, exchanges a fresh
control nonce, rechecks proof age (`c1609_publication_fresh`, 900s), persists
the control record, writes the small receipt payloads, and runs the
`ready-published` check. Apply admits only when a fresh fingerprint snapshot
matches the receipt, and consume compares the inventory digest after
`c1008_audit_checked`. The fingerprint snapshot was already before the clock
on the base; moving the render before the clock lengthens that gap by the
render (measured 6260ms on the Debug tip). Apply still re-snapshots before
deletion. The 5000 and 900 constants are unchanged; the only 5000 diff hunks
are diagnostic text.

Nothing weakened. Retired-temp assertions remain 10 outcomes (1 baseline, 9
current) across two shards. Rolling assertions remain 34 (5 baseline, 29
current) across shards `[0,12,8,7,7]`. The N3 split keeps the happy path and
`after-rm` resume on `N3`, and moves `replacement` / `no-intent` to
`N3refusals` with the same refusal assertions. Fixture edits supply the
current retirement scripts, jq, apply token, and live proof exchange. The
group77 `RunShard` fallback finds the expression-bodied shard helper. The only
new timeouts are the two `[Timeout(100_000)]` witnesses. No assertion was
deleted or loosened.

Diagnostic. `c1609_publication_diagnostic` writes schema, phaseNonce, stage,
elapsedMs, and `limitMs:5000`. Stages are `control`, `ready-published`,
`effect`, and `exchange-required`. An existing `publication-diagnostics.jsonl`
must be a regular custody file of at most 7680 bytes before one small record
is appended through `c1609_persist` (umask 077, temp file, fsync, rename).
No path, URL, environment value, or audit body is stored.

Activation. `docs/docker-stack.md` script-only procedure is sound for
`retire-temp -DryRun` only. `git archive` of the landed tooling commit
contains the siblings `c590-real.ps1` copies (`c590-remote.sh`,
`c1008-retirement.sh`, `c1008-content-audit.sh`, `c1008-volume-metadata.sh`)
and the dotted producers (`c1008-host-proof.ps1`, `c1609-host-proof.ps1`,
`c1609-read-packet.ps1`). Passing `-Sha a71c45ab4d5703d480f4eee4c4e30adab45bc4f9`
keeps the rc runtime SHA. The empty-Sha fallback is not used; that fallback
would need a git checkout. Host-jq check mode does not require the archive to
contain `.git`. Do not point any other rolling phase at the export.

Syntax and egress. `bash -n` passed for `scripts/c1008-retirement.sh` and
`scripts/c590-remote.sh`. shellcheck is not installed. The changed PowerShell
diff is ASCII. The production diff adds no network client.

## Verification

Evidence-diff: `commits=26 entries=0 violations=0` for
`52a90a3935f1f8ed4198200ce3a5e21ca8dab370..f1251c5cc19b524ef81ddef30f96d9e3f52e209b`.
`git merge-tree --write-tree origin/master HEAD` wrote
`27c765e32bf30770f7667e32bb5fe62f517b9b45` with no conflicts.
`origin/master` was `cd4165b16280e2a781acaa527b059c9c9cb71c75`.

On that tree the impact driver was built first, then regenerated twice:
`derived-impact-index unchanged` both times (`windowsOnly=31`, `linuxOnly=1`).
`PrintInputDigest` was
`2f48120a65280582b586f9369e2eaf6b1bdc464a2362c8e49d434bd852479f60`.
`policyHash` was
`c2e3939821a8fef911600c44be767b951c8212bcb62824cb746e48c5aaba6909`.
The guard union there passed 114, skipped 1, failed 0 (115 total).

At the reviewed SHA, `VolumeRetirementReceiptTests` passed 17/17 in six
groups, including both new witnesses. The five-class guard batch passed 53/53.
The 37 literal pieces passed except three RemoteScriptContract batches that
the 9-minute session aborted with 0 assertion failures (piece-7 succeeded 6
before abort; 2-piece-2 succeeded 13; 2-piece-3 succeeded 28). The unfinished
methods of those three pieces were rerun one or two at a time and passed,
including piece-7 as five pairs (10/10) and the queued 2-piece-2 and 2-piece-3
methods (20 further results, 0 failed). No red was new, so the base was not
re-run. The known inherited C994 RD-4 `TempRetirementJournalUnknown` was not
in this selection. Mutation remains waived and owed. Positive controls remain
pending.

## Advisories, not land holds

- C994 RD-4 still refuses the next deploy with `TempRetirementJournalUnknown`
  on this candidate and on the base. Separate from this fence.
- A C994 child timeout can be reported as a stale preview success.
- The four-mode jq/rolling roster setup is slow. Do not widen its timeout to
  hide that.
