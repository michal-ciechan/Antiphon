using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0497: Windows Codex launch normalization and the authoritative command-line budget.
/// Identity is the request's Codex transcript/kind, not the executable basename. Called after
/// ordinary validation and before session registration, file writes, host creation, or Herdr contact.
/// </summary>
internal static class CodexWindowsLaunchPolicy
{
    /// <summary>
    /// Stock npm <c>codex.cmd</c> forwarder (npm 10 / @openai/codex 0.153.4 on this machine).
    /// Recognition is this shape after newline/trailing-space normalize — a customized wrapper
    /// is neither rewritten nor executed.
    /// </summary>
    internal const string StockNpmShimText =
        """
        @ECHO off
        GOTO start
        :find_dp0
        SET dp0=%~dp0
        EXIT /b
        :start
        SETLOCAL
        CALL :find_dp0

        IF EXIST "%dp0%\node.exe" (
          SET "_prog=%dp0%\node.exe"
        ) ELSE (
          SET "_prog=node"
          SET PATHEXT=%PATHEXT:;.JS;=;%
        )

        endLocal & goto #_undefined_# 2>NUL || title %COMSPEC% & "%_prog%"  "%dp0%\node_modules\@openai\codex\bin\codex.js" %*
        """;

    public static RunnerLaunchRequest Apply(RunnerLaunchRequest request, bool useHerdr)
    {
        if (!OperatingSystem.IsWindows() || !IsCodexIdentity(request))
            return request;

        var env = CopyEnv(request);
        var cwd = string.IsNullOrWhiteSpace(request.Cwd) ? Environment.CurrentDirectory : request.Cwd;
        var originalArgs = request.Args ?? Array.Empty<string>();
        var measuredArgs = ExpandForMeasurement(originalArgs, env, useHerdr);

        RefuseIfNul(request.SessionId, request.Exe, originalArgs);
        RefuseIfNul(request.SessionId, request.Exe, measuredArgs);

        var budget = request.CommandLineBudgetChars ?? CodexLaunchProblemTypes.TransportCeilingChars;
        if (budget <= 0)
        {
            throw new CodexLaunchException(
                CodexLaunchProblemTypes.CommandLineTooLong,
                FormatRefusal(
                    request.SessionId,
                    CodexLaunchProblemTypes.CommandLineTooLong,
                    $"command-line budget is not positive ({budget})."));
        }

        if (IsDirectNodeCodexJs(request.Exe, originalArgs))
        {
            EnforceNodeBudget(request.SessionId, request.Exe, originalArgs, measuredArgs, env, cwd, budget);
            return request;
        }

        var resolvedExe = ResolveExisting(request.Exe, cwd, env);
        if (resolvedExe is not null && IsNativeCodexExe(resolvedExe))
        {
            EnforceSerializedBudget(
                request.SessionId,
                "codex.exe",
                resolvedExe,
                measuredArgs,
                EffectiveCeiling(budget, CodexLaunchProblemTypes.TransportCeilingChars),
                hop2: null);
            return request;
        }

        if (TryResolveCmdLauncher(request.Exe, cwd, env, out var cmdPath))
        {
            if (IsStockNpmCodexShim(cmdPath))
                return NormalizeStockShim(request, cmdPath, originalArgs, measuredArgs, env, cwd, budget);

            EnforceSerializedBudget(
                request.SessionId,
                "cmd/batch",
                cmdPath,
                measuredArgs,
                EffectiveCeiling(budget, CodexLaunchProblemTypes.BatchCeilingChars),
                hop2: null,
                batchGuidance: true);
            return request;
        }

        if (LooksLikeCmdName(request.Exe))
        {
            throw Unavailable(
                request.SessionId,
                "the Codex cmd launcher was not found beside the working directory or on PATH. "
                + "Install the npm Codex CLI or configure a native executable; the overflowing cmd shim is never used as a fallback.");
        }

        EnforceSerializedBudget(
            request.SessionId,
            "codex",
            resolvedExe ?? request.Exe,
            measuredArgs,
            EffectiveCeiling(budget, CodexLaunchProblemTypes.TransportCeilingChars),
            hop2: null);
        return request;
    }

    internal static bool IsStockNpmCodexShimText(string text) =>
        string.Equals(NormalizeShim(text), NormalizeShim(StockNpmShimText), StringComparison.OrdinalIgnoreCase);

    private static RunnerLaunchRequest NormalizeStockShim(
        RunnerLaunchRequest request,
        string cmdPath,
        IReadOnlyList<string> originalArgs,
        IReadOnlyList<string> measuredArgs,
        IDictionary<string, string> env,
        string cwd,
        int budget)
    {
        var cmdDir = Path.GetDirectoryName(cmdPath)
            ?? throw Unavailable(request.SessionId, "the npm Codex shim has no directory.");
        var jsPath = Path.GetFullPath(Path.Combine(cmdDir, "node_modules", "@openai", "codex", "bin", "codex.js"));
        if (!File.Exists(jsPath))
        {
            throw Unavailable(
                request.SessionId,
                "the npm package entrypoint node_modules\\@openai\\codex\\bin\\codex.js was not found. "
                + "Reinstall @openai/codex; the overflowing cmd shim is never used as a fallback.");
        }

        var nodePath = ResolveNode(cmdDir, cwd, env)
            ?? throw Unavailable(
                request.SessionId,
                "Node.exe was not found beside the npm shim or on PATH. "
                + "Install Node or configure a native Codex executable; the overflowing cmd shim is never used as a fallback.");

        var nativePath = TryFindLongestNativeExe(jsPath);
        if (nativePath is null)
        {
            throw Unsupported(
                request.SessionId,
                "no vendored native Codex executable was found under the recognized package layout. "
                + "The runner will not assume a short path.");
        }

        var nodeArgs = Prepend(jsPath, originalArgs);
        var nodeMeasured = Prepend(jsPath, measuredArgs);
        EnforceNodeHops(
            request.SessionId,
            nodePath,
            jsPath,
            nativePath,
            nodeMeasured,
            measuredArgs,
            EffectiveCeiling(budget, CodexLaunchProblemTypes.TransportCeilingChars));

        return request with { Exe = nodePath, Args = nodeArgs };
    }

    private static void EnforceNodeBudget(
        Guid sessionId,
        string nodeExe,
        IReadOnlyList<string> originalArgs,
        IReadOnlyList<string> measuredArgs,
        IDictionary<string, string> env,
        string cwd,
        int budget)
    {
        var jsPath = Path.GetFullPath(originalArgs[0]);
        if (!File.Exists(jsPath))
        {
            throw Unavailable(
                sessionId,
                "the npm package entrypoint codex.js named in the already-normalized launch was not found.");
        }

        var nativePath = TryFindLongestNativeExe(jsPath);
        if (nativePath is null)
        {
            throw Unsupported(
                sessionId,
                "no vendored native Codex executable was found under the recognized package layout. "
                + "The runner will not assume a short path.");
        }

        var resolvedNode = ResolveExisting(nodeExe, cwd, env) ?? nodeExe;
        var userMeasured = measuredArgs.Count > 0 ? measuredArgs.Skip(1).ToArray() : Array.Empty<string>();
        EnforceNodeHops(
            sessionId,
            resolvedNode,
            jsPath,
            nativePath,
            measuredArgs,
            userMeasured,
            EffectiveCeiling(budget, CodexLaunchProblemTypes.TransportCeilingChars));
    }

    private static void EnforceNodeHops(
        Guid sessionId,
        string nodeExe,
        string jsPath,
        string nativePath,
        IReadOnlyList<string> hop1Args,
        IReadOnlyList<string> hop2Args,
        int ceiling)
    {
        var hop1 = WindowsCommandLine.Measure(nodeExe, hop1Args);
        var hop2 = WindowsCommandLine.Measure(nativePath, hop2Args);
        if (hop1 > ceiling)
        {
            throw TooLong(
                sessionId,
                "node.exe codex.js",
                hop1,
                ceiling,
                hop2: null);
        }

        if (hop2 > ceiling)
        {
            throw TooLong(
                sessionId,
                "native hop",
                hop2,
                ceiling,
                hop2: (hop1, hop2));
        }
    }

    private static void EnforceSerializedBudget(
        Guid sessionId,
        string launcher,
        string exe,
        IReadOnlyList<string> args,
        int ceiling,
        (int Hop1, int Hop2)? hop2,
        bool batchGuidance = false)
    {
        var measured = WindowsCommandLine.Measure(exe, args);
        if (measured <= ceiling)
            return;
        throw TooLong(sessionId, launcher, measured, ceiling, hop2, batchGuidance);
    }

    private static CodexLaunchException TooLong(
        Guid sessionId,
        string launcher,
        int measured,
        int ceiling,
        (int Hop1, int Hop2)? hop2,
        bool batchGuidance = false)
    {
        var body = hop2 is { } hops
            ? $"native hop measured {hops.Hop2:N0} UTF-16 units (node.exe codex.js hop measured {hops.Hop1:N0}) against an effective budget of {ceiling:N0}."
            : $"launcher {launcher} measured {measured:N0} UTF-16 units against an effective budget of {ceiling:N0}.";
        var remedy = batchGuidance
            ? " Configure a recognized npm or native Codex launcher; a custom wrapper is not rewritten."
            : " Shrink the developer instructions or standing prompt; the runner refuses before creating a child.";
        return new CodexLaunchException(
            CodexLaunchProblemTypes.CommandLineTooLong,
            FormatRefusal(sessionId, CodexLaunchProblemTypes.CommandLineTooLong, body + remedy));
    }

    private static CodexLaunchException Unavailable(Guid sessionId, string detail) =>
        new(
            CodexLaunchProblemTypes.LauncherUnavailable,
            FormatRefusal(sessionId, CodexLaunchProblemTypes.LauncherUnavailable, detail));

    private static CodexLaunchException Unsupported(Guid sessionId, string detail) =>
        new(
            CodexLaunchProblemTypes.LauncherUnsupported,
            FormatRefusal(sessionId, CodexLaunchProblemTypes.LauncherUnsupported, detail));

    private static string FormatRefusal(Guid sessionId, string code, string detail) =>
        $"Session {sessionId:D}: {code}: {detail}";

    private static int EffectiveCeiling(int configured, int transportOrBatch) =>
        Math.Min(configured, transportOrBatch);

    private static bool IsCodexIdentity(RunnerLaunchRequest request) =>
        string.Equals(request.TranscriptFormat, TranscriptFormats.Codex, StringComparison.OrdinalIgnoreCase)
        || string.Equals(request.Herdr?.AgentKind, HerdrAgentKinds.Codex, StringComparison.OrdinalIgnoreCase);

    private static bool IsDirectNodeCodexJs(string exe, IReadOnlyList<string> args) =>
        IsNodeExe(exe) && args.Count > 0 && IsCodexJsPath(args[0]);

    private static bool IsNodeExe(string exe)
    {
        var name = Path.GetFileName(exe);
        return name.Equals("node.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("node", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNativeCodexExe(string exe)
    {
        var name = Path.GetFileName(exe);
        return name.Equals("codex.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCodexJsPath(string path)
    {
        var normalized = path.Replace('/', '\\');
        return normalized.EndsWith(@"\bin\codex.js", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains(@"\@openai\codex\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeCmdName(string exe)
    {
        var name = Path.GetFileName(exe);
        return name.Equals("codex.cmd", StringComparison.OrdinalIgnoreCase)
            || name.Equals("codex.bat", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStockNpmCodexShim(string cmdPath)
    {
        try
        {
            var text = File.ReadAllText(cmdPath);
            return IsStockNpmCodexShimText(text);
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

    private static string NormalizeShim(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        var lines = normalized.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            lines[i] = lines[i].TrimEnd();
        return string.Join('\n', lines);
    }

    private static string? ResolveNode(string cmdDir, string cwd, IDictionary<string, string> env)
    {
        var sibling = Path.Combine(cmdDir, "node.exe");
        if (File.Exists(sibling))
            return Path.GetFullPath(sibling);

        var fromPath = ResolveExisting("node.exe", cwd, env) ?? ResolveExisting("node", cwd, env);
        return fromPath is not null && File.Exists(fromPath) ? fromPath : null;
    }

    private static bool TryResolveCmdLauncher(
        string exe,
        string cwd,
        IDictionary<string, string> env,
        out string cmdPath)
    {
        cmdPath = "";
        foreach (var candidate in CmdCandidates(exe, cwd, env))
        {
            if (File.Exists(candidate)
                && Path.GetExtension(candidate) is { } ext
                && (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)))
            {
                cmdPath = Path.GetFullPath(candidate);
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> CmdCandidates(string exe, string cwd, IDictionary<string, string> env)
    {
        var rooted = TryRooted(exe);
        if (rooted is not null)
        {
            yield return rooted;
            yield break;
        }

        yield return Path.Combine(cwd, exe);
        if (!HasCmdExtension(exe))
            yield return Path.Combine(cwd, exe + ".cmd");

        var resolved = WindowsCommandLine.ResolveExecutable(exe, cwd, env);
        if (!string.IsNullOrWhiteSpace(resolved))
            yield return resolved;

        var fileName = Path.GetFileName(exe);
        var cmdName = HasCmdExtension(fileName) ? fileName : "codex.cmd";
        foreach (var dir in PathEntries(cwd, env))
            yield return Path.Combine(dir, cmdName);
    }

    private static string? TryRooted(string exe)
    {
        try
        {
            return Path.IsPathRooted(exe) ? exe : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool HasCmdExtension(string exe) =>
        exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
        || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveExisting(string exe, string cwd, IDictionary<string, string> env)
    {
        try
        {
            var resolved = WindowsCommandLine.ResolveExecutable(exe, cwd, env);
            return File.Exists(resolved) ? Path.GetFullPath(resolved) : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static IEnumerable<string> PathEntries(string cwd, IDictionary<string, string> env)
    {
        var pathValue = env.TryGetValue("PATH", out var p) && !string.IsNullOrWhiteSpace(p)
            ? p
            : Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
            yield break;
        foreach (var entry in pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            bool rooted;
            try { rooted = Path.IsPathRooted(entry); }
            catch (ArgumentException) { continue; }
            yield return rooted ? entry : Path.Combine(cwd, entry);
        }
    }

    internal static string? TryFindLongestNativeExe(string jsPath)
    {
        var binDir = Path.GetDirectoryName(jsPath);
        var packageRoot = binDir is null ? null : Path.GetDirectoryName(binDir);
        if (packageRoot is null)
            return null;

        var candidates = new List<string>();
        CollectNative(Path.Combine(packageRoot, "node_modules", "@openai"), candidates);
        var openaiDir = Path.GetDirectoryName(packageRoot);
        if (openaiDir is not null)
            CollectNative(openaiDir, candidates);

        return candidates.Count == 0
            ? null
            : candidates.OrderByDescending(c => c.Length).First();
    }

    private static void CollectNative(string openaiDir, List<string> candidates)
    {
        if (!Directory.Exists(openaiDir))
            return;
        IEnumerable<string> platforms;
        try
        {
            platforms = Directory.EnumerateDirectories(openaiDir, "codex-win32-*");
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        foreach (var platform in platforms)
        {
            var vendor = Path.Combine(platform, "vendor");
            if (!Directory.Exists(vendor))
                continue;
            IEnumerable<string> triples;
            try
            {
                triples = Directory.EnumerateDirectories(vendor);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var triple in triples)
            {
                var exe = Path.Combine(triple, "bin", "codex.exe");
                if (File.Exists(exe))
                    candidates.Add(Path.GetFullPath(exe));
            }
        }
    }

    private static IReadOnlyList<string> ExpandForMeasurement(
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string> env,
        bool useHerdr)
    {
        if (!useHerdr || args.Count == 0)
            return args;

        var expanded = new string[args.Count];
        for (var i = 0; i < args.Count; i++)
        {
            var original = args[i] ?? "";
            expanded[i] = HerdrLaunchScript.TryResolveEnvToken(original, env, out var resolved)
                ? resolved
                : original;
        }

        return expanded;
    }

    private static IReadOnlyList<string> Prepend(string first, IReadOnlyList<string> rest)
    {
        var result = new string[rest.Count + 1];
        result[0] = first;
        for (var i = 0; i < rest.Count; i++)
            result[i + 1] = rest[i];
        return result;
    }

    private static void RefuseIfNul(Guid sessionId, string exe, IReadOnlyList<string> args)
    {
        if (exe.IndexOf('\0') >= 0 || args.Any(a => a is not null && a.IndexOf('\0') >= 0))
        {
            throw new CodexLaunchException(
                CodexLaunchProblemTypes.CommandLineTooLong,
                FormatRefusal(
                    sessionId,
                    CodexLaunchProblemTypes.CommandLineTooLong,
                    "command line contains a NUL character and cannot be launched."));
        }
    }

    private static Dictionary<string, string> CopyEnv(RunnerLaunchRequest request)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.Env is null)
            return env;
        foreach (var (key, value) in request.Env)
            env[key] = value ?? "";
        return env;
    }
}
