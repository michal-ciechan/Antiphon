namespace Antiphon.FakeGrok;

// A terminal line feed does not return the cursor to column zero.
internal static class SignInScreen
{
    internal static string Render(string cwd) => ($"""
            {cwd}
                                                             Connecting...
                                             Approve in your browser to finish signing in.
                                                               FYED-XF4N
                                                Make sure your browser shows this code.
                                                If it doesn't open, click here to copy.
                                           Copying not working? Click here to show full URL.
                                                        Waiting for approval...
                                                             ctrl+q  quit
            """).ReplaceLineEndings("\r\n") + "\r\n";
}
