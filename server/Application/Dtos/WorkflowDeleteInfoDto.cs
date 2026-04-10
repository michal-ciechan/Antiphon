namespace Antiphon.Server.Application.Dtos;

public record WorkflowDeletePeerDto(Guid Id, string Name);

public record WorkflowDeleteInfoDto(
    string BranchName,
    IReadOnlyList<WorkflowDeletePeerDto> PeerWorkflows);
