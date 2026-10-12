using System.Diagnostics;
using System.Text.Json;
using Antiphon.SessionRunner;
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
    public async Task C1032_ForeignOwnerAdoptionRefused()
    {
        // case: task A saves its creation intent and is cancelled; task B, whose short id collides, creates the mirror; A retries the same preparation.
        // mutation: adopt an existing directory when only its presence and HEAD match, ignoring the task id and creation generation written before creation.
        // red: foreignAdopted.ShouldBeFalse() — the old recovery completes A's binding for B's tree.
        using var scratch = Scratch.Create();
        var service = scratch.Service(scratch.Clone);
        var taskA = Guid.Parse("deadbeef-0000-4000-8000-000000000011");
        var taskB = Guid.Parse("deadbeef-0000-4000-8000-000000000012");
        var preparationA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
        var preparationB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
        var requestA = Request(scratch, taskA, preparationA);
        var requestB = Request(scratch, taskB, preparationB);
        requestA.Name.ShouldBe(requestB.Name);
        service.BindingPhaseCut = phase =>
        {
            if (phase == "before-add")
                throw new InvalidOperationException("cut-before-add");
        };

        await Should.ThrowAsync<InvalidOperationException>(() => service.MirrorAsync(requestA, CancellationToken.None));
        var intent = service.OpenBindingStore().Read(preparationA);
        intent.Kind.ShouldBe(BindingReadKind.Intent);
        intent.Record!.TreeExistedAtIntent.ShouldBeFalse();
        Directory.Exists(intent.Record.WorktreePath).ShouldBeFalse();

        service.BindingPhaseCut = null;
        var createdByB = await service.MirrorAsync(requestB, CancellationToken.None);
        createdByB.Binding.ShouldNotBeNull();
        createdByB.Binding!.TaskId.ShouldBe(taskB);
        Directory.Exists(createdByB.Path).ShouldBeTrue();

        var foreignAdopted = false;
        PhoneHomeAdmissionException? refusal = null;
        try
        {
            var retry = await service.MirrorAsync(requestA, CancellationToken.None);
            foreignAdopted = retry.Binding is { } got && got.TaskId == taskA;
        }
        catch (PhoneHomeAdmissionException ex)
        {
            refusal = ex;
        }

        foreignAdopted.ShouldBeFalse();
        refusal.ShouldNotBeNull();
        refusal!.Message.ShouldBe("foreign_mirror_owner");
        service.OpenBindingStore().Read(preparationA).Kind.ShouldNotBe(BindingReadKind.Complete);
        service.OpenBindingStore().Read(preparationB).Kind.ShouldBe(BindingReadKind.Complete);
        service.OpenBindingStore().Read(preparationB).Record!.BindingId.ShouldBe(createdByB.Binding.BindingId);
    }

    [Test]
    public async Task C1032_AbsentMirrorReplayRefused()
    {
        // case: a completed binding is replayed exactly after ordinary removal of that worktree.
        // mutation: return the completed record without checking that this generation's directory, registration, HEAD and owner stamp are still present.
        // red: staleSuccess.ShouldBeFalse() — the old shortcut returns success for the missing directory.
        using var scratch = Scratch.Create();
        var service = scratch.Service(scratch.Clone);
        var taskId = Guid.Parse("deadbeef-0000-4000-8000-000000000021");
        var preparationId = Guid.Parse("cccccccc-0000-4000-8000-000000000003");
        var request = Request(scratch, taskId, preparationId);
        var original = await service.MirrorAsync(request, CancellationToken.None);
        var originalBindingId = original.Binding!.BindingId;
        var removed = await service.RemoveAsync(new PhoneHomeWorkspaceRemoveRequest(original.Path), CancellationToken.None);
        removed.Removed.ShouldBeTrue();
        Directory.Exists(original.Path).ShouldBeFalse();

        var staleSuccess = false;
        PhoneHomeAdmissionException? refusal = null;
        try
        {
            var replay = await service.MirrorAsync(request, CancellationToken.None);
            staleSuccess = replay.Binding is not null && !Directory.Exists(replay.Path);
        }
        catch (PhoneHomeAdmissionException ex)
        {
            refusal = ex;
        }

        staleSuccess.ShouldBeFalse();
        refusal.ShouldNotBeNull();
        refusal!.Message.ShouldBe("stale_mirror_binding");
        var stored = service.OpenBindingStore().Read(preparationId);
        stored.Kind.ShouldBe(BindingReadKind.Complete);
        stored.Record!.BindingId.ShouldBe(originalBindingId);
    }

    [Test]
    public async Task C1032_StoreMismatchRefused()
    {
        // case: a bound mirror request names a store, or a runner, that is not this recipient.
        // mutation: copy RunnerStoreId and RunnerId from the request into a completed binding without comparing them to the recipient.
        // red: wrongStoreCompleted.ShouldBeFalse() — the recipient creates the tree and stores the foreign store id.
        using var scratch = Scratch.Create();
        var foreignStore = Guid.Parse("22222222-2222-2222-2222-222222222222").ToString("D");
        var serviceTask = Guid.Parse("deadbeef-0000-4000-8000-000000000031");
        var servicePreparation = Guid.Parse("dddddddd-0000-4000-8000-000000000004");
        var serviceRequest = Request(scratch, serviceTask, servicePreparation) with
        {
            Binding = Request(scratch, serviceTask, servicePreparation).Binding! with { RunnerStoreId = foreignStore },
        };
        var service = scratch.Service(scratch.Clone);
        var wrongStoreCompleted = false;
        PhoneHomeAdmissionException? serviceRefusal = null;
        try
        {
            var created = await service.MirrorAsync(serviceRequest, CancellationToken.None);
            wrongStoreCompleted = created.Binding is { } got
                && string.Equals(got.RunnerStoreId, foreignStore, StringComparison.Ordinal)
                && Directory.Exists(created.Path);
        }
        catch (PhoneHomeAdmissionException ex)
        {
            serviceRefusal = ex;
        }

        var dispatchTask = Guid.Parse("deadbeef-0000-4000-8000-000000000032");
        var dispatchPreparation = Guid.Parse("eeeeeeee-0000-4000-8000-000000000005");
        var dispatchRequest = Request(scratch, dispatchTask, dispatchPreparation) with
        {
            Binding = Request(scratch, dispatchTask, dispatchPreparation).Binding! with
            {
                RunnerId = "runner-b",
                RunnerStoreId = foreignStore,
            },
        };
        var runtime = new StoreRuntime(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var dispatcher = new PhoneHomeCommandDispatcher(runtime, new PhoneHomeSettings
        {
            Enabled = true,
            RunnerId = "runner-a",
            AllowedCwd = scratch.Work,
            RunnerRepository = scratch.Clone.Replace('\\', '/'),
            Capacity = 1,
            CapacityStatePath = scratch.Root.Replace('\\', '/') + "/capacity",
        });
        var reply = await dispatcher.DispatchAsync(
            new PhoneHomeFrame(
                PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.WorkspaceMirror,
                JsonSerializer.SerializeToElement(dispatchRequest, PhoneHomeFraming.Json)),
            CancellationToken.None);
        var dispatchPath = scratch.Work + "/worktrees/" + dispatchRequest.Name;
        if (reply.Kind == PhoneHomeFrameKind.Result)
        {
            var mirrored = reply.Payload?.Deserialize<PhoneHomeWorkspaceMirrorResponse>(PhoneHomeFraming.Json);
            if (mirrored?.Binding is { } got
                && string.Equals(got.RunnerStoreId, foreignStore, StringComparison.Ordinal)
                && Directory.Exists(dispatchPath))
                wrongStoreCompleted = true;
        }

        wrongStoreCompleted.ShouldBeFalse();
        serviceRefusal.ShouldNotBeNull();
        serviceRefusal!.Message.ShouldBe("runner_store_mismatch");
        Directory.Exists(scratch.Work + "/worktrees/" + serviceRequest.Name).ShouldBeFalse();
        service.OpenBindingStore().Read(servicePreparation).Kind.ShouldBe(BindingReadKind.Missing);
        reply.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        reply.ErrorDetail.ShouldBe("runner_store_mismatch");
        Directory.Exists(dispatchPath).ShouldBeFalse();
        var dispatchRecord = new RunnerWorkspaceService(scratch.Clone, scratch.Work, scratch.Origin, static psi =>
        {
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            return Process.Start(psi);
        }).OpenBindingStore().Read(dispatchPreparation);
        dispatchRecord.Kind.ShouldBe(BindingReadKind.Missing);
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
            var service = new RunnerWorkspaceService(repository, Work, Origin, psi =>
            {
                psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
                psi.Environment["GIT_CONFIG_GLOBAL"] = emptyConfig;
                psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
                return Process.Start(psi);
            });
            service.BindRecipient("runner-a", Guid.Parse("11111111-1111-1111-1111-111111111111"));
            return service;
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

    private sealed class StoreRuntime(Guid storeId) : IPhoneHomeRuntimeSurface
    {
        public Guid RunnerStoreId { get; } = storeId;
        public int OwnedSessionCount => 0;
        public RunnerCapabilitiesDto Capabilities() => new("InboxConhost", "inbox", "test", false);
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => [];
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public RunnerBufferDto GetBuffer(Guid sessionId) => throw new NotSupportedException();
        public RunnerSnapshotDto GetSnapshot(Guid sessionId) => throw new NotSupportedException();
        public RunnerTranscriptDto GetTranscript(Guid sessionId) => throw new NotSupportedException();
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) => throw new NotSupportedException();
    }
}
