// CARD-1836 S6a compiled dirty launch order. Shadow only: effectiveMode stays Full and gateCredit stays false.
// Containment is ancestor, otherwise one exact cherry-pick trailer line. No trust transition.
// Results serialize with the pre-port camelCase names (CARD-2010 A-2), whatever options the writer passes.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    public sealed class RcDirtyLaunchItem
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }
        [JsonPropertyName("band")]
        public string Band { get; set; }
        [JsonPropertyName("disposition")]
        public string Disposition { get; set; }
    }

    public sealed class RcDirtyLaunchOrder
    {
        [JsonPropertyName("format")]
        public string Format { get; set; }
        [JsonPropertyName("launch")]
        public RcDirtyLaunchItem[] Launch { get; set; }
        [JsonPropertyName("omitted")]
        public string[] Omitted { get; set; }
        [JsonPropertyName("gateCredit")]
        public bool GateCredit { get; set; }
        [JsonPropertyName("effectiveMode")]
        public string EffectiveMode { get; set; }
        [JsonPropertyName("trustTransition")]
        public bool TrustTransition { get; set; }
        [JsonPropertyName("shadow")]
        public bool Shadow { get; set; }
        [JsonPropertyName("activation")]
        public string Activation { get; set; }
        [JsonPropertyName("candidateSha")]
        public string CandidateSha { get; set; }
    }

    public static class RcDirtyOrder
    {
        static readonly Regex Sha40 = new Regex(@"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant);
        static readonly HashSet<string> Outcomes = new HashSet<string>(StringComparer.Ordinal)
        {
            "failed", "unexpected-skip", "interrupted", "not-run", "unproven", "passed", "expected-skip"
        };

        public static RcDirtyLaunchOrder Launch(object obligations, object attempts, object fixes, string repositoryRoot, string candidateSha)
        {
            var states = new Dictionary<string, State>(StringComparer.Ordinal);
            var omitted = new List<string>();
            foreach (var row in Rows(obligations))
            {
                var key = Text(RcPort.DirtyProp(row, "key"));
                if (string.IsNullOrWhiteSpace(key)) RcPort.Throw("invalid-obligation");
                var disposition = Text(RcPort.DirtyProp(row, "disposition"));
                if (disposition != "Dirty" && disposition != "Sample" && disposition != "Carry") RcPort.Throw("invalid-disposition");
                if (states.ContainsKey(key)) RcPort.Throw("duplicate-obligation");
                var invariant = NightlyTestsPlan.IsInvariantClass(Text(RcPort.DirtyProp(row, "className")));
                states.Add(key, new State { Key = key, Disposition = invariant ? "Dirty" : disposition, RepositoryInvariant = invariant });
            }
            foreach (var fix in Rows(fixes))
            {
                if (fix == null) continue;
                var fixKey = Text(RcPort.DirtyProp(fix, "key"));
                if (!states.TryGetValue(fixKey, out var state)) continue;
                state.Fixes.Add(Text(RcPort.DirtyProp(fix, "fixSha")));
            }
            var ordered = new List<AttemptRow>();
            foreach (var attempt in Rows(attempts))
            {
                if (attempt == null) continue;
                var sequence = RcPort.DirtyProp(attempt, "sequence");
                if (sequence == null) RcPort.Throw("invalid-attempt");
                ordered.Add(new AttemptRow
                {
                    Sequence = Sequence(sequence),
                    SourceSha = Text(RcPort.DirtyProp(attempt, "sourceSha")),
                    Attempt = attempt
                });
            }
            ordered = ordered.OrderBy(item => item.Sequence).ThenBy(item => item.SourceSha, StringComparer.Ordinal).ToList();
            var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var item in ordered)
            {
                var sha = item.SourceSha;
                foreach (var result in Rows(RcPort.DirtyProp(item.Attempt, "results")))
                {
                    if (result == null) continue;
                    var outcome = Text(RcPort.DirtyProp(result, "outcome"));
                    if (!Outcomes.Contains(outcome)) RcPort.Throw("invalid-outcome");
                    var resultKey = Text(RcPort.DirtyProp(result, "key"));
                    if (!states.TryGetValue(resultKey, out var state)) continue;
                    if (outcome == "failed" || outcome == "unexpected-skip")
                    {
                        state.Failed = true;
                        state.FailureSha = sha;
                        state.Interrupted = false;
                        state.InterruptSha = "";
                    }
                    else if (outcome == "interrupted")
                    {
                        if (!state.Failed)
                        {
                            state.Interrupted = true;
                            state.InterruptSha = sha;
                        }
                    }
                    else if (outcome == "passed" || outcome == "expected-skip")
                    {
                        state.SawGreen = true;
                        if (state.Failed && CanDischarge(repositoryRoot, state.FailureSha, sha, state.Fixes, cache))
                        {
                            state.Failed = false;
                            state.FailureSha = "";
                        }
                        if (state.Interrupted && !string.Equals(sha, state.InterruptSha, StringComparison.Ordinal))
                        {
                            state.Interrupted = false;
                            state.InterruptSha = "";
                        }
                    }
                }
            }
            var ranked = new List<Ranked>();
            foreach (var state in states.Values)
            {
                if (state.Disposition == "Carry")
                {
                    if (state.Failed || state.Interrupted) RcPort.Throw("carry-with-open-failure");
                    omitted.Add(state.Key);
                    continue;
                }
                string band;
                int rank;
                if (state.Failed) { band = "failed"; rank = 0; }
                else if (state.Interrupted) { band = "interrupted"; rank = 1; }
                else if (!state.SawGreen) { band = "not-run"; rank = 2; }
                else if (state.Disposition == "Dirty") { band = "changed"; rank = 3; }
                else { band = "sample"; rank = 4; }
                ranked.Add(new Ranked { Key = state.Key, Band = band, Disposition = state.Disposition, Rank = rank, RepositoryInvariant = state.RepositoryInvariant });
            }
            ranked = ranked.OrderByDescending(item => item.RepositoryInvariant).ThenBy(item => item.Rank).ThenBy(item => item.Key, StringComparer.Ordinal).ToList();
            var launch = new RcDirtyLaunchItem[ranked.Count];
            for (var i = 0; i < ranked.Count; i++)
            {
                launch[i] = new RcDirtyLaunchItem
                {
                    Key = ranked[i].Key,
                    Band = ranked[i].Band,
                    Disposition = ranked[i].Disposition
                };
            }
            omitted.Sort(StringComparer.Ordinal);
            return new RcDirtyLaunchOrder
            {
                Format = "antiphon-rc-dirty-order-v1",
                Launch = launch,
                Omitted = omitted.ToArray(),
                GateCredit = false,
                EffectiveMode = "Full",
                TrustTransition = false,
                Shadow = true,
                Activation = "off",
                CandidateSha = candidateSha ?? ""
            };
        }

        static bool CanDischarge(string root, string failureSha, string greenSha, List<string> fixes, Dictionary<string, bool> cache)
        {
            if (string.Equals(greenSha, failureSha, StringComparison.Ordinal)) return false;
            foreach (var fix in fixes)
            {
                if (string.IsNullOrWhiteSpace(fix)) continue;
                if (Contained(root, failureSha, fix, cache)) continue;
                if (Contained(root, greenSha, fix, cache)) return true;
            }
            return false;
        }

        static bool Contained(string root, string sourceSha, string fixSha, Dictionary<string, bool> cache)
        {
            if (string.IsNullOrWhiteSpace(root)) return false;
            if (sourceSha == null || !Sha40.IsMatch(sourceSha) || fixSha == null || !Sha40.IsMatch(fixSha))
                RcPort.Throw("repo-sha-unresolved");
            var key = sourceSha + "|" + fixSha;
            if (cache.TryGetValue(key, out var cached)) return cached;
            var ancestor = Git(root, "merge-base", "--is-ancestor", fixSha, sourceSha);
            bool contained;
            if (ancestor.Exit == 0) contained = true;
            else if (ancestor.Exit != 1) throw new InvalidOperationException("repo-sha-unresolved");
            else
            {
                var pattern = "^\\(cherry picked from commit " + fixSha + "\\)$";
                var log = Git(root, "log", "-1", "--format=%H", sourceSha, "-E", "--grep=" + pattern);
                if (log.Exit != 0) throw new InvalidOperationException("repo-sha-unresolved");
                var text = FirstLine(log.Stdout).Trim();
                contained = Sha40.IsMatch(text);
            }
            cache[key] = contained;
            return contained;
        }

        static GitResult Git(string root, params string[] args)
        {
            var start = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            start.Environment["GIT_TERMINAL_PROMPT"] = "0";
            using var process = Process.Start(start);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000))
            {
                try { process.Kill(true); } catch (Exception) { }
                try { process.WaitForExit(); } catch (Exception) { }
                RcPort.Throw("repo-sha-unresolved");
            }
            process.WaitForExit();
            _ = stderr.GetAwaiter().GetResult();
            return new GitResult { Exit = process.ExitCode, Stdout = stdout.GetAwaiter().GetResult() ?? "" };
        }

        static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var split = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return split.Length == 0 ? "" : split[0];
        }

        static List<object> Rows(object value)
        {
            if (value == null) return new List<object>();
            return RcPort.Unwrap(value);
        }

        static string Text(object value)
        {
            if (value == null) return "";
            if (value is string text) return text;
            if (value is Array array)
            {
                var parts = new string[array.Length];
                for (var i = 0; i < array.Length; i++) parts[i] = Text(array.GetValue(i));
                return string.Join(" ", parts);
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        static long Sequence(object value)
        {
            switch (value)
            {
                case long number: return number;
                case int number: return number;
                case short number: return number;
                case byte number: return number;
                case sbyte number: return number;
                case ushort number: return number;
                case uint number: return number;
                case ulong number when number <= long.MaxValue: return (long)number;
                case double number: return Convert.ToInt64(number);
                case float number: return Convert.ToInt64(number);
                case decimal number: return Convert.ToInt64(number);
                case string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                    return parsed;
                default:
                    RcPort.Throw("invalid-attempt");
                    return 0;
            }
        }

        sealed class State
        {
            public string Key;
            public string Disposition;
            public bool RepositoryInvariant;
            public bool Failed;
            public string FailureSha = "";
            public bool Interrupted;
            public string InterruptSha = "";
            public bool SawGreen;
            public List<string> Fixes = new List<string>();
        }

        sealed class AttemptRow
        {
            public long Sequence;
            public string SourceSha;
            public object Attempt;
        }

        sealed class Ranked
        {
            public string Key;
            public string Band;
            public string Disposition;
            public int Rank;
            public bool RepositoryInvariant;
        }

        sealed class GitResult
        {
            public int Exit;
            public string Stdout;
        }
    }
}
