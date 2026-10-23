using System.Text;
using System.Text.RegularExpressions;
#if HOSTCLEANUP_DIAGNOSTICS
namespace Antiphon.HostCleanup.DiagnosticText;
#elif CHECKPOINTS_DIAGNOSTICS
namespace Antiphon.Checkpoints.DiagnosticText;
#else
namespace Antiphon.DiagnosticText;
#endif
internal static partial class DiagnosticCredentialRedactor
{
    private const int MaximumSecretValues = 256;
    private const int MaximumEnvironmentValueLength = 4000;
    private const int MaximumSecretMaterialLength = 256 * 1024;
    internal static SanitizedOutput Sanitize(
        string standardOutput,
        string standardError,
        IReadOnlyList<string> secretValues)
    {
        var sensitive = false;
        if (!ExactSecretMatcher.TryCreate(secretValues, out var exactSecretMatcher))
            return new SanitizedOutput(string.Empty, string.Empty, SensitiveOutputDetected: true);

        standardOutput = RedactOutput(standardOutput, exactSecretMatcher, ref sensitive);
        standardError = RedactOutput(standardError, exactSecretMatcher, ref sensitive);
        return new SanitizedOutput(standardOutput, standardError, sensitive);
    }

    private static string RedactOutput(
        string value,
        ExactSecretMatcher exactSecretMatcher,
        ref bool sensitive)
    {
        if (value.Length == 0)
            return value;

        var redactionDeltas = new int[value.Length + 1];
        var detected = CollectCredentialAssignmentRedactions(value, redactionDeltas);
        detected |= CollectBearerCredentialRedactions(value, redactionDeltas);
        detected |= exactSecretMatcher.CollectRedactions(value, redactionDeltas);
        if (!detected)
            return value;

        sensitive = true;
        return RenderRedactions(value, redactionDeltas);
    }

    private static bool CollectBearerCredentialRedactions(string value, int[] redactionDeltas)
    {
        var detected = false;
        foreach (Match match in BearerCredentialRegex().Matches(value))
        {
            AddRedaction(redactionDeltas, match.Index, match.Index + match.Length);
            detected = true;
        }
        return detected;
    }

    private static bool CollectCredentialAssignmentRedactions(string value, int[] redactionDeltas)
    {
        var matches = CredentialAssignmentRegex().Matches(value);
        var lineEnds = IndexLineEnds(value);
        var lineEndIndex = 0;
        var coveredThrough = 0;
        var detected = false;

        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            if (!IsCredentialName(match.Groups["name"].Value))
                continue;

            var assignment = match.Groups["assignment"];
            if (assignment.Index < coveredThrough)
                continue;
            var redactionEnd = assignment.Index + assignment.Length;
            var unquotedValue = match.Groups["unquotedValue"];
            if (unquotedValue.Success)
            {
                while (lineEnds[lineEndIndex] < redactionEnd)
                    lineEndIndex++;
                var lineEnd = lineEnds[lineEndIndex];
                var malformedQuotedValue = unquotedValue.Value[0] is '"' or '\'';
                redactionEnd = !malformedQuotedValue
                               && index + 1 < matches.Count
                               && matches[index + 1].Index < lineEnd
                    ? matches[index + 1].Index
                    : lineEnd;
            }

            AddRedaction(redactionDeltas, assignment.Index, redactionEnd);
            coveredThrough = redactionEnd;
            detected = true;
        }
        return detected;
    }

    private static void AddRedaction(int[] redactionDeltas, int start, int end)
    {
        redactionDeltas[start]++;
        redactionDeltas[end]--;
    }

    private static string RenderRedactions(string value, int[] redactionDeltas)
    {
        var redacted = new StringBuilder(value.Length);
        var copiedThrough = 0;
        var redactionStart = -1;
        var coverage = 0;
        for (var index = 0; index < value.Length; index++)
        {
            coverage += redactionDeltas[index];
            if (coverage > 0)
            {
                if (redactionStart < 0)
                    redactionStart = index;
                continue;
            }

            if (redactionStart < 0)
                continue;
            redacted.Append(value, copiedThrough, redactionStart - copiedThrough);
            redacted.Append('*');
            copiedThrough = index;
            redactionStart = -1;
        }

        if (redactionStart >= 0)
        {
            redacted.Append(value, copiedThrough, redactionStart - copiedThrough);
            redacted.Append('*');
            copiedThrough = value.Length;
        }
        redacted.Append(value, copiedThrough, value.Length - copiedThrough);
        return redacted.ToString();
    }

    private static int[] IndexLineEnds(string value)
    {
        var lineEnds = new List<int>();
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] is '\r' or '\n')
                lineEnds.Add(index);
        }
        return [.. lineEnds, value.Length];
    }

    private static bool IsCredentialName(string name) =>
        ExactOrDelimitedCredentialNameRegex().IsMatch(name)
        || CamelCaseCredentialSuffixRegex().IsMatch(name);

    [GeneratedRegex(
        @"(?:\A|[^A-Za-z0-9_])(?<assignment>[""']?(?<name>[A-Za-z][A-Za-z0-9_-]*)[""']?[ \t]*[:=][ \t]*(?<value>(?<quotedValue>""(?:\\[^\r\n]|[^""\\\r\n])*""|'(?:\\[^\r\n]|[^'\\\r\n])*')|(?<unquotedValue>[^\s,;&|}\]]+)))",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex CredentialAssignmentRegex();

    [GeneratedRegex(
        @"(?i)\A(?:[A-Za-z][A-Za-z0-9]*[_-])*(?:api[_-]?(?:key|token)|access[_-]?token|refresh[_-]?token|secret[_-]?access[_-]?key|private[_-]?key|database[_-]?url|connection[_-]?string|token|password|secret|authorization)\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex ExactOrDelimitedCredentialNameRegex();

    [GeneratedRegex(
        @"[a-z0-9](?:ApiKey|ApiToken|AccessToken|RefreshToken|SecretAccessKey|PrivateKey|DatabaseUrl|ConnectionString|AuthToken|Authorization|Token|Password|Secret)\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex CamelCaseCredentialSuffixRegex();

    [GeneratedRegex(
        @"(?im)\bbearer\s+[^\s\r\n]+",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex BearerCredentialRegex();

    private sealed class ExactSecretMatcher
    {
        private readonly List<SecretMatchState> _states;
        private readonly List<SecretMatchEdge> _edges;
        private readonly Dictionary<SecretTransition, int> _transitions;

        private ExactSecretMatcher(int maximumStateCount)
        {
            _states = new List<SecretMatchState>(maximumStateCount)
            {
                new() { FirstEdge = -1 }
            };
            _edges = new List<SecretMatchEdge>(Math.Max(0, maximumStateCount - 1));
            _transitions = new Dictionary<SecretTransition, int>(Math.Max(0, maximumStateCount - 1));
        }

        public static bool TryCreate(
            IReadOnlyList<string> secrets,
            out ExactSecretMatcher matcher)
        {
            matcher = null!;
            if (secrets.Count > MaximumSecretValues)
                return false;

            var secretMaterialLength = 0;
            var distinctSecrets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var secret in secrets)
            {
                if (secret is null || secret.Length > MaximumEnvironmentValueLength)
                    return false;
                secretMaterialLength += secret.Length;
                if (secretMaterialLength > MaximumSecretMaterialLength)
                    return false;
                if (secret.Length > 0)
                    distinctSecrets.Add(secret);
            }

            matcher = new ExactSecretMatcher(secretMaterialLength + 1);
            foreach (var secret in distinctSecrets)
                matcher.Add(secret);
            matcher.BuildFailureLinks();
            return true;
        }

        public bool CollectRedactions(string value, int[] redactionDeltas)
        {
            var stateIndex = 0;
            var detected = false;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                while (stateIndex != 0 && !TryTransition(stateIndex, character, out _))
                    stateIndex = _states[stateIndex].Failure;

                if (TryTransition(stateIndex, character, out var nextState))
                    stateIndex = nextState;
                else
                    stateIndex = 0;

                var matchLength = _states[stateIndex].MaximumMatchLength;
                if (matchLength == 0)
                    continue;

                AddRedaction(redactionDeltas, index + 1 - matchLength, index + 1);
                detected = true;
            }
            return detected;
        }

        private void Add(string secret)
        {
            var stateIndex = 0;
            foreach (var character in secret)
            {
                var transition = new SecretTransition(stateIndex, character);
                if (!_transitions.TryGetValue(transition, out var nextState))
                {
                    nextState = _states.Count;
                    _states.Add(new SecretMatchState { FirstEdge = -1 });
                    var parent = _states[stateIndex];
                    _edges.Add(new SecretMatchEdge(
                        character,
                        nextState,
                        parent.FirstEdge));
                    parent.FirstEdge = _edges.Count - 1;
                    _states[stateIndex] = parent;
                    _transitions.Add(transition, nextState);
                }
                stateIndex = nextState;
            }

            var terminal = _states[stateIndex];
            terminal.MaximumMatchLength = Math.Max(
                terminal.MaximumMatchLength,
                secret.Length);
            _states[stateIndex] = terminal;
        }

        private void BuildFailureLinks()
        {
            var pending = new Queue<int>(_states.Count);
            for (var edgeIndex = _states[0].FirstEdge;
                 edgeIndex >= 0;
                 edgeIndex = _edges[edgeIndex].NextEdge)
            {
                pending.Enqueue(_edges[edgeIndex].TargetState);
            }

            while (pending.TryDequeue(out var stateIndex))
            {
                var state = _states[stateIndex];
                for (var edgeIndex = state.FirstEdge;
                     edgeIndex >= 0;
                     edgeIndex = _edges[edgeIndex].NextEdge)
                {
                    var edge = _edges[edgeIndex];
                    var failure = state.Failure;
                    while (failure != 0 && !TryTransition(failure, edge.Character, out _))
                        failure = _states[failure].Failure;
                    if (TryTransition(failure, edge.Character, out var fallback))
                        failure = fallback;

                    var child = _states[edge.TargetState];
                    child.Failure = failure;
                    child.MaximumMatchLength = Math.Max(
                        child.MaximumMatchLength,
                        _states[failure].MaximumMatchLength);
                    _states[edge.TargetState] = child;
                    pending.Enqueue(edge.TargetState);
                }
            }
        }

        private bool TryTransition(int state, char character, out int nextState) =>
            _transitions.TryGetValue(new SecretTransition(state, character), out nextState);

        private struct SecretMatchState
        {
            public int FirstEdge;
            public int Failure;
            public int MaximumMatchLength;
        }

        private readonly record struct SecretMatchEdge(
            char Character,
            int TargetState,
            int NextEdge);
        private readonly record struct SecretTransition(int State, char Character);
    }

    internal sealed record SanitizedOutput(string StandardOutput, string StandardError, bool SensitiveOutputDetected);
}
