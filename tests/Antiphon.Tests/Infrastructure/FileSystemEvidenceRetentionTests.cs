using System.Security.Cryptography;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1446 V-6 (S4a): evidence-class ignored files are copied under
/// <c>&lt;root&gt;/&lt;task:N&gt;/&lt;attempt:N&gt;/&lt;relative&gt;</c> with a matching SHA-256; a budget or
/// conflict refusal names its reason and leaves the source untouched. Temp directories, no database.
/// </summary>
[Category("Unit")]
public sealed class FileSystemEvidenceRetentionTests
{
    [Test]
    public async Task C1446_copies_and_verifies()
    {
        using var temp = new TempTree();
        var tree = temp.Dir("tree");
        var load = Write(tree, ".antiphon/c820-s3-final/load.jsonl", RandomBytes(4096));
        var report = Write(tree, ".antiphon/task-ab12cd34.md", "# report\n"u8.ToArray());
        var root = Path.Combine(temp.Path, "retained");
        var task = Task();
        var attempt = Guid.NewGuid();

        var result = await Build(new WorktreeCleanupSettings { RetainedEvidenceRoot = root })
            .RetainAsync(task, tree, [".antiphon/c820-s3-final/load.jsonl", ".antiphon/task-ab12cd34.md"], attempt, CancellationToken.None);

        result.Reason.ShouldBeNull();
        var expectedRoot = Path.Combine(root, task.Id.ToString("N"), attempt.ToString("N"));
        result.Root.ShouldBe(expectedRoot);
        result.Files.Select(f => f.Relative).ToArray().ShouldBe([".antiphon/c820-s3-final/load.jsonl", ".antiphon/task-ab12cd34.md"]);
        foreach (var file in result.Files)
        {
            var source = Path.Combine(tree, file.Relative.Replace('/', Path.DirectorySeparatorChar));
            file.RetainedPath.ShouldBe(Path.Combine(expectedRoot, file.Relative.Replace('/', Path.DirectorySeparatorChar)));
            var sourceBytes = await File.ReadAllBytesAsync(source);
            (await File.ReadAllBytesAsync(file.RetainedPath)).ShouldBe(sourceBytes, file.Relative);
            file.Bytes.ShouldBe(sourceBytes.LongLength);
            file.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(sourceBytes)), file.Relative);
        }
        // A copy, never a move: the gate's second reading requires the evidence set unchanged.
        File.Exists(load).ShouldBeTrue();
        File.Exists(report).ShouldBeTrue();
    }

    [Test]
    [Arguments("bytes")]
    [Arguments("files")]
    [Arguments("conflict")]
    public async Task C1446_refusals(string kind)
    {
        using var temp = new TempTree();
        var tree = temp.Dir("tree");
        var first = RandomBytes(64);
        var second = RandomBytes(64);
        Write(tree, ".antiphon/a/one.jsonl", first);
        Write(tree, ".antiphon/a/two.jsonl", second);
        var root = Path.Combine(temp.Path, "retained");
        var settings = new WorktreeCleanupSettings { RetainedEvidenceRoot = root };
        var task = Task();
        var attempt = Guid.NewGuid();
        var destinationRoot = Path.Combine(root, task.Id.ToString("N"), attempt.ToString("N"));
        var conflicting = RandomBytes(64);
        string expected;
        switch (kind)
        {
            case "bytes":
                settings.MaxRetainedEvidenceBytes = 100; // two 64-byte files
                expected = "evidence_retention_budget_bytes";
                break;
            case "files":
                settings.MaxRetainedEvidenceFiles = 1;
                expected = "evidence_retention_budget_files";
                break;
            default:
                Write(destinationRoot, ".antiphon/a/one.jsonl", conflicting);
                expected = "evidence_retention_conflict";
                break;
        }

        var result = await Build(settings)
            .RetainAsync(task, tree, [".antiphon/a/one.jsonl", ".antiphon/a/two.jsonl"], attempt, CancellationToken.None);

        result.Reason.ShouldBe(expected);
        result.Files.ShouldBeEmpty();
        result.Root.ShouldBeNull();
        (await File.ReadAllBytesAsync(Path.Combine(tree, ".antiphon", "a", "one.jsonl"))).ShouldBe(first, "source untouched");
        (await File.ReadAllBytesAsync(Path.Combine(tree, ".antiphon", "a", "two.jsonl"))).ShouldBe(second, "source untouched");
        if (kind == "conflict")
            (await File.ReadAllBytesAsync(Path.Combine(destinationRoot, ".antiphon", "a", "one.jsonl"))).ShouldBe(conflicting, "the existing copy is never overwritten");
        else
            Directory.Exists(destinationRoot).ShouldBeFalse("a budget refusal copies nothing");
    }

    private static FileSystemEvidenceRetention Build(WorktreeCleanupSettings settings) =>
        new(Options.Create(settings), Options.Create(new GitSettings()), NullLogger<FileSystemEvidenceRetention>.Instance);

    private static AgentTask Task() => new()
    {
        Id = Guid.NewGuid(), Title = "C1446", Goal = "C1446", Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code,
        Workspace = WorkspaceMode.Worktree, WorkingDirectory = "/tree", Status = AgentTaskStatus.Succeeded,
        ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow,
    };

    private static byte[] RandomBytes(int count)
    {
        var bytes = new byte[count];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    private static string Write(string root, string relative, byte[] bytes)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private sealed class TempTree : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("c1446-retention-").FullName;

        public string Dir(string name)
        {
            var path = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
