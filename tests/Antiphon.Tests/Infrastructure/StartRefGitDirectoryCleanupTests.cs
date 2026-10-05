using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class StartRefGitDirectoryCleanupTests
{
    [Test]
    public async Task C1057_ChildJunction_PreservesOutsideAndRemovesRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("C1057 child junction proof requires native Windows mklink /J.");
            return;
        }

        using var fixture = new CleanupFixture();
        var sentinels = fixture.SeedOutside();
        var nested = fixture.SeedOwned();
        var link = fixture.RecordLink(Path.Combine(nested, "junction"), directory: true);
        await CreateJunctionAsync(fixture, link);
        AssertReparsePoint(link);

        StartRefGit.DeleteDirectory(fixture.Owned);

        AssertOutsideUnchanged(sentinels, "c1057-child-outside-exists",
            "c1057-child-outside-attributes", "c1057-child-outside-bytes");
        AssertRemoved(fixture, "c1057-child-root-removed");
    }

    [Test]
    public async Task C1057_RootJunction_PreservesOutsideAndRemovesLink()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("C1057 root junction proof requires native Windows mklink /J.");
            return;
        }

        using var fixture = new CleanupFixture();
        var sentinels = fixture.SeedOutside();
        var link = fixture.RecordLink(fixture.Owned, directory: true);
        await CreateJunctionAsync(fixture, link);
        AssertReparsePoint(link);

        StartRefGit.DeleteDirectory(fixture.Owned);

        AssertOutsideUnchanged(sentinels, "c1057-root-outside-exists",
            "c1057-root-outside-attributes", "c1057-root-outside-bytes");
        AssertRemoved(fixture, "c1057-root-link-removed");
    }

    [Test]
    public void C1057_ChildDirectorySymlink_PreservesOutsideAndRemovesRoot()
    {
        using var fixture = new CleanupFixture();
        var sentinels = fixture.SeedOutside();
        var nested = fixture.SeedOwned();
        var link = fixture.RecordLink(Path.Combine(nested, "symlink"), directory: true);
        CreateDirectorySymlink(link, fixture.Outside);
        AssertReparsePoint(link);

        StartRefGit.DeleteDirectory(fixture.Owned);

        AssertOutsideUnchanged(sentinels, "c1057-dirlink-outside-exists",
            "c1057-dirlink-outside-attributes", "c1057-dirlink-outside-bytes");
        AssertRemoved(fixture, "c1057-dirlink-root-removed");
    }

    [Test]
    public void C1057_RootDirectorySymlink_PreservesOutsideAndRemovesLink()
    {
        using var fixture = new CleanupFixture();
        var sentinels = fixture.SeedOutside();
        var link = fixture.RecordLink(fixture.Owned, directory: true);
        CreateDirectorySymlink(link, fixture.Outside);
        AssertReparsePoint(link);

        StartRefGit.DeleteDirectory(fixture.Owned);

        AssertOutsideUnchanged(sentinels, "c1057-rootlink-outside-exists",
            "c1057-rootlink-outside-attributes", "c1057-rootlink-outside-bytes");
        AssertRemoved(fixture, "c1057-rootlink-removed");
    }

    [Test]
    public void C1057_FileSymlink_PreservesOutsideAndRemovesRoot()
    {
        using var fixture = new CleanupFixture();
        var sentinels = fixture.SeedOutside();
        fixture.SeedOwned();
        var link = fixture.RecordLink(Path.Combine(fixture.Owned, "sentinel-link"), directory: false);
        try
        {
            File.CreateSymbolicLink(link, sentinels[0].Path);
        }
        catch (Exception ex) when (OperatingSystem.IsWindows()
            && ex is IOException or UnauthorizedAccessException)
        {
            Skip.Test($"File.CreateSymbolicLink: Windows denied symlink creation (privilege may be unavailable): {ex}");
            return;
        }
        AssertReparsePoint(link);

        StartRefGit.DeleteDirectory(fixture.Owned);

        AssertOutsideUnchanged(sentinels, "c1057-filelink-outside-exists",
            "c1057-filelink-outside-attributes", "c1057-filelink-outside-bytes");
        AssertRemoved(fixture, "c1057-filelink-root-removed");
    }

    private static void CreateDirectorySymlink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (OperatingSystem.IsWindows()
            && ex is IOException or UnauthorizedAccessException)
        {
            Skip.Test($"Directory.CreateSymbolicLink: Windows denied symlink creation (privilege may be unavailable): {ex}");
        }
    }

    private static async Task CreateJunctionAsync(CleanupFixture fixture, string link)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            WorkingDirectory = fixture.Scratch,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, fixture.Outside })
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("C1057 failed to start required junction creation.");
        fixture.JunctionChildJoined = false;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var exit = process.WaitForExitAsync();
        var completion = Task.WhenAll(exit, stdout, stderr);
        try
        {
            await completion.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (TimeoutException)
        {
            // Own this exact child until it has exited and both redirected streams have drained.
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await completion.WaitAsync(TimeSpan.FromSeconds(5));
            throw new TimeoutException($"C1057 mklink /J timed out for {link}; child killed and joined.");
        }
        finally
        {
            fixture.JunctionChildJoined = exit.IsCompletedSuccessfully;
        }

        process.ExitCode.ShouldBe(0,
            $"c1057-required-junction-created: stdout={await stdout}; stderr={await stderr}");
    }

    private static void AssertReparsePoint(string link) =>
        File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint)
            .ShouldBeTrue("c1057-fixture-reparse-point");

    private static void AssertOutsideUnchanged(
        Sentinel[] sentinels, string existsLabel, string attributesLabel, string bytesLabel)
    {
        foreach (var sentinel in sentinels)
        {
            File.Exists(sentinel.Path).ShouldBeTrue(existsLabel);
            File.GetAttributes(sentinel.Path).ShouldBe(sentinel.Attributes, attributesLabel);
            File.ReadAllBytes(sentinel.Path).ShouldBe(sentinel.Bytes, bytesLabel);
        }
    }

    private static void AssertRemoved(CleanupFixture fixture, string label)
    {
        Directory.Exists(fixture.Owned).ShouldBeFalse(label);
        // Exists alone would accept a dangling link. Inspect the real parent's immediate entries too.
        Directory.EnumerateFileSystemEntries(fixture.Scratch).ShouldNotContain(fixture.Owned, label);
    }

    private sealed record Sentinel(string Path, FileAttributes Attributes, byte[] Bytes);

    private sealed class CleanupFixture : IDisposable
    {
        private readonly List<string> _directories = [];
        private readonly List<string> _files = [];
        private readonly List<(string Path, bool Directory)> _links = [];

        public string Scratch { get; } = Directory.CreateTempSubdirectory("c1057-cleanup-").FullName;
        public string Owned => Path.Combine(Scratch, "owned");
        public string Outside => Path.Combine(Scratch, "outside");
        public bool JunctionChildJoined { get; set; } = true;

        public Sentinel[] SeedOutside()
        {
            CreateDirectory(Outside);
            var nested = CreateDirectory(Path.Combine(Outside, "nested"));
            return
            [
                WriteReadOnly(Path.Combine(Outside, "sentinel.bin"), [0, 1, 2, 255]),
                WriteReadOnly(Path.Combine(nested, "sentinel.bin"), [255, 128, 0, 42])
            ];
        }

        public string SeedOwned()
        {
            CreateDirectory(Owned);
            var nested = CreateDirectory(Path.Combine(Owned, "nested"));
            WriteReadOnly(Path.Combine(nested, "owned.bin"), [5, 6, 7]);
            return nested;
        }

        public string RecordLink(string path, bool directory)
        {
            // Record before creation so even partially failed setup uses independent unlinking.
            _links.Add((path, directory));
            return path;
        }

        private string CreateDirectory(string path)
        {
            _directories.Add(path);
            Directory.CreateDirectory(path);
            return path;
        }

        private Sentinel WriteReadOnly(string path, byte[] bytes)
        {
            _files.Add(path);
            File.WriteAllBytes(path, bytes);
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            var attributes = File.GetAttributes(path);
            attributes.HasFlag(FileAttributes.ReadOnly).ShouldBeTrue("c1057-fixture-read-only");
            return new Sentinel(path, attributes, File.ReadAllBytes(path));
        }

        public void Dispose()
        {
            if (!JunctionChildJoined)
                throw new InvalidOperationException($"C1057 junction child did not join; retaining {Scratch}");
            try
            {
                // No helper-under-test call or recursive deletion, including on the failure path.
                foreach (var (path, directory) in _links)
                {
                    var parent = Path.GetDirectoryName(path)!;
                    if (!Directory.Exists(parent)
                        || !Directory.EnumerateFileSystemEntries(parent).Contains(path))
                        continue;
                    if (!File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                        throw new IOException($"Expected fixture link at {path}; refusing cleanup.");
                    if (directory)
                    {
                        try { Directory.Delete(path, recursive: false); }
                        catch (DirectoryNotFoundException) when (!OperatingSystem.IsWindows())
                        {
                            // A directory symlink may now dangle if the tested code damaged its target.
                            File.Delete(path);
                        }
                    }
                    else
                    {
                        File.Delete(path);
                    }
                    if (Directory.EnumerateFileSystemEntries(parent).Contains(path))
                        throw new IOException($"Fixture link survived unlink at {path}.");
                }

                // All links are gone before any fixture-owned sentinel's attributes are repaired.
                foreach (var file in _files)
                {
                    if (!File.Exists(file)) continue;
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                for (var i = _directories.Count - 1; i >= 0; i--)
                    if (Directory.Exists(_directories[i]))
                        Directory.Delete(_directories[i], recursive: false);
                Directory.Delete(Scratch, recursive: false);
            }
            catch (Exception ex)
            {
                throw new IOException($"C1057 independent cleanup failed; retaining scratch path {Scratch}", ex);
            }
        }
    }
}
