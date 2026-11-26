// The command line of the live-lane commands (live-lane-run, live-lane-input-digest): after the command name every
// argument is a declared flag, "--kebab value", or a bare switch. An unknown, repeated or valueless flag, or a positional
// argument, is a usage error (LiveLaneUsageException; the CLI answers exit 2). The rules of the nightly core's
// NightlyArgs, copied so this project references nothing that changes often. Plain C#, framework only. ASCII only.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Antiphon.LiveLane
{
    /// <summary>A malformed live-lane command line: the CLI prints the reason with the usage and exits 2.</summary>
    public sealed class LiveLaneUsageException : Exception
    {
        public LiveLaneUsageException(string message) : base(message) { }
    }

    /// <summary>The command line of the live-lane commands: declared "--kebab value" flags and bare switches only.</summary>
    public sealed class LiveLaneArgs
    {
        private readonly Dictionary<string, string> _values;
        private readonly HashSet<string> _switches;

        private LiveLaneArgs(Dictionary<string, string> values, HashSet<string> switches)
        {
            _values = values;
            _switches = switches;
        }

        /// <summary>Parses the arguments after the command name; LiveLaneUsageException for anything undeclared.</summary>
        public static LiveLaneArgs Parse(IReadOnlyList<string> arguments, IReadOnlyCollection<string> valued, IReadOnlyCollection<string> switches)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var given = new HashSet<string>(StringComparer.Ordinal);
            var count = arguments?.Count ?? 0;
            for (var i = 0; i < count; i++)
            {
                var argument = arguments[i] ?? "";
                var isValued = valued.Contains(argument, StringComparer.Ordinal);
                var isSwitch = switches.Contains(argument, StringComparer.Ordinal);
                if (!isValued && !isSwitch)
                {
                    if (argument.StartsWith("--", StringComparison.Ordinal)) throw new LiveLaneUsageException("unknown flag " + argument);
                    throw new LiveLaneUsageException("unexpected argument '" + argument + "'");
                }
                if (!given.Add(argument)) throw new LiveLaneUsageException("repeated flag " + argument);
                if (isSwitch) continue;
                var next = i + 1 < count ? arguments[i + 1] ?? "" : null;
                if (next == null || valued.Contains(next, StringComparer.Ordinal) || switches.Contains(next, StringComparer.Ordinal))
                    throw new LiveLaneUsageException("missing value for " + argument);
                values[argument] = next;
                i++;
            }
            return new LiveLaneArgs(values, new HashSet<string>(given.Where(name => switches.Contains(name, StringComparer.Ordinal)), StringComparer.Ordinal));
        }

        public string Value(string flag) => _values.TryGetValue(flag, out var value) ? value : null;

        public bool Switch(string flag) => _switches.Contains(flag);
    }
}
