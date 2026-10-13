using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit"), ParallelLimiter<ProcessSpawnLimit>]
public sealed class ShellFixtureLineEndingContractTests
{
    private const string RequiredRule = "*.sh text eol=lf";
    private const string Restore = "git restore --source=HEAD --worktree -- scripts/fixtures/*.sh";
    private static readonly string[] FormerlyCrLfFixtures =
    [
        "scripts/fixtures/c1192-workspace-contract.sh",
        "scripts/fixtures/c913-image-contract.sh",
        "scripts/fixtures/c913-seed-contract.sh",
    ];

    [Test]
    public async Task Shell_scripts_are_forced_lf_by_gitattributes()
    {
        var root = DockerStackDocuments.RepoRoot;
        var paths = await TrackedShells(root);
        var attributes = File.ReadAllText(Path.Combine(root, ".gitattributes"));

        using var scratch = new ScratchRepository();
        await scratch.Initialize();
        var cases = new (string Name, string Attributes, string? Problem)[]
        {
            ("healthy", RequiredRule + "\n", null),
            ("commented", "# " + RequiredRule + "\n", "missing active rule"),
            ("missing", "* text=auto\n", "missing active rule"),
            ("later crlf", RequiredRule + "\nscripts/fixtures/*.sh eol=crlf\n", "eol=crlf"),
            ("later unset", RequiredRule + "\nscripts/fixtures/*.sh -eol\n", "eol=unset"),
            ("later binary", RequiredRule + "\nscripts/fixtures/*.sh -text\n", "text=unset"),
        };
        foreach (var row in cases)
        {
            File.WriteAllText(Path.Combine(scratch.Root, ".gitattributes"), row.Attributes);
            var problems = await AttributeProblems(scratch.Root, row.Attributes, FormerlyCrLfFixtures);
            if (row.Problem is null)
                problems.ShouldBeEmpty(row.Name);
            else
                problems.ShouldContain(problem => problem.Contains(row.Problem, StringComparison.Ordinal),
                    "c1210-effective-eol: " + row.Name);
        }

        var actual = await AttributeProblems(root, attributes, paths);
        actual.ShouldBeEmpty(string.Join("\n", actual));
    }

    [Test]
    public async Task Shell_scripts_have_no_carriage_returns()
    {
        var root = DockerStackDocuments.RepoRoot;
        var paths = await TrackedShells(root);
        AssertNoCarriageReturns(root, paths);

        using var scratch = new ScratchRepository();
        await scratch.Initialize();
        var sample = FormerlyCrLfFixtures[2];
        var samplePath = Path.Combine(scratch.Root, sample);
        Directory.CreateDirectory(Path.GetDirectoryName(samplePath)!);
        var cases = new (string Name, byte[] Bytes, bool HasCr)[]
        {
            ("healthy LF", "#!/bin/sh\nexit 0\n"u8.ToArray(), false),
            ("injected CRLF", "#!/bin/sh\r\nexit 0\n"u8.ToArray(), true),
            ("isolated CR", "#!/bin/sh\n# stray\rbyte\n"u8.ToArray(), true),
        };
        foreach (var row in cases)
        {
            File.WriteAllBytes(samplePath, row.Bytes);
            if (!row.HasCr)
                AssertNoCarriageReturns(scratch.Root, [sample]);
            else
            {
                var failure = Should.Throw<ShouldAssertException>(() => AssertNoCarriageReturns(scratch.Root, [sample]));
                failure.Message.ShouldContain("c1210-shell-cr");
                failure.Message.ShouldContain(sample);
                failure.Message.ShouldContain(Restore);
            }
        }

        // Exercise Git's Windows-style checkout conversion on either native platform.
        File.WriteAllText(Path.Combine(scratch.Root, ".gitattributes"),
            File.ReadAllText(Path.Combine(root, ".gitattributes")));
        foreach (var fixture in FormerlyCrLfFixtures)
        {
            var destination = Path.Combine(scratch.Root, fixture);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, File.ReadAllBytes(Path.Combine(root, fixture)));
        }
        await Git(scratch.Root, "-c", "core.autocrlf=true", "add", "--", ".gitattributes", "scripts/fixtures");
        await Git(scratch.Root, "-c", "user.name=Shell fixture contract", "-c", "user.email=test@antiphon.local",
            "-c", "commit.gpgsign=false", "commit", "--quiet", "--no-verify", "-m", "LF checkout fixture");
        foreach (var fixture in FormerlyCrLfFixtures)
            File.Delete(Path.Combine(scratch.Root, fixture));
        await Git(scratch.Root, "-c", "core.autocrlf=true", "restore", "--source=HEAD", "--worktree", "--", "scripts/fixtures/*.sh");
        AssertNoCarriageReturns(scratch.Root, FormerlyCrLfFixtures);
    }

    private static async Task<string[]> TrackedShells(string root)
    {
        var paths = (await Git(root, "ls-files", "-z", "--", "*.sh"))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        paths.ShouldNotBeEmpty("c1210-shell-census: git ls-files must find tracked shell scripts");
        foreach (var fixture in FormerlyCrLfFixtures)
            paths.ShouldContain(fixture, "c1210-shell-census: formerly CRLF fixture");
        return paths;
    }

    private static async Task<List<string>> AttributeProblems(string root, string attributes, string[] paths)
    {
        var problems = new List<string>();
        var active = attributes.Split('\n').Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'));
        if (!active.Contains(RequiredRule, StringComparer.Ordinal))
            problems.Add("c1210-shell-rule: missing active rule: " + RequiredRule);

        var arguments = new[] { "check-attr", "-z", "text", "eol", "--" }.Concat(paths).ToArray();
        var fields = (await Git(root, arguments)).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        fields.Length.ShouldBe(paths.Length * 6, "c1210-shell-attributes: complete Git attribute response");
        for (var i = 0; i < paths.Length; i++)
        {
            var offset = i * 6;
            fields[offset].ShouldBe(paths[i]);
            fields[offset + 1].ShouldBe("text");
            fields[offset + 3].ShouldBe(paths[i]);
            fields[offset + 4].ShouldBe("eol");
            if (fields[offset + 2] != "set")
                problems.Add(paths[i] + " text=" + fields[offset + 2]);
            if (fields[offset + 5] != "lf")
                problems.Add(paths[i] + " eol=" + fields[offset + 5]);
        }
        return problems;
    }

    private static void AssertNoCarriageReturns(string root, string[] paths)
    {
        var problems = paths.Where(path => File.ReadAllBytes(Path.Combine(root, path)).Contains((byte)13))
            .Select(path => "c1210-shell-cr: " + path + "; on a clean checkout run: " + Restore)
            .ToArray();
        problems.ShouldBeEmpty(string.Join("\n", problems));
    }

    private static async Task<string> Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // The scratch repository must not inherit an enclosing runner's Git workspace.
        foreach (var key in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE" })
            start.Environment.Remove(key);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
        }
        process.ExitCode.ShouldBe(0, "c1210-shell-git: " + string.Join(" ", arguments) + "\n" + await stderr);
        return await stdout;
    }

    private sealed class ScratchRepository : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("c1210-shell-").FullName;

        public async Task Initialize()
        {
            await Git(Root, "init", "--quiet");
            await Git(Root, "config", "core.safecrlf", "false");
            await Git(Root, "config", "core.hooksPath", Path.Combine(Root, "no-hooks"));
        }

        public void Dispose()
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
