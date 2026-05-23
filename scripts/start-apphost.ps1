param([string]$LogFile = "C:\src\antiphon\apphost-run.log")
Set-Location "C:\src\antiphon\Antiphon.AppHost"
dotnet run --no-build *> $LogFile
