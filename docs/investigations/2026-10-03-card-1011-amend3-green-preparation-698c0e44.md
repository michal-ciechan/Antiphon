# CARD-1011 amendment 3: inert green-phase preparation

**Not applied or compiled.** The committed source at 614cc8f4017bb962d91ee5a50fc696ad8d753bff retains the legacy visible-trust requirement for the mandatory fake red proof. The checkpoint tool refused before any build/test because the original owner is still Failed. Restore a live owner binding first; run CP-7 against the legacy source and inspect all eight TRX results, including the expected false-startup assertion failure. Only then apply this candidate and finish the owner/test/ledger clause updates from amendment 3. Commit and push before the green CP-7 run. No final executable harness SHA exists yet.

The candidate shares observation/startup/turn/receipt projection with the new Unit fixture, renames the Explicit probe, measures CLI version before launch and actual host line before startup/turn assertions, hashes the loaded conpty module and shipped console sibling, captures runner/host binary identity, requires exactly one nonce prompt/reply/later TurnEnd in the final transcript, and writes success only after confirmed owned-child exit. It uses the existing normal launch/settings and cleanup/deadline constraints. Windows behavior and this candidate remain unqualified; the caller commissions one modern paid run only after ordinary red/green proof and the pushed executable harness exist. Review the cleanup and final-read paths when implementing; this preparation is not a receipt.

The exact three owner clauses and corresponding C1011_qualification_gates labels are in plan amendment 3 (modern-review, fresh-startup, preland-gate). Reconcile the ledger to WQ-1 operator-excluded for CARD-1022, WQ-2 reported met on real Review 02e5b9b7 with 1.0.46, and WQ-3's failed historical no-dialog run followed by the pending amended one-turn commission. Preserve dated 1.0.41 facts, and keep inbox fake regressions. Keep the prompt held; its unchanged exact replacement makes no trust claim.

Apply the following only after valid assertion-red evidence. This is a reviewable source candidate, not completed implementation or passing evidence.

```diff
diff --git a/tests/Antiphon.Tests/TestHelpers/C1011GrokQualification.cs b/tests/Antiphon.Tests/TestHelpers/C1011GrokQualification.cs
index bfd7deae1..141c01434 100644
--- a/tests/Antiphon.Tests/TestHelpers/C1011GrokQualification.cs
+++ b/tests/Antiphon.Tests/TestHelpers/C1011GrokQualification.cs
@@ -20,9 +20,8 @@ internal static class C1011GrokQualification
         if (!ready || GrokStartupScreen.Classify(screen).Reason != GrokStartupReason.Ready
             || GrokTrustPromptDetector.IsVisibleOnScreen(screen))
             return StartupVerdict.NotReady;
-        // Preparatory red phase: retain the old requirement until absence is proven red.
-        string[] expectedInputs = ["y"];
-        return observer.TrustBeforeFirstInput && observer.StartupInputs.SequenceEqual(expectedInputs)
+        string[] expectedInputs = observer.TrustBeforeFirstInput ? ["y"] : [];
+        return observer.StartupInputs.SequenceEqual(expectedInputs)
             ? StartupVerdict.Accepted : StartupVerdict.UnexpectedStartupInput;
     }
 
diff --git a/tests/Antiphon.Tests/Agents/RunnerGrokAdapterReadyTestsPty.cs b/tests/Antiphon.Tests/Agents/RunnerGrokAdapterReadyTestsPty.cs
index 44c3a11ba..db8cf965e 100644
--- a/tests/Antiphon.Tests/Agents/RunnerGrokAdapterReadyTestsPty.cs
+++ b/tests/Antiphon.Tests/Agents/RunnerGrokAdapterReadyTestsPty.cs
@@ -75,20 +75,16 @@ public class RunnerGrokAdapterReadyTestsPty
     [Explicit]
     [NotInParallel("Headed")]
     [Timeout(600_000)]
-    public async Task C1011_real_fresh_worktree_trust(CancellationToken cancellationToken)
+    public async Task C1011_real_fresh_worktree_ready_and_one_turn(CancellationToken cancellationToken)
     {
         if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS") != "1")
             throw new SkipTestException("Requires Windows and ANTIPHON_HEADED_TESTS=1 under an explicit S0 commission");
-        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok", "bin", "grok.exe");
-        File.Exists(exe).ShouldBeTrue("The commissioned real Grok CLI must be installed; missing setup is incomplete qualification");
-        var root = Path.Combine(Path.GetTempPath(), "c1011-real-trust-" + Guid.NewGuid().ToString("N"));
+        var root = Path.Combine(Path.GetTempPath(), "c1011-real-ready-" + Guid.NewGuid().ToString("N"));
         Directory.CreateDirectory(root);
+        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok", "bin", "grok.exe");
         var cwd = Path.Combine(root, "worktree");
         using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
         deadline.CancelAfter(TimeSpan.FromMinutes(10));
-        var source = (await GitAsync(DelegateScriptRunner.RepoRoot, ["rev-parse", "HEAD"], deadline.Token)).Trim();
-        var version = (await RunAsync(exe, ["--version"], DelegateScriptRunner.RepoRoot, deadline.Token)).Trim();
-        await GitAsync(DelegateScriptRunner.RepoRoot, ["worktree", "add", "--detach", cwd, source], deadline.Token);
         var sessionId = Guid.NewGuid();
         await using var client = new DirectSessionRunnerClient(Path.Combine(root, "logs"), ptyBackend: "modern");
         var observer = new C1011GrokQualification.Observer(client);
@@ -96,51 +92,91 @@ public class RunnerGrokAdapterReadyTestsPty
         {
             GrokStartupCaptureDirectory = Path.Combine(root, "startup"),
         }));
+        string source = "", version = "", backendLine = "", body = "", nonce = "";
+        string? bannerVersion = null, failure = null;
+        object? provenance = null, hostBuild = null;
         Process? ownedChild = null;
+        var worktreeCreated = false;
+        var launchAttempted = false;
         var started = false;
         var releaseConfirmed = false;
+        var turnComplete = false;
+        SessionRunnerTranscriptDto? finalTranscript = null;
         try
         {
+            File.Exists(exe).ShouldBeTrue("The commissioned real CLI must be installed; missing setup is incomplete");
+            source = (await GitAsync(DelegateScriptRunner.RepoRoot, ["rev-parse", "HEAD"], deadline.Token)).Trim();
+            await GitAsync(DelegateScriptRunner.RepoRoot, ["worktree", "add", "--detach", cwd, source], deadline.Token);
+            worktreeCreated = true;
+            // Capture the actual CLI tuple immediately before this one launch.
+            version = (await RunAsync(exe, ["--version"], cwd, deadline.Token)).Trim();
+            launchAttempted = true;
             await adapter.StartAsync(new AgentLaunchSpec("grok", AgentKind.Grok, exe,
                 ["--always-approve", "--no-alt-screen", "--model", "grok-4.7", "--session-id", sessionId.ToString("D")],
                 new Dictionary<string, string>(), cwd, 120, 30, SessionId: sessionId), deadline.Token);
             started = true;
             adapter.Pid.ShouldNotBeNull();
             ownedChild = Process.GetProcessById(adapter.Pid.Value);
-            (await adapter.WaitForReadyAsync(deadline.Token)).ShouldBeTrue();
+            // Persist the actual host evidence even if a later startup or prompt assertion fails.
+            var hostLog = await ReadHostLogAsync(client, sessionId, deadline.Token);
+            backendLine = hostLog.Split('\n').Single(x => x.Contains("pty backend:", StringComparison.Ordinal)).TrimEnd('\r');
+            backendLine.ShouldContain("pty backend: ModernConPty (requested 'modern')");
+            var session = await client.GetAsync(sessionId, deadline.Token);
+            session.HostPid.ShouldNotBeNull("Modern binary provenance requires the bound, owned host");
+            using (var host = Process.GetProcessById(session.HostPid.Value))
+            {
+                hostBuild = C1011BinaryIdentity(host.MainModule!.FileName);
+                var loadedDll = host.Modules.Cast<ProcessModule>().Single(x =>
+                    string.Equals(x.ModuleName, ConPtyRedistributable.DllName, StringComparison.OrdinalIgnoreCase)).FileName;
+                var console = Path.Combine(Path.GetDirectoryName(loadedDll)!, ConPtyRedistributable.ConsoleHostName);
+                var hashes = ConPtyRedistributable.VerifyShippedHashes(loadedDll);
+                hashes.Ok.ShouldBeTrue(hashes.Detail);
+                provenance = new
+                {
+                    hostPid = session.HostPid, package = ConPtyRedistributable.PackageId,
+                    packageVersion = ConPtyRedistributable.PackageVersion,
+                    loadedConPty = C1011BinaryIdentity(loadedDll),
+                    shippedOpenConsoleSibling = C1011BinaryIdentity(console),
+                };
+            }
+            var readyResult = await adapter.WaitForReadyAsync(deadline.Token);
             var ready = await observer.GetSnapshotAsync(sessionId, deadline.Token);
-            C1011GrokQualification.Startup(true, ready.RenderedScreen, observer)
+            C1011GrokQualification.Startup(readyResult, ready.RenderedScreen, observer)
                 .ShouldBe(C1011GrokQualification.StartupVerdict.Accepted, "startup-accepted");
+            readyResult.ShouldBeTrue();
             GrokStartupScreen.Classify(ready.RenderedScreen).Reason.ShouldBe(GrokStartupReason.Ready);
             GrokTrustPromptDetector.IsVisibleOnScreen(ready.RenderedScreen).ShouldBeFalse();
-            (await ReadHostLogAsync(client, sessionId, deadline.Token))
-                .ShouldContain("pty backend: ModernConPty (requested 'modern')");
+            ready.RenderedScreen.Split('\n')[25][4].ShouldBe('>');
+            bannerVersion = System.Text.RegularExpressions.Regex.Match(ready.RenderedScreen,
+                @"\b\d+\.\d+\.\d+(?:\s*\([A-Za-z0-9]+\))?").Value;
+            (await client.GetTranscriptAsync(sessionId, deadline.Token)).Entries
+                .ShouldNotContain(x => x.Kind == TranscriptKinds.UserPrompt);
             observer.StartupComplete = true;
-            var nonce = "C1011 TRUST " + Guid.NewGuid().ToString("N");
-            var body = $"Reply exactly {nonce}. Do not use tools or change files.";
-            await adapter.SendPromptAsync(body, deadline.Token);
-            var transcript = await WaitForPromptAsync(client, sessionId, deadline.Token);
-            var prompt = transcript.Entries.Single(x => x.Kind == TranscriptKinds.UserPrompt);
-            prompt.Text.ShouldBe(body);
-            do
+            nonce = "C1011_" + Guid.NewGuid().ToString("N");
+            body = $"Reply exactly {nonce}. Do not use tools or change files.";
+            await adapter.SendPromptAsync(body, deadline.Token); // Exactly one submission, no manual retry.
+            while (true)
             {
-                transcript = await client.GetTranscriptAsync(sessionId, deadline.Token);
-                if (transcript.Entries.Any(x => x.Kind == TranscriptKinds.TurnEnd && x.Sequence > prompt.Sequence)) break;
+                finalTranscript = await client.GetTranscriptAsync(sessionId, deadline.Token);
+                var prompts = finalTranscript.Entries.Where(x => x.Kind == TranscriptKinds.UserPrompt).ToArray();
+                if (prompts.Length > 1 || (prompts.Length == 1 && finalTranscript.Entries.Any(x =>
+                        x.Kind == TranscriptKinds.TurnEnd && x.Sequence > prompts[0].Sequence))) break;
                 await Task.Delay(250, deadline.Token);
-            } while (true);
-            transcript.Entries.ShouldContain(x => x.Kind == TranscriptKinds.AssistantText
-                && x.Sequence > prompt.Sequence && x.Text != null && x.Text.Contains(nonce));
-            await File.WriteAllTextAsync(Path.Combine(root, "receipt.json"), JsonSerializer.Serialize(new
-            {
-                source, version, sessionId, cwd, cols = 120, rows = 30,
-                prompt.Text, prompt.Sequence, response = transcript.Entries.Where(x => x.Sequence > prompt.Sequence),
-            }), deadline.Token);
+            }
+            C1011GrokQualification.Turn(finalTranscript, body, nonce).Verdict
+                .ShouldBe(C1011GrokQualification.TurnVerdict.Accepted, "complete-paid-turn");
+            turnComplete = true;
+        }
+        catch (Exception error)
+        {
+            failure = error.GetType().Name; // Never export arbitrary exception/CLI/sign-in content.
+            throw;
         }
         finally
         {
             try
             {
-                if (started)
+                if (launchAttempted)
                 {
                     using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                     await client.KillAsync(sessionId, cleanup.Token);
@@ -151,17 +187,36 @@ public class RunnerGrokAdapterReadyTestsPty
                         releaseConfirmed = true;
                     }
                 }
+                if (turnComplete)
+                {
+                    releaseConfirmed.ShouldBeTrue("Success is finalized only after confirmed owned-child exit");
+                    using var finalRead = new CancellationTokenSource(TimeSpan.FromSeconds(10));
+                    finalTranscript = await client.GetTranscriptAsync(sessionId, finalRead.Token);
+                    var turn = C1011GrokQualification.Turn(finalTranscript, body, nonce);
+                    turn.Verdict.ShouldBe(C1011GrokQualification.TurnVerdict.Accepted, "final-paid-turn");
+                    var models = finalTranscript.Entries.Select(x => x.Model)
+                        .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct().ToArray();
+                    models.ShouldNotBeEmpty("Qualification retains observed transcript model metadata");
+                    var evidence = new C1011GrokQualification.ReceiptEvidence(source, version, backendLine,
+                        sessionId, cwd, "grok-4.7", models,
+                        C1011BinaryIdentity(typeof(Antiphon.SessionRunner.SessionRunnerRuntime).Assembly.Location),
+                        hostBuild!, provenance!, observer.TrustBeforeFirstInput, observer.StartupInputs,
+                        observer.Observations, body, turn, releaseConfirmed, BannerVersion: bannerVersion);
+                    await File.WriteAllTextAsync(Path.Combine(root, "receipt.json"),
+                        C1011GrokQualification.Receipt(evidence), CancellationToken.None);
+                }
             }
             finally
             {
                 ownedChild?.Dispose();
                 await File.WriteAllTextAsync(Path.Combine(root, "observations.json"), JsonSerializer.Serialize(new
                 {
-                    source, version, sessionId, observer.TrustBeforeFirstInput, observer.StartupInputs,
-                    observer.Observations, releaseConfirmed,
+                    source, version, backendLine, sessionId, cwd, bannerVersion, failure, started,
+                    observer.TrustBeforeFirstInput, observer.StartupInputs, observer.Observations,
+                    hostBuild, provenance, releaseConfirmed,
+                    turn = finalTranscript is null ? null : C1011GrokQualification.Turn(finalTranscript, body, nonce),
                 }), CancellationToken.None);
-                // Remove only this exact owned worktree, after confirmed child exit. No force.
-                if (!started || releaseConfirmed)
+                if (worktreeCreated && (!launchAttempted || releaseConfirmed))
                 {
                     using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                     await GitAsync(DelegateScriptRunner.RepoRoot, ["worktree", "remove", cwd], cleanup.Token);
@@ -171,6 +226,17 @@ public class RunnerGrokAdapterReadyTestsPty
         }
     }
 
+    private static object C1011BinaryIdentity(string path)
+    {
+        using var stream = File.OpenRead(path);
+        return new
+        {
+            path,
+            sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream)),
+            fileVersion = FileVersionInfo.GetVersionInfo(path).FileVersion,
+        };
+    }
+
     private static async Task<string> ReadHostLogAsync(DirectSessionRunnerClient client, Guid sessionId, CancellationToken ct)
     {
         var path = Path.Combine(Path.GetDirectoryName(client.PtyHostManifestDir)!, "logs", sessionId.ToString("N") + ".log");
```
