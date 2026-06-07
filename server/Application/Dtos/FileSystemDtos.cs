namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// Response for the working-directory autocomplete. <see cref="Suggestions"/> are full
/// forward-slash directory paths. When the input is empty, <see cref="IsDrivesListing"/>
/// is true and the suggestions are drive roots ("C:/", "D:/").
/// </summary>
public sealed record DirectoryBrowseResponse(
    string NormalizedPath,
    bool Exists,
    bool IsDrivesListing,
    IReadOnlyList<string> Suggestions);
