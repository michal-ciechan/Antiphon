using System.Data.Common;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.HostLoad;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RunnerSettings = Antiphon.SessionRunner.HostStatsSettings;

try
{
    var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(args.Single()), PhoneHomeFraming.Json) ?? throw new InvalidDataException();
    if (!Path.IsPathFullyQualified(config.Root) || !Uri.TryCreate(config.SourceUrl, UriKind.Absolute, out var url)
        || url.Host != "127.0.0.1" || url.Port is 17204 or 8080) throw new InvalidDataException();
    var clock = new Clock(config.Now); var boundary = new Boundary(config);
    var runner = new RunnerSettings { RunnerId = "grok-linux", HistoryPath = Path.Combine(config.Root, "spool"), PhysicalHostKey = "owned-host", BindingRevision = 1, SourceEpoch = config.Sample.SourceEpoch };
    if (config.Cut.StartsWith("append-", StringComparison.Ordinal))
    {
        var spool = new HostLoadSpool(runner.HistoryPath, runner, clock, config.Sample.Source.BootId,
            (stage, _) => { if (config.Cut == "append-" + stage) boundary.Ready(); });
        spool.Append(config.Sample); return 3;
    }
    using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    var client = new SessionRunnerHttpClient(http, new RefusingFactory(), Options.Create(new Antiphon.Server.Application.Settings.SessionRunnerSettings { BaseUrl = config.SourceUrl }), time: clock);
    var settings = new HostLoadHistorySettings(); settings.Bindings.Add(new() { RunnerId = "grok-linux", PhysicalHostKey = "owned-host", SourceEpoch = config.Sample.SourceEpoch!.Value });
    var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<TimeProvider>(clock); services.AddSingleton(Options.Create(settings));
    services.AddSingleton<ISessionRunnerDirectory>(new DirectoryClient(client)); services.AddSingleton<HostLoadCounterCache>();
    services.AddDbContext<AppDbContext>(o => o.UseNpgsql(config.ConnectionString).AddInterceptors(new SaveBoundary(boundary), new TransactionBoundary(boundary)));
    services.AddScoped<HostLoadIngestService>(); services.AddScoped<HostLoadTaskWindowObserver>(); services.AddScoped<HostLoadDerivedService>(); services.AddSingleton<HostLoadIngestPump>();
    using var provider = services.BuildServiceProvider(); var pump = provider.GetRequiredService<HostLoadIngestPump>();
    if (config.Cut == "ingest-before-enqueue")
    {
        using var scope = provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var after = await db.HostLoadIngestCursors.Where(c => c.RunnerId == "grok-linux").Select(c => c.Cursor).SingleOrDefaultAsync();
        _ = await client.GetHostLoadHistoryAsync(after, 120, CancellationToken.None); boundary.Ready();
    }
    await pump.PollOnceAsync("grok-linux", CancellationToken.None);
    if (config.Cut == "ingest-after-enqueue") boundary.Ready();
    boundary.Phase = "ingest"; await pump.ProcessOneAsync(CancellationToken.None);
    if (config.Cut == "derive-before-call") boundary.Ready();
    boundary.Phase = "derive";
    using (var scope = provider.CreateScope()) await scope.ServiceProvider.GetRequiredService<HostLoadDerivedService>().DeriveOnceAsync(CancellationToken.None);
    return 3;
}
catch { Console.Error.WriteLine("c1514-probe-failed"); return 2; }

internal sealed record Config(string Root, string SourceUrl, string ConnectionString, DateTimeOffset Now,
    string Cut, string Nonce, string ReadyPath, HostLoadSample Sample);
internal sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
internal sealed class Boundary(Config config)
{
    public string Phase { get; set; } = "setup";
    public bool DerivedWrite { get; private set; }
    public void Saving(DbContext context)
    {
        DerivedWrite = Phase == "derive" && context.ChangeTracker.Entries<HostLoadHourlyRollup>().Any(e => e.State is EntityState.Added or EntityState.Modified);
        if ((Phase == "ingest" && config.Cut == "ingest-before-save") || (DerivedWrite && config.Cut == "derive-before-save")) Ready();
    }
    public void Saved() { if (DerivedWrite && config.Cut == "derive-after-write") Ready(); }
    public void Committing() { if (Phase == "ingest" && config.Cut == "ingest-before-commit") Ready(); }
    public void Committed() { if ((Phase == "ingest" && config.Cut == "ingest-after-commit") || (DerivedWrite && config.Cut == "derive-after-commit")) Ready(); }
    public void Ready()
    {
        using (var file = new FileStream(config.ReadyPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        { file.Write(JsonSerializer.SerializeToUtf8Bytes(new { config.Nonce, config.Cut, config.Sample.Source }, PhoneHomeFraming.Json)); file.Flush(true); }
        using var forever = new ManualResetEventSlim(); forever.Wait();
    }
}
internal sealed class SaveBoundary(Boundary boundary) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { boundary.Saving(eventData.Context!); return ValueTask.FromResult(result); }
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    { boundary.Saved(); return ValueTask.FromResult(result); }
}
internal sealed class TransactionBoundary(Boundary boundary) : DbTransactionInterceptor
{
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    { boundary.Committing(); return ValueTask.FromResult(result); }
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    { boundary.Committed(); return Task.CompletedTask; }
}
internal sealed class RefusingFactory : IHttpClientFactory { public HttpClient CreateClient(string name) => throw new InvalidOperationException(); }
internal sealed class DirectoryClient(ISessionRunnerClient source) : ISessionRunnerDirectory
{
    public ISessionRunnerClient Local => source; public ISessionRunnerClient Resolve(string? id) => source;
    public IReadOnlyList<string> KnownRunnerIds => ["grok-linux"];
    public Guid? GetLiveStoreId(string? id) => null;
    public Task<SessionRunnerOwner?> GetOwnerAsync(Guid id, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(null);
    public Task<SessionRunnerBinding> GetBindingAsync(Guid id, CancellationToken ct) => Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Missing.Instance);
    public Task<RunnerInventory> GetInventoryAsync(string? id, CancellationToken ct) => Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable("owned probe"));
}
