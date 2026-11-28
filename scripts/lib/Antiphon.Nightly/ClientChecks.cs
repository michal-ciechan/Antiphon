// The client gates and the client suite (unit 32 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). They
// replaced scripts/lint-client.ps1, scripts/typecheck-client.ps1 and the body of scripts/test-client.ps1 (which stays as a
// thin launcher because scripts/c590-remote.sh runs it by path); the CLI runs them as
//
//   antiphon-scripts lint-client [--path <file>]
//   antiphon-scripts typecheck-client [--project <tsconfig>]
//   antiphon-scripts test-client [--json-result-path <file>] [--typecheck-project <tsconfig>] [--lint-path <file>]
//                                [--repo-root <dir>] [<vitest filter or flag> ...]
//
// (scripts/antiphon <command> on sh, scripts\antiphon.cmd <command> on Windows). Each tool runs on the ClientToolRunner seam,
// its lines teed to the run's output and a log under logs/, and each command ends with an unlosable verdict line, "CLIENT
// <LINT|TYPECHECK|TESTS> EXIT CODE: n  (PASS)" (or "(FAIL - ...)"), so a tail-capped capture still shows the verdict
// (CARD-0069). A missing binary is installed once through ClientNpmInstall first. None of them takes a build slot.
//
// lint-client (CARD-1393): node client/node_modules/eslint/bin/eslint.js . --max-warnings 0 --cache --cache-strategy content
// --cache-location client/node_modules/.tmp/eslintcache in client/ (content keys, so a same-size edit with a restored mtime
// still misses, CARD-1398); --path <file> (relative to the checkout) lints that file from its own directory. Log
// logs/client-lint.log.
//
// typecheck-client (CARD-1387): tsc -p tsconfig.app.json, then tsconfig.node.json, each --pretty false --noEmit --incremental
// --tsBuildInfoFile node_modules/.tmp/<project>.tsbuildinfo in client/ (content hashes, not tsc -b's mtimes); the worse code
// wins, and a 1 with a file(line,col) diagnostic in the log is the historical gate code 2 (CARD-1394). --project <tsconfig>
// runs tsc --pretty false --noEmit -p <tsconfig> from the checkout root. Log logs/client-typecheck.log.
//
// test-client (CARD-0069, CARD-0307): the typecheck gate (--typecheck-project), then the lint gate (--lint-path), then
// node client/node_modules/vitest/vitest.mjs run [<reporters for --json-result-path>] <forwarded arguments> in client/: every
// argument that is not one of its flags is a Vitest filter or flag and reaches Node's argv unchanged (never npx: its npx.ps1
// shim rebuilt argv from the calling line's source text). A failing gate ends the run with its code before Vitest starts.
// Log logs/client-tests.log. --repo-root names the checkout (the nightly passes its clone); default the CLI's.
//
// Exit codes are the tool's (1 when it cannot start or a binary is still missing). Usage errors are NightlyUsageException
// (the CLI's exit 2). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The lint-client command's options (lint-client.ps1 -Path).</summary>
    public sealed class ClientLintOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "lint-client [--path <file>]";

        /// <summary>One file to lint (relative to the checkout or rooted); blank: the whole client with the cache.</summary>
        public string Path { get; init; } = "";

        /// <summary>The checkout whose client is linted.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static ClientLintOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--path" }, Array.Empty<string>());
            return new ClientLintOptions { Path = flags.Value("--path", ""), RepoRoot = repoRoot ?? "" };
        }
    }

    /// <summary>The client lint gate (lint-client.ps1).</summary>
    public static class ClientLint
    {
        /// <summary>The log under logs/.</summary>
        public const string LogName = "client-lint.log";

        /// <summary>Lints, prints the verdict and returns eslint's exit code (1 when it is not installed or the file is missing).</summary>
        public static int Run(ClientLintOptions options, NightlyHost host, IFileSystem files, ClientToolRunner tools)
        {
            using var phase = ClientPhase.Start("client lint");
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            var root = options.RepoRoot;
            var clientDir = System.IO.Path.Combine(root, "client");
            var logFile = System.IO.Path.Combine(ClientTools.Logs(files, root), LogName);
            var eslint = ClientTools.Eslint(root);
            if (!files.File.Exists(eslint))
            {
                host.Out.WriteLine("eslint is not installed at " + eslint + " - installing client dependencies first.");
                var installCode = ClientNpmInstall.Install(root, new[] { eslint }, host, files, tools);
                if (installCode != 0) host.Out.WriteLine("npm ci exited with code " + ClientTools.Number(installCode) + ".");
            }
            if (!files.File.Exists(eslint))
            {
                host.Out.WriteLine("eslint is still not installed at " + eslint + "; run npm ci in client/ by hand and look at why it failed.");
                host.Out.WriteLine("");
                host.Out.WriteLine("CLIENT LINT EXIT CODE: 1  (FAIL - do not report this run as green)");
                return 1;
            }

            ClientToolLaunch launch;
            if (!string.IsNullOrWhiteSpace(options.Path))
            {
                var target = System.IO.Path.IsPathRooted(options.Path) ? options.Path : System.IO.Path.Combine(root, options.Path);
                target = files.Path.GetFullPath(target);
                if (!files.File.Exists(target) && !files.Directory.Exists(target))
                {
                    host.Out.WriteLine("lint target is missing at " + target);
                    host.Out.WriteLine("");
                    host.Out.WriteLine("CLIENT LINT EXIT CODE: 1  (FAIL - do not report this run as green)");
                    host.Out.WriteLine("Full output: " + logFile);
                    return 1;
                }
                launch = new ClientToolLaunch("node", new[] { eslint, System.IO.Path.GetFileName(target), "--max-warnings", "0" }, System.IO.Path.GetDirectoryName(target));
            }
            else
            {
                var cache = System.IO.Path.Combine(clientDir, "node_modules", ".tmp", "eslintcache");
                files.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cache));
                var arguments = new List<string> { eslint, ".", "--max-warnings", "0", "--cache", "--cache-strategy", "content", "--cache-location", cache };
                // Bound the Windows cold full-client lint to two workers.
                if (host.Probe.Platform() == "windows") arguments.AddRange(new[] { "--concurrency", "2" });
                launch = new ClientToolLaunch("node", arguments, clientDir);
            }
            if (!ClientGate.Tee(files, logFile, false, host, tools, launch, out var code)) return 1;
            ClientGate.Footer(host, "CLIENT LINT EXIT CODE: ", code, logFile);
            return code;
        }
    }

    /// <summary>The typecheck-client command's options (typecheck-client.ps1 -Project).</summary>
    public sealed class ClientTypecheckOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "typecheck-client [--project <tsconfig>]";

        /// <summary>One tsconfig (relative to the checkout or rooted); blank: both client projects, incrementally.</summary>
        public string Project { get; init; } = "";

        /// <summary>The checkout whose client is typechecked.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static ClientTypecheckOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--project" }, Array.Empty<string>());
            return new ClientTypecheckOptions { Project = flags.Value("--project", ""), RepoRoot = repoRoot ?? "" };
        }
    }

    /// <summary>The client typecheck gate (typecheck-client.ps1).</summary>
    public static class ClientTypecheck
    {
        /// <summary>The log under logs/.</summary>
        public const string LogName = "client-typecheck.log";

        /// <summary>The client projects, in the order they are checked.</summary>
        public static readonly IReadOnlyList<string> Projects = new[] { "tsconfig.app.json", "tsconfig.node.json" };

        // tsc -p --noEmit --incremental exits 2 for a source diagnostic; tsc -b exited 1 (CARD-1394).
        private static readonly Regex SourceDiagnostic = new Regex(@"\(\d+,\d+\): error TS", RegexOptions.CultureInvariant);

        /// <summary>Typechecks, prints the verdict and returns the gate code (1 when tsc is not installed).</summary>
        public static int Run(ClientTypecheckOptions options, NightlyHost host, IFileSystem files, ClientToolRunner tools)
        {
            using var phase = ClientPhase.Start("client typecheck");
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            var root = options.RepoRoot;
            var clientDir = Path.Combine(root, "client");
            var logFile = Path.Combine(ClientTools.Logs(files, root), LogName);
            var tsc = ClientTools.Tsc(root);
            if (!files.File.Exists(tsc))
            {
                host.Out.WriteLine("typescript is not installed at " + tsc + " - installing client dependencies first.");
                var installCode = ClientNpmInstall.Install(root, new[] { tsc }, host, files, tools);
                if (installCode != 0) host.Out.WriteLine("npm ci exited with code " + ClientTools.Number(installCode) + ".");
            }
            if (!files.File.Exists(tsc))
            {
                host.Out.WriteLine("typescript is still not installed at " + tsc + "; run npm ci in client/ by hand and look at why it failed.");
                host.Out.WriteLine("");
                host.Out.WriteLine("CLIENT TYPECHECK EXIT CODE: 1  (FAIL - do not report this run as green)");
                return 1;
            }

            int code;
            if (!string.IsNullOrWhiteSpace(options.Project))
            {
                var launch = new ClientToolLaunch("node", new[] { tsc, "--pretty", "false", "--noEmit", "-p", options.Project }, root);
                if (!ClientGate.Tee(files, logFile, false, host, tools, launch, out code)) return 1;
            }
            else
            {
                code = 0;
                for (var i = 0; i < Projects.Count; i++)
                {
                    var project = Projects[i];
                    using var projectPhase = ClientPhase.Start("client typecheck " + project);
                    var launch = new ClientToolLaunch("node", new[]
                    {
                        tsc, "-p", project, "--pretty", "false", "--noEmit", "--incremental",
                        "--tsBuildInfoFile", "node_modules/.tmp/" + Path.GetFileNameWithoutExtension(project) + ".tsbuildinfo",
                    }, clientDir);
                    if (!ClientGate.Tee(files, logFile, i > 0, host, tools, launch, out var projectCode)) return 1;
                    // The first project's code, unless a later one failed and the first did not, or failed worse.
                    if (i == 0) code = projectCode;
                    else if (projectCode != 0 && (code == 0 || projectCode > code)) code = projectCode;
                }
                if (code == 1 && files.File.Exists(logFile) && SourceDiagnostic.IsMatch(files.File.ReadAllText(logFile))) code = 2;
            }
            ClientGate.Footer(host, "CLIENT TYPECHECK EXIT CODE: ", code, logFile);
            return code;
        }
    }

    /// <summary>The test-client command's options (test-client.ps1's arguments).</summary>
    public sealed class ClientTestsOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "test-client [--json-result-path <file>] [--typecheck-project <tsconfig>] [--lint-path <file>] [--repo-root <dir>] [<vitest filter or flag> ...]";

        private static readonly string[] Valued = { "--json-result-path", "--typecheck-project", "--lint-path", "--repo-root" };

        /// <summary>Vitest's JSON report (its --outputFile, relative to client/ unless rooted); blank: none.</summary>
        public string JsonResultPath { get; init; } = "";

        /// <summary>The typecheck gate's --project; blank: both client projects.</summary>
        public string TypecheckProject { get; init; } = "";

        /// <summary>The lint gate's --path; blank: the whole client.</summary>
        public string LintPath { get; init; } = "";

        /// <summary>Every other argument, in order: Vitest's filters and flags.</summary>
        public IReadOnlyList<string> Forward { get; init; } = Array.Empty<string>();

        /// <summary>The checkout whose client is tested.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>
        /// The arguments after the command name: the four flags (each once, with a value that is not another of them, as
        /// "--flag value", never "--flag=value"), and everything else forwarded. A blank --repo-root is repoRoot.
        /// </summary>
        public static ClientTestsOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var forward = new List<string>();
            var count = arguments?.Count ?? 0;
            for (var i = 0; i < count; i++)
            {
                var argument = arguments[i] ?? "";
                if (Array.IndexOf(Valued, argument) < 0)
                {
                    var equals = argument.IndexOf('=');
                    if (equals > 0 && Array.IndexOf(Valued, argument.Substring(0, equals)) >= 0)
                    {
                        var name = argument.Substring(0, equals);
                        throw new NightlyUsageException("write " + name + " <value>, not " + name + "=<value>");
                    }
                    forward.Add(argument);
                    continue;
                }
                if (values.ContainsKey(argument)) throw new NightlyUsageException("repeated flag " + argument);
                if (i + 1 >= count || Array.IndexOf(Valued, arguments[i + 1] ?? "") >= 0) throw new NightlyUsageException("missing value for " + argument);
                values[argument] = arguments[++i] ?? "";
            }
            string Value(string flag) => values.TryGetValue(flag, out var value) ? value : "";
            var root = Value("--repo-root");
            return new ClientTestsOptions
            {
                JsonResultPath = Value("--json-result-path"),
                TypecheckProject = Value("--typecheck-project"),
                LintPath = Value("--lint-path"),
                Forward = forward.ToArray(),
                RepoRoot = string.IsNullOrWhiteSpace(root) ? repoRoot ?? "" : root,
            };
        }
    }

    /// <summary>The client Vitest suite behind its gates (test-client.ps1).</summary>
    public static class ClientTests
    {
        /// <summary>The log under logs/.</summary>
        public const string LogName = "client-tests.log";

        /// <summary>Installs what is missing, runs the gates and Vitest, prints the verdict and returns the first failing code.</summary>
        public static int Run(ClientTestsOptions options, NightlyHost host, IFileSystem files, ClientToolRunner tools)
        {
            using var phase = ClientPhase.Start("client launcher");
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            var root = options.RepoRoot;
            var clientDir = Path.Combine(root, "client");
            var logFile = Path.Combine(ClientTools.Logs(files, root), LogName);
            var vitest = ClientTools.Vitest(root);
            var required = new[] { vitest, ClientTools.Tsc(root), ClientTools.Eslint(root) };
            // A fresh linked worktree has no client/node_modules: install once here instead of failing the suite (the
            // install is somebody else's job no longer). Typecheck and lint share this one install (CARD-1393).
            bool installed;
            using (ClientPhase.Start("client dependency check"))
                installed = required.All(files.File.Exists);
            if (!installed)
            {
                using var installPhase = ClientPhase.Start("client dependency install");
                host.Out.WriteLine("client dependencies are not installed - installing once for vitest, tsc and eslint.");
                var installCode = ClientNpmInstall.Install(root, required, host, files, tools);
                if (installCode != 0) host.Out.WriteLine("npm ci exited with code " + ClientTools.Number(installCode) + ".");
            }
            if (!files.File.Exists(vitest))
            {
                host.Out.WriteLine("vitest is still not installed at " + vitest + "; run npm ci in client/ by hand and look at why it failed.");
                host.Out.WriteLine("");
                host.Out.WriteLine("CLIENT TESTS EXIT CODE: 1  (FAIL - do not report this run as green)");
                return 1;
            }

            var typecheck = ClientTypecheck.Run(new ClientTypecheckOptions { Project = options.TypecheckProject, RepoRoot = root }, host, files, tools);
            if (typecheck != 0)
            {
                host.Out.WriteLine("");
                host.Out.WriteLine("CLIENT TESTS EXIT CODE: " + ClientTools.Number(typecheck) + "  (FAIL - typecheck failed; vitest was not started)");
                return typecheck;
            }
            var lint = ClientLint.Run(new ClientLintOptions { Path = options.LintPath, RepoRoot = root }, host, files, tools);
            if (lint != 0)
            {
                host.Out.WriteLine("");
                host.Out.WriteLine("CLIENT TESTS EXIT CODE: " + ClientTools.Number(lint) + "  (FAIL - lint failed; vitest was not started)");
                return lint;
            }

            var arguments = new List<string> { vitest, "run" };
            if (!string.IsNullOrWhiteSpace(options.JsonResultPath))
                arguments.AddRange(new[] { "--reporter=default", "--reporter=json", "--outputFile", options.JsonResultPath });
            arguments.AddRange(options.Forward ?? Array.Empty<string>());
            int code;
            using (ClientPhase.Start("client Vitest"))
                if (!ClientGate.Tee(files, logFile, false, host, tools, new ClientToolLaunch("node", arguments, clientDir), out code)) return 1;
            ClientGate.Footer(host, "CLIENT TESTS EXIT CODE: ", code, logFile);
            return code;
        }
    }

    /// <summary>What the three commands share: one tool run teed to its log, and the verdict footer.</summary>
    internal static class ClientGate
    {
        /// <summary>
        /// Runs the launch with its lines teed to the log (created, or appended to). False, after one host.Error line, when
        /// the log cannot be written; then code is 1.
        /// </summary>
        internal static bool Tee(IFileSystem files, string logFile, bool append, NightlyHost host, ClientToolRunner tools, ClientToolLaunch launch, out int code)
        {
            ClientLog log;
            try
            {
                log = ClientLog.Open(files, logFile, append, host.Out);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                host.Error.WriteLine("cannot write " + logFile + ": " + e.Message);
                code = 1;
                return false;
            }
            using (log) code = ClientTools.Invoke(tools, () => launch, log.Line);
            return true;
        }

        /// <summary>The blank line, the verdict line and the log's path.</summary>
        internal static void Footer(NightlyHost host, string label, int code, string logFile)
        {
            host.Out.WriteLine("");
            host.Out.WriteLine(label + ClientTools.Number(code) + "  (" + ClientTools.Verdict(code) + ")");
            host.Out.WriteLine("Full output: " + logFile);
        }
    }
}
