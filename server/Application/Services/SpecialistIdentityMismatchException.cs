namespace Antiphon.Server.Application.Services;

/// <summary>A pinned specialist changed execution identity after the task was created.</summary>
public sealed class SpecialistIdentityMismatchException(string message) : InvalidOperationException(message);
