using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.SessionRunner.Tests;

internal sealed class CodexCliVersionTestFixture : IDisposable
{
    internal static readonly DateTimeOffset T = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "c959-" + Guid.NewGuid().ToString("N"));
    public FakeTimeProvider Clock { get; } = new(T);
    public List<ProcessStartInfo> Starts { get; } = [];
    public List<string> Reads { get; } = [];
    public List<Process> Children { get; } = [];
    public string Mode { get; set; } = "success";
    public Func<ProcessStartInfo, string>? ChildMode { get; set; }
    public string Executable { get; }
    public object? Probe { get; private set; }
    public SessionRunnerRuntime Runtime { get; }
    public RunnerBuildDto Build { get; } = new("test", "d40c1670", T.UtcDateTime, T.UtcDateTime);
    public int AuthOpens { get; private set; }
    public List<bool> EmptyHomes { get; } = [];

    public CodexCliVersionTestFixture(TimeProvider? probeClock = null)
    {
        Directory.CreateDirectory(Root);
        Executable = Path.Combine(Root, OperatingSystem.IsWindows() ? "codex.exe" : "codex");
        // A real native executable-format fixture; execution is redirected solely at process I/O.
        File.Copy(Environment.ProcessPath!, Executable);
        Runtime = new SessionRunnerRuntime(Options.Create(new SessionRunnerSettings { SessionLogPath = Root }),
            NullLogger<SessionRunnerRuntime>.Instance, timeProvider: Clock);
        var probeType = typeof(SessionRunnerRuntime).Assembly.GetType("Antiphon.SessionRunner.CodexCliVersionProbe");
        if (probeType is null) return; // The baseline's actual producer has no probe evidence.
        var settingsType = probeType.Assembly.GetType("Antiphon.SessionRunner.CodexCliVersionSettings")!;
        var settings = Activator.CreateInstance(settingsType)!;
        settingsType.GetProperty("Executable")!.SetValue(settings, Executable);
        settingsType.GetProperty("ResolutionCwd")!.SetValue(settings, Root);
        var options = typeof(Options).GetMethod("Create")!.MakeGenericMethod(settingsType).Invoke(null, [settings]);
        Probe = Activator.CreateInstance(probeType, probeClock ?? Clock, new PhoneHomeProcessIdentity(), options,
            (Func<ProcessStartInfo, Process>)Start,
            (Func<string, int, byte[]>)Read);
        typeof(SessionRunnerRuntime).GetProperty("CodexCliProbe", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(Runtime, Probe);
    }

    public JsonElement Local() => JsonSerializer.SerializeToElement(Runtime.DescribeCapabilities(Build, [SessionBackends.PtyHost], []), Web);
    public JsonElement Registration() => JsonSerializer.SerializeToElement(new PhoneHomeRuntimeAdapter(Runtime, Build).Capabilities(), Web);

    public async Task<JsonElement> Attempt(string? executable = null, bool force = true, CancellationToken ct = default,
        string? resolutionCwd = null, string? path = null, string? pathExt = null, string? codexJsPrefix = null)
    {
        if (Probe is null) return Local();
        var requestType = typeof(RunnerCapabilitiesDto).Assembly.GetType("Antiphon.SessionRunner.Contracts.RunnerCodexCliProbeRequest")!;
        var request = Activator.CreateInstance(requestType, executable ?? Executable, resolutionCwd ?? Root, path, pathExt, codexJsPrefix)!;
        var task = (Task)Probe.GetType().GetMethod("ProbeAsync")!.Invoke(Probe, [request, force, ct])!;
        await task;
        return JsonSerializer.SerializeToElement(task.GetType().GetProperty("Result")!.GetValue(task), Web);
    }

    public async Task Refresh(CancellationToken ct = default)
    {
        if (Probe is not null)
            await (Task)Probe.GetType().GetMethod("RefreshDefaultAsync")!.Invoke(Probe, [ct])!;
    }

    public async Task WaitForReceiptAsync(string mode, int? ordinal = null)
    {
        var receipt = Path.Combine(Root, "receipts-" + (ordinal ?? Starts.Count), mode + ".json");
        var limit = Stopwatch.StartNew();
        while (!File.Exists(receipt) && limit.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
        if (!File.Exists(receipt)) throw new InvalidOperationException("owned child did not become ready");
    }

    public bool ReceiptIsAlive(string mode, int? ordinal = null)
    {
        var path = Path.Combine(Root, "receipts-" + (ordinal ?? Starts.Count), mode + ".json");
        var receipt = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        var pid = receipt.GetProperty("pid").GetInt32();
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited || process.StartTime.ToUniversalTime()
                != receipt.GetProperty("startedUtc").GetDateTime().ToUniversalTime()) return false;
            // A killed grandchild may await the container init's waitpid; it has no live code or pipe.
            if (!OperatingSystem.IsWindows() && File.Exists($"/proc/{pid}/stat"))
            {
                var stat = File.ReadAllText($"/proc/{pid}/stat");
                if (stat[(stat.LastIndexOf(')') + 2)..].StartsWith('Z')) return false;
            }
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    public static string? Text(JsonElement value, string field) =>
        value.TryGetProperty(field, out var member) && member.ValueKind != JsonValueKind.Null ? member.GetString() : null;

    private byte[] Read(string path, int max)
    {
        Reads.Add(path);
        if (Path.GetFileName(path) == "auth.json") { AuthOpens++; throw new InvalidOperationException("auth read prohibited"); }
        using var stream = File.OpenRead(path);
        var buffer = new byte[Math.Min(max, (int)Math.Min(stream.Length, max))];
        stream.ReadExactly(buffer);
        return buffer;
    }

    private Process Start(ProcessStartInfo actual)
    {
        Starts.Add(actual);
        EmptyHomes.Add(Directory.Exists(actual.Environment["CODEX_HOME"]!)
            && !Directory.EnumerateFileSystemEntries(actual.Environment["CODEX_HOME"]!).Any());
        var receipts = Path.Combine(Root, "receipts-" + Starts.Count);
        Directory.CreateDirectory(receipts);
        var child = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = actual.WorkingDirectory
        };
        child.Environment.Clear();
        foreach (var item in actual.Environment) child.Environment[item.Key] = item.Value;
        var mode = ChildMode?.Invoke(actual) ?? Mode;
        // Additional literal version records are independent harmless process-I/O fixtures.
        if (mode == "version-old" || mode == "version-floor")
        {
            foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
                         mode == "version-old" ? "[Console]::Out.WriteLine('codex-cli 0.156.1')"
                             : "[Console]::Out.WriteLine('codex-cli 0.159.1')" }) child.ArgumentList.Add(arg);
        }
        else
            foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File",
                         ChildScript(), "-Mode", mode, "-ReceiptRoot", receipts }) child.ArgumentList.Add(arg);
        var process = Process.Start(child)!;
        // Keep an independent observation/rescue handle; the product owns and disposes its handle.
        Children.Add(Process.GetProcessById(process.Id));
        return process;
    }

    private static string ChildScript()
    {
        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "tests/Antiphon.SessionRunner.Tests/Fixtures/CodexVersionChild.ps1");
            if (File.Exists(path)) return path;
        }
        throw new InvalidOperationException("missing owned child fixture");
    }

    public void RescueLeaves()
    {
        // Independent rescue owner, including grandchildren left alive by product cleanup defects.
        foreach (var receipt in Directory.EnumerateFiles(Root, "*.json", SearchOption.AllDirectories))
        {
            if (!Path.GetFileName(receipt).Equals("leaf.json", StringComparison.Ordinal)) continue;
            var data = JsonDocument.Parse(File.ReadAllText(receipt)).RootElement;
            try
            {
                using var process = Process.GetProcessById(data.GetProperty("pid").GetInt32());
                if (process.StartTime.ToUniversalTime() == data.GetProperty("startedUtc").GetDateTime().ToUniversalTime()
                    && !process.HasExited) { process.Kill(true); process.WaitForExit(5000); }
            }
            catch (ArgumentException) { }
        }
    }

    public void Dispose()
    {
        RescueLeaves();
        foreach (var process in Children)
        {
            if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); }
            process.Dispose();
        }
        if (Probe is IDisposable disposable) disposable.Dispose();
        Directory.Delete(Root, recursive: true);
    }

    private static JsonSerializerOptions Web => new(JsonSerializerDefaults.Web);
}
