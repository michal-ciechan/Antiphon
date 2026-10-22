# CARD-1423 masking repair 2: Final Review (task 9229aca3)

Outcome: clean. R-DEF-1..5 and A-1 from Final Review 5d4be758 are repaired at the subject tip. I found no regression and no new leak of a form inside the plan contract. Subject: Code task abecccc3-aa7b-4aa2-aeeb-6c4d287108e6, `feat/card-task-abecccc3` at `6522543fbfc4f277f94dc193824da9c69dde71ff`. The tested source is cf27b421d; the only change after it is `.antiphon/task-abecccc3.md`, so `scripts/` is byte-identical between the two.

## Receipt (ordinary scope: Full)

- CP-1, the plan's only row (`pwsh -NoProfile -File scripts/test-postgres-cutover-monitor.ps1`, covering V-1..V-7), ran through the checkpoint tool on server2.
  - Run `20261022-055857-f359`, at 6522543fb with `--expected-source-sha`.
  - It exited 0 in one wait (no 75): slot=granted, waited=0s, dirty=0, sourceState=clean, verdict GREEN, `unlisted: none`.
  - The harness console printed `POSTGRES-CUTOVER-MONITOR TESTS passed=298 failed=0`: 298 PASS lines, 0 FAIL, 32 cases. Expect was at least 80.
- `scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261022-055857-f359/report.json -ExpectedSourceSha 6522543fb... -Rows CP-1` printed `CHECKPOINT SOURCE VALID source=6522543fb... rows=1` and exited 0.
- `git merge-tree --write-tree origin/master HEAD` against master `f2b0f1748376a88e6c0ef4ed1bdd4e1959b2d41d` (which contains 294fa3d1) exited 0 with tree `d41dfdb87bb5d8ebf77fe3d07b7800fdfa507feb`, no conflicts.
- `scripts/check-evidence-diff.ps1 -BaseRef 705026da8 (merge-base) -HeadRef HEAD` reported commits=19, entries=3, violations=0. The three entries are the force-added Code reports, which hold placeholders only.
- Platform:
  - `GET /api/runner-defaults` is revision 2, global server2.
  - `GET /api/session-runners` shows a windows runner and a linux runner available. The row pins neither `-Runner` nor `-Platform`.
- Invariants:
  - Both scripts are ASCII-only (0 non-ASCII bytes).
  - GET-only: the only HTTP call is `Invoke-WebRequest -Method Get` (line 517); the other network use is a TcpClient connect (line 586).
  - The param block, the `Plan` default and `-Execute` are untouched by the diff.
  - The added lines hold sentinel values only (`SENT...1423`, `S`, `SE NT`). There is no real secret anywhere.
- The producer/recipient, queue and UserPrompt-transcript audit does not apply: this is a script-only change, with no session, queue or delivery path.

## Prior defects re-run against 6522543fb (independent probe, sentinel values, HttpGet seams)

| Item | Reproduction | Result |
|---|---|---|
| R-DEF-1 | Plan with `-PipelineUrl ...?token=SENRA1 SENRB1` | `?token=<redacted>`, no tail. The fragment `#access%5Ftoken=SENRF1 SENRF2&x=1` gives `#access%5Ftoken=<redacted>&x=1`. |
| R-DEF-1 | Watch with `-TaskListUrl ...?Password=SENRC1 SENRD1`; the seam throws with AbsoluteUri, OriginalString, Query and the decoded Query | Exit 4. UNREADABLE and REASON show `Password=<redacted>` in all four spellings. The result JSON has no sentinel. |
| R-DEF-1 | `Host=h;Password=SE NT;Database=d` | `Host=h;Password=<redacted>;Database=d`. `Pwd='SE N;T'` is masked whole. |
| R-DEF-2 | `{"password":"S"}`, `{"token": "S"}`, `"apiKey":"S"`, `{"Password" : "S"}`, and key/auth/sig/signature/bearer/cookie pairs | All masked in raw and `-JsonText` mode. |
| R-DEF-2 | Baseline with pool diagnostics auth/privateKey/sig | Dropped from the baseline and result JSON. `MaxPoolSize` is kept, so the check is not vacuous. |
| R-DEF-3 | `( , : [ { < / \|` before `Password=` | Masked raw and in JSON, and `Test-CutoverSecretValue` returns True. A pool note `(Password=...)` and `Host=h,Password=...` are `<redacted>` in the result JSON. `access_token` and `my.key` stay one key. |
| R-DEF-4 | `1http://u:S@h`, `_postgres://u:S@h`, `x9postgresql://u:S@h/db` | All `...://<redacted>@h`. |
| R-DEF-5 | Scratch copies, loop bound `-lt 3` / `-lt 5`, through build-slot, then removed | `C1423_DecodeBoundPinnedFromBothSides` is RED 3/2 for each mutant (encoded four times kept / encoded five times masked). Unmutated, it is GREEN 5/0. The old `C1423_FiveTimesEncodedValueMasked` still survives both mutants at 14/0, as expected. |
| A-1 | Baseline+Watch with `-ServerBaseUrl http://0@`, `http://ms@`, `http://1234@fake.invalid`, medians 1000 then 5000 | Exits 0/2. REASON `median 5000 ms ... baseline median 1000 ms (3000 ms)`, ROLLBACK, `CUTOVER-MONITOR ... medianMs=5000 baselineMedianMs=1000` and MEASURED numbers are intact. The JSON redacts only `http://<redacted>@`. |
| Regression | Watch/Baseline with no secrets | No `<redacted>` anywhere in the lines, the baseline JSON or the result JSON. JSON pair masking does not hit the monitor's own fields. |

## Disclosures (out of contract; not findings)

- A JSON object serialized inside a pool-diagnostics string value is written verbatim. `"nest":"{\"password\":\"SENTNEST\"}"` appears as-is in the baseline and result JSON. `Hide-CutoverSecrets` classifies a string only by userinfo or `key=` shape, and the `-JsonText` pair rule does not see the escaped-quote spelling. This falls in the nested-object class.
  - Backlog suggestion: in `Hide-CutoverSecrets`, pass each string value through `Hide-CutoverText` (raw mode) before serialization.
- An A-1 consequence, by design: a userinfo or query form shorter than four characters, or purely numeric, is no longer registered. A bare `u:p` quoted away from its URL is therefore no longer replaced. This is under the four-character bar.
- Over-masking, as reported by Code: `(Password=S)` loses `)`, and a free-text secret pair masks to the end of the line.
- Carried from 5d4be758, still unverified: the `-Verbose` stream of Invoke-WebRequest.

## Unlisted runs (all through build-slot, slot=granted)

- `dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-rev9229/` (held 14s). Both `bin-rev9229` directories were deleted.
- Three probe runs of the harness with an extra global `Test-PROBE_*` case: two had probe-script syntax errors, and the third plus one more were the probe results above. These were reproductions of the 5d4be758 witnesses; no repo file was written.
- Four mutant runs and one unmutated control for R-DEF-5. The scratch copies were removed.
- No `/tmp/c1408-*` roots remain. The tracked tree is clean.
