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

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime-base
ARG SOURCE_REVISION=unknown
LABEL org.opencontainers.image.revision="${SOURCE_REVISION}"
WORKDIR /app
# Phone-home stays off until compose/launch injects PhoneHome__Enabled and PhoneHome__ServerOrigin.
# Do not bake a ServerOrigin URL into the image.
# Do not COPY OAuth credential files or any GROK_HOME contents (CARD-0575).
USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl ca-certificates bash git \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /tmp/grok-install \
 && curl -fsSL https://x.ai/cli/install.sh | env HOME=/tmp/grok-install GROK_BIN_DIR=/usr/local/bin bash -s 1.0.34 \
 && install -m 0755 /tmp/grok-install/.grok/downloads/grok-linux-x86_64 /usr/local/bin/grok \
 && ln -sf grok /usr/local/bin/agent \
 && /usr/local/bin/grok --version \
 && /usr/local/bin/grok --version | grep -F '1.0.34' \
 && rm -rf /tmp/grok-install
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
USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates tar gzip \
 && mkdir -p /usr/local/lib/docker/cli-plugins /opt/powershell \
 && curl -fsSL https://download.docker.com/linux/static/stable/x86_64/docker-27.5.1.tgz | tar -xz -C /usr/local/bin --strip-components=1 docker/docker \
 && curl -fsSL -o /usr/local/lib/docker/cli-plugins/docker-compose https://github.com/docker/compose/releases/download/v2.32.4/docker-compose-linux-x86_64 \
 && chmod 0755 /usr/local/bin/docker /usr/local/lib/docker/cli-plugins/docker-compose \
 && curl -fsSL -o /tmp/powershell.tar.gz https://github.com/PowerShell/PowerShell/releases/download/v7.5.4/powershell-7.5.4-linux-x64.tar.gz \
 && tar -xzf /tmp/powershell.tar.gz -C /opt/powershell \
 && chmod 0755 /opt/powershell/pwsh \
 && ln -sf /opt/powershell/pwsh /usr/local/bin/pwsh \
 && rm -rf /var/lib/apt/lists/* /tmp/powershell.tar.gz \
 && docker --version \
 && docker compose version \
 && pwsh -NoProfile -Command "Write-Output ok"
USER 1654:1654

FROM runtime-base AS runtime
