using Antiphon.HostCleanup.Checkpoints;
using System.Text.Json;

namespace Antiphon.Checkpoints;

// The shared parser validates generic receipts. Reports retain the tool's extra shape contract.
public static class ReceiptJson
{
    public static T Read<T>(string path, JsonSerializerOptions options) where T : class =>
        Read<T>(CheckpointCleanupDefaults.Context, path, options);

    public static T Read<T>(CheckpointCleanupContext context, string path, JsonSerializerOptions options,
        CancellationToken cancellationToken = default) where T : class =>
        Parse<T>(ReceiptFile.ReadText(context, path, cancellationToken), options);

    public static T Parse<T>(string text, JsonSerializerOptions options) where T : class
    {
        var value = CheckpointReceiptJson.Parse<T>(text, options);
        ValidateReport(value);
        return value;
    }

    public static void Validate(object value)
    {
        CheckpointReceiptJson.Validate(value);
        ValidateReport(value);
    }

    private static void ValidateReport(object value)
    {
        if (value is ReportModel report)
        {
            if (string.IsNullOrWhiteSpace(report.MarkdownSha256)) throw new JsonException("receipt-shape: markdownSha256");
            if (report.OutputNames.Count > 0 && report.OutputPaths.Count == 0)
                throw new JsonException("receipt-shape: outputPaths required for outputNames");
        }
    }
}
