# CARD-1423 repair R-DEF-1..5 and A-1 (masking): Code report (task abecccc3)

Outcome: the five in-contract masking defects and the advisory from Final Review 5d4be758 are repaired on `feat/card-task-abecccc3` (fast-forward from `feat/card-task-0e93cdd7` tip `964f81b03285f6c10e0cb9eb33e91e9fd45e6f2a`; master-only; this task is the landing owner of the whole CARD-1423 delta). Red witnesses first (`ba2599f1d7c7d9a95aca59c9c8b310892c2ab942`), then the fix with the plan amendment (`536702c56c88fe849cf2ad65f066c1ef44f18b6f`), then one test-side field-name correction (`cf27b421dca014cf9cf35f7e9052be84c79fbdbd`, the tested tip). CP-1 is GREEN at `cf27b421d` through the checkpoint tool (298 harness assertions, 0 failed, 32 cases), its receipt validates against that SHA, the evidence policy reports 0 violations and `git merge-tree --write-tree origin/master HEAD` (master `294fa3d1a81ff97abb285af3180d93ee4d4b25f6`) is clean. Worktree `/work/worktrees/task-abecccc3` (server2 runner mirror). Plan `docs/superpowers/plans/2026-10-22-card-1423-monitor-followups-plan.md` (D-1, D-2 amended; V-7 added; CP-1 covers V-7).

## What changed (`scripts/postgres-cutover-monitor.ps1`)

- **R-DEF-1 (value stops at whitespace).** New `Get-CutoverSecretValueForms`: for each given URL (raw text and its `[uri]` `AbsoluteUri` spelling) every pair after the first `?` or `#` whose key passes `Test-CutoverSecretKey` registers its raw value (to the next `&`, `#` or end of line) plus the percent-escaped and percent-decoded spellings. `Get-CutoverKnownForms` = userinfo forms + secret value forms; `Invoke-PostgresCutoverMonitor` registers them for `-ServerBaseUrl`, `-PipelineUrl`, `-TaskListUrl` (`$script:CutoverKnownForms`, cleared in `finally`); `Invoke-CutoverHttpGet`'s catch adds the requested URL's forms (`-KnownForms`, alias `-KnownUserinfo`). `Hide-CutoverText` now replaces the known forms FIRST (longest first, JSON-escaped spelling too in `-JsonText`), so `?token=SEN T1` is replaced whole where the pair rule would stop at the space. The pair pass is a manual scan: a secret-keyed pair led by `?`, `&`, `#` or `&` (query/fragment) keeps the whitespace-terminated value so the text after a URL on a PLAN or MEASURED line stays; any other secret-keyed pair (connection string, free text) runs to the next `;`, `&` or end of line (`"` too in JSON text), so `Host=h;Password=SE NT;Database=d` masks `SE NT`. Plain values still stop at whitespace and are searched/decoded as before.
- **R-DEF-2 (JSON pairs, name list).** One `Get-CutoverSecretNamePattern` (`password|passwd|pwd|secret|token|key|auth|sig|signature|bearer|cookie|credential|connectionstring|connstr|dsn|apikey|api_key|authorization`) serves `Test-CutoverSecretName` (properties dropped by `Hide-CutoverSecrets`) and `Test-CutoverSecretKey` (pair keys after percent-decoding). `Hide-CutoverText` masks a JSON pair `"name":"value"` (string, number, `true`, `false` or `null` value; escaped quotes honoured) whose name is a secret name, in raw and `-JsonText` mode.
- **R-DEF-3 (lead set).** The pair lead is `(?:(?<q>[?&#]|\\u0026)|(?<![a-z0-9_.%\-]))`: any character that cannot be part of a key leads a pair; `Test-CutoverSecretValue` uses the same lookbehind. `access_token` and `my.key` stay one key.
- **R-DEF-4.** The userinfo regex has no `\b` before the scheme: `1http://u:S@h` and `_postgres://u:S@h` mask.
- **R-DEF-5.** No product change; the bound is pinned by the new case (below). Comment on `Test-CutoverEncodedSecret` states the two-sided contract.
- **A-1 (bare username).** `Test-CutoverKnownForm`: a form is registered only when at least four characters and not only digits, separators and spaces (`^[\d\s:.,;\-]*$`). `http://0@h`, `http://ms@h`, `http://1234@h` and `?token=5000` register nothing; the userinfo and pair rules still mask them inside their URL or key=value occurrence. `u:p`-style forms shorter than four characters are likewise not registered.
- Header DESCRIPTION updated. The monitor stays GET-only (`Invoke-WebRequest -Method Get` plus a TcpClient connect) and the request uses the unmasked URL.

## Red witnesses and green (`scripts/test-postgres-cutover-monitor.ps1`)

`Invoke-C1423MaskingForms`'s exception-text assertion now goes through `Invoke-CutoverHttpGet` with a throwing seam (the real catch path that adds the URL's forms) instead of a bare `Hide-CutoverText` call; every existing helper-based case stays green. All `-Case` runs below went through `scripts/build-slot.ps1`.

| Case | Red at ba2599f1d (unfixed monitor 964f81b03) | Green at cf27b421d |
|---|---|---|
| C1423_SpaceInSecretValueMasked (R-DEF-1: `;`-run connection-string values leading/trailing/JSON, verdict-line pairs kept, `-PipelineUrl token=SENTA SENTB` on every helper surface, failing `-TaskListUrl Password=SENTC SENTD` UNREADABLE/REASON/JSON, form registration, numeric value never a form) | passed=3 failed=13 (threw at `Get-CutoverKnownForms`, absent at base) | passed=17 failed=0 |
| C1423_JsonSecretPairsMasked (R-DEF-2: 13 secret names, 4 plain names, 5 JSON pair shapes x raw/JsonText, plain JSON kept, `Hide-CutoverSecrets` drop, Baseline pool diagnostics auth/privateKey/sig/nested signature) | passed=12 failed=19 | passed=31 failed=0 |
| C1423_PairLeadCharactersMasked (R-DEF-3: `( , : [ { < / \|` x raw/JSON/`Test-CutoverSecretValue`, `(Password=S)`, key characters join the key, `&` lead, pool note in result JSON) | passed=3 failed=26 | passed=29 failed=0 |
| C1423_SchemeAfterWordCharacterMasked (R-DEF-4) | passed=4 failed=2 | passed=6 failed=0 |
| C1423_DecodeBoundPinnedFromBothSides (R-DEF-5) | passed=5 failed=0 at base (adequacy witness; red against the mutants below) | passed=5 failed=0 |
| C1423_BareUsernameKeepsNumbersReadable (A-1: `http://0@fake.invalid` Baseline+Watch ROLLBACK, REASON/verdict/MEASURED/JSON numbers, form rules, URL masking) | passed=0 failed=6 (the bare `0` also corrupted the baseline path, so Watch was INVALID and the case threw) | passed=8 failed=0 (after cf27b421d fixed my assertion's field name: the Watch result records the baseline median at `baseline.medianMs`; at 536702c56 that one assertion was red 7/1) |

Adjacent cases green at 536702c56 in the same lease: C1423_SlashInUserinfoMasked 8/0, C1423_EncodedValueSecretsMasked 21/0, C1423_SpaceInUserinfoMasked 11/0, C1423_QuotedConnectionStringPasswordMasked 29/0, C1423_PlusAsSpaceInEncodedValueMasked 11/0, C1423_FiveTimesEncodedValueMasked 14/0, C1423_VerdictLinesReadableWithMaskedUserinfo 8/0, C1408_MaskingBypassForms 10/0, C1408_UrlSecretsNeverPrinted 6/0. At ba2599f1d (base monitor) C1423_SlashInUserinfoMasked 8/0 and C1423_QuotedConnectionStringPasswordMasked 29/0 confirmed the helper change keeps old cases green.

## R-DEF-5 mutants (scratch copies of harness and monitor in the scratchpad, through build-slot, removed afterwards; the tracked tree was never mutated)

| Mutant (`Test-CutoverEncodedSecret` loop) | C1423_DecodeBoundPinnedFromBothSides | C1423_FiveTimesEncodedValueMasked (old case) |
|---|---|---|
| `-lt 3` (three rounds) | RED passed=3 failed=2 ("benign encoded four times is kept" x2) | GREEN 14/0 (survives, as the Review found) |
| `-lt 5` (five rounds) | RED passed=3 failed=2 ("benign encoded five times is masked" x2) | GREEN 14/0 |

## Checkpoint (closed list, one row, one run)

Run `20261022-054903-3ff7`, `dotnet tools/Antiphon.Checkpoints/bin-c1423r2/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-22-card-1423-monitor-followups-plan.md --rows CP-1 --expected-source-sha cf27b421dca014cf9cf35f7e9052be84c79fbdbd` (exit 0 in one wait, no 75):

```
CHECKPOINT CP-1 commit=cf27b421dca014cf9cf35f7e9052be84c79fbdbd build=n/a filter=pwsh -NoProfile -File scripts/test-postgres-cutover-monitor.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=30s dirty=0 source=cf27b421dca014cf9cf35f7e9052be84c79fbdbd sourceState=clean buildSource=notApplicable
```

A command row prints `executed=n/a`; the harness's own counts are in `.antiphon/checkpoints/20261022-054903-3ff7/rows/CP-1/console.log`: `POSTGRES-CUTOVER-MONITOR TESTS passed=298 failed=0`, 298 PASS lines, 0 FAIL lines, exit 0 (Expect at least 80 passed, 0 failed; 32 cases). `validate --evidence .../report.json --expected-source-sha cf27b421d... --rows CP-1`: `CHECKPOINT SOURCE VALID source=cf27b421dca014cf9cf35f7e9052be84c79fbdbd rows=1`, exit 0. Report `.antiphon/checkpoints/20261022-054903-3ff7/report.md`. Reruns 0; repetitions of the proof selection 1. Wall 1m52s.

## Unlisted runs (all through `scripts/build-slot.ps1`, every lease `slot=granted`)

- `c1423-ckpt-build` (waited 0s, held 7s): `dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1423r2/`; both `bin-c1423r2/` directories (tool and `src/Antiphon.HostCleanup`) deleted after validation.
- `c1423-rdef-red` x2 (waited 0s, held 3s and 55s): the first lease ran no case (the `-File` driver got the comma list as one string, a wasted lease); the second is the red table above.
- `c1423-rdef-green` (waited 75s, held 104s): the six new cases plus nine adjacent cases at 536702c56.
- `c1423-a1-rerun` (waited 31s, held 4s): C1423_BareUsernameKeepsNumbersReadable at cf27b421d.
- `c1423-rdef5-mutants` x2 (waited 0s held 3s, a wasted lease from `-Command` splitting the case list; then waited 75s held 12s): the mutant table above.
- One ad-hoc pwsh probe that dot-sources the monitor with sentinel values (no server, no database, not a test driver); no file written outside the scratchpad.

## Forms tested versus out of contract (for the Review)

In contract and tested: everything in the 0e93cdd7 report plus: a space inside a secret query value of a given URL (PLAN, MEASURED, JSON url fields, request error with `AbsoluteUri` and `OriginalString`, child stdout/stderr); a space inside a secret value of a failing `-TaskListUrl` (UNREADABLE and REASON lines, result JSON); `Host=h;Password=SE NT;Database=d` and its leading/trailing/JSON-text shapes; JSON pairs `"password"`, `"token"`, `"apiKey"`, `"auth"`, `"sig"`, `"privateKey"`, numeric `"secret"`, with an escaped quote, in raw and serialized text; pool-diagnostics properties `auth`, `privateKey`, `sig`, nested `signature`; pair leads `( , : [ { < / |`; `(Password=S)` in a pool note; schemes after a digit or underscore; the decode bound from both sides; a numeric or short bare username and a numeric secret value beside D-11 numbers.

Out of contract (not masked, by design or scope; the next Review may end clean when only these remain): secret keys and names outside the list (`pass=`, `pw=`, `pin=`, `otp=`, `session=`, `nonce=`, `hash=`); scheme-less userinfo (`u:x@h` without `://`); opaque or base64 values under a plain key (`x=U2VjcmV0`); header and colon forms in free text (`Authorization: Bearer S`, `password: S`); a secret value shorter than four characters or purely numeric that appears away from its URL or key=value (the forms rule skips it so `medianMs=5000` survives; inside its pair or URL it is masked); a space inside a secret query value of a URL the monitor was NOT given (a URL inside a response body): the pair rule masks to the space; a JSON key spelled with `\u` escapes; a nested object under a secret-named JSON key in free text (the structural `Hide-CutoverSecrets` drops it for pool diagnostics; the text rule masks scalars only); a newline inside a password; credentials in response bodies other than the pool-diagnostics names and values `Hide-CutoverSecrets` handles.

Over-masking the Review should expect: the 0e93cdd7 list (later `@` on a line, two URLs collapsing, benign value encoded five or more times, a given URL's userinfo text replaced anywhere) plus: a connection-string or free-text secret pair masks to the next `;`, `&` or end of line (`error: Password=S happened` loses `happened`; `(Password=S)` loses `)`); a given URL's secret query value (four or more characters, non-numeric) is replaced wherever it appears, host names included; a secret-named JSON key anywhere in printed text masks its scalar value; property names containing `key`, `sig` or `auth` (`sortKey`, `signal`, `author`) are dropped or masked.

## Verification design outcomes

V-7 (new): passed inside CP-1 and in the red/green table. V-1, V-2, V-6: unchanged cases, green inside CP-1 (the helper's exception-text assertion now runs through `Invoke-CutoverHttpGet`). V-3, V-4, V-5: unchanged, green inside CP-1. The thirteen CARD-1408 cases stay green. No R-n in the plan.

## Policy checks

- `scripts/check-evidence-diff.ps1 -BaseRef 705026da81f9e00c6f9e6b901d9cc77fd1382873 -HeadRef HEAD` (full task base, the merge-base with master): `EVIDENCE result commits=18 entries=2 violations=0` at cf27b421d (the two entries are the earlier force-added reports; re-run over the report commit in the final message).
- `git merge-tree --write-tree origin/master HEAD` at master `294fa3d1a81ff97abb285af3180d93ee4d4b25f6`: exit 0, tree `9edb49d70a9d6b6b93d8115d0d66049cbd5d3f1e`, no conflicts (the plan file exists only on this branch, so no add/add risk).
- Platform: `GET /api/runner-defaults` revision 2 (global server2); `GET /api/session-runners` lists desktop (windows) and server2 (linux) available and eligible; the script-test row pins neither `-Runner` nor `-Platform`.
- No `bin-*` directories remain, no `/tmp/c1408-*` scratch roots remain, the tracked tree is clean, no secret in any commit (sentinels only).

## Pending for Mutation

No PCs commissioned. Candidate deliberate mutants (method-scoped, SourceLanding): the forms-first order in `Hide-CutoverText` (move the form loop after the pair pass; C1423_SpaceInSecretValueMasked); `$runRe` run-to-`;` for non-query secret pairs (restore whitespace stop); the `q` group alternatives (drop `#` or `\\u0026`); `Test-CutoverKnownForm` (drop the length or numeric test; C1423_BareUsernameKeepsNumbersReadable); `Get-CutoverSecretValueForms` (skip the `AbsoluteUri` text; drop the Escape/Unescape spellings); the JSON pair regex (string values only); `Get-CutoverSecretNamePattern` (drop `key`, `auth`, `sig`, `bearer`, `cookie`); the lookbehind lead in `Test-CutoverSecretValue`; the `\b` restored before the scheme; the decode bound at 3 or 5 (killed above). Candidate doc: `docs/superpowers/mutations/`.

Restart: none (script only).
