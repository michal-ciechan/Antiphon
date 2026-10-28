#requires -Version 7.0
param([string]$Repository, [string]$Scratch, [string]$Assembly, [switch]$Embedded)
$ErrorActionPreference = 'Stop'
if ($Embedded) {
    . (Join-Path $Repository 'scripts/lib/checkpoint-file-reader.ps1')
    $type = [Antiphon.Checkpoints.Coverage.ConfinedFileReader]
} else {
    $type = [Reflection.Assembly]::LoadFrom($Assembly).GetType('Antiphon.Checkpoints.CheckpointFileReader', $true)
}
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
public static class ReceiptPoolWitness
{
    public static void Run(Type reader, string root)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "receipt.json");
        byte[] expected = Enumerable.Repeat((byte)42, 85).ToArray();
        File.WriteAllBytes(path, expected);
        var read = reader.GetMethods().Single(m => m.Name == "ReadBytes" && m.GetParameters().Length == 5 && m.GetParameters()[0].ParameterType == typeof(string));
        ThreadPool.GetMinThreads(out int min, out int minIo);
        ThreadPool.GetMaxThreads(out int max, out int maxIo);
        using var entered = new CountdownEvent(4);
        using var release = new ManualResetEventSlim();
        using var finished = new CountdownEvent(4);
        Exception failure = null;
        bool completed = false;
        try
        {
            if (!ThreadPool.SetMinThreads(4, minIo) || !ThreadPool.SetMaxThreads(4, maxIo)) throw new Exception("could not cap isolated pool");
            for (int i = 0; i < 4; i++) Task.Run(() =>
            {
                entered.Signal();
                release.Wait();
                try
                {
                    var bytes = (byte[])read.Invoke(null, new object[] { root, path, 1024, null, CancellationToken.None });
                    if (!bytes.SequenceEqual(expected)) throw new Exception("wrong receipt bytes");
                }
                catch (Exception ex) { Interlocked.CompareExchange(ref failure, ex, null); }
                finally { finished.Signal(); }
            });
            if (!entered.Wait(TimeSpan.FromSeconds(10))) throw new Exception("pool callers did not enter");
            release.Set();
            completed = finished.Wait(TimeSpan.FromSeconds(10));
        }
        finally
        {
            ThreadPool.SetMaxThreads(max, maxIo);
            ThreadPool.SetMinThreads(min, minIo);
            release.Set();
            if (!finished.Wait(TimeSpan.FromSeconds(20))) throw new Exception("isolated pool did not drain after restoration");
        }
        if (!completed) throw new Exception("healthy receipts did not complete with all pool workers occupied");
        if (failure != null) throw new Exception("healthy receipt refused", failure);
    }
}
'@
[ReceiptPoolWitness]::Run($type, $Scratch)
'PASS pool-saturation readers=4'
