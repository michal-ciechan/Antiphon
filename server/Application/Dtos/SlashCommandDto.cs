namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// One entry in a session's slash-command autocomplete catalog: a built-in Claude command, a custom
/// command (<c>.claude/commands/*.md</c>), or a skill (<c>.claude/skills/*/SKILL.md</c>), discovered
/// from the user (<c>~/.claude</c>) and project (<c>&lt;cwd&gt;/.claude</c>) scopes.
/// </summary>
/// <param name="Name">The text the user types to invoke it, e.g. <c>/help</c> or <c>/git:commit</c>.</param>
/// <param name="Description">One-line summary (from frontmatter, first body line, or the name).</param>
/// <param name="Source">One of <c>builtin</c> | <c>command</c> | <c>skill</c>.</param>
/// <param name="Scope">One of <c>builtin</c> | <c>user</c> | <c>project</c>.</param>
public sealed record SlashCommandDto(string Name, string Description, string Source, string Scope);
