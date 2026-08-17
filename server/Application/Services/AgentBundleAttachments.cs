using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Reads and writes per-agent bundle attachments (CARD-0058 slice 6): the DATA half of the
/// mechanism, sitting between the code-level role map and the repo-level bundle files.
///
/// <para>Static helpers over the DbContext rather than a service, for the same reason
/// <see cref="InstructionBundles"/> is static: every launch path already has a DbContext, and a
/// registration to forget would mean an agent silently launching without its attachments — the one
/// failure this whole card is about.</para>
///
/// <para>Reads deliberately do NOT go through <c>Agent.BundleAttachments</c>. An agent loaded
/// without that include has an empty collection, which is indistinguishable from an agent with no
/// attachments, so a launch path that forgot the include would drop a contract without a sound.
/// Every read here is an explicit query.</para>
/// </summary>
public static class AgentBundleAttachments
{
    /// <summary>
    /// Attached bundle keys for several agents at once, in composition order, with keys the catalog
    /// no longer knows dropped and logged.
    ///
    /// <para>Dropping is the deliberate choice for a stale key. A PR that renames or deletes a
    /// bundle file leaves rows naming it, and <see cref="InstructionBundles.Get"/> throws — which on
    /// this path would mean an always-on agent that cannot start at all because one optional block
    /// was renamed. It is not silent: the drop is a Warning naming the agent and the key, and the
    /// composition the settings modal shows stops listing it.</para>
    /// </summary>
    public static async Task<Dictionary<Guid, IReadOnlyList<string>>> LoadAsync(
        AppDbContext db,
        IReadOnlyCollection<Guid> agentIds,
        ILogger? logger,
        CancellationToken ct)
    {
        if (agentIds.Count == 0)
            return [];

        var rows = await db.AgentBundleAttachments
            .AsNoTracking()
            .Where(a => agentIds.Contains(a.AgentId))
            // ThenBy(BundleKey) is the tiebreak that makes the order TOTAL. Position alone would
            // leave two attachments sharing a position ordered by whatever the database felt like,
            // and the drift stamp is an ordered string — it would flap between requests.
            .OrderBy(a => a.Position).ThenBy(a => a.BundleKey)
            .Select(a => new { a.AgentId, a.BundleKey })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, IReadOnlyList<string>>();
        foreach (var group in rows.GroupBy(r => r.AgentId))
        {
            var keys = new List<string>();
            foreach (var row in group)
            {
                if (InstructionBundles.Exists(row.BundleKey))
                {
                    keys.Add(row.BundleKey);
                    continue;
                }

                logger?.LogWarning(
                    "Agent {AgentId} is attached to instruction bundle '{BundleKey}', which is no longer in "
                    + "the catalog (known: {Known}). It is left out of the composition — the launch is not "
                    + "failed for it. Detach it, or restore server/Bundles/{BundleKey}.md.",
                    group.Key, row.BundleKey, string.Join(", ", InstructionBundles.All.Keys.Order()), row.BundleKey);
            }

            if (keys.Count > 0)
                result[group.Key] = keys;
        }

        return result;
    }

    /// <summary>Attached bundle keys for one agent, in composition order. Empty when none.</summary>
    public static async Task<IReadOnlyList<string>> LoadAsync(
        AppDbContext db, Guid agentId, ILogger? logger, CancellationToken ct) =>
        (await LoadAsync(db, [agentId], logger, ct)).GetValueOrDefault(agentId, []);

    /// <summary>
    /// Replace an agent's attachments with exactly <paramref name="bundleKeys"/>, in that order.
    /// Returns true when anything actually changed, so a caller can skip a pointless event publish.
    ///
    /// <para>Validates against the catalog and rejects (422) rather than dropping: an operator
    /// typing a key that does not exist has made a mistake worth hearing about, which is the
    /// opposite case from a stored row whose file was renamed out from under it after the fact.</para>
    /// </summary>
    public static async Task<bool> SetAsync(
        AppDbContext db,
        Agent agent,
        IReadOnlyList<string> bundleKeys,
        DateTime now,
        CancellationToken ct)
    {
        var keys = Validate(bundleKeys);

        var existing = await db.AgentBundleAttachments
            .Where(a => a.AgentId == agent.Id)
            .ToListAsync(ct);

        var unchanged = existing.Count == keys.Count
            && existing
                .OrderBy(a => a.Position).ThenBy(a => a.BundleKey)
                .Select(a => a.BundleKey)
                .SequenceEqual(keys, StringComparer.Ordinal);
        if (unchanged)
            return false;

        // Replace wholesale. The set is tiny and the alternative — diffing keys against positions —
        // has to get reordering right too, for no gain over three deleted rows.
        db.AgentBundleAttachments.RemoveRange(existing);
        for (var i = 0; i < keys.Count; i++)
        {
            db.AgentBundleAttachments.Add(new AgentBundleAttachment
            {
                AgentId = agent.Id,
                BundleKey = keys[i],
                Position = i,
                CreatedAt = now,
            });
        }

        return true;
    }

    /// <summary>
    /// The submitted keys, trimmed, deduped (first occurrence keeps its place) and checked against
    /// the catalog. Throws <see cref="ValidationException"/> on anything unknown or unattachable.
    /// </summary>
    public static List<string> Validate(IReadOnlyList<string> bundleKeys)
    {
        var keys = new List<string>(bundleKeys.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unknown = new List<string>();
        var styles = new List<string>();

        foreach (var raw in bundleKeys)
        {
            var key = raw?.Trim() ?? string.Empty;
            if (key.Length == 0 || !seen.Add(key))
                continue;

            if (!InstructionBundles.Exists(key))
                unknown.Add(key);
            // A style is a real bundle, so Exists() says yes — it is simply not attachable, and the
            // message has to say WHY or an operator reads it as a typo they did not make.
            else if (InstructionBundles.IsStyle(key))
                styles.Add(key);
            else
                keys.Add(key);
        }

        if (unknown.Count > 0)
        {
            throw new ValidationException(
                nameof(Dtos.UpdateAgentRequest.BundleKeys),
                $"Unknown instruction bundle(s): {string.Join(", ", unknown)}. Attachable bundles: "
                + $"{string.Join(", ", InstructionBundles.Attachable.Select(b => b.Key))}.");
        }

        if (styles.Count > 0)
        {
            throw new ValidationException(
                nameof(Dtos.UpdateAgentRequest.BundleKeys),
                $"Bundle(s) {string.Join(", ", styles)} carry a reply style and are chosen with the agent's "
                + "ReplyStyle, not attached — attaching one would give the agent two voices at once.");
        }

        return keys;
    }
}
