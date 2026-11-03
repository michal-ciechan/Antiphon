using System.Text;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// A baked image keeps its data directory in a committed image layer. Run there, every relation
/// file Postgres first opens read-write is an overlayfs copy-up, and a backend signalled during one
/// (or while it waits for another backend's copy-up of the same file) gets EINTR from open(), which
/// Postgres does not retry: <c>XX000 could not open file "base/16384/…": Interrupted system call</c>
/// (rc/2026-11-03 group-78, server2 merge smoke). A started baked container must therefore run
/// Postgres from a data directory that is not on overlay. Real Docker: bakes a throwaway image.
/// </summary>
[Category("Integration")]
public sealed class TestDbBakedImageDataDirectoryTests
{
    private const string WitnessSql =
        "CREATE TABLE c1685_witness (id int); INSERT INTO c1685_witness VALUES (1685);\n";

    [Test]
    public async Task Baked_image_runs_postgres_off_the_overlay_image_layer_with_the_baked_data()
    {
        var key = $"c1685-witness-{Guid.NewGuid():N}";
        var images = new ThrowawayBakedImages();
        var ops = new TestDbOperations();
        OwnedTestDbContainer? owned = null;
        try
        {
            await images.BakeAsync(key, Encoding.UTF8.GetBytes(WitnessSql));
            (await images.ExistsAsync(key)).ShouldBeTrue("the throwaway bake must produce its image");

            owned = await ops.CreateFromImageAsync(TestDbBakedImages.ImageName(key));
            await ops.StartAsync(owned);

            await using var connection = new NpgsqlConnection(owned.ConnectionString);
            await connection.OpenAsync();
            await using (var baked = new NpgsqlCommand("SELECT id FROM c1685_witness", connection))
                (await baked.ExecuteScalarAsync()).ShouldBe(1685, "the started container serves the baked data");
            string dataDirectory;
            await using (var show = new NpgsqlCommand("SHOW data_directory", connection))
                dataDirectory = (string)(await show.ExecuteScalarAsync())!;

            var mounts = await owned.Real!.ExecAsync(["cat", "/proc/mounts"]);
            mounts.ExitCode.ShouldBe(0L);
            var (mountPoint, fileSystem) = MountOf(dataDirectory, mounts.Stdout);
            fileSystem.ShouldNotBe("overlay",
                $"data_directory {dataDirectory} is on mount {mountPoint} ({fileSystem}): an image layer, " +
                "where a signalled first open of a relation file fails with EINTR");
        }
        finally
        {
            if (owned is not null)
                await ops.DisposeOwnedAsync(owned);
            await DeleteImageAsync(TestDbBakedImages.ImageName(key));
        }
    }

    /// <summary>The longest <c>/proc/mounts</c> mount point containing <paramref name="path"/>.</summary>
    private static (string MountPoint, string FileSystem) MountOf(string path, string procMounts)
    {
        var best = ("", "");
        foreach (var line in procMounts.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(' ');
            if (fields.Length < 3)
                continue;
            var mountPoint = fields[1].Replace("\\040", " ", StringComparison.Ordinal);
            var contains = mountPoint == "/" || path == mountPoint
                || path.StartsWith(mountPoint + "/", StringComparison.Ordinal);
            if (contains && mountPoint.Length >= best.Item1.Length)
                best = (mountPoint, fields[2]);
        }
        return best;
    }

    private static async Task DeleteImageAsync(string image)
    {
        using var client = TestcontainersSettings.OS.DockerEndpointAuthConfig
            .GetDockerClientBuilder(ResourceReaper.DefaultSessionId).Build();
        try
        {
            await client.Images.DeleteImageAsync(image, new ImageDeleteParameters { Force = true });
        }
        catch (Docker.DotNet.DockerImageNotFoundException)
        {
        }
    }

    private sealed class ThrowawayBakedImages() : TestDbBakedImages(enabled: true)
    {
        protected override bool PrunesAfterBake => false;
    }
}
