# Self-contained Docker stack (CARD-0590)

Ordinary server2 testing uses three containers: the server (with the built client), the session runner, and PostgreSQL. The root `Dockerfile` publishes the server for `linux-x64` with SDK 10 and the ASP.NET 9 runtime. `docker/session-runner-grok/Dockerfile` keeps the phone-home runtime as its default target. `receipt-probe` adds FakeGrok and no Docker socket. `session-testing` adds the Docker CLI, Compose, and PowerShell for an explicit ordinary test session.

Base `docker-compose.yml` publishes only the server, on `127.0.0.1:5000` unless `ANTIPHON_BIND_ADDRESS` is set. The runner and Postgres have no published ports and no Docker socket. `docker-compose.session-testing.yml` is the only application override that mounts the socket, and only into the test runner. Pass `DOCKER_SOCKET_GID` from the socket's group. Do not chmod the socket.

`scripts/test-docker.ps1 -Group small|backend|all` is the foreground test entry. `scripts/verify-docker-stack.ps1 -Case <literal> -Manifest <path>` runs one checkpoint. SourceLanding Mutation stays on the Windows local path (CARD-0598). A Linux image run is not a custody receipt.

The class accounting checked into `tests/linux-test-roster.json` is the executable copy of the frozen plan roster. Do not drop a class because Linux is red.

Windows CP-1 through CP-5 passed on `3569ed7cd520e3a76bafc0643feb1da0b42c73ca`. Live cases run on server2 through `scripts/c590-remote.sh`, invoked by `scripts/verify-docker-stack.ps1` when `ANTIPHON_C590_STUB` is unset. Evidence for a run is `/work/test-evidence/<run-id>/<case>/`. Stub mode still answers `UnknownCase` for a case the local guards do not implement.
