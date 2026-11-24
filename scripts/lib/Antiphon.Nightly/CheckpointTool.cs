// Unit 20 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the checkpoint tool (tools/Antiphon.Checkpoints)
// built from a checkout and run as a child, `antiphon-scripts checkpoints <verb> [arguments...]`. scripts/run-checkpoint.ps1
// (its row verb) and scripts/validate-checkpoint-receipt.ps1 (its validate verb) are thin launchers of this command. The
// tool stays its own program (it carries libgit2, Roslyn and YamlDotNet), so this core never references it: the command
// hashes the tool's sources, builds it once per source hash into <checkout>/bin-launcher/antiphon-checkpoints/<hash>/ and
// runs that build's DLL with dotnet exec, in the caller's directory and on this process's console, with the arguments as
// given. A running row keeps its own folder, so a later source change builds beside it instead of over it.
//
// The sources are the tool's project and every project it references (each one's .cs, .csproj, .props and .targets, bin,
// obj and bin-* folders excluded), every file a project compiles from outside its folder (a Compile Include link) and the
// repository's shared build files, each as its forward-slash relative name, a line feed and its bytes, SHA-256 over the
// ordinal-sorted list. The build (dotnet build -c Release into a fresh folder with its own artifacts path, quiet, two
// nodes, no reused nodes or compiler server) runs under bin-launcher/antiphon-checkpoints/build.lock, without the caller's
// MSBuild* environment, and its output goes to build.log there, never to the console. A build folder unused for 7 days
// is removed after the next build. Exit: the tool's own code; 9 when the tool could not be built or started (one stderr
// line, as the launchers' own exit 9). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The checkpoints command's options: the checkout whose tool runs, the caller's directory, the verb and its arguments.</summary>
    public sealed class CheckpointToolOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "checkpoints <verb> [arguments...] (the checkpoint tool built from this checkout: row, validate, run, start, wait, report, ...)";

        /// <summary>The checkout the tool is built from: a full path.</summary>
        public string Checkout { get; init; } = "";

        /// <summary>The directory the tool runs in (the caller's).</summary>
        public string WorkingDirectory { get; init; } = "";

        /// <summary>The tool's arguments, verb first, passed on as given.</summary>
        public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

        /// <summary>The environment the build child starts from (MSBuild* names are left out of it); null inherits this process's.</summary>
        public IReadOnlyDictionary<string, string> Environment { get; init; }

        /// <summary>The arguments after the command name, verbatim. NightlyUsageException when there is no verb.</summary>
        public static CheckpointToolOptions Parse(IReadOnlyList<string> arguments, string checkout, string currentDirectory,
            IReadOnlyDictionary<string, string> environment)
        {
            if (string.IsNullOrWhiteSpace(checkout)) throw new ArgumentException("checkout is required", nameof(checkout));
            if (arguments == null || arguments.Count == 0 || string.IsNullOrWhiteSpace(arguments[0]))
                throw new NightlyUsageException("a verb is required: " + Usage);
            return new CheckpointToolOptions
            {
                Checkout = Path.GetFullPath(checkout),
                WorkingDirectory = string.IsNullOrWhiteSpace(currentDirectory) ? Path.GetFullPath(checkout) : currentDirectory,
                Arguments = arguments.ToArray(),
                Environment = environment,
            };
        }
    }

    /// <summary>The checkpoint tool built from a checkout and run as a child.</summary>
    public static class CheckpointTool
    {
        /// <summary>The tool's project, relative to a checkout.</summary>
        public const string ProjectRelative = "tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj";

        /// <summary>The tool's assembly.</summary>
        public const string DllName = "Antiphon.Checkpoints.dll";

        /// <summary>The source hash a build folder was made from, beside its DLL.</summary>
        public const string StampName = "source-hash";

        /// <summary>Exit code when the tool could not be built or started.</summary>
        public const int Unavailable = 9;

        /// <summary>How long one resolution waits for another process's build.</summary>
        public static readonly TimeSpan LockWait = TimeSpan.FromMinutes(15);

        /// <summary>How often the build lock is tried while another process holds it.</summary>
        public static readonly TimeSpan LockPoll = TimeSpan.FromMilliseconds(250);

        /// <summary>A build folder not used for this long is removed after the next build.</summary>
        public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

        /// <summary>The repository's shared build files, hashed when present.</summary>
        public static readonly IReadOnlyList<string> SharedBuildFiles = Array.AsReadOnly(new[]
        {
            "Directory.Build.props", "Directory.Build.targets", "Directory.Build.rsp", "Directory.Packages.props", "global.json",
            "NuGet.config", "tools/Directory.Build.props", "tools/Directory.Build.targets", "src/Directory.Build.props",
            "src/Directory.Build.targets",
        });

        private static readonly Regex SourceFile = new Regex(@"\.(cs|csproj|props|targets)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex BuildFolder = new Regex("/(bin|obj|bin-[^/]*)/", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex CompileLink = new Regex("<Compile\\s+Include=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        private static readonly Regex ProjectReference = new Regex("<ProjectReference\\s+Include=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        private static readonly Regex ErrorLine = new Regex(@"\berror\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>The folder the builds live in: &lt;checkout&gt;/bin-launcher/antiphon-checkpoints.</summary>
        public static string Root(string checkout, IFileSystem files) => files.Path.Combine(checkout, "bin-launcher", "antiphon-checkpoints");

        /// <summary>
        /// The tool's sources: forward-slash paths relative to the checkout, ordinal-sorted. The tool's project and the projects
        /// it references, transitively: their .cs, .csproj, .props and .targets files (bin, obj and bin-* folders excluded) and
        /// every file a project's Compile Include names; then the shared build files present.
        /// </summary>
        public static IReadOnlyList<string> SourceFiles(string checkout, IFileSystem files)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            checkout = files.Path.GetFullPath(checkout).TrimEnd('\\', '/');
            var found = new SortedSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<string>();
            pending.Enqueue(files.Path.Combine(checkout, ProjectRelative));
            while (pending.Count > 0)
            {
                var project = files.Path.GetFullPath(pending.Dequeue());
                if (!seen.Add(project) || !files.File.Exists(project)) continue;
                var directory = files.Path.GetDirectoryName(project);
                foreach (var file in files.Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                {
                    var relative = Relative(checkout, file);
                    if (relative == null || !SourceFile.IsMatch(relative)) continue;
                    if (BuildFolder.IsMatch("/" + Relative(directory, file))) continue;
                    found.Add(relative);
                }
                var text = files.File.ReadAllText(project);
                foreach (Match link in CompileLink.Matches(text))
                {
                    var target = files.Path.GetFullPath(files.Path.Combine(directory, link.Groups[1].Value.Replace('\\', '/')));
                    var relative = Relative(checkout, target);
                    if (relative != null && files.File.Exists(target)) found.Add(relative);
                }
                foreach (Match reference in ProjectReference.Matches(text))
                    pending.Enqueue(files.Path.Combine(directory, reference.Groups[1].Value.Replace('\\', '/')));
            }
            foreach (var shared in SharedBuildFiles)
                if (files.File.Exists(files.Path.Combine(checkout, shared))) found.Add(shared);
            return found.ToArray();
        }

        /// <summary>SHA-256 (lower-case hex) over each source's name, a line feed and its bytes, in SourceFiles order.</summary>
        public static string SourceHash(string checkout, IFileSystem files)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var relative in SourceFiles(checkout, files))
            {
                hash.AppendData(Encoding.UTF8.GetBytes(relative + "\n"));
                hash.AppendData(files.File.ReadAllBytes(files.Path.Combine(checkout, relative)));
            }
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }

        /// <summary>The build's dotnet arguments: Release into its own output and artifacts path, quiet, two nodes, no reused nodes or compiler server.</summary>
        public static IReadOnlyList<string> BuildArguments(string project, string output, string artifacts) => new[]
        {
            "build", project, "-c", "Release", "-o", output, "--artifacts-path", artifacts, "-nologo", "-v", "q", "-m:2",
            "-nodeReuse:false", "-p:UseSharedCompilation=false",
        };

        /// <summary>
        /// The tool's DLL for the checkout's current sources, built first when no folder holds that hash. Null after one
        /// line on host.Error when the project is missing or the build failed or could not start.
        /// </summary>
        public static string Resolve(CheckpointToolOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            var checkout = options.Checkout;
            var project = files.Path.GetFullPath(files.Path.Combine(checkout, ProjectRelative));
            if (!files.File.Exists(project))
            {
                host.Error.WriteLine("checkpoints: missing " + project);
                return null;
            }
            var hash = SourceHash(checkout, files);
            var root = Root(checkout, files);
            var folder = files.Path.Combine(root, hash.Substring(0, 24));
            var dll = files.Path.Combine(folder, DllName);
            if (Current(folder, hash, files)) return Touch(folder, dll, files);

            files.Directory.CreateDirectory(root);
            var lockPath = files.Path.Combine(root, "build.lock");
            var deadline = host.UtcNow + LockWait;
            Stream held = null;
            while (held == null)
            {
                try
                {
                    held = files.File.Open(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    if (host.UtcNow > deadline)
                    {
                        host.Error.WriteLine("checkpoints: the build lock " + lockPath + " was still held after " + (int)LockWait.TotalMinutes + " minutes");
                        return null;
                    }
                    host.Sleep(LockPoll);
                }
            }
            using (held)
            {
                // Another process may have built it while this one waited.
                if (Current(folder, hash, files)) return Touch(folder, dll, files);
                host.Error.WriteLine("checkpoints: building the checkpoint tool (" + hash.Substring(0, 12) + ")");
                var work = files.Path.Combine(root, "tmp-" + Guid.NewGuid().ToString("N"));
                var built = files.Path.Combine(work, "out");
                var log = files.Path.Combine(root, "build.log");
                files.Directory.CreateDirectory(work);
                try
                {
                    var started = host.Clock.GetTimestamp();
                    NightlyLaunchResult build;
                    try
                    {
                        build = host.Launch(new NightlyLaunch("dotnet", BuildArguments(project, built, files.Path.Combine(work, "artifacts")),
                            checkout, Environment: WithoutMsBuild(options.Environment), CaptureOutput: true));
                    }
                    catch (Exception e) when (e is Win32Exception || e is InvalidOperationException)
                    {
                        host.Error.WriteLine("checkpoints: dotnet did not start to build the checkpoint tool: " + e.Message);
                        return null;
                    }
                    files.File.WriteAllText(log, build.LogText ?? "", new UTF8Encoding(false));
                    if (build.ExitCode != 0 || !files.File.Exists(files.Path.Combine(built, DllName)))
                    {
                        var first = (build.LogText ?? "").Replace("\r\n", "\n").Split('\n').Where(line => ErrorLine.IsMatch(line)).Take(3).Select(line => line.Trim());
                        host.Error.WriteLine("checkpoints: the checkpoint tool did not build (exit " + build.ExitCode + ", log " + log + ")"
                            + (first.Any() ? ": " + string.Join(" | ", first) : ""));
                        return null;
                    }
                    files.File.WriteAllText(files.Path.Combine(built, StampName), hash, new UTF8Encoding(false));
                    if (files.Directory.Exists(folder)) TryDelete(folder, files);
                    files.Directory.Move(built, folder);
                    var seconds = (int)Math.Round(host.Clock.GetElapsedTime(started).TotalSeconds, MidpointRounding.ToEven);
                    host.Error.WriteLine("checkpoints: built in " + seconds + " s");
                    Sweep(root, folder, host, files);
                    return dll;
                }
                finally
                {
                    if (files.Directory.Exists(work)) TryDelete(work, files);
                }
            }
        }

        /// <summary>
        /// The checkpoints command: Resolve, then dotnet exec of the tool with the arguments as given, in the caller's
        /// directory, on this process's console. The tool's exit code; Unavailable (9) when it could not be built or started.
        /// </summary>
        public static int Run(CheckpointToolOptions options, NightlyHost host, IFileSystem files)
        {
            var dll = Resolve(options, host, files);
            if (dll == null) return Unavailable;
            var arguments = new List<string> { "exec", dll };
            arguments.AddRange(options.Arguments);
            try
            {
                return host.Launch(new NightlyLaunch("dotnet", arguments, options.WorkingDirectory)).ExitCode;
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException)
            {
                host.Error.WriteLine("checkpoints: dotnet did not start the checkpoint tool: " + e.Message);
                return Unavailable;
            }
        }

        /// <summary>The environment without the names starting with MSBuild (ignoring case); null stays null (inherit).</summary>
        public static IReadOnlyDictionary<string, string> WithoutMsBuild(IReadOnlyDictionary<string, string> environment)
        {
            if (environment == null) return null;
            var child = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in environment)
                if (!pair.Key.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)) child[pair.Key] = pair.Value;
            return child;
        }

        private static bool Current(string folder, string hash, IFileSystem files)
        {
            var stamp = files.Path.Combine(folder, StampName);
            return files.File.Exists(files.Path.Combine(folder, DllName)) && files.File.Exists(stamp)
                && string.Equals(files.File.ReadAllText(stamp).Trim(), hash, StringComparison.Ordinal);
        }

        // In use now: the sweep goes by this time.
        private static string Touch(string folder, string dll, IFileSystem files)
        {
            try { files.Directory.SetLastWriteTimeUtc(folder, DateTime.UtcNow); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            return dll;
        }

        // Best effort: a folder in use cannot be removed on Windows and stays for a later sweep.
        private static void Sweep(string root, string keep, NightlyHost host, IFileSystem files)
        {
            foreach (var directory in files.Directory.GetDirectories(root))
            {
                if (string.Equals(directory, keep, StringComparison.OrdinalIgnoreCase)) continue;
                var name = files.Path.GetFileName(directory);
                var stale = name.StartsWith("tmp-", StringComparison.Ordinal)
                    || Regex.IsMatch(name, "^[0-9a-f]{24}$", RegexOptions.CultureInvariant);
                if (!stale) continue;
                try
                {
                    if (host.UtcNow - files.Directory.GetLastWriteTimeUtc(directory) > Retention) TryDelete(directory, files);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
        }

        private static string Relative(string root, string path)
        {
            var full = Path.GetFullPath(path);
            var prefix = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            return full.Substring(prefix.Length).Replace('\\', '/');
        }

        private static void TryDelete(string directory, IFileSystem files)
        {
            try { files.Directory.Delete(directory, true); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }
    }
}
