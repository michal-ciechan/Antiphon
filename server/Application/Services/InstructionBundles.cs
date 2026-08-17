using System.Collections.Frozen;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// One named block of standing instructions: a markdown file under <c>server/Bundles/</c>, compiled
/// into this assembly as an embedded resource, versioned by the hash of its own content.
/// </summary>
/// <param name="Key">The filename without <c>.md</c> — <c>delegate-basics</c>, <c>orchestrator</c>.</param>
/// <param name="Version">
/// First 8 hex digits of the SHA-256 of <paramref name="Text"/>. There is nothing to bump by hand:
/// editing the file changes the version, which is the whole reason the version is a hash and not a
/// constant somebody has to remember (the check interpreter's <c>ContractVersion = "1"</c> is the
/// prototype this generalises, and it never got bumped either).
/// </param>
/// <param name="Text">The file's content, line endings normalised to LF and outer blank space trimmed.</param>
public sealed record InstructionBundle(string Key, string Version, string Text)
{
    /// <summary>
    /// The line that rides above the text, so an operator reading launch args, an agent row or a
    /// transcript can see WHICH bundles and WHICH versions that agent is carrying without diffing
    /// prose against the repo.
    /// </summary>
    public string Header => $"[bundle:{Key} v{Version}]";

    /// <summary>Header then text — what actually reaches the model.</summary>
    public string Render() => $"{Header}\n{Text}";

    /// <summary>Compact form for a DTO or a log line: <c>delegate-basics v3f9a1b2c</c>.</summary>
    public string Stamp => $"{Key} v{Version}";
}

/// <summary>
/// The bundle catalog (CARD-0058). Bundles are FILES IN THE REPO, not database rows: an
/// operator-editable content table would reinstate exactly the drift this card exists to remove,
/// and the check-interpreter prototype's whole lesson is that a behaviour change is a PR. The
/// database holds attachment state only — which agent carries which optional bundle.
///
/// <para>Because the composed text is a LAUNCH ARGUMENT (<c>--append-system-prompt</c>) and never
/// typed into the pty, none of the CARD-0027/0037 pty ceilings apply to it. Its real bounds are the
/// Windows command line (~32 767 UTF-16 chars for <c>CreateProcessW</c>, guarded by
/// <see cref="InstructionBundleComposer.EnsureWithinCommandLineBudget"/>) and the fact that a
/// running session only picks up a changed bundle at its NEXT launch.</para>
///
/// <para>Nothing here is registered in DI: the catalog is immutable, process-wide and derived from
/// the assembly, so a static is the honest shape and every consumer — including a pure composer —
/// can reach it without a service.</para>
/// </summary>
public static class InstructionBundles
{
    /// <summary>The standing harness rules every delegate needs (foreground, no sub-delegation, commit each slice, …).</summary>
    public const string DelegateBasics = "delegate-basics";

    /// <summary>How to work the card API. On no role by default — see <see cref="ForDelegate"/>.</summary>
    public const string BoardApi = "board-api";

    /// <summary>The sub-orchestrator contract, moved here from <c>DelegationReportFormatter</c>.</summary>
    public const string Orchestrator = "orchestrator";

    /// <summary>The check interpreter's contract, moved here from <c>CheckInterpretation</c>.</summary>
    public const string CheckInterpreter = "check-interpreter";

    /// <summary>
    /// The reply-style blocks (CARD-0060), one per <see cref="AgentReplyStyle"/> value. Resolved
    /// through <see cref="AgentReplyStyles"/> rather than named directly — the map from enum to key
    /// is the one place that knows <c>Normal</c> is not composed at all.
    /// </summary>
    public const string StylePrefix = "style-";

    private const string ResourcePrefix = "Antiphon.Server.Bundles.";
    private const string ResourceSuffix = ".md";

    private static readonly Lazy<FrozenDictionary<string, InstructionBundle>> Loaded = new(Load);

    /// <summary>Every bundle in the catalog, keyed by <see cref="InstructionBundle.Key"/>.</summary>
    public static FrozenDictionary<string, InstructionBundle> All => Loaded.Value;

    /// <summary>
    /// A bundle by key. Throws rather than returning null: every key in the system is a compile-time
    /// constant or a role-map entry, so a miss is a build-time mistake that shipped, and a launch
    /// that silently drops an agent's contract is the failure this whole card is about.
    /// </summary>
    public static InstructionBundle Get(string key) =>
        All.TryGetValue(key, out var bundle)
            ? bundle
            : throw new KeyNotFoundException(
                $"No instruction bundle '{key}'. Known bundles: {string.Join(", ", All.Keys.Order())}. "
                + "A bundle is a markdown file under server/Bundles/, embedded by Antiphon.Server.csproj.");

    /// <summary>The bundle's text alone, for the two constants that forward to the catalog.</summary>
    public static string TextOf(string key) => Get(key).Text;

    /// <summary>
    /// Which bundles a DELEGATE launch carries, from the shape of its task (plan §4). A code-level
    /// map, not configuration: which standing rules a role needs is a design decision that belongs
    /// in a PR next to the rules themselves.
    ///
    /// <para>Delegate agent rows are ephemeral (<c>task-&lt;id&gt;</c>, deleted when the task
    /// settles), so per-agent attachments could never reach them — the role is the only durable
    /// handle there is.</para>
    /// </summary>
    public static IReadOnlyList<string> ForDelegate(AgentTaskKind kind, AgentTaskRole role)
    {
        // STRUCTURAL CARVE-OUT, not incidental. A Check task is pinned to the standing check
        // interpreter, whose own contract is reconciled onto its row and rendered by
        // AgentControlService; it has no tools and a deny-all PreToolUse hook that refuses every one.
        // Handing it "commit and push each slice" would be an impossible instruction, and it would
        // reach it on exactly the path nobody watches: the dispatcher only builds a launch spec for a
        // pinned agent when that agent's session is NOT already up.
        if (role == AgentTaskRole.Check)
            return [];

        // A sub-orchestrator is a delegate too — it gets the delegate rules AND its own contract. The
        // orchestrator block comes first because it is what the agent IS; the basics are how the
        // harness behaves around it.
        return kind == AgentTaskKind.Orchestrator
            ? [Orchestrator, DelegateBasics]
            : [DelegateBasics];
    }

    private static FrozenDictionary<string, InstructionBundle> Load()
    {
        var assembly = typeof(InstructionBundles).Assembly;
        var bundles = new Dictionary<string, InstructionBundle>(StringComparer.Ordinal);

        // Enumerated from the manifest rather than named one by one: adding a bundle stays a
        // one-file change, and the naming rules MSBuild applies to a resource path never have to be
        // guessed at (InstructionBundleTests pins the resulting key set, so a rename fails loudly).
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                || !resource.EndsWith(ResourceSuffix, StringComparison.Ordinal))
                continue;

            var key = resource[ResourcePrefix.Length..^ResourceSuffix.Length];
            var text = ReadNormalised(assembly, resource);
            bundles[key] = new InstructionBundle(key, HashOf(text), text);
        }

        return bundles.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static string ReadNormalised(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded bundle '{resource}' could not be opened.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        // LF, always. The working tree is CRLF here (`* text=auto`, core.autocrlf=true), so hashing
        // the file as-checked-out would give a different version on a Linux checkout of the same
        // commit — a version that changes with nothing but the clone is not a version.
        return reader.ReadToEnd().ReplaceLineEndings("\n").Trim();
    }

    private static string HashOf(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8];
}
