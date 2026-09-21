# CARD-0590 test image. SDK 10, ASP.NET runtime 9, Node 22, Git, PowerShell 7.
# delivery-fixture is a separate target and is not the server image.
ARG SOURCE_REVISION=unknown

FROM node:22-bookworm AS node22
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime9
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS test-runner
ARG SOURCE_REVISION=unknown
USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends git curl ca-certificates tar gzip \
 && rm -rf /var/lib/apt/lists/*
COPY --from=node22 /usr/local /usr/local
COPY --from=runtime9 /usr/share/dotnet /usr/share/dotnet9
RUN mkdir -p /opt/powershell \
 && curl -fsSL -o /tmp/powershell.tar.gz https://github.com/PowerShell/PowerShell/releases/download/v7.5.4/powershell-7.5.4-linux-x64.tar.gz \
 && tar -xzf /tmp/powershell.tar.gz -C /opt/powershell \
 && chmod 0755 /opt/powershell/pwsh \
 && ln -sf /opt/powershell/pwsh /usr/local/bin/pwsh \
 && rm -f /tmp/powershell.tar.gz \
 && node --version \
 && npm --version \
 && git --version \
 && pwsh -NoProfile -Command "Write-Output ok" \
 && /usr/share/dotnet9/dotnet --list-runtimes
WORKDIR /src
COPY . .
ENV SOURCE_REVISION=${SOURCE_REVISION}
ENV DOTNET_ROOT=/usr/share/dotnet
ENV DOTNET_RUNTIME9=/usr/share/dotnet9

FROM node:22-bookworm AS client-build
WORKDIR /src/client
COPY client/package.json client/package-lock.json ./
RUN npm ci
COPY client/ ./
RUN npm run build

FROM test-runner AS delivery-fixture
ARG SOURCE_REVISION=unknown
WORKDIR /src
RUN dotnet publish tests/Antiphon.DockerStack.Fixture/Antiphon.DockerStack.Fixture.csproj -c Release -r linux-x64 --self-contained false -p:SourceRevisionId=$SOURCE_REVISION -o /fixture
COPY --from=client-build /src/client/dist /fixture/wwwroot
COPY server/Bundles /fixture/Bundles
RUN test -f /fixture/Antiphon.DockerStack.Fixture.dll \
 && test -f /fixture/wwwroot/index.html \
 && test -f /fixture/Antiphon.Server.dll
USER 1654:1654
ENTRYPOINT ["dotnet", "/fixture/Antiphon.DockerStack.Fixture.dll"]
