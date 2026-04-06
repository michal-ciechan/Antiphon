namespace Antiphon.Server.Application.Dtos;

public record FeatureStatusDto(
    string FeatureName,
    List<string> CompletedStages);
