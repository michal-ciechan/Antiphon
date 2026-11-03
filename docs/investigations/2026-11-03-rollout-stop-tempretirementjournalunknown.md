# 2026-11-03 server2 rollout stop: `TempRetirementJournalUnknown` (task 4bf5220c)

Read-only diagnosis. Nothing was changed on server2 or in the canonical checkout.

## Verdict

This is a desktop-side PowerShell bug that fails every real `deploy-temp`. It is not a journal problem.
`Get-TempRetirementCensusRaw` (`scripts/deploy-server2.ps1:1187`) has the statement
`$write.GetAwaiter().GetResult(); $proc.StandardInput.Close()` without `[void]`.
`StreamWriter.WriteAsync` returns a `Task`. At run time it is `Task<VoidTaskResult>`, so PowerShell's
`GetAwaiter().GetResult()` returns a `System.Threading.Tasks.VoidTaskResult` object. That object goes to the
function's output stream, so the function returns `@(VoidTaskResult, '{"volumes":[],"journals":[]}')`.
`Assert-TempRetirementClearable` (line 1205) passes this array to `Assert-C994RawManifest -Json [string]`. The array
is joined into `"System.Threading.Tasks.VoidTaskResult {...}"`, `JsonDocument.Parse` throws, and the error comes
back as `TempContainerContextInvalid`. The catch at line 1205 renames that to `TempRetirementJournalUnknown`.

The server2 side is clean, and the new tooling accepts it as it stands:

* `/home/mc/antiphon-server2/retirement` does not exist, so there are no journals of any schema.
* None of the four `antiphon-runner-temp_*` volumes exist (checked with `docker volume ls`).
* The exact remote read packet (`New-C1609RetirementReadPacket -Clearable`, generated from `C:\src\Antiphon` at
  919d8d42f) run over `ssh mc@server2 bash -s` returns exit 0 and `{"volumes":[],"journals":[]}`, with an empty stderr.
* The custody chain `/`, `/home`, `/home/mc` and `/home/mc/antiphon-server2` is owned by uid 0 or 1000 with mode 755,
  so it is accepted.
* The server2 journal at 08:19:02Z shows the census ssh session start and complete within the same second, so it
  was not a timeout.

### Reproduction (read-only)

The test extracts both functions verbatim from the AST of `C:\src\Antiphon\scripts\deploy-server2.ps1` and runs
them against the real server2 (pwsh 7.6.9, .NET 10.0.14).

| Variant | `Get-TempRetirementCensusRaw` returns | `Assert-TempRetirementClearable` |
|---|---|---|
| As at 919d8d42f | count=2: `VoidTaskResult, String` | throws `TempRetirementJournalUnknown` (5/5 runs; inner `TempContainerContextInvalid` from `c590-real.ps1:291`) |
| With `[void]$write.GetAwaiter().GetResult()` | count=1: `String` | passes (deploy-temp would proceed to `/drain/clear`) |

The scratch scripts are in the delegate scratchpad (`census-ast2.ps1` labels each throw site; `census-ast3.ps1` runs
the as-is and voided versions).

### Why tests missed it

`VolumeRetirementWrapperTests` drives `deploy-temp` through `C727_TEST_VERIFY_STUB`. In stub mode
`Get-TempRetirementCensusRaw` skips the stdin write (`if (-not $env:C727_TEST_VERIFY_STUB)`), so the leaking
statement never runs under test. The bug came in with 85776c1c8 (2026-10-31, "bind v2 host graphs and prior proof
consumption and recheck retirement under the clear lock"). 8edda999 has no `TempRetirement*` census at all, which is
why the 2026-11-02 rollout did not hit it. It is still present on origin/master bcfe77b3d, rc/2026-11-01 10a826b9d
and rc/2026-11-03 919d8d42f.

### Same defect elsewhere

* The `retire-temp -RetirementProof <op>` path (line 1456) calls the same function, so it would fail
  `TempRetirementProofUnknown` every time.
* `scripts/deploy-server2.ps1:125` (`Invoke-HostJq`) has the same unvoided `$inputTask.GetAwaiter().GetResult()`.
  It leaks a `VoidTaskResult` into `Invoke-HostJq`'s output, which does not break the gate. Void it as hygiene.

## Answers to the brief

1. **Where it is raised and why.** `deploy-server2.ps1` raises it in `Get-TempRetirementCensusRaw` (1196: any
   transport, exit or empty-output failure) and in `Assert-TempRetirementClearable` (1205: manifest or JSON parse;
   1208: a remote `error` that is not one of the three known reasons; 1211/1214: wrong shape or volume name; 1220/1221:
   journal schema other than 2; 1239: unknown state). It is also raised by `c1609-read-packet.ps1` and by
   `c1609_temp_retirement_clearable` (the default `c1609_clear_reason`, `c590-remote.sh:9726`). The function reads the
   four temp volumes and `$SERVER2_ROOT/retirement/c1008<32hex>/{receipt,lifecycle,closure,close-pending}.json` on
   server2. In this run it read nothing bad: the manifest-parse site (1205) fired on the leaked object. "Unknown" here
   means only "the census could not be interpreted". The name points at journals, but it is a catch-all.
2. **Old versus new tooling.** The old tooling (8edda999) wrote no retirement journal. The 2026-10-30 retirement
   and the 2026-11-02 manual volume removal left no `retirement/` directory. The new tooling treats a missing root as
   clearable (`[ -d "$root" ] || { [ ! -e "$root" ]; return $?; }`), so this is **neither** a CARD-1609 schema
   rejection **nor** a missing record.
3. **Documented recovery.** `docs/docker-stack.md` (gate 1 text after the rollout table) documents only
   `TempVolumesRetained` (remove the volumes through retire-temp) and unresolved journals (Resume, or
   `retire-runner-volumes.ps1 -Close`). Neither applies: there are no volumes and no journal. No operator record or
   declaration is needed, and no apply-gate action is needed. `ANTIPHON_VOLUME_RETIREMENT_APPLY` is irrelevant and
   must stay unset. No documented operator path gets past the bug. The only bypass is the test seam
   `C727_TEST_VERIFY_STUB`, which must not be used in production.
4. **Minimal safe fix (Code or Debug).** Two one-token edits in `scripts/deploy-server2.ps1`:
   line 1187 becomes `[void]$write.GetAwaiter().GetResult(); $proc.StandardInput.Close()`, and line 125 becomes
   `[void]$inputTask.GetAwaiter().GetResult()`. This changes nothing on server2.
   **Red-first witness:** add a test to `tests/Antiphon.Tests/Scripts/VolumeRetirementWrapperTests.cs` (or a sibling)
   that runs a `pwsh -NoProfile` child. The child dot-sources `c590-real.ps1` and `c1609-read-packet.ps1`, extracts
   `Get-TempRetirementCensusRaw` and `Assert-TempRetirementClearable` verbatim from the `deploy-server2.ps1` AST, and
   leaves `C727_TEST_VERIFY_STUB` unset. Prefix `PATH` with a temp directory holding a fake `ssh`
   (`#!/bin/sh` / `cat >/dev/null` / `printf '{"volumes":[],"journals":[]}\n'`, mode 0755; Linux only, skip on
   Windows). The child should print `@(Get-TempRetirementCensusRaw).Count` and run `Assert-TempRetirementClearable`.
   Assert count == 1 and no throw. This is red at 919d8d42f (count 2, `TempRetirementJournalUnknown`) and green after
   the `[void]`. Optional second witness: the same fake `ssh` printing a Ready proof JSON for the `-ProofOperation` path.
   Optional follow-up: write a `temp-retirement-read.json` evidence row (exit, elapsed ms, inner reason) so later
   failures do not all collapse into one token.
5. **Alternatives.** Running `redeploy-old` without a temp contradicts the continuous-dispatch rule
   (`docs/docker-stack.md` lines 18-20: "there must always be a server2 (standing or temp) accepting work"). The
   preceding `drain-old` zero gate would also drain the only server2 runner, so server2 would stop dispatching for
   the whole drain and swap. The doc does not allow it as a planned path. Not rolling now leaves server2 on 8edda999
   with the new server, which the caller's checklist records as supported. Nothing was deployed. The standing container
   is unchanged: `antiphon-runner-session-runner-1` id `4551c5a7852f8c617e75951cb8f9c18565a74f060d0fc23a8af1f6694680d56c`,
   StartedAt `2026-10-29T23:46:39.884887321Z`, image `antiphon-server2/session-testing:8edda999623a`.

## Resuming the rollout after the fix

`deploy-temp` does not bind local HEAD to `-Sha`. Only `Invoke-HostJq -Mode provision` checks HEAD == Sha, and
deploy-temp runs `-Mode check`. Once the canonical checkout carries the fix, the orchestrator can re-run
`pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd -Phase deploy-temp`
(this keeps the image at the activated cut), or use `-Sha <new rc tip>`. Which one is the orchestrator's decision,
weighed against the "no rc pushes while a run is healthy" rule. The retire-temp `-RetirementProof` path needs the
same fix before gate 9's apply.
