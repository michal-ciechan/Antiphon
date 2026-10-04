namespace Antiphon.Agents.Pty.Tests;

/// <summary>Both parity peers deliberately choose raw typing or bracketed paste on modern.</summary>
internal static class C1022InputMode
{
    internal static string Encode(string body, bool paste) =>
        paste ? PtyInputEncoding.EncodeBody(body) : PtyInputEncoding.NormalizeBody(body);
}
