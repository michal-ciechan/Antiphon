using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0604 D-19 / G-31. Custody support is a property of the runner that will EXECUTE the task,
// not of whatever runner happens to be local.
//
// Before Cut B this method took no runner at all and asked the local client. For a Mutation bound
// to server2 that is a Windows machine answering on behalf of a Linux one: the admission passes,
// the execution is reserved with the desktop's store id and windows-job-v1, and the runner that
// actually runs it can only ever refuse the binding or produce a receipt in a method it did not
// use. Either way the row is unresolvable, which is precisely what CARD-0598 asked to be made
// impossible.
[Category("Unit")]
public sealed class SourceLandingAdmissionTests
{
    [Test]
    public async Task Remote_task_checks_remote_runner_capabilities()
    {
        var directory = new FakeDirectory
        {
            Local = Capable(VerificationCustodyBackends.WindowsJob),
            Remote = Capable(VerificationCustodyBackends.LinuxCgroup),
        };
        var admission = Admission(directory);

        var support = await admission.RequireSupportAsync("server2", CancellationToken.None);

        support.Backend.ShouldBe(VerificationCustodyBackends.LinuxCgroup);
        support.RunnerStoreId.ShouldBe(directory.Remote!.StoreId);
        directory.Asked.ShouldBe(["server2"]);

        // And the local task still gets the local answer, unchanged.
        var local = await admission.RequireSupportAsync(null, CancellationToken.None);
        local.Backend.ShouldBe(VerificationCustodyBackends.WindowsJob);
        local.RunnerStoreId.ShouldBe(directory.Local!.StoreId);
    }

    // The unavailable remote runner is the phone-home 503 the directory already raises. It must
    // NOT be caught and turned into a local answer: that is how a remote Mutation would end up
    // admitted against the desktop's custody store.
    [Test]
    public async Task Unavailable_remote_runner_is_503_not_local()
    {
        var directory = new FakeDirectory
        {
            Local = Capable(VerificationCustodyBackends.WindowsJob),
            RemoteUnavailable = true,
        };
        var admission = Admission(directory);

        var error = await Should.ThrowAsync<ServiceUnavailableException>(
            () => admission.RequireSupportAsync("server2", CancellationToken.None));

        error.Code.ShouldBe(PhoneHomeProblemTypes.Unavailable);
        directory.Asked.ShouldBe(["server2"]);
    }

    [Test]
    public async Task Foreign_backend_capability_is_refused()
    {
        foreach (var backend in new[] { "cgroup-v9", "", "linux-cgroup-v2", "job" })
        {
            var directory = new FakeDirectory { Remote = Capable(backend) };
            var error = await Should.ThrowAsync<ConflictException>(
                () => Admission(directory).RequireSupportAsync("server2", CancellationToken.None));
            error.Code.ShouldBe("verification_custody_unsupported_backend");
        }

        // A runner that advertises a supported backend but no store id is equally unusable: the
        // binding has to name a store for the receipt to be checked against.
        var storeless = new FakeDirectory
        {
            Remote = new FakeClient(VerificationCustodyBackends.LinuxCgroup, Guid.Empty, advertise: true),
        };
        (await Should.ThrowAsync<ConflictException>(
                () => Admission(storeless).RequireSupportAsync("server2", CancellationToken.None)))
            .Code.ShouldBe("verification_custody_unsupported_backend");

        // As is one that has the backend string but never advertised the feature.
        var unfeatured = new FakeDirectory
        {
            Remote = new FakeClient(VerificationCustodyBackends.LinuxCgroup, Guid.NewGuid(), advertise: false),
        };
        (await Should.ThrowAsync<ConflictException>(
                () => Admission(unfeatured).RequireSupportAsync("server2", CancellationToken.None)))
            .Code.ShouldBe("verification_custody_unsupported_backend");
    }

    private static SourceLandingAdmission Admission(ISessionRunnerDirectory directory) =>
        new(null!, null!, directory);

    private static FakeClient Capable(string backend) => new(backend, Guid.NewGuid(), advertise: true);

    private sealed class FakeClient(string backend, Guid storeId, bool advertise) : ISessionRunnerClient
    {
        public Guid StoreId { get; } = storeId;

        public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) =>
            Task.FromResult<RunnerCapabilitiesDto?>(new("InboxConhost", "inbox", "test", false,
                Features: advertise ? [RunnerCapabilityFeatures.VerificationCustodyV1] : [],
                VerificationCustodyBackend: backend, RunnerStoreId: StoreId));

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeDirectory : ISessionRunnerDirectory
    {
        public FakeClient? Local { get; init; }
        public FakeClient? Remote { get; init; }
        public bool RemoteUnavailable { get; init; }
        public List<string?> Asked { get; } = [];

        ISessionRunnerClient ISessionRunnerDirectory.Local => Local!;

        public ISessionRunnerClient Resolve(string? runnerId)
        {
            Asked.Add(runnerId);
            if (string.IsNullOrWhiteSpace(runnerId)) return Local!;
            if (RemoteUnavailable)
                throw new ServiceUnavailableException("Phone-home runner is unavailable.", PhoneHomeProblemTypes.Unavailable);
            return Remote!;
        }

        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) => throw new NotSupportedException();
        public IReadOnlyList<string> KnownRunnerIds => ["server2"];
        public Guid? LiveStoreId => Remote?.StoreId;
    }
}
