#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Antiphon.Checkpoints
{
    // Shared by the checkpoint tool and run-checkpoint.ps1. Add-Type loads this file, so it stays
    // on the older C# shape: no nullable annotations, no file-scoped namespace, no collection expressions.
    // The reader has no cache. A kill can omit at most one torn final line, which the recorder flushes per line.
    public static class PhaseJournalDiagnostic
    {
        public const int LineLimit = 262144;
        public const int InFlightLimit = 20;

        public static bool IsAntiphonTestsProject(string project)
        {
            if (string.IsNullOrWhiteSpace(project))
                return false;
            var name = Path.GetFileName(project.Trim().TrimEnd('/', '\\'));
            return name.Equals("Antiphon.Tests", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Antiphon.Tests.csproj", StringComparison.OrdinalIgnoreCase);
        }

        public static void Enable(IDictionary<string, string> environment, string resultsDirectory, string rowId, string runId)
        {
            if (environment == null || string.IsNullOrWhiteSpace(resultsDirectory))
                return;
            var root = Path.GetFullPath(Path.Combine(resultsDirectory, "phase"));
            SetIfBlank(environment, "ANTIPHON_TEST_PHASE_ROOT", root);
            SetIfBlank(environment, "ANTIPHON_TEST_PHASE_RUN_ID", string.IsNullOrWhiteSpace(runId) ? "row" : runId.Trim());
            SetIfBlank(environment, "ANTIPHON_TEST_PHASE_GROUP_ID", string.IsNullOrWhiteSpace(rowId) ? "row" : rowId);
            SetIfBlank(environment, "ANTIPHON_TEST_PHASE_ATTEMPT", "1");
            SetIfBlank(environment, "ANTIPHON_TEST_PHASE_SUITE", "antiphon-tests");
            string configured;
            if (!environment.TryGetValue("ANTIPHON_TEST_PHASE_ROOT", out configured) || string.IsNullOrWhiteSpace(configured))
                return;
            try
            {
                Directory.CreateDirectory(configured);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        public static void EnableProcess(ProcessStartInfo start, string project, string resultsDirectory, string rowId, string runId)
        {
            if (start == null || !IsAntiphonTestsProject(project))
                return;
            Enable(start.Environment, resultsDirectory, rowId, runId);
        }

        public static string ConfiguredRoot(ProcessStartInfo start)
        {
            if (start == null)
                return null;
            string root;
            if (!start.Environment.TryGetValue("ANTIPHON_TEST_PHASE_ROOT", out root) || string.IsNullOrWhiteSpace(root))
                return null;
            return root;
        }

        public static string FormatFromEnvironment(IReadOnlyDictionary<string, string> environment, string rowId)
        {
            if (environment == null)
                return null;
            string root;
            if (!environment.TryGetValue("ANTIPHON_TEST_PHASE_ROOT", out root) || string.IsNullOrWhiteSpace(root))
                return null;
            return Format(root, rowId);
        }

        // Null when the phase directory was never created (the host did not launch). A directory with
        // no cases file is unavailable, not zero.
        public static string Format(string phaseRoot, string rowId)
        {
            if (string.IsNullOrWhiteSpace(phaseRoot) || !Directory.Exists(phaseRoot) || IsReparse(phaseRoot))
                return null;
            var files = new List<string>();
            Collect(phaseRoot, files);
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(phaseRoot, "chunk-*"))
                {
                    if (IsReparse(directory))
                        continue;
                    Collect(directory, files);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }

            var id = Token(rowId);
            if (id.Length == 0)
                id = "row";
            if (files.Count == 0)
                return "JOURNAL-DIAGNOSTIC " + id + " completed=unavailable pending=unavailable reason=journal-missing";

            files.Sort(StringComparer.Ordinal);
            var cases = new Dictionary<string, CaseState>(StringComparer.Ordinal);
            foreach (var file in files)
                ReadFile(file, cases);

            var completed = 0;
            var passed = 0;
            var failed = 0;
            var skipped = 0;
            var pending = new List<CaseState>();
            foreach (var entry in cases.Values)
            {
                if (!entry.Complete)
                {
                    if (entry.Started)
                        pending.Add(entry);
                    continue;
                }

                completed++;
                if (entry.Outcome == "Passed")
                    passed++;
                else if (entry.Outcome == "Failed" || entry.Outcome == "Error" || entry.Outcome == "Timeout" || entry.Outcome == "Aborted")
                    failed++;
                else if (entry.Outcome == "Skipped" || entry.Outcome == "NotExecuted")
                    skipped++;
            }

            pending.Sort(delegate (CaseState left, CaseState right) { return left.Order.CompareTo(right.Order); });
            var shown = new StringBuilder();
            var hidden = 0;
            for (var index = 0; index < pending.Count; index++)
            {
                if (index >= InFlightLimit)
                {
                    hidden++;
                    continue;
                }

                if (shown.Length > 0)
                    shown.Append(',');
                shown.Append(pending[index].Name);
            }

            var inFlight = shown.Length == 0 ? "none" : shown.ToString();
            if (hidden > 0)
                inFlight = inFlight + ",+" + hidden.ToString(CultureInfo.InvariantCulture);
            return "JOURNAL-DIAGNOSTIC " + id
                + " completed=" + completed.ToString(CultureInfo.InvariantCulture)
                + " pending=" + pending.Count.ToString(CultureInfo.InvariantCulture)
                + " outcome-passed=" + passed.ToString(CultureInfo.InvariantCulture)
                + " outcome-failed=" + failed.ToString(CultureInfo.InvariantCulture)
                + " outcome-skipped=" + skipped.ToString(CultureInfo.InvariantCulture)
                + " in-flight=" + inFlight;
        }

        static void SetIfBlank(IDictionary<string, string> environment, string key, string value)
        {
            string existing;
            if (environment.TryGetValue(key, out existing) && !string.IsNullOrWhiteSpace(existing))
                return;
            environment[key] = value;
        }

        static void Collect(string directory, List<string> files)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "cases-*.jsonl"))
                {
                    if (!IsReparse(file))
                        files.Add(file);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        static bool IsReparse(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return true;
            }
        }

        static void ReadFile(string path, Dictionary<string, CaseState> cases)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                        Apply(line, cases);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        static void Apply(string line, Dictionary<string, CaseState> cases)
        {
            if (line == null || line.Length == 0 || line.Length > LineLimit)
                return;
            string kind;
            string testId;
            string className;
            string method;
            string outcome;
            if (!TryRead(line, out kind, out testId, out className, out method, out outcome))
                return;
            if (kind != "start" && kind != "complete")
                return;
            if (string.IsNullOrEmpty(testId))
                return;
            CaseState state;
            if (!cases.TryGetValue(testId, out state))
            {
                state = new CaseState();
                state.Order = cases.Count;
                cases.Add(testId, state);
            }

            if (kind == "start")
            {
                if (state.Complete || state.Started)
                    return;
                state.Started = true;
                state.Order = cases.Count;
                state.Name = Display(className, method, testId);
                return;
            }

            state.Complete = true;
            state.Outcome = outcome ?? "";
            if (string.IsNullOrEmpty(state.Name))
                state.Name = Display(className, method, testId);
        }

        static string Display(string className, string method, string testId)
        {
            var name = !string.IsNullOrEmpty(className) && !string.IsNullOrEmpty(method)
                ? className + "." + method
                : !string.IsNullOrEmpty(className) ? className : testId;
            var token = Token(name);
            return token.Length == 0 ? "unknown" : token;
        }

        static string Token(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            var builder = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                var ch = value[index];
                if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n' || ch == '=' || ch == ',')
                    continue;
                builder.Append(ch);
            }

            return builder.ToString();
        }

        static bool TryRead(string line, out string kind, out string testId, out string className, out string method, out string outcome)
        {
            kind = null;
            testId = null;
            className = null;
            method = null;
            outcome = null;
            var index = 0;
            Skip(line, ref index);
            if (index >= line.Length || line[index] != '{')
                return false;
            index++;
            Skip(line, ref index);
            if (index < line.Length && line[index] == '}')
            {
                index++;
                Skip(line, ref index);
                return index == line.Length;
            }

            while (index < line.Length)
            {
                Skip(line, ref index);
                string key;
                if (!TryString(line, ref index, out key))
                    return false;
                Skip(line, ref index);
                if (index >= line.Length || line[index] != ':')
                    return false;
                index++;
                Skip(line, ref index);
                if (index >= line.Length)
                    return false;
                if (line[index] == '"')
                {
                    string value;
                    if (!TryString(line, ref index, out value))
                        return false;
                    if (key == "recordKind")
                        kind = value;
                    else if (key == "testId")
                        testId = value;
                    else if (key == "className")
                        className = value;
                    else if (key == "method")
                        method = value;
                    else if (key == "outcome")
                        outcome = value;
                }
                else if (!SkipValue(line, ref index))
                {
                    return false;
                }

                Skip(line, ref index);
                if (index >= line.Length)
                    return false;
                if (line[index] == ',')
                {
                    index++;
                    continue;
                }

                if (line[index] != '}')
                    return false;
                index++;
                Skip(line, ref index);
                return index == line.Length;
            }

            return false;
        }

        static bool SkipValue(string line, ref int index)
        {
            if (index >= line.Length)
                return false;
            var ch = line[index];
            if (ch == '"')
            {
                string ignored;
                return TryString(line, ref index, out ignored);
            }

            if (ch == '{' || ch == '[')
                return SkipContainer(line, ref index, ch, ch == '{' ? '}' : ']');
            if (Match(line, ref index, "true") || Match(line, ref index, "false") || Match(line, ref index, "null"))
                return true;
            if (ch == '-')
            {
                index++;
                if (index >= line.Length || line[index] < '0' || line[index] > '9')
                    return false;
            }
            else if (ch < '0' || ch > '9')
            {
                return false;
            }

            while (index < line.Length && line[index] >= '0' && line[index] <= '9')
                index++;
            if (index < line.Length && line[index] == '.')
            {
                index++;
                if (index >= line.Length || line[index] < '0' || line[index] > '9')
                    return false;
                while (index < line.Length && line[index] >= '0' && line[index] <= '9')
                    index++;
            }

            if (index < line.Length && (line[index] == 'e' || line[index] == 'E'))
            {
                index++;
                if (index < line.Length && (line[index] == '+' || line[index] == '-'))
                    index++;
                if (index >= line.Length || line[index] < '0' || line[index] > '9')
                    return false;
                while (index < line.Length && line[index] >= '0' && line[index] <= '9')
                    index++;
            }

            return true;
        }

        static bool SkipContainer(string line, ref int index, char open, char close)
        {
            var depth = 0;
            while (index < line.Length)
            {
                var ch = line[index];
                if (ch == '"')
                {
                    string ignored;
                    if (!TryString(line, ref index, out ignored))
                        return false;
                    continue;
                }

                index++;
                if (ch == open)
                    depth++;
                else if (ch == close)
                {
                    depth--;
                    if (depth == 0)
                        return true;
                }
            }

            return false;
        }

        static bool TryString(string line, ref int index, out string value)
        {
            value = null;
            if (index >= line.Length || line[index] != '"')
                return false;
            index++;
            var builder = new StringBuilder();
            while (index < line.Length)
            {
                var ch = line[index++];
                if (ch == '"')
                {
                    value = builder.ToString();
                    return true;
                }

                if (ch == '\\')
                {
                    if (index >= line.Length)
                        return false;
                    var escaped = line[index++];
                    if (escaped == '"' || escaped == '\\' || escaped == '/')
                        builder.Append(escaped);
                    else if (escaped == 'b')
                        builder.Append('\b');
                    else if (escaped == 'f')
                        builder.Append('\f');
                    else if (escaped == 'n')
                        builder.Append('\n');
                    else if (escaped == 'r')
                        builder.Append('\r');
                    else if (escaped == 't')
                        builder.Append('\t');
                    else if (escaped == 'u')
                    {
                        if (index + 4 > line.Length)
                            return false;
                        var code = 0;
                        for (var hex = 0; hex < 4; hex++)
                        {
                            var digit = Hex(line[index++]);
                            if (digit < 0)
                                return false;
                            code = (code << 4) + digit;
                        }

                        builder.Append((char)code);
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (ch < 0x20)
                {
                    return false;
                }
                else
                {
                    builder.Append(ch);
                }
            }

            return false;
        }

        static int Hex(char ch)
        {
            if (ch >= '0' && ch <= '9')
                return ch - '0';
            if (ch >= 'a' && ch <= 'f')
                return ch - 'a' + 10;
            if (ch >= 'A' && ch <= 'F')
                return ch - 'A' + 10;
            return -1;
        }

        static bool Match(string line, ref int index, string token)
        {
            if (index + token.Length > line.Length)
                return false;
            for (var offset = 0; offset < token.Length; offset++)
            {
                if (line[index + offset] != token[offset])
                    return false;
            }

            index += token.Length;
            return true;
        }

        static void Skip(string line, ref int index)
        {
            while (index < line.Length)
            {
                var ch = line[index];
                if (ch != ' ' && ch != '\t' && ch != '\r' && ch != '\n')
                    return;
                index++;
            }
        }

        sealed class CaseState
        {
            public bool Started;
            public bool Complete;
            public string Outcome;
            public string Name;
            public int Order;
        }
    }
}
