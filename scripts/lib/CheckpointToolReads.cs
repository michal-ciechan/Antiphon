#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Antiphon.Checkpoints.Script
{
    // run-checkpoint.ps1's git reads answered by the checkpoint tool's own in-process readers (libgit2), when a run
    // names a tool build (ANTIPHON_CHECKPOINT_TOOL_ASSEMBLY): the source snapshot (SourceSnapshot.Capture, which
    // falls back to git itself) and the confinement reads (BuildConfinement.ScriptRead and ScriptTrackedStatus, null
    // when git must answer). The build is loaded once per process into its own load context: its Antiphon.* and
    // LibGit2Sharp copies, and their native library, resolve from its folder through its deps.json and never meet
    // the script's compiled types of the same names.
    public sealed class CheckpointToolReads : AssemblyLoadContext
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Tool> Loaded = new Dictionary<string, Tool>(StringComparer.Ordinal);

        private readonly AssemblyDependencyResolver _resolver;

        private CheckpointToolReads(string main) : base("antiphon-checkpoint-tool")
        {
            _resolver = new AssemblyDependencyResolver(main);
        }

        protected override Assembly Load(AssemblyName name)
        {
            var path = _resolver.ResolveAssemblyToPath(name);
            return path == null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string name)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(name);
            return path == null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }

        private sealed class Tool
        {
            public object Snapshot;
            public MethodInfo Capture;
            public MethodInfo Read;
            public MethodInfo TrackedStatus;
        }

        // Throws when the named build cannot be loaded or lacks one of the three entry points.
        private static Tool Of(string toolAssembly)
        {
            var main = Path.GetFullPath(toolAssembly);
            lock (Gate)
            {
                if (Loaded.TryGetValue(main, out var tool)) return tool;
                if (!File.Exists(main)) throw new FileNotFoundException("checkpoint tool assembly not found", main);
                var assembly = new CheckpointToolReads(main).LoadFromAssemblyPath(main);
                var snapshot = assembly.GetType("Antiphon.Checkpoints.SourceSnapshot", true);
                var confinement = assembly.GetType("Antiphon.Checkpoints.BuildConfinement", true);
                tool = new Tool
                {
                    Snapshot = Activator.CreateInstance(snapshot, new object[] { null }),
                    Capture = Need(snapshot.GetMethod("Capture", new[] { typeof(string) }), "SourceSnapshot.Capture"),
                    Read = Need(confinement.GetMethod("ScriptRead", new[] { typeof(string), typeof(string[]) }), "BuildConfinement.ScriptRead"),
                    TrackedStatus = Need(confinement.GetMethod("ScriptTrackedStatus", new[] { typeof(string) }), "BuildConfinement.ScriptTrackedStatus"),
                };
                Loaded[main] = tool;
                return tool;
            }
        }

        private static MethodInfo Need(MethodInfo method, string name) =>
            method ?? throw new MissingMethodException("checkpoint tool build lacks " + name);

        // Whether the named build loads with every entry point; throws (with the reason) when it does not.
        public static bool Ready(string toolAssembly) => Of(toolAssembly) != null;

        // The same deadlines as the git children these calls stand for (Invoke-CheckpointGit's 15 s per read; the git
        // snapshot's 30 s per call, two status passes): a call past it throws TimeoutException and the script
        // leaves the rest of its run to git. The abandoned read finishes or dies with the process.
        private static T Bounded<T>(Func<T> call, int milliseconds)
        {
            var task = System.Threading.Tasks.Task.Run(call);
            try
            {
                if (!task.Wait(milliseconds)) throw new TimeoutException("checkpoint tool read exceeded " + milliseconds + " ms");
            }
            catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
            {
                var inner = ex.InnerException is TargetInvocationException { InnerException: { } invoked } ? invoked : ex.InnerException;
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
            }
            return task.Result;
        }

        // The tool's observation of this repository as [commit, dirtyFiles, fingerprint, captureStatus, errorCode].
        // The tool itself turns every read failure into an unknown observation with a code.
        public static string[] Capture(string toolAssembly, string repository)
        {
            var tool = Of(toolAssembly);
            var observation = Bounded(() => tool.Capture.Invoke(tool.Snapshot, new object[] { repository }), 60000);
            var shape = observation.GetType();
            string Field(string name)
            {
                var value = shape.GetProperty(name).GetValue(observation);
                return value == null ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            }

            return new[] { Field("Commit"), Field("DirtyFiles"), Field("Fingerprint"), Field("CaptureStatus"), Field("ErrorCode") };
        }

        // git's stdout for this read, answered in process; null when git must answer.
        public static string Read(string toolAssembly, string workingDirectory, string[] args)
        {
            var tool = Of(toolAssembly);
            return Bounded(() => (string)tool.Read.Invoke(null, new object[] { workingDirectory, args }), 15000);
        }

        // The tracked paths `git status --porcelain=v2 -z --untracked-files=no` lists; null when git must answer.
        public static string[] TrackedStatus(string toolAssembly, string repository)
        {
            var tool = Of(toolAssembly);
            return Bounded(() => (string[])tool.TrackedStatus.Invoke(null, new object[] { repository }), 15000);
        }
    }
}
