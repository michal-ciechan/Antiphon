// The build slot commands (unit 34 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): build-slot (it replaced
// the body of scripts/build-slot.ps1, which stays as a thin launcher because agents and other checkouts run it by path) and
// measure-linux-shared-compiler (scripts/measure-linux-shared-compiler.ps1). The behaviour is Antiphon.Nightly's (BuildSlot,
// LinuxCompilerMeasure); this file binds what is not in the core: BuildDriverEnvironment (CARD-1506, each child's start),
// LeaseTempDirectory (CARD-1981, a dotnet command's private TMPDIR on Linux) and LinuxCompilerToolset (CARD-1885), and turns
// Ctrl+C and SIGTERM into the command's cancellation (the child's tree is killed and the lease released, exit 130). The
// slot reads this process's environment through its host (ANTIPHON_BUILD_SLOTS_URL, ANTIPHON_SESSION_ID, ANTIPHON_TASK_ID,
// PATH, PATHEXT and the C589_SLOT_* offline seams); the measurement gets ANTIPHON_BUILD_SLOT_MAXCPUCOUNT, the outer lease's
// grant. In-process callers (run-daemon, qualification-launch, post-land-early-warning) take Runner(). ASCII only.
using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Antiphon.Build;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class BuildSlotCommands
    {
        internal const string BuildSlotName = "build-slot";
        internal const string MeasureLinuxSharedCompiler = "measure-linux-shared-compiler";

        internal const string Usage = BuildSlotOptions.Usage + " | " + LinuxCompilerMeasureOptions.Usage;

        private static string Checkout => SourceCheckout.Root() ?? Environment.CurrentDirectory;

        /// <summary>The parts of a child's start the core does not carry: the build-driver policy and the Linux lease TMPDIR.</summary>
        internal static BuildSlotPlatform Platform { get; } = new BuildSlotPlatform
        {
            PrepareChild = (start, grant) =>
            {
                AddBuildPlacementImport(start);
                new BuildDriverEnvironment().Apply(start, grant);
            },
            LeaseTemp = () =>
            {
                var handle = LeaseTempDirectory.TryCreate();
                return handle == null ? null : new BuildSlotTemp(handle.DirectoryPath, handle);
            },
        };

        /// <summary>The build slot for an in-process caller: this process's console unless it captures.</summary>
        internal static BuildSlotRunner Runner() => BuildSlot.Runner(Platform, Console.Out, Console.Error);

        private static void AddBuildPlacementImport(ProcessStartInfo start)
        {
            if (!OperatingSystem.IsLinux() || !BuildSlotCommandLine.IsDotnet(start.FileName)
                || start.ArgumentList.Count == 0 || start.ArgumentList[0] != "build")
                return;

            // Use the launcher checkout. An older project still imports this guard for restore,
            // including the cross-targeting evaluation used by SDK projects with TargetFrameworks.
            var targets = Path.Combine(Checkout, "scripts", "guard-build-placement.targets");
            var separator = start.ArgumentList.IndexOf("--");
            var insertion = separator < 0 ? start.ArgumentList.Count : separator;
            start.ArgumentList.Insert(insertion, "-p:CustomBeforeMicrosoftCommonTargets=" + targets);
            start.ArgumentList.Insert(insertion + 1, "-p:CustomBeforeMicrosoftCommonCrossTargetingTargets=" + targets);
        }

        // The command's own code; 4 no slot within the wait; 1 the command cannot be resolved or started; 130 interrupted;
        // usage 2 (a missing command among them).
        internal static int RunBuildSlot(string[] args)
        {
            var options = BuildSlotOptions.Parse(args.Skip(1).ToArray(), Environment.CurrentDirectory);
            using var cancellation = new CancellationTokenSource();
            using var interrupt = Register(PosixSignal.SIGINT, cancellation);
            using var terminate = Register(PosixSignal.SIGTERM, cancellation);
            return BuildSlot.Run(options, BuildSlotHost.Production(Console.Out, Console.Error, Platform, cancellation.Token));
        }

        // Exit 0 accepted; 1 refused (one stderr line), the build failed or the sample was not accepted; usage 2.
        internal static int RunMeasureLinuxSharedCompiler(string[] args)
        {
            var options = LinuxCompilerMeasureOptions.Parse(args.Skip(1).ToArray(), Checkout, Environment.GetEnvironmentVariable("ANTIPHON_BUILD_SLOT_MAXCPUCOUNT"));
            return LinuxCompilerMeasure.Run(options, NightlyHost.Production(Console.Out, Console.Error), new LinuxCompilerMeasureSeams
            {
                IsLinux = OperatingSystem.IsLinux(),
                Toolset = new Toolset(),
                Files = new FileSystem(),
                FindTool = OnPath,
            });
        }

        // The signal no longer ends this process: the command sees the cancellation, stops its child and releases the lease.
        // review-live-github (ReviewEngineCommands) registers the same two around its in-process slot.
        internal static IDisposable Register(PosixSignal signal, CancellationTokenSource cancellation)
        {
            try
            {
                return PosixSignalRegistration.Create(signal, context =>
                {
                    context.Cancel = true;
                    cancellation.Cancel();
                });
            }
            catch (PlatformNotSupportedException)
            {
                return null;
            }
        }

        // A tool on PATH, as Get-Command found it (PATHEXT on Windows).
        private static string OnPath(string name) =>
            BuildSlotTarget.Resolve(new[] { name }, Environment.CurrentDirectory, Environment.GetEnvironmentVariable, OperatingSystem.IsWindows(), new FileSystem())?.Resolved;

        private sealed class Toolset : ILinuxCompilerToolset
        {
            public string Prepare(ProcessStartInfo start, int grant, int idleSeconds, string isolation, bool capped) =>
                LinuxCompilerToolset.Prepare(start, grant, idleSeconds, isolation, capped);

            public string Probe(string executable, string[] arguments, string workingDirectory, StringDictionary environment) =>
                LinuxCompilerToolset.Probe(executable, arguments, workingDirectory, environment);

            public string StatusValue(string path, string key) => LinuxCompilerToolset.StatusValue(path, key);
        }
    }
}
