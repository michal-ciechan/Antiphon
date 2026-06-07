using System.Text;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Ranks candidate child directories against the partial leaf segment the user is typing, so the
/// working-directory autocomplete behaves like editor intellisense rather than a strict prefix
/// filter. Matching is performed on each candidate's <em>leaf</em> name (the part after the last
/// '/') and combines three signals:
///
/// <list type="bullet">
///   <item><description><b>Partial</b> — a contiguous substring match (e.g. "lea" in "torquay-leander").</description></item>
///   <item><description><b>Fuzzy</b> — a subsequence match when no substring exists (e.g. "tql" in "torquay-leander").</description></item>
///   <item><description><b>BM25</b> — names are tokenized on separators/camel humps and scored against the
///     sibling set as a corpus, so a match on a rarer token (higher IDF) ranks above a match on a
///     token shared by many siblings.</description></item>
/// </list>
///
/// Prefix and word-boundary matches are boosted so the most "obvious" completions float to the top.
/// The leaf-character indices that matched are intentionally <em>not</em> returned: highlighting is a
/// presentational concern recomputed client-side from the same query, keeping the wire contract a
/// plain ordered list of paths.
/// </summary>
public static class DirectoryMatcher
{
    // BM25 free parameters (Robertson/Zaragoza defaults). k1 controls term-frequency saturation,
    // b controls length normalization. Folder names are short so these barely bend, but keeping the
    // canonical form makes the intent legible.
    private const double Bm25K1 = 1.2;
    private const double Bm25B = 0.75;

    // Relative weights for the composite score. Substring/prefix dominate; BM25 breaks ties among
    // otherwise equally-good positional matches.
    private const double SubstringBase = 1000;
    private const double PrefixBonus = 500;
    private const double BoundaryBonus = 300;
    private const double SubsequenceBase = 200;
    private const double ContiguousBonus = 20;
    private const double SubsequenceBoundaryBonus = 40;
    private const double Bm25Weight = 10;

    /// <summary>
    /// Returns the subset of <paramref name="children"/> whose leaf name matches
    /// <paramref name="query"/> (case-insensitive), ordered best-first. An empty query returns the
    /// children unchanged (nothing has been typed in this segment yet).
    /// </summary>
    public static IReadOnlyList<string> Rank(string query, IReadOnlyList<string> children)
    {
        if (string.IsNullOrEmpty(query))
            return children;

        var leaves = new string[children.Count];
        for (var i = 0; i < children.Count; i++)
            leaves[i] = Leaf(children[i]);

        var docFreq = BuildDocFrequencies(leaves);
        var avgTokens = leaves.Length == 0 ? 1.0 : leaves.Average(l => Tokenize(l).Count);
        if (avgTokens <= 0) avgTokens = 1.0;

        var scored = new List<(string path, double score)>();
        for (var i = 0; i < children.Count; i++)
        {
            if (!TryScore(query, leaves[i], out var positional))
                continue;
            var bm25 = Bm25(query, leaves[i], docFreq, children.Count, avgTokens);
            scored.Add((children[i], positional + bm25 * Bm25Weight));
        }

        return scored
            .OrderByDescending(s => s.score)
            .ThenBy(s => Leaf(s.path).Length)
            .ThenBy(s => s.path, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.path)
            .ToList();
    }

    /// <summary>The portion of a forward-slash path after its last separator ("C:/src/foo" → "foo").</summary>
    private static string Leaf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    /// <summary>
    /// Scores how well <paramref name="query"/> matches <paramref name="text"/> positionally.
    /// Returns false when there is not even a subsequence match. A contiguous substring is scored
    /// well above a scattered subsequence; prefix and word-boundary alignment add further boosts.
    /// </summary>
    private static bool TryScore(string query, string text, out double score)
    {
        score = 0;
        var q = query.ToLowerInvariant();
        var t = text.ToLowerInvariant();

        var sub = t.IndexOf(q, StringComparison.Ordinal);
        if (sub >= 0)
        {
            score = SubstringBase;
            if (sub == 0)
                score += PrefixBonus;
            else if (IsBoundary(text, sub))
                score += BoundaryBonus;
            score -= sub;                              // earlier in the name is better
            score -= (t.Length - q.Length) * 0.5;      // tighter (less leftover) is better
            return true;
        }

        // Fall back to a greedy subsequence match (fuzzy). Track contiguity and boundary hits to
        // reward matches that read more like the intended word.
        var ti = 0;
        var firstIdx = -1;
        var lastIdx = -1;
        var contiguous = 0;
        var boundaryHits = 0;
        for (var qi = 0; qi < q.Length; qi++)
        {
            var found = false;
            while (ti < t.Length)
            {
                if (t[ti] == q[qi])
                {
                    if (firstIdx < 0) firstIdx = ti;
                    if (lastIdx == ti - 1) contiguous++;
                    if (IsBoundary(text, ti)) boundaryHits++;
                    lastIdx = ti;
                    ti++;
                    found = true;
                    break;
                }
                ti++;
            }
            if (!found)
                return false;
        }

        var span = lastIdx - firstIdx;
        score = SubsequenceBase
            + contiguous * ContiguousBonus
            + boundaryHits * SubsequenceBoundaryBonus
            - span
            - t.Length * 0.5;
        return true;
    }

    /// <summary>
    /// BM25 over the sibling set: the query is treated as a (prefix) term and scored against the
    /// tokens of <paramref name="leaf"/>, weighted by inverse document frequency across the siblings.
    /// Returns the best-matching token's BM25 contribution (0 if the query is no token's prefix).
    /// </summary>
    private static double Bm25(
        string query,
        string leaf,
        IReadOnlyDictionary<string, int> docFreq,
        int docCount,
        double avgTokens)
    {
        var tokens = Tokenize(leaf);
        if (tokens.Count == 0)
            return 0;

        var dl = tokens.Count;
        var best = 0.0;
        foreach (var token in tokens)
        {
            if (!token.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                continue;
            var df = docFreq.TryGetValue(token, out var v) ? v : 1;
            var idf = Math.Log(1 + (docCount - df + 0.5) / (df + 0.5));
            const double tf = 1;
            var term = idf * (tf * (Bm25K1 + 1)) /
                       (tf + Bm25K1 * (1 - Bm25B + Bm25B * dl / avgTokens));
            if (term > best)
                best = term;
        }
        return best;
    }

    /// <summary>Document frequency of each token across the sibling leaf names (for BM25 IDF).</summary>
    private static Dictionary<string, int> BuildDocFrequencies(IReadOnlyList<string> leaves)
    {
        var df = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var leaf in leaves)
            foreach (var token in Tokenize(leaf).Distinct(StringComparer.OrdinalIgnoreCase))
                df[token] = df.TryGetValue(token, out var v) ? v + 1 : 1;
        return df;
    }

    /// <summary>
    /// Splits a name into lower-cased word tokens on separators (space, '-', '_', '.') and camelCase
    /// humps. "torquay-leander" → ["torquay", "leander"]; "MyApp.Core" → ["my", "app", "core"].
    /// </summary>
    private static List<string> Tokenize(string name)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length > 0)
            {
                tokens.Add(current.ToString().ToLowerInvariant());
                current.Clear();
            }
        }

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c))
            {
                Flush();
                continue;
            }
            // camelCase boundary: an uppercase letter following a lowercase one starts a new token.
            if (i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1]))
                Flush();
            current.Append(c);
        }
        Flush();
        return tokens;
    }

    /// <summary>
    /// True when index <paramref name="i"/> begins a word in <paramref name="text"/>: the start of
    /// the string, immediately after a separator, or a camelCase hump.
    /// </summary>
    private static bool IsBoundary(string text, int i)
    {
        if (i <= 0)
            return true;
        var prev = text[i - 1];
        if (!char.IsLetterOrDigit(prev))
            return true;
        return char.IsUpper(text[i]) && char.IsLower(prev);
    }
}
