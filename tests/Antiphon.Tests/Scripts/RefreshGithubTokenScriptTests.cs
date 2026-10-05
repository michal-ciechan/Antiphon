using System.Text.RegularExpressions;
using Antiphon.Tests.Infrastructure;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class RefreshGithubTokenScriptTests
{
    [Test]
    public void Refresh_script_streams_the_token_over_stdin_and_never_prints_it()
    {
        var text = Script();
        text.All(c => c <= 127).ShouldBeTrue("PowerShell scripts must remain ASCII");
        text.ShouldContain("$vaultItem = '97819ca3-710f-43f8-99ce-b4da013e32c1'");
        text.ShouldContain("$token = (& bw get password $vaultItem --nointeraction 2>$null | Out-String).Trim()");
        text.ShouldContain("$token | & ssh -o BatchMode=yes -o ConnectTimeout=30 $Server $remote 1>$null 2>$null");
        text.ShouldContain("sudo -n sh -c");
        text.ShouldContain("mktemp \"$d/token.tmp.XXXXXXXX\"");
        text.ShouldContain("cat > \"$tmp\" && test -s \"$tmp\" && chown 1654:1654 \"$tmp\" && chmod 0400 \"$tmp\" && mv -f \"$tmp\" \"$d/token\"");
        text.ShouldContain("trap 'rm -f -- \"$tmp\"' EXIT HUP INT TERM");
        text.ShouldContain("finally {\n    $token = ''\n    $env:BW_SESSION = $previous");
        foreach (var line in text.Split('\n'))
        {
            Regex.IsMatch(line, @"(?i)\b(Write-Host|Write-Output|Write-Warning|Write-Verbose|Write-Debug|Write-Information)\b.*\$token\b")
                .ShouldBeFalse("no token printing: " + line);
            Regex.IsMatch(line, @"(?i)\bssh\b.*\$token\b").ShouldBeFalse("no token in SSH argv");
            Regex.IsMatch(line, @"(?i)\$env:[\w]+\s*=\s*\$token\b").ShouldBeFalse("no token in environment");
            Regex.IsMatch(line, @"(?i)\b(Set-Content|Out-File|WriteAllText|WriteAllBytes)\b").ShouldBeFalse("no desktop credential file");
        }
        text.ShouldNotContain("Write-Warning $_");
        text.ShouldNotContain("https://x-access-token:");
    }

    [Test]
    public void Refresh_script_skips_without_a_relay_session_and_reports_mode_only()
    {
        var text = Script();
        text.ShouldContain("[string]$Server = 'mc@server2'");
        text.ShouldContain("$previous = $env:BW_SESSION");
        text.ShouldContain("Join-Path $HOME '.bw-session'");
        text.ShouldContain("if (Test-Path -LiteralPath $pickup)");
        var guard = text.IndexOf("if (-not $session -or", StringComparison.Ordinal);
        guard.ShouldBeGreaterThan(0);
        var skip = text.IndexOf("exit 2", guard, StringComparison.Ordinal);
        skip.ShouldBeGreaterThan(guard);
        skip.ShouldBeLessThan(text.IndexOf("$token = (& bw", StringComparison.Ordinal));
        skip.ShouldBeLessThan(text.IndexOf("$token | & ssh", StringComparison.Ordinal));
        text.ShouldContain("refresh skipped, previous file unchanged");
        text.ShouldContain("$LASTEXITCODE -ne 0 -or -not $token");
        text.ShouldContain("stat -c '%u:%g %a' \"$d/token\"");
        text.ShouldContain("printf 'present=true\\n'");
        text.ShouldContain("$metadata[0] -cne '1654:1654 400'");
        text.ShouldContain("$metadata[1] -cne 'present=true'");
        text.ShouldContain("Write-Output $metadata");
        var verify = text[(text.IndexOf("$verify = @'", StringComparison.Ordinal))..];
        verify.ShouldNotContain("cat ");
        verify.ShouldNotContain("sha256sum");
        verify.ShouldNotContain("Get-Content");
    }

    private static string Script() => DockerStackDocuments.Read("scripts/refresh-server2-github-token.ps1").Replace("\r\n", "\n");
}
