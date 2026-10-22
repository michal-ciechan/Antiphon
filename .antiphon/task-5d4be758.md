# CARD-1423 DEF-1 repair: Final Review (task 5d4be758)

Outcome: four in-contract masking misses that print a credential (all four already present at master 673a8c6d7 and at base 3a74807d4, so none is a regression) and one adequacy defect. The DEF-1 repair itself holds. Subject: Code task 0e93cdd7, `feat/card-task-0e93cdd7` at `964f81b03285f6c10e0cb9eb33e91e9fd45e6f2a`. The tested source is 2db914c83; the only change after it is the report file.

## Receipt

- CP-1 ran through the checkpoint tool, run `20261022-050228-e272`, at 964f81b03 on server2. It exited 0 in one wait (no 75), slot=granted, waited=45s, sourceState=clean. The harness printed `POSTGRES-CUTOVER-MONITOR TESTS passed=208 failed=0`.
- `scripts/validate-checkpoint-receipt.ps1 -Evidence .../report.json -ExpectedSourceSha 964f81b03... -Rows CP-1` printed `CHECKPOINT SOURCE VALID rows=1` and exited 0.
- `git merge-tree --write-tree origin/master HEAD` against master 673a8c6d7 exited 0 with tree 9ecf6e42, no conflicts.
- `check-evidence-diff.ps1 -BaseRef 705026da8 (merge-base) -HeadRef HEAD` reported commits=15, entries=2, violations=0. The two force-added reports (`task-173228b3.md` and `task-0e93cdd7.md`) contain only placeholder values (`S`, `SENT...1423`) and no secrets.
- GET-only: the monitor's only HTTP call is `Invoke-WebRequest -Method Get`, plus a TcpClient connect. No probe or test reached a real host.

## DEF-1 repair checks (all OK)

These forms were masked on the surfaces tested:
- userinfo passwords holding `#`, `?`, `%` or `@`
- two URLs on one line
- IPv6 hosts, including a zone id
- a URL inside a JSON string
- `Password=` and `Pwd=` with `;` separators, quoted with either quote kind, and with doubled-quote escapes
- `token=` and `api_key=`, plus `access%5Ftoken` and `%70assword` keys
- encoded secrets: 1x, 2x, `+` read as a space, upper and lower case `%3d`, an encoded userinfo URL (1x and 2x), and `a%2BPassword%3D`

D-11 readability holds with `u:p` userinfo: the REASON, ROLLBACK and CUTOVER-MONITOR lines kept their numbers.

## Defects

**R-DEF-1: a secret pair's value stops at whitespace.**
- Where: `Hide-CutoverText` `$valClass` (`scripts/postgres-cutover-monitor.ps1:230`).
- Failure: with `-PipelineUrl 'http://fake.invalid/api/agent-tasks/pipeline?token=SEN T1'`, the PLAN line prints `?token=<redacted> T1`. `[uri]` sends the value as `SEN%20T1`, so the whole value is the secret. With `-TaskListUrl '...?Password=SEN TERR'` (the request throws with the URL), the UNREADABLE and REASON lines print `<redacted> TERR`. `Host=h;Password=SE NT;Database=d` prints `NT`.
- Why: this is the DEF-1 class (a raw character that .NET keeps inside the value) applied to key=value pairs. The repair covered userinfo only.
- Fix: register the given URLs' secret-key query values as known forms, the same way userinfo is registered. Take the raw text up to `&`, `#` or end of line, plus its escaped and unescaped spellings and the `[uri].Query` spelling. In `;`-separated connection-string text, run the value to the next `;`.

**R-DEF-2: JSON pairs for the secret-key list are not masked.**
- Where: `Test-CutoverSecretName` (line 131) lacks `key|auth|sig|signature` from `Test-CutoverSecretKey`. `Hide-CutoverText` has no `"k":"v"` or `k: v` rule.
- Failure: a diagnostics body `{"pool":{"auth":"SENTAUTH","privateKey":"SENTKEY","sig":"SENTSIG"}}` is written verbatim into the Baseline result JSON (confirmed through `Invoke-PostgresCutoverMonitor -Mode Baseline -Execute` with an HttpGet seam). `{"password":"S"}`, `{"token": "S"}` and `"apiKey":"S"` pass through `Hide-CutoverText` unchanged, in both raw and `-JsonText` mode.
- Fix: make `Test-CutoverSecretName` cover at least the secret-key list. Mask JSON pairs (`"<secret key>"\s*:\s*"..."`, aware of escaped quotes) in `Hide-CutoverText`, in both modes.

**R-DEF-3: the pair lead set is too narrow.**
- Where: `$lead` `(?:^|[?&;#"'\s]|\\u0026)` (line 213), and the `(^|[;\s])` prefix in `Test-CutoverSecretValue` (line 141).
- Failure: these print whole: `(Password=S)`, `Host=h,Password=S`, `error:Password=S`, `[Password=S]`, `{Password=S}`, `<Password=S>`, `/Password=S`, `|Password=S`. The pool-diagnostics string `"note":"(Password=SENTPAREN)"` is written verbatim into the result JSON (confirmed).
- Fix: use a lead of "not preceded by a key character", `(?<![a-z0-9_.%\-])`, in both places.

**R-DEF-4 (low): the userinfo rule needs a word boundary before the scheme.**
- Where: the `\b` in the userinfo regex (line 212).
- Failure: `1http://u:S@h` and `_postgres://u:S@h` are printed unmasked.
- Fix: drop `\b`, so the scheme is matched from its first letter.

**R-DEF-5 (adequacy): the decode-bound mutant survives.**
- Where: `scripts/test-postgres-cutover-monitor.ps1`, `C1423_FiveTimesEncodedValueMasked`.
- Failure: the throwaway mutant `for ($i = 0; $i -lt 3; ...)` passes that case 14/0 and the whole harness 208/0. The post-bound "still encoded" rule masks a secret pair anyway, so the mutant only over-masks a benign value encoded exactly 4 times, and no case pins that.
- Fix: assert that a benign value encoded 4 times is kept (and one encoded 5 times is masked).

## Mutations (scratch copies through build-slot, now removed)

| Mutant | Run | Result |
|---|---|---|
| userinfo end narrowed to `[^\s"']*@` | `C1423_SpaceInUserinfoMasked` | RED, passed=8 failed=3 |
| userinfo end narrowed to `[^\s"']*@` | `C1423_ApostropheInUserinfoMasked` | RED, passed=7 failed=2 |
| decode loop stopped at 3 passes | `C1423_FiveTimesEncodedValueMasked` | GREEN 14/0, survived (R-DEF-5) |
| decode loop stopped at 3 passes | full harness | GREEN 208/0 |

## Advisory (over-masking; not a verdict item)

**A-1: userinfo without a password breaks readability.** For userinfo with no `:` (`http://0@h`, `http://ms@h`), the registered form is the bare username, and it is replaced everywhere. With `http://0@h`, `medianMs=5000` became `5<redacted><redacted><redacted>`, and the D-11 REASON line lost its numbers. D-1's claim that "forms carry the `user:` part" is false for this shape. Fix: register only forms that contain `:` or meet a minimum length, or replace on token boundaries.

## Disclosure (out of contract)

- Header and colon text forms in free text (`Authorization: Bearer S`, `password: S`).
- The verbose stream of Invoke-WebRequest under `-Verbose` was not exercised. PS 7.5 prints no URL there, but this was not verified.

## Unlisted runs

- `dotnet build tools/Antiphon.Checkpoints` (`bin-r5d4/`) through build-slot, 21s; the output directories were deleted.
- Two build-slot runs of the mutant harness (34s and 73s).
- Three ad-hoc `pwsh` probe scripts that dot-source the monitor with sentinel values and HttpGet seams. These are not build or test drivers. Two probe JSON files landed in this worktree's gitignored `.antiphon/postgres-cutover/` and were deleted.
