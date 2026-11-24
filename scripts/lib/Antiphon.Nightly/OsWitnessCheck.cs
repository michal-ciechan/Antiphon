// CARD-1526: the OS-branch witness declaration lint of docs/testing-and-build.md#os-branch-witnesses. It replaced
// scripts/check-os-witnesses.ps1, deleted 2026-11-24 (unit 35 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md);
// the CLI runs it as
//
//   antiphon-scripts check-os-witnesses --base-ref <full-sha> --head-ref <full-sha> --evidence-path <json> --plan <repo-relative-plan.md>
//       [--repository <dir>]
//
// (scripts/antiphon check-os-witnesses on sh, scripts\antiphon.cmd check-os-witnesses on Windows). It is bounded: it never
// executes evidence commands, authenticates a receipt or certifies coverage. Both refs must be full commit SHAs; the checker
// reads Git objects at those revisions only (the plan at head, every inventoried and witness path), so unstaged source cannot
// satisfy it. The evidence is a version-1 JSON object (baseSha, headSha, inspectedPaths, witnesses, restartImpact) whose keys
// are matched exactly; its values compare as the script's -eq did (ignoring case) except the SHAs and paths, which compare
// exactly.
//
// Output (host.Out): "OS-WITNESS base=<sha> head=<sha>", then in order of discovery "OBLIGATION <message>" for each missing or
// contradictory obligation, "CHANGED <head|removed> <path>" for each changed path, "CLASSIFY unrecognized <path>",
// "CANDIDATE <base|head> <path>:<line>" for each lexical OS-check candidate and "NATIVE pending <context>"; last either
// "OS-WITNESS complete syntax/inventory; execution and runtime identity require Review" (exit 0), "OS-WITNESS obligations=<n>"
// (exit 1) or "OS-WITNESS invalid input: <reason>" (exit 2: an invalid ref, an unresolved commit, unreadable or malformed
// evidence, a SHA mismatch, an invalid plan path, a plan without "### Checkpoints" CP-<digits> rows, an ambiguous or
// unterminated literal in a test source, a git failure or timeout). Git runs through the OsWitnessGit seam (production:
// NativeGit, 15 s per call). Usage errors are NightlyUsageException (the CLI's exit 2). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The check-os-witnesses command's options (check-os-witnesses.ps1 -BaseRef -HeadRef -EvidencePath -Plan -Repository).</summary>
    public sealed class OsWitnessCheckOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "check-os-witnesses --base-ref <full-sha> --head-ref <full-sha> --evidence-path <json> --plan <repo-relative-plan.md> [--repository <dir>]";

        /// <summary>The base commit (a full SHA).</summary>
        public string BaseRef { get; init; } = "";

        /// <summary>The head commit (a full SHA).</summary>
        public string HeadRef { get; init; } = "";

        /// <summary>The declaration JSON (normally an ignored file).</summary>
        public string EvidencePath { get; init; } = "";

        /// <summary>The plan, repository-relative, read at head.</summary>
        public string Plan { get; init; } = "";

        /// <summary>The checkout git runs in (default: the working directory).</summary>
        public string Repository { get; init; } = ".";

        /// <summary>The flags after the command name; the four inputs are required, --repository defaults to ".".</summary>
        public static OsWitnessCheckOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--base-ref", "--head-ref", "--evidence-path", "--plan", "--repository" }, Array.Empty<string>());
            return new OsWitnessCheckOptions
            {
                BaseRef = flags.Required("--base-ref"),
                HeadRef = flags.Required("--head-ref"),
                EvidencePath = flags.Required("--evidence-path"),
                Plan = flags.Required("--plan"),
                Repository = flags.Value("--repository", "") is { Length: > 0 } repository ? repository : ".",
            };
        }
    }

    /// <summary>One git call: its exit code, its stdout exactly as written and its stderr.</summary>
    public sealed record OsWitnessGitResult(int ExitCode, string Output, string Error);

    /// <summary>Runs git -C repository with these arguments (production: OsWitnessCheck.NativeGit).</summary>
    public delegate OsWitnessGitResult OsWitnessGit(string repository, IReadOnlyList<string> arguments);

    /// <summary>The OS-branch witness declaration lint (check-os-witnesses.ps1).</summary>
    public static class OsWitnessCheck
    {
        /// <summary>Each git call's bound.</summary>
        public static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(15);

        private const string DocPattern = @"(?i)\.(md|rst|txt)$";
        private const string SourcePattern = @"(?i)\.(cs|csx|ps1|psm1|psd1|sh|bash|zsh|js|jsx|mjs|cjs|ts|tsx)$";
        private const string StartupPattern = @"^(dev-aspire\.ps1|scripts/(restart-apphost\.ps1|build-slot\.ps1|apphost-common\.ps1|lib/.*)|Antiphon\.AppHost/.*)$";

        // Bounded lexical candidates, including unchanged guards in changed files and named helpers; matched ignoring case.
        private static readonly Regex OsPattern = new Regex(
            @"OperatingSystem\.Is\w+\s*\(|RuntimeInformation\.IsOSPlatform\s*\(|Environment\.OSVersion\.Platform|\$(?:IsWindows|IsLinux|IsMacOS)\b|\$env:OS\b|#\s*(?:if|elif|ifdef|ifndef).*\b(?:WINDOWS|WIN32|_WIN32|LINUX|__linux__|OSX|MACOS|__APPLE__)\b|\buname\b|\$\{?OSTYPE\b|process\.platform\b|\bos\.platform\s*\(",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// The production git: git (found on PATH) -C repository and the arguments, no shell, both streams read as UTF-8 to the end.
        /// A call that outlives GitTimeout is killed with its tree and throws "git timeout".
        /// </summary>
        public static OsWitnessGitResult NativeGit(string repository, IReadOnlyList<string> arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom,
            };
            start.ArgumentList.Add("-C");
            start.ArgumentList.Add(repository ?? ".");
            foreach (var argument in arguments ?? Array.Empty<string>()) start.ArgumentList.Add(argument);
            using var child = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit((int)GitTimeout.TotalMilliseconds))
            {
                try { child.Kill(true); } catch (InvalidOperationException) { }
                child.WaitForExit();
                throw new TimeoutException("git timeout");
            }
            child.WaitForExit();
            return new OsWitnessGitResult(child.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }

        /// <summary>Lints the declaration; see the header for the output and the exit codes (0, 1, 2).</summary>
        public static int Run(OsWitnessCheckOptions options, NightlyHost host, IFileSystem files, OsWitnessGit git)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (git == null) throw new ArgumentNullException(nameof(git));
            var checker = new Checker(host.Out, git, options.Repository);
            try
            {
                return checker.Check(options, files);
            }
            catch (Exception e)
            {
                host.Out.WriteLine("OS-WITNESS invalid input: " + e.Message);
                return 2;
            }
        }

        /// <summary>A malformed input the checker refuses as a whole (the script's throw; exit 2).</summary>
        private sealed class InvalidInputException : Exception
        {
            public InvalidInputException(string message) : base(message) { }
        }

        // ---- the lexical view of a test source ----

        /// <summary>
        /// Test-Lexemes: Code keeps string literals and blanks comments and other code; Tokens blanks literals too. Offsets and line
        /// breaks are kept. A partial or ambiguous view is never used to accept a witness: it throws instead.
        /// </summary>
        internal static (string Code, string Tokens) Lexemes(string text, string path)
        {
            text ??= "";
            var extension = System.IO.Path.GetExtension(path ?? "") ?? "";
            var powershell = new[] { ".ps1", ".psm1", ".psd1" }.Contains(extension, StringComparer.OrdinalIgnoreCase);
            var code = text.ToCharArray();
            var tokens = text.ToCharArray();
            var i = 0;
            while (i < text.Length)
            {
                var start = i;
                var literal = false;
                var ch = text[i];
                var pair = i + 1 < text.Length ? text.Substring(i, 2) : "";
                if (pair == "/*" || pair == "<#")
                {
                    var kind = pair;
                    var depth = 1;
                    i += 2;
                    while (i < text.Length && depth > 0)
                    {
                        pair = i + 1 < text.Length ? text.Substring(i, 2) : "";
                        if (kind == "<#" && pair == "<#") { depth++; i += 2; }
                        else if ((kind == "<#" && pair == "#>") || (kind == "/*" && pair == "*/")) { depth--; i += 2; }
                        else i++;
                    }
                    if (depth != 0) throw new InvalidInputException("unterminated " + kind + " block comment in test source");
                }
                else if (pair == "//" || ch == '#')
                {
                    while (i < text.Length && text[i] != '\r' && text[i] != '\n') i++;
                }
                else
                {
                    if ((!powershell && (pair == "$\"" || pair == "$@" || pair == "@$")) || (powershell && (pair == "@\"" || pair == "@'")))
                        throw new InvalidInputException("ambiguous string literal: interpolated C# and PowerShell here-strings are unsupported");
                    var verbatim = !powershell && pair == "@\"";
                    if (verbatim) { i++; ch = text[i]; }
                    if (ch != '"' && ch != '\'') { i++; continue; }
                    if (ch == '"' && i + 2 < text.Length && text.Substring(i, 3) == "\"\"\"")
                        throw new InvalidInputException("ambiguous string literal: C# raw strings are unsupported");
                    literal = true;
                    var quote = ch;
                    var closed = false;
                    i++;
                    while (i < text.Length)
                    {
                        ch = text[i];
                        if (ch == quote)
                        {
                            if ((verbatim || (powershell && quote == '\'')) && i + 1 < text.Length && text[i + 1] == quote) { i += 2; continue; }
                            i++;
                            closed = true;
                            break;
                        }
                        if ((!powershell && !verbatim && ch == '\\') || (powershell && quote == '"' && ch == '`'))
                        {
                            if (i + 1 >= text.Length) break;
                            i += 2;
                            continue;
                        }
                        if (powershell && quote == '"' && ch == '$' && i + 1 < text.Length && text[i + 1] == '(')
                            throw new InvalidInputException("ambiguous string literal: PowerShell subexpressions are unsupported");
                        if (!powershell && !verbatim && (ch == '\r' || ch == '\n')) break;
                        i++;
                    }
                    if (!closed) throw new InvalidInputException("unterminated string literal in test source");
                }
                for (var j = start; j < i; j++)
                {
                    if (text[j] == '\r' || text[j] == '\n') continue;
                    tokens[j] = ' ';
                    if (!literal) code[j] = ' ';
                }
            }
            return (new string(code), new string(tokens));
        }

        /// <summary>Code-Fragment: the fragment (whitespace runs flexible) appears as code, starting outside a string.</summary>
        internal static bool CodeFragment((string Code, string Tokens) lexemes, string fragment)
        {
            if (string.IsNullOrWhiteSpace(fragment)) return false;
            var pattern = @"(?<![\w])" + Regex.Escape(fragment.Trim()).Replace(@"\ ", @"\s+") + @"(?![\w])";
            foreach (Match match in Regex.Matches(lexemes.Code, pattern))
                if (!char.IsWhiteSpace(lexemes.Tokens[match.Index])) return true;
            return false;
        }

        /// <summary>Test-Declaration: an attributed C# test method, a PowerShell function or a named It/Run-Case/Test-Case case.</summary>
        internal static bool Declaration((string Code, string Tokens) lexemes, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var exact = Regex.Escape(name);
            var method = @"(?m)^\s*(?:\[[^\]\r\n]+\]\s*)+\s*(?:(?:public|private|protected|internal|static|async|virtual|override)\s+)*(?:[\w.<>,?\[\]]+\s+)" + exact + @"\s*\([^;{}]*\)\s*(?:\{|=>)";
            var function = @"(?m)^\s*function\s+" + exact + @"\s*(?:\([^;{}]*\)\s*)?\{";
            foreach (Match match in Regex.Matches(lexemes.Tokens, method))
                if (Regex.IsMatch(match.Value, @"\[(?:[\w]+\.)*(?:Test|Fact|Theory|TestMethod|DataTestMethod)(?:Attribute)?\s*(?:\(|\])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    return true;
            if (Regex.IsMatch(lexemes.Tokens, function)) return true;
            var namedCase = @"(?m)^\s*(?<keyword>It|Run-Case|Test-Case)\s+(?:-Name\s+)?(?<quote>['""])" + exact + @"\k<quote>(?=\s|$)";
            foreach (Match match in Regex.Matches(lexemes.Code, namedCase))
                if (!char.IsWhiteSpace(lexemes.Tokens[match.Groups["keyword"].Index])) return true;
            return false;
        }

        // ---- JSON values, read as the script's hashtables were (keys exact) ----

        private static JsonNode Field(JsonNode record, string name) =>
            record is JsonObject value && value.TryGetPropertyValue(name, out var node) ? node : null;

        private static bool Has(JsonNode record, string name) => record is JsonObject value && value.ContainsKey(name);

        private static bool IsString(JsonNode node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.String;

        private static bool IsBool(JsonNode node, out bool flag)
        {
            flag = false;
            if (node is not JsonValue value) return false;
            var kind = value.GetValueKind();
            if (kind != JsonValueKind.True && kind != JsonValueKind.False) return false;
            flag = kind == JsonValueKind.True;
            return true;
        }

        // A scalar's text as a [string] cast gave it: null for JSON null, an object or an array.
        private static string Scalar(JsonNode node)
        {
            if (node is not JsonValue value) return null;
            switch (value.GetValueKind())
            {
                case JsonValueKind.String: return value.GetValue<string>();
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                case JsonValueKind.Number:
                    var element = value.GetValue<JsonElement>();
                    if (element.TryGetInt64(out var whole)) return whole.ToString(CultureInfo.InvariantCulture);
                    return element.GetDouble().ToString(CultureInfo.InvariantCulture);
                default: return null;
            }
        }

        private static string Text(JsonNode node) => Scalar(node) ?? "";

        // -eq / -in against literal names: the value's text, ignoring case.
        private static bool Is(JsonNode node, string expected) => string.Equals(Scalar(node), expected, StringComparison.OrdinalIgnoreCase);

        private static bool IsAny(JsonNode node, params string[] expected) => expected.Any(name => Is(node, name));

        // PowerShell truthiness of a value: null, "", 0 and false are false.
        private static bool Truthy(JsonNode node)
        {
            if (node == null) return false;
            if (node is JsonArray array) return array.Count > 0;
            if (node is JsonObject) return true;
            var value = (JsonValue)node;
            switch (value.GetValueKind())
            {
                case JsonValueKind.String: return value.GetValue<string>().Length > 0;
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Number: return value.GetValue<JsonElement>().GetDouble() != 0;
                default: return false;
            }
        }

        private static bool IsVersionOne(JsonNode node) =>
            node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.GetValue<JsonElement>().GetDouble() == 1;

        private static bool ValidPath(string path) =>
            !string.IsNullOrEmpty(path) && !Regex.IsMatch(path, @"(^/|\\|(^|/)\.\.?(/|$)|[\x00-\x1f])") && !Regex.IsMatch(path, "^[A-Za-z]:");

        /// <summary>One lint run's state: the obligations found, the blob cache and the resolved head.</summary>
        private sealed class Checker
        {
            private readonly System.IO.TextWriter _out;
            private readonly OsWitnessGit _git;
            private readonly string _repository;
            private readonly List<string> _errors = new List<string>();
            private readonly Dictionary<string, string> _blobs = new Dictionary<string, string>(StringComparer.Ordinal);
            private string _head = "";

            public Checker(System.IO.TextWriter output, OsWitnessGit git, string repository)
            {
                _out = output;
                _git = git;
                _repository = string.IsNullOrEmpty(repository) ? "." : repository;
            }

            private void Fail(string message)
            {
                _errors.Add(message);
                _out.WriteLine("OBLIGATION " + message);
            }

            private void Need(JsonNode record, string[] fields, string context)
            {
                foreach (var field in fields)
                {
                    var value = Field(record, field);
                    if (record is not JsonObject || !IsString(value) || string.IsNullOrWhiteSpace(value.GetValue<string>()))
                        Fail(context + " missing " + field);
                }
            }

            private string Git(params string[] arguments)
            {
                var result = _git(_repository, arguments);
                if (result.ExitCode != 0) throw new InvalidInputException("git failed: " + result.Error);
                return result.Output ?? "";
            }

            private string Blob(string sha, string path)
            {
                if (!ValidPath(path))
                {
                    Fail("invalid path: " + path);
                    return null;
                }
                var key = sha + ":" + path;
                if (_blobs.TryGetValue(key, out var cached)) return cached;
                var entry = Git("--literal-pathspecs", "ls-tree", "-z", sha, "--", path);
                if (entry.Length == 0 || !Regex.IsMatch(entry, "^100(644|755) blob ", RegexOptions.IgnoreCase))
                {
                    Fail("path missing or not regular: " + path + " at " + sha);
                    return null;
                }
                var value = Git("cat-file", "blob", key);
                _blobs[key] = value;
                return value;
            }

            private void Native(JsonNode record, string context, string os, bool required)
            {
                if (record is not JsonObject)
                {
                    Fail(context + " missing native obligation");
                    return;
                }
                if (Is(Field(record, "status"), "not-required"))
                {
                    Need(record, new[] { "reason" }, context);
                    if (required) Fail(context + " native obligation required");
                    return;
                }
                Need(record, new[] { "status", "targetOS", "sourceSha", "command" }, context);
                if (!string.Equals(Scalar(Field(record, "targetOS")), os, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Text(Field(record, "sourceSha")), _head, StringComparison.Ordinal))
                    Fail(context + " native OS/source mismatch");
                var status = Field(record, "status");
                if (Is(status, "pending"))
                {
                    Need(record, new[] { "reason" }, context);
                    _out.WriteLine("NATIVE pending " + context);
                }
                else if (Is(status, "recorded"))
                {
                    Need(record, new[] { "receiptReference", "executionMode" }, context);
                    if (!Is(Field(record, "executionMode"), "native")) Fail(context + " native receipt requires native execution");
                }
                else Fail(context + " invalid native status");
            }

            public int Check(OsWitnessCheckOptions options, IFileSystem files)
            {
                foreach (var reference in new[] { options.BaseRef, options.HeadRef })
                    if (!Regex.IsMatch(reference ?? "", "^(?:[a-fA-F0-9]{40}|[a-fA-F0-9]{64})$"))
                        throw new InvalidInputException("full commit SHAs required");
                string baseSha;
                try
                {
                    baseSha = Git("rev-parse", "--verify", options.BaseRef + "^{commit}").Trim();
                    _head = Git("rev-parse", "--verify", options.HeadRef + "^{commit}").Trim();
                }
                catch (Exception e)
                {
                    throw new InvalidInputException("unresolved commit: " + e.Message);
                }
                _out.WriteLine("OS-WITNESS base=" + baseSha + " head=" + _head);

                var evidence = JsonNode.Parse(files.File.ReadAllText(options.EvidencePath));
                if (evidence is not JsonObject || !IsVersionOne(Field(evidence, "version"))) throw new InvalidInputException("invalid version/object");
                if (!string.Equals(Text(Field(evidence, "baseSha")), baseSha, StringComparison.Ordinal)
                    || !string.Equals(Text(Field(evidence, "headSha")), _head, StringComparison.Ordinal))
                    throw new InvalidInputException("SHA mismatch");
                foreach (var name in new[] { "inspectedPaths", "witnesses" })
                {
                    if (Field(evidence, name) is not JsonArray records) throw new InvalidInputException("invalid array: " + name);
                    foreach (var record in records)
                        if (record is not JsonObject) throw new InvalidInputException("invalid record: " + name);
                }
                if (Field(evidence, "restartImpact") is not JsonObject restart) throw new InvalidInputException("invalid restartImpact");
                var inspected = (JsonArray)Field(evidence, "inspectedPaths");
                var witnessRecords = (JsonArray)Field(evidence, "witnesses");

                var planText = Blob(_head, options.Plan);
                if (planText == null) throw new InvalidInputException("invalid plan path");
                var section = Regex.Match(planText, @"(?ms)^### Checkpoints\s*\r?\n(?<body>.*?)(?=^#{1,3} |\z)");
                var checkpointIds = Regex.Matches(section.Groups["body"].Value, @"(?m)^\|\s*(CP-\d+)\s*\|").Select(match => match.Groups[1].Value).ToList();
                if (checkpointIds.Count == 0) throw new InvalidInputException("plan has no checkpoint rows");

                // -z retains whitespace and both rename identities; no shell parsing of paths.
                var tokens = Git("diff", "--name-status", "-z", "--find-renames", baseSha, _head, "--").Split('\0');
                string Token(int index) => index < tokens.Length ? tokens[index] : throw new InvalidInputException("malformed git diff output");
                var changedOrder = new List<string>();
                var changed = new Dictionary<string, string>(StringComparer.Ordinal);
                void Changed(string path, string state)
                {
                    if (!changed.ContainsKey(path)) changedOrder.Add(path);
                    changed[path] = state;
                }
                for (var i = 0; i < tokens.Length && tokens[i].Length > 0;)
                {
                    var status = Token(i++);
                    var path = Token(i++);
                    if (Regex.IsMatch(status, "^[RC]", RegexOptions.IgnoreCase))
                    {
                        Changed(path, "removed");
                        Changed(Token(i++), "head");
                    }
                    else Changed(path, string.Equals(status, "D", StringComparison.OrdinalIgnoreCase) ? "removed" : "head");
                }

                var inventoryOrder = new List<string>();
                var inventory = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
                foreach (JsonObject entry in inspected)
                {
                    Need(entry, new[] { "path", "disposition", "reason" }, "inventory");
                    var path = Text(Field(entry, "path"));
                    if (!ValidPath(path)) { Fail("invalid inventory path: " + path); continue; }
                    if (inventory.ContainsKey(path)) { Fail("duplicate inventory: " + path); continue; }
                    inventoryOrder.Add(path);
                    inventory[path] = entry;
                    if (!IsAny(Field(entry, "disposition"), "os-gated", "portable", "test-only", "removed")) Fail("invalid disposition: " + path);
                    var removed = changed.TryGetValue(path, out var state) && state == "removed";
                    if (Is(Field(entry, "disposition"), "removed") != removed) Fail("removed disposition mismatch: " + path);
                    Blob(removed ? baseSha : _head, path);
                    if (Field(entry, "helpers") is not JsonArray || Field(entry, "candidates") is not JsonArray)
                        throw new InvalidInputException("invalid helpers/candidates arrays: " + path);
                }
                foreach (var path in changedOrder)
                {
                    _out.WriteLine("CHANGED " + changed[path] + " " + path);
                    var documentation = Regex.IsMatch(path, DocPattern);
                    if (!documentation && !inventory.ContainsKey(path)) Fail("inventory missing: " + path);
                    if (!documentation && !Regex.IsMatch(path, SourcePattern)) _out.WriteLine("CLASSIFY unrecognized " + path);
                }

                var witnesses = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
                foreach (JsonObject w in witnessRecords)
                {
                    Need(w, new[] { "id", "sourcePath", "branchAnchor", "targetOS", "witnessKind", "productionType", "factory", "testPath", "testCase", "setup", "outcome", "executionMode", "checkpointId" }, "witness");
                    if (!Truthy(Field(w, "id"))) continue;
                    var id = Text(Field(w, "id"));
                    if (witnesses.ContainsKey(id)) { Fail("duplicate witness: " + id); continue; }
                    witnesses[id] = w;
                    if (!Is(Field(w, "witnessKind"), "real-type")) Fail(id + " real-type required");
                    if (!IsAny(Field(w, "targetOS"), "Windows", "Linux", "macOS", "FreeBSD", "Android", "iOS", "Browser", "Other")) Fail(id + " invalid targetOS");
                    var sourcePath = Text(Field(w, "sourcePath"));
                    if (!inventory.TryGetValue(sourcePath, out var sourceEntry) || !Is(Field(sourceEntry, "disposition"), "os-gated"))
                        Fail(id + " witness requires os-gated inventory");
                    var source = Blob(_head, sourcePath);
                    var testPath = Text(Field(w, "testPath"));
                    var test = Lexemes(Blob(_head, testPath), testPath);
                    if (!Declaration(test, Text(Field(w, "testCase")))) Fail(id + " testCase declaration missing: " + Text(Field(w, "testCase")));
                    foreach (var field in new[] { "productionType", "factory" })
                        if (!CodeFragment(test, Text(Field(w, field)))) Fail(id + " " + field + " missing from test code");
                    if (!string.IsNullOrEmpty(source) && Truthy(Field(w, "branchAnchor")) && !source.Contains(Text(Field(w, "branchAnchor")), StringComparison.Ordinal))
                        Fail(id + " branch anchor missing");
                    if (!checkpointIds.Contains(Scalar(Field(w, "checkpointId")) ?? "\0", StringComparer.OrdinalIgnoreCase)) Fail(id + " checkpoint missing");
                    if (!IsAny(Field(w, "executionMode"), "portable-forced", "native")) Fail(id + " invalid execution mode");
                    Native(Field(w, "nativeObligation"), id, Scalar(Field(w, "targetOS")), Is(Field(w, "executionMode"), "native"));
                    if (Field(w, "input") is not JsonObject input) { Fail(id + " missing input"); continue; }
                    if (Is(Field(input, "kind"), "optional-config"))
                    {
                        Need(input, new[] { "key" }, id + " input");
                        if (Field(input, "cases") is not JsonArray cases) throw new InvalidInputException("invalid input cases");
                        var states = cases.Select(item => item is JsonObject ? Field(item, "state") : null).ToList();
                        if (!states.Any(s => Is(s, "absent")) || !states.Any(s => Is(s, "empty")) || !states.Any(s => Is(s, "value")))
                            Fail(id + " input needs absent, empty and value");
                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var item in cases)
                        {
                            if (item is not JsonObject inputCase) throw new InvalidInputException("invalid input case");
                            Need(inputCase, new[] { "state", "setupAssertion" }, id + " input");
                            var stateNode = Field(inputCase, "state");
                            if (!CodeFragment(test, Text(Field(inputCase, "setupAssertion")))) Fail(id + " " + Text(stateNode) + " setupAssertion missing from test code");
                            var hasValue = Has(inputCase, "value");
                            var caseValue = hasValue ? Field(inputCase, "value") : null;
                            if (!seen.Add(Text(stateNode) + ":" + Text(caseValue))) Fail(id + " duplicate input case");
                            var present = IsBool(Field(inputCase, "keyPresent"), out var keyPresent);
                            if (!present) Fail(id + " keyPresent must be boolean");
                            if (Is(stateNode, "absent"))
                            {
                                if (!present || keyPresent || hasValue) Fail(id + " absent requires removal and no value");
                            }
                            else if (Is(stateNode, "empty"))
                            {
                                if (!present || !keyPresent || !IsString(caseValue) || caseValue.GetValue<string>().Length != 0) Fail(id + " empty requires present empty value");
                            }
                            else if (Is(stateNode, "value"))
                            {
                                if (!present || !keyPresent || !IsString(caseValue) || caseValue.GetValue<string>().Length == 0) Fail(id + " value requires present nonempty value");
                            }
                            else Fail(id + " invalid input state");
                        }
                    }
                    else if (Is(Field(input, "kind"), "other")) Need(input, new[] { "description", "setupAssertion" }, id + " input");
                    else Fail(id + " invalid input kind");
                }

                foreach (var path in inventoryOrder)
                {
                    var entry = inventory[path];
                    foreach (var helper in (JsonArray)Field(entry, "helpers"))
                        if (!inventory.ContainsKey(Text(helper))) Fail("helper not inspected: " + Text(helper));
                    if (Is(Field(entry, "disposition"), "os-gated")
                        && !witnessRecords.Any(w => string.Equals(Scalar(Field(w, "sourcePath")), path, StringComparison.Ordinal)))
                        Fail("missing witness: " + path);
                    var actualOrder = new List<string>();
                    var actual = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var revision in new[] { "base", "head" })
                    {
                        var sha = revision == "base" ? baseSha : _head;
                        if (Git("--literal-pathspecs", "ls-tree", "-z", sha, "--", path).Length == 0) continue;
                        if (!Regex.IsMatch(path, SourcePattern)) continue;
                        var lines = (Blob(sha, path) ?? "").Split('\n');
                        for (var line = 0; line < lines.Length; line++)
                        {
                            if (!OsPattern.IsMatch(lines[line])) continue;
                            var key = revision + ":" + (line + 1).ToString(CultureInfo.InvariantCulture);
                            if (actual.Add(key)) actualOrder.Add(key);
                            _out.WriteLine("CANDIDATE " + revision + " " + path + ":" + (line + 1).ToString(CultureInfo.InvariantCulture));
                        }
                    }
                    var declared = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var item in (JsonArray)Field(entry, "candidates"))
                    {
                        if (item is not JsonObject candidate) throw new InvalidInputException("invalid candidate record");
                        var witnessId = Has(candidate, "witnessId") ? Field(candidate, "witnessId") : null;
                        var key = Text(Field(candidate, "revision")) + ":" + Text(Field(candidate, "line"));
                        if (!declared.Add(key)) Fail("duplicate candidate: " + path + " " + key);
                        if (!actual.Contains(key)) Fail("candidate mismatch: " + path + " " + key);
                        if (Is(Field(candidate, "disposition"), "non-behavior"))
                        {
                            Need(candidate, new[] { "reason" }, "candidate " + path + " " + key);
                            if (Truthy(witnessId)) Fail("contradictory candidate: " + path + " " + key);
                        }
                        else if (Is(Field(candidate, "disposition"), "witness"))
                        {
                            if (!Truthy(witnessId) || !witnesses.TryGetValue(Text(witnessId), out var witness)) Fail("missing witness for candidate: " + path + " " + key);
                            else if (!string.Equals(Text(Field(witness, "sourcePath")), path, StringComparison.Ordinal)) Fail("candidate witness path mismatch: " + path + " " + key);
                        }
                        else Fail("candidate disposition invalid: " + path + " " + key);
                    }
                    foreach (var key in actualOrder)
                        if (!declared.Contains(key)) Fail("candidate unclassified: " + path + " " + key);
                }

                if (!IsBool(Field(restart, "required"), out var restartRequired) || Field(restart, "paths") is not JsonArray restartPaths
                    || Field(restart, "additionalPaths") is not JsonArray additionalPaths)
                    throw new InvalidInputException("invalid restartImpact fields");
                Need(restart, new[] { "reason" }, "restartImpact");
                var startup = changedOrder.Where(p => Regex.IsMatch(p, StartupPattern, RegexOptions.IgnoreCase) && !Regex.IsMatch(p, DocPattern)).ToList();
                var declaredPaths = restartPaths.Select(Text).ToList();
                var startupSet = new HashSet<string>(startup, StringComparer.OrdinalIgnoreCase);
                if (!startupSet.SetEquals(declaredPaths) || declaredPaths.Distinct(StringComparer.Ordinal).Count() != declaredPaths.Count)
                    Fail("restart path census mismatch");
                var additional = additionalPaths.Select(Text).ToList();
                foreach (var path in additional)
                    if (!inventory.ContainsKey(path)) Fail("restart dependency not inspected: " + path);
                var impacted = startup.Concat(additional).Where(p => !inventory.TryGetValue(p, out var entry) || !Is(Field(entry, "disposition"), "test-only")).ToList();
                if (restartRequired != (impacted.Count > 0)) Fail("restart required mismatch");
                if (restartRequired) Native(Field(restart, "nativeObligation"), "restartImpact", "Windows", true);
                else if (Has(restart, "nativeObligation")) Fail("contradictory restart native obligation");

                if (_errors.Count > 0)
                {
                    _out.WriteLine("OS-WITNESS obligations=" + _errors.Count.ToString(CultureInfo.InvariantCulture));
                    return 1;
                }
                _out.WriteLine("OS-WITNESS complete syntax/inventory; execution and runtime identity require Review");
                return 0;
            }
        }
    }
}
