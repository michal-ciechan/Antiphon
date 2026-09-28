using System.Diagnostics;
using System.Text.Json;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Per-argument CP-42 census. The sink outlives the fixture and its cloned database.</summary>
internal sealed class LandingPilotTrace : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<object> _children = [];
    private readonly List<object> _phases = [];
    private readonly List<object> _observations = [];
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly string _caseId;
    private readonly object[] _arguments;
    private long _sequence;

    private LandingPilotTrace(string caseId, object[] arguments)
    {
        _caseId = caseId;
        _arguments = arguments;
        Mark("case", "entered");
    }

    public static LandingPilotTrace? ForCase(string caseId, params object[] arguments) =>
        Environment.GetEnvironmentVariable("ANTIPHON_FAKEGIT_SAMPLE") == "CP-42"
            ? new(caseId, arguments) : null;

    public static async Task RecordBootstrapAsync(long startedTicks, long finishedTicks, string outcome)
    {
        if (Environment.GetEnvironmentVariable("ANTIPHON_FAKEGIT_SAMPLE") != "CP-42") return;
        var folder = ReceiptFolder();
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "bootstrap.json"),
            JsonSerializer.Serialize(new { startedTicks, finishedTicks, outcome }));
    }

    public void Mark(string phase, string disposition)
    {
        lock (_gate)
            _phases.Add(new { phase, disposition, ticks = Stopwatch.GetTimestamp(), at = DateTimeOffset.UtcNow });
    }

    public void Observe(string kind, object facts)
    {
        lock (_gate)
            _observations.Add(new { kind, facts = JsonSerializer.SerializeToElement(facts),
                ticks = Stopwatch.GetTimestamp() });
    }

    public void Child(string role, ProcessStartInfo start, int pid, long startTicks, long finishTicks,
        int exitCode, string stdout, string stderr)
    {
        lock (_gate)
            _children.Add(new { launchId = ++_sequence, role, cwd = start.WorkingDirectory,
                argv = start.ArgumentList.ToArray(), pid, startTicks, finishTicks, exitCode, stdout, stderr });
    }

    public async ValueTask DisposeAsync()
    {
        Mark("case", "exited");
        var folder = ReceiptFolder();
        Directory.CreateDirectory(folder);
        object[] children, phases, observations;
        lock (_gate)
        {
            children = _children.ToArray();
            phases = _phases.ToArray();
            observations = _observations.ToArray();
        }
        var receipt = new { checkpoint = "CP-42", backend = "real", storage = "physical",
            @class = "Antiphon.Tests.Application.AgentTaskLandBoundaryTests", caseId = _caseId,
            arguments = _arguments, startedAt = _startedAt, finishedAt = DateTimeOffset.UtcNow,
            startTicks = _started, finishTicks = Stopwatch.GetTimestamp(),
            observedGitLaunches = children.Length, launchObserverPresent = true,
            children, phases, observations };
        await File.WriteAllTextAsync(Path.Combine(folder, _caseId + ".json"),
            JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string ReceiptFolder()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, ".git"))
            && !Directory.Exists(Path.Combine(root.FullName, ".git"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Cannot find repository root for landing trace");
        using var process = Process.GetCurrentProcess();
        var processKey = process.Id + "-" + process.StartTime.ToUniversalTime().ToString("yyyyMMddHHmmssfff");
        return Path.Combine(root.FullName, ".antiphon", "fakegit-receipts", "CP-42-" + processKey);
    }
}
