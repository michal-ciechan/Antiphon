# CARD-1669 g3 Code report (task bd1133db)

Branch feat/card-task-bd1133db, base 7dc9e1168a2b4f714a4e3e29ac99e2aad485524c, runner mirror /work/worktrees/task-bd1133db (server2, Linux).

## Commits
- f8db39c0c6f3b5f200ae85aa268cab46ae23cedc test: red witnesses (new gkp separator invariant method; Agents.Pty Probe file-root test un-skipped on Unix and renamed Probe_reports_unavailable_for_non_directory_storage; PtyHostLeakSweep regex [\\/]; PhoneHome hang test holds the accepted peer through a forced GC)
- 32445500fba1958eebe3f0dfc88ba805ef62d79a test: PhoneHome 300 ms yield before the forced GC (makes the reference-drop mutant red)
- a3ae4cfebc3afef3efcd6b932fafb8342a671d68 fix: HerdrGkpLaunchGuard.FileName + GrokNativeSessionStore.Probe

## Product changes
1. HerdrGkpLaunchGuard.IsGkpLaunch: FileName(arg) = Path.GetFileName, then the part after the last '\', then strip a drive-relative "X:" when the arg had no separator. On Windows both extra steps are no-ops (GetFileName already splits '\' and strips the drive root there), so Windows classification is byte-identical. Dropped the dead ArgumentException catch (Path.GetFileName never throws it on .NET Core). Known residual: Windows reads \\server\gkp.ps1 (bare UNC root) as no file name, Linux now reads gkp.ps1 (fail-safe direction; pre-existing for //server/gkp.ps1).
2. GrokNativeSessionStore.Probe: root hoisted; catch (DirectoryNotFoundException) when (!File.Exists(root)) -> Missing; a file root falls to the IOException arm -> Unavailable. Windows unchanged (it already throws IOException there). Not changed: a FILE as GROK_HOME itself gives DirectoryNotFound on both OSes and stays Missing (the Debug note's claim that the root guard covers it is wrong: File.Exists(file/sessions) is false); identical across OS, separate card if wanted.
Missing-still-Missing witnesses (pre-existing, green at tip): GrokNativeSessionStoreTests.Probe_distinguishes_missing_and_found_storage (no sessions dir -> Missing) and HerdrGrokResumeGuardTests.Guid_resume_with_no_directory_throws_the_named_code.

## Runs (one build-slot lease each, native output, UseAppHost=false)
Filter SR: /*/*/(HerdrGkpLaunchGuardTests*)|(HerdrGrokResumeGuardTests*)|(PtyHostLeakSweepTests*)|(PhoneHomeConnectionServiceTests*)/*; Pty: /*/*/GrokNativeSessionStoreTests/*
- RED / product mutants (both fixes reverted = commit f8db39c0c,): SR 36 total, 31 passed, 5 failed: Is_gkp_launch_keys..., Require_throws_the_gkp_code..., Missing_chat_proxy_url_warns..., Gkp_launch_is_classified_alike... (C:/Users/x\.local/bin\gkp.ps1), Unavailable_store_is_an_IO_failure... ; Pty 2 total, 1 failed: Probe_reports_unavailable_for_non_directory_storage (Missing, expected Unavailable). Slots waited 615 s / 721 s.
- Row-6 mutant (using var accepted -> _ = await, with the 300 ms yield): Websocket_connect_that_hangs... red: should throw OperationCanceledException but threw WebSocketException. (Without the yield the same mutant stayed green: Debug MoveNext IL locals keep the client live within one frame; hence the yield.)
- GREEN at a3ae4cfeb: SR 36/36, Pty 2/2, skipped 0. TRX: all six target methods + the two new/renamed methods present and passed.
- Row 6 repeat: Websocket_connect_that_hangs... 5/5 green, five sequential --no-build dotnet run processes inside ONE lease (144bc2a2, held 22 s; the wrapper warned about a loop under one lease; no build ran inside it).

## Row 6 pwsh probe (scratch, .NET 9 pwsh, ClientWebSocket to a hanging loopback TcpListener, 1 s cancel, GC 300 ms in)
mode=discard run=1 elapsedMs=459 outcome="Unable to connect to the remote server" <- WebSocketException: Unable to connect to the remote server <- HttpRequestException: An error occurred while sending the request. <- IOException: Unable to read data from the transport connection: Connection reset by peer. <- SocketException: Connection reset by peer
mode=discard run=2 elapsedMs=318 outcome="Unable to connect to the remote server" <- WebSocketException: Unable to connect to the remote server <- HttpRequestException: An error occurred while sending the request. <- IOException: Unable to read data from the transport connection: Connection reset by peer. <- SocketException: Connection reset by peer
mode=discard run=3 elapsedMs=323 outcome="Unable to connect to the remote server" <- WebSocketException: Unable to connect to the remote server <- HttpRequestException: An error occurred while sending the request. <- IOException: Unable to read data from the transport connection: Connection reset by peer. <- SocketException: Connection reset by peer
mode=discard run=4 elapsedMs=321 outcome="Unable to connect to the remote server" <- WebSocketException: Unable to connect to the remote server <- HttpRequestException: An error occurred while sending the request. <- IOException: Unable to read data from the transport connection: Connection reset by peer. <- SocketException: Connection reset by peer
mode=discard run=5 elapsedMs=317 outcome="Unable to connect to the remote server" <- WebSocketException: Unable to connect to the remote server <- HttpRequestException: An error occurred while sending the request. <- IOException: Unable to read data from the transport connection: Connection reset by peer. <- SocketException: Connection reset by peer
mode=hold run=1 elapsedMs=1032 outcome="The operation was canceled." <- TaskCanceledException: The operation was canceled. <- IOException: Unable to read data from the transport connection: Operation canceled. <- SocketException: Operation canceled
mode=hold run=2 elapsedMs=1005 outcome="The operation was canceled." <- TaskCanceledException: The operation was canceled. <- IOException: Unable to read data from the transport connection: Operation canceled. <- SocketException: Operation canceled
mode=hold run=3 elapsedMs=1005 outcome="The operation was canceled." <- TaskCanceledException: The operation was canceled. <- IOException: Unable to read data from the transport connection: Operation canceled. <- SocketException: Operation canceled
mode=hold run=4 elapsedMs=998 outcome="The operation was canceled." <- TaskCanceledException: The operation was canceled. <- IOException: Unable to read data from the transport connection: Operation canceled. <- SocketException: Operation canceled
mode=hold run=5 elapsedMs=1003 outcome="The operation was canceled." <- TaskCanceledException: The operation was canceled. <- IOException: Unable to read data from the transport connection: Operation canceled. <- SocketException: Operation canceled

## Hygiene
- check-evidence-diff base..HEAD: commits=3 entries=0 violations=0 exit 0.
- merge-tree origin/master HEAD: clean.
- No bin-* directories; native outputs only. Row 7 (server2 image rebuild) not touched (operator).
