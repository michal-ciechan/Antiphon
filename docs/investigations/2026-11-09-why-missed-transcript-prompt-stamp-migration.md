# Why missed: Card1773TranscriptPromptStampMigrationTests hard-coded a Linux newline

This note covers rc run 231f573c (rc/2026-11-13 = `653f1145d`), chunk `antiphon-all-group-96`. One
test failed on Windows:
`Card1773TranscriptPromptStampMigrationTests.Up_renames_an_invalid_index_drops_it_and_creates_if_missing`.
The Debug task is e43d428d, from master `f7fc1573ee03114992c48ae266fdcf34584bb943`. The Windows log
extract is `docs/investigations/2026-11-09-run-231f573c-transcript-prompt-stamp-migration-windows-failure.md`,
on `origin/feat/card-task-5522b4af`. CARD-1781.

## Failure

```
ShouldAssertException: commands[1].CommandText should be
"DROP INDEX CONCURRENTLY IF EXISTS "IX_TranscriptEntries_AgentSessionId_Kind_Sequence_invalid";\n"
but was "...;\r\n"   (codes 59 10 expected, 59 13 10 actual; line 41)
```

The failure is deterministic and happens only on Windows. It does not come from Docker, Postgres,
ports, timing or migration ordering. Both real-Postgres retry tests in the class passed on Windows
in the same chunk, so the recovery SQL works there.

`Generate(SqlOperation)` passes the SQL to `MigrationCommandListBuilder.AppendLine(string)`. That
calls `IndentedStringBuilder.AppendLine(string)`, then `StringBuilder.AppendLine(string)`, which
writes `Environment.NewLine`. This chain was read from the EF Core 9.0.21 Relational IL on server2.
`NpgsqlMigrationsSqlGenerator` does not override `Generate(SqlOperation)`. The test expected a
literal `"\n"` on lines 41, 48 and 49. Lines 48 and 49 never ran on Windows, but they would have
failed the same way.

Fix: `efe450127d57be1c9701f387688ad297e30b0b9e` on `feat/card-task-e43d428d` (test-only). The three
exact assertions now expect `{Environment.NewLine}`. They stay exact on each platform; this is not
`ReplaceLineEndings`.

On Linux server2, filter `/*/*/Card1773TranscriptPromptStampMigrationTests/*`:

| Run | Result |
|---|---|
| master `f7fc1573e` | 3/3 |
| fix `efe450127` | 3/3 |

Linux cannot show the red, because there `Environment.NewLine` is `"\n"`. The red witness is the
Windows run 231f573c. The test bytes at `653f1145d` and at master `f7fc1573e` are identical.

## Introducing commit/card

introducing commit/card: 57bbfc99d1810c99f2bf6e1a928427918c05d1fd (CARD-1773)

That commit is the master pick of `938bd7c13c0b0a71d6e83a5ea66b2a2560510c1b`, from Code task
b009716e. It made a Linux red green. Before it, the test expected no trailing newline, and the Code
task's own run at `2c11fc427` was 2 passed, 1 failed. It fixed the expectation to the byte that
Linux produces, `\n`. The assertion it replaced came in with `34ede7c9e`, the same
card's retry witness.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- 3e56dbb0, round 1, Final, NOT CLEAN on B-1 (the migration retry). This review came before the
  newline commit, so it is not the approving review for these bytes.
- eb0f53d8, round 2, Final, diff only, CLEAN. Its subject was `aa07b33d5`, which contains
  `938bd7c13`. It ran on server2 (Linux):
  - CP-1 `/*/*/(Card1773TranscriptPromptStampMigrationTests*)|(TranscriptHotPathMigrationTests*)/*`: 14/14
  - CP-2 `(C0824RefOnlySnapshotMigrationTests*)|(C1514HostLoadMigrationTests*)`: 12/12
  - CP-3, the guard batch: 50/50
- The Merge batch final-tree run was also on Linux. The CARD-1782/1801 `migrations` kind derives
  this class: it contains `IMigrator` and `MigrationTestDatabase`, and the diff touched
  `server/Migrations/**`. So any selection that ran it ran it on Linux.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration/Slow lane,
`Integration` + `Slow`, `tests/Antiphon.Tests/Application/Card1773TranscriptPromptStampMigrationTests.cs`

The failing class was not outside the selection by class: every stage selected it and saw it green.
The platform was the gap. Code, both Reviews and the Merge batch ran only on server2 Linux. The
`migrations` kind has no `"windows": "required"`, so no report listed the class as owed on the
native Windows lane. The first Windows execution was the rc qualification. Neither review asked
where the expected `\n` came from. The Code report even described it as "the newline
`IMigrationsSqlGenerator.Generate` appends via `AppendLine`", a platform-dependent call.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`server/Migrations/*Card1773TranscriptPromptStampIndex*.cs`, `server/Migrations/*AddTranscriptHotPathIndexes.cs`, `tests/Antiphon.Tests/Application/Card1773TranscriptPromptStampMigrationTests.cs` -> `/*/*/(Card1773TranscriptPromptStampMigrationTests*)|(TranscriptHotPathMigrationTests*)/*` with `"windows": "required"`

Proposed CARD-1801 row (not applied; `tests/impacted-classes.json` is unchanged):

```json
{
  "id": "migration-generated-sql-text",
  "globs": [
    "server/Migrations/*Card1773TranscriptPromptStampIndex*.cs",
    "server/Migrations/*AddTranscriptHotPathIndexes.cs",
    "tests/Antiphon.Tests/Application/Card1773TranscriptPromptStampMigrationTests.cs",
    "tests/Antiphon.Tests/Application/TranscriptHotPathMigrationTests.cs"
  ],
  "classes": [ "Card1773TranscriptPromptStampMigrationTests", "TranscriptHotPathMigrationTests" ],
  "windows": "required"
}
```

`Up_renames_...` is metadata only. It took 3.9 s on the loaded Windows host and needs no Postgres,
so a method-scoped Windows row costs seconds:
`/*/*/Card1773TranscriptPromptStampMigrationTests/Up_renames_an_invalid_index_drops_it_and_creates_if_missing`.
The `windows` flag only lists the row as owed. It still needs a native Windows run before land to
stop this class of red.

The cheaper general check runs on Linux. Add a guard over the `added` diff lines (`tests/**`) that
refuses an exact `ShouldBe(...)` on `CommandText` or other `StringBuilder.AppendLine` output whose
expected literal ends in `\n"` or `\r\n"`. It points the author to `Environment.NewLine`. This needs
a card for the guard class; this Debug task does not add it.

## Exemption

same cause as an already written note: none
