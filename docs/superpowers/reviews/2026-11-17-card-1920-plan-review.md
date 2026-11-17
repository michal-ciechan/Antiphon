# CARD-1920 plan review (round 1)

Subject: `docs/superpowers/plans/2026-11-17-card-1920-suppressjit-clustered-switches-plan.md` at `dc12d620a11afdbbe152caa8874982d478c62b6d` (origin/feat/card-task-f4a56d33, Grok author). Master read: `300c8a99d0d267a39791410b7540189ab702f251`. Reviewer: Claude Opus, task 10145984.

Verdict: **CLEAN**. No blockers. Advisories A-1..A-4 below do not hold the land; A-1 should be carried into the Code brief.

## 1. Mechanism

What is rewritten: the Npgsql `Options` value (the startup-packet `options` parameter) on application connection strings. Code: `ApplicationPostgresConnection.SuppressJit` at `server/Infrastructure/Data/ApplicationPostgresConnection.cs:28`, splitter `ScanOptions` at :77-122, `AssignsJit` at :137-156. The plan's line citations match this SHA.

Grammar. PostgreSQL splits `options` with `pg_split_opts` (backslash escapes the next byte, unescaped whitespace separates, no quote handling) and then parses the tokens with `getopt` in `process_postgres_switches`. A "clustered short switch" is a getopt cluster such as `-ecjit=on`: no-argument letters chain, and the first argument letter takes the rest of the cluster or the next token.

The optstring was extracted from the `postgres:16-alpine` binary (the fixture image). Both strings in the plan are exact:

- backend: `B:bC:c:D:d:EeFf:h:ijk:lN:nOPp:r:S:sTt:v:W:-:`
- postmaster: `B:bC:c:D:d:EeFf:h:ijk:lN:OPp:r:S:sTt:W:-:`

Real-server probe. A disposable PostgreSQL 16.15 container (tmpfs data, no volume, removed afterwards) received each string as `PGOPTIONS` through psql. The output is `current_setting('jit')`.

| Options | jit | Meaning |
|---|---|---|
| `-ecjit=on -c application_name=label\` (cited) | on | raw |
| `-c jit=off -ecjit=on -c application_name=label\` | on | today's prefix output; the bug is confirmed |
| `-ecjit=on -c jit=off -c application_name=label\` | off | plan rule 3 output (A) |
| `-ec jit=on -c jit=off …\` / `-ncjit=on -c jit=off …\` / `-nc jit=on -c jit=off …\` / `-encjit=on -c jit=off …\` | off | B, C, D, E after splice |
| `-c jit=on -c application_name=label\` | on | case I today (warn and keep) |
| `-c jit=on -c jit=off -c application_name=label\` | off | case I after splice (D-5) |
| `-c jit=off -dcjit=on …\` | off | F: `-d` consumes `cjit=on` |
| `-c jit=off -Cjit=on …\` | off | `-C` is not `-c` |
| `-c jit=off -vcjit=on` | off | `v:` takes an argument (backend string) |
| `-c jit=off -bcjit=on` | on | `b` is a flag |
| `-c jit=off -e-jit=on` and `-c jit=off -e- jit=on` | on | the `-` letter inside a cluster assigns; the plan's walk covers it |
| `-c jit=off -c JIT=ON` and `-c jit=off --JIT=on` | on | case-insensitive name; `IsJitValue` is ignore-case |
| `-c jit=off -c ji\t=on` | on | escapes decode before matching; `ScanOptions` already decodes |
| `-ecjit=on\`, `-ec jit=on\`, `-c jit=off -ecjit=on\` | on | G, H, rule 2: keeping the original is the only safe result |
| `-zcjit=on …\`, `- …\`, `-c jit=on foo …\`, `-c jit=on -d` | FATAL | unknown letter, lone dash, non-option, missing argument; the server refuses each one raw |

Splice safety. A `ScanOptions` token ends only at an unescaped whitespace byte or at end of string (:93-107). Escaped-at-end therefore happens only on the last token. Inserting `" -c jit=off"` at the raw end index of any non-last token places it on an unescaped separator boundary. The new tokens are two whole getopt elements between two complete units, so no later cluster or separate argument is re-associated. The only way to shift unit boundaries is a wrong optstring, and the plan uses the verified backend string.

Adversarial inputs:

- A quoted `'jit=on'` or `"jit=on"` inside a literal is not a `jit=` token: pg_split_opts has no quoting.
- Two assignments splice after the last one.
- Uppercase is handled.
- A backslash on the assignment token keeps the string and warns (rule 2).
- An unknown letter keeps the string and warns (rule 1). The server FATALs on it anyway.
- Empty Options takes the existing :33 path. Whitespace-only Options takes the no-escape append.
- A very long string costs a linear scan. The 11 added bytes are the same as today's append and prefix.

In every case the result either splices correctly or keeps the original.

## 2. Witnesses and checkpoints

- Existing counts match the plan: `PostgresJitPolicyTests` has 11 `[Test]` methods (Integration), `PostgresJitPolicyTestsSetup` has 9 (Unit), and `TestLaneCategoryGuardTests` has 1 method. The 3 `[Test]` text hits in that file are string literals.
- Helpers `OptionsAfter`, `RawShared`, `WithOptions` and `JitAsync` (`SHOW jit` cross-checked against `current_setting`) exist. `RawShared` sets no ApplicationName, so the `application_name=label` assertion works. The existing `trailing-backslash` boundary case proves it.
- Red at S1. On current code, A-E produce the prefix (jit on, as probed). I warns and keeps (jit on, as probed). G and H are prefixed, so the string-equality assertion fails. CP-1 and CP-2 each select one new method, Min 1, so exit 1 from an assertion is the witness. F, `-Cjit=on` and `-zcjit=on` sit inside the red methods as controls, not as standalone stubs.
- Green after S2. The probe shows that every planned output string gives jit off on a real PG16 server.
- Roster: the card's "7 real-database cases" from review 71200794 is not in this tree (UNOBSERVED, as the plan says). The plan defines 9 integration cases (A-I) and 11 unit cases (A-E, I, F, `-Cjit=on`, G, H, `-zcjit=on`).
- Import: `import --plan … --out .antiphon/c1920-review-import.yaml` (ignored path, build-slot lease 85d6e8cb, load 10.92) exited 0. Result: `imported 5 rows`, 0 import warnings, ids CP-1..CP-5, Min 1/1/12/10/1, and row timeouts 45/15/45/24/15 minutes. CP-1's estimate of 15 maps to 45 minutes, the CARD-1720 floor, with no warning.

## 3. Scope and collisions

- The plan edits only `server/Infrastructure/Data/ApplicationPostgresConnection.cs` and `tests/Antiphon.Tests/Infrastructure/PostgresJitPolicyTests.cs`. It has no migration, no setting change and no host restart.
- I searched every `origin/feat/*` branch with a commit in the last 21 days for either file. The only hits are the pre-rebase CARD-1886 commits carried by stale branches 219d1fd9, 3b08ce53 and 43aa1954. That content already landed on master by rebase, so none of these is a live edit.
- CARD-1890 (server logging) and CARD-1964 (phone-home) share no file with this plan.
- `git merge-tree --write-tree origin/master dc12d620a` gave tree `82342a21f`, exit 0, with no conflict. `check-evidence-diff` over 300c8a99d..dc12d620a reported commits=1, entries=0, violations=0.

## 4. Operator decisions

OD-1 yes, OD-2 yes, OD-3 yes, OD-4 no, OD-5 yes. None should differ. The probes confirm that OD-1 and OD-5 turn jit off and that OD-2 is the only safe result. OD-3 keeps a pre-existing terminator defect unchanged (A-2), which is acceptable for this card.

## Advisories (non-blocking; card or carry)

A-1 (carry into the Code brief):
- Problem: the current guard at :51 (`StartsWithOwnTokens && !AssignsJit(tokens, 2)`) uses the old cluster-blind scan. If Code keeps it in front of the walk, `-c jit=off -ecjit=on -c application_name=label\` returns unchanged with no warning. The probe shows jit on. No planned witness catches this, because none of A-I starts with `-c jit=off`.
- Plan reading: rules 1-4, read as the whole branch, already handle the case. The last assignment is `-ecjit=on`, so the plan splices after it.
- Fix: rules 1-4 replace :51-60 entirely. Add that string to both new methods.

A-2 (card):
- Problem: the terminator path predates this card. An argument letter consumes a following `--` as its value. `-d -- -c jit=on` opens with jit on. Today's output `-d -c jit=off -- -c jit=on` fails with FATAL `invalid command-line argument for server process: jit=off`.
- Status: this is an exotic, pre-existing CARD-1886 behaviour, and D-3 rightly keeps it out of this card. The new walk could fix it later.

A-3 (informational): the test fixture runs `postgres:16-alpine` (musl getopt), while `docker-compose*.yml` runs `postgres:16` (Debian, glibc getopt). glibc permutes non-option tokens and musl does not. The walk treats non-dash tokens as uncertain, so no splice depends on that difference. The witnesses run on musl only.

A-4 (Code detail): a lone `-` token should be classified as uncertain (rule 1). The plan's "`-` plus letters" walk leaves zero letters undefined. The server FATALs on a lone `-` either way.

## Evidence

- Real-server probe: PostgreSQL 16.15 on x86_64-pc-linux-musl, a transient container with no volume, 46 strings tested.
- Optstrings extracted from `/usr/local/bin/postgres` in `postgres:16-alpine`.
- No checkpoint row was executed. CP-1..CP-5 name methods this tree does not contain, so they belong to Code. `reviewedSourceClean` stays false because the import is a build-slot lease, not a checkpoint-tool receipt.
- The guard batch was not run because the subject changes no code or tests.
- Producer/recipient delivery and CARD-1083 input-wait release do not apply. The plan adds no session that waits for input. No OS-selected production path changes.
