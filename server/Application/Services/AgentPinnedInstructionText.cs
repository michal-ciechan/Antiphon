using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

public static class AgentPinnedInstructionText
{
    public static string NormalizeAndValidate(string? text)
    {
        if (text is null)
            throw new ValidationException("text", "Text is required.");

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (normalized.Length == 0)
            throw new ValidationException("text", "Text is required.");
        if (normalized.Length > AgentPinnedInstruction.MaxTextLength)
            throw new ValidationException("text", $"Text cannot exceed {AgentPinnedInstruction.MaxTextLength} characters.");

        foreach (var ch in normalized)
        {
            if (ch is '\n' or '\t')
                continue;
            if (char.IsControl(ch))
                throw new ValidationException("text", "Text contains forbidden control characters.");
        }

        return normalized;
    }

    public static string? NormalizeOptional(string? value, string field, int maxLength)
    {
        if (value is null)
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return null;
        if (trimmed.Length > maxLength)
            throw new ValidationException(field, $"{field} cannot exceed {maxLength} characters.");
        foreach (var ch in trimmed)
        {
            if (char.IsControl(ch))
                throw new ValidationException(field, $"{field} contains forbidden control characters.");
        }

        return trimmed;
    }
}
