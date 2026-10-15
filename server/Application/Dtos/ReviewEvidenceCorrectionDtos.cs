using System.Text.Json.Serialization;

namespace Antiphon.Server.Application.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewCorrectionSelection(string Path, string CommitSha, string Section,
    string Digest, string[] Rows);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewEvidenceCorrectionRequest(Guid PredecessorEvidenceId, string ExpectedReportDigest,
    Guid SubjectTaskId, string SourceSha, string SourceRef, string RepositoryPath,
    ReviewCorrectionSelection Selection, string ProducerIdentity, string BundleDigest, string Reason);

public sealed record ReviewEvidenceCorrectionResponse(string Disposition, Guid? CorrectionId,
    Guid? SuccessorEvidenceId, string[] MissingRows);
