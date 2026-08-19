using System.Text;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Origin-agnostic spill-to-file for bodies that would otherwise be typed whole into a pty
/// (CARD-0025). Delegation already has <c>FitBriefForTyping</c> / <c>FitRefinementForTyping</c> —
/// those stay; this is the helper for Channel/UI/System/spawn, which have no task id to hang a
/// brief pointer on.
///
/// <para>Under the caller-supplied byte ceiling the original is returned and no file is written.
/// Over it, the original is written to the request's absolute path and a short pointer naming
/// the cwd-relative path is what the caller types. A filesystem failure returns the original
/// so the caller keeps today's type-anyway + oversize-incident behaviour.</para>
/// </summary>
internal static class TypedBodySpill
{
    public const string PointerHeadline = "YOUR MESSAGE IS NOT IN THIS MESSAGE.";

    public readonly record struct Request(
        string Body,
        int CeilingBytes,
        string? AbsoluteSpillPath,
        string? RelativeSpillPath = null,
        AgentKind AgentKind = AgentKind.ClaudeCode,
        string? EnvelopePrefix = null,
        string? ApiFallback = null,
        ILogger? Logger = null);

    public readonly record struct Result(string ToType, bool Spilled)
    {
        public static Result Inline(string body) => new(body, Spilled: false);
    }

    public static Result Fit(Request request)
    {
        var body = request.Body ?? string.Empty;
        if (Encoding.UTF8.GetByteCount(body) <= request.CeilingBytes)
            return Result.Inline(body);

        string? written = null;
        if (!string.IsNullOrWhiteSpace(request.AbsoluteSpillPath))
        {
            try
            {
                var dir = Path.GetDirectoryName(request.AbsoluteSpillPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(request.AbsoluteSpillPath, body);
                written = request.AbsoluteSpillPath;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                request.Logger?.LogWarning(
                    ex, "Could not write the spill file at {Path}; typing the original body",
                    request.AbsoluteSpillPath);
            }
        }

        if (written is null)
        {
            if (!string.IsNullOrWhiteSpace(request.ApiFallback))
                return new Result(BuildPointer(body.Length, request.ApiFallback, request.AgentKind, request.EnvelopePrefix), Spilled: true);
            return Result.Inline(body);
        }

        var relative = string.IsNullOrWhiteSpace(request.RelativeSpillPath)
            ? written
            : request.RelativeSpillPath;
        return new Result(BuildPointer(body.Length, relative, request.AgentKind, request.EnvelopePrefix), Spilled: true);
    }

    /// <summary>
    /// The <c>[Telegram "Family" — Mike 14:32]</c> header on a channel-enveloped body, or null
    /// when the body is not an envelope. Used so a spilled Channel pointer still orients the
    /// agent on provider/chat/author without carrying the (oversize) text.
    /// </summary>
    public static string? TryReadChannelEnvelope(string? body)
    {
        if (string.IsNullOrEmpty(body) || body[0] != '[')
            return null;

        var lineEnd = body.AsSpan().IndexOfAny('\r', '\n');
        var line = lineEnd < 0 ? body : body[..lineEnd];
        var close = line.IndexOf(']');
        return close > 0 ? line[..(close + 1)] : null;
    }

    public static string InboxRelativePath(string fileStem) =>
        $".antiphon/inbox/{fileStem}.md";

    public static string InboxAbsolutePath(string cwd, string fileStem) =>
        Path.Combine(cwd, ".antiphon", "inbox", fileStem + ".md");

    private static string BuildPointer(
        int fullLength, string where, AgentKind agentKind, string? envelopePrefix)
    {
        var joins = PtyDeliveryCeilings.ComposerJoinsTypedLines(agentKind);
        var display = joins ? $"'{where}'" : where;

        var pointer = $"""
            {PointerHeadline} It is {fullLength:N0} characters — too long to type
            into a terminal without the transport dropping part of it, so it was written out
            instead. Read it in full before you do anything else:

                {display}

            Everything you need is there. Do not start from this summary.
            """.ReplaceLineEndings("\n");

        if (!string.IsNullOrWhiteSpace(envelopePrefix))
            pointer = envelopePrefix.Trim() + "\n\n" + pointer;

        return joins ? DelegationReportFormatter.FlattenForJoiningComposer(pointer) : pointer;
    }
}
