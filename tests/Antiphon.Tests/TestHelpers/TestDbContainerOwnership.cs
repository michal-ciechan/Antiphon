using Win32Exception = System.ComponentModel.Win32Exception;
using System.Diagnostics;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Test Postgres containers run without Testcontainers' resource reaper (Ryuk): starting the
/// reaper container costs ~1.4 s per test process, more than a baked image takes to start.
/// Each container is labelled with its owner (<see cref="OwnerLabel"/>: host, pid, process start
/// time). Normal teardown removes it as before. A process that dies without teardown leaves it
/// behind, so every bootstrap runs <see cref="SweepAsync"/>, which removes labelled containers
/// on this host whose owner process is gone. The pid alone is not enough because pids are reused;
/// the start time must match too.
/// <para>
/// Trace evidence: <c>db-create start</c> carries <c>antiphon.reaper</c> (<c>owner-sweep</c> or
/// <c>ryuk</c>), and <c>db-container sweep</c> carries examined/alive/removed/foreign counts.
/// <c>ANTIPHON_TEST_PG_RYUK=on</c> puts the reaper back for these containers.
/// </para>
/// </summary>
internal static class TestDbContainerOwnership
{
    public const string EnvVar = "ANTIPHON_TEST_PG_RYUK";
    public const string OwnerLabel = "net.codeperf.antiphon.test-db-owner";

    private static readonly TimeSpan StartTolerance = TimeSpan.FromSeconds(2);

    public static bool RyukEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnvVar)?.Trim(), "on", StringComparison.OrdinalIgnoreCase);

    public static string Reaper => RyukEnabled ? "ryuk" : "owner-sweep";

    private static readonly Lazy<string> Owner = new(() =>
    {
        using var self = Process.GetCurrentProcess();
        return $"{Environment.MachineName}/{Environment.ProcessId}/{self.StartTime.ToUniversalTime().Ticks}";
    });

    public static string CurrentOwner => Owner.Value;

    /// <summary>Labels the container with its owner and, unless Ryuk is asked for, opts it out of the reaper.</summary>
    public static PostgreSqlBuilder Own(this PostgreSqlBuilder builder)
    {
        var owned = builder.WithLabel(OwnerLabel, CurrentOwner);
        return RyukEnabled ? owned : owned.WithCleanUp(false);
    }

    /// <summary>
    /// Removes one container and its anonymous volumes. A no-op with Ryuk on (Testcontainers'
    /// dispose already removes it) or when it is already gone. Never throws.
    /// </summary>
    public static async Task RemoveAsync(string? containerId)
    {
        if (RyukEnabled || string.IsNullOrEmpty(containerId))
            return;
        using var span = TestTrace.Span("db-container remove");
        try
        {
            using var client = TestcontainersSettings.OS.DockerEndpointAuthConfig
                .GetDockerClientBuilder(ResourceReaper.DefaultSessionId).Build();
            await client.Containers.RemoveContainerAsync(containerId,
                new ContainerRemoveParameters { Force = true, RemoveVolumes = true }).ConfigureAwait(false);
        }
        catch (DockerContainerNotFoundException)
        {
        }
        catch (Exception ex)
        {
            span?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            Console.Error.WriteLine($"C1420 pg-container remove failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Removes <paramref name="container"/> when disposed, after Testcontainers' own dispose.</summary>
    public static IAsyncDisposable RemoveOnDispose(IContainer container) => new Remover(container);

    private sealed class Remover(IContainer container) : IAsyncDisposable
    {
        private readonly string? _id = SafeId(container);

        public async ValueTask DisposeAsync() => await RemoveAsync(_id ?? SafeId(container)).ConfigureAwait(false);

        private static string? SafeId(IContainer c)
        {
            try { return c.Id; }
            catch (InvalidOperationException) { return null; } // not created yet
        }
    }

    /// <summary>Removes labelled containers on this host whose owner process has exited. Never throws.</summary>
    public static async Task SweepAsync()
    {
        using var span = TestTrace.Span("db-container sweep");
        int examined = 0, alive = 0, removed = 0, foreign = 0;
        try
        {
            using var client = TestcontainersSettings.OS.DockerEndpointAuthConfig
                .GetDockerClientBuilder(ResourceReaper.DefaultSessionId).Build();
            var containers = await client.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool> { [OwnerLabel] = true }
                }
            }).ConfigureAwait(false);
            foreach (var container in containers)
            {
                examined++;
                if (container.Labels is null || !container.Labels.TryGetValue(OwnerLabel, out var owner))
                    continue;
                var parts = owner.Split('/');
                if (parts.Length != 3 || !string.Equals(parts[0], Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                    || !int.TryParse(parts[1], out var pid) || !long.TryParse(parts[2], out var startTicks))
                {
                    foreign++;
                    continue;
                }
                if (IsAlive(pid, startTicks))
                {
                    alive++;
                    continue;
                }
                try
                {
                    await client.Containers.RemoveContainerAsync(container.ID,
                        new ContainerRemoveParameters { Force = true, RemoveVolumes = true }).ConfigureAwait(false);
                    removed++;
                }
                catch (DockerApiException)
                {
                    // Another sweeper removed it first.
                }
            }
        }
        catch (Exception ex)
        {
            span?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            Console.Error.WriteLine($"C1420 pg-container sweep failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            span?.SetTag("antiphon.sweep.examined", examined);
            span?.SetTag("antiphon.sweep.alive", alive);
            span?.SetTag("antiphon.sweep.removed", removed);
            span?.SetTag("antiphon.sweep.foreign", foreign);
            if (removed > 0)
                Console.Error.WriteLine($"C1420 pg-container sweep removed={removed} of {examined} (owner process gone)");
        }
    }

    internal static bool IsAlive(int pid, long startTicks)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var started = process.StartTime.ToUniversalTime().Ticks;
            return Math.Abs(started - startTicks) <= StartTolerance.Ticks;
        }
        catch (ArgumentException)
        {
            return false; // no such process
        }
        catch (InvalidOperationException)
        {
            return false; // exited while we looked
        }
        catch (Win32Exception)
        {
            return true; // exists but unreadable: never remove what might be live
        }
    }
}
