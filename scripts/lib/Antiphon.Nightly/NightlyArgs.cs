// W2-nightly S01: the command line of the nightly CLI commands (plan 2026-11-21-w2-nightly-port-slices.md, sections 3
// and 4). After the command name every argument is a declared flag: "--kebab value", or a bare switch. A list flag's
// value is comma separated (a launcher joins a [string[]] with commas). An unknown, repeated or valueless flag, or a
// positional argument, is a usage error (NightlyUsageException; the CLI answers exit 2). The launchers
// (scripts/lib/antiphon-cli.ps1) leave out a flag whose value is empty, so an absent flag means the default.
// Plain C#: no PowerShell types. Flag names are compared ordinally. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>A malformed command line. The message names the flag or argument; the CLI prints it with the usage and exits 2.</summary>
    public sealed class NightlyUsageException : Exception
    {
        /// <summary>A usage error with its reason.</summary>
        public NightlyUsageException(string message) : base(message) { }
    }

    /// <summary>One parsed command line: the values of the valued flags given and the switches given.</summary>
    public sealed class NightlyArgs
    {
        private static readonly Regex Kebab = new Regex("^--[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

        private readonly Dictionary<string, string> _values;
        private readonly HashSet<string> _switches;
        private readonly HashSet<string> _declared;

        private NightlyArgs(Dictionary<string, string> values, HashSet<string> switches, HashSet<string> declared)
        {
            _values = values;
            _switches = switches;
            _declared = declared;
        }

        /// <summary>
        /// Parses the arguments after the command name. valued names the flags that take a value, switches the bare
        /// flags; each name is "--" and lower-case kebab words, and no name is in both lists (otherwise ArgumentException:
        /// a programming error, not a usage error). A valued flag takes the next argument as its value, which may be empty
        /// but may not be another declared flag. Throws NightlyUsageException for an unknown flag, a repeated flag, a
        /// valued flag with no value, or an argument that is not a flag.
        /// </summary>
        public static NightlyArgs Parse(IReadOnlyList<string> arguments, IEnumerable<string> valued, IEnumerable<string> switches)
        {
            var valuedSet = Declare(valued, nameof(valued));
            var switchSet = Declare(switches, nameof(switches));
            foreach (var name in valuedSet)
                if (switchSet.Contains(name)) throw new ArgumentException("flag declared as both valued and switch: " + name);
            var declared = new HashSet<string>(valuedSet.Concat(switchSet), StringComparer.Ordinal);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var given = new HashSet<string>(StringComparer.Ordinal);
            var count = arguments?.Count ?? 0;
            for (var i = 0; i < count; i++)
            {
                var argument = arguments[i] ?? "";
                if (!declared.Contains(argument))
                {
                    if (argument.StartsWith("--", StringComparison.Ordinal))
                    {
                        var equals = argument.IndexOf('=');
                        var name = equals > 0 ? argument.Substring(0, equals) : argument;
                        throw new NightlyUsageException(equals > 0 && declared.Contains(name)
                            ? "write " + name + " <value>, not " + name + "=<value>"
                            : "unknown flag " + name);
                    }
                    throw new NightlyUsageException("unexpected argument '" + argument + "'");
                }
                if (!given.Add(argument)) throw new NightlyUsageException("repeated flag " + argument);
                if (switchSet.Contains(argument)) continue;
                if (i + 1 >= count || declared.Contains(arguments[i + 1] ?? "")) throw new NightlyUsageException("missing value for " + argument);
                values[argument] = arguments[++i] ?? "";
            }
            var switchesGiven = new HashSet<string>(given.Where(switchSet.Contains), StringComparer.Ordinal);
            return new NightlyArgs(values, switchesGiven, declared);
        }

        /// <summary>True when the flag (valued or switch) was given.</summary>
        public bool Has(string flag)
        {
            Known(flag);
            return _values.ContainsKey(flag) || _switches.Contains(flag);
        }

        /// <summary>A valued flag's value exactly as given (possibly empty), or fallback when it was not given.</summary>
        public string Value(string flag, string fallback = null)
        {
            Known(flag);
            return _values.TryGetValue(flag, out var value) ? value : fallback;
        }

        /// <summary>A valued flag's value; NightlyUsageException ("missing --flag") when it was not given or is empty.</summary>
        public string Required(string flag)
        {
            var value = Value(flag);
            if (string.IsNullOrEmpty(value)) throw new NightlyUsageException("missing " + flag);
            return value;
        }

        /// <summary>True when the switch was given.</summary>
        public bool Switch(string flag)
        {
            Known(flag);
            return _switches.Contains(flag);
        }

        /// <summary>A list flag: its value split at commas, each entry trimmed, empty entries dropped. Empty when the flag was not given.</summary>
        public IReadOnlyList<string> List(string flag)
        {
            var value = Value(flag);
            if (string.IsNullOrEmpty(value)) return Array.Empty<string>();
            return value.Split(',').Select(entry => entry.Trim()).Where(entry => entry.Length > 0).ToArray();
        }

        /// <summary>An integer flag (invariant culture, optional sign), fallback when it was not given or is empty; NightlyUsageException when it does not parse.</summary>
        public int Int(string flag, int fallback)
        {
            var value = Value(flag);
            if (string.IsNullOrEmpty(value)) return fallback;
            if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
                throw new NightlyUsageException(flag + " expects an integer, not '" + value + "'");
            return parsed;
        }

        private static HashSet<string> Declare(IEnumerable<string> names, string list)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names ?? Array.Empty<string>())
            {
                if (name == null || !Kebab.IsMatch(name)) throw new ArgumentException("flag names are --kebab-case: '" + name + "'", list);
                if (!set.Add(name)) throw new ArgumentException("flag declared twice: " + name, list);
            }
            return set;
        }

        private void Known(string flag)
        {
            // Reading an undeclared flag is a programming error: it could never be given.
            if (flag == null || !_declared.Contains(flag)) throw new ArgumentException("undeclared flag: " + flag, nameof(flag));
        }
    }
}
