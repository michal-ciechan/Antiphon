using System.Diagnostics;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-1032 S2. Creation identity for an ordinary mirror, against a scratch git repository.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunnerWorkspaceBindingTests
{
    [Test]
    public async Task C1032_BindingFullIdentityCollision()
    {
        using var scratch = Scratch.Create();
        var service = scratch.Service(scratch.Clone);
        var taskA = Guid.Parse("deadbeef-0000-4000-8000-000000000001");
        var taskB = Guid.Parse("deadbeef-0000-4000-8000-000000000002");
        var first = await service.MirrorAsync(Request(scratch, taskA, Guid.NewGuid()), CancellationToken.None);
        var ownedId = first.Binding!.BindingId;
        var preparationB = Guid.NewGuid();

        var foreignBindingAccepted = false;
        try
        {
            var second = await service.MirrorAsync(Request(scratch, taskB, preparationB), CancellationToken.None);
            foreignBindingAccepted = second.Binding is { } got
                && (got.TaskId == taskA || got.BindingId == ownedId || got.TaskId != taskB);
        }
        catch (PhoneHomeAdmissionException)
        {
            foreignBindingAccepted = false;
        }

        var foreign = service.OpenBindingStore().Read(preparationB);
        if (foreign.Kind == BindingReadKind.Complete)
            foreignBindingAccepted = true;
        foreignBindingAccepted.ShouldBeFalse();
    }

    [Test]
    public async Task C1032_BindingCreationRecovery()
    {
        using var scratch = Scratch.Create();
        var service = scratch.Service(scratch.Clone);
        var taskId = Guid.Parse("deadbeef-0000-4000-8000-00000000000a");
        var preparationId = Guid.NewGuid();
        var request = Request(scratch, taskId, preparationId);
        var creationIntentAtGitAdd = false;
        service.BindingPhaseCut = phase =>
        {
            if (phase == "before-add")
            {
                var seen = service.OpenBindingStore().Read(preparationId);
                creationIntentAtGitAdd = seen.Kind == BindingReadKind.Intent && seen.Record?.TreeExistedAtIntent == false;
            }

            if (phase == "added")
                throw new InvalidOperationException("cut-after-add");
        };

        await Should.ThrowAsync<InvalidOperationException>(() => service.MirrorAsync(request, CancellationToken.None));
        creationIntentAtGitAdd.ShouldBeTrue();
        var intentId = service.OpenBindingStore().Read(preparationId).Record!.BindingId;

        var resumed = scratch.Service(scratch.Clone);
        var recovered = await resumed.MirrorAsync(request, CancellationToken.None);
        recovered.Binding!.BindingId.ShouldBe(intentId);

        using var legacyScratch = Scratch.Create();
        var legacyService = legacyScratch.Service(legacyScratch.Clone);
        await legacyService.MirrorAsync(
            new PhoneHomeWorkspaceMirrorRequest(Scratch.Branch, legacyScratch.Sha, "task-cafef00d"),
            CancellationToken.None);
        var legacyTask = Guid.Parse("cafef00d-0000-4000-8000-000000000001");
        var legacyPreparation = Guid.NewGuid();
        var legacyTreeEnrolled = false;
        try
        {
            var enrolled = await legacyService.MirrorAsync(
                Request(legacyScratch, legacyTask, legacyPreparation), CancellationToken.None);
            legacyTreeEnrolled = enrolled.Binding is not null;
        }
        catch (PhoneHomeAdmissionException)
        {
            legacyTreeEnrolled = false;
        }

        if (legacyService.OpenBindingStore().Read(legacyPreparation).Kind == BindingReadKind.Complete)
            legacyTreeEnrolled = true;
        legacyTreeEnrolled.ShouldBeFalse();
    }

    [Test]
    public async Task C1032_BindingExactReplay()
    {
        using var scratch = Scratch.Create();
        var service = scratch.Service(scratch.Clone);
        var taskId = Guid.Parse("deadbeef-0000-4000-8000-0000000000b1");
        var preparationId = Guid.NewGuid();
        var request = Request(scratch, taskId, preparationId);
        var original = await service.MirrorAsync(request, CancellationToken.None);
        var originalBindingId = original.Binding!.BindingId;

        var replayed = scratch.Service(scratch.Clone);
        var replay = await replayed.MirrorAsync(request, CancellationToken.None);
        var replayedBindingId = replay.Binding!.BindingId;
        replayedBindingId.ShouldBe(originalBindingId);

        var changedPreparationAccepted = false;
        var changed = new[]
        {
            request with { Binding = request.Binding! with { Attempt = request.Binding.Attempt + 1 } },
            request with { Branch = "feat/card-task-cafef00d", Binding = request.Binding! with { SourceFullRef = "refs/heads/feat/card-task-cafef00d" } },
            request with { Sha = new string('a', 40) },
        };
        foreach (var mutated in changed)
        {
            try
            {
                var response = await replayed.MirrorAsync(mutated, CancellationToken.None);
                if (response.Binding is not null)
                    changedPreparationAccepted = true;
            }
            catch (PhoneHomeAdmissionException)
            {
            }
        }

        changedPreparationAccepted.ShouldBeFalse();
        var stored = replayed.OpenBindingStore().Read(preparationId).Record!;
        stored.BindingId.ShouldBe(originalBindingId);
        stored.Attempt.ShouldBe(1);
        stored.CreationBaselineSha.ShouldBe(scratch.Sha);
    }

    [Test]
    public async Task C1032_BindingStateRootIsolation()
    {
        using var scratch = Scratch.Create();
        Scratch.Git(scratch.Origin, "branch", "feat/card-task-cafef00d", scratch.Sha);
        var service = scratch.Service(scratch.Clone);
        var inside = scratch.Work + "/worktrees/task-deadbeef/requested-state";
        var outside = scratch.Root + "/outside-state";
        var taskA = Guid.Parse("deadbeef-0000-4000-8000-0000000000c1");
        var taskB = Guid.Parse("cafef00d-0000-4000-8000-0000000000c2");
        var preparationA = Guid.NewGuid();
        var preparationB = Guid.NewGuid();
        var requestA = Request(scratch, taskA, preparationA, inside);
        var requestB = Request(scratch, taskB, preparationB, outside, "feat/card-task-cafef00d");
        var first = await service.MirrorAsync(requestA, CancellationToken.None);
        first.Binding.ShouldNotBeNull();
        var second = await service.MirrorAsync(requestB, CancellationToken.None);
        second.Binding.ShouldNotBeNull();

        var store = service.OpenBindingStore();
        var bindingStoredAtConfiguredRoot =
            File.Exists(store.RecordPath(preparationA))
            && File.Exists(store.RecordPath(preparationB))
            && store.RecordPath(preparationA).StartsWith(service.BindingStateRoot + "/", StringComparison.Ordinal)
            && store.RecordPath(preparationB).StartsWith(service.BindingStateRoot + "/", StringComparison.Ordinal)
            && !Directory.Exists(inside)
            && !Directory.Exists(outside);
        bindingStoredAtConfiguredRoot.ShouldBeTrue();

        var bindingAccepted = false;
        foreach (var corrupt in new Action<string>[]
                 {
                     path => File.WriteAllText(path, "{"),
                     path => File.WriteAllBytes(path, [0xFF, 0xFE, 0x00]),
                     path => File.Delete(path),
                 })
        {
            corrupt(store.RecordPath(preparationA));
            try
            {
                var response = await service.MirrorAsync(requestA, CancellationToken.None);
                if (response.Binding is not null)
                    bindingAccepted = true;
            }
            catch (Exception ex) when (ex is PhoneHomeAdmissionException or InvalidOperationException)
            {
            }
        }

        bindingAccepted.ShouldBeFalse();
    }

    [Test]
    public async Task C1032_BindingLegacyCompatibility()
    {
        using var scratch = Scratch.Create();
        var service = scratch.Service(scratch.Clone);
        var response = await service.MirrorAsync(
            new PhoneHomeWorkspaceMirrorRequest(Scratch.Branch, scratch.Sha, "task-deadbeef"),
            CancellationToken.None);
        var legacyMirrorExists = Directory.Exists(response.Path);
        legacyMirrorExists.ShouldBeTrue();
        response.Binding.ShouldBeNull();
    }

    private static PhoneHomeWorkspaceMirrorRequest Request(
        Scratch scratch, Guid taskId, Guid preparationId, string? requestedStateRoot = null, string? branch = null)
    {
        branch ??= Scratch.Branch;
        var name = "task-" + taskId.ToString("N")[..8];
        return new PhoneHomeWorkspaceMirrorRequest(
            branch,
            scratch.Sha,
            name,
            null,
            new PhoneHomeWorkspaceMirrorBinding(
                taskId,
                1,
                preparationId,
                "runner-a",
                Guid.Parse("11111111-1111-1111-1111-111111111111").ToString("D"),
                "refs/heads/" + branch,
                requestedStateRoot));
    }

    private sealed class Scratch : IDisposable
    {
        public const string Branch = "feat/card-task-deadbeef";

        public required string Root { get; init; }
        public required string Origin { get; init; }
        public required string Clone { get; init; }
        public required string Work { get; init; }
        public required string Sha { get; init; }

        public static Scratch Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "c1032-ws-" + Guid.NewGuid().ToString("N"));
            var origin = Path.Combine(root, "origin");
            var clone = Path.Combine(root, "repo");
            var work = Path.Combine(root, "work");
            Directory.CreateDirectory(origin);
            Directory.CreateDirectory(work);
            Git(origin, "init", "--initial-branch=main");
            Git(origin, "config", "user.email", "c1032@localhost");
            Git(origin, "config", "user.name", "c1032");
            File.WriteAllText(Path.Combine(origin, "README.md"), "c1032");
            Git(origin, "add", "README.md");
            Git(origin, "commit", "-m", "base");
            Git(origin, "checkout", "-b", Branch);
            File.WriteAllText(Path.Combine(origin, "task.txt"), "task work");
            Git(origin, "add", "task.txt");
            Git(origin, "commit", "-m", "task");
            var sha = Git(origin, "rev-parse", "HEAD").Trim();
            // The clone must not have the task branch checked out, or worktree add -B refuses it.
            Git(origin, "checkout", "main");
            Git(root, "clone", origin, clone);
            Git(clone, "config", "user.email", "c1032@localhost");
            Git(clone, "config", "user.name", "c1032");
            return new Scratch
            {
                Root = root,
                Origin = origin,
                Clone = clone,
                Work = work.Replace('\\', '/'),
                Sha = sha,
            };
        }

        public RunnerWorkspaceService Service(string repository)
        {
            var emptyConfig = Path.Combine(Root, "empty.gitconfig");
            if (!File.Exists(emptyConfig))
                File.WriteAllText(emptyConfig, "");
            Directory.CreateDirectory(Work);
            return new RunnerWorkspaceService(repository, Work, Origin, psi =>
            {
                psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
                psi.Environment["GIT_CONFIG_GLOBAL"] = emptyConfig;
                psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
                return Process.Start(psi);
            });
        }

        public void Dispose()
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(Root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        public static string Git(string workingDirectory, params string[] args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
            return stdout;
        }
    }
}
