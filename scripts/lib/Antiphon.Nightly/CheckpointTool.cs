// Unit 20 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the checkpoint tool (tools/Antiphon.Checkpoints)
// built from a checkout and run as a child, `antiphon-scripts checkpoints <verb> [arguments...]`. scripts/run-checkpoint.ps1
// (its row verb) and scripts/validate-checkpoint-receipt.ps1 (its validate verb) are thin launchers of this command, and
// since unit 59 the server's receipt validator and sh callers start the command directly. The tool stays its own program
// (it carries libgit2, Roslyn and YamlDotNet), so this core never references it: the command runs the tool's published
// build with dotnet exec, in the caller's directory and on this process's console, with the arguments as given.
//
// Unit 59: the tool is published the way the cached launcher publishes the CLI (scripts/lib/Antiphon.CliLauncher, Product
// below): its inputs are its project graph by CliInputs' rules (every file of the tool's project and the projects it
// references, the files they link, the build files above them), its identity CliIdentity's, its cache
// <checkout>/bin-launcher/antiphon-checkpoints/ with the pointer `current`, one folder per identity, the build lock, the
// shared incremental build (Release, quiet, two nodes, no reused nodes or compiler server, without the caller's MSBuild*
// environment, its output to build.log there) and pruning. A warm call compares the pointer's manifest with the inputs by
// size and time (the bytes only where those differ) instead of hashing every source. A row keeps the folder it runs from:
// this process holds that folder's lease until the tool exits, so pruning skips it. Exit: the tool's own code; 9 when the
// tool could not be built or started (one stderr line, as the launchers' own exit 9). Plain C#: no PowerShell types. ASCII.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.CliLauncher;

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

        /// <summary>Exit code when the tool could not be built or started.</summary>
        public const int Unavailable = 9;

        /// <summary>The tool as the cached launcher publishes it: bin-launcher/antiphon-checkpoints/&lt;identity&gt;/.</summary>
        public static CliProduct Product { get; } = new CliProduct("antiphon-checkpoints", ProjectRelative, "Antiphon.Checkpoints",
            "the checkpoint tool", "checkpoints");

        /// <summary>The folder the builds live in: &lt;checkout&gt;/bin-launcher/antiphon-checkpoints.</summary>
        public static string Root(string checkout, IFileSystem files) => files.Path.Combine(checkout, "bin-launcher", Product.CacheName);

        /// <summary>The tool's build inputs in a checkout (CliInputs' rules from its project), ordered by name.</summary>
        public static IReadOnlyList<CliInput> Inputs(string checkout) => CliInputs.Discover(checkout, ProjectRelative);

        /// <summary>
        /// The tool's DLL for the checkout's current inputs: the folder the pointer names when its manifest matches them, else
        /// the one CliPublisher publishes (building first when no folder holds their identity). Null after one line on
        /// host.Error when the project is missing or the build failed, could not start or did not get the lock in time.
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
            var current = Published(checkout);
            if (current != null) return current;

            var started = host.Clock.GetTimestamp();
            var built = false;
            var dll = CliPublisher.Ensure(checkout, new CliPublishOptions
            {
                Error = host.Error,
                UtcNow = () => host.UtcNow,
                Sleep = host.Sleep,
                Build = request =>
                {
                    built = true;
                    host.Error.WriteLine("checkpoints: building the checkpoint tool");
                    var build = host.Launch(new NightlyLaunch("dotnet", request.Arguments, request.Checkout,
                        Environment: WithoutMsBuild(options.Environment), CaptureOutput: true));
                    return new CliBuildResult { ExitCode = build.ExitCode, Log = build.LogText ?? "" };
                },
            }, Product);
            if (dll != null && built)
                host.Error.WriteLine("checkpoints: built in " + (int)Math.Round(host.Clock.GetElapsedTime(started).TotalSeconds, MidpointRounding.ToEven) + " s");
            return dll;
        }

        /// <summary>
        /// The checkpoints command: Resolve, then dotnet exec of the tool with the arguments as given, in the caller's
        /// directory, on this process's console, holding the tool folder's lease until the tool exits. The tool's exit code;
        /// Unavailable (9) when it could not be built or started.
        /// </summary>
        public static int Run(CheckpointToolOptions options, NightlyHost host, IFileSystem files)
        {
            var dll = Resolve(options, host, files);
            if (dll == null) return Unavailable;
            var arguments = new List<string> { "exec", dll };
            arguments.AddRange(options.Arguments);
            using var lease = CliCache.OpenLease(Path.GetDirectoryName(dll));
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

        // The warm path, without the lock: the folder the pointer names, when its manifest still matches the inputs (the
        // recorded size and time, else the bytes). Anything unreadable falls through to the locked publish.
        private static string Published(string checkout)
        {
            try
            {
                var cache = new CliCache(checkout, Product);
                var name = cache.ReadPointer();
                if (name == null) return null;
                var folder = cache.FolderOf(name);
                var dll = Path.Combine(folder, DllName);
                var stamp = CliCache.Stamp(folder);
                return stamp != null && File.Exists(dll) && CliIdentity.Matches(Inputs(checkout), stamp, Product.Recipe) ? dll : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is CliInputException) { return null; }
        }
    }
}
