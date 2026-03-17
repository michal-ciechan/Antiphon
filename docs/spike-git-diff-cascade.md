# Spike: Git Diff for Cascade Updates (Story 2.13)

## Purpose

De-risk Epic 5 (Course Correction & Cascade Updates) by validating that git's path-filtered diff mechanism provides accurate, parseable, and performant diffs between artifact versions.

## Approach

Used `GitService.GetDiffBetweenTagsAsync` with a path filter parameter that maps to `git diff tag1..tag2 -- path/filter`. Tested against real git repos created in temp directories.

## Key Findings

### 1. Path-filtered diffs work correctly

`git diff {tag1}..{tag2} -- _antiphon/artifacts/workflow-{id}/` accurately filters output to only show changes within the artifact directory. Non-artifact files (source code, configs) are excluded from the diff even when they changed between tags.

### 2. Unified diff format is AI-parseable

The standard unified diff output contains:
- `diff --git a/path b/path` headers identifying each changed file
- `---` / `+++` markers showing old and new file paths
- `@@` hunk headers with line number context
- `-` prefixed removed lines and `+` prefixed added lines
- Context lines (unchanged lines surrounding changes, 3 lines by default)

This format provides sufficient context for an AI agent to understand what changed and update downstream artifacts accordingly.

### 3. Performance is well within bounds

Diff computation for moderately-sized artifacts (200+ sections with code blocks) completes in under 100ms, far below the NFR5 requirement of 5 seconds. Git diff is O(n) in the size of changes, not repo size, so this will scale well for repos under 1GB.

### 4. Multiple file changes are captured

When multiple artifact files change between versions, all changes appear in a single diff output with clear file-level separation. Files that did not change are correctly excluded.

## Recommended Approach for Epic 5

### Cascade Update Flow

1. When a stage artifact is approved/updated, compute diff: `git diff {stage}-v{old}..{stage}-v{new} -- _antiphon/artifacts/`
2. Parse diff to identify which artifact files changed
3. For each downstream stage, check if its artifacts reference/depend on the changed artifacts
4. Present affected stages to the user with cascade options (Update from diff / Regenerate / Keep as-is)
5. For "Update from diff": inject the diff into the AI agent's context when re-executing the downstream stage

### API Design

```csharp
// Already implemented:
Task<string> GetDiffBetweenTagsAsync(string tag1, string tag2, string repoPath, string pathFilter, CancellationToken ct);

// Future additions for Epic 5:
Task<IReadOnlyList<DiffEntry>> ParseDiffAsync(string diffOutput, CancellationToken ct);
Task<IReadOnlyList<AffectedStage>> DetectAffectedStagesAsync(Guid workflowId, IReadOnlyList<DiffEntry> changes, CancellationToken ct);
```

### Context Injection Pattern

The diff output can be directly included in agent system prompts:

```
The following changes were made to the upstream {stage} artifact:

{diff output}

Please update this stage's artifact to reflect these changes.
```

## Risks Mitigated

- **Accuracy**: Path filtering correctly isolates artifact changes from code changes.
- **Parseability**: Standard unified diff format is well-understood by LLMs and can be parsed programmatically.
- **Performance**: Sub-100ms for realistic artifact sizes; NFR5 satisfied with large margin.
- **Multi-file**: Works correctly when multiple artifacts change in a single stage version.
