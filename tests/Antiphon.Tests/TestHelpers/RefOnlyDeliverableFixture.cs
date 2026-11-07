using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

internal sealed class RefOnlyDeliverableFixture : IAsyncDisposable
{
    public RunnerSettlementWorld World { get; private init; } = null!;
    public RefOnlyPersistentRoot Root { get; private init; } = null!;
    public DeliverablesSettings Settings { get; } = new();
    public BlobGit Git { get; } = new();
    public DeliverableBundleService Bundles { get; private set; } = null!;
    public static async Task<RefOnlyDeliverableFixture> CreateAsync(AgentTaskRole role = AgentTaskRole.Docs,
        bool refOnly = true, IReadOnlyDictionary<string, string>? initialFiles = null)
    {
        var root = new RefOnlyPersistentRoot();
        RunnerSettlementWorld world;
        try { world = await RunnerSettlementWorld.CreateAsync(role, initialFiles: initialFiles); }
        catch { root.Dispose(); throw; }
        var f = new RefOnlyDeliverableFixture { Root = root, World = world };
        try
        {
            if (refOnly) await f.World.RecordRefOnlyAsync();
            f.Settings.StorageRoot = f.Root.Path;
            f.Bundles = new(f.Git, Options.Create(f.Settings), NullLogger<DeliverableBundleService>.Instance);
            f.World.ConfigureServices = services => services.AddSingleton(f.Bundles);
            await f.World.RestartServicesAsync();
            return f;
        }
        catch { await f.DisposeAsync(); throw; }
    }
    public async Task<string> CommitAsync(IReadOnlyDictionary<string, byte[]> files)
    {
        var w = World.Git;
        await w.EnsureRunnerAsync();
        foreach (var file in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(w.Runner, file.Key))!);
            await File.WriteAllBytesAsync(Path.Combine(w.Runner, file.Key), file.Value);
            // These fixtures promise exact object bytes, independent of host autocrlf and clean filters.
            var blob = await w.RunAsync(w.Runner, "hash-object", "--no-filters", "-w", "--", file.Key);
            await w.RunAsync(w.Runner, "update-index", "--add", "--cacheinfo", "100644", blob, file.Key);
        }
        await w.RunAsync(w.Runner, "commit", "-m", "exact deliverable source");
        await w.RunAsync(w.Runner, "push", "origin", "HEAD:" + w.FullRef);
        return await w.RunAsync(w.Runner, "rev-parse", "HEAD");
    }
    public async Task<DeliverableBundleService.SourceManifest> ManifestAsync()
    {
        var dir = World.Task.DeliverableBundleDir.ShouldNotBeNull("source bundle exists");
        return JsonSerializer.Deserialize<DeliverableBundleService.SourceManifest>(
            await File.ReadAllTextAsync(Path.Combine(dir, DeliverableBundleService.SourceManifestName)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldNotBeNull();
    }
    public async ValueTask DisposeAsync()
    {
        await World.DisposeAsync(); Root.Dispose();
    }
    internal sealed class BlobGit() : GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance,
        settings: Options.Create(new GitSettings { TimeoutSeconds = 10 }))
    {
        public List<(string Path, string Sha)> LengthReads { get; } = [];
        public List<(string Path, string Sha, long Budget)> ByteReads { get; } = [];
        public Func<string, long?>? LengthOverride { get; set; }
        public override async Task<long?> GetContentLengthAtAsync(string repository, string relative, string sha, CancellationToken ct)
        {
            LengthReads.Add((relative, sha));
            return LengthOverride is null ? await base.GetContentLengthAtAsync(repository, relative, sha, ct) : LengthOverride(relative);
        }
        public override Task<byte[]?> GetContentBytesAtAsync(string repository, string relative, string sha, long max, CancellationToken ct)
        {
            ByteReads.Add((relative, sha, max));
            return base.GetContentBytesAtAsync(repository, relative, sha, max, ct);
        }
    }
}
