# Compiles the complete current helper with an explicit fake owner. No project build
# or native owner is launched. The deletion helper is copied verbatim from source.
param([switch]$OmitDeletion)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$source = [IO.File]::ReadAllText((Join-Path $root 'tests/Antiphon.Tests/Scripts/ScriptHarnessProcess.cs'))
$source = $source.Replace('using Antiphon.Tests.TestHelpers;', '')
$pattern = '(?s)private static IOwnedScriptProcess CreateOwner\(ScriptProcessRequest request\) =>.*?;'
if (-not [regex]::IsMatch($source,$pattern)) { throw 'native owner factory cut missing' }
$source = [regex]::Replace($source,$pattern,'private static IOwnedScriptProcess CreateOwner(ScriptProcessRequest request) => throw new NotSupportedException("explicit witness owner required");')
if ($OmitDeletion) {
    if (-not $source.Contains('StartRefGit.DeleteDirectory(results);')) { throw 'deletion negative-control cut missing' }
    $source = $source.Replace('StartRefGit.DeleteDirectory(results);', 'GC.KeepAlive(results);')
}
$helper = [IO.File]::ReadAllText((Join-Path $root 'tests/Antiphon.Tests/TestHelpers/StartRefGit.cs'))
$start = $helper.IndexOf('    public static void DeleteDirectory(string path)')
$end = $helper.IndexOf('    /// <summary>', $start)
if ($start -lt 0 -or $end -le $start) { throw 'deletion helper missing' }
$deletion = $helper.Substring($start,$end-$start)
$driver = @'
internal static class StartRefGit { DELETION }
public static class HarnessFlowWitness {
    private sealed class Owner : IOwnedScriptProcess {
        internal bool Uncertain, Stopped, Confirmed, Closed, Disposed;
        public StreamReader Stdout { get; } = new(new MemoryStream(Encoding.UTF8.GetBytes("owned output")));
        public StreamReader Stderr { get; } = new(new MemoryStream());
        public Task<int> StartAndWaitForRootAsync(CancellationToken token) => Task.FromResult(0);
        public Task TerminateAsync(CancellationToken token) { Stopped = true; return Task.CompletedTask; }
        public Task ConfirmDeadAsync(CancellationToken token) { Confirmed = true; if (Uncertain) throw new IOException("uncertain death"); return Task.CompletedTask; }
        public void CloseStreams() { Closed = true; Stdout.Dispose(); Stderr.Dispose(); }
        public void Dispose() { Disposed = true; }
    }
    public static async Task Run(string sibling) {
        Directory.CreateDirectory(sibling);
        var sentinel = Path.Combine(sibling, "sentinel");
        File.WriteAllText(sentinel, "foreign bytes");
        File.SetAttributes(sentinel, FileAttributes.ReadOnly);
        var attributes = File.GetAttributes(sentinel);
        try {
            foreach (var mode in new[] { "ordinary", "uncertain-death", "primary-error" }) {
                var owner = new Owner { Uncertain = mode == "uncertain-death" };
                ScriptProcessRequest request = null;
                var primary = new InvalidOperationException("primary witness");
                var options = new ScriptHarnessOptions(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5),
                    ExecutablePath: "inert-explicit-owner", OwnerFactory: value => {
                        request = value;
                        Directory.CreateDirectory(value.ResultsDirectory);
                        Directory.CreateDirectory(value.ControlDirectory);
                        var objects = Directory.CreateDirectory(Path.Combine(value.ResultsDirectory, ".git", "objects", "ab")).FullName;
                        var blob = Path.Combine(objects, "cdef"); File.WriteAllText(blob, "owned object");
                        File.SetAttributes(blob, FileAttributes.ReadOnly);
                        Directory.CreateSymbolicLink(Path.Combine(value.ResultsDirectory, "foreign-link"), sibling);
                        return owner;
                    });
                Exception seen = null;
                try {
                    var result = await ScriptHarnessProcess.RunAsync("witness", "WITNESS", mode, "inert.ps1", options,
                        CancellationToken.None, result => {
                            if (result.Stdout != "owned output" || result.ExitCode != 0) throw new Exception("output lost");
                            if (mode == "primary-error") throw primary;
                        });
                } catch (Exception error) { seen = error; }
                try {
                    if (mode == "ordinary" && seen != null) throw new Exception("ordinary cleanup failed", seen);
                    if (mode == "uncertain-death" && (seen is not IOException || !seen.Message.Contains("uncertain death"))) throw new Exception("uncertain death lost", seen);
                    if (mode == "primary-error" && !ReferenceEquals(seen, primary)) throw new Exception("primary error lost", seen);
                    var retained = mode == "uncertain-death";
                    if (Directory.Exists(request.ResultsDirectory) != retained || Directory.Exists(request.ControlDirectory) != retained) throw new Exception("wrong path retention");
                    if (!owner.Stopped || !owner.Confirmed || !owner.Closed || !owner.Disposed) throw new Exception("owner lifecycle incomplete");
                    if (File.ReadAllText(sentinel) != "foreign bytes" || File.GetAttributes(sentinel) != attributes) throw new Exception("foreign linked target changed");
                    Console.WriteLine("HARNESS_FLOW " + mode + " passed");
                } finally {
                    StartRefGit.DeleteDirectory(request.ResultsDirectory);
                    StartRefGit.DeleteDirectory(request.ControlDirectory);
                }
            }
        } finally { StartRefGit.DeleteDirectory(sibling); }
    }
}
'@
$prefix = "#nullable enable annotations`nusing System; using System.IO; using System.Linq; using System.Collections.Generic; using System.Threading; using System.Threading.Tasks;`n"
Add-Type ($prefix + $source + "`n" + $driver.Replace('DELETION',$deletion))
$sibling = Join-Path ([IO.Path]::GetTempPath()) ('ab1cd00f-flow-' + [guid]::NewGuid().ToString('N'))
[Antiphon.Tests.Scripts.HarnessFlowWitness]::Run($sibling).GetAwaiter().GetResult()
Write-Host 'HARNESS_FLOW cases=3 failed=0 nativeOwner=not-executed'
