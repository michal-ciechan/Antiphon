using System.IO.Abstractions;
using Antiphon.Nightly;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 66 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the desktop proof's real bounded children
/// (C1609BoundedProcesses) with only the transport mapped, as the PowerShell tests' Invoke-C1609HostGit shims mapped it: a fixture
/// origin URL in a Git call becomes its local bare repository, and file transport is allowed after the proof's own protocol policy.
/// Git reads no system or global configuration and never prompts. Around, when set, sees each Git call's arguments (after the
/// policy options, unmapped) and requested working directory, as the shims' $GitArgs and $WorkingDirectory, and runs the real call
/// through its continuation (or throws, which the proof reads as a Git failure). ssh and scp calls run unmapped.
/// </summary>
internal sealed class C1609MappedGit : IC1609Processes
{
    /// <summary>The policy options the proof puts before every Git call (six -c pairs).</summary>
    public const int PolicyArguments = 12;

    public delegate C1609ProcessResult Shim(IReadOnlyList<string> gitArgs, string workingDirectory, Func<C1609ProcessResult> run);

    private readonly IReadOnlyDictionary<string, string> _origins;
    private readonly C1609BoundedProcesses _inner;

    public C1609MappedGit(IReadOnlyDictionary<string, string> origins, Func<string>? currentDirectory = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        _origins = origins;
        var overlay = new Dictionary<string, string>(StringComparer.Ordinal) { ["GIT_CONFIG_SYSTEM"] = "/dev/null", ["GIT_CONFIG_GLOBAL"] = "/dev/null", ["GIT_TERMINAL_PROMPT"] = "0" };
        foreach (var pair in environment ?? new Dictionary<string, string>()) overlay[pair.Key] = pair.Value;
        _inner = new C1609BoundedProcesses(new FileSystem(), currentDirectory, overlay);
    }

    public Shim? Around { get; init; }

    public C1609ProcessResult Run(C1609ProcessCall call)
    {
        if (call.FileName != "git") return _inner.Run(call);
        var gitArgs = call.Arguments.Skip(PolicyArguments).ToArray();
        C1609ProcessResult Real() => _inner.Run(new C1609ProcessCall
        {
            FileName = call.FileName,
            Arguments = call.Arguments.Take(PolicyArguments).Concat(["-c", "protocol.file.allow=always"])
                .Concat(gitArgs.Select(a => _origins.TryGetValue(a, out var local) ? local : a)).ToArray(),
            TimeoutMs = call.TimeoutMs,
            WorkingDirectory = call.WorkingDirectory,
            InputText = call.InputText,
            OutputFile = call.OutputFile,
            CaptureOutput = call.CaptureOutput,
            GitEnvironment = call.GitEnvironment,
        });
        return Around == null ? Real() : Around(gitArgs, call.WorkingDirectory, Real);
    }
}
