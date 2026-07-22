namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// The system notes a channel-facing agent receives right after an interactive launch. Two bodies,
/// not one: the launch path decides which applies where the truth lives —
/// <see cref="FreshBody"/> on a genuinely fresh session AND on the resume→fresh fallback
/// (same session row, but a brand-new conversation that must bootstrap), <see cref="ResumeBody"/>
/// only on a successful resume. Null <see cref="ResumeBody"/> = say nothing after a resume.
/// </summary>
public sealed record LaunchNotes(string FreshBody, string? ResumeBody);
