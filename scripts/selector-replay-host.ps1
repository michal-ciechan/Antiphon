#requires -Version 7.0
# Test harness only: retain code/runtime, never a request's runspace or input data.
# Each JSON-line request invokes the real script with its normal parameter binding.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$hostRoot = [IO.Directory]::GetCurrentDirectory()
while ($null -ne ($requestLine = [Console]::In.ReadLine())) {
    $request = $requestLine | ConvertFrom-Json
    $out = [IO.StringWriter]::new()
    $err = [IO.StringWriter]::new()
    $consoleOut = [Console]::Out
    $consoleErr = [Console]::Error
    $ps = [powershell]::Create()
    $exitCode = 1
    try {
        [IO.Directory]::SetCurrentDirectory([string]$request.Root)
        $ps.Runspace.SessionStateProxy.Path.SetLocation([string]$request.Root)
        $ps.Runspace.SessionStateProxy.SetVariable('LASTEXITCODE', 0)
        [void]$ps.AddCommand([string]$request.Script)
        [void]$ps.AddParameter('Repo', [string]$request.Root)
        $arguments = @($request.Args)
        for ($i = 0; $i -lt $arguments.Count; $i++) {
            $name = ([string]$arguments[$i]).TrimStart('-')
            if ($name -in @('Json', 'MergeSubset', 'Audit')) {
                [void]$ps.AddParameter($name)
            }
            else {
                $i++
                if ($i -ge $arguments.Count) { throw ('missing argument for ' + $name) }
                [void]$ps.AddParameter($name, [string]$arguments[$i])
            }
        }
        [Console]::SetOut($out)
        [Console]::SetError($err)
        $items = @($ps.Invoke())
        $exitCode = [int]$ps.Runspace.SessionStateProxy.GetVariable('LASTEXITCODE')
        foreach ($item in $items) { $out.WriteLine([string]$item) }
        foreach ($errorRecord in $ps.Streams.Error) { $err.WriteLine([string]$errorRecord) }
        if ($ps.HadErrors -and $exitCode -eq 0) { $exitCode = 1 }
    }
    catch { $err.WriteLine($_.Exception.ToString()); $exitCode = 1 }
    finally {
        [Console]::SetOut($consoleOut)
        [Console]::SetError($consoleErr)
        $ps.Dispose()
        [IO.Directory]::SetCurrentDirectory($hostRoot)
    }
    $reply = [ordered]@{ Id = [string]$request.Id; Exit = $exitCode; Stdout = $out.ToString(); Stderr = $err.ToString(); ProcessId = $PID }
    [Console]::Out.WriteLine(($reply | ConvertTo-Json -Compress -Depth 4))
    $out.Dispose()
    $err.Dispose()
    $request = $null
    $reply = $null
}
