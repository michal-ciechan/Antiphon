# CARD-0351 — `delegate.ps1 -Title` exists; cap it at 80 and stop treating Goal as the header

**Date:** 2026-09-03 (Plan pass, task 2f3bc820 — design only; no production code changed)
**Card:** CARD-0351 "`delegate.ps1` has no `-Title` flag - Title/Goal already exist as separate server fields, the CLI just never sets Title" (InProgress, Normal/Normal, rank 10)
**Refines:** CARD-0350 (check-header dump of `task.Title`). This card owns the *value* of `task.Title`. CARD-0350 still owns how the check header *displays* it (card alias, `#348` shorthand, emoji).

---

## Verdict up front

**Do not add `-Title`. It has been on the script since the first commit of `delegate.ps1` (38fdeae3, 2026-08-07), is already posted as `body['title']`, and is already in the skill options table.** The card's grep was wrong. The gap is that callers never pass it, so `AgentTaskService.BuildTitle` falls back to the Goal's first line, and tonight's Goals are one unbroken paragraph — so the stored title is a 300-char excerpt, which is what check headers, completion notes, attention, and the home rail then dump.

One slice, Shared workspace, ~2 h.

1. Cap an explicit `-Title` at **80 characters**, refuse locally (do not clamp, do not round-trip).
2. Warn (still create) when `-Title` is omitted **and** the Goal's first line is longer than 80.
3. Do **not** tighten the server's 300-char `BuildTitle` clamp.
4. Teach the skill to always pass `-Title` as 2–5 words.

---

## Ground truth (verified this pass)

### The flag is already wired

```21:22:scripts/delegate.ps1
    [Parameter(ParameterSetName = 'Create')]
    [string]$Title,
```

```371:371:scripts/delegate.ps1
        if ($Title) { $body['title'] = $Title }
```

`.claude/skills/antiphon-delegate/SKILL.md:83` already documents it: *"a short label for the board; defaults to the goal's first line"*. The skill examples on lines 20 and 23 still omit it.

`CreateAgentTaskRequest.Title` is `string?` (`server/Application/Dtos/AgentTaskDtos.cs:12`). The TypeScript twin already has `title?: string | null`.

### How the stored title is chosen

```1849:1861:server/Application/Services/AgentTaskService.cs
    private static string BuildTitle(CreateAgentTaskRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Title))
            return Clamp(request.Title.Trim(), 300);

        // Fall back to the goal's first line — a board chip needs something readable.
        var firstLine = request.Goal.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? "Delegated task";
        return Clamp(firstLine, 300);
    }
```

`AgentTask.Title` is `character varying(300)` (`server/Migrations/20260806220756_AddAgentTasks.cs`). Same width as a Card title (`CardService.MaxTitleLength = 300`). Auto-spawned merge tasks prefix the original: `"Resolve merge conflict: {Clamp(conflicted.Title, 250)}"` (`AgentTaskService.cs:1571`).

The fallback is correct. A one-paragraph `-Goal` with no early newline *is* one "first line", so the clamp at 300 is what the user is seeing. This session's own dispatch of this Plan task is the canary: Goal opened with the whole card title plus the mechanism sentence, no `-Title` was sent, and `BuildBriefPointer` then echoed that long `task.Title` as the pointer headline.

### Where `task.Title` surfaces (untruncated unless noted)

| Surface | Truncation today |
|---|---|
| Check header (`AgentTaskCheckService.BuildNote` bit 2) | none (CARD-0350) |
| Completion note (`DelegationReportFormatter.BuildCompletionNote`) | none |
| Brief pointer headline (`BuildBriefPointer`) | none |
| Attention, pipeline status, home tasks, card thread | none |
| Away digest task row | **80** (`AwayDigestFormatter` `Clean(task.Title, 80)`) |

80 is already this repo's chip width for a task title in a human-facing one-liner. That is the number this card picks, not a new aesthetic.

### CARD-0040 still binds from Title

Omitted `-Card` binds from the first `CARD-nnnn` in the title (explicit `-Card` wins; miss is 422). An 80-char cap still fits `CARD-0351 short titles` (25 chars). `-Pin` without `-Card` already inspects `$Title` for that identifier (`delegate.ps1:339`). Keep that check on the trimmed value.

---

## Decisions

### D1. 80-char hard cap on the CLI, refuse, do not clamp

Skill guidance is **2–5 words** (~20–40 chars), matching the user's CARD-0350 example (`Plan #348 - Status Stuck`). The hard ceiling is **80** so `CARD-nnnn` plus a short phrase always fits, and so the CLI matches `AwayDigestFormatter`'s existing 80-char title clean.

Refuse locally, the same shape as `card.ps1`'s `Assert-WithinLimit`: name the field, the actual length, the limit, and where the rest goes. Do not silently ellipsis-truncate — the caller typed a title and would not notice the mangling until a check header appeared.

```
Title is 142 characters; the limit is 80. -Title is a 2-5 word label for check
headers and the board, not a second Goal. Trim it, or put the rest in -Goal.
```

Measure after `.Trim()`. A title containing CR/LF is also a local refusal (*"-Title is a single line"*) — check headers already flatten newlines, but a title is not a paragraph. Whitespace-only is treated as omitted (do not send `title`).

No `-TitleFile`. A title that needs a file is the bug.

### D2. Do not tighten `BuildTitle`'s 300-char clamp

Rejected:

- **Server clamp 80, silent ellipsis.** HTTP / test / merge-prefix titles would be mangled without a 422 the caller could act on. Merge titles are `"Resolve merge conflict: " + Clamp(original, 250)` — already designed against 300.
- **Server 422 at 80.** Breaks any non-CLI caller that still sends a long explicit Title, and does not help the omitted-Title case (that path never sends Title).
- **Tighter fallback clamp (Goal first line → 80).** Stores a mid-sentence excerpt and *looks* like a title. That hides the missing `-Title` instead of teaching it.

300 stays the DB / HTTP backstop, same number as Card titles. Display truncation of a *legacy* long title is CARD-0350's fallback-when-no-alias case, not this card.

### D3. Warn when `-Title` is omitted *and* the Goal's first line is already >80. Do not require `-Title`.

Requiring `-Title` would break every skill example, every `DelegateScriptKindTests` create, and every one-line Goal (`-Goal "run the suite"`) where the fallback *is* the title.

Warning on every omit is the same noise for those short Goals.

Warn only when the fallback would actually be the giant excerpt the user is seeing. Still create. Write it as `Write-Output "WARNING: ..."` so it shows up the same way a server `created.warning` already does (`delegate.ps1:461`), not `Write-Warning` (script tests concatenate stdout+stderr and `$ErrorActionPreference = 'Stop'` is already set).

```
WARNING: no -Title; the goal's first line is 246 characters and will become the
check-header/board title (server clamp 300). Pass -Title with 2-5 words (max 80).
```

First-line extraction in the script must match the server: replace CR, split on LF, skip empty, trim, take first. Do this *before* the POST so a failed create still teaches the habit.

### D4. Skill is the habit fix. Not `orchestrator.md`.

The card's third ask — "this orchestrator session's own dispatch habit needs to change" — is not code. The durable copy is the skill, which is what a dispatching agent actually reads.

- Options row: `-Title` is a 2–5 word label (max 80) for check headers, completion notes, and the board. Omitted, the Goal's first line is stored (clamped 300). Always pass it when `-Goal` is more than one short sentence.
- Both examples at the top of the skill gain `-Title`.
- One Rules bullet: always pass `-Title`; it is not a second Goal.

Do not restate the flag list in `server/Bundles/orchestrator.md` (that file points at `delegate.ps1` and does not enumerate flags). CARD-0040's "lead `-Title` with `CARD-nnnn`" remains valid; `-Card` is still the explicit form.

---

## Slice (one)

**S1 — cap, warn, skill, tests (~2 h)**

1. `scripts/delegate.ps1` (ASCII-only)
   - Comment on the `$Title` param: 2–5 words, max 80, check headers / board chip, not a second Goal.
   - In `Create`, after the Goal-required check: trim; refuse CR/LF; refuse length >80; send the trimmed value (or omit the field).
   - Use the trimmed value in the existing `-Pin` CARD-nnnn regex.
   - When omitted and Goal first line >80, emit the WARNING above, then still POST.
2. `.claude/skills/antiphon-delegate/SKILL.md` — D4.
3. Tests — new `DelegateScriptTitleTests` next to `DelegateScriptKindTests`, same `DelegateScriptRunner` + stub API. `[Category("Integration")]` + `[ParallelLimiter<ProcessSpawnLimit>]` (the class starts pwsh).

No server change. No migration. No client change. No `BuildNote` change.

---

## Test matrix

Drive the **real script** under pwsh against a stub API, the same way `DelegateScriptKindTests` pins `-Kind`. A string match on the source would pass if the cap never ran.

| Case | Expect |
|---|---|
| `-Title "add Fizz"` | 0, body `title` is `add Fizz` |
| omitted `-Title`, Goal `"run the suite"` | 0, **no** `title` property (byte-identical to today — Kind tests stay green) |
| `-Title` of 80 chars | 0, body `title` is those 80 |
| `-Title` of 81 chars | non-zero, output contains `80`, `RequestCount == 0` |
| `-Title "a`n b"` | non-zero, output contains `single line`, no request |
| omitted `-Title`, Goal first line 90 chars | 0, no `title` property, output contains `WARNING` and `90` |
| `-Title "  short  "` | 0, body `title` is `short` (trimmed) |

Do not add `-Title` to existing `DelegateScriptKindTests` cases. The omitted-title contract is the lock that those tests already hold for `-Kind` / `-Complexity`.

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0351/ --treenode-filter "/*/*/DelegateScriptTitleTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0351/ --treenode-filter "/*/*/DelegateScriptKindTests/*"
```

Delete the `bin-card0351` directories afterwards. Do not run the Application namespace.

---

## What this card does not do

- Add `-Title` (it exists).
- Tighten `BuildTitle`'s 300 clamp, or 422 on the HTTP API.
- Truncate the Goal-first-line fallback.
- Rewrite `AgentTaskCheckService.BuildNote` (CARD-0350).
- Card alias / `-Alias` / `#348` / emoji check headers (CARD-0350).
- Haiku / CARD-0330 auto-summarisation of titles.
- `-GoalFile` / `-TitleFile`.
- Making `-Title` mandatory.
- Client create form (already has optional `title`).
- Changing `varchar(300)` or Card title limits.

---

## Execute notes

Shared workspace. ASCII-only in `delegate.ps1`. Do not "fix" Kind-test output if the new WARNING fires — it must not fire on their short Goals; if it does, the first-line extractor is wrong. Do not widen a timeout or loosen `RequestCount == 0` on the 81-char case.
