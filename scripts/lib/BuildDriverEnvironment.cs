using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Diagnostics;

namespace Antiphon.Build
{
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
