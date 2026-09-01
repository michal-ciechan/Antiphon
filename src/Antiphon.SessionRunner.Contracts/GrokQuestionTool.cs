namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// CARD-0241: Grok's in-turn question popup. Measured against incident session
/// <c>53a16758</c> (2026-08-23): the opening <c>tool_call</c> has
/// <c>title = ask_user_question</c> and <c>_meta["x.ai/tool"]</c>
/// <c>name = ask_user_question</c>, <c>kind = ask_user</c>. The completed
/// <c>tool_call_update</c> joins by <c>toolCallId</c>, has empty title / no
/// <c>_meta</c>, and wraps the typed option in
/// <see cref="CompletedAnswerPrefix"/>. Shared by the Grok normalizer (ingest),
/// the confirm loop (match), and <c>-Reply</c> (open-tool gate).
/// </summary>
public static class GrokQuestionTool
{
    public const string AskUserQuestionName = "ask_user_question";
    public const string AskUserKind = "ask_user";
    public const string CompletedAnswerPrefix = "User has answered your questions:";
    public const string XaiToolMetaKey = "x.ai/tool";

    public static bool IsQuestionTool(string? name, string? kind) =>
        string.Equals(name, AskUserQuestionName, StringComparison.Ordinal)
        || string.Equals(kind, AskUserKind, StringComparison.Ordinal);

    public static bool IsQuestionToolName(string? name) =>
        string.Equals(name, AskUserQuestionName, StringComparison.Ordinal);

    /// <summary>
    /// A <c>ToolResult</c> may confirm an overlay answer when it is this question-tool
    /// by name, or when its text is the measured completed-update wrapper (the completed
    /// row itself carries no name). Do not widen to every <c>ToolResult</c> — Claude
    /// already emits those for file/command output.
    /// </summary>
    public static bool IsConfirmingResult(string? toolName, string? text) =>
        IsQuestionToolName(toolName)
        || (text is not null && text.StartsWith(CompletedAnswerPrefix, StringComparison.Ordinal));
}
