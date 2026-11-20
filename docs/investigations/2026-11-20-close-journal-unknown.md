# Close of c100884f prints JournalUnknown and cannot succeed

**Date:** 2026-11-20. **Role:** Debug, read-only. No journal, receipt, lifecycle, or host file was edited. Nothing was run on server2. Source is this worktree (`scripts/retire-runner-volumes.ps1`, `scripts/c1008-retirement.sh`, `scripts/c590-remote.sh`).

Operation `c100884f1e02a03b541118d329379ce16e480`. Host directory `/home/mc/antiphon-server2/retirement/<id>/`. Observed Close:

```text
TempRetirementCloseRefused: JournalUnknown
```

exit 2, with `-ExpectedJournalDigest a006437f65f3f756716f7ae229615bd79949eb29bb810bb7d8b10ca03e7365fa`. `deploy-temp` currently refuses `TempRetirementUnresolved` on this same record (PartialRefused, consumed, owner null, 3 effects, 3 intents, no `closure.json`, no `close-pending.json`, all four temp volumes absent).

## Verdict

There is no corrected Close command. The digest, the authority file, and the operation id are not what refused.

The line the operator saw is written by the PowerShell wrapper, which throws away the remote reason. The shell check that current code cannot pass, once the digest and the Docker daemon id match, is the sibling scan: every directory under `retirement/` must already have a canonical `receipt.json`. `c100871b94454a62f4cbfb51c5ad40c91bbf8` does not. `deploy-temp` skips that directory. Close does not.

## What printed JournalUnknown

`scripts/retire-runner-volumes.ps1` `Invoke-Server2Bash` (lines 78–81) discards remote stderr and then:

```powershell
if ($proc.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($text)) { Fail-Close 'JournalUnknown' }
```

`Fail-Close` (lines 16–18) prints `TempRetirementCloseRefused: JournalUnknown` and exits 2. The same function runs on a 90s timeout (line 78) and on any thrown exception (line 163).

The remote script prints its own `TempRetirementCloseRefused: <reason>` on stderr (`c1008-retirement.sh` `c1609_close_fail`, line 1162) or returns 2 with no text (the sibling scan). Success is the only stdout (`TempRetirementClosed …`, line 1328). Every refusal therefore becomes the wrapper’s `JournalUnknown`. The token does not mean the shell took a `JournalUnknown` branch.

## (a) Digest

`-ExpectedJournalDigest` is the raw file hash, not a canonical re-encoding.

`c1609_close_host` compares it here (`c1008-retirement.sh:1249`):

```bash
[ "$(sha256sum "$file" | cut -d' ' -f1)" = "$expected" ] || { c1609_close_fail DigestMismatch; return 2; }
```

`$file` is `receipt.json`. `sha256sum` covers the bytes on disk, including the trailing newline `jq -cS` writes. A mismatch is shell `DigestMismatch`, which the wrapper still prints as `JournalUnknown`.

Canonical form is a different check, and it runs first (`c1609_read_canonical`, lines 980–987): custody, size ≤ 1 MiB, `readlink -e` equals the path, exactly one JSON value, and the file with trailing newlines stripped equals

```bash
jq -cS -s 'if length==1 then .[0] else error("record") end'
```

Failure there is shell `JournalUnknown` (line 1248). This receipt is already past that check. See below.

`a006437f65f3f756716f7ae229615bd79949eb29bb810bb7d8b10ca03e7365fa` was `sha256sum` of this `receipt.json` at 06:21Z and again at 06:31Z on 2026-11-18 (`docs/investigations/2026-11-18-retire-temp-resume-mismatch.md`, `docs/investigations/2026-11-18-retire-temp-census-unavailable.md`). Re-read it immediately before any future Close. A match does not make Close succeed.

Recompute (read-only):

```bash
ssh -o BatchMode=yes -o ConnectTimeout=15 mc@server2 'sha256sum /home/mc/antiphon-server2/retirement/c100884f1e02a03b541118d329379ce16e480/receipt.json'
```

## (b) Authority

`-AuthorityFile` was not the refusal. A missing or bad token or grant is `TempRetirementCloseRefused: Authority` (`retire-runner-volumes.ps1:46–55`), and this run printed `JournalUnknown`.

`Get-CloseAuthority` (lines 43–58) only checks that the operator token file exists and is non-empty (`scripts/lib/runner-operator-token.ps1`: `$ANTIPHON_OPERATOR_TOKEN_FILE`, otherwise `%LOCALAPPDATA%\Antiphon\operator-token`). The token value is not sent to server2. With no `-AuthorityFile`, the remote actor is `operator` and the authority is `operator-token`. Both match the host regex (`c1008-retirement.sh:1244`).

`-AuthorityFile` is optional. When present it must be a regular file whose JSON is `schema: 1`, `actor` and `authorityRef` matching `^[A-Za-z0-9][A-Za-z0-9._:/@-]{0,255}$`, and `operationId` equal to this operation. Supplying one does not move the failing check.

## (c) Id length, lifecycle, custody

The operation id is 37 characters: the prefix `c1008` plus 32 hex digits `84f1e02a03b541118d329379ce16e480`. That is `^c1008[0-9a-f]{32}$` (`retire-runner-volumes.ps1:22–24` and `c1008-retirement.sh:1242`). A mismatch would be `OperationId`, before ssh. There is no stray character.

`deploy-temp`’s current `TempRetirementUnresolved` is raised only at `c590-remote.sh:9946`, after all of the following have succeeded for this directory:

| Check | Where | What Unresolved already proves |
|---|---|---|
| Directory name and custody | `c590-remote.sh:9913` | Name matches `^c1008[0-9a-f]{32}$`, custody accepts the directory |
| Lifecycle | `:9916–9922` | `lifecycle.json` passes `c1609_lifecycle_valid`, `complete==true`, every child `joined==true`, owner pid is not the recorded startTicks |
| This receipt | `:9930` | `c1609_read_canonical` and `c1609_close_receipt_valid` both pass |
| No partial appendix | `:9931–9936` | No `closure.json` failure and no `close-pending.json` |
| State | `:9946` | `PartialRefused` (also `Applying` or `Partial`) |

Those are the same `c1609_read_canonical` and `c1609_close_receipt_valid` that `c1609_close_host` uses at lines 1248 and 1250. The third shell `JournalUnknown` site (the structural `jq` at lines 1252–1262) repeats that validator for a PartialRefused receipt whose `owner` is null. This receipt is not the unknown journal.

`c1609_clear_reason` starts as `TempRetirementJournalUnknown` (`c590-remote.sh:9896`) and is replaced with `TempRetirementUnresolved` only after those checks. A custody, lifecycle, or receipt failure on this directory would have been `TempRetirementJournalUnknown`, not the reason deploy-temp is returning.

## The check that returns 2

`c1609_close_host` then requires the immutable preview (`preview-<phaseNonce>.json`) to be canonical, `c1609_close_receipt_valid`, `state=="Ready"`, `consumed==false`, and equal to the receipt apart from ledger fields (lines 1263–1269). `c1609_prepare_resume` passed that equality on 2026-11-18 before the receipt’s `.state` was written back to PartialRefused. Close does not rewrite a receipt when it refuses. This gate is not the new failure.

The next gate is the live Docker daemon id (lines 1270–1273):

```bash
[ "$daemon" = "$(jq -r .daemon "$file")" ] || { c1609_close_fail Daemon; return 2; }
```

and the same id on `lifecycle.json`, else `ChildrenUnknown`. `c1609_temp_retirement_clearable` does not compare the daemon id. A changed id is shell `Daemon` or `ChildrenUnknown`, still printed as `JournalUnknown`, and it cannot be repaired without editing the receipt. That edit is not allowed. Confirm with the read-only command at the end. The daemon id matched at 06:13Z on 2026-11-18, when Resume rewrote this lifecycle.

If the id still matches, Close reaches the sibling scan (lines 1279–1285):

```bash
for entry in "$SERVER2_ROOT/retirement"/*; do
    [ -e "$entry" ] || { [ ! -L "$entry" ] || return 2; continue; }
    [[ "${entry##*/}" =~ ^c1008[0-9a-f]{32}$ ]] && c1609_custody "$entry" dir || return 2
    c1609_read_canonical "$entry/receipt.json" >/dev/null || return 2
    (local C1008_OPERATION="${entry##*/}"; c1609_close_receipt_valid "$entry/receipt.json") || return 2
done
```

A missing `receipt.json` makes `c1609_read_canonical` return 1. The loop then `return 2` with no reason text. The wrapper prints `JournalUnknown`.

`c1609_temp_retirement_clearable` has the exemption Close lacks (`c590-remote.sh:9924–9928`): no `receipt.json`, `lifecycle.json` `readyPublished==false`, and no closure or close-pending, and the directory is skipped. That is why deploy-temp can name this PartialRefused record while Close dies on a different directory.

Concrete sibling, lexical order before `c100884f…` (`c100871b` < `c100884f`), so today’s Unresolved walk already passed it:

- `c100871b94454a62f4cbfb51c5ad40c91bbf8`
- Preview never reached Ready (`docs/investigations/2026-11-18-retire-temp-git-audit-refusal.md`): `lifecycle` `readyPublished=false`, phase Preview, complete, children joined. No receipt was cited.
- The later census the same morning counted 28 retirement directories: 5 Completed, 2 Ready with 0 effects, 1 Refused with 0 effects, and the rest receipt-less previews (`docs/investigations/2026-11-18-retire-temp-resume-mismatch.md`). Clearable accepts those receipt-less directories. Close requires a receipt from each of them.

Nothing in `c1609_close_host` creates or removes those directories. A fresh retire-temp uses a new operation id. This Close cannot succeed while any of those directories still has no canonical receipt.

Volume absence is checked only after the sibling scan (lines 1286–1288). All four names being absent does not reach that line.

## What not to run

Do not edit `receipt.json`, `lifecycle.json`, `closure.json`, or any other file under `retirement/`. Do not delete a receipt-less directory to make the scan pass. `docs/docker-stack.md` (the Close paragraph under crash resume, and the gate-1 recovery sentence) tells the operator to Close this shape and does not mention the sibling receipt rule or the wrapper relabel. That recovery command cannot work on this host.

No `-ExpectedJournalDigest`, `-AuthorityFile`, or operation-id spelling changes the sibling scan.

## Read-only confirmation

This task did not run these. They do not write.

```bash
ssh -o BatchMode=yes -o ConnectTimeout=15 mc@server2 'sha256sum /home/mc/antiphon-server2/retirement/c100884f1e02a03b541118d329379ce16e480/receipt.json'
```

```bash
ssh -o BatchMode=yes -o ConnectTimeout=15 mc@server2 "docker info --format '{{.ID}}'; jq -r .daemon /home/mc/antiphon-server2/retirement/c100884f1e02a03b541118d329379ce16e480/receipt.json; jq -r .daemon /home/mc/antiphon-server2/retirement/c100884f1e02a03b541118d329379ce16e480/lifecycle.json"
```

```bash
ssh -o BatchMode=yes -o ConnectTimeout=15 mc@server2 'for d in /home/mc/antiphon-server2/retirement/*; do [ -e "$d/receipt.json" ] || printf "missing-receipt %s\n" "${d##*/}"; done'
```

If the three daemon ids differ, this run stopped at `c1609_close_fail Daemon` (`c1008-retirement.sh:1271`) or `ChildrenUnknown` (`:1273`), and the wrapper renamed it. If they match, the first `missing-receipt` line is the directory whose absent `receipt.json` made line 1283 return 2. Either result leaves no passing Close command.

## Docs and code

`docs/docker-stack.md` gate 1 (around lines 139–152) and the crash-resume Close example (around lines 746–759) present `-Close` as the recovery for an unresolved journal once the four volumes are absent. They do not say that a receipt-less sibling returns 2, or that `retire-runner-volumes.ps1` relabels every remote refusal as `JournalUnknown`. The 2026-11-18 notes that gave this Close command (`2026-11-18-retire-temp-resume-mismatch.md`, `2026-11-18-retire-temp-census-unavailable.md`) have the same gap: the sibling scan was described as a deploy-temp concern only.

Code defect, separate from the docs handoff: `c1609_close_host` should apply the same receipt-less exemption as `c1609_temp_retirement_clearable` (`readyPublished==false`, no receipt, no closure, no close-pending), and `Invoke-Server2Bash` should surface the remote stderr reason instead of always printing `JournalUnknown`. Until that lands, `deploy-temp` stays `TempRetirementUnresolved` on `c100884f…`.
