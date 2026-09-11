namespace Antiphon.Server.Application.Dtos;

public sealed record VerificationWorktreeCreation(Guid CreationId, string RepositoryPath,
    string WorktreePath, string GitDirectory, string Branch, string InitialSha);
