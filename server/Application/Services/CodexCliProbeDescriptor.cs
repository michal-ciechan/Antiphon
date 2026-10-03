using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Pure launcher-resolution data for explicit diagnostics; never a dispatch permission.</summary>
internal sealed record CodexCliProbeDescriptor(
    string RunnerId, string? Model, Guid? ProfileRevisionId, RunnerCodexCliProbeRequest? Request,
    string? Error = null)
{
    internal static CodexCliProbeDescriptor FromSpec(string runner, string? model, Guid? revision,
        string executable, IReadOnlyList<string> args, IReadOnlyDictionary<string,string> env, string cwd)
    {
        var node = Path.GetFileName(executable.Replace('\\','/'));
        var prefix = node.Equals("node", StringComparison.OrdinalIgnoreCase) || node.Equals("node.exe", StringComparison.OrdinalIgnoreCase)
            ? args.FirstOrDefault() : null;
        string? Get(string name) => env.FirstOrDefault(e => e.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        if (env.Any(e => IsLoaderName(e.Key) && !string.IsNullOrEmpty(e.Value)))
            return new(runner, model, revision, null, "launcher_unverified");
        var request = new RunnerCodexCliProbeRequest(executable, cwd, Get("PATH"), Get("PATHEXT"), prefix);
        if (new[] { request.Executable, request.ResolutionCwd, request.Path, request.PathExt, request.CodexJsPrefix }
            .Any(v => v is not null && (v.Length > 32768 || v.Contains('\0') || v.Contains("${secret:", StringComparison.OrdinalIgnoreCase)
                || v.Contains("{{key:", StringComparison.OrdinalIgnoreCase))))
            return new(runner, model, revision, null, "launcher_unverified");
        return new(runner, model, revision, request);
    }

    private static bool IsLoaderName(string name) => name.Equals("NODE_OPTIONS", StringComparison.OrdinalIgnoreCase)
        || name.Equals("NODE_PATH", StringComparison.OrdinalIgnoreCase) || name.Equals("LD_PRELOAD", StringComparison.OrdinalIgnoreCase)
        || name.Equals("LD_LIBRARY_PATH", StringComparison.OrdinalIgnoreCase) || name.Equals("DOTNET_STARTUP_HOOKS", StringComparison.OrdinalIgnoreCase);

}
