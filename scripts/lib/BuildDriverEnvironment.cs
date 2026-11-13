using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Diagnostics;

namespace Antiphon.Build
{
    // S1 red baseline: stock preparation, before the opted-in implementation.
    public static class LinuxSharedCompilerPolicy
    {
        public static bool Enabled(bool windows, string setting) { return false; }
        public static bool Compiles(string executable, string[] args) { return false; }
        public static string SelectCpus(string allowed, int grant) { return allowed; }
        public static string Identity(string sdk, string scope, string cpus) { return sdk; }
        public static string[] Arguments(string[] args, string toolset, string pipe) { return args; }
        public static bool CacheMatches(string expected, string actual, bool trusted) { return true; }
        public static Dictionary<string, string> CompilerEnvironment(int count) { return new Dictionary<string, string>(); }
    }

    // Shared by the server, checkpoint tool and PowerShell. Apply before child startup:
    // project properties are too late to configure the driver's GC or MSBuild server.
    public sealed class BuildDriverEnvironment
    {
        public void Apply(ProcessStartInfo start)
        {
            Apply(start.EnvironmentVariables, Environment.OSVersion.Platform == PlatformID.Win32NT);
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
