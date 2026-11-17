# CARD-1920 — SuppressJit misses clustered short switches

Plan only. No production code, setting, migration, or host restart is in this task.

Master read: `300c8a99d0d267a39791410b7540189ab702f251` (`origin/master` and this branch tip at plan time).

Card `c8f5bcb8-658d-4e96-9b7b-2030e750fe67`, In Progress, Low/Normal, platform Any. Not parked, not operator-only, and not already done: `AssignsJit` still only recognizes a whole token `-c`, a token starting with `-c`, or a token starting with `--`. This card does not touch the selector/index chain.

This change does not add or change a session that waits for input. With parking disabled there is no automatic release deadline to specify (CARD-1083). It does not touch transcripts, delivery evidence, or the HTTP/database read-retry registry in `docs/resilience.md`.

`GET /api/runner-defaults` and `GET /api/session-runners` from this runner: UNOBSERVED (connection refused on the local API port). Checkpoints below name the lane. Omit `-Runner` and `-Platform`.

## Problem

`ApplicationPostgresConnection.SuppressJit` (`server/Infrastructure/Data/ApplicationPostgresConnection.cs`, method at line 28) rewrites the Npgsql `Options` value so a later PostgreSQL startup switch sets `jit=off`. The split matches the in-file `pg_split_opts` contract (backslash escapes the next character, an unescaped space separates tokens, a token `--` ends options). `AssignsJit` (line 137) does not parse a short-option cluster.

PostgreSQL 16 parses client startup options in `process_postgres_switches` with this getopt string (`src/backend/tcop/postgres.c` line 3893, REL_16_STABLE):

`B:bC:c:D:d:EeFf:h:ijk:lN:nOPp:r:S:sTt:v:W:-:`

A letter with no following colon is a flag. A letter with a colon consumes the rest of that cluster as its argument, or the next token when the cluster ends on that letter. No-argument letters are `b E e F i j l n O P s T`. Argument letters are `B C c D d f h k N p r S t v W` and `-`. The postmaster string at `postmaster.c` line 691 is `B:bC:c:D:d:EeFf:h:ijk:lN:OPp:r:S:sTt:W:-:` (no `n`, no `v:`). Startup `Options` are parsed by the backend string. `-n` is accepted there and ignored (`postgres.c` case `'n'`). `-e` sets `datestyle=euro`.

Cited string, traced on this SHA. Options `-ecjit=on -c application_name=label\`:

- Tokens: `-ecjit=on`, `-c`, `application_name=label`, `IncompleteEscape` true, no `--`.
- `AssignsJit` is false: `-ecjit=on` does not start with `-c` or `--`, and the separate `-c` is followed by `application_name=label`.
- Lines 49–60 therefore prefix and do not warn.
- Result bytes: `-c jit=off -ecjit=on -c application_name=label\`.
- Backend getopt then sets jit off, sets it on from `-e` plus `-c jit=on`, then sets `application_name`. Effective jit is on.

Same silent loss for a separate argument (`-ec jit=on …\`) and for `-n` (`-ncjit=on`, `-nc jit=on`) and for `-en` (`-encjit=on`), and for an already-recognized `-c jit=on` when the trailing backslash sits on a later token (`-c jit=on -c application_name=label\`): today that last form warns and keeps the original, so jit stays on. A trailing backslash on the assignment token itself (`-c jit=on\`, `-ecjit=on\`) cannot be followed by a real token; keeping the original is required.

Forms with no trailing backslash already append `-c jit=off`, and a `--` terminator already gets the insertion immediately before `--`. Both land after the cluster, so effective jit is already off. Those paths are not the bug.

How often this options shape appears on a real pool: UNOBSERVED. No log or `pg_stat_statements` query was run. The card records the operator's 2026-11-14 disposition: the shape is exotic, ordinary connection strings are unaffected, and today's outcome is jit left on, which is not a regression. The review's "7 real-database cases, 4 passed, 3 failed" and the "525-case differential corpus" are UNOBSERVED in this checkout (review task 71200794 is not in the tree).

## Ground truth

| Card claim | Code at `300c8a99d0d267a39791410b7540189ab702f251` |
|---|---|
| `SuppressJit` is about line 49 and `AssignsJit` about line 137 | Prefix branch is lines 49–60. `AssignsJit` is lines 137–155. `SuppressJit` itself starts at line 28. |
| Split follows `pg_split_opts` | `ScanOptions` (lines 77–122) implements the contract stated in the file comment. This plan does not change split rules. |
| An unescaped `--` gets `-c jit=off` inserted before it | Insert at `TerminatorStart` (lines 43–47), which is the index of the `--` token, so the new token is after earlier switches. |
| A trailing unmatched backslash prefixes when no later token assigns jit | Lines 59–60, and only when `AssignsJit` is false. |
| A later token that assigns jit keeps the original string and warns once | True only for shapes `AssignsJit` recognizes (`-c` plus value, `-c…`, `--…`). A cluster such as `-ecjit=on` is invisible, so the code prefixes and stays silent. |
| `-ecjit=on -c application_name=label\` reports jit on with no warning | Paper trace above. Live `SHOW jit` for that string: UNOBSERVED in this plan task (no database was opened). |
| Attached and separate `-ec` / `-nc` forms, 7 cases, 4 passed, 3 failed | Exact roster UNOBSERVED. The paper failures are the incomplete-escape shapes in the witness table. No-escape attached and separate forms pass today because append/terminator insertion is after the cluster. |

## Decisions

D-1. Place `-c jit=off` after the last parsed jit assignment when a trailing backslash sits on a later token. Reason: that is the smallest edit that makes `SHOW jit` off for the cited string. Rejected: fail-open for every cluster (keeps jit on; allowed by the card only when placement is uncertain). Rejected: special-case the spellings `-ec` and `-nc` (misses `-enc`, a separate argument, and any other no-arg letter).

D-2. Keep the original string and the existing one-shot warning when the unmatched backslash sits on the jit-assignment token, or when the walk cannot classify a short option. Reason: a following token would be glued to the assignment, or a wrong cluster decode would hide a still-on jit. Rejected: rewriting bytes inside the caller's last token.

D-3. Leave the no-escape append path and the `--` insertion path byte-identical. Reason: those paths already land after every switch, and the existing tests lock the bytes. The new walk runs only in the incomplete-escape branch.

D-4. Use the backend REL_16 optstring, including `n` and `v:`. Reason: that is the parser for startup `Options`. Rejected: the postmaster string (no `n`, so `-ncjit=on` would be treated as unknown and left unchanged).

D-5. One placement rule for recognized and unrecognized assignments. `-c jit=on -c application_name=label\` splices the same way as `-ecjit=on …\`. Reason: the backslash is not on the assignment token, so a later `-c jit=off` is a real switch. Rejected: preserving warn-and-keep for assignments `AssignsJit` already sees (leaves `SHOW jit` on for a form the walk understands).

D-6. No migration, no config key change, no restart in the Code task. `DisableJit` stays default true. Hangfire and the maintenance string stay off this helper.

Defaults above are what Code implements. OD-1 through OD-4 at the end are the same choices as yes/no.

## Design

Add a private walk over the option tokens `ScanOptions` already produced, using the backend optstring as a constant. Do not call getopt and do not shell out.

For each token before `--`:

- `--name=value` (the existing long form) is a jit assignment when the text after `--` starts with `jit=` under ordinal ignore-case, matching `IsJitValue`.
- A token of `-` plus letters walks each letter. A no-arg letter continues the cluster. An argument letter takes the rest of the cluster, or the next token when nothing remains in the cluster. `c` and the long-option letter `-` are jit assignments when that argument starts with `jit=` under ordinal ignore-case. `C`, `d`, `f`, `v`, and the other argument letters are not jit assignments: `-dcjit=on` is `-d` with argument `cjit=on`, and `-Cjit=on` is `-C` with argument `jit=on`.
- An unknown letter, a missing argument, or a token that does not start with `-` makes the walk uncertain.

Incomplete-escape branch (the only branch that changes):

1. Uncertain walk: `WarnUnapplied()`, return the original string. Do not prefix.
2. The last jit assignment includes the incomplete last token: `WarnUnapplied()`, return the original string. Covers `-c jit=on\` and `-ecjit=on\`.
3. A jit assignment ends on an earlier token: if that assignment is already the two tokens `-c` and `jit=off` (ordinal, the same pair `StartsWithOwnTokens` uses), return the original string. Otherwise insert a space plus `JitOffOption` at the end index of the last assignment token, before the following separator. `-ecjit=on -c application_name=label\` becomes `-ecjit=on -c jit=off -c application_name=label\`. `-ec jit=on -c application_name=label\` becomes `-ec jit=on -c jit=off -c application_name=label\`.
4. No jit assignment: keep today's prefix (`-dcjit=on -c application_name=label\` still starts with `-c jit=off `).

`ScanOptions` gains end indexes so the insert is between tokens. Split rules stay. Second call hits rule 3 and returns the spliced string unchanged. `WarnUnapplied` stays once per process and keeps its current sentence. Tests must not require the log line: the gate is process-global and order-dependent. Failure text follows the existing tests (case label, jit value, `application_name` length). No connection string and no password in an assertion message.

Update the class comment so it matches rules 1–4. `ApplyPolicy`, `JitOffOption`, Hangfire, `PostgresSettings`, and `Program.cs` stay as they are.

## What must not change

- No-escape append, including idempotence and a single trailing `JitOffOption`, for the strings the unit tests already pass through `SuppressJit`.
- Insertion immediately before a `--` token.
- Escaped trailing space `-c application_name=label\ ` still starts with that text and ends with a separate `JitOffOption`.
- `-c application_name=label\` still prefixes, because it assigns no jit.
- `-c jit=on\` stays byte-identical.
- `disableJit: false`, null, shipped `appsettings.json`, and the `PostgresSettings` default.
- `Program.cs` still calls `ApplyPolicy` before `UseNpgsql`. `HangfireConfiguration.cs` still has no reference to the helper. `TestDbFixtureLifecycle` still derives maintenance from the raw string before suppressing the shared string.
- `scripts/postgres-jit-window.ps1` still reads statement time and jit columns and never resets `pg_stat_statements`.
- Unrelated Npgsql fields on the builder (host, port, database, user, password, pooling, timeout, application name, error detail).
- Selector/index files, phone-home files, and `AgentSessionRuntime.cs`. See the collision list.

## Slices

S1. Red witnesses only, in `tests/Antiphon.Tests/Infrastructure/PostgresJitPolicyTests.cs`. Production file untouched. Commit before any checkpoint run.

S2. The walk and the incomplete-escape placement in `server/Infrastructure/Data/ApplicationPostgresConnection.cs`, plus the comment. Commit. No other production file.

## Existing tests affected

No migration, public DTO, route, operation label, remote script, or statement-budget kind. Those CARD-1782 categories are none.

Editing `tests/Antiphon.Tests/**/*.cs` selects impacted-classes row `test-lane-category` (`TestLaneCategoryGuardTests`). CP-5 covers it. `server/Infrastructure/Data/ApplicationPostgresConnection.cs` matches no glob in `tests/impacted-classes.json` (the Data glob is `**/*Model.cs` only).

`PostgresJitPolicyTestsComposition` is outside the closed list. It locks `DisableJit` composition, which this diff does not change. If Code edits `Program.cs` or `PostgresSettings`, stop and amend the plan before building.

## Performance budget

Budgets are provisional. A measured p95 for these classes is UNOBSERVED in this checkout. The importer deadline `max(15, 3 * EstimatedMinutes)` is the CARD-1720 floor, not a measured 2x p95. Estimate 15 yields a 45-minute row deadline and no advisory warning; estimate 16 warns. Do not widen a timeout. Do not add `totalTimeoutMinutes`.

`TestDbFixture` already builds one Postgres 16 container per assembly run. New cases use `Pooling = false` and the existing `RawShared` / `WithOptions` helpers. No per-test container, migration, or seed. CP-1 is one new method and does not run the 10-million-row `EXPLAIN ANALYZE`.

Record build, slot wait, fixture start, body, teardown, and remainder. Unknown phases stay unknown. `ANTIPHON_LINUX_COMPILER_POLICY=0` on every `dotnet` command (policy 2.2x). Checkpoint rows use the tool's named `bin-` outputs. Any other build uses the worktree's native output through `scripts/build-slot.ps1`, one build at a time.

If an observed row exceeds 30 minutes, stop and file a Debug task with the selection, SHA, load, and run identity (R1). Do not retry it as the next step. No full `Antiphon.Tests` assembly and no Unit lane.

Load at plan import: recorded in the plan commit message's companion import output, not baked in here. The plan-time 1-minute load was under 16 (`15.34` on the first sample; re-read immediately before the import).

## Verification design

Class segments, confirmed in this tree: `PostgresJitPolicyTests` (Integration, 11 existing `[Test]` methods), `PostgresJitPolicyTestsSetup` (Unit, 9 existing `[Test]` methods), `PostgresJitPolicyTestsComposition` (separate class; not selected). Filters use the full class segment so `PostgresJitPolicyTests` does not match `PostgresJitPolicyTestsSetup`.

New integration method `clustered_short_switches_report_jit_off` on `PostgresJitPolicyTests`. One method, so Min on the red row is 1. It opens the shared test database through the existing maintenance string with `Pooling = false`. Each case calls `SuppressJit` twice and requires the same connection string. `SHOW jit` and `current_setting('jit')` go through the existing `JitAsync` helper.

| Case | Options | Required after S2 | Why red at S1 |
|---|---|---|---|
| A | `-ecjit=on -c application_name=label\` | jit `off`, `application_name` `label` | Cited string. Prefix loses. `SHOW jit` is on. |
| B | `-ec jit=on -c application_name=label\` | jit `off`, name `label` | Separate `-ec` argument. Same prefix loss. |
| C | `-ncjit=on -c application_name=label\` | jit `off`, name `label` | Attached `-n`. |
| D | `-nc jit=on -c application_name=label\` | jit `off`, name `label` | Separate `-n`. |
| E | `-encjit=on -c application_name=label\` | jit `off`, name `label` | Two no-arg letters. A one-letter special case stays red. |
| I | `-c jit=on -c application_name=label\` | jit `off`, name `label` | Recognized assignment, backslash on a later token. Today warns and keeps jit on. |
| F | `-dcjit=on -c application_name=label\` | jit `off`, options still contain `-dcjit=on` | Control. Green at S1 (prefix already wins; `-d` consumes `cjit=on`). |
| G | `-ecjit=on\` | full connection string unchanged; jit equals the unsuppressed connection | Backslash on the assignment. String equality is red at S1 because today the string is prefixed. |
| H | `-ec jit=on\` | full connection string unchanged; jit equals the raw connection | Same, separate argument. |

A through E, I, G, and H fail at S1 on current code. F is inside the same method so a walk that treats `-d` as a jit cluster fails after the fix. A witness that is true at S1 for every assertion is a stub; F alone would be a stub, which is why it is not its own test.

New unit method `suppress_places_jit_off_after_a_clustered_switch` on `PostgresJitPolicyTestsSetup`. Assert through `OptionsAfter` / full-string equality, the same helpers the class uses:

- A–E and I: options contain the cluster (or `-c jit=on`) followed by ` -c jit=off`, and do not start with `-c jit=off `. Red at S1.
- F and `-Cjit=on -c application_name=label\`: still start with `-c jit=off ` and still contain the original argument token. Controls, true at S1, kept in this method so a case-fold of `C` to `c` fails the method after the fix.
- G, H, and `-zcjit=on -c application_name=label\`: full string unchanged. Red at S1 (today they are prefixed). `-z` is absent from the optstring, so the walk is uncertain.

No-escape `-ecjit=on` and `-ecjit=on --` stay covered by the existing class runs in CP-3 and CP-4. Do not retune them. They are green at S1 and are not the red witness.

Two slice-group invocations. Both use `--serial` and neither passes `--row-timeout`. The tool takes the build slot.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card-1920-cp -- dotnet run --project tools/Antiphon.Checkpoints --no-launch-profile -- run --plan docs/superpowers/plans/2026-11-17-card-1920-suppressjit-clustered-switches-plan.md --rows CP-1,CP-2 --serial
```

After S1 that command's exit 1 is the witness: the two new methods failed, and no other method in those exact filters ran. Then S2, then the same command with `--rows CP-3,CP-4,CP-5`. Exit 0, the counts below, fresh TRX per row. A red CP-3/CP-4/CP-5 is a failed fix, not a cue to loosen an assertion.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1920-red/` | clustered-red | `/*/*/PostgresJitPolicyTests/clustered_short_switches_report_jit_off` | V-1 | new method assertion-red; row exit 1 is the witness | 1 | 15 |
| CP-2 | S1 | `CP-1` | unit-red | `/*/*/PostgresJitPolicyTestsSetup/suppress_places_jit_off_after_a_clustered_switch` | V-2 | new method assertion-red; cited options still prefixed | 1 | 5 |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1920/` | jit-integration | `/*/*/PostgresJitPolicyTests/*` | V-1, R-1 | all 12 methods, 0 failed | 12 | 15 |
| CP-4 | S2 | `CP-3` | jit-unit | `/*/*/PostgresJitPolicyTestsSetup/*` | V-2, R-2 | all 10 methods, 0 failed | 10 | 8 |
| CP-5 | S2 | `CP-3` | lane-guard | `/*/*/TestLaneCategoryGuardTests/*` | R-3 | the one guard method, 0 failed | 1 | 5 |

V-1 is cases A–I and G–H on a real connection. V-2 is the unit placement and the uncertain-letter lock. R-1 is the existing 11 integration methods. R-2 is the existing 9 unit methods. R-3 is `test-lane-category`.

## Files, collisions, rollout

Edit only:

- `tests/Antiphon.Tests/Infrastructure/PostgresJitPolicyTests.cs` (S1 and any assertion fix that belongs to the witness)
- `server/Infrastructure/Data/ApplicationPostgresConnection.cs` (S2)

Must not touch:

- `server/Program.cs`, `server/Application/Settings/PostgresSettings.cs`, `server/appsettings.json`
- `server/Infrastructure/Agents/HangfireConfiguration.cs`
- `tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs`
- `scripts/postgres-jit-window.ps1`
- `server/Infrastructure/Agents/AgentSessionRuntime.cs` (CARD-1888)
- Phone-home files owned by CARD-1960 and CARD-1964. Their current diffs versus this helper: UNOBSERVED. The planned diff does not include them.
- Selector/index chain: `scripts/impacted-classes.ps1`, `scripts/lib/release-impact*`, `tests/impacted-classes.json`, `tests/derived-impact-index.json`. The brief names CARD-1820 as the holder. The triage note at this SHA also names CARD-1958 as the current editor of the derived index. This card does not enter that chain.
- `docs/docker-stack.md`, `scripts/deploy-server2.ps1`

No migration. A migration is a human-only gate; none is proposed, so that gate is not opened. No AppHost, desktop, or server2 restart in Code. The running process keeps the connection string it built at startup until a later publication restart. A master land is not activation. No feature flag. `Postgres:DisableJit` stays true in shipped settings.

## Operator decisions

OD-1. Splice `-c jit=off` after a clustered jit assignment when the trailing backslash is on a later token, so `SHOW jit` is off? Recommendation: yes (D-1).

OD-2. Keep the original string and the one-shot warning when the backslash is on the assignment token or the short option is unknown? Recommendation: yes (D-2).

OD-3. Leave no-escape append and `--` insertion byte-identical? Recommendation: yes (D-3).

OD-4. Restart a host or add a migration in the Code task? Recommendation: no (D-6).

OD-5. Use that same splice for an already-recognized `-c jit=on` when the backslash is on a later token? Recommendation: yes (D-5).

## Review and merge

Plan review reads this file against `ApplicationPostgresConnection.cs` and `PostgresJitPolicyTests.cs` at the recorded master SHA. Code then runs the two slice groups above. Ordinary Review is at most three rounds; rounds 2 and 3 are the diff from the previous reviewed SHA. Land with `git fetch origin master` and `git merge-tree --write-tree origin/master HEAD`, reported, not merged to resolve. No `-AllowWorktree`. Publication is a later rc activation, not this plan.
