namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// How an agent writes (CARD-0060). One choice on a scale, resolved at launch into one instruction
/// block appended after the agent's bundles and before its own <c>SystemPromptAppend</c>.
///
/// <para><see cref="Normal"/> is 0 deliberately, and it is the migration default: every agent that
/// existed before this column composes to exactly the launch arguments it composed to yesterday,
/// byte for byte. Style is a thing an operator opts INTO, never something a migration does to a
/// working agent.</para>
/// </summary>
public enum AgentReplyStyle
{
    /// <summary>No style instruction at all. Composes to NOTHING — see <c>AgentReplyStyles</c>.</summary>
    Normal = 0,

    /// <summary>Answer first, as few words as carry it.</summary>
    Terse = 1,

    /// <summary>Caveman. Short word. Drop small word.</summary>
    Caveman = 2,

    /// <summary>Show the reasoning, name the alternatives, say what would change the answer.</summary>
    Explanatory = 3,

    /// <summary>Short bullets. Minimum words. Only what changes a decision.</summary>
    Brief = 4,
}
