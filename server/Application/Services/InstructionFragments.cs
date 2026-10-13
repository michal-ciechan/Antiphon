using System.Collections.Frozen;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// One shared instruction block that catalog bundles pull in with a <c>{{NAME}}</c> placeholder
/// (CARD-1213 D-5): a markdown file under <c>server/Bundles/Fragments/</c>, embedded like a bundle
/// and versioned by the hash of its own content. The bundle catalog skips it
/// (InstructionFragmentTests.C1213_CatalogAndHash), so it is not an attachable bundle.
/// </summary>
/// <param name="Key">The filename without <c>.md</c>: <c>git-common</c>.</param>
/// <param name="Version">First 8 hex digits of the SHA-256 of <paramref name="Text"/>, as for bundles.</param>
/// <param name="Text">The file's content, line endings normalised to LF and outer blank space trimmed.</param>
public sealed record InstructionFragment(string Key, string Version, string Text)
{
    /// <summary>The placeholder name: the key upper-cased, <c>GIT-COMMON</c>.</summary>
    public string Name => Key.ToUpperInvariant();

    /// <summary>The token a bundle writes: <c>{{GIT-COMMON}}</c>.</summary>
    public string Placeholder => "{{" + Name + "}}";

    public string Header => $"[fragment:{Key} v{Version}]";

    /// <summary>What the first placeholder in a composition becomes: header then text.</summary>
    public string Render() => $"{Header}\n{Text}";

    /// <summary>What every later placeholder in the same composition becomes.</summary>
    public string SeeAbove => $"[fragment:{Key} v{Version}: see above]";

    /// <summary>Joins <see cref="ComposedInstructions.Stamps"/> beside the bundle stamps.</summary>
    public string Stamp => $"{Key} v{Version}";
}

/// <summary>
/// The fragment catalog and the placeholder expansion (CARD-1213 D-5..D-7). Expansion runs inside
/// <see cref="InstructionBundleComposer.Compose(IEnumerable{string}?, string?, string?)"/> over catalog
/// bundle text only; the operator append, styles' semantics and briefs are not scanned.
/// Exercised by InstructionFragmentTests (catalog, grammar, once-per-composition, limits, stamps).
/// </summary>
public static class InstructionFragments
{
    public const string GitCommon = "git-common";

    /// <summary>Largest fragment text accepted by <see cref="Create"/> (C1213_FragmentLimits).</summary>
    public const int MaxChars = 1_500;

    /// <summary>The section marker every fragment carries (D-7).</summary>
    public const string InvariantsMarker = "INVARIANTS";

    internal const string ResourcePrefix = "Antiphon.Server.Bundles.Fragments.";
    private const string ResourceSuffix = ".md";

    /// <summary>
    /// D-5 grammar, anchored at the match start: <c>{{NAME}}</c> with NAME matching
    /// <c>^[A-Z][A-Z0-9-]{1,31}$</c>.
    /// </summary>
    public static readonly Regex PlaceholderPattern =
        new(@"\G\{\{([A-Z][A-Z0-9-]{1,31})\}\}", RegexOptions.CultureInvariant);

    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9-]{1,31}$", RegexOptions.CultureInvariant);
    private static readonly Regex ContractLine = new(@"^[A-Z][A-Z0-9-]{1,31} contract v[1-9][0-9]*\.$", RegexOptions.CultureInvariant);

    private static readonly Lazy<FrozenDictionary<string, InstructionFragment>> Loaded = new(Load);

    /// <summary>Every fragment, keyed by <see cref="InstructionFragment.Key"/>.</summary>
    public static FrozenDictionary<string, InstructionFragment> All => Loaded.Value;

    public static InstructionFragment Get(string key) =>
        All.TryGetValue(key, out var fragment)
            ? fragment
            : throw new KeyNotFoundException(
                $"No instruction fragment '{key}'. Known fragments: {string.Join(", ", All.Keys.Order())}.");

    /// <summary>
    /// Validate and version one fragment. Throws <see cref="InvalidOperationException"/> when the
    /// normalised text is over <see cref="MaxChars"/>, contains <c>{{</c>, <c>{agentName}</c> or
    /// <c>{channels}</c>, does not open with <c>&lt;NAME&gt; contract v&lt;n&gt;.</c>, or has no
    /// <see cref="InvariantsMarker"/> line (C1213_FragmentLimits).
    /// </summary>
    internal static InstructionFragment Create(string key, string rawText)
    {
        if (!KeyPattern.IsMatch(key))
            throw new InvalidOperationException($"Instruction fragment key '{key}' must match {KeyPattern}.");
        var text = rawText.ReplaceLineEndings("\n").Trim();
        var name = key.ToUpperInvariant();
        if (text.Length > MaxChars)
            throw new InvalidOperationException(
                $"Instruction fragment '{key}' is {text.Length:N0} chars; the limit is {MaxChars:N0}.");
        foreach (var forbidden in new[] { "{{", ChannelPreamble.AgentNamePlaceholder, ChannelPreamble.ChannelsPlaceholder })
        {
            if (text.Contains(forbidden, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Instruction fragment '{key}' contains '{forbidden}'; a fragment may not carry a placeholder.");
        }
        var firstLine = text.Split('\n')[0];
        if (!ContractLine.IsMatch(firstLine) || !firstLine.StartsWith(name + " contract v", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Instruction fragment '{key}' must open with '{name} contract v<n>.'; it opens with '{firstLine}'.");
        if (!text.Split('\n').Any(line => line.StartsWith(InvariantsMarker, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"Instruction fragment '{key}' has no line starting with '{InvariantsMarker}'.");
        return new InstructionFragment(key, HashOf(text), text);
    }

    /// <summary>
    /// Expand every placeholder in one rendered bundle. The first occurrence of a fragment in a
    /// composition (tracked by <paramref name="used"/>) becomes <see cref="InstructionFragment.Render"/>,
    /// every later one <see cref="InstructionFragment.SeeAbove"/> (C1213_ExpansionOnce). Any <c>{{</c>
    /// that does not start a known, well-formed placeholder throws <see cref="InvalidOperationException"/>
    /// naming the bundle, the token and the known placeholders (C1213_UnknownPlaceholderFailsLoudly,
    /// C1213_ExpansionScope).
    /// </summary>
    internal static string Expand(
        string bundleKey,
        string text,
        IReadOnlyCollection<InstructionFragment> fragments,
        List<InstructionFragment> used)
    {
        var open = text.IndexOf("{{", StringComparison.Ordinal);
        if (open < 0)
            return text;

        var result = new StringBuilder(text.Length);
        var at = 0;
        while (open >= 0)
        {
            result.Append(text, at, open - at);
            var match = PlaceholderPattern.Match(text, open);
            if (!match.Success)
                throw new InvalidOperationException(
                    $"Instruction bundle '{bundleKey}' has a malformed placeholder '{Snippet(text, open)}' at offset {open}. "
                    + $"A placeholder is {{{{NAME}}}} with NAME matching ^[A-Z][A-Z0-9-]{{1,31}}$. Known: {Known(fragments)}.");
            var name = match.Groups[1].Value;
            var fragment = fragments.FirstOrDefault(f => f.Name == name)
                ?? throw new InvalidOperationException(
                    $"Instruction bundle '{bundleKey}' references unknown placeholder '{match.Value}'. Known: {Known(fragments)}.");
            if (used.Any(f => f.Key == fragment.Key))
                result.Append(fragment.SeeAbove);
            else
            {
                used.Add(fragment);
                result.Append(fragment.Render());
            }
            at = open + match.Length;
            open = text.IndexOf("{{", at, StringComparison.Ordinal);
        }
        result.Append(text, at, text.Length - at);
        return result.ToString();
    }

    private static string Known(IReadOnlyCollection<InstructionFragment> fragments) =>
        fragments.Count == 0 ? "none" : string.Join(", ", fragments.Select(f => f.Placeholder).Order(StringComparer.Ordinal));

    private static string Snippet(string text, int open)
    {
        var end = Math.Min(text.Length, open + 40);
        var newline = text.IndexOf('\n', open);
        if (newline >= 0 && newline < end)
            end = newline;
        return text[open..end];
    }

    private static FrozenDictionary<string, InstructionFragment> Load()
    {
        var assembly = typeof(InstructionFragments).Assembly;
        var fragments = new Dictionary<string, InstructionFragment>(StringComparer.Ordinal);
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                || !resource.EndsWith(ResourceSuffix, StringComparison.Ordinal))
                continue;
            var key = resource[ResourcePrefix.Length..^ResourceSuffix.Length];
            fragments[key] = Create(key, Read(assembly, resource));
        }
        return fragments.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static string Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded fragment '{resource}' could not be opened.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string HashOf(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8];
}
