using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Antiphon.SessionRunner.Tests;

internal sealed record ShellIdentity(
    string RequestedName,
    string NormalizedPath,
    string ExeSha256,
    string Version,
    string EngineSha256);

internal sealed record PreflightInputs(
    string RequestedShell,
    string EntryPath,
    string HelperPath,
    string PlatformPath,
    string WrapperPath);

internal sealed record InputSnapshot(
    string Entry,
    string Helper,
    string Platform,
    string Wrapper);

internal sealed record PreflightValidationCall(
    ShellIdentity Shell,
    PreflightInputs Inputs,
    InputSnapshot Snapshot);

internal enum PreflightDisposition
{
    Hit,
    Miss
}

internal sealed class ShellResolutionException : InvalidOperationException
{
    public string RequestedShell { get; }

    public ShellResolutionException(string requestedShell, string message)
        : base(message + ": " + requestedShell)
    {
        RequestedShell = requestedShell;
    }
}

internal sealed class PreflightRefusalException : InvalidOperationException
{
    public PreflightRefusalException(string message) : base(message)
    {
    }

    public PreflightRefusalException(string message, Exception inner) : base(message, inner)
    {
    }
}

internal static class RestartAstValidator
{
    public const string Schema = "restart-ast-v1";

    public const string CopiedHelperFileName = "session-runner-restart-health.core.ps1";

    public const string WrapperText =
        ". (Join-Path $PSScriptRoot '" + CopiedHelperFileName + "')\n" +
        ". (Join-Path $PSScriptRoot 'platform.ps1')\n";

    public static readonly byte[] WrapperBytes = Encoding.UTF8.GetBytes(WrapperText);

    public const string Text = """
        param([string]$EntryPath,[string]$HelperPath)
        $ErrorActionPreference='Stop'
        $entryErrors=$null
        $helperErrors=$null
        $entry=[System.Management.Automation.Language.Parser]::ParseFile($EntryPath,[ref]$null,[ref]$entryErrors)
        $helper=[System.Management.Automation.Language.Parser]::ParseFile($HelperPath,[ref]$null,[ref]$helperErrors)
        if($entryErrors -and $entryErrors.Count){ throw ("parser error: " + $entryErrors[0].ToString()) }
        if($helperErrors -and $helperErrors.Count){ throw ("parser error: " + $helperErrors[0].ToString()) }
        foreach($statement in $helper.EndBlock.Statements) {
            if($statement -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { throw 'helper import is not inert' }
        }
        $core=$helper.Find({param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Invoke-RunnerRestart'},$true)
        if(-not $core){ throw 'missing Invoke-RunnerRestart' }
        foreach($command in $entry.FindAll({param($n) $n -is [System.Management.Automation.Language.CommandAst]},$true)) {
            $name=$command.GetCommandName()
            if($name -and $name -notin @('Join-Path','Split-Path','New-RunnerRestartPlatform','Invoke-RunnerRestart','Write-Host','ConvertTo-Json')) { throw "entry escaped thin boundary: $name" }
            if(-not $name -and $command.InvocationOperator -ne 'Dot') { throw 'entry contains unchecked dynamic command' }
        }
        foreach($call in $entry.FindAll({param($n) $n -is [System.Management.Automation.Language.InvokeMemberExpressionAst]},$true)) {
            if($call.Member.Value -notin @('StartNew','ToString')) { throw 'entry contains unchecked member call' }
        }
        foreach($command in $core.FindAll({param($n) $n -is [System.Management.Automation.Language.CommandAst]},$true)) {
            $name=$command.GetCommandName()
            if($name -and $name -notin @('Write-Host','New-RunnerMilestoneReader','Read-RunnerMilestones')) { throw "core bypassed platform: $name" }
            if(-not $name -and -not $command.Extent.Text.StartsWith('& $Platform.')) { throw 'core contains unchecked dynamic command' }
        }
        """;

    public static readonly string Identity =
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Schema + "\n" + Text)));
}

internal sealed class ShellIdentityResolver
{
    public ShellIdentity Resolve(string requestedShell)
    {
        var path = FindReadableExecutable(requestedShell);
        if (path is null)
            throw new ShellResolutionException(requestedShell, "no readable regular file for");
        var bytes = File.ReadAllBytes(path);
        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        var info = FileVersionInfo.GetVersionInfo(path);
        var version = info.FileVersion ?? info.ProductVersion ?? "";
        var engine = FingerprintEngine(path);
        return new ShellIdentity(
            requestedShell,
            Path.GetFullPath(path),
            sha,
            version,
            engine);
    }

    internal static string? FindReadableExecutable(string requestedShell)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var raw in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = raw.Trim().Trim('"');
            if (dir.Length == 0)
                continue;
            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(dir, requestedShell));
            }
            catch (Exception)
            {
                continue;
            }

            if (IsReadableRegularFile(candidate))
                return candidate;
        }

        return null;
    }

    internal static bool IsReadableRegularFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.Directory) != 0)
                return false;
            if ((attrs & FileAttributes.ReparsePoint) != 0)
                return false;
            var info = new FileInfo(path);
            if (info.Length == 0)
                return false;
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return stream.Length > 0 && stream.ReadByte() >= 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string FingerprintEngine(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath) ?? "";
        var adjacent = Path.Combine(dir, "System.Management.Automation.dll");
        if (IsReadableRegularFile(adjacent))
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(adjacent)));

        var powershellRoot = Path.GetFullPath(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell"));
        var fullDir = Path.GetFullPath(dir);
        if (fullDir.StartsWith(powershellRoot, StringComparison.OrdinalIgnoreCase))
        {
            var gac = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                @"Microsoft.NET\assembly\GAC_MSIL\System.Management.Automation\v4.0_3.0.0.0__31bf3856ad364e35\System.Management.Automation.dll");
            if (IsReadableRegularFile(gac))
                return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(gac)));
        }

        throw new ShellResolutionException(exePath, "engine fingerprint unavailable for");
    }
}

internal sealed class RestartPreflightCache
{
    public static RestartPreflightCache Shared { get; } = new();

    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<PreflightValidationCall, CancellationToken, Task<int>> _validator;
    private readonly Func<string, ShellIdentity> _resolver;

    public RestartPreflightCache(
        Func<PreflightValidationCall, CancellationToken, Task<int>>? validator = null,
        Func<string, ShellIdentity>? resolver = null,
        string? validatorIdentity = null)
    {
        _validator = validator ?? DefaultUnavailableValidator;
        _resolver = resolver ?? (name => new ShellIdentityResolver().Resolve(name));
        ValidatorIdentity = validatorIdentity ?? RestartAstValidator.Identity;
    }

    public string ValidatorIdentity { get; set; }

    public int SuccessfulPreflights { get; private set; }

    public int Hits { get; private set; }

    public Func<string, ShellIdentity> Resolver => _resolver;

    public async Task<PreflightDisposition> ApproveAsync(
        PreflightInputs inputs,
        Func<PreflightValidationCall, CancellationToken, Task<int>>? validate = null,
        Func<Task>? execute = null,
        CancellationToken cancellationToken = default)
    {
        var shell = _resolver(inputs.RequestedShell);
        var snapshot = HashInputs(inputs);
        var key = BuildKey(shell, snapshot, ValidatorIdentity);
        var validator = validate ?? _validator;
        PreflightDisposition disposition;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_keys.Contains(key))
            {
                Hits++;
                disposition = PreflightDisposition.Hit;
            }
            else
            {
                var call = new PreflightValidationCall(shell, inputs, snapshot);
                int exit;
                try
                {
                    var validateTask = validator(call, cancellationToken);
                    exit = cancellationToken.CanBeCanceled
                        ? await validateTask.WaitAsync(cancellationToken).ConfigureAwait(false)
                        : await validateTask.ConfigureAwait(false);
                }
                catch
                {
                    throw;
                }

                if (exit != 0)
                    throw new PreflightRefusalException("validator exit " + exit);

                var published = HashInputs(inputs);
                if (published != snapshot)
                    throw new PreflightRefusalException("input changed between validation and publication");

                var relaunch = _resolver(inputs.RequestedShell);
                if (relaunch != shell)
                    throw new PreflightRefusalException("shell identity drift");

                _keys.Add(key);
                SuccessfulPreflights++;
                disposition = PreflightDisposition.Miss;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (execute is not null)
            await execute().ConfigureAwait(false);
        return disposition;
    }

    internal static InputSnapshot HashInputs(PreflightInputs inputs) =>
        new(
            HashBytes(File.ReadAllBytes(inputs.EntryPath)),
            HashBytes(File.ReadAllBytes(inputs.HelperPath)),
            HashBytes(File.ReadAllBytes(inputs.PlatformPath)),
            HashBytes(File.ReadAllBytes(inputs.WrapperPath)));

    internal static string BuildKey(ShellIdentity shell, InputSnapshot snapshot, string validatorIdentity)
    {
        var raw = string.Join(
            '\n',
            shell.NormalizedPath,
            shell.ExeSha256,
            shell.Version,
            shell.EngineSha256,
            snapshot.Entry,
            snapshot.Helper,
            snapshot.Platform,
            snapshot.Wrapper,
            validatorIdentity);
        return HashBytes(Encoding.UTF8.GetBytes(raw));
    }

    internal static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static Task<int> DefaultUnavailableValidator(PreflightValidationCall call, CancellationToken ct) =>
        throw new InvalidOperationException("A real validator callback is required for " + call.Inputs.EntryPath);
}
