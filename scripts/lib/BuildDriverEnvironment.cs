using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Threading;

namespace Antiphon.Build
{
    // Pure policy; C# 5 is intentional (Windows PowerShell compiles this file).
    public static class LinuxSharedCompilerPolicy
    {
        public static bool Enabled(bool windows, string setting) { return !windows && setting == "1"; }

        public static bool Compiles(string executable, string[] args)
        {
            if (!string.Equals(Path.GetFileName(executable), "dotnet", StringComparison.OrdinalIgnoreCase) || args.Length == 0)
                return false;
            string verb = args[0].ToLowerInvariant();
            if (verb != "build" && verb != "publish" && verb != "pack" && verb != "msbuild" && verb != "test" && verb != "run")
                return false;
            bool evaluation = false, target = false;
            foreach (string arg in args)
            {
                if (arg == "--") break;
                if ((verb == "test" || verb == "run") && arg == "--no-build") return false;
                string lower = arg.ToLowerInvariant();
                if (lower.StartsWith("-getproperty") || lower.StartsWith("-getitem") || lower.StartsWith("/getproperty") || lower.StartsWith("/getitem")) evaluation = true;
                if (lower.StartsWith("-t:") || lower.StartsWith("-target:") || lower.StartsWith("/t:") || lower.StartsWith("/target:")) target = true;
            }
            return !evaluation || target;
        }

        public static string SelectCpus(string allowed, int grant)
        {
            if (grant <= 0 || grant > 4096 || string.IsNullOrWhiteSpace(allowed)) throw new ArgumentException("invalid CPU grant/affinity");
            var cpus = new SortedSet<int>();
            foreach (string part in allowed.Split(','))
            {
                string[] range = part.Trim().Split('-');
                int first, last;
                if (range.Length > 2 || !int.TryParse(range[0], out first) || first < 0 || first > 4095)
                    throw new ArgumentException("invalid CPU affinity");
                last = first;
                if (range.Length == 2 && (!int.TryParse(range[1], out last) || last < first || last > 4095))
                    throw new ArgumentException("invalid CPU affinity range");
                for (int cpu = first; cpu <= last; cpu++) cpus.Add(cpu);
            }
            var chosen = new List<string>();
            foreach (int cpu in cpus) { if (chosen.Count == grant) break; chosen.Add(cpu.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            return string.Join(",", chosen.ToArray());
        }

        public static string Identity(string sdk, string scope, string cpus)
        {
            return Hash("c1885-v1\n" + sdk + "\n" + scope + "\n" + cpus + "\nheap=100000000;idle=120");
        }

        public static string[] Arguments(string[] args, string toolset, string pipe)
        {
            foreach (string arg in args)
            {
                if (arg == "--") break;
                if (arg.StartsWith("@")) throw new ArgumentException("response-file toolset cannot be qualified");
                string lower = arg.ToLowerInvariant();
                foreach (string key in ConflictingKeys)
                    if (lower.IndexOf(key.ToLowerInvariant() + "=", StringComparison.Ordinal) >= 0)
                        throw new ArgumentException("explicit compiler property: " + key);
            }
            var result = new List<string>();
            if (args.Length == 0) throw new ArgumentException("missing command");
            result.Add(args[0]);
            result.Add("-p:RoslynTargetsPath=" + toolset);
            result.Add("-p:RoslynTasksAssembly=" + Path.Combine(toolset, "Microsoft.Build.Tasks.CodeAnalysis.dll"));
            result.Add("-p:SharedCompilationId=" + pipe);
            for (int i = 1; i < args.Length; i++) result.Add(args[i]);
            return result.ToArray();
        }

        public static readonly string[] ConflictingKeys = { "RoslynTargetsPath", "RoslynTasksAssembly", "SharedCompilationId",
            "UseSharedCompilation", "CscToolPath", "CscToolExe", "VbcToolPath", "VbcToolExe" };

        public static bool CacheMatches(string expected, string actual, bool trusted)
        { return trusted && !string.IsNullOrEmpty(expected) && string.Equals(expected, actual, StringComparison.Ordinal); }

        public static Dictionary<string, string> CompilerEnvironment(int count)
        {
            if (count <= 0) throw new ArgumentException("invalid compiler processor count");
            return new Dictionary<string, string> { { "DOTNET_PROCESSOR_COUNT", count.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { "DOTNET_GCHeapHardLimit", "100000000" } };
        }

        public static string Hash(string value)
        {
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
        internal static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    }

    // Filesystem/process boundary. Never called for Windows or policy-off commands.
    // Entries are immutable and user-owned; there is intentionally no cache sweeper.
    public static class LinuxCompilerToolset
    {
        public static IList<string> Argv(ProcessStartInfo start)
        {
            var property = typeof(ProcessStartInfo).GetProperty("ArgumentList");
            if (property == null || !string.IsNullOrEmpty(start.Arguments)) throw new ArgumentException("literal ArgumentList required");
            return (IList<string>)property.GetValue(start, null);
        }

        public static string StatusValue(string path, string key)
        {
            foreach (string line in File.ReadAllLines(path))
                if (line.StartsWith(key + ":", StringComparison.Ordinal)) return line.Substring(key.Length + 1).Trim();
            throw new IOException("missing Linux process status field");
        }

        public static string FindTool(string name, StringDictionary environment)
        {
            string path = environment.ContainsKey("PATH") ? environment["PATH"] : "";
            foreach (string directory in path.Split(Path.PathSeparator))
            {
                if (!Path.IsPathRooted(directory)) continue;
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate)) return candidate;
            }
            throw new IOException("required utility unavailable: " + name);
        }

        // Only bounded identity/utility children, never a compiler or a live PID.
        public static string Probe(string executable, string[] arguments, string cwd, StringDictionary environment)
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = string.IsNullOrEmpty(cwd) ? Environment.CurrentDirectory : cwd };
            foreach (string argument in arguments) Argv(start).Add(argument);
            start.EnvironmentVariables.Clear();
            foreach (string key in environment.Keys) start.EnvironmentVariables[key] = environment[key];
            start.EnvironmentVariables["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
            using (var process = Process.Start(start))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(15000)) { process.Kill(); process.WaitForExit(1000); throw new IOException("compiler preparation probe timeout"); }
                if (!stdout.Wait(1000) || !stderr.Wait(1000)) throw new IOException("compiler preparation probe drain timeout");
                if (process.ExitCode != 0 || stdout.Result.Length > 131072 || stderr.Result.Length > 131072)
                    throw new IOException("compiler preparation utility failed: " + Path.GetFileName(executable));
                return stdout.Result.Trim();
            }
        }

        private static void NoLinks(string path)
        {
            for (string current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("linked compiler cache/toolset path");
        }

        private static void Trusted(string path, string uid, string stat, StringDictionary env)
        {
            NoLinks(path);
            string result = Probe(stat, new[] { "-c", "%u:%a:%h", "--", path }, "/", env);
            string[] fields = result.Split(':');
            int mode = Convert.ToInt32(fields[1], 8);
            if (fields[0] != uid || (mode & 18) != 0 || (File.Exists(path) && fields[2] != "1"))
                throw new IOException("untrusted compiler cache owner/mode/link count");
        }

        private static void Files(string root, List<string> files, Stopwatch budget)
        {
            if (budget.Elapsed.TotalSeconds > 30) throw new IOException("toolset preparation deadline");
            NoLinks(root);
            foreach (string file in Directory.GetFiles(root))
            {
                NoLinks(file);
                if (new FileInfo(file).Length > 134217728 || files.Count >= 500) throw new IOException("unbounded compiler toolset");
                files.Add(file);
            }
            foreach (string directory in Directory.GetDirectories(root)) Files(directory, files, budget);
        }

        private static string Fingerprint(string root, Stopwatch budget)
        {
            var files = new List<string>(); Files(root, files, budget); files.Sort(StringComparer.Ordinal);
            var text = new StringBuilder();
            foreach (string file in files)
            {
                if (Path.GetFileName(file) == "manifest.sha256") continue;
                if (budget.Elapsed.TotalSeconds > 30) throw new IOException("toolset hashing deadline");
                using (var stream = File.OpenRead(file))
                using (var sha = SHA256.Create()) text.Append(file.Substring(root.Length)).Append('|').Append(LinuxSharedCompilerPolicy.Hex(sha.ComputeHash(stream))).Append('\n');
            }
            return LinuxSharedCompilerPolicy.Hash(text.ToString());
        }

        private static string Quote(string value) { return "'" + value.Replace("'", "'\"'\"'") + "'"; }

        // Alternate idle/isolation/uncapped parameters are for explicitly owned experiments.
        // Normal callers always use capped=true, idle=120, isolation="".
        public static string Prepare(ProcessStartInfo start, int grant, int idleSeconds, string isolation, bool capped)
        {
            var args = new List<string>(Argv(start)).ToArray();
            LinuxSharedCompilerPolicy.Arguments(args, "/preflight", "preflight");
            foreach (string conflictKey in LinuxSharedCompilerPolicy.ConflictingKeys)
                if (start.EnvironmentVariables.ContainsKey(conflictKey) && !string.IsNullOrEmpty(start.EnvironmentVariables[conflictKey]))
                    throw new ArgumentException("explicit compiler environment: " + conflictKey);
            if (idleSeconds < 1 || idleSeconds > 600) throw new ArgumentException("invalid compiler idle timeout");
            string cpus = LinuxSharedCompilerPolicy.SelectCpus(StatusValue("/proc/self/status", "Cpus_allowed_list"), grant);
            string uid = StatusValue("/proc/self/status", "Uid").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
            var env = start.EnvironmentVariables;
            string dotnet = Path.IsPathRooted(start.FileName) ? start.FileName : FindTool("dotnet", env);
            string taskset = FindTool("taskset", env), stat = FindTool("stat", env), chmod = FindTool("chmod", env);
            if (capped) Probe(taskset, new[] { "-c", cpus, FindTool("true", env) }, start.WorkingDirectory, env);
            var resolution = Stopwatch.StartNew();
            string info = Probe(dotnet, new[] { "--info" }, start.WorkingDirectory, env);
            string sdk = null;
            foreach (string line in info.Split('\n')) if (line.Trim().StartsWith("Base Path:", StringComparison.Ordinal)) sdk = line.Trim().Substring(10).Trim().TrimEnd('/');
            if (sdk == null || Path.GetFileName(sdk) != "10.0.404") throw new IOException("selected SDK is not qualified (requires native witness)");
            string source = Path.Combine(sdk, "Roslyn");
            foreach (string file in new[] { "Microsoft.Build.Tasks.CodeAnalysis.dll", "Microsoft.CSharp.Core.targets", "bincore/VBCSCompiler", "bincore/VBCSCompiler.dll", "bincore/VBCSCompiler.runtimeconfig.json" })
                if (!File.Exists(Path.Combine(source, file))) throw new IOException("incomplete selected SDK compiler toolset");
            resolution.Stop();
            var budget = Stopwatch.StartNew();
            string content = Fingerprint(source, budget);
            string scope = uid + "\n" + File.ReadAllText("/proc/self/cgroup") + "\n" + Probe(FindTool("readlink", env), new[] { "/proc/self/ns/mnt" }, "/", env);
            string key = LinuxSharedCompilerPolicy.Identity(content, scope + "\n" + isolation + "\nidle=" + idleSeconds + ";capped=" + capped, cpus);
            string pipe = "ac1885-" + key.Substring(0, 40);
            string cacheBase = env.ContainsKey("XDG_CACHE_HOME") ? env["XDG_CACHE_HOME"] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
            if (!Path.IsPathRooted(cacheBase)) throw new IOException("compiler cache must be absolute");
            string cache = Path.Combine(cacheBase, "antiphon-compiler");
            NoLinks(cache);
            if (!Directory.Exists(cache)) { Directory.CreateDirectory(cache); Probe(chmod, new[] { "700", "--", cache }, "/", env); }
            Trusted(cache, uid, stat, env);
            string entry = Path.Combine(cache, key), lockPath = Path.Combine(cache, key + ".lock");
            NoLinks(lockPath);
            var lockWait = Stopwatch.StartNew();
            FileStream gate = null;
            while (gate == null)
            {
                try { gate = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { if (lockWait.Elapsed.TotalSeconds >= 30) throw new IOException("compiler cache lock timeout"); Thread.Sleep(50); }
            }
            lockWait.Stop();
            bool hit = Directory.Exists(entry);
            using (gate)
            {
                Probe(chmod, new[] { "600", "--", lockPath }, "/", env); Trusted(lockPath, uid, stat, env);
                budget.Restart();
                if (!hit)
                {
                    string staging = entry + ".building-" + Guid.NewGuid().ToString("N");
                    Directory.CreateDirectory(staging); Probe(chmod, new[] { "700", "--", staging }, "/", env);
                    var files = new List<string>(); Files(source, files, budget);
                    foreach (string file in files)
                    {
                        string target = staging + file.Substring(source.Length);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file, target, false);
                    }
                    var launcher = new StringBuilder("#!/bin/sh\n");
                    if (capped)
                    {
                        // Only this launcher process (about to become the compiler) is changed.
                        launcher.Append("if ").Append(Quote(taskset)).Append(" -pc ").Append(Quote(cpus)).Append(" \"$$\" >/dev/null 2>&1; then\n");
                        foreach (var pair in LinuxSharedCompilerPolicy.CompilerEnvironment(cpus.Split(',').Length))
                            launcher.Append("export ").Append(pair.Key).Append('=').Append(Quote(pair.Value)).Append('\n');
                        launcher.Append("exec ").Append(Quote(dotnet)).Append(" exec ").Append(Quote(Path.Combine(source, "bincore/VBCSCompiler.dll")))
                            .Append(" -timeout:").Append(idleSeconds).Append(" \"$@\"\nfi\n")
                            .Append("echo 'LINUX COMPILER fallback reason=affinity-denied stock compiler' >&2\n");
                    }
                    else launcher.Append("exec ").Append(Quote(dotnet)).Append(" exec ").Append(Quote(Path.Combine(source, "bincore/VBCSCompiler.dll")))
                        .Append(" -timeout:").Append(idleSeconds).Append(" \"$@\"\n");
                    launcher.Append("exec ").Append(Quote(Path.Combine(source, "bincore/VBCSCompiler"))).Append(" \"$@\"\n");
                    File.WriteAllText(Path.Combine(staging, "bincore/VBCSCompiler"), launcher.ToString(), Encoding.ASCII);
                    string fingerprint = Fingerprint(staging, budget);
                    File.WriteAllText(Path.Combine(staging, "manifest.sha256"), content + "\n" + key + "\n" + fingerprint + "\n", Encoding.ASCII);
                    var seal = new List<string>(); Files(staging, seal, budget);
                    foreach (string file in seal) Probe(chmod, new[] { "500", "--", file }, "/", env);
                    foreach (string directory in Directory.GetDirectories(staging, "*", SearchOption.AllDirectories)) Probe(chmod, new[] { "500", "--", directory }, "/", env);
                    Probe(chmod, new[] { "500", "--", staging }, "/", env);
                    Directory.Move(staging, entry);
                }
                Trusted(entry, uid, stat, env);
                var cached = new List<string>(); Files(entry, cached, budget);
                // One stat invocation checks the entire bounded inventory, including directories.
                var statArgs = new List<string> { "-c", "%u:%a:%h", "--" }; statArgs.AddRange(cached);
                statArgs.AddRange(Directory.GetDirectories(entry, "*", SearchOption.AllDirectories));
                int statOrdinal = 0;
                foreach (string row in Probe(stat, statArgs.ToArray(), "/", env).Split('\n'))
                {
                    string[] parts = row.Trim().Split(':');
                    int fileMode = Convert.ToInt32(parts[1], 8);
                    if (parts[0] != uid || (fileMode & 18) != 0 || (statOrdinal < cached.Count && parts[2] != "1"))
                        throw new IOException("untrusted compiler cache contents");
                    if (statOrdinal < cached.Count && cached[statOrdinal] == Path.Combine(entry, "bincore/VBCSCompiler") && (fileMode & 64) == 0)
                        throw new IOException("private compiler launcher is not executable");
                    statOrdinal++;
                }
                string manifest = Path.Combine(entry, "manifest.sha256"); NoLinks(manifest);
                string[] fields = File.ReadAllLines(manifest);
                if (fields.Length != 3 || fields[0] != content || fields[1] != key || !LinuxSharedCompilerPolicy.CacheMatches(fields[2], Fingerprint(entry, budget), true))
                    throw new IOException("compiler cache integrity mismatch");
            }
            string[] prepared = LinuxSharedCompilerPolicy.Arguments(args, entry, pipe);
            var argv = Argv(start); argv.Clear(); foreach (string token in prepared) argv.Add(token);
            return "LINUX COMPILER policy=c1885-v1 sdk=" + sdk + " sdkContent=" + content + " identity=" + key + " pipe=" + pipe + " cpus=" + cpus
                + " heap=" + (capped ? "100000000" : "stock") + " idle=" + idleSeconds + " cache=" + (hit ? "hit" : "copy")
                + " resolutionMs=" + resolution.ElapsedMilliseconds + " prepareMs=" + budget.ElapsedMilliseconds + " lockMs=" + lockWait.ElapsedMilliseconds
                + " argvSha256=" + LinuxSharedCompilerPolicy.Hash(string.Join("\n", prepared));
        }
    }

    // Shared by the server, checkpoint tool and PowerShell. Apply before child startup:
    // project properties are too late to configure the driver's GC or MSBuild server.
    public sealed class BuildDriverEnvironment
    {
        public string Preparation { get; private set; }

        public void Apply(ProcessStartInfo start)
        {
            Apply(start, 0);
        }

        public void Apply(ProcessStartInfo start, int maxCpuCount)
        {
            bool windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
            Apply(start.EnvironmentVariables, windows);
            var environment = start.EnvironmentVariables;
            if (!LinuxSharedCompilerPolicy.Enabled(windows, environment.ContainsKey("ANTIPHON_LINUX_COMPILER_POLICY")
                ? environment["ANTIPHON_LINUX_COMPILER_POLICY"] : null)) return;
            if (!string.Equals(Path.GetFileName(start.FileName), "dotnet", StringComparison.OrdinalIgnoreCase)) return;
            IList<string> argv = null;
            string[] original = null;
            try
            {
                argv = LinuxCompilerToolset.Argv(start);
                original = new List<string>(argv).ToArray();
                if (!LinuxSharedCompilerPolicy.Compiles(start.FileName, original)) return;
                string provenance = maxCpuCount > 0 ? "typed" : "argv";
                foreach (string arg in original)
                {
                    if (arg == "--") break;
                    string lower = arg.ToLowerInvariant();
                    if (lower.StartsWith("-maxcpucount:") || lower.StartsWith("/maxcpucount:") || lower.StartsWith("-m:") || lower.StartsWith("/m:"))
                    {
                        int requested;
                        if (!int.TryParse(arg.Substring(arg.IndexOf(':') + 1), out requested) || requested <= 0)
                            throw new ArgumentException("invalid explicit CPU count");
                        maxCpuCount = maxCpuCount > 0 ? Math.Min(maxCpuCount, requested) : requested;
                    }
                }
                if (maxCpuCount <= 0 && environment.ContainsKey("ANTIPHON_BUILD_SLOT_MAXCPUCOUNT"))
                {
                    int.TryParse(environment["ANTIPHON_BUILD_SLOT_MAXCPUCOUNT"], out maxCpuCount);
                    provenance = "wrapper";
                }
                if (maxCpuCount <= 0) throw new ArgumentException("no positive build CPU grant");
                Preparation = LinuxCompilerToolset.Prepare(start, maxCpuCount, 120, "", true) + " grantSource=" + provenance;
            }
            catch (Exception error)
            {
                // Preparation is atomic to the caller, including unsupported explicit choices.
                // Preserve the stock command and sharing behavior on every policy refusal.
                if (argv != null && original != null) { argv.Clear(); foreach (string arg in original) argv.Add(arg); }
                string reason = error.Message.Replace('\r', ' ').Replace('\n', ' ');
                if (reason.Length > 240) reason = reason.Substring(0, 240);
                Preparation = "LINUX COMPILER fallback=stock reason=" + reason;
            }
            Console.Error.WriteLine(Preparation);
        }

        public void Apply(StringDictionary environment, bool windows)
        {
            foreach (var pair in GetOverrides(environment, windows))
                environment[pair.Key] = pair.Value;
        }

        public Dictionary<string, string> GetOverrides(StringDictionary environment, bool windows)
        {
            var values = new Dictionary<string, string>();
            // ProcessStartInfo's StringDictionary adapter throws for absent keys.
            if (!windows || (environment.ContainsKey("ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT") &&
                environment["ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT"] == "1"))
                return values;

            values["UseSharedCompilation"] = "false";
            values["DOTNET_gcServer"] = "0";
            values["DOTNET_gcConcurrent"] = "0";
            values["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
            values["MSBUILDDISABLENODEREUSE"] = "1";
            // SDK CLI admission uses this switch as well as the requested USE setting.
            values["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
            return values;
        }
    }
}
