using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Tests.Application;

// Literal contract boundaries, independent of either validator's implementation constant.
internal sealed record CodexCliDescriptorCase(string Field, string Name, RunnerCodexCliProbeRequest Request,
    bool Accepted, string DescriptorControl, string RunnerControl);

internal static class CodexCliDescriptorCases
{
    internal static IEnumerable<CodexCliDescriptorCase> All(RunnerCodexCliProbeRequest valid)
    {
        // PATH first: a valid absolute native executable avoids incidental filesystem refusals.
        foreach (var field in new[] { "Path", "PathExt", "Executable", "ResolutionCwd", "CodexJsPrefix" })
        foreach (var (name, value, accepted, descriptorControl, runnerControl) in new[]
        {
            ("at-limit", new string('X', 32768), true, "C1029-v19", "C1029-v13"),
            ("over-limit", new string('X', 32769), false, "C1029-pc-257", "C959-pc-117"),
            ("nul", "NUL\0sentinel", false, "C1029-pc-258", "C959-pc-244"),
            ("dollar", "${secret:C959}", false, "C1029-pc-259", "C959-pc-118"),
            ("dollar-mixed", "${SeCrEt:C959}", false, "C1029-pc-259", "C959-pc-118"),
            ("key", "{{key:C959}}", false, "C1029-pc-260", "C959-pc-245"),
            ("key-mixed", "{{KeY:C959}}", false, "C1029-pc-260", "C959-pc-245"),
        })
        {
            var request = field switch
            {
                "Executable" => valid with { Executable = value },
                "ResolutionCwd" => valid with { ResolutionCwd = value },
                "Path" => valid with { Path = value },
                "PathExt" => valid with { PathExt = value },
                "CodexJsPrefix" => valid with { CodexJsPrefix = value },
                _ => throw new InvalidOperationException(field),
            };
            yield return new(field, name, request, accepted, descriptorControl, runnerControl);
        }
    }
}
