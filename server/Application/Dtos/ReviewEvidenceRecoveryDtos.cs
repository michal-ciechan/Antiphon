using System.Text.Json.Serialization;

namespace Antiphon.Server.Application.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewEvidenceRecoveryRequest(Guid EvidenceId, string ExpectedReportSha256, string Reason);

public sealed record ReviewEvidenceRecoveryResponse(
    string Disposition, Guid ReviewTaskId, Guid PreviousEvidenceId, Guid ReviewEvidenceId,
    string ReportSha256, Guid SubjectTaskId, string ReviewedSourceSha);
