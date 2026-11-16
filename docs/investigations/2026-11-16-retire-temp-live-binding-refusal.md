# Retire-temp live control refusal after the CARD-1955 activation, 2026-11-16

The first live preview with the CARD-1955 tooling refused
`RecycleGitAuditUnknown reason=proof-stale`. This was not a proof-response binding
failure and not a clock bound. The desktop coordinator's **final live control
check** threw, killed the control stream, and the host's `read -t 10` for
`C1609_CONTROL_RESPONSE` then failed. The direct cause is a path length introduced
by the documented script-only export. `Assert-C1609LiveOriginProof` ran
`git ls-remote` with the renewed proof directory as its working directory. Under
the export, that directory is 264 characters. Windows cannot start a process whose
current directory reaches MAX_PATH (260). The earlier run's directory was 194
characters, so it passed the same check.

Fix: the live check starts Git in its own short scratch directory
(`c1609-live-<guid>` under the temp path) and removes it afterwards. The five-second
bound, the 900-second lifetime, the advertisement comparison and the protocol are
unchanged.

## Attempt identity

- Operation `c1008d39c6761f7344685aa02e337e47998fa`, phase nonce
  `a1cb4bb32682e28f348d0d5bcfc1205f`, 12:43:02Z to 12:50:23Z.
- The tooling export of `db584731991a7efcae75bfa1665139bac05b5170` under
  `C:\src\Antiphon\.antiphon\retirement-tooling\<sha>\`. Its evidence root is
  `...\.antiphon\rolling-server2\c7274a9d49ca8de0\`.
- The first attempt, without `-ProjectId`, refused `RecycleProjectUnresolved
  cause=NoMatch`. The export has no Git origin and is not the registered project
  path. The owner command in `docs/docker-stack.md` now passes `-ProjectId`.

All facts come from the caller's supplied host and desktop observations and from
the source at the tooling commit. This delegate had no host access.

## Which check failed

`failure-line=389` in `remote-output.txt` is `$_.InvocationInfo.ScriptLineNumber`
of the PowerShell error that the coordinator in `scripts/c590-real.ps1`
(`Invoke-C1609RetirementCoordinator`) caught. It is not a line of
`scripts/c1008-retirement.sh`. The receipt omits the script name. At the tooling
commit two PowerShell lines are numbered 389:

- `scripts/c1609-host-proof.ps1:389` is the `catch` of
  `Assert-C1609LiveOriginProof`. It rethrows every live-check failure as
  `RecycleGitAuditUnknown audit check=host-ls-remote status=2 repo=...`.
- `scripts/c590-real.ps1:389` is the coordinator's 16 MiB `output-bound` guard.

The host evidence selects the first:

1. Both proof exchanges passed the host binding check. A host `exchange-<nonce>.json`
   is written only after the marker, nonce, previous, objects, name and inventory
   bindings, freshness and digest all pass (`c1609_request_host_proof`). The host
   has both the initial and the renew record, and the desktop logged both.
2. Proof requests can only be `initial` or `renew`. No third proof exchange
   exists. The step after renew is `c1609_publication_gate ready`. It runs the
   staged render (`c1609_prepare_ready_receipt`, 12:50:00.27 to 12:50:01.10), the
   live inventory re-check (`c1609_recheck_origin_bindings effect`, finished at the
   inventory directory mtime 12:50:18.69), then prints `C1609_CONTROL_REQUEST`.
   The coordinator does not log control requests to `coordinator-exchanges.jsonl`.
3. There is no `control-*.json`, so the host never read a valid control response.
   Lifecycle completion was at 12:50:21.18. That is at most about 2.5 s after the
   request, including the refusal and the helper joins. The desktop therefore
   failed quickly. This is not the 5000 ms control budget and not a network
   deadline: 142.4 s of the 600 s network budget had been used.
4. The host refusal is `c590-remote.sh:9469`
   (`c1609_publication_gate ready || c1008_refuse ... proof-stale`). When the
   coordinator catches an exception, it closes stdin and kills ssh, so the gate's
   `read -t 10` returns non-zero.

The staged render did not change the protocol. The control request carries the
renew record's proof digest (`809a4953...`) and objects digest (`6f4da913...`).
The coordinator compares both against its own state. The renew-to-control gap was
about 35 to 40 s, against about 45 s in the passing run `c100884e`.

## Why the path broke it

| Run | Desktop renewed proof directory | Length |
|---|---|---|
| `c100884e` (rc checkout) | `C:\src\Antiphon\.antiphon\rolling-server2\c72708e6bad9b7dc\exchange-<32>\c1008-proof-<op>-<10>-<32>` | 194 |
| `c1008d39` (export) | `C:\src\Antiphon\.antiphon\retirement-tooling\<40>\.antiphon\rolling-server2\c7274a9d49ca8de0\exchange-<32>\c1008-proof-<op>-<10>-<32>` | 264 |

CARD-1693 already noted that the desktop proof directory is 194 characters. It
moved bundle writes out of that path for this reason. All proof-writer Git calls
use a short `c1609-host-<guid>` scratch directory. The live check was the only Git
call whose working directory was the proof directory
(`-WorkingDirectory ([IO.Path]::GetDirectoryName($ProofFile))`). .NET file reads,
`Get-FileHash` and the ssh transfer handle long paths. That is why both exchanges
and the proof digests succeeded at the same depth.

## Why the tests missed it

The `Assert-C1609LiveOriginProof` witnesses (the publication fixture, the native
N3 real-exchange harness and `c1609-retirement-real-cases.mjs`) run on Linux, with
fixture roots under `/var/tmp` or the temp directory. Linux has no MAX_PATH limit
on the working directory. No fixture used the export's nested evidence root. The
roughly 70 s `networkMs` per exchange is not related: the control budget starts
when the desktop receives the control request.

## Witness

`VolumeRetirementHostProofTests.Final_live_check_runs_git_outside_a_long_desktop_proof_directory`
copies a real proof into a directory of at least 264 characters. It then runs the
real `Assert-C1609LiveOriginProof` through the fixture's Git shim, which emulates
the Windows rule: a working directory of 260 or more characters throws. The test
also requires that the scratch directory is removed.

- Red at `b818d2e5b` (production unchanged): exit 2,
  `RecycleGitAuditUnknown audit check=host-ls-remote status=2 repo=repos/outer`.
  This is the live run's refusal shape.
- Green at `70313d942`: 1 passed.

The shim emulates the limit. This witness does not run native Windows.

## Residual uncertainty and the desktop confirmation

The receipt does not name the failing script, and the reason token is collapsed
into the diagnosis. Two other fast failures of the same check cannot be ruled out
from the evidence alone: a `withdrawn-ref` (the busy shared origin moved or deleted
an advertised ref in the roughly 40 s gap) or a transient Git or network failure.
One read-only desktop command separates them:

```powershell
$d = Get-ChildItem -Directory -LiteralPath 'C:\src\Antiphon\.antiphon\retirement-tooling\db584731991a7efcae75bfa1665139bac05b5170\.antiphon\rolling-server2\c7274a9d49ca8de0\exchange-8455ea35fe9eaf3e22a045d94d6b6ffe' -Filter 'c1008-proof-*' | Select-Object -First 1
$d.FullName.Length
$psi = [Diagnostics.ProcessStartInfo]::new('git'); $psi.ArgumentList.Add('--version')
$psi.WorkingDirectory = $d.FullName; $psi.UseShellExecute = $false
try { $p = [Diagnostics.Process]::Start($psi); $p.WaitForExit(); "exit=$($p.ExitCode)" } catch { $_.Exception.Message }
```

Expected: `264`, then a start failure such as "The directory name is invalid". If
Git starts and exits 0, the path is not the cause. The re-run preview then
separates `withdrawn-ref` from a transient failure.

Follow-up (not in this change): add the failing script's leaf name and the fixed
reason token to `remote-output.txt`, so that `failure-line` cannot be ambiguous.

## Activation

The landed fix needs a fresh export of the new tooling commit, invoked with
`-ProjectId d4ea7ae9-e769-474b-95b9-aa25fbc1303f` as `docs/docker-stack.md` now
shows. Do not apply operation `c1008d39`: it never reached Ready.

## Verification at `70313d942` (scripts) / `e1a8e696c` (docs)

All runs were gated, on Linux server2, with `--timeout 570s`.

- VolumeRetirementReceiptTests: 18/18 passed in five exact-method shards. The
  whole-class run aborted at the 570 s cap after 10 passed and 0 failed.
- VolumeRetirementApplyGateTests: 13/13 passed.
- VolumeRetirementHostProofTests: 15/15 passed.
- The guard union, ImpactedClassesSelectorTests*, TestTimingOperationLabelTests* and
  DockerStackDocumentationTests*: 185 passed, 1 expected platform skip
  (`C1210_Windows_linux_only_outcomes_are_exact`).
- `bash -n` is clean on the two host shell scripts. They are unchanged.
- check-evidence-diff: 0 violations. `merge-tree` against origin/master is clean.
- The policy and pinned inputs are unchanged.
