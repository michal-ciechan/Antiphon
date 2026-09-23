using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0604 D-19 / G-32, G-38. The remote half of the verification workspace believes nothing the
// runner says without re-checking it. Two properties are being held here:
//
//  * every coordinate must be POSIX and rooted under the repository/workspace the runner declared
//    -- a coordinate outside them is either a misconfigured runner or a runner answering about
//    something that is not this execution's snapshot; and
//  * no call ever reads the desktop filesystem. For a remote task there is no snapshot there, no
//    evidence root and no receipt, so a fall-through would confidently answer "absent" about a
//    snapshot that exists and is intact -- and a cleanup would then remove a branch and a row for
//    a Mutation whose custody was never actually resolved.
[Category("Unit")]
public sealed class RemoteVerificationWorkspaceTests
{
    private const string Repository = "/work/repos/antiphon";
    private const string Workspace = "/work";
    private const string Sha = "1111111111111111111111111111111111111111";

    [Test]
    public async Task Coordinates_outside_runner_repository_are_refused()
    {
        var creationId = Guid.NewGuid();
        foreach (var rogue in new[]
                 {
                     Good(creationId) with { WorktreePath = "/tmp/elsewhere" },
                     Good(creationId) with { RepositoryPath = "/work/repos/other" },
                     Good(creationId) with { CommonGitDirectory = "/etc" },
                     Good(creationId) with { WorktreeGitDirectory = "/work/repos/antiphon/../secrets" },
                     // A Windows path is not a runner path, however plausible it looks.
                     Good(creationId) with { WorktreePath = @"C:\Antiphon\worktrees\task-12345678" },
                 })
        {
            var transport = new FakeTransport { Create = new(rogue, Sha) };
            var workspace = new RemoteVerificationWorkspace(transport, Repository, Workspace);

            await Should.ThrowAsync<ConflictException>(
                () => workspace.CreateAsync(Repository, "task-12345678", Sha, CancellationToken.None));
        }
    }

    [Test]
    public async Task Restoration_read_never_touches_desktop_fs()
    {
        var transport = new FakeTransport { Restoration = "{\"schemaVersion\":1}"u8.ToArray() };
        var workspace = new RemoteVerificationWorkspace(transport, Repository, Workspace);

        var bytes = await workspace.ReadRestorationAsync(
            Repository + "/.git", Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        // The bytes came from the transport, which is the runner. Nothing on this machine was
        // opened -- there is no such path here, and a local fallback would have returned null.
        bytes.ShouldBe(transport.Restoration);
        transport.RestorationReads.ShouldBe(1);

        // And an evidence root outside the runner's repository never reaches the wire at all.
        await Should.ThrowAsync<ConflictException>(() => workspace.ReadRestorationAsync(
            @"C:\Antiphon\.git", Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
        transport.RestorationReads.ShouldBe(1);
    }

    [Test]
    public async Task Validate_compares_coordinates_ordinally()
    {
        var creationId = Guid.NewGuid();
        var coordinates = Good(creationId);

        // The runner says "valid" -- about a snapshot whose path differs only in case. On a
        // POSIX runner those are two different directories, and the one this execution is bound
        // to is the one the reservation recorded.
        var transport = new FakeTransport
        {
            Valid = new(true, null),
            Inspect = Inspection(coordinates with { WorktreePath = "/work/worktrees/TASK-12345678" }),
        };
        var workspace = new RemoteVerificationWorkspace(transport, Repository, Workspace);

        var mismatched = await workspace.ValidateAsync(coordinates, Sha, CancellationToken.None);
        mismatched.Valid.ShouldBeFalse();
        mismatched.Reason.ShouldBe("verification_creation_identity_mismatch");

        // A different creation id is the same refusal: a snapshot re-created at the same path is
        // not the snapshot this execution reserved.
        transport.Inspect = Inspection(coordinates) with { CreationId = Guid.NewGuid() };
        (await workspace.ValidateAsync(coordinates, Sha, CancellationToken.None)).Valid.ShouldBeFalse();

        // Exactly equal coordinates and the landed sha: valid.
        transport.Inspect = Inspection(coordinates);
        (await workspace.ValidateAsync(coordinates, Sha, CancellationToken.None)).Valid.ShouldBeTrue();

        // And the runner's own "not valid" is never overridden by a matching inspection.
        transport.Valid = new(false, "verification_creation_dirty");
        (await workspace.ValidateAsync(coordinates, Sha, CancellationToken.None)).Reason.ShouldBe("verification_creation_dirty");
    }

    [Test]
    public async Task Removal_passes_the_exact_expected_output_list()
    {
        var coordinates = Good(Guid.NewGuid());
        var transport = new FakeTransport { Remove = new(true, true, true, null) };
        var workspace = new RemoteVerificationWorkspace(transport, Repository, Workspace);

        var removal = await workspace.RemoveAsync(coordinates, Sha, ["report.md", "trx/run.trx"], CancellationToken.None);

        removal.Residue.ShouldBeNull();
        transport.LastRemove!.ExpectedSha.ShouldBe(Sha);
        transport.LastRemove.Outputs.ShouldBe(["report.md", "trx/run.trx"]);
    }

    private static VerificationCreationCoordinates Good(Guid creationId) => new(
        Repository, Repository + "/.git", "/work/worktrees/task-12345678",
        Repository + "/.git/worktrees/task-12345678", "feat/card-task-12345678", creationId);

    private static PhoneHomeVerificationInspectResponse Inspection(VerificationCreationCoordinates c) =>
        new(c.CreationId, Sha, c.Branch, c.RepositoryPath, c.WorktreePath, c.WorktreeGitDirectory,
            Sha, true, true, false);

    private sealed class FakeTransport : IVerificationWorkspaceTransport
    {
        public PhoneHomeVerificationCreateResponse? Create { get; init; }
        public PhoneHomeVerificationValidateResponse Valid { get; set; } = new(true, null);
        public PhoneHomeVerificationInspectResponse? Inspect { get; set; }
        public byte[]? Restoration { get; init; }
        public PhoneHomeVerificationRemoveResponse Remove { get; init; } = new(true, true, true, null);
        public int RestorationReads { get; private set; }
        public PhoneHomeVerificationRemoveRequest? LastRemove { get; private set; }

        public Task<PhoneHomeVerificationCreateResponse> CreateAsync(
            PhoneHomeVerificationCreateRequest request, CancellationToken ct) => Task.FromResult(Create!);

        public Task<PhoneHomeVerificationValidateResponse> ValidateAsync(
            PhoneHomeVerificationValidateRequest request, CancellationToken ct) => Task.FromResult(Valid);

        public Task<PhoneHomeVerificationInspectResponse> InspectAsync(
            PhoneHomeVerificationInspectRequest request, CancellationToken ct) => Task.FromResult(Inspect!);

        public Task<PhoneHomeVerificationReadRestorationResponse> ReadRestorationAsync(
            PhoneHomeVerificationReadRestorationRequest request, CancellationToken ct)
        {
            RestorationReads++;
            return Task.FromResult(new PhoneHomeVerificationReadRestorationResponse(Restoration));
        }

        public Task<PhoneHomeVerificationRemoveResponse> RemoveAsync(
            PhoneHomeVerificationRemoveRequest request, CancellationToken ct)
        {
            LastRemove = request;
            return Task.FromResult(Remove);
        }
    }
}
