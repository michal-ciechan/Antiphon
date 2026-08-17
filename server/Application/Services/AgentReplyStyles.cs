using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The map from <see cref="AgentReplyStyle"/> to the bundle that carries it (CARD-0060).
///
/// <para>Two lookups, and the difference between them is the whole of this class:</para>
/// <list type="bullet">
/// <item><see cref="BundleKey"/> is TOTAL — every style, including <see cref="AgentReplyStyle.Normal"/>,
/// names a real bundle file. That is what lets a test iterate the enum and assert that every block
/// ends with the correctness-beats-brevity sentence, including the one nobody composes today.</item>
/// <item><see cref="ComposedKey"/> is what the LAUNCH paths use, and it returns null for
/// <see cref="AgentReplyStyle.Normal"/>. Normal composes to NOTHING, so every agent that existed
/// before the column did launches with byte-identical arguments after the migration. A default that
/// silently changed how every agent writes would be a behaviour change disguised as a schema
/// change.</item>
/// </list>
///
/// <para>If Normal ever needs to say something out loud, <c>style-normal.md</c> is already written
/// and this is a one-line change — which is exactly why the file exists rather than the map having a
/// hole in it.</para>
/// </summary>
public static class AgentReplyStyles
{
    /// <summary>The sentence every style block ends with, whatever else it asks for.</summary>
    public const string CorrectnessSentence =
        "Whatever the style: never drop a caveat, a risk, an uncertainty or a correction to save words.";

    /// <summary>The bundle for this style. Total: every value has one, Normal included.</summary>
    public static string BundleKey(AgentReplyStyle style) =>
        $"{InstructionBundles.StylePrefix}{style.ToString().ToLowerInvariant()}";

    /// <summary>
    /// The bundle this style contributes to a launch, or null when it contributes nothing. Null for
    /// <see cref="AgentReplyStyle.Normal"/>, and that is the property the migration rests on.
    /// </summary>
    public static string? ComposedKey(AgentReplyStyle style) =>
        style == AgentReplyStyle.Normal ? null : BundleKey(style);
}
