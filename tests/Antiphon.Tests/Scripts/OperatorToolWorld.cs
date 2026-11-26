using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 51 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the tool seam of the W8 operator commands in a test.
/// It starts nothing: Find answers from <see cref="Found"/> (a name maps to a fake path, or is absent), and each Run is recorded
/// and answered by <see cref="Behaviour"/> (exit 0 with no output when there is none). Thread-safe.
/// </summary>
public sealed class FakeOperatorTools : IOperatorToolSeams
{
    private readonly object _gate = new();
    private readonly List<OperatorToolCall> _calls = [];

    /// <summary>The names Find answers, with the path it answers.</summary>
    public Dictionary<string, string> Found { get; } = new(StringComparer.Ordinal);

    /// <summary>The answer to a run; null answers exit 0 with no output.</summary>
    public Func<OperatorToolCall, OperatorToolResult>? Behaviour { get; set; }

    /// <summary>The runs so far, in order.</summary>
    public IReadOnlyList<OperatorToolCall> Calls
    {
        get { lock (_gate) return _calls.ToArray(); }
    }

    /// <inheritdoc />
    public string? Find(string name) => Found.TryGetValue(name, out var path) ? path : null;

    /// <inheritdoc />
    public OperatorToolResult Run(OperatorToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        lock (_gate) _calls.Add(call);
        return Behaviour?.Invoke(call) ?? OperatorToolResult.Of(0);
    }
}

/// <summary>
/// A temporary root, the host's output, error and clock, and an environment of the test's own (never this process's), for the
/// W8 operator commands. Dispose removes the root.
/// </summary>
public sealed class OperatorToolWorld : IDisposable
{
    private readonly NightlyFixture _fixture;

    /// <summary>A fresh root; prefix names the test family.</summary>
    public OperatorToolWorld(string prefix) => _fixture = NightlyFixture.Create(prefix);

    /// <summary>The root.</summary>
    public string Root => _fixture.Root;

    /// <summary>The host's environment: only what the test sets.</summary>
    public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

    /// <summary>The host's clock.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    /// <summary>The tools.</summary>
    public FakeOperatorTools Tools { get; } = new();

    /// <summary>The host's standard output so far.</summary>
    public string Out => _fixture.Out.ToString().ReplaceLineEndings("\n");

    /// <summary>The host's standard error so far.</summary>
    public string Error => _fixture.Error.ToString().ReplaceLineEndings("\n");

    /// <summary>The output lines so far.</summary>
    public string[] Lines => Out.TrimEnd('\n').Split('\n');

    /// <summary>A path under the root.</summary>
    public string PathOf(params string[] parts) => _fixture.PathOf(parts);

    /// <summary>A host over fakes with this world's streams, clock and environment.</summary>
    public NightlyHost Host() => new()
    {
        Clock = Clock,
        Git = new FakeGit().Run,
        Launcher = new RecordingLauncher(),
        Processes = new FakeProcessTable(),
        Probe = new FakeHostProbe(),
        Out = _fixture.Out,
        Error = _fixture.Error,
        Trace = TextWriter.Null,
        Variables = name => Variables.TryGetValue(name, out var value) ? value : null,
    };

    /// <summary>Clears the host's streams.</summary>
    public void ClearOutput()
    {
        _fixture.Out.GetStringBuilder().Clear();
        _fixture.Error.GetStringBuilder().Clear();
    }

    /// <summary>Removes the root.</summary>
    public void Dispose() => _fixture.Dispose();
}
