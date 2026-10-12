using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Dtos;

/// <summary>CARD-1032. Mirror path plus the binding the runner returned, if it returned one.</summary>
public sealed record RemoteWorkspaceMirrorResult(
    string Path,
    CompletedCardWorkspaceBinding? Binding,
    MirrorBindingExpectation? Expectation);

/// <summary>Facts committed before the mirror request. A returned binding must match them.</summary>
public sealed record MirrorBindingExpectation(
    Guid TaskId,
    int Attempt,
    Guid PreparationId,
    string RunnerId,
    string RunnerStoreId,
    string RepositoryIdentity,
    string SourceFullRef,
    string CreationBaselineSha);
