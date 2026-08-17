namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// One optional instruction bundle attached to one agent (CARD-0058 slice 6).
///
/// <para>This table is the ONLY thing the database is allowed to know about bundles. The CONTENT
/// stays a markdown file in the repo, embedded in the server assembly and versioned by its own hash
/// — an operator-editable content table would reinstate exactly the drift the card exists to remove.
/// What an operator genuinely needs to decide at runtime is WHICH agent carries WHICH bundle, and
/// that is all a row here says: an agent id, a key, and where in the composition it goes.</para>
///
/// <para>It exists because the role map cannot express this. <c>board-api</c> is attached to no role
/// at all, so a delegate working the card API never receives it, and widening the role map would
/// hand it to every delegate of that role instead. An attachment is the narrow answer: this agent,
/// this bundle.</para>
///
/// <para>The key is NOT a foreign key to anything — the catalog is code. A row whose bundle file was
/// renamed or deleted in a later PR therefore names nothing, and is dropped (with a warning) at
/// composition time rather than failing the launch: attachment state is data and outlives the code
/// it points at, and an always-on agent that will not start is a worse outcome than one that starts
/// without an optional block.</para>
/// </summary>
public class AgentBundleAttachment
{
    public Guid AgentId { get; set; }

    /// <summary>
    /// The bundle's catalog key — <c>board-api</c>. Half of the primary key, so "attached twice" is
    /// impossible in the database rather than merely deduped by the composer.
    /// </summary>
    public string BundleKey { get; set; } = string.Empty;

    /// <summary>
    /// Where this bundle sits among the agent's attachments, from the order the operator submitted
    /// them. Composition order is meaningful (earlier blocks are read first, and the agent's own
    /// <c>SystemPromptAppend</c> always keeps the last word), and it has to be STABLE besides: the
    /// drift comparison is a string match over the composed stamps, so an order that varied with the
    /// database's row order would flap the badge on its own.
    /// </summary>
    public int Position { get; set; }

    public DateTime CreatedAt { get; set; }

    public Agent? Agent { get; set; }
}
