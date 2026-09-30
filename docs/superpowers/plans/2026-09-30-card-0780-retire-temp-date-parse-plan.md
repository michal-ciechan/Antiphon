# CARD-0780: retire-temp-runner accepts a real tempRetiredAt on PowerShell 7

Date: 2026-09-30. Stage: Plan, with the verification design folded in (the brief asks for the
closed `### Checkpoints` table). Next: Code.
Baseline: `a8b4e9e5a0715a644a21c4270dac8b4d60810d4a`.
Card `cf40a786-5d51-4dd6-bfc9-0feebb4ab052` on board `8988ca03-7414-47ad-b0b6-51556c701703`,
read with `scripts/card.ps1 get CARD-0780`. Owner docs read: `docs/docker-stack.md` (host lane,
`verify-docker-stack.ps1`), `docs/testing-and-build.md` (Checkpoint manifest, Checkpoint runner
tool, Build slots, Combined class filters), the CARD-0727 plan's D-17 (the retire-temp phase and
its offline harness).

## Outcome and scope

Make `scripts/c590-real.ps1`'s `retire-temp-runner` case accept the `tempRetiredAt` value that
`scripts/deploy-server2.ps1` writes into the manifest, on every PowerShell 7 host, by turning the
`[datetime]` that `ConvertFrom-Json` hands back into the ISO-8601 UTC text the runner wrote before
the existing shape regex runs. Keep the regex and its `tempRetiredAt rejected` refusal: the value
is single-quoted into a bash `export` line on server2. Add a regression test that drives the
unmodified `verify-docker-stack.ps1` (its line 13 is the exact `ConvertFrom-Json` call) with a real
ISO-8601 manifest and asserts the bridge exports the original timestamp, red at the baseline.
Record the PowerShell 7 date-parsing behaviour in `docs/docker-stack.md`.

Footprint: `scripts/c590-real.ps1` (one block), new
`tests/Antiphon.Tests/Scripts/RetireTempRunnerTimestampTests.cs`, `docs/docker-stack.md` (one
sentence). No change to `scripts/deploy-server2.ps1` (its write side is already correct, see ground
truth), `scripts/verify-docker-stack.ps1`, `scripts/c590-remote.sh`, `scripts/test-deploy-server2.ps1`
or its `c727-fake-*` fixtures, `tests/linux-test-roster.json`, AGENTS.md or any bundle.

## Ground truth

Measured during this Plan on the server2-temp runner (Linux, pwsh 7.5.4, invariant culture,
`TZ=Etc/UTC`) from the worktree at the baseline. The card's own observation was pwsh 7.6.6 on the
Windows desktop.

| Card / brief assumes | What the code and the host do | Consequence |
|---|---|---|
| `[string]$Manifest.tempRetiredAt` renders a culture date, not the ISO text. | Reproduced. `'{"tempRetiredAt":"2026-09-27T18:47:03.0367129Z"}' \| ConvertFrom-Json` returns `System.DateTime` Kind=Utc; `[string]` gives `09/27/2026 18:47:03` even under the invariant culture; `-match '^[0-9TZ:+.-]{10,40}$'` is `False`. Running the unmodified `verify-docker-stack.ps1 -Case retire-temp-runner` with such a manifest and `ANTIPHON_C590_STUB` unset exits 1 at `c590-real.ps1:276` (`tempRetiredAt rejected`) before any `ssh` or `scp` call. | Every real retire-temp run fails unconditionally on any PowerShell 7 host, not only under one locale. The fix belongs where the value is consumed and validated (D-1). |
| The manifest carries the `Z` form that `deploy-server2.ps1` writes. | True: `deploy-server2.ps1:169-172` already turns the status `retiredAt` (a `[datetime]` after `ConvertFrom-Json` of the DTO's `DateTimeOffset?`, `PhoneHomeContracts.cs:381`, which System.Text.Json writes as `+00:00`) into `yyyy-MM-ddTHH:mm:ss.fffffffZ` and `ConvertTo-Json` writes that string. | The write side is correct; this card is read-side only. `deploy-server2.ps1` stays untouched (D-8). |
| The existing fixtures bypass the path. | True: `scripts/fixtures/c727-fake-verify.ps1` never reads the manifest, so `test-deploy-server2.ps1` T-1 passes with a broken bridge; `C590Harness.RunAsync` always sets `ANTIPHON_C590_STUB=1`, which skips `Invoke-C590LiveCase`; no test under `tests/` mentions `tempRetiredAt`. The stub arm at `verify-docker-stack.ps1:206` only tests non-emptiness and works on a `[datetime]`. | A new test must run the live branch with `ANTIPHON_C590_STUB` unset and intercept the bridge's native calls (D-2). |
| The card's reformat (`-is [datetime]` then ISO-8601 UTC) is a complete fix. | Measured on a scratch copy of the three scripts with the block from D-1 applied: `...0367129Z` exports `'2026-09-27T18:47:03.0367129Z'` (exact: `DateTime` ticks are 100 ns, so seven fractional digits round-trip); `...0367129+00:00` (parsed Kind=Local) and `2026-09-27T19:47:03.0367129+01:00` both export the same `Z` string; the injection shape `2026'; touch /tmp/c780; '` stays a `String` under `ConvertFrom-Json`, is refused with `tempRetiredAt rejected`, and no `ssh`/`scp` runs. A zone-less string parses as Kind=Unspecified, which `ToUniversalTime()` treats as local; no producer writes one. | D-1's expected values and D-3's four test rows are measured, not predicted. |
| Reading the manifest with a parser that does not convert dates is an alternative. | `ConvertFrom-Json -DateKind String` exists on this host (7.5.4) and returns `System.String`; it is a 7.5+ parameter at the shared read site (`verify-docker-stack.ps1:13`) that serves every case. | Rejected in D-1: a version floor and a type change for ~80 cases to fix one field. |
| The remote needs the exact text. | `scripts/c590-remote.sh:1567-1575` `case_retire_temp_runner` uses `C590_TEMP_RETIRED_AT` for the `TempRunnerNotRetired` emptiness refusal and writes it verbatim into `temp-down.txt` as evidence. The bridge writes it as `export C590_TEMP_RETIRED_AT='<value>'` (`c590-real.ps1:322`). | V-1 asserts the export line, not just the exit code. |
| A test can intercept `ssh`/`scp` without a PATH shim. | Measured: with `function ssh { ... }` defined in the session, `& ssh -o BatchMode=yes host "echo hi"` runs the function (PowerShell resolves Function before Application for the call operator). Dot-sourcing `verify-docker-stack.ps1 -Case ... -Manifest ...` from a probe binds its mandatory parameters, keeps its own `$PSScriptRoot` for the two nested dot-sources, and `Write-C590Result`'s `exit` ends the probe with the case's code. Four native calls were intercepted per accepted run. | D-2's probe design is proven on the unmodified verifier. |
| Platform. | `GET /api/runner-defaults` at plan time: global default `server2`, supported kinds Grok/ClaudeCode/Codex. `GET /api/session-runners`: `desktop` (windows, accepting), `server2` (linux, draining, `acceptingNewWork:false`), `server2-temp` (linux, accepting). | The work is platform-neutral (D-7); no runner pin, no platform. |

## Decisions

### D-1: reformat a parsed `[datetime]` to ISO-8601 UTC in the retire-temp-runner block, before the regex

In `Invoke-C590LiveCase` (`scripts/c590-real.ps1`, the `if ($Case -eq 'retire-temp-runner')`
block, lines 274-277 at the baseline) replace the single `[string]` cast with:

```powershell
if ($names -contains 'tempRetiredAt' -and $Manifest.tempRetiredAt) {
    # CARD-0780. PowerShell 7's ConvertFrom-Json returns an ISO-8601 string as [datetime];
    # [string] on that is the culture form (09/27/2026 18:47:03), which the regex below
    # rejects. Put it back into the ISO-8601 UTC text the runner wrote.
    $rawRetiredAt = $Manifest.tempRetiredAt
    $tempRetiredAt = if ($rawRetiredAt -is [datetime]) {
        $rawRetiredAt.ToUniversalTime().ToString('o')
    } else { [string]$rawRetiredAt }
}
if ($tempRetiredAt -and $tempRetiredAt -notmatch '^[0-9TZ:+.-]{10,40}$') { throw 'tempRetiredAt rejected' }
```

The regex line is unchanged and still runs after the reformat. The file stays ASCII-only.

Why: the value is consumed, validated and exported here, so the conversion sits next to the guard
it feeds. `ToUniversalTime()` returns Kind=Utc for the Utc and Local kinds PowerShell produces for
`Z` and `+hh:mm` inputs, and the round-trip format `'o'` then writes `yyyy-MM-ddTHH:mm:ss.fffffffZ`
with no culture-substitutable separators; for a Utc value it is byte-for-byte what
`deploy-server2.ps1:170` writes with its explicit format. Seven fractional digits are the
`DateTime` tick resolution, so the original text round-trips exactly (measured).

Rejected: `ConvertFrom-Json -DateKind String` at `verify-docker-stack.ps1:13` (introduces a
PowerShell 7.5 floor at the one read site every case shares and changes the type of every
date-shaped field in every manifest; the desktop is 7.6.6 and this runner 7.5.4, but the floor buys
nothing here). Raw-text or `System.Text.Json.JsonDocument` extraction of one field (a second parser
for the same file). Writing a non-date-looking token from `deploy-server2.ps1` (the field name is
the CARD-0727 D-17 contract and its text is the remote's `temp-down.txt` evidence). Forcing
Kind=Unspecified to UTC with `SpecifyKind` (no producer writes a zone-less value, the pre-bug path
accepted zone-less text verbatim, and it would diverge from `deploy-server2.ps1`'s expression).
Dropping the regex now that the value is normalised (the `else` branch still passes arbitrary
manifest text into a single-quoted bash export).

### D-2: the regression test dot-sources the unmodified verifier with `ssh` and `scp` shadowed

The test writes a probe script to a fresh temp root and runs `pwsh -NoProfile -File probe.ps1`
with `ANTIPHON_C590_STUB` removed from the child environment. The probe:

```powershell
$ErrorActionPreference = 'Stop'
function ssh { Add-Content -LiteralPath $env:C780_TRACE -Value ('ssh ' + ($args -join ' ')) }
function scp {
    Add-Content -LiteralPath $env:C780_TRACE -Value ('scp ' + ($args -join ' '))
    if ($args -contains '-r') {
        $dest = [string]$args[-1]
        $case = ([string]$args[-2]).Split('/')[-1]
        $dir = Join-Path $dest $case
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        '{"accepted":true,"diagnosis":"c780-fake-remote","exit":0}' |
            Set-Content -LiteralPath (Join-Path $dir 'c590-result.json') -Encoding ascii
    }
}
. $env:C780_VERIFY -Case retire-temp-runner -Manifest $env:C780_MANIFEST
```

`C780_VERIFY` is `<repo>/scripts/verify-docker-stack.ps1` (from `C590Harness.RepoRoot`),
`C780_MANIFEST` the manifest path, `C780_TRACE` a file that exists only after the first native
call. The manifest is written by the test as JSON with the five keys `deploy-server2.ps1`'s
`Invoke-HostCase` writes: `evidenceRoot` (the temp evidence directory), `sourceSha` (40 hex),
`runId` (`c780test`), `c604Branch` (`master`), `tempRetiredAt` (the row's input).

Why: the card names `verify-docker-stack.ps1`'s `ConvertFrom-Json` as the line to round-trip
through, and dot-sourcing runs that exact line, then the unmodified bridge, then the real
`Write-C590Result`. Shadow functions intercept the bridge's four native calls (`ssh mkdir`, `scp`
of `c590-remote.sh`, `ssh` of the export line, `scp -r` of the result) with no PATH shim and no
per-OS `.cmd`, so one probe text serves Windows and Linux. The fake `scp -r` writes the result file
the bridge then reads, so the accepted path is exercised end to end. The case name is taken from
the last `/` segment of the remote source, not `Split-Path`, because `mc@server2:/...` is not a
filesystem path on Windows.

Rejected: `C590Harness.RunAsync` (it always sets `ANTIPHON_C590_STUB=1`, and a live flag would
still need the interception). PATH shims as in `ClaudeTokenRefreshOptInTests` (three files: a
`.cmd` and a `sh` wrapper each delegating to a `.ps1`, for the same effect). Extracting the reformat
into a function and unit-testing it (does not pass through line 13, and a green helper says nothing
about the guard order). A T-7 in `scripts/test-deploy-server2.ps1` (its verifier is the fake
`c727-fake-verify.ps1`; the wrapper under test is not modified). A text-contract assertion in
`RemoteScriptContractTests` on the new lines (the behavioural rows already fail if the reformat is
missing or runs after the regex; a second, brittle copy of the same fact).

### D-3: four measured rows in one new Integration class

`tests/Antiphon.Tests/Scripts/RetireTempRunnerTimestampTests.cs`, class
`RetireTempRunnerTimestampTests`, `[Category("Integration")]`, `[ParallelLimiter<ProcessSpawnLimit>]`
(it spawns `pwsh`, like `ClaudeTokenRefreshOptInTests`). The probe text is a raw string inside the
test file, as that class does. All files ASCII.

1. `Bridge_exports_a_parsed_manifest_timestamp_as_iso_8601_utc(string input, string expected)`
   with three `[Arguments]` rows:
   `("2026-09-27T18:47:03.0367129Z", "2026-09-27T18:47:03.0367129Z")`,
   `("2026-09-27T18:47:03.0367129+00:00", "2026-09-27T18:47:03.0367129Z")`,
   `("2026-09-27T19:47:03.0367129+01:00", "2026-09-27T18:47:03.0367129Z")`.
   Asserts: exit code 0 (message = merged output); output contains `DIAGNOSIS=c780-fake-remote`;
   `<evidence>/c590-result.json` has `accepted: true`; the trace contains
   `export C590_TEMP_RETIRED_AT='<expected>'`; output does not contain `tempRetiredAt rejected`.
   The second and third rows prove the reformat preserves the instant and runs before the regex.
2. `Bridge_still_rejects_a_non_timestamp_before_any_remote_call()` with input
   `2026'; touch /tmp/c780; '`. Asserts: exit code not 0; output contains `tempRetiredAt rejected`;
   the trace file does not exist.

Four TUnit results. Measured at the baseline: rows 1a-1c exit 1 with `tempRetiredAt rejected` and
no trace (red on the first assertion, with the pwsh error in the message); row 2 passes. Measured
with D-1 applied to a scratch copy: rows 1a-1c export the expected `Z` string with four intercepted
calls and an accepted result; row 2 still refuses with no trace, and `/tmp/c780` was never created.

### D-4: documentation

`docs/docker-stack.md`, the paragraph that begins "Windows CP-1 through CP-5 passed on": append
one sentence stating that PowerShell 7's `ConvertFrom-Json` returns an ISO-8601-looking manifest
field (`tempRetiredAt`) as `[datetime]`, that `Invoke-C590LiveCase` reformats it with
`ToUniversalTime().ToString('o')` before validating, and that a case must never `[string]` such a
field directly (CARD-0780). Keep every phrase `DockerStackContractTests` asserts on that document;
do not introduce the phrase "delivers from the vault", which it asserts absent. No AGENTS.md change.

### D-5: no roster or allowlist change

The class is Integration, not Slow, so `slow-tests-allowlist.txt` is untouched.
`tests/linux-test-roster.json` is the frozen CARD-0590 Docker-lane roster; it already lists none of
`RemoteScriptContractTests`, `ClaudeTokenRefreshOptInTests` or `BuildSlotScriptTests`, the
checkpoint tool does not read it, and CARD-0800 D-5 set the same precedent.

### D-6: scope for the Code stage

`-Scope "ops,docs,tests/Antiphon.Tests/Scripts/RetireTempRunnerTimestampTests.cs"` (`ops` is
`scripts/**`, `docs` is `docs/**` with weight allow; the test file is in no area). A Code task
already touching `scripts/c590-real.ps1`, `scripts/verify-docker-stack.ps1`,
`scripts/c590-remote.sh` or `scripts/deploy-server2.ps1` defers this one.

### D-7: lane

Platform-neutral. No `-Runner`, no `-Platform`. The defect exists on every PowerShell 7 host (the
card saw it on the Windows desktop at 7.6.6 under a `dd/MM/yyyy` culture; this Plan reproduced it on
Linux at 7.5.4 under the invariant culture), the test spawns only `pwsh`, and the probe has no
per-OS branch. The checkpoint rows run on whichever runner the Code stage lands on; on Linux the
checkpoint tool and `run-checkpoint.ps1` add `UseAppHost=false` themselves. At plan time the global
default runner `server2` was draining and `server2-temp` was accepting.

### D-8: out of scope

`scripts/deploy-server2.ps1` (its explicit format at line 170 is equivalent for a Utc value; the
write side is not the defect). The `409 phone_home_unsupported_operation` refusal the card met on
the forced-retire API for a pre-retire-runtime container (a separate card if wanted). The stub arm
at `verify-docker-stack.ps1:206`. `scripts/c590-remote.sh`. The `[string]` casts on the other
manifest fields in `Invoke-C590LiveCase` (`runId`, `sourceSha`, `project`, `checkpoint`, `filter`,
`c604Branch`, `c604Origin`) are not date-shaped.

## Slices

### S1: red-first (test only)

Files: new `tests/Antiphon.Tests/Scripts/RetireTempRunnerTimestampTests.cs` (D-2, D-3).
Commit: `test(CARD-0780): pin the retire-temp-runner timestamp round trip red-first`.

Acceptance: CP-1 red exactly as its `Expect` says (three failures naming `tempRetiredAt rejected`,
one pass).

### S2: fix and doc

Files: `scripts/c590-real.ps1` (D-1; the block only; ASCII), `docs/docker-stack.md` (D-4).
Commit: `fix(CARD-0780): reformat a parsed tempRetiredAt to ISO-8601 UTC before the bridge regex`.

Acceptance: CP-2, CP-3 and CP-4 green; `git diff --stat` for S2 shows only those two files; the
regex line and `throw 'tempRetiredAt rejected'` are unchanged.

## Verification design

No build or test ran during Plan; the ground truth came from `pwsh` directly against the baseline
scripts and against a scratch copy carrying D-1, outside the worktree. Existing method counts at
the baseline: `RemoteScriptContractTests` 28 `[Test]` (one skips on Windows, where there is no
Linux shell), `ClaudeTokenRefreshOptInTests` 5 `[Test]` with one two-row `[Arguments]` method
(6 results), `DockerStackContractTests` 110 `[Test]`.

| ID | Evidence |
|---|---|
| V-1 | `RetireTempRunnerTimestampTests.Bridge_exports_a_parsed_manifest_timestamp_as_iso_8601_utc`, row `Z`: the export line carries the original text, the result is accepted, exit 0. |
| V-2 | Same method, rows `+00:00` and `+01:00`: the export line is the same UTC instant in `Z` form, which also proves the reformat runs before the regex. |
| V-3 | `RetireTempRunnerTimestampTests.Bridge_still_rejects_a_non_timestamp_before_any_remote_call`: refusal with `tempRetiredAt rejected` and no `ssh`/`scp` call. |
| R-1 | `ClaudeTokenRefreshOptInTests` (6 results): `c590-real.ps1` still dot-sources and the deploy-parent path is unchanged. |
| R-2 | `RemoteScriptContractTests` (28, 27 on Windows): the live roster and stub-arm contracts are unchanged. |
| R-3 | `DockerStackContractTests` (110): the bridge and `docs/docker-stack.md` text contracts hold after D-1 and D-4. |
| R-4 | Review reads `c590-real.ps1`: the reformat precedes the unchanged regex; `'o'` follows `ToUniversalTime()`; no other case is touched; ASCII only. |

### Positive controls for the later Mutation stage (method-scoped)

- PC-1: replace the `-is [datetime]` branch with the baseline `[string]$Manifest.tempRetiredAt`.
  Run `/*/*/RetireTempRunnerTimestampTests/Bridge_exports_a_parsed_manifest_timestamp_as_iso_8601_utc`:
  all three rows red (exit 1, `tempRetiredAt rejected`). Restore, rerun, green.
- PC-2: drop `.ToUniversalTime()` and format the parsed value directly. Same filter: the two offset
  rows red (the export line ends in `+00:00` or `+01:00`, not `Z`). Restore, green.
- PC-3: remove the `throw 'tempRetiredAt rejected'` line. Run
  `/*/*/RetireTempRunnerTimestampTests/Bridge_still_rejects_a_non_timestamp_before_any_remote_call`:
  red (exit 0, trace present). Restore, green.

Code does not run these; Mutation records its own leased, method-scoped red/restore runs.

### Execution

Run the table through the checkpoint tool, one run per committed slice group
(`dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-30-card-0780-retire-temp-date-parse-plan.md`,
then `wait` until the exit is not 75). If the tool is unavailable on the host, run each row with
`scripts/run-checkpoint.ps1`, which takes its own build slot; CP-3 and CP-4 reuse CP-2's output
with `-NoBuild`:

```text
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-c780/ -Filter '/*/*/RetireTempRunnerTimestampTests/*' -MinExecuted 4 -Expect RetireTempRunnerTimestampTests
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-2 -Project tests/Antiphon.Tests -OutputPath bin-c780s2/ -Filter '/*/*/RetireTempRunnerTimestampTests/*' -MinExecuted 4 -Expect RetireTempRunnerTimestampTests
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-3 -Project tests/Antiphon.Tests -OutputPath bin-c780s2/ -NoBuild -Filter '/*/Antiphon.Tests.Scripts/(RemoteScriptContractTests*)|(ClaudeTokenRefreshOptInTests*)/*' -MinExecuted 33 -Expect RemoteScriptContractTests,ClaudeTokenRefreshOptInTests
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-4 -Project tests/Antiphon.Tests -OutputPath bin-c780s2/ -NoBuild -Filter '/*/*/DockerStackContractTests/*' -MinExecuted 110 -Expect DockerStackContractTests
```

Report every `CHECKPOINT` line with executed/passed/failed/skipped and the TRX path. A red in
CP-3 or CP-4 that is not in the new class is checked at the base commit for the same class before
it is attributed to S2. Delete every `bin-c780*` directory once verification is complete.
`scripts/test-deploy-server2.ps1` is not a row: the deploy wrapper is unchanged and its fake
verifier never reaches `c590-real.ps1`.

### Cost

Ordinary Code floor: 6 + 6 + 2 + 1 = **15 minutes** (two isolated builds of `tests/Antiphon.Tests`
at about 4 minutes each on server2 under a slot, CARD-0738 via CARD-0800; the new class spawns
four `pwsh` processes). Authoring: 30-45 minutes. Code `ExpectAbout`: 50-60 minutes. No open
decisions or external prerequisites.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c780/` | retire-timestamp-red | `/*/*/RetireTempRunnerTimestampTests/*` | V-1, V-2, V-3 (red) | 4 executed, 3 failed, 1 passed: the three `Bridge_exports_a_parsed_manifest_timestamp_as_iso_8601_utc` rows fail on exit code 1 with `tempRetiredAt rejected` in the message and no trace; the reject method passes; not rerun | 4 | 6 |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c780s2/` | retire-timestamp-green | `/*/*/RetireTempRunnerTimestampTests/*` | V-1, V-2, V-3 | all 4 results, 0 failed, 0 skipped | 4 | 6 |
| CP-3 | S1-S2 | CP-2 | bridge-readers | `/*/Antiphon.Tests.Scripts/(RemoteScriptContractTests*)\|(ClaudeTokenRefreshOptInTests*)/*` | R-1, R-2 | 34 executed on Linux (33 on Windows, where one `RemoteScriptContractTests` method skips), 0 failed | 33 | 2 |
| CP-4 | S1-S2 | CP-2 | stack-contract | `/*/*/DockerStackContractTests/*` | R-3 | all 110 results, 0 failed | 110 | 1 |
