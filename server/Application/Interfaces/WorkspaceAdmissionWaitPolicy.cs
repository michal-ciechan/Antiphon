namespace Antiphon.Server.Application.Interfaces;

/// <summary>Use bounded admission only at an HTTP boundary that can expose a safe retry.</summary>
public enum WorkspaceAdmissionWaitPolicy
{
    Background,
    Interactive,
}
