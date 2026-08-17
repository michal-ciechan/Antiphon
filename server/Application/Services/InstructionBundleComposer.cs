using System.Text;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The result of one composition: the text to hand to <c>--append-system-prompt</c>, and which
/// bundles (with versions) went into it — so a caller can log it, stamp it on a session row, or show
/// it in the UI without re-deriving the composition.
/// </summary>
public sealed record ComposedInstructions(string Text, IReadOnlyList<InstructionBundle> Bundles)
{
    public static ComposedInstructions Empty { get; } = new(string.Empty, []);

    /// <summary>Nothing to append: no bundles, no style, no agent contract. The flag is then omitted entirely.</summary>
    public bool IsEmpty => Text.Length == 0;

    /// <summary><c>["delegate-basics v3f9a1b2c", …]</c> — for a log line, a DTO or a drift check.</summary>
    public IReadOnlyList<string> Stamps => [.. Bundles.Select(b => b.Stamp)];

    /// <summary>
    /// The whole composition on one line — <c>"orchestrator v1a2b3c4d, delegate-basics v3f9a1b2c"</c>
    /// — which is what a session row stores at launch and what the drift check compares (CARD-0058
    /// slice 6). Empty for a composition that carried no bundles, and that empty string is a real
    /// answer: it says this launch carried nothing, which a later attachment then contradicts.
    ///
    /// <para>It rides the CONTENT HASHES already in each stamp. There is no second version to bump,
    /// which is the entire reason the comparison can be a string match.</para>
    /// </summary>
    public string StampLine => string.Join(", ", Stamps);
}

/// <summary>
/// Composes an agent's standing instructions from the bundle catalog (CARD-0058). ONE pure function,
/// used by every launch path, so a changed bundle reaches every agent at its next launch and there is
/// no stored composed text anywhere that could drift from the repo.
///
/// <para>Order is the whole design, and it is fixed: attached/role bundles in declared order, then
/// the reply-style block (CARD-0060), then <c>Agent.SystemPromptAppend</c> LAST. The agent's own
/// free-text contract goes last because it is the most specific thing anybody said about this one
/// agent — composing it earlier would let a general bundle have the final word over an operator who
/// wrote instructions for this agent by hand.</para>
///
/// <para>Pure and static: no database, no clock, no IO past the embedded catalog. Every rule here is
/// worth pinning, and a composition that needed a service could not be exercised by the launch tests
/// that matter.</para>
/// </summary>
public static class InstructionBundleComposer
{
    /// <summary>Between blocks. A blank line, because these are separate instruction blocks and a model reads them as such.</summary>
    public const string BlockSeparator = "\n\n";

    /// <summary>
    /// Compose the append for one launch.
    /// </summary>
    /// <param name="bundleKeys">
    /// Role defaults and/or per-agent attachments, in the order they should appear. DEDUPED BY KEY,
    /// first occurrence wins: a bundle reachable both as a role default and as an explicit attachment
    /// must appear once, or the agent pays for it twice and reads it twice.
    /// </param>
    /// <param name="styleBundleKey">
    /// The reply-style block (CARD-0060), resolved from the agent's style enum. Null — which is what
    /// the <c>Normal</c> style resolves to — composes nothing at all, so an existing agent's launch
    /// args stay byte-identical after that migration.
    /// </param>
    /// <param name="systemPromptAppend">
    /// The agent's own contract, verbatim and unheadered. Passed through untouched (not trimmed, not
    /// LF-normalised) so that with no bundles and no style this method is a byte-for-byte no-op — the
    /// property that lets the standing-agent launch path adopt it without changing any behaviour.
    /// Whitespace-only counts as absent, matching the existing <c>AgentControlService</c> gate.
    /// </param>
    public static ComposedInstructions Compose(
        IEnumerable<string>? bundleKeys = null,
        string? styleBundleKey = null,
        string? systemPromptAppend = null)
    {
        var keys = new List<string>();
        if (bundleKeys is not null)
            keys.AddRange(bundleKeys);
        if (styleBundleKey is not null)
            keys.Add(styleBundleKey);

        var bundles = new List<InstructionBundle>(keys.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (!seen.Add(key))
                continue;
            bundles.Add(InstructionBundles.Get(key));
        }

        var text = new StringBuilder();
        foreach (var bundle in bundles)
        {
            if (text.Length > 0)
                text.Append(BlockSeparator);
            text.Append(bundle.Render());
        }

        if (!string.IsNullOrWhiteSpace(systemPromptAppend))
        {
            if (text.Length > 0)
                text.Append(BlockSeparator);
            text.Append(systemPromptAppend);
        }

        return text.Length == 0
            ? ComposedInstructions.Empty
            : new ComposedInstructions(text.ToString(), bundles);
    }

    /// <summary>
    /// Is a session running with instructions the repo has since moved on from (CARD-0058 slice 6)?
    ///
    /// <para>A pure string comparison of the launch's stamp line against a composition recomputed
    /// now. Because every stamp carries the bundle's own content hash, an edited bundle file, an
    /// attachment added or removed, and a changed reply style all show up the same way, and none of
    /// them needs a version anybody remembers to bump.</para>
    ///
    /// <para><paramref name="launchedStamp"/> null means NO EVIDENCE — a session from before the
    /// column existed, or one launched by a path that composes nothing — and no evidence never
    /// raises the badge. Only a recorded stamp that disagrees does.</para>
    ///
    /// <para>Answering true is the whole job. Nothing acts on it: a running session keeps the
    /// bundles it was launched with until its next launch, and the badge exists to make that
    /// bounded staleness visible, not to type instructions into a live session.</para>
    /// </summary>
    public static bool IsOutOfDate(string? launchedStamp, ComposedInstructions current) =>
        launchedStamp is not null
        && !string.Equals(launchedStamp, current.StampLine, StringComparison.Ordinal);

    /// <summary>
    /// The command-line guard. THROWS RATHER THAN TRUNCATES: a truncated system prompt is an agent
    /// running under half a contract with nothing to show that it is, which is strictly worse than a
    /// launch that fails loudly and names what to shrink.
    ///
    /// <para>Counted in UTF-16 CHARS, deliberately — unlike every pty ceiling in this codebase, which
    /// counts UTF-8 bytes because the receiving TUI's read quantum is measured in bytes. This is not
    /// the pty: the append is an argument to <c>CreateProcessW</c>, whose ~32 767 limit is a count of
    /// UTF-16 characters, so an em-dash costs one here and three there.</para>
    ///
    /// <para><paramref name="otherArgs"/> is everything else this launch will put on the command line
    /// that the caller knows about. The budget sits below the OS limit to leave room for what the
    /// caller does NOT know about — the resolved executable path, the definition's own base args, the
    /// <c>--session-id</c>/<c>--resume</c> the runner adds — and each argument is charged three extra
    /// characters for the space and the pair of quotes that wrap it. Pessimistic on purpose; the
    /// measured worst-case composition uses a small fraction of the budget, so the guard only ever
    /// fires on something genuinely runaway.</para>
    /// </summary>
    /// <param name="subject">Named in the exception so a failed launch says WHOSE it was.</param>
    /// <exception cref="InvalidOperationException">The composition does not fit.</exception>
    public static void EnsureWithinCommandLineBudget(
        ComposedInstructions composed,
        IReadOnlyCollection<string> otherArgs,
        int budgetChars,
        string subject)
    {
        const int perArgumentOverhead = 3; // one space, two quotes

        var otherChars = otherArgs.Sum(a => a.Length + perArgumentOverhead);
        var appendChars = composed.IsEmpty
            ? 0
            : composed.Text.Length + "--append-system-prompt".Length + (2 * perArgumentOverhead);
        var total = otherChars + appendChars;
        if (total <= budgetChars)
            return;

        var breakdown = composed.Bundles.Count == 0
            ? "no bundles"
            : string.Join(", ", composed.Bundles.Select(b => $"{b.Stamp} {b.Text.Length:N0} chars"));
        throw new InvalidOperationException(
            $"{subject}: composed instructions do not fit the command line — {total:N0} chars against a "
            + $"budget of {budgetChars:N0} ({appendChars:N0} for --append-system-prompt, {otherChars:N0} for "
            + $"the other {otherArgs.Count} args). Bundles: {breakdown}. Nothing was truncated: shrink a "
            + "bundle under server/Bundles/, or shorten the agent's own system prompt append.");
    }
}
