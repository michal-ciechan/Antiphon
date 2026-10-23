using System.Data;
using System.Diagnostics;
using System.IO.Abstractions;
using Antiphon.Observability;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.FileSystem;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Antiphon.Observability.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ExternalBoundaryTests
{
    [Test]
    public async Task V7_Db_callbacks_preserve_results_and_measure_success_failure_cancellation(CancellationToken cancellationToken)
    {
        var log = new CaptureLogger();
        using var timing = new LiveTiming(log);
        using var services = new ServiceCollection().AddLogging().AddSingleton(timing)
            .AddSingleton<DbCommandMetrics>().BuildServiceProvider();
        var interceptor = services.GetRequiredService<DbCommandMetrics>();
        using var connection = new NpgsqlConnection();
        using var command = new NpgsqlCommand("SELECT 'secret-canary'", connection);
        var duration = TimeSpan.FromMilliseconds(1100);
        var end = new CommandExecutedEventData(null!, (_, _) => "secret-canary", connection, command, null,
            DbCommandMethod.ExecuteScalar, Guid.NewGuid(), Guid.NewGuid(), 42, false, false,
            DateTimeOffset.UtcNow, duration, CommandSource.Unknown);
        using var reader = new DataTable().CreateDataReader();
        interceptor.ScalarExecuted(command, end, 42).ShouldBe(42);
        (await interceptor.ScalarExecutedAsync(command, end, 42, cancellationToken)).ShouldBe(42);
        interceptor.ReaderExecuted(command, end, reader).ShouldBeSameAs(reader);
        (await interceptor.ReaderExecutedAsync(command, end, reader, cancellationToken)).ShouldBeSameAs(reader);
        interceptor.NonQueryExecuted(command, end, 7).ShouldBe(7);
        (await interceptor.NonQueryExecutedAsync(command, end, 7, cancellationToken)).ShouldBe(7);
        var error = new CommandErrorEventData(null!, (_, _) => "secret-canary", connection, command, null,
            DbCommandMethod.ExecuteScalar, Guid.NewGuid(), Guid.NewGuid(), new IOException("secret-canary"),
            false, false, DateTimeOffset.UtcNow, duration, CommandSource.Unknown);
        interceptor.CommandFailed(command, error);
        await interceptor.CommandFailedAsync(command, error, cancellationToken);
        interceptor.CommandCanceled(command, end);
        await interceptor.CommandCanceledAsync(command, end, cancellationToken);
        log.Rows.Count.ShouldBe(10);
        log.Rows.Count(x => x.Text.Contains("failed-or-canceled")).ShouldBe(4);
        string.Join(" ", log.Rows.Select(x => x.Text)).ShouldNotContain("secret-canary");
    }

    [Test]
    public async Task V8_Git_and_filesystem_boundaries_emit_safe_spans(CancellationToken cancellationToken)
    {
        var log = new CaptureLogger();
        using var timing = new LiveTiming(log);
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == LiveTiming.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { lock (spans) spans.Add(a); }
        };
        ActivitySource.AddActivityListener(listener);
        using var services = new ServiceCollection().AddLogging().AddSingleton(timing)
            .AddSingleton<IFileSystem, FileSystem>().AddTransient<FileSystemDirectoryLister>()
            .AddTransient<FileSystemDirectoryWriter>().AddTransient<GitService>().BuildServiceProvider();
        var root = Path.Combine(Path.GetTempPath(), "c1564-secret-canary-" + Guid.NewGuid().ToString("N"));
        try
        {
            services.GetRequiredService<FileSystemDirectoryWriter>().CreateDirectory(root);
            services.GetRequiredService<FileSystemDirectoryLister>().ListDirectories(root).ShouldBeEmpty();
            await Should.ThrowAsync<InvalidOperationException>(() => services.GetRequiredService<GitService>()
                .GetBranchDiffAsync("secret-canary", "secret-canary", root, cancellationToken));
            Activity[] owned;
            lock (spans) owned = spans.Where(a => a.GetTagItem("run.id")?.ToString() == timing.ReadStartup().RunId).ToArray();
            owned.Count(a => a.OperationName == "filesystem").ShouldBe(2);
            var git = owned.Single(a => a.OperationName == "git");
            git.Status.ShouldBe(ActivityStatusCode.Error);
            string.Join(" ", owned.SelectMany(a => a.TagObjects).Select(t => t.Value)).ShouldNotContain("secret-canary");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

public sealed class ProcessSpawnLimit : IParallelLimit { public int Limit => 1; }
