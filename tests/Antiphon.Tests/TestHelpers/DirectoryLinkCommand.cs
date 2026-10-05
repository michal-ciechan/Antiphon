using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts;

namespace Antiphon.Tests.TestHelpers;

internal static class DirectoryLinkCommand
{
    internal static ScriptHarnessOptions DefaultOptions => new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));

    internal static Task<ScriptHarnessResult> RunAsync(string path, string target,
        ScriptHarnessOptions? options = null, string caseName = "Create", CancellationToken ct = default)
    {
        options ??= DefaultOptions;
        var arguments = new List<string>(options.AdditionalArguments ?? []);
        if (options.ScriptPath is null)
            arguments.AddRange(["-LinkPath", path, "-TargetPath", target]);
        var script = options.ScriptPath ?? Path.Combine(DelegateScriptRunner.RepoRoot,
            "tests", "Antiphon.Tests", "Scripts", "Fixtures", "directory-link.ps1");
        options = options with
        {
            ExecutablePath = options.ExecutablePath ?? ScriptHarnessProcessFixture.ResolveInstalledPowerShell(),
            AdditionalArguments = arguments,
        };
        return ScriptHarnessProcess.RunAsync("directory-link", "C1061", caseName, script, options, ct);
    }
}
