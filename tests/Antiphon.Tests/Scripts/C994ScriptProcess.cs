using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
namespace Antiphon.Tests.Scripts;
internal static class C994ScriptProcess
{
    internal static async Task<(int Exit,string Output)> Run(string file, params string[] args) {
        var psi=new ProcessStartInfo(file){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,
            WorkingDirectory=DelegateScriptRunner.RepoRoot};
        foreach(var arg in args)psi.ArgumentList.Add(arg);
        using var child=Process.Start(psi)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await child.WaitForExitAsync(deadline.Token);}catch{if(!child.HasExited){child.Kill(true);await child.WaitForExitAsync();}throw;}
        return(child.ExitCode,await stdout+await stderr);
    }
}
