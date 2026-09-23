namespace Antiphon.Server.Application.Dtos;

// The runner's ProviderAuth operation is added in the S4 round. Keep the server's
// wire shape available while the two rounds are developed independently.
public sealed record RunnerProviderAuthDto(
    string Provider,
    bool? LoggedIn,
    string? AuthMethod,
    string? SubscriptionType,
    DateTimeOffset CheckedAtUtc,
    string? Error);
