# CARD-0490 linux-x64 phone-home runner, extended for CARD-0590 targets.
# The default target is the last stage (runtime). receipt-probe and session-testing
# are opt-in and must not become the default image.
ARG SOURCE_REVISION=unknown

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG SOURCE_REVISION=unknown
WORKDIR /src
COPY . .
RUN dotnet publish src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj -c Release -r linux-x64 --self-contained false -p:SourceRevisionId=$SOURCE_REVISION -o /out/runner \
 && dotnet publish src/Antiphon.PtyHost/Antiphon.PtyHost.csproj -c Release -r linux-x64 --self-contained false -p:SourceRevisionId=$SOURCE_REVISION -o /out/ptyhost \
 && dotnet publish src/Antiphon.FakeGrok/Antiphon.FakeGrok.csproj -c Release -r linux-x64 --self-contained false -p:SourceRevisionId=$SOURCE_REVISION -o /out/fakegrok

FROM node:22-bookworm AS node22

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime-base
ARG SOURCE_REVISION=unknown
# CARD-0628 D-2: Claude Code's native linux-x64 binary, pinned by version AND published SHA-256.
# A re-published 2.1.280 fails the build at sha256sum -c; the remedy is a new pinned pair, never
# dropping the check.
ARG CLAUDE_CODE_VERSION=2.1.280
ARG CLAUDE_CODE_SHA256=1e08503dbdf3c2cb0d706d32f3408277388d1c76ef108673e8fe42c1b322925b
LABEL org.opencontainers.image.revision="${SOURCE_REVISION}"
WORKDIR /app
# Phone-home stays off until compose/launch injects PhoneHome__Enabled and PhoneHome__ServerOrigin.
# Do not bake a ServerOrigin URL into the image.
# Do not COPY OAuth credential files or any GROK_HOME or CLAUDE_CONFIG_DIR contents (CARD-0575,
# CARD-0628). Claude's credential is CLAUDE_CODE_OAUTH_TOKEN injected by the deploy environment,
# or an interactive login on the runner-state volume -- never a layer of this image.
USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl ca-certificates bash git \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /tmp/grok-install \
 && curl -fsSL https://x.ai/cli/install.sh | env HOME=/tmp/grok-install GROK_BIN_DIR=/usr/local/bin bash -s 1.0.40 \
 && install -m 0755 /tmp/grok-install/.grok/downloads/grok-linux-x86_64 /usr/local/bin/grok \
 && ln -sf grok /usr/local/bin/agent \
 && /usr/local/bin/grok --version \
 && /usr/local/bin/grok --version | grep -F '1.0.40' \
 && rm -rf /tmp/grok-install \
 && mkdir -p /tmp/claude-install \
 && curl -fsSL -o /tmp/claude-install/claude "https://downloads.claude.ai/claude-code-releases/${CLAUDE_CODE_VERSION}/linux-x64/claude" \
 && echo "${CLAUDE_CODE_SHA256}  /tmp/claude-install/claude" | sha256sum -c - \
 && install -m 0755 /tmp/claude-install/claude /usr/local/bin/claude \
 && env HOME=/tmp/claude-install CLAUDE_CONFIG_DIR=/tmp/claude-install/cfg DISABLE_AUTOUPDATER=1 /usr/local/bin/claude --version | grep -F "${CLAUDE_CODE_VERSION}" \
 && rm -rf /tmp/claude-install
COPY --from=build /out/runner/ /app/
COPY --from=build /out/ptyhost/ /tmp/ptyhost/
RUN cp /tmp/ptyhost/Antiphon.PtyHost /app/Antiphon.PtyHost \
 && cp /tmp/ptyhost/libporta_pty.so /app/libporta_pty.so \
 && cp /tmp/ptyhost/Antiphon.PtyHost.runtimeconfig.json /app/Antiphon.PtyHost.runtimeconfig.json \
 && cp /tmp/ptyhost/Antiphon.PtyHost.deps.json /app/Antiphon.PtyHost.deps.json \
 && cp /tmp/ptyhost/*.dll /app/ \
 && chmod 0755 /app/Antiphon.PtyHost \
 && test -x /app/Antiphon.PtyHost \
 && test -f /app/libporta_pty.so \
 && test -f /app/Antiphon.PtyHost.runtimeconfig.json \
 && rm -rf /tmp/ptyhost
ENV PhoneHome__Enabled=false
ENV ASPNETCORE_URLS=http://+:8080
HEALTHCHECK CMD curl -fsS http://127.0.0.1:8080/health || exit 1
USER 1654:1654
ENTRYPOINT ["dotnet", "Antiphon.SessionRunner.dll"]

FROM runtime-base AS receipt-probe
USER root
COPY --from=build /out/fakegrok/ /opt/antiphon-tests/fakegrok/
COPY docker/session-testing/fakegrok-linux.sh /opt/antiphon-tests/fakegrok-linux.sh
RUN chmod 0755 /opt/antiphon-tests/fakegrok/fakegrok /opt/antiphon-tests/fakegrok-linux.sh \
 && test -x /opt/antiphon-tests/fakegrok/fakegrok \
 && test -x /opt/antiphon-tests/fakegrok-linux.sh
USER 1654:1654

FROM receipt-probe AS session-testing
# CARD-0604 D-4: the persistent server2 runner owns a nested engine. The whole engine comes from
# the same pinned tarball the CLI already came from (no second engine source); iptables is pinned
# to the legacy backend because server2 is kernel 4.15 with iptables 1.6.1 and mixing backends
# across the two layers is the classic DinD failure.
USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates tar gzip iptables iproute2 openssh-client sudo \
 && mkdir -p /usr/local/lib/docker/cli-plugins /opt/powershell \
 && curl -fsSL https://download.docker.com/linux/static/stable/x86_64/docker-27.5.1.tgz \
    | tar -xz -C /usr/local/bin --strip-components=1 \
      docker/docker docker/dockerd docker/containerd docker/containerd-shim-runc-v2 \
      docker/runc docker/docker-init docker/docker-proxy docker/ctr \
 && curl -fsSL -o /usr/local/lib/docker/cli-plugins/docker-compose https://github.com/docker/compose/releases/download/v2.32.4/docker-compose-linux-x86_64 \
 && chmod 0755 /usr/local/bin/docker /usr/local/bin/dockerd /usr/local/bin/containerd \
      /usr/local/bin/containerd-shim-runc-v2 /usr/local/bin/runc /usr/local/bin/docker-init \
      /usr/local/bin/docker-proxy /usr/local/bin/ctr /usr/local/lib/docker/cli-plugins/docker-compose \
 && curl -fsSL -o /tmp/powershell.tar.gz https://github.com/PowerShell/PowerShell/releases/download/v7.5.4/powershell-7.5.4-linux-x64.tar.gz \
 && tar -xzf /tmp/powershell.tar.gz -C /opt/powershell \
 && chmod 0755 /opt/powershell/pwsh \
 && ln -sf /opt/powershell/pwsh /usr/local/bin/pwsh \
 && update-alternatives --set iptables /usr/sbin/iptables-legacy \
 && update-alternatives --set ip6tables /usr/sbin/ip6tables-legacy \
 && groupadd -g 1656 docker-nested \
 && rm -rf /var/lib/apt/lists/* /tmp/powershell.tar.gz
# Toolchain for in-runner builds and tests (D-6). COPY merges, so the aspnet 9 runtime already in
# the base survives alongside SDK 10 and runtime 10.
COPY --from=build /usr/share/dotnet /usr/share/dotnet
COPY --from=node22 /usr/local /usr/local
# Baked, non-secret git and ssh custody configuration (D-8). The private key half never enters
# the image; dind-entrypoint.sh materialises it on the /run/antiphon tmpfs at 0400 uid 1654,
# and stages the phone-home secret the same way (D-1) because a compose secret file arrives
# owned by the host uid at 0600 and uid 1654 cannot open it.
COPY docker/session-runner-grok/daemon.json /etc/docker/daemon.json
COPY docker/session-runner-grok/ssh_config /etc/antiphon/ssh_config
COPY docker/session-runner-grok/github_known_hosts /etc/antiphon/github_known_hosts
COPY docker/session-runner-grok/gitconfig /etc/gitconfig
COPY docker/session-runner-grok/dind-entrypoint.sh /usr/local/bin/antiphon-dind-entrypoint.sh
# CARD-0604 D-17 (Cut B): the custody mechanism. Both helpers are root-owned 0755 and the
# sudoers drop-in is root-owned 0440; uid 1654 may run exactly these two commands and nothing
# else (G-35, G-40). They exist only in this stage -- the default runtime and receipt-probe
# targets must never carry them, which is what Default_runtime_excludes_custody_helpers guards.
COPY docker/session-runner-grok/antiphon-custody-enter.sh /usr/local/bin/antiphon-custody-enter
COPY docker/session-runner-grok/antiphon-custody-kill.sh /usr/local/bin/antiphon-custody-kill
COPY docker/session-runner-grok/sudoers-antiphon-custody /etc/sudoers.d/antiphon-custody
# V-28's four containment measurements, run in-image by the harness (CP-14) and by the
# server2 custody-containment case (CP-15). It is a measurement, not a privilege: it runs as
# uid 1654 and reaches the helpers exactly the way a pty-host does.
COPY docker/session-runner-grok/custody-containment-probe.sh /usr/local/bin/antiphon-custody-containment-probe
RUN chmod 0755 /usr/local/bin/antiphon-dind-entrypoint.sh \
 && chown 0:0 /usr/local/bin/antiphon-custody-enter /usr/local/bin/antiphon-custody-kill \
 && chmod 0755 /usr/local/bin/antiphon-custody-enter /usr/local/bin/antiphon-custody-kill \
 && chmod 0755 /usr/local/bin/antiphon-custody-containment-probe \
 && chown 0:0 /etc/sudoers.d/antiphon-custody \
 && chmod 0440 /etc/sudoers.d/antiphon-custody \
 && visudo -c \
 && chmod 0644 /etc/docker/daemon.json /etc/antiphon/ssh_config /etc/antiphon/github_known_hosts /etc/gitconfig \
 && mkdir -p /home/app \
 && chown 1654:1654 /home/app \
 && docker --version \
 && docker compose version \
 && dockerd --version \
 && runc --version \
 && containerd --version \
 && iptables --version | grep -F 'legacy' \
 && dotnet --list-sdks | grep -E '^10\.' \
 && dotnet --list-runtimes | grep -F 'Microsoft.AspNetCore.App 9.' \
 && dotnet --list-runtimes | grep -F 'Microsoft.AspNetCore.App 10.' \
 && node --version \
 && env HOME=/tmp/claude-probe CLAUDE_CONFIG_DIR=/tmp/claude-probe DISABLE_AUTOUPDATER=1 claude --version | grep -F '2.1.280' \
 && rm -rf /tmp/claude-probe \
 && ssh -V \
 && sudo --version \
 && setpriv --help | grep -F -- '--no-new-privs' \
 && getent group docker-nested \
 && pwsh -NoProfile -Command "Write-Output ok"
# The container starts as root so dind-entrypoint.sh can run dockerd; it drops the runner to
# uid 1654 with the docker-nested supplementary group (D-3 step 6). The base compose file and
# every other target stay non-root.
USER 0:0
ENTRYPOINT ["/usr/local/bin/antiphon-dind-entrypoint.sh"]
CMD ["dotnet", "Antiphon.SessionRunner.dll"]

FROM runtime-base AS runtime
