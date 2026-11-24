// CARD-1586: the static guard over the server image's linked sources. It replaced scripts/check-server-docker-inputs.ps1 and
// its offline driver scripts/test-server-docker-inputs.ps1 (unit 46 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md,
// W8 slice A); the CLI runs it as
//
//   antiphon-scripts check-server-docker-inputs [--repository <dir>]
//
// (scripts/antiphon check-server-docker-inputs on sh, which the CI job's "Check server Docker linked inputs" step runs;
// scripts\antiphon.cmd check-server-docker-inputs on Windows). Every explicit Compile Include of a project under server/ or
// src/ that points outside its own project directory is a linked input, and each must reach /src/<path> through a COPY of the
// Dockerfile's server-build stage before its dotnet publish, without Dockerfile.dockerignore (else .dockerignore) excluding it
// or one of its parent directories (Docker cannot re-include a child of an excluded directory).
//
// It reads files only: no MSBuild evaluation, no build, no Docker, no network. Conditional items count as if they applied;
// a dynamic Include ($, @, %, * or ?), a COPY option other than --from, --chown or --chmod, or a COPY with a variable fails
// closed. Lines on host.Out: "DOCKER INPUT missing <path> (<reason>)" for each missing input (by path), then
// "DOCKER INPUT projects=<n> linked=<n> missing=<n>"; or one "DOCKER INPUT error: <message>". Exit 0 every input copied, 1 an
// input missing, 2 an error (and usage). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace Antiphon.Nightly
{
    /// <summary>The check-server-docker-inputs command's options (check-server-docker-inputs.ps1 -Repository).</summary>
    public sealed class ServerDockerInputsOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "check-server-docker-inputs [--repository <dir>]";

        /// <summary>The checkout whose projects, Dockerfile and ignore file are read.</summary>
        public string Repository { get; init; } = "";

        /// <summary>The arguments after the command name; defaultRepository is the checkout the CLI was built in.</summary>
        public static ServerDockerInputsOptions Parse(IReadOnlyList<string> arguments, string defaultRepository)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--repository" }, Array.Empty<string>());
            var repository = flags.Value("--repository", "");
            return new ServerDockerInputsOptions { Repository = string.IsNullOrWhiteSpace(repository) ? defaultRepository ?? "" : repository };
        }
    }

    /// <summary>The linked-input guard (check-server-docker-inputs.ps1).</summary>
    public static class ServerDockerInputs
    {
        /// <summary>Exit 0: every linked input is copied.</summary>
        public const int Complete = 0;

        /// <summary>Exit 1: an input is missing from the image.</summary>
        public const int Missing = 1;

        /// <summary>Exit 2: the inputs could not be checked.</summary>
        public const int Error = 2;

        private const RegexOptions Insensitive = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private static readonly Regex From = new Regex(@"^\s*FROM\s+\S+\s+AS\s+(\S+)", Insensitive);
        private static readonly Regex Publish = new Regex(@"^\s*RUN\s+.*\bdotnet\s+publish\b", Insensitive);
        private static readonly Regex Workdir = new Regex(@"^\s*WORKDIR\s+(\S+)\s*$", Insensitive);
        private static readonly Regex Copy = new Regex(@"^\s*COPY\s+(.+)$", Insensitive);
        private static readonly Regex CopyFrom = new Regex("^--from=", Insensitive);
        private static readonly Regex Ownership = new Regex(@"^(--(?:chown|chmod)=\S+\s+)+", Insensitive);
        private static readonly Regex Token = new Regex("\"[^\"]*\"|\\S+", RegexOptions.CultureInvariant);
        private static readonly Regex Dynamic = new Regex("[$@%*?]", RegexOptions.CultureInvariant);

        private sealed record CopyRule(string Source, string Target, bool DirectoryTarget);

        /// <summary>Checks options.Repository and writes the lines to host.Out. Returns Complete, Missing or Error.</summary>
        public static int Run(ServerDockerInputsOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            try
            {
                return Check(files.Path.GetFullPath(options.Repository), host.Out, files);
            }
            catch (Exception e)
            {
                // Fail closed: whatever stopped the check (an unreadable or malformed file, an unsupported line) is exit 2.
                host.Out.WriteLine("DOCKER INPUT error: " + e.Message);
                return Error;
            }
        }

        private static int Check(string repository, TextWriter output, IFileSystem files)
        {
            var separator = files.Path.DirectorySeparatorChar;
            var required = new HashSet<string>(StringComparer.Ordinal);
            var projects = new List<string>();
            foreach (var root in new[] { "server", "src" })
            {
                var directory = files.Path.Combine(repository, root);
                if (!files.Directory.Exists(directory)) throw new InvalidOperationException("Cannot find path '" + directory + "' because it does not exist.");
                FindProjects(files, directory, projects);
            }
            foreach (var project in projects)
            {
                var projectDirectory = files.Path.GetDirectoryName(project);
                var xml = new XmlDocument { XmlResolver = null };
                xml.LoadXml(files.File.ReadAllText(project));
                foreach (XmlElement item in xml.SelectNodes("//Compile[@Include]"))
                {
                    foreach (var include in item.GetAttribute("Include").Split(';'))
                    {
                        if (Dynamic.IsMatch(include)) throw new InvalidOperationException("Dynamic Compile Include needs guard support: " + include);
                        var path = files.Path.GetFullPath(JoinPath(projectDirectory, include.Replace('\\', '/'), separator));
                        var relative = files.Path.GetRelativePath(repository, path).Replace('\\', '/');
                        // Local Compile declarations are already covered by the project directory COPY.
                        if (!path.StartsWith(projectDirectory + separator, StringComparison.Ordinal)) required.Add(Normalize(relative));
                    }
                }
            }
            if (projects.Count == 0 || required.Count == 0) throw new InvalidOperationException("No projects or linked Compile inputs found");

            var ignorePath = files.Path.Combine(repository, "Dockerfile.dockerignore");
            if (!files.File.Exists(ignorePath)) ignorePath = files.Path.Combine(repository, ".dockerignore");
            var rules = files.File.ReadAllLines(ignorePath).Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToArray();
            var copies = ServerBuildCopies(files.File.ReadAllText(files.Path.Combine(repository, "Dockerfile")));

            var missing = new List<string>();
            foreach (var path in required.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ThenBy(p => p, StringComparer.Ordinal))
            {
                var expected = "src/" + path;
                var copied = false;
                foreach (var copy in copies)
                {
                    var source = Normalize(copy.Source);
                    var sourceDirectory = files.Directory.Exists(JoinPath(repository, source, separator));
                    string actual = null;
                    if (sourceDirectory && (source.Length == 0 || path.StartsWith(source + "/", StringComparison.Ordinal)))
                    {
                        var suffix = source.Length == 0 ? path : path.Substring(source.Length + 1);
                        actual = Normalize(copy.Target + "/" + suffix);
                    }
                    else if (Glob(source, path))
                    {
                        actual = copy.DirectoryTarget ? Normalize(copy.Target + "/" + path.Split('/')[^1]) : copy.Target;
                    }
                    if (string.Equals(actual, expected, StringComparison.Ordinal)) copied = true;
                }
                if (!files.File.Exists(JoinPath(repository, path, separator))) missing.Add(path + " (source absent)");
                else if (IsIgnored(path, rules)) missing.Add(path + " (dockerignore excludes input)");
                else if (!copied) missing.Add(path + " (no COPY to /" + expected + " before publish)");
            }
            foreach (var failure in missing) output.WriteLine("DOCKER INPUT missing " + failure);
            output.WriteLine("DOCKER INPUT projects=" + projects.Count + " linked=" + required.Count + " missing=" + missing.Count);
            return missing.Count == 0 ? Complete : Missing;
        }

        // The server-build stage's COPY rules before its dotnet publish (line continuations joined first).
        private static List<CopyRule> ServerBuildCopies(string dockerfile)
        {
            var copies = new List<CopyRule>();
            var stage = "";
            var workdir = "";
            var foundStage = false;
            var foundPublish = false;
            foreach (var line in Regex.Split(Regex.Replace(dockerfile, @"\\\r?\n", " "), @"\r?\n"))
            {
                var from = From.Match(line);
                if (from.Success)
                {
                    stage = from.Groups[1].Value;
                    workdir = "";
                    continue;
                }
                if (!string.Equals(stage, "server-build", StringComparison.OrdinalIgnoreCase)) continue;
                foundStage = true;
                if (Publish.IsMatch(line))
                {
                    foundPublish = true;
                    break;
                }
                var dir = Workdir.Match(line);
                if (dir.Success)
                {
                    var value = dir.Groups[1].Value;
                    workdir = Normalize(value.StartsWith('/') ? value : workdir + "/" + value);
                }
                var copy = Copy.Match(line);
                if (!copy.Success) continue;
                var body = copy.Groups[1].Value;
                if (CopyFrom.IsMatch(body)) continue;
                body = Ownership.Replace(body, "");
                if (body.StartsWith("--", StringComparison.Ordinal) || body.Contains('$')) throw new InvalidOperationException("Unsupported COPY: " + line);
                var tokens = body.StartsWith('[') ? JsonTokens(body, line) : Token.Matches(body).Select(m => m.Value.Trim('"')).ToArray();
                if (tokens.Length < 2) throw new InvalidOperationException("Invalid COPY: " + line);
                var destination = tokens[^1];
                var target = Normalize(destination.StartsWith('/') ? destination : workdir + "/" + destination);
                var directoryTarget = destination.EndsWith('/') || destination == "." || tokens.Length > 2;
                foreach (var source in tokens.Take(tokens.Length - 1)) copies.Add(new CopyRule(source.TrimStart('/').TrimEnd('/'), target, directoryTarget));
            }
            if (!foundStage || !foundPublish) throw new InvalidOperationException("Missing server-build stage or dotnet publish");
            return copies;
        }

        // The exec form: a JSON array of strings.
        private static string[] JsonTokens(string body, string line)
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("Invalid COPY: " + line);
            return document.RootElement.EnumerateArray()
                .Select(element => element.ValueKind == JsonValueKind.String ? element.GetString() : throw new InvalidOperationException("Invalid COPY: " + line))
                .ToArray();
        }

        // Get-ChildItem -Filter *.csproj -Recurse -File: hidden and system entries skipped, links listed but not entered.
        private static void FindProjects(IFileSystem files, string directory, List<string> found)
        {
            foreach (var file in files.Directory.EnumerateFiles(directory))
            {
                if ((files.File.GetAttributes(file) & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                if (file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) found.Add(file);
            }
            foreach (var child in files.Directory.EnumerateDirectories(directory))
            {
                var attributes = files.File.GetAttributes(child);
                if ((attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0) continue;
                FindProjects(files, child, found);
            }
        }

        // Join-Path: one separator between the parent and the child.
        private static string JoinPath(string parent, string child, char separator) =>
            parent.TrimEnd('\\', '/') + separator + child.TrimStart('\\', '/');

        /// <summary>A repository-relative path with '/' separators and no '.', '..' or empty segments; a '..' above the root throws.</summary>
        public static string Normalize(string path)
        {
            var parts = new List<string>();
            foreach (var part in path.Replace('\\', '/').Split('/'))
            {
                if (part.Length == 0 || part == ".") continue;
                if (part == "..")
                {
                    if (parts.Count == 0) throw new InvalidOperationException("Path escapes root: " + path);
                    parts.RemoveAt(parts.Count - 1);
                }
                else
                {
                    parts.Add(part);
                }
            }
            return string.Join("/", parts);
        }

        /// <summary>A Docker-style glob ('**/', '**', '*', '?'; '*' and '?' stop at '/') over the whole path, case-sensitive.</summary>
        public static bool Glob(string pattern, string path)
        {
            var regex = Regex.Escape(pattern).Replace(@"\*\*/", "(?:.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]");
            return Regex.IsMatch(path, "^" + regex + "$", RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// True when the ignore rules exclude the path or one of its parent directories (Docker cannot re-include a child of
        /// an excluded directory); later rules win, '!' re-includes.
        /// </summary>
        public static bool IsIgnored(string path, IReadOnlyList<string> rules)
        {
            var parts = path.Split('/');
            for (var i = 1; i <= parts.Length; i++)
            {
                var prefix = string.Join("/", parts.Take(i));
                var ignored = false;
                foreach (var rule in rules)
                {
                    var negated = rule.StartsWith('!');
                    var pattern = rule.TrimStart('!').Trim('/');
                    if (pattern.Length > 0 && Glob(pattern, prefix)) ignored = !negated;
                }
                if (ignored) return true;
            }
            return false;
        }
    }
}
