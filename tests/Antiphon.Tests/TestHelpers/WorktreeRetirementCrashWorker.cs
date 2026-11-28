using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TUnit.Core;
using ReflectionAssembly = System.Reflection.Assembly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0890 retirement crash child. Same admission shape as the land worker, with the
/// retirement cut whitelist kept separate from the land whitelist.
/// </summary>
internal static class WorktreeRetirementCrashWorker
{
    internal const string Marker = "ANTIPHON_C459_RETIREMENT_WORKER";
    internal const string CompiledModelVariable = "ANTIPHON_C459_COMPILED_MODEL";

    private static readonly Lazy<Task<bool>> CompiledModelReady = new(VerifyCompiledModelAsync);
    internal static readonly string[] AllowedCuts =
    [
        "release-before", "release-after", "claim-before", "claim-after",
        "intent-before", "intent-after", "git-exit", "directory-result",
        "registration-result", "branch-cas", "terminal-before", "terminal-after",
        "run-projection", "resume",
    ];

    internal sealed record Request(string Root, Guid TaskId, string Cut, string Ready,
        ReleaseWorktreeRetirementRequest? Release = null);

    internal static ReleaseWorktreeRetirementRequest ReleaseBody(AgentTask task, string sha) =>
        new(task.ConcurrencyToken, sha,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(task.Result ?? ""))),
            true, "reviewed no further use");

    internal static async Task<ReleaseWorktreeRetirementRequest> ReadReleaseAsync(LandingSafetyHarness harness, AppDbContext db)
    {
        var task = await db.AgentTasks.SingleAsync(t => t.Id == harness.Fixture.TaskId);
        var sha = (await harness.Fixture.RequiredAsync(task.WorktreePath ?? harness.Fixture.Source, "rev-parse", "HEAD")).Trim();
        return ReleaseBody(task, sha);
    }

    // The generator checks the model in the staged probe. Compare it again in the test host
    // using the retirement fixture's options before handing it to any crash or resume child.
    internal static Task<bool> PrepareCompiledModelAsync() => CompiledModelReady.Value;

    private static async Task<bool> VerifyCompiledModelAsync()
    {
        using var span = TestTrace.ChunkSpan("retirement worker prepare compiled model");
        try
        {
            var path = await ProbeGeneratedModel.RequireSelectedStagedArtifactAsync();

            var options = TestDbFixture.CreateDbContextOptions("Host=unused");
            using var runtime = new AppDbContext(options);
            using var compiled = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options)
                .UseModel(LoadCompiledModel(path)).Options);
            var differences = ProbeModelComparison.Differences(runtime.Model, compiled.Model);
            if (differences.Count == 0)
                return true;

            Console.WriteLine("Retirement worker uses the runtime model: " + differences[0]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TypeLoadException
            or TypeInitializationException or TargetInvocationException or InvalidOperationException
            or BadImageFormatException or MissingMemberException or NotSupportedException)
        {
            Console.WriteLine("Retirement worker uses the runtime model: " + ex.GetType().Name);
        }
        return false;
    }

    private static IModel LoadCompiledModel(string path)
    {
        using var span = TestTrace.ChunkSpan("retirement worker load compiled model");
        // The generated model uses the probe's Npgsql element-mapping resolver.
        ReflectionAssembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "channel-outbound-probe",
            "Antiphon.ChannelOutbound.Probe.dll"));
        return ProbeModel.Load(ReflectionAssembly.LoadFrom(path));
    }

    [Before(TestDiscovery)]
    public static async Task RunBeforeDiscoveryAsync()
    {
        // These children use the parent's database and exit from the worker. They never run
        // the selected test. Enter the worker before discovering the whole test assembly.
        if (TestWorkerModes.Requested() is not { } worker || worker.Mode.Marker != Marker)
            return;

        ProductionRunnerGuard.PointEveryProgramBootAwayFromTheProductionRunner();
        PtyBackendEnvGuard.ClearInheritedPtyBackend();
        LibGit2ConfigIsolation.IsolateLibGit2Config();
        await TestWorkerModes.RunAndExitAsync(worker.Mode, worker.Payload);
    }

    internal static async Task RunAsync(string encoded)
    {
        var request = JsonSerializer.Deserialize<Request>(encoded)
            ?? throw new InvalidOperationException(CrashWorkerProcess.StderrSentinel + ": Missing retirement worker request");
        var rejection = Rejection(request);
        if (rejection is not null)
            throw new InvalidOperationException(CrashWorkerProcess.StderrSentinel + ": " + rejection);

        CrashWorkerProcess.WriteStdoutSentinel();
        var model = Environment.GetEnvironmentVariable(CompiledModelVariable) == "1"
            ? LoadCompiledModel(ProbeGeneratedModel.RequireStagedArtifact())
            : null;
        await LandingSafetyHarness.RunRetirementCrashWorkerAsync(
            Path.GetFullPath(request.Root), request.TaskId, request.Cut, Path.GetFullPath(request.Ready), model, request.Release);
    }

    /// <summary>Null when the request is owned. Does not open a database or a process.</summary>
    internal static string? Rejection(Request request)
    {
        var root = Path.GetFullPath(request.Root);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
            + Path.DirectorySeparatorChar;
        if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(root).StartsWith("antiphon-c448-", StringComparison.Ordinal))
            return "unowned root";

        var ready = Path.GetFullPath(request.Ready);
        if (!string.Equals(Path.GetDirectoryName(ready), root, StringComparison.OrdinalIgnoreCase))
            return "ready outside root";

        var ownerPath = Path.Combine(root, "canonical", "fixture-owner.txt");
        if (!File.Exists(ownerPath))
            return "missing fixture owner";
        var owner = File.ReadAllText(ownerPath).Trim();
        if (!string.Equals(owner, request.TaskId.ToString("N"), StringComparison.Ordinal))
            return "task is not the fixture owner";

        if (Array.IndexOf(AllowedCuts, request.Cut) < 0)
            return "cut is not a retirement cut";

        return null;
    }
}
