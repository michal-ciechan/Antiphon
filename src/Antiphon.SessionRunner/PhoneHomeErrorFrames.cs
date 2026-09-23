using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0631 D-5: the one Error frame both reply boundaries use for a fault no typed admission
/// covers - the dispatcher's final catch and the receive pump's backstop. The frame keeps the
/// request's epoch, id and operation; its detail is the exception type plus a bounded message.
/// Never a payload, the environment or a stack trace.
/// </summary>
internal static class PhoneHomeErrorFrames
{
    internal const int MaxMessageChars = 512;

    public static PhoneHomeFrame Internal(PhoneHomeFrame request, Exception ex, int maxUtf8Bytes)
    {
        var type = ex.GetType().Name;
        var message = Truncate(ex.Message ?? "", MaxMessageChars);
        while (true)
        {
            var frame = Make(request, message.Length == 0 ? type : $"{type}: {message}");
            if (message.Length == 0 || Fits(frame, maxUtf8Bytes))
                return frame;
            // Shrink the message until the frame fits the budget. If even the bare type does not
            // fit, the writer refuses the frame and the connection fails visibly.
            message = Truncate(message, message.Length / 2);
        }
    }

    private static PhoneHomeFrame Make(PhoneHomeFrame request, string detail) =>
        new(PhoneHomeFrameKind.Error, request.Epoch, request.RequestId, request.Operation,
            ErrorCode: PhoneHomeProblemTypes.RunnerInternalError, ErrorDetail: detail, StatusCode: 500);

    private static bool Fits(PhoneHomeFrame frame, int maxUtf8Bytes) =>
        JsonSerializer.SerializeToUtf8Bytes(frame, PhoneHomeFraming.Json).Length <= maxUtf8Bytes;

    private static string Truncate(string value, int maxChars)
    {
        if (value.Length <= maxChars)
            return value;
        var cut = maxChars;
        if (cut > 0 && char.IsHighSurrogate(value[cut - 1]))
            cut--;
        return value[..cut];
    }
}
